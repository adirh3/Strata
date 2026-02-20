using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Rich-content artifact canvas for AI chat integration. Hosts arbitrary
/// controls (code, documents, diagrams, checklists) alongside a conversation
/// surface with a live stream rail, version navigation, tab strip, and
/// Strata-signature visual language.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataCanvas Title="Generated Code"
///                        Subtitle="Python · 42 lines"
///                        IsGenerating="True"
///                        Version="2" VersionCount="3"&gt;
///     &lt;controls:StrataCanvas.ToolBar&gt;
///         &lt;Button Classes="subtle" Content="Copy" /&gt;
///     &lt;/controls:StrataCanvas.ToolBar&gt;
///     &lt;controls:StrataCanvas.Content&gt;
///         &lt;TextBlock Text="print('hello')" /&gt;
///     &lt;/controls:StrataCanvas.Content&gt;
/// &lt;/controls:StrataCanvas&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_StreamTrack (Border),
/// PART_StreamBar (Border), PART_StatusDot (Border), PART_CloseButton (Button),
/// PART_PrevButton (Button), PART_NextButton (Button), PART_ContentHost (Border).</para>
/// <para><b>Pseudo-classes:</b> :open, :closed, :generating, :ready, :has-subtitle,
/// :has-toolbar, :has-icon, :has-footer, :has-versions.</para>
/// </remarks>
public class StrataCanvas : TemplatedControl
{
    private Border? _root;
    private Border? _streamTrack;
    private Border? _streamBar;
    private Border? _statusDot;
    private Button? _closeButton;
    private Button? _prevButton;
    private Button? _nextButton;

    /// <summary>Title displayed in the canvas title bar.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataCanvas, string>(nameof(Title), string.Empty);

    /// <summary>Optional subtitle shown next to the title (e.g. file type, line count).</summary>
    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<StrataCanvas, string>(nameof(Subtitle), string.Empty);

    /// <summary>Optional icon content displayed before the title.</summary>
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(Icon));

    /// <summary>The main content hosted inside the canvas.</summary>
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(Content));

    /// <summary>Optional toolbar content displayed in the title bar (right-aligned).</summary>
    public static readonly StyledProperty<object?> ToolBarProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(ToolBar));

    /// <summary>Optional footer content displayed below the main content.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(Footer));

    /// <summary>Whether the canvas is open and visible.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<StrataCanvas, bool>(nameof(IsOpen), true);

    /// <summary>Whether content is actively being generated. Drives the stream rail animation.</summary>
    public static readonly StyledProperty<bool> IsGeneratingProperty =
        AvaloniaProperty.Register<StrataCanvas, bool>(nameof(IsGenerating));

    /// <summary>Current version number (1-based). Shown in version nav when VersionCount &gt; 1.</summary>
    public static readonly StyledProperty<int> VersionProperty =
        AvaloniaProperty.Register<StrataCanvas, int>(nameof(Version), 1);

    /// <summary>Total number of artifact versions. Set &gt; 1 to show version navigation.</summary>
    public static readonly StyledProperty<int> VersionCountProperty =
        AvaloniaProperty.Register<StrataCanvas, int>(nameof(VersionCount), 1);

    /// <summary>Whether to show the built-in close button in the title bar.</summary>
    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<StrataCanvas, bool>(nameof(ShowCloseButton), true);

    /// <summary>Raised when the close button is clicked.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
        RoutedEvent.Register<StrataCanvas, RoutedEventArgs>(nameof(CloseRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user navigates to the previous version.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> PreviousVersionRequestedEvent =
        RoutedEvent.Register<StrataCanvas, RoutedEventArgs>(nameof(PreviousVersionRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user navigates to the next version.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> NextVersionRequestedEvent =
        RoutedEvent.Register<StrataCanvas, RoutedEventArgs>(nameof(NextVersionRequested), RoutingStrategies.Bubble);

    static StrataCanvas()
    {
        IsOpenProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.OnIsOpenChanged());
        IsGeneratingProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.OnGeneratingChanged());
        SubtitleProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        ToolBarProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        IconProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        FooterProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        VersionCountProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        VersionProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdateVersionButtons());
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public object? ToolBar { get => GetValue(ToolBarProperty); set => SetValue(ToolBarProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public bool IsGenerating { get => GetValue(IsGeneratingProperty); set => SetValue(IsGeneratingProperty, value); }
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public int VersionCount { get => GetValue(VersionCountProperty); set => SetValue(VersionCountProperty, value); }
    public bool ShowCloseButton { get => GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }

    public event EventHandler<RoutedEventArgs>? CloseRequested
    { add => AddHandler(CloseRequestedEvent, value); remove => RemoveHandler(CloseRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? PreviousVersionRequested
    { add => AddHandler(PreviousVersionRequestedEvent, value); remove => RemoveHandler(PreviousVersionRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? NextVersionRequested
    { add => AddHandler(NextVersionRequestedEvent, value); remove => RemoveHandler(NextVersionRequestedEvent, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _root = e.NameScope.Find<Border>("PART_Root");
        _streamTrack = e.NameScope.Find<Border>("PART_StreamTrack");
        _streamBar = e.NameScope.Find<Border>("PART_StreamBar");
        _statusDot = e.NameScope.Find<Border>("PART_StatusDot");
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        _prevButton = e.NameScope.Find<Button>("PART_PrevButton");
        _nextButton = e.NameScope.Find<Button>("PART_NextButton");

        if (_closeButton is not null)
            _closeButton.Click += (_, ev) =>
            {
                ev.Handled = true;
                IsOpen = false;
                RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
            };

        if (_prevButton is not null)
            _prevButton.Click += (_, ev) =>
            {
                ev.Handled = true;
                if (Version > 1)
                {
                    Version--;
                    RaiseEvent(new RoutedEventArgs(PreviousVersionRequestedEvent));
                }
            };

        if (_nextButton is not null)
            _nextButton.Click += (_, ev) =>
            {
                ev.Handled = true;
                if (Version < VersionCount)
                {
                    Version++;
                    RaiseEvent(new RoutedEventArgs(NextVersionRequestedEvent));
                }
            };

        if (_streamTrack is not null)
            _streamTrack.SizeChanged += (_, _) =>
            {
                if (IsGenerating)
                    StartStreamAnimation();
            };

        UpdatePseudoClasses();
        UpdateVersionButtons();

        Dispatcher.UIThread.Post(() =>
        {
            if (IsGenerating)
            {
                StartStreamAnimation();
                StartStatusPulse();
            }
            else
            {
                HideStreamBar();
            }
        }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsOpen)
        {
            e.Handled = true;
            IsOpen = false;
            RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
        }
        else if (e.Key == Key.Left && VersionCount > 1 && Version > 1)
        {
            e.Handled = true;
            Version--;
            RaiseEvent(new RoutedEventArgs(PreviousVersionRequestedEvent));
        }
        else if (e.Key == Key.Right && VersionCount > 1 && Version < VersionCount)
        {
            e.Handled = true;
            Version++;
            RaiseEvent(new RoutedEventArgs(NextVersionRequestedEvent));
        }
    }

    private void OnIsOpenChanged()
    {
        UpdatePseudoClasses();
        if (IsOpen)
            AnimateOpen();
    }

    private void OnGeneratingChanged()
    {
        UpdatePseudoClasses();
        if (IsGenerating)
        {
            StartStreamAnimation();
            StartStatusPulse();
        }
        else
        {
            HideStreamBar();
            StopStatusPulse();
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":open", IsOpen);
        PseudoClasses.Set(":closed", !IsOpen);
        PseudoClasses.Set(":generating", IsGenerating);
        PseudoClasses.Set(":ready", !IsGenerating);
        PseudoClasses.Set(":has-subtitle", !string.IsNullOrWhiteSpace(Subtitle));
        PseudoClasses.Set(":has-toolbar", ToolBar is not null);
        PseudoClasses.Set(":has-icon", Icon is not null);
        PseudoClasses.Set(":has-footer", Footer is not null);
        PseudoClasses.Set(":has-versions", VersionCount > 1);
    }

    private void UpdateVersionButtons()
    {
        if (_prevButton is not null)
            _prevButton.IsEnabled = Version > 1;
        if (_nextButton is not null)
            _nextButton.IsEnabled = Version < VersionCount;
    }

    private void AnimateOpen()
    {
        if (_root is null)
            return;

        var visual = ElementComposition.GetElementVisual(_root);
        if (visual is null)
            return;

        var comp = visual.Compositor;

        // Slide + scale entrance from offset
        visual.CenterPoint = new Avalonia.Vector3D(
            _root.Bounds.Width / 2,
            _root.Bounds.Height / 2, 0);

        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.96f, 0.96f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(350);

        var offsetAnim = comp.CreateVector3KeyFrameAnimation();
        offsetAnim.Target = "Offset";
        offsetAnim.InsertKeyFrame(0f, new Vector3(16f, 0f, 0f));
        offsetAnim.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f));
        offsetAnim.Duration = TimeSpan.FromMilliseconds(350);

        var fadeAnim = comp.CreateScalarKeyFrameAnimation();
        fadeAnim.Target = "Opacity";
        fadeAnim.InsertKeyFrame(0f, 0f);
        fadeAnim.InsertKeyFrame(1f, 1f);
        fadeAnim.Duration = TimeSpan.FromMilliseconds(280);

        visual.StartAnimation("Scale", scaleAnim);
        visual.StartAnimation("Offset", offsetAnim);
        visual.StartAnimation("Opacity", fadeAnim);
    }

    private void StartStreamAnimation()
    {
        if (_streamBar is null || _streamTrack is null)
            return;

        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null)
            return;

        var trackWidth = _streamTrack.Bounds.Width;
        if (trackWidth < 10)
            trackWidth = Math.Max(220, Bounds.Width - 40);

        var barWidth = _streamBar.Bounds.Width;
        if (barWidth < 4)
            barWidth = 80;

        var comp = visual.Compositor;

        var offset = comp.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        offset.InsertKeyFrame(0f, new Vector3((float)-barWidth, 0f, 0f));
        offset.InsertKeyFrame(1f, new Vector3((float)trackWidth, 0f, 0f));
        offset.Duration = TimeSpan.FromMilliseconds(1200);
        offset.IterationBehavior = AnimationIterationBehavior.Forever;

        var opacity = comp.CreateScalarKeyFrameAnimation();
        opacity.Target = "Opacity";
        opacity.InsertKeyFrame(0f, 0.2f);
        opacity.InsertKeyFrame(0.4f, 0.95f);
        opacity.InsertKeyFrame(1f, 0.2f);
        opacity.Duration = TimeSpan.FromMilliseconds(1200);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation("Offset", offset);
        visual.StartAnimation("Opacity", opacity);
    }

    private void HideStreamBar()
    {
        if (_streamBar is null)
            return;

        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null)
            return;

        visual.Opacity = 0;
    }

    private void StartStatusPulse()
    {
        if (_statusDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_statusDot);
        if (visual is null)
            return;

        var comp = visual.Compositor;
        var anim = comp.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.3f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(1400);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopStatusPulse()
    {
        if (_statusDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_statusDot);
        if (visual is null)
            return;

        var comp = visual.Compositor;
        var reset = comp.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }
}
