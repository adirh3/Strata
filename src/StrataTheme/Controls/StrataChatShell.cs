using System;
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

public sealed class StrataTranscriptViewportChangedEventArgs : EventArgs
{
    public StrataTranscriptViewportChangedEventArgs(
        double verticalOffset,
        double viewportHeight,
        double extentHeight,
        bool isPinnedToBottom,
        double distanceFromBottom)
    {
        VerticalOffset = verticalOffset;
        ViewportHeight = viewportHeight;
        ExtentHeight = extentHeight;
        IsPinnedToBottom = isPinnedToBottom;
        DistanceFromBottom = distanceFromBottom;
    }

    public double VerticalOffset { get; }
    public double ViewportHeight { get; }
    public double ExtentHeight { get; }
    public bool IsPinnedToBottom { get; }
    public double DistanceFromBottom { get; }
}

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
/// <para>Call <see cref="ScrollToEnd"/> during streaming to follow the tail while the
/// shell is in follow mode. Call <see cref="JumpToLatest"/> when the user explicitly
/// wants to return to the newest content.</para>
/// <para><b>Template parts:</b> PART_Root (Border), PART_TranscriptScroll (ScrollViewer),
/// PART_HeaderPresenter (ContentPresenter), PART_PresenceDot (Border), PART_PresencePill (Border),
/// PART_ScrollToBottomButton (Button – scroll-to-bottom action),
/// PART_NewContentDot (Border – pulsing badge on the scroll-to-bottom button).</para>
/// <para><b>Pseudo-classes:</b> :online, :offline, :has-header, :has-presence, :scrolling,
/// :scrolled-away, :show-scroll-to-bottom, :has-new-content, :pulse-new-content.</para>
/// </remarks>
public class StrataChatShell : TemplatedControl
{
    private const int ScrollingVisualCooldownMs = 140;
    private const double UserScrollDeltaThreshold = 0.05;
    private const double LayoutShiftDeltaTolerance = 0.2;

    private ScrollViewer? _scrollViewer;
    private Panel? _transcriptPanel;
    private ItemsControl? _transcriptItemsControl;
    private INotifyCollectionChanged? _transcriptCollection;
    private readonly ChatScrollPolicy _scrollPolicy = new();
    private bool _isProgrammaticScroll;
    private bool _alignmentQueued;
    private bool _scrollQueued;
    private long _queuedScrollGeneration;
    private bool _viewportCompensationQueued;
    private long _viewportCompensationGeneration;
    private double _pendingViewportCompensation;
    private int _programmaticScrollReleaseVersion;
    private bool _forceFullAlignment = true;
    private int _lastAlignedMessageCount;
    private readonly DispatcherTimer _scrollingStateTimer;
    private DateTime _lastScrollActivityUtc;
    private bool _isTranscriptScrolling;
    private Button? _scrollToBottomButton;
    private bool _hasNewContent;


    /// <summary>Optional header content displayed at the top of the shell.</summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataChatShell, object?>(nameof(Header));

    /// <summary>Scrollable transcript content hosted inside the shell's ScrollViewer.</summary>
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

    /// <summary>
    /// Whether the agent is actively streaming a response. When true and the user
    /// is scrolled away, the scroll-to-bottom button shows a pulsing accent badge.
    /// </summary>
    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<StrataChatShell, bool>(nameof(IsStreaming));

    /// <summary>
    /// Read-only indicator that new content is available below the current scroll
    /// position — either because the agent is streaming or new messages arrived
    /// while the user was scrolled away.
    /// </summary>
    public static readonly DirectProperty<StrataChatShell, bool> HasNewContentProperty =
        AvaloniaProperty.RegisterDirect<StrataChatShell, bool>(
            nameof(HasNewContent), o => o.HasNewContent);

    static StrataChatShell()
    {
        IsOnlineProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.OnIsOnlineChanged());
        HeaderProperty.Changed.AddClassHandler<StrataChatShell>((c, _) =>
            c.PseudoClasses.Set(":has-header", c.Header is not null));
        PresenceTextProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.OnPresenceTextChanged());
        IsStreamingProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.UpdateHasNewContent());
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
    public bool IsStreaming { get => GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public event EventHandler<StrataTranscriptViewportChangedEventArgs>? TranscriptViewportChanged;
    public event Action? JumpToLatestRequested;

    /// <summary>
    /// True when the user is scrolled away and new content exists below (streaming or new messages).
    /// </summary>
    public bool HasNewContent
    {
        get => _hasNewContent;
        private set => SetAndRaise(HasNewContentProperty, ref _hasNewContent, value);
    }

    public bool IsFollowingTail => _scrollPolicy.IsFollowingTail;
    public long ScrollGeneration => _scrollPolicy.Generation;
    public ScrollViewer? TranscriptScrollViewer => _scrollViewer;
    public double VerticalOffset => _scrollViewer?.Offset.Y ?? 0d;
    public double ViewportHeight => _scrollViewer?.Viewport.Height ?? 0d;
    public double ExtentHeight => _scrollViewer?.Extent.Height ?? 0d;
    public double CurrentDistanceFromBottom => _scrollViewer is null ? 0d : DistanceFromBottom(_scrollViewer);
    public bool IsPinnedToBottom => IsFollowingTail
        && CurrentDistanceFromBottom <= ChatScrollPolicy.DefaultBottomTolerance;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        DetachTemplatePartHandlers();

        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_TranscriptScroll");
        _scrollToBottomButton = e.NameScope.Find<Button>("PART_ScrollToBottomButton");

        AttachTemplatePartHandlers();

        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);

        ConfigureAlignmentSubscription();

        // Apply all pseudo-classes once; targeted handlers maintain them afterwards.
        var online = IsOnline;
        PseudoClasses.Set(":online", online);
        PseudoClasses.Set(":offline", !online);
        PseudoClasses.Set(":has-header", Header is not null);
        PseudoClasses.Set(":has-presence", !string.IsNullOrWhiteSpace(PresenceText));
        PseudoClasses.Set(":scrolled-away", !IsFollowingTail);
        UpdateScrollToBottomButtonVisibility();

        UpdateHasNewContent();
        ScheduleApplyTranscriptAlignment(forceFull: true);
        NotifyTranscriptLayoutChanged();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollingStateTimer.Tick -= OnScrollingStateTimerTick;
        _scrollingStateTimer.Tick += OnScrollingStateTimerTick;
        AttachTemplatePartHandlers();
        ConfigureAlignmentSubscription();
        Dispatcher.UIThread.Post(() =>
        {
            UpdateScrollToBottomButtonVisibility();
            UpdateHasNewContent();
            ApplyTranscriptAlignment(forceFull: true);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachTemplatePartHandlers();

        _scrollingStateTimer.Tick -= OnScrollingStateTimerTick;
        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);
        UnsubscribeTranscriptPanel();
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachTemplatePartHandlers()
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnUserWheelScroll);
            _scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnUserWheelScroll, RoutingStrategies.Bubble, handledEventsToo: true);
            _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed);
            _scrollViewer.AddHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            _scrollViewer.RemoveHandler(InputElement.KeyDownEvent, OnTranscriptKeyDown);
            _scrollViewer.AddHandler(InputElement.KeyDownEvent, OnTranscriptKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            _scrollViewer.RemoveHandler(InputElement.ScrollGestureEvent, OnTranscriptScrollGesture);
            _scrollViewer.AddHandler(InputElement.ScrollGestureEvent, OnTranscriptScrollGesture, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        if (_scrollToBottomButton is not null)
        {
            _scrollToBottomButton.Click -= OnScrollToBottomButtonClick;
            _scrollToBottomButton.Click += OnScrollToBottomButtonClick;
        }
    }

    private void DetachTemplatePartHandlers()
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnUserWheelScroll);
            _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed);
            _scrollViewer.RemoveHandler(InputElement.KeyDownEvent, OnTranscriptKeyDown);
            _scrollViewer.RemoveHandler(InputElement.ScrollGestureEvent, OnTranscriptScrollGesture);
        }

        if (_scrollToBottomButton is not null)
            _scrollToBottomButton.Click -= OnScrollToBottomButtonClick;
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
    }

    private void OnPresenceTextChanged()
    {
        PseudoClasses.Set(":has-presence", !string.IsNullOrWhiteSpace(PresenceText));
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
        var isEndAppend = e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is { Count: > 0 }
            && e.NewStartingIndex >= 0
            && e.NewStartingIndex + e.NewItems.Count == currentCount;

        ScheduleApplyTranscriptAlignment(forceFull);

        if (_isTranscriptScrolling)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && AreAllControls(e.NewItems))
                ApplyScrollingStateToItems(e.NewItems, isScrolling: true);
        }

        if (isEndAppend)
            NotifyTranscriptContentChanged();

        Dispatcher.UIThread.Post(UpdateScrollToBottomButtonVisibility, DispatcherPriority.Loaded);
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
            StrataChatRole.User => 560d,
            StrataChatRole.Assistant => 760d,
            StrataChatRole.System => 760d,
            StrataChatRole.Tool => 760d,
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

    /// <summary>
    /// Scrolls transcript to the bottom. Call this during streaming/generation.
    /// Respects user scroll-away: if the user scrolled up, this is a no-op
    /// until <see cref="ResetAutoScroll"/> is called.
    /// Coalesced: multiple calls within the same frame produce one scroll.
    /// </summary>
    public void ScrollToEnd() => NotifyTranscriptContentChanged();

    /// <summary>
    /// Re-enters follow-tail mode and jumps to the newest visible content.
    /// Use this for explicit user intent such as sending a message or tapping
    /// the scroll-to-bottom button.
    /// </summary>
    public void JumpToLatest()
    {
        ExecuteScrollDecision(_scrollPolicy.RequestRevealSentMessage());
        RefreshScrollPolicyState();
    }

    /// <summary>
    /// Re-enters follow-tail mode without forcing an immediate scroll.
    /// </summary>
    public void EnterFollowTailMode()
    {
        _scrollPolicy.EnterFollowMode();
        RefreshScrollPolicyState();
    }

    /// <summary>
    /// Leaves follow mode for explicit navigation to older content.
    /// </summary>
    public void PreserveViewport()
    {
        _scrollPolicy.PreserveViewport();
        RefreshScrollPolicyState();
    }

    /// <summary>
    /// Starts a deterministic chat-open landing at the bottom.
    /// </summary>
    public void RequestInitialBottom()
    {
        ExecuteScrollDecision(_scrollPolicy.RequestInitialBottom());
        RefreshScrollPolicyState();
    }

    /// <summary>
    /// Reports newly arrived transcript content. Follows only when the reader is already following.
    /// </summary>
    public void NotifyTranscriptContentChanged()
    {
        ExecuteScrollDecision(_scrollPolicy.OnContentChanged(CaptureMetrics(), markAsUnseen: true));
        RefreshScrollPolicyState();
    }

    /// <summary>
    /// Reports layout-only growth such as streaming measurement, resizing, or virtualization.
    /// </summary>
    public void NotifyTranscriptLayoutChanged()
    {
        ExecuteScrollDecision(_scrollPolicy.OnContentChanged(CaptureMetrics(), markAsUnseen: false));
        RefreshScrollPolicyState();
    }

    /// <summary>
    /// Preserves the reader's visual position when content strictly above the viewport changes height.
    /// </summary>
    public void CompensateForContentAbove(double delta)
    {
        ExecuteScrollDecision(_scrollPolicy.OnContentAboveViewportResized(delta));
    }

    private void ExecuteScrollDecision(ChatScrollDecision decision)
    {
        switch (decision.Action)
        {
            case ChatScrollAction.ScrollToBottom:
                QueueScrollToEnd(decision.Generation);
                break;
            case ChatScrollAction.CompensateViewport:
                QueueViewportCompensation(decision);
                break;
        }
    }

    private void QueueScrollToEnd(long generation)
    {
        if (_scrollViewer is null || generation != _scrollPolicy.Generation || !IsFollowingTail)
            return;

        _queuedScrollGeneration = generation;
        if (_scrollQueued)
            return;

        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            var queuedGeneration = _queuedScrollGeneration;
            if (!CanApplyFollowDecision(queuedGeneration))
                return;

            ApplyProgrammaticScroll(static scrollViewer => scrollViewer.ScrollToEnd());

            Dispatcher.UIThread.Post(() =>
            {
                if (!CanApplyFollowDecision(queuedGeneration))
                    return;

                ApplyProgrammaticScroll(static scrollViewer => scrollViewer.ScrollToEnd());
                _scrollPolicy.OnBottomLanded(CaptureMetrics());
                RefreshScrollPolicyState();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Render);
    }

    private void QueueViewportCompensation(ChatScrollDecision decision)
    {
        if (decision.Generation != _scrollPolicy.Generation || IsFollowingTail)
            return;

        if (!_viewportCompensationQueued
            || _viewportCompensationGeneration != decision.Generation)
        {
            _pendingViewportCompensation = 0d;
            _viewportCompensationGeneration = decision.Generation;
        }

        _pendingViewportCompensation += decision.OffsetDelta;
        if (_viewportCompensationQueued)
            return;

        _viewportCompensationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _viewportCompensationQueued = false;
            var delta = _pendingViewportCompensation;
            _pendingViewportCompensation = 0d;

            if (_scrollViewer is null
                || _viewportCompensationGeneration != _scrollPolicy.Generation
                || IsFollowingTail
                || Math.Abs(delta) < ChatScrollPolicy.FractionalEpsilon)
            {
                return;
            }

            var maxOffset = Math.Max(0d, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
            var targetOffset = Math.Clamp(_scrollViewer.Offset.Y + delta, 0d, maxOffset);
            ApplyProgrammaticScroll(scrollViewer =>
                scrollViewer.Offset = scrollViewer.Offset.WithY(targetOffset));
        }, DispatcherPriority.Loaded);
    }

    private bool CanApplyFollowDecision(long generation) =>
        _scrollViewer is not null
        && generation == _scrollPolicy.Generation
        && IsFollowingTail;

    private void ApplyProgrammaticScroll(Action<ScrollViewer> apply)
    {
        if (_scrollViewer is null)
            return;

        _isProgrammaticScroll = true;
        var releaseVersion = ++_programmaticScrollReleaseVersion;
        apply(_scrollViewer);
        UpdateScrollToBottomButtonVisibility();
        RaiseTranscriptViewportChanged();

        Dispatcher.UIThread.Post(() =>
        {
            if (releaseVersion == _programmaticScrollReleaseVersion)
                _isProgrammaticScroll = false;
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Resets the user-scrolled-away flag so auto-scroll resumes.
    /// Call this when the user sends a new message.
    /// </summary>
    public void ResetAutoScroll() => EnterFollowTailMode();

    public void ScrollToVerticalOffset(double verticalOffset)
    {
        TryScrollToVerticalOffset(verticalOffset, _scrollPolicy.Generation);
    }

    public bool TryScrollToVerticalOffset(double verticalOffset, long expectedGeneration)
    {
        if (_scrollViewer is null || expectedGeneration != _scrollPolicy.Generation)
            return false;

        var maxOffset = Math.Max(0d, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        var clampedOffset = Math.Clamp(verticalOffset, 0d, maxOffset);

        ApplyProgrammaticScroll(scrollViewer =>
            scrollViewer.Offset = scrollViewer.Offset.WithY(clampedOffset));
        return true;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        UpdateScrollToBottomButtonVisibility();
        if (_isProgrammaticScroll)
        {
            RaiseTranscriptViewportChanged();
            return;
        }

        var offsetDeltaY = Math.Abs(e.OffsetDelta.Y);
        if (offsetDeltaY < UserScrollDeltaThreshold)
            return;

        // Ignore extent-driven anchor shifts caused by streaming/layout growth.
        if (IsLikelyLayoutDrivenOffsetChange(e, offsetDeltaY))
            return;

        if (IsFollowingTail && Math.Abs(e.ExtentDelta.Y) > UserScrollDeltaThreshold)
        {
            NotifyTranscriptLayoutChanged();
            RaiseTranscriptViewportChanged();
            return;
        }

        MarkTranscriptScrollingActive();
        _scrollPolicy.OnUserScroll(CaptureMetrics(), e.OffsetDelta.Y);
        RefreshScrollPolicyState();
        RaiseTranscriptViewportChanged();
    }

    private void OnUserWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        BeginUserScrollInput(leavesTail: e.Delta.Y > 0);
    }

    private void OnTranscriptPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsScrollbarInteraction(e.Source))
            return;

        BeginUserScrollInput(leavesTail: true);
    }

    private void OnTranscriptKeyDown(object? sender, KeyEventArgs e)
    {
        var isPageScroll = e.Key is Key.PageUp or Key.PageDown;
        var isScrollbarScroll = IsScrollbarInteraction(e.Source)
            && e.Key is Key.Up or Key.Down or Key.Home or Key.End;
        if (!isPageScroll && !isScrollbarScroll)
            return;

        BeginUserScrollInput(leavesTail: e.Key is Key.PageUp or Key.Up or Key.Home);
    }

    private void OnTranscriptScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        BeginUserScrollInput(leavesTail: e.Delta.Y < -ChatScrollPolicy.FractionalEpsilon);
    }

    private void BeginUserScrollInput(bool leavesTail)
    {
        _programmaticScrollReleaseVersion++;
        _isProgrammaticScroll = false;
        MarkTranscriptScrollingActive();
        _scrollPolicy.OnUserInput(leavesTail);
        RefreshScrollPolicyState();
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
        // Walk children and set IsHostScrolling on each StrataChatMessage.
        // The handler avoids pseudo-class changes (which trigger InvalidateMeasure)
        // and only toggles IsHitTestVisible on the message bubble.
        var itemsControl = Transcript as ItemsControl;
        if (itemsControl is not null)
        {
            if (itemsControl.ItemsPanelRoot is Panel itemsHostPanel)
            {
                foreach (var child in itemsHostPanel.Children)
                {
                    ApplyScrollingRenderHint(child, isScrolling);
                    if (!TryApplyScrollingStateFlatScan(child, isScrolling))
                        ApplyScrollingStateRecursive(child, isScrolling);
                }
                return;
            }
            return;
        }

        if (Transcript is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                ApplyScrollingRenderHint(child, isScrolling);
                if (!TryApplyScrollingStateFlatScan(child, isScrolling))
                    ApplyScrollingStateRecursive(child, isScrolling);
            }
            return;
        }

        ApplyScrollingStateRecursive(Transcript, isScrolling);
    }

    private static void ApplyScrollingRenderHint(Control container, bool isScrolling)
    {
        if (!container.IsVisible)
        {
            // An off-screen/collapsed container never needs a scroll rasterization cache. Release it
            // unconditionally (even mid-scroll) so a container that scrolls out of view — or is about
            // to be virtualized away — cannot retain a BitmapCache (a render-thread bitmap surface /
            // large-object allocation) after it leaves the visible set.
            if (container.CacheMode is not null)
                container.CacheMode = null;

            return;
        }

        if (isScrolling)
        {
            // Keep nested interactive controls (for example the expanded reasoning
            // ScrollViewer inside StrataThink) hit-testable while the host transcript
            // is scrolling. Message-level hover simplification still flows through
            // StrataChatMessage.IsHostScrolling.
            container.CacheMode ??= new BitmapCache
            {
                RenderAtScale = 1d,
                SnapsToDevicePixels = true,
                EnableClearType = true
            };
            return;
        }

        if (container.CacheMode is not null)
            container.CacheMode = null;
    }

    /// <summary>
    /// Fast non-recursive scan for StrataChatMessage inside a single container.
    /// Handles the common case of ContentPresenter wrapping a message control
    /// without the overhead of a full recursive tree walk.
    /// </summary>
    private static bool TryApplyScrollingStateFlatScan(Control container, bool isScrolling)
    {
        // Direct message
        if (container is StrataChatMessage message)
        {
            if (message.IsHostScrolling != isScrolling)
                message.IsHostScrolling = isScrolling;
            return true;
        }

        // ContentPresenter wrapping a message (ItemsControl container)
        if (container is ContentPresenter cp)
        {
            if (cp.Content is StrataChatMessage cpMsg)
            {
                if (cpMsg.IsHostScrolling != isScrolling)
                    cpMsg.IsHostScrolling = isScrolling;
                return true;
            }
            // Content might be a non-message control (StrataThink, etc.) — skip
            return false;
        }

        return false;
    }

    private static void ApplyScrollingStateToItems(IList? items, bool isScrolling)
    {
        if (items is null || items.Count == 0)
            return;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is Control control)
            {
                ApplyScrollingRenderHint(control, isScrolling);
                if (TryApplyScrollingStateFlatScan(control, isScrolling))
                    continue;
            }

            ApplyScrollingStateRecursive(items[i], isScrolling);
        }
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

    private static double DistanceFromBottom(ScrollViewer scrollViewer)
    {
        return Math.Max(0d, scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y);
    }

    private void UpdateScrollToBottomButtonVisibility()
    {
        var shouldShow = _scrollViewer is not null
            && DistanceFromBottom(_scrollViewer) > ChatScrollPolicy.DefaultBottomTolerance;

        PseudoClasses.Set(":show-scroll-to-bottom", shouldShow);
    }

    private void RaiseTranscriptViewportChanged()
    {
        if (_scrollViewer is null)
            return;

        TranscriptViewportChanged?.Invoke(
            this,
            new StrataTranscriptViewportChangedEventArgs(
                _scrollViewer.Offset.Y,
                _scrollViewer.Viewport.Height,
                _scrollViewer.Extent.Height,
                IsPinnedToBottom,
                DistanceFromBottom(_scrollViewer)));
    }

    private static bool IsScrollbarInteraction(object? source)
    {
        if (source is not Control control)
            return false;

        return control is ScrollBar or Thumb or Track or RepeatButton
            || control.FindAncestorOfType<ScrollBar>() is not null
            || control.FindAncestorOfType<Thumb>() is not null
            || control.FindAncestorOfType<Track>() is not null;
    }

    private ChatScrollMetrics CaptureMetrics()
    {
        return _scrollViewer is null
            ? new ChatScrollMetrics(0d, 0d, 0d)
            : new ChatScrollMetrics(
                _scrollViewer.Offset.Y,
                _scrollViewer.Extent.Height,
                _scrollViewer.Viewport.Height);
    }

    private void RefreshScrollPolicyState()
    {
        PseudoClasses.Set(":scrolled-away", !IsFollowingTail);
        UpdateHasNewContent();
    }

    /// <summary>
    /// Re-evaluates whether the scroll-to-bottom badge should be visible.
    /// True when the user is scrolled away AND either streaming or unseen messages exist.
    /// The badge pulses only while actively streaming; it shows as a static dot
    /// for unseen content when streaming has ended.
    /// </summary>
    private void UpdateHasNewContent()
    {
        var hasNew = !IsFollowingTail && (_scrollPolicy.HasUnseenContent || IsStreaming);

        PseudoClasses.Set(":has-new-content", hasNew);
        PseudoClasses.Set(":pulse-new-content", hasNew && IsStreaming);

        HasNewContent = hasNew;
    }

    private void OnScrollToBottomButtonClick(object? sender, RoutedEventArgs e)
    {
        if (JumpToLatestRequested is not null)
        {
            JumpToLatestRequested.Invoke();
            return;
        }

        JumpToLatest();
    }

}
