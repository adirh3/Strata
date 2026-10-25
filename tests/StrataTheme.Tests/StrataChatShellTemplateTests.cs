using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Rendering.Composition;
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

    [Fact]
    public async Task GlassComposer_StaysFullSizeWhileWriting_AndDrawsAFinerStrataLine()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer();
            composer.Classes.Add("glass");
            var elsewhere = new TextBox();
            var window = new Window { Width = 640, Height = 360 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = new DockPanel { Children = { elsewhere, composer } };
            DockPanel.SetDock(elsewhere, Dock.Top);

            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            Border Part(string name) => composer.GetVisualDescendants().OfType<Border>().First(border => border.Name == name);
            var root = Part("PART_Root");
            var line = Part("PART_FocusLine");
            var input = composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");
            var visual = ElementComposition.GetElementVisual(composer);
            Assert.NotNull(visual);

            // One glass surface, whose shadow a clip at the control's bounds would cut off square at the corners.
            Assert.False(composer.ClipToBounds);
            Assert.Equal(new Thickness(0), root.Margin);
            var resting = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(root.Background);
            Assert.True(root.BoxShadow.Count > 0);

            var fullSize = new Vector3D(1, 1, 1);
            Assert.Equal(fullSize, visual!.Scale);
            Assert.Equal(default, visual.CenterPoint);
            var bounds = composer.Bounds;
            var inputBounds = input.Bounds;

            Assert.True(input.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":engaged", composer.Classes);

            Assert.Equal(fullSize, visual.Scale);
            Assert.Equal(default, visual.CenterPoint);
            Assert.Equal(bounds, composer.Bounds);
            Assert.Equal(inputBounds, input.Bounds);
            Assert.Same(resting, root.Background);

            Assert.True(elsewhere.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":engaged", composer.Classes);
            Assert.Equal(fullSize, visual.Scale);
            Assert.Equal(default, visual.CenterPoint);

            composer.Classes.Add("mobile");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullSize, visual.Scale);
            composer.Classes.Remove("mobile");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullSize, visual.Scale);
            // Without motion the line also settles at once, so the rest reads its looks.
            composer.Classes.Add("motion-disabled");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullSize, visual.Scale);

            Assert.True(input.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullSize, visual.Scale);
            Assert.Equal(default, visual.CenterPoint);

            // The Strata line: finer than the plain composer's, and feathered at its ends.
            Assert.True(line.IsEffectivelyVisible);
            Assert.Equal(1, line.Opacity);
            Assert.Equal(1.5, line.Height);
            Assert.NotNull(line.OpacityMask);

            window.Close();
        });
    }

    [Fact]
    public async Task DesktopComposer_GivesTheTextRoom_LessBelowChips_AndLeavesTouchAlone()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer();
            var window = new Window { Width = 640, Height = 360 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;

            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var input = composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");
            var toolbar = composer.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "PART_Toolbar");

            Assert.Equal(new Thickness(18, 18, 18, 8), input.Padding);
            Assert.Equal(48, input.MinHeight);
            Assert.Equal(new Thickness(10), toolbar.Margin);

            composer.AgentName = "Coding Lumi";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(18, 12, 18, 8), input.Padding);

            composer.Classes.Add("mobile");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(12, 10, 12, 10), input.Padding);
            Assert.Equal(48, input.MinHeight);
            Assert.Equal(new Thickness(0), toolbar.Margin);

            window.Close();
        });
    }

    [Theory]
    [InlineData(false, 18)]
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
    public async Task ComposerDefaultEditor_CanPreferMobileAutocompleteAbove()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer
            {
                PreferAutoCompleteAbove = true,
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
            Assert.Equal(PlacementMode.Top, popup.Placement);
            Assert.Null(popup.PlacementRect);
            Assert.False(
                popup.PlacementConstraintAdjustment.HasFlag(
                    PopupPositionerConstraintAdjustment.FlipY));
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
