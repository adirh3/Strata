using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Progressive text disclosure. Shows a collapsed view of content with a
/// gradient fade-out. Click "Read more" and the content smoothly expands
/// with an animated height transition. The fade overlay dissolves during reveal.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataPeek CollapsedHeight="80" PeekText="Show more" CollapseText="Show less"&gt;
///     &lt;controls:StrataPeek.Content&gt;
///         &lt;TextBlock TextWrapping="Wrap" Text="Very long text that gets clipped..." /&gt;
///     &lt;/controls:StrataPeek.Content&gt;
/// &lt;/controls:StrataPeek&gt;
/// </code>
/// <para><b>Template parts:</b> PART_FadeOverlay (Border), PART_ContentHost (Border),
/// PART_ContentPresenter (ContentPresenter), PART_ToggleButton (Button).</para>
/// </remarks>
public class StrataPeek : TemplatedControl
{
    private Border? _fadeOverlay;
    private Border? _contentHost;
    private ContentPresenter? _contentPresenter;
    private bool _isAnimating;
    private CancellationTokenSource? _transitionCts;

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataPeek, object?>(nameof(Content));

    public static readonly StyledProperty<double> CollapsedHeightProperty =
        AvaloniaProperty.Register<StrataPeek, double>(nameof(CollapsedHeight), 80d);

    public static readonly StyledProperty<bool> IsRevealedProperty =
        AvaloniaProperty.Register<StrataPeek, bool>(nameof(IsRevealed));

    public static readonly StyledProperty<string> PeekTextProperty =
        AvaloniaProperty.Register<StrataPeek, string>(nameof(PeekText), "Read more");

    public static readonly StyledProperty<string> CollapseTextProperty =
        AvaloniaProperty.Register<StrataPeek, string>(nameof(CollapseText), "Show less");

    static StrataPeek()
    {
        IsRevealedProperty.Changed.AddClassHandler<StrataPeek>((peek, _) => peek.OnIsRevealedChanged());
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public double CollapsedHeight
    {
        get => GetValue(CollapsedHeightProperty);
        set => SetValue(CollapsedHeightProperty, value);
    }

    public bool IsRevealed
    {
        get => GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public string PeekText
    {
        get => GetValue(PeekTextProperty);
        set => SetValue(PeekTextProperty, value);
    }

    public string CollapseText
    {
        get => GetValue(CollapseTextProperty);
        set => SetValue(CollapseTextProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _fadeOverlay = e.NameScope.Find<Border>("PART_FadeOverlay");
        _contentHost = e.NameScope.Find<Border>("PART_ContentHost");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");

        var toggleButton = e.NameScope.Find<Button>("PART_ToggleButton");
        if (toggleButton is not null)
            toggleButton.Click += (_, _) =>
            {
                if (_isAnimating)
                    return;

                IsRevealed = !IsRevealed;
            };

        ApplyStateInstant();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _transitionCts?.Cancel();
        _transitionCts?.Dispose();
        _transitionCts = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnIsRevealedChanged()
    {
        _ = AnimateRevealAsync();
    }

    private void ApplyStateInstant()
    {
        if (_contentHost is null || _contentPresenter is null)
            return;

        if (IsRevealed)
        {
            _contentHost.MaxHeight = double.PositiveInfinity;
            if (_fadeOverlay is not null)
                _fadeOverlay.Opacity = 0;
        }
        else
        {
            _contentHost.MaxHeight = CollapsedHeight;
            if (_fadeOverlay is not null)
                _fadeOverlay.Opacity = 1;
        }
    }

    private async Task AnimateRevealAsync()
    {
        if (_contentHost is null || _contentPresenter is null) return;

        _transitionCts?.Cancel();
        _transitionCts?.Dispose();
        _transitionCts = new CancellationTokenSource();
        var token = _transitionCts.Token;

        _isAnimating = true;

        // Measure the full content height
        var width = _contentHost.Bounds.Width > 0 ? _contentHost.Bounds.Width : Math.Max(260, Bounds.Width);
        _contentPresenter.Measure(new Size(width, double.PositiveInfinity));
        var fullHeight = _contentPresenter.DesiredSize.Height;
        var collapsedH = CollapsedHeight;

        var from = _contentHost.Bounds.Height > 1
            ? _contentHost.Bounds.Height
            : (IsRevealed ? collapsedH : fullHeight);
        var to = IsRevealed ? fullHeight : collapsedH;

        var easing = new CubicEaseInOut();
        var duration = TimeSpan.FromMilliseconds(350);
        var steps = 30;
        var stepDuration = duration.TotalMilliseconds / steps;

        // Animate fade overlay via composition
        AnimateFadeOverlay(!IsRevealed);

        for (var i = 1; i <= steps; i++)
        {
            if (token.IsCancellationRequested)
                return;

            var progress = easing.Ease((double)i / steps);
            var height = from + (to - from) * progress;
            _contentHost.MaxHeight = Math.Max(0, height);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(stepDuration), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        if (token.IsCancellationRequested)
            return;

        _contentHost.MaxHeight = IsRevealed ? double.PositiveInfinity : collapsedH;
        _isAnimating = false;
    }

    private void AnimateFadeOverlay(bool show)
    {
        if (_fadeOverlay is null) return;
        var visual = ElementComposition.GetElementVisual(_fadeOverlay);
        if (visual is null) return;

        var comp = visual.Compositor;
        var anim = comp.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, show ? 0f : 1f);
        anim.InsertKeyFrame(1f, show ? 1f : 0f);
        anim.Duration = TimeSpan.FromMilliseconds(300);
        visual.StartAnimation("Opacity", anim);
    }
}
