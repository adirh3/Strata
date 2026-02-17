using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace StrataTheme.Controls;

/// <summary>
/// A rich agent identity card with an icon, name, description, tool/skill counts,
/// and an expandable detail area with tabbed content (Overview, Skills, Tools).
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataAiAgent AgentName="Code Reviewer"
///                          IconGlyph="◉"
///                          Description="Reviews PRs for quality."
///                          ToolsCount="5" SkillsCount="3"
///                          DetailMarkdown="## Overview ..." /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Header (Border), PART_Detail (Border),
/// PART_Markdown (StrataMarkdown), PART_TabHost (TabControl).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :has-description, :has-detail, :has-tools, :has-skills.</para>
/// </remarks>
public class StrataAiAgent : TemplatedControl
{
    private Border? _header;

    /// <summary>Single-character glyph displayed in the icon badge.</summary>
    public static readonly StyledProperty<string> IconGlyphProperty =
        AvaloniaProperty.Register<StrataAiAgent, string>(nameof(IconGlyph), "◉");

    /// <summary>Display name of the agent.</summary>
    public static readonly StyledProperty<string> AgentNameProperty =
        AvaloniaProperty.Register<StrataAiAgent, string>(nameof(AgentName), "Custom Agent");

    /// <summary>Short description shown below the agent name (max 2 lines).</summary>
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<StrataAiAgent, string>(nameof(Description), string.Empty);

    /// <summary>Number of tools available to this agent. Shown as a badge.</summary>
    public static readonly StyledProperty<int> ToolsCountProperty =
        AvaloniaProperty.Register<StrataAiAgent, int>(nameof(ToolsCount), 0);

    /// <summary>Number of skills available to this agent. Shown as a badge.</summary>
    public static readonly StyledProperty<int> SkillsCountProperty =
        AvaloniaProperty.Register<StrataAiAgent, int>(nameof(SkillsCount), 0);

    /// <summary>Markdown content for the Overview tab in the expanded area.</summary>
    public static readonly StyledProperty<string?> DetailMarkdownProperty =
        AvaloniaProperty.Register<StrataAiAgent, string?>(nameof(DetailMarkdown));

    /// <summary>Content for the Tools tab. Set to any UI element or ItemsControl.</summary>
    public static readonly StyledProperty<object?> ToolsContentProperty =
        AvaloniaProperty.Register<StrataAiAgent, object?>(nameof(ToolsContent));

    /// <summary>Content for the Skills tab. Set to any UI element or ItemsControl.</summary>
    public static readonly StyledProperty<object?> SkillsContentProperty =
        AvaloniaProperty.Register<StrataAiAgent, object?>(nameof(SkillsContent));

    /// <summary>Whether the detail area (tabs) is expanded.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataAiAgent, bool>(nameof(IsExpanded));

    static StrataAiAgent()
    {
        DescriptionProperty.Changed.AddClassHandler<StrataAiAgent>((control, _) => control.UpdateState());
        DetailMarkdownProperty.Changed.AddClassHandler<StrataAiAgent>((control, _) => control.UpdateState());
        ToolsContentProperty.Changed.AddClassHandler<StrataAiAgent>((control, _) => control.UpdateState());
        SkillsContentProperty.Changed.AddClassHandler<StrataAiAgent>((control, _) => control.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataAiAgent>((control, _) => control.UpdateState());
    }

    public string IconGlyph
    {
        get => GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string AgentName
    {
        get => GetValue(AgentNameProperty);
        set => SetValue(AgentNameProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public int ToolsCount
    {
        get => GetValue(ToolsCountProperty);
        set => SetValue(ToolsCountProperty, value);
    }

    public int SkillsCount
    {
        get => GetValue(SkillsCountProperty);
        set => SetValue(SkillsCountProperty, value);
    }

    public string? DetailMarkdown
    {
        get => GetValue(DetailMarkdownProperty);
        set => SetValue(DetailMarkdownProperty, value);
    }

    public object? ToolsContent
    {
        get => GetValue(ToolsContentProperty);
        set => SetValue(ToolsContentProperty, value);
    }

    public object? SkillsContent
    {
        get => GetValue(SkillsContentProperty);
        set => SetValue(SkillsContentProperty, value);
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
        PseudoClasses.Set(":has-tools", ToolsContent is not null);
        PseudoClasses.Set(":has-skills", SkillsContent is not null);
        PseudoClasses.Set(":expanded", IsExpanded);
    }
}
