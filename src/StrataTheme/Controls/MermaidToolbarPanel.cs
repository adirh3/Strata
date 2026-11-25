using System;
using Avalonia;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>
/// Lays out a diagram's toolbar: its first child, the kind tag, on the left and its second, the tool
/// pills, on the right, in one row while both fit. Where they don't, the tag gives way whole and the
/// pills wrap onto more rows, so no tool is ever cut off and the tag never runs into them.
/// </summary>
internal sealed class MermaidToolbarPanel : Panel
{
    private bool _showsTag = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
            return base.MeasureOverride(availableSize);

        var tag = Children[0];
        var tools = Children[1];
        tag.Measure(Size.Infinity);
        tools.Measure(availableSize.WithWidth(double.PositiveInfinity));
        _showsTag = tag.DesiredSize.Width + tools.DesiredSize.Width <= availableSize.Width;
        if (!_showsTag)
            tools.Measure(availableSize);

        var width = double.IsInfinity(availableSize.Width)
            ? tag.DesiredSize.Width + tools.DesiredSize.Width
            : availableSize.Width;
        return new Size(width, Math.Max(tools.DesiredSize.Height, _showsTag ? tag.DesiredSize.Height : 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
            return base.ArrangeOverride(finalSize);

        var tag = Children[0];
        var tools = Children[1];
        var toolsWidth = Math.Min(tools.DesiredSize.Width, finalSize.Width);
        tools.Arrange(new Rect(finalSize.Width - toolsWidth, 0, toolsWidth, finalSize.Height));
        tag.Arrange(_showsTag ? new Rect(0, 0, finalSize.Width - toolsWidth, finalSize.Height) : default);
        return finalSize;
    }
}
