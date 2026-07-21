using System;
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
/// Runs a compositor opacity pulse only while its target is active, attached, and visible.
/// </summary>
public sealed class LifecycleOpacityPulse
{
    private static readonly ConditionalWeakTable<Visual, PulseState> States = new();
    private static readonly LinearEasing LinearSegmentEasing = new();

    public static readonly AttachedProperty<bool> IsActiveProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, bool>("IsActive");

    public static readonly AttachedProperty<double> FromOpacityProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, double>(
            "FromOpacity",
            1d,
            validate: IsValidOpacity);

    public static readonly AttachedProperty<double> ToOpacityProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, double>(
            "ToOpacity",
            0.5d,
            validate: IsValidOpacity);

    public static readonly AttachedProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, TimeSpan>(
            "Duration",
            TimeSpan.FromSeconds(1),
            validate: value => value > TimeSpan.Zero);

    public static readonly AttachedProperty<double> HoldUntilProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, double>(
            "HoldUntil",
            0d,
            validate: IsValidProgress);

    public static readonly AttachedProperty<double> PeakAtProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, double>(
            "PeakAt",
            0.5d,
            validate: IsValidProgress);

    public static readonly AttachedProperty<PlaybackDirection> PlaybackDirectionProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, PlaybackDirection>(
            "PlaybackDirection",
            PlaybackDirection.Normal);

    public static readonly AttachedProperty<LifecycleOpacityPulseEasing> EasingProperty =
        AvaloniaProperty.RegisterAttached<LifecycleOpacityPulse, Visual, LifecycleOpacityPulseEasing>(
            "Easing",
            LifecycleOpacityPulseEasing.Linear);

    static LifecycleOpacityPulse()
    {
        IsActiveProperty.Changed.AddClassHandler<Visual>(OnIsActiveChanged);
        FromOpacityProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
        ToOpacityProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
        DurationProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
        HoldUntilProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
        PeakAtProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
        PlaybackDirectionProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
        EasingProperty.Changed.AddClassHandler<Visual>(OnConfigurationChanged);
    }

    private LifecycleOpacityPulse()
    {
    }

    public static bool GetIsActive(Visual visual) => visual.GetValue(IsActiveProperty);

    public static void SetIsActive(Visual visual, bool value) => visual.SetValue(IsActiveProperty, value);

    public static double GetFromOpacity(Visual visual) => visual.GetValue(FromOpacityProperty);

    public static void SetFromOpacity(Visual visual, double value) => visual.SetValue(FromOpacityProperty, value);

    public static double GetToOpacity(Visual visual) => visual.GetValue(ToOpacityProperty);

    public static void SetToOpacity(Visual visual, double value) => visual.SetValue(ToOpacityProperty, value);

    public static TimeSpan GetDuration(Visual visual) => visual.GetValue(DurationProperty);

    public static void SetDuration(Visual visual, TimeSpan value) => visual.SetValue(DurationProperty, value);

    public static double GetHoldUntil(Visual visual) => visual.GetValue(HoldUntilProperty);

    public static void SetHoldUntil(Visual visual, double value) => visual.SetValue(HoldUntilProperty, value);

    public static double GetPeakAt(Visual visual) => visual.GetValue(PeakAtProperty);

    public static void SetPeakAt(Visual visual, double value) => visual.SetValue(PeakAtProperty, value);

    public static PlaybackDirection GetPlaybackDirection(Visual visual) =>
        visual.GetValue(PlaybackDirectionProperty);

    public static void SetPlaybackDirection(Visual visual, PlaybackDirection value) =>
        visual.SetValue(PlaybackDirectionProperty, value);

    public static LifecycleOpacityPulseEasing GetEasing(Visual visual) => visual.GetValue(EasingProperty);

    public static void SetEasing(Visual visual, LifecycleOpacityPulseEasing value) =>
        visual.SetValue(EasingProperty, value);

    internal static bool IsRunning(Visual visual) =>
        States.TryGetValue(visual, out var state) && state.IsRunning;

    private static bool IsValidOpacity(double value) =>
        !double.IsNaN(value) && value is >= 0d and <= 1d;

    private static bool IsValidProgress(double value) =>
        !double.IsNaN(value) && value is >= 0d and <= 1d;

    private static void OnIsActiveChanged(Visual visual, AvaloniaPropertyChangedEventArgs _)
    {
        if (GetIsActive(visual))
        {
            States.GetValue(visual, static target => new PulseState(target)).Update();
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

    private sealed class PulseState : IDisposable
    {
        private readonly Visual _target;
        private readonly EffectiveVisibilityObserver _visibilityObserver;
        private CompositionVisual? _compositionVisual;
        private PulseConfiguration _configuration;
        private int _scheduledUpdateVersion;
        private bool _isAttached;
        private bool _isRunning;
        private bool _isDisposed;

        public PulseState(Visual target)
        {
            _target = target;
            _visibilityObserver = new EffectiveVisibilityObserver(target, () => Update());
            _target.AttachedToVisualTree += OnAttachedToVisualTree;
            _target.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _isAttached = target.IsAttachedToVisualTree();
            if (_isAttached)
                _visibilityObserver.Subscribe();
        }

        public bool IsRunning => _isRunning;

        public void Update(bool retryIfVisualUnavailable = true)
        {
            if (_isDisposed)
                return;

            if (!GetIsActive(_target) ||
                !_isAttached ||
                !_target.IsEffectivelyVisible)
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

            var configuration = PulseConfiguration.Read(_target);
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
            _visibilityObserver.Dispose();
        }

        private void Start(CompositionVisual visual, PulseConfiguration configuration)
        {
            if (configuration.PlaybackDirection is not PlaybackDirection.Alternate and
                not PlaybackDirection.AlternateReverse &&
                (configuration.HoldUntil >= configuration.PeakAt ||
                configuration.PeakAt >= 1d))
            {
                throw new InvalidOperationException(
                   $"Opacity pulse HoldUntil ({configuration.HoldUntil}) must be before PeakAt " +
                   $"({configuration.PeakAt}), and PeakAt must be before 1.");
            }

            var animation = visual.Compositor.CreateStableScalarKeyFrameAnimation();
            animation.Target = "Opacity";
            animation.Duration = configuration.Duration;
            animation.Direction = configuration.PlaybackDirection;
            animation.IterationBehavior = AnimationIterationBehavior.Forever;

            InsertKeyFrame(animation, 0f, (float)configuration.FromOpacity, null);

            if (configuration.PlaybackDirection is PlaybackDirection.Alternate or PlaybackDirection.AlternateReverse)
            {
                InsertTimelineKeyFrame(
                    animation,
                    0d,
                    1d,
                    (float)configuration.ToOpacity,
                    configuration.Easing);
            }
            else
            {
                var previousCue = 0d;
                if (configuration.HoldUntil > 0)
                {
                    InsertTimelineKeyFrame(
                        animation,
                        previousCue,
                        configuration.HoldUntil,
                        (float)configuration.FromOpacity,
                        configuration.Easing);
                    previousCue = configuration.HoldUntil;
                }

                InsertTimelineKeyFrame(
                    animation,
                    previousCue,
                    configuration.PeakAt,
                    (float)configuration.ToOpacity,
                    configuration.Easing);
                InsertTimelineKeyFrame(
                    animation,
                    configuration.PeakAt,
                    1d,
                    (float)configuration.FromOpacity,
                    configuration.Easing);
            }

            visual.Opacity = (float)_target.Opacity;
            visual.StartAnimation("Opacity", animation);

            _compositionVisual = visual;
            _configuration = configuration;
            _isRunning = true;
        }

        private void Stop()
        {
            _scheduledUpdateVersion++;

            if (_compositionVisual is not null)
            {
                _compositionVisual.StopAnimation("Opacity");
                _compositionVisual.Opacity = (float)_target.Opacity;
            }

            _compositionVisual = null;
            _isRunning = false;
        }

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
            Update();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = false;
            _visibilityObserver.Unsubscribe();
            Stop();
        }

        private static void InsertKeyFrame(
            ScalarKeyFrameAnimation animation,
            float progress,
            float value,
            IEasing? easing)
        {
            if (easing is null)
                animation.InsertKeyFrame(progress, value);
            else
                animation.InsertKeyFrame(progress, value, easing);
        }

        private static void InsertTimelineKeyFrame(
            ScalarKeyFrameAnimation animation,
            double previousLogicalCue,
            double logicalCue,
            float value,
            LifecycleOpacityPulseEasing easing)
        {
            var previousAnimationCue = MapTimelineCue(easing, previousLogicalCue);
            var animationCue = MapTimelineCue(easing, logicalCue);
            var segmentEasing = CreateSegmentEasing(
                easing,
                previousLogicalCue,
                logicalCue,
                previousAnimationCue,
                animationCue);

            InsertKeyFrame(animation, (float)animationCue, value, segmentEasing);
        }
    }

    internal static double MapTimelineCue(LifecycleOpacityPulseEasing easing, double logicalCue) =>
        easing switch
        {
            LifecycleOpacityPulseEasing.Linear => logicalCue,
            LifecycleOpacityPulseEasing.CompositorDefault => logicalCue,
            LifecycleOpacityPulseEasing.SineEaseInOut =>
                Math.Acos(1d - (2d * logicalCue)) / Math.PI,
            _ => throw new InvalidOperationException($"Unsupported opacity pulse easing '{easing}'."),
        };

    internal static IEasing? CreateSegmentEasing(
        LifecycleOpacityPulseEasing easing,
        double logicalStart,
        double logicalEnd,
        double animationStart,
        double animationEnd) =>
        easing switch
        {
            LifecycleOpacityPulseEasing.Linear => LinearSegmentEasing,
            LifecycleOpacityPulseEasing.CompositorDefault => null,
            LifecycleOpacityPulseEasing.SineEaseInOut => new TimelineSegmentEasing(
                easing,
                logicalStart,
                logicalEnd,
                animationStart,
                animationEnd),
            _ => throw new InvalidOperationException($"Unsupported opacity pulse easing '{easing}'."),
        };

    internal static double EaseTimelineSegment(
        LifecycleOpacityPulseEasing easing,
        double logicalStart,
        double logicalEnd,
        double animationStart,
        double animationEnd,
        double segmentProgress)
    {
        var timelineProgress = animationStart +
                               ((animationEnd - animationStart) * Math.Clamp(segmentProgress, 0d, 1d));
        var easedTimelineProgress = ApplyTimelineEasing(easing, timelineProgress);

        return Math.Clamp(
            (easedTimelineProgress - logicalStart) / (logicalEnd - logicalStart),
            0d,
            1d);
    }

    private static double ApplyTimelineEasing(
        LifecycleOpacityPulseEasing easing,
        double timelineProgress) =>
        easing switch
        {
            LifecycleOpacityPulseEasing.Linear => timelineProgress,
            LifecycleOpacityPulseEasing.CompositorDefault => timelineProgress,
            LifecycleOpacityPulseEasing.SineEaseInOut =>
                0.5d * (1d - Math.Cos(timelineProgress * Math.PI)),
            _ => throw new InvalidOperationException($"Unsupported opacity pulse easing '{easing}'."),
        };

    private sealed class TimelineSegmentEasing : Easing
    {
        private readonly LifecycleOpacityPulseEasing _easing;
        private readonly double _logicalStart;
        private readonly double _logicalEnd;
        private readonly double _animationStart;
        private readonly double _animationEnd;

        public TimelineSegmentEasing(
            LifecycleOpacityPulseEasing easing,
            double logicalStart,
            double logicalEnd,
            double animationStart,
            double animationEnd)
        {
            _easing = easing;
            _logicalStart = logicalStart;
            _logicalEnd = logicalEnd;
            _animationStart = animationStart;
            _animationEnd = animationEnd;
        }

        public override double Ease(double progress) =>
            EaseTimelineSegment(
                _easing,
                _logicalStart,
                _logicalEnd,
                _animationStart,
                _animationEnd,
                progress);
    }

    private readonly record struct PulseConfiguration(
        double FromOpacity,
        double ToOpacity,
        TimeSpan Duration,
        double HoldUntil,
        double PeakAt,
        PlaybackDirection PlaybackDirection,
        LifecycleOpacityPulseEasing Easing)
    {
        public static PulseConfiguration Read(Visual target) =>
            new(
                GetFromOpacity(target),
                GetToOpacity(target),
                GetDuration(target),
                GetHoldUntil(target),
                GetPeakAt(target),
                GetPlaybackDirection(target),
                GetEasing(target));
    }
}

public enum LifecycleOpacityPulseEasing
{
    Linear,
    CompositorDefault,
    SineEaseInOut,
}
