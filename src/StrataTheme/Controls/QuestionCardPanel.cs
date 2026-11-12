using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>Interpolates the question card's body and answered chip without squeezing either layout.</summary>
internal sealed class QuestionCardPanel : Panel
{
    private static readonly Easing MotionEasing = new CubicEaseOut();
    private bool _isOpen = true;
    private bool _isMoving;
    private int _motionVersion;
    private double _reveal = 1;
    private double _from;
    private double _durationMs;
    private long _start;

    internal double Reveal => _reveal;

    internal void SetOpen(bool open, bool animate)
    {
        _motionVersion++;
        _isMoving = false;
        _isOpen = open;
        var target = open ? 1d : 0d;
        if (!animate || Math.Abs(_reveal - target) < 0.001
            || Bounds.Width <= 0 || !IsEffectivelyVisible
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            SetReveal(target);
            return;
        }

        _from = _reveal;
        _durationMs = (open ? 220 : 170) * Math.Max(0.4, Math.Abs(target - _from));
        _start = 0;
        _isMoving = true;
        SetReveal(_reveal);
        var version = _motionVersion;
        topLevel.RequestAnimationFrame(_ => OnMotionFrame(topLevel, version));
    }

    private void OnMotionFrame(TopLevel topLevel, int version)
    {
        if (version != _motionVersion)
            return;

        // Let the newly visible body lay out before starting its motion.
        if (_start == 0)
        {
            if (IsMeasureValid)
                _start = Stopwatch.GetTimestamp();
            topLevel.RequestAnimationFrame(_ => OnMotionFrame(topLevel, version));
            return;
        }

        var t = Math.Clamp(Stopwatch.GetElapsedTime(_start).TotalMilliseconds / _durationMs, 0, 1);
        var target = _isOpen ? 1d : 0d;
        if (t >= 1)
            _isMoving = false;
        SetReveal(t >= 1 ? target : _from + (target - _from) * MotionEasing.Ease(t));
        if (_isMoving)
            topLevel.RequestAnimationFrame(_ => OnMotionFrame(topLevel, version));
    }

    private void SetReveal(double reveal)
    {
        _reveal = reveal;
        if (Children.Count == 2)
        {
            var body = Children[0];
            var chip = Children[1];
            body.IsVisible = _isMoving || reveal > 0;
            chip.IsVisible = _isMoving || reveal < 1;
            body.Opacity = reveal;
            chip.Opacity = 1 - reveal;
            body.IsHitTestVisible = _isOpen;
            chip.IsHitTestVisible = !_isOpen;
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(availableSize.WithHeight(double.PositiveInfinity));
        if (Children.Count != 2)
            return base.MeasureOverride(availableSize);

        var body = Children[0].DesiredSize;
        var chip = Children[1].DesiredSize;
        return new Size(chip.Width + (body.Width - chip.Width) * _reveal,
            chip.Height + (body.Height - chip.Height) * _reveal);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
            child.Arrange(new Rect(child.DesiredSize.WithWidth(
                Math.Max(finalSize.Width, child.DesiredSize.Width))));
        return finalSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetOpen(_isOpen, animate: false);
        base.OnDetachedFromVisualTree(e);
    }
}
