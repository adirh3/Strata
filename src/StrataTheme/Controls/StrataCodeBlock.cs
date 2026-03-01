using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
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
/// <para>The control renders code as colored <see cref="Run"/> inlines inside a
/// <see cref="SelectableTextBlock"/>, supporting text selection and copy.</para>
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
    private readonly SelectableTextBlock _presenter;

    // ── Caching to skip redundant work ───────────────────────────────────────
    private string? _cachedText;
    private string? _cachedLanguage;
    private bool _cachedIsDark;

    /// <summary>
    /// Line height used for the code presenter.
    /// Must match the value used in <see cref="StrataMarkdown"/> MinHeight calculations.
    /// </summary>
    internal const double CodeLineHeight = 18;

    public StrataCodeBlock()
    {
        _presenter = new SelectableTextBlock
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
    /// Re-tokenizes the text and rebuilds the highlighted inline collection.
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
        _presenter.Inlines?.Clear();

        if (string.IsNullOrWhiteSpace(text))
            return;

        var inlines = _presenter.Inlines ??= new InlineCollection();

        try
        {
            Highlight(inlines, text!, language, isDark);
        }
        catch
        {
            // Graceful fallback: clear any partial inlines and render plain text.
            inlines.Clear();
            AppendPlainTextLines(inlines, text!);
        }
    }

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

    private static void Highlight(InlineCollection inlines, string text, string? language, bool isDark)
    {
        var (options, registry) = GetRegistryPair(isDark);

        IGrammar? grammar = null;
        if (!string.IsNullOrWhiteSpace(language))
            grammar = ResolveGrammar(options, registry, language!);

        // No grammar found → plain text with line breaks preserved.
        if (grammar is null)
        {
            AppendPlainTextLines(inlines, text);
            return;
        }

        var theme = registry.GetTheme();
        var brushMap = GetBrushMap(isDark);

        IStateStack? ruleStack = null;
        int lineStart = 0;

        while (lineStart <= text.Length)
        {
            // Find end of current line (without allocating substrings for splitting).
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;

            // Trim trailing \r without allocating.
            int contentEnd = lineEnd;
            if (contentEnd > lineStart && text[contentEnd - 1] == '\r')
                contentEnd--;

            if (lineStart > 0)
                inlines.Add(new LineBreak());

            int lineLen = contentEnd - lineStart;

            if (lineLen == 0)
            {
                // Tokenize empty lines to maintain state across multi-line constructs.
                var emptyResult = TokenizeLineWithState(grammar, string.Empty, ruleStack);
                ruleStack = emptyResult?.RuleStack ?? ruleStack;
                lineStart = lineEnd + 1;
                continue;
            }

            // Extract line substring (unavoidable — TextMateSharp API requires string).
            var line = text.Substring(lineStart, lineLen);

            var result = TokenizeLineWithState(grammar, line, ruleStack);

            if (result?.Tokens is null || result.Tokens.Length == 0)
            {
                inlines.Add(new Run(line));
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

                var tokenText = line.Substring(start, end - start);

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

                var run = new Run(tokenText);

                if (fgColorId > 0)
                {
                    var brush = GetOrCreateBrush(brushMap, theme, fgColorId);
                    if (brush != Brushes.Transparent)
                        run.Foreground = brush;
                }

                if (fontStyleBits > 0)
                {
                    if ((fontStyleBits & (int)TextMateSharp.Themes.FontStyle.Italic) != 0)
                        run.FontStyle = Avalonia.Media.FontStyle.Italic;
                    if ((fontStyleBits & (int)TextMateSharp.Themes.FontStyle.Bold) != 0)
                        run.FontWeight = FontWeight.Bold;
                    if ((fontStyleBits & (int)TextMateSharp.Themes.FontStyle.Underline) != 0)
                        run.TextDecorations = TextDecorations.Underline;
                }

                inlines.Add(run);
            }

            lineStart = lineEnd + 1;
        }
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

    internal static IGrammar? ResolveGrammar(RegistryOptions options, Registry registry, string language)
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

    internal static string? MapLanguageAlias(string language) =>
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

    // ── Plain text helper ────────────────────────────────────────────────────

    /// <summary>
    /// Appends multi-line text as Run + LineBreak inlines, preserving visual line breaks.
    /// A single <see cref="Run"/> containing '\n' does not render as line breaks
    /// in <see cref="SelectableTextBlock"/> — explicit <see cref="LineBreak"/> inlines are required.
    /// </summary>
    internal static void AppendPlainTextLines(InlineCollection inlines, string text)
    {
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;

            int contentEnd = lineEnd;
            if (contentEnd > lineStart && text[contentEnd - 1] == '\r')
                contentEnd--;

            if (lineStart > 0)
                inlines.Add(new LineBreak());

            int lineLen = contentEnd - lineStart;
            if (lineLen > 0)
                inlines.Add(new Run(text.Substring(lineStart, lineLen)));

            lineStart = lineEnd + 1;
        }
    }

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
}
