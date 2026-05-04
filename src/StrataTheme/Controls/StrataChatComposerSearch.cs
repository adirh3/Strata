using System.Collections;
using StrataSearch;

namespace StrataTheme.Controls;

internal static class StrataChatComposerSearch
{
    private static readonly SearchOptions ChipSearchOptions = new()
    {
        ParallelThreshold = 2_000
    };

    public static List<StrataComposerChip> Rank(
        IEnumerable source,
        string? query,
        Func<StrataComposerChip, bool>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var entries = new List<ChipEntry>();
        var index = 0;
        foreach (var item in source)
        {
            var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
            if (exclude?.Invoke(chip) != true)
                entries.Add(new ChipEntry(chip, index));

            index++;
        }

        var searchQuery = SearchQuery.Create(query);
        if (searchQuery.IsEmpty)
            return entries.Select(static entry => entry.Chip).ToList();

        var documents = entries.Select(static entry => SearchEngine.Prepare(CreateDocument(entry), entry.OriginalIndex)).ToArray();
        return SearchEngine
            .SearchPrepared(documents, searchQuery, ChipSearchOptions)
            .Select(static result => result.Item.Chip)
            .ToList();
    }

    internal static double ScoreChip(StrataComposerChip chip, string? query)
    {
        var searchQuery = SearchQuery.Create(query);
        if (searchQuery.IsEmpty)
            return 1;

        var result = SearchEngine.SearchPrepared(
            [SearchEngine.Prepare(CreateDocument(new ChipEntry(chip, 0)))],
            searchQuery,
            new SearchOptions { MaxResults = 1 });

        return result.Count == 0 ? 0 : result[0].Score;
    }

    private static SearchDocument<ChipEntry> CreateDocument(ChipEntry entry)
    {
        var fields = string.IsNullOrWhiteSpace(entry.Chip.SecondaryText)
            ? [SearchField.Primary(entry.Chip.Name)]
            : new[]
            {
                SearchField.Primary(entry.Chip.Name),
                new SearchField(entry.Chip.SecondaryText, 0.58)
            };

        return new SearchDocument<ChipEntry>(
            entry,
            fields,
            sort: new SearchSortMetadata(
                Length: entry.Chip.Name.Length,
                StableIndex: entry.OriginalIndex,
                Text: entry.Chip.Name));
    }

    private readonly record struct ChipEntry(StrataComposerChip Chip, int OriginalIndex);
}
