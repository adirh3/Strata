using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMarkdownImageTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQMcAAAAASUVORK5CYII=");

    private readonly AvaloniaFixture _fixture;

    public StrataMarkdownImageTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("http://example.com/image.jpg")]
    public void TryResolveMarkdownImageSource_AcceptsHttpImages(string target)
    {
        Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var source));
        Assert.NotNull(source.RemoteUri);
        Assert.Null(source.LocalPath);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("ftp://example.com/image.png")]
    public void TryResolveMarkdownImageSource_RejectsUnsupportedSchemes(string target)
    {
        Assert.False(StrataMarkdown.TryResolveMarkdownImageSource(target, out _));
    }

    [Fact]
    public void TryResolveMarkdownImageSource_AcceptsAbsoluteLocalPathAndFileUri()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "strata-markdown-image.png"));

        Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(path, out var pathSource));
        Assert.Equal(path, pathSource.LocalPath);
        Assert.Null(pathSource.RemoteUri);

        Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(new Uri(path).AbsoluteUri, out var uriSource));
        Assert.Equal(path, uriSource.LocalPath);
        Assert.Null(uriSource.RemoteUri);
    }

    [Fact]
    public void TryResolveMarkdownImageSource_RejectsNetworkFilePaths()
    {
        Assert.False(StrataMarkdown.TryResolveMarkdownImageSource("file://attacker/share/image.png", out _));
        Assert.False(StrataMarkdown.TryResolveMarkdownImageSource(@"\\attacker\share\image.png", out _));
    }

    [Theory]
    [InlineData("http://127.0.0.1/image.png")]
    [InlineData("http://10.0.0.1/image.png")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/image.png")]
    public async Task IsPublicRemoteImageUriAsync_RejectsPrivateDestinations(string target)
    {
        Assert.False(await StrataMarkdown.IsPublicRemoteImageUriAsync(new Uri(target)));
    }

    [Fact]
    public async Task IsPublicRemoteImageUriAsync_AcceptsPublicIpLiteral()
    {
        Assert.True(await StrataMarkdown.IsPublicRemoteImageUriAsync(
            new Uri("https://8.8.8.8/image.png")));
    }

    [Fact]
    public async Task LoadMarkdownImageAsync_RejectsLoopbackBeforeRequest()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            StrataMarkdown.LoadMarkdownImageAsync("http://127.0.0.1/image.png"));
    }

    [Theory]
    [InlineData("https://example.com/image.jpg", "https://example.com/other.png", true)]
    [InlineData("https://example.com/image.jpg", "https://other.example.com/image.jpg", false)]
    [InlineData("https://example.com/image.jpg", "http://example.com/image.jpg", false)]
    [InlineData("https://b\u00fccher.example/image.jpg", "https://xn--bcher-kva.example/other.png", true)]
    public void GetRemoteMarkdownImageHostKey_GroupsByOrigin(
        string first,
        string second,
        bool shouldMatch)
    {
        var firstKey = StrataMarkdown.GetRemoteMarkdownImageHostKey(new Uri(first));
        var secondKey = StrataMarkdown.GetRemoteMarkdownImageHostKey(new Uri(second));

        Assert.Equal(shouldMatch, string.Equals(firstKey, secondKey, StringComparison.Ordinal));
    }

    [Fact]
    public void GetRemoteMarkdownImageCooldown_UsesRetryAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        Assert.Equal(
            TimeSpan.FromSeconds(12),
            StrataMarkdown.GetRemoteMarkdownImageCooldown(response, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GetRemoteMarkdownImageCooldown_IgnoresSuccessfulResponses()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        Assert.Equal(
            TimeSpan.Zero,
            StrataMarkdown.GetRemoteMarkdownImageCooldown(response, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GetRemoteMarkdownImageDelaySlice_BoundsLongRetryAfter()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            StrataMarkdown.GetRemoteMarkdownImageDelaySlice(TimeSpan.FromDays(60)));
        Assert.Equal(
            TimeSpan.FromSeconds(12),
            StrataMarkdown.GetRemoteMarkdownImageDelaySlice(TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public async Task ImageDownloadGate_IsOwnedByEachMarkdownComponent()
    {
        await _fixture.Dispatch(() =>
        {
            var gateField = typeof(StrataMarkdown).GetField(
                "_imageDownloadGate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var first = new StrataMarkdown();
            var second = new StrataMarkdown();

            Assert.NotSame(gateField?.GetValue(first), gateField?.GetValue(second));
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void ShouldRetryRemoteMarkdownImageFailure_RetriesTransientStatusOnce(
        HttpStatusCode statusCode)
    {
        var error = new HttpRequestException("Transient image response.", null, statusCode);

        Assert.True(StrataMarkdown.ShouldRetryRemoteMarkdownImageFailure(error, attempt: 0));
        Assert.False(StrataMarkdown.ShouldRetryRemoteMarkdownImageFailure(error, attempt: 1));
    }

    [Fact]
    public void ShouldRetryRemoteMarkdownImageFailure_DoesNotRetryPermanentStatus()
    {
        var error = new HttpRequestException(
            "Image not found.",
            null,
            HttpStatusCode.NotFound);

        Assert.False(StrataMarkdown.ShouldRetryRemoteMarkdownImageFailure(error, attempt: 0));
    }

    [Fact]
    public void InvalidImageData_IsARecoverableRenderFailure()
    {
        Assert.True(StrataMarkdown.IsRecoverableMarkdownImageFailure(
            new InvalidDataException("Unsupported image data.")));
        Assert.False(StrataMarkdown.IsTransientMarkdownImageFailure(
            new InvalidDataException("Unsupported image data.")));
        Assert.True(StrataMarkdown.IsTransientMarkdownImageFailure(
            new IOException("Temporary image read failure.")));
    }

    [Fact]
    public void TryReadMarkdownImageDimensions_ReadsPngAndBoundsDecodeSize()
    {
        Assert.True(StrataMarkdown.TryReadMarkdownImageDimensions(TinyPng, out var size));
        Assert.Equal(new PixelSize(1, 1), size);
        Assert.Equal(1, StrataMarkdown.CalculateMarkdownImageDecodeWidth(size));
        Assert.Equal(480, StrataMarkdown.CalculateMarkdownImageDecodeWidth(new PixelSize(12_000, 12_000)));
        Assert.Equal(6, StrataMarkdown.CalculateMarkdownImageDecodeWidth(new PixelSize(100, 8_000)));
    }

    [Fact]
    public void TryReadMarkdownImageDimensions_ReadsAdvertisedRasterFormats()
    {
        AssertDimensions(CreateJpegHeader(3, 2), 3, 2);
        AssertDimensions(CreateGifHeader(4, 5), 4, 5);
        AssertDimensions(CreateBmpHeader(6, 7), 6, 7);
        AssertDimensions(CreateWebpVp8XHeader(8, 9), 8, 9);
    }

    [Fact]
    public void ParseBlocks_StandaloneImageUsesDedicatedBlock()
    {
        var blocks = StrataMarkdown.ParseBlocks(
            "Before ![small](https://example.com/small.png) after\n\n![large](https://example.com/large.png)");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Image, blocks[1].Kind);
        Assert.Equal("large", blocks[1].Content);
        Assert.Equal("https://example.com/large.png", blocks[1].Language);
    }

    [Fact]
    public async Task LoadMarkdownImageAsync_DecodesLocalFile()
    {
        var path = CreateTempPng();
        try
        {
            await _fixture.Dispatch(() =>
            {
                using var bitmap = StrataMarkdown.LoadMarkdownImageAsync(path).GetAwaiter().GetResult();
                Assert.Equal(new PixelSize(1, 1), bitmap.PixelSize);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AppendFormattedInlines_ReplacesImagePlaceholderWithLoadedImage()
    {
        var path = CreateTempPng();
        Window? window = null;
        Image? image = null;
        StackPanel? loadingContent = null;

        try
        {
            await _fixture.Dispatch(() =>
            {
                var markdown = new StrataMarkdown();
                var textBlock = new SelectableTextBlock
                {
                    FontSize = 14,
                    Width = 480,
                    TextWrapping = TextWrapping.Wrap,
                    Inlines = new InlineCollection(),
                };

                markdown.AppendFormattedInlines(textBlock, $"Before ![fixture image]({path}) after");

                var container = Assert.Single(textBlock.Inlines!.OfType<InlineUIContainer>());
                var frame = Assert.IsType<Border>(container.Child);
                var content = Assert.IsType<Grid>(frame.Child);
                image = Assert.Single(content.Children.OfType<Image>());
                loadingContent = Assert.Single(content.Children.OfType<StackPanel>());
                var progress = Assert.Single(loadingContent.Children.OfType<ProgressBar>());
                Assert.True(progress.IsIndeterminate);

                window = new Window
                {
                    Width = 520,
                    Height = 220,
                    Content = textBlock,
                };
                window.Show();
            });

            var loaded = false;
            for (var attempt = 0; attempt < 100 && !loaded; attempt++)
            {
                loaded = await _fixture.Dispatch(() => image?.Source is not null);
                if (!loaded)
                    await Task.Delay(20);
            }

            Assert.True(loaded, "The inline markdown image did not finish loading.");
            Assert.False(await _fixture.Dispatch(() => loadingContent!.IsVisible));
        }
        finally
        {
            if (window is not null)
                await _fixture.Dispatch(window.Close);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedImageLoad_RemainsCachedAcrossMarkdownRebuilds()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"strata-markdown-invalid-image-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "not an image");

        try
        {
            await _fixture.Dispatch(async () =>
            {
                var markdown = new StrataMarkdown
                {
                    IsInline = true,
                    Markdown = $"![broken image]({path})",
                };
                var window = new Window
                {
                    Width = 520,
                    Height = 220,
                    Content = markdown,
                };

                try
                {
                    window.Show();
                    InvokeMarkdownRebuild(markdown);

                    var firstEntry = GetCachedImageEntry(markdown, path);
                    Assert.NotNull(firstEntry);
                    await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    {
                        await GetBitmapTask(firstEntry);
                    });
                    await Dispatcher.UIThread.InvokeAsync(
                        static () => { },
                        DispatcherPriority.Background);
                    Assert.Same(firstEntry, GetCachedImageEntry(markdown, path));

                    markdown.Markdown = $"![broken image]({path})\n\nStill rendering.";
                    InvokeMarkdownRebuild(markdown);

                    Assert.Same(firstEntry, GetCachedImageEntry(markdown, path));
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TransientImageFailure_RetriesAfterCacheCooldown()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"strata-markdown-missing-image-{Guid.NewGuid():N}.png");

        await _fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown
            {
                IsInline = true,
                Markdown = $"![missing image]({path})",
            };
            var window = new Window
            {
                Width = 520,
                Height = 220,
                Content = markdown,
            };

            try
            {
                window.Show();
                InvokeMarkdownRebuild(markdown);

                var firstEntry = GetCachedImageEntry(markdown, path);
                Assert.NotNull(firstEntry);
                await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                {
                    await GetBitmapTask(firstEntry);
                });
                Assert.False(GetCacheEntryRetryReady(firstEntry));

                SetCacheEntryRetryAt(firstEntry, DateTimeOffset.UtcNow.AddSeconds(-1));
                Assert.True(GetCacheEntryRetryReady(firstEntry));

                InvokeMarkdownRebuild(markdown);

                Assert.NotSame(firstEntry, GetCachedImageEntry(markdown, path));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task ExpiredImageFailure_RebuildsSkippedStreamingPrefix()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"strata-markdown-prefix-image-{Guid.NewGuid():N}.png");
        var initialMarkdown = $"""
            ![missing image]({path})

            ---

            ## One

            ---

            ## Two
            """;

        await _fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown
            {
                IsInline = true,
                Markdown = initialMarkdown,
            };
            var window = new Window
            {
                Width = 520,
                Height = 420,
                Content = markdown,
            };

            try
            {
                window.Show();
                InvokeMarkdownRebuild(markdown);

                var firstEntry = GetCachedImageEntry(markdown, path);
                Assert.NotNull(firstEntry);
                await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                {
                    await GetBitmapTask(firstEntry);
                });
                SetCacheEntryRetryAt(firstEntry, DateTimeOffset.UtcNow.AddSeconds(-1));

                markdown.Markdown = initialMarkdown + "\n\nAppended streaming tail.";
                InvokeMarkdownRebuild(markdown);

                Assert.NotSame(firstEntry, GetCachedImageEntry(markdown, path));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task StandaloneImages_RenderAsSeparateBlockControls()
    {
        var path = CreateTempPng();
        Window? window = null;
        StrataMarkdown? markdown = null;

        try
        {
            await _fixture.Dispatch(() =>
            {
                markdown = new StrataMarkdown
                {
                    IsInline = true,
                    Markdown = $"![first]({path})\n\n![second]({path})",
                };
                window = new Window
                {
                    Width = 720,
                    Height = 600,
                    Content = markdown,
                };
                window.Show();
            });

            Border[] frames = [];
            for (var attempt = 0; attempt < 100 && frames.Length != 2; attempt++)
            {
                frames = await _fixture.Dispatch(() =>
                    markdown!.GetVisualDescendants()
                        .OfType<Border>()
                        .Where(control => control.Classes.Contains("strata-md-image"))
                        .ToArray());
                if (frames.Length != 2)
                    await Task.Delay(20);
            }

            Assert.Equal(2, frames.Length);
            Assert.All(frames, frame => Assert.IsType<StackPanel>(frame.Parent));

            var nonOverlapping = false;
            for (var attempt = 0; attempt < 100 && !nonOverlapping; attempt++)
            {
                nonOverlapping = await _fixture.Dispatch(() =>
                    frames[1].Bounds.Top >= frames[0].Bounds.Bottom);
                if (!nonOverlapping)
                    await Task.Delay(20);
            }

            Assert.True(nonOverlapping, "Standalone image blocks must not overlap.");
        }
        finally
        {
            if (window is not null)
                await _fixture.Dispatch(window.Close);
            File.Delete(path);
        }
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"strata-markdown-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, TinyPng);
        return path;
    }

    private static void AssertDimensions(byte[] imageData, int width, int height)
    {
        Assert.True(StrataMarkdown.TryReadMarkdownImageDimensions(imageData, out var size));
        Assert.Equal(new PixelSize(width, height), size);
    }

    private static byte[] CreateJpegHeader(ushort width, ushort height)
    {
        var data = new byte[21];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        data[3] = 0xC0;
        data[4] = 0x00;
        data[5] = 0x11;
        data[6] = 0x08;
        data[7] = (byte)(height >> 8);
        data[8] = (byte)height;
        data[9] = (byte)(width >> 8);
        data[10] = (byte)width;
        return data;
    }

    private static byte[] CreateGifHeader(ushort width, ushort height)
    {
        var data = "GIF89a0000"u8.ToArray();
        data[6] = (byte)width;
        data[7] = (byte)(width >> 8);
        data[8] = (byte)height;
        data[9] = (byte)(height >> 8);
        return data;
    }

    private static byte[] CreateBmpHeader(int width, int height)
    {
        var data = new byte[26];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes(40u).CopyTo(data, 14);
        BitConverter.GetBytes(width).CopyTo(data, 18);
        BitConverter.GetBytes(height).CopyTo(data, 22);
        return data;
    }

    private static byte[] CreateWebpVp8XHeader(int width, int height)
    {
        var data = new byte[30];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8X"u8.CopyTo(data.AsSpan(12));
        WriteUInt24LittleEndian(data.AsSpan(24), width - 1);
        WriteUInt24LittleEndian(data.AsSpan(27), height - 1);
        return data;
    }

    private static void WriteUInt24LittleEndian(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private static object? GetCachedImageEntry(StrataMarkdown markdown, string imageTarget)
    {
        Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(imageTarget, out var source));
        var cacheField = typeof(StrataMarkdown).GetField(
            "_imageCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var cache = Assert.IsAssignableFrom<IDictionary>(cacheField?.GetValue(markdown));
        return cache[source.CacheKey];
    }

    private static Task<Bitmap> GetBitmapTask(object cacheEntry)
    {
        var bitmapTask = cacheEntry.GetType()
            .GetProperty("BitmapTask", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(cacheEntry);
        return Assert.IsAssignableFrom<Task<Bitmap>>(bitmapTask);
    }

    private static bool GetCacheEntryRetryReady(object cacheEntry)
    {
        var isRetryReady = cacheEntry.GetType()
            .GetProperty("IsRetryReady", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(cacheEntry);
        return Assert.IsType<bool>(isRetryReady);
    }

    private static void SetCacheEntryRetryAt(object cacheEntry, DateTimeOffset retryAt)
    {
        var retryAtField = cacheEntry.GetType().GetField(
            "_retryAtUtcTicks",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(retryAtField);
        retryAtField.SetValue(cacheEntry, retryAt.UtcDateTime.Ticks);
    }

    private static void InvokeMarkdownRebuild(StrataMarkdown markdown)
    {
        var rebuild = typeof(StrataMarkdown).GetMethod(
            "Rebuild",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(rebuild);
        rebuild.Invoke(markdown, null);
    }
}
