using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

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
    private const double DismissThreshold = 96;
    private const double DismissVelocity = 2200;

    private Border? _scrim;
    private Border? _sheet;
    private Border? _handle;

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

    public event EventHandler<RoutedEventArgs>? Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        EndDrag();

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
        PseudoClasses.Set(":open", IsOpen);

        // Reset any leftover drag so a reopened sheet never starts half-dismissed.
        if (!IsOpen)
            EndDrag();
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
            _dragPointer is not null)
            return;

        _dragPointer = e.Pointer;
        _dragging = true;
        _dragStartY = e.GetPosition(this).Y;
        _dragOffset = 0;
        _lastDragTimestamp = e.Timestamp;
        _dragVelocity = 0;
        e.Pointer.Capture(_handle);

        if (!ReferenceEquals(e.Pointer.Captured, _handle))
        {
            EndDrag();
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
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(sender, _handle) ||
            !ReferenceEquals(e.Pointer, _dragPointer))
            return;

        EndDrag();
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
        _sheet.RenderTransform = new TranslateTransform(0, _dragOffset);
    }

    private void EndDrag()
    {
        var pointer = _dragPointer;
        var handle = _handle;

        _dragPointer = null;
        _dragging = false;
        _dragStartY = 0;
        _dragOffset = 0;
        _lastDragTimestamp = 0;
        _lastVelocityTimestamp = 0;
        _dragVelocity = 0;
        _sheet?.ClearValue(RenderTransformProperty);

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

        IsOpen = false;
        RaiseEvent(new RoutedEventArgs(ClosedEvent));
    }
}
