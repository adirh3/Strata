using System;
using System.Globalization;
using System.Net;
using System.Text;
using Avalonia.Input;

namespace StrataTheme.Controls;

internal static class ChatClipboardData
{
    private static readonly DataFormat<string> MarkdownClipboardFormat =
        DataFormat.CreateStringPlatformFormat("text/markdown");
    private static readonly DataFormat<string> HtmlClipboardFormat =
        DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<byte[]> WindowsHtmlClipboardFormat =
        DataFormat.CreateBytesPlatformFormat("HTML Format");
    private static readonly DataFormat<string> MacHtmlClipboardFormat =
        DataFormat.CreateStringPlatformFormat("public.html");

    internal static DataTransfer CreateText(string text)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        return data;
    }

    internal static DataTransfer CreateMarkdown(string markdown)
    {
        var item = new DataTransferItem();
        item.SetText(markdown);
        item.Set(MarkdownClipboardFormat, markdown);
        return CreateTransfer(item);
    }

    internal static DataTransfer CreateRichText(string markdown)
        => CreateHtml(MarkdownClipboardFormatter.ToHtmlFragment(markdown), markdown);

    internal static DataTransfer CreateHtml(string htmlFragment, string plainText)
    {
        var item = new DataTransferItem();
        item.SetText(plainText);
        item.Set(HtmlClipboardFormat, htmlFragment);

        if (OperatingSystem.IsWindows())
            item.Set(WindowsHtmlClipboardFormat, BuildWindowsHtmlClipboardBytes(htmlFragment));
        else if (OperatingSystem.IsMacOS())
            item.Set(MacHtmlClipboardFormat, htmlFragment);

        return CreateTransfer(item);
    }

    internal static byte[] BuildWindowsHtmlClipboardBytes(string htmlFragment)
    {
        const string headerTemplate =
            "Version:1.0\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";
        const string htmlPrefix =
            "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
        const string htmlSuffix = "<!--EndFragment--></body></html>";

        var placeholderHeader = string.Format(
            CultureInfo.InvariantCulture,
            headerTemplate,
            0,
            0,
            0,
            0);
        var startHtml = Encoding.UTF8.GetByteCount(placeholderHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlPrefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(htmlFragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(htmlSuffix);
        var header = string.Format(
            CultureInfo.InvariantCulture,
            headerTemplate,
            startHtml,
            endHtml,
            startFragment,
            endFragment);

        return Encoding.UTF8.GetBytes(header + htmlPrefix + htmlFragment + htmlSuffix);
    }

    private static DataTransfer CreateTransfer(DataTransferItem item)
    {
        var data = new DataTransfer();
        data.Add(item);
        return data;
    }
}

internal static class MarkdownClipboardFormatter
{
    private const string TableCellStyle =
        "border:1px solid #d1d5db;padding:6px 10px;text-align:left;vertical-align:top;";

    internal static string ToHtmlFragment(string markdown)
    {
        var normalized = MarkdownParser.NormalizeLineEndings(markdown);
        var blocks = MarkdownParser.Parse(normalized);
        var builder = new StringBuilder(Math.Max(markdown.Length + 128, 256));
        var listKind = HtmlListKind.None;

        builder.Append(
            "<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;line-height:1.45;\">");

        foreach (var block in blocks)
        {
            var nextListKind = block.Kind switch
            {
                MdBlockKind.Bullet or MdBlockKind.TaskItem => HtmlListKind.Unordered,
                MdBlockKind.NumberedItem => HtmlListKind.Ordered,
                _ => HtmlListKind.None,
            };

            if (listKind != nextListKind)
            {
                CloseList(builder, listKind);
                OpenList(builder, nextListKind);
                listKind = nextListKind;
            }

            switch (block.Kind)
            {
                case MdBlockKind.Paragraph:
                    builder.Append("<p style=\"margin:0 0 10px 0;\">");
                    AppendInlineHtml(builder, block.Content);
                    builder.Append("</p>");
                    break;

                case MdBlockKind.Heading:
                    var headingLevel = Math.Clamp(block.Level, 1, 3);
                    builder.Append("<h").Append(headingLevel)
                        .Append(" style=\"margin:12px 0 6px 0;\">");
                    AppendInlineHtml(builder, block.Content);
                    builder.Append("</h").Append(headingLevel).Append('>');
                    break;

                case MdBlockKind.Bullet:
                    builder.Append("<li>");
                    AppendInlineHtml(builder, block.Content);
                    builder.Append("</li>");
                    break;

                case MdBlockKind.NumberedItem:
                    builder.Append("<li value=\"")
                        .Append(Math.Max(1, block.Level))
                        .Append("\">");
                    AppendInlineHtml(builder, block.Content);
                    builder.Append("</li>");
                    break;

                case MdBlockKind.TaskItem:
                    builder.Append("<li>")
                        .Append(block.Level == 1 ? "&#x2611; " : "&#x2610; ");
                    AppendInlineHtml(builder, block.Content);
                    builder.Append("</li>");
                    break;

                case MdBlockKind.Blockquote:
                    builder.Append(
                        "<blockquote style=\"margin:8px 0;padding-left:12px;border-left:3px solid #c8c8c8;\">");
                    AppendInlineHtml(builder, block.Content);
                    builder.Append("</blockquote>");
                    break;

                case MdBlockKind.HorizontalRule:
                    builder.Append("<hr style=\"border:0;border-top:1px solid #d1d5db;margin:12px 0;\" />");
                    break;

                case MdBlockKind.Table:
                    AppendTableHtml(builder, block.Content);
                    break;

                case MdBlockKind.Image:
                    AppendImageHtml(builder, block.Content, block.Language);
                    break;

                case MdBlockKind.CodeBlock:
                case MdBlockKind.Chart:
                case MdBlockKind.Mermaid:
                case MdBlockKind.Confidence:
                case MdBlockKind.Comparison:
                case MdBlockKind.Card:
                case MdBlockKind.Sources:
                    AppendCodeBlockHtml(builder, block.Content, block.Language);
                    break;
            }
        }

        CloseList(builder, listKind);
        builder.Append("</div>");
        return builder.ToString();
    }

    private static void OpenList(StringBuilder builder, HtmlListKind listKind)
    {
        if (listKind == HtmlListKind.Unordered)
            builder.Append("<ul style=\"margin:0 0 10px 22px;padding:0;\">");
        else if (listKind == HtmlListKind.Ordered)
            builder.Append("<ol style=\"margin:0 0 10px 22px;padding:0;\">");
    }

    private static void CloseList(StringBuilder builder, HtmlListKind listKind)
    {
        if (listKind == HtmlListKind.Unordered)
            builder.Append("</ul>");
        else if (listKind == HtmlListKind.Ordered)
            builder.Append("</ol>");
    }

    private static void AppendCodeBlockHtml(StringBuilder builder, string content, string language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            builder.Append("<div style=\"font-size:12px;font-weight:600;margin:8px 0 2px 0;\">")
                .Append(WebUtility.HtmlEncode(language))
                .Append("</div>");
        }

        builder.Append(
                "<pre style=\"margin:0 0 10px 0;padding:10px;background:#f3f3f3;border:1px solid #dedede;white-space:pre-wrap;\"><code>")
            .Append(WebUtility.HtmlEncode(content))
            .Append("</code></pre>");
    }

    private static void AppendImageHtml(StringBuilder builder, string altText, string target)
    {
        builder.Append("<p style=\"margin:0 0 10px 0;\">");
        if (TryGetSafeUri(target, out var uri))
        {
            builder.Append("<img src=\"")
                .Append(WebUtility.HtmlEncode(uri.AbsoluteUri))
                .Append("\" alt=\"")
                .Append(WebUtility.HtmlEncode(altText))
                .Append("\" style=\"max-width:100%;height:auto;\" />");
        }
        else
        {
            builder.Append("<em>Image: ")
                .Append(WebUtility.HtmlEncode(altText))
                .Append("</em>");
        }

        builder.Append("</p>");
    }

    private static void AppendTableHtml(StringBuilder builder, string tableContent)
    {
        var lines = tableContent.Split('\n');
        if (lines.Length == 0)
            return;

        var headers = MarkdownParser.TrimCells(MarkdownParser.SplitTableCells(lines[0]));
        if (headers.Length == 0)
            return;

        builder.Append("<table style=\"border-collapse:collapse;margin:4px 0 10px 0;\"><thead><tr>");
        foreach (var header in headers)
        {
            builder.Append("<th style=\"").Append(TableCellStyle).Append("\">");
            AppendInlineHtml(builder, header);
            builder.Append("</th>");
        }

        builder.Append("</tr></thead><tbody>");
        var dataStartIndex = lines.Length >= 2 && MarkdownParser.IsTableSeparator(lines[1]) ? 2 : 1;
        for (var rowIndex = dataStartIndex; rowIndex < lines.Length; rowIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[rowIndex]) || MarkdownParser.IsTableSeparator(lines[rowIndex]))
                continue;

            var cells = MarkdownParser.TrimCells(MarkdownParser.SplitTableCells(lines[rowIndex]));
            builder.Append("<tr>");
            for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
            {
                builder.Append("<td style=\"").Append(TableCellStyle).Append("\">");
                if (columnIndex < cells.Length)
                    AppendInlineHtml(builder, cells[columnIndex]);
                builder.Append("</td>");
            }
            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
    }

    private static void AppendInlineHtml(StringBuilder builder, string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var plainStart = index;
            while (index < text.Length && !IsInlineSpecial(text[index]))
                index++;

            if (index > plainStart)
                builder.Append(WebUtility.HtmlEncode(text[plainStart..index]));
            if (index >= text.Length)
                break;

            if (text[index] == '\n')
            {
                builder.Append("<br />");
                index++;
                continue;
            }

            if (text[index] == '`' && TryAppendCode(builder, text, ref index))
                continue;
            if (text[index] == '!' && TryAppendLink(builder, text, ref index, isImage: true))
                continue;
            if (text[index] == '[' && TryAppendLink(builder, text, ref index, isImage: false))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "***", "<strong><em>", "</em></strong>"))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "___", "<strong><em>", "</em></strong>"))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "**", "<strong>", "</strong>"))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "__", "<strong>", "</strong>"))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "~~", "<del>", "</del>"))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "*", "<em>", "</em>"))
                continue;
            if (TryAppendDelimited(builder, text, ref index, "_", "<em>", "</em>"))
                continue;

            builder.Append(WebUtility.HtmlEncode(text[index].ToString()));
            index++;
        }
    }

    private static bool TryAppendCode(StringBuilder builder, string text, ref int index)
    {
        var closing = text.IndexOf('`', index + 1);
        if (closing < 0)
            return false;

        builder.Append("<code>")
            .Append(WebUtility.HtmlEncode(text[(index + 1)..closing]))
            .Append("</code>");
        index = closing + 1;
        return true;
    }

    private static bool TryAppendDelimited(
        StringBuilder builder,
        string text,
        ref int index,
        string delimiter,
        string openingTag,
        string closingTag)
    {
        if (!text.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
            return false;

        var contentStart = index + delimiter.Length;
        var closing = text.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
        if (closing <= contentStart)
            return false;

        builder.Append(openingTag);
        AppendInlineHtml(builder, text[contentStart..closing]);
        builder.Append(closingTag);
        index = closing + delimiter.Length;
        return true;
    }

    private static bool TryAppendLink(StringBuilder builder, string text, ref int index, bool isImage)
    {
        var labelStart = index + (isImage ? 2 : 1);
        if (isImage && (index + 1 >= text.Length || text[index + 1] != '['))
            return false;

        var bracketClose = text.IndexOf("](", labelStart, StringComparison.Ordinal);
        if (bracketClose < 0)
            return false;

        var targetStart = bracketClose + 2;
        var parenClose = FindClosingParenthesis(text, targetStart);
        if (parenClose < 0)
            return false;

        var label = text[labelStart..bracketClose];
        var target = text[targetStart..parenClose].Trim();

        if (isImage)
        {
            if (TryGetSafeUri(target, out var imageUri))
            {
                builder.Append("<img src=\"")
                    .Append(WebUtility.HtmlEncode(imageUri.AbsoluteUri))
                    .Append("\" alt=\"")
                    .Append(WebUtility.HtmlEncode(label))
                    .Append("\" style=\"max-width:100%;height:auto;\" />");
            }
            else
            {
                builder.Append("<em>Image: ")
                    .Append(WebUtility.HtmlEncode(label))
                    .Append("</em>");
            }
        }
        else if (TryGetSafeUri(target, out var linkUri))
        {
            builder.Append("<a href=\"")
                .Append(WebUtility.HtmlEncode(linkUri.AbsoluteUri))
                .Append("\">");
            AppendInlineHtml(builder, label);
            builder.Append("</a>");
        }
        else
        {
            AppendInlineHtml(builder, label);
            if (!string.IsNullOrWhiteSpace(target))
            {
                builder.Append(" (")
                    .Append(WebUtility.HtmlEncode(target))
                    .Append(')');
            }
        }

        index = parenClose + 1;
        return true;
    }

    private static int FindClosingParenthesis(string text, int targetStart)
    {
        var depth = 0;
        for (var index = targetStart; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index++;
                continue;
            }

            if (text[index] == '(')
            {
                depth++;
            }
            else if (text[index] == ')')
            {
                if (depth == 0)
                    return index;
                depth--;
            }
        }

        return -1;
    }

    private static bool TryGetSafeUri(string target, out Uri uri)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out uri!) &&
            uri.Scheme is "http" or "https" or "mailto")
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsInlineSpecial(char value)
        => value is '\n' or '`' or '!' or '[' or '*' or '_' or '~';

    private enum HtmlListKind
    {
        None,
        Unordered,
        Ordered,
    }
}
