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
