using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

/// <summary>
/// An expandable card that represents an AI skill. Displays an icon, name, and
/// optional description in collapsed form. Expanding reveals a markdown detail panel.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataAiSkill SkillName="Code Review"
///                          IconGlyph="✦"
///                          Description="Analyses pull requests for defects."
///                          DetailMarkdown="## How it works\n- Reads the diff..." /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Header (Border), PART_Detail (Border), PART_Markdown (StrataMarkdown).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :has-description, :has-detail.</para>
/// </remarks>
public class StrataAiSkill : TemplatedControl
{
    private Border? _header;
    private Border? _root;
    private ContextMenu? _contextMenu;

    /// <summary>Single-character glyph displayed in the icon badge.</summary>
    public static readonly StyledProperty<string> IconGlyphProperty =
        AvaloniaProperty.Register<StrataAiSkill, string>(nameof(IconGlyph), "✦");

    /// <summary>Display name of the skill.</summary>
    public static readonly StyledProperty<string> SkillNameProperty =
        AvaloniaProperty.Register<StrataAiSkill, string>(nameof(SkillName), "Skill");

    /// <summary>Short description shown below the name (max 2 lines).</summary>
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<StrataAiSkill, string>(nameof(Description), string.Empty);

    /// <summary>Markdown content displayed in the expanded detail pane.</summary>
    public static readonly StyledProperty<string?> DetailMarkdownProperty =
        AvaloniaProperty.Register<StrataAiSkill, string?>(nameof(DetailMarkdown));

    /// <summary>Whether the detail pane is expanded.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataAiSkill, bool>(nameof(IsExpanded));

    static StrataAiSkill()
    {
        DescriptionProperty.Changed.AddClassHandler<StrataAiSkill>((control, _) => control.UpdateState());
        DetailMarkdownProperty.Changed.AddClassHandler<StrataAiSkill>((control, _) => control.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataAiSkill>((control, _) => control.UpdateState());
    }

    public string IconGlyph
    {
        get => GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string SkillName
    {
        get => GetValue(SkillNameProperty);
        set => SetValue(SkillNameProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? DetailMarkdown
    {
        get => GetValue(DetailMarkdownProperty);
        set => SetValue(DetailMarkdownProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _header = e.NameScope.Find<Border>("PART_Header");
        _root = e.NameScope.Find<Border>("PART_Root");

        if (_header is not null)
        {
            _header.PointerPressed += (_, pointerEvent) =>
            {
                if (pointerEvent.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    IsExpanded = !IsExpanded;
                    pointerEvent.Handled = true;
                }
            };
        }

        AttachContextMenu();

        UpdateState();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Enter or Key.Space)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsExpanded)
        {
            IsExpanded = false;
            e.Handled = true;
        }
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":has-description", !string.IsNullOrWhiteSpace(Description));
        PseudoClasses.Set(":has-detail", !string.IsNullOrWhiteSpace(DetailMarkdown));
        PseudoClasses.Set(":expanded", IsExpanded);
    }

    private void AttachContextMenu()
    {
        if (_root is null)
            return;

        _contextMenu ??= new ContextMenu();
        _contextMenu.Opening -= OnContextMenuOpening;
        _contextMenu.Opening += OnContextMenuOpening;

        RebuildContextMenuItems();
        _root.ContextMenu = _contextMenu;
        ContextMenu = _contextMenu;
    }

    private void OnContextMenuOpening(object? sender, EventArgs e)
    {
        RebuildContextMenuItems();
    }

    private void RebuildContextMenuItems()
    {
        if (_contextMenu is null)
            return;

        var items = new List<object>();

        var copySummaryItem = new MenuItem { Header = "Copy summary", Icon = CreateMenuIcon("\uE8C8") };
        copySummaryItem.Click += async (_, _) => await CopyToClipboardAsync(GetSummaryText());
        items.Add(copySummaryItem);

        if (!string.IsNullOrWhiteSpace(DetailMarkdown))
        {
            var copyDetailsItem = new MenuItem { Header = "Copy details", Icon = CreateMenuIcon("\uE8A7") };
            copyDetailsItem.Click += async (_, _) => await CopyToClipboardAsync(DetailMarkdown!);
            items.Add(copyDetailsItem);
        }

        if (items.Count > 0)
            items.Add(new Separator());

        var toggleItem = new MenuItem
        {
            Header = IsExpanded ? "Collapse" : "Expand",
            Icon = CreateMenuIcon(IsExpanded ? "\uE70E" : "\uE70D")
        };
        toggleItem.Click += (_, _) => IsExpanded = !IsExpanded;
        items.Add(toggleItem);

        _contextMenu.ItemsSource = items;
    }

    private static TextBlock CreateMenuIcon(string glyph)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Width = 14,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private string GetSummaryText()
    {
        var lines = new List<string> { SkillName };

        if (!string.IsNullOrWhiteSpace(Description))
            lines.Add(Description);

        return string.Join(Environment.NewLine, lines);
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(text);
    }
}
