using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMarkdownCitationTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData("See [1](https://a.example/one) here", 4, "1", "https://a.example/one", null)]
    [InlineData("Ranked [12](https://a.example/twelve).", 7, "12", "https://a.example/twelve", null)]
    [InlineData("Says so ([Reuters](https://www.reuters.com/x)).", 8, "Reuters", "https://www.reuters.com/x", null)]
    [InlineData("Per ([https://www.bbc.com/news/1](https://www.bbc.com/news/1))", 4, "bbc.com", "https://www.bbc.com/news/1", null)]
    [InlineData("Proven[^1] twice", 6, "1", null, "1")]
    [InlineData("Noted[^src-a].", 5, "src-a", null, "src-a")]
    public void CitationsAreRecognized(string text, int pos, string label, string? target, string? footnote)
    {
        Assert.True(StrataMarkdown.TryMatchCitation(text, pos, out var end, out var foundLabel, out var foundTarget, out var foundFootnote));
        Assert.Equal(label, foundLabel);
        Assert.Equal(target, foundTarget);
        Assert.Equal(footnote, foundFootnote);
        Assert.True(end > pos && end <= text.Length);
    }

    [Theory]
    [InlineData("[docs](https://a.example)", 0)]
    [InlineData("[2024](https://a.example)", 0)]
    [InlineData("[1]", 0)]
    [InlineData("see ([the **docs**](https://a.example))", 4)]
    [InlineData("see ([docs](https://a.example) and more)", 4)]
    [InlineData("[^1](https://a.example)", 0)]
    [InlineData("[^a b]", 0)]
    public void OtherLinksAndBracketsAreNotCitations(string text, int pos)
    {
        Assert.False(StrataMarkdown.TryMatchCitation(text, pos, out _, out _, out _, out _));
    }

    [Fact]
    public void FootnoteDefinitionsBecomeTheirOwnBlocks()
    {
        var blocks = MarkdownParser.Parse("Claim[^1].\n\n[^1]: [Paper](https://a.example/p)\n[^note]: Plain note\n    [^2]: indented code\n[^ ]: nope");

        var notes = blocks.Where(block => block.Kind == MdBlockKind.Footnote).ToList();
        Assert.Equal(["1", "note"], notes.Select(note => note.Language));
        Assert.Equal("[Paper](https://a.example/p)", notes[0].Content);
        Assert.Equal("Plain note", notes[1].Content);
    }

    [Theory]
    [InlineData("[Paper](https://a.example/p) by X", "https://a.example/p")]
    [InlineData("Smith, 2020. https://doi.example/10.1/x.", "https://doi.example/10.1/x")]
    [InlineData("Just words", null)]
    public void AFootnoteLeadsToItsFirstAddress(string definition, string? expected)
    {
        Assert.Equal(expected, StrataMarkdown.FirstLinkTarget(definition));
    }

    [Fact]
    public Task CitationsRenderAsChipsThatLeadToTheirSources_AndNotesGatherBelow() => fixture.Dispatch(async () =>
    {
        var markdown = new StrataMarkdown
        {
            Markdown = "Rates fell [1](https://a.example/one) as reported ([Reuters](https://www.reuters.com/x)) and confirmed[^2].\n\n"
                + "[^2]: [Central bank statement](https://bank.example/statement)\n[^3]: Unused note",
        };
        var window = new Window { Width = 640, Height = 400 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/")) { Source = new Uri("avares://StrataTheme/StrataTheme.axaml") });
        window.Content = markdown;
        try
        {
            window.Show();
            await SettleAsync();

            var blocks = markdown.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();
            var chips = blocks.SelectMany(block => block.Inlines?.OfType<StrataMarkdown.CitationRun>() ?? []).ToList();
            Assert.Equal(["1", "Reuters", "2", "2", "3"], chips.Select(chip => chip.Label));

            var answer = blocks.Single(block => block.Inlines!.OfType<StrataMarkdown.CitationRun>().Any(chip => !chip.IsFootnoteLabel));
            var references = answer.Inlines!.OfType<StrataMarkdown.CitationRun>().ToList();
            Assert.Equal("https://a.example/one", markdown.GetLinkTarget(references[0]));
            Assert.Equal("https://www.reuters.com/x", markdown.GetLinkTarget(references[1]));
            // A footnote reference leads where its definition does.
            Assert.Equal("https://bank.example/statement", markdown.GetLinkTarget(references[2]));
            // The parentheses around a cited site go with the chip.
            Assert.DoesNotContain("(", string.Concat(answer.Inlines!.OfType<Run>().Select(run => run.Text)));

            // The chip is hit like any link, here the footnote's.
            var chipBounds = answer.TextLayout.HitTestTextRange(Offset(answer, references[2]), references[2].Text!.Length).First();
            Assert.Equal("https://bank.example/statement", markdown.GetLinkAtPoint(answer, chipBounds.Center));

            // Definitions are notes, not paragraphs of raw syntax.
            var notes = Assert.Single(blocks, block => block.Classes.Contains("strata-md-footnotes"));
            Assert.All(notes.Inlines!.OfType<StrataMarkdown.CitationRun>(), chip => Assert.True(chip.IsFootnoteLabel));
            Assert.DoesNotContain(blocks, block => (block.Text ?? string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text))).Contains("[^"));

            // Copying the answer keeps its citations as markdown.
            var copied = ChatClipboardData.SelectionMarkdown(
                [new StrataTextSelection.TextRange(answer, 0, answer.Inlines!.Text.Length)], includeEmbeddedBlocks: false);
            Assert.Contains("[1](https://a.example/one)", copied);
            Assert.Contains("[Reuters](https://www.reuters.com/x)", copied);
            Assert.Contains("[^2]", copied);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task AFootnoteWithoutAnAddressIsNotALink() => fixture.Dispatch(async () =>
    {
        var markdown = new StrataMarkdown { Markdown = "Claim[^n] and pending[^later].\n\n[^n]: Author's own note" };
        var window = new Window { Width = 500, Height = 300, Content = markdown };
        try
        {
            window.Show();
            await SettleAsync();
            var chips = markdown.GetVisualDescendants().OfType<SelectableTextBlock>()
                .SelectMany(block => block.Inlines?.OfType<StrataMarkdown.CitationRun>() ?? [])
                .Where(chip => !chip.IsFootnoteLabel).ToList();
            Assert.Equal(2, chips.Count);
            Assert.All(chips, chip => Assert.Null(markdown.GetLinkTarget(chip)));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task NotesStreamingInAreUpdatedInPlace() => fixture.Dispatch(async () =>
    {
        const string answer = "Claim[^1].\n\n[^1]: [Paper](https://a.example/p)";
        var markdown = new StrataMarkdown { Markdown = answer };
        var window = new Window { Width = 500, Height = 300, Content = markdown };
        try
        {
            window.Show();
            await SettleAsync();
            var notes = Notes(markdown);

            markdown.Markdown = answer + " by Smith\n[^2]: https://b.example/q";
            await SettleAsync();

            Assert.Same(notes, Notes(markdown));
            Assert.Equal(["1", "2"], notes.Inlines!.OfType<StrataMarkdown.CitationRun>().Select(chip => chip.Label));
            Assert.Contains("by Smith", string.Concat(notes.Inlines!.OfType<Run>().Select(run => run.Text)));
        }
        finally
        {
            window.Close();
        }

        static SelectableTextBlock Notes(StrataMarkdown markdown) => markdown.GetVisualDescendants()
            .OfType<SelectableTextBlock>().Single(block => block.Classes.Contains("strata-md-footnotes"));
    });

    [Fact]
    public Task AChipNeverBreaksAcrossLines() => fixture.Dispatch(() =>
    {
        var markdown = new StrataMarkdown();
        var text = new SelectableTextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
        markdown.AppendFormattedInlines(text,
            "Rates held steady across the quarter [12](https://a.example) while others cited ([New York Times](https://b.example)) again.");
        var window = new Window { Width = 700, Height = 400, Content = text };
        try
        {
            window.Show();
            var chips = text.Inlines!.OfType<StrataMarkdown.CitationRun>().ToList();
            Assert.Equal(2, chips.Count);
            // Whatever the width (wider than the longest chip itself), each chip lands on a single line.
            for (var width = 220; width <= 660; width += 2)
            {
                text.Width = width;
                Dispatcher.UIThread.RunJobs();
                foreach (var chip in chips)
                {
                    // Zero-width boxes draw nothing: a chip that fills a line can push its invisible
                    // closing mark to the next one.
                    Assert.Single(text.TextLayout.HitTestTextRange(Offset(text, chip), chip.Text!.Length),
                        rect => rect.Width > 0);
                }
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void PlainTextCopyKeepsEachNoteWithItsId()
    {
        var plain = MarkdownClipboardFormatter.ToPlainText("Claim[^1] and more[^2].\n\n[^1]: [Paper](https://a.example/p)\n[^2]: Plain note");

        Assert.Contains("Claim[^1] and more[^2].", plain);
        Assert.Contains("[^1]: Paper", plain);
        Assert.Contains("[^2]: Plain note", plain);
    }

    private static int Offset(SelectableTextBlock block, Inline target)
    {
        var offset = 0;
        foreach (var inline in block.Inlines!)
        {
            if (ReferenceEquals(inline, target))
                return offset;
            offset += inline is Run run ? run.Text?.Length ?? 0 : inline is LineBreak ? Environment.NewLine.Length : 1;
        }
        throw new InvalidOperationException("The inline is not in the block.");
    }

    private static async Task SettleAsync()
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 200)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16);
        }
    }
}
