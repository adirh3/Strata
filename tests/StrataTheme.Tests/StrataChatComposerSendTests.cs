using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataChatComposerSendTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataChatComposerSendTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleSendAction_WhenPromptIsEmptyAndEmptySendIsAllowed_ExecutesSendCommand()
    {
        await _fixture.Dispatch(() =>
        {
            var command = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "",
                CanSendWithoutPrompt = true,
                SendCommand = command
            };

            var sendRequested = false;
            composer.SendRequested += (_, _) => sendRequested = true;

            InvokeHandleSendAction(composer);

            Assert.True(sendRequested);
            Assert.Equal(1, command.ExecuteCount);
        });
    }

    [Theory]
    [InlineData("הרץ dotnet run --project SomeVeryLongProjectName", FlowDirection.RightToLeft)]
    [InlineData("شغل dotnet run --project SomeVeryLongProjectName", FlowDirection.RightToLeft)]
    [InlineData("Run שלום עולם בדיקה ארוכה בעברית", FlowDirection.LeftToRight)]
    [InlineData("123", FlowDirection.LeftToRight)]
    public async Task PromptDirection_FollowsTheFirstStrongCharacter(string text, FlowDirection expected)
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer();
            var input = new TextBox();
            typeof(StrataChatComposer)
                .GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(composer, input);

            composer.PromptText = text;

            Assert.Equal(expected, input.FlowDirection);
            Assert.Equal(expected == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
                input.TextAlignment);

            composer.PromptText = "";
            Assert.Equal(FlowDirection.LeftToRight, input.FlowDirection);
        });
    }

    [Fact]
    public async Task HandleSendAction_WhenPromptIsEmptyAndEmptySendIsNotAllowed_DoesNotExecuteSendCommand()
    {
        await _fixture.Dispatch(() =>
        {
            var command = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "",
                CanSendWithoutPrompt = false,
                SendCommand = command
            };

            var sendRequested = false;
            composer.SendRequested += (_, _) => sendRequested = true;

            InvokeHandleSendAction(composer);

            Assert.False(sendRequested);
            Assert.Equal(0, command.ExecuteCount);
        });
    }

    [Fact]
    public async Task HandleSendAction_WithExternalEditor_UsesPromptTextInsteadOfHiddenInput()
    {
        await _fixture.Dispatch(() =>
        {
            var command = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "native draft",
                EditorContent = new RecordingEditor(),
                SendCommand = command
            };
            typeof(StrataChatComposer)
                .GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(composer, new TextBox { Text = "stale hidden text" });

            InvokeHandleSendAction(composer);

            Assert.Equal(1, command.ExecuteCount);
            Assert.Equal("native draft", command.LastParameter);
            Assert.Equal("native draft", composer.PromptText);
        });
    }

    [Fact]
    public async Task FocusInputAtEnd_WithExternalEditor_DelegatesToHost()
    {
        await _fixture.Dispatch(() =>
        {
            var editor = new RecordingEditor();
            var composer = new StrataChatComposer { EditorContent = editor };

            composer.FocusInputAtEnd();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, editor.FocusCount);
        });
    }

    [Fact]
    public async Task InsertTextAtSelection_WithExternalEditor_UsesExternalCaret()
    {
        await _fixture.Dispatch(() =>
        {
            var editor = new RecordingEditor { CaretIndex = 1 };
            var composer = new StrataChatComposer
            {
                PromptText = "ab",
                EditorContent = editor
            };
            typeof(StrataChatComposer)
                .GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(composer, new TextBox { Text = "stale", CaretIndex = 5 });

            composer.InsertTextAtSelection("X");

            Assert.Equal("aXb", composer.PromptText);
            Assert.Equal(2, editor.CaretIndex);
            Assert.Equal(1, editor.FocusCount);
        });
    }

    [Fact]
    public async Task HandleSendAction_WhenEditingAndBusy_UsesEditSendInsteadOfSteerOrStop()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var stop = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "updated turn",
                IsBusy = true,
                IsEditing = true,
                SendCommand = send,
                StopCommand = stop
            };

            InvokeHandleSendAction(composer);

            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("updated turn", send.LastParameter);
            Assert.Equal(0, stop.ExecuteCount);
            Assert.Contains(":editing", composer.Classes);
            Assert.DoesNotContain(":busy", composer.Classes);
            Assert.DoesNotContain(":steer", composer.Classes);
        });
    }

    [Fact]
    public async Task Escape_WhenEditing_ExecutesCancelCommand()
    {
        await _fixture.Dispatch(() =>
        {
            var cancel = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                IsEditing = true,
                CancelEditCommand = cancel
            };
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
            };

            typeof(StrataChatComposer)
                .GetMethod("OnInputKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(composer, [composer, args]);

            Assert.True(args.Handled);
            Assert.Equal(1, cancel.ExecuteCount);
        });
    }

    [Fact]
    public async Task AltEnter_WhenEditingAndBusy_ExecutesEditSendOnly()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var stop = new RecordingCommand();
            var stopAndSend = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "updated turn",
                IsBusy = true,
                IsEditing = true,
                SendCommand = send,
                StopCommand = stop,
                StopAndSendCommand = stopAndSend
            };
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                KeyModifiers = KeyModifiers.Alt
            };

            typeof(StrataChatComposer)
                .GetMethod("OnInputKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(composer, [composer, args]);

            Assert.True(args.Handled);
            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("updated turn", send.LastParameter);
            Assert.Equal(0, stop.ExecuteCount);
            Assert.Equal(0, stopAndSend.ExecuteCount);
        });
    }

    [Fact]
    public async Task HandleStopAndSendAction_WhenCommandBound_ExecutesItWithPromptAndRaisesEvent()
    {
        await _fixture.Dispatch(() =>
        {
            var stopAndSend = new RecordingCommand();
            var send = new RecordingCommand();
            var stop = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "steer me",
                IsBusy = true,
                StopAndSendCommand = stopAndSend,
                SendCommand = send,
                StopCommand = stop
            };

            var raised = false;
            composer.StopAndSendRequested += (_, _) => raised = true;

            InvokeHandleStopAndSendAction(composer);

            Assert.True(raised);
            Assert.Equal(1, stopAndSend.ExecuteCount);
            Assert.Equal("steer me", stopAndSend.LastParameter);
            // The dedicated command handles abort+send itself; the Send/Stop fallback must NOT fire.
            Assert.Equal(0, send.ExecuteCount);
            Assert.Equal(0, stop.ExecuteCount);
        });
    }

    [Fact]
    public async Task HandleStopAndSendAction_WhenNoCommandBound_FallsBackToSendThenStop()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var stop = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "draft",
                IsBusy = true,
                SendCommand = send,
                StopCommand = stop
            };

            var stopAndSendRaised = false;
            var sendRaised = false;
            var stopRaised = false;
            composer.StopAndSendRequested += (_, _) => stopAndSendRaised = true;
            composer.SendRequested += (_, _) => sendRaised = true;
            composer.StopRequested += (_, _) => stopRaised = true;

            InvokeHandleStopAndSendAction(composer);

            Assert.True(stopAndSendRaised);
            Assert.True(sendRaised);
            Assert.True(stopRaised);
            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("draft", send.LastParameter);
            Assert.Equal(1, stop.ExecuteCount);
        });
    }

    [Fact]
    public async Task HandleStopAndSendAction_WhenEmptyAndCannotSendWithoutPrompt_DoesNothing()
    {
        await _fixture.Dispatch(() =>
        {
            var stopAndSend = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "   ",
                IsBusy = true,
                CanSendWithoutPrompt = false,
                StopAndSendCommand = stopAndSend
            };

            var raised = false;
            composer.StopAndSendRequested += (_, _) => raised = true;

            InvokeHandleStopAndSendAction(composer);

            Assert.False(raised);
            Assert.Equal(0, stopAndSend.ExecuteCount);
        });
    }

    [Theory]
    // Enter sends: Ctrl+Enter queues while busy.
    [InlineData(true, KeyModifiers.Control, true, 1, 0)]
    [InlineData(true, KeyModifiers.None, true, 0, 1)]
    // Ctrl+Enter sends: Ctrl+Shift+Enter queues while busy.
    [InlineData(false, KeyModifiers.Control | KeyModifiers.Shift, true, 1, 0)]
    [InlineData(false, KeyModifiers.Control, true, 0, 1)]
    // Not busy: the queue gesture is an ordinary send.
    [InlineData(true, KeyModifiers.Control, false, 0, 1)]
    public async Task QueueGesture_WhileBusy_QueuesInsteadOfSteering(
        bool sendWithEnter, KeyModifiers modifiers, bool busy, int expectedQueues, int expectedSends)
    {
        if (OperatingSystem.IsMacOS() && modifiers.HasFlag(KeyModifiers.Control))
            modifiers = (modifiers & ~KeyModifiers.Control) | KeyModifiers.Meta;

        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var queue = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "after this",
                IsBusy = busy,
                SendWithEnter = sendWithEnter,
                SendCommand = send,
                QueueCommand = queue
            };
            var queueRaised = false;
            composer.QueueRequested += (_, _) => queueRaised = true;
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                KeyModifiers = modifiers
            };

            typeof(StrataChatComposer)
                .GetMethod("OnInputKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(composer, [composer, args]);

            Assert.True(args.Handled);
            Assert.Equal(expectedQueues, queue.ExecuteCount);
            Assert.Equal(expectedQueues == 1, queueRaised);
            Assert.Equal(expectedSends, send.ExecuteCount);
            if (expectedQueues == 1)
                Assert.Equal("after this", queue.LastParameter);
        });
    }

    [Fact]
    public async Task QueueGesture_WithoutAQueueCommand_SteersAsBefore()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var stop = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "steer me",
                IsBusy = true,
                SendCommand = send,
                StopCommand = stop
            };
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                KeyModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control
            };

            typeof(StrataChatComposer)
                .GetMethod("OnInputKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(composer, [composer, args]);

            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal(0, stop.ExecuteCount);
        });
    }

    [Theory]
    [InlineData(true, "steer me", true, true)]
    [InlineData(false, "steer me", true, false)]
    [InlineData(true, "  ", true, false)]
    // Hosts that don't offer queueing keep a plain send button (no menu, no long-press menu on touch).
    [InlineData(true, "steer me", false, false)]
    public async Task SendButtonMenu_OffersSteerQueueAndStopAndSend_OnlyWhileBusyWithADraft(
        bool busy, string prompt, bool canQueue, bool opens)
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var queue = new RecordingCommand();
            var stopAndSend = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = prompt,
                IsBusy = busy,
                SendCommand = send,
                QueueCommand = canQueue ? queue : null,
                StopAndSendCommand = stopAndSend
            };
            var menu = (ContextMenu)typeof(StrataChatComposer)
                .GetMethod("BuildSendModeMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(composer, null)!;

            var prepared = (bool)typeof(StrataChatComposer)
                .GetMethod("PrepareSendModeMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(composer, [menu])!;

            Assert.Equal(opens, prepared);
            if (!opens)
                return;

            var items = menu.Items.Cast<MenuItem>().ToArray();
            var labels = InteractiveLabels.Current;
            Assert.Equal([labels["sendToSteer"], labels["addToQueue"], labels["stopAndSend"]],
                items.Select(item => (string)item.Header!));
            Assert.All(items, item => Assert.True(item.IsVisible));
            Assert.Equal(Key.Enter, items[1].InputGesture!.Key);

            items[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, queue.ExecuteCount);
            Assert.Equal("steer me", queue.LastParameter);
            Assert.Equal(0, send.ExecuteCount);
        });
    }

    [Fact]
    public void ShortcutText_NamesEnterAsTheKeyboardDoes()
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var enter = OperatingSystem.IsMacOS() ? "↩" : "Enter";

        Assert.Equal(enter, StrataKeyGestureConverter.Format(new KeyGesture(Key.Enter)));
        Assert.EndsWith(enter, StrataKeyGestureConverter.Format(new KeyGesture(Key.Enter, command)));
        Assert.EndsWith(enter, StrataKeyGestureConverter.Format(new KeyGesture(Key.Enter, KeyModifiers.Alt)));
        Assert.DoesNotContain("Return", StrataKeyGestureConverter.Format(new KeyGesture(Key.Enter, command | KeyModifiers.Shift)));
        Assert.Equal(
            Avalonia.Controls.Converters.PlatformKeyGestureConverter.ToPlatformString(new KeyGesture(Key.X, command)),
            StrataKeyGestureConverter.Format(new KeyGesture(Key.X, command)));
    }

    private static void InvokeHandleSendAction(StrataChatComposer composer)
    {
        typeof(StrataChatComposer)
            .GetMethod("HandleSendAction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, null);
    }

    private static void InvokeHandleStopAndSendAction(StrataChatComposer composer)
    {
        typeof(StrataChatComposer)
            .GetMethod("HandleStopAndSendAction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, null);
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

    private sealed class RecordingEditor : IStrataComposerEditor
    {
        public int FocusCount { get; private set; }
        public int CaretIndex { get; set; }

        public void FocusAt(int caretIndex)
        {
            CaretIndex = caretIndex;
            FocusCount++;
        }

        public void FocusAtEnd() => FocusCount++;
    }
}
