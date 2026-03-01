using System;
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// Full chat shell: a scrollable transcript area stacked above a docked composer,
/// with an optional header row and presence indicator. Provides smart auto-scroll
/// that pauses when the user scrolls up to read history.
/// Also aligns transcript message roles (User/Assistant/System/Tool) based on the shell flow direction.
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
/// <para><b>Pseudo-classes:</b> :online, :offline, :has-header, :has-presence, :scrolling.</para>
/// </remarks>
public class StrataChatShell : TemplatedControl
{
    private const int AutoScrollInteractionCooldownMs = 220;
    private const int ScrollingVisualCooldownMs = 140;
    private static readonly TimeSpan ProgrammaticScrollMinInterval = TimeSpan.FromMilliseconds(90);
    private const double UserScrollDeltaThreshold = 0.05;
    private const double LayoutShiftDeltaTolerance = 0.2;

    private Border? _presenceDot;
    private ScrollViewer? _scrollViewer;
    private Panel? _transcriptPanel;
    private ItemsControl? _transcriptItemsControl;
    private INotifyCollectionChanged? _transcriptCollection;
    private bool _userScrolledAway;
    private bool _isProgrammaticScroll;
    private bool _alignmentQueued;
    private bool _scrollQueued;
    private DateTime _suspendAutoScrollUntilUtc;
    private DateTime _lastProgrammaticScrollUtc;
    private bool _forceFullAlignment = true;
    private int _lastAlignedMessageCount;
    private bool _isPulseRunning;
    private readonly DispatcherTimer _scrollingStateTimer;
    private DateTime _lastScrollActivityUtc;
    private bool _isTranscriptScrolling;

    /// <summary>Optional header content displayed at the top of the shell.</summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Header));

    /// <summary>
    /// Scrollable transcript content. Use <see cref="StrataChatTranscript"/> for
    /// long conversations to enable dedicated chat virtualization.
    /// </summary>
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
        IsOnlineProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.OnIsOnlineChanged());
        HeaderProperty.Changed.AddClassHandler<StrataChatShell>((c, _) =>
            c.PseudoClasses.Set(":has-header", c.Header is not null));
        PresenceTextProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.OnPresenceTextChanged());
        TranscriptProperty.Changed.AddClassHandler<StrataChatShell>((c, _) =>
        {
            c._lastAlignedMessageCount = 0;
            c.ConfigureAlignmentSubscription();
            c.ScheduleApplyTranscriptAlignment(forceFull: true);
        });
    }

    public StrataChatShell()
    {
        _scrollingStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };
        _scrollingStateTimer.Tick += OnScrollingStateTimerTick;
    }

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? Transcript { get => GetValue(TranscriptProperty); set => SetValue(TranscriptProperty, value); }
    public object? Composer { get => GetValue(ComposerProperty); set => SetValue(ComposerProperty, value); }
    public bool IsOnline { get => GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
    public string PresenceText { get => GetValue(PresenceTextProperty); set => SetValue(PresenceTextProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnUserWheelScroll;
        }

        _presenceDot = e.NameScope.Find<Border>("PART_PresenceDot");
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_TranscriptScroll");

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.PointerWheelChanged += OnUserWheelScroll;
        }

        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);

        ConfigureAlignmentSubscription();

        // Apply all pseudo-classes once; targeted handlers maintain them afterwards.
        var online = IsOnline;
        PseudoClasses.Set(":online", online);
        PseudoClasses.Set(":offline", !online);
        PseudoClasses.Set(":has-header", Header is not null);
        PseudoClasses.Set(":has-presence", !string.IsNullOrWhiteSpace(PresenceText));

        _isPulseRunning = false;
        UpdatePresencePulse();
        ScheduleApplyTranscriptAlignment(forceFull: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Compositor visual may have been recreated on re-attach.
        _isPulseRunning = false;
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePresencePulse();
            ApplyTranscriptAlignment(forceFull: true);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnUserWheelScroll;
        }

        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);
        UnsubscribeTranscriptPanel();
        _isPulseRunning = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlowDirectionProperty)
            ScheduleApplyTranscriptAlignment(forceFull: true);
    }

    private void OnIsOnlineChanged()
    {
        var online = IsOnline;
        PseudoClasses.Set(":online", online);
        PseudoClasses.Set(":offline", !online);
        UpdatePresencePulse();
    }

    private void OnPresenceTextChanged()
    {
        PseudoClasses.Set(":has-presence", !string.IsNullOrWhiteSpace(PresenceText));
        UpdatePresencePulse();
    }

    private void UpdatePresencePulse()
    {
        if (IsOnline && !string.IsNullOrWhiteSpace(PresenceText))
            StartPresencePulse();
        else
            StopPresencePulse();
    }

    private void ConfigureAlignmentSubscription()
    {
        UnsubscribeTranscriptPanel();

        _transcriptPanel = Transcript as Panel;

        if (_transcriptPanel?.Children is INotifyCollectionChanged panelChildren)
        {
            _transcriptCollection = panelChildren;
            _transcriptCollection.CollectionChanged += OnTranscriptChildrenChanged;
            return;
        }

        _transcriptItemsControl = Transcript as ItemsControl;
        if (_transcriptItemsControl?.Items is INotifyCollectionChanged itemCollection)
        {
            _transcriptCollection = itemCollection;
            _transcriptCollection.CollectionChanged += OnTranscriptChildrenChanged;
        }
    }

    private void UnsubscribeTranscriptPanel()
    {
        if (_transcriptCollection is not null)
            _transcriptCollection.CollectionChanged -= OnTranscriptChildrenChanged;

        _transcriptCollection = null;
        _transcriptPanel = null;
        _transcriptItemsControl = null;
    }

    private void OnTranscriptChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var currentCount = _transcriptPanel?.Children.Count
            ?? _transcriptItemsControl?.Items.Count
            ?? 0;

        // Only incremental alignment when items are appended at the end.
        var forceFull = e.Action != NotifyCollectionChangedAction.Add
            || e.NewStartingIndex < 0
            || e.NewStartingIndex < _lastAlignedMessageCount
            || currentCount < _lastAlignedMessageCount;

        ScheduleApplyTranscriptAlignment(forceFull);

        if (_isTranscriptScrolling)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && AreAllControls(e.NewItems))
                ApplyScrollingStateToItems(e.NewItems, isScrolling: true);
            else
                ApplyTranscriptScrollingState(true);
        }
    }

    private void ScheduleApplyTranscriptAlignment(bool forceFull = false)
    {
        if (forceFull)
            _forceFullAlignment = true;

        if (_alignmentQueued) return;
        _alignmentQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _alignmentQueued = false;
            ApplyTranscriptAlignment(_forceFullAlignment);
            _forceFullAlignment = false;
        }, DispatcherPriority.Loaded);
    }

    private void ApplyTranscriptAlignment(bool forceFull)
    {
        var panel = Transcript as Panel;

        if (panel is not null)
        {
            var childCount = panel.Children.Count;
            if (forceFull || _lastAlignedMessageCount > childCount)
            {
                foreach (var child in panel.Children)
                    AlignMessagesInNode(child);
            }
            else
            {
                for (var i = _lastAlignedMessageCount; i < childCount; i++)
                    AlignMessagesInNode(panel.Children[i]);
            }

            _lastAlignedMessageCount = childCount;
            return;
        }

        var itemsControl = Transcript as ItemsControl;
        if (itemsControl is not null)
        {
            var itemCount = itemsControl.Items.Count;
            if (forceFull || _lastAlignedMessageCount > itemCount)
            {
                for (var i = 0; i < itemCount; i++)
                    AlignMessagesInNode(itemsControl.ContainerFromIndex(i) ?? itemsControl.Items[i]);
            }
            else
            {
                for (var i = _lastAlignedMessageCount; i < itemCount; i++)
                    AlignMessagesInNode(itemsControl.ContainerFromIndex(i) ?? itemsControl.Items[i]);
            }

            _lastAlignedMessageCount = itemCount;
            return;
        }

        ApplyAlignmentRecursive(Transcript);
        _lastAlignedMessageCount = 0;
    }

    private void AlignMessagesInNode(object? node)
    {
        if (node is null)
            return;

        if (node is StrataChatMessage message)
        {
            ApplyAlignment(message);
            return;
        }

        if (node is ContentControl contentControl)
        {
            AlignMessagesInNode(contentControl.Content);
            return;
        }

        if (node is Decorator decorator)
        {
            AlignMessagesInNode(decorator.Child);
            return;
        }

        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
                AlignMessagesInNode(child);
        }
    }

    private static void ApplyAlignment(StrataChatMessage message)
    {
        var alignment = message.Role switch
        {
            StrataChatRole.User => HorizontalAlignment.Right,
            StrataChatRole.Assistant => HorizontalAlignment.Left,
            StrataChatRole.System => HorizontalAlignment.Left,
            StrataChatRole.Tool => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Stretch
        };

        var maxWidth = message.Role switch
        {
            StrataChatRole.User => 600d,
            StrataChatRole.Assistant => 600d,
            StrataChatRole.System => 700d,
            StrataChatRole.Tool => 700d,
            _ => double.PositiveInfinity
        };

        if (message.HorizontalAlignment != alignment)
            message.HorizontalAlignment = alignment;

        if (message.MaxWidth != maxWidth)
            message.MaxWidth = maxWidth;
    }

    /// <summary>
    /// Walks the transcript tree and applies alignment directly, avoiding
    /// the intermediate list allocation that CollectTranscriptMessages used.
    /// </summary>
    private static void ApplyAlignmentRecursive(object? node)
    {
        if (node is null)
            return;

        if (node is StrataChatMessage message)
        {
            ApplyAlignment(message);
            return;
        }

        if (node is ContentControl contentControl)
        {
            ApplyAlignmentRecursive(contentControl.Content);
            return;
        }

        if (node is Decorator decorator)
        {
            ApplyAlignmentRecursive(decorator.Child);
            return;
        }

        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
                ApplyAlignmentRecursive(child);

            return;
        }

        if (node is ItemsControl itemsControl)
        {
            for (var i = 0; i < itemsControl.Items.Count; i++)
                ApplyAlignmentRecursive(itemsControl.ContainerFromIndex(i) ?? itemsControl.Items[i]);
        }
    }

    private void StartPresencePulse()
    {
        if (_presenceDot is null || _isPulseRunning)
            return;

        var visual = ElementComposition.GetElementVisual(_presenceDot);
        if (visual is null)
            return;

        _isPulseRunning = true;
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
        if (_presenceDot is null || !_isPulseRunning)
            return;

        _isPulseRunning = false;
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
    /// Debounced: multiple calls within the same frame coalesce into one scroll.
    /// </summary>
    public void ScrollToEnd()
    {
        if (_scrollViewer is null || _userScrolledAway || IsAutoScrollSuspended())
            return;

        var now = DateTime.UtcNow;
        if (_lastProgrammaticScrollUtc != default && (now - _lastProgrammaticScrollUtc) < ProgrammaticScrollMinInterval)
            return;

        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            if (_scrollViewer is null || _userScrolledAway || IsAutoScrollSuspended()) return;

            _isProgrammaticScroll = true;
            _lastProgrammaticScrollUtc = DateTime.UtcNow;
            _scrollViewer.ScrollToEnd();

            // Scroll once more after layout settles so streaming content growth
            // (e.g., markdown reflow) still keeps the viewport pinned to the bottom.
            Dispatcher.UIThread.Post(() =>
            {
                if (_scrollViewer is not null && !_userScrolledAway && !IsAutoScrollSuspended())
                {
                    _lastProgrammaticScrollUtc = DateTime.UtcNow;
                    _scrollViewer.ScrollToEnd();
                }

                _isProgrammaticScroll = false;
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// Resets the user-scrolled-away flag so auto-scroll resumes.
    /// Call this when the user sends a new message.
    /// </summary>
    public void ResetAutoScroll()
    {
        _userScrolledAway = false;
        _suspendAutoScrollUntilUtc = default;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || _isProgrammaticScroll)
            return;

        var offsetDeltaY = Math.Abs(e.OffsetDelta.Y);
        if (offsetDeltaY < UserScrollDeltaThreshold)
            return;

        // Ignore extent-driven anchor shifts caused by streaming/layout growth.
        // Touchpad smooth scrolling often emits tiny deltas, so we use a small
        // threshold and compare against extent delta rather than a coarse cutoff.
        if (IsLikelyLayoutDrivenOffsetChange(e, offsetDeltaY))
            return;

        MarkTranscriptScrollingActive();

        SuspendAutoScrollBriefly();

        var distanceFromBottom = DistanceFromBottom(_scrollViewer);
        _userScrolledAway = distanceFromBottom > 40;
    }

    private void OnUserWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        // Don't intercept — let ScrollViewer handle natively (especially for touchpad).
        // Just detect scroll-away intent so auto-scroll pauses during streaming.
        MarkTranscriptScrollingActive();
        SuspendAutoScrollBriefly();
        if (e.Delta.Y > 0)
            _userScrolledAway = true;
        else if (_scrollViewer is not null && e.Delta.Y < 0 && DistanceFromBottom(_scrollViewer) <= 8)
            _userScrolledAway = false;
    }

    private void MarkTranscriptScrollingActive()
    {
        _lastScrollActivityUtc = DateTime.UtcNow;

        if (!_isTranscriptScrolling)
            SetTranscriptScrollingState(true);

        if (!_scrollingStateTimer.IsEnabled)
            _scrollingStateTimer.Start();
    }

    private void OnScrollingStateTimerTick(object? sender, EventArgs e)
    {
        if (!_isTranscriptScrolling)
        {
            _scrollingStateTimer.Stop();
            return;
        }

        if ((DateTime.UtcNow - _lastScrollActivityUtc).TotalMilliseconds < ScrollingVisualCooldownMs)
            return;

        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);
    }

    private void SetTranscriptScrollingState(bool isScrolling)
    {
        if (_isTranscriptScrolling == isScrolling)
            return;

        _isTranscriptScrolling = isScrolling;
        PseudoClasses.Set(":scrolling", isScrolling);
        ApplyTranscriptScrollingState(isScrolling);
    }

    private void ApplyTranscriptScrollingState(bool isScrolling)
    {
        var panel = Transcript as Panel;
        if (panel is not null)
        {
            foreach (var child in panel.Children)
                ApplyScrollingStateRecursive(child, isScrolling);
            return;
        }

        var itemsControl = Transcript as ItemsControl;
        if (itemsControl is not null)
        {
            if (itemsControl.ItemsPanelRoot is Panel itemsHostPanel)
            {
                foreach (var child in itemsHostPanel.Children)
                    ApplyScrollingStateRecursive(child, isScrolling);
                return;
            }

            // Fallback: walk realized visuals only. Avoid iterating all data items
            // because virtualized transcripts can contain tens of thousands.
            foreach (var visual in itemsControl.GetVisualDescendants())
            {
                if (visual is StrataChatMessage message && message.IsHostScrolling != isScrolling)
                    message.IsHostScrolling = isScrolling;
            }
            return;
        }

        ApplyScrollingStateRecursive(Transcript, isScrolling);
    }

    private static void ApplyScrollingStateToItems(IList? items, bool isScrolling)
    {
        if (items is null || items.Count == 0)
            return;

        for (var i = 0; i < items.Count; i++)
            ApplyScrollingStateRecursive(items[i], isScrolling);
    }

    private static bool AreAllControls(IList? items)
    {
        if (items is null || items.Count == 0)
            return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not Control)
                return false;
        }

        return true;
    }

    private static void ApplyScrollingStateRecursive(object? node, bool isScrolling)
    {
        if (node is null)
            return;

        if (node is StrataChatMessage message)
        {
            if (message.IsHostScrolling != isScrolling)
                message.IsHostScrolling = isScrolling;
            return;
        }

        if (node is ContentControl contentControl)
        {
            ApplyScrollingStateRecursive(contentControl.Content, isScrolling);
            return;
        }

        if (node is Decorator decorator)
        {
            ApplyScrollingStateRecursive(decorator.Child, isScrolling);
            return;
        }

        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
                ApplyScrollingStateRecursive(child, isScrolling);
        }
    }

    private static bool IsLikelyLayoutDrivenOffsetChange(ScrollChangedEventArgs e, double absoluteOffsetDeltaY)
    {
        var extentDeltaY = Math.Abs(e.ExtentDelta.Y);
        if (extentDeltaY < UserScrollDeltaThreshold)
            return false;

        var viewportDeltaY = Math.Abs(e.ViewportDelta.Y);
        if (viewportDeltaY > UserScrollDeltaThreshold)
            return false;

        return Math.Abs(absoluteOffsetDeltaY - extentDeltaY) <= LayoutShiftDeltaTolerance;
    }

    private bool IsAutoScrollSuspended()
    {
        return DateTime.UtcNow < _suspendAutoScrollUntilUtc;
    }

    private void SuspendAutoScrollBriefly()
    {
        _suspendAutoScrollUntilUtc = DateTime.UtcNow.AddMilliseconds(AutoScrollInteractionCooldownMs);
    }

    private static double DistanceFromBottom(ScrollViewer scrollViewer)
    {
        return scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
    }
}
