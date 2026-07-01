using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

// Regression guard for "the two-option comparison component sometimes doesn't show".
//
// The ```comparison``` block (and its chart/card/confidence/sources siblings) is rendered by
// deserializing its JSON payload. Models occasionally emit a *nearly* valid payload — most
// commonly a dropped trailing brace — and the old strict JsonDocument.Parse threw, so the whole
// block silently collapsed to a placeholder. ParseVizJson now falls back to a structure-balancing
// repair so a single missing brace no longer blanks the component.
public class ComparisonBlockRepairTests
{
    private const string MalformedComparison =
        "Here is my recommendation\n\n" +
        "```comparison\n" +
        "{\"optionA\":{\"title\":\"A\",\"content\":\"alpha\"},\"optionB\":{\"title\":\"B\",\"content\":\"beta\"}\n" + // missing final }
        "```\n";

    private const string ValidComparison =
        "```comparison\n" +
        "{\"optionA\":{\"title\":\"A\",\"content\":\"alpha\"},\"optionB\":{\"title\":\"B\",\"content\":\"beta\"}}\n" +
        "```\n";

    private const string BrokenComparison =
        "```comparison\n" +
        "{\"optionA\":\n" + // genuinely unrenderable — no value, cannot be repaired into valid shape
        "```\n";

    // ── Pure RepairVizJson / ParseVizJson unit tests (no Avalonia) ──────────

    // Drives the real StrataMarkdown.ParseVizJson so these tests fail if the production
    // strict-then-repair logic — or the streaming gate — ever changes.
    private static bool TryParseComparison(string json, bool allowRepair)
    {
        try
        {
            using var doc = StrataMarkdown.ParseVizJson(json.Trim(), allowRepair);
            return doc.RootElement.TryGetProperty("optionA", out _) || doc.RootElement.TryGetProperty("optionB", out _);
        }
        catch { return false; }
    }

    [Fact]
    public void RepairVizJson_AppendsMissingClosingBrace()
    {
        const string missing = "{\"optionA\":{\"title\":\"A\"},\"optionB\":{\"title\":\"B\"}"; // 3 opens, 2 closes
        var repaired = StrataMarkdown.RepairVizJson(missing);
        Assert.Equal(missing + "}", repaired);
        using var doc = JsonDocument.Parse(repaired);
        Assert.True(doc.RootElement.TryGetProperty("optionB", out _));
    }

    [Fact]
    public void RepairVizJson_LeavesValidJsonUntouched()
    {
        const string valid = "{\"optionA\":{\"title\":\"A\",\"content\":\"a\"},\"optionB\":{\"title\":\"B\",\"content\":\"b\"}}";
        Assert.Equal(valid, StrataMarkdown.RepairVizJson(valid));
    }

    [Fact]
    public void RepairVizJson_DoesNotAlterBracesInsideStringValues()
    {
        // Structural chars inside string content must be treated as literals.
        const string tricky = "{\"optionA\":{\"title\":\"T\",\"content\":\"code: if (x) { y[0]; } done\"},\"optionB\":{\"title\":\"U\",\"content\":\"x\"}}";
        Assert.Equal(tricky, StrataMarkdown.RepairVizJson(tricky));
        Assert.True(TryParseComparison(tricky, allowRepair: true));
    }

    [Fact]
    public void RepairVizJson_ClosesDanglingStringAndObjects()
    {
        const string partial = "{\"optionA\":{\"title\":\"A partial title";
        var repaired = StrataMarkdown.RepairVizJson(partial);
        using var doc = JsonDocument.Parse(repaired); // must be valid JSON
        Assert.True(doc.RootElement.TryGetProperty("optionA", out _));
    }

    [Theory]
    [InlineData("{\"optionA\":{\"title\":\"A\"},\"optionB\":{\"title\":\"B\"}")]          // missing brace
    [InlineData("{\"optionA\":{\"title\":\"A\"},\"optionB\":{\"title\":\"B\"},}")]        // trailing commas
    [InlineData("{\"optionA\":{\"title\":\"A\"},\"optionB\":{\"title\":\"B\"}}")]         // already valid
    public void ParseVizJson_WithRepair_RecoversNearlyValidPayloads(string json)
    {
        Assert.True(TryParseComparison(json, allowRepair: true));
    }

    [Fact]
    public void ParseVizJson_WithRepair_StillRejectsGenuinelyBrokenPayload()
    {
        Assert.False(TryParseComparison("{\"optionA\":", allowRepair: true));
    }

    [Fact]
    public void ParseVizJson_RepairIsGatedToCompleteBlocks()
    {
        const string missingBrace = "{\"optionA\":{\"title\":\"A\"},\"optionB\":{\"title\":\"B\"}"; // 3 opens, 2 closes

        // While the block is still streaming (allowRepair:false) an incomplete payload must NOT be
        // repaired — the caller falls back to the placeholder instead of rebuilding a half-streamed
        // control (and restarting chart animations) on every chunk.
        Assert.ThrowsAny<JsonException>(() => StrataMarkdown.ParseVizJson(missingBrace, allowRepair: false));

        // Once the closing fence has arrived (allowRepair:true) the same payload is repaired.
        using var doc = StrataMarkdown.ParseVizJson(missingBrace, allowRepair: true);
        Assert.True(doc.RootElement.TryGetProperty("optionB", out _));
    }

    // ── Render-level regression tests ───────────────────────────────────────

    [Collection("Avalonia UI")]
    public class Rendering
    {
        private readonly AvaloniaFixture _fixture;

        public Rendering(AvaloniaFixture fixture) => _fixture = fixture;

        private async Task<int> CountControls<T>(string markdown) where T : Control
        {
            return await _fixture.Dispatch(() =>
            {
                var md = new StrataMarkdown { FontSize = 14 };
                var host = new Border();
                var window = new Window { Width = 500, Height = 400, Content = host };
                host.Child = md;
                window.Show();

                md.Markdown = markdown;
                Dispatcher.UIThread.RunJobs();

                var count = md.GetLogicalDescendants().OfType<T>().Count();
                window.Close();
                return count;
            });
        }

        private Task<int> CountForks(string markdown) => CountControls<StrataFork>(markdown);

        [Fact]
        public async Task MalformedComparison_MissingBrace_StillRendersFork()
        {
            var forks = await CountForks(MalformedComparison);
            Assert.True(forks > 0, "A completed comparison block with a single dropped closing brace should still render a StrataFork, not collapse to a placeholder.");
        }

        [Fact]
        public async Task ValidComparison_RendersFork()
        {
            Assert.True(await CountForks(ValidComparison) > 0);
        }

        [Fact]
        public async Task GenuinelyBrokenComparison_RendersNoFork()
        {
            Assert.Equal(0, await CountForks(BrokenComparison));
        }

        [Fact]
        public async Task MalformedComparison_WhileStreaming_ShowsPlaceholderNotFork()
        {
            // No closing ``` fence yet => the block is still streaming, so its JSON is legitimately
            // incomplete. Repair is gated off while streaming, so the placeholder shows instead of a
            // repaired-but-half-built fork flickering on every chunk; the fork appears once the fence closes.
            var streaming =
                "Here is my recommendation\n\n" +
                "```comparison\n" +
                "{\"optionA\":{\"title\":\"A\",\"content\":\"alpha\"},\"optionB\":{\"title\":\"B\",\"content\":\"beta\"}\n"; // unclosed fence
            Assert.Equal(0, await CountForks(streaming));
        }

        [Fact]
        public async Task MalformedConfidence_MissingBrace_StillRenders()
        {
            // Coverage for a non-comparison viz block that shares the ParseVizJson repair path.
            var md =
                "```confidence\n" +
                "{\"label\":\"Answer confidence\",\"value\":72\n" + // missing final brace
                "```\n";
            Assert.True(await CountControls<StrataConfidence>(md) > 0);
        }
    }
}
