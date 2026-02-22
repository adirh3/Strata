using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Full chat shell: a scrollable transcript area stacked above a docked composer,
/// with an optional header row and presence indicator. Provides smart auto-scroll
/// that pauses when the user scrolls up to read history.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataChatShell IsOnline="True" PresenceText="Online"&gt;
///     &lt;controls:StrataChatShell.Header&gt;
///         &lt;TextBlock Text="Support Chat" FontWeight="Bold" /&gt;
///     &lt;/controls:StrataChatShell.Header&gt;
///     &lt;controls:StrataChatShell.Transcript&gt;
///         &lt;StackPanel Spacing="12"&gt;
///             &lt;!-- StrataChatMessage items here --&gt;
///         &lt;/StackPanel&gt;
///     &lt;/controls:StrataChatShell.Transcript&gt;
///     &lt;controls:StrataChatShell.Composer&gt;
///         &lt;controls:StrataChatComposer /&gt;
///     &lt;/controls:StrataChatShell.Composer&gt;
/// &lt;/controls:StrataChatShell&gt;
/// </code>
/// <para>Call <see cref="ScrollToEnd"/> during streaming to auto-scroll.
/// Call <see cref="ResetAutoScroll"/> when the user sends a new message.</para>
/// <para><b>Template parts:</b> PART_Root (Border), PART_TranscriptScroll (ScrollViewer),
/// PART_HeaderPresenter (ContentPresenter), PART_PresenceDot (Border), PART_PresencePill (Border).</para>
/// <para><b>Pseudo-classes:</b> :online, :offline, :has-header, :has-presence.</para>
/// </remarks>
public class StrataChatShell : TemplatedControl
{
    private Border? _presenceDot;
    private ScrollViewer? _scrollViewer;
    private bool _userScrolledAway;
    private bool _isProgrammaticScroll;

    /// <summary>Optional header content displayed at the top of the shell.</summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Header));

    /// <summary>Scrollable transcript content (typically a StackPanel of StrataChatMessage).</summary>
    public static readonly StyledProperty<object?> TranscriptProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Transcript));

    /// <summary>Composer content docked at the bottom (typically a StrataChatComposer).</summary>
    public static readonly StyledProperty<object?> ComposerProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Composer));

    /// <summary>Whether the agent/service is online. Drives the presence dot colour.</summary>
    public static readonly StyledProperty<bool> IsOnlineProperty =
        AvaloniaProperty.Register<StrataChatShell, bool>(nameof(IsOnline), true);

    /// <summary>Text shown next to the presence dot (e.g. "Online", "Away").</summary>
    public static readonly StyledProperty<string> PresenceTextProperty =
        AvaloniaProperty.Register<StrataChatShell, string>(nameof(PresenceText), string.Empty);

    static StrataChatShell()
    {
        IsOnlineProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.Refresh());
        HeaderProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.Refresh());
        PresenceTextProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.Refresh());
    }

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? Transcript { get => GetValue(TranscriptProperty); set => SetValue(TranscriptProperty, value); }
    public object? Composer { get => GetValue(ComposerProperty); set => SetValue(ComposerProperty, value); }
    public bool IsOnline { get => GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
    public string PresenceText { get => GetValue(PresenceTextProperty); set => SetValue(PresenceTextProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _presenceDot = e.NameScope.Find<Border>("PART_PresenceDot");
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_TranscriptScroll");

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            // Detect user scroll-away intent (don't intercept — let ScrollViewer handle natively)
            _scrollViewer.PointerWheelChanged += OnUserWheelScroll;

            // Set up compositor-driven smooth scrolling after template is realized
            var scrollContent = e.NameScope.Find<Panel>("PART_ScrollContent");
            if (scrollContent is not null)
                Dispatcher.UIThread.Post(() => SetupCompositorSmoothing(scrollContent), DispatcherPriority.Loaded);
        }

        Refresh();
    }

    /// <summary>
    /// Applies an implicit composition animation on the scroll content's visual Offset,
    /// so every scroll position change is smoothly animated on the compositor/render thread
    /// at full vsync rate — independent of UI thread load.
    /// </summary>
    private void SetupCompositorSmoothing(Control scrollContent)
    {
        var visual = ElementComposition.GetElementVisual(scrollContent);
        if (visual is null) return;

        var compositor = visual.Compositor;
        var anim = compositor.CreateVector3KeyFrameAnimation();
        anim.Target = "Offset";
        anim.InsertExpressionKeyFrame(1f, "this.FinalValue");
        anim.Duration = TimeSpan.FromMilliseconds(120);

        var implicitAnims = compositor.CreateImplicitAnimationCollection();
        implicitAnims["Offset"] = anim;
        visual.ImplicitAnimations = implicitAnims;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        PseudoClasses.Set(":online", IsOnline);
        PseudoClasses.Set(":offline", !IsOnline);
        PseudoClasses.Set(":has-header", Header is not null);
        PseudoClasses.Set(":has-presence", !string.IsNullOrWhiteSpace(PresenceText));

        if (IsOnline && !string.IsNullOrWhiteSpace(PresenceText))
            StartPresencePulse();
        else
            StopPresencePulse();
    }

    private void StartPresencePulse()
    {
        if (_presenceDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_presenceDot);
        if (visual is null)
            return;

        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.45f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(1500);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopPresencePulse()
    {
        if (_presenceDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_presenceDot);
        if (visual is null)
            return;

        var reset = visual.Compositor.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }

    /// <summary>
    /// Scrolls transcript to the bottom. Call this during streaming/generation.
    /// Respects user scroll-away: if the user scrolled up, this is a no-op
    /// until <see cref="ResetAutoScroll"/> is called.
    /// The compositor implicit animation handles visual smoothing.
    /// </summary>
    public void ScrollToEnd()
    {
        if (_scrollViewer is null || _userScrolledAway)
            return;

        _isProgrammaticScroll = true;
        _scrollViewer.ScrollToEnd();
        Dispatcher.UIThread.Post(() => _isProgrammaticScroll = false, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Resets the user-scrolled-away flag so auto-scroll resumes.
    /// Call this when the user sends a new message.
    /// </summary>
    public void ResetAutoScroll()
    {
        _userScrolledAway = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || _isProgrammaticScroll)
            return;

        var distanceFromBottom = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - _scrollViewer.Offset.Y;
        _userScrolledAway = distanceFromBottom > 40;
    }

    private void OnUserWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        // Don't intercept — let ScrollViewer handle natively (especially for touchpad).
        // Just detect scroll-away intent so auto-scroll pauses during streaming.
        if (e.Delta.Y > 0)
            _userScrolledAway = true;
    }
}
