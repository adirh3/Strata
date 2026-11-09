using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

public partial class StrataMarkdown
{
    private const int LinkPreviewHeadCharacters = 768 * 1024;
    private const int MaxLinkPreviewPages = 512;
    private static readonly TimeSpan LinkPreviewTimeout = TimeSpan.FromSeconds(12);
    private static readonly ConcurrentDictionary<string, Task<string?>> LinkPreviewPages = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim LinkPreviewGate = new(4, 4);

    // The names a page gives its share picture, most specific first.
    private static readonly string[] LinkPreviewImageNames =
        ["og:image:secure_url", "og:image:url", "og:image", "twitter:image", "twitter:image:src", "image_src", "image"];

    // Pages are untrusted, so both patterns run in linear time whatever the markup.
    private static readonly Regex LinkPreviewTagPattern = new(@"<(meta|link)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex LinkPreviewAttributePattern = new(
        """([\w:.-]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));

    /// <summary>Test seam: answers in place of reading pages from the network.</summary>
    internal static Func<Uri, Task<string?>>? LinkPreviewReader;

    /// <summary>
    /// The picture a page shows when it is shared — its Open Graph or Twitter image, as messaging
    /// apps preview a link — or null. Each page is read once per run, only up to the end of its
    /// head, through the same guarded client as markdown images: public addresses only, every
    /// redirect checked again, and per-site limits.
    /// </summary>
    internal static Task<string?> GetLinkPreviewImageAsync(string? pageUrl)
    {
        if (!TryGetLinkPreviewPage(pageUrl, out var page))
            return Task.FromResult<string?>(null);
        if (LinkPreviewReader is { } reader)
            return reader(page);
        // A browser app may not read other sites' pages.
        if (OperatingSystem.IsBrowser())
            return Task.FromResult<string?>(null);
        if (LinkPreviewPages.Count >= MaxLinkPreviewPages)
            LinkPreviewPages.Clear();
        return LinkPreviewPages.GetOrAdd(page.AbsoluteUri, static (_, uri) => ReadLinkPreviewImageAsync(uri), page);
    }

    /// <summary>A page whose picture was already found in this run, so a view can make room for it at once.</summary>
    internal static bool HasKnownLinkPreviewImage(string? pageUrl) =>
        TryGetLinkPreviewPage(pageUrl, out var page)
        && LinkPreviewPages.TryGetValue(page.AbsoluteUri, out var read)
        && read.IsCompletedSuccessfully && read.Result is not null;

    private static bool TryGetLinkPreviewPage(string? pageUrl, [NotNullWhen(true)] out Uri? page) =>
        Uri.TryCreate(pageUrl?.Trim(), UriKind.Absolute, out page) && page.Scheme is "https" or "http";

    private static async Task<string?> ReadLinkPreviewImageAsync(Uri page)
    {
        await LinkPreviewGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(LinkPreviewTimeout);
            using var lease = await GetRemoteMarkdownImageResponseAsync(
                page, timeout.Token, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8").ConfigureAwait(false);
            var response = lease.Response;
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType is { } type
                && !type.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var head = await ReadHtmlHeadAsync(stream, timeout.Token).ConfigureAwait(false);
            return FindLinkPreviewImage(head, response.RequestMessage?.RequestUri ?? page);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A preview is decoration: a page that can't be read keeps the option's own tile.
            return null;
        }
        finally
        {
            LinkPreviewGate.Release();
        }
    }

    /// <summary>The page up to the end of its head, where the share tags are, and never more than a bounded amount.</summary>
    private static async Task<string> ReadHtmlHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024, leaveOpen: true);
        var text = new StringBuilder();
        var buffer = new char[16 * 1024];
        while (text.Length < LinkPreviewHeadCharacters)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            var from = Math.Max(0, text.Length - 8);
            text.Append(buffer, 0, read);
            var recent = text.ToString(from, text.Length - from);
            if (recent.Contains("</head", StringComparison.OrdinalIgnoreCase)
                || recent.Contains("<body", StringComparison.OrdinalIgnoreCase))
                break;
        }
        return text.ToString();
    }

    /// <summary>The page's share picture, as an absolute web address resolved against the page.</summary>
    internal static string? FindLinkPreviewImage(string html, Uri page)
    {
        var found = new string?[LinkPreviewImageNames.Length];
        foreach (Match tag in LinkPreviewTagPattern.Matches(html))
        {
            string? name = null, value = null, rel = null;
            foreach (Match attribute in LinkPreviewAttributePattern.Matches(tag.Value))
            {
                var content = attribute.Groups[2].Success ? attribute.Groups[2].Value
                    : attribute.Groups[3].Success ? attribute.Groups[3].Value : attribute.Groups[4].Value;
                switch (attribute.Groups[1].Value.ToLowerInvariant())
                {
                    case "property" or "name" or "itemprop":
                        name ??= content;
                        break;
                    case "content" or "href":
                        value ??= content;
                        break;
                    case "rel":
                        rel = content;
                        break;
                }
            }
            if (tag.Groups[1].Value.Equals("link", StringComparison.OrdinalIgnoreCase))
                name = rel?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => part.Equals("image_src", StringComparison.OrdinalIgnoreCase)) == true ? "image_src" : null;

            var rank = name is null ? -1 : Array.IndexOf(LinkPreviewImageNames, name.Trim().ToLowerInvariant());
            var address = value is null ? "" : WebUtility.HtmlDecode(value).Trim();
            // An empty tag would resolve to the page itself, and must not hide the names after it.
            if (rank >= 0 && found[rank] is null && address.Length > 0
                && Uri.TryCreate(page, address, out var image)
                && image.Scheme is "https" or "http" && image.AbsoluteUri != page.AbsoluteUri)
                found[rank] = image.AbsoluteUri;
        }
        foreach (var image in found)
        {
            if (image is not null)
                return image;
        }
        return null;
    }
}
