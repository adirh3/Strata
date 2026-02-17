using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using AvaloniaEdit;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using System;
using System.Diagnostics;
using System.IO;
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
    private string _lastThemeVariant = string.Empty;

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
            Spacing = 8,
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

        if (!string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
            return;

        var currentVariant = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light).ToString();
        if (string.Equals(currentVariant, _lastThemeVariant, StringComparison.Ordinal))
            return;

        _lastThemeVariant = currentVariant;
        _inlineCodeBrush = null;
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

    private void Rebuild()
    {
        _contentHost.Children.Clear();

        var source = Markdown;
        if (string.IsNullOrWhiteSpace(source))
            return;

        var normalized = source.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        var paragraphBuffer = new StringBuilder();
        var codeBuffer = new StringBuilder();
        var inCodeBlock = false;
        var codeLanguage = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine ?? string.Empty;

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraphBuffer);

                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLanguage = line.Length > 3 ? line[3..].Trim() : string.Empty;
                    codeBuffer.Clear();
                }
                else
                {
                    AddCodeBlock(codeBuffer.ToString(), codeLanguage);
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

            if (TryParseHeading(line, out var level, out var headingText))
            {
                FlushParagraph(paragraphBuffer);
                AddHeading(level, headingText);
                continue;
            }

            if (TryParseBullet(line, out var bulletText))
            {
                FlushParagraph(paragraphBuffer);
                AddBullet(bulletText);
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
            AddCodeBlock(codeBuffer.ToString(), codeLanguage);

        FlushParagraph(paragraphBuffer);
    }

    private void FlushParagraph(StringBuilder paragraphBuffer)
    {
        var text = paragraphBuffer.ToString().Trim();
        paragraphBuffer.Clear();

        if (string.IsNullOrWhiteSpace(text))
            return;

        var paragraph = CreateRichText(text, 12.5, 19, TextWrapping.Wrap);
        paragraph.Classes.Add("strata-md-paragraph");
        _contentHost.Children.Add(paragraph);
    }

    private void AddHeading(int level, string text)
    {
        var heading = CreateRichText(
            text,
            level switch
            {
                1 => 16,
                2 => 14,
                _ => 13
            },
            20,
            TextWrapping.Wrap);
        heading.FontWeight = FontWeight.SemiBold;
        heading.Margin = new Thickness(0, level == 1 ? 6 : 2, 0, 2);
        heading.Classes.Add("strata-md-heading");
        _contentHost.Children.Add(heading);
    }

    private void AddBullet(string text)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*")
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

        var textBlock = CreateRichText(text, 12.5, 19, TextWrapping.Wrap);
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

                var linkText = new TextBlock
                {
                    Text = linkLabel,
                    FontSize = target.FontSize,
                    VerticalAlignment = VerticalAlignment.Center
                };
                linkText.Classes.Add("strata-md-link-text");

                var linkButton = new Button
                {
                    Content = linkText,
                    Padding = new Thickness(2, 0),
                    Margin = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                linkButton.Classes.Add("strata-md-link");
                ToolTip.SetTip(linkButton, BuildLinkTooltip(linkTarget));
                linkButton.Click += (_, _) => OpenLink(linkTarget);

                target.Inlines?.Add(new InlineUIContainer
                {
                    Child = linkButton,
                    BaselineAlignment = BaselineAlignment.Center
                });
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
}
