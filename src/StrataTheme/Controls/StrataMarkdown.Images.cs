using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

internal readonly record struct MarkdownImageSource(
    string CacheKey,
    string DisplayTarget,
    Uri? RemoteUri,
    string? LocalPath);

public partial class StrataMarkdown
{
    private const long MaxMarkdownImageBytes = 20 * 1024 * 1024;
    private const double MaxMarkdownImageWidth = 640;
    private const double MaxMarkdownImageHeight = 480;
    private const double MaxInlineMarkdownImageWidth = 240;
    private const double MaxInlineMarkdownImageHeight = 180;
    private const int MaxMarkdownImageRedirects = 4;
    private const int MaxConcurrentRemoteMarkdownImages = 2;
    private const int MaxRemoteMarkdownImageAttempts = 2;
    private static readonly TimeSpan MarkdownImageRequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MarkdownImageRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MarkdownImageRateLimitRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MarkdownImageRequestSpacing = TimeSpan.FromMilliseconds(500);
    private static readonly HttpClient MarkdownImageHttpClient = CreateMarkdownImageHttpClient();
    private static readonly SemaphoreSlim MarkdownImageDownloadGate =
        new(MaxConcurrentRemoteMarkdownImages, MaxConcurrentRemoteMarkdownImages);
    private static readonly object MarkdownImageRequestScheduleLock = new();
    private static DateTimeOffset _nextMarkdownImageRequestAt;

    private readonly Dictionary<string, MarkdownImageCacheEntry> _imageCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _imageKeysUsed = new(StringComparer.Ordinal);

    private Inline CreateImageInline(string altText, string imageTarget, double fontSize)
    {
        var frame = CreateMarkdownImageFrame(
            altText,
            imageTarget,
            fontSize,
            MaxInlineMarkdownImageWidth,
            MaxInlineMarkdownImageHeight);
        return frame is null
            ? CreateImageFallbackInline(altText, fontSize)
            : new InlineUIContainer(frame);
    }

    private Control CreateImageBlockControl(string altText, string imageTarget)
    {
        var frame = CreateMarkdownImageFrame(
            altText,
            imageTarget,
            _bodyFontSize,
            MaxMarkdownImageWidth,
            MaxMarkdownImageHeight);
        if (frame is not null)
            return frame;

        var placeholder = new TextBlock
        {
            Text = $"Image: {(string.IsNullOrWhiteSpace(altText) ? "Image unavailable" : altText)}",
            FontSize = _bodyFontSize,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 10),
        };
        placeholder.Classes.Add("strata-md-image-placeholder");

        var fallback = new Border
        {
            Child = placeholder,
            MinWidth = 80,
            MinHeight = 56,
            MaxWidth = MaxMarkdownImageWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        fallback.Classes.Add("strata-md-image");
        return fallback;
    }

    private Border? CreateMarkdownImageFrame(
        string altText,
        string imageTarget,
        double fontSize,
        double maxWidth,
        double maxHeight)
    {
        if (!TryResolveMarkdownImageSource(imageTarget, out var source))
            return null;

        _imageKeysUsed.Add(source.CacheKey);
        if (!_imageCache.TryGetValue(source.CacheKey, out var cacheEntry))
        {
            cacheEntry = new MarkdownImageCacheEntry(source);
            _imageCache[source.CacheKey] = cacheEntry;
        }

        var label = GetMarkdownImageLabel(altText, source);
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            IsVisible = false,
            Opacity = 0,
        };
        image.Classes.Add("strata-md-image-content");

        var placeholder = new TextBlock
        {
            Text = label,
            FontSize = fontSize,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 10, 12, 6),
        };
        placeholder.Classes.Add("strata-md-image-placeholder");

        var progress = new ProgressBar
        {
            IsIndeterminate = true,
        };
        progress.Classes.Add("strata-md-image-progress");

        var loadingContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        loadingContent.Children.Add(placeholder);
        loadingContent.Children.Add(progress);

        var content = new Grid();
        content.Children.Add(image);
        content.Children.Add(loadingContent);

        var frame = new Border
        {
            Child = content,
            MinWidth = 80,
            MinHeight = 56,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        frame.Classes.Add("strata-md-image");
        AutomationProperties.SetName(frame, label);
        AutomationProperties.SetHelpText(frame, $"Loading markdown image from {source.DisplayTarget}");
        ToolTip.SetTip(frame, source.DisplayTarget);

        _ = PopulateMarkdownImageAsync(
            cacheEntry,
            frame,
            image,
            loadingContent,
            progress,
            placeholder,
            label);
        return frame;
    }

    private static Inline CreateImageFallbackInline(string altText, double fontSize)
    {
        var label = string.IsNullOrWhiteSpace(altText) ? "Image unavailable" : altText;
        return new Run($"Image: {label}")
        {
            FontSize = fontSize,
            FontStyle = FontStyle.Italic,
            TextDecorations = TextDecorations.Underline,
        };
    }

    private async Task PopulateMarkdownImageAsync(
        MarkdownImageCacheEntry cacheEntry,
        Border frame,
        Image image,
        StackPanel loadingContent,
        ProgressBar progress,
        TextBlock placeholder,
        string label)
    {
        try
        {
            var bitmap = await cacheEntry.BitmapTask.ConfigureAwait(false);
            if (cacheEntry.IsDisposed)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cacheEntry.IsDisposed)
                    return;

                image.Source = bitmap;
                image.IsVisible = true;
                image.Opacity = 1;
                loadingContent.IsVisible = false;
                AutomationProperties.SetHelpText(
                    frame,
                    $"Markdown image from {cacheEntry.Source.DisplayTarget}");
            });
        }
        catch (OperationCanceledException ex)
        {
            if (!cacheEntry.IsDisposed)
            {
                await ShowMarkdownImageFailureAsync(
                    frame,
                    loadingContent,
                    progress,
                    placeholder,
                    label,
                    ex).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsRecoverableMarkdownImageFailure(ex))
        {
            await ShowMarkdownImageFailureAsync(
                frame,
                loadingContent,
                progress,
                placeholder,
                label,
                ex).ConfigureAwait(false);
        }
    }

    private async Task ShowMarkdownImageFailureAsync(
        Border frame,
        StackPanel loadingContent,
        ProgressBar progress,
        TextBlock placeholder,
        string label,
        Exception error)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            placeholder.Text = $"{label}\nImage unavailable";
            progress.IsVisible = false;
            loadingContent.IsVisible = true;
            ToolTip.SetTip(frame, error.Message);
            AutomationProperties.SetHelpText(frame, $"Image unavailable: {error.Message}");
        });
    }

    internal static bool IsRecoverableMarkdownImageFailure(Exception error) =>
        error is HttpRequestException
            or IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or FormatException
            or InvalidOperationException
            or System.Security.SecurityException;

    private static string GetMarkdownImageLabel(string altText, MarkdownImageSource source)
    {
        if (!string.IsNullOrWhiteSpace(altText))
            return altText.Trim();

        var candidate = source.LocalPath is { Length: > 0 }
            ? Path.GetFileName(source.LocalPath)
            : Path.GetFileName(source.RemoteUri?.AbsolutePath);
        return string.IsNullOrWhiteSpace(candidate) ? "Markdown image" : Uri.UnescapeDataString(candidate);
    }

    private static bool CanContainInlineImages(MdBlockKind kind) => kind is
        MdBlockKind.Paragraph
        or MdBlockKind.Heading
        or MdBlockKind.Bullet
        or MdBlockKind.NumberedItem
        or MdBlockKind.TaskItem
        or MdBlockKind.Blockquote
        or MdBlockKind.Table;

    private void TrackImageCacheKeys(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("![", StringComparison.Ordinal) < 0)
            return;

        var span = text.AsSpan();
        var pos = 0;
        while (pos + 1 < span.Length)
        {
            var relativeStart = span[pos..].IndexOf("![", StringComparison.Ordinal);
            if (relativeStart < 0)
                return;

            var imageStart = pos + relativeStart;
            var bracketClose = FindClosingBracket(span, imageStart + 2);
            if (bracketClose < 0
                || bracketClose + 1 >= span.Length
                || span[bracketClose + 1] != '(')
            {
                pos = imageStart + 2;
                continue;
            }

            var parenClose = FindClosingParen(span, bracketClose + 2);
            if (parenClose < 0)
                return;

            var target = text[(bracketClose + 2)..parenClose];
            if (TryResolveMarkdownImageSource(target, out var source))
                _imageKeysUsed.Add(source.CacheKey);

            pos = parenClose + 1;
        }
    }

    internal static bool TryResolveMarkdownImageSource(
        string imageTarget,
        out MarkdownImageSource source)
    {
        var normalizedTarget = NormalizeLinkTarget(imageTarget);
        if (normalizedTarget.Length >= 2
            && normalizedTarget[0] == '<'
            && normalizedTarget[^1] == '>')
        {
            normalizedTarget = normalizedTarget[1..^1].Trim();
        }

        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            source = default;
            return false;
        }

        if (Path.IsPathFullyQualified(normalizedTarget))
        {
            var fullPath = Path.GetFullPath(normalizedTarget);
            if (!IsSafeLocalMarkdownImagePath(fullPath))
            {
                source = default;
                return false;
            }

            source = CreateLocalMarkdownImageSource(fullPath);
            return true;
        }

        if (Uri.TryCreate(normalizedTarget, UriKind.Absolute, out var absoluteUri))
        {
            if (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                source = new MarkdownImageSource(
                    absoluteUri.AbsoluteUri,
                    absoluteUri.AbsoluteUri,
                    absoluteUri,
                    null);
                return true;
            }

            if (absoluteUri.IsFile)
            {
                if (!string.IsNullOrEmpty(absoluteUri.Host) || absoluteUri.IsUnc)
                {
                    source = default;
                    return false;
                }

                var fullPath = Path.GetFullPath(absoluteUri.LocalPath);
                if (!IsSafeLocalMarkdownImagePath(fullPath))
                {
                    source = default;
                    return false;
                }

                source = CreateLocalMarkdownImageSource(fullPath);
                return true;
            }
        }

        var resolvedPath = ResolveLocalPath(normalizedTarget);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            var fullPath = Path.GetFullPath(resolvedPath);
            if (!IsSafeLocalMarkdownImagePath(fullPath))
            {
                source = default;
                return false;
            }

            source = CreateLocalMarkdownImageSource(fullPath);
            return true;
        }

        source = default;
        return false;
    }

    private static MarkdownImageSource CreateLocalMarkdownImageSource(string fullPath) =>
        new($"file:{fullPath}", fullPath, null, fullPath);

    private static bool IsSafeLocalMarkdownImagePath(string fullPath)
    {
        if (!Path.IsPathFullyQualified(fullPath))
            return false;

        if (!OperatingSystem.IsWindows())
            return !fullPath.StartsWith("//", StringComparison.Ordinal);

        return !fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            && !fullPath.StartsWith(@"//", StringComparison.Ordinal)
            && !fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)
            && !fullPath.StartsWith(@"\\.\", StringComparison.Ordinal);
    }

    private static HttpClient CreateMarkdownImageHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectToPublicMarkdownImageHostAsync,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StrataMarkdown/1.0");
        return client;
    }

    internal static async Task<Bitmap> LoadMarkdownImageAsync(
        string imageTarget,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveMarkdownImageSource(imageTarget, out var source))
            throw new NotSupportedException($"Unsupported markdown image source: {imageTarget}");

        return await LoadMarkdownImageAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Bitmap> LoadMarkdownImageAsync(
        MarkdownImageSource source,
        CancellationToken cancellationToken)
    {
        if (source.RemoteUri is { } remoteUri)
            return await LoadRemoteMarkdownImageAsync(remoteUri, cancellationToken).ConfigureAwait(false);

        var localPath = source.LocalPath
            ?? throw new InvalidOperationException("A markdown image source must be remote or local.");
        var fileInfo = new FileInfo(localPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Markdown image file was not found.", localPath);

        await using var fileStream = new FileStream(
            localPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var localImageData = await ReadMarkdownImageDataAsync(
            fileStream,
            fileInfo.Length,
            cancellationToken).ConfigureAwait(false);
        return DecodeMarkdownImage(localImageData);
    }

    private static async Task<Bitmap> LoadRemoteMarkdownImageAsync(
        Uri remoteUri,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await LoadRemoteMarkdownImageOnceAsync(remoteUri, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRetryRemoteMarkdownImageFailure(
                       ex,
                       attempt,
                       cancellationToken))
            {
                var retryDelay = GetRemoteMarkdownImageRetryDelay(ex);
                DelayRemoteMarkdownImageRequests(retryDelay);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<Bitmap> LoadRemoteMarkdownImageOnceAsync(
        Uri remoteUri,
        CancellationToken cancellationToken)
    {
        await MarkdownImageDownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(MarkdownImageRequestTimeout);
            var requestToken = requestTimeout.Token;

            using var response = await GetRemoteMarkdownImageResponseAsync(
                remoteUri,
                requestToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(requestToken)
                .ConfigureAwait(false);
            using var imageData = await ReadMarkdownImageDataAsync(
                responseStream,
                response.Content.Headers.ContentLength,
                requestToken).ConfigureAwait(false);
            return DecodeMarkdownImage(imageData);
        }
        finally
        {
            MarkdownImageDownloadGate.Release();
        }
    }

    internal static bool ShouldRetryRemoteMarkdownImageFailure(
        Exception error,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        if (attempt >= MaxRemoteMarkdownImageAttempts - 1 || cancellationToken.IsCancellationRequested)
            return false;

        if (error is OperationCanceledException or IOException)
            return true;

        if (error is not HttpRequestException requestError)
            return false;

        if (requestError.StatusCode is { } statusCode)
        {
            return statusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                || (int)statusCode >= 500;
        }

        for (Exception? current = requestError; current is not null; current = current.InnerException)
        {
            if (current is SocketException or IOException)
                return true;
        }

        return false;
    }

    private static TimeSpan GetRemoteMarkdownImageRetryDelay(Exception error) =>
        error is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            ? MarkdownImageRateLimitRetryDelay
            : MarkdownImageRetryDelay;

    private static async Task WaitForRemoteMarkdownImageRequestSlotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            lock (MarkdownImageRequestScheduleLock)
            {
                var now = DateTimeOffset.UtcNow;
                if (_nextMarkdownImageRequestAt <= now)
                {
                    _nextMarkdownImageRequestAt = now + MarkdownImageRequestSpacing;
                    return;
                }

                delay = _nextMarkdownImageRequestAt - now;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void DelayRemoteMarkdownImageRequests(TimeSpan delay)
    {
        lock (MarkdownImageRequestScheduleLock)
        {
            var delayedUntil = DateTimeOffset.UtcNow + delay;
            if (delayedUntil > _nextMarkdownImageRequestAt)
                _nextMarkdownImageRequestAt = delayedUntil;
        }
    }

    private static async Task<HttpResponseMessage> GetRemoteMarkdownImageResponseAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirect = 0; redirect <= MaxMarkdownImageRedirects; redirect++)
        {
            if (!IsAllowedRemoteMarkdownImageUri(currentUri))
                throw new HttpRequestException("Remote markdown images must resolve to a public internet address.");

            await WaitForRemoteMarkdownImageRequestSlotAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await MarkdownImageHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 300 and < 400
                && response.Headers.Location is { } location)
            {
                if (redirect == MaxMarkdownImageRedirects)
                {
                    response.Dispose();
                    throw new HttpRequestException("Remote markdown image redirected too many times.");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                response.Dispose();
                currentUri = nextUri;
                continue;
            }

            return response;
        }

        throw new HttpRequestException("Remote markdown image redirected too many times.");
    }

    private static async ValueTask<Stream> ConnectToPublicMarkdownImageHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolvePublicRemoteImageAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken).ConfigureAwait(false);
        Exception? lastError = null;

        foreach (var address in GetMarkdownImageConnectionOrder(addresses))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            var ownershipTransferred = false;
            try
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(TimeSpan.FromSeconds(5));

                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    attemptTimeout.Token).ConfigureAwait(false);
                var stream = new NetworkStream(socket, ownsSocket: true);
                ownershipTransferred = true;
                return stream;
            }
            catch (SocketException ex)
            {
                lastError = ex;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }
            finally
            {
                if (!ownershipTransferred)
                    socket.Dispose();
            }
        }

        throw new HttpRequestException(
            $"Could not connect to public markdown image host '{context.DnsEndPoint.Host}'.",
            lastError);
    }

    private static IPAddress[] GetMarkdownImageConnectionOrder(IPAddress[] addresses)
    {
        var ipv4 = addresses
            .Where(static address => address.AddressFamily == AddressFamily.InterNetwork)
            .ToArray();
        var ipv6 = addresses
            .Where(static address => address.AddressFamily == AddressFamily.InterNetworkV6)
            .ToArray();
        var preferIpv6 = addresses[0].AddressFamily == AddressFamily.InterNetworkV6;
        var ordered = new List<IPAddress>(addresses.Length);

        for (var index = 0; index < Math.Max(ipv4.Length, ipv6.Length); index++)
        {
            if (preferIpv6)
            {
                if (index < ipv6.Length)
                    ordered.Add(ipv6[index]);
                if (index < ipv4.Length)
                    ordered.Add(ipv4[index]);
            }
            else
            {
                if (index < ipv4.Length)
                    ordered.Add(ipv4[index]);
                if (index < ipv6.Length)
                    ordered.Add(ipv6[index]);
            }
        }

        return ordered.ToArray();
    }

    internal static async Task<bool> IsPublicRemoteImageUriAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        if (!IsAllowedRemoteMarkdownImageUri(uri))
            return false;

        try
        {
            await ResolvePublicRemoteImageAddressesAsync(uri.DnsSafeHost, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static bool IsAllowedRemoteMarkdownImageUri(Uri uri) =>
        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
         || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        && !uri.IsLoopback
        && string.IsNullOrEmpty(uri.UserInfo)
        && !string.Equals(uri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase);

    private static async Task<IPAddress[]> ResolvePublicRemoteImageAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var address))
        {
            addresses = [address];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new HttpRequestException($"Could not resolve markdown image host '{host}'.", ex);
            }
        }

        if (addresses.Length == 0)
            throw new HttpRequestException($"Markdown image host '{host}' did not resolve.");

        foreach (var candidate in addresses)
        {
            if (!IsPublicInternetAddress(candidate))
                throw new HttpRequestException(
                    $"Markdown image host '{host}' resolved to a non-public address.");
        }

        return addresses;
    }

    private static bool IsPublicInternetAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && bytes[0] < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xFE) != 0xFC
            && !(ipv6[0] == 0x20
                 && ipv6[1] == 0x01
                 && ipv6[2] == 0x0D
                 && ipv6[3] == 0xB8);
    }

    private static async Task<MemoryStream> ReadMarkdownImageDataAsync(
        Stream source,
        long? knownLength,
        CancellationToken cancellationToken)
    {
        if (knownLength > MaxMarkdownImageBytes)
            throw new InvalidDataException($"Markdown images cannot exceed {MaxMarkdownImageBytes / (1024 * 1024)} MB.");

        var capacity = knownLength is > 0 and <= int.MaxValue ? (int)knownLength.Value : 0;
        var destination = new MemoryStream(capacity);
        var buffer = new byte[81920];
        var completed = false;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                if (destination.Length + read > MaxMarkdownImageBytes)
                    throw new InvalidDataException($"Markdown images cannot exceed {MaxMarkdownImageBytes / (1024 * 1024)} MB.");

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            destination.Position = 0;
            completed = true;
            return destination;
        }
        finally
        {
            if (!completed)
                destination.Dispose();
        }
    }

    private static Bitmap DecodeMarkdownImage(MemoryStream imageData)
    {
        if (!imageData.TryGetBuffer(out var buffer)
            || !TryReadMarkdownImageDimensions(
                buffer.Array.AsSpan(buffer.Offset, checked((int)imageData.Length)),
                out var pixelSize))
        {
            throw new InvalidDataException("The markdown image format is unsupported or invalid.");
        }

        var targetWidth = CalculateMarkdownImageDecodeWidth(pixelSize);
        imageData.Position = 0;
        return Bitmap.DecodeToWidth(
            imageData,
            targetWidth,
            BitmapInterpolationMode.HighQuality);
    }

    internal static int CalculateMarkdownImageDecodeWidth(PixelSize pixelSize)
    {
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize), "Image dimensions must be positive.");

        var scale = Math.Min(
            1d,
            Math.Min(
                MaxMarkdownImageWidth / pixelSize.Width,
                MaxMarkdownImageHeight / pixelSize.Height));
        return Math.Max(1, (int)Math.Round(pixelSize.Width * scale));
    }

    internal static bool TryReadMarkdownImageDimensions(
        ReadOnlySpan<byte> data,
        out PixelSize pixelSize)
    {
        if (TryReadPngDimensions(data, out pixelSize)
            || TryReadJpegDimensions(data, out pixelSize)
            || TryReadGifDimensions(data, out pixelSize)
            || TryReadBmpDimensions(data, out pixelSize)
            || TryReadWebpDimensions(data, out pixelSize))
        {
            return IsValidMarkdownImageSize(pixelSize);
        }

        pixelSize = default;
        return false;
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> data, out PixelSize pixelSize)
    {
        if (data.Length >= 24
            && data[0] == 0x89
            && data[1] == 0x50
            && data[2] == 0x4E
            && data[3] == 0x47
            && data[4] == 0x0D
            && data[5] == 0x0A
            && data[6] == 0x1A
            && data[7] == 0x0A)
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(data[16..20]);
            var height = BinaryPrimitives.ReadUInt32BigEndian(data[20..24]);
            if (width <= int.MaxValue && height <= int.MaxValue)
            {
                pixelSize = new PixelSize((int)width, (int)height);
                return true;
            }
        }

        pixelSize = default;
        return false;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> data, out PixelSize pixelSize)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            pixelSize = default;
            return false;
        }

        var offset = 2;
        while (offset + 3 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < data.Length && data[offset] == 0xFF)
                offset++;
            if (offset >= data.Length)
                break;

            var marker = data[offset++];
            if (marker is 0x01 or >= 0xD0 and <= 0xD9)
                continue;

            if (offset + 1 >= data.Length)
                break;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[offset..(offset + 2)]);
            if (segmentLength < 2 || offset + segmentLength > data.Length)
                break;

            if (IsJpegStartOfFrame(marker) && segmentLength >= 7)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 3)..(offset + 5)]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..(offset + 7)]);
                pixelSize = new PixelSize(width, height);
                return true;
            }

            offset += segmentLength;
        }

        pixelSize = default;
        return false;
    }

    private static bool IsJpegStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xC3
            or >= 0xC5 and <= 0xC7
            or >= 0xC9 and <= 0xCB
            or >= 0xCD and <= 0xCF;

    private static bool TryReadGifDimensions(ReadOnlySpan<byte> data, out PixelSize pixelSize)
    {
        if (data.Length >= 10
            && (data[..6].SequenceEqual("GIF87a"u8)
                || data[..6].SequenceEqual("GIF89a"u8)))
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]);
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]);
            pixelSize = new PixelSize(width, height);
            return true;
        }

        pixelSize = default;
        return false;
    }

    private static bool TryReadBmpDimensions(ReadOnlySpan<byte> data, out PixelSize pixelSize)
    {
        if (data.Length >= 26
            && data[0] == (byte)'B'
            && data[1] == (byte)'M'
            && BinaryPrimitives.ReadUInt32LittleEndian(data[14..18]) >= 40)
        {
            var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..22]);
            var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..26]);
            if (width > 0 && rawHeight != int.MinValue)
            {
                pixelSize = new PixelSize(width, Math.Abs(rawHeight));
                return true;
            }
        }

        pixelSize = default;
        return false;
    }

    private static bool TryReadWebpDimensions(ReadOnlySpan<byte> data, out PixelSize pixelSize)
    {
        if (data.Length < 30
            || !data[..4].SequenceEqual("RIFF"u8)
            || !data[8..12].SequenceEqual("WEBP"u8))
        {
            pixelSize = default;
            return false;
        }

        if (data[12..16].SequenceEqual("VP8X"u8))
        {
            pixelSize = new PixelSize(
                1 + ReadUInt24LittleEndian(data[24..27]),
                1 + ReadUInt24LittleEndian(data[27..30]));
            return true;
        }

        if (data[12..16].SequenceEqual("VP8L"u8) && data[20] == 0x2F)
        {
            var width = 1 + data[21] + ((data[22] & 0x3F) << 8);
            var height = 1
                + ((data[22] & 0xC0) >> 6)
                + (data[23] << 2)
                + ((data[24] & 0x0F) << 10);
            pixelSize = new PixelSize(width, height);
            return true;
        }

        if (data[12..16].SequenceEqual("VP8 "u8)
            && data[23] == 0x9D
            && data[24] == 0x01
            && data[25] == 0x2A)
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..28]) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..30]) & 0x3FFF;
            pixelSize = new PixelSize(width, height);
            return true;
        }

        pixelSize = default;
        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16);

    private static bool IsValidMarkdownImageSize(PixelSize pixelSize) =>
        pixelSize.Width is > 0 and <= 1_000_000
        && pixelSize.Height is > 0 and <= 1_000_000;

    private void EvictStaleImageCache()
    {
        if (_imageCache.Count == 0)
            return;

        _evictBuffer.Clear();
        foreach (var key in _imageCache.Keys)
        {
            if (!_imageKeysUsed.Contains(key))
                _evictBuffer.Add(key);
        }

        foreach (var key in _evictBuffer)
        {
            if (_imageCache.Remove(key, out var cacheEntry))
                cacheEntry.Dispose();
        }
    }

    private void ClearImageCache()
    {
        foreach (var cacheEntry in _imageCache.Values)
            cacheEntry.Dispose();

        _imageCache.Clear();
    }

    private sealed class MarkdownImageCacheEntry : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _disposed;

        public MarkdownImageCacheEntry(MarkdownImageSource source)
        {
            Source = source;
            BitmapTask = LoadMarkdownImageAsync(source, _cancellation.Token);
        }

        public MarkdownImageSource Source { get; }

        public Task<Bitmap> BitmapTask { get; }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _cancellation.Cancel();
            _ = BitmapTask.ContinueWith(
                static task => task.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            _cancellation.Dispose();
        }
    }
}
