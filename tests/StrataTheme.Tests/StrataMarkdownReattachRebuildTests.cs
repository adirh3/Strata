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

    // Regression guard for the chat-switch / finish-writing UI freeze.
    //
    // Transcript bubbles use RetainContentOnDetach = true, and ChatSessionStore keeps backgrounded
    // chat surfaces alive. So when the user switches to another chat, the previous chat's markdown
    // controls stay alive and bound -- just detached. If that chat keeps streaming or finishes
    // writing, its Markdown changes and, without a guard, ScheduleRebuild()/FlushQueuedRebuild()
    // would parse + syntax-highlight the (possibly huge) content ON THE UI THREAD while off-screen,
    // freezing the foreground chat. The fix defers the parse while detached (RetainContentOnDetach)
    // and re-posts one coalesced rebuild on reattach.
    //
    // StreamingRebuildThrottleMs = 0 forces the immediate (delayMs == 0) Loaded-post path -- the one
    // OnDetached does NOT cancel -- so RunJobs() actually drives FlushQueuedRebuild while detached and
    // exercises the new guard. RebuildCount (incremented in Rebuild()'s finally) is the authoritative
    // "did it parse?" signal.
    [Fact]
    public async Task RetainDetached_MarkdownChange_DefersParseUntilReattach()
    {
        var (detachedDelta, reattachDelta) = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                RetainContentOnDetach = true,
                StreamingRebuildThrottleMs = 0,
                FontSize = 14,
            };

            var host = new Border();
            var window = new Window { Width = 400, Height = 300, Content = host };
            host.Child = markdown;
            window.Show();

            // Parse initial content while attached.
            markdown.Markdown = "**bold** OLD content marker one";
            Dispatcher.UIThread.RunJobs();

            // Detach (retained), then change content while off-screen -- as a backgrounded chat would
            // while it keeps streaming / finishes writing. This must NOT parse on the UI thread.
            host.Child = null;
            var beforeDetachedChange = StrataMarkdown.CaptureDiagnostics();
            markdown.Markdown = "**bold** NEW content marker that differs from the old one";
            Dispatcher.UIThread.RunJobs();
            var detachedDelta = StrataMarkdown.CaptureDiagnostics() - beforeDetachedChange;

            // Reattach: the deferred change must now flush exactly once so content is not lost.
            var beforeReattach = StrataMarkdown.CaptureDiagnostics();
            host.Child = markdown;
            Dispatcher.UIThread.RunJobs();
            var reattachDelta = StrataMarkdown.CaptureDiagnostics() - beforeReattach;

            window.Close();
            return (detachedDelta, reattachDelta);
        });

        // The actual freeze: zero UI-thread parsing while detached. Airtight -- RebuildCount counts
        // every Rebuild() entry (including fast-path/empty exits), so 0 invocations means 0 parses.
        Assert.Equal(0, detachedDelta.RebuildCount);

        // Reattach must flush EXACTLY ONE coalesced rebuild. Asserting == 1 (not > 0) proves the
        // deferred updates coalesce -- a regression that re-posted two rebuilds on reattach would fail.
        Assert.Equal(1, reattachDelta.RebuildCount);

        // ...and that single rebuild must perform exactly one *real* parse. RebuildCount alone also
        // counts empty/identical fast-path exits, so assert a genuine full-or-incremental parse ran,
        // proving the retained content was actually refreshed to the latest Markdown.
        Assert.Equal(1, reattachDelta.FullParseCount + reattachDelta.IncrementalParseCount);
    }

    // Complements the deferral guard: a control that is STILL ATTACHED must keep parsing markdown
    // changes immediately, so the detached-defer guard cannot over-suppress the normal hot path.
    [Fact]
    public async Task RetainAttached_MarkdownChange_StillParsesImmediately()
    {
        var parsedWhileAttached = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                RetainContentOnDetach = true,
                StreamingRebuildThrottleMs = 0,
                FontSize = 14,
            };

            var host = new Border();
            var window = new Window { Width = 400, Height = 300, Content = host };
            host.Child = markdown;
            window.Show();

            markdown.Markdown = "**bold** initial attached content";
            Dispatcher.UIThread.RunJobs();

            var before = StrataMarkdown.CaptureDiagnostics();
            markdown.Markdown = "**bold** updated attached content that changed";
            Dispatcher.UIThread.RunJobs();
            var delta = StrataMarkdown.CaptureDiagnostics() - before;

            window.Close();
            return delta.RebuildCount;
        });

        Assert.True(
            parsedWhileAttached > 0,
            "An attached retain-content markdown must still parse content changes immediately.");
    }

    // Regression guard for the latent staleness edge found in code review: when a retained, detached
    // control has its Markdown set to null while a rebuild is deferred, OnAttachedToVisualTree must
    // still flush that deferred rebuild on reattach so the stale retained content is cleared -- rather
    // than taking the "Markdown is null" early-out and leaving _rebuildQueued stuck true with the old
    // tree on screen. StreamingRebuildThrottleMs = 0 drives the deferred clear through the new guard.
    [Fact]
    public async Task ReattachAfterRetainDetach_MarkdownSetNullWhileDetached_ClearsStaleContent()
    {
        var remainingTextBlocks = await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown
            {
                RetainContentOnDetach = true,
                StreamingRebuildThrottleMs = 0,
                FontSize = 14,
            };

            var host = new Border();
            var window = new Window { Width = 400, Height = 300, Content = host };
            host.Child = markdown;
            window.Show();

            // Render real content while attached so the retained tree has children to leave stale.
            markdown.Markdown = "**bold** content that will be cleared";
            Dispatcher.UIThread.RunJobs();

            // Detach (retained -- children kept), then clear Markdown to null while off-screen. The new
            // FlushQueuedRebuild guard defers the clear, leaving _rebuildQueued == true and the OLD
            // content still rendered.
            host.Child = null;
            markdown.Markdown = null;
            Dispatcher.UIThread.RunJobs();

            // Reattach: the deferred clear must flush even though Markdown is now null.
            host.Child = markdown;
            Dispatcher.UIThread.RunJobs();

            var count = markdown.GetLogicalDescendants().OfType<SelectableTextBlock>().Count();
            window.Close();
            return count;
        });

        Assert.Equal(0, remainingTextBlocks);
    }
}
