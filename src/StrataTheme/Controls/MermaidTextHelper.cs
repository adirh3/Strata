using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace StrataTheme.Controls;

internal static class MermaidTextHelper
{
    private static readonly Regex BrTagRegex = new(
        @"<br\s*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BlockTagRegex = new(
        @"</?(?:div|p|section|article|header|footer|blockquote|ul|ol)\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ListItemOpenTagRegex = new(
        @"<li\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ListItemCloseTagRegex = new(
        @"</li>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InlineHtmlTagRegex = new(
        @"</?(?:a|b|strong|i|em|code|u|s|strike|del|mark|small|sub|sup|span|font)\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MarkdownLinkRegex = new(
        @"!?\[(?<text>[^\]]*)\]\((?<url>[^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownBoldItalicRegex = new(
        @"(?<!\\)\*\*\*(?<text>.+?)(?<!\\)\*\*\*",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownBoldRegex = new(
        @"(?<!\\)\*\*(?<text>.+?)(?<!\\)\*\*",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownItalicRegex = new(
        @"(?<!\\)\*(?<text>[^*\r\n]+?)(?<!\\)\*",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownStrikeRegex = new(
        @"(?<!\\)~~(?<text>.+?)(?<!\\)~~",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownCodeRegex = new(
        @"(?<!\\)`(?<text>[^`\r\n]+)`",
        RegexOptions.Compiled);

    private static readonly Regex SurroundingNewlineWhitespaceRegex = new(
        @"[ \t]*\n[ \t]*",
        RegexOptions.Compiled);

    private static readonly Regex MultipleNewlinesRegex = new(
        @"\n{2,}",
        RegexOptions.Compiled);

    internal static string NormalizeLabelText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim();
        normalized = UnwrapKnownWrappers(normalized);
        normalized = normalized.Replace("\\n", "\n");
        normalized = WebUtility.HtmlDecode(normalized);
        normalized = BrTagRegex.Replace(normalized, "\n");
        normalized = BlockTagRegex.Replace(normalized, "\n");
        normalized = ListItemOpenTagRegex.Replace(normalized, "- ");
        normalized = ListItemCloseTagRegex.Replace(normalized, "\n");
        normalized = InlineHtmlTagRegex.Replace(normalized, string.Empty);
        normalized = MarkdownLinkRegex.Replace(normalized, "${text}");
        normalized = MarkdownCodeRegex.Replace(normalized, "${text}");
        normalized = MarkdownBoldItalicRegex.Replace(normalized, "${text}");
        normalized = MarkdownBoldRegex.Replace(normalized, "${text}");
        normalized = MarkdownItalicRegex.Replace(normalized, "${text}");
        normalized = MarkdownStrikeRegex.Replace(normalized, "${text}");
        normalized = normalized
            .Replace("\\*", "*")
            .Replace("\\`", "`")
            .Replace("\\[", "[")
            .Replace("\\]", "]")
            .Replace("\\(", "(")
            .Replace("\\)", ")")
            .Replace("\\<", "<")
            .Replace("\\>", ">");
        normalized = SurroundingNewlineWhitespaceRegex.Replace(normalized, "\n");
        normalized = MultipleNewlinesRegex.Replace(normalized, "\n");

        return normalized.Trim();
    }

    internal static FlowDirection GetFlowDirection(string? text, FlowDirection fallback = FlowDirection.LeftToRight)
    {
        return StrataTextDirectionDetector.Detect(text) ?? fallback;
    }

    private static string UnwrapKnownWrappers(string text)
    {
        while (text.Length >= 2)
        {
            var first = text[0];
            var last = text[^1];
            if ((first == '"' && last == '"')
                || (first == '\'' && last == '\'')
                || (first == '`' && last == '`'))
            {
                text = text[1..^1].Trim();
                continue;
            }

            break;
        }

        return text;
    }
}
