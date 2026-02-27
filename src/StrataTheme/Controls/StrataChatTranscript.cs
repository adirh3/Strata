using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace StrataTheme.Controls;

/// <summary>
/// Virtualized transcript host for chat shells.
/// Uses a dedicated chat virtualizing panel to handle long histories,
/// variable message heights, and streaming height updates.
/// </summary>
public class StrataChatTranscript : ItemsControl
{
    public StrataChatTranscript()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StrataChatVirtualizingPanel
        {
            CacheLength = 2.0,
        });
    }
}