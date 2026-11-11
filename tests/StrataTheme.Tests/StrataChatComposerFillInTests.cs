using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataChatComposerFillInTests
{
    private const string Template = "Help me choose a [product] under [budget]";

    private readonly AvaloniaFixture _fixture;

    public StrataChatComposerFillInTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BeginFillIn_SelectsTheFirstBlank_AndTabMovesBetweenTheBlanks()
    {
        await _fixture.Dispatch(() =>
        {
            var (composer, input, _) = NewComposer(Template);

            composer.BeginFillIn(["[product]", "[budget]"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("[product]", Selected(input));

            Assert.True(Press(composer, Key.Tab));
            Assert.Equal("[budget]", Selected(input));

            // Past the last blank, Tab wraps to the first; Shift+Tab goes back.
            Assert.True(Press(composer, Key.Tab));
            Assert.Equal("[product]", Selected(input));
            Assert.True(Press(composer, Key.Tab, KeyModifiers.Shift));
            Assert.Equal("[budget]", Selected(input));
        });
    }

    [Fact]
    public async Task Enter_ShowsEachBlankOnce_ThenSends()
    {
        await _fixture.Dispatch(() =>
        {
            var (composer, input, send) = NewComposer(Template);
            composer.BeginFillIn(["[product]", "[budget]"]);
            Dispatcher.UIThread.RunJobs();

            // The user types over the first blank, and Enter takes them to the next one instead of sending.
            Type(composer, input, "Help me choose a laptop under [budget]");
            Assert.True(Press(composer, Key.Enter));
            Assert.Equal("[budget]", Selected(input));
            Assert.Equal(0, send.ExecuteCount);

            // Every blank has been shown: Enter sends what is there, even a blank left as it is.
            Assert.True(Press(composer, Key.Enter));
            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("Help me choose a laptop under [budget]", send.LastParameter);
        });
    }

    [Fact]
    public async Task Enter_SendsOnceEveryBlankIsFilled()
    {
        await _fixture.Dispatch(() =>
        {
            var (composer, input, send) = NewComposer("Plan a trip to [where?]");
            composer.BeginFillIn(["[where?]"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("[where?]", Selected(input));

            Type(composer, input, "Plan a trip to Rome");
            Assert.True(Press(composer, Key.Enter));

            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("Plan a trip to Rome", send.LastParameter);
        });
    }

    [Fact]
    public async Task ClearingThePrompt_EndsTheGuide()
    {
        await _fixture.Dispatch(() =>
        {
            var (composer, input, _) = NewComposer(Template);
            composer.BeginFillIn(["[product]", "[budget]"]);
            Dispatcher.UIThread.RunJobs();

            Type(composer, input, "");
            Type(composer, input, "My own [notes] stay as they are");

            Assert.False(Press(composer, Key.Tab));
        });
    }

    [Fact]
    public async Task APromptWithoutBlanks_PutsTheCaretAtTheEnd()
    {
        await _fixture.Dispatch(() =>
        {
            var (composer, input, _) = NewComposer("Review my changes");
            composer.BeginFillIn([]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Review my changes".Length, input.CaretIndex);
            Assert.Equal("", Selected(input));
            Assert.False(Press(composer, Key.Tab));
        });
    }

    [Fact]
    public async Task TypingInTheRealComposer_ReplacesOnlyTheSelectedBlank()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var composer = new StrataChatComposer { SendWithEnter = true, SendCommand = send };
            var window = new Window { Width = 640, Height = 320 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var input = composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");

                composer.PromptText = Template;
                composer.BeginFillIn(["[product]", "[budget]"]);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("[product]", Selected(input));

                input.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "laptop" });
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Help me choose a laptop under [budget]", input.Text);

                Assert.True(Press(composer, Key.Enter));
                input.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "$1000" });
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Help me choose a laptop under $1000", input.Text);
                Assert.Equal("Help me choose a laptop under $1000", composer.PromptText);

                Assert.False(Press(composer, Key.Enter, KeyModifiers.Shift));
                Assert.True(Press(composer, Key.Enter));
                Assert.Equal(1, send.ExecuteCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task AHostEditor_HasItsBlanksSelected_AndTheAdvancingSendButtonWalksThemBeforeSending()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var editor = new RecordingEditor();
            var composer = new StrataChatComposer
            {
                SendCommand = send,
                PromptText = Template,
                EditorContent = editor,
                AdvancesFillInOnSend = true
            };

            composer.BeginFillIn(["[product]", "[budget]"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((Template.IndexOf("[product]", StringComparison.Ordinal), "[product]".Length), editor.Selections.Single());
            Assert.Contains(":fill-in-next", composer.Classes);

            // The user types over the first blank in their editor; send takes them to the next one instead.
            composer.PromptText = "Help me choose a laptop under [budget]";
            editor.CaretIndex = "Help me choose a laptop".Length;
            Click(composer);
            Assert.Equal(0, send.ExecuteCount);
            Assert.Equal(("Help me choose a laptop under ".Length, "[budget]".Length), editor.Selections[^1]);
            Assert.DoesNotContain(":fill-in-next", composer.Classes);

            // Every blank has been shown: send sends.
            Click(composer);
            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("Help me choose a laptop under [budget]", send.LastParameter);
        });
    }

    [Fact]
    public async Task WithoutAdvancing_TheSendButtonSendsAtOnce()
    {
        await _fixture.Dispatch(() =>
        {
            var (composer, input, send) = NewComposer(Template);
            composer.BeginFillIn(["[product]", "[budget]"]);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":fill-in-next", composer.Classes);

            Click(composer);

            Assert.Equal(1, send.ExecuteCount);
            Assert.Equal("[product]", Selected(input));
        });
    }

    [Fact]
    public async Task TheAdvancingSendButton_SaysItGoesToTheNextBlank_AndTurnsItsArrow()
    {
        await _fixture.Dispatch(() =>
        {
            var send = new RecordingCommand();
            var composer = new StrataChatComposer { SendCommand = send, AdvancesFillInOnSend = true };
            var window = new Window { Width = 640, Height = 320 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var input = composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");
                var button = composer.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "PART_SendButton");

                composer.PromptText = Template;
                composer.BeginFillIn(["[product]", "[budget]"]);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("[product]", Selected(input));
                Assert.Equal(InteractiveLabels.Current["nextBlank"], Avalonia.Automation.AutomationProperties.GetName(button));
                Assert.Contains(":fill-in-next", composer.Classes);

                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("[budget]", Selected(input));
                Assert.True(input.IsFocused);
                Assert.Equal(0, send.ExecuteCount);
                Assert.Equal(InteractiveLabels.Current["send"], Avalonia.Automation.AutomationProperties.GetName(button));
                Assert.DoesNotContain(":fill-in-next", composer.Classes);

                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, send.ExecuteCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task ClearingThePrompt_TurnsTheAdvancingSendButtonBackAtOnce()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer { SendCommand = new RecordingCommand(), AdvancesFillInOnSend = true };
            var window = new Window { Width = 640, Height = 320 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var button = composer.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "PART_SendButton");
                composer.PromptText = Template;
                composer.BeginFillIn(["[product]", "[budget]"]);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(":fill-in-next", composer.Classes);

                composer.IsBusy = true;
                composer.PromptText = "";

                Assert.DoesNotContain(":fill-in-next", composer.Classes);
                Assert.Equal(InteractiveLabels.Current["stopGeneration"], Avalonia.Automation.AutomationProperties.GetName(button));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (StrataChatComposer Composer, TextBox Input, RecordingCommand Send) NewComposer(string prompt)
    {
        var send = new RecordingCommand();
        var composer = new StrataChatComposer { SendWithEnter = true, SendCommand = send };
        var input = new TextBox();
        typeof(StrataChatComposer)
            .GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(composer, input);
        composer.PromptText = prompt;
        return (composer, input, send);
    }

    private static void Type(StrataChatComposer composer, TextBox input, string text)
    {
        composer.PromptText = text;
        input.Text = text;
        input.CaretIndex = text.Length;
    }

    private static string Selected(TextBox input)
    {
        var start = Math.Min(input.SelectionStart, input.SelectionEnd);
        var end = Math.Max(input.SelectionStart, input.SelectionEnd);
        return (input.Text ?? "")[start..end];
    }

    private static bool Press(StrataChatComposer composer, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        typeof(StrataChatComposer)
            .GetMethod("OnInputKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, [composer, args]);
        return args.Handled;
    }

    /// <summary>What the send button does when it is pressed.</summary>
    private static void Click(StrataChatComposer composer) =>
        typeof(StrataChatComposer)
            .GetMethod("HandleSendAction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, []);

    /// <summary>A host's own editor, as a phone's native text field is: it only knows its caret and what it was told to select.</summary>
    private sealed class RecordingEditor : Control, IStrataComposerEditor
    {
        public int CaretIndex { get; set; }
        public List<(int Start, int Length)> Selections { get; } = [];

        public void FocusAt(int caretIndex) => CaretIndex = caretIndex;

        public void FocusAtEnd()
        {
        }

        public void FocusSelect(int start, int length)
        {
            Selections.Add((start, length));
            CaretIndex = start;
        }
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
