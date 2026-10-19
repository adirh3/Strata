using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using Theme = TextMateSharp.Themes.Theme;

namespace StrataTheme.Controls;

/// <summary>
/// A lightweight, high-performance control for rendering syntax-highlighted source code.
/// Uses TextMateSharp for tokenization without depending on AvaloniaEdit.
/// </summary>
/// <remarks>
/// <para>The control renders code in a <see cref="SelectableTextBlock"/>, supporting text selection and
/// copy. Highlighted ranges are applied directly to its text layout, so tokens create no inline
/// elements.</para>
/// <para><b>Pseudo-classes:</b></para>
/// <list type="bullet">
/// <item><c>:empty</c> — Applied when <see cref="Text"/> is null or whitespace.</item>
/// </list>
/// </remarks>
public class StrataCodeBlock : Control
{
    /// <summary>Defines the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StrataCodeBlock, string?>(nameof(Text));

    /// <summary>Defines the <see cref="Language"/> property.</summary>
    public static readonly StyledProperty<string?> LanguageProperty =
        AvaloniaProperty.Register<StrataCodeBlock, string?>(nameof(Language));

    // ── Shared TextMate state (expensive to create) ──────────────────────────
    private static RegistryOptions? s_darkOptions;
    private static Registry? s_darkRegistry;
    private static RegistryOptions? s_lightOptions;
    private static Registry? s_lightRegistry;

    // ── Grammar cache (scope → grammar) ──────────────────────────────────────
    private static readonly Dictionary<string, IGrammar> s_grammarCache = new();

    // ── Per-theme brush lookup (color ID → immutable brush) ──────────────────
    private static Dictionary<int, IBrush>? s_darkBrushMap;
    private static Dictionary<int, IBrush>? s_lightBrushMap;

    private static readonly TimeSpan s_tokenizeTimeout = TimeSpan.FromMilliseconds(500);

    // ── Visual child ─────────────────────────────────────────────────────────
    private readonly CodeTextBlock _presenter;

    // ── Caching to skip redundant work ───────────────────────────────────────
    private string? _cachedText;
    private string? _cachedLanguage;
    private bool _cachedIsDark;

    /// <summary>
    /// Line height used for the code presenter.
    /// Must match the value used in <see cref="StrataMarkdown"/> MinHeight calculations.
    /// </summary>
    public const double CodeLineHeight = 18;

    public StrataCodeBlock()
    {
        _presenter = new CodeTextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            LineHeight = CodeLineHeight,
        };

        LogicalChildren.Add(_presenter);
        VisualChildren.Add(_presenter);
    }

    /// <summary>Gets or sets the source code text to display.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the programming language for syntax highlighting (e.g. "csharp", "json").</summary>
    public string? Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty || change.Property == LanguageProperty)
            InvalidateHighlighting();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // Always measure with unconstrained width so the presenter reports
        // the full text width (needed for horizontal scrolling in the parent
        // ScrollViewer to reach the end of long lines).
        _presenter.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        return _presenter.DesiredSize;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange at least as wide as the text needs so the scroll extent
        // covers the full content width.
        var arrangeWidth = Math.Max(finalSize.Width, _presenter.DesiredSize.Width);
        var arrangeSize = new Size(arrangeWidth, finalSize.Height);
        _presenter.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    /// <summary>
    /// Re-tokenizes the text and rebuilds the highlighted styles of the presenter.
    /// Called automatically when <see cref="Text"/> or <see cref="Language"/> changes.
    /// </summary>
    internal void InvalidateHighlighting()
    {
        var text = Text;
        var language = Language;
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

        if (string.Equals(text, _cachedText, StringComparison.Ordinal)
            && language == _cachedLanguage && isDark == _cachedIsDark)
            return;

        _cachedText = text;
        _cachedLanguage = language;
        _cachedIsDark = isDark;

        PseudoClasses.Set(":empty", string.IsNullOrWhiteSpace(text));

        if (string.IsNullOrWhiteSpace(text))
        {
            _presenter.SetCode("", null);
            return;
        }

        var code = NormalizeLineEndings(text!);
        List<CodeSpan>? spans;
        try
        {
            spans = Highlight(code, language, isDark);
        }
        catch
        {
            // Graceful fallback: render plain text.
            spans = null;
        }

        _presenter.SetCode(code, spans);
    }

    /// <summary>The code as presented: line breaks are '\n' only, as the highlighter tokenizes it.</summary>
    internal string PresentedText => _presenter.Text ?? "";

    /// <summary>The styled ranges of <see cref="PresentedText"/>; unstyled text uses the default foreground.</summary>
    internal IReadOnlyList<CodeSpan> PresentedSpans => _presenter.Spans ?? [];

    // ── Core highlighting ────────────────────────────────────────────────────

    private static IBrush GetOrCreateBrush(Dictionary<int, IBrush> brushMap, Theme theme, int colorId)
    {
        if (brushMap.TryGetValue(colorId, out var brush))
            return brush;

        var hex = theme.GetColor(colorId);
        var color = ParseColor(hex);
        brush = color.HasValue
            ? new SolidColorBrush(color.Value).ToImmutable()
            : Brushes.Transparent;
        brushMap[colorId] = brush;
        return brush;
    }

    private static Dictionary<int, IBrush> GetBrushMap(bool isDark)
    {
        if (isDark)
            return s_darkBrushMap ??= new Dictionary<int, IBrush>();
        else
            return s_lightBrushMap ??= new Dictionary<int, IBrush>();
    }

    /// <summary>
    /// Tokenizes <paramref name="code"/> and returns its styled ranges, merging neighbours that share
    /// a style. Returns null when the language has no grammar, so the code renders as plain text.
    /// </summary>
    private static List<CodeSpan>? Highlight(string code, string? language, bool isDark)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        var (options, registry) = GetRegistryPair(isDark);
        var grammar = ResolveGrammar(options, registry, language!);
        if (grammar is null)
            return null;

        var theme = registry.GetTheme();
        var brushMap = GetBrushMap(isDark);
        var spans = new List<CodeSpan>();

        IStateStack? ruleStack = null;
        int lineStart = 0;

        while (lineStart <= code.Length)
        {
            int lineEnd = code.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = code.Length;
            int lineLen = lineEnd - lineStart;

            if (lineLen == 0)
            {
                // Tokenize empty lines to maintain state across multi-line constructs.
                var emptyResult = TokenizeLineWithState(grammar, string.Empty, ruleStack);
                ruleStack = emptyResult?.RuleStack ?? ruleStack;
                lineStart = lineEnd + 1;
                continue;
            }

            // Extract line substring (unavoidable — TextMateSharp API requires string).
            var line = code.Substring(lineStart, lineLen);
            var result = TokenizeLineWithState(grammar, line, ruleStack);

            if (result?.Tokens is null || result.Tokens.Length == 0)
            {
                ruleStack = result?.RuleStack ?? ruleStack;
                lineStart = lineEnd + 1;
                continue;
            }

            ruleStack = result.RuleStack;
            var tokens = result.Tokens;

            for (int j = 0; j < tokens.Length; j++)
            {
                var token = tokens[j];
                int start = token.StartIndex;
                int end = (j + 1 < tokens.Length) ? tokens[j + 1].StartIndex : lineLen;

                if (start >= lineLen) break;
                if (end > lineLen) end = lineLen;
                if (start >= end) continue;

                // Resolve foreground and font style from theme trie.
                int fgColorId = 0;
                int fontStyleBits = -1;

                var rules = theme.Match(token.Scopes);
                if (rules is not null)
                {
                    foreach (var rule in rules)
                    {
                        if (rule.foreground > 0)
                            fgColorId = rule.foreground;
                        if ((int)rule.fontStyle >= 0)
                            fontStyleBits = (int)rule.fontStyle;
                    }
                }

                IBrush? foreground = null;
                if (fgColorId > 0)
                {
                    var brush = GetOrCreateBrush(brushMap, theme, fgColorId);
                    if (brush != Brushes.Transparent)
                        foreground = brush;
                }

                var italic = fontStyleBits > 0 && (fontStyleBits & (int)TextMateSharp.Themes.FontStyle.Italic) != 0;
                var bold = fontStyleBits > 0 && (fontStyleBits & (int)TextMateSharp.Themes.FontStyle.Bold) != 0;
                var underline = fontStyleBits > 0 && (fontStyleBits & (int)TextMateSharp.Themes.FontStyle.Underline) != 0;
                if (foreground is null && !italic && !bold && !underline)
                    continue;

                AddSpan(spans, code, new CodeSpan(lineStart + start, end - start, foreground, italic, bold, underline));
            }

            lineStart = lineEnd + 1;
        }

        return spans;
    }

    /// <summary>Appends a styled range, extending the previous one when only a line break separates them.</summary>
    private static void AddSpan(List<CodeSpan> spans, string code, CodeSpan span)
    {
        if (spans.Count > 0)
        {
            var last = spans[^1];
            var gap = span.Start - last.End;
            if (last.HasSameStyle(span) && (gap == 0 || (gap == 1 && code[last.End] == '\n')))
            {
                spans[^1] = last with { Length = span.End - last.Start };
                return;
            }
        }

        spans.Add(span);
    }

    /// <summary>Removes carriage returns that precede a line break or end the text.</summary>
    private static string NormalizeLineEndings(string text)
    {
        if (!text.Contains('\r'))
            return text;

        var normalized = text.Replace("\r\n", "\n");
        return normalized.EndsWith('\r') ? normalized[..^1] : normalized;
    }

    // ── Registry / grammar helpers ───────────────────────────────────────────

    private static (RegistryOptions options, Registry registry) GetRegistryPair(bool isDark)
    {
        if (isDark)
        {
            s_darkOptions ??= new RegistryOptions(ThemeName.DarkPlus);
            s_darkRegistry ??= new Registry(s_darkOptions);
            return (s_darkOptions, s_darkRegistry);
        }
        else
        {
            s_lightOptions ??= new RegistryOptions(ThemeName.LightPlus);
            s_lightRegistry ??= new Registry(s_lightOptions);
            return (s_lightOptions, s_lightRegistry);
        }
    }

    public static IGrammar? ResolveGrammar(RegistryOptions options, Registry registry, string language)
    {
        // Check cache first — avoids repeated extension/scope lookups.
        if (s_grammarCache.TryGetValue(language, out var cached))
            return cached;

        var lang = options.GetLanguageByExtension("." + language);

        if (lang is null)
        {
            var ext = MapLanguageAlias(language);
            if (ext is not null)
                lang = options.GetLanguageByExtension(ext);
        }

        if (lang is null)
            return null;

        var scope = options.GetScopeByLanguageId(lang.Id);
        if (scope is null)
            return null;

        var grammar = registry.LoadGrammar(scope);
        if (grammar is not null)
            s_grammarCache[language] = grammar;
        return grammar;
    }

    public static string? MapLanguageAlias(string language) =>
        language.ToLowerInvariant() switch
        {
            "csharp" or "c#" => ".cs",
            "javascript" or "js" or "jsx" => ".js",
            "typescript" or "ts" or "tsx" => ".ts",
            "python" or "py" => ".py",
            "ruby" or "rb" => ".rb",
            "rust" or "rs" => ".rs",
            "kotlin" or "kt" => ".kt",
            "swift" => ".swift",
            "golang" or "go" => ".go",
            "cpp" or "c++" => ".cpp",
            "bash" or "shell" or "sh" or "zsh" => ".sh",
            "powershell" or "pwsh" or "ps1" => ".ps1",
            "dockerfile" => ".dockerfile",
            "fsharp" or "f#" => ".fs",
            "scala" => ".scala",
            "haskell" or "hs" => ".hs",
            "lua" => ".lua",
            "perl" or "pl" => ".pl",
            "r" => ".r",
            "objc" or "objective-c" => ".m",
            "clojure" or "clj" => ".clj",
            "elixir" or "ex" => ".ex",
            "erlang" or "erl" => ".erl",
            "groovy" => ".groovy",
            "diff" or "patch" => ".diff",
            "cmake" => ".cmake",
            "makefile" or "make" => ".makefile",
            "graphql" or "gql" => ".graphql",
            "vue" => ".vue",
            "svelte" => ".svelte",
            "proto" or "protobuf" => ".proto",
            "toml" => ".toml",
            "ini" or "properties" => ".ini",
            _ => null,
        };

    // ── Tokenization helpers ──────────────────────────────────────────────────

    private static ITokenizeLineResult? TokenizeLineWithState(
        IGrammar grammar, string line, IStateStack? ruleStack)
    {
        // Always use the timeout overload to guard against catastrophic regex.
        // Passing null ruleStack is fine — TextMateSharp initializes the state
        // the same way the 1-arg overload does.
        return grammar.TokenizeLine(line, ruleStack, s_tokenizeTimeout);
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        return Color.TryParse(hex, out var color) ? color : null;
    }

    // ── Presentation ─────────────────────────────────────────────────────────

    /// <summary>A range of code drawn with its own colour or font style.</summary>
    internal readonly record struct CodeSpan(
        int Start,
        int Length,
        IBrush? Foreground,
        bool Italic,
        bool Bold,
        bool Underline)
    {
        public int End => Start + Length;

        public bool HasSameStyle(CodeSpan other) =>
            ReferenceEquals(Foreground, other.Foreground)
            && Italic == other.Italic && Bold == other.Bold && Underline == other.Underline;
    }

    /// <summary>
    /// Lays the whole code out as one string and applies the highlighted styles directly to its text
    /// layout. Highlighting therefore creates no inline elements, which would each be styled and would
    /// each invalidate the layout as they were added.
    /// </summary>
    private sealed class CodeTextBlock : SelectableTextBlock
    {
        private IReadOnlyList<CodeSpan>? _spans;
        private Rect? _renderViewport;

        // TextBlock rebuilds its layout on every arrange; like its own cache, this keeps the shaped runs
        // so only line layout is redone when the width alone changes.
        private readonly TextRunCache _runCache = new();
        private ShapingKey _shapingKey;
        private GenericTextRunProperties? _defaults;

        // One properties object per style rather than per token: each resolves its font once.
        private readonly Dictionary<RunStyle, TextRunProperties> _runProperties = new();

        public CodeTextBlock()
        {
            EffectiveViewportChanged += (_, e) =>
            {
                var viewport = e.EffectiveViewport;
                if (viewport.Height <= 0 || !double.IsFinite(viewport.Height))
                {
                    _renderViewport = null;
                    return;
                }

                // Keep a screen of prepared lines on each side so small scrolls only move
                // retained drawing commands, rather than rebuilding them every frame.
                if (_renderViewport is { } current
                    && current.Top < viewport.Top - CodeLineHeight
                    && current.Bottom > viewport.Bottom + CodeLineHeight)
                    return;

                _renderViewport = viewport.Inflate(new Thickness(0, viewport.Height));
                InvalidateVisual();
            };
        }

        protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

        internal IReadOnlyList<CodeSpan>? Spans => _spans;

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _renderViewport = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void RenderTextLayout(DrawingContext context, Point origin)
        {
            // Keep the framework's selection rendering and the unrooted bitmap/export path.
            if (_renderViewport is not { } viewport || SelectionStart != SelectionEnd)
            {
                base.RenderTextLayout(context, origin);
                return;
            }

            var y = origin.Y;
            foreach (var line in TextLayout.TextLines)
            {
                if (y > viewport.Bottom)
                    break;
                if (y + line.Height >= viewport.Top)
                    line.Draw(context, new Point(origin.X, y));
                y += line.Height;
            }
        }

        internal void SetCode(string text, IReadOnlyList<CodeSpan>? spans)
        {
            _spans = spans is { Count: > 0 } ? spans : null;
            if (string.Equals(Text, text, StringComparison.Ordinal))
                InvalidateTextLayout();
            else
                Text = text;
        }

        protected override TextLayout CreateTextLayout(string? text)
        {
            if (_spans is null || Inlines is { Count: > 0 })
                return base.CreateTextLayout(text);

            text ??= "";
            var defaults = GetDefaults();
            var paragraph = new GenericTextParagraphProperties(
                FlowDirection, TextAlignment, firstLineInParagraph: true, alwaysCollapsible: false,
                defaults, TextWrapping, LineHeight, 0, LetterSpacing);

            var selectionStart = Math.Min(SelectionStart, SelectionEnd);
            var selectionEnd = Math.Max(SelectionStart, SelectionEnd);
            var selectionForeground = selectionEnd > selectionStart ? SelectionForegroundBrush : null;

            var shapingKey = new ShapingKey(
                text, _spans, defaults, selectionStart, selectionEnd, selectionForeground, FlowDirection, LetterSpacing);
            if (shapingKey != _shapingKey)
            {
                _runCache.Invalidate();
                _shapingKey = shapingKey;
            }

            var source = new CodeTextSource(
                text, defaults, _spans, selectionStart, selectionEnd, selectionForeground, GetRunProperties);
            var max = GetMaxSizeFromConstraint();
            return new TextLayout(source, paragraph, TextTrimming, max.Width, max.Height, MaxLines, _runCache);
        }

        private GenericTextRunProperties GetDefaults()
        {
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            if (_defaults is { } defaults
                && defaults.Typeface.Equals(typeface)
                && defaults.FontRenderingEmSize.Equals(FontSize)
                && ReferenceEquals(defaults.TextDecorations, TextDecorations)
                && ReferenceEquals(defaults.ForegroundBrush, Foreground)
                && ReferenceEquals(defaults.FontFeatures, FontFeatures))
            {
                return defaults;
            }

            _runProperties.Clear();
            return _defaults = new GenericTextRunProperties(
                typeface, FontSize, TextDecorations, Foreground, null, BaselineAlignment.Baseline, null, FontFeatures);
        }

        private TextRunProperties GetRunProperties(IBrush? foreground, CodeSpan style)
        {
            var defaults = _defaults!;
            var key = new RunStyle(foreground ?? defaults.ForegroundBrush, style.Italic, style.Bold, style.Underline);
            if (_runProperties.TryGetValue(key, out var properties))
                return properties;

            var typeface = defaults.Typeface;
            if (style.Italic || style.Bold)
            {
                typeface = new Typeface(
                    typeface.FontFamily,
                    style.Italic ? Avalonia.Media.FontStyle.Italic : typeface.Style,
                    style.Bold ? FontWeight.Bold : typeface.Weight,
                    typeface.Stretch);
            }

            properties = new GenericTextRunProperties(
                typeface,
                defaults.FontRenderingEmSize,
                style.Underline ? Avalonia.Media.TextDecorations.Underline : defaults.TextDecorations,
                key.Foreground,
                null,
                BaselineAlignment.Baseline,
                null,
                defaults.FontFeatures);
            _runProperties[key] = properties;
            return properties;
        }

        private readonly record struct RunStyle(IBrush? Foreground, bool Italic, bool Bold, bool Underline);

        /// <summary>Everything a shaped run depends on; the layout width is deliberately absent.</summary>
        private readonly record struct ShapingKey(
            string? Text,
            IReadOnlyList<CodeSpan>? Spans,
            TextRunProperties? Defaults,
            int SelectionStart,
            int SelectionEnd,
            IBrush? SelectionForeground,
            FlowDirection FlowDirection,
            double LetterSpacing);
    }

    private sealed class CodeTextSource(
        string text,
        TextRunProperties defaults,
        IReadOnlyList<CodeSpan> spans,
        int selectionStart,
        int selectionEnd,
        IBrush? selectionForeground,
        Func<IBrush?, CodeSpan, TextRunProperties> getProperties) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= text.Length)
                return new TextEndOfParagraph();

            var index = FindSpan(textSourceIndex);
            var inSpan = index >= 0 && textSourceIndex < spans[index].End;
            int end;
            TextRunProperties properties;
            if (inSpan)
            {
                end = spans[index].End;
                properties = getProperties(spans[index].Foreground, spans[index]);
            }
            else
            {
                end = index + 1 < spans.Count ? spans[index + 1].Start : text.Length;
                properties = defaults;
            }

            if (selectionForeground is not null)
            {
                if (textSourceIndex < selectionStart)
                {
                    end = Math.Min(end, selectionStart);
                }
                else if (textSourceIndex < selectionEnd)
                {
                    end = Math.Min(end, selectionEnd);
                    properties = getProperties(selectionForeground, inSpan ? spans[index] : default);
                }
            }

            return new TextCharacters(text.AsMemory(textSourceIndex, Math.Min(end, text.Length) - textSourceIndex), properties);
        }

        /// <summary>Index of the last span starting at or before <paramref name="position"/>, or -1.</summary>
        private int FindSpan(int position)
        {
            int low = 0, high = spans.Count - 1, found = -1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (spans[middle].Start <= position)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return found;
        }
    }
}
