using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A small pill that seamlessly expands into a full reasoning trace.
/// Designed for AI "thinking" disclosure. Collapsed shows a short label;
/// click morphs the pill into a card with animated height, corner radius,
/// and opacity — all via XAML transitions. C# only handles toggle + dot pulse.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataThink Label="Reasoning…" IsActive="True"&gt;
///     &lt;controls:StrataThink.Content&gt;
///         &lt;TextBlock TextWrapping="Wrap" Text="Step 1: analyse the query..." /&gt;
///     &lt;/controls:StrataThink.Content&gt;
/// &lt;/controls:StrataThink&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Dot (Border), PART_Pill (Border), PART_HeaderRow (StackPanel).</para>
/// <para><b>Pseudo-classes:</b> :active.</para>
/// </remarks>
public class StrataThink : TemplatedControl
{
    private static readonly TimeSpan ExpandedRevealDelay = TimeSpan.FromMilliseconds(340);

    private Border? _dot;
    private Border? _pill;
    private Border? _header;
    private Border? _contentHost;
    private Control? _headerRow;
    private Transitions? _savedPillTransitions;
    private CancellationTokenSource? _expandedRevealCts;
    private bool _initialWidthTransitionSuppressed;
    private bool _pulseRunning;
    private bool _isUserInteractionExpand;
    private object? _displayedContent;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataThink, string>(nameof(Label), "Thinking\u2026");

    public static readonly StyledProperty<string?> MetaProperty =
        AvaloniaProperty.Register<StrataThink, string?>(nameof(Meta));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataThink, object?>(nameof(Content));

    public static readonly DirectProperty<StrataThink, object?> DisplayedContentProperty =
        AvaloniaProperty.RegisterDirect<StrataThink, object?>(nameof(DisplayedContent), control => control.DisplayedContent);

    /// <summary>
    /// Optional extra content rendered in the header row after the label/meta. Use for colored inline elements.
    /// </summary>
    public static readonly StyledProperty<object?> HeaderExtraProperty =
        AvaloniaProperty.Register<StrataThink, object?>(nameof(HeaderExtra));

    /// <summary>
    /// Optional progress percentage (0-100) shown as a compact progress line under the header.
    /// Set to a negative value to hide.
    /// </summary>
    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<StrataThink, double>(nameof(ProgressValue), -1);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataThink, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StrataThink, bool>(nameof(IsActive));

    static StrataThink()
    {
        IsActiveProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.UpdatePseudoClasses());
        IsExpandedProperty.Changed.AddClassHandler<StrataThink>((t, _) =>
        {
            t.UpdateDisplayedContent();
            t.ApplyWidthForState();
            t.OnIsExpandedChanged();
        });
        ContentProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.UpdateDisplayedContent());
        LabelProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.ApplyCollapsedWidth());
        MetaProperty.Changed.AddClassHandler<StrataThink>((t, _) =>
        {
            t.UpdatePseudoClasses();
            t.ApplyCollapsedWidth();
        });
        ProgressValueProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.UpdatePseudoClasses());
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Meta { get => GetValue(MetaProperty); set => SetValue(MetaProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public object? DisplayedContent
    {
        get => _displayedContent;
        private set => SetAndRaise(DisplayedContentProperty, ref _displayedContent, value);
    }
    public object? HeaderExtra { get => GetValue(HeaderExtraProperty); set => SetValue(HeaderExtraProperty, value); }
    public double ProgressValue { get => GetValue(ProgressValueProperty); set => SetValue(ProgressValueProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_header is not null)
            _header.PointerPressed -= OnHeaderPointerPressed;
        if (_pill is not null)
            _pill.PointerPressed -= OnPillPointerPressed;

        _dot = e.NameScope.Find<Border>("PART_Dot");
        _pill = e.NameScope.Find<Border>("PART_Pill");
        _header = e.NameScope.Find<Border>("PART_Header");
        _contentHost = e.NameScope.Find<Border>("PART_ContentHost");
        _headerRow = e.NameScope.Find<Control>("PART_HeaderRow");

        if (_header is not null)
            _header.PointerPressed += OnHeaderPointerPressed;
        if (_pill is not null)
            _pill.PointerPressed += OnPillPointerPressed;

        if (_pill is not null && !IsExpanded)
        {
            // Seed with a safe compact width so first paint is never full-width.
            _pill.Width = 56;
            SuppressInitialWidthTransition();
        }

        // Apply immediately to avoid one-frame full-width flash on initial render.
        ApplyWidthForState();

        // Run one corrective pass after layout settles.
        Dispatcher.UIThread.Post(() =>
        {
            RestoreWidthTransitionIfNeeded();
            ApplyWidthForState();
        }, DispatcherPriority.Loaded);

        UpdateDisplayedContent();
        UpdatePseudoClasses();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Parent is Control parent)
            parent.SizeChanged += OnParentSizeChanged;

        if (IsActive)
            Dispatcher.UIThread.Post(() => { if (IsActive) StartPulse(); }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_header is not null)
            _header.PointerPressed -= OnHeaderPointerPressed;
        if (_pill is not null)
            _pill.PointerPressed -= OnPillPointerPressed;

        if (Parent is Control parent)
            parent.SizeChanged -= OnParentSizeChanged;

        CancelExpandedReveal();
        StopPulse();
        _pulseRunning = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            _isUserInteractionExpand = !IsExpanded;
            IsExpanded = !IsExpanded;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isUserInteractionExpand = !IsExpanded;
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
    }

    private void OnPillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // When collapsed, clicking anywhere on the pill should expand.
        // When expanded, only the PART_Header area handles toggling — this
        // prevents clicks on child content from collapsing the parent.
        if (!IsExpanded && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isUserInteractionExpand = true;
            IsExpanded = true;
            e.Handled = true;
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":active", IsActive);
        PseudoClasses.Set(":has-meta", !string.IsNullOrWhiteSpace(Meta));
        PseudoClasses.Set(":has-progress", ProgressValue >= 0);
        PseudoClasses.Set(":complete", ProgressValue >= 99.999);

        if (IsActive && !_pulseRunning)
        {
            _pulseRunning = true;
            StartPulse();
        }
        else if (!IsActive && _pulseRunning)
        {
            _pulseRunning = false;
            StopPulse();
        }
    }

    private void UpdateDisplayedContent()
    {
        DisplayedContent = IsExpanded ? Content : null;
    }

    private void OnIsExpandedChanged()
    {
        CancelExpandedReveal();

        if (!IsExpanded)
        {
            _isUserInteractionExpand = false;
            // Clear any local MaxHeight override so the style/template defaults
            // take over and the collapse animation (5000 → 0) plays smoothly.
            _contentHost?.ClearValue(MaxHeightProperty);
            return;
        }

        // Only scroll into view when the user expanded via click/keyboard.
        // Programmatic expansions (e.g. streaming tool calls setting IsExpanded
        // via data binding) must not steal the scroll position.
        if (_isUserInteractionExpand)
        {
            _isUserInteractionExpand = false;
            ScheduleExpandedReveal();
        }

        ScheduleMaxHeightUncap();
    }

    private void OnParentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (IsExpanded)
            ApplyExpandedWidth();
        else
            ApplyCollapsedWidth();
    }

    private void ApplyWidthForState()
    {
        if (IsExpanded)
        {
            ApplyExpandedWidth();
            return;
        }

        ApplyCollapsedWidth();
    }

    private void ApplyCollapsedWidth()
    {
        if (_pill is null || _headerRow is null || IsExpanded)
            return;

        _headerRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var horizontalPadding = _pill.Padding.Left + _pill.Padding.Right;
        var width = Math.Max(56, _headerRow.DesiredSize.Width + horizontalPadding);

        if (Parent is Control parent && parent.Bounds.Width > 1)
        {
            // Keep collapsed chip inside available transcript width.
            width = Math.Min(width, parent.Bounds.Width);
        }

        _pill.Width = width;
    }

    private void ApplyExpandedWidth()
    {
        if (_pill is null)
            return;

        var width = GetAvailableWidth();
        width = Math.Min(width, GetExpandedMaxWidth());

        _pill.Width = width;
    }

    private double GetAvailableWidth()
    {
        var width = Bounds.Width;
        for (var ancestor = Parent as Control; ancestor is not null; ancestor = ancestor.Parent as Control)
        {
            if (ancestor.Bounds.Width > width)
                width = ancestor.Bounds.Width;
        }

        if (width < 1)
            width = 420;

        return width;
    }

    private double GetExpandedMaxWidth()
    {
        var maxWidth = MaxWidth;
        if (!double.IsNaN(maxWidth) && !double.IsInfinity(maxWidth) && maxWidth > 0)
            return maxWidth;

        return 480;
    }

    private void ScheduleExpandedReveal()
    {
        var cancellationTokenSource = new CancellationTokenSource();
        _expandedRevealCts = cancellationTokenSource;
        _ = RunExpandedRevealAsync(cancellationTokenSource);
    }

    private async Task RunExpandedRevealAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            if (cancellationTokenSource.IsCancellationRequested || !IsExpanded)
                return;

            RequestExpandedReveal();

            await Task.Delay(ExpandedRevealDelay, cancellationTokenSource.Token);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            if (cancellationTokenSource.IsCancellationRequested || !IsExpanded)
                return;

            RequestExpandedReveal();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_expandedRevealCts, cancellationTokenSource))
                _expandedRevealCts = null;

            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// After the expand animation completes (~300ms), remove the MaxHeight cap
    /// so content of any size renders without clipping or internal scrollbars.
    /// </summary>
    private async void ScheduleMaxHeightUncap()
    {
        await Task.Delay(360); // slightly longer than the 300ms MaxHeight transition

        if (!IsExpanded || _contentHost is null)
            return;

        _contentHost.MaxHeight = double.PositiveInfinity;
    }

    private void RequestExpandedReveal()
    {
        Control target = _pill is not null
            ? _pill
            : _contentHost is not null
                ? _contentHost
                : this;
        target.BringIntoView();
    }

    private void CancelExpandedReveal()
    {
        var expandedRevealCts = _expandedRevealCts;
        _expandedRevealCts = null;
        if (expandedRevealCts is null)
            return;

        expandedRevealCts.Cancel();
        expandedRevealCts.Dispose();
    }

    private void StartPulse()
    {
        if (_dot is null) return;
        var visual = ElementComposition.GetElementVisual(_dot);
        if (visual is null) return;

        var comp = visual.Compositor;
        var anim = comp.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.3f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(1400);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopPulse()
    {
        if (_dot is null) return;
        var visual = ElementComposition.GetElementVisual(_dot);
        if (visual is null) return;

        var comp = visual.Compositor;
        var reset = comp.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.InsertKeyFrame(1f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }

    private void SuppressInitialWidthTransition()
    {
        if (_pill is null || _pill.Transitions is not Transitions transitions || transitions.Count == 0)
            return;

        var filtered = new Transitions();
        var removedWidthTransition = false;

        foreach (var transition in transitions)
        {
            if (transition is DoubleTransition dt && dt.Property == WidthProperty)
            {
                removedWidthTransition = true;
                continue;
            }

            filtered.Add(transition);
        }

        if (!removedWidthTransition)
            return;

        _savedPillTransitions = transitions;
        _pill.Transitions = filtered;
        _initialWidthTransitionSuppressed = true;
    }

    private void RestoreWidthTransitionIfNeeded()
    {
        if (!_initialWidthTransitionSuppressed || _pill is null)
            return;

        _pill.Transitions = _savedPillTransitions;
        _savedPillTransitions = null;
        _initialWidthTransitionSuppressed = false;
    }
}
