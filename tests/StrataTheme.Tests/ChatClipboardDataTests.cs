using System.Text;
using Avalonia.Input;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class ChatClipboardDataTests
{
    [Fact]
    public void ToHtmlFragment_PreservesCommonMarkdownFormatting()
    {
        const string markdown =
            """
            # Heading

            A **bold** and *italic* [link](https://example.com) with `code`.

            - First
            - Second

            | Name | Value |
            | --- | --- |
            | Lumi | Rich |
            """;

        var html = MarkdownClipboardFormatter.ToHtmlFragment(markdown);

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<a href=\"https://example.com/\">link</a>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<ul", html);
        Assert.Contains("<table", html);
        Assert.Contains("<td", html);
    }

    [Fact]
    public void ToHtmlFragment_EncodesMarkupAndRejectsUnsafeLinks()
    {
        const string markdown = "<script>alert(1)</script> [unsafe](javascript:alert(1))";

        var html = MarkdownClipboardFormatter.ToHtmlFragment(markdown);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("unsafe (javascript:alert(1))", html);
    }

    [Fact]
    public void ToHtmlFragment_ConvertsInteractiveFencesToOrdinaryContent()
    {
        const string markdown = "Your list:\n\n```checklist\n{\"id\":\"prep\",\"title\":\"Prep\",\"items\":[{\"id\":\"milk\",\"label\":\"Milk\"}]}\n```";

        var html = MarkdownClipboardFormatter.ToHtmlFragment(markdown);

        Assert.DoesNotContain("<pre", html);
        Assert.DoesNotContain("&quot;id&quot;", html);
        Assert.Contains("<h3", html);
        Assert.Contains("<li>", html);
        Assert.Contains("Milk", html);
    }

    [Fact]
    public void ToHtmlFragment_PreservesWindowsPaths()
    {
        const string markdown = @"Open C:\Temp\file.txt, \\server\share, and C:\repo\.git\config";

        var html = MarkdownClipboardFormatter.ToHtmlFragment(markdown);

        Assert.Contains(@"C:\Temp\file.txt", html);
        Assert.Contains(@"\\server\share", html);
        Assert.Contains(@"C:\repo\.git\config", html);
    }

    [Fact]
    public void ConvertedTable_PreservesEscapedCellContent()
    {
        var source = new StringBuilder("| Value |\n| --- |\n");
        var values = new[] { @"C:\Temp\report.txt", @"\\server\share\report.txt", "First | Second" };
        foreach (var value in values)
            MarkdownClipboardFormatter.AppendRow(source, [value]);

        using var data = ChatClipboardData.CreateRichText(source.ToString());
        var item = Assert.Single(data.Items);
        Assert.Equal("Value\n" + string.Join("\n", values), item.TryGetRaw(DataFormat.Text));
        var html = Assert.IsType<string>(item.TryGetRaw(DataFormat.CreateStringPlatformFormat("text/html")));
        foreach (var value in values)
            Assert.Contains(System.Net.WebUtility.HtmlEncode(value), html);
    }

    [Fact]
    public void ConvertedTable_InlineCodeKeepsLiteralBackslashes()
    {
        const string path = @"\\server\share\";
        var source = new StringBuilder("| Value |\n| --- |\n");
        MarkdownClipboardFormatter.AppendRow(source, [$"`{path}`"]);
        using var data = ChatClipboardData.CreateRichText(source.ToString());
        var item = Assert.Single(data.Items);
        Assert.Equal("Value\n" + path, item.TryGetRaw(DataFormat.Text));
        var html = Assert.IsType<string>(item.TryGetRaw(DataFormat.CreateStringPlatformFormat("text/html")));
        Assert.Contains($"<code>{System.Net.WebUtility.HtmlEncode(path)}</code>", html);
    }

    [Fact]
    public void BuildWindowsHtmlClipboardBytes_UsesUtf8ByteOffsets()
    {
        const string fragment = "<p>Hello שלום</p>";

        var bytes = ChatClipboardData.BuildWindowsHtmlClipboardBytes(fragment);
        var payload = Encoding.UTF8.GetString(bytes);
        var startHtml = ReadOffset(payload, "StartHTML:");
        var endHtml = ReadOffset(payload, "EndHTML:");
        var startFragment = ReadOffset(payload, "StartFragment:");
        var endFragment = ReadOffset(payload, "EndFragment:");

        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes[startHtml..endHtml]));
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes[startFragment..endFragment]));
        Assert.EndsWith("</body></html>", Encoding.UTF8.GetString(bytes[startHtml..endHtml]));
        Assert.Equal(bytes.Length - 1, endHtml);
        Assert.Equal(0, bytes[^1]);
    }

    [Fact]
    public void CreateRichText_PublishesHtmlAndPlainTextFallbacks()
    {
        const string markdown = "**Lumi**";

        var data = ChatClipboardData.CreateRichText(markdown);
        var identifiers = data.Formats.Select(static format => format.Identifier).ToArray();
        var item = Assert.Single(data.Items);
        var htmlFormat = Assert.IsType<DataFormat<string>>(
            data.Formats.Single(static format => format.Identifier == "text/html"));

        Assert.Contains(DataFormat.Text.Identifier, identifiers);
        Assert.Contains("text/html", identifiers);
        Assert.Equal("Lumi", item.TryGetRaw(DataFormat.Text));
        Assert.Contains("<strong>Lumi</strong>", Assert.IsType<string>(item.TryGetRaw(htmlFormat)));

        if (OperatingSystem.IsWindows())
            Assert.Contains("HTML Format", identifiers);
        else if (OperatingSystem.IsMacOS())
            Assert.Contains("public.html", identifiers);
    }

    private static int ReadOffset(string payload, string key)
    {
        var start = payload.IndexOf(key, StringComparison.Ordinal) + key.Length;
        return int.Parse(payload.AsSpan(start, 10));
    }
}
