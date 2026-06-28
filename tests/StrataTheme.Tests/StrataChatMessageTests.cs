using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataChatMessageTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataChatMessageTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

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
    public async Task ExtractSelectedText_FindsNestedSelectableTextBlock()
    {
        await _fixture.Dispatch(() =>
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
    public async Task EditButtons_StillWorkAfterDetachReattach()
    {
        var result = await _fixture.Dispatch(() =>
        {
            var command = new RecordingCommand();
            var message = new StrataChatMessage
            {
                Template = BuildChatMessageTemplate(),
                IsEditable = true,
                Content = new TextBlock { Text = "Before" },
                EditText = "Before",
                EditConfirmedCommand = command
            };

            var host = new Border();
            var window = new Window { Width = 420, Height = 260, Content = host };
            host.Child = message;
            window.Show();
            message.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            host.Child = null;
            Dispatcher.UIThread.RunJobs();
            host.Child = message;
            Dispatcher.UIThread.RunJobs();

            Click(FindButton(message, "PART_EditButton"));
            var editOpened = message.IsEditing;

            message.EditText = "After";
            Click(FindButton(message, "PART_SaveButton"));
            var editClosed = !message.IsEditing;

            window.Close();
            return (editOpened, editClosed, command.ExecuteCount, command.LastParameter);
        });

        Assert.True(result.editOpened);
        Assert.True(result.editClosed);
        Assert.Equal(1, result.ExecuteCount);
        Assert.Equal("After", Assert.IsType<string>(result.LastParameter));
    }

    [Fact]
    public async Task ExtractCopyText_PrefersCachedContextMenuSelection()
    {
        await _fixture.Dispatch(() =>
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

    [Fact]
    public async Task ExtractCopyText_NoSelectionUsesWholeMessageText()
    {
        await _fixture.Dispatch(() =>
        {
            var message = new StrataChatMessage
            {
                Content = new SelectableTextBlock { Text = "Whole message text" }
            };

            var copy = ExtractCopyText(message);

            Assert.Equal("Whole message text", copy.Text);
            Assert.False(copy.IsSelection);
        });
    }

    [Fact]
    public async Task RebuildContextMenuItems_NoSelectionShowsWholeMessageActions()
    {
        await _fixture.Dispatch(() =>
        {
            var menu = new ContextMenu();
            var message = new StrataChatMessage
            {
                Role = StrataChatRole.Assistant,
                Content = new SelectableTextBlock { Text = "Whole message text" }
            };
            SetPrivateField(message, "_contextMenu", menu);

            InvokePrivate(message, "RebuildContextMenuItems");

            var headers = Assert
                .IsAssignableFrom<IEnumerable<object>>(menu.ItemsSource)
                .OfType<MenuItem>()
                .Select(static item => item.Header?.ToString())
                .ToArray();
            Assert.Contains("Copy message", headers);
            Assert.Contains("Copy assistant turn", headers);
            Assert.DoesNotContain("Copy selected text", headers);
        });
    }

    private static void ConfirmEdit(StrataChatMessage message)
    {
        var method = typeof(StrataChatMessage).GetMethod("ConfirmEdit", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(message, null);
    }

    private static FuncControlTemplate<StrataChatMessage> BuildChatMessageTemplate() =>
        new((_, scope) =>
        {
            var streamBar = new Border { Name = "PART_StreamBar" };
            var bubble = new Border { Name = "PART_Bubble" };
            var actionLayer = new Border { Name = "PART_ActionLayer" };
            var editArea = new Border { Name = "PART_EditArea" };
            var editBox = new TextBox { Name = "PART_EditBox" };
            var editHint = new TextBlock { Name = "PART_EditHint" };
            var copyButton = new Button { Name = "PART_CopyButton" };
            var editButton = new Button { Name = "PART_EditButton" };
            var regenerateButton = new Button { Name = "PART_RegenerateButton" };
            var saveButton = new Button { Name = "PART_SaveButton" };
            var cancelButton = new Button { Name = "PART_CancelButton" };
            var editSeparator = new Border { Name = "PART_EditSep" };
            var regenerateSeparator = new Border { Name = "PART_RegenerateSep" };

            actionLayer.Child = new StackPanel
            {
                Children =
                {
                    copyButton,
                    editSeparator,
                    editButton,
                    regenerateSeparator,
                    regenerateButton
                }
            };
            editArea.Child = new StackPanel
            {
                Children =
                {
                    editBox,
                    editHint,
                    saveButton,
                    cancelButton
                }
            };

            scope.Register("PART_StreamBar", streamBar);
            scope.Register("PART_Bubble", bubble);
            scope.Register("PART_ActionLayer", actionLayer);
            scope.Register("PART_EditArea", editArea);
            scope.Register("PART_EditBox", editBox);
            scope.Register("PART_EditHint", editHint);
            scope.Register("PART_CopyButton", copyButton);
            scope.Register("PART_EditButton", editButton);
            scope.Register("PART_RegenerateButton", regenerateButton);
            scope.Register("PART_SaveButton", saveButton);
            scope.Register("PART_CancelButton", cancelButton);
            scope.Register("PART_EditSep", editSeparator);
            scope.Register("PART_RegenerateSep", regenerateSeparator);

            return new StackPanel
            {
                Children =
                {
                    streamBar,
                    bubble,
                    actionLayer,
                    editArea
                }
            };
        });

    private static Button FindButton(StrataChatMessage message, string name) =>
        message.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

    private static void InvokePrivate(StrataChatMessage message, string name)
    {
        var method = typeof(StrataChatMessage).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(message, null);
    }

    private static void SetPrivateField<T>(StrataChatMessage message, string name, T value)
    {
        var field = typeof(StrataChatMessage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field!.SetValue(message, value);
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecuteCount { get; private set; }
        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }
    }
}
