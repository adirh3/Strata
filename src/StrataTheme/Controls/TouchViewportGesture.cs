using System;
using Avalonia;
using Avalonia.Input;

namespace StrataTheme.Controls;

internal sealed class TouchViewportGesture(Visual viewport, Action<Point, Point, double?> transform)
{
    private IPointer? _first, _second;
    private Point _firstAt, _secondAt;
    private bool _claimed;
    private double _pinchZoomPerDip;

    internal bool HasContact => _first is not null;

    internal bool Press(PointerPressedEventArgs e, double zoom, bool panWithOneFinger)
    {
        if (_first is null)
        {
            _first = e.Pointer;
            _firstAt = e.GetPosition(viewport);
            _claimed = panWithOneFinger;
        }
        else if (_second is null)
        {
            _second = e.Pointer;
            _secondAt = e.GetPosition(viewport);
            _pinchZoomPerDip = zoom / Distance();
            _claimed = true;
        }

        if (_claimed)
            Claim(e);
        return _claimed;
    }

    internal bool Move(PointerEventArgs e)
    {
        var first = e.Pointer == _first;
        if (!first && e.Pointer != _second)
            return false;

        var previous = Center();
        if (first)
            _firstAt = e.GetPosition(viewport);
        else
            _secondAt = e.GetPosition(viewport);

        if (!_claimed)
            return false;

        var current = Center();
        // Use the starting zoom and span so individual finger reports at either zoom limit
        // don't accumulate a zoom change when fingers move together.
        if (current != previous)
            transform(previous, current, _second is null ? null : Distance() * _pinchZoomPerDip);
        Claim(e);
        return true;
    }

    internal void Release(PointerReleasedEventArgs e)
    {
        Move(e);
        Forget(e.Pointer);
    }

    internal void Forget(IPointer pointer)
    {
        if (pointer == _first)
        {
            _first = _second;
            _firstAt = _secondAt;
            _second = null;
        }
        else if (pointer == _second)
            _second = null;

        // The finger still on the glass continues from its own position, not the old midpoint.
        if (_first is null)
            _claimed = false;
    }

    internal void Clear()
    {
        _first = _second = null;
        _claimed = false;
    }

    private Point Center() => _second is null ? _firstAt : _firstAt + (_secondAt - _firstAt) / 2;
    private double Distance() => Math.Max(1, ((Vector)(_secondAt - _firstAt)).Length);

    // Avalonia already implicitly captures each touch to its hit target. Prevent a parent scroll or
    // drawer recognizer from taking that capture once this viewport owns the gesture.
    private static void Claim(PointerEventArgs e)
    {
        e.PreventGestureRecognition();
        e.Handled = true;
    }
}
