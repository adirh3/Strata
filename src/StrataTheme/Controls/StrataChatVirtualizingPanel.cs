using Avalonia.Controls;
using Avalonia.Layout;

namespace StrataTheme.Controls;

/// <summary>
/// Virtualizing panel specialized for chat transcripts with variable-height items.
/// Uses Avalonia's native virtualization with an aggressive default cache window.
/// </summary>
/// <remarks>
/// <para>Built on top of <see cref="VirtualizingStackPanel"/> to reuse Avalonia's
/// container generation/recycling pipeline and viewport-based realization.</para>
/// <para>Aggressively keeps the largest supported cache window so touchpad smooth
/// scrolling has enough realized buffer above and below the viewport.</para>
/// </remarks>
public class StrataChatVirtualizingPanel : VirtualizingStackPanel
{
    // Avalonia validates VirtualizingStackPanel.CacheLength to [0, 2].
    // Use the max value to reduce edge-of-viewport realization hitching.
    private const double MaxSupportedCacheLength = 2.0;

    public StrataChatVirtualizingPanel()
    {
        Orientation = Orientation.Vertical;
        CacheLength = MaxSupportedCacheLength;
    }
}
