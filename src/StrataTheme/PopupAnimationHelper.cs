using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Animation;

namespace StrataTheme;

/// <summary>
/// Placement-aware, render-thread open and close animations for native and overlay popups.
/// Expansion takes precedence when both attached animation properties are enabled.
/// </summary>
public static class PopupAnimationHelper
{
    private static readonly ConditionalWeakTable<Popup, PopupMotion> Motions = new();
    private static readonly Vector3D RestingScale = new(1, 1, 1);
    internal static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(220);
    internal static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(150);

    public static readonly AttachedProperty<bool> EnableOpenAnimationProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>(
            "EnableOpenAnimation", typeof(PopupAnimationHelper));

    public static readonly AttachedProperty<bool> EnableOverlayAnimationProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>(
            "EnableOverlayAnimation", typeof(PopupAnimationHelper));

    /// <summary>Opening duration for one popup. Closing timing and other popups are unchanged.</summary>
    public static readonly AttachedProperty<TimeSpan> OpenAnimationDurationProperty =
        AvaloniaProperty.RegisterAttached<Popup, TimeSpan>(
            "OpenAnimationDuration", typeof(PopupAnimationHelper), OpenDuration,
            validate: duration => duration > TimeSpan.Zero);

    static PopupAnimationHelper()
    {
        EnableOpenAnimationProperty.Changed.AddClassHandler<Popup>((popup, _) => Update(popup));
        EnableOverlayAnimationProperty.Changed.AddClassHandler<Popup>((popup, _) => Update(popup));
    }

    public static bool GetEnableOpenAnimation(Popup popup) =>
        popup.GetValue(EnableOpenAnimationProperty);

    public static void SetEnableOpenAnimation(Popup popup, bool value) =>
        popup.SetValue(EnableOpenAnimationProperty, value);

    public static bool GetEnableOverlayAnimation(Popup popup) =>
        popup.GetValue(EnableOverlayAnimationProperty);

    public static void SetEnableOverlayAnimation(Popup popup, bool value) =>
        popup.SetValue(EnableOverlayAnimationProperty, value);

    public static TimeSpan GetOpenAnimationDuration(Popup popup) =>
        popup.GetValue(OpenAnimationDurationProperty);

    public static void SetOpenAnimationDuration(Popup popup, TimeSpan value) =>
        popup.SetValue(OpenAnimationDurationProperty, value);

    internal static void RegisterOverlay(Control control)
    {
        if (control.FindLogicalAncestorOfType<Popup>() is not { } popup)
            return;
        if (control is PopupRoot or OverlayPopupHost)
        {
            // Popup bypasses normal control styling; its laid-out host supplies the theme default.
            if (OverlayAnimationHelper.GetAnimateOpen(control) &&
                !popup.IsSet(EnableOverlayAnimationProperty))
                SetEnableOverlayAnimation(popup, true);
            return;
        }
        var motion = Motions.GetValue(popup, static owner => new PopupMotion(owner));
        motion.Presenter = control;
        Update(popup);
    }

    internal static int GetOpenAnimationCount(Popup popup) =>
        Motions.TryGetValue(popup, out var motion) ? motion.OpenAnimationCount : 0;

    private static void Update(Popup popup)
    {
        if (!Motions.TryGetValue(popup, out var motion))
        {
            if (!GetEnableOpenAnimation(popup) && !GetEnableOverlayAnimation(popup))
                return;
            motion = Motions.GetValue(popup, static owner => new PopupMotion(owner));
        }

        if (!motion.IsEnabled)
        {
            Motions.Remove(popup);
            motion.Dispose();
        }
        else
            motion.EnsureOpen();
    }

    // Avalonia 12's cancellable Popup.Closing event is internal. Typed access keeps the
    // actual host alive for its exit without replacing popups, bindings or focus ownership.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "add_Closing")]
    private static extern void AddClosing(Popup popup, EventHandler<CancelEventArgs> handler);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "remove_Closing")]
    private static extern void RemoveClosing(Popup popup, EventHandler<CancelEventArgs> handler);

    private sealed class PopupMotion : IDisposable
    {
        private readonly Popup _popup;
        private Control? _child;
        private Control? _target;
        private CompositionVisual? _visual;
        private EffectiveVisibilityObserver? _targetVisibility;
        private IDisposable? _opacityHold;
        private IDisposable? _hitTestHold;
        private IDisposable? _enabledHold;
        private IDisposable? _closeTimer;
        private Vector3D _collapsedScale;
        private float _restingOpacity;
        private bool _openingPending;
        private bool _compositionQueued;
        private bool _resuming;
        private bool _closing;
        private bool _allowClose;
        private bool _disposed;
        private int _version;

        public PopupMotion(Popup popup)
        {
            _popup = popup;
            popup.Opened += OnOpened;
            popup.Closed += OnClosed;
            popup.PropertyChanged += OnPopupPropertyChanged;
            AddClosing(popup, OnClosing);
        }

        public Control? Presenter { get; set; }
        public int OpenAnimationCount { get; private set; }
        public bool IsEnabled => GetEnableOpenAnimation(_popup) ||
            GetEnableOverlayAnimation(_popup) ||
            (Presenter is { } presenter && OverlayAnimationHelper.GetAnimateOpen(presenter));

        public void EnsureOpen()
        {
            if (_disposed || !IsEnabled || !_popup.IsOpen ||
                _popup.Child is not { } child || !child.IsAttachedToVisualTree())
                return;
            if (_child is not null && !_closing)
                return;

            _resuming = _closing;
            _closing = false;
            _closeTimer?.Dispose();
            _closeTimer = null;
            RestoreInput();
            if (_child is null)
            {
                _child = child;
                _restingOpacity = (float)child.Opacity;
                child.DetachedFromVisualTree += OnDetached;
                _opacityHold = child.SetValue(Visual.OpacityProperty, 0d, BindingPriority.Animation);
                _target = _popup.PlacementTarget ?? _popup.TemplatedParent as Control ??
                    _popup.Parent as Control;
                if (_target is { } target)
                {
                    target.DetachedFromVisualTree += OnTargetDetached;
                    _targetVisibility = new EffectiveVisibilityObserver(target, () =>
                    {
                        if (!target.IsEffectivelyVisible)
                            CloseImmediately();
                    });
                    _targetVisibility.Subscribe();
                }
            }
            _openingPending = true;
            _compositionQueued = false;
            var version = ++_version;
            child.Loaded += OnReady;
            child.LayoutUpdated += OnLayoutUpdated;
            Dispatcher.UIThread.Post(() => QueueOpen(version), DispatcherPriority.Loaded);
        }

        private void OnOpened(object? sender, EventArgs e) => EnsureOpen();
        private void OnClosed(object? sender, EventArgs e) => Reset();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Reset();
        private void OnReady(object? sender, EventArgs e) => QueueOpen(_version);
        private void OnLayoutUpdated(object? sender, EventArgs e) => QueueOpen(_version);

        private void OnTargetDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (ReferenceEquals(sender, _target))
                CloseImmediately();
        }

        private void OnPopupPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Popup.IsOpenProperty && _popup.IsOpen && _closing)
                EnsureOpen();
            else if (e.Property == Popup.PlacementTargetProperty && _closing &&
                _popup.PlacementTarget is { } target && !ReferenceEquals(target, _target))
                CloseImmediately();
            else if (e.Property == Popup.ChildProperty && !ReferenceEquals(_child, _popup.Child))
            {
                Reset();
                Dispatcher.UIThread.Post(EnsureOpen, DispatcherPriority.Loaded);
            }
        }

        private void QueueOpen(int version)
        {
            if (!CanOpen(version) || _compositionQueued)
                return;
            var compositor = ElementComposition.GetElementVisual(_child!)?.Compositor ??
                (_target is not null ? ElementComposition.GetElementVisual(_target)?.Compositor : null);
            if (compositor is null)
                return;
            _compositionQueued = true;
            compositor.RequestCompositionUpdate(() => StartOpen(version));
        }

        private bool CanOpen(int version) => !_disposed && version == _version &&
            _openingPending && !_closing && IsEnabled && _popup.IsOpen &&
            _child is { } child && child.IsAttachedToVisualTree();

        private void StartOpen(int version)
        {
            if (!CanOpen(version))
                return;
            _compositionQueued = false;
            var child = _child!;
            if (child.Bounds.Width <= 0 || child.Bounds.Height <= 0)
                return;
            if (ElementComposition.GetElementVisual(child) is not { } visual)
            {
                QueueOpen(version);
                return;
            }
            StopWaitingForLayout();
            _opacityHold?.Dispose();
            _opacityHold = null;
            _visual = visual;
            UpdateOrigin(child, visual);
            visual.Scale = RestingScale;
            visual.Opacity = _restingOpacity;
            Play(opening: true, initial: !_resuming);
            OpenAnimationCount++;
        }

        private void UpdateOrigin(Control child, CompositionVisual visual)
        {
            var width = child.Bounds.Width;
            var height = child.Bounds.Height;
            var center = new Point(width / 2, height / 2);
            var targetHeight = height * 0.12;
            if (_popup.Placement == PlacementMode.Pointer)
                center = default;
            else if (_target is { } target && target.IsAttachedToVisualTree())
            {
                center = child.PointToClient(target.PointToScreen(
                    new Point(target.Bounds.Width / 2, target.Bounds.Height / 2)));
                var top = child.PointToClient(target.PointToScreen(default));
                var bottom = child.PointToClient(target.PointToScreen(new Point(0, target.Bounds.Height)));
                targetHeight = Math.Abs(bottom.Y - top.Y);
            }

            if (!GetEnableOpenAnimation(_popup))
                center = new Point(Math.Clamp(center.X, 0, width), Math.Clamp(center.Y, 0, height));
            visual.CenterPoint = new Vector3D(center.X, center.Y, 0);
            _collapsedScale = GetEnableOpenAnimation(_popup)
                ? new Vector3D(1, Math.Clamp(targetHeight / height, 0.12, 0.85), 1)
                : new Vector3D(0.96, 0.96, 1);
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_allowClose || _disposed || e.Cancel || _child is null)
                return;
            // ToolTip.Close clears placement bindings and theme context even if closing is cancelled.
            // Its host must close with its owner; other popup owners retain their exit animation.
            if (_child is ToolTip)
                return;
            if (_visual is null || !_child.IsAttachedToVisualTree() ||
                (_target is { } target &&
                    (!target.IsAttachedToVisualTree() || !target.IsEffectivelyVisible)))
            {
                Reset();
                return;
            }

            e.Cancel = true;
            if (_closing)
                return;
            _closing = true;
            StopWaitingForLayout();
            var version = ++_version;
            _hitTestHold = _child.SetValue(Control.IsHitTestVisibleProperty, false, BindingPriority.Animation);
            _enabledHold = _child.SetValue(Control.IsEnabledProperty, false, BindingPriority.Animation);
            _popup.SetCurrentValue(Popup.IsOpenProperty, false);
            _visual.Compositor.RequestCompositionUpdate(() =>
            {
                if (_disposed || version != _version || !_closing)
                    return;
                UpdateOrigin(_child!, _visual!);
                Play(opening: false);
                _closeTimer = DispatcherTimer.RunOnce(() =>
                {
                    if (version == _version && _closing)
                        CloseImmediately();
                }, CloseDuration, DispatcherPriority.Normal);
            });
        }

        private void Play(bool opening, bool initial = false)
        {
            var visual = _visual!;
            var easing = opening ? (Easing)new CubicEaseOut() : new CubicEaseIn();
            var duration = opening ? GetOpenAnimationDuration(_popup) : CloseDuration;
            var scale = visual.Compositor.CreateStableVector3DKeyFrameAnimation();
            scale.Target = nameof(CompositionVisual.Scale);
            scale.Duration = duration;
            scale.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            var opacity = visual.Compositor.CreateStableScalarKeyFrameAnimation();
            opacity.Target = nameof(CompositionVisual.Opacity);
            opacity.Duration = duration;
            opacity.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            if (initial)
            {
                scale.InsertKeyFrame(0, _collapsedScale, easing);
                opacity.InsertKeyFrame(0, 0, easing);
            }
            else
            {
                // StartingValue is sampled on the render thread, including interrupted motion.
                scale.InsertExpressionKeyFrame(0, "this.StartingValue", easing);
                opacity.InsertExpressionKeyFrame(0, "this.StartingValue", easing);
            }
            scale.InsertKeyFrame(1, opening ? RestingScale : _collapsedScale, easing);
            opacity.InsertKeyFrame(1, opening ? _restingOpacity : 0, easing);
            var group = visual.Compositor.CreateAnimationGroup();
            group.Add(scale);
            group.Add(opacity);
            visual.StartAnimationGroup(group);
        }

        private void CloseImmediately()
        {
            _allowClose = true;
            try
            {
                _popup.Close();
            }
            finally
            {
                _allowClose = false;
                Reset();
            }
        }

        private void StopWaitingForLayout()
        {
            _openingPending = false;
            _compositionQueued = false;
            if (_child is not null)
            {
                _child.Loaded -= OnReady;
                _child.LayoutUpdated -= OnLayoutUpdated;
            }
        }

        private void RestoreInput()
        {
            _hitTestHold?.Dispose();
            _hitTestHold = null;
            _enabledHold?.Dispose();
            _enabledHold = null;
        }

        private void Reset()
        {
            _version++;
            StopWaitingForLayout();
            _closeTimer?.Dispose();
            _closeTimer = null;
            _opacityHold?.Dispose();
            _opacityHold = null;
            _targetVisibility?.Dispose();
            _targetVisibility = null;
            if (_target is not null)
                _target.DetachedFromVisualTree -= OnTargetDetached;
            RestoreInput();
            if (_visual is not null)
            {
                _visual.StopAnimation(nameof(CompositionVisual.Scale));
                _visual.StopAnimation(nameof(CompositionVisual.Opacity));
                _visual.Scale = RestingScale;
                _visual.Opacity = _restingOpacity;
                _visual.CenterPoint = default;
                _visual = null;
            }
            if (_child is not null)
            {
                _child.DetachedFromVisualTree -= OnDetached;
                _child = null;
            }
            _target = null;
            _closing = false;
        }

        public void Dispose()
        {
            _disposed = true;
            if (_closing)
                CloseImmediately();
            Reset();
            _popup.Opened -= OnOpened;
            _popup.Closed -= OnClosed;
            _popup.PropertyChanged -= OnPopupPropertyChanged;
            RemoveClosing(_popup, OnClosing);
        }
    }
}
