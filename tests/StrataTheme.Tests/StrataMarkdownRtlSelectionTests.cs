using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class StrataMarkdownRtlSelectionTests
{
    [Fact]
    public void ApplyDirectionalTextLayout_UpdatesSelectableTextBlockForRtl()
    {
        var textBlock = new SelectableTextBlock
        {
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Left,
            Padding = default
        };

        StrataMarkdown.ApplyDirectionalTextLayout(textBlock, FlowDirection.RightToLeft);

        Assert.Equal(FlowDirection.RightToLeft, textBlock.FlowDirection);
        Assert.Equal(TextAlignment.Right, textBlock.TextAlignment);
        Assert.Equal(new Thickness(0, 0, 4, 0), textBlock.Padding);
    }

    [Fact]
    public void ApplyDirectionalTextLayout_UpdatesWrappedSelectableTextBlockForRtl()
    {
        var textBlock = new SelectableTextBlock
        {
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Left,
            Padding = default
        };

        var wrapped = StrataMarkdown.WrapWithCodeLayer(textBlock);

        StrataMarkdown.ApplyDirectionalTextLayout(wrapped, FlowDirection.RightToLeft);

        Assert.Equal(FlowDirection.RightToLeft, textBlock.FlowDirection);
        Assert.Equal(TextAlignment.Right, textBlock.TextAlignment);
        Assert.Equal(new Thickness(0, 0, 4, 0), textBlock.Padding);
    }

    [Fact]
    public void ApplyDirectionalTextLayout_ResetsWrappedSelectableTextBlockForLtr()
    {
        var textBlock = new SelectableTextBlock
        {
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            Padding = new Thickness(0, 0, 4, 0)
        };

        var wrapped = StrataMarkdown.WrapWithCodeLayer(textBlock);

        StrataMarkdown.ApplyDirectionalTextLayout(wrapped, FlowDirection.LeftToRight);

        Assert.Equal(FlowDirection.LeftToRight, textBlock.FlowDirection);
        Assert.Equal(TextAlignment.Left, textBlock.TextAlignment);
        Assert.Equal(new Thickness(0), textBlock.Padding);
    }
}
