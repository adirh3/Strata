using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// Alignment mode for <see cref="StrataChatPanel.ScrollToIndex"/>.
/// </summary>
public enum ScrollToAlignment
{
    /// <summary>Place the target item at the top of the viewport.</summary>
    Start,
    /// <summary>Place the target item centered vertically in the viewport.</summary>
    Center,
    /// <summary>Place the target item at the bottom of the viewport.</summary>
    End,
    /// <summary>Scroll only if the item is not already fully visible.</summary>
    Nearest,
}

/// <summary>
/// A 3-zone virtualizing panel for chat messages with scroll-anchor correction,
/// container recycling, per-item height caching, and instant scroll-to-index.
/// </summary>
/// <remarks>
/// <para><b>Zones:</b></para>
/// <list type="table">
///   <item>
///     <term>Visible (viewport ± 1×)</term>
///     <description>Items in tree with <c>IsVisible=true</c> — rendered normally.</description>
///   </item>
///   <item>
///     <term>Warm (1×–3× viewport)</term>
///     <description>Items in tree with <c>IsVisible=false</c> — compositor skips them,
///     but re-showing is instant (no AddChild/Measure cost).</description>
///   </item>
///   <item>
///     <term>Cold (beyond 3×)</term>
///     <description>Containers returned to recycling pool — zero visual-tree overhead.</description>
///   </item>
/// </list>
/// <para><b>Scroll anchor:</b> Before each layout pass the panel captures the first
/// visible item as a positional anchor.  After heights are recalculated the viewport
/// offset is corrected so that the anchor item stays at the same visual position,
/// eliminating scroll-bar jumping caused by variable-height items.</para>
/// <para><b>Container recycling:</b> Containers leaving the warm zone are returned
/// to a keyed pool rather than destroyed, making re-realization near-free.</para>
/// <para><b>Height oracle:</b> Measured heights are cached in a
/// <c>ConditionalWeakTable</c> keyed by data item so they survive container recycling.
/// Role-based estimates provide accurate initial heights for unmeasured items.</para>
/// </remarks>
public class StrataChatPanel : VirtualizingPanel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<StrataChatPanel, double>(nameof(Spacing), 0);

    /// <summary>Vertical spacing between items in device-independent pixels.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// When <c>true</c>, the panel pins the viewport to the bottom of the extent
    /// instead of applying scroll-anchor correction. Set by the host shell during
    /// streaming so new content stays visible.
    /// </summary>
    public static readonly StyledProperty<bool> IsBottomPinnedProperty =
        AvaloniaProperty.Register<StrataChatPanel, bool>(nameof(IsBottomPinned));

    /// <summary>Gets or sets whether the panel is in bottom-pinned (auto-scroll) mode.</summary>
    public bool IsBottomPinned
    {
        get => GetValue(IsBottomPinnedProperty);
        set => SetValue(IsBottomPinnedProperty, value);
    }

    static StrataChatPanel()
    {
        AffectsMeasure<StrataChatPanel>(SpacingProperty);
    }

    // -- Tuning constants --
    private const int BufferItems = 5;
    private const double VisibleBufferRatio = 1.0;
    private const double WarmBufferRatio = 3.0;
    private const int MaxRealizePerFrame = 14;
    private const int MaxRealizeOnJump = 50;
    private const int MaxRecyclePoolPerKey = 20;
    private const double ScrollMeasureThreshold = 1.0;

    // Role-based height defaults (pixels). Better than a single average for
    // chats that mix short user prompts with long assistant responses.
    private const double EstimateUser = 60;
    private const double EstimateAssistant = 300;
    private const double EstimateSystem = 80;
    private const double EstimateTool = 120;
    private const double EstimateFallback = 120;

    // -- Per-item state --
    private readonly List<ItemSlot> _slots = new();
    private double _lastMeasureWidth = -1;
    private int _firstInTree = -1;
    private int _lastInTree = -1;
    private bool _isInLayout;
    private double _cachedTotalHeight;

    // Prefix-sum array: _prefixHeights[i] = sum of CachedHeight for slots 0..i-1.
    // _prefixHeights[_slots.Count] = total height. Enables O(log n) range lookups.
    private double[] _prefixHeights = Array.Empty<double>();
    private bool _prefixDirty = true;
    private int _prefixDirtyFrom = int.MaxValue; // earliest index needing rebuild

    // -- ScrollViewer state --
    private ScrollViewer? _scrollViewer;
    private double _scrollOffset;
    private double _viewportHeight;
    private double _prevScrollOffset;
    private double _scrollExtentPadding; // extra extent from parent padding/margin

    // -- Scroll anchor correction --
    private int _anchorIndex = -1;
    private double _anchorOffsetInViewport;
    private bool _suppressAnchorCorrection;
    private bool _wasNearBottom;
    private bool _isJump;

    // -- Height oracle: per-item cache surviving container recycling --
    private static readonly ConditionalWeakTable<object, HeightRecord> HeightCache = new();

    // -- Running height estimates (O(1) update) --
    private double _measuredHeightSum;
    private int _measuredHeightCount;
    private readonly double[] _roleHeightSums = new double[4]; // indexed by StrataChatRole
    private readonly int[] _roleHeightCounts = new int[4];

    // -- Cached delegates to avoid per-frame allocations --
    private Action? _cachedInvalidateMeasure;
    private Action? _cachedClearSuppressAnchor;

    // -- Container recycling pool --
    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();

    private struct ItemSlot
    {
        public Control? Element;       // non-null = in the visual tree (visible or hidden)
        public double CachedHeight;
        public bool HasBeenMeasured;
        public double MeasuredAtWidth;
        public object? RecycleKey;     // key for the container recycling pool
    }

    private sealed class HeightRecord
    {
        public double Height;
        public double MeasuredAtWidth;
    }

    // -- Visual-tree hooks --

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _cachedInvalidateMeasure ??= InvalidateMeasure;
        _cachedClearSuppressAnchor ??= ClearSuppressAnchorCorrection;
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
            _scrollViewer = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null) return;

        var newOffset = _scrollViewer.Offset.Y;
        var newViewport = _scrollViewer.Viewport.Height;

        // After anchor/bottom-pin correction, _scrollOffset already equals the
        // new value we wrote, so the delta is ~0 and we skip the threshold.
        // This naturally suppresses feedback without a flag.
        var offsetDelta = Math.Abs(newOffset - _scrollOffset);
        if (offsetDelta > ScrollMeasureThreshold ||
            Math.Abs(newViewport - _viewportHeight) > 1)
        {
            _scrollOffset = newOffset;
            _viewportHeight = newViewport;
            InvalidateMeasure();
        }
    }

    // -- VirtualizingPanel contract --

    protected override Control? ContainerFromIndex(int index)
    {
        if ((uint)index >= (uint)_slots.Count) return null;
        return _slots[index].Element;
    }

    protected override int IndexFromContainer(Control container)
    {
        for (int i = Math.Max(0, _firstInTree); i <= _lastInTree && i < _slots.Count; i++)
        {
            if (ReferenceEquals(_slots[i].Element, container))
                return i;
        }
        return -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
    {
        if (_firstInTree < 0) yield break;
        for (int i = _firstInTree; i <= _lastInTree && i < _slots.Count; i++)
        {
            if (_slots[i].Element is { } el)
                yield return el;
        }
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (from is not Control c) return null;
        var idx = IndexFromContainer(c);
        if (idx < 0) return null;

        var next = direction switch
        {
            NavigationDirection.Down or NavigationDirection.Next => idx + 1,
            NavigationDirection.Up or NavigationDirection.Previous => idx - 1,
            NavigationDirection.First => 0,
            NavigationDirection.Last => _slots.Count - 1,
            _ => -1,
        };

        if (next < 0 || next >= _slots.Count) return null;
        return EnsureInTree(next, visible: true);
    }

    protected override Control? ScrollIntoView(int index)
    {
        if ((uint)index >= (uint)_slots.Count || _isInLayout) return null;
        var el = EnsureInTree(index, visible: true);
        el?.BringIntoView();
        return el;
    }

    // -- Public API: ScrollToIndex --

    /// <summary>
    /// Instantly positions the viewport so that item at <paramref name="index"/>
    /// is visible according to <paramref name="alignment"/>.  Does not animate —
    /// the offset is set directly and items around the target are realized
    /// in the same frame.
    /// </summary>
    /// <param name="index">Zero-based item index to navigate to.</param>
    /// <param name="alignment">Where to place the target item in the viewport.</param>
    public void ScrollToIndex(int index, ScrollToAlignment alignment = ScrollToAlignment.Start)
    {
        if ((uint)index >= (uint)_slots.Count || _scrollViewer is null)
            return;

        EnsurePrefixHeights();

        var itemTop = _prefixHeights[index];
        var itemHeight = _slots[index].CachedHeight;
        var vpH = _viewportHeight > 0 ? _viewportHeight : _scrollViewer.Viewport.Height;
        if (vpH <= 0) vpH = 800;
        var maxOffset = Math.Max(0, _cachedTotalHeight + _scrollExtentPadding - vpH);

        double targetOffset = alignment switch
        {
            ScrollToAlignment.Start => itemTop,
            ScrollToAlignment.Center => itemTop - (vpH - itemHeight) / 2.0,
            ScrollToAlignment.End => itemTop + itemHeight - vpH,
            ScrollToAlignment.Nearest => ComputeNearestOffset(itemTop, itemHeight, vpH),
            _ => itemTop,
        };

        targetOffset = Math.Clamp(targetOffset, 0, maxOffset);

        // Suppress anchor correction — this is an intentional teleport.
        _suppressAnchorCorrection = true;
        _scrollOffset = targetOffset;
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetOffset);
        InvalidateMeasure();

        // Re-enable after layout settles
        Avalonia.Threading.Dispatcher.UIThread.Post(_cachedClearSuppressAnchor!,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private double ComputeNearestOffset(double itemTop, double itemHeight, double vpH)
    {
        var itemBottom = itemTop + itemHeight;
        var vpTop = _scrollOffset;
        var vpBottom = vpTop + vpH;

        // Already fully visible
        if (itemTop >= vpTop && itemBottom <= vpBottom)
            return _scrollOffset;

        // Closer to top or bottom?
        return (itemTop < vpTop) ? itemTop : itemTop + itemHeight - vpH;
    }

    // -- Collection changes --

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                var newCount = e.NewItems!.Count;
                var addIndex = e.NewStartingIndex;
                if (newCount == 1)
                {
                    // Fast path for single-item add (common during streaming)
                    var slot = new ItemSlot { CachedHeight = EstimateHeightForItem(e.NewItems![0]) };
                    _slots.Insert(addIndex, slot);
                }
                else
                {
                    var newSlots = new ItemSlot[newCount];
                    for (int i = 0; i < newCount; i++)
                    {
                        newSlots[i] = new ItemSlot { CachedHeight = EstimateHeightForItem(e.NewItems![i]) };
                    }
                    _slots.InsertRange(addIndex, newSlots);
                }
                if (_firstInTree >= 0 && addIndex <= _firstInTree)
                {
                    _firstInTree += newCount;
                    _lastInTree += newCount;
                    // Anchor also shifted
                    if (_anchorIndex >= 0 && addIndex <= _anchorIndex)
                        _anchorIndex += newCount;
                }
                MarkPrefixDirtyFrom(addIndex);
                break;

            case NotifyCollectionChangedAction.Remove:
                for (int i = e.OldItems!.Count - 1; i >= 0; i--)
                {
                    var idx = e.OldStartingIndex + i;
                    if (idx < _slots.Count)
                    {
                        if (_slots[idx].Element is { } el)
                            ReturnToPool(el, idx);
                        RemoveSlotHeightFromEstimate(idx);
                        _slots.RemoveAt(idx);
                    }
                }
                RecalcInTreeRange();
                MarkPrefixDirtyFrom(e.OldStartingIndex);
                break;

            case NotifyCollectionChangedAction.Replace:
                for (int i = 0; i < e.NewItems!.Count; i++)
                {
                    var idx = e.NewStartingIndex + i;
                    if (idx < _slots.Count)
                    {
                        if (_slots[idx].Element is { } el)
                            ReturnToPool(el, idx);
                        RemoveSlotHeightFromEstimate(idx);
                        var item = e.NewItems![i];
                        _slots[idx] = new ItemSlot { CachedHeight = EstimateHeightForItem(item) };
                    }
                }
                MarkPrefixDirtyFrom(e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Reset:
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Element is { } el)
                        ReturnToPool(el, i);
                }
                _slots.Clear();
                _firstInTree = -1;
                _lastInTree = -1;
                _anchorIndex = -1;
                _measuredHeightSum = 0;
                _measuredHeightCount = 0;
                Array.Clear(_roleHeightSums);
                Array.Clear(_roleHeightCounts);
                ClearRecyclePool();
                for (int i = 0; i < items.Count; i++)
                    _slots.Add(new ItemSlot { CachedHeight = EstimateHeightForItem(items[i]) });
                MarkPrefixDirtyFrom(0);
                break;
        }

        InvalidateMeasure();
    }

    // -- Measure / Arrange --

    protected override Size MeasureOverride(Size availableSize)
    {
        _isInLayout = true;
        try
        {
            // Sync slots with Items count (items may have been added before
            // the panel was attached to the visual tree).
            var itemCount = Items.Count;
            bool itemsAdded = itemCount > _slots.Count;
            if (_slots.Count != itemCount)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Element is { } el)
                        ReturnToPool(el, i);
                }
                _slots.Clear();
                _firstInTree = -1;
                _lastInTree = -1;
                _anchorIndex = -1;
                _measuredHeightSum = 0;
                _measuredHeightCount = 0;
                Array.Clear(_roleHeightSums);
                Array.Clear(_roleHeightCounts);
                var items2 = Items;
                for (int i = 0; i < itemCount; i++)
                    _slots.Add(new ItemSlot { CachedHeight = EstimateHeightForItem(i < items2.Count ? items2[i] : null) });
                MarkPrefixDirtyFrom(0);
            }

            var width = double.IsPositiveInfinity(availableSize.Width) ? 800 : availableSize.Width;
            var widthChanged = Math.Abs(width - _lastMeasureWidth) > 0.5;

            if (widthChanged)
            {
                _lastMeasureWidth = width;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].HasBeenMeasured)
                    {
                        var s = _slots[i];
                        s.MeasuredAtWidth = -1;
                        _slots[i] = s;
                    }
                }
                MarkPrefixDirtyFrom(0);
            }

            if (_scrollViewer is not null)
            {
                _scrollOffset = _scrollViewer.Offset.Y;
                _viewportHeight = _scrollViewer.Viewport.Height;
                _scrollExtentPadding = Math.Max(0, _scrollViewer.Extent.Height - _cachedTotalHeight);
            }

            // Detect if user is at/near the bottom BEFORE measurements change heights.
            // Used by ApplyAnchorOrBottomPin to implement bottom-stickiness.
            // Only apply when new items were added — NOT when existing items grew
            // (e.g., expanding a tool group), to avoid the viewport shifting and
            // collapsing the visual gap above the expanding item.
            if (itemsAdded && _scrollViewer is not null && _cachedTotalHeight + _scrollExtentPadding > _viewportHeight)
                _wasNearBottom = _scrollOffset >= _cachedTotalHeight + _scrollExtentPadding - _viewportHeight - 2;
            else
                _wasNearBottom = false;

            // -- Capture scroll anchor BEFORE any height changes --
            CaptureAnchor();

            var vpH = _viewportHeight > 0 ? _viewportHeight : 800;
            var (visFirst, visLast) = ComputeRange(vpH * VisibleBufferRatio);
            var (warmFirst, warmLast) = ComputeRange(vpH * WarmBufferRatio);

            _isJump = _firstInTree < 0
                || Math.Abs(_scrollOffset - _prevScrollOffset) > vpH * 2;
            _prevScrollOffset = _scrollOffset;
            int maxRealize = _isJump ? MaxRealizeOnJump : MaxRealizePerFrame;

            // Remove cold items (outside warm zone) — return to recycle pool
            RemoveColdItems(warmFirst, warmLast);

            // Realize/toggle visibility for warm+visible zone
            var constraint = new Size(width, double.PositiveInfinity);
            var items = Items;
            int newlyRealized = 0;

            for (int i = warmFirst; i <= warmLast && i < _slots.Count; i++)
            {
                bool shouldBeVisible = i >= visFirst && i <= visLast;
                var slot = _slots[i];

                if (slot.Element is null)
                {
                    if (newlyRealized >= maxRealize)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(_cachedInvalidateMeasure!,
                            Avalonia.Threading.DispatcherPriority.Background);
                        continue;
                    }

                    if (i >= items.Count) continue;
                    var element = RealizeContainer(i);
                    if (element is null) continue;

                    slot.Element = element;
                    element.IsVisible = shouldBeVisible;
                    element.Measure(constraint);
                    var measuredH = element.DesiredSize.Height;
                    UpdateHeightOnMeasure(i, items[i], measuredH, width, ref slot);
                    _slots[i] = slot;
                    newlyRealized++;
                }
                else
                {
                    if (slot.Element.IsVisible != shouldBeVisible)
                        slot.Element.IsVisible = shouldBeVisible;

                    // Re-measure if width changed or child invalidated itself.
                    // Warm-only items with valid measure are skipped to reduce
                    // per-frame work during pure scrolling.
                    bool needsMeasure = widthChanged
                        || Math.Abs(slot.MeasuredAtWidth - width) > 0.5
                        || !slot.Element.IsMeasureValid;

                    if (needsMeasure && (shouldBeVisible || !slot.Element.IsMeasureValid))
                    {
                        slot.Element.Measure(constraint);
                        slot.MeasuredAtWidth = width;
                    }

                    // Check for height drift — covers both freshly re-measured
                    // containers and children that re-measured themselves between
                    // our layout passes.
                    if (shouldBeVisible || needsMeasure)
                    {
                        var h = slot.Element.DesiredSize.Height;
                        if (Math.Abs(h - slot.CachedHeight) > 0.5)
                        {
                            UpdateSlotHeight(i, items[i], h, width, ref slot);
                            _slots[i] = slot;
                        }
                    }
                }
            }

            // Maintain _firstInTree/_lastInTree incrementally
            UpdateInTreeRangeAfterLayout(warmFirst, warmLast);
            EnsurePrefixHeights();

            // -- Apply scroll anchor / bottom-pin AFTER heights updated --
            ApplyAnchorOrBottomPin();

            return new Size(width, _cachedTotalHeight);
        }
        finally
        {
            _isInLayout = false;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_firstInTree < 0 || _slots.Count == 0)
            return finalSize;

        EnsurePrefixHeights();
        var width = finalSize.Width;

        for (int i = _firstInTree; i <= _lastInTree && i < _slots.Count; i++)
        {
            if (_slots[i].Element is { } el)
                el.Arrange(new Rect(0, _prefixHeights[i], width, _slots[i].CachedHeight));
        }

        return finalSize;
    }

    // =======================================================================
    //  Scroll Anchor Correction
    // =======================================================================

    /// <summary>
    /// Records the first fully-visible item and its pixel offset from the
    /// viewport top so we can compensate for height changes after layout.
    /// </summary>
    private void CaptureAnchor()
    {
        if (_suppressAnchorCorrection || IsBottomPinned || _slots.Count == 0 || _scrollViewer is null)
        {
            _anchorIndex = -1;
            return;
        }

        EnsurePrefixHeights();
        _anchorIndex = FindFirstSlotBelow(_scrollOffset);
        if (_anchorIndex >= 0 && _anchorIndex < _slots.Count)
            _anchorOffsetInViewport = _prefixHeights[_anchorIndex] - _scrollOffset;
        else
            _anchorIndex = -1;
    }

    /// <summary>
    /// Handles bottom-pin (streaming), bottom-stickiness (user scrolled to end),
    /// and scroll-anchor correction (keeping visible content stable when item
    /// heights above the viewport change due to measurement or streaming).
    /// </summary>
    private void ApplyAnchorOrBottomPin()
    {
        if (_scrollViewer is null) return;

        // Bottom-pinned mode (explicit) or bottom stickiness (user was at bottom):
        // Pin viewport to extent bottom so content stays anchored at the end.
        if (IsBottomPinned || _wasNearBottom)
        {
            var maxOff = Math.Max(0, _cachedTotalHeight + _scrollExtentPadding - _viewportHeight);
            if (Math.Abs(_scrollViewer.Offset.Y - maxOff) > 0.5)
            {
                _suppressAnchorCorrection = true;
                _scrollOffset = maxOff;
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, maxOff);
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    _cachedClearSuppressAnchor!,
                    Avalonia.Threading.DispatcherPriority.Loaded);
            }
            return;
        }

        // Skip anchor correction for jump-scrolls (scrollbar drag, fast fling).
        // After a jump, many items are newly realized with unreliable anchors.
        if (_isJump) return;

        if (_anchorIndex < 0 || _suppressAnchorCorrection) return;
        if ((uint)_anchorIndex >= (uint)_slots.Count) return;

        var newAnchorY = _prefixHeights[_anchorIndex];
        var expectedAnchorY = _scrollOffset + _anchorOffsetInViewport;
        var delta = newAnchorY - expectedAnchorY;

        // Apply correction for any measurable shift (sub-pixel dead zone only).
        // No per-frame cap — corrections are naturally bounded by MaxRealizePerFrame
        // and keeping content stable is always better than letting it jump.
        if (Math.Abs(delta) > 0.5)
        {
            var newOffset = Math.Clamp(
                _scrollOffset + delta,
                0,
                Math.Max(0, _cachedTotalHeight + _scrollExtentPadding - _viewportHeight));

            _suppressAnchorCorrection = true;
            _scrollOffset = newOffset;
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, newOffset);
            Avalonia.Threading.Dispatcher.UIThread.Post(
                _cachedClearSuppressAnchor!,
                Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    // =======================================================================
    //  Height Oracle
    // =======================================================================

    /// <summary>
    /// Returns the best height estimate for a data item that has not yet been
    /// measured at the current width. Checks (1) the WeakTable cache, then
    /// (2) role-based running averages, then (3) static role defaults.
    /// </summary>
    private double EstimateHeightForItem(object? item)
    {
        // 1. WeakTable lookup (survives container recycling)
        if (item is not null && HeightCache.TryGetValue(item, out var record))
        {
            if (_lastMeasureWidth <= 0 || Math.Abs(record.MeasuredAtWidth - _lastMeasureWidth) < 1)
                return record.Height;
        }

        // 2. Role-based running average (from measured items of the same role)
        var roleIndex = GetRoleIndex(item);
        if (roleIndex >= 0 && _roleHeightCounts[roleIndex] > 0)
            return _roleHeightSums[roleIndex] / _roleHeightCounts[roleIndex];

        // 3. Static role defaults
        return roleIndex switch
        {
            0 => EstimateAssistant,
            1 => EstimateUser,
            2 => EstimateSystem,
            3 => EstimateTool,
            _ => _measuredHeightCount > 0 ? _measuredHeightSum / _measuredHeightCount : EstimateFallback,
        };
    }

    /// <summary>Maps a data item to its StrataChatRole index (0-3), or -1 if unknown.</summary>
    private static int GetRoleIndex(object? item)
    {
        if (item is StrataChatMessage msg)
            return (int)msg.Role;
        return -1;
    }

    /// <summary>
    /// Updates all height tracking state when a container is measured for the first time
    /// or remeasured with a new height.
    /// </summary>
    private void UpdateHeightOnMeasure(int slotIndex, object? item, double height, double width, ref ItemSlot slot)
    {
        var heightChanged = Math.Abs(slot.CachedHeight - height) > 0.5;

        if (!slot.HasBeenMeasured)
        {
            // First measurement: update running averages for future estimates.
            _measuredHeightSum += height;
            _measuredHeightCount++;
            var ri = GetRoleIndex(item);
            if (ri >= 0)
            {
                _roleHeightSums[ri] += height;
                _roleHeightCounts[ri]++;
            }
        }
        else if (heightChanged)
        {
            // Re-measurement with changed height (streaming, reflow).
            _measuredHeightSum += height - slot.CachedHeight;
            var ri = GetRoleIndex(item);
            if (ri >= 0)
                _roleHeightSums[ri] += height - slot.CachedHeight;
        }

        if (heightChanged)
        {
            slot.CachedHeight = height;
            MarkPrefixDirtyFrom(slotIndex);
        }

        slot.HasBeenMeasured = true;
        slot.MeasuredAtWidth = width;

        CacheHeight(item, height, width);
    }

    /// <summary>
    /// Updates slot height for an already-measured container whose height changed
    /// (e.g. during streaming).
    /// </summary>
    private void UpdateSlotHeight(int slotIndex, object? item, double height, double width, ref ItemSlot slot)
    {
        _measuredHeightSum += height - slot.CachedHeight;
        var ri = GetRoleIndex(item);
        if (ri >= 0)
            _roleHeightSums[ri] += height - slot.CachedHeight;

        slot.CachedHeight = height;
        slot.MeasuredAtWidth = width;
        MarkPrefixDirtyFrom(slotIndex);

        CacheHeight(item, height, width);
    }

    /// <summary>
    /// Removes the height contribution of a slot being removed or replaced,
    /// keeping running estimates O(1).
    /// </summary>
    private void RemoveSlotHeightFromEstimate(int slotIndex)
    {
        if (slotIndex >= _slots.Count) return;
        var slot = _slots[slotIndex];
        if (!slot.HasBeenMeasured) return;

        _measuredHeightSum -= slot.CachedHeight;
        _measuredHeightCount--;
        var item = slotIndex < Items.Count ? Items[slotIndex] : null;
        var ri = GetRoleIndex(item);
        if (ri >= 0)
        {
            _roleHeightSums[ri] -= slot.CachedHeight;
            _roleHeightCounts[ri]--;
        }
    }

    // =======================================================================
    //  Prefix Heights (incremental rebuild)
    // =======================================================================

    /// <summary>
    /// Marks prefix heights dirty from <paramref name="fromIndex"/> onward.
    /// The next <see cref="EnsurePrefixHeights"/> call rebuilds only the
    /// affected range instead of the entire array.
    /// </summary>
    private void MarkPrefixDirtyFrom(int fromIndex)
    {
        _prefixDirty = true;
        if (fromIndex < _prefixDirtyFrom)
            _prefixDirtyFrom = fromIndex;
    }

    private void EnsurePrefixHeights()
    {
        var count = _slots.Count;
        var requiredLen = count + 1;

        if (!_prefixDirty && _prefixHeights.Length >= requiredLen)
            return;

        if (_prefixHeights.Length < requiredLen)
        {
            // Geometric growth: at least double, minimum 64 elements
            var newLen = Math.Max(requiredLen, Math.Max(64, _prefixHeights.Length * 2));
            var newArr = new double[newLen];
            // Copy the still-valid prefix portion (0.._prefixDirtyFrom inclusive,
            // because the rebuild loop reads _prefixHeights[start] as a base).
            var validCount = Math.Min(_prefixDirtyFrom + 1, _prefixHeights.Length);
            if (validCount > 0)
                Array.Copy(_prefixHeights, newArr, validCount);
            _prefixHeights = newArr;
            if (_prefixDirtyFrom > validCount - 1)
                _prefixDirtyFrom = Math.Max(0, validCount - 1);
        }

        var spacing = Spacing;
        var start = Math.Max(0, Math.Min(_prefixDirtyFrom, count));

        if (start == 0)
            _prefixHeights[0] = 0;

        for (int i = start; i < count; i++)
        {
            var gap = i < count - 1 ? spacing : 0;
            _prefixHeights[i + 1] = _prefixHeights[i] + _slots[i].CachedHeight + gap;
        }

        _cachedTotalHeight = count > 0 ? _prefixHeights[count] : 0;
        _prefixDirty = false;
        _prefixDirtyFrom = int.MaxValue;
    }

    // =======================================================================
    //  Binary Search Helpers
    // =======================================================================

    /// <summary>
    /// Binary searches for the first slot whose bottom edge > y.
    /// Caller MUST call <see cref="EnsurePrefixHeights"/> before invoking.
    /// </summary>
    private int FindFirstSlotBelow(double y)
    {
        var slotCount = _slots.Count;
        if (slotCount == 0) return 0;
        int lo = 0, hi = slotCount - 1, result = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if ((uint)(mid + 1) < (uint)_prefixHeights.Length && _prefixHeights[mid + 1] > y)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return result;
    }

    /// <summary>
    /// Binary searches for the last slot whose top edge &lt;= y.
    /// Caller MUST call <see cref="EnsurePrefixHeights"/> before invoking.
    /// </summary>
    private int FindLastSlotAbove(double y)
    {
        var slotCount = _slots.Count;
        if (slotCount == 0) return 0;
        int lo = 0, hi = slotCount - 1, result = slotCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if ((uint)mid < (uint)_prefixHeights.Length && _prefixHeights[mid] <= y)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }

    /// <summary>
    /// Computes the index range of items that overlap (scrollOffset ± bufferPx).
    /// Caller MUST call <see cref="EnsurePrefixHeights"/> before invoking.
    /// </summary>
    private (int first, int last) ComputeRange(double bufferPx)
    {
        if (_slots.Count == 0) return (0, -1);
        EnsurePrefixHeights();

        var vpH = _viewportHeight > 0 ? _viewportHeight : 800;
        var rangeTop = Math.Max(0, _scrollOffset - bufferPx);
        var rangeBottom = _scrollOffset + vpH + bufferPx;

        var first = FindFirstSlotBelow(rangeTop);
        var last = FindLastSlotAbove(rangeBottom);

        first = Math.Max(0, first - BufferItems);
        last = Math.Min(_slots.Count - 1, last + BufferItems);

        return (first, last);
    }

    // =======================================================================
    //  Zone Management
    // =======================================================================

    private void RemoveColdItems(int keepFirst, int keepLast)
    {
        for (int i = Math.Max(0, _firstInTree); i < keepFirst && i < _slots.Count; i++)
        {
            if (_slots[i].Element is { } el)
            {
                ReturnToPool(el, i);
                var s = _slots[i];
                s.Element = null;
                _slots[i] = s;
            }
        }
        for (int i = Math.Min(_slots.Count - 1, _lastInTree); i > keepLast && i >= 0; i--)
        {
            if (_slots[i].Element is { } el)
            {
                ReturnToPool(el, i);
                var s = _slots[i];
                s.Element = null;
                _slots[i] = s;
            }
        }
    }

    private Control? EnsureInTree(int index, bool visible)
    {
        if ((uint)index >= (uint)_slots.Count) return null;
        if (_slots[index].Element is { } existing)
        {
            if (visible) existing.IsVisible = true;
            return existing;
        }

        var items = Items;
        if (index >= items.Count) return null;
        var element = RealizeContainer(index);
        if (element is null) return null;

        var slot = _slots[index];
        slot.Element = element;
        element.IsVisible = visible;

        var w = _lastMeasureWidth > 0 ? _lastMeasureWidth : Bounds.Width;
        element.Measure(new Size(w, double.PositiveInfinity));
        UpdateHeightOnMeasure(index, items[index], element.DesiredSize.Height, w, ref slot);
        _slots[index] = slot;

        if (_firstInTree < 0 || index < _firstInTree) _firstInTree = index;
        if (index > _lastInTree) _lastInTree = index;

        return element;
    }

    /// <summary>
    /// Fast incremental update of <c>_firstInTree</c>/<c>_lastInTree</c>
    /// based on the current warm zone bounds, avoiding a full O(n) scan.
    /// </summary>
    private void UpdateInTreeRangeAfterLayout(int warmFirst, int warmLast)
    {
        // After layout, in-tree items are exactly those in [warmFirst..warmLast]
        // that have a non-null element (some may have been skipped due to
        // maxRealize throttling). Scan only within that range.
        int newFirst = -1, newLast = -1;
        var lo = Math.Max(0, Math.Min(warmFirst, _firstInTree >= 0 ? _firstInTree : warmFirst));
        var hi = Math.Min(_slots.Count - 1, Math.Max(warmLast, _lastInTree >= 0 ? _lastInTree : warmLast));
        for (int i = lo; i <= hi; i++)
        {
            if (_slots[i].Element is not null)
            {
                if (newFirst < 0) newFirst = i;
                newLast = i;
            }
        }
        _firstInTree = newFirst;
        _lastInTree = newLast;
    }

    private void RecalcInTreeRange()
    {
        _firstInTree = -1;
        _lastInTree = -1;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Element is not null)
            {
                if (_firstInTree < 0) _firstInTree = i;
                _lastInTree = i;
            }
        }
    }

    // =======================================================================
    //  Container Recycling Pool
    // =======================================================================

    private Control? RealizeContainer(int index)
    {
        var items = Items;
        if ((uint)index >= (uint)items.Count) return null;
        var item = items[index];
        if (item is null) return null;

        var generator = ItemContainerGenerator;
        if (generator is null) return null;

        Control container;
        object? recycleKey = null;

        if (generator.NeedsContainer(item, index, out recycleKey))
        {
            // Try to pull from recycling pool
            if (recycleKey is not null &&
                _recyclePool.TryGetValue(recycleKey, out var pool) &&
                pool.Count > 0)
            {
                container = pool.Pop();
            }
            else
            {
                container = generator.CreateContainer(item, index, recycleKey);
            }
        }
        else
        {
            container = (Control)item;
        }

        generator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);

        // Store the recycle key in the slot
        var slot = _slots[index];
        slot.RecycleKey = recycleKey;
        _slots[index] = slot;

        return container;
    }

    /// <summary>
    /// Returns a container to the recycling pool instead of destroying it.
    /// </summary>
    private void ReturnToPool(Control container, int index)
    {
        var recycleKey = (uint)index < (uint)_slots.Count ? _slots[index].RecycleKey : null;
        ItemContainerGenerator?.ClearItemContainer(container);
        RemoveInternalChild(container);

        if (recycleKey is not null)
        {
            if (!_recyclePool.TryGetValue(recycleKey, out var pool))
            {
                pool = new Stack<Control>();
                _recyclePool[recycleKey] = pool;
            }
            if (pool.Count < MaxRecyclePoolPerKey)
                pool.Push(container);
        }
    }

    private void ClearRecyclePool()
    {
        foreach (var pool in _recyclePool.Values)
            pool.Clear();
        _recyclePool.Clear();
    }

    private void ClearSuppressAnchorCorrection() => _suppressAnchorCorrection = false;

    /// <summary>
    /// Persists a measured height in the <see cref="HeightCache"/> WeakTable
    /// so the value survives container recycling.
    /// </summary>
    private static void CacheHeight(object? item, double height, double width)
    {
        if (item is null) return;
        if (HeightCache.TryGetValue(item, out var rec))
        {
            rec.Height = height;
            rec.MeasuredAtWidth = width;
        }
        else
        {
            HeightCache.AddOrUpdate(item, new HeightRecord { Height = height, MeasuredAtWidth = width });
        }
    }
}

// =======================================================================
//  StrataChatPanelAlgorithms: testable core logic extracted from the panel
// =======================================================================

/// <summary>
/// Standalone testable algorithms for <see cref="StrataChatPanel"/>:
/// prefix-sum height array, binary search, height estimation, and slot management.
/// This class carries no Avalonia dependencies and can be instantiated in headless tests.
/// </summary>
internal sealed class StrataChatPanelAlgorithms
{
    // Mirrors the role-based defaults in StrataChatPanel
    private const double EstimateUser = 60;
    private const double EstimateAssistant = 300;
    private const double EstimateSystem = 80;
    private const double EstimateTool = 120;
    private const double EstimateFallback = 120;

    public struct Slot
    {
        public double CachedHeight;
        public bool HasBeenMeasured;
        public double MeasuredAtWidth;
        public int RoleIndex; // 0=Assistant,1=User,2=System,3=Tool,-1=unknown
    }

    public readonly List<Slot> Slots = new();
    public double Spacing;
    public double LastMeasureWidth = -1;

    // Prefix-sum with geometric growth
    private double[] _prefixHeights = Array.Empty<double>();
    private bool _prefixDirty = true;
    private int _prefixDirtyFrom = int.MaxValue;
    public double CachedTotalHeight;

    // Running estimates
    public double MeasuredHeightSum;
    public int MeasuredHeightCount;
    public readonly double[] RoleHeightSums = new double[4];
    public readonly int[] RoleHeightCounts = new int[4];

    /// <summary>Number of times the prefix array was reallocated (for testing geometric growth).</summary>
    public int PrefixReallocCount;

    // -- Slot management --

    public void AddSlot(double height, int roleIndex = -1)
    {
        Slots.Add(new Slot { CachedHeight = height, RoleIndex = roleIndex });
        MarkPrefixDirtyFrom(Slots.Count - 1);
    }

    public void InsertSlot(int index, double height, int roleIndex = -1)
    {
        Slots.Insert(index, new Slot { CachedHeight = height, RoleIndex = roleIndex });
        MarkPrefixDirtyFrom(index);
    }

    public void RemoveSlot(int index)
    {
        var slot = Slots[index];
        if (slot.HasBeenMeasured)
        {
            MeasuredHeightSum -= slot.CachedHeight;
            MeasuredHeightCount--;
            if (slot.RoleIndex >= 0 && slot.RoleIndex < 4)
            {
                RoleHeightSums[slot.RoleIndex] -= slot.CachedHeight;
                RoleHeightCounts[slot.RoleIndex]--;
            }
        }
        Slots.RemoveAt(index);
        MarkPrefixDirtyFrom(index);
    }

    public void Clear()
    {
        Slots.Clear();
        MeasuredHeightSum = 0;
        MeasuredHeightCount = 0;
        Array.Clear(RoleHeightSums);
        Array.Clear(RoleHeightCounts);
        _prefixDirty = true;
        _prefixDirtyFrom = 0;
    }

    /// <summary>Simulates measuring a slot: sets its height and updates estimates.</summary>
    public void MeasureSlot(int index, double measuredHeight, double width)
    {
        var slot = Slots[index];
        var heightChanged = Math.Abs(slot.CachedHeight - measuredHeight) > 0.5;

        if (!slot.HasBeenMeasured)
        {
            MeasuredHeightSum += measuredHeight;
            MeasuredHeightCount++;
            if (slot.RoleIndex >= 0 && slot.RoleIndex < 4)
            {
                RoleHeightSums[slot.RoleIndex] += measuredHeight;
                RoleHeightCounts[slot.RoleIndex]++;
            }
        }
        else if (heightChanged)
        {
            MeasuredHeightSum += measuredHeight - slot.CachedHeight;
            if (slot.RoleIndex >= 0 && slot.RoleIndex < 4)
                RoleHeightSums[slot.RoleIndex] += measuredHeight - slot.CachedHeight;
        }

        if (heightChanged)
        {
            slot.CachedHeight = measuredHeight;
            MarkPrefixDirtyFrom(index);
        }
        slot.HasBeenMeasured = true;
        slot.MeasuredAtWidth = width;
        Slots[index] = slot;
    }

    // -- Height estimation --

    public double EstimateHeightForRole(int roleIndex)
    {
        if (roleIndex >= 0 && roleIndex < 4 && RoleHeightCounts[roleIndex] > 0)
            return RoleHeightSums[roleIndex] / RoleHeightCounts[roleIndex];

        return roleIndex switch
        {
            0 => EstimateAssistant,
            1 => EstimateUser,
            2 => EstimateSystem,
            3 => EstimateTool,
            _ => MeasuredHeightCount > 0 ? MeasuredHeightSum / MeasuredHeightCount : EstimateFallback,
        };
    }

    // -- Prefix heights --

    public void MarkPrefixDirtyFrom(int fromIndex)
    {
        _prefixDirty = true;
        if (fromIndex < _prefixDirtyFrom)
            _prefixDirtyFrom = fromIndex;
    }

    public void EnsurePrefixHeights()
    {
        var count = Slots.Count;
        var requiredLen = count + 1;

        if (!_prefixDirty && _prefixHeights.Length >= requiredLen)
            return;

        if (_prefixHeights.Length < requiredLen)
        {
            var newLen = Math.Max(requiredLen, Math.Max(64, _prefixHeights.Length * 2));
            var newArr = new double[newLen];
            var validCount = Math.Min(_prefixDirtyFrom + 1, _prefixHeights.Length);
            if (validCount > 0)
                Array.Copy(_prefixHeights, newArr, validCount);
            _prefixHeights = newArr;
            if (_prefixDirtyFrom > validCount - 1)
                _prefixDirtyFrom = Math.Max(0, validCount - 1);
            PrefixReallocCount++;
        }

        var spacing = Spacing;
        var start = Math.Max(0, Math.Min(_prefixDirtyFrom, count));
        if (start == 0)
            _prefixHeights[0] = 0;

        for (int i = start; i < count; i++)
        {
            var gap = i < count - 1 ? spacing : 0;
            _prefixHeights[i + 1] = _prefixHeights[i] + Slots[i].CachedHeight + gap;
        }

        CachedTotalHeight = count > 0 ? _prefixHeights[count] : 0;
        _prefixDirty = false;
        _prefixDirtyFrom = int.MaxValue;
    }

    public double GetSlotTop(int index)
    {
        EnsurePrefixHeights();
        return _prefixHeights[index];
    }

    public double GetSlotBottom(int index)
    {
        EnsurePrefixHeights();
        return _prefixHeights[index + 1];
    }

    public int PrefixArrayLength => _prefixHeights.Length;

    // -- Binary search --

    public int FindFirstSlotBelow(double y)
    {
        EnsurePrefixHeights();
        int lo = 0, hi = Slots.Count - 1, result = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_prefixHeights[mid + 1] > y)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return result;
    }

    public int FindLastSlotAbove(double y)
    {
        EnsurePrefixHeights();
        int lo = 0, hi = Slots.Count - 1, result = Slots.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_prefixHeights[mid] <= y)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }

    public (int first, int last) ComputeRange(double scrollOffset, double viewportHeight, double bufferPx, int bufferItems = 5)
    {
        if (Slots.Count == 0) return (0, -1);
        EnsurePrefixHeights();

        var vpH = viewportHeight > 0 ? viewportHeight : 800;
        var rangeTop = Math.Max(0, scrollOffset - bufferPx);
        var rangeBottom = scrollOffset + vpH + bufferPx;

        var first = FindFirstSlotBelow(rangeTop);
        var last = FindLastSlotAbove(rangeBottom);

        first = Math.Max(0, first - bufferItems);
        last = Math.Min(Slots.Count - 1, last + bufferItems);

        return (first, last);
    }
}
