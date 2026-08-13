using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using System.Reflection;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataChatShellTemplateTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataChatShellTemplateTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AppliedTemplateExposesOverrideableHostsAndKeepsDesktopPadding()
    {
        await _fixture.Dispatch(() =>
        {
            var shell = new StrataChatShell
            {
                Transcript = new Border(),
                Composer = new Border()
            };
            var window = new Window { Width = 640, Height = 480 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = shell;

            window.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var composerHost = shell.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "PART_ComposerHost");
            var scrollContent = shell.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "PART_ScrollContent");

            Assert.Equal(new Thickness(12, 8, 12, 10), composerHost.Padding);
            Assert.Equal(new Thickness(16, 12, 16, 12), scrollContent.Padding);

            window.Close();
        });
    }

    [Theory]
    [InlineData(false, 16)]
    [InlineData(true, 48)]
    public async Task ComposerChipRemovalTargetsKeepDesktopDensityAndMeetMobileTouchFloor(
        bool mobile,
        double expectedSize)
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer
            {
                AgentName = "Coding Lumi",
                ProjectName = "Lumi"
            };
            if (mobile)
                composer.Classes.Add("mobile");

            var window = new Window { Width = 480, Height = 320 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;

            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            foreach (var name in new[] { "PART_AgentRemoveButton", "PART_ProjectRemoveButton" })
            {
                var button = composer.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(candidate => candidate.Name == name);

                Assert.True(button.IsEffectivelyVisible);
                Assert.Equal(expectedSize, button.Bounds.Width, 1);
                Assert.Equal(expectedSize, button.Bounds.Height, 1);
            }

            window.Close();
        });
    }

    [Fact]
    public async Task ComposerExternalEditor_ReplacesBuiltInInputInAppliedTemplate()
    {
        await _fixture.Dispatch(() =>
        {
            var externalEditor = new Border();
            var composer = new StrataChatComposer
            {
                EditorContent = externalEditor
            };
            var window = new Window { Width = 480, Height = 320 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;

            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var input = composer.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(control => control.Name == "PART_Input");
            var presenter = composer.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .Single(control => control.Name == "PART_EditorContent");

            Assert.False(input.IsEffectivelyVisible);
            Assert.True(presenter.IsEffectivelyVisible);
            Assert.Same(externalEditor, presenter.Content);

            window.Close();
        });
    }

    [Fact]
    public async Task ComposerExternalEditor_AutocompleteUsesExternalCaretAndRestoresFocus()
    {
        await _fixture.Dispatch(() =>
        {
            var editor = new ExternalEditor { CaretIndex = 3 };
            var composer = new StrataChatComposer
            {
                EditorContent = editor,
                PromptText = "@al",
                AvailableAgents = new[]
                {
                    new StrataComposerChip("Alice", "A", Value: "alice")
                }
            };
            var window = ShowComposer(composer);
            InstallAutocompleteParts(composer);
            Invoke(composer, "UpdateAutoCompletePlacementTarget");

            Invoke(composer, "CheckAutoComplete");
            var popup = GetPrivateField<Popup>(composer, "_autoCompletePopup");
            Assert.True(popup.IsOpen);
            Assert.Equal(PlacementMode.Top, popup.Placement);
            Assert.False(
                popup.PlacementConstraintAdjustment.HasFlag(
                    PopupPositionerConstraintAdjustment.FlipY));

            Invoke(composer, "ConfirmAutoComplete");

            Assert.Equal("", composer.PromptText);
            Assert.Equal("Alice", composer.AgentName);
            Assert.Equal("alice", composer.AgentValue);
            Assert.Equal(0, editor.CaretIndex);
            Assert.Equal(1, editor.FocusCount);
            window.Close();
        });
    }

    [Fact]
    public async Task ComposerExternalEditor_MentionButtonDoesNotFocusHiddenInput()
    {
        await _fixture.Dispatch(() =>
        {
            var editor = new ExternalEditor { CaretIndex = 5 };
            var composer = new StrataChatComposer
            {
                EditorContent = editor,
                PromptText = "hello",
                AvailableAgents = new[] { new StrataComposerChip("Alice", "A") }
            };
            var window = ShowComposer(composer);
            InstallAutocompleteParts(composer);

            Invoke(composer, "ShowMentionPopup");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("hello @", composer.PromptText);
            Assert.Equal(7, editor.CaretIndex);
            Assert.Equal(1, editor.FocusCount);
            Assert.True(GetPrivateField<Popup>(composer, "_autoCompletePopup").IsOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task ComposerDefaultEditor_KeepsDesktopAutocompletePlacement()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer();
            var window = ShowComposer(composer);
            InstallAutocompleteParts(composer);

            Invoke(composer, "UpdateAutoCompletePlacementTarget");

            var popup = GetPrivateField<Popup>(composer, "_autoCompletePopup");
            Assert.Equal(PlacementMode.AnchorAndGravity, popup.Placement);
            Assert.True(
                popup.PlacementConstraintAdjustment.HasFlag(
                    PopupPositionerConstraintAdjustment.FlipY));
            Assert.True(
                popup.PlacementConstraintAdjustment.HasFlag(
                    PopupPositionerConstraintAdjustment.SlideY));
            Assert.True(
                popup.PlacementConstraintAdjustment.HasFlag(
                    PopupPositionerConstraintAdjustment.ResizeY));
            window.Close();
        });
    }

    [Fact]
    public async Task ComposerExternalEditor_RefreshesAsyncFileSuggestionCollection()
    {
        await _fixture.Dispatch(() =>
        {
            var files = new ObservableCollection<StrataComposerChip>();
            var editor = new ExternalEditor { CaretIndex = 5 };
            var composer = new StrataChatComposer
            {
                EditorContent = editor,
                PromptText = "#chat",
                AvailableFiles = files
            };
            var window = ShowComposer(composer);
            InstallAutocompleteParts(composer);

            Invoke(composer, "CheckAutoComplete");
            Assert.False(GetPrivateField<Popup>(composer, "_autoCompletePopup").IsOpen);

            files.Add(new StrataComposerChip(
                "ChatView.axaml",
                "📄",
                SecondaryText: "src/Lumi/Views",
                Value: @"C:\repo\src\Lumi\Views\ChatView.axaml"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(GetPrivateField<Popup>(composer, "_autoCompletePopup").IsOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task ComposerExternalEditor_DoesNotConfirmSuggestionAfterCaretLeavesTrigger()
    {
        await _fixture.Dispatch(() =>
        {
            var editor = new ExternalEditor { CaretIndex = 3 };
            var composer = new StrataChatComposer
            {
                EditorContent = editor,
                PromptText = "@al",
                AvailableAgents = new[] { new StrataComposerChip("Alice", "A") }
            };
            var window = ShowComposer(composer);
            InstallAutocompleteParts(composer);
            Invoke(composer, "CheckAutoComplete");
            Assert.True(GetPrivateField<Popup>(composer, "_autoCompletePopup").IsOpen);

            editor.CaretIndex = 0;
            Invoke(composer, "ConfirmAutoComplete");

            Assert.Equal("@al", composer.PromptText);
            Assert.Null(composer.AgentName);
            Assert.False(GetPrivateField<Popup>(composer, "_autoCompletePopup").IsOpen);
            window.Close();
        });
    }

    private static Window ShowComposer(StrataChatComposer composer)
    {
        var window = new Window { Width = 480, Height = 320, Content = composer };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        composer.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void InstallAutocompleteParts(StrataChatComposer composer)
    {
        SetPrivateField(composer, "_autoCompletePopup", new Popup());
        SetPrivateField(composer, "_autoCompletePanel", new StackPanel());
        SetPrivateField(composer, "_autoCompleteScrollViewer", new ScrollViewer());
    }

    private static void Invoke(StrataChatComposer composer, string methodName) =>
        typeof(StrataChatComposer)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(composer, null);

    private static T GetPrivateField<T>(StrataChatComposer composer, string fieldName) where T : class =>
        Assert.IsType<T>(
            typeof(StrataChatComposer)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(composer));

    private static void SetPrivateField<T>(StrataChatComposer composer, string fieldName, T value) where T : class =>
        typeof(StrataChatComposer)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(composer, value);

    private sealed class ExternalEditor : Border, IStrataComposerEditor
    {
        public int CaretIndex { get; set; }
        public int FocusCount { get; private set; }

        public void FocusAt(int caretIndex)
        {
            CaretIndex = caretIndex;
            FocusCount++;
        }

        public void FocusAtEnd() => FocusCount++;
    }
}
