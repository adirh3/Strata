using System.Reflection;
using System.Windows.Input;
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
}
