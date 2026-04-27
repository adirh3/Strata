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

    [Fact]
    public void Rank_MultiTermQueryMatchesAcrossNameAndSecondaryText()
    {
        var chips = new[]
        {
            new StrataComposerChip("Daily Planner", SecondaryText: "plans todos schedule"),
            new StrataComposerChip("Schedule Importer", SecondaryText: "calendar ingestion"),
            new StrataComposerChip("Daily Journal", SecondaryText: "notes and reflections")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "daily schedule");

        Assert.Single(ranked);
        Assert.Equal("Daily Planner", ranked[0].Name);
    }

    [Fact]
    public void Rank_MultiTermQueryIsOrderInsensitive()
    {
        var chips = new[]
        {
            new StrataComposerChip("Code Reviewer", SecondaryText: "pull request bugs security"),
            new StrataComposerChip("Security Scanner", SecondaryText: "vulnerability checks"),
            new StrataComposerChip("Pull Request Summarizer", SecondaryText: "change overview")
        };

        var forward = StrataChatComposerSearch.Rank(chips, "pull security");
        var reversed = StrataChatComposerSearch.Rank(chips, "security pull");

        Assert.Equal("Code Reviewer", forward[0].Name);
        Assert.Equal("Code Reviewer", reversed[0].Name);
    }

    [Fact]
    public void Rank_PrimaryPrefixBeatsSecondaryOnlyMatch()
    {
        var chips = new[]
        {
            new StrataComposerChip("Daily Planner", SecondaryText: "creates schedule suggestions"),
            new StrataComposerChip("Schedule Builder", SecondaryText: "daily planning helper")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "schedule");

        Assert.Equal("Schedule Builder", ranked[0].Name);
    }

    [Fact]
    public void Rank_DiacriticsAreIgnored()
    {
        var chips = new[]
        {
            new StrataComposerChip("R\u00e9sum\u00e9 Writer"),
            new StrataComposerChip("Research Assistant")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "resume");

        Assert.Single(ranked);
        Assert.Equal("R\u00e9sum\u00e9 Writer", ranked[0].Name);
    }

    [Fact]
    public void Rank_EquivalentPrefixScoresPreferShorterNameThenOriginalOrder()
    {
        var chips = new[]
        {
            new StrataComposerChip("Build Helper"),
            new StrataComposerChip("Build"),
            new StrataComposerChip("Build Tools"),
            new StrataComposerChip("Build Tasks")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "build");

        Assert.Equal(["Build", "Build Tools", "Build Tasks", "Build Helper"], ranked.Select(static chip => chip.Name).ToArray());
    }

    [Fact]
    public void Rank_ExcludePredicateRemovesMatchesBeforeRanking()
    {
        var chips = new[]
        {
            new StrataComposerChip("Daily Planner"),
            new StrataComposerChip("Daily Notes")
        };

        var ranked = StrataChatComposerSearch.Rank(
            chips,
            "daily",
            chip => chip.Name == "Daily Planner");

        Assert.Single(ranked);
        Assert.Equal("Daily Notes", ranked[0].Name);
    }
}
