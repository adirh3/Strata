using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StrataTheme.Diff;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

public sealed record DiffLine(DiffLineKind Kind, int? OldLineNumber, int? NewLineNumber, string Text);

public sealed record DiffHunk(string? Header, IReadOnlyList<DiffLine> Lines);

public sealed record DiffDocument(
    IReadOnlyList<DiffHunk> Hunks,
    int AddedLineCount,
    int RemovedLineCount,
    bool HasAccurateLineNumbers,
    string? EmptyStateText = null);

public static class UnifiedDiffBuilder
{
    private static readonly Regex HunkHeaderRegex = new(
        @"^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@",
        RegexOptions.Compiled);

    public static DiffDocument BuildFromSnapshots(string? originalContent, string? currentContent, int contextLineCount = 3)
    {
        var oldLines = SplitLines(originalContent);
        var newLines = SplitLines(currentContent);
        var diffLines = BuildDiffLines(oldLines, newLines, oldStartLine: 1, newStartLine: 1);
        var hunks = CollapseToHunks(diffLines, contextLineCount);

        if (hunks.Count == 0)
            return new DiffDocument([], 0, 0, HasAccurateLineNumbers: true, EmptyStateText: "No visible changes.");

        return new DiffDocument(
            hunks,
            AddedLineCount: diffLines.Count(static line => line.Kind == DiffLineKind.Added),
            RemovedLineCount: diffLines.Count(static line => line.Kind == DiffLineKind.Removed),
            HasAccurateLineNumbers: true);
    }

    public static DiffDocument BuildFromEdits(IReadOnlyList<(string? OldText, string? NewText)> edits, bool isCreate)
    {
        if (isCreate)
        {
            var createdContent = string.Join("\n", edits
                .Select(static edit => NormalizeLineEndings(edit.NewText ?? string.Empty))
                .Where(static text => !string.IsNullOrEmpty(text)));

            return BuildFromSnapshots(string.Empty, createdContent, contextLineCount: int.MaxValue);
        }

        if (edits.Count == 0)
            return new DiffDocument([], 0, 0, HasAccurateLineNumbers: false, EmptyStateText: "No diff information was captured for this change.");

        var hunks = new List<DiffHunk>(edits.Count);
        var added = 0;
        var removed = 0;

        for (var i = 0; i < edits.Count; i++)
        {
            var (oldText, newText) = edits[i];
            var diffLines = BuildDiffLines(SplitLines(oldText), SplitLines(newText), oldStartLine: 1, newStartLine: 1);
            if (diffLines.Count == 0)
                continue;

            added += diffLines.Count(static line => line.Kind == DiffLineKind.Added);
            removed += diffLines.Count(static line => line.Kind == DiffLineKind.Removed);

            var header = edits.Count > 1 ? $"Edit {i + 1}" : null;
            hunks.Add(new DiffHunk(header, diffLines));
        }

        if (hunks.Count == 0)
            return new DiffDocument([], 0, 0, HasAccurateLineNumbers: false, EmptyStateText: "No visible changes.");

        return new DiffDocument(hunks, added, removed, HasAccurateLineNumbers: false);
    }

    public static DiffDocument BuildFromUnifiedDiff(string? unifiedDiff)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
            return new DiffDocument([], 0, 0, HasAccurateLineNumbers: true, EmptyStateText: "Unable to load diff.");

        var hunks = new List<DiffHunk>();
        var added = 0;
        var removed = 0;

        var lines = NormalizeLineEndings(unifiedDiff).Split('\n');
        List<DiffLine>? currentLines = null;
        string? currentHeader = null;
        var oldLineNumber = 0;
        var newLineNumber = 0;

        foreach (var rawLine in lines)
        {
            if (rawLine.StartsWith("diff --git ", StringComparison.Ordinal)
                || rawLine is "Staged changes" or "Working tree changes")
            {
                FlushCurrentHunk(hunks, currentHeader, currentLines);
                currentHeader = null;
                currentLines = null;
                continue;
            }

            if (rawLine.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushCurrentHunk(hunks, currentHeader, currentLines);

                currentHeader = rawLine;
                currentLines = [];

                var match = HunkHeaderRegex.Match(rawLine);
                if (match.Success)
                {
                    oldLineNumber = int.Parse(match.Groups["oldStart"].Value);
                    newLineNumber = int.Parse(match.Groups["newStart"].Value);
                }
                else
                {
                    oldLineNumber = 0;
                    newLineNumber = 0;
                }

                continue;
            }

            if (currentLines is null)
                continue;

            if (rawLine.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
                continue;

            if (rawLine.StartsWith('+'))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Added, null, newLineNumber, rawLine[1..]));
                newLineNumber++;
                added++;
                continue;
            }

            if (rawLine.StartsWith('-'))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Removed, oldLineNumber, null, rawLine[1..]));
                oldLineNumber++;
                removed++;
                continue;
            }

            if (rawLine.StartsWith(' '))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Context, oldLineNumber, newLineNumber, rawLine[1..]));
                oldLineNumber++;
                newLineNumber++;
                continue;
            }

        }

        FlushCurrentHunk(hunks, currentHeader, currentLines);

        if (hunks.Count == 0)
            return new DiffDocument([], 0, 0, HasAccurateLineNumbers: true, EmptyStateText: unifiedDiff.Trim());

        return new DiffDocument(hunks, added, removed, HasAccurateLineNumbers: true);
    }

    private static void FlushCurrentHunk(List<DiffHunk> hunks, string? header, List<DiffLine>? lines)
    {
        if (lines is not { Count: > 0 })
            return;

        hunks.Add(new DiffHunk(header, lines));
    }

    private static List<DiffHunk> CollapseToHunks(IReadOnlyList<DiffLine> lines, int contextLineCount)
    {
        if (lines.Count == 0)
            return [];

        if (contextLineCount >= lines.Count)
            return [new DiffHunk(BuildHunkHeader(lines), lines.ToList())];

        var changeIndexes = lines
            .Select(static (line, index) => (line, index))
            .Where(static entry => entry.line.Kind != DiffLineKind.Context)
            .Select(static entry => entry.index)
            .ToList();

        if (changeIndexes.Count == 0)
            return [];

        var hunks = new List<DiffHunk>();
        var start = Math.Max(0, changeIndexes[0] - contextLineCount);
        var end = Math.Min(lines.Count - 1, changeIndexes[0] + contextLineCount);

        for (var i = 1; i < changeIndexes.Count; i++)
        {
            var index = changeIndexes[i];
            var nextStart = Math.Max(0, index - contextLineCount);
            var nextEnd = Math.Min(lines.Count - 1, index + contextLineCount);

            if (nextStart <= end + 1)
            {
                end = Math.Max(end, nextEnd);
                continue;
            }

            hunks.Add(CreateHunk(lines, start, end));
            start = nextStart;
            end = nextEnd;
        }

        hunks.Add(CreateHunk(lines, start, end));
        return hunks;
    }

    private static DiffHunk CreateHunk(IReadOnlyList<DiffLine> lines, int start, int end)
    {
        var slice = lines.Skip(start).Take(end - start + 1).ToList();
        return new DiffHunk(BuildHunkHeader(slice), slice);
    }

    private static string BuildHunkHeader(IReadOnlyList<DiffLine> lines)
    {
        var first = lines[0];
        var oldNumbers = lines.Where(static line => line.OldLineNumber.HasValue).Select(static line => line.OldLineNumber!.Value).ToList();
        var newNumbers = lines.Where(static line => line.NewLineNumber.HasValue).Select(static line => line.NewLineNumber!.Value).ToList();

        var oldStart = first.OldLineNumber ?? Math.Max(0, (first.NewLineNumber ?? 1) - 1);
        var newStart = first.NewLineNumber ?? Math.Max(0, (first.OldLineNumber ?? 1) - 1);

        return $"@@ -{oldStart},{oldNumbers.Count} +{newStart},{newNumbers.Count} @@";
    }

    private static List<DiffLine> BuildDiffLines(string[] oldLines, string[] newLines, int oldStartLine, int newStartLine)
    {
        var matches = BuildMatches(oldLines, newLines);
        var result = new List<DiffLine>(oldLines.Length + newLines.Length);

        var oldIndex = 0;
        var newIndex = 0;
        var matchIndex = 0;
        var oldLineNumber = oldStartLine;
        var newLineNumber = newStartLine;

        while (oldIndex < oldLines.Length || newIndex < newLines.Length)
        {
            if (matchIndex < matches.Count && oldIndex == matches[matchIndex].OldIndex && newIndex == matches[matchIndex].NewIndex)
            {
                result.Add(new DiffLine(DiffLineKind.Context, oldLineNumber, newLineNumber, newLines[newIndex]));
                oldIndex++;
                newIndex++;
                oldLineNumber++;
                newLineNumber++;
                matchIndex++;
                continue;
            }

            var nextOldMatch = matchIndex < matches.Count ? matches[matchIndex].OldIndex : oldLines.Length;
            var nextNewMatch = matchIndex < matches.Count ? matches[matchIndex].NewIndex : newLines.Length;

            while (oldIndex < nextOldMatch)
            {
                result.Add(new DiffLine(DiffLineKind.Removed, oldLineNumber, null, oldLines[oldIndex]));
                oldIndex++;
                oldLineNumber++;
            }

            while (newIndex < nextNewMatch)
            {
                result.Add(new DiffLine(DiffLineKind.Added, null, newLineNumber, newLines[newIndex]));
                newIndex++;
                newLineNumber++;
            }
        }

        return result;
    }

    private static List<(int OldIndex, int NewIndex)> BuildMatches(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        var m = oldLines.Count;
        var n = newLines.Count;
        var lcs = new int[m + 1, n + 1];

        for (var i = m - 1; i >= 0; i--)
            for (var j = n - 1; j >= 0; j--)
                lcs[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var matches = new List<(int OldIndex, int NewIndex)>();
        var oldIndex = 0;
        var newIndex = 0;

        while (oldIndex < m && newIndex < n)
        {
            if (string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal))
            {
                matches.Add((oldIndex, newIndex));
                oldIndex++;
                newIndex++;
                continue;
            }

            if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
                oldIndex++;
            else
                newIndex++;
        }

        return matches;
    }

    private static string[] SplitLines(string? text)
    {
        var normalized = NormalizeLineEndings(text ?? string.Empty);
        if (string.IsNullOrEmpty(normalized))
            return [];

        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            Array.Resize(ref lines, lines.Length - 1);
        return lines;
    }

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
