using Avalonia.Media;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class MermaidTextHelperTests
{
    [Theory]
    [InlineData("\"`<b>שלום</b><br/>**עולם**`\"", "שלום\nעולם")]
    [InlineData("`[Docs](https://example.com) &lt;T&gt;`", "Docs <T>")]
    [InlineData("<div><strong>Hello</strong></div><div>*world*</div>", "Hello\nworld")]
    public void NormalizeLabelText_StripsSupportedMermaidFormatting(string input, string expected)
    {
        Assert.Equal(expected, MermaidTextHelper.NormalizeLabelText(input));
    }

    [Fact]
    public void NormalizeLabelText_PreservesLiteralUnderscores()
    {
        Assert.Equal("user_id", MermaidTextHelper.NormalizeLabelText("user_id"));
    }

    [Theory]
    [InlineData("`<b>שלום</b>`", FlowDirection.RightToLeft)]
    [InlineData("`**Hello**`", FlowDirection.LeftToRight)]
    public void GetFlowDirection_DetectsRenderedLabelDirection(string input, FlowDirection expected)
    {
        var normalized = MermaidTextHelper.NormalizeLabelText(input);
        Assert.Equal(expected, MermaidTextHelper.GetFlowDirection(normalized));
    }
}
