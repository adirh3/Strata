using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace StrataTheme.Controls;

public class StrataAiSkill : TemplatedControl
{
    private Border? _header;

    public static readonly StyledProperty<string> IconGlyphProperty =
        AvaloniaProperty.Register<StrataAiSkill, string>(nameof(IconGlyph), "✦");

    public static readonly StyledProperty<string> SkillNameProperty =
        AvaloniaProperty.Register<StrataAiSkill, string>(nameof(SkillName), "Skill");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<StrataAiSkill, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<string?> DetailMarkdownProperty =
        AvaloniaProperty.Register<StrataAiSkill, string?>(nameof(DetailMarkdown));

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
        PseudoClasses.Set(":expanded", IsExpanded);
    }
}
