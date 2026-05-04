using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace StrataSearch;

public enum SearchFieldKind
{
    General,
    Primary,
    Content
}

public readonly record struct SearchField(string? Text, double Weight = 1, SearchFieldKind Kind = SearchFieldKind.General)
{
    public static SearchField Primary(string? text, double weight = 1) => new(text, weight, SearchFieldKind.Primary);
    public static SearchField Content(string? text, double weight = 1) => new(text, weight, SearchFieldKind.Content);
}

public readonly record struct SearchSortMetadata(
    DateTimeOffset? Timestamp = null,
    int? Length = null,
    int? StableIndex = null,
    string? Text = null);

public sealed class SearchDocument<T>
{
    public SearchDocument(
        T item,
        IEnumerable<SearchField> fields,
        double baseScore = 0,
        SearchSortMetadata sort = default)
    {
        Item = item;
        Fields = fields.ToArray();
        BaseScore = baseScore;
        Sort = sort;
    }

    public T Item { get; }
    public IReadOnlyList<SearchField> Fields { get; }
    public double BaseScore { get; }
    public SearchSortMetadata Sort { get; }
}

public sealed class PreparedSearchDocument<T>
{
    public PreparedSearchDocument(
        T item,
        IEnumerable<PreparedSearchField> fields,
        double baseScore = 0,
        SearchSortMetadata sort = default,
        int originalIndex = 0)
    {
        Item = item;
        Fields = fields.ToArray();
        BaseScore = baseScore;
        Sort = sort;
        OriginalIndex = originalIndex;
    }

    public T Item { get; }
    public IReadOnlyList<PreparedSearchField> Fields { get; }
    public double BaseScore { get; }
    public SearchSortMetadata Sort { get; }
    public int OriginalIndex { get; }
}

public sealed class PreparedSearchField
{
    public PreparedSearchField(string? text, double weight = 1, SearchFieldKind kind = SearchFieldKind.General)
    {
        Original = text ?? "";
        Weight = weight;
        Kind = kind;
        Text = SearchText.Create(text);
    }

    public string Original { get; }
    public double Weight { get; }
    public SearchFieldKind Kind { get; }
    public SearchText Text { get; }
    public bool IsContent => Kind == SearchFieldKind.Content;
    public bool IsPrimary => Kind == SearchFieldKind.Primary;
}

public sealed class SearchResult<T>
{
    public SearchResult(
        PreparedSearchDocument<T> document,
        T item,
        double score,
        SearchSortMetadata sort,
        int originalIndex,
        bool isContentMatch,
        string? bestContentSnippet)
    {
        Document = document;
        Item = item;
        Score = score;
        Sort = sort;
        OriginalIndex = originalIndex;
        IsContentMatch = isContentMatch;
        BestContentSnippet = bestContentSnippet;
    }

    public PreparedSearchDocument<T> Document { get; }
    public T Item { get; }
    public double Score { get; }
    public SearchSortMetadata Sort { get; }
    public int OriginalIndex { get; }
    public bool IsContentMatch { get; }
    public string? BestContentSnippet { get; }
}

public sealed class SearchEvaluation
{
    public static readonly SearchEvaluation NoMatch = new(false, 0, false, null);

    public SearchEvaluation(bool isMatch, double score, bool isContentMatch, string? bestContentSnippet)
    {
        IsMatch = isMatch;
        Score = score;
        IsContentMatch = isContentMatch;
        BestContentSnippet = bestContentSnippet;
    }

    public bool IsMatch { get; }
    public double Score { get; }
    public bool IsContentMatch { get; }
    public string? BestContentSnippet { get; }
}

public sealed class SearchOptions
{
    public static readonly SearchOptions Default = new();

    public int MaxResults { get; init; } = int.MaxValue;
    public int ParallelThreshold { get; init; } = 2_000;
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
}

public sealed class SearchQuery
{
    private SearchQuery(string original, SearchText text, SearchText[] terms)
    {
        Original = original;
        Text = text;
        Terms = terms;
    }

    public string Original { get; }
    public SearchText Text { get; }
    public IReadOnlyList<SearchText> Terms { get; }
    public bool IsEmpty => Text.IsEmpty;

    public static SearchQuery Create(string? query)
    {
        var original = query?.Trim() ?? "";
        var text = SearchText.Create(original);
        if (text.IsEmpty)
            return new SearchQuery("", text, []);

        var terms = text.Tokens
            .Distinct(StringComparer.Ordinal)
            .Select(SearchText.Create)
            .Where(static term => !term.IsEmpty)
            .ToArray();

        return new SearchQuery(original, text, terms);
    }
}

public readonly record struct SearchText(
    string Normalized,
    string Compact,
    string[] Tokens,
    string Initials,
    string[] TokenSpans)
{
    public bool IsEmpty => Compact.Length == 0;

    public static SearchText Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SearchText("", "", [], "", []);

        var normalizedBuilder = new StringBuilder(text.Length);
        var compactBuilder = new StringBuilder(text.Length);
        var tokens = new List<string>();
        var currentToken = new StringBuilder();
        var previous = '\0';

        void AppendSeparator()
        {
            if (normalizedBuilder.Length > 0 && normalizedBuilder[^1] != ' ')
                normalizedBuilder.Append(' ');
        }

        void FlushToken()
        {
            if (currentToken.Length == 0)
                return;

            tokens.Add(currentToken.ToString());
            currentToken.Clear();
        }

        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (!char.IsLetterOrDigit(character))
            {
                AppendSeparator();
                FlushToken();
                previous = '\0';
                continue;
            }

            var startsNewToken = currentToken.Length > 0
                                 && ((char.IsUpper(character) && char.IsLower(previous))
                                     || (char.IsDigit(character) != char.IsDigit(previous)));

            if (startsNewToken)
            {
                AppendSeparator();
                FlushToken();
            }

            var lower = char.ToLowerInvariant(character);
            normalizedBuilder.Append(lower);
            compactBuilder.Append(lower);
            currentToken.Append(lower);
            previous = character;
        }

        FlushToken();
        var normalized = normalizedBuilder.ToString().Trim();
        return new SearchText(
            normalized,
            compactBuilder.ToString(),
            tokens.Count == 0 ? [] : tokens.ToArray(),
            BuildInitials(tokens),
            BuildTokenSpans(tokens));
    }

    private static string BuildInitials(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return "";

        var builder = new StringBuilder(tokens.Count);
        foreach (var token in tokens)
            builder.Append(token[0]);

        return builder.ToString();
    }

    private static string[] BuildTokenSpans(IReadOnlyList<string> tokens)
    {
        if (tokens.Count <= 1)
            return [];

        var spans = new List<string>();
        for (var start = 0; start < tokens.Count; start++)
        {
            var builder = new StringBuilder();
            for (var length = 1; length <= 4 && start + length <= tokens.Count; length++)
            {
                builder.Append(tokens[start + length - 1]);
                if (length > 1)
                    spans.Add(builder.ToString());
            }
        }

        return spans.Distinct(StringComparer.Ordinal).ToArray();
    }
}

public static class SearchEngine
{
    public static bool IsMatch(string? query, params string?[] fields) => Score(query, fields) > 0;

    public static double Score(string? query, params string?[] fields)
        => Score(query, (IEnumerable<string?>)fields);

    public static double Score(string? query, IEnumerable<string?> fields)
    {
        var searchQuery = SearchQuery.Create(query);
        if (searchQuery.IsEmpty)
            return 1;

        var preparedFields = fields
            .Select(static field => new PreparedSearchField(field, 1, SearchFieldKind.Primary))
            .Where(static field => !field.Text.IsEmpty)
            .ToArray();

        return Evaluate(searchQuery, preparedFields).Score;
    }

    public static SearchEvaluation Evaluate(SearchQuery query, IEnumerable<SearchField> fields)
    {
        var preparedFields = fields
            .Select(static field => new PreparedSearchField(field.Text, field.Weight, field.Kind))
            .Where(static field => !field.Text.IsEmpty)
            .ToArray();

        return Evaluate(query, preparedFields);
    }

    public static SearchEvaluation Evaluate(SearchQuery query, IReadOnlyList<PreparedSearchField> fields)
    {
        if (query.IsEmpty)
            return new SearchEvaluation(true, 1, false, null);
        if (fields.Count == 0)
            return SearchEvaluation.NoMatch;

        var phraseMatch = BestFieldMatch(fields, query.Text);
        if (query.Terms.Count <= 1)
            return phraseMatch.Score > 0 ? ToEvaluation(phraseMatch) : SearchEvaluation.NoMatch;

        var termMatch = ScoreTermsAcrossFields(fields, query.Terms);
        if (termMatch.Score <= 0)
            return SearchEvaluation.NoMatch;

        var best = termMatch.Score > phraseMatch.Score ? termMatch : phraseMatch;
        return ToEvaluation(best);
    }

    public static PreparedSearchDocument<T> Prepare<T>(SearchDocument<T> document, int originalIndex = 0)
    {
        return new PreparedSearchDocument<T>(
            document.Item,
            document.Fields.Select(static field => new PreparedSearchField(field.Text, field.Weight, field.Kind)),
            document.BaseScore,
            document.Sort,
            originalIndex);
    }

    public static List<SearchResult<T>> Search<T>(
        IEnumerable<SearchDocument<T>> documents,
        string? query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var index = 0;
        var prepared = documents
            .Select(document => Prepare(document, index++))
            .ToArray();

        return SearchPrepared(prepared, SearchQuery.Create(query), options, cancellationToken);
    }

    public static List<SearchResult<T>> SearchPrepared<T>(
        IReadOnlyList<PreparedSearchDocument<T>> documents,
        SearchQuery query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= SearchOptions.Default;
        if (documents.Count == 0)
            return [];

        cancellationToken.ThrowIfCancellationRequested();
        var results = documents.Count >= options.ParallelThreshold && options.MaxDegreeOfParallelism > 1
            ? SearchPreparedParallel(documents, query, options, cancellationToken)
            : SearchPreparedSequential(documents, query, cancellationToken);

        return TakeTopResults(results, options.MaxResults);
    }

    private static List<SearchResult<T>> SearchPreparedSequential<T>(
        IReadOnlyList<PreparedSearchDocument<T>> documents,
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult<T>>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ScoreDocument(document, query);
            if (result is not null)
                results.Add(result);
        }

        return results;
    }

    private static List<SearchResult<T>> SearchPreparedParallel<T>(
        IReadOnlyList<PreparedSearchDocument<T>> documents,
        SearchQuery query,
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult<T>>();
        var resultsLock = new object();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism
        };

        Parallel.ForEach(
            documents,
            parallelOptions,
            () => new List<SearchResult<T>>(),
            (document, _, localResults) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ScoreDocument(document, query);
                if (result is not null)
                    localResults.Add(result);

                return localResults;
            },
            localResults =>
            {
                if (localResults.Count == 0)
                    return;

                lock (resultsLock)
                    results.AddRange(localResults);
            });

        return results;
    }

    private static SearchResult<T>? ScoreDocument<T>(PreparedSearchDocument<T> document, SearchQuery query)
    {
        var evaluation = Evaluate(query, document.Fields);
        if (!evaluation.IsMatch)
            return null;

        return new SearchResult<T>(
            document,
            document.Item,
            document.BaseScore + evaluation.Score,
            document.Sort,
            document.OriginalIndex,
            evaluation.IsContentMatch,
            evaluation.BestContentSnippet);
    }

    private static FieldMatch BestFieldMatch(IReadOnlyList<PreparedSearchField> fields, SearchText query)
    {
        var best = default(FieldMatch);
        foreach (var field in fields)
        {
            var score = ScorePrepared(field.Text, query);
            if (score <= 0)
                continue;

            score *= field.Weight;
            if (field.IsPrimary)
                score += 180;

            if (score > best.Score)
                best = new FieldMatch(score, field.IsContent, field.IsContent ? BuildSnippet(field.Original, query.Compact) : null, field.IsPrimary);
        }

        return best;
    }

    private static FieldMatch ScoreTermsAcrossFields(IReadOnlyList<PreparedSearchField> fields, IReadOnlyList<SearchText> terms)
    {
        var total = 0d;
        var primaryMatches = 0;
        var bestContentScore = 0d;
        string? bestContentSnippet = null;
        var hasContentMatch = false;

        foreach (var term in terms)
        {
            var best = BestFieldMatch(fields, term);
            if (best.Score <= 0)
                return default;

            total += best.Score;
            if (best.IsPrimary)
                primaryMatches++;

            if (best.IsContent)
            {
                hasContentMatch = true;
                if (best.Score > bestContentScore)
                {
                    bestContentScore = best.Score;
                    bestContentSnippet = best.ContentSnippet;
                }
            }
        }

        total += terms.Count * 110;
        if (primaryMatches == terms.Count)
            total += 180;
        else if (primaryMatches > 0)
            total += primaryMatches * 70;

        return new FieldMatch(total, hasContentMatch, bestContentSnippet, primaryMatches > 0);
    }

    private static SearchEvaluation ToEvaluation(FieldMatch match)
        => new(true, match.Score, match.IsContent, match.ContentSnippet);

    private static double ScorePrepared(SearchText candidate, SearchText query)
    {
        if (candidate.IsEmpty || query.IsEmpty)
            return 0;

        var score = 0d;

        if (string.Equals(candidate.Compact, query.Compact, StringComparison.Ordinal))
            score = Math.Max(score, 1_700);
        if (string.Equals(candidate.Normalized, query.Normalized, StringComparison.Ordinal))
            score = Math.Max(score, 1_620);

        score = Math.Max(score, ScorePrefix(candidate.Compact, query.Compact, 1_520));
        score = Math.Max(score, ScoreContains(candidate.Compact, query.Compact, 1_260));
        score = Math.Max(score, ScoreInitials(candidate.Initials, query.Compact, 1_410, 1_330, 1_180));
        score = Math.Max(score, ScoreTokenSpans(candidate.TokenSpans, query.Compact, 1_130));
        score = Math.Max(score, ScoreTokens(candidate.Tokens, query.Compact));

        if (candidate.Compact.Length <= 384)
        {
            score = Math.Max(score, ScoreApproximateContains(candidate.Compact, query.Compact, 1_080));
            score = Math.Max(score, ScoreSubsequence(candidate.Compact, query.Compact, 980));
            score = Math.Max(score, ScoreEditDistance(candidate.Compact, query.Compact, 940));
        }

        return score > 0 ? score + GetLengthBonus(candidate.Compact.Length, query.Compact.Length) : 0;
    }

    private static double ScoreTokens(IReadOnlyList<string> tokens, string query)
    {
        var best = 0d;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var positionPenalty = index * 24;

            if (string.Equals(token, query, StringComparison.Ordinal))
            {
                best = Math.Max(best, 1_600 - positionPenalty);
                continue;
            }

            best = Math.Max(best, ScorePrefix(token, query, 1_460 - positionPenalty));
            best = Math.Max(best, ScoreContains(token, query, 1_200 - positionPenalty));
            best = Math.Max(best, ScoreApproximateContains(token, query, 1_080 - positionPenalty));
            best = Math.Max(best, ScoreSubsequence(token, query, 980 - positionPenalty));
            best = Math.Max(best, ScoreEditDistance(token, query, 940 - positionPenalty));
        }

        return best;
    }

    private static double ScoreTokenSpans(IReadOnlyList<string> spans, string query, double baseScore)
    {
        var best = 0d;
        foreach (var span in spans)
        {
            best = Math.Max(best, ScorePrefix(span, query, baseScore));
            best = Math.Max(best, ScoreContains(span, query, baseScore - 120));
            best = Math.Max(best, ScoreApproximateContains(span, query, baseScore - 170));
            best = Math.Max(best, ScoreSubsequence(span, query, baseScore - 210));
            best = Math.Max(best, ScoreEditDistance(span, query, baseScore - 190));
        }

        return best;
    }

    private static double ScorePrefix(string candidate, string query, double baseScore)
    {
        if (string.IsNullOrEmpty(candidate)
            || string.IsNullOrEmpty(query)
            || !candidate.StartsWith(query, StringComparison.Ordinal))
        {
            return 0;
        }

        return baseScore - (Math.Max(0, candidate.Length - query.Length) * 8);
    }

    private static double ScoreContains(string candidate, string query, double baseScore)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(query))
            return 0;

        var index = candidate.IndexOf(query, StringComparison.Ordinal);
        if (index < 0)
            return 0;

        return baseScore
               - (index * 22)
               - (Math.Max(0, candidate.Length - query.Length) * 4);
    }

    private static double ScoreInitials(
        string initials,
        string query,
        double exactScore,
        double prefixScore,
        double subsequenceScore)
    {
        if (string.IsNullOrEmpty(initials) || string.IsNullOrEmpty(query))
            return 0;

        if (string.Equals(initials, query, StringComparison.Ordinal))
            return exactScore;

        var fuzzyScore = Math.Max(
            ScorePrefix(initials, query, prefixScore),
            ScoreSubsequence(initials, query, subsequenceScore));
        return Math.Min(fuzzyScore, exactScore - 1);
    }

    private static double ScoreApproximateContains(string candidate, string query, double baseScore)
    {
        if (string.IsNullOrEmpty(candidate) || query.Length < 5 || candidate.Length < 3)
            return 0;

        if (candidate.Length > Math.Max(48, query.Length * 3))
            return 0;

        var maxDistance = GetApproximateMaxDistance(query.Length);
        if (!HasCharacterOverlap(candidate, query, query.Length - maxDistance))
            return 0;

        var minWindowLength = Math.Max(3, query.Length - Math.Min(maxDistance, 1));
        var maxWindowLength = Math.Min(candidate.Length, query.Length + maxDistance);
        if (minWindowLength > maxWindowLength)
            return 0;

        var bestScore = 0d;
        for (var windowLength = minWindowLength; windowLength <= maxWindowLength; windowLength++)
        {
            for (var start = 0; start <= candidate.Length - windowLength; start++)
            {
                if (!IsPlausibleApproximateStart(candidate, query, start))
                    continue;

                var distance = DamerauLevenshteinDistance(candidate.AsSpan(start, windowLength), query.AsSpan(), maxDistance);
                if (distance <= 0 || distance > maxDistance)
                    continue;

                var score = baseScore
                            - (distance * 120)
                            - (Math.Abs(windowLength - query.Length) * 36)
                            - (start * 18)
                            + (Math.Min(1d, (double)query.Length / candidate.Length) * 90);
                if (score > bestScore)
                    bestScore = score;
            }
        }

        return bestScore;
    }

    private static bool IsPlausibleApproximateStart(string candidate, string query, int start)
    {
        return start == 0
               || candidate[start] == query[0]
               || (query.Length > 1 && candidate[start] == query[1])
               || (start > 0 && candidate[start - 1] == query[0]);
    }

    private static bool HasCharacterOverlap(string candidate, string query, int requiredMatches)
    {
        if (requiredMatches <= 0)
            return true;

        if (candidate.Length == 0 || query.Length == 0)
            return false;

        Span<byte> used = candidate.Length <= 256 ? stackalloc byte[candidate.Length] : new byte[candidate.Length];
        var matches = 0;
        foreach (var queryChar in query)
        {
            for (var candidateIndex = 0; candidateIndex < candidate.Length; candidateIndex++)
            {
                if (used[candidateIndex] != 0 || candidate[candidateIndex] != queryChar)
                    continue;

                used[candidateIndex] = 1;
                matches++;
                if (matches >= requiredMatches)
                    return true;

                break;
            }
        }

        return false;
    }

    private static double ScoreSubsequence(string candidate, string query, double baseScore)
    {
        if (query.Length < 2 || candidate.Length < query.Length)
            return 0;

        var queryIndex = 0;
        var firstMatchIndex = -1;
        var previousMatchIndex = -2;
        var consecutiveBonus = 0;
        var gapPenalty = 0;

        for (var candidateIndex = 0; candidateIndex < candidate.Length && queryIndex < query.Length; candidateIndex++)
        {
            if (candidate[candidateIndex] != query[queryIndex])
                continue;

            if (firstMatchIndex < 0)
                firstMatchIndex = candidateIndex;

            if (candidateIndex == previousMatchIndex + 1)
                consecutiveBonus += 28;
            else if (previousMatchIndex >= 0)
                gapPenalty += candidateIndex - previousMatchIndex - 1;

            previousMatchIndex = candidateIndex;
            queryIndex++;
        }

        if (queryIndex != query.Length)
            return 0;

        var densityBonus = ((double)query.Length / candidate.Length) * 150;
        var startBonus = firstMatchIndex == 0 ? 40 : 0;
        var firstMatchPenalty = Math.Max(0, firstMatchIndex) * 16;
        return baseScore + densityBonus + startBonus + consecutiveBonus - firstMatchPenalty - (gapPenalty * 14);
    }

    private static double ScoreEditDistance(string candidate, string query, double baseScore)
    {
        if (candidate.Length < 3
            || query.Length < 3
            || Math.Abs(candidate.Length - query.Length) > 2)
        {
            return 0;
        }

        var maxDistance = GetApproximateMaxDistance(query.Length);
        var distance = DamerauLevenshteinDistance(candidate.AsSpan(), query.AsSpan(), maxDistance);
        if (distance > maxDistance)
            return 0;

        return baseScore
               - (distance * 130)
               - (Math.Abs(candidate.Length - query.Length) * 40);
    }

    private static int GetApproximateMaxDistance(int queryLength)
    {
        return queryLength switch
        {
            <= 4 => 1,
            <= 8 => 2,
            <= 14 => 3,
            _ => 4
        };
    }

    private static double GetLengthBonus(int candidateLength, int queryLength)
    {
        if (candidateLength <= 0 || queryLength <= 0)
            return 0;

        return Math.Min(1d, (double)queryLength / candidateLength) * 65;
    }

    private static int DamerauLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int maxDistance)
    {
        if (source.Length == 0)
            return target.Length;
        if (target.Length == 0)
            return source.Length;

        if (Math.Abs(source.Length - target.Length) > maxDistance)
            return maxDistance + 1;

        Span<int> previous = target.Length <= 128 ? stackalloc int[target.Length + 1] : new int[target.Length + 1];
        Span<int> current = target.Length <= 128 ? stackalloc int[target.Length + 1] : new int[target.Length + 1];
        Span<int> transposition = target.Length <= 128 ? stackalloc int[target.Length + 1] : new int[target.Length + 1];

        for (var column = 0; column <= target.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= source.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];

            for (var column = 1; column <= target.Length; column++)
            {
                var substitutionCost = source[row - 1] == target[column - 1] ? 0 : 1;
                var value = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + substitutionCost);

                if (row > 1
                    && column > 1
                    && source[row - 1] == target[column - 2]
                    && source[row - 2] == target[column - 1])
                {
                    value = Math.Min(value, transposition[column - 2] + 1);
                }

                current[column] = value;
                rowMinimum = Math.Min(rowMinimum, value);
            }

            if (rowMinimum > maxDistance)
                return maxDistance + 1;

            var oldTransposition = transposition;
            transposition = previous;
            previous = current;
            current = oldTransposition;
        }

        return previous[target.Length];
    }

    private static string? BuildSnippet(string text, string compactTerm)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalizedTerm = SearchText.Create(compactTerm).Normalized;
        if (normalizedTerm.Length == 0)
            return TrimForSnippet(text, 0);

        var index = CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            text,
            normalizedTerm,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

        return TrimForSnippet(text, Math.Max(index, 0));
    }

    private static string TrimForSnippet(string text, int matchIndex)
    {
        const int radius = 72;
        var start = Math.Max(0, matchIndex - radius);
        var length = Math.Min(text.Length - start, radius * 2);
        var snippet = text.Substring(start, length).Trim();

        if (start > 0)
            snippet = "..." + snippet;
        if (start + length < text.Length)
            snippet += "...";

        return snippet;
    }

    private static List<SearchResult<T>> TakeTopResults<T>(List<SearchResult<T>> results, int maxResults)
    {
        if (maxResults <= 0 || results.Count == 0)
            return [];

        if (maxResults >= results.Count || results.Count <= (long)maxResults * 4)
        {
            results.Sort(CompareResults);
            return results.Count > maxResults ? results.Take(maxResults).ToList() : results;
        }

        var top = new List<SearchResult<T>>(maxResults);
        var worstIndex = -1;
        foreach (var candidate in results)
        {
            if (top.Count < maxResults)
            {
                top.Add(candidate);
                worstIndex = FindWorstIndex(top);
                continue;
            }

            if (CompareResults(candidate, top[worstIndex]) >= 0)
                continue;

            top[worstIndex] = candidate;
            worstIndex = FindWorstIndex(top);
        }

        top.Sort(CompareResults);
        return top;
    }

    public static int CompareResults<T>(SearchResult<T> left, SearchResult<T> right)
    {
        var scoreComparison = right.Score.CompareTo(left.Score);
        if (scoreComparison != 0)
            return scoreComparison;

        if (left.Sort.Timestamp.HasValue && right.Sort.Timestamp.HasValue)
        {
            var timestampComparison = right.Sort.Timestamp.Value.CompareTo(left.Sort.Timestamp.Value);
            if (timestampComparison != 0)
                return timestampComparison;
        }

        if (left.Sort.Length.HasValue && right.Sort.Length.HasValue)
        {
            var lengthComparison = left.Sort.Length.Value.CompareTo(right.Sort.Length.Value);
            if (lengthComparison != 0)
                return lengthComparison;
        }

        if (left.Sort.StableIndex.HasValue && right.Sort.StableIndex.HasValue)
        {
            var stableComparison = left.Sort.StableIndex.Value.CompareTo(right.Sort.StableIndex.Value);
            if (stableComparison != 0)
                return stableComparison;
        }

        var textComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Sort.Text, right.Sort.Text);
        if (textComparison != 0)
            return textComparison;

        return left.OriginalIndex.CompareTo(right.OriginalIndex);
    }

    private static int FindWorstIndex<T>(IReadOnlyList<SearchResult<T>> results)
    {
        var worstIndex = 0;
        for (var i = 1; i < results.Count; i++)
        {
            if (CompareResults(results[i], results[worstIndex]) > 0)
                worstIndex = i;
        }

        return worstIndex;
    }

    private readonly record struct FieldMatch(
        double Score,
        bool IsContent,
        string? ContentSnippet,
        bool IsPrimary);
}
