using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Animation;

/// <summary>Reveals an attached entrance host without changing its measured layout.</summary>
public static class SlideFadeEntrance
{
    private static readonly TransformOperations RestingTransform =
        TransformOperations.Parse("translateY(0px)");
    private static readonly ConditionalWeakTable<Control, CompositedEntrance> CompositedEntrances = new();

    /// <summary>
    /// Plays a short rise and fade on a host whose opacity, transform and transitions are owned
    /// by the entrance. Repeated calls replace the previous motion; detaching leaves the final state.
    /// A <paramref name="delay"/> holds the host hidden first, so a group of hosts can cascade in.
    /// </summary>
    public static void Play(Control host, double offsetY = 12, TimeSpan? duration = null, TimeSpan? delay = null)
    {
        if (!host.IsAttachedToVisualTree() || !host.IsEffectivelyVisible)
            return;

        var motionDuration = duration ?? TimeSpan.FromMilliseconds(260);
        var motionDelay = delay ?? TimeSpan.Zero;
        host.Transitions = null;
        host.SetCurrentValue(Visual.OpacityProperty, 0d);
        host.SetCurrentValue(Visual.RenderTransformProperty,
            TransformOperations.Parse(FormattableString.Invariant($"translateY({offsetY}px)")));

        host.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = motionDuration * 0.7,
                Delay = motionDelay,
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = motionDuration,
                Delay = motionDelay,
                Easing = new CubicEaseOut(),
            },
        };

        // Keep the resting values underneath the transitions so interruption never strands a host.
        host.SetCurrentValue(Visual.OpacityProperty, 1d);
        host.SetCurrentValue(Visual.RenderTransformProperty, RestingTransform);
    }

    /// <summary>
    /// Plays the same entrance on the render thread. Translation does not change the layout,
    /// effective viewport or scroll anchors, and playback continues while the UI thread is busy.
    /// The host must own its opacity and transform. Hiding, detaching or replaying cancels the motion.
    /// </summary>
    public static void PlayComposited(
        Control host, double offsetY = 12, TimeSpan? duration = null, TimeSpan? delay = null)
    {
        StopComposited(host);
        if (!host.IsAttachedToVisualTree() || !host.IsEffectivelyVisible)
            return;

        var entrance = new CompositedEntrance(
            host, offsetY, duration ?? TimeSpan.FromMilliseconds(260), delay ?? TimeSpan.Zero);
        CompositedEntrances.Add(host, entrance);
        entrance.Queue();
    }

    /// <summary>Whether a render-thread entrance is queued or playing for this host.</summary>
    internal static bool IsCompositedRunning(Control host) => CompositedEntrances.TryGetValue(host, out _);

    /// <summary>Stops a render-thread entrance and leaves the host in its resting state.</summary>
    public static void StopComposited(Control host)
    {
        if (!CompositedEntrances.TryGetValue(host, out var entrance))
            return;
        CompositedEntrances.Remove(host);
        entrance.Dispose();
    }

    private sealed class CompositedEntrance : IDisposable
    {
        private readonly Control _host;
        private readonly double _offsetY;
        private readonly TimeSpan _duration;
        private readonly TimeSpan _delay;
        private readonly EffectiveVisibilityObserver _visibility;
        private CompositionVisual? _visual;
        private IDisposable? _completion;
        private bool _disposed;

        public CompositedEntrance(Control host, double offsetY, TimeSpan duration, TimeSpan delay)
        {
            _host = host;
            _offsetY = offsetY;
            _duration = duration;
            _delay = delay;
            _visibility = new EffectiveVisibilityObserver(host, () =>
            {
                if (!host.IsEffectivelyVisible)
                    StopComposited(host);
            });
            _visibility.Subscribe();
            host.DetachedFromVisualTree += OnDetached;
        }

        public void Queue()
        {
            if (ElementComposition.GetElementVisual(_host) is { } visual)
                visual.Compositor.RequestCompositionUpdate(Start);
            else
                Dispatcher.UIThread.Post(Start, DispatcherPriority.Loaded);
        }

        private void Start()
        {
            if (_disposed)
                return;
            if (!_host.IsAttachedToVisualTree() || !_host.IsEffectivelyVisible)
            {
                StopComposited(_host);
                return;
            }

            _host.Transitions = null;
            _host.SetCurrentValue(Visual.OpacityProperty, 1d);
            _host.SetCurrentValue(Visual.RenderTransformProperty, RestingTransform);
            _visual = ElementComposition.GetElementVisual(_host);
            if (_visual is null)
            {
                StopComposited(_host);
                Play(_host, _offsetY, _duration, _delay);
                return;
            }

            var easing = new CubicEaseOut();
            var fade = _visual.Compositor.CreateStableScalarKeyFrameAnimation();
            fade.Duration = _duration * 0.7;
            fade.DelayTime = _delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            fade.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            fade.InsertKeyFrame(0f, 0f, easing);
            fade.InsertKeyFrame(1f, 1f, easing);

            var rise = _visual.Compositor.CreateStableVector3DKeyFrameAnimation();
            rise.Duration = _duration;
            rise.DelayTime = _delay;
            rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            rise.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            rise.InsertKeyFrame(0f, new Vector3D(0, _offsetY, 0), easing);
            rise.InsertKeyFrame(1f, default, easing);

            _visual.Opacity = 1f;
            _visual.Translation = default;
            _visual.StartAnimation(nameof(CompositionVisual.Opacity), fade);
            _visual.StartAnimation(nameof(CompositionVisual.Translation), rise);
            _completion = DispatcherTimer.RunOnce(
                () =>
                {
                    if (!_disposed)
                        StopComposited(_host);
                }, _duration + _delay, DispatcherPriority.Background);
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => StopComposited(_host);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _completion?.Dispose();
            _visibility.Dispose();
            _host.DetachedFromVisualTree -= OnDetached;
            if (_visual is not null)
            {
                _visual.StopAnimation(nameof(CompositionVisual.Opacity));
                _visual.StopAnimation(nameof(CompositionVisual.Translation));
                // Unchanged base values are not serialized, even after StopAnimation.
                // These writes coalesce into one commit containing only the resting values.
                var restingOpacity = (float)_host.Opacity;
                _visual.Opacity = restingOpacity == 0f ? 1f : 0f;
                _visual.Opacity = restingOpacity;
                _visual.Translation = new Vector3D(0, 1, 0);
                _visual.Translation = default;
                _visual = null;
            }
        }
    }
}
