using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StrataTheme.Diff;
using StrataTheme.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using Theme = TextMateSharp.Themes.Theme;

namespace StrataTheme.Controls;

/// <summary>
/// Shared unified diff viewer with source highlighting, old/new line numbers and hunk navigation.
/// Set <see cref="TouchMode"/> for touch-sized navigation and scroll-safe code on phones.
/// </summary>
public partial class StrataDiffView : UserControl
{
    public static readonly StyledProperty<string?> UnifiedDiffTextProperty =
        AvaloniaProperty.Register<StrataDiffView, string?>(nameof(UnifiedDiffText));
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<StrataDiffView, string?>(nameof(FilePath));
    public static readonly StyledProperty<double> CodeFontSizeProperty =
        AvaloniaProperty.Register<StrataDiffView, double>(nameof(CodeFontSize), 12.5);
    public static readonly StyledProperty<bool> TouchModeProperty =
        AvaloniaProperty.Register<StrataDiffView, bool>(nameof(TouchMode));
    public static readonly StyledProperty<bool> IsRenderingProperty =
        AvaloniaProperty.Register<StrataDiffView, bool>(nameof(IsRendering));

    public string? UnifiedDiffText { get => GetValue(UnifiedDiffTextProperty); set => SetValue(UnifiedDiffTextProperty, value); }
    public string? FilePath { get => GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public double CodeFontSize { get => GetValue(CodeFontSizeProperty); set => SetValue(CodeFontSizeProperty, value); }
    public bool TouchMode { get => GetValue(TouchModeProperty); set => SetValue(TouchModeProperty, value); }
    public bool IsRendering { get => GetValue(IsRenderingProperty); private set => SetValue(IsRenderingProperty, value); }

    private StackPanel? _diffContent;
    private ScrollViewer? _diffScroller;
    private TextBlock? _statsText;
    private TextBlock? _changeCountText;
    private Button? _prevBtn;
    private Button? _nextBtn;
    private Panel? _loadingOverlay;

    private readonly List<Border> _changeRegionControls = [];
    private int _currentChangeIndex = -1;
    private EventHandler? _pendingScrollHandler;
    private Func<DiffDocument>? _createDocument;
    private CancellationTokenSource? _renderCancellation;
    private long _renderVersion;
    private bool _renderQueued;

    public StrataDiffView()
    {
        InitializeComponent();
        _diffContent = this.FindControl<StackPanel>("DiffContent");
        _diffScroller = this.FindControl<ScrollViewer>("DiffScroller");
        _statsText = this.FindControl<TextBlock>("StatsText");
        _changeCountText = this.FindControl<TextBlock>("ChangeCountText");
        _prevBtn = this.FindControl<Button>("PrevChangeBtn");
        _nextBtn = this.FindControl<Button>("NextChangeBtn");
        _loadingOverlay = this.FindControl<Panel>("LoadingOverlay");

        if (_prevBtn is not null) _prevBtn.Click += (_, _) => NavigateChange(-1);
        if (_nextBtn is not null) _nextBtn.Click += (_, _) => NavigateChange(1);
        ActualThemeVariantChanged += (_, _) => QueueRender();
        UpdateTouchMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UnifiedDiffTextProperty)
        {
            var text = UnifiedDiffText;
            _createDocument = () => UnifiedDiffBuilder.BuildFromUnifiedDiff(text);
            QueueRender();
        }
        else if (change.Property == FilePathProperty || change.Property == CodeFontSizeProperty)
            QueueRender();
        else if (change.Property == TouchModeProperty)
        {
            UpdateTouchMode();
            QueueRender();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        QueueRender();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderVersion++;
        _renderCancellation?.Cancel();
        CancelPendingScroll();
        base.OnDetachedFromVisualTree(e);
    }

    public void SetSnapshotDiff(string filePath, string? originalContent, string? currentContent) =>
        SetDocument(filePath, () => UnifiedDiffBuilder.BuildFromSnapshots(originalContent, currentContent));

    public void SetUnifiedDiffText(string filePath, string? unifiedDiff) =>
        SetDocument(filePath, () => UnifiedDiffBuilder.BuildFromUnifiedDiff(unifiedDiff));

    public void SetDocument(string filePath, Func<DiffDocument> createDocument)
    {
        ArgumentNullException.ThrowIfNull(createDocument);
        SetCurrentValue(FilePathProperty, filePath);
        _createDocument = createDocument;
        QueueRender();
    }

    private void UpdateTouchMode()
    {
        foreach (var button in new[] { _prevBtn, _nextBtn })
        {
            if (button is null)
                continue;
            button.Height = button.Width = TouchMode ? 48 : 24;
            button.MinHeight = TouchMode ? 48 : 0;
        }
    }

    private void QueueRender()
    {
        if (_diffContent is null || _createDocument is null)
            return;
        _renderVersion++;
        _renderCancellation?.Cancel();
        if (_renderQueued)
            return;
        _renderQueued = true;
        Dispatcher.UIThread.Post(async () =>
        {
            _renderQueued = false;
            if (TopLevel.GetTopLevel(this) is null || _createDocument is null)
                return;
            await RenderDiffAsync(_createDocument, _renderVersion);
        }, DispatcherPriority.Background);
    }

    private async Task RenderDiffAsync(Func<DiffDocument> createDocument, long version)
    {
        if (_diffContent is null)
            throw new InvalidOperationException("Diff view content is not initialized.");
        using var cancellation = new CancellationTokenSource();
        _renderCancellation = cancellation;
        var token = cancellation.Token;
        var language = Path.GetExtension(FilePath)?.TrimStart('.') ?? string.Empty;
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        IsRendering = true;
        _diffContent.Children.Clear();
        _changeRegionControls.Clear();
        _currentChangeIndex = -1;
        CancelPendingScroll();

        if (_loadingOverlay is not null)
            _loadingOverlay.IsVisible = true;

        try
        {
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var document = createDocument();
                var displayLines = document.Hunks.SelectMany(hunk => hunk.Lines).Select(line => line.Text).ToArray();
                var tokenizedLines = TokenizeLines(displayLines, language, isDark, token);
                return (Document: document, TokenizedLines: tokenizedLines);
            }, token);
            if (version != _renderVersion)
                return;
            await BuildDiffViewAsync(result.Document, result.TokenizedLines, isDark, token);
            token.ThrowIfCancellationRequested();
            if (version != _renderVersion)
                return;
            UpdateStats(result.Document.AddedLineCount, result.Document.RemovedLineCount);
            UpdateNavigation();
            ScheduleScrollToFirstChange();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_renderCancellation, cancellation))
            {
                _renderCancellation = null;
                IsRendering = false;
                if (_loadingOverlay is not null)
                    _loadingOverlay.IsVisible = false;
            }
        }
    }

    private readonly record struct TokenRun(int Start, int End, int FgColorId, int FontStyleBits, string? ColorHex = null);
    private readonly record struct TokenizedLine(TokenRun[] Runs);

    private static readonly object SyntaxGate = new();

    private static bool UsePortableHighlighting => OperatingSystem.IsAndroid() || OperatingSystem.IsBrowser();

    private static TokenizedLine[] TokenizeLines(string[] lines, string language, bool isDark, CancellationToken token)
    {
        if (UsePortableHighlighting)
        {
            var result = new TokenizedLine[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                result[i] = new TokenizedLine(PortableCodeHighlighter.Tokenize(lines[i], language)
                    .Select(part => new TokenRun(part.Start, part.Start + part.Length, 0, 0,
                        PortableCodeHighlighter.ColorFor(part.Kind, isDark))).ToArray());
            }
            return result;
        }
        lock (SyntaxGate)
            return TokenizeNativeLines(lines, language, isDark, token);
    }

    private static TokenizedLine[] TokenizeNativeLines(string[] lines, string language, bool isDark, CancellationToken cancellationToken)
    {
        var (options, registry) = GetRegistryPair(isDark);
        IGrammar? grammar = null;
        if (!string.IsNullOrWhiteSpace(language))
            grammar = StrataCodeBlock.ResolveGrammar(options, registry, language);
        Theme? theme = grammar is not null ? registry.GetTheme() : null;

        var result = new TokenizedLine[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineText = lines[i];
            if (grammar is null || theme is null || string.IsNullOrEmpty(lineText))
            {
                result[i] = new TokenizedLine([]);
                continue;
            }

            try
            {
                var tokenResult = grammar.TokenizeLine(lineText, null, TimeSpan.FromMilliseconds(150));
                if (tokenResult?.Tokens is not { Length: > 0 })
                {
                    result[i] = new TokenizedLine([]);
                    continue;
                }

                var runs = new TokenRun[tokenResult.Tokens.Length];
                for (var j = 0; j < tokenResult.Tokens.Length; j++)
                {
                    var token = tokenResult.Tokens[j];
                    var start = token.StartIndex;
                    var end = j + 1 < tokenResult.Tokens.Length
                        ? tokenResult.Tokens[j + 1].StartIndex
                        : lineText.Length;

                    if (start >= lineText.Length)
                    {
                        runs[j] = new TokenRun(0, 0, 0, -1);
                        continue;
                    }

                    if (end > lineText.Length)
                        end = lineText.Length;

                    var fgColorId = 0;
                    var fontStyleBits = -1;
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

                    runs[j] = new TokenRun(start, end, fgColorId, fontStyleBits);
                }

                result[i] = new TokenizedLine(runs);
            }
            catch
            {
                result[i] = new TokenizedLine([]);
            }
        }

        return result;
    }

    private async Task BuildDiffViewAsync(DiffDocument document, TokenizedLine[] tokenizedLines, bool isDark, CancellationToken token)
    {
        if (_diffContent is null)
            return;

        if (document.Hunks.Count == 0)
        {
            _diffContent.Children.Add(BuildEmptyState(document.EmptyStateText ?? "No diff available."));
            return;
        }

        var monoFont = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New, monospace");
        var fontSize = CodeFontSize;
        const int batchSize = 80;

        var brushMap = GetBrushMap(isDark);
        Theme? theme = UsePortableHighlighting ? null : GetRegistryPair(isDark).registry.GetTheme();

        var palette = DiffPalette.Create(this, isDark);
        var displayLineIndex = 0;

        foreach (var hunk in document.Hunks)
        {
            token.ThrowIfCancellationRequested();
            var header = BuildHunkHeader(hunk.Header, monoFont, fontSize, palette);
            _diffContent.Children.Add(header);
            _changeRegionControls.Add(header);

            foreach (var line in hunk.Lines)
            {
                token.ThrowIfCancellationRequested();
                var tokenized = displayLineIndex < tokenizedLines.Length
                    ? tokenizedLines[displayLineIndex]
                    : new TokenizedLine([]);
                displayLineIndex++;

                _diffContent.Children.Add(BuildDiffLine(line, tokenized, theme, brushMap, monoFont, fontSize, palette, TouchMode));

                if (displayLineIndex % batchSize == 0)
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }
    }

    private sealed record DiffPalette(
        IBrush HeaderBackground,
        IBrush HeaderForeground,
        IBrush LineNumberBackground,
        IBrush LineNumberForeground,
        IBrush AddedBackground,
        IBrush AddedLineNumberBackground,
        IBrush AddedForeground,
        IBrush RemovedBackground,
        IBrush RemovedLineNumberBackground,
        IBrush RemovedForeground,
        IBrush ContextBackground)
    {
        public static DiffPalette Create(Control owner, bool isDark)
        {
            var fallback = isDark
                ? new DiffPalette(
                    HeaderBackground: new SolidColorBrush(Color.FromArgb(28, 88, 166, 255)),
                    HeaderForeground: new SolidColorBrush(Color.FromArgb(180, 200, 220, 255)),
                    LineNumberBackground: new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
                    LineNumberForeground: new SolidColorBrush(Color.FromArgb(120, 200, 200, 200)),
                    AddedBackground: new SolidColorBrush(Color.FromArgb(28, 63, 185, 80)),
                    AddedLineNumberBackground: new SolidColorBrush(Color.FromArgb(60, 63, 185, 80)),
                    AddedForeground: new SolidColorBrush(Color.FromArgb(220, 63, 185, 80)),
                    RemovedBackground: new SolidColorBrush(Color.FromArgb(32, 248, 81, 73)),
                    RemovedLineNumberBackground: new SolidColorBrush(Color.FromArgb(60, 248, 81, 73)),
                    RemovedForeground: new SolidColorBrush(Color.FromArgb(220, 248, 81, 73)),
                    ContextBackground: Brushes.Transparent)
                : new DiffPalette(
                    HeaderBackground: new SolidColorBrush(Color.FromArgb(28, 0, 102, 204)),
                    HeaderForeground: new SolidColorBrush(Color.FromArgb(200, 0, 102, 204)),
                    LineNumberBackground: new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)),
                    LineNumberForeground: new SolidColorBrush(Color.FromArgb(120, 80, 80, 80)),
                    AddedBackground: new SolidColorBrush(Color.FromArgb(24, 0, 160, 0)),
                    AddedLineNumberBackground: new SolidColorBrush(Color.FromArgb(48, 0, 160, 0)),
                    AddedForeground: new SolidColorBrush(Color.FromArgb(210, 0, 140, 0)),
                    RemovedBackground: new SolidColorBrush(Color.FromArgb(24, 208, 0, 0)),
                    RemovedLineNumberBackground: new SolidColorBrush(Color.FromArgb(48, 208, 0, 0)),
                    RemovedForeground: new SolidColorBrush(Color.FromArgb(210, 176, 0, 0)),
                    ContextBackground: Brushes.Transparent);
            IBrush Brush(string key, IBrush defaultBrush) =>
                owner.TryFindResource(key, out var value) && value is IBrush brush ? brush : defaultBrush;
            return fallback with
            {
                HeaderBackground = Brush("Brush.AccentSubtle", fallback.HeaderBackground),
                HeaderForeground = Brush("Brush.AccentDefault", fallback.HeaderForeground),
                LineNumberBackground = Brush("Brush.Surface2", fallback.LineNumberBackground),
                LineNumberForeground = Brush("Brush.TextTertiary", fallback.LineNumberForeground),
                AddedBackground = Brush("Brush.SuccessSubtle", fallback.AddedBackground),
                AddedLineNumberBackground = Brush("Brush.SuccessSubtle", fallback.AddedLineNumberBackground),
                AddedForeground = Brush("Brush.SuccessDefault", fallback.AddedForeground),
                RemovedBackground = Brush("Brush.DangerSubtle", fallback.RemovedBackground),
                RemovedLineNumberBackground = Brush("Brush.DangerSubtle", fallback.RemovedLineNumberBackground),
                RemovedForeground = Brush("Brush.DangerDefault", fallback.RemovedForeground)
            };
        }
    }

    private static Border BuildHunkHeader(string? headerText, FontFamily font, double fontSize, DiffPalette palette)
    {
        return new Border
        {
            Background = palette.HeaderBackground,
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 6, 0, 2),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(headerText) ? "Change" : headerText,
                FontFamily = font,
                FontSize = fontSize * 0.95,
                Foreground = palette.HeaderForeground,
            }
        };
    }

    private static Border BuildDiffLine(
        DiffLine line,
        TokenizedLine tokenized,
        Theme? theme,
        Dictionary<int, IBrush> brushMap,
        FontFamily font,
        double fontSize,
        DiffPalette palette,
        bool touchMode)
    {
        var lineBackground = line.Kind switch
        {
            DiffLineKind.Added => palette.AddedBackground,
            DiffLineKind.Removed => palette.RemovedBackground,
            _ => palette.ContextBackground
        };

        var lineNumberBackground = line.Kind switch
        {
            DiffLineKind.Added => palette.AddedLineNumberBackground,
            DiffLineKind.Removed => palette.RemovedLineNumberBackground,
            _ => palette.LineNumberBackground
        };

        var symbolForeground = line.Kind switch
        {
            DiffLineKind.Added => palette.AddedForeground,
            DiffLineKind.Removed => palette.RemovedForeground,
            _ => palette.LineNumberForeground
        };

        var oldNumber = BuildLineNumberCell(line.OldLineNumber, font, fontSize, lineNumberBackground, palette.LineNumberForeground);
        var newNumber = BuildLineNumberCell(line.NewLineNumber, font, fontSize, lineNumberBackground, palette.LineNumberForeground);

        var symbol = new Border
        {
            Background = lineNumberBackground,
            Width = 18,
            Padding = new Thickness(0),
            Child = new TextBlock
            {
                Text = line.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "−",
                    _ => " "
                },
                FontFamily = font,
                FontSize = fontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = symbolForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };

        TextBlock content = touchMode ? new TextBlock() : new SelectableTextBlock();
        content.FontFamily = font;
        content.FontSize = fontSize;
        content.LineHeight = Math.Max(StrataCodeBlock.CodeLineHeight, fontSize * 1.5);
        content.TextWrapping = TextWrapping.NoWrap;
        content.Margin = new Thickness(8, 0, 0, 0);
        content.VerticalAlignment = VerticalAlignment.Center;
        content.Classes.Add("diff-code");

        var inlines = content.Inlines ??= new InlineCollection();
        AddTokenizedInlines(inlines, line.Text, tokenized, theme, brushMap);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
        Grid.SetColumn(oldNumber, 0);
        Grid.SetColumn(newNumber, 1);
        Grid.SetColumn(symbol, 2);
        Grid.SetColumn(content, 3);
        grid.Children.Add(oldNumber);
        grid.Children.Add(newNumber);
        grid.Children.Add(symbol);
        grid.Children.Add(content);

        var row = new Border
        {
            Background = lineBackground,
            MinHeight = touchMode ? 26 : 20,
            Padding = new Thickness(0, 1),
            Child = grid,
        };
        row.Classes.Add(line.Kind switch
        {
            DiffLineKind.Added => "diff-added",
            DiffLineKind.Removed => "diff-removed",
            _ => "diff-context"
        });
        return row;
    }

    private static Border BuildLineNumberCell(int? lineNumber, FontFamily font, double fontSize, IBrush background, IBrush foreground)
    {
        return new Border
        {
            Background = background,
            Width = 48,
            Padding = new Thickness(4, 0),
            Child = new TextBlock
            {
                Text = lineNumber?.ToString() ?? string.Empty,
                FontFamily = font,
                FontSize = fontSize,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
    }

    private static void AddTokenizedInlines(
        InlineCollection inlines,
        string text,
        TokenizedLine tokenized,
        Theme? theme,
        Dictionary<int, IBrush> brushMap)
    {
        if (tokenized.Runs.Length > 0 && !string.IsNullOrEmpty(text))
        {
            var offset = 0;
            foreach (var run in tokenized.Runs)
            {
                if (run.Start >= run.End || run.Start >= text.Length || run.Start < offset)
                    continue;

                var end = Math.Min(run.End, text.Length);
                if (run.Start > offset)
                    inlines.Add(new Run(text[offset..run.Start]));
                var segment = new Run(text[run.Start..end]);

                if (run.ColorHex is { } colorHex)
                    segment.Foreground = new SolidColorBrush(Color.Parse(colorHex)).ToImmutable();
                else if (run.FgColorId > 0 && theme is not null)
                {
                    var brush = GetOrCreateBrush(brushMap, theme, run.FgColorId);
                    if (brush != Brushes.Transparent)
                        segment.Foreground = brush;
                }

                if (run.FontStyleBits > 0)
                {
                    if ((run.FontStyleBits & (int)TextMateSharp.Themes.FontStyle.Italic) != 0)
                        segment.FontStyle = Avalonia.Media.FontStyle.Italic;
                    if ((run.FontStyleBits & (int)TextMateSharp.Themes.FontStyle.Bold) != 0)
                        segment.FontWeight = FontWeight.Bold;
                }

                inlines.Add(segment);
                offset = end;
            }
            if (offset < text.Length)
                inlines.Add(new Run(text[offset..]));
        }
        else if (!string.IsNullOrEmpty(text))
        {
            inlines.Add(new Run(text));
        }
    }

    private static Border BuildEmptyState(string message)
    {
        return new Border
        {
            Padding = new Thickness(16),
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Classes = { "caption" },
                Foreground = Brushes.Gray
            }
        };
    }

    private void UpdateStats(int added, int removed)
    {
        if (_statsText is null)
            return;

        _statsText.Text = removed == 0
            ? $"+{added}"
            : $"+{added}  −{removed}";
    }

    private void UpdateNavigation()
    {
        if (_changeCountText is null)
            return;

        _changeCountText.Text = _changeRegionControls.Count > 0
            ? _currentChangeIndex >= 0
                ? $"{_currentChangeIndex + 1} / {_changeRegionControls.Count}"
                : $"{_changeRegionControls.Count} changes"
            : string.Empty;
        if (_prevBtn is not null) _prevBtn.IsEnabled = _changeRegionControls.Count > 0;
        if (_nextBtn is not null) _nextBtn.IsEnabled = _changeRegionControls.Count > 0;
    }

    private void NavigateChange(int direction)
    {
        if (_changeRegionControls.Count == 0 || _diffScroller is null)
            return;

        _currentChangeIndex += direction;
        if (_currentChangeIndex >= _changeRegionControls.Count)
            _currentChangeIndex = 0;
        if (_currentChangeIndex < 0)
            _currentChangeIndex = _changeRegionControls.Count - 1;

        ScrollToCurrentChange();
        UpdateNavigation();
    }

    private void ScrollToCurrentChange()
    {
        if (_changeRegionControls.Count == 0 || _diffScroller is null)
            return;
        if (_currentChangeIndex < 0 || _currentChangeIndex >= _changeRegionControls.Count)
            return;

        var control = _changeRegionControls[_currentChangeIndex];
        if (control.Bounds.Height > 0)
            _diffScroller.Offset = new Vector(_diffScroller.Offset.X, Math.Max(0, control.Bounds.Y - 8));
    }

    private void ScheduleScrollToFirstChange()
    {
        CancelPendingScroll();
        if (_changeRegionControls.Count == 0 || _diffContent is null || _diffScroller is null)
            return;

        _pendingScrollHandler = (_, _) =>
        {
            if (_changeRegionControls.Count == 0)
            {
                CancelPendingScroll();
                return;
            }

            var first = _changeRegionControls[0];
            if (first.Bounds.Height <= 0)
                return;

            CancelPendingScroll();
            _currentChangeIndex = 0;
            ScrollToCurrentChange();
            UpdateNavigation();
        };

        _diffContent.LayoutUpdated += _pendingScrollHandler;
    }

    private void CancelPendingScroll()
    {
        if (_pendingScrollHandler is not null && _diffContent is not null)
        {
            _diffContent.LayoutUpdated -= _pendingScrollHandler;
            _pendingScrollHandler = null;
        }
    }

    private static (RegistryOptions options, Registry registry) GetRegistryPair(bool isDark)
    {
        if (isDark)
        {
            s_darkOptions ??= new RegistryOptions(ThemeName.DarkPlus);
            s_darkRegistry ??= new Registry(s_darkOptions);
            return (s_darkOptions, s_darkRegistry);
        }

        s_lightOptions ??= new RegistryOptions(ThemeName.LightPlus);
        s_lightRegistry ??= new Registry(s_lightOptions);
        return (s_lightOptions, s_lightRegistry);
    }

    private static RegistryOptions? s_darkOptions;
    private static Registry? s_darkRegistry;
    private static RegistryOptions? s_lightOptions;
    private static Registry? s_lightRegistry;
    private static Dictionary<int, IBrush>? s_darkBrushMap;
    private static Dictionary<int, IBrush>? s_lightBrushMap;

    private static Dictionary<int, IBrush> GetBrushMap(bool isDark)
        => isDark ? (s_darkBrushMap ??= []) : (s_lightBrushMap ??= []);

    private static IBrush GetOrCreateBrush(Dictionary<int, IBrush> brushMap, Theme theme, int colorId)
    {
        if (brushMap.TryGetValue(colorId, out var brush))
            return brush;

        var hex = theme.GetColor(colorId);
        brush = Color.TryParse(hex, out var color)
            ? new SolidColorBrush(color).ToImmutable()
            : Brushes.Transparent;
        brushMap[colorId] = brush;
        return brush;
    }
}
