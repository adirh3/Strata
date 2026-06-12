using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataMarkdownReattachRebuildTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataMarkdownReattachRebuildTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    // Regression guard for a retain-detach blank-content bug found in multi-model code review.
    //
    // When RetainContentOnDetach is true, OnDetachedFromVisualTree cancels the pending delayed
    // rebuild timer but intentionally keeps _rebuildQueued == true (so the reattach else-if can
    // re-post it). If that cancellation happens while content is still empty -- e.g. a just-started
    // assistant turn (Markdown "" -> first token) is switched away-and-back mid-throttle -- the
    // reattach hits the Children.Count == 0 branch, whose ScheduleRebuild() would early-return on
    // the still-true _rebuildQueued flag, leaving the control permanently blank with the flag stuck.
    // The fix clears the stale flag in that branch before scheduling.
    //
    // Determinism notes:
    //  * The lambda is synchronous and pumps via Dispatcher.UIThread.RunJobs(). An async lambda is
    //    unsafe here: AvaloniaFixture.Dispatch forwards to Dispatch<TResult>(Func<TResult>), so an
    //    async lambda is wrapped as Task<Task> and its inner continuation (and any assertion failure
    //    in it) is never observed -- the test would pass vacuously.
    //  * delayMs is steered via StreamingRebuildThrottleMs, not wall-clock: a huge throttle forces
    //    the pre-detach update onto the cancellable delayed-rebuild path (which OnDetached cancels,
    //    leaving the flag stuck), and throttle 0 makes the post-reattach rebuild an immediate Loaded
    //    post that RunJobs() flushes.
    //  * FontSize is pinned locally so the inherited-FontSize re-resolution on attach (see
    //    StrataMarkdown.OnPropertyChanged) cannot trigger a self-healing Rebuild() that would mask
    //    the bug.
    [Fact]
    public async Task ReattachAfterRetainDetach_WhileRebuildPendingAndEmpty_RendersContent()
    {
        var selectableTextBlocks = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                RetainContentOnDetach = true,
                StreamingRebuildThrottleMs = 100_000,
                FontSize = 14,
            };

            var host = new Border();
            var window = new Window
            {
                Width = 400,
                Height = 300,
                Content = host,
            };
            host.Child = markdown;
            window.Show();

            // First render with empty content: first-ever rebuild takes the immediate (delayMs == 0)
            // path, clears to zero children, and stamps _lastRebuildAtUtc to "now".
            markdown.Markdown = "";
            Dispatcher.UIThread.RunJobs();

            // 1. Set real content -> elapsed ~0 against the huge throttle -> the DELAYED (cancellable)
            //    rebuild path runs, leaving _rebuildQueued == true with a pending CTS while content is
            //    still empty (no RunJobs here, so the delayed rebuild never fires).
            markdown.Markdown = "Hello world regression content";
            // 2. Retain-detach -> cancels the CTS but keeps _rebuildQueued == true (children still 0).
            host.Child = null;
            // 3. Drop the throttle to 0 so the rebuild the reattach *should* schedule is an immediate
            //    Loaded post (this ScheduleRebuild no-ops while the stale flag is set).
            markdown.StreamingRebuildThrottleMs = 0;
            // 4. Reattach while _contentHost is still empty -> the stuck-flag window.
            host.Child = markdown;

            // Flush the Loaded rebuild the (fixed) reattach scheduled. Without the fix nothing is
            // scheduled and the control stays blank.
            Dispatcher.UIThread.RunJobs();

            var count = markdown.GetLogicalDescendants().OfType<SelectableTextBlock>().Count();
            window.Close();
            return count;
        });

        Assert.True(
            selectableTextBlocks > 0,
            "StrataMarkdown rendered blank after a retain-detach/reattach that cancelled a pending rebuild while empty (stuck _rebuildQueued regression).");
    }
}
