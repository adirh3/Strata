using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// A modal sheet that slides up from the bottom edge, with a grab handle and drag-to-dismiss.
///
/// <para>This is the primitive that makes a UI feel like a phone rather than a shrunken desktop.
/// An anchored <c>Popup</c> or a <c>ContextFlyout</c> assumes a cursor: it is positioned relative to
/// a small target and dismissed by clicking "away". A bottom sheet instead rises into the thumb
/// zone, is as wide as the screen, and is dismissed by flicking it back down — no precise pointing
/// anywhere in the interaction.</para>
///
/// <para>Host it in a <c>Panel</c> that covers the window; it renders its own scrim and is
/// collapsed (and hit-test invisible) whenever <see cref="IsOpen"/> is false.</para>
/// </summary>
[TemplatePart("PART_Scrim", typeof(Border))]
[TemplatePart("PART_Sheet", typeof(Border))]
[TemplatePart("PART_Handle", typeof(Border))]
[PseudoClasses(":open")]
public class StrataBottomSheet : ContentControl
{
    /// <summary>Drag distance past which releasing dismisses instead of springing back.</summary>
    private const double DismissThreshold = 72;
    private const double DismissVelocity = 900;

    private Border? _scrim;
    private Border? _sheet;
    private Border? _handle;
    private Border? _motion;
    private ContentPresenter? _contentPresenter;
    private DispatcherTimer? _closeTimer;
    private int _motionVersion;
    private readonly DoubleTransition _fadeTransition = new() { Property = OpacityProperty };
    private readonly TransformOperationsTransition _slideTransition = new()
    {
        Property = RenderTransformProperty,
        Easing = new CubicEaseOut()
    };
    private readonly TranslateTransform _dragTransform = new();
    private bool _isPresented;

    private double _dragStartY;
    private double _dragOffset;
    private bool _dragging;
    private IPointer? _dragPointer;
    private ulong _lastDragTimestamp;
    private ulong _lastVelocityTimestamp;
    private double _dragVelocity;

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<StrataBottomSheet, bool>(
            nameof(IsOpen),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Optional title shown above the content.</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<StrataBottomSheet, string?>(nameof(Title));

    /// <summary>Set false for sheets that must be dismissed by an explicit choice.</summary>
    public static readonly StyledProperty<bool> IsDismissableProperty =
        AvaloniaProperty.Register<StrataBottomSheet, bool>(nameof(IsDismissable), true);

    /// <summary>Space around the floating sheet, including any host safe-area or keyboard insets.</summary>
    public static readonly StyledProperty<Thickness> SheetMarginProperty =
        AvaloniaProperty.Register<StrataBottomSheet, Thickness>(nameof(SheetMargin));

    /// <summary>Whether the sheet is mounted, including its closing animation.</summary>
    public static readonly DirectProperty<StrataBottomSheet, bool> IsPresentedProperty =
        AvaloniaProperty.RegisterDirect<StrataBottomSheet, bool>(nameof(IsPresented), sheet => sheet.IsPresented);

    /// <summary>Allows a host with native overlays to track the sheet's full visual lifetime.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> PresentationChangedEvent =
        RoutedEvent.Register<StrataBottomSheet, RoutedEventArgs>(nameof(PresentationChanged), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ClosedEvent =
        RoutedEvent.Register<StrataBottomSheet, RoutedEventArgs>(nameof(Closed), RoutingStrategies.Bubble);

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool IsDismissable
    {
        get => GetValue(IsDismissableProperty);
        set => SetValue(IsDismissableProperty, value);
    }

    public Thickness SheetMargin
    {
        get => GetValue(SheetMarginProperty);
        set => SetValue(SheetMarginProperty, value);
    }

    public bool IsPresented => _isPresented;

    public event EventHandler<RoutedEventArgs>? PresentationChanged
    {
        add => AddHandler(PresentationChangedEvent, value);
        remove => RemoveHandler(PresentationChangedEvent, value);
    }

    private void SetPresented(bool value)
    {
        if (SetAndRaise(IsPresentedProperty, ref _isPresented, value))
            RaiseEvent(new RoutedEventArgs(PresentationChangedEvent));
    }

    public event EventHandler<RoutedEventArgs>? Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        EndDrag();
        _closeTimer?.Stop();
        _motionVersion++;

        if (_scrim is not null)
            _scrim.PointerPressed -= OnScrimPressed;

        if (_handle is not null)
        {
            _handle.PointerPressed -= OnHandlePressed;
            _handle.PointerMoved -= OnHandleMoved;
            _handle.PointerReleased -= OnHandleReleased;
            _handle.PointerCaptureLost -= OnHandleCaptureLost;
        }

        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        _sheet = e.NameScope.Find<Border>("PART_Sheet");
        _handle = e.NameScope.Find<Border>("PART_Handle");
        _motion = e.NameScope.Find<Border>("PART_SheetMotion");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        if (_contentPresenter is not null)
            _contentPresenter.IsVisible = false;

        if (_scrim is not null)
            _scrim.PointerPressed += OnScrimPressed;

        if (_handle is not null)
        {
            _handle.PointerPressed += OnHandlePressed;
            _handle.PointerMoved += OnHandleMoved;
            _handle.PointerReleased += OnHandleReleased;
            _handle.PointerCaptureLost += OnHandleCaptureLost;
        }

        UpdateOpenState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty)
            UpdateOpenState();
    }

    private void UpdateOpenState()
    {
        var version = ++_motionVersion;
        _closeTimer?.Stop();
        PseudoClasses.Set(":open", IsOpen);
        if (!IsOpen)
            EndDrag();

        if (_motion is null || _contentPresenter is null)
        {
            SetPresented(IsOpen && this.IsAttachedToVisualTree());
            return;
        }

        if (IsOpen)
        {
            var wasPresented = _contentPresenter.IsVisible;
            _motion.IsVisible = true;
            _contentPresenter.IsVisible = true;
            _contentPresenter.IsHitTestVisible = true;
            SetPresented(true);

            // Measure the newly mounted payload before starting; a fixed 900px entry skips most
            // of a short sheet's animation and a close must retain its content through the exit.
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _motionVersion || !IsOpen || !this.IsAttachedToVisualTree())
                    return;

                if (!wasPresented)
                {
                    _motion.Transitions = null;
                    _motion.RenderTransform = ClosedTransform();
                    _motion.Opacity = 0;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (version == _motionVersion && IsOpen)
                        AnimateMotion(open: true);
                }, DispatcherPriority.Render);
            }, DispatcherPriority.Loaded);
            return;
        }

        _contentPresenter.IsHitTestVisible = false;
        if (!_contentPresenter.IsVisible || !this.IsAttachedToVisualTree())
        {
            _motion.Transitions = null;
            _motion.RenderTransform = ClosedTransform();
            _motion.Opacity = 0;
            _motion.IsVisible = false;
            _contentPresenter.IsVisible = false;
            SetPresented(false);
            return;
        }

        AnimateMotion(open: false);
        _closeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _closeTimer.Tick -= OnCloseAnimationFinished;
        _closeTimer.Tick += OnCloseAnimationFinished;
        _closeTimer.Start();
    }

    private TransformOperations ClosedTransform()
    {
        var distance = Math.Max(32, (_sheet?.Bounds.Height ?? 0) + SheetMargin.Bottom);
        return TranslationY(distance);
    }

    private static TransformOperations TranslationY(double offset) =>
        TransformOperations.Parse(FormattableString.Invariant($"translateY({offset}px)"));

    private void AnimateMotion(bool open)
    {
        if (_motion is null)
            return;

        var duration = TimeSpan.FromMilliseconds(open ? 220 : 160);
        _fadeTransition.Duration = duration;
        _slideTransition.Duration = duration;
        _motion.Transitions ??= new Transitions { _fadeTransition, _slideTransition };
        _motion.RenderTransform = open ? TransformOperations.Identity : ClosedTransform();
        _motion.Opacity = open ? 1 : 0;
    }

    private void OnCloseAnimationFinished(object? sender, EventArgs e)
    {
        _closeTimer?.Stop();
        if (!IsOpen && _contentPresenter is not null)
        {
            _contentPresenter.IsVisible = false;
            if (_motion is not null)
                _motion.IsVisible = false;
            SetPresented(false);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateOpenState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _motionVersion++;
        _closeTimer?.Stop();
        EndDrag();
        if (_contentPresenter is not null)
            _contentPresenter.IsVisible = false;
        if (_motion is not null)
        {
            _motion.Transitions = null;
            _motion.Opacity = 0;
            _motion.IsVisible = false;
        }
        SetPresented(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDismissable)
            return;

        e.Handled = true;
        Close();
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(sender, _handle) ||
            !IsDismissable ||
            !IsOpen ||
            _dragPointer is not null ||
            (_motion ?? _sheet) is not { } surface)
            return;

        var offset = _motion?.RenderTransform?.Value.M32 ?? 0;
        if (_motion is not null)
        {
            _motion.Transitions = null;
            _motion.Opacity = 1;
        }
        _dragTransform.Y = offset;
        surface.RenderTransform = _dragTransform;
        _dragPointer = e.Pointer;
        _dragging = true;
        _dragStartY = e.GetPosition(this).Y - offset;
        _dragOffset = offset;
        _lastDragTimestamp = e.Timestamp;
        _dragVelocity = 0;
        e.Pointer.Capture(_handle);

        if (!ReferenceEquals(e.Pointer.Captured, _handle))
        {
            EndDrag();
            AnimateMotion(open: true);
            return;
        }

        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(sender, _handle) ||
            !_dragging ||
            !IsOpen ||
            !ReferenceEquals(e.Pointer, _dragPointer) ||
            _sheet is null)
            return;

        UpdateDrag(e.GetPosition(this).Y, e.Timestamp);
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(sender, _handle) ||
            !_dragging ||
            !ReferenceEquals(e.Pointer, _dragPointer))
            return;

        UpdateDrag(e.GetPosition(this).Y, e.Timestamp);
        if (_lastVelocityTimestamp == 0
            || e.Timestamp > _lastVelocityTimestamp + 180)
        {
            _dragVelocity = 0;
        }
        var shouldClose = _dragOffset >= DismissThreshold || _dragVelocity >= DismissVelocity;
        EndDrag();

        if (shouldClose)
            Close();
        else
            AnimateMotion(open: true);
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(sender, _handle) ||
            !ReferenceEquals(e.Pointer, _dragPointer))
            return;

        EndDrag();
        if (IsOpen)
            AnimateMotion(open: true);
    }

    private void UpdateDrag(double pointerY, ulong timestamp)
    {
        if (_sheet is null)
            return;

        // Downward only. Dragging up would lift the sheet off the bottom edge and expose a gap.
        var nextOffset = Math.Max(0, pointerY - _dragStartY);
        var elapsed = timestamp > _lastDragTimestamp
            ? (timestamp - _lastDragTimestamp) / 1000.0
            : 0;
        var movement = nextOffset - _dragOffset;
        if (elapsed > 0 && Math.Abs(movement) > 0.5)
        {
            _dragVelocity = movement / elapsed;
            _lastVelocityTimestamp = timestamp;
        }
        _dragOffset = nextOffset;
        _lastDragTimestamp = timestamp;
        _dragTransform.Y = _dragOffset;
    }

    private void EndDrag()
    {
        var pointer = _dragPointer;
        var handle = _handle;

        // Preserve the finger's position when switching back to an animated settle.
        if (_motion is not null && ReferenceEquals(_motion.RenderTransform, _dragTransform))
            _motion.RenderTransform = TranslationY(_dragOffset);
        else if (_motion is null)
            _sheet?.ClearValue(RenderTransformProperty);

        _dragPointer = null;
        _dragging = false;
        _dragStartY = 0;
        _dragOffset = 0;
        _lastDragTimestamp = 0;
        _lastVelocityTimestamp = 0;
        _dragVelocity = 0;

        // Template replacement can transfer capture from the old handle to this control.
        // Never release capture that has already moved to an unrelated control.
        if (pointer is not null &&
            (ReferenceEquals(pointer.Captured, handle) ||
             ReferenceEquals(pointer.Captured, this)))
        {
            pointer.Capture(null);
        }
    }

    public void Close()
    {
        EndDrag();

        if (!IsOpen)
            return;

        SetCurrentValue(IsOpenProperty, false);
        RaiseEvent(new RoutedEventArgs(ClosedEvent));
    }
}
