using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

public sealed class StrataChatVirtualizingPanel : VirtualizingPanel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<StrataChatVirtualizingPanel, double>(nameof(Spacing), 0d);

    public static readonly StyledProperty<double> CacheLengthProperty =
        AvaloniaProperty.Register<StrataChatVirtualizingPanel, double>(nameof(CacheLength), 1d,
            validate: value => value is >= 0d and <= 3d);

    public static readonly StyledProperty<bool> IsBottomPinnedProperty =
        AvaloniaProperty.Register<StrataChatVirtualizingPanel, bool>(nameof(IsBottomPinned));

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<StrataChatVirtualizingPanel, Control, object?>("RecycleKey");

    private static readonly object ItemIsOwnContainer = new();
    private const double DefaultEstimatedHeight = 120d;
    private const double DefaultViewportHeight = 800d;
    private const double ScrollTolerance = 0.5d;
    private const double ViewportDeltaTolerance = 1d;
    private const int MaxRecyclePoolPerKey = 32;
    private const int MaxSizeCacheEntries = 20000;
    private const double OwnContainerWarmBufferFactor = 3d;

    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();
    private readonly Dictionary<object, SizeCacheEntry> _sizeCache = new();
    private readonly List<double> _estimatedHeights = new();

    private double[] _prefixHeights = Array.Empty<double>();
    private bool _prefixDirty = true;
    private int _prefixDirtyFrom = 0;
    private double _lastEstimatedHeight = DefaultEstimatedHeight;
    private double _lastMeasureWidth = -1d;

    private StrataRealizedChatElements? _realizedElements;
    private StrataRealizedChatElements? _measureElements;
    private IScrollAnchorProvider? _scrollAnchorProvider;
    private ScrollViewer? _scrollViewer;
    private Rect _viewport;
    private Rect _extendedViewport;
    private double _lastScrollOffset = double.NaN;
    private double _lastViewportWidth = double.NaN;
    private double _lastViewportHeight = double.NaN;
    private bool _viewportMeasureQueued;
    private double _bufferFactor = 1d;
    private bool _bottomPinDirty = true;
    private int _pendingScrollIndex = -1;
    private ScrollToAlignment _pendingScrollAlignment = ScrollToAlignment.Nearest;

    static StrataChatVirtualizingPanel()
    {
        AffectsMeasure<StrataChatVirtualizingPanel>(SpacingProperty, CacheLengthProperty, IsBottomPinnedProperty);
        CacheLengthProperty.Changed.AddClassHandler<StrataChatVirtualizingPanel>((panel, args) =>
            panel._bufferFactor = args.GetNewValue<double>());
        IsBottomPinnedProperty.Changed.AddClassHandler<StrataChatVirtualizingPanel>((panel, args) =>
        {
            if (args.GetNewValue<bool>())
                panel._bottomPinDirty = true;
        });
    }

    public StrataChatVirtualizingPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double CacheLength
    {
        get => GetValue(CacheLengthProperty);
        set => SetValue(CacheLengthProperty, value);
    }

    public bool IsBottomPinned
    {
        get => GetValue(IsBottomPinnedProperty);
        set => SetValue(IsBottomPinnedProperty, value);
    }

    public int FirstRealizedIndex => _realizedElements?.FirstIndex ?? -1;
    public int LastRealizedIndex => _realizedElements?.LastIndex ?? -1;
    public int RealizedElementCount => _realizedElements?.Count ?? 0;
    public int AttachedElementCount => Children.Count;

    public void ScrollToIndex(int index, ScrollToAlignment alignment = ScrollToAlignment.Start)
    {
        if ((uint)index >= (uint)Items.Count)
            return;

        _pendingScrollIndex = index;
        _pendingScrollAlignment = alignment;
        InvalidateMeasure();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollAnchorProvider = this.FindAncestorOfType<IScrollAnchorProvider>();
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
            _lastScrollOffset = _scrollViewer.Offset.Y;
            _lastViewportWidth = _scrollViewer.Viewport.Width;
            _lastViewportHeight = _scrollViewer.Viewport.Height;
            UpdateViewports(new Rect(0, _lastScrollOffset, _lastViewportWidth, _lastViewportHeight));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;

        _scrollViewer = null;
        _scrollAnchorProvider = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        var newOffset = _scrollViewer.Offset.Y;
        var newViewportWidth = _scrollViewer.Viewport.Width;
        var newViewportHeight = _scrollViewer.Viewport.Height;

        var offsetChanged = double.IsNaN(_lastScrollOffset) || Math.Abs(newOffset - _lastScrollOffset) > ScrollTolerance;
        var viewportChanged = double.IsNaN(_lastViewportHeight)
            || Math.Abs(newViewportHeight - _lastViewportHeight) > ViewportDeltaTolerance
            || Math.Abs(newViewportWidth - _lastViewportWidth) > ViewportDeltaTolerance;

        if (!offsetChanged && !viewportChanged)
            return;

        var previousViewport = _viewport;
        var previousExtendedViewport = _extendedViewport;

        _lastScrollOffset = newOffset;
        _lastViewportWidth = newViewportWidth;
        _lastViewportHeight = newViewportHeight;

        UpdateViewports(new Rect(0, newOffset, newViewportWidth, newViewportHeight));

        if (viewportChanged)
            _bottomPinDirty = true;

        var logicalViewportChanged = !AreClose(previousViewport.Top, _viewport.Top)
            || !AreClose(previousViewport.Bottom, _viewport.Bottom)
            || !AreClose(previousExtendedViewport.Top, _extendedViewport.Top)
            || !AreClose(previousExtendedViewport.Bottom, _extendedViewport.Bottom);

        if (_pendingScrollIndex >= 0 || viewportChanged || (logicalViewportChanged && NeedsViewportRealization()))
            QueueViewportMeasure();
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        base.OnItemsControlChanged(oldValue);

        if (_realizedElements is not null)
            _realizedElements.RecycleAllElements(RecycleElement);

        _realizedElements?.ResetForReuse();
        _measureElements?.ResetForReuse();
        _realizedElements = null;
        _measureElements = null;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);

        UpdateEstimatedHeights(items, e);

        if (_realizedElements is null)
        {
            InvalidateMeasure();
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0 && e.NewItems is not null:
                _realizedElements.ItemsInserted(e.NewStartingIndex, e.NewItems.Count, UpdateElementIndex);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0 && e.OldItems is not null:
                _realizedElements.ItemsRemoved(e.OldStartingIndex, e.OldItems.Count, UpdateElementIndex, RecycleElementOnItemRemoved);
                break;
            case NotifyCollectionChangedAction.Replace when e.OldStartingIndex >= 0 && e.OldItems is not null:
                _realizedElements.ItemsReplaced(e.OldStartingIndex, e.OldItems.Count, RecycleElementOnItemRemoved);
                break;
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
            default:
                _realizedElements.ItemsReset(RecycleElementOnItemRemoved);
                break;
        }

        InvalidateMeasure();
    }

    protected override Control? ContainerFromIndex(int index)
    {
        if (index < 0 || index >= Items.Count)
            return null;

        if (_realizedElements?.GetElement(index) is { } realized)
            return realized;

        if (Items[index] is Control control && Equals(control.GetValue(RecycleKeyProperty), ItemIsOwnContainer))
            return control;

        return null;
    }

    protected override int IndexFromContainer(Control container)
    {
        return _realizedElements?.GetIndex(container) ?? -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
    {
        if (_realizedElements is null)
            yield break;

        foreach (var element in _realizedElements.Elements)
        {
            if (element is not null)
                yield return element;
        }
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var count = Items.Count;
        var fromControl = from as Control;

        if (count == 0 || (fromControl is null && direction is not NavigationDirection.First and not NavigationDirection.Last))
            return null;

        var fromIndex = fromControl is not null ? IndexFromContainer(fromControl) : -1;
        var toIndex = fromIndex;

        switch (direction)
        {
            case NavigationDirection.First:
                toIndex = 0;
                break;
            case NavigationDirection.Last:
                toIndex = count - 1;
                break;
            case NavigationDirection.Down:
            case NavigationDirection.Next:
                toIndex++;
                break;
            case NavigationDirection.Up:
            case NavigationDirection.Previous:
                toIndex--;
                break;
            default:
                return null;
        }

        if (wrap)
        {
            if (toIndex < 0)
                toIndex = count - 1;
            else if (toIndex >= count)
                toIndex = 0;
        }

        if (toIndex < 0 || toIndex >= count)
            return null;

        return ScrollIntoView(toIndex) ?? from;
    }

    protected override Control? ScrollIntoView(int index)
    {
        if ((uint)index >= (uint)Items.Count)
            return null;

        if (ContainerFromIndex(index) is { } realized)
        {
            realized.BringIntoView();
            return realized;
        }

        ScrollToIndex(index, ScrollToAlignment.Nearest);
        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        if (items.Count == 0)
            return default;

        _realizedElements ??= new StrataRealizedChatElements();
        _measureElements ??= new StrataRealizedChatElements();
        _realizedElements.ValidateStartU();

        SyncEstimatedHeights(items, availableSize.Width);

        var viewport = CalculateMeasureViewport(items, availableSize);
        if (viewport.ViewportIsDisjunct)
            _realizedElements.RecycleAllElements(RecycleElement);

        RealizeElements(items, availableSize, ref viewport);

        (_measureElements, _realizedElements) = (_realizedElements, _measureElements);
        _measureElements.ResetForReuse();

        return CalculateDesiredSize(items.Count, viewport.MeasuredWidth);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var viewportHeight = _scrollViewer?.Viewport.Height > 0
            ? _scrollViewer.Viewport.Height
            : _viewport.Height > 0
                ? _viewport.Height
                : finalSize.Height;

        if (_realizedElements is null || _realizedElements.Count == 0)
        {
            ApplyBottomPin(viewportHeight);
            return finalSize;
        }

        var u = _realizedElements.StartU;
        var itemCount = Items.Count;

        for (var i = 0; i < _realizedElements.Count; i++)
        {
            var index = _realizedElements.FirstIndex + i;
            var element = _realizedElements.Elements[i];
            var height = _realizedElements.SizeU[i];

            if (element is not null)
            {
                var rect = new Rect(0, u, finalSize.Width, height);
                element.Arrange(rect);

                if (element.IsVisible && _viewport.Intersects(rect))
                {
                    try
                    {
                        _scrollAnchorProvider?.RegisterAnchorCandidate(element);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            u += height + GetSpacingAfter(index, itemCount);
        }

        ApplyPendingScroll(viewportHeight);
        ApplyBottomPin(viewportHeight);

        return finalSize;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var previousViewport = _viewport;
        var previousExtendedViewport = _extendedViewport;
        var viewportBounds = new Rect(Bounds.Size);
        var effectiveViewport = e.EffectiveViewport.Intersect(viewportBounds);

        if ((effectiveViewport.Width <= 0 || effectiveViewport.Height <= 0) && viewportBounds.Height > 0)
        {
            effectiveViewport = new Rect(0, _scrollViewer?.Offset.Y ?? 0, viewportBounds.Width, _scrollViewer?.Viewport.Height ?? viewportBounds.Height);
        }

        UpdateViewports(effectiveViewport);

        if (!AreClose(previousViewport.Height, _viewport.Height))
            _bottomPinDirty = true;

        var viewportChanged = !AreClose(previousViewport.Top, _viewport.Top)
            || !AreClose(previousViewport.Bottom, _viewport.Bottom)
            || !AreClose(previousExtendedViewport.Top, _extendedViewport.Top)
            || !AreClose(previousExtendedViewport.Bottom, _extendedViewport.Bottom);

        var viewportSizeChanged = !AreClose(previousViewport.Width, _viewport.Width)
            || !AreClose(previousViewport.Height, _viewport.Height);

        if (_pendingScrollIndex >= 0 || viewportSizeChanged || (viewportChanged && NeedsViewportRealization()))
            QueueViewportMeasure();
    }

    private void QueueViewportMeasure()
    {
        if (_viewportMeasureQueued)
            return;

        _viewportMeasureQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _viewportMeasureQueued = false;
            InvalidateMeasure();
        }, DispatcherPriority.Render);
    }

    private bool NeedsViewportRealization()
    {
        if (_realizedElements is not { Count: > 0 } realized || Items.Count == 0)
            return true;

        var firstIndex = realized.FirstIndex;
        var lastIndex = realized.LastIndex;

        var realizedTop = GetRealizedTop(firstIndex);
        if (double.IsNaN(realizedTop))
            realizedTop = GetEstimatedTop(firstIndex, Items.Count);

        var realizedBottomTop = GetRealizedTop(lastIndex);
        if (double.IsNaN(realizedBottomTop))
            realizedBottomTop = GetEstimatedTop(lastIndex, Items.Count);

        var realizedBottomHeight = GetRealizedHeight(lastIndex);
        if (double.IsNaN(realizedBottomHeight))
            realizedBottomHeight = GetEstimatedHeight(lastIndex);

        var realizedBottom = realizedBottomTop + realizedBottomHeight;

        return _extendedViewport.Top < realizedTop + ScrollTolerance
            || _extendedViewport.Bottom > realizedBottom - ScrollTolerance;
    }

    private void UpdateViewports(Rect viewport)
    {
        _viewport = viewport;

        var viewportHeight = viewport.Height > 0 ? viewport.Height : _scrollViewer?.Viewport.Height ?? 0;
        var extentHeight = Math.Max(Bounds.Height, GetEstimatedTotalHeight(Items.Count));
        var averageItemBuffer = Math.Clamp(
            Math.Max(_lastEstimatedHeight, DefaultEstimatedHeight),
            DefaultEstimatedHeight,
            Math.Max(DefaultViewportHeight, viewportHeight));
        var bufferSize = Math.Max(viewportHeight * _bufferFactor, averageItemBuffer);

        var extendedStart = Math.Max(0, viewport.Top - bufferSize);
        var extendedEnd = Math.Min(extentHeight, viewport.Bottom + bufferSize);

        var spaceAbove = viewport.Top - bufferSize;
        var spaceBelow = extentHeight - (viewport.Bottom + bufferSize);
        if (spaceAbove < 0 && spaceBelow >= 0)
            extendedEnd = Math.Min(extentHeight, extendedEnd + Math.Abs(spaceAbove));
        if (spaceAbove >= 0 && spaceBelow < 0)
            extendedStart = Math.Max(0, extendedStart - Math.Abs(spaceBelow));

        _extendedViewport = new Rect(viewport.X, extendedStart, viewport.Width, Math.Max(0, extendedEnd - extendedStart));
    }

    private MeasureViewport CalculateMeasureViewport(IReadOnlyList<object?> items, Size availableSize)
    {
        var itemCount = items.Count;
        var viewportHeight = GetViewportHeight(availableSize);
        var viewportStart = _extendedViewport.Height > 0 ? _extendedViewport.Top : _scrollViewer?.Offset.Y ?? 0;
        var viewportEnd = _extendedViewport.Height > 0 ? _extendedViewport.Bottom : viewportStart + viewportHeight;

        if (_pendingScrollIndex >= 0)
        {
            var targetOffset = ComputeTargetOffset(_pendingScrollIndex, _pendingScrollAlignment, viewportHeight, itemCount, preferRealized: false);
            var buffer = viewportHeight * _bufferFactor;
            viewportStart = Math.Max(0, targetOffset - buffer);
            viewportEnd = Math.Min(GetEstimatedTotalHeight(itemCount), targetOffset + viewportHeight + buffer);
        }

        int anchorIndex;
        double anchorU;

        if (_pendingScrollIndex >= 0)
        {
            anchorIndex = Math.Clamp(_pendingScrollIndex, 0, itemCount - 1);
            anchorU = GetEstimatedTop(anchorIndex, itemCount);
        }
        else if (TryGetRealizedAnchor(viewportStart, viewportEnd, itemCount, out anchorIndex, out anchorU))
        {
        }
        else
        {
            anchorIndex = FindFirstSlotBelow(viewportStart, itemCount);
            anchorU = GetEstimatedTop(anchorIndex, itemCount);
        }

        var disjunct = _realizedElements is null
            || _realizedElements.Count == 0
            || anchorIndex < _realizedElements.FirstIndex
            || anchorIndex > _realizedElements.LastIndex;

        return new MeasureViewport
        {
            AnchorIndex = anchorIndex,
            AnchorU = anchorU,
            ViewportStart = viewportStart,
            ViewportEnd = viewportEnd,
            ViewportIsDisjunct = disjunct,
        };
    }

    private void RealizeElements(IReadOnlyList<object?> items, Size availableSize, ref MeasureViewport viewport)
    {
        if (_measureElements is null || _realizedElements is null)
            return;

        var width = GetMeasureWidth(availableSize);
        var constraint = new Size(width, double.PositiveInfinity);
        var index = viewport.AnchorIndex;
        var u = viewport.AnchorU;
        var itemCount = items.Count;

        _measureElements.RecycleElementsBefore(index, RecycleElement);

        do
        {
            var element = GetOrCreateElement(items, index);
            if (ShouldMeasureElement(element, items[index], width))
                element.Measure(constraint);

            var height = element.DesiredSize.Height;
            var childWidth = element.DesiredSize.Width;
            UpdateMeasuredHeight(index, items[index], height, width);

            _measureElements.Add(index, element, u, height);
            viewport.MeasuredWidth = Math.Max(viewport.MeasuredWidth, childWidth);

            u += height + GetSpacingAfter(index, itemCount);
            index++;
        }
        while (u < viewport.ViewportEnd && index < itemCount);

        viewport.LastIndex = index - 1;
        _realizedElements.RecycleElementsAfter(viewport.LastIndex, RecycleElement);

        index = viewport.AnchorIndex - 1;
        u = viewport.AnchorU;

        while (u > viewport.ViewportStart && index >= 0)
        {
            var element = GetOrCreateElement(items, index);
            if (ShouldMeasureElement(element, items[index], width))
                element.Measure(constraint);

            var height = element.DesiredSize.Height;
            var childWidth = element.DesiredSize.Width;
            UpdateMeasuredHeight(index, items[index], height, width);

            u -= height + GetSpacingAfter(index, itemCount);
            _measureElements.Add(index, element, u, height);
            viewport.MeasuredWidth = Math.Max(viewport.MeasuredWidth, childWidth);
            index--;
        }

        _realizedElements.RecycleElementsBefore(index + 1, RecycleElement);
    }

    private bool ShouldMeasureElement(Control element, object? item, double width)
    {
        if (!element.IsMeasureValid)
            return true;

        var measureKey = GetMeasureKey(item);
        if (measureKey is null)
            return true;

        return !_sizeCache.TryGetValue(measureKey, out var cached)
            || Math.Abs(cached.Width - width) >= 1;
    }

    private Size CalculateDesiredSize(int itemCount, double measuredWidth)
    {
        var totalHeight = GetEstimatedTotalHeight(itemCount);
        return new Size(measuredWidth, totalHeight);
    }

    private bool TryGetRealizedAnchor(double viewportStart, double viewportEnd, int itemCount, out int index, out double position)
    {
        if (_realizedElements is { Count: > 0 } realized && !double.IsNaN(realized.StartU))
        {
            var u = realized.StartU;
            for (var i = 0; i < realized.Count; i++)
            {
                var element = realized.Elements[i];
                var indexAtPosition = realized.FirstIndex + i;
                var height = realized.SizeU[i];
                var endU = u + height;

                if (element is not null && endU > viewportStart && u < viewportEnd)
                {
                    index = indexAtPosition;
                    position = u;
                    return true;
                }

                u = endU + GetSpacingAfter(indexAtPosition, itemCount);
            }
        }

        index = 0;
        position = 0;
        return false;
    }

    private double ComputeTargetOffset(int index, ScrollToAlignment alignment, double viewportHeight, int itemCount, bool preferRealized)
    {
        if (itemCount == 0)
            return 0;

        var itemTop = preferRealized ? GetRealizedTop(index) : double.NaN;
        if (double.IsNaN(itemTop))
            itemTop = GetEstimatedTop(index, itemCount);

        var itemHeight = preferRealized ? GetRealizedHeight(index) : double.NaN;
        if (double.IsNaN(itemHeight))
            itemHeight = GetEstimatedHeight(index);

        var targetOffset = alignment switch
        {
            ScrollToAlignment.Start => itemTop,
            ScrollToAlignment.Center => itemTop - Math.Max(0, (viewportHeight - itemHeight) / 2d),
            ScrollToAlignment.End => itemTop + itemHeight - viewportHeight,
            ScrollToAlignment.Nearest => ComputeNearestOffset(itemTop, itemHeight, viewportHeight),
            _ => itemTop,
        };

        var maxOffset = Math.Max(0, GetEstimatedTotalHeight(itemCount) - viewportHeight);
        return Math.Clamp(targetOffset, 0, maxOffset);
    }

    private double ComputeNearestOffset(double itemTop, double itemHeight, double viewportHeight)
    {
        var viewportTop = _scrollViewer?.Offset.Y ?? _viewport.Top;
        var viewportBottom = viewportTop + viewportHeight;
        var itemBottom = itemTop + itemHeight;

        if (itemTop >= viewportTop && itemBottom <= viewportBottom)
            return viewportTop;

        return itemTop < viewportTop ? itemTop : itemBottom - viewportHeight;
    }

    private void ApplyPendingScroll(double viewportHeight)
    {
        if (_pendingScrollIndex < 0 || _scrollViewer is null || viewportHeight <= 0 || _pendingScrollIndex >= Items.Count)
            return;

        var targetOffset = ComputeTargetOffset(_pendingScrollIndex, _pendingScrollAlignment, viewportHeight, Items.Count, preferRealized: true);
        if (Math.Abs(_scrollViewer.Offset.Y - targetOffset) > ScrollTolerance)
        {
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetOffset);
            return;
        }

        _pendingScrollIndex = -1;
    }

    private void ApplyBottomPin(double viewportHeight)
    {
        if (!IsBottomPinned || _scrollViewer is null || viewportHeight <= 0 || Items.Count == 0)
            return;

        if (!_bottomPinDirty)
            return;

        var maxOffset = Math.Max(0, GetEstimatedTotalHeight(Items.Count) - viewportHeight);
        if (Math.Abs(_scrollViewer.Offset.Y - maxOffset) > ScrollTolerance)
        {
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, maxOffset);
            return;
        }

        _bottomPinDirty = false;
    }

    private Control GetOrCreateElement(IReadOnlyList<object?> items, int index)
    {
        if (_realizedElements?.GetElement(index) is { } realized)
            return realized;

        var item = items[index];
        var generator = ItemContainerGenerator;
        if (generator is null)
            throw new InvalidOperationException("ItemContainerGenerator is not available.");

        if (generator.NeedsContainer(item, index, out var recycleKey))
            return GetRecycledElement(item, index, recycleKey) ?? CreateElement(item, index, recycleKey);

        return GetItemAsOwnContainer(item, index);
    }

    private Control GetItemAsOwnContainer(object? item, int index)
    {
        var controlItem = (Control)item!;
        var generator = ItemContainerGenerator!;

        if (!controlItem.IsSet(RecycleKeyProperty))
        {
            generator.PrepareItemContainer(controlItem, controlItem, index);
            AddInternalChild(controlItem);
            controlItem.SetValue(RecycleKeyProperty, ItemIsOwnContainer);
            generator.ItemContainerPrepared(controlItem, item, index);
        }
        else if (!ReferenceEquals(controlItem.GetVisualParent(), this))
        {
            AddInternalChild(controlItem);
        }

        controlItem.SetCurrentValue(Visual.IsVisibleProperty, true);
        return controlItem;
    }

    private Control? GetRecycledElement(object? item, int index, object? recycleKey)
    {
        if (recycleKey is null)
            return null;

        if (_recyclePool.TryGetValue(recycleKey, out var pool) && pool.Count > 0)
        {
            var recycled = pool.Pop();
            recycled.SetCurrentValue(Visual.IsVisibleProperty, true);
            ItemContainerGenerator!.PrepareItemContainer(recycled, item, index);
            AddInternalChild(recycled);
            ItemContainerGenerator.ItemContainerPrepared(recycled, item, index);
            return recycled;
        }

        return null;
    }

    private Control CreateElement(object? item, int index, object? recycleKey)
    {
        var container = ItemContainerGenerator!.CreateContainer(item, index, recycleKey);
        container.SetValue(RecycleKeyProperty, recycleKey);
        ItemContainerGenerator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        ItemContainerGenerator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private void RecycleElement(Control element, int index)
    {
        _scrollAnchorProvider?.UnregisterAnchorCandidate(element);

        var recycleKey = element.GetValue(RecycleKeyProperty);
        if (recycleKey is null)
        {
            ItemContainerGenerator?.ClearItemContainer(element);
            RemoveInternalChild(element);
            return;
        }

        if (Equals(recycleKey, ItemIsOwnContainer))
        {
            element.SetCurrentValue(Visual.IsVisibleProperty, false);
            if (!ShouldKeepOwnContainerAttached(index))
                RemoveInternalChild(element);
            return;
        }

        ItemContainerGenerator?.ClearItemContainer(element);
        PushToRecyclePool(recycleKey, element);
        element.SetCurrentValue(Visual.IsVisibleProperty, false);
        RemoveInternalChild(element);
    }

    private void RecycleElementOnItemRemoved(Control element)
    {
        _scrollAnchorProvider?.UnregisterAnchorCandidate(element);

        var recycleKey = element.GetValue(RecycleKeyProperty);
        if (recycleKey is null)
        {
            ItemContainerGenerator?.ClearItemContainer(element);
            RemoveInternalChild(element);
            return;
        }

        if (Equals(recycleKey, ItemIsOwnContainer))
        {
            RemoveInternalChild(element);
            return;
        }

        ItemContainerGenerator?.ClearItemContainer(element);
        PushToRecyclePool(recycleKey, element);
        element.SetCurrentValue(Visual.IsVisibleProperty, false);
        RemoveInternalChild(element);
    }

    private bool ShouldKeepOwnContainerAttached(int index)
    {
        var itemCount = Items.Count;
        if (itemCount == 0)
            return false;

        var viewportHeight = _viewport.Height > 0
            ? _viewport.Height
            : _scrollViewer?.Viewport.Height ?? DefaultViewportHeight;
        var buffer = Math.Max(viewportHeight * OwnContainerWarmBufferFactor, Math.Max(_lastEstimatedHeight, DefaultEstimatedHeight));
        var warmStart = Math.Max(0, _viewport.Top - buffer);
        var warmEnd = Math.Min(GetEstimatedTotalHeight(itemCount), _viewport.Bottom + buffer);

        var itemTop = GetEstimatedTop(index, itemCount);
        var itemBottom = itemTop + GetEstimatedHeight(index);
        return itemBottom >= warmStart - ScrollTolerance && itemTop <= warmEnd + ScrollTolerance;
    }

    private void PushToRecyclePool(object recycleKey, Control element)
    {
        if (!_recyclePool.TryGetValue(recycleKey, out var pool))
        {
            pool = new Stack<Control>();
            _recyclePool.Add(recycleKey, pool);
        }

        if (pool.Count < MaxRecyclePoolPerKey)
            pool.Push(element);
    }

    private void UpdateElementIndex(Control container, int oldIndex, int newIndex)
    {
        ItemContainerGenerator?.ItemContainerIndexChanged(container, oldIndex, newIndex);
    }

    private void SyncEstimatedHeights(IReadOnlyList<object?> items, double availableWidth)
    {
        var width = GetMeasureWidth(new Size(availableWidth, 0));
        if (Math.Abs(width - _lastMeasureWidth) > 0.5)
        {
            _lastMeasureWidth = width;
            RebuildEstimatedHeights(items);
        }
        else if (_estimatedHeights.Count != items.Count)
        {
            RebuildEstimatedHeights(items);
        }
    }

    private void RebuildEstimatedHeights(IReadOnlyList<object?> items)
    {
        _estimatedHeights.Clear();
        for (var i = 0; i < items.Count; i++)
            _estimatedHeights.Add(EstimateHeightForItem(items[i]));
        MarkPrefixDirtyFrom(0);
    }

    private void UpdateEstimatedHeights(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0 && e.NewItems is not null:
                for (var i = 0; i < e.NewItems.Count; i++)
                    _estimatedHeights.Insert(e.NewStartingIndex + i, EstimateHeightForItem(e.NewItems[i]));
                MarkPrefixDirtyFrom(e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0 && e.OldItems is not null:
                _estimatedHeights.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
                MarkPrefixDirtyFrom(Math.Min(e.OldStartingIndex, _estimatedHeights.Count));
                break;
            case NotifyCollectionChangedAction.Replace when e.NewStartingIndex >= 0 && e.NewItems is not null:
                for (var i = 0; i < e.NewItems.Count; i++)
                    _estimatedHeights[e.NewStartingIndex + i] = EstimateHeightForItem(e.NewItems[i]);
                MarkPrefixDirtyFrom(e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
            default:
                RebuildEstimatedHeights(items);
                break;
        }
    }

    private double EstimateHeightForItem(object? item)
    {
        if (item is null)
            return _lastEstimatedHeight;

        var measureKey = GetMeasureKey(item);
        if (measureKey is not null && _sizeCache.TryGetValue(measureKey, out var cached))
        {
            if (_lastMeasureWidth <= 0 || Math.Abs(cached.Width - _lastMeasureWidth) < 1)
                return cached.Height;
        }

        if (item is IStrataVirtualizedItem virtualized && virtualized.VirtualizationHeightHint is > 0)
            return virtualized.VirtualizationHeightHint.Value;

        if (item is StrataChatMessage message)
        {
            return message.Role switch
            {
                StrataChatRole.User => 96d,
                StrataChatRole.Assistant => 280d,
                StrataChatRole.System => 110d,
                StrataChatRole.Tool => 140d,
                _ => _lastEstimatedHeight,
            };
        }

        return _lastEstimatedHeight;
    }

    private void UpdateMeasuredHeight(int index, object? item, double height, double width)
    {
        if ((uint)index >= (uint)_estimatedHeights.Count)
            return;

        var previous = _estimatedHeights[index];
        if (Math.Abs(previous - height) > 0.5)
        {
            _estimatedHeights[index] = height;
            MarkPrefixDirtyFrom(index);
        }

        _lastEstimatedHeight = _lastEstimatedHeight <= 0 ? height : ((_lastEstimatedHeight * 7d) + height) / 8d;

        var measureKey = GetMeasureKey(item);
        if (measureKey is not null)
        {
            _sizeCache[measureKey] = new SizeCacheEntry(height, width);
            if (_sizeCache.Count > MaxSizeCacheEntries)
                _sizeCache.Clear();
        }
    }

    private object? GetMeasureKey(object? item)
    {
        if (item is null)
            return null;

        if (item is IStrataVirtualizedItem virtualized && virtualized.VirtualizationMeasureKey is not null)
            return virtualized.VirtualizationMeasureKey;
        return item;
    }

    private void MarkPrefixDirtyFrom(int fromIndex)
    {
        _prefixDirty = true;
        _prefixDirtyFrom = Math.Min(Math.Max(fromIndex, 0), _estimatedHeights.Count);
        _bottomPinDirty = true;
    }

    private void EnsurePrefixHeights(int itemCount)
    {
        var requiredLength = itemCount + 1;
        if (!_prefixDirty && _prefixHeights.Length >= requiredLength)
            return;

        if (_prefixHeights.Length < requiredLength)
        {
            var newLength = Math.Max(requiredLength, Math.Max(64, _prefixHeights.Length * 2));
            var newPrefix = new double[newLength];
            if (_prefixHeights.Length > 0)
                Array.Copy(_prefixHeights, newPrefix, Math.Min(_prefixHeights.Length, newPrefix.Length));
            _prefixHeights = newPrefix;
        }

        var start = Math.Min(_prefixDirtyFrom, itemCount);
        if (start == 0)
            _prefixHeights[0] = 0;

        for (var i = start; i < itemCount; i++)
        {
            var gap = i < itemCount - 1 ? Spacing : 0;
            _prefixHeights[i + 1] = _prefixHeights[i] + _estimatedHeights[i] + gap;
        }

        _prefixDirty = false;
        _prefixDirtyFrom = itemCount;
    }

    private int FindFirstSlotBelow(double y, int itemCount)
    {
        if (itemCount <= 0)
            return 0;

        EnsurePrefixHeights(itemCount);
        var lo = 0;
        var hi = itemCount - 1;
        var result = 0;

        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
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

    private double GetEstimatedTop(int index, int itemCount)
    {
        if (itemCount <= 0)
            return 0;

        EnsurePrefixHeights(itemCount);
        return _prefixHeights[Math.Clamp(index, 0, itemCount)];
    }

    private double GetEstimatedTotalHeight(int itemCount)
    {
        if (itemCount <= 0)
            return 0;

        EnsurePrefixHeights(itemCount);
        return _prefixHeights[itemCount];
    }

    private double GetEstimatedHeight(int index)
    {
        if ((uint)index >= (uint)_estimatedHeights.Count)
            return _lastEstimatedHeight;
        return _estimatedHeights[index];
    }

    private double GetRealizedTop(int index)
    {
        if (_realizedElements is not { Count: > 0 } realized || index < realized.FirstIndex || index > realized.LastIndex)
            return double.NaN;

        var u = realized.StartU;
        if (double.IsNaN(u))
            return double.NaN;

        for (var i = realized.FirstIndex; i < index; i++)
            u += realized.SizeU[i - realized.FirstIndex] + GetSpacingAfter(i, Items.Count);

        return u;
    }

    private double GetRealizedHeight(int index)
    {
        if (_realizedElements is not { Count: > 0 } realized || index < realized.FirstIndex || index > realized.LastIndex)
            return double.NaN;

        return realized.SizeU[index - realized.FirstIndex];
    }

    private double GetViewportHeight(Size availableSize)
    {
        if (_viewport.Height > 0)
            return _viewport.Height;

        if (!double.IsInfinity(availableSize.Height) && availableSize.Height > 0)
            return availableSize.Height;

        if (_scrollViewer?.Viewport.Height > 0)
            return _scrollViewer.Viewport.Height;

        return DefaultViewportHeight;
    }

    private double GetMeasureWidth(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
            return availableSize.Width;
        if (_viewport.Width > 0)
            return _viewport.Width;
        if (_scrollViewer?.Viewport.Width > 0)
            return _scrollViewer.Viewport.Width;
        if (Bounds.Width > 0)
            return Bounds.Width;
        return 800d;
    }

    private double GetSpacingAfter(int index, int itemCount)
    {
        return index < itemCount - 1 ? Spacing : 0d;
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) < 0.5d;
    }

    private readonly record struct SizeCacheEntry(double Height, double Width);

    private struct MeasureViewport
    {
        public int AnchorIndex;
        public double AnchorU;
        public double ViewportStart;
        public double ViewportEnd;
        public double MeasuredWidth;
        public int LastIndex;
        public bool ViewportIsDisjunct;
    }
}

internal sealed class StrataRealizedChatElements
{
    private int _firstIndex;
    private List<Control?>? _elements;
    private List<double>? _sizes;
    private double _startU;
    private bool _startUUnstable;

    public int Count => _elements?.Count ?? 0;
    public int FirstIndex => Count > 0 ? _firstIndex : -1;
    public int LastIndex => Count > 0 ? _firstIndex + Count - 1 : -1;
    public IReadOnlyList<Control?> Elements => _elements ??= new List<Control?>();
    public IReadOnlyList<double> SizeU => _sizes ??= new List<double>();
    public double StartU => _startUUnstable ? double.NaN : _startU;

    public void Add(int index, Control element, double u, double sizeU)
    {
        _elements ??= new List<Control?>();
        _sizes ??= new List<double>();

        if (Count == 0)
        {
            _firstIndex = index;
            _startU = u;
            _startUUnstable = false;
            _elements.Add(element);
            _sizes.Add(sizeU);
            return;
        }

        if (index == LastIndex + 1)
        {
            _elements.Add(element);
            _sizes.Add(sizeU);
            return;
        }

        if (index == FirstIndex - 1)
        {
            _firstIndex--;
            _startU = u;
            _elements.Insert(0, element);
            _sizes.Insert(0, sizeU);
            return;
        }

        throw new NotSupportedException("Can only add items to the beginning or end of the realized range.");
    }

    public Control? GetElement(int index)
    {
        if (_elements is null)
            return null;

        var offset = index - _firstIndex;
        return offset >= 0 && offset < _elements.Count ? _elements[offset] : null;
    }

    public int GetIndex(Control element)
    {
        if (_elements is null)
            return -1;

        var offset = _elements.IndexOf(element);
        return offset >= 0 ? _firstIndex + offset : -1;
    }

    public void ItemsInserted(int index, int count, Action<Control, int, int> updateElementIndex)
    {
        if (_elements is null || _elements.Count == 0)
            return;

        var realizedIndex = index - _firstIndex;
        if (realizedIndex >= Count)
            return;

        var start = Math.Max(realizedIndex, 0);
        for (var i = start; i < _elements.Count; i++)
        {
            if (_elements[i] is Control element)
            {
                var oldIndex = i + _firstIndex;
                updateElementIndex(element, oldIndex, oldIndex + count);
            }
        }

        if (realizedIndex < 0)
        {
            _firstIndex += count;
            _startUUnstable = true;
            return;
        }

        _elements.InsertRange(realizedIndex, CreateNullControls(count));
        _sizes!.InsertRange(realizedIndex, CreateNaNSizes(count));
    }

    public void ItemsRemoved(int index, int count, Action<Control, int, int> updateElementIndex, Action<Control> recycleElement)
    {
        if (_elements is null || _elements.Count == 0)
            return;

        var startIndex = index - _firstIndex;
        var endIndex = index + count - _firstIndex;

        if (endIndex < 0)
        {
            _firstIndex -= count;
            _startUUnstable = true;

            var newIndex = _firstIndex;
            for (var i = 0; i < _elements.Count; i++)
            {
                if (_elements[i] is Control element)
                    updateElementIndex(element, newIndex + count, newIndex);
                newIndex++;
            }
            return;
        }

        if (startIndex >= _elements.Count)
            return;

        var start = Math.Max(startIndex, 0);
        var end = Math.Min(endIndex, _elements.Count - 1);
        for (var i = start; i <= end; i++)
        {
            if (_elements[i] is Control element)
            {
                _elements[i] = null;
                recycleElement(element);
            }
        }

        _elements.RemoveRange(start, end - start + 1);
        _sizes!.RemoveRange(start, end - start + 1);

        if (startIndex <= 0)
        {
            _firstIndex = index;
            _startUUnstable = true;
        }

        var updatedIndex = _firstIndex + start;
        for (var i = start; i < _elements.Count; i++)
        {
            if (_elements[i] is Control element)
                updateElementIndex(element, updatedIndex + count, updatedIndex);
            updatedIndex++;
        }
    }

    public void ItemsReplaced(int index, int count, Action<Control> recycleElement)
    {
        if (_elements is null || _elements.Count == 0)
            return;

        var startIndex = index - _firstIndex;
        var endIndex = Math.Min(startIndex + count, _elements.Count);
        if (startIndex < 0 || startIndex >= endIndex)
            return;

        for (var i = startIndex; i < endIndex; i++)
        {
            if (_elements[i] is Control element)
            {
                recycleElement(element);
                _elements[i] = null;
                _sizes![i] = double.NaN;
            }
        }
    }

    public void ItemsReset(Action<Control> recycleElement)
    {
        if (_elements is null || _elements.Count == 0)
            return;

        for (var i = 0; i < _elements.Count; i++)
        {
            if (_elements[i] is Control element)
            {
                _elements[i] = null;
                recycleElement(element);
            }
        }

        ResetForReuse();
    }

    public void RecycleElementsBefore(int index, Action<Control, int> recycleElement)
    {
        if (_elements is null || _elements.Count == 0 || index <= FirstIndex)
            return;

        if (index > LastIndex)
        {
            RecycleAllElements(recycleElement);
            return;
        }

        var endIndex = index - _firstIndex;
        for (var i = 0; i < endIndex; i++)
        {
            if (_elements[i] is Control element)
            {
                _elements[i] = null;
                recycleElement(element, i + _firstIndex);
            }
        }

        _elements.RemoveRange(0, endIndex);
        _sizes!.RemoveRange(0, endIndex);
        _firstIndex = index;
    }

    public void RecycleElementsAfter(int index, Action<Control, int> recycleElement)
    {
        if (_elements is null || _elements.Count == 0 || index >= LastIndex)
            return;

        if (index < FirstIndex)
        {
            RecycleAllElements(recycleElement);
            return;
        }

        var startIndex = (index + 1) - _firstIndex;
        for (var i = startIndex; i < _elements.Count; i++)
        {
            if (_elements[i] is Control element)
            {
                _elements[i] = null;
                recycleElement(element, i + _firstIndex);
            }
        }

        _elements.RemoveRange(startIndex, _elements.Count - startIndex);
        _sizes!.RemoveRange(startIndex, _sizes.Count - startIndex);
    }

    public void RecycleAllElements(Action<Control, int> recycleElement)
    {
        if (_elements is null || _elements.Count == 0)
            return;

        for (var i = 0; i < _elements.Count; i++)
        {
            if (_elements[i] is Control element)
            {
                _elements[i] = null;
                recycleElement(element, i + _firstIndex);
            }
        }

        ResetForReuse();
    }

    public void ResetForReuse()
    {
        _firstIndex = 0;
        _startU = 0;
        _startUUnstable = false;
        _elements?.Clear();
        _sizes?.Clear();
    }

    public void ValidateStartU()
    {
        if (_elements is null || _sizes is null || _startUUnstable)
            return;

        for (var i = 0; i < _elements.Count; i++)
        {
            if (_elements[i] is not { } element)
                continue;

            if (Math.Abs(element.DesiredSize.Height - _sizes[i]) > 0.5)
            {
                _startUUnstable = true;
                break;
            }
        }
    }

    private static IEnumerable<Control?> CreateNullControls(int count)
    {
        for (var i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerable<double> CreateNaNSizes(int count)
    {
        for (var i = 0; i < count; i++)
            yield return double.NaN;
    }
}
