using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataMarkdownAutoDirectionTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataMarkdownAutoDirectionTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HebrewParagraph_AutomaticallyRendersRightToLeft()
    {
        var layout = await RenderParagraphAsync("\u05e9\u05dc\u05d5\u05dd \u05e2\u05d5\u05dc\u05dd");

        Assert.Equal(FlowDirection.RightToLeft, layout.FlowDirection);
        Assert.Equal(TextAlignment.Right, layout.TextAlignment);
    }

    [Fact]
    public async Task EnglishParagraph_RemainsLeftToRight()
    {
        var layout = await RenderParagraphAsync("Hello world");

        Assert.Equal(FlowDirection.LeftToRight, layout.FlowDirection);
        Assert.Equal(TextAlignment.Left, layout.TextAlignment);
    }

    [Fact]
    public async Task HebrewLink_UsesVisibleLabelInsteadOfHiddenUrl()
    {
        var layout = await RenderParagraphAsync(
            "[\u05e9\u05dc\u05d5\u05dd](https://example.com/english-destination)");

        Assert.Equal(FlowDirection.RightToLeft, layout.FlowDirection);
        Assert.Equal(TextAlignment.Right, layout.TextAlignment);
    }

    [Theory]
    [InlineData("`dotnet run --project Lumi.csproj` שלום עולם", FlowDirection.RightToLeft)]
    [InlineData("**`Dictionary<string, int>`** שלום עולם", FlowDirection.RightToLeft)]
    [InlineData("`שלום` Hello world", FlowDirection.LeftToRight)]
    [InlineData("`dotnet run`", FlowDirection.LeftToRight)]
    public async Task InlineCode_DoesNotOverrideTheSurroundingProseDirection(
        string source, FlowDirection expected)
    {
        var layout = await RenderParagraphAsync(source);

        Assert.Equal(expected, layout.FlowDirection);
        Assert.Equal(expected == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
            layout.TextAlignment);
    }

    [Fact]
    public Task LongInlineCode_DirectionProbeDoesNotCopyTheWholeCodeSpan()
    {
        return _fixture.Dispatch(() =>
        {
            var block = new SelectableTextBlock();
            var source = $"`{new string('x', 32_768)}`";
            StrataMarkdown.ApplyDirectionalTextLayout(block, FlowDirection.LeftToRight, "`warmup`");

            var before = GC.GetAllocatedBytesForCurrentThread();
            StrataMarkdown.ApplyDirectionalTextLayout(block, FlowDirection.LeftToRight, source);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(allocated < 32_768,
                $"The direction probe copied the full code span, allocating {allocated} bytes.");
            Assert.Equal(FlowDirection.LeftToRight, block.FlowDirection);

            StrataMarkdown.ApplyDirectionalTextLayout(block, FlowDirection.LeftToRight, source + " שלום עולם");
            Assert.Equal(FlowDirection.RightToLeft, block.FlowDirection);
        });
    }

    [Theory]
    [InlineData(FlowDirection.LeftToRight, "> שלום עם `code`", FlowDirection.RightToLeft)]
    [InlineData(FlowDirection.RightToLeft, "> English with `code`", FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.LeftToRight, "| שם | ערך |\n| --- | --- |\n| שלום | `code` |", FlowDirection.RightToLeft)]
    [InlineData(FlowDirection.RightToLeft, "| Name | Value |\n| --- | --- |\n| Hello | `code` |", FlowDirection.LeftToRight)]
    public async Task QuotesAndTables_FollowTheirContentInsteadOfTheInterfaceDirection(
        FlowDirection hostDirection, string source, FlowDirection expected)
    {
        await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                FlowDirection = hostDirection,
                Markdown = source,
                IsInline = true,
                StreamingRebuildThrottleMs = 0,
            };
            var window = new Window { Width = 500, Height = 240, Content = markdown };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var surface = Assert.Single(markdown.GetLogicalDescendants().OfType<Border>(),
                    control => control.Classes.Contains("strata-md-blockquote")
                        || control.Classes.Contains("strata-md-table"));

                Assert.Equal(expected, surface.FlowDirection);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData("\n \n")]
    [InlineData("\n\t\n")]
    public async Task AdjacentEnglishAndHebrewParagraphs_KeepIndependentDirections(string separator)
    {
        var directions = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = $"Hello world{separator}\u05e9\u05dc\u05d5\u05dd \u05e2\u05d5\u05dc\u05dd",
                FontSize = 14,
                StreamingRebuildThrottleMs = 0
            };
            var window = new Window
            {
                Width = 400,
                Height = 200,
                Content = markdown
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var result = markdown.GetLogicalDescendants()
                .OfType<SelectableTextBlock>()
                .Select(block => block.FlowDirection)
                .ToArray();
            window.Close();
            return result;
        });

        Assert.Equal(
            [FlowDirection.LeftToRight, FlowDirection.RightToLeft],
            directions);
    }

    [Theory]
    [InlineData("**Hello**\n\nשלום", "**Hello**\n\nשלום Hello", "Hello", "שלום Hello")]
    [InlineData("**שלום**\n\nHello", "**שלום**\n\nHello שלום עולם", "שלום", "Hello שלום עולם")]
    public async Task StreamingDirectionMerge_PreservesBothParagraphs(
        string initial,
        string appended,
        string firstExpected,
        string secondExpected)
    {
        var rendered = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = initial,
                FontSize = 14,
                StreamingRebuildThrottleMs = 0
            };
            var window = new Window { Width = 400, Height = 200, Content = markdown };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            markdown.Markdown = appended;
            Dispatcher.UIThread.RunJobs();

            var text = markdown.GetLogicalDescendants()
                .OfType<SelectableTextBlock>()
                .Select(RenderedText)
                .ToArray();
            window.Close();
            return text;
        });

        Assert.Contains(rendered, text => text.Contains(firstExpected, StringComparison.Ordinal));
        Assert.Contains(rendered, text => text.Contains(secondExpected, StringComparison.Ordinal));
    }

    private async Task<(FlowDirection FlowDirection, TextAlignment TextAlignment)> RenderParagraphAsync(string text)
    {
        return await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = text,
                FontSize = 14,
                StreamingRebuildThrottleMs = 0
            };
            var window = new Window
            {
                Width = 400,
                Height = 200,
                Content = markdown
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var paragraph = markdown.GetLogicalDescendants()
                .OfType<SelectableTextBlock>()
                .Single();
            var result = (paragraph.FlowDirection, paragraph.TextAlignment);
            window.Close();
            return result;
        });
    }

    private static string RenderedText(SelectableTextBlock block)
    {
        if (block.Text is { } text)
            return text;
        if (block.Inlines is null)
            return "";

        return string.Concat(block.Inlines.Select(inline => inline switch
        {
            Run run => run.Text,
            LineBreak => "\n",
            _ => ""
        }));
    }
}
