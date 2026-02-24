using StrataTheme.Controls;
using static StrataTheme.Controls.StrataMarkdown;

namespace StrataTheme.Tests;

/// <summary>
/// Tests for the block-level diffing logic that <see cref="StrataMarkdown"/>
/// uses during streaming to avoid recreating unchanged UI controls.
/// </summary>
public class BlockDiffTests
{
    // ─── MdBlock equality ───────────────────────────────────────

    [Fact]
    public void MdBlock_EqualBlocks_AreEqual()
    {
        var a = new MdBlock { Kind = MdBlockKind.Paragraph, Content = "Hello", Level = 0, Language = "" };
        var b = new MdBlock { Kind = MdBlockKind.Paragraph, Content = "Hello", Level = 0, Language = "" };

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MdBlock_DifferentContent_NotEqual()
    {
        var a = new MdBlock { Kind = MdBlockKind.Paragraph, Content = "Hello", Level = 0, Language = "" };
        var b = new MdBlock { Kind = MdBlockKind.Paragraph, Content = "World", Level = 0, Language = "" };

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MdBlock_DifferentKind_NotEqual()
    {
        var a = new MdBlock { Kind = MdBlockKind.Paragraph, Content = "Text", Level = 0, Language = "" };
        var b = new MdBlock { Kind = MdBlockKind.Heading, Content = "Text", Level = 1, Language = "" };

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MdBlock_DifferentLevel_NotEqual()
    {
        var a = new MdBlock { Kind = MdBlockKind.Heading, Content = "Title", Level = 1, Language = "" };
        var b = new MdBlock { Kind = MdBlockKind.Heading, Content = "Title", Level = 2, Language = "" };

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MdBlock_DifferentLanguage_NotEqual()
    {
        var a = new MdBlock { Kind = MdBlockKind.CodeBlock, Content = "x", Level = 0, Language = "python" };
        var b = new MdBlock { Kind = MdBlockKind.CodeBlock, Content = "x", Level = 0, Language = "csharp" };

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void MdBlock_NullContent_HandledSafely()
    {
        // MdBlock.Content is not nullable by annotation but default struct has null
        var a = default(MdBlock);
        var b = default(MdBlock);

        // Should not throw
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void MdBlock_EqualsObject_WorksCorrectly()
    {
        var a = new MdBlock { Kind = MdBlockKind.Bullet, Content = "Item", Level = 0, Language = "" };
        object b = new MdBlock { Kind = MdBlockKind.Bullet, Content = "Item", Level = 0, Language = "" };

        Assert.True(a.Equals(b));
        Assert.False(a.Equals("not a block"));
        Assert.False(a.Equals(null));
    }

    // ─── Streaming diff simulation ──────────────────────────────

    [Fact]
    public void StreamingAppend_PrefixBlocksRemainIdentical()
    {
        var step1 = "## Title\n\nFirst paragraph.";
        var step2 = step1 + " More words appended.";

        var blocks1 = StrataMarkdown.ParseBlocks(step1);
        var blocks2 = StrataMarkdown.ParseBlocks(step2);

        // The heading block should be identical
        Assert.True(blocks1[0].Equals(blocks2[0]));
        // The paragraph block changes (more text appended)
        Assert.False(blocks1[1].Equals(blocks2[1]));
    }

    [Fact]
    public void StreamingAppend_NewBlocksAppeared()
    {
        var step1 = "## Title\n\nParagraph.";
        var step2 = step1 + "\n\n- New bullet";

        var blocks1 = StrataMarkdown.ParseBlocks(step1);
        var blocks2 = StrataMarkdown.ParseBlocks(step2);

        Assert.Equal(2, blocks1.Count);
        Assert.Equal(3, blocks2.Count);
        // First two blocks should match
        Assert.True(blocks1[0].Equals(blocks2[0]));
        Assert.True(blocks1[1].Equals(blocks2[1]));
        // Third is new
        Assert.Equal(MdBlockKind.Bullet, blocks2[2].Kind);
    }

    [Fact]
    public void StreamingAppend_NoDuplicateBlocks()
    {
        // This is the regression test for the duplicate lines bug.
        // Simulate streaming: each step appends more words.
        var steps = new[]
        {
            "## Response",
            "## Response\n\nHere",
            "## Response\n\nHere is",
            "## Response\n\nHere is the",
            "## Response\n\nHere is the analysis.",
            "## Response\n\nHere is the analysis.\n\n- First",
            "## Response\n\nHere is the analysis.\n\n- First point",
            "## Response\n\nHere is the analysis.\n\n- First point\n- Second point",
        };

        List<MdBlock>? previousBlocks = null;
        string? previousNormalized = null;
        int previousLength = 0;

        foreach (var step in steps)
        {
            var normalized = step.Replace("\r\n", "\n");
            var newBlocks = StrataMarkdown.ParseBlocks(normalized);

            // Verify no duplicate block content in a single parse result
            for (int i = 0; i < newBlocks.Count; i++)
            {
                for (int j = i + 1; j < newBlocks.Count; j++)
                {
                    // Same-kind blocks with identical non-empty content = duplicate
                    if (newBlocks[i].Kind == newBlocks[j].Kind &&
                        !string.IsNullOrEmpty(newBlocks[i].Content) &&
                        newBlocks[i].Content == newBlocks[j].Content)
                    {
                        Assert.Fail($"Duplicate block at step '{step}': block[{i}] and block[{j}] " +
                                    $"both have Kind={newBlocks[i].Kind}, Content='{newBlocks[i].Content}'");
                    }
                }
            }

            // Verify streaming append detection is correct
            if (previousNormalized is not null)
            {
                var isAppend = normalized.Length > previousLength &&
                               normalized.AsSpan().StartsWith(previousNormalized.AsSpan());
                Assert.True(isAppend, $"Step '{step}' should be detected as streaming append");
            }

            // Simulate the diff: unchanged blocks at the start should match
            if (previousBlocks is not null)
            {
                var minCount = Math.Min(previousBlocks.Count, newBlocks.Count);
                var diffStart = previousBlocks.Count > 0 ? previousBlocks.Count - 1 : 0;

                // Blocks before diffStart should be unchanged
                for (int i = 0; i < diffStart; i++)
                {
                    Assert.True(previousBlocks[i].Equals(newBlocks[i]),
                        $"Block {i} should be unchanged between steps");
                }
            }

            previousBlocks = newBlocks;
            previousNormalized = normalized;
            previousLength = normalized.Length;
        }
    }

    [Fact]
    public void StreamingAppend_CodeBlockAppearing_NoDuplicates()
    {
        // Code blocks being streamed in should not duplicate surrounding blocks
        var steps = new[]
        {
            "## Title\n\nSome text.",
            "## Title\n\nSome text.\n\n```python",
            "## Title\n\nSome text.\n\n```python\nprint('hello')",
            "## Title\n\nSome text.\n\n```python\nprint('hello')\n```",
            "## Title\n\nSome text.\n\n```python\nprint('hello')\n```\n\nDone.",
        };

        foreach (var step in steps)
        {
            var blocks = StrataMarkdown.ParseBlocks(step);

            // Count headings — should always be exactly 1
            var headingCount = blocks.Count(b => b.Kind == MdBlockKind.Heading);
            Assert.Equal(1, headingCount);

            // "Some text." paragraph should appear at most once
            var someTextCount = blocks.Count(b =>
                b.Kind == MdBlockKind.Paragraph && b.Content.Contains("Some text."));
            Assert.True(someTextCount <= 1, $"Duplicate 'Some text.' paragraphs at step: {step}");
        }
    }

    [Fact]
    public void StreamingAppend_TableAppearing_NoDuplicates()
    {
        var steps = new[]
        {
            "## Results\n\nData below:",
            "## Results\n\nData below:\n\n| Name | Value |",
            "## Results\n\nData below:\n\n| Name | Value |\n| --- | --- |",
            "## Results\n\nData below:\n\n| Name | Value |\n| --- | --- |\n| A | 1 |",
            "## Results\n\nData below:\n\n| Name | Value |\n| --- | --- |\n| A | 1 |\n| B | 2 |",
        };

        foreach (var step in steps)
        {
            var blocks = StrataMarkdown.ParseBlocks(step);
            var headingCount = blocks.Count(b => b.Kind == MdBlockKind.Heading);
            Assert.Equal(1, headingCount);
        }
    }

    // ─── Non-streaming (replacement) scenarios ──────────────────

    [Fact]
    public void FullReplace_DifferentContent_AllBlocksChange()
    {
        var old = StrataMarkdown.ParseBlocks("## Old\n\nOld paragraph.");
        var newer = StrataMarkdown.ParseBlocks("## New\n\nNew paragraph.");

        Assert.Equal(old.Count, newer.Count);
        Assert.False(old[0].Equals(newer[0]));
        Assert.False(old[1].Equals(newer[1]));
    }

    [Fact]
    public void IdenticalContent_AllBlocksMatch()
    {
        var md = "## Title\n\n- Item 1\n- Item 2\n\n```py\nx=1\n```";
        var blocks1 = StrataMarkdown.ParseBlocks(md);
        var blocks2 = StrataMarkdown.ParseBlocks(md);

        Assert.Equal(blocks1.Count, blocks2.Count);
        for (int i = 0; i < blocks1.Count; i++)
        {
            Assert.True(blocks1[i].Equals(blocks2[i]),
                $"Block {i} should be identical on re-parse");
        }
    }

    // ─── Determinism ────────────────────────────────────────────

    [Fact]
    public void Parser_IsDeterministic()
    {
        var md = "## H\n\nPara **bold** and `code`.\n\n- A\n- B\n\n1. X\n2. Y\n\n---\n\n```rust\nlet x = 1;\n```";

        var run1 = StrataMarkdown.ParseBlocks(md);
        var run2 = StrataMarkdown.ParseBlocks(md);
        var run3 = StrataMarkdown.ParseBlocks(md);

        Assert.Equal(run1.Count, run2.Count);
        Assert.Equal(run2.Count, run3.Count);

        for (int i = 0; i < run1.Count; i++)
        {
            Assert.True(run1[i].Equals(run2[i]));
            Assert.True(run2[i].Equals(run3[i]));
        }
    }
}
