using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A lightweight, borderless disclosure that collapses completed AI turn blocks
/// (tool-call groups and reasoning traces) into a single summary line.
/// Collapsed: a thin inline row with status dot, label, and chevron.
/// Expanded: the original blocks slide in beneath a subtle left accent bar.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataTurnSummary Label="Reasoned · 4 actions"&gt;
///     &lt;controls:StrataTurnSummary.Content&gt;
///         &lt;StackPanel&gt;
///             &lt;controls:StrataAiToolCall … /&gt;
///             &lt;controls:StrataAiToolCall … /&gt;
///         &lt;/StackPanel&gt;
///     &lt;/controls:StrataTurnSummary.Content&gt;
/// &lt;/controls:StrataTurnSummary&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Header (Border), PART_Chevron (TextBlock).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :has-failures.</para>
/// </remarks>
public class StrataTurnSummary : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataTurnSummary, string>(nameof(Label), "Finished");

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataTurnSummary, object?>(nameof(Content));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataTurnSummary, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<bool> HasFailuresProperty =
        AvaloniaProperty.Register<StrataTurnSummary, bool>(nameof(HasFailures));

    static StrataTurnSummary()
    {
        IsExpandedProperty.Changed.AddClassHandler<StrataTurnSummary>((c, _) => c.UpdatePseudoClasses());
        HasFailuresProperty.Changed.AddClassHandler<StrataTurnSummary>((c, _) => c.UpdatePseudoClasses());
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool HasFailures { get => GetValue(HasFailuresProperty); set => SetValue(HasFailuresProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var header = e.NameScope.Find<Border>("PART_Header");
        if (header is not null)
        {
            header.PointerPressed += (_, pe) =>
            {
                if (pe.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    IsExpanded = !IsExpanded;
                    pe.Handled = true;
                }
            };
        }

        UpdatePseudoClasses();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-failures", HasFailures);
    }
}
