using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>
/// Tests for <see cref="MarkdownParser.Parse"/> — the core parser
/// that converts markdown source into block descriptors.
/// </summary>
public class MarkdownParserTests
{
    // ─── Headings ───────────────────────────────────────────────

    [Theory]
    [InlineData("# H1", 1, "H1")]
    [InlineData("## H2", 2, "H2")]
    [InlineData("### H3", 3, "H3")]
    [InlineData("#### H4", 3, "H4")]         // clamped to 3
    [InlineData("##### H5", 3, "H5")]
    [InlineData("###### H6", 3, "H6")]
    public void Heading_ParsedWithCorrectLevel(string md, int expectedLevel, string expectedText)
    {
        var blocks = MarkdownParser.Parse(md);

        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(expectedLevel, blocks[0].Level);
        Assert.Equal(expectedText, blocks[0].Content);
    }

    [Theory]
    [InlineData("#No space")]
    [InlineData("##No space")]
    [InlineData("####### TooMany")]
    [InlineData("# ")]
    public void Heading_InvalidPatterns_NotParsedAsHeading(string md)
    {
        var blocks = MarkdownParser.Parse(md);

        foreach (var block in blocks)
            Assert.NotEqual(MdBlockKind.Heading, block.Kind);
    }

    [Fact]
    public void Heading_LeadingSpaces_StillParsed()
    {
        var blocks = MarkdownParser.Parse("   ## Indented Heading");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal("Indented Heading", blocks[0].Content);
    }

    // ─── Paragraphs ─────────────────────────────────────────────

    [Fact]
    public void SingleParagraph_Parsed()
    {
        var blocks = MarkdownParser.Parse("Hello world");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("Hello world", blocks[0].Content);
    }

    [Fact]
    public void MultiLineParagraph_MergedIntoOne()
    {
        var blocks = MarkdownParser.Parse("Line one\nLine two\nLine three");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Contains("Line one", blocks[0].Content);
        Assert.Contains("Line three", blocks[0].Content);
    }

    [Fact]
    public void EmptyLinesSeparateParagraphs()
    {
        var blocks = MarkdownParser.Parse("Para one\n\nPara two");
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Paragraph, b.Kind));
        Assert.Equal("Para one", blocks[0].Content);
        Assert.Equal("Para two", blocks[1].Content);
    }

    [Fact]
    public void WhitespaceOnlyInput_NoBlocks()
    {
        Assert.Empty(MarkdownParser.Parse("   \n  \n  "));
    }

    // ─── Bullet lists ───────────────────────────────────────────

    [Theory]
    [InlineData("- Item", "Item")]
    [InlineData("* Star item", "Star item")]
    [InlineData("  - Indented", "Indented")]
    public void Bullet_ParsedCorrectly(string md, string expectedText)
    {
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(expectedText, blocks[0].Content);
    }

    [Fact]
    public void Bullet_IndentLevel_Computed()
    {
        var blocks = MarkdownParser.Parse("    - Deep item");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(2, blocks[0].Level); // 4 spaces / 2 = indent level 2
    }

    [Fact]
    public void Bullet_TabIndent_ConvertedToSpaces()
    {
        var blocks = MarkdownParser.Parse("\t- Tab item");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal(2, blocks[0].Level); // tab = 4 spaces, / 2 = 2
    }

    [Theory]
    [InlineData("-No space")]
    [InlineData("*No space")]
    [InlineData("- ")]
    public void Bullet_InvalidPatterns_NotParsedAsBullet(string md)
    {
        var blocks = MarkdownParser.Parse(md);
        foreach (var block in blocks)
            Assert.NotEqual(MdBlockKind.Bullet, block.Kind);
    }

    // ─── Numbered lists ─────────────────────────────────────────

    [Theory]
    [InlineData("1. First", 1, "First")]
    [InlineData("2. Second", 2, "Second")]
    [InlineData("10. Tenth", 10, "Tenth")]
    [InlineData("999. Big", 999, "Big")]
    public void NumberedItem_ParsedCorrectly(string md, int expectedNum, string expectedText)
    {
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[0].Kind);
        Assert.Equal(expectedNum, blocks[0].Level);
        Assert.Equal(expectedText, blocks[0].Content);
    }

    [Theory]
    [InlineData("1.No space")]
    [InlineData("1234. TooMany")]
    [InlineData("a. NotANumber")]
    [InlineData("1. ")]
    public void NumberedItem_InvalidPatterns_NotParsedAsNumbered(string md)
    {
        var blocks = MarkdownParser.Parse(md);
        foreach (var block in blocks)
            Assert.NotEqual(MdBlockKind.NumberedItem, block.Kind);
    }

    // ─── Horizontal rules ───────────────────────────────────────

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    [InlineData("----")]
    [InlineData("  ---  ")]
    public void HorizontalRule_ValidPatterns(string md)
    {
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[0].Kind);
    }

    [Theory]
    [InlineData("--")]
    [InlineData("-*-")]
    [InlineData("abc")]
    public void HorizontalRule_InvalidPatterns(string md)
    {
        var blocks = MarkdownParser.Parse(md);
        foreach (var block in blocks)
            Assert.NotEqual(MdBlockKind.HorizontalRule, block.Kind);
    }

    // ─── Code blocks ────────────────────────────────────────────

    [Fact]
    public void CodeBlock_BasicFenced()
    {
        var md = "```\nvar x = 1;\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
        Assert.Contains("var x = 1;", blocks[0].Content);
        Assert.Equal(string.Empty, blocks[0].Language);
    }

    [Fact]
    public void CodeBlock_WithLanguage()
    {
        var md = "```csharp\nvar x = 1;\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
        Assert.Equal("csharp", blocks[0].Language);
        Assert.Contains("var x = 1;", blocks[0].Content);
    }

    [Fact]
    public void CodeBlock_UnclosedFence_StillCaptured()
    {
        var md = "```python\nprint('hello')";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
        Assert.Equal("python", blocks[0].Language);
        Assert.Contains("print('hello')", blocks[0].Content);
    }

    [Fact]
    public void CodeBlock_MultipleBlocks()
    {
        var md = "```js\nconst a = 1;\n```\n\nSome text\n\n```py\nx = 2\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
        Assert.Equal("js", blocks[0].Language);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[2].Kind);
        Assert.Equal("py", blocks[2].Language);
    }

    [Fact]
    public void CodeBlock_EmptyContent()
    {
        var md = "```\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
    }

    // ─── Special code block kinds ───────────────────────────────

    [Theory]
    [InlineData("chart", "Chart")]
    [InlineData("Chart", "Chart")]
    [InlineData("CHART", "Chart")]
    [InlineData("mermaid", "Mermaid")]
    [InlineData("Mermaid", "Mermaid")]
    [InlineData("confidence", "Confidence")]
    [InlineData("comparison", "Comparison")]
    [InlineData("card", "Card")]
    [InlineData("sources", "Sources")]
    public void CodeBlock_SpecialLanguages_MapToSpecialKinds(string lang, string expectedKindName)
    {
        var expectedKind = Enum.Parse<MdBlockKind>(expectedKindName);
        var md = $"```{lang}\ncontent\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(expectedKind, blocks[0].Kind);
    }

    [Theory]
    [InlineData("typescript")]
    [InlineData("python")]
    [InlineData("rust")]
    [InlineData("")]
    public void CodeBlock_RegularLanguages_MapToCodeBlock(string lang)
    {
        var md = $"```{lang}\ncontent\n```";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[0].Kind);
    }

    // ─── Tables ─────────────────────────────────────────────────

    [Fact]
    public void Table_BasicTable()
    {
        var md = "| Header1 | Header2 |\n| --- | --- |\n| A | B |";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        Assert.Contains("Header1", blocks[0].Content);
        Assert.Contains("A", blocks[0].Content);
    }

    [Fact]
    public void Table_TerminatedByNonTableLine()
    {
        var md = "| H1 | H2 |\n| -- | -- |\n| A | B |\nNot a table line";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Table, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
    }

    // ─── CRLF handling ──────────────────────────────────────────

    [Fact]
    public void CrLf_HandledIdenticallyToLf()
    {
        var lfBlocks = MarkdownParser.Parse("## Title\n\nParagraph\n\n- Bullet");
        var crlfBlocks = MarkdownParser.Parse("## Title\r\n\r\nParagraph\r\n\r\n- Bullet");

        Assert.Equal(lfBlocks.Count, crlfBlocks.Count);
        for (int i = 0; i < lfBlocks.Count; i++)
        {
            Assert.Equal(lfBlocks[i].Kind, crlfBlocks[i].Kind);
            Assert.Equal(lfBlocks[i].Content, crlfBlocks[i].Content);
            Assert.Equal(lfBlocks[i].Level, crlfBlocks[i].Level);
        }
    }

    // ─── Mixed content ──────────────────────────────────────────

    [Fact]
    public void MixedContent_ComplexDocument()
    {
        var md = string.Join("\n", new[]
        {
            "## Title",
            "",
            "Intro paragraph with **bold**.",
            "",
            "- Bullet one",
            "- Bullet two",
            "",
            "1. First step",
            "2. Second step",
            "",
            "---",
            "",
            "```python",
            "print(42)",
            "```",
            "",
            "Closing."
        });

        var blocks = MarkdownParser.Parse(md);

        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[4].Kind);
        Assert.Equal(MdBlockKind.NumberedItem, blocks[5].Kind);
        Assert.Equal(MdBlockKind.HorizontalRule, blocks[6].Kind);
        Assert.Equal(MdBlockKind.CodeBlock, blocks[7].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[8].Kind);
        Assert.Equal(9, blocks.Count);
    }

    [Fact]
    public void EmptyInput_NoBlocks()
    {
        Assert.Empty(MarkdownParser.Parse(""));
    }

    [Fact]
    public void OnlyNewlines_NoBlocks()
    {
        Assert.Empty(MarkdownParser.Parse("\n\n\n"));
    }

    // ─── Blockquotes ────────────────────────────────────────────

    [Fact]
    public void Blockquote_BasicSingleLine()
    {
        var blocks = MarkdownParser.Parse("> This is a quote");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("This is a quote", blocks[0].Content);
    }

    [Fact]
    public void Blockquote_MultipleConsecutiveLines()
    {
        var md = "> Line one\n> Line two\n> Line three";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Contains("Line one", blocks[0].Content);
        Assert.Contains("Line two", blocks[0].Content);
        Assert.Contains("Line three", blocks[0].Content);
    }

    [Fact]
    public void Blockquote_WithoutSpaceAfterMarker()
    {
        var blocks = MarkdownParser.Parse(">No space after marker");
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("No space after marker", blocks[0].Content);
    }

    [Fact]
    public void Blockquote_FollowedByParagraph()
    {
        var md = "> A quote\n\nA paragraph";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("A quote", blocks[0].Content);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
    }

    [Fact]
    public void Blockquote_PrecededByParagraph()
    {
        var md = "Intro text\n\n> Quoted text";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[1].Kind);
    }

    [Fact]
    public void Blockquote_WithInlineFormatting()
    {
        var md = "> This has **bold** and `code` and [link](url)";
        var blocks = MarkdownParser.Parse(md);
        Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Contains("**bold**", blocks[0].Content);
        Assert.Contains("`code`", blocks[0].Content);
        Assert.Contains("[link](url)", blocks[0].Content);
    }

    [Fact]
    public void Blockquote_EmptyQuote()
    {
        // A bare `>` with no text content produces no block (trimmed to empty)
        var blocks = MarkdownParser.Parse(">");
        Assert.Empty(blocks);
    }

    [Fact]
    public void Blockquote_Interleaved_CreatesMultipleBlocks()
    {
        var md = "> Quote one\n\nMiddle paragraph\n\n> Quote two";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal("Quote one", blocks[0].Content);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[2].Kind);
        Assert.Equal("Quote two", blocks[2].Content);
    }

    [Fact]
    public void Blockquote_ImmediatelyAfterHeading()
    {
        var md = "## Title\n> Quote right after heading";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[1].Kind);
    }

    [Fact]
    public void Blockquote_ImmediatelyBeforeBullet()
    {
        var md = "> Quote text\n- Bullet after";
        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Blockquote, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[1].Kind);
    }

    [Fact]
    public void Blockquote_InComplexDocument()
    {
        var md = string.Join("\n", new[]
        {
            "## Overview",
            "",
            "> Important note about the system.",
            "> This spans multiple lines.",
            "",
            "Some regular text.",
            "",
            "- Bullet one",
            "- Bullet two",
            "",
            "> Another quote after bullets."
        });

        var blocks = MarkdownParser.Parse(md);

        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[1].Kind);
        Assert.Contains("Important note", blocks[1].Content);
        Assert.Contains("multiple lines", blocks[1].Content);
        Assert.Equal(MdBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[4].Kind);
        Assert.Equal(MdBlockKind.Blockquote, blocks[5].Kind);
        Assert.Equal(6, blocks.Count);
    }
}
