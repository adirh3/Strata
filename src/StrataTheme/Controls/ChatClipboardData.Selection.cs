using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

internal static partial class ChatClipboardData
{
    internal static string SelectionMarkdown(
        IReadOnlyList<StrataTextSelection.TextRange> ranges, bool includeEmbeddedBlocks = true)
    {
        if (includeEmbeddedBlocks)
        {
            var spans = new List<string>();
            for (var start = 0; start < ranges.Count;)
            {
                var owner = ranges[start].Block.GetVisualAncestors().OfType<StrataMarkdown>().LastOrDefault();
                var end = start + 1;
                while (end < ranges.Count && ReferenceEquals(owner,
                    ranges[end].Block.GetVisualAncestors().OfType<StrataMarkdown>().LastOrDefault()))
                    end++;
                var span = ranges.Skip(start).Take(end - start).ToArray();
                spans.Add(owner is not null && owner.TryExportSelectedSpan(span, out var portable)
                    ? portable : SelectionMarkdown(span, includeEmbeddedBlocks: false));
                start = end;
            }
            return string.Join("\n\n", spans);
        }
        var parts = new List<string>();
        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            if (StrataMarkdown.GetSelectionTableCell(range.Block) is { } firstCell)
            {
                var end = index;
                var cells = new List<(int Row, int Column, StrataTextSelection.TextRange Range)>();
                while (end < ranges.Count && StrataMarkdown.GetSelectionTableCell(ranges[end].Block) is { } cell
                    && ReferenceEquals(cell.Table, firstCell.Table))
                {
                    cells.Add((cell.Row, cell.Column, ranges[end]));
                    end++;
                }
                if (cells.Count > 1)
                {
                    var columns = cells.Select(cell => cell.Column).Distinct().Order().ToArray();
                    var table = new StringBuilder();
                    MarkdownClipboardFormatter.AppendRow(table, columns.Select(column =>
                        cells.FirstOrDefault(cell => cell.Row == 0 && cell.Column == column) is { Range.Block: not null } header
                            ? RangeMarkdown(header.Range) : firstCell.Headers[column]));
                    MarkdownClipboardFormatter.AppendRow(table, columns.Select(_ => "---"));
                    foreach (var row in cells.Where(cell => cell.Row > 0).GroupBy(cell => cell.Row))
                        MarkdownClipboardFormatter.AppendRow(table, columns.Select(column =>
                            row.FirstOrDefault(cell => cell.Column == column) is { Range.Block: not null } value
                                ? RangeMarkdown(value.Range) : ""));
                    parts.Add(table.ToString().TrimEnd());
                    index = end - 1;
                    continue;
                }
            }
            parts.Add(RangeMarkdown(range));
        }
        return string.Join("\n\n", parts);
    }

    private static string RangeMarkdown(StrataTextSelection.TextRange range)
    {
        if (range.Block.FindAncestorOfType<StrataCodeBlock>() is { } code)
        {
            var fence = range.Text.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            return $"{fence}{code.Language}\n{range.Text}\n{fence}";
        }
        if (range.Block.Inlines is not { Count: > 0 } inlines)
            return EscapeSelection(range.Text);

        var result = new StringBuilder();
        var start = Math.Min(range.Start, range.End);
        var end = Math.Max(range.Start, range.End);
        var offset = 0;
        var markdown = range.Block.FindAncestorOfType<StrataMarkdown>();
        foreach (var inline in inlines)
        {
            var text = inline switch
            {
                Run run => run.Text ?? "",
                LineBreak => Environment.NewLine,
                _ => "\uFFFC"
            };
            var from = Math.Max(0, start - offset);
            var to = Math.Min(text.Length, end - offset);
            offset += text.Length;
            if (to <= from)
                continue;
            var selected = StrataTextSelection.CleanSelectionText(text[from..to]);
            if (selected.Length == 0)
                continue;
            if (inline is StrataMarkdown.InlineCodeRun)
                selected = "`" + selected + "`";
            else
            {
                selected = EscapeSelection(selected);
                if (inline.FontWeight >= FontWeight.SemiBold)
                    selected = "**" + selected + "**";
                if (inline.FontStyle == FontStyle.Italic)
                    selected = "*" + selected + "*";
                if (inline.TextDecorations?.Contains(TextDecorations.Strikethrough[0]) == true)
                    selected = "~~" + selected + "~~";
            }
            if (inline is Run link && markdown?.GetLinkTarget(link) is { } url)
                selected = $"[{selected}]({url.Replace("(", "%28").Replace(")", "%29")})";
            result.Append(selected);
        }
        var part = result.ToString();
        part = Regex.Replace(part, @"(?m)^(\s*)• ", "$1- ");
        part = Regex.Replace(part, @"(?m)^(\s*)☐ ", "$1- [ ] ");
        part = Regex.Replace(part, @"(?m)^(\s*)☑ ", "$1- [x] ");
        return part;
    }

    private static string EscapeSelection(string text) => Regex.Replace(text, @"([`*_\[\]<>#~])", @"\$1");
}
