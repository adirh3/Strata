using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
/// A summary card: a title, an optional status pill and a summary, read on their own. A card with
/// detail ends in a Read more pill that opens the rest under the summary and then reads Show less;
/// clicking the card's title or summary opens and closes it too.
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
/// <para><b>Template parts:</b> PART_Root (Border), PART_FocusRing (Border), PART_Header (Border),
/// PART_StatusPill (Border), PART_Detail (TurnSummaryRevealHost), PART_MoreButton (Button),
/// PART_MoreLabel (RollingLabelPanel).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :has-detail, :has-subtitle, :has-footer,
/// :info, :success, :warning, :error.</para>
/// <para><b>Classes:</b> <c>motion-disabled</c> opens and closes at once.</para>
/// </remarks>
public class StrataCard : TemplatedControl
{
    private static readonly TimeSpan MotionDuration = TimeSpan.FromMilliseconds(300);

    private TapReleaseHandler? _headerTap;
    private TurnSummaryRevealHost? _detailHost;
    private Button? _moreButton;
    private RollingLabelPanel? _moreLabel;
    private TextBlock? _statusLabel;
    private int _motionVersion;

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
        IsExpandedProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.OnExpandedChanged());
        DetailProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.OnDetailChanged());
        StatusProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdateState());
        StatusTextProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdateState());
        SubtitleProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdateState());
        FooterProperty.Changed.AddClassHandler<StrataCard>((c, _) => c.UpdateState());
    }

    /// <summary>Gets or sets the header content (title).</summary>
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    /// <summary>Gets or sets a secondary subtitle below the header.</summary>
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    /// <summary>Gets or sets the summary content, which stays in view whether the card is open or not.</summary>
    public object? Summary { get => GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }

    /// <summary>Gets or sets the detail content, which opening the card shows under the summary.</summary>
    public object? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    /// <summary>Gets or sets optional footer content below the detail.</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>Gets or sets whether the card is open, showing its detail.</summary>
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    /// <summary>Gets or sets the status which colours the status pill.</summary>
    public StrataCardStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>Gets or sets the label shown in the status pill.</summary>
    public string? StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

    private bool HasDetail => Detail is not null;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _headerTap?.Dispose();
        if (_moreButton is not null) _moreButton.Click -= OnMoreClicked;
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Header") is { } header)
            _headerTap = new TapReleaseHandler(header, ToggleByReader);
        _moreButton = e.NameScope.Find<Button>("PART_MoreButton");
        if (_moreButton is not null) _moreButton.Click += OnMoreClicked;
        _moreLabel = e.NameScope.Find<RollingLabelPanel>("PART_MoreLabel");

        _detailHost = e.NameScope.Find<TurnSummaryRevealHost>("PART_Detail");
        _statusLabel = e.NameScope.Find<TextBlock>("PART_StatusLabel");
        UpdateState();
        ApplyExpandedState(animate: false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!HasDetail || e.Key is not (Key.Enter or Key.Space))
            return;

        e.Handled = true;
        SetCurrentValue(IsExpandedProperty, !IsExpanded);
    }

    private void OnMoreClicked(object? sender, RoutedEventArgs e) => ToggleByReader();

    private void ToggleByReader()
    {
        if (HasDetail)
            SetCurrentValue(IsExpandedProperty, !IsExpanded);
    }

    private void OnExpandedChanged()
    {
        UpdateState();
        ApplyExpandedState(animate: true);
    }

    private void OnDetailChanged()
    {
        UpdateState();
        ApplyExpandedState(animate: false);
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-detail", HasDetail);
        PseudoClasses.Set(":has-subtitle", !string.IsNullOrWhiteSpace(Subtitle));
        PseudoClasses.Set(":has-footer", Footer is not null);
        PseudoClasses.Set(":info", Status == StrataCardStatus.Info);
        PseudoClasses.Set(":success", Status == StrataCardStatus.Success);
        PseudoClasses.Set(":warning", Status == StrataCardStatus.Warning);
        PseudoClasses.Set(":error", Status == StrataCardStatus.Error);

        if (_statusLabel is not null)
            _statusLabel.Text = StatusText ?? (Status != StrataCardStatus.None ? Status.ToString() : null);
        if (_moreButton is not null)
            AutomationProperties.SetName(_moreButton, InteractiveLabels.Current[IsExpanded ? "cardShowLess" : "cardReadMore"]);
    }

    // The detail grows from under the summary at its natural size, so it never reflows mid-motion,
    // while the pill's label rolls from Read more to Show less in step with it.
    private void ApplyExpandedState(bool animate)
    {
        var version = ++_motionVersion;
        var open = IsExpanded && HasDetail;
        animate &= !Classes.Contains("motion-disabled") && IsEffectivelyVisible && Bounds.Width > 0;

        if (_moreLabel is { } label)
        {
            label.Transitions = animate
                ? [new DoubleTransition { Property = RollingLabelPanel.ProgressProperty, Duration = MotionDuration, Easing = new SplineEasing(0.2, 0, 0, 1) }]
                : null;
            label.Progress = open ? 1 : 0;
        }

        if (_detailHost is not { } host)
            return;

        host.Transitions = animate
            ? [new DoubleTransition { Property = TurnSummaryRevealHost.RevealProperty, Duration = MotionDuration, Easing = new SplineEasing(0.2, 0, 0, 1) }]
            : null;
        if (open)
            host.IsVisible = true;
        host.Reveal = open ? 1 : 0;

        if (open)
            return;
        if (!animate)
        {
            host.IsVisible = false;
            return;
        }

        DispatcherTimer.RunOnce(() =>
        {
            if (version == _motionVersion && !IsExpanded)
                host.IsVisible = false;
        }, MotionDuration + TimeSpan.FromMilliseconds(40));
    }
}
