using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

public class StrataChatComposerSearchTests
{
    [Fact]
    public void Rank_OrdersExactPrefixAndContainsIntuitively()
    {
        var chips = new[]
        {
            new StrataComposerChip("Build"),
            new StrataComposerChip("BuildPipeline"),
            new StrataComposerChip("PipelineBuild")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "build");

        Assert.Equal(["Build", "BuildPipeline", "PipelineBuild"], ranked.Select(static chip => chip.Name).ToArray());
    }

    [Fact]
    public void Rank_UsesSecondarySearchText()
    {
        var chips = new[]
        {
            new StrataComposerChip("Daily Planner", SecondaryText: "plans your day todos schedule"),
            new StrataComposerChip("Code Reviewer", SecondaryText: "reviews pull requests bugs security")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "schedule");

        Assert.Single(ranked);
        Assert.Equal("Daily Planner", ranked[0].Name);
    }

    [Fact]
    public void Rank_AcronymMatch_BeatsLooseFuzzyMatch()
    {
        var chips = new[]
        {
            new StrataComposerChip("ChatViewModel"),
            new StrataComposerChip("ConversationValueMapper"),
            new StrataComposerChip("ChannelViewer")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "cvm");

        Assert.Equal("ChatViewModel", ranked[0].Name);
    }

    [Fact]
    public void Rank_TypoStillFindsBestCandidate()
    {
        var chips = new[]
        {
            new StrataComposerChip("Architecture Advisor"),
            new StrataComposerChip("Archive Manager"),
            new StrataComposerChip("Daily Planner")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "archtecture");

        Assert.NotEmpty(ranked);
        Assert.Equal("Architecture Advisor", ranked[0].Name);
    }
}
