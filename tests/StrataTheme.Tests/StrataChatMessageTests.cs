using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class StrataChatMessageTests : IClassFixture<AvaloniaFixture>
{
    private sealed class PlainPayload
    {
    }

    [Fact]
    public void ExtractText_SkipsUnrenderedDataObjects()
    {
        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Hello" },
                new ContentControl { Content = new[] { new PlainPayload() } }
            }
        };

        var text = ChatContentExtractor.ExtractText(panel);

        Assert.Equal("Hello", text);
    }

    [Fact]
    public void ExtractSelectedText_FindsNestedSelectableTextBlock()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var panel = new StackPanel();
            panel.Children.Add(new ContentControl
            {
                Content = new SelectableTextBlock
                {
                    Text = "copy only this part",
                    SelectionStart = 10,
                    SelectionEnd = 19
                }
            });

            Assert.Equal("this part", ChatContentExtractor.ExtractSelectedText(panel));
        });
    }

    [Fact]
    public void ExtractText_UsesAttachmentDisplayText()
    {
        var attachment = new StrataFileAttachment
        {
            FileName = "notes.txt",
            FileSize = "4 KB"
        };

        Assert.Equal("notes.txt (4 KB)", ChatContentExtractor.ExtractText(attachment));
    }

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

    [Fact]
    public void ExtractCopyText_PrefersCachedContextMenuSelection()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var message = new StrataChatMessage
            {
                Content = new SelectableTextBlock { Text = "Whole message text" }
            };

            SetPrivateField(message, "_contextMenuSelectionText", "cached selected text");

            var copy = ExtractCopyText(message);

            Assert.Equal("cached selected text", copy.Text);
            Assert.True(copy.IsSelection);
        });
    }

    private static void ConfirmEdit(StrataChatMessage message)
    {
        var method = typeof(StrataChatMessage).GetMethod("ConfirmEdit", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(message, null);
    }

    private static (string Text, bool IsSelection) ExtractCopyText(StrataChatMessage message)
    {
        var method = typeof(StrataChatMessage).GetMethod("ExtractCopyText", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method!.Invoke(message, null);

        Assert.NotNull(result);
        var resultType = result!.GetType();
        var text = Assert.IsType<string>(resultType.GetProperty("Text")?.GetValue(result));
        var isSelection = Assert.IsType<bool>(resultType.GetProperty("IsSelection")?.GetValue(result));

        return (text, isSelection);
    }

    private static void SetPrivateField<T>(StrataChatMessage message, string name, T value)
    {
        var field = typeof(StrataChatMessage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field!.SetValue(message, value);
    }
}
