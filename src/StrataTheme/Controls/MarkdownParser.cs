using System;
using System.Collections.Generic;
using System.Text;

namespace StrataTheme.Controls;

/// <summary>Kind of markdown block produced by <see cref="MarkdownParser"/>.</summary>
public enum MdBlockKind
{
    Paragraph,
    Heading,
    Bullet,
    NumberedItem,
    CodeBlock,
    HorizontalRule,
    Table,
    Chart,
    Mermaid,
    Confidence,
    Comparison,
    Card,
    Sources,
    Blockquote,
}

/// <summary>
/// Lightweight value type representing a single parsed markdown block.
/// Used by <see cref="MarkdownParser"/> and consumed by <see cref="StrataMarkdown"/> for rendering.
/// </summary>
public readonly struct MdBlock : IEquatable<MdBlock>
{
    public MdBlockKind Kind { get; init; }
    public string Content { get; init; }
    public int Level { get; init; }       // heading level, indent, item number
    public string Language { get; init; }  // code block language
    public int SourceStart { get; init; }

    public bool Equals(MdBlock other) =>
        Kind == other.Kind && Level == other.Level &&
        string.Equals(Content, other.Content, StringComparison.Ordinal) &&
        string.Equals(Language, other.Language, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MdBlock b && Equals(b);
    public override int GetHashCode() => HashCode.Combine(Kind, Content, Level, Language);
}

/// <summary>
/// Pure markdown parser that converts markdown source text into a flat list of
/// <see cref="MdBlock"/> descriptors. Zero Avalonia dependencies. Supports headings,
/// bullets, numbered items, fenced code blocks, tables, and horizontal rules.
/// </summary>
/// <remarks>
/// Static methods are allocation-friendly (new buffers per call). For streaming
/// scenarios requiring zero-allocation incremental parsing, create an instance
/// to reuse internal <see cref="StringBuilder"/> buffers across calls via
/// <see cref="ParsePooled"/> and <see cref="ParseIncrementalAppendPooled"/>.
/// </remarks>
public sealed class MarkdownParser
{
    private readonly StringBuilder _paragraphBuffer = new();
    private readonly StringBuilder _codeBuffer = new();
    private readonly StringBuilder _blockquoteBuffer = new();
    private readonly List<string> _tableBuffer = new();

    // ── Static API ──────────────────────────────────────────────

    /// <summary>Parses markdown text into block descriptors. Allocates fresh buffers.</summary>
    public static List<MdBlock> Parse(string normalized)
    {
        var blocks = new List<MdBlock>();
        ParseCore(normalized.AsSpan(), blocks, new StringBuilder(), new StringBuilder(), new StringBuilder(), new List<string>(), 0);
        return blocks;
    }

    /// <summary>
    /// Incrementally parses appended markdown. Reuses previously parsed blocks for the
    /// unchanged prefix and only re-parses from the last block's start position onward.
    /// </summary>
    public static List<MdBlock> ParseIncrementalAppend(
        string previousNormalized,
        IReadOnlyList<MdBlock> previousBlocks,
        string nextNormalized)
    {
        if (previousBlocks.Count == 0 ||
            string.IsNullOrEmpty(previousNormalized) ||
            nextNormalized.Length <= previousNormalized.Length ||
            !nextNormalized.AsSpan().StartsWith(previousNormalized.AsSpan()))
        {
            return Parse(nextNormalized);
        }

        var lastBlockStart = previousBlocks[^1].SourceStart;
        if (lastBlockStart < 0 || lastBlockStart > nextNormalized.Length)
            return Parse(nextNormalized);

        var merged = new List<MdBlock>(Math.Max(previousBlocks.Count + 8, 16));
        for (var i = 0; i < previousBlocks.Count - 1; i++)
            merged.Add(previousBlocks[i]);

        ParseCore(
            nextNormalized.AsSpan(lastBlockStart),
            merged,
            new StringBuilder(),
            new StringBuilder(),
            new StringBuilder(),
            new List<string>(),
            lastBlockStart);

        return merged;
    }

    // ── Instance (pooled) API ───────────────────────────────────

    /// <summary>Parses markdown, reusing internal buffers for zero-allocation streaming.</summary>
    public void ParsePooled(string normalized, List<MdBlock> target)
    {
        target.Clear();
        _paragraphBuffer.Clear();
        _codeBuffer.Clear();
        _blockquoteBuffer.Clear();
        _tableBuffer.Clear();
        ParseCore(normalized.AsSpan(), target, _paragraphBuffer, _codeBuffer, _blockquoteBuffer, _tableBuffer, 0);
    }

    /// <summary>
    /// Incrementally parses appended markdown, reusing internal buffers.
    /// </summary>
    public void ParseIncrementalAppendPooled(
        string previousNormalized,
        IReadOnlyList<MdBlock> previousBlocks,
        string nextNormalized,
        List<MdBlock> target)
    {
        if (previousBlocks.Count == 0 ||
            string.IsNullOrEmpty(previousNormalized) ||
            nextNormalized.Length <= previousNormalized.Length ||
            !nextNormalized.AsSpan().StartsWith(previousNormalized.AsSpan()))
        {
            ParsePooled(nextNormalized, target);
            return;
        }

        var lastBlockStart = previousBlocks[^1].SourceStart;
        if (lastBlockStart < 0 || lastBlockStart > nextNormalized.Length)
        {
            ParsePooled(nextNormalized, target);
            return;
        }

        target.Clear();
        var neededCapacity = Math.Max(previousBlocks.Count + 8, 16);
        if (target.Capacity < neededCapacity)
            target.Capacity = neededCapacity;
        for (var i = 0; i < previousBlocks.Count - 1; i++)
            target.Add(previousBlocks[i]);

        _paragraphBuffer.Clear();
        _codeBuffer.Clear();
        _blockquoteBuffer.Clear();
        _tableBuffer.Clear();
        ParseCore(
            nextNormalized.AsSpan(lastBlockStart),
            target,
            _paragraphBuffer,
            _codeBuffer,
            _blockquoteBuffer,
            _tableBuffer,
            lastBlockStart);
    }

    // ── Core parser ─────────────────────────────────────────────

    /// <summary>
    /// Core line-by-line parser. Enumerates lines from <paramref name="source"/> via span
    /// slicing and appends parsed block descriptors to <paramref name="blocks"/>.
    /// </summary>
    internal static void ParseCore(
        ReadOnlySpan<char> source, List<MdBlock> blocks,
        StringBuilder paragraphBuffer, StringBuilder codeBuffer, StringBuilder blockquoteBuffer, List<string> tableBuffer,
        int baseOffset)
    {
        paragraphBuffer.Clear();
        codeBuffer.Clear();
        blockquoteBuffer.Clear();
        tableBuffer.Clear();
        var inCodeBlock = false;
        var codeLanguage = string.Empty;

        var paragraphStart = -1;
        var codeStart = -1;
        var tableStart = -1;
        var blockquoteStart = -1;

        // Enumerate lines using span slicing — no Split allocation
        var remaining = source;
        var localOffset = 0;
        while (remaining.Length > 0)
        {
            int nlPos = remaining.IndexOf('\n');
            ReadOnlySpan<char> lineSpan;
            int consumedLength;
            if (nlPos >= 0)
            {
                lineSpan = remaining[..nlPos];
                consumedLength = nlPos + 1;
                remaining = remaining[consumedLength..];
            }
            else
            {
                lineSpan = remaining;
                consumedLength = remaining.Length;
                remaining = ReadOnlySpan<char>.Empty;
            }

            var absoluteLineStart = baseOffset + localOffset;
            localOffset += consumedLength;

            // Trim trailing \r if present
            if (lineSpan.Length > 0 && lineSpan[^1] == '\r')
                lineSpan = lineSpan[..^1];

            // Check for fenced code blocks — trim leading whitespace so indented fences
            // (e.g. inside list items) are recognized
            var trimmedSpan = lineSpan.TrimStart();
            if (trimmedSpan.Length >= 3 && trimmedSpan[0] == '`' && trimmedSpan[1] == '`' && trimmedSpan[2] == '`')
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                FlushTableBlock(tableBuffer, blocks, tableStart);
                tableStart = -1;

                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLanguage = trimmedSpan.Length > 3 ? trimmedSpan[3..].Trim().ToString() : string.Empty;
                    codeBuffer.Clear();
                    codeStart = absoluteLineStart;
                }
                else
                {
                    var code = codeBuffer.ToString();
                    var kind = MapCodeLanguageToBlockKind(codeLanguage);
                    blocks.Add(new MdBlock
                    {
                        Kind = kind,
                        Content = code,
                        Language = codeLanguage,
                        SourceStart = codeStart >= 0 ? codeStart : absoluteLineStart,
                    });
                    inCodeBlock = false;
                    codeLanguage = string.Empty;
                    codeBuffer.Clear();
                    codeStart = -1;
                }
                continue;
            }

            if (inCodeBlock)
            {
                if (codeBuffer.Length > 0)
                    codeBuffer.Append('\n');
                codeBuffer.Append(lineSpan);
                continue;
            }

            if (IsTableLine(lineSpan))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                if (tableBuffer.Count == 0)
                    tableStart = absoluteLineStart;
                tableBuffer.Add(lineSpan.ToString());
                continue;
            }

            // During streaming, a partial table line (starts with | but missing
            // closing pipe) should continue the table rather than flush it.
            if (tableBuffer.Count > 0 && StartsWithPipe(lineSpan))
            {
                tableBuffer.Add(lineSpan.ToString());
                continue;
            }

            if (tableBuffer.Count > 0)
            {
                FlushTableBlock(tableBuffer, blocks, tableStart);
                tableStart = -1;
            }

            // Blockquote: lines starting with > (optionally followed by space)
            if (TryParseBlockquote(lineSpan, out var quoteText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                if (blockquoteStart < 0)
                    blockquoteStart = absoluteLineStart;
                if (blockquoteBuffer.Length > 0)
                    blockquoteBuffer.Append('\n');
                blockquoteBuffer.Append(quoteText);
                continue;
            }

            FlushBlockquoteBlock(blockquoteBuffer, blocks, blockquoteStart);
            blockquoteStart = -1;

            if (TryParseHeading(lineSpan, out var level, out var headingText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.Heading,
                    Content = headingText,
                    Level = level,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            // HR must be checked before bullet: "- - -" is a valid HR that also matches
            // the bullet prefix ("- "). IsHorizontalRule rejects lines with mixed chars
            // or non-rule content, so genuine bullets like "- Item" fall through safely.
            if (IsHorizontalRule(lineSpan))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.HorizontalRule,
                    Content = string.Empty,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            if (TryParseBullet(lineSpan, out var bulletText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                var indentLevel = GetIndentLevel(lineSpan);
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.Bullet,
                    Content = bulletText,
                    Level = indentLevel,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            if (TryParseNumberedItem(lineSpan, out var number, out var numText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.NumberedItem,
                    Content = numText,
                    Level = number,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            if (lineSpan.IsWhiteSpace())
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                continue;
            }

            if (paragraphStart < 0)
                paragraphStart = absoluteLineStart;
            if (paragraphBuffer.Length > 0)
                paragraphBuffer.Append('\n');
            paragraphBuffer.Append(lineSpan);
        }

        var endOffset = baseOffset + source.Length;

        // Handle unclosed code block
        if (inCodeBlock)
        {
            var code = codeBuffer.ToString();
            var kind = MapCodeLanguageToBlockKind(codeLanguage);
            blocks.Add(new MdBlock
            {
                Kind = kind,
                Content = code,
                Language = codeLanguage,
                SourceStart = codeStart >= 0 ? codeStart : endOffset,
            });
        }

        FlushTableBlock(tableBuffer, blocks, tableStart);
        FlushBlockquoteBlock(blockquoteBuffer, blocks, blockquoteStart);
        FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, endOffset);
    }

    // ── Block detection helpers ─────────────────────────────────

    internal static MdBlockKind MapCodeLanguageToBlockKind(string language)
    {
        if (language.Equals("chart", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Chart;
        if (language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Mermaid;
        if (language.Equals("confidence", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Confidence;
        if (language.Equals("comparison", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Comparison;
        if (language.Equals("card", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Card;
        if (language.Equals("sources", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Sources;
        return MdBlockKind.CodeBlock;
    }

    internal static bool TryParseHeading(ReadOnlySpan<char> line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#')
            return false;

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
            i++;

        if (i is < 1 or > 6)
            return false;

        if (i >= trimmed.Length || trimmed[i] != ' ')
            return false;

        level = Math.Min(i, 3);
        text = trimmed[(i + 1)..].Trim().ToString();
        return text.Length > 0;
    }

    internal static bool TryParseBullet(ReadOnlySpan<char> line, out string text)
    {
        text = string.Empty;
        var trimmed = line.TrimStart();

        if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*') && trimmed[1] == ' ')
        {
            text = trimmed[2..].Trim().ToString();
            return text.Length > 0;
        }

        return false;
    }

    internal static bool TryParseNumberedItem(ReadOnlySpan<char> line, out int number, out string text)
    {
        number = 0;
        text = string.Empty;
        var trimmed = line.TrimStart();

        // Look for "N. " pattern (1-3 digit number followed by ". ")
        var dotIndex = trimmed.IndexOf(stackalloc char[] { '.', ' ' });
        if (dotIndex is < 1 or > 3) return false;

        // Verify digits before the dot
        var numSpan = trimmed[..dotIndex];
        number = 0;
        foreach (var c in numSpan)
        {
            if (c < '0' || c > '9') return false;
            number = number * 10 + (c - '0');
        }

        if (dotIndex + 2 > trimmed.Length) return false;
        text = trimmed[(dotIndex + 2)..].Trim().ToString();
        return text.Length > 0;
    }

    internal static bool IsHorizontalRule(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3) return false;

        var first = trimmed[0];
        if (first is not ('-' or '*' or '_')) return false;

        // Count rule chars, skipping spaces
        var ruleChars = 0;
        foreach (var c in trimmed)
        {
            if (c == first) ruleChars++;
            else if (c != ' ') return false;
        }
        return ruleChars >= 3;
    }

    internal static int GetIndentLevel(ReadOnlySpan<char> line)
    {
        var spaces = 0;
        foreach (var c in line)
        {
            if (c == ' ') spaces++;
            else if (c == '\t') spaces += 4;
            else break;
        }
        return Math.Min(spaces / 2, 3);
    }

    /// <summary>
    /// Returns true if the line is a blockquote (starts with '>' optionally followed by a space).
    /// Outputs the text content after stripping the '>' prefix.
    /// </summary>
    internal static bool TryParseBlockquote(ReadOnlySpan<char> line, out ReadOnlySpan<char> text)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length >= 1 && trimmed[0] == '>')
        {
            // Strip '>' and optional single space after it
            text = trimmed.Length > 1 && trimmed[1] == ' '
                ? trimmed[2..]
                : trimmed[1..];
            return true;
        }
        text = default;
        return false;
    }

    internal static bool IsTableLine(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 1 && trimmed[0] == '|' && trimmed[1..].IndexOf('|') >= 0;
    }

    /// <summary>
    /// Returns true when a line starts with a pipe character.
    /// Used to keep partial table rows (missing the closing pipe during streaming)
    /// inside the table buffer instead of flushing the table prematurely.
    /// </summary>
    internal static bool StartsWithPipe(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '|';
    }

    internal static bool IsTableSeparator(string line)
    {
        var span = line.AsSpan().Trim();
        if (span.Length == 0) return false;

        // Strip leading/trailing pipes
        if (span[0] == '|') span = span[1..];
        if (span.Length > 0 && span[^1] == '|') span = span[..^1];
        if (span.Length == 0) return false;

        // Check each cell separated by | matches :?-+:? pattern
        int cellCount = 0;
        while (span.Length > 0)
        {
            int pipeIdx = span.IndexOf('|');
            var cell = pipeIdx >= 0 ? span[..pipeIdx] : span;
            cell = cell.Trim();

            if (!IsTableSeparatorCell(cell))
                return false;

            cellCount++;
            span = pipeIdx >= 0 ? span[(pipeIdx + 1)..] : ReadOnlySpan<char>.Empty;
        }

        return cellCount > 0;
    }

    /// <summary>
    /// Checks if a cell matches the table separator pattern :?-+:? without regex allocation.
    /// </summary>
    internal static bool IsTableSeparatorCell(ReadOnlySpan<char> cell)
    {
        if (cell.Length == 0) return false;
        int i = 0;
        if (cell[i] == ':') i++;
        int dashStart = i;
        while (i < cell.Length && cell[i] == '-') i++;
        if (i == dashStart) return false; // no dashes
        if (i < cell.Length && cell[i] == ':') i++;
        return i == cell.Length;
    }

    // ── Table utility helpers ───────────────────────────────────

    /// <summary>Cache key for a table block: the trimmed header row.</summary>
    internal static string GetTableCacheKey(string tableContent)
    {
        var nl = tableContent.IndexOf('\n');
        return (nl >= 0 ? tableContent[..nl] : tableContent).Trim();
    }

    internal static string[] SplitTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|');
    }

    /// <summary>Trims each cell string in-place, avoiding LINQ Select/ToArray allocation.</summary>
    internal static string[] TrimCells(string[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
            cells[i] = cells[i].Trim();
        return cells;
    }

    /// <summary>Counts occurrences of a character in a span without LINQ allocation.</summary>
    internal static int CountChar(ReadOnlySpan<char> span, char c)
    {
        int count = 0;
        for (int i = 0; i < span.Length; i++)
            if (span[i] == c) count++;
        return count;
    }

    /// <summary>Converts rows (string[][]) to List&lt;List&lt;string&gt;&gt; without LINQ closure allocations.</summary>
    internal static List<List<string>> RowsToListOfLists(List<string[]> rows)
    {
        var result = new List<List<string>>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
            result.Add(new List<string>(rows[i]));
        return result;
    }

    // ── Normalization ───────────────────────────────────────────

    /// <summary>Normalizes line endings from CRLF to LF.</summary>
    public static string NormalizeLineEndings(string source)
    {
        return source.IndexOf('\r') >= 0
            ? source.Replace("\r\n", "\n")
            : source;
    }

    // ── Flush helpers ───────────────────────────────────────────

    private static void FlushParagraphBlock(StringBuilder buffer, List<MdBlock> blocks, int paragraphStart, int fallbackStart)
    {
        if (buffer.Length == 0)
            return;

        // Compute trim bounds on the StringBuilder directly to produce a single
        // string allocation instead of the two from buffer.ToString() + .Trim().
        int start = 0;
        while (start < buffer.Length && char.IsWhiteSpace(buffer[start])) start++;
        if (start >= buffer.Length)
        {
            buffer.Clear();
            return;
        }
        int end = buffer.Length;
        while (end > start && char.IsWhiteSpace(buffer[end - 1])) end--;

        var text = (start == 0 && end == buffer.Length)
            ? buffer.ToString()
            : buffer.ToString(start, end - start);
        buffer.Clear();

        blocks.Add(new MdBlock
        {
            Kind = MdBlockKind.Paragraph,
            Content = text,
            SourceStart = paragraphStart >= 0 ? paragraphStart : fallbackStart,
        });
    }

    private static void FlushBlockquoteBlock(StringBuilder buffer, List<MdBlock> blocks, int blockquoteStart)
    {
        if (buffer.Length == 0)
            return;

        var text = buffer.ToString().Trim();
        buffer.Clear();

        if (text.Length == 0)
            return;

        blocks.Add(new MdBlock
        {
            Kind = MdBlockKind.Blockquote,
            Content = text,
            SourceStart = blockquoteStart >= 0 ? blockquoteStart : 0,
        });
    }

    private static void FlushTableBlock(List<string> tableLines, List<MdBlock> blocks, int tableStart)
    {
        if (tableLines.Count == 0) return;
        blocks.Add(new MdBlock
        {
            Kind = MdBlockKind.Table,
            Content = string.Join("\n", tableLines),
            SourceStart = tableStart,
        });
        tableLines.Clear();
    }
}
