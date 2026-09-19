using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using StrataTheme.Animation;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class TextBoxFocusVisualTests
{
    private readonly AvaloniaFixture _fixture;

    public TextBoxFocusVisualTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FocusAccentFollowsTheRoundedTextBoxOutline()
    {
        await _fixture.Dispatch(() =>
        {
            var textBox = new TextBox
            {
                Width = 260,
                CornerRadius = new CornerRadius(16)
            };
            var window = new Window { Width = 320, Height = 120 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = textBox;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            textBox.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var accent = textBox.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "FocusAccentBar");

            Assert.Equal(textBox.CornerRadius, accent.CornerRadius);
            Assert.Equal(new Thickness(2), accent.BorderThickness);
            Assert.True(double.IsNaN(accent.Height));
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, accent.VerticalAlignment);
            window.Close();
        });
    }

    [Fact]
    public async Task EmbeddedChatEditorsUseTheirIntendedFocusTreatment()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer();
            var message = new StrataChatMessage
            {
                Content = "Editable message",
                EditText = "Editable message",
                IsEditing = true
            };
            var window = new Window
            {
                Width = 420,
                Height = 320
            };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = new StackPanel
            {
                Children =
                {
                    composer,
                    message
                }
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            message.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var composerEditor = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(textBox => textBox.Classes.Contains("composer-embed"));
            composerEditor.ApplyTemplate();
            var composerAccent = composerEditor.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "FocusAccentBar");
            Assert.False(composerAccent.IsVisible);
            var composerLine = composer.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "PART_FocusLine");
            Assert.Equal(2, composerLine.Height);
            Assert.Equal(new Thickness(14, 0), composerLine.Margin);
            Assert.Equal(new Thickness(0), composerLine.BorderThickness);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, composerLine.VerticalAlignment);

            var messageEditor = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(textBox => textBox.Classes.Contains("message-edit-embed"));
            messageEditor.ApplyTemplate();
            var messageAccent = messageEditor.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "FocusAccentBar");
            Assert.False(messageAccent.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public Task ComposerSweep_WaitsForTheDrawThenStopsAfterTwoSeconds() =>
        _fixture.Dispatch(async () =>
        {
            var composer = new StrataChatComposer { Width = 400 };
            var outside = new Button { Content = "Outside" };
            var window = CreateComposerWindow(new StackPanel { Children = { composer, outside } });
            try
            {
                var input = FindPart<TextBox>(composer, "PART_Input");
                var line = FindPart<Border>(composer, "PART_FocusLine");
                var sweep = FindPart<Border>(composer, "PART_FocusSweep");
                Assert.True(outside.Focus());
                Assert.True(input.Focus());
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);

                await Task.Delay(180);
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                await Task.Delay(370);
                Dispatcher.UIThread.RunJobs();
                Assert.True(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0.6, sweep.Opacity);

                composer.PromptText = "Typing does not extend the highlight";
                composer.Width = 430;
                window.UpdateLayout();
                await Task.Delay(1300);
                Assert.True(LifecycleOffsetSweep.IsRunning(sweep));

                await Task.Delay(900);
                Dispatcher.UIThread.RunJobs();
                Assert.False(LifecycleOffsetSweep.GetIsActive(sweep));
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);
                Assert.Equal(1, line.Opacity);
                AssertNoFocusTimer(composer);

                composer.PromptText += ". Still focused";
                composer.Width = 420;
                window.UpdateLayout();
                await Task.Delay(450);
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);

                Assert.True(outside.Focus());
                Assert.True(input.Focus());
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                await Task.Delay(500);
                Dispatcher.UIThread.RunJobs();
                Assert.True(LifecycleOffsetSweep.IsRunning(sweep));
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task ComposerSweep_BlurAndDisabledMotionCancelPendingStarts() =>
        _fixture.Dispatch(async () =>
        {
            var composer = new StrataChatComposer { Width = 400 };
            var outside = new Button { Content = "Outside" };
            var window = CreateComposerWindow(new StackPanel { Children = { composer, outside } });
            try
            {
                var input = FindPart<TextBox>(composer, "PART_Input");
                var sweep = FindPart<Border>(composer, "PART_FocusSweep");
                Assert.True(outside.Focus());
                Assert.True(input.Focus());
                await Task.Delay(100);
                Assert.True(outside.Focus());
                await Task.Delay(450);
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);

                Assert.True(input.Focus());
                composer.Classes.Add("motion-disabled");
                await Task.Delay(500);
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);

                composer.Classes.Remove("motion-disabled");
                await Task.Delay(500);
                Assert.True(LifecycleOffsetSweep.IsRunning(sweep));
                composer.Classes.Add("motion-disabled");
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);
            }
            finally
            {
                window.Close();
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComposerSweep_DetachCancelsPendingAndRunningTimers(bool afterDraw) =>
        _fixture.Dispatch(async () =>
        {
            var composer = new StrataChatComposer { Width = 400 };
            var window = CreateComposerWindow(composer);
            try
            {
                var input = FindPart<TextBox>(composer, "PART_Input");
                var sweep = FindPart<Border>(composer, "PART_FocusSweep");
                Assert.True(input.Focus());
                if (afterDraw)
                {
                    await Task.Delay(500);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(LifecycleOffsetSweep.IsRunning(sweep));
                }

                window.Content = new Border();
                Dispatcher.UIThread.RunJobs();
                AssertNoFocusTimer(composer);
                Assert.False(LifecycleOffsetSweep.GetIsActive(sweep));
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                Assert.Equal(0, sweep.Opacity);

                await Task.Delay(500);
                Assert.False(LifecycleOffsetSweep.IsRunning(sweep));
                AssertNoFocusTimer(composer);
            }
            finally
            {
                window.Close();
            }
        });

    private static void AssertNoFocusTimer(StrataChatComposer composer)
    {
        var field = typeof(StrataChatComposer).GetField("_focusSweepTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field.GetValue(composer));
    }

    private static T FindPart<T>(Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);

    private static Window CreateComposerWindow(Control content)
    {
        var window = new Window { Width = 500, Height = 260 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = content;
        window.Show();
        if (content is StackPanel panel)
        {
            foreach (var composer in panel.Children.OfType<StrataChatComposer>())
                composer.ApplyTemplate();
        }
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
