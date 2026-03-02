using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace StrataTheme.Controls;

/// <summary>
/// Virtualized transcript host for chat shells.
/// Uses <see cref="StrataChatPanel"/> which physically removes off-viewport
/// items from the visual tree for O(visible) compositor cost.
/// </summary>
public class StrataChatTranscript : ItemsControl
{
    public StrataChatTranscript()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StrataChatPanel { Spacing = 8 });
    }

    /// <summary>
    /// Instantly positions the viewport so that item at <paramref name="index"/>
    /// is visible according to <paramref name="alignment"/>.
    /// Delegates to the underlying <see cref="StrataChatPanel"/>.
    /// </summary>
    public void ScrollToIndex(int index, ScrollToAlignment alignment = ScrollToAlignment.Start)
    {
        if (ItemsPanelRoot is StrataChatPanel panel)
            panel.ScrollToIndex(index, alignment);
    }

    /// <summary>Gets the underlying panel, if realized.</summary>
    internal StrataChatPanel? Panel => ItemsPanelRoot as StrataChatPanel;
}