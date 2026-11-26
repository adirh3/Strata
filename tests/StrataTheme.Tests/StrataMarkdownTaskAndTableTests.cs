using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMarkdownTaskAndTableTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task TaskList_DrawsStrataCheckboxes_NotTextGlyphs() => fixture.Dispatch(() =>
    {
        var (window, markdown) = Show("- [x] Ship the beta\n- [ ] Write the notes");
        try
        {
            var text = Assert.Single(markdown.GetVisualDescendants().OfType<SelectableTextBlock>());
            var inlines = Assert.IsType<InlineCollection>(text.Inlines);
            var markers = inlines.OfType<StrataMarkdown.TaskMarkerRun>().ToList();
            Assert.Equal([true, false], markers.Select(marker => marker.IsChecked));
            Assert.DoesNotContain('☐', inlines.Text!);
            Assert.DoesNotContain('☑', inlines.Text!);

            // Done text is struck through and quieter; its checkbox is not.
            var done = inlines.TakeWhile(inline => inline is not LineBreak).OfType<Run>()
                .Where(run => run is not StrataMarkdown.TaskMarkerRun).ToList();
            Assert.All(done, run => Assert.Equal(TextDecorationLocation.Strikethrough,
                Assert.Single(run.TextDecorations!).Location));
            Assert.Null(markers[0].TextDecorations);
            var open = inlines.SkipWhile(inline => inline is not LineBreak).OfType<Run>()
                .Where(run => run is not StrataMarkdown.TaskMarkerRun).ToList();
            Assert.All(open, run => Assert.Null(run.TextDecorations));

            // Two boxes and a tick, each in its own em space at the start of its line.
            var layer = Assert.IsType<StrataMarkdown.InlineCodeLayer>(text.Parent);
            var drawing = new DrawingGroup();
            using (var context = drawing.Open())
                layer.Render(context);
            var shapes = Flatten(drawing).OfType<GeometryDrawing>().ToList();
            Assert.Equal(3, shapes.Count);
            var space = text.TextLayout.HitTestTextRange(0, 1).Single();
            var bounds = drawing.GetBounds();
            Assert.InRange(bounds.Left, space.Left - 1, space.Left + 3);
            Assert.InRange(bounds.Right, space.Right - 3, space.Right + 1);
            Assert.True(bounds.Height > text.LineHeight, "Both lines have a box.");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TaskList_TellsAssistiveTechnologyEachTasksState() => fixture.Dispatch(() =>
    {
        static string Name(Control text) =>
            ControlAutomationPeer.CreatePeerForElement(text).GetName().Replace("\r\n", "\n");

        var (window, markdown) = Show("- [x] Ship the beta\n- [ ] Write the notes");
        var (loneWindow, lone) = Show("```\ncode\n```\n\n- [ ] Revi");
        var (plainWindow, plain) = Show("A **plain** line");
        try
        {
            // A checkbox is only room in the text, so it is read out as the box it shows.
            Assert.Equal("☑ Ship the beta\n☐ Write the notes",
                Name(Assert.Single(markdown.GetVisualDescendants().OfType<SelectableTextBlock>())));
            Assert.Equal("☐ Revi", Name(lone.GetVisualDescendants().OfType<SelectableTextBlock>().Last()));
            // Other text reads as it always has.
            Assert.Equal("A plain line", Name(Assert.Single(plain.GetVisualDescendants().OfType<SelectableTextBlock>())));
        }
        finally
        {
            window.Close();
            loneWindow.Close();
            plainWindow.Close();
        }
    });

    [Fact]
    public Task TaskList_SelectionReadsAndCopiesEachTasksState() => fixture.Dispatch(() =>
    {
        var (window, markdown) = Show("- [x] Ship the ~~alpha~~ beta\n- [ ] Write the notes");
        try
        {
            StrataTextSelection.SelectAll(markdown);
            var selected = StrataTextSelection.GetSelectedText(markdown)!.Replace("\r\n", "\n");
            Assert.Equal("☑ Ship the alpha beta\n☐ Write the notes", selected);

            // A done task copies as written: its strike is only how it shows, while its own ~~ stays.
            var copied = ChatClipboardData.SelectionMarkdown(StrataTextSelection.GetSelection(markdown))
                .Replace("\r\n", "\n");
            Assert.Equal("- [x] Ship the ~~alpha~~ beta\n- [ ] Write the notes", copied);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LoneTaskItem_KeepsItsCheckboxWhileItStreams() => fixture.Dispatch(() =>
    {
        var (window, markdown) = Show("```\ncode\n```\n\n- [ ] Revi");
        try
        {
            var before = markdown.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(block => block.Classes.Contains("strata-md-task-text"));

            markdown.Markdown = "```\ncode\n```\n\n- [ ] Review the plan";
            Pump(window);

            var after = markdown.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(block => block.Classes.Contains("strata-md-task-text"));
            Assert.Same(before, after);
            var inlines = Assert.IsType<InlineCollection>(after.Inlines);
            Assert.IsType<StrataMarkdown.TaskMarkerRun>(inlines[0]);
            Assert.EndsWith("Review the plan", inlines.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TableCopyButton_KeepsClearOfTheLastHeader() => fixture.Dispatch(() =>
    {
        var (window, markdown) = Show(
            "| Name | A rather long final column header that keeps going |\n|---|---|\n| One | Two |", width: 420);
        try
        {
            var table = markdown.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("strata-md-table"));
            var copy = table.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_CopyTableButton");
            var chip = copy.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "PART_Chip");
            var lastHeader = table.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("strata-md-table-header-cell"))
                .OrderBy(border => border.TranslatePoint(default, table)!.Value.X)
                .Last();
            var headerText = lastHeader.GetVisualDescendants().OfType<SelectableTextBlock>().Single();

            var textRight = headerText.TranslatePoint(new Point(headerText.Bounds.Width, 0), table)!.Value.X;
            var copyLeft = chip.TranslatePoint(default, table)!.Value.X;
            Assert.True(textRight <= copyLeft - 2,
                $"The header's text reaches {textRight:0.#}, under the copy button at {copyLeft:0.#}.");
        }
        finally { window.Close(); }
    });

    private static (Window Window, StrataMarkdown Markdown) Show(string source, double width = 640)
    {
        var markdown = new StrataMarkdown
        {
            Markdown = source,
            IsInline = true,
            StreamingRebuildThrottleMs = 0,
        };
        var window = new Window { Width = width, Height = 420, Content = markdown };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Show();
        Pump(window);
        return (window, markdown);
    }

    private static IEnumerable<Drawing> Flatten(DrawingGroup group) =>
        group.Children.SelectMany(child => child is DrawingGroup inner ? Flatten(inner) : [child]);

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
