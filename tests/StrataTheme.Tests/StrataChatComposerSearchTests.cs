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
    public void Rank_TypoInsideLongCompactNameStillFindsBestCandidate()
    {
        var chips = new[]
        {
            new StrataComposerChip("File Search Service"),
            new StrataComposerChip("File Storage Service"),
            new StrataComposerChip("Search Settings")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "fileserach");

        Assert.NotEmpty(ranked);
        Assert.Equal("File Search Service", ranked[0].Name);
    }

    [Fact]
    public void Rank_TypoInsideCamelCaseSegmentStillFindsBestCandidate()
    {
        var chips = new[]
        {
            new StrataComposerChip("ChatViewModel"),
            new StrataComposerChip("ChatVirtualMachine"),
            new StrataComposerChip("ConversationViewModel")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "viewmodle");

        Assert.NotEmpty(ranked);
        Assert.Equal("ChatViewModel", ranked[0].Name);
    }

    [Fact]
    public void Rank_AcronymInitialsFindLogAnalyticsWorkspace()
    {
        var chips = new[]
        {
            new StrataComposerChip("LaunchWindow"),
            new StrataComposerChip("LogAnalyticsWorkspace"),
            new StrataComposerChip("LegalAdvice")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "law");

        Assert.NotEmpty(ranked);
        Assert.Equal("LogAnalyticsWorkspace", ranked[0].Name);
    }

    [Fact]
    public void Rank_ExactShortNameBeatsAcronymExpansion()
    {
        var chips = new[]
        {
            new StrataComposerChip("LogAnalyticsWorkspace"),
            new StrataComposerChip("Law")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "law");

        Assert.Equal("Law", ranked[0].Name);
    }

    [Fact]
    public void Rank_SeparatorInitialsFindAcronymCandidate()
    {
        var chips = new[]
        {
            new StrataComposerChip("LaunchWindow"),
            new StrataComposerChip("Log Analytics Workspace")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "law");

        Assert.NotEmpty(ranked);
        Assert.Equal("Log Analytics Workspace", ranked[0].Name);
    }

    [Fact]
    public void Rank_MultiTermPrefixAndTypoFindsBestCandidate()
    {
        var chips = new[]
        {
            new StrataComposerChip("LogAnalyticsWriter"),
            new StrataComposerChip("LogAnalyticsWorkspace")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "log workspce");

        Assert.NotEmpty(ranked);
        Assert.Equal("LogAnalyticsWorkspace", ranked[0].Name);
    }

    [Fact]
    public void Rank_AcronymMatchesAcrossNameAndSecondaryText()
    {
        var chips = new[]
        {
            new StrataComposerChip("Azure Resource", SecondaryText: "Log Analytics Workspace"),
            new StrataComposerChip("Azure Database", SecondaryText: "storage account"),
            new StrataComposerChip("Logs", SecondaryText: "application window")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "azure law");

        Assert.Single(ranked);
        Assert.Equal("Azure Resource", ranked[0].Name);
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
    public void Rank_MultiTermTypoMatchesAcrossNameAndSecondaryText()
    {
        var chips = new[]
        {
            new StrataComposerChip("Code Reviewer", SecondaryText: "finds security vulnerabilities and correctness bugs"),
            new StrataComposerChip("Security Scanner", SecondaryText: "dependency vulnerability checks"),
            new StrataComposerChip("Code Formatter", SecondaryText: "formats whitespace and imports")
        };

        var ranked = StrataChatComposerSearch.Rank(chips, "code secuirty");

        Assert.Single(ranked);
        Assert.Equal("Code Reviewer", ranked[0].Name);
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
