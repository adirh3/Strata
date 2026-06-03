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

    private static void InvokeHandleSendAction(StrataChatComposer composer)
    {
        typeof(StrataChatComposer)
            .GetMethod("HandleSendAction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, null);
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecuteCount { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => ExecuteCount++;
    }
}
