using System;
using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// An expanding summary card. Shows a compact summary that, when clicked,
/// smoothly expands to reveal full content with animated height, cross-fading
/// content, and a pulsing stratum accent line.
/// </summary>
public class StrataCard : TemplatedControl
{
    private Border? _stratumLine;
    private Border? _contentHost;
    private ContentPresenter? _summaryPresenter;
    private ContentPresenter? _detailPresenter;
    private bool _isAnimating;

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Header));

    public static readonly StyledProperty<object?> SummaryProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Summary));

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Detail));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataCard, bool>(nameof(IsExpanded));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public object? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _stratumLine = e.NameScope.Find<Border>("PART_StratumLine");
        _contentHost = e.NameScope.Find<Border>("PART_ContentHost");
        _summaryPresenter = e.NameScope.Find<ContentPresenter>("PART_SummaryPresenter");
        _detailPresenter = e.NameScope.Find<ContentPresenter>("PART_DetailPresenter");
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (_isAnimating)
            return;

        e.Handled = true;
        Toggle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_isAnimating)
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            Toggle();
        }
    }

    private void Toggle()
    {
        if (_contentHost is null || _summaryPresenter is null || _detailPresenter is null)
            return;

        // Measure both states to get heights
        var currentHeight = _contentHost.Bounds.Height;

        // Make the target visible to measure it, keep both visible during animation
        var appearing = IsExpanded ? _summaryPresenter : _detailPresenter;
        var disappearing = IsExpanded ? _detailPresenter : _summaryPresenter;

        appearing.IsVisible = true;
        appearing.Opacity = 0;
        appearing.Measure(new Size(_contentHost.Bounds.Width, double.PositiveInfinity));
        var targetHeight = appearing.DesiredSize.Height;

        // Flip state
        IsExpanded = !IsExpanded;

        // Animate height of the content host
        _isAnimating = true;
        AnimateHeight(currentHeight, targetHeight, appearing, disappearing);
        AnimateStratumLine();
    }

    private async void AnimateHeight(double from, double to,
        ContentPresenter appearing, ContentPresenter disappearing)
    {
        if (_contentHost is null) return;

        var duration = TimeSpan.FromMilliseconds(320);
        var easing = new CubicEaseInOut();
        var steps = 30;
        var stepDuration = duration.TotalMilliseconds / steps;

        // Cross-fade: fade out the disappearing, fade in the appearing
        AnimatePresenterOpacity(appearing, 0, 1, duration);
        AnimatePresenterOpacity(disappearing, 1, 0, TimeSpan.FromMilliseconds(150));

        // Animate height step-by-step
        for (int i = 1; i <= steps; i++)
        {
            var progress = easing.Ease((double)i / steps);
            var height = from + (to - from) * progress;
            _contentHost.MaxHeight = Math.Max(0, height);
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(stepDuration));
        }

        // Clean up
        _contentHost.MaxHeight = double.PositiveInfinity;
        disappearing.IsVisible = false;
        disappearing.Opacity = 1;
        _isAnimating = false;
    }

    private void AnimatePresenterOpacity(ContentPresenter presenter, float from, float to, TimeSpan duration)
    {
        var visual = ElementComposition.GetElementVisual(presenter);
        if (visual is null) return;

        var comp = visual.Compositor;
        var anim = comp.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, from);
        anim.InsertKeyFrame(1f, to);
        anim.Duration = duration;
        visual.StartAnimation("Opacity", anim);
    }

    private void AnimateStratumLine()
    {
        if (_stratumLine is null) return;
        var visual = ElementComposition.GetElementVisual(_stratumLine);
        if (visual is null) return;

        var comp = visual.Compositor;
        visual.CenterPoint = new Avalonia.Vector3D(
            _stratumLine.Bounds.Width / 2,
            _stratumLine.Bounds.Height / 2, 0);

        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scaleAnim.InsertKeyFrame(0.35f, new Vector3(1f, 2f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(400);
        visual.StartAnimation("Scale", scaleAnim);
    }
}

