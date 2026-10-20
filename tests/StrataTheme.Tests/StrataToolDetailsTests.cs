using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>
/// A tool step's opened details: parameters written as <c>**Key:** value</c> lines become an aligned
/// list of fields, anything else stays markdown, and a failed step's result reads as its error.
/// </summary>
[Collection("Avalonia UI")]
public sealed class StrataToolDetailsTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataToolDetailsTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FieldLines_BecomeKeysAndValues_WithInlineCodeUnwrapped()
    {
        var fields = StrataToolDetails.ParseFields("**File:** `Program.cs`\n**Location:** `C:\\src`\n\n**Search query:** best OLED TV");

        Assert.NotNull(fields);
        Assert.Equal(
            new[] { ("File", "Program.cs"), ("Location", "C:\\src"), ("Search query", "best OLED TV") },
            fields!.Select(field => (field.Key, field.Value)));
    }

    [Theory]
    [InlineData("```powershell\ndotnet test\n```")]
    [InlineData("**Prompt:** inspect the fixture\nThen report back.")]
    [InlineData("Intent: Summarizing")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnythingButFieldLines_StaysMarkdown(string? text)
        => Assert.Null(StrataToolDetails.ParseFields(text));

    [Fact]
    public async Task OpenedDetails_ShowFieldsThenTheError_AndBuildOnlyWhileShown()
    {
        await _fixture.Dispatch(() =>
        {
            var details = new StrataToolDetails
            {
                Parameters = "**Search query:** Busy",
                Result = "The search service did not answer.",
                IsFailed = true,
            };
            Assert.Null(details.Child);
            // Folded away, like a closed step's well.
            var folded = new StrataToolDetails { Parameters = "**File:** a.txt" };
            var well = new Border { IsVisible = false, Child = folded };

            var window = new Window { Width = 480, Height = 300, Content = new StackPanel { Children = { details, well } } };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataAiToolCall.axaml"),
            });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = details.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Contains("Search query", texts);
            Assert.Contains("Busy", texts);
            Assert.Contains("The search service did not answer.", texts);
            Assert.Single(details.GetVisualDescendants().OfType<DockPanel>(), panel => panel.Classes.Contains("tool-detail-error"));
            Assert.Empty(details.GetVisualDescendants().OfType<StrataMarkdown>());
            Assert.Null(folded.Child);

            well.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("a.txt", folded.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

            details.IsFailed = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(details.GetVisualDescendants().OfType<DockPanel>(), panel => panel.Classes.Contains("tool-detail-error"));

            details.Parameters = "```sql\nselect 1\n```";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(details.GetVisualDescendants().OfType<StrataMarkdown>());

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public async Task Progress_RendersAndUpdatesMarkdown_WithoutReformattingResults()
    {
        await _fixture.Dispatch(() =>
        {
            const string progress = "## Searching **Microsoft 365**\n\n- Reading `mail`";
            const string result = "**Literal result** with `code`";
            var details = new StrataToolDetails
            {
                Parameters = "**Query:** project update",
                Progress = progress,
                Result = result,
                IsFailed = true,
            };
            Assert.Null(details.Child);
            var window = new Window { Width = 480, Height = 400, Content = details };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(progress, Assert.Single(details.GetVisualDescendants().OfType<StrataMarkdown>()).Markdown);
                Assert.Contains(details.GetVisualDescendants().OfType<SelectableTextBlock>(),
                    text => text.Text == result);

                details.Progress += "\n- Finding documents";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(details.Progress, Assert.Single(details.GetVisualDescendants().OfType<StrataMarkdown>()).Markdown);

                details.Progress = null;
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(details.GetVisualDescendants().OfType<StrataMarkdown>());
                Assert.Contains(details.GetVisualDescendants().OfType<SelectableTextBlock>(),
                    text => text.Text == result);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public async Task ElapsedText_CountsFromItsStart_AndStaysEmptyForTheFirstSecond()
    {
        await _fixture.Dispatch(() =>
        {
            var elapsed = new StrataElapsedText();
            var window = new Window { Width = 200, Height = 100, Content = elapsed };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Set once attached, so the readout is computed at once.
            elapsed.Since = DateTimeOffset.UtcNow.AddSeconds(-75);
            Assert.Equal("1m 15s", elapsed.Text);

            elapsed.Since = DateTimeOffset.UtcNow;
            Assert.Null(elapsed.Text);

            elapsed.Since = null;
            Assert.Null(elapsed.Text);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(42, "42 ms")]
    [InlineData(999.4, "999 ms")]
    [InlineData(999.6, "1.0s")]
    [InlineData(1234, "1.2s")]
    [InlineData(59_960, "1m 00s")]
    [InlineData(64_000, "1m 04s")]
    public void Duration_ReadsInTheUnitItRoundsTo(double milliseconds, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, RunningElapsedClock.FormatDuration(milliseconds));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}
