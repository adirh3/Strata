using System;
using System.Collections.Specialized;
using System.Collections.Generic;
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
/// <para><b>Pseudo-classes:</b> :online, :offline, :has-header, :has-presence.</para>
/// </remarks>
public class StrataChatShell : TemplatedControl
{
    private const int AutoScrollInteractionCooldownMs = 220;
    private static readonly TimeSpan ProgrammaticScrollMinInterval = TimeSpan.FromMilliseconds(90);

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
    private int _lastObservedTranscriptCount;
    private int _lastAlignedMessageCount;

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
        IsOnlineProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.Refresh());
        HeaderProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.Refresh());
        PresenceTextProperty.Changed.AddClassHandler<StrataChatShell>((c, _) => c.Refresh());
        TranscriptProperty.Changed.AddClassHandler<StrataChatShell>((c, _) =>
        {
            c._lastObservedTranscriptCount = 0;
            c._lastAlignedMessageCount = 0;
            c.ConfigureAlignmentSubscription();
            c.ScheduleApplyTranscriptAlignment(forceFull: true);
        });
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
            // Detect user scroll-away intent (don't intercept — let ScrollViewer handle natively)
            _scrollViewer.PointerWheelChanged += OnUserWheelScroll;
        }

        ConfigureAlignmentSubscription();

        Refresh();
        ScheduleApplyTranscriptAlignment(forceFull: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            Refresh();
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

        UnsubscribeTranscriptPanel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlowDirectionProperty)
            ScheduleApplyTranscriptAlignment(forceFull: true);
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

        ScheduleApplyTranscriptAlignment(forceFull: true);
    }

    private void ConfigureAlignmentSubscription()
    {
        UnsubscribeTranscriptPanel();

        _transcriptPanel = Transcript as Panel;

        if (_transcriptPanel?.Children is INotifyCollectionChanged panelChildren)
        {
            _transcriptCollection = panelChildren;
            _transcriptCollection.CollectionChanged += OnTranscriptChildrenChanged;
            _lastObservedTranscriptCount = _transcriptPanel.Children.Count;
            return;
        }

        _transcriptItemsControl = Transcript as ItemsControl;
        if (_transcriptItemsControl?.Items is INotifyCollectionChanged itemCollection)
        {
            _transcriptCollection = itemCollection;
            _transcriptCollection.CollectionChanged += OnTranscriptChildrenChanged;
            _lastObservedTranscriptCount = _transcriptItemsControl.Items.Count;
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

        _lastObservedTranscriptCount = currentCount;

        var forceFull = e.Action switch
        {
            NotifyCollectionChangedAction.Add when e.NewStartingIndex >= _lastAlignedMessageCount => false,
            NotifyCollectionChangedAction.Add when e.NewStartingIndex < 0 => true,
            NotifyCollectionChangedAction.Add => true,
            NotifyCollectionChangedAction.Reset => true,
            NotifyCollectionChangedAction.Remove => true,
            NotifyCollectionChangedAction.Replace => true,
            NotifyCollectionChangedAction.Move => true,
            _ => true
        };

        if (currentCount < _lastAlignedMessageCount)
            forceFull = true;

        ScheduleApplyTranscriptAlignment(forceFull);
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

        foreach (var message in CollectTranscriptMessages())
            ApplyAlignment(message);

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

    private IReadOnlyCollection<StrataChatMessage> CollectTranscriptMessages()
    {
        var result = new List<StrataChatMessage>();
        CollectFromTranscriptObject(Transcript, result);
        return result;
    }

    private static void CollectFromTranscriptObject(object? node, ICollection<StrataChatMessage> collector)
    {
        if (node is null)
            return;

        if (node is StrataChatMessage message)
        {
            collector.Add(message);
            return;
        }

        if (node is ContentControl contentControl)
        {
            CollectFromTranscriptObject(contentControl.Content, collector);
            return;
        }

        if (node is Decorator decorator)
        {
            CollectFromTranscriptObject(decorator.Child, collector);
            return;
        }

        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
                CollectFromTranscriptObject(child, collector);

            return;
        }

        if (node is ItemsControl itemsControl)
        {
            for (var i = 0; i < itemsControl.Items.Count; i++)
                CollectFromTranscriptObject(itemsControl.ContainerFromIndex(i) ?? itemsControl.Items[i], collector);
        }
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

        // Ignore extent-only changes from streaming/layout updates.
        // We only treat explicit viewport movement as user scroll intent.
        if (Math.Abs(e.OffsetDelta.Y) <= 0.5)
            return;

        SuspendAutoScrollBriefly();

        var distanceFromBottom = DistanceFromBottom(_scrollViewer);
        _userScrolledAway = distanceFromBottom > 40;
    }

    private void OnUserWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        // Don't intercept — let ScrollViewer handle natively (especially for touchpad).
        // Just detect scroll-away intent so auto-scroll pauses during streaming.
        SuspendAutoScrollBriefly();
        if (e.Delta.Y > 0)
            _userScrolledAway = true;
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
