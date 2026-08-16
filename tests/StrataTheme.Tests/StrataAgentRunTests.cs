using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
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
}
