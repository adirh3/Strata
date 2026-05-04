using System.Diagnostics;
using StrataSearch;
using Xunit;

namespace StrataTheme.Tests;

public sealed class SearchEngineTests
{
    [Fact]
    public void Search_OrdersExactPrefixContainsAndStableTies()
    {
        var results = SearchEngine.Search(
            [
                Document("BuildPipeline"),
                Document("PipelineBuild"),
                Document("Build")
            ],
            "build");

        Assert.Equal(["Build", "BuildPipeline", "PipelineBuild"], results.Select(static result => result.Item));
    }

    [Theory]
    [InlineData("archtecture", "Architecture Advisor")]
    [InlineData("viewmodle", "ChatViewModel")]
    [InlineData("opentempla", "OpenAI.Template.json")]
    public void Search_FindsTyposAndSeparatorSpanningQueries(string query, string expected)
    {
        var results = SearchEngine.Search(
            [
                Document("Architecture Advisor"),
                Document("Archive Manager"),
                Document("ChatViewModel"),
                Document("ChatVirtualMachine"),
                Document("OpenAI.Template.json"),
                Document("OpenTelemetry.Provider.json")
            ],
            query);

        Assert.NotEmpty(results);
        Assert.Equal(expected, results[0].Item);
    }

    [Theory]
    [InlineData("law", "Log Analytics Workspace")]
    [InlineData("cvm", "ChatViewModel")]
    public void Search_FindsAcronymsBetweenSeparatorsAndCamelCase(string query, string expected)
    {
        var results = SearchEngine.Search(
            [
                Document("LaunchWindow"),
                Document("LegalAdvice"),
                Document("Log Analytics Workspace"),
                Document("ConversationValueMapper"),
                Document("ChatViewModel")
            ],
            query);

        Assert.NotEmpty(results);
        Assert.Equal(expected, results[0].Item);
    }

    [Fact]
    public void Search_RequiresEveryMultiTermToMatchAcrossFields()
    {
        var results = SearchEngine.Search(
            [
                Document("Daily Planner", ("plans todos schedule", 1.4)),
                Document("Daily Journal", ("notes and reflections", 1.4)),
                Document("Schedule Importer", ("calendar ingestion", 1.4))
            ],
            "daily schedule");

        var match = Assert.Single(results);
        Assert.Equal("Daily Planner", match.Item);
    }

    [Fact]
    public void Search_PrimaryFieldBeatsSecondaryOnlyMatch()
    {
        var results = SearchEngine.Search(
            [
                Document("Daily Planner", ("creates schedule suggestions", 1.2)),
                Document("Schedule Builder", ("daily planning helper", 1.2))
            ],
            "schedule");

        Assert.Equal("Schedule Builder", results[0].Item);
    }

    [Fact]
    public void Search_UsesDeterministicTopNSelection()
    {
        var documents = Enumerable.Range(0, 5_000)
            .Select(index => new SearchDocument<string>(
                $"item-{index:D4}",
                [SearchField.Primary($"Common Result {index:D4}")],
                sort: new SearchSortMetadata(StableIndex: index, Text: $"Common Result {index:D4}")))
            .ToArray();

        var first = SearchEngine.Search(documents, "common", new SearchOptions { MaxResults = 25, ParallelThreshold = 128 })
            .Select(static result => result.Item)
            .ToArray();
        var second = SearchEngine.Search(documents, "common", new SearchOptions { MaxResults = 25, ParallelThreshold = 128 })
            .Select(static result => result.Item)
            .ToArray();

        Assert.Equal(first, second);
        Assert.Equal(25, first.Length);
    }

    [Fact]
    public void Search_UnlimitedResultsDoesNotOverflowTopN()
    {
        var results = SearchEngine.Search(
            [Document("Daily Planner"), Document("Daily Notes")],
            "daily",
            new SearchOptions { MaxResults = int.MaxValue });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SearchEngine.Search(
                [Document("Daily Planner")],
                "daily",
                cancellationToken: cts.Token));
    }

    [Fact]
    public void Search_LargeIndexCompletesWithinInteractiveBudget()
    {
        var documents = Enumerable.Range(0, 12_000)
            .Select(index => Document(index == 7_777 ? "OpenAI.Template.json" : $"unrelated-file-{index:D5}.txt"))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var results = SearchEngine.Search(
            documents,
            "opentempla",
            new SearchOptions { MaxResults = 10, ParallelThreshold = 256 });
        stopwatch.Stop();

        Assert.Equal("OpenAI.Template.json", results[0].Item);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Search took {stopwatch.Elapsed.TotalMilliseconds:N0}ms for {documents.Length:N0} documents.");
    }

    private static SearchDocument<string> Document(string name, params (string Text, double Weight)[] secondaryFields)
    {
        var fields = new List<SearchField> { SearchField.Primary(name, 3.0) };
        foreach (var (text, weight) in secondaryFields)
            fields.Add(new SearchField(text, weight));

        return new SearchDocument<string>(
            name,
            fields,
            sort: new SearchSortMetadata(Length: name.Length, Text: name));
    }
}
