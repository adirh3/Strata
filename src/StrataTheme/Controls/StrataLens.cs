using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace StrataTheme.Controls;

/// <summary>
/// Collapsible reading surface. Shows a header and summary at all times.
/// Toggling reveals the full detail content below with animated MaxHeight.
/// Chevron and detail animations are driven purely by XAML transitions.
/// </summary>
public class StrataLens : TemplatedControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataLens, object?>(nameof(Header));

    public static readonly StyledProperty<object?> SummaryProperty =
        AvaloniaProperty.Register<StrataLens, object?>(nameof(Summary));

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<StrataLens, object?>(nameof(Detail));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataLens, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<string> ExpandTextProperty =
        AvaloniaProperty.Register<StrataLens, string>(nameof(ExpandText), "Show more");

    public static readonly StyledProperty<string> CollapseTextProperty =
        AvaloniaProperty.Register<StrataLens, string>(nameof(CollapseText), "Show less");

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? Summary { get => GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public object? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public string ExpandText { get => GetValue(ExpandTextProperty); set => SetValue(ExpandTextProperty, value); }
    public string CollapseText { get => GetValue(CollapseTextProperty); set => SetValue(CollapseTextProperty, value); }

    // Compat: old XAML with Report= still compiles
    public static readonly StyledProperty<object?> ReportProperty =
        AvaloniaProperty.Register<StrataLens, object?>(nameof(Report));
    public object? Report { get => GetValue(ReportProperty); set => SetValue(ReportProperty, value); }

    public static readonly StyledProperty<string> SummaryLabelProperty =
        AvaloniaProperty.Register<StrataLens, string>(nameof(SummaryLabel), "Summary");
    public string SummaryLabel { get => GetValue(SummaryLabelProperty); set => SetValue(SummaryLabelProperty, value); }

    public static readonly StyledProperty<string> ReportLabelProperty =
        AvaloniaProperty.Register<StrataLens, string>(nameof(ReportLabel), "Report");
    public string ReportLabel { get => GetValue(ReportLabelProperty); set => SetValue(ReportLabelProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var toggle = e.NameScope.Find<Button>("PART_ToggleButton");
        if (toggle is not null)
            toggle.Click += (_, _) => IsExpanded = !IsExpanded;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            IsExpanded = !IsExpanded;
        }
    }
}
