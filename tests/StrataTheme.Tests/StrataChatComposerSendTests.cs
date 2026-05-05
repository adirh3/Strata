using System.Reflection;
using System.Windows.Input;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class StrataChatComposerSendTests : IClassFixture<AvaloniaFixture>
{
    [Fact]
    public void HandleSendAction_WhenPromptIsEmptyAndEmptySendIsAllowed_ExecutesSendCommand()
    {
        Dispatcher.UIThread.Invoke(() =>
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
    public void HandleSendAction_WhenPromptIsEmptyAndEmptySendIsNotAllowed_DoesNotExecuteSendCommand()
    {
        Dispatcher.UIThread.Invoke(() =>
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