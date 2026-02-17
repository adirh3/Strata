using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public class StrataChatShell : TemplatedControl
{
    private Border? _presenceDot;
    private ScrollViewer? _scrollViewer;
    private bool _userScrolledAway;
    private bool _isAutoScrolling;

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Header));

    public static readonly StyledProperty<object?> TranscriptProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Transcript));

    public static readonly StyledProperty<object?> ComposerProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Composer));

    public static readonly StyledProperty<bool> IsOnlineProperty =
        AvaloniaProperty.Register<StrataChatShell, bool>(nameof(IsOnline), true);

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
            _scrollViewer.PointerWheelChanged += OnUserWheelScroll;
        }

        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Loaded);
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
    /// </summary>
    public void ScrollToEnd()
    {
        if (_scrollViewer is null || _userScrolledAway)
            return;

        _isAutoScrolling = true;
        _scrollViewer.ScrollToEnd();
        Dispatcher.UIThread.Post(() => _isAutoScrolling = false, DispatcherPriority.Loaded);
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
        if (_scrollViewer is null || _isAutoScrolling)
            return;

        // If user scrolled up more than ~40px from bottom, mark as scrolled away
        var distanceFromBottom = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - _scrollViewer.Offset.Y;
        if (distanceFromBottom > 40)
            _userScrolledAway = true;
        else
            _userScrolledAway = false;
    }

    private void OnUserWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        // Any upward wheel scroll = user wants to read history
        if (e.Delta.Y > 0)
            _userScrolledAway = true;
    }
}
