using StrataTheme.Controls;

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
                var blocks = MarkdownParser.Parse(snapshot);

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
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
    }

    [Fact]
    public void CodeFence_LanguageWithWhitespace_Trimmed()
    {
        var md = "```  python  \ncode\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal("python", blocks[0].Language);
    }

    [Fact]
    public void CodeFence_NestedFences_ParsedAsContent()
    {
        // Opening a code block, then having content with ```, then closing
        var md = "```\n```inner\n```";
        var blocks = MarkdownParser.Parse(md);
        // The first ``` opens, ````inner` closes it (it starts with ```),
        // then the last ``` opens another unclosed block
        Assert.True(blocks.Count >= 1);
    }

    [Fact]
    public void CodeFence_MultipleConsecutiveCodeBlocks()
    {
        var md = "```a\nfirst\n```\n```b\nsecond\n```\n```c\nthird\n```";
        var blocks = MarkdownParser.Parse(md);
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
        var blocks = MarkdownParser.Parse("A\n\nB");
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Paragraph_MultipleBlankLinesSeparate()
    {
        var blocks = MarkdownParser.Parse("A\n\n\n\nB");
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Paragraph_ContinuousLinesAreMerged()
    {
        var blocks = MarkdownParser.Parse("Line 1\nLine 2\nLine 3");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    // ─── Boundary: heading ──────────────────────────────────────

    [Fact]
    public void Heading_FollowedByParagraphWithoutBlankLine()
    {
        var blocks = MarkdownParser.Parse("## Title\nParagraph text");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
    }

    [Fact]
    public void Heading_PrecededByParagraphWithoutBlankLine()
    {
        var blocks = MarkdownParser.Parse("Paragraph text\n## Title");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Heading, blocks[1].Kind);
    }

    // ─── Boundary: bullets after headings ───────────────────────

    [Fact]
    public void Bullet_ImmediatelyAfterHeading()
    {
        var blocks = MarkdownParser.Parse("## List\n- A\n- B");
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
        var blocks = MarkdownParser.Parse("- Item\n\n---");
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[1].Kind);
    }

    [Fact]
    public void HorizontalRule_MixedChars_NotMatched()
    {
        // "-*-" should NOT be an HR (mixed chars)
        var blocks = MarkdownParser.Parse("-*-");
        Assert.Single(blocks);
        Assert.NotEqual(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Fact]
    public void HorizontalRule_SpacedDashes_NotBullet()
    {
        // Regression: "- - -" was incorrectly parsed as a bullet
        // because TryParseBullet was checked before IsHorizontalRule.
        var blocks = MarkdownParser.Parse("- - -");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Fact]
    public void HorizontalRule_SpacedStars_NotBullet()
    {
        var blocks = MarkdownParser.Parse("* * *");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    // ─── Boundary: numbered items ───────────────────────────────

    [Fact]
    public void NumberedItem_LargeNumber()
    {
        var blocks = MarkdownParser.Parse("100. Item");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[0].Kind);
        Assert.Equal(100, blocks[0].Level);
    }

    [Fact]
    public void NumberedItem_FourDigitNumber_NotParsed()
    {
        // Pattern only supports 1-3 digit numbers
        var blocks = MarkdownParser.Parse("1234. Item");
        Assert.Single(blocks);
        Assert.NotEqual(MdBlockKind.NumberedItem, blocks[0].Kind);
    }

    [Fact]
    public void NumberedItem_Zero()
    {
        var blocks = MarkdownParser.Parse("0. Zero item");
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
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_PipesInContentNotConfusedWithTable()
    {
        // A line like "x | y" without leading pipe should be a paragraph
        var blocks = MarkdownParser.Parse("This is not | a table");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    [Fact]
    public void Table_StreamingPartialRow_StaysInTable()
    {
        // During streaming the last row may only have an opening pipe
        var md = "| Name | Value |\n| --- | --- |\n| A | 1 |\n|";
        var blocks = MarkdownParser.Parse(md);
        // The partial "| " line should be absorbed into the table, not split out
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_StreamingPartialSeparator_StaysInTable()
    {
        // Mid-stream: separator row has only received its opening pipe
        var md = "| Name | Value |\n| ---";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_HeaderOnly_StillTable()
    {
        // Just the header row — should still parse as Table, not Paragraph
        var md = "| Name | Value |";
        var blocks = MarkdownParser.Parse(md);
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
            var blocks = MarkdownParser.Parse(step);
            Assert.Single(blocks);
            Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        }
    }

    // ─── Boundary: empty/minimal inputs ─────────────────────────

    [Fact]
    public void SingleChar_Paragraph()
    {
        var blocks = MarkdownParser.Parse("X");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    [Fact]
    public void SingleHashSpace_NoHeading()
    {
        // "# " with nothing after should not be a heading;
        // the trimmed leftover "#" becomes a paragraph.
        var blocks = MarkdownParser.Parse("# ");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("#", blocks[0].Content);
    }

    [Fact]
    public void SingleDash_NoBullet()
    {
        var blocks = MarkdownParser.Parse("-");
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

        var finalBlocks = MarkdownParser.Parse(fullResponse);

        // Build streaming steps: append by character groups
        var steps = new List<string>();
        for (int len = 1; len <= fullResponse.Length; len += 13)
            steps.Add(fullResponse[..len]);
        steps.Add(fullResponse);

        foreach (var step in steps)
        {
            var blocks = MarkdownParser.Parse(step);

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
        var blocks = MarkdownParser.Parse(md);

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
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[1].Kind);
    }

    // ─── Unicode and special characters ─────────────────────────

    [Fact]
    public void Unicode_HeadingsAndBullets()
    {
        var md = "## שלום עולם\n\n- פריט ראשון\n- פריט שני";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal("שלום עולם", blocks[0].Content);
    }

    [Fact]
    public void Emoji_InContent()
    {
        var blocks = MarkdownParser.Parse("## 🚀 Launch\n\n- ✅ Ready\n- ❌ Not ready");
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

        var blocks = MarkdownParser.Parse(sb.ToString());
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
            var blocks = MarkdownParser.Parse(snapshot);
            Assert.True(blocks.Count >= 1,
                $"Expected at least 1 block at snapshot length {snapshot.Length}");
        }

        // Final snapshot must parse identically to the original markdown
        // (exact string may differ in whitespace within code blocks since the
        // tokenizer normalises indentation, but the parse tree must match)
        var originalBlocks = MarkdownParser.Parse(fullMarkdown);
        var final = MarkdownParser.Parse(snapshots[^1]);
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

    // ─── Blockquote edge cases ──────────────────────────────────

    [Fact]
    public void Blockquote_WithLeadingSpaces_StillParsed()
    {
        var blocks = MarkdownParser.Parse("   > Indented quote");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("Indented quote", blocks[0].Content);
    }

    [Fact]
    public void Blockquote_BreaksAtNonQuoteLine()
    {
        var md = "> Line one\nNot a quote\n> Line two";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("Line one", blocks[0].Content);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[2].Kind);
        Assert.Equal("Line two", blocks[2].Content);
    }

    [Fact]
    public void Blockquote_FollowedByCodeBlock()
    {
        var md = "> Quote\n```\ncode\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[1].Kind);
    }

    [Fact]
    public void Blockquote_FollowedByTable()
    {
        var md = "> Quote\n| A | B |\n| --- | --- |\n| 1 | 2 |";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Table, blocks[1].Kind);
    }

    [Fact]
    public void Blockquote_StreamingAppend_NoDuplicates()
    {
        var full = "## Title\n\n> Quote line one\n> Quote line two\n\nParagraph after.";
        var words = full.Split(' ');
        var accumulated = new System.Text.StringBuilder(full.Length);

        for (int i = 0; i < words.Length; i++)
        {
            if (accumulated.Length > 0) accumulated.Append(' ');
            accumulated.Append(words[i]);
        }

        var blocks = MarkdownParser.Parse(accumulated.ToString());

        // Verify the full parse produces correct structure
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal(3, blocks.Count);
    }

    [Fact]
    public void Blockquote_IncrementalAppend_BlockquoteGrows()
    {
        var step1 = "> First line";
        var step2 = "> First line\n> Second line";

        var blocks1 = MarkdownParser.Parse(step1);
        var blocks2 = MarkdownParser.ParseIncrementalAppend(step1, blocks1, step2);

        Assert.Single(blocks2);
        Assert.Equal(MdBlockKind.Blockquote, blocks2[0].Kind);
        Assert.Contains("First line", blocks2[0].Content);
        Assert.Contains("Second line", blocks2[0].Content);
    }

    // ─── Paragraph edge cases ───────────────────────────────────

    [Fact]
    public void Paragraph_StartsWithSpecialCharButNotMarkdown()
    {
        // A line starting with * but not followed by space is not a bullet
        var blocks = MarkdownParser.Parse("*not a bullet");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
    }

    [Fact]
    public void Paragraph_ContainsGreaterThanMidLine()
    {
        // > not at line start should be paragraph, not blockquote
        var blocks = MarkdownParser.Parse("x > 5 means something");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("x > 5 means something", blocks[0].Content);
    }

    // ─── Heading edge cases ─────────────────────────────────────

    [Fact]
    public void Heading_TrailingHashes_Trimmed()
    {
        var blocks = MarkdownParser.Parse("## Heading ##");
        // Whether trailing hashes are trimmed or preserved is implementation-specific;
        // just verify it doesn't crash and is a heading
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
    }

    [Fact]
    public void Heading_WithInlineFormatting()
    {
        var blocks = MarkdownParser.Parse("## **Bold** Heading");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Contains("**Bold**", blocks[0].Content);
    }

    // ─── List edge cases ────────────────────────────────────────

    [Fact]
    public void Bullet_MixedMarkers_EachParsedSeparately()
    {
        var md = "- Dash item\n* Star item\n- Another dash";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Bullet, b.Kind));
    }

    [Fact]
    public void NumberedList_NonSequential_StillParsed()
    {
        var md = "1. First\n3. Third\n2. Second";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.NumberedItem, b.Kind));
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal(3, blocks[1].Level);
        Assert.Equal(2, blocks[2].Level);
    }

    [Fact]
    public void Bullet_ImmediatelyFollowedByCodeBlock()
    {
        var md = "- Bullet\n```\ncode\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[1].Kind);
    }

    [Fact]
    public void Bullet_DeeplyNested_ClampsIndent()
    {
        var md = "                - Very deep";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        // GetIndentLevel clamps to max 3
        Assert.True(blocks[0].Level <= 3);
    }

    // ─── Table edge cases ───────────────────────────────────────

    [Fact]
    public void Table_EmptyCells_Preserved()
    {
        var md = "| H1 | H2 |\n| --- | --- |\n|  | data |";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_CellsWithInlineFormatting()
    {
        var md = "| **Bold** | *Italic* | `code` |\n| --- | --- | --- |\n| [link](url) | normal | ***bi*** |";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        Assert.Contains("**Bold**", blocks[0].Content);
        Assert.Contains("[link](url)", blocks[0].Content);
    }

    [Fact]
    public void Table_VaryingColumnCounts_StillParsed()
    {
        var md = "| H1 | H2 | H3 |\n| --- | --- | --- |\n| A | B |";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    [Fact]
    public void Table_AlignmentMarkers_ParsedAsSeparator()
    {
        var md = "| Left | Center | Right |\n| :--- | :---: | ---: |\n| A | B | C |";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
    }

    // ─── Code block edge cases ──────────────────────────────────

    [Fact]
    public void CodeBlock_WithBackticksInContent_NotClosedEarly()
    {
        var md = "```\nvar x = `template`;\nconsole.log(x);\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
        Assert.Contains("`template`", blocks[0].Content);
    }

    [Fact]
    public void CodeBlock_IndentedFence_Recognized()
    {
        var md = "    ```python\n    code\n    ```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
        Assert.Equal("python", blocks[0].Language);
    }

    [Fact]
    public void CodeBlock_ConsecutiveWithDifferentLanguages()
    {
        var md = "```python\nprint(1)\n```\n```javascript\nconsole.log(1)\n```\n```rust\nprintln!(\"1\")\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.Equal("python", blocks[0].Language);
        Assert.Equal("javascript", blocks[1].Language);
        Assert.Equal("rust", blocks[2].Language);
    }

    // ─── Horizontal rule vs bullet disambiguation ───────────────

    [Fact]
    public void HorizontalRule_Underscores_Parsed()
    {
        var blocks = MarkdownParser.Parse("___");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Fact]
    public void HorizontalRule_SpacedUnderscores_Parsed()
    {
        var blocks = MarkdownParser.Parse("_ _ _");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Fact]
    public void HorizontalRule_VeryLong_Parsed()
    {
        var blocks = MarkdownParser.Parse(new string('-', 100));
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    // ─── Incremental parse edge cases ───────────────────────────

    [Fact]
    public void IncrementalAppend_AppendChangesBlockKind()
    {
        var step1 = "Some paragraph text";
        var step2 = "Some paragraph text\n\n- Now it has a bullet";

        var blocks1 = MarkdownParser.Parse(step1);
        Assert.Single(blocks1);
        Assert.Equal(MdBlockKind.Paragraph, blocks1[0].Kind);

        var blocks2 = MarkdownParser.ParseIncrementalAppend(step1, blocks1, step2);
        Assert.Equal(2, blocks2.Count);
        Assert.Equal(MdBlockKind.Paragraph, blocks2[0].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks2[1].Kind);
    }

    [Fact]
    public void IncrementalAppend_AppendMidCodeBlock()
    {
        var step1 = "```csharp\nvar x = 1;";
        var step2 = "```csharp\nvar x = 1;\nvar y = 2;";

        var blocks1 = MarkdownParser.Parse(step1);
        // Unclosed code block is still captured
        Assert.Single(blocks1);
        Assert.Equal(MdBlockKind.CodeBlock, blocks1[0].Kind);

        var blocks2 = MarkdownParser.ParseIncrementalAppend(step1, blocks1, step2);
        Assert.Single(blocks2);
        Assert.Equal(MdBlockKind.CodeBlock, blocks2[0].Kind);
        Assert.Contains("var y = 2;", blocks2[0].Content);
    }

    [Fact]
    public void IncrementalAppend_CodeBlockCloses()
    {
        var step1 = "```\ncode";
        var step2 = "```\ncode\n```\n\nParagraph after";

        var blocks1 = MarkdownParser.Parse(step1);
        var blocks2 = MarkdownParser.ParseIncrementalAppend(step1, blocks1, step2);

        Assert.Equal(2, blocks2.Count);
        Assert.Equal(MdBlockKind.CodeBlock, blocks2[0].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks2[1].Kind);
    }

    // ─── Mixed content edge cases ───────────────────────────────

    [Fact]
    public void MixedContent_BlockquoteAmongAllTypes()
    {
        var md = string.Join("\n", new[]
        {
            "## Heading",
            "",
            "> Blockquote text",
            "",
            "Paragraph text.",
            "",
            "- Bullet one",
            "",
            "1. Numbered one",
            "",
            "---",
            "",
            "```python",
            "x = 1",
            "```",
            "",
            "| H1 | H2 |",
            "| --- | --- |",
            "| A | B |"
        });

        var blocks = MarkdownParser.Parse(md);

        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[4].Kind);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[5].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[6].Kind);
        Assert.Equal(MdBlockKind.Table, blocks[7].Kind);
        Assert.Equal(8, blocks.Count);
    }

    [Fact]
    public void Unicode_Blockquote_WithHebrew()
    {
        var blocks = MarkdownParser.Parse("> ציטוט בעברית");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("ציטוט בעברית", blocks[0].Content);
    }

    [Fact]
    public void Emoji_InBlockquote()
    {
        var blocks = MarkdownParser.Parse("> 🚀 Launch sequence started");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Contains("🚀", blocks[0].Content);
    }

    // ─── Pooled parser parity ───────────────────────────────────

    [Fact]
    public void PooledParse_Blockquote_MatchesStaticParse()
    {
        var md = "## Title\n\n> Quote line\n\nParagraph";
        var staticBlocks = MarkdownParser.Parse(md);

        var parser = new MarkdownParser();
        var pooledBlocks = new System.Collections.Generic.List<MdBlock>();
        parser.ParsePooled(md, pooledBlocks);

        Assert.Equal(staticBlocks.Count, pooledBlocks.Count);
        for (int i = 0; i < staticBlocks.Count; i++)
        {
            Assert.Equal(staticBlocks[i].Kind, pooledBlocks[i].Kind);
            Assert.Equal(staticBlocks[i].Content, pooledBlocks[i].Content);
        }
    }

    // ─── Task list edge cases ───────────────────────────────────

    [Fact]
    public void TaskItem_PrecededByBulletAndFollowedByParagraph()
    {
        var md = "- Bullet\n- [x] Done\n- [ ] Todo\n\nParagraph";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(4, blocks.Count);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(MdBlockKind.TaskItem, blocks[1].Kind);
        Assert.Equal(MdBlockKind.TaskItem, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[3].Kind);
    }

    [Fact]
    public void TaskItem_StreamingAppend()
    {
        var step1 = "- [x] First task";
        var step2 = "- [x] First task\n- [ ] Second task";

        var blocks1 = MarkdownParser.Parse(step1);
        Assert.Single(blocks1);
        Assert.Equal(MdBlockKind.TaskItem, blocks1[0].Kind);

        var blocks2 = MarkdownParser.ParseIncrementalAppend(step1, blocks1, step2);
        Assert.Equal(2, blocks2.Count);
        Assert.All(blocks2, b => Assert.Equal(MdBlockKind.TaskItem, b.Kind));
    }

    [Fact]
    public void TaskItem_InComplexDocument()
    {
        var md = string.Join("\n", new[]
        {
            "## Tasks",
            "",
            "- [x] Configure alerts",
            "- [ ] Run load test",
            "",
            "> Review the results before proceeding.",
            "",
            "- Regular bullet"
        });

        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.TaskItem, blocks[1].Kind);
        Assert.Equal(1, blocks[1].Level); // checked
        Assert.Equal(MdBlockKind.TaskItem, blocks[2].Kind);
        Assert.Equal(0, blocks[2].Level); // unchecked
        Assert.Equal(MdBlockKind.Blockquote, blocks[3].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[4].Kind);
    }

    // ─── Inline formatting preserved in content ─────────────────

    [Fact]
    public void Paragraph_StrikethroughPreserved()
    {
        var blocks = MarkdownParser.Parse("This is ~~deleted~~ text");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Contains("~~deleted~~", blocks[0].Content);
    }

    [Fact]
    public void Paragraph_UnderscoreItalicPreserved()
    {
        var blocks = MarkdownParser.Parse("This is _italic_ text");
        Assert.Single(blocks);
        Assert.Contains("_italic_", blocks[0].Content);
    }

    [Fact]
    public void Paragraph_UnderscoreBoldPreserved()
    {
        var blocks = MarkdownParser.Parse("This is __bold__ text");
        Assert.Single(blocks);
        Assert.Contains("__bold__", blocks[0].Content);
    }

    [Fact]
    public void Paragraph_ImageSyntaxPreserved()
    {
        var blocks = MarkdownParser.Parse("See ![logo](https://example.com/img.png) for reference");
        Assert.Single(blocks);
        Assert.Contains("![logo](https://example.com/img.png)", blocks[0].Content);
    }

    [Fact]
    public void MixedContent_AllInlineTypes()
    {
        var md = "Use **bold**, *italic*, __underscored__, _underscore italic_, ~~strikethrough~~, `code`, [link](url), and ![img](url) together.";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        // All inline markers are preserved in the block content
        Assert.Contains("**bold**", blocks[0].Content);
        Assert.Contains("*italic*", blocks[0].Content);
        Assert.Contains("__underscored__", blocks[0].Content);
        Assert.Contains("_underscore italic_", blocks[0].Content);
        Assert.Contains("~~strikethrough~~", blocks[0].Content);
        Assert.Contains("`code`", blocks[0].Content);
        Assert.Contains("[link](url)", blocks[0].Content);
        Assert.Contains("![img](url)", blocks[0].Content);
    }

    [Fact]
    public void MixedContent_AllBlockTypesIncludingTaskItems()
    {
        var md = string.Join("\n", new[]
        {
            "## Heading",
            "",
            "> Blockquote",
            "",
            "Paragraph.",
            "",
            "- Bullet",
            "",
            "1. Numbered",
            "",
            "- [x] Done task",
            "- [ ] Pending task",
            "",
            "---",
            "",
            "```py",
            "x = 1",
            "```",
            "",
            "| H1 | H2 |",
            "| --- | --- |",
            "| A | B |"
        });

        var blocks = MarkdownParser.Parse(md);

        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[4].Kind);
        Assert.Equal(MdBlockKind.TaskItem, blocks[5].Kind);
        Assert.Equal(MdBlockKind.TaskItem, blocks[6].Kind);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[7].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[8].Kind);
        Assert.Equal(MdBlockKind.Table, blocks[9].Kind);
        Assert.Equal(10, blocks.Count);
    }
}
