using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataChatComposerVoiceTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataChatComposerVoiceTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Escape_WhileRecording_DiscardsDictation()
    {
        await _fixture.Dispatch(() =>
        {
            var discard = new CountingCommand();
            var composer = new StrataChatComposer
            {
                IsRecording = true,
                CanVoice = true,
                VoiceCancelCommand = discard
            };
            var args = KeyDown(Key.Escape);

            InvokeKeyDown(composer, args);

            Assert.True(args.Handled);
            Assert.Equal(1, discard.ExecuteCount);
        });
    }

    [Fact]
    public async Task Escape_WithoutRecording_LeavesDictationAlone()
    {
        await _fixture.Dispatch(() =>
        {
            var discard = new CountingCommand();
            var composer = new StrataChatComposer { VoiceCancelCommand = discard };
            var args = KeyDown(Key.Escape);

            InvokeKeyDown(composer, args);

            Assert.False(args.Handled);
            Assert.Equal(0, discard.ExecuteCount);
        });
    }

    [Fact]
    public async Task Enter_WhileRecordingWithNothingTypedYet_SendsSoTheHostCanFinishFirst()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new CountingCommand();
            var composer = new StrataChatComposer
            {
                PromptText = "",
                IsRecording = true,
                CanVoice = true,
                SendCommand = send
            };
            var args = KeyDown(Key.Enter);

            InvokeKeyDown(composer, args);

            Assert.True(args.Handled);
            Assert.Equal(1, send.ExecuteCount);
        });
    }

    [Fact]
    public async Task VoiceStatusAndPreview_AreShownAsPseudoClasses()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer { IsRecording = true };
            Assert.DoesNotContain(":voice-status", composer.Classes);
            Assert.DoesNotContain(":voice-preview", composer.Classes);

            composer.VoiceStatus = "Starting…";
            composer.VoicePreview = "שלום";
            Assert.Contains(":voice-status", composer.Classes);
            Assert.Contains(":voice-preview", composer.Classes);
            Assert.Contains(":recording", composer.Classes);

            composer.VoiceStatus = "";
            composer.VoicePreview = "  ";
            Assert.DoesNotContain(":voice-status", composer.Classes);
            Assert.DoesNotContain(":voice-preview", composer.Classes);
        });
    }

    [Fact]
    public async Task Waveform_AddsTheLoudestLevelOfEachMomentAndStartsAfreshWhenListeningAgain()
    {
        await _fixture.Dispatch(() =>
        {
            var waveform = new StrataVoiceWaveform { IsActive = true };
            waveform.Level = 0.2;
            waveform.Level = 0.9;
            waveform.Level = 0.4;
            waveform.Sample();
            waveform.Sample();
            Assert.Equal(2, waveform.HeardCount);

            waveform.Level = 7;
            Assert.Equal(1, waveform.Level);
            waveform.Level = double.NaN;
            Assert.Equal(0, waveform.Level);

            waveform.IsActive = false;
            Assert.Equal(2, waveform.HeardCount);
            waveform.IsActive = true;
            Assert.Equal(0, waveform.HeardCount);
        });
    }

    [Fact]
    public async Task TailPanel_KeepsTheNewestLinesInView()
    {
        await _fixture.Dispatch(() =>
        {
            var text = new TextBlock { Text = "one two three four five six seven", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Width = 40 };
            var panel = new ComposerTailPanel { Child = text, MaxHeight = 20 };
            panel.Measure(new Size(40, double.PositiveInfinity));
            panel.Arrange(new Rect(panel.DesiredSize));

            Assert.True(text.DesiredSize.Height > 20);
            Assert.Equal(20, panel.Bounds.Height);
            Assert.Equal(20 - text.DesiredSize.Height, text.Bounds.Y, precision: 3);
            Assert.Contains(":clipped", panel.Classes);
        });
    }

    private static KeyEventArgs KeyDown(Key key) => new()
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key
    };

    private static void InvokeKeyDown(StrataChatComposer composer, KeyEventArgs args)
        => typeof(StrataChatComposer)
            .GetMethod("OnInputKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, [composer, args]);

    private sealed class CountingCommand : ICommand
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
