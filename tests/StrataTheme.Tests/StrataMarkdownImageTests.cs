using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
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
        Assert.False(StrataMarkdown.TryResolveMarkdownImageSource(target, out _, Path.GetTempPath()));
    }

    [Theory]
    [InlineData("chart.png", "chart.png")]
    [InlineData("images/chart.png", "images/chart.png")]
    [InlineData("../chart.png", "../chart.png")]
    [InlineData("chart%20one.png", "chart one.png")]
    public void DocumentRelativeImagesUseTheirBaseDirectory(string target, string relativePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "strata-preview", "document");
        Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var source, directory));
        Assert.Equal(Path.GetFullPath(relativePath, directory), source.LocalPath);
        Assert.Null(source.RemoteUri);
    }

    [Fact]
    public void DocumentBaseDoesNotFallBackToTheWorkingDirectory()
    {
        var target = $"strata-preview-{Guid.NewGuid():N}.png";
        var workingDirectoryImage = Path.Combine(Directory.GetCurrentDirectory(), target);
        var documentDirectory = Path.Combine(Path.GetTempPath(), $"strata-preview-{Guid.NewGuid():N}");
        File.WriteAllBytes(workingDirectoryImage, TinyPng);
        try
        {
            Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var original));
            Assert.Equal(workingDirectoryImage, original.LocalPath);
            Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var source, documentDirectory));
            Assert.Equal(Path.Combine(documentDirectory, target), source.LocalPath);
            Assert.False(File.Exists(source.LocalPath));
        }
        finally
        {
            File.Delete(workingDirectoryImage);
        }
    }

    [Fact]
    public void DocumentBasePreservesAbsoluteAndRemoteImages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "strata-preview", "document");
        var absolutePath = Path.Combine(Path.GetTempPath(), "absolute-image.png");
        foreach (var target in new[] { absolutePath, new Uri(absolutePath).AbsoluteUri, "https://example.com/image.png" })
        {
            Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var original));
            Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var documentSource, directory));
            Assert.Equal(original, documentSource);
        }
        Assert.False(StrataMarkdown.TryResolveMarkdownImageSource("file://attacker/share/image.png", out _, directory));
        Assert.False(StrataMarkdown.TryResolveMarkdownImageSource(@"\\attacker\share\image.png", out _, directory));
    }

    [Fact]
    public async Task ChangingDocumentBaseRebuildsImagesWithoutChangingMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"strata-document-images-{Guid.NewGuid():N}");
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        var firstImage = Path.Combine(first, "chart.png");
        var secondImage = Path.Combine(second, "chart.png");
        File.WriteAllBytes(firstImage, TinyPng);
        File.WriteAllBytes(secondImage, TinyPng);
        try
        {
            await _fixture.Dispatch(() =>
            {
                var markdown = new StrataMarkdown
                {
                    ImageBaseDirectory = first,
                    Markdown = "![Chart](chart.png)\n\nInline ![Chart](chart.png) example."
                };
                var window = new Window { Content = markdown };
                try
                {
                    window.Show();
                    InvokeMarkdownRebuild(markdown);
                    var firstEntry = GetCachedImageEntry(markdown, firstImage);
                    markdown.ImageBaseDirectory = second;
                    InvokeMarkdownRebuild(markdown);
                    var secondEntry = GetCachedImageEntry(markdown, secondImage);
                    Assert.NotSame(firstEntry, secondEntry);
                    InvokeMarkdownRebuild(markdown);
                    Assert.Same(secondEntry, GetCachedImageEntry(markdown, secondImage));
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    public void GetRemoteMarkdownImageHostKey_DoesNotThrottleBrowserBlobUrls()
    {
        Assert.Null(StrataMarkdown.GetRemoteMarkdownImageHostKey(
            new Uri("blob:http://localhost/image-id")));
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

    [Theory]
    [InlineData(12_000, 12_000, 1920, 1080, 1080)]
    [InlineData(4_000, 2_000, 1920, 1080, 1920)]
    [InlineData(200, 100, 1920, 1080, 200)]
    public void ExpandedImageDecode_UsesScreenBoundsWithoutUpscaling(
        int width, int height, int screenWidth, int screenHeight, int expectedWidth)
    {
        Assert.Equal(expectedWidth, StrataMarkdown.CalculateMarkdownImageDecodeWidth(
            new PixelSize(width, height),
            new PixelSize(screenWidth, screenHeight)));
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

        try
        {
            await _fixture.Dispatch(async () =>
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
                var image = Assert.Single(content.Children.OfType<Image>());
                var loadingContent = Assert.Single(content.Children.OfType<StackPanel>());
                var progress = Assert.Single(loadingContent.Children.OfType<ProgressBar>());
                Assert.True(progress.IsIndeterminate);

                var window = new Window
                {
                    Width = 520,
                    Height = 220,
                    Content = textBlock,
                };

                try
                {
                    window.Show();

                    var loaded = false;
                    for (var attempt = 0; attempt < 100 && !loaded; attempt++)
                    {
                        loaded = image.Source is not null;
                        if (!loaded)
                            await Task.Delay(20);
                    }

                    Assert.True(loaded, "The inline markdown image did not finish loading.");
                    Assert.False(loadingContent.IsVisible);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpandedImage_FillsWindowAndRestoresKeyboardFocusAndWindowState(bool inText)
    {
        var path = CreateTempPng();
        try
        {
            await _fixture.Dispatch(async () =>
            {
                var markdownText = $"![fixture image]({path})";
                var window = CreateImageViewerWindow(inText ? $"Before {markdownText} after" : markdownText);
                try
                {
                    await WaitForImageViewerAsync(window, () =>
                        window.GetVisualDescendants().OfType<Image>()
                            .Any(image => image.Classes.Contains("strata-md-image-content") && image.Source is not null));
                    var expand = FindImageViewerControl<Button>(window, "MarkdownImageExpand");
                    Assert.True(expand.IsVisible);
                    Assert.Equal("View image full screen", AutomationProperties.GetName(expand));
                    var layer = Assert.IsType<OverlayLayer>(OverlayLayer.GetOverlayLayer(expand));

                    foreach (var previousState in new[] { WindowState.Normal, WindowState.Maximized, WindowState.FullScreen })
                    {
                        window.WindowState = previousState;
                        Assert.True(expand.Focus());
                        PressImageViewerKey(window, previousState == WindowState.FullScreen ? Key.Space : Key.Enter);
                        PumpImageViewer(window);
                        Assert.Equal(WindowState.FullScreen, window.WindowState);
                        var overlay = FindImageViewerControl<Border>(window, "MarkdownImageFullScreen");
                        var image = FindImageViewerControl<Image>(window, "ExpandedMarkdownImage");
                        var close = FindImageViewerControl<Button>(window, "MarkdownImageClose");
                        Assert.Equal(layer.Bounds.Size, overlay.Bounds.Size);
                        Assert.Equal(window.ClientSize, overlay.Bounds.Size);
                        Assert.Equal(Stretch.Uniform, image.Stretch);
                        Assert.True(image.Bounds.Height > 500);
                        Assert.NotNull(image.Source);
                        Assert.Same(close, window.FocusManager!.GetFocusedElement());

                        window.Width = 1040;
                        window.Height = 780;
                        PumpImageViewer(window);
                        Assert.Equal(window.ClientSize, overlay.Bounds.Size);

                        if (previousState == WindowState.Maximized)
                            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else
                            PressImageViewerKey(window, Key.Escape);
                        PumpImageViewer(window);
                        Assert.Empty(layer.Children);
                        Assert.Null(image.Source);
                        Assert.Equal(previousState, window.WindowState);
                        Assert.Same(expand, window.FocusManager.GetFocusedElement());
                    }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingExpandedImage_ClosesViewerAndReleasesImage(bool detachOwner)
    {
        var path = CreateTempPng();
        try
        {
            await _fixture.Dispatch(async () =>
            {
                var window = CreateImageViewerWindow($"![fixture image]({path})");
                try
                {
                    await WaitForImageViewerAsync(window, () =>
                        FindImageViewerControl<Button>(window, "MarkdownImageExpand").IsVisible);
                    window.WindowState = WindowState.Maximized;
                    var expand = FindImageViewerControl<Button>(window, "MarkdownImageExpand");
                    expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpImageViewer(window);
                    var layer = Assert.IsType<OverlayLayer>(OverlayLayer.GetOverlayLayer(expand));
                    var image = FindImageViewerControl<Image>(window, "ExpandedMarkdownImage");
                    Assert.Equal(WindowState.FullScreen, window.WindowState);

                    if (detachOwner)
                        window.Content = null;
                    else
                    {
                        var markdown = Assert.IsType<StrataMarkdown>(window.Content);
                        markdown.Markdown = "The image has been removed.";
                        InvokeMarkdownRebuild(markdown);
                    }
                    PumpImageViewer(window);
                    Assert.Empty(layer.Children);
                    Assert.Null(image.Source);
                    Assert.Equal(WindowState.Maximized, window.WindowState);
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
    public async Task ExpandedImageLoadFailure_ShowsErrorAndRetainsPreview()
    {
        var path = CreateTempPng();
        try
        {
            await _fixture.Dispatch(async () =>
            {
                var window = CreateImageViewerWindow($"![fixture image]({path})");
                try
                {
                    await WaitForImageViewerAsync(window, () =>
                        FindImageViewerControl<Button>(window, "MarkdownImageExpand").IsVisible);
                    var preview = window.GetVisualDescendants().OfType<Image>()
                        .Single(image => image.Classes.Contains("strata-md-image-content")).Source;
                    File.Delete(path);
                    FindImageViewerControl<Button>(window, "MarkdownImageExpand")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpImageViewer(window);
                    await WaitForImageViewerAsync(window, () =>
                        window.GetVisualDescendants().OfType<TextBlock>()
                            .Any(text => text.Text == "Full-resolution image unavailable. Showing the inline preview."));
                    Assert.Same(preview, FindImageViewerControl<Image>(window, "ExpandedMarkdownImage").Source);
                    FindImageViewerControl<Button>(window, "MarkdownImageClose")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpImageViewer(window);
                    Assert.Equal(WindowState.Normal, window.WindowState);
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
    public async Task ClosingAndReopeningDuringImageLoad_CancelsBothRequestsWithoutUpdatingClosedViewers()
    {
        var path = CreateTempPng();
        try
        {
            await _fixture.Dispatch(async () =>
            {
                var window = CreateImageViewerWindow(string.Empty);
                var markdown = Assert.IsType<StrataMarkdown>(window.Content);
                var gate = Assert.IsType<SemaphoreSlim>(typeof(StrataMarkdown)
                    .GetField("_imageDownloadGate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(markdown));
                var permits = gate.CurrentCount;
                for (var i = 0; i < permits; i++)
                    await gate.WaitAsync();
                var released = false;
                try
                {
                    // Supply a cached preview while larger downloads wait before making any HTTP request.
                    const string target = "https://example.com/pending-image.png";
                    Assert.True(StrataMarkdown.TryResolveMarkdownImageSource(target, out var source));
                    var cache = Assert.IsAssignableFrom<IDictionary>(typeof(StrataMarkdown)
                        .GetField("_imageCache", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(markdown));
                    var entryType = typeof(StrataMarkdown)
                        .GetNestedType("MarkdownImageCacheEntry", BindingFlags.NonPublic);
                    Assert.NotNull(entryType);
                    Func<MarkdownImageSource, CancellationToken, Task<Bitmap>> loadPreview =
                        (_, token) => StrataMarkdown.LoadMarkdownImageAsync(path, token);
                    var entry = Activator.CreateInstance(entryType, source, loadPreview);
                    Assert.NotNull(entry);
                    cache.Add(source.CacheKey, entry);
                    markdown.Markdown = $"![fixture image]({target})";
                    InvokeMarkdownRebuild(markdown);
                    await WaitForImageViewerAsync(window, () =>
                        FindImageViewerControl<Button>(window, "MarkdownImageExpand").IsVisible);
                    var preview = window.GetVisualDescendants().OfType<Image>()
                        .Single(image => image.Classes.Contains("strata-md-image-content")).Source;
                    var expand = FindImageViewerControl<Button>(window, "MarkdownImageExpand");
                    var layer = Assert.IsType<OverlayLayer>(OverlayLayer.GetOverlayLayer(expand));

                    expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpImageViewer(window);
                    var firstImage = FindImageViewerControl<Image>(window, "ExpandedMarkdownImage");
                    Assert.Same(preview, firstImage.Source);
                    FindImageViewerControl<Button>(window, "MarkdownImageClose")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpImageViewer(window);
                    var secondImage = FindImageViewerControl<Image>(window, "ExpandedMarkdownImage");
                    Assert.NotSame(firstImage, secondImage);
                    Assert.Null(firstImage.Source);
                    Assert.Same(preview, secondImage.Source);
                    Assert.Single(layer.Children);
                    Assert.Equal(WindowState.FullScreen, window.WindowState);

                    PressImageViewerKey(window, Key.Escape);
                    PumpImageViewer(window);
                    Assert.Null(secondImage.Source);
                    Assert.Empty(layer.Children);
                    Assert.Equal(WindowState.Normal, window.WindowState);
                    gate.Release(permits);
                    released = true;
                    Assert.Equal(permits, gate.CurrentCount);
                }
                finally
                {
                    window.Close();
                    if (!released)
                        gate.Release(permits);
                }
            });
        }
        finally
        {
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
                    Assert.False(markdown.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Name == "MarkdownImageExpand").IsVisible);

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

    private static Window CreateImageViewerWindow(string markdownText)
    {
        var markdown = new StrataMarkdown { IsInline = true, Markdown = markdownText };
        var window = new Window
        {
            Width = 1000,
            Height = 750,
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner }
                }.RegisterInNameScope(scope)
            }),
            Content = markdown
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme.Tests/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Show();
        window.ApplyTemplate();
        InvokeMarkdownRebuild(markdown);
        PumpImageViewer(window);
        return window;
    }

    private static void PumpImageViewer(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitForImageViewerAsync(Window window, Func<bool> ready)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            PumpImageViewer(window);
            if (ready())
                return;
            await Task.Delay(20);
        }
        Assert.True(ready(), "The markdown image viewer did not reach its expected state.");
    }

    private static T FindImageViewerControl<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void PressImageViewerKey(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
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
