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

/// <summary>
/// What a page says about itself when it is shared, as messaging apps preview a link: its title,
/// summary, site name, share picture and icon, each an absolute web address or plain text, or null.
/// </summary>
internal sealed record LinkPreview(string? Title, string? Description, string? SiteName, string? Image, string? Icon)
{
    public bool IsEmpty => Title is null && Description is null && SiteName is null && Image is null && Icon is null;
}

public partial class StrataMarkdown
{
    private const int LinkPreviewHeadCharacters = 768 * 1024;
    private const int MaxLinkPreviewPages = 512;
    private const int MaxLinkPreviewTextLength = 400;
    private static readonly TimeSpan LinkPreviewTimeout = TimeSpan.FromSeconds(12);
    private static readonly ConcurrentDictionary<string, Task<LinkPreview?>> LinkPreviewPages = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim LinkPreviewGate = new(4, 4);

    // The names a page gives each part of its preview, most specific first.
    private static readonly string[] LinkPreviewImageNames =
        ["og:image:secure_url", "og:image:url", "og:image", "twitter:image", "twitter:image:src", "image_src", "image"];
    private static readonly string[] LinkPreviewTitleNames = ["og:title", "twitter:title"];
    private static readonly string[] LinkPreviewDescriptionNames = ["og:description", "twitter:description", "description"];
    private static readonly string[] LinkPreviewSiteNames = ["og:site_name", "application-name"];
    // Touch icons are large pictures; plain icons are often .ico or .svg, which markdown images don't decode.
    private static readonly string[] LinkPreviewIconNames = ["apple-touch-icon", "apple-touch-icon-precomposed", "icon", "shortcut icon"];

    // Pages are untrusted, so the patterns run in linear time whatever the markup.
    private static readonly Regex LinkPreviewTagPattern = new(@"<(meta|link)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex LinkPreviewAttributePattern = new(
        """([\w:.-]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));

    private static readonly Regex LinkPreviewTitlePattern = new(@"<title\b[^>]*>([^<]*)</title",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Test seam: answers with a page's picture in place of reading pages from the network.</summary>
    internal static Func<Uri, Task<string?>>? LinkPreviewReader;

    /// <summary>Test seam: answers with a page's whole preview in place of reading pages from the network.</summary>
    internal static Func<Uri, Task<LinkPreview?>>? LinkPreviewPageReader;

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
        return ImageOf(GetLinkPreviewAsync(pageUrl));

        static async Task<string?> ImageOf(Task<LinkPreview?> preview) => (await preview.ConfigureAwait(false))?.Image;
    }

    /// <summary>
    /// The page's whole preview — title, summary, site, picture and icon — or null when it can't be
    /// read. Read once per run with the picture, through the same guarded client.
    /// </summary>
    internal static Task<LinkPreview?> GetLinkPreviewAsync(string? pageUrl)
    {
        if (!TryGetLinkPreviewPage(pageUrl, out var page))
            return Task.FromResult<LinkPreview?>(null);
        if (LinkPreviewPageReader is { } pageReader)
            return pageReader(page);
        if (LinkPreviewReader is { } reader)
            return PictureOnly(reader(page));
        // A browser app may not read other sites' pages.
        if (OperatingSystem.IsBrowser())
            return Task.FromResult<LinkPreview?>(null);
        if (LinkPreviewPages.Count >= MaxLinkPreviewPages)
            LinkPreviewPages.Clear();
        return LinkPreviewPages.GetOrAdd(page.AbsoluteUri, static (_, uri) => ReadLinkPreviewAsync(uri), page);

        static async Task<LinkPreview?> PictureOnly(Task<string?> image) =>
            await image.ConfigureAwait(false) is { } found ? new LinkPreview(null, null, null, found, null) : null;
    }

    /// <summary>A page whose picture was already found in this run, so a view can make room for it at once.</summary>
    internal static bool HasKnownLinkPreviewImage(string? pageUrl) =>
        TryGetLinkPreviewPage(pageUrl, out var page)
        && LinkPreviewPages.TryGetValue(page.AbsoluteUri, out var read)
        && read.IsCompletedSuccessfully && read.Result?.Image is not null;

    private static bool TryGetLinkPreviewPage(string? pageUrl, [NotNullWhen(true)] out Uri? page) =>
        Uri.TryCreate(pageUrl?.Trim(), UriKind.Absolute, out page) && page.Scheme is "https" or "http";

    private static async Task<LinkPreview?> ReadLinkPreviewAsync(Uri page)
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
            var preview = FindLinkPreview(head, response.RequestMessage?.RequestUri ?? page);
            return preview.IsEmpty ? null : preview;
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
    internal static string? FindLinkPreviewImage(string html, Uri page) => FindLinkPreview(html, page).Image;

    /// <summary>
    /// What the page's head says about it when shared: its title, summary and site name as plain text,
    /// and its picture and icon as absolute web addresses resolved against the page.
    /// </summary>
    internal static LinkPreview FindLinkPreview(string html, Uri page)
    {
        var images = new string?[LinkPreviewImageNames.Length];
        var titles = new string?[LinkPreviewTitleNames.Length];
        var descriptions = new string?[LinkPreviewDescriptionNames.Length];
        var sites = new string?[LinkPreviewSiteNames.Length];
        var icons = new string?[LinkPreviewIconNames.Length];
        foreach (Match tag in LinkPreviewTagPattern.Matches(html))
        {
            string? name = null, value = null, rel = null, type = null;
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
                    case "type":
                        type = content;
                        break;
                }
            }
            var address = value is null ? "" : WebUtility.HtmlDecode(value).Trim();
            if (tag.Groups[1].Value.Equals("link", StringComparison.OrdinalIgnoreCase))
            {
                var relation = rel is null ? "" : string.Join(' ', rel.ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (relation.Split(' ').Contains("image_src"))
                    Keep(images, Array.IndexOf(LinkPreviewImageNames, "image_src"), WebAddress(address));
                // Vector icons don't decode as markdown images.
                if (type?.Contains("svg", StringComparison.OrdinalIgnoreCase) != true
                    && !address.Split('?', '#')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    Keep(icons, Array.IndexOf(LinkPreviewIconNames, relation), WebAddress(address));
                continue;
            }

            var key = name?.Trim().ToLowerInvariant();
            if (key is null)
                continue;
            Keep(images, Array.IndexOf(LinkPreviewImageNames, key), WebAddress(address));
            Keep(titles, Array.IndexOf(LinkPreviewTitleNames, key), Text(address));
            Keep(descriptions, Array.IndexOf(LinkPreviewDescriptionNames, key), Text(address));
            Keep(sites, Array.IndexOf(LinkPreviewSiteNames, key), Text(address));
        }

        var title = First(titles);
        if (title is null && LinkPreviewTitlePattern.Match(html) is { Success: true } titleTag)
            title = Text(WebUtility.HtmlDecode(titleTag.Groups[1].Value));
        return new LinkPreview(title, First(descriptions), First(sites), First(images), First(icons));

        // An empty tag would resolve to the page itself, and must not hide the names after it.
        string? WebAddress(string address) =>
            address.Length > 0 && Uri.TryCreate(page, address, out var resolved)
            && resolved.Scheme is "https" or "http" && resolved.AbsoluteUri != page.AbsoluteUri
                ? resolved.AbsoluteUri : null;

        static string? Text(string text)
        {
            var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (collapsed.Length == 0)
                return null;
            return collapsed.Length <= MaxLinkPreviewTextLength ? collapsed : collapsed[..MaxLinkPreviewTextLength].TrimEnd() + "…";
        }

        static void Keep(string?[] found, int rank, string? value)
        {
            if (rank >= 0 && value is not null)
                found[rank] ??= value;
        }

        static string? First(string?[] found)
        {
            foreach (var value in found)
            {
                if (value is not null)
                    return value;
            }
            return null;
        }
    }
}