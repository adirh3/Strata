using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>
/// The transcript rail's geometry and its contract with <see cref="StrataChatShell"/>. Pointer behavior needs
/// a themed ScrollViewer and is covered by the Lumi chat view's tests, which run the real application theme.
/// </summary>
[Collection("Avalonia UI")]
public sealed class StrataTranscriptRailTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataTranscriptRailTests(AvaloniaFixture fixture) => _fixture = fixture;

    private static readonly TranscriptRailLayout Long = new(
        TrackTop: 6, TrackLength: 600, Extent: 10_000, Viewport: 800, Offset: 0, ReadingInset: 64, MinThumbLength: 28);

    [Fact]
    public void MarkSitsWhereTheThumbsReadingLineWillBeOnceItIsRead()
    {
        foreach (var position in new[] { 20d, 64, 1_000, 5_000, 9_230, 9_900 })
        {
            // Bring the place to the reading line, as far as the transcript scrolls.
            var offset = Math.Clamp(position - Long.ReadingInset, 0, Long.MaxOffset);
            var read = Long with { Offset = offset };
            var readingLine = read.ThumbTop + read.ThumbLength * (position - offset) / Long.Viewport;
            Assert.Equal(readingLine, Long.MarkY(position), 3);
        }

        // Marks keep the transcript's order, stay on the track and do not move as it scrolls.
        var ys = Enumerable.Range(0, 101).Select(static i => Long.MarkY(i * 100d)).ToArray();
        Assert.True(ys.Zip(ys.Skip(1)).All(static pair => pair.First <= pair.Second));
        Assert.All(ys, static y => Assert.InRange(y, Long.TrackTop, Long.TrackTop + Long.TrackLength));
        Assert.Equal(Long.MarkY(3_000), (Long with { Offset = 7_000 }).MarkY(3_000), 6);
    }

    [Fact]
    public void ThumbKeepsAGraspableLengthAndDraggingItMapsBackToTheOffset()
    {
        var huge = Long with { Extent = 400_000 };
        Assert.Equal(28, huge.ThumbLength);
        var scrolled = huge with { Offset = 150_000 };
        Assert.Equal(150_000, huge.OffsetForThumbTop(scrolled.ThumbTop), 3);
        Assert.Equal(0, huge.OffsetForThumbTop(-50));
        Assert.Equal(huge.MaxOffset, huge.OffsetForThumbTop(10_000));

        // A transcript that fits has nothing to map.
        Assert.False((Long with { Extent = 700 }).CanScroll);
        Assert.False((Long with { TrackLength = 0 }).CanScroll);
    }

    [Fact]
    public void FindsTheMarkBeingReadAndTheMarkNearestThePointer()
    {
        var marks = new[] { 100d, 400, 900 }.Select(static position => new StrataTranscriptMark(position, $"at {position}")).ToArray();
        Assert.Equal(-1, TranscriptRailLayout.IndexAtReadingLine(marks, offset: 0, readingInset: 64));
        Assert.Equal(0, TranscriptRailLayout.IndexAtReadingLine(marks, offset: 30, readingInset: 64));
        Assert.Equal(1, TranscriptRailLayout.IndexAtReadingLine(marks, offset: 400, readingInset: 64));
        Assert.Equal(2, TranscriptRailLayout.IndexAtReadingLine(marks, offset: 5_000, readingInset: 64));
        Assert.Equal(-1, TranscriptRailLayout.IndexAtReadingLine(Array.Empty<StrataTranscriptMark>(), 0, 64));

        var ys = new[] { 10d, 50, 52, 200 };
        Assert.Equal(0, TranscriptRailLayout.NearestIndex(ys, 12, reach: 22));
        Assert.Equal(2, TranscriptRailLayout.NearestIndex(ys, 51.5, reach: 22));
        Assert.Equal(1, TranscriptRailLayout.NearestIndex(ys, 40, reach: 22));
        Assert.Equal(3, TranscriptRailLayout.NearestIndex(ys, 260, reach: 80));
        Assert.Equal(-1, TranscriptRailLayout.NearestIndex(ys, 120, reach: 22));
        Assert.Equal(-1, TranscriptRailLayout.NearestIndex(Array.Empty<double>(), 0, reach: 22));
    }

    [Fact]
    public void MarksRippleOutFromTheReaderAsTheRailOpens()
    {
        Assert.Equal(0, StrataTranscriptRail.Reveal(openness: 0, y: 100, origin: 100, trackLength: 600));
        Assert.Equal(1, StrataTranscriptRail.Reveal(openness: 1, y: 600, origin: 0, trackLength: 600));
        Assert.True(StrataTranscriptRail.Reveal(0.4, y: 110, origin: 100, trackLength: 600)
                    > StrataTranscriptRail.Reveal(0.4, y: 500, origin: 100, trackLength: 600));
    }

    [Fact]
    public async Task ShellShowsTheRailWhenAskedAndCollectsTheHostsMarksInOrder()
    {
        await _fixture.Dispatch(() =>
        {
            var shell = new StrataChatShell
            {
                Header = new Border { Height = 64 },
                Composer = new Border { Height = 96 },
                Transcript = new StackPanel(),
                ExtendsTranscriptBehindChrome = true,
            };
            var window = new Window { Width = 400, Height = 800 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = shell;
            window.Show();
            try
            {
                shell.ApplyTemplate();
                Pump(window);
                var rail = Assert.IsType<StrataTranscriptRail>(shell.TranscriptRail);
                Assert.False(rail.IsVisible);
                Assert.DoesNotContain(":transcript-rail", shell.Classes);

                shell.ShowsTranscriptRail = true;
                Pump(window);
                Assert.True(rail.IsVisible);
                Assert.Contains(":transcript-rail", shell.Classes);
                // Kept between the floating header and composer, like the scrollbar it replaces.
                var header = shell.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_HeaderChrome");
                var composer = shell.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_ComposerHost");
                Assert.Equal(new Thickness(0, header.Bounds.Height, 0, composer.Bounds.Height), rail.Margin);
                Assert.Equal(header.Bounds.Height + 8, shell.ReadingLineInset, 1);
                // It stacks above the floating chrome, so their frost never covers its marks or preview.
                Assert.True(rail.ZIndex > header.ZIndex && rail.ZIndex > composer.ZIndex);

                // A host whose header frosts what is below it moves the reading line past the frost.
                shell.ReadingLineGap = 40;
                Assert.Equal(header.Bounds.Height + 40, shell.ReadingLineInset, 1);
                Assert.Equal(shell.ReadingLineInset, rail.ReadingInset, 1);

                var requests = 0;
                shell.TranscriptMarksRequested += (_, e) =>
                {
                    requests++;
                    e.Marks.Add(new StrataTranscriptMark(900, "third"));
                    e.Marks.Add(new StrataTranscriptMark(double.NaN, "unplaced"));
                    e.Marks.Add(new StrataTranscriptMark(100, "first"));
                    e.Marks.Add(new StrataTranscriptMark(100, "second"));
                };
                Assert.Equal(new[] { "first", "second", "third" }, shell.CollectTranscriptMarks().Select(static mark => mark.Text));
                Assert.Equal(1, requests);

                // A host that lands the place itself handles the pick.
                StrataTranscriptMark? picked = null;
                shell.TranscriptMarkInvoked += (_, e) =>
                {
                    picked = e.Mark;
                    e.Handled = true;
                };
                var mark = new StrataTranscriptMark(400, "picked", tag: "turn");
                shell.InvokeTranscriptMark(mark);
                Assert.Same(mark, picked);
                Assert.True(shell.IsFollowingTail);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Pump(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
