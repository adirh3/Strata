using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
}
