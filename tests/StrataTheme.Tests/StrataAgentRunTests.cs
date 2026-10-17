using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataAgentRunTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataAgentRunTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunningCard_ShowsLiveRailAndActivity_ThenDropsThemWhenFinished()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataAgentRun
            {
                AgentName = "Explore agent",
                Title = "Inspect the transcript pipeline",
                Activity = "Running command \u00b7 dotnet build",
                Status = StrataAiToolCallStatus.InProgress,
            };

            var window = Host(card);
            Dispatcher.UIThread.RunJobs();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(":inprogress", card.Classes);
            Assert.Contains(":has-activity", card.Classes);
            Assert.Contains(":has-progress", card.Classes);
            // No measurable progress yet, so the rail reads as indeterminate rather than stuck at 0.
            Assert.Contains(":progress-unknown", card.Classes);

            card.ProgressValue = 40;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":progress-unknown", card.Classes);

            card.Status = StrataAiToolCallStatus.Completed;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(":completed", card.Classes);
            // The activity line and progress rail are live-only affordances.
            Assert.DoesNotContain(":has-activity", card.Classes);
            Assert.DoesNotContain(":has-progress", card.Classes);
        });
    }

    [Fact]
    public async Task Byline_JoinsIdentityPartsAndSkipsMissingOnes()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataAgentRun { AgentName = "Research agent" };
            var window = Host(card);
            Dispatcher.UIThread.RunJobs();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Research agent", card.Byline);

            card.ModelName = "Claude Sonnet 4.6";
            card.ModeLabel = "Background";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Research agent \u00b7 Claude Sonnet 4.6 \u00b7 Background", card.Byline);
        });
    }

    [Fact]
    public async Task StatusLabel_OverridesTheDefaultWordingForLocalizedHosts()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataAgentRun { Status = StrataAiToolCallStatus.InProgress };
            var window = Host(card);
            Dispatcher.UIThread.RunJobs();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Working", card.StatusText);

            card.StatusLabel = "\u05e4\u05d5\u05e2\u05dc";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("\u05e4\u05d5\u05e2\u05dc", card.StatusText);
        });
    }

    [Fact]
    public async Task TimingText_ReadsElapsedWhileRunningAndFreezesOnCompletion()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataAgentRun
            {
                Status = StrataAiToolCallStatus.InProgress,
                RunningSince = DateTimeOffset.UtcNow.AddSeconds(-42),
            };

            var window = Host(card);
            Dispatcher.UIThread.RunJobs();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            // Derived from the authoritative start instant, so it survives control recreation.
            Assert.False(string.IsNullOrWhiteSpace(card.TimingText));

            card.DurationMs = 2500;
            card.Status = StrataAiToolCallStatus.Completed;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("2.5s", card.TimingText);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public async Task InlineChip_PutsItsActionAfterTheOutcome_WhileOtherCardsKeepItFirst()
    {
        await _fixture.Dispatch(() =>
        {
            foreach (var inline in new[] { true, false })
            {
                var action = new Border { Width = 10, Height = 10 };
                var card = new StrataAgentRun
                {
                    AgentName = "Explore agent",
                    Title = "Inspect the transcript",
                    Status = StrataAiToolCallStatus.Completed,
                    HeaderAction = action,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                if (inline)
                    card.Classes.Add("inline");

                var window = inline ? HostWithTheme(card) : Host(card);
                Dispatcher.UIThread.RunJobs();

                var pill = card.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_StatusPill");
                var actionX = action.TranslatePoint(default, card)!.Value.X;
                var pillX = pill.TranslatePoint(default, card)!.Value.X;
                if (inline)
                {
                    Assert.True(actionX > pillX, $"An inline chip's action ({actionX}) should follow its outcome ({pillX}).");

                    // One row: the chip's height is the row's tallest part plus the header's padding, so it
                    // matches the other 31px chips in the app's font, with every part on one centre line.
                    var avatar = card.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Avatar");
                    var title = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Title");
                    var row = Math.Max(avatar.Bounds.Height, title.Bounds.Height);
                    var titleRow = card.GetVisualDescendants().OfType<DockPanel>().Single(panel => panel.Name == "PART_TitleRow");
                    var statusArea = card.GetVisualDescendants().OfType<DockPanel>().Single(panel => panel.Name == "PART_StatusArea");
                    row = Math.Max(row, Math.Max(titleRow.Bounds.Height, statusArea.Bounds.Height));
                    Assert.True(Math.Abs(row + 12 + 2 - card.Bounds.Height) < 0.6,
                        $"Chip {card.Bounds.Height} should be its row {row} plus padding; avatar {avatar.Bounds.Height}, title {title.Bounds.Height}, title row {titleRow.Bounds.Height}, status {statusArea.Bounds.Height}.");
                    double CenterY(Visual visual) => visual.TranslatePoint(new Point(0, visual.Bounds.Height / 2), card)!.Value.Y;
                    // Within half a pixel: an odd gap rounds to whole pixels at 100% scaling.
                    Assert.InRange(CenterY(title) - CenterY(avatar), -0.6, 0.6);
                    Assert.InRange(CenterY(pill) - CenterY(avatar), -0.6, 0.6);
                }
                else
                {
                    Assert.True(actionX < pillX, $"A card's action ({actionX}) should precede its status pill ({pillX}).");
                }

                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    private static Window Host(StrataAgentRun card)
    {
        var window = new Window { Width = 640, Height = 300 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/Controls/StrataAgentRun.axaml"),
        });
        window.Content = card;
        window.Show();
        return window;
    }

    /// <summary>With the full theme, so text has the app's font metrics.</summary>
    private static Window HostWithTheme(StrataAgentRun card)
    {
        var window = new Window { Width = 640, Height = 300 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml"),
        });
        window.Content = card;
        window.Show();
        return window;
    }
}
