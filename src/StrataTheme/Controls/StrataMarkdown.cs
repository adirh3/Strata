using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using AvaloniaEdit;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

/// <summary>
/// Lightweight markdown renderer. Supports headings, bullet lists, paragraphs,
/// inline links, and fenced code blocks with syntax highlighting (via TextMate).
/// </summary>
/// <remarks>
/// <para><b>XAML usage (standalone card):</b></para>
/// <code>
/// &lt;controls:StrataMarkdown Markdown="## Hello\nSome **text** here."
///                            ShowTitle="True" Title="Response" /&gt;
/// </code>
/// <para><b>XAML usage (inline in a chat message):</b></para>
/// <code>
/// &lt;controls:StrataMarkdown Markdown="{Binding Text}" IsInline="True" /&gt;
/// </code>
/// </remarks>
public class StrataMarkdown : ContentControl
{
    private static readonly Regex LinkRegex = new(@"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.Compiled);

    private readonly StackPanel _root;
    private readonly Border _surface;
    private readonly TextBlock _title;
    private readonly StackPanel _contentHost;
    private IBrush? _inlineCodeBrush;
    private IBrush? _linkBrush;
    private readonly Dictionary<Run, string> _linkRuns = new();
    private string _lastThemeVariant = string.Empty;
    private double _bodyFontSize = 14;
    private readonly Dictionary<string, StrataChart> _chartCache = new();
    private HashSet<string> _chartKeysUsed = new();
    private readonly Dictionary<string, StrataMermaid> _diagramCache = new();
    private HashSet<string> _diagramKeysUsed = new();
    private Border? _chartPlaceholder;

    private readonly Dictionary<string, StrataConfidence> _confidenceCache = new();
    private HashSet<string> _confidenceKeysUsed = new();

    private readonly Dictionary<string, StrataFork> _comparisonCache = new();
    private HashSet<string> _comparisonKeysUsed = new();

    private readonly Dictionary<string, StrataCard> _cardCache = new();
    private HashSet<string> _cardKeysUsed = new();

    private readonly Dictionary<string, Control> _sourcesCache = new();
    private HashSet<string> _sourcesKeysUsed = new();

    private Border? _blockPlaceholder;

    /// <summary>Markdown source text. The control re-renders whenever this changes.</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<StrataMarkdown, string?>(nameof(Markdown));

    /// <summary>Title text shown above the rendered content when <see cref="ShowTitle"/> is true.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataMarkdown, string>(nameof(Title), "Markdown");

    /// <summary>Whether to display the <see cref="Title"/> header above the content.</summary>
    public static readonly StyledProperty<bool> ShowTitleProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(ShowTitle));

    /// <summary>When true, strips the card-like surface border for embedding inside other controls.</summary>
    public static readonly StyledProperty<bool> IsInlineProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(IsInline));

    static StrataMarkdown()
    {
        MarkdownProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.Rebuild());
        TitleProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.UpdateTitle());
        ShowTitleProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.UpdateTitle());
        IsInlineProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.UpdateSurfaceMode());
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowTitle
    {
        get => GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public bool IsInline
    {
        get => GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    public StrataMarkdown()
    {
        _title = new TextBlock();
        _title.Classes.Add("strata-md-title");

        _contentHost = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _surface = new Border { Child = _contentHost };
        _surface.Classes.Add("strata-md-surface");

        _root = new StackPanel { Spacing = 6 };
        _root.Children.Add(_title);
        _root.Children.Add(_surface);

        Content = _root;
        _lastThemeVariant = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light).ToString();
        Rebuild();
        UpdateTitle();
        UpdateSurfaceMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (string.Equals(change.Property.Name, nameof(FontSize), StringComparison.Ordinal))
        {
            Rebuild();
            return;
        }

        if (!string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
            return;

        var currentVariant = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light).ToString();
        if (string.Equals(currentVariant, _lastThemeVariant, StringComparison.Ordinal))
            return;

        _lastThemeVariant = currentVariant;
        _inlineCodeBrush = null;
        _linkBrush = null;
        Rebuild();
    }

    private void UpdateTitle()
    {
        _title.Text = Title;
        _title.IsVisible = ShowTitle && !string.IsNullOrWhiteSpace(Title);
    }

    private void UpdateSurfaceMode()
    {
        if (IsInline)
        {
            _surface.Classes.Remove("strata-md-surface");
            _surface.Classes.Add("strata-md-surface-inline");
        }
        else
        {
            _surface.Classes.Remove("strata-md-surface-inline");
            _surface.Classes.Add("strata-md-surface");
        }
    }

    /// <summary>Returns the effective body font size, using the inherited FontSize if &gt; 12, otherwise resolving Font.SizeBody.</summary>
    private double GetBodyFontSize()
    {
        var fs = FontSize;
        if (fs > 12) return fs;

        // Try resolving from theme resources
        if (this.TryFindResource("Font.SizeBody", ActualThemeVariant, out var res) && res is double d)
            return d;

        return 14; // Strata default
    }

    private void Rebuild()
    {
        _contentHost.Children.Clear();
        _linkRuns.Clear();
        _bodyFontSize = GetBodyFontSize();
        _chartKeysUsed = new HashSet<string>();
        _diagramKeysUsed = new HashSet<string>();
        _confidenceKeysUsed = new HashSet<string>();
        _comparisonKeysUsed = new HashSet<string>();
        _cardKeysUsed = new HashSet<string>();
        _sourcesKeysUsed = new HashSet<string>();

        var source = Markdown;
        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalized = source.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');

            var paragraphBuffer = new StringBuilder();
            var codeBuffer = new StringBuilder();
            var inCodeBlock = false;
            var codeLanguage = string.Empty;
            var tableBuffer = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;

                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph(paragraphBuffer);
                    FlushTable(tableBuffer);

                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeLanguage = line.Length > 3 ? line[3..].Trim() : string.Empty;
                        codeBuffer.Clear();
                    }
                    else
                    {
                        var code = codeBuffer.ToString();
                        if (string.Equals(codeLanguage, "chart", StringComparison.OrdinalIgnoreCase))
                            AddChart(code);
                        else if (string.Equals(codeLanguage, "mermaid", StringComparison.OrdinalIgnoreCase))
                            AddMermaidChart(code);
                        else if (string.Equals(codeLanguage, "confidence", StringComparison.OrdinalIgnoreCase))
                            AddConfidence(code);
                        else if (string.Equals(codeLanguage, "comparison", StringComparison.OrdinalIgnoreCase))
                            AddComparison(code);
                        else if (string.Equals(codeLanguage, "card", StringComparison.OrdinalIgnoreCase))
                            AddCard(code);
                        else if (string.Equals(codeLanguage, "sources", StringComparison.OrdinalIgnoreCase))
                            AddSources(code);
                        else
                            AddCodeBlock(code, codeLanguage);
                        inCodeBlock = false;
                        codeLanguage = string.Empty;
                        codeBuffer.Clear();
                    }

                    continue;
                }

                if (inCodeBlock)
                {
                    codeBuffer.AppendLine(line);
                    continue;
                }

                if (IsTableLine(line))
                {
                    FlushParagraph(paragraphBuffer);
                    tableBuffer.Add(line);
                    continue;
                }

                if (tableBuffer.Count > 0)
                    FlushTable(tableBuffer);

                if (TryParseHeading(line, out var level, out var headingText))
                {
                    FlushParagraph(paragraphBuffer);
                    AddHeading(level, headingText);
                    continue;
                }

                var indentLevel = GetIndentLevel(line);

                if (TryParseBullet(line, out var bulletText))
                {
                    FlushParagraph(paragraphBuffer);
                    AddBullet(bulletText, indentLevel);
                    continue;
                }

                if (TryParseNumberedItem(line, out var number, out var numText))
                {
                    FlushParagraph(paragraphBuffer);
                    AddNumberedItem(number, numText, indentLevel);
                    continue;
                }

                if (IsHorizontalRule(line))
                {
                    FlushParagraph(paragraphBuffer);
                    AddHorizontalRule();
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph(paragraphBuffer);
                    continue;
                }

                paragraphBuffer.AppendLine(line);
            }

            if (inCodeBlock)
            {
                var code = codeBuffer.ToString();
                if (string.Equals(codeLanguage, "chart", StringComparison.OrdinalIgnoreCase))
                    AddChart(code);
                else if (string.Equals(codeLanguage, "mermaid", StringComparison.OrdinalIgnoreCase))
                    AddMermaidChart(code);
                else if (string.Equals(codeLanguage, "confidence", StringComparison.OrdinalIgnoreCase))
                    AddConfidence(code);
                else if (string.Equals(codeLanguage, "comparison", StringComparison.OrdinalIgnoreCase))
                    AddComparison(code);
                else if (string.Equals(codeLanguage, "card", StringComparison.OrdinalIgnoreCase))
                    AddCard(code);
                else if (string.Equals(codeLanguage, "sources", StringComparison.OrdinalIgnoreCase))
                    AddSources(code);
                else
                    AddCodeBlock(code, codeLanguage);
            }

            FlushTable(tableBuffer);
            FlushParagraph(paragraphBuffer);
        }

        // Evict cached controls no longer present in the markdown
        foreach (var staleKey in _chartCache.Keys.Except(_chartKeysUsed).ToList())
            _chartCache.Remove(staleKey);
        foreach (var staleKey in _diagramCache.Keys.Except(_diagramKeysUsed).ToList())
            _diagramCache.Remove(staleKey);
        foreach (var staleKey in _confidenceCache.Keys.Except(_confidenceKeysUsed).ToList())
            _confidenceCache.Remove(staleKey);
        foreach (var staleKey in _comparisonCache.Keys.Except(_comparisonKeysUsed).ToList())
            _comparisonCache.Remove(staleKey);
        foreach (var staleKey in _cardCache.Keys.Except(_cardKeysUsed).ToList())
            _cardCache.Remove(staleKey);
        foreach (var staleKey in _sourcesCache.Keys.Except(_sourcesKeysUsed).ToList())
            _sourcesCache.Remove(staleKey);
    }

    private void FlushParagraph(StringBuilder paragraphBuffer)
    {
        var text = paragraphBuffer.ToString().Trim();
        paragraphBuffer.Clear();

        if (string.IsNullOrWhiteSpace(text))
            return;

        var paragraph = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        paragraph.Classes.Add("strata-md-paragraph");
        _contentHost.Children.Add(paragraph);
    }

    private void AddHeading(int level, string text)
    {
        var heading = CreateRichText(
            text,
            level switch
            {
                1 => _bodyFontSize * 1.28,
                2 => _bodyFontSize * 1.12,
                _ => _bodyFontSize * 1.04
            },
            _bodyFontSize * 1.6,
            TextWrapping.Wrap);
        heading.FontWeight = FontWeight.SemiBold;
        var topMargin = _contentHost.Children.Count > 0
            ? level switch { 1 => 14, 2 => 10, _ => 6 }
            : 0;
        heading.Margin = new Thickness(0, topMargin, 0, 2);
        heading.Classes.Add("strata-md-heading");
        _contentHost.Children.Add(heading);
    }

    private void AddBullet(string text, int indentLevel = 0)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*"),
            Margin = new Thickness(indentLevel * 16, 0, 0, 0)
        };

        var dot = new Border
        {
            Width = 5,
            Height = 5,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(2.5),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 0, 0)
        };
        dot.Classes.Add("strata-md-bullet-dot");

        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        textBlock.Classes.Add("strata-md-bullet-text");

        Grid.SetColumn(dot, 0);
        Grid.SetColumn(textBlock, 2);
        row.Children.Add(dot);
        row.Children.Add(textBlock);

        _contentHost.Children.Add(row);
    }

    private SelectableTextBlock CreateRichText(string text, double fontSize, double lineHeight, TextWrapping wrapping)
    {
        var textBlock = new SelectableTextBlock
        {
            FontSize = fontSize,
            LineHeight = lineHeight,
            TextWrapping = wrapping
        };

        AppendFormattedInlines(textBlock, text);

        if (textBlock.Inlines != null)
        {
            foreach (var inline in textBlock.Inlines)
            {
                if (inline is Run run && _linkRuns.ContainsKey(run))
                {
                    textBlock.Tapped += OnLinkTapped;
                    textBlock.PointerMoved += OnTextBlockPointerMoved;
                    break;
                }
            }
        }

        return textBlock;
    }

    private static readonly Regex InlineRegex = new(
        @"(?<code>`[^`]+`)|(?<bolditalic>\*\*\*(?<bi_text>.+?)\*\*\*)|(?<bold>\*\*(?<b_text>.+?)\*\*)|(?<italic>\*(?<i_text>.+?)\*)|(?<link>\[(?<l_text>[^\]]+)\]\((?<l_url>[^)]+)\))",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private void AppendFormattedInlines(SelectableTextBlock target, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var matches = InlineRegex.Matches(text);
        if (matches.Count == 0)
        {
            target.Text = text;
            return;
        }

        var lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
                target.Inlines?.Add(new Run(text[lastIndex..match.Index]));

            if (match.Groups["code"].Success)
            {
                var codeText = match.Value[1..^1]; // strip backticks
                target.Inlines?.Add(new Run(" " + codeText + " ")
                {
                    FontFamily = ResolveMonoFont(),
                    FontSize = target.FontSize > 1 ? target.FontSize - 1 : target.FontSize,
                    Background = _inlineCodeBrush ??= ResolveInlineCodeBrush()
                });
            }
            else if (match.Groups["bolditalic"].Success)
            {
                target.Inlines?.Add(new Run(match.Groups["bi_text"].Value)
                {
                    FontWeight = FontWeight.Bold,
                    FontStyle = FontStyle.Italic
                });
            }
            else if (match.Groups["bold"].Success)
            {
                target.Inlines?.Add(new Run(match.Groups["b_text"].Value)
                {
                    FontWeight = FontWeight.Bold
                });
            }
            else if (match.Groups["italic"].Success)
            {
                target.Inlines?.Add(new Run(match.Groups["i_text"].Value)
                {
                    FontStyle = FontStyle.Italic
                });
            }
            else if (match.Groups["link"].Success)
            {
                var linkLabel = match.Groups["l_text"].Value;
                var linkTarget = match.Groups["l_url"].Value.Trim();

                var linkRun = new Run(linkLabel)
                {
                    Foreground = _linkBrush ??= ResolveLinkBrush(),
                    TextDecorations = TextDecorations.Underline,
                };
                _linkRuns[linkRun] = linkTarget;
                target.Inlines?.Add(linkRun);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            target.Inlines?.Add(new Run(text[lastIndex..]));
    }

    private static void OpenLink(string linkTarget)
    {
        if (string.IsNullOrWhiteSpace(linkTarget))
            return;

        try
        {
            if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps || absoluteUri.Scheme == Uri.UriSchemeFile))
            {
                Process.Start(new ProcessStartInfo(absoluteUri.ToString()) { UseShellExecute = true });
                return;
            }

            var resolvedPath = ResolveLocalPath(linkTarget);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                Process.Start(new ProcessStartInfo(resolvedPath) { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }

    private static string BuildLinkTooltip(string linkTarget)
    {
        if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var absoluteUri))
            return $"Open {absoluteUri}";

        var resolvedPath = ResolveLocalPath(linkTarget);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            return $"Open file: {resolvedPath}";

        return $"Reference: {linkTarget}";
    }

    private static string? ResolveLocalPath(string linkTarget)
    {
        if (Path.IsPathRooted(linkTarget))
            return File.Exists(linkTarget) ? linkTarget : null;

        var current = Directory.GetCurrentDirectory();
        var candidate = Path.GetFullPath(Path.Combine(current, linkTarget));
        if (File.Exists(candidate))
            return candidate;

        var cursor = new DirectoryInfo(current);
        for (var i = 0; i < 6 && cursor is not null; i++)
        {
            candidate = Path.Combine(cursor.FullName, linkTarget);
            if (File.Exists(candidate))
                return candidate;

            cursor = cursor.Parent;
        }

        return null;
    }


    private void AddCodeBlock(string code, string language)
    {
        var normalizedCode = code.TrimEnd('\r', '\n');
        var displayCode = string.IsNullOrWhiteSpace(normalizedCode) ? " " : normalizedCode;
        var lineCount = displayCode.Split('\n').Length;

        var shell = new Border();
        shell.Classes.Add("strata-md-code-block");

        var langText = language?.Trim().ToLowerInvariant() ?? string.Empty;

        // Header row with language label + copy button
        var headerRow = new DockPanel
        {
            Margin = new Thickness(10, 6, 6, 0)
        };

        if (!string.IsNullOrWhiteSpace(langText))
        {
            var label = new TextBlock
            {
                Text = langText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.Classes.Add("strata-md-code-lang");
            headerRow.Children.Add(label);
        }

        var copyBtn = new Button
        {
            Content = new TextBlock
            {
                Text = "Copy",
                FontSize = 11,
                Foreground = Brushes.Gray
            },
            Padding = new Thickness(8, 2),
            MinHeight = 0,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        copyBtn.Classes.Add("subtle");
        DockPanel.SetDock(copyBtn, Dock.Right);
        headerRow.Children.Insert(0, copyBtn);

        var capturedCode = displayCode;
        copyBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(copyBtn);
            if (topLevel?.Clipboard is not null)
                await topLevel.Clipboard.SetTextAsync(capturedCode);

            if (copyBtn.Content is TextBlock tb)
            {
                tb.Text = "Copied!";
                await Task.Delay(1200);
                tb.Text = "Copy";
            }
        };

        // Syntax-highlighted editor
        var editor = new TextEditor
        {
            Text = displayCode,
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontSize = 12,
            FontFamily = ResolveMonoFont(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 4, 10, 8),
            MinHeight = Math.Min(lineCount * 18 + 12, 400),
            MaxHeight = 400
        };
        editor.Classes.Add("strata-md-code-editor");

        ApplyTextMateHighlighting(editor, langText);

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(headerRow);
        stack.Children.Add(editor);

        shell.Child = stack;
        _contentHost.Children.Add(shell);
    }

    private void AddChart(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddChartPlaceholder();
            return;
        }

        // Reuse cached chart if JSON hasn't changed (preserves animation state during streaming)
        if (_chartCache.TryGetValue(trimmed, out var cached))
        {
            _chartKeysUsed.Add(trimmed);
            _contentHost.Children.Add(cached);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var typeStr = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "bar";
            var chartType = typeStr?.ToLowerInvariant() switch
            {
                "line" => StrataChartType.Line,
                "bar" => StrataChartType.Bar,
                "donut" => StrataChartType.Donut,
                "pie" => StrataChartType.Pie,
                _ => StrataChartType.Bar
            };

            var labels = new List<string>();
            if (root.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
                foreach (var l in labelsProp.EnumerateArray())
                    labels.Add(l.GetString() ?? "");

            var seriesList = new List<StrataChartSeries>();
            if (root.TryGetProperty("series", out var seriesProp) && seriesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in seriesProp.EnumerateArray())
                {
                    var name = s.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    var values = new List<double>();
                    if (s.TryGetProperty("values", out var vp) && vp.ValueKind == JsonValueKind.Array)
                        foreach (var v in vp.EnumerateArray())
                            values.Add(v.TryGetDouble(out var d) ? d : 0);
                    seriesList.Add(new StrataChartSeries { Name = name, Values = values });
                }
            }

            var showLegend = !root.TryGetProperty("showLegend", out var slp) || slp.ValueKind != JsonValueKind.False;
            var showGrid = !root.TryGetProperty("showGrid", out var sgp) || sgp.ValueKind != JsonValueKind.False;
            var height = root.TryGetProperty("height", out var hp) && hp.TryGetDouble(out var hv) ? hv : 220;

            var chart = new StrataChart
            {
                ChartType = chartType,
                Labels = labels,
                Series = seriesList,
                ShowLegend = showLegend,
                ShowGrid = showGrid,
                ChartHeight = Math.Clamp(height, 120, 500),
            };

            if (root.TryGetProperty("donutCenterValue", out var dcv))
                chart.DonutCenterValue = dcv.GetString();
            if (root.TryGetProperty("donutCenterLabel", out var dcl))
                chart.DonutCenterLabel = dcl.GetString();

            _chartCache[trimmed] = chart;
            _chartKeysUsed.Add(trimmed);
            _contentHost.Children.Add(chart);
        }
        catch
        {
            // Incomplete/malformed JSON during streaming — show placeholder
            AddChartPlaceholder();
        }
    }

    private void AddMermaidChart(string mermaidText)
    {
        var trimmed = mermaidText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddChartPlaceholder();
            return;
        }

        var firstLine = trimmed.Split('\n')[0].TrimStart().ToLowerInvariant();

        // Diagram types → StrataMermaid
        if (firstLine.StartsWith("graph") || firstLine.StartsWith("flowchart") ||
            firstLine.StartsWith("sequencediagram") || firstLine.StartsWith("statediagram") ||
            firstLine.StartsWith("erdiagram") || firstLine.StartsWith("classdiagram") ||
            firstLine.StartsWith("timeline") || firstLine.StartsWith("quadrantchart") ||
            firstLine.StartsWith("quadrant-chart"))
        {
            AddMermaidDiagram(trimmed);
            return;
        }

        // Chart types → StrataChart
        var cacheKey = "mermaid:" + trimmed;
        if (_chartCache.TryGetValue(cacheKey, out var cached))
        {
            _chartKeysUsed.Add(cacheKey);
            _contentHost.Children.Add(cached);
            return;
        }

        try
        {
            StrataChart? chart = null;

            if (trimmed.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
                chart = ParseMermaidPie(trimmed);
            else if (trimmed.StartsWith("xychart-beta", StringComparison.OrdinalIgnoreCase)
                  || trimmed.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
                chart = ParseMermaidXyChart(trimmed);

            if (chart is not null)
            {
                _chartCache[cacheKey] = chart;
                _chartKeysUsed.Add(cacheKey);
                _contentHost.Children.Add(chart);
            }
            else
            {
                AddCodeBlock(trimmed, "mermaid");
            }
        }
        catch
        {
            AddChartPlaceholder();
        }
    }

    private void AddMermaidDiagram(string mermaidText)
    {
        var cacheKey = "mermaid-diag:" + mermaidText;
        if (_diagramCache.TryGetValue(cacheKey, out var cached))
        {
            _diagramKeysUsed.Add(cacheKey);
            _contentHost.Children.Add(cached);
            return;
        }

        var diagram = new StrataMermaid { Source = mermaidText };
        _diagramCache[cacheKey] = diagram;
        _diagramKeysUsed.Add(cacheKey);
        _contentHost.Children.Add(diagram);
    }

    private static readonly Regex MermaidPieEntryRegex = new(
        @"^\s*""(?<label>[^""]+)""\s*:\s*(?<value>[\d.]+)",
        RegexOptions.Compiled);

    private StrataChart? ParseMermaidPie(string text)
    {
        var lines = text.Split('\n');
        var labels = new List<string>();
        var values = new List<double>();
        string? title = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                title = line[6..].Trim().Trim('"');
                continue;
            }

            var match = MermaidPieEntryRegex.Match(line);
            if (match.Success)
            {
                labels.Add(match.Groups["label"].Value);
                if (double.TryParse(match.Groups["value"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var val))
                    values.Add(val);
                else
                    values.Add(0);
            }
        }

        if (labels.Count == 0 || values.Count == 0)
            return null;

        var total = values.Sum();
        var chart = new StrataChart
        {
            ChartType = StrataChartType.Pie,
            Labels = labels,
            Series = new List<StrataChartSeries>
            {
                new() { Name = title ?? "", Values = values }
            },
            ShowLegend = true,
            ShowGrid = false,
            ChartHeight = 220,
        };

        return chart;
    }

    private static readonly Regex MermaidBracketListRegex = new(
        @"\[(?<items>[^\]]*)\]",
        RegexOptions.Compiled);

    private static readonly Regex MermaidQuotedListRegex = new(
        @"""(?<item>[^""]*)""|(?<bare>[\w.]+)",
        RegexOptions.Compiled);

    private StrataChart? ParseMermaidXyChart(string text)
    {
        var lines = text.Split('\n');
        string? title = null;
        var xLabels = new List<string>();
        var barSeriesValues = new List<List<double>>();
        var lineSeriesValues = new List<List<double>>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[5..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(rest))
                    title = rest;
                continue;
            }

            if (line.StartsWith("x-axis", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[6..].Trim();
                xLabels = ParseMermaidBracketValues(rest);
                continue;
            }

            if (line.StartsWith("y-axis", StringComparison.OrdinalIgnoreCase))
                continue; // Informational only — StrataChart auto-scales

            if (line.StartsWith("bar", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[3..].Trim();
                var nums = ParseMermaidNumericList(rest);
                if (nums.Count > 0)
                    barSeriesValues.Add(nums);
                continue;
            }

            if (line.StartsWith("line", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[4..].Trim();
                var nums = ParseMermaidNumericList(rest);
                if (nums.Count > 0)
                    lineSeriesValues.Add(nums);
                continue;
            }
        }

        var hasBar = barSeriesValues.Count > 0;
        var hasLine = lineSeriesValues.Count > 0;
        if (!hasBar && !hasLine) return null;

        // If no explicit x-axis labels, generate numeric indices
        var maxCount = Math.Max(
            barSeriesValues.DefaultIfEmpty(new()).Max(s => s.Count),
            lineSeriesValues.DefaultIfEmpty(new()).Max(s => s.Count));
        if (xLabels.Count == 0)
            for (int i = 1; i <= maxCount; i++)
                xLabels.Add(i.ToString());

        // Determine chart type: prefer Bar when bars exist, Line when only lines
        var chartType = hasBar ? StrataChartType.Bar : StrataChartType.Line;

        var seriesList = new List<StrataChartSeries>();
        for (int i = 0; i < barSeriesValues.Count; i++)
            seriesList.Add(new StrataChartSeries
            {
                Name = barSeriesValues.Count > 1 ? $"Bar {i + 1}" : title ?? "Bar",
                Values = barSeriesValues[i]
            });
        for (int i = 0; i < lineSeriesValues.Count; i++)
            seriesList.Add(new StrataChartSeries
            {
                Name = lineSeriesValues.Count > 1 ? $"Line {i + 1}" : title ?? "Line",
                Values = lineSeriesValues[i]
            });

        // When mixed bar+line, use Line type so both are visible as overlapping series
        if (hasBar && hasLine)
            chartType = StrataChartType.Line;

        return new StrataChart
        {
            ChartType = chartType,
            Labels = xLabels,
            Series = seriesList,
            ShowLegend = seriesList.Count > 1,
            ShowGrid = true,
            ChartHeight = 220,
        };
    }

    private static List<string> ParseMermaidBracketValues(string text)
    {
        var results = new List<string>();
        var bracketMatch = MermaidBracketListRegex.Match(text);
        if (bracketMatch.Success)
        {
            var inner = bracketMatch.Groups["items"].Value;
            foreach (var part in inner.Split(','))
            {
                var val = part.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(val))
                    results.Add(val);
            }
        }
        else
        {
            // Fallback: try to parse label after possible quoted title text
            var quoteIdx = text.IndexOf('"');
            if (quoteIdx >= 0)
            {
                // e.g.: "Month" Jan, Feb, Mar
                // Find closing quote, then parse remaining comma-separated values
                var closeQuote = text.IndexOf('"', quoteIdx + 1);
                if (closeQuote >= 0)
                {
                    var rest = text[(closeQuote + 1)..].Trim();
                    foreach (var part in rest.Split(','))
                    {
                        var val = part.Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(val))
                            results.Add(val);
                    }
                }
            }
        }
        return results;
    }

    private static List<double> ParseMermaidNumericList(string text)
    {
        var results = new List<double>();
        var bracketMatch = MermaidBracketListRegex.Match(text);
        if (bracketMatch.Success)
        {
            var inner = bracketMatch.Groups["items"].Value;
            foreach (var part in inner.Split(','))
            {
                var trimmed = part.Trim();
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                    results.Add(val);
            }
        }
        return results;
    }

    private void AddChartPlaceholder()
    {
        // Reuse the cached placeholder so dot animations survive rebuilds
        if (_chartPlaceholder is null)
        {
            _chartPlaceholder = new Border
            {
                Height = 100,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _chartPlaceholder.Classes.Add("strata-md-code-block");

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
            };

            var icon = new TextBlock
            {
                Text = "\U0001F4CA",
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.35,
            };

            var dotsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
            };

            for (int i = 0; i < 3; i++)
            {
                var dot = new Border
                {
                    Width = 5,
                    Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Opacity = 0.3,
                };
                dot.Classes.Add("strata-md-bullet-dot");
                dotsRow.Children.Add(dot);
            }

            stack.Children.Add(icon);
            stack.Children.Add(dotsRow);
            _chartPlaceholder.Child = stack;

            // Start dot animations once the placeholder is in the visual tree
            _chartPlaceholder.AttachedToVisualTree += (_, _) =>
            {
                var dots = dotsRow.Children.OfType<Border>().ToList();
                for (int i = 0; i < dots.Count; i++)
                    StartDotPulse(dots[i], i * 200);
            };
        }

        // Remove from previous parent if still attached (Rebuild cleared children)
        if (_chartPlaceholder.Parent is Panel oldParent)
            oldParent.Children.Remove(_chartPlaceholder);

        _contentHost.Children.Add(_chartPlaceholder);
    }

    private static void StartDotPulse(Border dot, int delayMs)
    {
        // Guard against double-starting
        if (dot.Tag is System.Threading.CancellationTokenSource existingCts)
        {
            if (!existingCts.IsCancellationRequested) return;
        }

        var cts = new System.Threading.CancellationTokenSource();
        dot.Tag = cts;

        async void Animate()
        {
            try
            {
                if (delayMs > 0)
                    await Task.Delay(delayMs, cts.Token);

                while (!cts.Token.IsCancellationRequested)
                {
                    dot.Opacity = 0.7;
                    await Task.Delay(400, cts.Token);
                    dot.Opacity = 0.2;
                    await Task.Delay(400, cts.Token);
                }
            }
            catch (TaskCanceledException) { }
        }

        Animate();
    }

    private void AddBlockPlaceholder(string emoji)
    {
        if (_blockPlaceholder is null)
        {
            _blockPlaceholder = new Border
            {
                Height = 60,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _blockPlaceholder.Classes.Add("strata-md-code-block");

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Orientation = Orientation.Horizontal,
            };

            var icon = new TextBlock
            {
                Text = emoji,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.35,
            };

            var dotsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
            };

            for (int i = 0; i < 3; i++)
            {
                var dot = new Border
                {
                    Width = 5,
                    Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Opacity = 0.3,
                };
                dot.Classes.Add("strata-md-bullet-dot");
                dotsRow.Children.Add(dot);
            }

            stack.Children.Add(icon);
            stack.Children.Add(dotsRow);
            _blockPlaceholder.Child = stack;

            _blockPlaceholder.AttachedToVisualTree += (_, _) =>
            {
                var dots = dotsRow.Children.OfType<Border>().ToList();
                for (int i = 0; i < dots.Count; i++)
                    StartDotPulse(dots[i], i * 200);
            };
        }

        if (_blockPlaceholder.Parent is Panel oldParent)
            oldParent.Children.Remove(_blockPlaceholder);

        _contentHost.Children.Add(_blockPlaceholder);
    }

    private void AddConfidence(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddBlockPlaceholder("\U0001F3AF"); // 🎯
            return;
        }

        if (_confidenceCache.TryGetValue(trimmed, out var cached))
        {
            _confidenceKeysUsed.Add(trimmed);
            _contentHost.Children.Add(cached);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var label = root.TryGetProperty("label", out var lp) ? lp.GetString() ?? "Confidence" : "Confidence";
            var value = root.TryGetProperty("value", out var vp) && vp.TryGetDouble(out var v) ? v : 72;
            var explanation = root.TryGetProperty("explanation", out var ep) ? ep.GetString() : null;

            var ctrl = new StrataConfidence
            {
                Label = label,
                Confidence = Math.Clamp(value, 0, 100),
                Margin = new Thickness(0, 4, 0, 4),
            };

            if (!string.IsNullOrWhiteSpace(explanation))
                ctrl.Explanation = new StrataMarkdown { Markdown = explanation, IsInline = true };

            _confidenceCache[trimmed] = ctrl;
            _confidenceKeysUsed.Add(trimmed);
            _contentHost.Children.Add(ctrl);
        }
        catch
        {
            AddBlockPlaceholder("\U0001F3AF");
        }
    }

    private void AddComparison(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddBlockPlaceholder("\u2696\uFE0F"); // ⚖️
            return;
        }

        if (_comparisonCache.TryGetValue(trimmed, out var cached))
        {
            _comparisonKeysUsed.Add(trimmed);
            _contentHost.Children.Add(cached);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var titleA = "Option A";
            var contentA = "";
            var titleB = "Option B";
            var contentB = "";

            if (root.TryGetProperty("optionA", out var aProp))
            {
                if (aProp.TryGetProperty("title", out var at)) titleA = at.GetString() ?? titleA;
                if (aProp.TryGetProperty("content", out var ac)) contentA = ac.GetString() ?? "";
            }

            if (root.TryGetProperty("optionB", out var bProp))
            {
                if (bProp.TryGetProperty("title", out var bt)) titleB = bt.GetString() ?? titleB;
                if (bProp.TryGetProperty("content", out var bc)) contentB = bc.GetString() ?? "";
            }

            var ctrl = new StrataFork
            {
                OptionATitle = titleA,
                OptionBTitle = titleB,
                OptionAContent = new StrataMarkdown { Markdown = contentA, IsInline = true },
                OptionBContent = new StrataMarkdown { Markdown = contentB, IsInline = true },
                Margin = new Thickness(0, 4, 0, 4),
            };

            _comparisonCache[trimmed] = ctrl;
            _comparisonKeysUsed.Add(trimmed);
            _contentHost.Children.Add(ctrl);
        }
        catch
        {
            AddBlockPlaceholder("\u2696\uFE0F");
        }
    }

    private void AddCard(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddBlockPlaceholder("\U0001F4CB"); // 📋
            return;
        }

        if (_cardCache.TryGetValue(trimmed, out var cached))
        {
            _cardKeysUsed.Add(trimmed);
            _contentHost.Children.Add(cached);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var header = root.TryGetProperty("header", out var hp) ? hp.GetString() ?? "" : "";
            var summary = root.TryGetProperty("summary", out var sp) ? sp.GetString() ?? "" : "";
            var detail = root.TryGetProperty("detail", out var dp) ? dp.GetString() ?? "" : "";

            var ctrl = new StrataCard
            {
                Margin = new Thickness(0, 4, 0, 4),
            };

            if (!string.IsNullOrWhiteSpace(header))
            {
                ctrl.Header = new TextBlock
                {
                    Text = header,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = _bodyFontSize * 1.08,
                    TextWrapping = TextWrapping.Wrap,
                };
            }

            if (!string.IsNullOrWhiteSpace(summary))
                ctrl.Summary = new StrataMarkdown { Markdown = summary, IsInline = true };

            if (!string.IsNullOrWhiteSpace(detail))
                ctrl.Detail = new StrataMarkdown { Markdown = detail, IsInline = true };

            _cardCache[trimmed] = ctrl;
            _cardKeysUsed.Add(trimmed);
            _contentHost.Children.Add(ctrl);
        }
        catch
        {
            AddBlockPlaceholder("\U0001F4CB");
        }
    }

    private void AddSources(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddBlockPlaceholder("\U0001F4CE"); // 📎
            return;
        }

        if (_sourcesCache.TryGetValue(trimmed, out var cached))
        {
            _sourcesKeysUsed.Add(trimmed);
            _contentHost.Children.Add(cached);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sources", out var sourcesProp) || sourcesProp.ValueKind != JsonValueKind.Array)
            {
                AddBlockPlaceholder("\U0001F4CE");
                return;
            }

            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 4),
            };

            var index = 1;
            foreach (var src in sourcesProp.EnumerateArray())
            {
                var title = src.TryGetProperty("title", out var tp) ? tp.GetString() ?? "Source" : "Source";
                var snippet = src.TryGetProperty("snippet", out var snp) ? snp.GetString() : null;
                var origin = src.TryGetProperty("origin", out var op) ? op.GetString() : null;
                var relevance = src.TryGetProperty("relevance", out var rp) && rp.TryGetDouble(out var rv) ? rv : 0;

                var trace = new StrataTrace
                {
                    Index = index++,
                    Title = title,
                    Snippet = snippet,
                    Origin = origin,
                    Relevance = relevance,
                    Margin = new Thickness(0, 0, 4, 4),
                };

                panel.Children.Add(trace);
            }

            _sourcesCache[trimmed] = panel;
            _sourcesKeysUsed.Add(trimmed);
            _contentHost.Children.Add(panel);
        }
        catch
        {
            AddBlockPlaceholder("\U0001F4CE");
        }
    }

    private static void ApplyTextMateHighlighting(TextEditor editor, string language)
    {
        try
        {
            var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            var themeName = isDark ? ThemeName.DarkPlus : ThemeName.LightPlus;
            var registryOptions = new RegistryOptions(themeName);

            var installation = editor.InstallTextMate(registryOptions);

            if (!string.IsNullOrWhiteSpace(language))
            {
                var lang = registryOptions.GetLanguageByExtension("." + language)
                        ?? registryOptions.GetLanguageByExtension(language == "csharp" ? ".cs" : "." + language);

                if (lang is not null)
                {
                    installation.SetGrammar(registryOptions.GetScopeByLanguageId(lang.Id));
                }
            }
        }
        catch
        {
            // Graceful fallback: no highlighting
        }
    }

    private FontFamily ResolveMonoFont()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Font.FamilyMono", Application.Current.ActualThemeVariant, out var font) &&
            font is FontFamily mono)
        {
            return mono;
        }

        return FontFamily.Default;
    }

    private string? GetLinkAtCharIndex(SelectableTextBlock tb, int charIndex)
    {
        if (tb.Inlines == null) return null;

        var pos = 0;
        foreach (var inline in tb.Inlines)
        {
            if (inline is Run run)
            {
                var len = run.Text?.Length ?? 0;
                if (charIndex >= pos && charIndex < pos + len && _linkRuns.TryGetValue(run, out var url))
                    return url;
                pos += len;
            }
        }
        return null;
    }

    private void OnLinkTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not SelectableTextBlock tb) return;
        var url = GetLinkAtCharIndex(tb, tb.SelectionStart);
        if (url != null) OpenLink(url);
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not SelectableTextBlock tb) return;

        var point = e.GetPosition(tb);
        var hit = tb.TextLayout.HitTestPoint(point);
        var isLink = hit.IsInside && GetLinkAtCharIndex(tb, hit.CharacterHit.FirstCharacterIndex) != null;
        tb.Cursor = isLink ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    private static IBrush ResolveLinkBrush()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Brush.AccentDefault", Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }

        return Brushes.DodgerBlue;
    }

    private static IBrush ResolveInlineCodeBrush()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Brush.AccentSubtle", Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }

        return Brushes.LightGray;
    }

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('#'))
            return false;

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
            i++;

        if (i is < 1 or > 6)
            return false;

        if (i >= trimmed.Length || trimmed[i] != ' ')
            return false;

        level = Math.Min(i, 3);
        text = trimmed[(i + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryParseBullet(string line, out string text)
    {
        text = string.Empty;
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            text = trimmed[2..].Trim();
            return !string.IsNullOrWhiteSpace(text);
        }

        return false;
    }

    private static bool TryParseNumberedItem(string line, out int number, out string text)
    {
        number = 0;
        text = string.Empty;
        var trimmed = line.TrimStart();

        var dotIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dotIndex is < 1 or > 3) return false;
        if (!int.TryParse(trimmed[..dotIndex], out number))
            return false;

        text = trimmed[(dotIndex + 2)..].Trim();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool IsHorizontalRule(string line)
    {
        var trimmed = line.Trim();
        var withoutSpaces = trimmed.Replace(" ", "");
        if (withoutSpaces.Length < 3) return false;

        var first = withoutSpaces[0];
        if (first is not ('-' or '*' or '_')) return false;

        foreach (var c in withoutSpaces)
            if (c != first) return false;

        return true;
    }

    private static int GetIndentLevel(string line)
    {
        var spaces = 0;
        foreach (var c in line)
        {
            if (c == ' ') spaces++;
            else if (c == '\t') spaces += 4;
            else break;
        }
        return Math.Min(spaces / 2, 3);
    }

    private void AddNumberedItem(int number, string text, int indentLevel = 0)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,6,*"),
            Margin = new Thickness(indentLevel * 16, 0, 0, 0)
        };

        var numBlock = new SelectableTextBlock
        {
            Text = $"{number}.",
            FontSize = _bodyFontSize,
            LineHeight = _bodyFontSize * 1.52,
            VerticalAlignment = VerticalAlignment.Top
        };
        numBlock.Classes.Add("strata-md-number");

        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        textBlock.Classes.Add("strata-md-bullet-text");

        Grid.SetColumn(numBlock, 0);
        Grid.SetColumn(textBlock, 2);
        row.Children.Add(numBlock);
        row.Children.Add(textBlock);

        _contentHost.Children.Add(row);
    }

    private void AddHorizontalRule()
    {
        var rule = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        rule.Classes.Add("strata-md-hr");
        _contentHost.Children.Add(rule);
    }

    private static bool IsTableLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 1 && trimmed[0] == '|' && trimmed.IndexOf('|', 1) >= 0;
    }

    private static bool IsTableSeparator(string line)
    {
        var cells = SplitTableCells(line);
        return cells.Length > 0 && cells.All(c => Regex.IsMatch(c.Trim(), @"^:?-+:?$"));
    }

    private static string[] SplitTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|');
    }

    private void FlushTable(List<string> tableLines)
    {
        if (tableLines.Count == 0) return;

        if (tableLines.Count < 2 || !IsTableSeparator(tableLines[1]))
        {
            foreach (var line in tableLines)
            {
                var para = CreateRichText(line, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
                para.Classes.Add("strata-md-paragraph");
                _contentHost.Children.Add(para);
            }
            tableLines.Clear();
            return;
        }

        var headers = SplitTableCells(tableLines[0]).Select(h => h.Trim()).ToArray();
        var rows = new List<string[]>();

        for (var i = 2; i < tableLines.Count; i++)
        {
            var cells = SplitTableCells(tableLines[i]).Select(c => c.Trim()).ToArray();
            var padded = new string[headers.Length];
            for (var j = 0; j < headers.Length; j++)
                padded[j] = j < cells.Length ? cells[j] : string.Empty;
            rows.Add(padded);
        }

        AddTable(headers, rows);
        tableLines.Clear();
    }

    private void AddTable(string[] headers, List<string[]> rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            MaxHeight = 400,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var colIndex = i;
            dataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = CreateRichText(headers[i], _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.NoWrap),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = new FuncDataTemplate<List<string>>((row, _) =>
                {
                    var cellText = row is not null && colIndex < row.Count ? row[colIndex] : string.Empty;
                    return CreateRichText(cellText, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
                })
            });
        }

        var items = rows.Select(r => r.ToList()).ToList();
        dataGrid.ItemsSource = items;

        // Size to content, capped at MaxHeight
        var estimatedHeight = 40 + (rows.Count * 36) + 4;
        dataGrid.Height = Math.Min(estimatedHeight, 400);

        _contentHost.Children.Add(dataGrid);
    }
}
