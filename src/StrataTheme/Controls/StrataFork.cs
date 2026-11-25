using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// StrataFork presents two alternatives (A/B). When it is wide enough it shows them side by
/// side under their own headings, so they can be compared at a glance; when narrow it shows
/// one at a time behind a segmented switch whose thumb slides to the chosen option.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataFork OptionATitle="Plan A" OptionBTitle="Plan B" SelectedIndex="0"&gt;
///     &lt;controls:StrataFork.OptionAContent&gt;&lt;TextBlock Text="Conservative approach" /&gt;&lt;/controls:StrataFork.OptionAContent&gt;
///     &lt;controls:StrataFork.OptionBContent&gt;&lt;TextBlock Text="Aggressive refactor" /&gt;&lt;/controls:StrataFork.OptionBContent&gt;
/// &lt;/controls:StrataFork&gt;
/// </code>
/// <para><b>Template parts:</b> PART_TabHost (Border), PART_Indicator (Border),
/// PART_OptionA (Button), PART_OptionB (Button),
/// PART_OptionAContent (ContentPresenter), PART_OptionBContent (ContentPresenter).</para>
/// <para><b>Pseudo-classes:</b> :a, :b, :split (both options shown side by side).</para>
/// </remarks>
public class StrataFork : TemplatedControl
{
    private const double SlideDistance = 14;
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(220);

    private Border? _tabHost;
    private Border? _indicator;
    private Button? _optionAButton;
    private Button? _optionBButton;
    private ContentPresenter? _optionAPresenter;
    private ContentPresenter? _optionBPresenter;
    private int _transitionVersion;

    public static readonly StyledProperty<string> OptionATitleProperty =
        AvaloniaProperty.Register<StrataFork, string>(nameof(OptionATitle), "Draft A");

    public static readonly StyledProperty<string> OptionBTitleProperty =
        AvaloniaProperty.Register<StrataFork, string>(nameof(OptionBTitle), "Draft B");

    public static readonly StyledProperty<object?> OptionAContentProperty =
        AvaloniaProperty.Register<StrataFork, object?>(nameof(OptionAContent));

    public static readonly StyledProperty<object?> OptionBContentProperty =
        AvaloniaProperty.Register<StrataFork, object?>(nameof(OptionBContent));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<StrataFork, int>(nameof(SelectedIndex), 0);

    /// <summary>
    /// The width from which both options are shown side by side. Set it to
    /// <see cref="double.PositiveInfinity"/> to always show one option at a time.
    /// </summary>
    public static readonly StyledProperty<double> SideBySideMinWidthProperty =
        AvaloniaProperty.Register<StrataFork, double>(nameof(SideBySideMinWidth), 520);

    static StrataFork()
    {
        SelectedIndexProperty.Changed.AddClassHandler<StrataFork>((fork, _) => fork.OnSelectedIndexChanged());
        SideBySideMinWidthProperty.Changed.AddClassHandler<StrataFork>((fork, _) => fork.UpdateLayoutMode());
    }

    public string OptionATitle
    {
        get => GetValue(OptionATitleProperty);
        set => SetValue(OptionATitleProperty, value);
    }

    public string OptionBTitle
    {
        get => GetValue(OptionBTitleProperty);
        set => SetValue(OptionBTitleProperty, value);
    }

    public object? OptionAContent
    {
        get => GetValue(OptionAContentProperty);
        set => SetValue(OptionAContentProperty, value);
    }

    public object? OptionBContent
    {
        get => GetValue(OptionBContentProperty);
        set => SetValue(OptionBContentProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value < 0 ? 0 : value > 1 ? 1 : value);
    }

    public double SideBySideMinWidth
    {
        get => GetValue(SideBySideMinWidthProperty);
        set => SetValue(SideBySideMinWidthProperty, value);
    }

    /// <summary>Whether both options are currently shown side by side.</summary>
    public bool IsSideBySide { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        // Unsubscribe from old handlers before reapplying template
        if (_optionAButton is not null)
            _optionAButton.Click -= OnOptionAClicked;
        if (_optionBButton is not null)
            _optionBButton.Click -= OnOptionBClicked;
        if (_tabHost is not null)
            _tabHost.SizeChanged -= OnTabHostSizeChanged;

        base.OnApplyTemplate(e);

        _tabHost = e.NameScope.Find<Border>("PART_TabHost");
        _indicator = e.NameScope.Find<Border>("PART_Indicator");
        _optionAButton = e.NameScope.Find<Button>("PART_OptionA");
        _optionBButton = e.NameScope.Find<Button>("PART_OptionB");
        _optionAPresenter = e.NameScope.Find<ContentPresenter>("PART_OptionAContent");
        _optionBPresenter = e.NameScope.Find<ContentPresenter>("PART_OptionBContent");

        if (_optionAButton is not null)
            _optionAButton.Click += OnOptionAClicked;

        if (_optionBButton is not null)
            _optionBButton.Click += OnOptionBClicked;

        if (_tabHost is not null)
            _tabHost.SizeChanged += OnTabHostSizeChanged;

        Dispatcher.UIThread.Post(ResyncVisualState, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The same instance can be re-parented (e.g. StrataMarkdown caches the fork for a
        // ```comparison``` block and re-adds it when the transcript re-renders). A plain
        // re-attach does NOT re-apply the template, so the tab buttons keep the click handlers
        // wired in OnApplyTemplate. Re-sync the visual state in case it was left mid-transition.
        Dispatcher.UIThread.Post(ResyncVisualState, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Deliberately do NOT unsubscribe the button / size-changed handlers here. The buttons
        // are template children that share this control's lifetime (so there is no leak), and
        // OnApplyTemplate is not called again on re-attach -- unsubscribing here permanently
        // killed the tab buttons after the first re-render, making the second option unclickable.
        _transitionVersion++;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged)
            UpdateLayoutMode();
    }

    private void ResyncVisualState()
    {
        UpdatePseudoClasses();
        UpdateLayoutMode();
        UpdateIndicatorGeometry(animated: false);
        ApplyContentStateInstant();
    }

    private void OnOptionAClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SelectedIndex = 0;

    private void OnOptionBClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SelectedIndex = 1;

    private void OnTabHostSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateIndicatorGeometry(animated: false);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var rtl = FlowDirection == FlowDirection.RightToLeft;
        if (e.Key == Key.Left)
        {
            e.Handled = true;
            SelectedIndex = rtl ? 1 : 0;
        }
        else if (e.Key == Key.Right)
        {
            e.Handled = true;
            SelectedIndex = rtl ? 0 : 1;
        }
    }

    private void OnSelectedIndexChanged()
    {
        UpdatePseudoClasses();
        UpdateIndicatorGeometry(animated: true);
        TransitionContent();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":a", SelectedIndex == 0);
        PseudoClasses.Set(":b", SelectedIndex == 1);
    }

    private void UpdateLayoutMode()
    {
        var width = Bounds.Width;
        var split = width > 0 && width >= SideBySideMinWidth;
        if (split == IsSideBySide)
            return;

        IsSideBySide = split;
        PseudoClasses.Set(":split", split);
        _transitionVersion++;
        ApplyContentStateInstant();
        Dispatcher.UIThread.Post(() => UpdateIndicatorGeometry(animated: false), DispatcherPriority.Loaded);
    }

    private void UpdateIndicatorGeometry(bool animated)
    {
        if (_indicator?.Parent is not Control track || IsSideBySide)
            return;

        var trackWidth = track.Bounds.Width;
        if (trackWidth < 2)
            return;

        var half = Math.Max(0, trackWidth / 2.0);
        _indicator.Width = half;

        // A render transform, not the composition offset: layout re-arranges the thumb and would put that back.
        var target = TransformOperations.Parse(FormattableString.Invariant($"translateX({(SelectedIndex == 0 ? 0 : half)}px)"));
        if (!animated)
        {
            _indicator.Transitions = null;
            _indicator.RenderTransform = target;
            return;
        }

        _indicator.Transitions ??=
        [
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new SplineEasing(0.2, 0, 0, 1),
            },
        ];
        _indicator.RenderTransform = target;
    }

    private void ApplyContentStateInstant()
    {
        if (_optionAPresenter is null || _optionBPresenter is null)
            return;

        var a = SelectedIndex == 0;
        foreach (var presenter in new[] { _optionAPresenter, _optionBPresenter })
        {
            presenter.Transitions = null;
            presenter.Opacity = 1;
            presenter.RenderTransform = null;
        }
        _optionAPresenter.IsVisible = IsSideBySide || a;
        _optionBPresenter.IsVisible = IsSideBySide || !a;
    }

    // One option slides in from the side it sits on while the other gives way.
    private void TransitionContent()
    {
        if (_optionAPresenter is null || _optionBPresenter is null)
            return;

        var version = ++_transitionVersion;
        if (IsSideBySide || !IsEffectivelyVisible)
        {
            ApplyContentStateInstant();
            return;
        }

        var showA = SelectedIndex == 0;
        var incoming = showA ? _optionAPresenter : _optionBPresenter;
        var outgoing = showA ? _optionBPresenter : _optionAPresenter;
        var from = showA ? -SlideDistance : SlideDistance;

        outgoing.Transitions = null;
        outgoing.IsVisible = false;
        outgoing.Opacity = 1;
        outgoing.RenderTransform = null;

        incoming.Transitions = null;
        incoming.IsVisible = true;
        incoming.Opacity = 0;
        incoming.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateX({from}px)"));
        incoming.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = SlideDuration, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = SlideDuration, Easing = new SplineEasing(0.2, 0, 0, 1) },
        ];

        // Let the start state reach a frame before moving to the end, so the transition has somewhere to start.
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _transitionVersion)
                return;
            incoming.Opacity = 1;
            incoming.RenderTransform = TransformOperations.Identity;
        }, DispatcherPriority.Render);
    }
}
