using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMarkdownRtlFlowArrowTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataMarkdownRtlFlowArrowTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void OrientFlowArrows_MirrorsCommonRtlFlowTokens()
    {
        const string source = "א ← ב → ג ⇐ ד ⇒ ה ⟵ ו ⟶ ז ⟸ ח ⟹ ט ⬅ י ➡ כ ↤ ל ↦ מ <- נ -> ס <-- ע --> פ";

        var oriented = StrataTextDirectionDetector.OrientFlowArrows(
            source,
            FlowDirection.RightToLeft);

        Assert.Equal(
            "א → ב ← ג ⇒ ד ⇐ ה ⟶ ו ⟵ ז ⟹ ח ⟸ ט ➡ י ⬅ כ ↦ ל ↤ מ -> נ <- ס --> ע <-- פ",
            oriented);
    }

    [Fact]
    public void OrientFlowArrows_LeavesLtrTextUnchanged()
    {
        const string source = "Plan → Build → Test → Ship";

        var oriented = StrataTextDirectionDetector.OrientFlowArrows(
            source,
            FlowDirection.LeftToRight);

        Assert.Same(source, oriented);
    }

    [Fact]
    public Task FormattedRtlMarkdown_OrientsProseArrowsButPreservesInlineCode()
    {
        return _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FlowDirection = FlowDirection.RightToLeft,
                Inlines = new InlineCollection()
            };

            markdown.AppendFormattedInlines(
                textBlock,
                "בקשה → **אימות → עיבוד** → [תשובה → סיום](https://example.com) → `x -> y`");

            var runs = textBlock.Inlines!.OfType<Run>().ToArray();
            Assert.Contains(runs, run => run.Text == "בקשה ← ");
            Assert.Contains(runs, run => run.Text == "אימות ← עיבוד" && run.FontWeight == FontWeight.Bold);
            Assert.Contains(runs, run => run.Text == "תשובה ← סיום");

            var codeRun = Assert.Single(runs.OfType<StrataMarkdown.InlineCodeRun>());
            Assert.Contains("x -> y", codeRun.Text);
        });
    }

    [Fact]
    public Task RtlBlock_PreservesEmbeddedLtrFlowFragments()
    {
        return _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FlowDirection = FlowDirection.RightToLeft,
                Inlines = new InlineCollection()
            };

            markdown.AppendFormattedInlines(
                textBlock,
                "שלום: **Plan → Build** _Test → Ship_ ~~Retry → Fix~~ [Open → Close](https://example.com) `x → y`");

            var runs = textBlock.Inlines!.OfType<Run>().ToArray();
            Assert.Contains(runs, run => run.Text == "Plan → Build" && run.FontWeight == FontWeight.Bold);
            Assert.Contains(runs, run => run.Text == "Test → Ship" && run.FontStyle == FontStyle.Italic);
            Assert.Contains(runs, run => run.Text == "Retry → Fix");
            Assert.Contains(runs, run => run.Text == "Open → Close");

            var codeRun = Assert.Single(runs.OfType<StrataMarkdown.InlineCodeRun>());
            Assert.Contains("x → y", codeRun.Text);
        });
    }

    [Fact]
    public Task RtlTarget_PreservesStandaloneLtrFragment()
    {
        return _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FlowDirection = FlowDirection.RightToLeft,
            };

            markdown.AppendFormattedInlines(textBlock, "Plan → Build");

            Assert.Equal("Plan → Build", textBlock.Text);
        });
    }

    [Fact]
    public async Task NeutralFormattedFlow_ReorientsWhenInheritedDirectionChanges()
    {
        var rendered = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = "**1** → **2** `3 → 4`",
                FontSize = 14,
                StreamingRebuildThrottleMs = 0
            };
            var window = new Window
            {
                Width = 400,
                Height = 200,
                FlowDirection = FlowDirection.RightToLeft,
                Content = markdown
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var rtl = ReadRenderedRuns(markdown);

            window.FlowDirection = FlowDirection.LeftToRight;
            Dispatcher.UIThread.RunJobs();
            var ltr = ReadRenderedRuns(markdown);

            window.FlowDirection = FlowDirection.RightToLeft;
            Dispatcher.UIThread.RunJobs();
            var rtlAgain = ReadRenderedRuns(markdown);

            window.Close();
            return (rtl, ltr, rtlAgain);
        });

        Assert.Contains("←", rendered.rtl.Prose);
        Assert.Contains("→", rendered.ltr.Prose);
        Assert.Contains("←", rendered.rtlAgain.Prose);
        Assert.All(
            new[] { rendered.rtl.Code, rendered.ltr.Code, rendered.rtlAgain.Code },
            code => Assert.Contains("3 → 4", code));
    }

    [Fact]
    public async Task NeutralPlainFlow_ReorientsWhenInheritedDirectionChanges()
    {
        var rendered = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = "1 → 2",
                FontSize = 14,
                StreamingRebuildThrottleMs = 0
            };
            var window = new Window
            {
                Width = 400,
                Height = 200,
                FlowDirection = FlowDirection.RightToLeft,
                Content = markdown
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var rtl = ReadRenderedRuns(markdown).Prose;

            window.FlowDirection = FlowDirection.LeftToRight;
            Dispatcher.UIThread.RunJobs();
            var ltr = ReadRenderedRuns(markdown).Prose;

            window.Close();
            return (rtl, ltr);
        });

        Assert.Contains("←", rendered.rtl);
        Assert.Contains("→", rendered.ltr);
    }

    [Fact]
    public async Task NeutralTableFlow_ReorientsWhenInheritedDirectionChanges()
    {
        var rendered = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                FontSize = 14,
                StreamingRebuildThrottleMs = 0
            };
            var window = new Window
            {
                Width = 500,
                Height = 250,
                FlowDirection = FlowDirection.RightToLeft,
                Content = markdown
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            markdown.Markdown =
                "| Flow | Code |\n" +
                "| --- | --- |\n" +
                "| **1** → **2** | `3 → 4` |";
            Dispatcher.UIThread.RunJobs();

            var rtlTable = FindTable(markdown);
            var rtl = ReadTableRuns(rtlTable);

            window.FlowDirection = FlowDirection.LeftToRight;
            Dispatcher.UIThread.RunJobs();
            var ltrTable = FindTable(markdown);
            var ltr = ReadTableRuns(ltrTable);

            window.FlowDirection = FlowDirection.RightToLeft;
            Dispatcher.UIThread.RunJobs();
            var rtlAgainTable = FindTable(markdown);
            var rtlAgain = ReadTableRuns(rtlAgainTable);

            window.Close();
            return (rtlTable, rtl, ltrTable, ltr, rtlAgainTable, rtlAgain);
        });

        Assert.Same(rendered.rtlTable, rendered.ltrTable);
        Assert.Same(rendered.rtlTable, rendered.rtlAgainTable);
        Assert.Contains("←", rendered.rtl.Prose);
        Assert.Contains("→", rendered.ltr.Prose);
        Assert.Contains("←", rendered.rtlAgain.Prose);
        Assert.All(
            new[] { rendered.rtl.Code, rendered.ltr.Code, rendered.rtlAgain.Code },
            code => Assert.Contains("3 → 4", code));
    }

    private static (string Prose, string Code) ReadRenderedRuns(StrataMarkdown markdown)
    {
        var textBlock = markdown.GetLogicalDescendants()
            .OfType<SelectableTextBlock>()
            .Single();
        return ReadRuns(textBlock);
    }

    private static Border FindTable(StrataMarkdown markdown)
    {
        return markdown.GetLogicalDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("strata-md-table"));
    }

    private static (string Prose, string Code) ReadTableRuns(Border table)
    {
        var cells = table.GetLogicalDescendants()
            .OfType<SelectableTextBlock>()
            .Select(ReadRuns)
            .ToArray();

        return (
            cells.Single(cell => cell.Prose.Contains('1')).Prose,
            cells.Single(cell => cell.Code.Contains('3')).Code);
    }

    private static (string Prose, string Code) ReadRuns(SelectableTextBlock textBlock)
    {
        var runs = textBlock.Inlines?.OfType<Run>().ToArray() ?? [];

        return (
            textBlock.Text
            ?? string.Concat(runs.Where(run => run is not StrataMarkdown.InlineCodeRun).Select(run => run.Text)),
            string.Concat(runs.OfType<StrataMarkdown.InlineCodeRun>().Select(run => run.Text)));
    }
}
