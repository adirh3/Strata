using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Avalonia.Input;
using Avalonia.Logging;
using ReverseMarkdown;
using ReverseMarkdown.Preprocessing;

namespace StrataTheme.Controls;

internal static class ChatClipboardText
{
    private static readonly string[] MarkdownFormats = ["text/markdown", "text/x-markdown", "net.daringfireball.markdown"];
    private static readonly string[] HtmlFormats = ["HTML Format", "text/html", "public.html"];
    private static readonly string[] RtfFormats = ["Rich Text Format", "text/rtf", "application/rtf", "public.rtf"];

    internal static async Task<string?> ReadAsync(IAsyncDataTransfer data, string? plainText)
    {
        foreach (var format in MarkdownFormats)
        {
            var markdown = await ReadFormatAsync(data, format);
            if (!string.IsNullOrEmpty(markdown))
                return markdown;
        }

        if (!string.IsNullOrEmpty(plainText) && data.Formats.Any(static format =>
                format.Identifier.Equals("vscode-editor-data", StringComparison.OrdinalIgnoreCase)))
            return plainText;

        foreach (var format in HtmlFormats)
        {
            var html = await ReadFormatAsync(data, format);
            if (string.IsNullOrEmpty(html))
                continue;

            var markdown = await ConvertAsync(html, isRtf: false, plainText);
            if (!string.IsNullOrWhiteSpace(markdown))
                return markdown;
        }

        foreach (var format in RtfFormats)
        {
            var rtf = await ReadFormatAsync(data, format);
            if (string.IsNullOrEmpty(rtf))
                continue;

            var markdown = await ConvertAsync(rtf, isRtf: true);
            if (!string.IsNullOrWhiteSpace(markdown))
                return markdown;
        }

        return plainText;
    }

    private static async Task<string?> ReadFormatAsync(IAsyncDataTransfer data, string identifier)
    {
        try
        {
            foreach (var format in data.Formats)
            {
                if (!format.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (format is DataFormat<string> textFormat)
                    return await data.TryGetValueAsync(textFormat);
                if (format is DataFormat<byte[]> bytesFormat
                    && await data.TryGetValueAsync(bytesFormat) is { } bytes)
                {
                    if (identifier == "HTML Format")
                        return ExtractWindowsHtml(bytes);
                    return (Array.IndexOf(RtfFormats, identifier) >= 0 ? Encoding.Latin1 : Encoding.UTF8)
                        .GetString(bytes).TrimEnd('\0');
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ExternalException)
        {
            Logger.TryGet(LogEventLevel.Warning, "Clipboard")?.Log(
                null, "Could not read {Format}: {Error}", identifier, ex.Message);
        }

        return null;
    }

    internal static string ExtractWindowsHtml(byte[] bytes)
    {
        var header = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        if (TryReadOffset(header, "StartFragment:", out var start)
            && TryReadOffset(header, "EndFragment:", out var end)
            && start >= 0 && end > start && end <= bytes.Length)
            return Encoding.UTF8.GetString(bytes, start, end - start);

        var html = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end >= start + startMarker.Length)
            return html[(start + startMarker.Length)..end];

        if (TryReadOffset(header, "StartHTML:", out start) && start >= 0 && start < bytes.Length)
            return Encoding.UTF8.GetString(bytes, start, bytes.Length - start).TrimEnd('\0');

        return html;
    }

    private static bool TryReadOffset(string header, string name, out int offset)
    {
        offset = -1;
        var start = header.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;
        start += name.Length;
        var end = header.IndexOfAny(['\r', '\n'], start);
        return int.TryParse(header.AsSpan(start, (end < 0 ? header.Length : end) - start),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
    }

    private static Task<string?> ConvertAsync(string content, bool isRtf, string? plainText = null) => Task.Run(() =>
    {
        try
        {
            if (isRtf)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                content = RtfPipe.Rtf.ToHtml(content);
            }

            var config = new Config
            {
                GithubFlavored = true,
                Formatting = { RemoveComments = true, OutputLineEnding = "\n" },
                Tags = { Unknown = Config.UnknownTagsOption.Bypass },
                Tables = { CellListHandling = Config.TableCellListHandlingOption.InlineText }
            };
            var preserveSource = false;
            if (!isRtf && !string.IsNullOrEmpty(plainText))
            {
                config.Preprocess.Add("clipboard-editor-source", root =>
                {
                    // Native clipboards may hide editor metadata inside Chromium's custom-data format.
                    var body = root.QuerySelector("body");
                    var source = body?.FirstElementChild;
                    preserveSource = body?.Children.Length == 1 && source?.LocalName == "div"
                        && InlineStyle.Has(source, "white-space", "pre")
                        && source.QuerySelectorAll("*").All(static element => element.LocalName is "div" or "span" or "br")
                        && body.ChildNodes.OfType<IText>().All(static text => string.IsNullOrWhiteSpace(text.Data));
                });
            }
            config.Preprocess.RemoveScripts().RemoveHidden().ConvertInlineStylesToTags()
                .Transform("b[style], strong[style]", static element =>
                {
                    // Google Docs uses a normal-weight <b> wrapper around the copied document.
                    if (InlineStyle.Has(element, "font-weight", "normal") || InlineStyle.Has(element, "font-weight", "400"))
                        element.ReplaceWith(element.ChildNodes.ToArray());
                })
                .RemoveStyles();
            var converter = new Converter(config);
            var document = converter.Parse(content);
            return preserveSource ? plainText : converter.Render(document).TrimEnd('\r', '\n');
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or NotSupportedException)
        {
            Logger.TryGet(LogEventLevel.Warning, "Clipboard")?.Log(
                null, "Could not convert {Format} to Markdown: {Error}", isRtf ? "RTF" : "HTML", ex.Message);
            return null;
        }
    });
}
