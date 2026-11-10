using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// Citations: the ways answers point at their sources drawn as small chips that open, preview and copy
/// like the links they are — a numbered link <c>[1](url)</c>, a site named in parentheses
/// <c>([Reuters](url))</c>, and a footnote reference <c>[^1]</c> whose definition (<c>[^1]: …</c>)
/// is gathered into a compact list of notes.
/// </summary>
public partial class StrataMarkdown
{
    private const double CitationFontScale = 0.8;
    private const int MaxCitationLabelLength = 40;
    private IBrush? _citationBrush;

    /// <summary>
    /// A citation chip. Like <see cref="InlineCodeRun"/> it stays a run, so it keeps the line's
    /// baseline, selection and copy; <see cref="InlineCodeLayer"/> draws its pill.
    /// </summary>
    internal sealed class CitationRun : Run
    {
        // Two quarter-em spaces each side (copy drops them) give one digit a near-round pill. A line may
        // break after such a space, so word joiners hold the label and the closing isolate to the
        // padding, and the label's own spaces don't break, keeping every chip whole on one line.
        public CitationRun(string label) : base($"\u2066\u2005\u2005\u2060{label.Replace(' ', '\u00A0')}\u2005\u2005\u2060\u2069")
        {
            Label = label;
        }

        public string Label { get; }

        /// <summary>The footnote this chip refers to, or the one it labels in the notes list.</summary>
        public string? FootnoteId { get; init; }

        /// <summary>The chip that starts a footnote's definition in the notes list, rather than a reference to it.</summary>
        public bool IsFootnoteLabel { get; init; }

        public IBrush? ChipBackground { get; init; }
    }

    /// <summary>
    /// A citation at <paramref name="pos"/>: <c>[^id]</c>, a link labelled with a number up to three
    /// digits, or a link that is all a pair of parentheses holds. The label is what its chip shows;
    /// a footnote has no target of its own, since its definition may still be streaming in.
    /// </summary>
    internal static bool TryMatchCitation(ReadOnlySpan<char> span, int pos, out int end, out string label,
        out string? target, out string? footnoteId)
    {
        end = pos;
        label = "";
        target = null;
        footnoteId = null;
        if (pos + 2 >= span.Length)
            return false;

        if (span[pos] == '[' && span[pos + 1] == '^')
        {
            var close = span[(pos + 2)..].IndexOf(']');
            if (close <= 0 || !MarkdownParser.IsFootnoteId(span.Slice(pos + 2, close)))
                return false;
            var after = pos + 2 + close + 1;
            if (after < span.Length && span[after] == '(')
                return false;
            footnoteId = span.Slice(pos + 2, close).ToString();
            label = footnoteId;
            end = after;
            return true;
        }

        var parenthesized = span[pos] == '(' && span[pos + 1] == '[';
        if (span[pos] != '[' && !parenthesized)
            return false;
        // Plain links are common in answers: rule them out from their first few characters, before
        // scanning for their end, so the link parser that follows doesn't pay for the scan twice.
        if (!parenthesized)
        {
            var window = span.Slice(pos + 1, Math.Min(4, span.Length - pos - 1));
            var labelEnd = window.IndexOf(']');
            if (labelEnd <= 0 || !IsAllDigits(window[..labelEnd]))
                return false;
        }
        var open = parenthesized ? pos + 1 : pos;
        var bracketClose = FindClosingBracket(span, open + 1);
        if (bracketClose < 0 || bracketClose + 1 >= span.Length || span[bracketClose + 1] != '(')
            return false;
        var parenClose = FindClosingParen(span, bracketClose + 2);
        if (parenClose < 0)
            return false;
        if (parenthesized && (parenClose + 1 >= span.Length || span[parenClose + 1] != ')'))
            return false;

        var text = span[(open + 1)..bracketClose].Trim();
        var isNumber = text.Length is > 0 and <= 3 && IsAllDigits(text);
        if (!isNumber && (!parenthesized || !IsPlainChipLabel(text)))
            return false;

        target = NormalizeLinkTarget(span[(bracketClose + 2)..parenClose].ToString());
        label = text.ToString();
        // A site cited by its own address reads better by name.
        if (!isNumber && Uri.TryCreate(label, UriKind.Absolute, out var labelUri) && MarkdownImageFiles.IsWebAddress(labelUri))
            label = LinkSiteLabel(labelUri, label);
        end = parenthesized ? parenClose + 2 : parenClose + 1;
        return true;

        static bool IsAllDigits(ReadOnlySpan<char> value)
        {
            foreach (var c in value)
            {
                if (!char.IsAsciiDigit(c))
                    return false;
            }
            return true;
        }

        // Formatting inside a parenthesized link is prose, not a source's name.
        static bool IsPlainChipLabel(ReadOnlySpan<char> value) =>
            value.Length is > 0 and <= MaxCitationLabelLength && value.IndexOfAny("[]`*_!\n") < 0;
    }

    /// <summary>A chip for a citation <see cref="TryMatchCitation"/> found, tracked as a link.</summary>
    private CitationRun CreateCitationRun(string label, string? target, string? footnoteId, double fontSize, bool isFootnoteLabel = false)
    {
        var chip = new CitationRun(label)
        {
            FontSize = fontSize * CitationFontScale,
            FontWeight = FontWeight.SemiBold,
            Foreground = _linkBrush ??= ResolveLinkBrush(),
            ChipBackground = _citationBrush ??= ResolveCitationBrush(),
            FootnoteId = footnoteId,
            IsFootnoteLabel = isFootnoteLabel,
        };
        // A reference is resolved when used (TryResolveLink); a definition's own label leads nowhere.
        if (!isFootnoteLabel)
            _linkRuns[chip] = target ?? string.Empty;
        return chip;
    }

    private static IBrush ResolveCitationBrush() =>
        TryGetApplicationResource("Brush.AccentSubtle", out var resource) && resource is IBrush brush
            ? brush
            : new SolidColorBrush(Color.FromArgb(40, 30, 144, 255));

    /// <summary>
    /// Where a link run leads: its own target, or for a footnote reference the first address in its
    /// definition. A footnote without a definition (yet) or without an address leads nowhere.
    /// </summary>
    private bool TryResolveLink(Run run, out string target)
    {
        if (run is CitationRun { FootnoteId: { } id, IsFootnoteLabel: false })
        {
            target = FootnoteTarget(id) ?? string.Empty;
            return target.Length > 0;
        }
        return _linkRuns.TryGetValue(run, out target!);
    }

    private string? FootnoteTarget(string id)
    {
        foreach (var block in _previousBlocks)
        {
            if (block.Kind == MdBlockKind.Footnote && string.Equals(block.Language, id, StringComparison.Ordinal))
                return FirstLinkTarget(block.Content);
        }
        return null;
    }

    /// <summary>The first address in a footnote's text: a markdown link's target, or a bare web address.</summary>
    internal static string? FirstLinkTarget(string text)
    {
        var span = text.AsSpan();
        for (var pos = 0; pos < span.Length; pos++)
        {
            var c = span[pos];
            if (c == '[')
            {
                var bracketClose = FindClosingBracket(span, pos + 1);
                if (bracketClose >= 0 && bracketClose + 1 < span.Length && span[bracketClose + 1] == '(')
                {
                    var parenClose = FindClosingParen(span, bracketClose + 2);
                    if (parenClose >= 0)
                        return NormalizeLinkTarget(span[(bracketClose + 2)..parenClose].ToString());
                }
            }
            else if (c is '<' or 'h' or 'H' && TryMatchAutolink(span, pos, out _, out var url))
                return url;
        }
        return null;
    }

    /// <summary>
    /// Consecutive footnote definitions as one compact list of notes: each its chip and text, smaller
    /// than the answer and set apart from it by a hairline.
    /// </summary>
    private Control CreateFootnotesControl(IReadOnlyList<MdBlock> blocks, int start, int count)
    {
        var fontSize = _bodyFontSize * 0.88;
        var lineHeight = fontSize * TextLineHeightRatio;
        var textBlock = new SelectableTextBlock
        {
            FontSize = fontSize,
            LineHeight = lineHeight,
            TextWrapping = TextWrapping.Wrap,
            ClipToBounds = false,
            Margin = ZeroThickness,
        };
        textBlock.Classes.Add("strata-md-footnotes");
        ApplyDirectionalTextLayout(textBlock, FlowDirection, blocks[start].Content);
        if (PopulateFootnoteInlines(textBlock, blocks, start, count))
            AttachLinkHandlers(textBlock);

        var section = new MarkdownBorder { Child = WrapWithCodeLayer(textBlock) };
        section.Classes.Add("strata-md-footnotes-section");
        return section;
    }

    /// <summary>
    /// Shows the notes again in the list already on screen, as streaming text updates a paragraph in
    /// place, instead of building the list anew for every token of its last note.
    /// </summary>
    private bool TryUpdateFootnotesInPlace(Control existing, IReadOnlyList<MdBlock> blocks, MdBlockGroup group)
    {
        if (FindSelectableTextBlock(existing) is not { } textBlock || !textBlock.Classes.Contains("strata-md-footnotes"))
            return false;

        RemoveLinkRunsForTextBlock(textBlock);
        textBlock.Text = null;
        textBlock.Inlines?.Clear();
        ApplyDirectionalTextLayout(textBlock, FlowDirection, blocks[group.StartIndex].Content);
        DetachLinkHandlers(textBlock);
        if (PopulateFootnoteInlines(textBlock, blocks, group.StartIndex, group.Count))
            AttachLinkHandlers(textBlock);
        (textBlock.Parent as Visual)?.InvalidateVisual();
        return true;
    }

    /// <summary>Each note's label chip and text, a line apart. True when any of them holds a link.</summary>
    private bool PopulateFootnoteInlines(SelectableTextBlock textBlock, IReadOnlyList<MdBlock> blocks, int start, int count)
    {
        var inlines = new InlineCollection();
        var hasLinks = false;
        for (var i = start; i < start + count; i++)
        {
            var block = blocks[i];
            if (i > start)
                inlines.Add(new LineBreak());
            inlines.Add(CreateCitationRun(block.Language, null, block.Language, textBlock.FontSize, isFootnoteLabel: true));
            hasLinks |= AppendFormattedInlines(textBlock, inlines, block.Content, " ", forceInlines: true);
        }
        ApplyFormattedInlines(textBlock, inlines, textBlock.FontSize * TextLineHeightRatio);
        return hasLinks;
    }
}
