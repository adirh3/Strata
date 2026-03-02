using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// A 3-zone virtualizing panel for chat messages:
///   Visible zone  (viewport +/- 1x): Items are in the tree, IsVisible=true -> rendered normally.
///   Warm zone     (1x-3x viewport): Items in the tree, IsVisible=false -> compositor skips them,
///                                    but re-showing is instant (no AddChild/Measure cost).
///   Cold zone     (beyond 3x):      Items removed from the tree entirely -> zero overhead.
///
/// Physical scrolling: parent ScrollViewer handles viewport clipping; the panel
/// reports its full stacked height as DesiredSize. Items are arranged at absolute
/// Y positions within that extent.
/// </summary>
public class StrataChatPanel : VirtualizingPanel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<StrataChatPanel, double>(nameof(Spacing), 0);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    static StrataChatPanel()
    {
        AffectsMeasure<StrataChatPanel>(SpacingProperty);
    }

    private const int BufferItems = 5;
    private const double VisibleBufferRatio = 1.0;
    private const double WarmBufferRatio = 3.0;
    private const int MaxRealizePerFrame = 8;
    private const int MaxRealizeOnJump = 40;

    private readonly List<ItemSlot> _slots = new();
    private double _lastMeasureWidth = -1;
    private double _estimatedItemHeight = 120;
    private int _firstInTree = -1;
    private int _lastInTree = -1;
    private bool _isInLayout;
    private double _cachedTotalHeight;

    // Prefix-sum array: _prefixHeights[i] = sum of CachedHeight for slots 0..i-1.
    // _prefixHeights[_slots.Count] = total height. Enables O(log n) range lookups.
    private double[] _prefixHeights = Array.Empty<double>();
    private bool _prefixDirty = true;

    private ScrollViewer? _scrollViewer;
    private double _scrollOffset;
    private double _viewportHeight;
    private double _prevScrollOffset;

    private struct ItemSlot
    {
        public Control? Element;       // non-null = in the visual tree (visible or hidden)
        public double CachedHeight;
        public bool HasBeenMeasured;
        public double MeasuredAtWidth;
    }

    // -- Visual-tree hooks --

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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

        if (Math.Abs(newOffset - _scrollOffset) > 20 ||
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

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                var newCount = e.NewItems!.Count;
                var newSlots = new ItemSlot[newCount];
                for (int i = 0; i < newCount; i++)
                    newSlots[i] = new ItemSlot { CachedHeight = _estimatedItemHeight };
                _slots.InsertRange(e.NewStartingIndex, newSlots);
                if (_firstInTree >= 0 && e.NewStartingIndex <= _firstInTree)
                {
                    _firstInTree += newCount;
                    _lastInTree += newCount;
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                for (int i = e.OldItems!.Count - 1; i >= 0; i--)
                {
                    var idx = e.OldStartingIndex + i;
                    if (idx < _slots.Count)
                    {
                        if (_slots[idx].Element is { } el)
                            UnrealizeContainer(el, idx);
                        _slots.RemoveAt(idx);
                    }
                }
                RecalcInTreeRange();
                break;

            case NotifyCollectionChangedAction.Replace:
                for (int i = 0; i < e.NewItems!.Count; i++)
                {
                    var idx = e.NewStartingIndex + i;
                    if (idx < _slots.Count)
                    {
                        if (_slots[idx].Element is { } el)
                            UnrealizeContainer(el, idx);
                        _slots[idx] = new ItemSlot { CachedHeight = _estimatedItemHeight };
                    }
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Element is { } el)
                        UnrealizeContainer(el, i);
                }
                _slots.Clear();
                _firstInTree = -1;
                _lastInTree = -1;
                for (int i = 0; i < items.Count; i++)
                    _slots.Add(new ItemSlot { CachedHeight = _estimatedItemHeight });
                break;
        }

        InvalidateMeasure();
        _prefixDirty = true;
    }

    // -- Measure / Arrange --

    protected override Size MeasureOverride(Size availableSize)
    {
        _isInLayout = true;
        try
        {
            // Sync slots with Items count - items may have been added before
            // the panel was attached to the visual tree (e.g. detached build).
            var itemCount = Items.Count;
            if (_slots.Count != itemCount)
            {
                // Full resync: clear all realized elements, rebuild slot list
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Element is { } el)
                        UnrealizeContainer(el, i);
                }
                _slots.Clear();
                _firstInTree = -1;
                _lastInTree = -1;
                for (int i = 0; i < itemCount; i++)
                    _slots.Add(new ItemSlot { CachedHeight = _estimatedItemHeight });
                _prefixDirty = true;
            }

            var width = double.IsPositiveInfinity(availableSize.Width) ? 800 : availableSize.Width;
            var widthChanged = Math.Abs(width - _lastMeasureWidth) > 0.5;

            if (widthChanged)
            {
                _lastMeasureWidth = width;
                _prefixDirty = true;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].HasBeenMeasured)
                    {
                        var s = _slots[i];
                        s.MeasuredAtWidth = -1;
                        _slots[i] = s;
                    }
                }
            }

            if (_scrollViewer is not null)
            {
                _scrollOffset = _scrollViewer.Offset.Y;
                _viewportHeight = _scrollViewer.Viewport.Height;
            }

            var vpH = _viewportHeight > 0 ? _viewportHeight : 800;
            var (visFirst, visLast) = ComputeRange(vpH * VisibleBufferRatio);
            var (warmFirst, warmLast) = ComputeRange(vpH * WarmBufferRatio);

            // Detect large jumps or initial population - allow more realizes in one frame
            bool isJump = _firstInTree < 0
                || Math.Abs(_scrollOffset - _prevScrollOffset) > vpH * 2;
            _prevScrollOffset = _scrollOffset;
            int maxRealize = isJump ? MaxRealizeOnJump : MaxRealizePerFrame;

            // Phase 1: Remove cold items (outside warm zone) from tree entirely
            RemoveColdItems(warmFirst, warmLast);

            // Phase 2: Hide warm-only items (in warm zone but outside visible zone)
            // Phase 3: Show/realize visible items
            var constraint = new Size(width, double.PositiveInfinity);
            var items = Items;
            int newlyRealized = 0;

            for (int i = warmFirst; i <= warmLast && i < _slots.Count; i++)
            {
                bool shouldBeVisible = i >= visFirst && i <= visLast;
                var slot = _slots[i];

                if (slot.Element is null)
                {
                    // Not in tree - need to realize it
                    if (newlyRealized >= maxRealize)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateMeasure,
                            Avalonia.Threading.DispatcherPriority.Background);
                        continue; // skip, will be realized next frame
                    }

                    if (i >= items.Count) continue;
                    var element = RealizeContainer(i);
                    if (element is null) continue;

                    slot.Element = element;
                    element.IsVisible = shouldBeVisible;
                    element.Measure(constraint);
                    slot.CachedHeight = element.DesiredSize.Height;
                    slot.HasBeenMeasured = true;
                    slot.MeasuredAtWidth = width;
                    _slots[i] = slot;
                    _prefixDirty = true;
                    newlyRealized++;
                }
                else
                {
                    // Already in tree - just toggle visibility
                    if (slot.Element.IsVisible != shouldBeVisible)
                        slot.Element.IsVisible = shouldBeVisible;

                    // Re-measure if width changed or child invalidated.
                    // Always compare DesiredSize with cache - Avalonia's layout
                    // manager may have re-measured the child (e.g. during streaming)
                    // before our MeasureOverride runs, making IsMeasureValid true
                    // while CachedHeight is stale.
                    if (widthChanged || Math.Abs(slot.MeasuredAtWidth - width) > 0.5
                        || !slot.Element.IsMeasureValid)
                    {
                        slot.Element.Measure(constraint);
                        slot.MeasuredAtWidth = width;
                    }

                    var h = slot.Element.DesiredSize.Height;
                    if (Math.Abs(h - slot.CachedHeight) > 0.5)
                    {
                        slot.CachedHeight = h;
                        _slots[i] = slot;
                        _prefixDirty = true;
                    }
                }
            }

            RecalcInTreeRange();
            UpdateHeightEstimate();
            EnsurePrefixHeights();

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

        // Arrange all in-tree items at absolute positions using prefix sums
        for (int i = _firstInTree; i <= _lastInTree && i < _slots.Count; i++)
        {
            if (_slots[i].Element is { } el)
                el.Arrange(new Rect(0, _prefixHeights[i], width, _slots[i].CachedHeight));
        }

        return finalSize;
    }

    // -- Private helpers --

    /// <summary>
    /// Rebuilds the prefix height array if dirty.
    /// </summary>
    private void EnsurePrefixHeights()
    {
        if (!_prefixDirty && _prefixHeights.Length == _slots.Count + 1)
            return;

        if (_prefixHeights.Length != _slots.Count + 1)
            _prefixHeights = new double[_slots.Count + 1];

        var spacing = Spacing;
        _prefixHeights[0] = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            var gap = i < _slots.Count - 1 ? spacing : 0;
            _prefixHeights[i + 1] = _prefixHeights[i] + _slots[i].CachedHeight + gap;
        }

        _cachedTotalHeight = _prefixHeights[_slots.Count];
        _prefixDirty = false;
    }

    /// <summary>
    /// Binary searches for the first slot whose bottom edge > y.
    /// </summary>
    private int FindFirstSlotBelow(double y)
    {
        EnsurePrefixHeights();
        int lo = 0, hi = _slots.Count - 1, result = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_prefixHeights[mid + 1] > y) // bottom edge of mid
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
    /// Binary searches for the last slot whose top edge <= y.
    /// </summary>
    private int FindLastSlotAbove(double y)
    {
        EnsurePrefixHeights();
        int lo = 0, hi = _slots.Count - 1, result = _slots.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_prefixHeights[mid] <= y) // top edge of mid
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
    /// Computes the index range of items that overlap (scrollOffset +/- bufferPx).
    /// Uses binary search on prefix sums: O(log n).
    /// </summary>
    private (int first, int last) ComputeRange(double bufferPx)
    {
        if (_slots.Count == 0) return (0, -1);

        var vpH = _viewportHeight > 0 ? _viewportHeight : 800;
        var rangeTop = Math.Max(0, _scrollOffset - bufferPx);
        var rangeBottom = _scrollOffset + vpH + bufferPx;

        var first = FindFirstSlotBelow(rangeTop);
        var last = FindLastSlotAbove(rangeBottom);

        first = Math.Max(0, first - BufferItems);
        last = Math.Min(_slots.Count - 1, last + BufferItems);

        return (first, last);
    }

    /// <summary>
    /// Removes items outside the warm range from the tree.
    /// </summary>
    private void RemoveColdItems(int keepFirst, int keepLast)
    {
        for (int i = Math.Max(0, _firstInTree); i < keepFirst && i < _slots.Count; i++)
        {
            if (_slots[i].Element is { } el)
            {
                UnrealizeContainer(el, i);
                var s = _slots[i];
                s.Element = null;
                _slots[i] = s;
            }
        }
        for (int i = Math.Min(_slots.Count - 1, _lastInTree); i > keepLast && i >= 0; i--)
        {
            if (_slots[i].Element is { } el)
            {
                UnrealizeContainer(el, i);
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
        slot.CachedHeight = element.DesiredSize.Height;
        slot.HasBeenMeasured = true;
        slot.MeasuredAtWidth = w;
        _slots[index] = slot;
        _prefixDirty = true;

        if (_firstInTree < 0 || index < _firstInTree) _firstInTree = index;
        if (index > _lastInTree) _lastInTree = index;

        return element;
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

    private void UpdateHeightEstimate()
    {
        double sum = 0;
        int count = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].HasBeenMeasured)
            {
                sum += _slots[i].CachedHeight;
                count++;
            }
        }
        if (count > 0)
            _estimatedItemHeight = sum / count;
    }

    // -- Container management via ItemContainerGenerator --

    private Control? RealizeContainer(int index)
    {
        var items = Items;
        if ((uint)index >= (uint)items.Count) return null;
        var item = items[index];
        if (item is null) return null;

        var generator = ItemContainerGenerator;
        if (generator is null)
            return null;

        Control container;

        if (generator.NeedsContainer(item, index, out var recycleKey))
        {
            container = generator.CreateContainer(item, index, recycleKey);
        }
        else
        {
            container = (Control)item;
        }

        generator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private void UnrealizeContainer(Control container, int index)
    {
        ItemContainerGenerator?.ClearItemContainer(container);
        RemoveInternalChild(container);
    }
}
