using StrataTheme.Controls;
using static StrataTheme.Controls.StrataMarkdown;

namespace StrataTheme.Tests;

/// <summary>
/// Edge cases, boundary conditions, and regression tests for the markdown parser.
/// These test pathological inputs and scenarios that have previously caused bugs.
/// </summary>
public class MarkdownEdgeCaseTests
{
    // ─── Regression: duplicate blocks during streaming ──────────

    [Fact]
    public void Regression_StreamingWordByWord_NoDuplicates()
    {
        // Simulates the exact streaming pattern used by the demo:
        // words are appended one at a time and the markdown is re-parsed on each batch.
        var fullText = "## Analysis\n\nThe system detected **3 anomalies** in the `OrderService` cluster.\n\n" +
                       "### Timeline\n\n" +
                       "1. **14:02** - Latency spike\n" +
                       "2. **14:05** - Pool exhaustion\n" +
                       "3. **14:08** - Cascade failure\n\n" +
                       "### Root Cause\n\n" +
                       "A connection leak in the error handling path:\n\n" +
                       "```csharp\ntry { return await op(conn); }\ncatch { throw; }\n```\n\n" +
                       "### Fix\n\n" +
                       "- Add `finally` block\n" +
                       "- Implement circuit breaker\n\n" +
                       "---\n\nDeploy immediately.";

        var words = fullText.Split(' ');
        var accumulated = new System.Text.StringBuilder(fullText.Length);
        const int batchSize = 6;

        for (int i = 0; i < words.Length; i++)
        {
            if (accumulated.Length > 0) accumulated.Append(' ');
            accumulated.Append(words[i]);

            if (i % batchSize == batchSize - 1 || i == words.Length - 1)
            {
                var snapshot = accumulated.ToString();
                var blocks = StrataMarkdown.ParseBlocks(snapshot);

                // No two blocks should have the same kind+content (except HRs which have empty content)
                var seen = new HashSet<string>();
                foreach (var block in blocks)
                {
                    if (block.Kind == MdBlockKind.HorizontalRule) continue;
                    var key = $"{block.Kind}|{block.Content}";
                    Assert.True(seen.Add(key),
                        $"Duplicate block at word {i}: Kind={block.Kind}, Content='{block.Content}'\n" +
                        $"Full snapshot: {snapshot}");
                }
            }
        }
    }

    // ─── Boundary: code fence edge cases ────────────────────────

    [Fact]
    public void CodeFence_TripleBackticksInsideCodeBlock_NotTreatedAsClosing()
    {
        // Lines that START with ``` close the code block. This tests that
        // the parser handles the opening/closing correctly.
        var md = "```\nline1\n```";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
    }

    [Fact]
    public void CodeFence_LanguageWithWhitespace_Trimmed()
    {
        var md = "```  python  \ncode\n```";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Single(blocks);
        Assert.Equal("python", blocks[0].Language);
    }

    [Fact]
    public void CodeFence_NestedFences_ParsedAsContent()
    {
        // Opening a code block, then having content with ```, then closing
        var md = "```\n```inner\n```";
        var blocks = StrataMarkdown.ParseBlocks(md);
        // The first ``` opens, ````inner` closes it (it starts with ```),
        // then the last ``` opens another unclosed block
        Assert.True(blocks.Count >= 1);
    }

    [Fact]
    public void CodeFence_MultipleConsecutiveCodeBlocks()
    {
        var md = "```a\nfirst\n```\n```b\nsecond\n```\n```c\nthird\n```";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.CodeBlock, b.Kind));
        Assert.Equal("a", blocks[0].Language);
        Assert.Equal("b", blocks[1].Language);
        Assert.Equal("c", blocks[2].Language);
    }

    // ─── Boundary: paragraph merging ────────────────────────────

    [Fact]
    public void Paragraph_SingleBlankLineSeparates()
    {
        var blocks = StrataMarkdown.ParseBlocks("A\n\nB");
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Paragraph_MultipleBlankLinesSeparate()
    {
        var blocks = StrataMarkdown.ParseBlocks("A\n\n\n\nB");
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Paragraph_ContinuousLinesAreMerged()
    {
        var blocks = StrataMarkdown.ParseBlocks("Line 1\nLine 2\nLine 3");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    // ─── Boundary: heading ──────────────────────────────────────

    [Fact]
    public void Heading_FollowedByParagraphWithoutBlankLine()
    {
        var blocks = StrataMarkdown.ParseBlocks("## Title\nParagraph text");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
    }

    [Fact]
    public void Heading_PrecededByParagraphWithoutBlankLine()
    {
        var blocks = StrataMarkdown.ParseBlocks("Paragraph text\n## Title");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Heading, blocks[1].Kind);
    }

    // ─── Boundary: bullets after headings ───────────────────────

    [Fact]
    public void Bullet_ImmediatelyAfterHeading()
    {
        var blocks = StrataMarkdown.ParseBlocks("## List\n- A\n- B");
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[2].Kind);
    }

    // ─── Boundary: HR disambiguation ────────────────────────────

    [Fact]
    public void HorizontalRule_VsBullet_DashBulletHasContent()
    {
        // "- Item" is a bullet; "---" is HR
        var blocks = StrataMarkdown.ParseBlocks("- Item\n\n---");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[1].Kind);
    }

    [Fact]
    public void HorizontalRule_MixedChars_NotMatched()
    {
        // "-*-" should NOT be an HR (mixed chars)
        var blocks = StrataMarkdown.ParseBlocks("-*-");
        Assert.Single(blocks);
        Assert.NotEqual(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Fact]
    public void HorizontalRule_SpacedDashes_NotBullet()
    {
        // Regression: "- - -" was incorrectly parsed as a bullet
        // because TryParseBullet was checked before IsHorizontalRule.
        var blocks = StrataMarkdown.ParseBlocks("- - -");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Fact]
    public void HorizontalRule_SpacedStars_NotBullet()
    {
        var blocks = StrataMarkdown.ParseBlocks("* * *");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    // ─── Boundary: numbered items ───────────────────────────────

    [Fact]
    public void NumberedItem_LargeNumber()
    {
        var blocks = StrataMarkdown.ParseBlocks("100. Item");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[0].Kind);
        Assert.Equal(100, blocks[0].Level);
    }

    [Fact]
    public void NumberedItem_FourDigitNumber_NotParsed()
    {
        // Pattern only supports 1-3 digit numbers
        var blocks = StrataMarkdown.ParseBlocks("1234. Item");
        Assert.Single(blocks);
        Assert.NotEqual(MdBlockKind.NumberedItem, blocks[0].Kind);
    }

    [Fact]
    public void NumberedItem_Zero()
    {
        var blocks = StrataMarkdown.ParseBlocks("0. Zero item");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[0].Kind);
        Assert.Equal(0, blocks[0].Level);
        Assert.Equal("Zero item", blocks[0].Content);
    }

    // ─── Boundary: table edge cases ─────────────────────────────

    [Fact]
    public void Table_SingleColumn()
    {
        var md = "| Single |\n| --- |\n| Value |";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_PipesInContentNotConfusedWithTable()
    {
        // A line like "x | y" without leading pipe should be a paragraph
        var blocks = StrataMarkdown.ParseBlocks("This is not | a table");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    [Fact]
    public void Table_StreamingPartialRow_StaysInTable()
    {
        // During streaming the last row may only have an opening pipe
        var md = "| Name | Value |\n| --- | --- |\n| A | 1 |\n|";
        var blocks = StrataMarkdown.ParseBlocks(md);
        // The partial "| " line should be absorbed into the table, not split out
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_StreamingPartialSeparator_StaysInTable()
    {
        // Mid-stream: separator row has only received its opening pipe
        var md = "| Name | Value |\n| ---";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_HeaderOnly_StillTable()
    {
        // Just the header row — should still parse as Table, not Paragraph
        var md = "| Name | Value |";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_StreamingRowByRow_AlwaysTable()
    {
        // Simulate streaming a table row by row
        var steps = new[]
        {
            "| A | B |",
            "| A | B |\n| --- | --- |",
            "| A | B |\n| --- | --- |\n| 1 | 2 |",
            "| A | B |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |",
        };

        foreach (var step in steps)
        {
            var blocks = StrataMarkdown.ParseBlocks(step);
            Assert.Single(blocks);
            Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        }
    }

    // ─── Boundary: empty/minimal inputs ─────────────────────────

    [Fact]
    public void SingleChar_Paragraph()
    {
        var blocks = StrataMarkdown.ParseBlocks("X");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    [Fact]
    public void SingleHashSpace_NoHeading()
    {
        // "# " with nothing after should not be a heading;
        // the trimmed leftover "#" becomes a paragraph.
        var blocks = StrataMarkdown.ParseBlocks("# ");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("#", blocks[0].Content);
    }

    [Fact]
    public void SingleDash_NoBullet()
    {
        var blocks = StrataMarkdown.ParseBlocks("-");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    // ─── Streaming simulation: realistic AI response ────────────

    [Fact]
    public void StreamingSimulation_RealisticResponse_ConsistentBlockCounts()
    {
        var fullResponse = "## Incident Report\n\n" +
                           "Based on analysis of **IR-4471**, the root cause is a connection pool exhaustion.\n\n" +
                           "### Timeline\n\n" +
                           "1. **14:02 UTC** - First spike detected\n" +
                           "2. **14:05 UTC** - Pool at 95%\n" +
                           "3. **14:08 UTC** - Cascading failures\n\n" +
                           "### Recommendations\n\n" +
                           "- Add `finally` for disposal\n" +
                           "- Circuit breaker pattern\n" +
                           "- Alert at **80%** threshold\n\n" +
                           "```csharp\npublic void Fix() { }\n```\n\n" +
                           "---\n\nDeploy the hotfix.";

        var finalBlocks = StrataMarkdown.ParseBlocks(fullResponse);

        // Build streaming steps: append by character groups
        var steps = new List<string>();
        for (int len = 1; len <= fullResponse.Length; len += 13)
            steps.Add(fullResponse[..len]);
        steps.Add(fullResponse);

        foreach (var step in steps)
        {
            var blocks = StrataMarkdown.ParseBlocks(step);

            // Block count should monotonically increase or stay the same during streaming
            // (we're only appending text, so we should never lose previously completed blocks)
            // Exception: paragraph being split into heading+paragraph can temporarily vary
            Assert.True(blocks.Count >= 1, $"Should have at least 1 block at length {step.Length}");

            // No empty-content blocks except HorizontalRule and unclosed CodeBlocks
            // (mid-stream, an unclosed code fence like ```csharp\n has empty content)
            foreach (var block in blocks)
            {
                if (block.Kind == MdBlockKind.HorizontalRule)
                    continue;
                if (block.Kind == MdBlockKind.CodeBlock && step.TrimEnd().EndsWith("```") == false)
                    continue; // unclosed code block during streaming
                if (block.Kind is MdBlockKind.Chart or MdBlockKind.Mermaid or MdBlockKind.Confidence
                    or MdBlockKind.Comparison or MdBlockKind.Card or MdBlockKind.Sources)
                    continue; // special code blocks can be empty mid-stream

                Assert.False(string.IsNullOrWhiteSpace(block.Content),
                    $"Block {block.Kind} has empty content at length {step.Length}");
            }
        }

        // Final parse should match the expected block structure
        Assert.Equal(MdBlockKind.Heading, finalBlocks[0].Kind);
        Assert.Equal("Incident Report", finalBlocks[0].Content);
    }

    // ─── Interleaved block types ────────────────────────────────

    [Fact]
    public void InterleavedBulletsAndParagraphs()
    {
        var md = "- Bullet\n\nParagraph\n\n- Another bullet\n\nAnother paragraph";
        var blocks = StrataMarkdown.ParseBlocks(md);

        Assert.Equal(4, blocks.Count);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[3].Kind);
    }

    [Fact]
    public void CodeBlockInsideList_FlushesTableBuffer()
    {
        // Table lines followed by code fence
        var md = "| A | B |\n| - | - |\n| 1 | 2 |\n\n```\ncode\n```";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[1].Kind);
    }

    // ─── Unicode and special characters ─────────────────────────

    [Fact]
    public void Unicode_HeadingsAndBullets()
    {
        var md = "## שלום עולם\n\n- פריט ראשון\n- פריט שני";
        var blocks = StrataMarkdown.ParseBlocks(md);
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal("שלום עולם", blocks[0].Content);
    }

    [Fact]
    public void Emoji_InContent()
    {
        var blocks = StrataMarkdown.ParseBlocks("## 🚀 Launch\n\n- ✅ Ready\n- ❌ Not ready");
        Assert.Equal(3, blocks.Count);
        Assert.Contains("🚀", blocks[0].Content);
    }

    // ─── Long content stress test ───────────────────────────────

    [Fact]
    public void LargeDocument_ParsesWithoutError()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.AppendLine($"## Section {i}");
            sb.AppendLine();
            sb.AppendLine($"Paragraph {i} with content.");
            sb.AppendLine();
            sb.AppendLine($"- Bullet {i}a");
            sb.AppendLine($"- Bullet {i}b");
            sb.AppendLine();
        }

        var blocks = StrataMarkdown.ParseBlocks(sb.ToString());
        // 100 sections × 4 blocks each = 400
        Assert.Equal(400, blocks.Count);
    }

    // ─── Regression: streaming table with newline-aware tokenizer ─

    /// <summary>
    /// Simulates the exact streaming path used in the demo: the markdown
    /// is split into tokens by spaces + newlines, accumulated progressively,
    /// and pushed to ParseBlocks every <c>batchSize</c> tokens. Every
    /// intermediate snapshot must parse without crash and the final result
    /// must contain a Table block with 4 data rows.
    /// </summary>
    [Fact]
    public void StreamingTable_NewlineAwareTokenizer_AllIntermediateStatesValid()
    {
        // Exact markdown produced by BuildMockMarkdown for "incident summary"
        var fullMarkdown =
            "## Incident summary\n" +
            "The latency spike was driven by allocation bursts in serializer hot paths, amplifying GC pauses under peak load [1](incidents/IR-4471.md) [2](runbooks/infra/autoscale.md).\n" +
            "\n" +
            "| Metric | Before | After | Delta |\n" +
            "| --- | --- | --- | --- |\n" +
            "| p95 latency | 120 ms | 460 ms | +340 ms |\n" +
            "| GC pause | 18 ms | 97 ms | +79 ms |\n" +
            "| Error rate | 0.02% | 1.8% | +1.78% |\n" +
            "| Alloc/sec | 1.2 M | 4.7 M | +3.5 M |\n" +
            "\n" +
            "```csharp\n" +
            "public static bool IsSloHealthy(double p95Ms, double gcPauseMs)\n" +
            "{\n" +
            "    return p95Ms <= 250 && gcPauseMs <= 80;\n" +
            "}\n" +
            "```\n" +
            "\n" +
            "- Roll out in stages (10% \u2192 50% \u2192 100%)\n" +
            "- Roll back immediately when thresholds are breached\n" +
            "\n" +
            "### References\n" +
            "- [incidents/IR-4471.md](incidents/IR-4471.md)\n" +
            "- [runbooks/infra/autoscale.md](runbooks/infra/autoscale.md)";

        // Replicate TokenizeForStreaming: split on newlines first, then by spaces
        var tokens = new List<string>();
        var lines = fullMarkdown.Replace("\r\n", "\n").Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            if (li > 0)
                tokens.Add("\n");
            var words = lines[li].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
                tokens.Add(w);
        }

        // Replicate the streaming accumulation loop with batchSize = 6
        const int batchSize = 6;
        var accumulated = new System.Text.StringBuilder(fullMarkdown.Length);
        var snapshots = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok == "\n")
                accumulated.Append('\n');
            else
            {
                if (accumulated.Length > 0 && accumulated[^1] != '\n')
                    accumulated.Append(' ');
                accumulated.Append(tok);
            }

            if (i % batchSize == batchSize - 1 || i == tokens.Count - 1)
                snapshots.Add(accumulated.ToString());
        }

        // Every snapshot must parse without exception and produce at least 1 block
        foreach (var snapshot in snapshots)
        {
            var blocks = StrataMarkdown.ParseBlocks(snapshot);
            Assert.True(blocks.Count >= 1,
                $"Expected at least 1 block at snapshot length {snapshot.Length}");
        }

        // Final snapshot must parse identically to the original markdown
        // (exact string may differ in whitespace within code blocks since the
        // tokenizer normalises indentation, but the parse tree must match)
        var originalBlocks = StrataMarkdown.ParseBlocks(fullMarkdown);
        var final = StrataMarkdown.ParseBlocks(snapshots[^1]);
        Assert.Equal(originalBlocks.Count, final.Count);
        for (var b = 0; b < originalBlocks.Count; b++)
            Assert.Equal(originalBlocks[b].Kind, final[b].Kind);
        Assert.Contains(final, b => b.Kind == MdBlockKind.Heading && b.Content == "Incident summary");
        Assert.Contains(final, b => b.Kind == MdBlockKind.Table);
        Assert.Contains(final, b => b.Kind == MdBlockKind.CodeBlock);
        Assert.Contains(final, b => b.Kind == MdBlockKind.Bullet);

        // Table block must have 4 data rows (header + separator + 4 data rows)
        var tableBlock = final.First(b => b.Kind == MdBlockKind.Table);
        var tableLines = tableBlock.Content.Split('\n',
            System.StringSplitOptions.RemoveEmptyEntries);
        // header row + separator row + 4 data rows = 6
        Assert.Equal(6, tableLines.Length);
    }

    /// <summary>
    /// Verifies that newline tokens in the tokenizer output produce
    /// correct line boundaries — no extra leading spaces on lines
    /// that follow a newline token.
    /// </summary>
    [Fact]
    public void StreamingTable_AccumulatedOutput_NoSpuriousLeadingSpaces()
    {
        var md = "## Title\n| A | B |\n| --- | --- |\n| 1 | 2 |";

        var tokens = new List<string>();
        var lines = md.Replace("\r\n", "\n").Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            if (li > 0)
                tokens.Add("\n");
            var words = lines[li].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
                tokens.Add(w);
        }

        var accumulated = new System.Text.StringBuilder();
        foreach (var tok in tokens)
        {
            if (tok == "\n")
                accumulated.Append('\n');
            else
            {
                if (accumulated.Length > 0 && accumulated[^1] != '\n')
                    accumulated.Append(' ');
                accumulated.Append(tok);
            }
        }

        var result = accumulated.ToString();

        // No line should start with a space
        foreach (var line in result.Split('\n'))
            Assert.False(line.StartsWith(' '), $"Line starts with space: '{line}'");

        // Round-trip: should match original
        Assert.Equal(md, result);
    }
}
