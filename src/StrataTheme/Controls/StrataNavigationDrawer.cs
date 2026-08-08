using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// A Material-style navigation drawer that tracks the finger.
///
/// <para>The point of this control is the drag. A drawer that only toggles — however well animated —
/// reads as a menu that happens to slide, because the panel never responds to the hand that is
/// moving it. Following the finger one-to-one, dimming the scrim in proportion, and letting a flick
/// finish the movement is the whole difference between "a sidebar on a phone" and a navigation
/// drawer.</para>
///
/// <para>Layout is deliberately not a <c>Panel</c> with a translated child: the panel is offset via
/// its own <see cref="Visual.RenderTransform"/> so that dragging never invalidates layout, which is
/// what keeps the movement smooth while a large chat transcript is mounted behind it.</para>
/// </summary>
/// <remarks>
/// Template parts: <c>PART_Scrim</c> (dim layer, fades with progress), <c>PART_Panel</c> (the
/// sliding surface). Pseudo-classes: <c>:open</c> when fully open, <c>:dragging</c> while the
/// finger is down.
/// </remarks>
public class StrataNavigationDrawer : ContentControl
{
    /// <summary>Whether the drawer is open. Two-way: a drag or a scrim tap writes back.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<StrataNavigationDrawer, bool>(
            nameof(IsOpen), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The drawer surface itself.</summary>
    public static readonly StyledProperty<object?> PanelProperty =
        AvaloniaProperty.Register<StrataNavigationDrawer, object?>(nameof(Panel));

    public static readonly StyledProperty<IDataTemplate?> PanelTemplateProperty =
        AvaloniaProperty.Register<StrataNavigationDrawer, IDataTemplate?>(nameof(PanelTemplate));

    /// <summary>How wide the drawer is when open, in DIPs.</summary>
    public static readonly StyledProperty<double> PanelWidthProperty =
        AvaloniaProperty.Register<StrataNavigationDrawer, double>(nameof(PanelWidth), 320d);

    /// <summary>Whether dragging is available at all. False when the drawer is docked open.</summary>
    public static readonly StyledProperty<bool> IsDragEnabledProperty =
        AvaloniaProperty.Register<StrataNavigationDrawer, bool>(nameof(IsDragEnabled), true);

    /// <summary>Opacity of the scrim at full open.</summary>
    public static readonly StyledProperty<double> ScrimOpacityProperty =
        AvaloniaProperty.Register<StrataNavigationDrawer, double>(nameof(ScrimOpacity), 0.55);

    /// <summary>
    /// 0 when closed, 1 when fully open. Driven continuously by the drag so the scrim and any
    /// host-side affordance can track it.
    /// </summary>
    public static readonly DirectProperty<StrataNavigationDrawer, double> ProgressProperty =
        AvaloniaProperty.RegisterDirect<StrataNavigationDrawer, double>(
            nameof(Progress), owner => owner.Progress);

    /// <summary>Velocity past which a flick decides the outcome regardless of position, DIPs/second.</summary>
    private const double FlingVelocity = 420;

    /// <summary>Position past which a release settles open, as a fraction of the panel width.</summary>
    private const double SettleFraction = 0.5;

    private static readonly TimeSpan SettleDuration = TimeSpan.FromMilliseconds(220);

    private readonly EdgeDragGestureRecognizer _drag = new();
    private Border? _scrim;
    private ContentPresenter? _panel;
    private double _progress;
    private bool _dragging;
    private DispatcherTimer? _settleTimer;
    private double _settleFrom;
    private double _settleTo;
    private DateTime _settleStart;
    private readonly IEasing _settleEasing = new CubicEaseOut();

    public StrataNavigationDrawer()
    {
        GestureRecognizers.Add(_drag);
        AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, OnDrag);
        AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, OnDragEnded);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public object? Panel
    {
        get => GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    public IDataTemplate? PanelTemplate
    {
        get => GetValue(PanelTemplateProperty);
        set => SetValue(PanelTemplateProperty, value);
    }

    public double PanelWidth
    {
        get => GetValue(PanelWidthProperty);
        set => SetValue(PanelWidthProperty, value);
    }

    public bool IsDragEnabled
    {
        get => GetValue(IsDragEnabledProperty);
        set => SetValue(IsDragEnabledProperty, value);
    }

    public double ScrimOpacity
    {
        get => GetValue(ScrimOpacityProperty);
        set => SetValue(ScrimOpacityProperty, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetAndRaise(ProgressProperty, ref _progress, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scrim is not null)
            _scrim.PointerPressed -= OnScrimPressed;

        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        _panel = e.NameScope.Find<ContentPresenter>("PART_Panel");
        UpdateDirection();

        if (_scrim is not null)
            _scrim.PointerPressed += OnScrimPressed;

        ApplyProgress(IsOpen ? 1 : 0, animate: false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty)
        {
            _drag.IsOpen = IsOpen;

            // A drag writes IsOpen at the end of its own settle, so re-animating from here would
            // restart the movement the user just finished.
            if (!_dragging)
                AnimateTo(IsOpen ? 1 : 0);
        }
        else if (change.Property == IsDragEnabledProperty)
        {
            _drag.IsEnabled = IsDragEnabled;
            if (IsDragEnabled)
            {
                if (!GestureRecognizers.Contains(_drag))
                    GestureRecognizers.Add(_drag);
            }
            else
            {
                var settleOpen = _progress >= SettleFraction;
                _drag.Cancel();
                _dragging = false;
                PseudoClasses.Set(":dragging", false);
                AnimateTo(settleOpen ? 1 : 0);
                IsOpen = settleOpen;
            }
        }

        else if (change.Property == PanelWidthProperty)
        {
            ApplyProgress(_progress, animate: false);
        }
        else if (change.Property == FlowDirectionProperty)
        {
            UpdateDirection();
            ApplyProgress(_progress, animate: false);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopSettle();
        _drag.Cancel();
        _dragging = false;
        PseudoClasses.Set(":dragging", false);
        ApplyProgress(IsOpen ? 1 : 0, animate: false);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        IsOpen = false;
        e.Handled = true;
    }

    private void OnDrag(object? sender, EdgeDragEventArgs e)
    {
        StopSettle();
        _dragging = true;
        PseudoClasses.Set(":dragging", true);

        var width = Math.Max(1, PanelWidth);
        ApplyProgress(Math.Clamp(_progress + e.Delta / width, 0, 1), animate: false);
        e.Handled = true;
    }

    private void OnDragEnded(object? sender, EdgeDragEndedEventArgs e)
    {
        _dragging = false;
        PseudoClasses.Set(":dragging", false);

        // A deliberate flick beats position: releasing at 20% while still moving right clearly
        // means "open", and requiring the user to drag past halfway would feel unresponsive.
        var open = Math.Abs(e.Velocity) > FlingVelocity
            ? e.Velocity > 0
            : _progress >= SettleFraction;

        AnimateTo(open ? 1 : 0);

        // Write back after deciding, so the property change handler does not re-drive the animation.
        IsOpen = open;
        e.Handled = true;
    }

    private void AnimateTo(double target)
    {
        StopSettle();
        if (Math.Abs(_progress - target) < 0.001)
        {
            ApplyProgress(target, animate: false);
            return;
        }

        _settleFrom = _progress;
        _settleTo = target;
        _settleStart = DateTime.UtcNow;

        if (_settleTimer is null)
        {
            _settleTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(8)
            };
            _settleTimer.Tick += OnSettleTick;
        }

        _settleTimer.Start();
    }

    private void OnSettleTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _settleStart;
        var t = Math.Clamp(elapsed.TotalMilliseconds / SettleDuration.TotalMilliseconds, 0, 1);
        var eased = _settleEasing.Ease(t);

        ApplyProgress(_settleFrom + (_settleTo - _settleFrom) * eased, animate: false);

        if (t >= 1)
            StopSettle();
    }

    private void StopSettle() => _settleTimer?.Stop();

    private void ApplyProgress(double progress, bool animate)
    {
        Progress = progress;

        if (_panel is not null)
        {
            var width = Math.Max(1, PanelWidth);
            var direction = FlowDirection == Avalonia.Media.FlowDirection.RightToLeft ? 1 : -1;
            _panel.RenderTransform = new TranslateTransform(direction * width * (1 - progress), 0);
            _panel.IsVisible = progress > 0.001;
        }

        if (_scrim is not null)
        {
            _scrim.Opacity = ScrimOpacity * progress;

            // The scrim must not swallow taps meant for the conversation while it is invisible.
            _scrim.IsVisible = progress > 0.001;
            _scrim.IsHitTestVisible = progress > 0.5;
        }

        PseudoClasses.Set(":open", progress > 0.999);
    }

    private void UpdateDirection()
    {
        var isRightToLeft = FlowDirection == Avalonia.Media.FlowDirection.RightToLeft;
        _drag.IsRightToLeft = isRightToLeft;
        if (_panel is not null)
            _panel.HorizontalAlignment = isRightToLeft
                ? Avalonia.Layout.HorizontalAlignment.Right
                : Avalonia.Layout.HorizontalAlignment.Left;
    }
}
