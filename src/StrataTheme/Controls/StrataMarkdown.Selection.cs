using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

public partial class StrataMarkdown
{
    internal bool TryExportSelectedSpan(IReadOnlyList<StrataTextSelection.TextRange> ranges, out string markdown)
    {
        markdown = "";
        if (ranges.Count == 0 || _previousGroups.Count != _contentHost.Children.Count)
            return false;

        int GroupIndex(SelectableTextBlock block)
        {
            for (var index = 0; index < _contentHost.Children.Count; index++)
            {
                if (_contentHost.Children[index].IsVisualAncestorOf(block)
                    || ReferenceEquals(_contentHost.Children[index], block))
                    return index;
            }
            return -1;
        }

        var first = GroupIndex(ranges[0].Block);
        var last = GroupIndex(ranges[^1].Block);
        bool IsEmbedded(int index) => _previousGroups[index].Kind is MdBlockKind.Chart or MdBlockKind.Mermaid
            or MdBlockKind.Card or MdBlockKind.Comparison or MdBlockKind.Confidence or MdBlockKind.Interactive
            or MdBlockKind.Sources or MdBlockKind.Image;
        if (first < 0 || last <= first
            || !Enumerable.Range(first + 1, last - first - 1).Any(IsEmbedded))
            return false;

        var parts = new List<string>();
        for (var index = first; index <= last; index++)
        {
            if (index > first && index < last && IsEmbedded(index))
            {
                var group = _previousGroups[index];
                var block = _previousBlocks[group.StartIndex];
                var endIndex = group.StartIndex + group.Count;
                var end = endIndex < _previousBlocks.Count
                    ? _previousBlocks[endIndex].SourceStart : _previousMarkdownNormalized!.Length;
                var source = _previousMarkdownNormalized![block.SourceStart..end];
                parts.Add(MarkdownClipboardFormatter.ToPortableMarkdown(source, GetCopyInteractionStates()));
            }
            else
            {
                var control = _contentHost.Children[index];
                var selected = ranges.Where(range => ReferenceEquals(control, range.Block)
                    || control.IsVisualAncestorOf(range.Block)).ToArray();
                if (selected.Length > 0)
                    parts.Add(ChatClipboardData.SelectionMarkdown(selected, includeEmbeddedBlocks: false));
            }
        }
        markdown = string.Join("\n\n", parts.Select(part => part.TrimEnd()));
        return true;
    }
}
