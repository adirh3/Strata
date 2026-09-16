using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace StrataTheme.Controls;

/// <summary>Completes a primary tap without taking the pointer from a parent scroll gesture.</summary>
internal sealed class TapReleaseHandler : IDisposable
{
    private readonly Control _control;
    private readonly Action _activate;
    private IPointer? _pointer;
    private Visual? _coordinateRoot;
    private Point _start;

    public TapReleaseHandler(Control control, Action activate)
    {
        _control = control;
        _activate = activate;
        control.PointerPressed += OnPressed;
        control.PointerMoved += OnMoved;
        control.PointerReleased += OnReleased;
        control.PointerCaptureLost += OnCaptureLost;
        control.DetachedFromVisualTree += OnDetached;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Pointer.IsPrimary || e.Handled || !e.GetCurrentPoint(_control).Properties.IsLeftButtonPressed)
            return;
        // A parent scroller may own the previous release. A fresh primary press starts a new tap.
        _pointer = e.Pointer;
        _coordinateRoot = TopLevel.GetTopLevel(_control) ?? (Visual)_control;
        _start = e.GetPosition(_coordinateRoot);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _pointer) && HasMoved(e))
            Reset();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer))
            return;
        var activate = !e.Handled && !HasMoved(e)
            && new Rect(_control.Bounds.Size).Contains(e.GetPosition(_control));
        Reset();
        if (activate)
        {
            _activate();
            e.Handled = true;
        }
    }

    private bool HasMoved(PointerEventArgs e)
    {
        var delta = e.GetPosition(_coordinateRoot) - _start;
        return delta.X * delta.X + delta.Y * delta.Y >= 100;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _pointer))
            Reset();
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Reset();
    private void Reset()
    {
        _pointer = null;
        _coordinateRoot = null;
    }

    public void Dispose()
    {
        Reset();
        _control.PointerPressed -= OnPressed;
        _control.PointerMoved -= OnMoved;
        _control.PointerReleased -= OnReleased;
        _control.PointerCaptureLost -= OnCaptureLost;
        _control.DetachedFromVisualTree -= OnDetached;
    }
}
