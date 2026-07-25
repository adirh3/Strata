using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Animation;

/// <summary>
/// Sweeps a visual across its parent on the compositor, but only while its target is active, attached,
/// and visible. Indeterminate progress indicators need a perpetual travelling motion, and a
/// lifecycle-managed compositor animation gives that without leaving a style animation running on hidden
/// or detached visuals.
/// </summary>
public sealed class LifecycleOffsetSweep
{
    private static readonly ConditionalWeakTable<Visual, SweepState> States = new();
    private static readonly LinearEasing SweepEasing = new();

    public static readonly AttachedProperty<bool> IsActiveProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOffsetSweep, Visual, bool>("IsActive");

    public static readonly AttachedProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOffsetSweep, Visual, TimeSpan>(
            "Duration",
            TimeSpan.FromSeconds(1.4),
            validate: value => value > TimeSpan.Zero);

    static LifecycleOffsetSweep()
    {
        IsActiveProperty.Changed.AddClassHandler<Visual>(OnIsActiveChanged);
        DurationProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
    }

    private LifecycleOffsetSweep()
    {
    }

    public static bool GetIsActive(Visual visual) => visual.GetValue(IsActiveProperty);

    public static void SetIsActive(Visual visual, bool value) => visual.SetValue(IsActiveProperty, value);

    public static TimeSpan GetDuration(Visual visual) => visual.GetValue(DurationProperty);

    public static void SetDuration(Visual visual, TimeSpan value) => visual.SetValue(DurationProperty, value);

    internal static bool IsRunning(Visual visual) =>
        States.TryGetValue(visual, out var state) && state.IsRunning;

    private static void OnIsActiveChanged(Visual visual, AvaloniaPropertyChangedEventArgs _)
    {
        if (GetIsActive(visual))
        {
            States.GetValue(visual, static target => new SweepState(target)).Update();
            return;
        }

        if (States.TryGetValue(visual, out var state))
        {
            state.Dispose();
            States.Remove(visual);
        }
    }

    private static void OnConfigurationChanged(Visual visual, AvaloniaPropertyChangedEventArgs _)
    {
        if (States.TryGetValue(visual, out var state))
            state.Update();
    }

    /// <summary>
    /// The travel keeps the target on screen for the whole cycle: it enters from just outside the leading
    /// edge and leaves at the trailing edge, so the sweep never pauses out of sight.
    /// </summary>
    private readonly record struct SweepConfiguration(Vector3 Origin, double From, double To, TimeSpan Duration)
    {
        public static SweepConfiguration Read(Visual target)
        {
            var bounds = target.Bounds;
            var trackWidth = (target.GetVisualParent() as Visual)?.Bounds.Width ?? 0d;
            return new SweepConfiguration(
                new Vector3((float)bounds.X, (float)bounds.Y, 0f),
                -bounds.Width,
                trackWidth,
                GetDuration(target));
        }

        public bool HasTravel => To - From > 1d;
    }

    private sealed class SweepState : IDisposable
    {
        private readonly Visual _target;
        private readonly EffectiveVisibilityObserver _visibilityObserver;
        private CompositionVisual? _compositionVisual;
        private Visual? _boundsSource;
        private SweepConfiguration _configuration;
        private int _scheduledUpdateVersion;
        private bool _isAttached;
        private bool _isRunning;
        private bool _isDisposed;

        public SweepState(Visual target)
        {
            _target = target;
            _visibilityObserver = new EffectiveVisibilityObserver(target, () => Update());
            _target.AttachedToVisualTree += OnAttachedToVisualTree;
            _target.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _target.PropertyChanged += OnBoundsChanged;
            _isAttached = target.IsAttachedToVisualTree();
            if (_isAttached)
            {
                _visibilityObserver.Subscribe();
                SubscribeToTrackBounds();
            }
        }

        public bool IsRunning => _isRunning;

        public void Update(bool retryIfVisualUnavailable = true)
        {
            if (_isDisposed)
                return;

            var configuration = SweepConfiguration.Read(_target);
            if (!GetIsActive(_target) ||
                !_isAttached ||
                !_target.IsEffectivelyVisible ||
                !configuration.HasTravel)
            {
                Stop();
                return;
            }

            var visual = ElementComposition.GetElementVisual(_target);
            if (visual is null)
            {
                if (retryIfVisualUnavailable)
                    ScheduleUpdate();
                return;
            }

            if (_isRunning &&
                ReferenceEquals(_compositionVisual, visual) &&
                configuration == _configuration)
            {
                return;
            }

            Stop();
            Start(visual, configuration);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _scheduledUpdateVersion++;
            Stop();
            _target.AttachedToVisualTree -= OnAttachedToVisualTree;
            _target.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            _target.PropertyChanged -= OnBoundsChanged;
            UnsubscribeFromTrackBounds();
            _visibilityObserver.Dispose();
        }

        private void Start(CompositionVisual visual, SweepConfiguration configuration)
        {
            var layoutOffset = configuration.Origin;

            var animation = visual.Compositor.CreateStableVector3KeyFrameAnimation();
            animation.Target = "Offset";
            animation.Duration = configuration.Duration;
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            animation.InsertKeyFrame(
                0f,
                layoutOffset with { X = layoutOffset.X + (float)configuration.From },
                SweepEasing);
            animation.InsertKeyFrame(
                1f,
                layoutOffset with { X = layoutOffset.X + (float)configuration.To },
                SweepEasing);

            visual.Offset = layoutOffset;
            visual.StartAnimation("Offset", animation);

            _compositionVisual = visual;
            _configuration = configuration;
            _isRunning = true;
        }

        private void Stop()
        {
            _scheduledUpdateVersion++;

            if (_compositionVisual is not null)
            {
                _compositionVisual.StopAnimation("Offset");
                _compositionVisual.Offset = LayoutOffset();
            }

            _compositionVisual = null;
            _isRunning = false;
        }

        private Vector3 LayoutOffset() => new((float)_target.Bounds.X, (float)_target.Bounds.Y, 0f);

        private void ScheduleUpdate()
        {
            var version = ++_scheduledUpdateVersion;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_isDisposed && version == _scheduledUpdateVersion)
                        Update(retryIfVisualUnavailable: false);
                },
                DispatcherPriority.Loaded);
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = true;
            _visibilityObserver.Subscribe();
            SubscribeToTrackBounds();
            Update();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = false;
            _visibilityObserver.Unsubscribe();
            UnsubscribeFromTrackBounds();
            Stop();
        }

        private void SubscribeToTrackBounds()
        {
            UnsubscribeFromTrackBounds();
            _boundsSource = _target.GetVisualParent();
            if (_boundsSource is not null)
                _boundsSource.PropertyChanged += OnBoundsChanged;
        }

        private void UnsubscribeFromTrackBounds()
        {
            if (_boundsSource is not null)
                _boundsSource.PropertyChanged -= OnBoundsChanged;

            _boundsSource = null;
        }

        private void OnBoundsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.BoundsProperty)
                Update();
        }
    }
}
