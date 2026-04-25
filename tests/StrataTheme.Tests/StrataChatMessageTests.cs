using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class StrataChatMessageTests : IClassFixture<AvaloniaFixture>
{
    [Fact]
    public void ConfirmEdit_ReplacesRegularTextBlockWithSelectableTextBlock()
    {
        var message = new StrataChatMessage
        {
            Content = new TextBlock
            {
                Text = "Before",
                TextWrapping = TextWrapping.NoWrap,
                FontSize = 17,
                FontStyle = FontStyle.Italic,
                LineHeight = 24
            },
            EditText = "After"
        };

        ConfirmEdit(message);

        var content = Assert.IsType<SelectableTextBlock>(message.Content);
        Assert.Equal("After", content.Text);
        Assert.Equal(TextWrapping.NoWrap, content.TextWrapping);
        Assert.Equal(17, content.FontSize);
        Assert.Equal(FontStyle.Italic, content.FontStyle);
        Assert.Equal(24, content.LineHeight);
    }

    [Fact]
    public void ConfirmEdit_CreatesSelectableTextBlockForPlainReplacementContent()
    {
        var message = new StrataChatMessage
        {
            Content = new Border(),
            EditText = "Plain edited message"
        };

        ConfirmEdit(message);

        var content = Assert.IsType<SelectableTextBlock>(message.Content);
        Assert.Equal("Plain edited message", content.Text);
        Assert.Equal(TextWrapping.Wrap, content.TextWrapping);
    }

    private static void ConfirmEdit(StrataChatMessage message)
    {
        var method = typeof(StrataChatMessage).GetMethod("ConfirmEdit", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(message, null);
    }
}
