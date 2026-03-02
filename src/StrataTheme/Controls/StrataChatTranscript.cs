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
}