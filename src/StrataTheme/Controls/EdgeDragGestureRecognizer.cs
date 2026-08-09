using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// Recognises a horizontal drag that starts within a screen edge gutter, optionally anywhere on the
/// closed surface, or anywhere over an already-open panel. Reports continuous deltas so the target
/// can track the finger one-to-one.
///
/// <para>A gesture recognizer is the only mechanism that works here. Once any recognizer captures a
/// pointer, Avalonia delivers subsequent moves straight to that recognizer and they stop travelling
/// the event route entirely, so a parent listening on <see cref="InputElement.PointerMovedEvent"/> —
/// even at tunnel priority — goes silent the moment the transcript's <c>ScrollGestureRecognizer</c>
/// takes over. That is why a hand-rolled pointer-event drawer swipe does nothing on a real device
/// while appearing to work against synthetic events on a desktop.</para>
///
/// <para>Competing with the scroller is resolved by intent rather than by racing it: the drag is
/// only claimed once it has travelled past the threshold AND is more horizontal than vertical, so a
/// vertical flick through the gutter still scrolls. Claiming is deliberately biased to happen early
/// (the threshold is smaller than <c>ScrollGestureRecognizer.ScrollStartDistance</c>) because the
/// edge gutter is a region the user can only plausibly be dragging from.</para>
/// </summary>
/// <remarks>
/// Raises <see cref="EdgeDragEvent"/> continuously while dragging and <see cref="EdgeDragEndedEvent"/>
/// once on release. Both are attached routed events, so the host subscribes with
/// <c>AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, handler)</c>.
/// </remarks>
public sealed class EdgeDragGestureRecognizer : GestureRecognizer
{
    /// <summary>Raised on every move once the drag is claimed, carrying the delta since the last report.</summary>
    public static readonly RoutedEvent<EdgeDragEventArgs> EdgeDragEvent =
        RoutedEvent.Register<EdgeDragEventArgs>(
            "EdgeDrag", RoutingStrategies.Bubble, typeof(EdgeDragGestureRecognizer));

    /// <summary>Raised once when the finger lifts, carrying the final velocity for a fling decision.</summary>
    public static readonly RoutedEvent<EdgeDragEndedEventArgs> EdgeDragEndedEvent =
        RoutedEvent.Register<EdgeDragEndedEventArgs>(
            "EdgeDragEnded", RoutingStrategies.Bubble, typeof(EdgeDragGestureRecognizer));

    /// <summary>
    /// How far from the left edge a drag may begin, in DIPs, when <see cref="IsOpen"/> is false.
    ///
    /// <para>Android's own back gesture owns roughly the outer 20dp of each edge and never forwards
    /// those touches to the app, so the usable gutter starts past it.</para>
    /// </summary>
    public double EdgeWidth { get; set; } = 72;

    /// <summary>Where the edge gutter starts, in DIPs — inside this the OS keeps the gesture.</summary>
    public double EdgeInset { get; set; } = 18;

    /// <summary>
    /// Allows a closed drawer's opening swipe to begin anywhere on the target. Direction-aware intent
    /// arbitration keeps vertical transcript scrolling in control.
    /// </summary>
    public bool CanOpenFromAnywhere { get; set; }

    /// <summary>Movement required for an opening swipe that begins outside the edge gutter.</summary>
    public double AnywhereThreshold { get; set; } = 6;

    /// <summary>
    /// Movement required before the drag is claimed, in DIPs. Below
    /// <c>ScrollGestureRecognizer.ScrollStartDistance</c> so an intentional horizontal drag in the
    /// gutter wins the race against a scroll.
    /// </summary>
    public double Threshold { get; set; } = 4;

    /// <summary>
    /// Whether the panel is currently open. When open a drag may start anywhere (so the user can
    /// close it by swiping the panel itself); when closed it must start inside the edge gutter.
    /// </summary>
    public bool IsOpen { get; set; }
    public bool IsRightToLeft { get; set; }
    public bool IsEnabled { get; set; } = true;

    private IPointer? _pointer;
    private Point _origin;
    private Point _last;
    private bool _dragging;
    private bool _abandoned;
    private ulong _lastTimestamp;
    private ulong _lastVelocityTimestamp;
    private double _velocity;
    private bool _startedOutsideGutter;
    private ScrollContentPresenter? _horizontalScrollOwner;

    protected override void PointerPressed(PointerPressedEventArgs e)
    {
        if (!IsEnabled || _pointer is not null)
            return;

        _dragging = false;
        _abandoned = false;
        _velocity = 0;
        _startedOutsideGutter = false;
        _horizontalScrollOwner = null;

        if (Target is not Visual target)
            return;

        // Mouse drags would fight text selection and the desktop has a real sidebar anyway.
        if (e.Pointer.Type == PointerType.Mouse)
            return;

        // Touch drags inside an editor belong to caret/selection handling. A navigation gesture may
        // start almost anywhere, but never by stealing the user's attempt to move the insertion point.
        if (IsInsideEditableTextBox(e.Source as Visual, target))
            return;

        var point = e.GetPosition(target);

        if (!IsOpen)
        {
            var withinGutter = IsRightToLeft
                ? point.X <= target.Bounds.Width - EdgeInset
                  && point.X >= target.Bounds.Width - EdgeInset - EdgeWidth
                : point.X >= EdgeInset && point.X <= EdgeInset + EdgeWidth;
            if (!withinGutter && !CanOpenFromAnywhere)
                return;

            _startedOutsideGutter = !withinGutter;
            if (_startedOutsideGutter)
                _horizontalScrollOwner = FindHorizontalScrollOwner(e.Source as Visual, target);
        }

        _pointer = e.Pointer;
        _origin = point;
        _last = point;
        _lastTimestamp = e.Timestamp;
    }

    protected override void PointerMoved(PointerEventArgs e)
    {
        ProcessMovement(e, captureWhenClaimed: true);
    }

    private void ProcessMovement(PointerEventArgs e, bool captureWhenClaimed)
    {
        if (_pointer is null || _abandoned || !ReferenceEquals(e.Pointer, _pointer) || Target is not Visual target)
            return;

        var point = e.GetPosition(target);

        if (!_dragging)
        {
            var dx = point.X - _origin.X;
            var dy = point.Y - _origin.Y;
            var threshold = !IsOpen && _startedOutsideGutter
                ? Math.Max(Threshold, AnywhereThreshold)
                : Threshold;

            // Vertical intent: hand the gesture back for good rather than fighting the scroller
            // for the rest of the stroke.
            if (Math.Abs(dy) > threshold && Math.Abs(dy) >= Math.Abs(dx))
            {
                Abandon();
                return;
            }

            var openingDistance = dx * (IsRightToLeft ? -1 : 1);
            if (!IsOpen
                && _startedOutsideGutter
                && openingDistance > 0
                && CanHorizontalScrollOwnerConsumeOpeningGesture())
            {
                Abandon();
                return;
            }

            if (!IsOpen && openingDistance < -threshold)
            {
                Abandon();
                return;
            }

            if ((!IsOpen && openingDistance <= threshold)
                || (IsOpen && Math.Abs(dx) <= threshold))
                return;

            _dragging = true;

            // Start measuring from the threshold rather than the touch-down point, so the panel
            // does not jump by Threshold on the first frame.
            _last = new Point(_origin.X + Math.Sign(dx) * threshold, point.Y);
            if (captureWhenClaimed)
                Capture(e.Pointer);
        }

        var direction = IsRightToLeft ? -1 : 1;
        var delta = (point.X - _last.X) * direction;
        var elapsed = e.Timestamp > _lastTimestamp ? (e.Timestamp - _lastTimestamp) / 1000.0 : 0;
        if (elapsed > 0 && Math.Abs(delta) > 0.01)
        {
            _velocity = delta / elapsed;
            _lastVelocityTimestamp = e.Timestamp;
        }

        _last = point;
        _lastTimestamp = e.Timestamp;

        if (delta != 0)
            Target?.RaiseEvent(new EdgeDragEventArgs(EdgeDragEvent, delta, point.X));

        e.Handled = true;
    }

    protected override void PointerReleased(PointerReleasedEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer))
            return;

        ProcessMovement(e, captureWhenClaimed: false);
        if (_lastVelocityTimestamp == 0
            || e.Timestamp > _lastVelocityTimestamp + 180)
        {
            _velocity = 0;
        }
        var wasDragging = _dragging;
        End();
        e.Handled = wasDragging;
        _dragging = false;
        _pointer = null;
        _horizontalScrollOwner = null;
    }

    protected override void PointerCaptureLost(IPointer pointer)
    {
        if (!ReferenceEquals(pointer, _pointer))
            return;

        End();
        _dragging = false;
        _pointer = null;
        _horizontalScrollOwner = null;
    }

    private void End()
    {
        if (_dragging)
            Target?.RaiseEvent(new EdgeDragEndedEventArgs(EdgeDragEndedEvent, _velocity));
    }

    public void Cancel()
    {
        _dragging = false;
        _abandoned = true;
        _velocity = 0;
        _lastVelocityTimestamp = 0;
        // Keep tracking the owning pointer until its normal release/capture-loss arrives. Avalonia's
        // gesture-capture release API is internal; retaining this recognizer lets the framework
        // unwind ownership safely instead of removing a still-captured recognizer.
    }

    private void Abandon()
    {
        _abandoned = true;
        _pointer = null;
        _horizontalScrollOwner = null;
    }

    private bool CanHorizontalScrollOwnerConsumeOpeningGesture()
    {
        if (_horizontalScrollOwner is not { } scrollPresenter)
            return false;

        return CanHorizontalScrollConsumeOpeningGesture(
            scrollPresenter.Offset,
            scrollPresenter.Extent,
            scrollPresenter.Viewport,
            scrollPresenter.FlowDirection,
            IsRightToLeft);
    }

    internal static bool CanHorizontalScrollConsumeOpeningGesture(
        Vector offset,
        Size extent,
        Size viewport,
        Avalonia.Media.FlowDirection scrollFlowDirection,
        bool drawerIsRightToLeft)
    {
        const double tolerance = 0.5;
        var maxOffset = Math.Max(0, extent.Width - viewport.Width);

        // ScrollGestureRecognizer reports previous-position minus current-position. The presenter
        // then reverses that delta for RTL content before applying it to Offset.X.
        var physicalOpeningDirection = drawerIsRightToLeft ? -1d : 1d;
        var scrollGestureDelta = -physicalOpeningDirection;
        var offsetDelta = scrollFlowDirection == Avalonia.Media.FlowDirection.RightToLeft
            ? -scrollGestureDelta
            : scrollGestureDelta;

        return offsetDelta < 0
            ? offset.X > tolerance
            : offset.X < maxOffset - tolerance;
    }

    private static ScrollContentPresenter? FindHorizontalScrollOwner(Visual? source, Visual target)
    {
        for (var visual = source;
             visual is not null && !ReferenceEquals(visual, target);
             visual = visual.GetVisualParent())
        {
            if (visual is ScrollContentPresenter scrollPresenter
                && scrollPresenter.CanHorizontallyScroll
                && scrollPresenter.Extent.Width > scrollPresenter.Viewport.Width + 0.5)
            {
                return scrollPresenter;
            }
        }

        return null;
    }

    private static bool IsInsideEditableTextBox(Visual? source, Visual target)
    {
        for (var visual = source;
             visual is not null && !ReferenceEquals(visual, target);
             visual = visual.GetVisualParent())
        {
            if (visual is TextBox { IsReadOnly: false, IsEnabled: true })
                return true;
        }

        return false;
    }
}

/// <summary>Carries one increment of an in-progress edge drag.</summary>
public sealed class EdgeDragEventArgs(RoutedEvent routedEvent, double delta, double position)
    : RoutedEventArgs(routedEvent)
{
    /// <summary>Horizontal movement since the previous report, in DIPs. Positive is rightwards.</summary>
    public double Delta { get; } = delta;

    /// <summary>Current horizontal position of the finger relative to the target, in DIPs.</summary>
    public double Position { get; } = position;
}

/// <summary>Carries the release of an edge drag.</summary>
public sealed class EdgeDragEndedEventArgs(RoutedEvent routedEvent, double velocity)
    : RoutedEventArgs(routedEvent)
{
    /// <summary>Horizontal velocity at release, in DIPs per second. Positive is rightwards.</summary>
    public double Velocity { get; } = velocity;
}
