using Avalonia.Controls;
using Avalonia.Layout;

namespace StrataTheme.Controls;

/// <summary>
/// Virtualizing panel specialized for chat transcripts with variable-height items.
/// Uses Avalonia's native virtualization, viewport tracking, and scroll anchoring.
/// </summary>
/// <remarks>
/// <para>Built on top of <see cref="VirtualizingStackPanel"/> to reuse Avalonia's
/// container generation/recycling pipeline and viewport-based realization.</para>
/// <para>This panel intentionally avoids manual <see cref="ScrollViewer.Offset"/> writes
/// so the scrollbar remains governed by Avalonia's built-in virtualization ecosystem.</para>
/// </remarks>
public class StrataChatVirtualizingPanel : VirtualizingStackPanel
{
    public StrataChatVirtualizingPanel()
    {
        Orientation = Orientation.Vertical;
        CacheLength = 2.0;
    }
}