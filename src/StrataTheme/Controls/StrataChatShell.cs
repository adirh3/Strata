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
/// PART_HeaderPresenter (ContentPresenter), PART_PresenceDot (Border), PART_PresencePill (Border),
/// PART_ScrollToBottomChrome (Border – scroll-to-bottom button surface),
/// PART_NewContentDot (Border – pulsing badge on the scroll-to-bottom button).</para>
/// <para><b>Pseudo-classes:</b> :online, :offline, :has-header, :has-presence, :scrolling,
/// :scrolled-away, :has-new-content.</para>
/// </remarks>
public class StrataChatShell : TemplatedControl
{
    private const int ScrollingVisualCooldownMs = 140;
    private const double UserScrollDeltaThreshold = 0.05;
    private const double LayoutShiftDeltaTolerance = 0.2;

    /// <summary>
    /// When the total scrollable distance is this small, suppress auto-scroll
    /// so the user's first message stays visible in short conversations.
    /// </summary>
    private const double ShortTranscriptMaxScroll = 300d;

    private Border? _presenceDot;
    private ScrollViewer? _scrollViewer;
    private Panel? _transcriptPanel;
    private ItemsControl? _transcriptItemsControl;
    private INotifyCollectionChanged? _transcriptCollection;
    private bool _userScrolledAway;
    private bool _isProgrammaticScroll;
    private bool _alignmentQueued;
    private bool _scrollQueued;
    private bool _forceFullAlignment = true;
    private int _lastAlignedMessageCount;
    private bool _isPulseRunning;
    private readonly DispatcherTimer _scrollingStateTimer;
    private DateTime _lastScrollActivityUtc;
    private bool _isTranscriptScrolling;
    private Border? _scrollToBottomChrome;
    private Border? _newContentDot;
    private bool _hasUnseenContent;
    private bool _isNewContentPulseRunning;
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

    /// <summary>
    /// True when the user is scrolled away and new content exists below (streaming or new messages).
    /// </summary>
    public bool HasNewContent
    {
        get => _hasNewContent;
        private set => SetAndRaise(HasNewContentProperty, ref _hasNewContent, value);
    }

    public ScrollViewer? TranscriptScrollViewer => _scrollViewer;
    public double VerticalOffset => _scrollViewer?.Offset.Y ?? 0d;
    public double ViewportHeight => _scrollViewer?.Viewport.Height ?? 0d;
    public double ExtentHeight => _scrollViewer?.Extent.Height ?? 0d;
    public double CurrentDistanceFromBottom => _scrollViewer is null ? 0d : DistanceFromBottom(_scrollViewer);
    public bool IsPinnedToBottom => !_userScrolledAway && CurrentDistanceFromBottom <= 8d;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnUserWheelScroll);
            _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed);
        }

        if (_scrollToBottomChrome is not null)
            _scrollToBottomChrome.RemoveHandler(InputElement.PointerPressedEvent, OnScrollToBottomPressed);

        _presenceDot = e.NameScope.Find<Border>("PART_PresenceDot");
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_TranscriptScroll");
        _scrollToBottomChrome = e.NameScope.Find<Border>("PART_ScrollToBottomChrome");
        _newContentDot = e.NameScope.Find<Border>("PART_NewContentDot");

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnUserWheelScroll, RoutingStrategies.Bubble, handledEventsToo: true);
            _scrollViewer.AddHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        if (_scrollToBottomChrome is not null)
            _scrollToBottomChrome.AddHandler(InputElement.PointerPressedEvent, OnScrollToBottomPressed, RoutingStrategies.Bubble);

        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);

        ConfigureAlignmentSubscription();

        // Apply all pseudo-classes once; targeted handlers maintain them afterwards.
        var online = IsOnline;
        PseudoClasses.Set(":online", online);
        PseudoClasses.Set(":offline", !online);
        PseudoClasses.Set(":has-header", Header is not null);
        PseudoClasses.Set(":has-presence", !string.IsNullOrWhiteSpace(PresenceText));
        PseudoClasses.Set(":scrolled-away", _userScrolledAway);

        _isPulseRunning = false;
        _isNewContentPulseRunning = false;
        UpdatePresencePulse();
        UpdateHasNewContent();
        ScheduleApplyTranscriptAlignment(forceFull: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Compositor visual may have been recreated on re-attach.
        _isPulseRunning = false;
        _isNewContentPulseRunning = false;
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePresencePulse();
            UpdateHasNewContent();
            ApplyTranscriptAlignment(forceFull: true);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnUserWheelScroll);
            _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed);
        }

        if (_scrollToBottomChrome is not null)
            _scrollToBottomChrome.RemoveHandler(InputElement.PointerPressedEvent, OnScrollToBottomPressed);

        _scrollingStateTimer.Stop();
        SetTranscriptScrollingState(false);
        UnsubscribeTranscriptPanel();
        StopPresencePulse();
        StopNewContentPulse();
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
        }

        // Mark unseen content when new messages arrive while scrolled away.
        if (_userScrolledAway && e.Action == NotifyCollectionChangedAction.Add)
        {
            _hasUnseenContent = true;
            UpdateHasNewContent();
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
    /// Coalesced: multiple calls within the same frame produce one scroll.
    /// </summary>
    public void ScrollToEnd()
    {
        if (_scrollViewer is null || _userScrolledAway)
            return;

        // Don't push the first message out of view in short conversations.
        if (_scrollViewer.Extent.Height - _scrollViewer.Viewport.Height <= ShortTranscriptMaxScroll)
            return;

        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            if (_scrollViewer is null || _userScrolledAway) return;

            _isProgrammaticScroll = true;
            _scrollViewer.ScrollToEnd();

            Dispatcher.UIThread.Post(() =>
            {
                if (_scrollViewer is not null && !_userScrolledAway)
                    _scrollViewer.ScrollToEnd();

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
        SetUserScrolledAway(false);
    }

    public void ScrollToVerticalOffset(double verticalOffset)
    {
        if (_scrollViewer is null)
            return;

        var maxOffset = Math.Max(0d, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        var clampedOffset = Math.Clamp(verticalOffset, 0d, maxOffset);

        _isProgrammaticScroll = true;
        _scrollViewer.Offset = _scrollViewer.Offset.WithY(clampedOffset);
        Dispatcher.UIThread.Post(() => _isProgrammaticScroll = false, DispatcherPriority.Loaded);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || _isProgrammaticScroll)
            return;

        var offsetDeltaY = Math.Abs(e.OffsetDelta.Y);
        if (offsetDeltaY < UserScrollDeltaThreshold)
            return;

        // Ignore extent-driven anchor shifts caused by streaming/layout growth.
        if (IsLikelyLayoutDrivenOffsetChange(e, offsetDeltaY))
            return;

        MarkTranscriptScrollingActive();

        var distanceFromBottom = DistanceFromBottom(_scrollViewer);
        if (distanceFromBottom > 40)
            SetUserScrolledAway(true);
        else if (distanceFromBottom <= 8 && e.OffsetDelta.Y > 0)
            SetUserScrolledAway(false);
    }

    private void OnUserWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        MarkTranscriptScrollingActive();
        // Only mark scroll-away on upward wheel input. Do NOT re-enable
        // auto-scroll from wheel-down: touchpad/precision-scroll devices
        // fire interleaved up+down deltas in the same gesture, which races
        // with programmatic ScrollToEnd and immediately undoes the user's
        // intent. Re-enabling auto-scroll is handled by ResetAutoScroll
        // (user sends a message) or by OnScrollChanged detecting a scrollbar
        // drag to within 8px of the bottom.
        if (e.Delta.Y > 0)
            SetUserScrolledAway(true);
    }

    private void OnTranscriptPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsScrollbarInteraction(e.Source))
            return;

        MarkTranscriptScrollingActive();
        SetUserScrolledAway(true);
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
            if (!isScrolling && container.CacheMode is not null)
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

    private static bool IsScrollbarInteraction(object? source)
    {
        if (source is not Control control)
            return false;

        return control is ScrollBar or Thumb or Track or RepeatButton
            || control.FindAncestorOfType<ScrollBar>() is not null
            || control.FindAncestorOfType<Thumb>() is not null
            || control.FindAncestorOfType<Track>() is not null;
    }

    /// <summary>
    /// Centralised setter for the user-scrolled-away state. Updates pseudo-class
    /// and clears unseen-content tracking when the user returns to the bottom.
    /// </summary>
    private void SetUserScrolledAway(bool value)
    {
        if (_userScrolledAway == value)
            return;

        _userScrolledAway = value;
        PseudoClasses.Set(":scrolled-away", value);

        if (!value)
            _hasUnseenContent = false;

        UpdateHasNewContent();
    }

    /// <summary>
    /// Re-evaluates whether the scroll-to-bottom badge should be visible.
    /// True when the user is scrolled away AND either streaming or unseen messages exist.
    /// </summary>
    private void UpdateHasNewContent()
    {
        var hasNew = _userScrolledAway && (_hasUnseenContent || IsStreaming);

        PseudoClasses.Set(":has-new-content", hasNew);

        var prev = _hasNewContent;
        HasNewContent = hasNew;

        if (hasNew && !prev)
            StartNewContentPulse();
        else if (!hasNew && prev)
            StopNewContentPulse();
    }

    private void OnScrollToBottomPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        _hasUnseenContent = false;
        SetUserScrolledAway(false);

        if (_scrollViewer is null) return;

        _isProgrammaticScroll = true;
        _scrollViewer.ScrollToEnd();

        Dispatcher.UIThread.Post(() =>
        {
            _scrollViewer?.ScrollToEnd();
            _isProgrammaticScroll = false;
        }, DispatcherPriority.Loaded);
    }

    private void StartNewContentPulse()
    {
        if (_newContentDot is null || _isNewContentPulseRunning)
            return;

        var visual = ElementComposition.GetElementVisual(_newContentDot);
        if (visual is null)
            return;

        _isNewContentPulseRunning = true;
        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.4f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(1800);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopNewContentPulse()
    {
        if (_newContentDot is null || !_isNewContentPulseRunning)
            return;

        _isNewContentPulseRunning = false;
        var visual = ElementComposition.GetElementVisual(_newContentDot);
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
}
