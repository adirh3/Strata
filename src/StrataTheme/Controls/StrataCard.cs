using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace StrataTheme.Controls;

/// <summary>
/// Status values for <see cref="StrataCard"/>.
/// </summary>
public enum StrataCardStatus
{
    /// <summary>Neutral / default appearance.</summary>
    None,
    /// <summary>Informational accent.</summary>
    Info,
    /// <summary>Positive / success accent.</summary>
    Success,
    /// <summary>Warning accent.</summary>
    Warning,
    /// <summary>Error / danger accent.</summary>
    Error,
}

/// <summary>
/// A compact, expanding summary card with a status-aware stratum accent line,
/// status dot, and optional status pill. Shows a summary when collapsed and
/// animated detail content when expanded. Follows the Strata visual language
/// used by StrataAiToolCall and StrataTurnSummary.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataCard Header="Deployment Complete"
///                       Subtitle="3 services updated"
///                       Status="Success" StatusText="Healthy"&gt;
///     &lt;controls:StrataCard.Summary&gt;&lt;TextBlock Text="All checks passed" /&gt;&lt;/controls:StrataCard.Summary&gt;
///     &lt;controls:StrataCard.Detail&gt;&lt;TextBlock Text="Service A, B, C details" /&gt;&lt;/controls:StrataCard.Detail&gt;
///     &lt;controls:StrataCard.Footer&gt;&lt;TextBlock Text="2 min ago" /&gt;&lt;/controls:StrataCard.Footer&gt;
/// &lt;/controls:StrataCard&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_FocusRing (Border),
/// PART_Stratum (Border), PART_StatusDot (Border), PART_StatusPill (Border),
/// PART_Detail (Border).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :info, :success, :warning, :error, :has-footer.</para>
/// </remarks>
public class StrataCard : TemplatedControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Header));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<StrataCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> SummaryProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Summary));

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Detail));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Footer));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataCard, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<StrataCardStatus> StatusProperty =
        AvaloniaProperty.Register<StrataCard, StrataCardStatus>(nameof(Status));

    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<StrataCard, string?>(nameof(StatusText));

    static StrataCard()
    {
        IsExpandedProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdatePseudoClasses());
        StatusProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdatePseudoClasses());
        FooterProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdatePseudoClasses());
    }

    /// <summary>Gets or sets the header content (title).</summary>
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    /// <summary>Gets or sets a secondary subtitle below the header.</summary>
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    /// <summary>Gets or sets the collapsed summary content.</summary>
    public object? Summary { get => GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }

    /// <summary>Gets or sets the expanded detail content.</summary>
    public object? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    /// <summary>Gets or sets optional footer content below the detail area.</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>Gets or sets whether the card is expanded to show detail.</summary>
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    /// <summary>Gets or sets the status which drives accent color and status dot.</summary>
    public StrataCardStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>Gets or sets the label shown in the status pill badge.</summary>
    public string? StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncStatusLabel(e);
        UpdatePseudoClasses();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        PseudoClasses.Set(":pressed", true);
        e.Handled = true;
        IsExpanded = !IsExpanded;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        PseudoClasses.Set(":pressed", false);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        PseudoClasses.Set(":pressed", false);
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

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":info", Status == StrataCardStatus.Info);
        PseudoClasses.Set(":success", Status == StrataCardStatus.Success);
        PseudoClasses.Set(":warning", Status == StrataCardStatus.Warning);
        PseudoClasses.Set(":error", Status == StrataCardStatus.Error);
        PseudoClasses.Set(":has-footer", Footer is not null);
    }

    private void SyncStatusLabel(TemplateAppliedEventArgs e)
    {
        var label = e.NameScope.Find<Avalonia.Controls.TextBlock>("PART_StatusLabel");
        if (label is null) return;

        // Use explicit StatusText, or fall back to status name
        label.Text = StatusText ?? (Status != StrataCardStatus.None ? Status.ToString() : null);
    }
}

