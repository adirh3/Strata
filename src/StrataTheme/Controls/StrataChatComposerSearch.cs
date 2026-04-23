using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StrataTheme.Controls;

internal static class StrataChatComposerSearch
{
    public static List<StrataComposerChip> Rank(
        IEnumerable source,
        string? query,
        Func<StrataComposerChip, bool>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var chips = new List<(StrataComposerChip Chip, int OriginalIndex)>();
        var index = 0;
        foreach (var item in source)
        {
            var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
            if (exclude?.Invoke(chip) == true)
            {
                index++;
                continue;
            }

            chips.Add((chip, index));
            index++;
        }

        var preparedQuery = PreparedSearchText.Create(query);
        if (preparedQuery.IsEmpty)
            return chips.Select(static entry => entry.Chip).ToList();

        var matches = new List<RankedChip>(chips.Count);
        foreach (var (chip, originalIndex) in chips)
        {
            var score = ScoreChip(chip, preparedQuery);
            if (score > 0)
                matches.Add(new RankedChip(chip, score, originalIndex));
        }

        matches.Sort(static (left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            if (scoreComparison != 0)
                return scoreComparison;

            var lengthComparison = left.Chip.Name.Length.CompareTo(right.Chip.Name.Length);
            if (lengthComparison != 0)
                return lengthComparison;

            var originalComparison = left.OriginalIndex.CompareTo(right.OriginalIndex);
            if (originalComparison != 0)
                return originalComparison;

            return StringComparer.OrdinalIgnoreCase.Compare(left.Chip.Name, right.Chip.Name);
        });

        return matches.Select(static match => match.Chip).ToList();
    }

    internal static double ScoreChip(StrataComposerChip chip, string? query)
        => ScoreChip(chip, PreparedSearchText.Create(query));

    private static double ScoreChip(StrataComposerChip chip, PreparedSearchText query)
    {
        if (query.IsEmpty)
            return 1;

        var title = PreparedSearchText.Create(chip.Name);
        var score = ScorePrepared(title, query, primary: true);

        if (!string.IsNullOrWhiteSpace(chip.SecondaryText))
        {
            var secondary = PreparedSearchText.Create(chip.SecondaryText);
            score = Math.Max(score, ScorePrepared(secondary, query, primary: false));
        }

        if (score <= 0)
            return 0;

        return score + GetLengthBonus(title.Compact.Length, query.Compact.Length);
    }

    private static double ScorePrepared(PreparedSearchText candidate, PreparedSearchText query, bool primary)
    {
        if (candidate.IsEmpty || query.IsEmpty)
            return 0;

        var score = 0d;

        if (string.Equals(candidate.Compact, query.Compact, StringComparison.Ordinal))
        {
            score = 1_700;
        }
        else if (string.Equals(candidate.Normalized, query.Normalized, StringComparison.Ordinal))
        {
            score = 1_620;
        }
        else
        {
            score = Math.Max(score, ScorePrefix(candidate.Compact, query.Compact, 1_520));
            score = Math.Max(score, ScoreContains(candidate.Compact, query.Compact, 1_260));
            score = Math.Max(score, ScoreTokens(candidate.Tokens, query.Compact));

            if (candidate.Initials.Length > 0)
            {
                if (string.Equals(candidate.Initials, query.Compact, StringComparison.Ordinal))
                    score = Math.Max(score, 1_340);
                else
                {
                    score = Math.Max(score, ScorePrefix(candidate.Initials, query.Compact, 1_280));
                    score = Math.Max(score, ScoreSubsequence(candidate.Initials, query.Compact, 1_120));
                }
            }

            score = Math.Max(score, ScoreSubsequence(candidate.Compact, query.Compact, 1_020));
            score = Math.Max(score, ScoreEditDistance(candidate.Compact, query.Compact, 980));
        }

        return primary ? score : score * 0.58;
    }

    private static double ScoreTokens(IReadOnlyList<string> tokens, string query)
    {
        if (tokens.Count == 0 || string.IsNullOrEmpty(query))
            return 0;

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
            best = Math.Max(best, ScoreSubsequence(token, query, 980 - positionPenalty));
            best = Math.Max(best, ScoreEditDistance(token, query, 940 - positionPenalty));
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

        var lengthPenalty = Math.Max(0, candidate.Length - query.Length) * 8;
        return baseScore - lengthPenalty;
    }

    private static double ScoreContains(string candidate, string query, double baseScore)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(query))
            return 0;

        var index = candidate.IndexOf(query, StringComparison.Ordinal);
        if (index < 0)
            return 0;

        var positionPenalty = index * 22;
        var lengthPenalty = Math.Max(0, candidate.Length - query.Length) * 4;
        return baseScore - positionPenalty - lengthPenalty;
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

        var maxDistance = query.Length <= 4 ? 1 : 2;
        var distance = DamerauLevenshteinDistance(candidate, query, maxDistance);
        if (distance > maxDistance)
            return 0;

        var distancePenalty = distance * 130;
        var lengthPenalty = Math.Abs(candidate.Length - query.Length) * 40;
        return baseScore - distancePenalty - lengthPenalty;
    }

    private static int DamerauLevenshteinDistance(string source, string target, int maxDistance)
    {
        if (Math.Abs(source.Length - target.Length) > maxDistance)
            return maxDistance + 1;

        var rows = source.Length + 1;
        var columns = target.Length + 1;
        var distances = new int[rows, columns];

        for (var row = 0; row < rows; row++)
            distances[row, 0] = row;

        for (var column = 0; column < columns; column++)
            distances[0, column] = column;

        for (var row = 1; row < rows; row++)
        {
            var minDistanceThisRow = int.MaxValue;
            for (var column = 1; column < columns; column++)
            {
                var substitutionCost = source[row - 1] == target[column - 1] ? 0 : 1;
                var deletion = distances[row - 1, column] + 1;
                var insertion = distances[row, column - 1] + 1;
                var substitution = distances[row - 1, column - 1] + substitutionCost;

                var distance = Math.Min(Math.Min(deletion, insertion), substitution);

                if (row > 1
                    && column > 1
                    && source[row - 1] == target[column - 2]
                    && source[row - 2] == target[column - 1])
                {
                    distance = Math.Min(distance, distances[row - 2, column - 2] + substitutionCost);
                }

                distances[row, column] = distance;
                minDistanceThisRow = Math.Min(minDistanceThisRow, distance);
            }

            if (minDistanceThisRow > maxDistance)
                return maxDistance + 1;
        }

        return distances[source.Length, target.Length];
    }

    private static double GetLengthBonus(int candidateLength, int queryLength)
    {
        if (candidateLength <= 0 || queryLength <= 0)
            return 0;

        var coverage = Math.Min(1d, (double)queryLength / candidateLength);
        return coverage * 65;
    }

    private readonly record struct RankedChip(StrataComposerChip Chip, double Score, int OriginalIndex);

    private readonly record struct PreparedSearchText(
        string Normalized,
        string Compact,
        string[] Tokens,
        string Initials)
    {
        public bool IsEmpty => Compact.Length == 0;

        public static PreparedSearchText Create(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new PreparedSearchText("", "", [], "");

            var tokens = new List<string>();
            var currentToken = new StringBuilder();
            var previous = '\0';

            void FlushToken()
            {
                if (currentToken.Length == 0)
                    return;

                tokens.Add(currentToken.ToString());
                currentToken.Clear();
            }

            foreach (var character in text)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    FlushToken();
                    previous = '\0';
                    continue;
                }

                var startsNewToken = currentToken.Length > 0
                                     && ((char.IsUpper(character) && char.IsLower(previous))
                                         || (char.IsDigit(character) != char.IsDigit(previous)));

                if (startsNewToken)
                    FlushToken();

                currentToken.Append(char.ToLowerInvariant(character));
                previous = character;
            }

            FlushToken();

            if (tokens.Count == 0)
                return new PreparedSearchText("", "", [], "");

            var initials = string.Concat(tokens.Select(static token => token[0]));
            var normalized = string.Join(' ', tokens);
            var compact = string.Concat(tokens);
            return new PreparedSearchText(normalized, compact, tokens.ToArray(), initials);
        }
    }
}
