using Avalonia.Input;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public sealed class ChatClipboardPortableTests
{
    [Theory]
    [InlineData("card", """{"header":"Report","summary":"A **bold** summary.","detail":"Full detail."}""",
        "### Report\n\nA **bold** summary.\n\nFull detail.")]
    [InlineData("confidence", """{"label":"Evidence","value":85,"explanation":"Verified."}""",
        "**Evidence: 85%**\n\nVerified.")]
    [InlineData("comparison", """{"optionA":{"title":"First","content":"Use A."},"optionB":{"title":"Second","content":"Use B."}}""",
        "### First\n\nUse A.\n\n### Second\n\nUse B.")]
    [InlineData("chart", """{"type":"bar","labels":["Q1","Q2"],"series":[{"name":"Revenue","values":[12,20]}]}""",
        "| Category | Revenue |\n| --- | --- |\n| Q1 | 12 |\n| Q2 | 20 |")]
    [InlineData("sources", """{"sources":[{"title":"Reference","url":"https://example.com","snippet":"Evidence."}]}""",
        "- [Reference](https://example.com/)\n  Evidence.")]
    public void SpecialBlocks_ExportAsPortableMarkdownAndHtml(string kind, string json, string expected)
    {
        var source = $"Before.\n\n```{kind}\n{json}\n```\n\nAfter.";
        var markdown = MarkdownClipboardFormatter.ToPortableMarkdown(source);
        Assert.Contains(expected, markdown);
        Assert.StartsWith("Before.", markdown);
        Assert.EndsWith("After.", markdown);
        Assert.DoesNotContain($"```{kind}", markdown);
        Assert.DoesNotContain(json, markdown);
        var html = MarkdownClipboardFormatter.ToHtmlFragment(source);
        Assert.DoesNotContain("<pre", html);
        Assert.DoesNotContain("&quot;optionA&quot;", html);
        Assert.DoesNotContain("&quot;labels&quot;", html);
    }

    [Fact]
    public void Checklist_ExportsItsCurrentCheckedState()
    {
        const string source = """
            ```checklist
            {"id":"pack","title":"Packing","items":[{"id":"one","label":"Passport"},{"id":"two","label":"Tickets","meta":"2 copies"}]}
            ```
            """;
        var states = new Dictionary<string, string>
        {
            ["pack"] = new InteractiveState { CheckedIds = ["one"] }.ToJson()
        };
        var markdown = MarkdownClipboardFormatter.ToPortableMarkdown(source, states);
        Assert.Contains("- [x] Passport", markdown);
        Assert.Contains("- [ ] Tickets (2 copies)", markdown);
    }

    [Fact]
    public void Form_ExportsCurrentValuesWithoutItsWidgetDefinition()
    {
        const string source = """
            ```form
            {"id":"trip","title":"Trip","fields":[{"id":"place","label":"Destination"},{"id":"interests","label":"Interests","type":"multiselect","options":["Food","Culture"]}]}
            ```
            """;
        var state = new InteractiveState
        {
            Values = { ["place"] = "Amsterdam" },
            Choices = { ["interests"] = ["Food", "Culture"] }
        };
        var markdown = MarkdownClipboardFormatter.ToPortableMarkdown(source, new Dictionary<string, string>
        {
            ["trip"] = state.ToJson()
        });
        Assert.Contains("| Destination | Amsterdam |", markdown);
        Assert.Contains("| Interests | Food, Culture |", markdown);
        Assert.DoesNotContain("```form", markdown);
    }

    [Fact]
    public void Calculator_ExportsInputsAndComputedResults()
    {
        const string source = """
            ```calculator
            {"id":"meal","title":"Dinner","template":"bill-split","inputs":{"bill":100,"tipPercent":10,"people":2}}
            ```
            """;
        var markdown = MarkdownClipboardFormatter.ToPortableMarkdown(source);
        Assert.Contains("### Dinner", markdown);
        Assert.Contains("| Input | Value |", markdown);
        Assert.Contains("| Result | Value |", markdown);
        Assert.Contains("55", markdown);
        Assert.DoesNotContain("```calculator", markdown);
    }

    [Fact]
    public void Calculator_WithAnIncompleteInput_ExportsItsCurrentValuesAndValidation()
    {
        const string source = """
            ```calculator
            {"id":"meal","title":"Dinner","template":"bill-split","inputs":{"bill":100,"tipPercent":10,"people":2}}
            ```
            """;
        var state = new InteractiveState { Values = { ["bill"] = "150", ["tipPercent"] = "10", ["people"] = "" } };
        var markdown = MarkdownClipboardFormatter.ToPortableMarkdown(source, new Dictionary<string, string>
        {
            ["meal"] = state.ToJson()
        });
        Assert.Contains("150", markdown);
        Assert.Contains("| Input | Value |", markdown);
        Assert.DoesNotContain("| Result | Value |", markdown);
        Assert.DoesNotContain("```calculator", markdown);
    }

    [Fact]
    public void Quiz_DoesNotRevealUnansweredQuestionsOrUncheckedExplanations()
    {
        const string source = """
            ```quiz
            {"id":"learn","title":"Practice","questions":[{"id":"q","prompt":"Pick one","options":["A","B"],"correctIndex":1,"explanation":"Hidden explanation."}]}
            ```
            """;
        var markdown = MarkdownClipboardFormatter.ToPortableMarkdown(source);
        Assert.Contains("1. Pick one", markdown);
        Assert.Contains("- [ ] A", markdown);
        Assert.Contains("- [ ] B", markdown);
        Assert.DoesNotContain("correctIndex", markdown);
        Assert.DoesNotContain("Hidden explanation", markdown);
        var checkedState = new InteractiveState { Answers = { ["q"] = 1 }, CheckedAnswers = ["q"] };
        var checkedMarkdown = MarkdownClipboardFormatter.ToPortableMarkdown(source, new Dictionary<string, string>
        {
            ["learn"] = checkedState.ToJson()
        });
        Assert.Contains("- [x] B", checkedMarkdown);
        Assert.Contains("Hidden explanation", checkedMarkdown);
    }

    [Fact]
    public void MarkdownCopy_PublishesPortableContent_InBothMarkdownAndTextFormats()
    {
        const string source = "```card\n{\"header\":\"Report\",\"summary\":\"Ready\"}\n```";
        using var data = ChatClipboardData.CreateMarkdown(source);
        var item = Assert.Single(data.Items);
        var format = Assert.IsType<DataFormat<string>>(data.Formats.Single(format => format.Identifier == "text/markdown"));
        Assert.Equal("### Report\n\nReady", item.TryGetRaw(format));
        Assert.Equal(item.TryGetRaw(format), item.TryGetRaw(DataFormat.Text));
    }

    [Fact]
    public void PlainText_HasReadableFormattingAndNoJsonOrMarkdownDelimiters()
    {
        const string source = "**Hello** and `code`.\n\n```confidence\n{\"label\":\"Evidence\",\"value\":85}\n```";
        var plain = MarkdownClipboardFormatter.ToPlainText(source);
        Assert.Equal("Hello and code.\n\nEvidence: 85%", plain);
    }

    [Theory]
    [InlineData("```card\nnot json\n```")]
    [InlineData("```chart\n{\"labels\":[\"one\"]}\n```")]
    [InlineData("```quiz\n{\"title\":\"Incomplete\"}\n```")]
    public void InvalidSpecialBlock_IsPreservedRatherThanLost(string source)
    {
        Assert.Equal(source, MarkdownClipboardFormatter.ToPortableMarkdown(source));
    }

    [Fact]
    public void OrdinaryMarkdownAndMermaid_AreNotRewritten()
    {
        const string source = "# Heading\n\n**Text**\n\n```mermaid\nflowchart LR\n A --> B\n```";
        Assert.Equal(source, MarkdownClipboardFormatter.ToPortableMarkdown(source));
    }
}
