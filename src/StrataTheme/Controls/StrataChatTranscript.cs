using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace StrataTheme.Controls;

/// <summary>
/// Virtualized transcript host for chat shells.
/// Uses <see cref="StrataChatVirtualizingPanel"/> for keyed container recycling,
/// variable-height estimation, and viewport-driven realization.
/// </summary>
public class StrataChatTranscript : ItemsControl
{
    private int _pendingScrollIndex = -1;
    private ScrollToAlignment _pendingScrollAlignment = ScrollToAlignment.End;

    public StrataChatTranscript()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StrataChatVirtualizingPanel
        {
            Spacing = 8,
            CacheLength = 0.35,
        });
    }

    /// <summary>
    /// Instantly positions the viewport so that item at <paramref name="index"/>
    /// is visible according to <paramref name="alignment"/>.
    /// Delegates to the underlying <see cref="StrataChatVirtualizingPanel"/>.
    /// </summary>
    public void ScrollToIndex(int index, ScrollToAlignment alignment = ScrollToAlignment.Start)
    {
        if (ItemsPanelRoot is StrataChatVirtualizingPanel panel)
        {
            _pendingScrollIndex = -1;
            panel.ScrollToIndex(index, alignment);
            return;
        }

        _pendingScrollIndex = index;
        _pendingScrollAlignment = alignment;
    }

    /// <summary>
    /// Queues a scroll request so the next realized layout can open directly
    /// at the requested item instead of rendering from the start first.
    /// </summary>
    public void PrepareScrollToIndex(int index, ScrollToAlignment alignment = ScrollToAlignment.Start)
    {
        _pendingScrollIndex = index;
        _pendingScrollAlignment = alignment;
        ApplyPendingScrollRequest();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        if (item is Control)
        {
            recycleKey = null;
            return false;
        }

        recycleKey = item is IStrataVirtualizedItem virtualized && virtualized.VirtualizationRecycleKey is not null
            ? virtualized.VirtualizationRecycleKey
            : item?.GetType();

        return true;
    }

    protected override void ContainerForItemPreparedOverride(Control container, object? item, int index)
    {
        base.ContainerForItemPreparedOverride(container, item, index);
        ApplyPendingScrollRequest();
    }

    /// <summary>Gets the underlying panel, if realized.</summary>
    internal StrataChatVirtualizingPanel? Panel => ItemsPanelRoot as StrataChatVirtualizingPanel;

    private void ApplyPendingScrollRequest()
    {
        if (_pendingScrollIndex < 0 || ItemsPanelRoot is not StrataChatVirtualizingPanel panel)
            return;

        panel.ScrollToIndex(_pendingScrollIndex, _pendingScrollAlignment);
        _pendingScrollIndex = -1;
    }
}