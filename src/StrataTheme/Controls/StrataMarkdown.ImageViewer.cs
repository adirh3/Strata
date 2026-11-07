using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

internal sealed record MarkdownImageFrameInfo(MarkdownImageSource Source, string Label, Image Image);

internal enum MarkdownImageAction
{
    Copy,
    Reveal,
    Open,
    CopyLocation,
    Save,
    Share,
}

public partial class StrataMarkdown
{
    /// <summary>
    /// The host's image actions (share, save the platform's way) for every markdown control inside the
    /// element it is set on. Without one, the viewer offers what the platform can do by itself.
    /// </summary>
    public static readonly AttachedProperty<IStrataImageActionHost?> ImageActionHostProperty =
        AvaloniaProperty.RegisterAttached<StrataMarkdown, Control, IStrataImageActionHost?>(
            "ImageActionHost",
            inherits: true);

    private static readonly AttachedProperty<MarkdownImageFrameInfo?> ImageFrameInfoProperty =
        AvaloniaProperty.RegisterAttached<StrataMarkdown, Border, MarkdownImageFrameInfo?>("ImageFrameInfo");

    public static IStrataImageActionHost? GetImageActionHost(Control element) =>
        element.GetValue(ImageActionHostProperty);

    public static void SetImageActionHost(Control element, IStrataImageActionHost? value) =>
        element.SetValue(ImageActionHostProperty, value);

    private IStrataImageActionHost? ImageActionHost => GetValue(ImageActionHostProperty);

    /// <summary>Phones and browsers: touch first, no hover, no right-click, no folders to show.</summary>
    internal static bool IsTouchFirstPlatform =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsBrowser();

    internal static bool OffersImageFileLocation(IStrataImageActionHost? host) =>
        host?.OffersFileLocation ?? !IsTouchFirstPlatform;

    internal static bool CanCopyMarkdownImage(IStrataImageActionHost? host) =>
        host?.CanCopyImage ?? !IsTouchFirstPlatform;

    internal static Bitmap DecodeMarkdownImageBytes(byte[] bytes, PixelSize? decodeBounds)
    {
        using var imageData = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        return DecodeMarkdownImage(imageData, decodeBounds);
    }

    /// <summary>
    /// Opens the viewer on this frame's picture. The other pictures of the same message come along, in
    /// reading order, so the viewer can step through them.
    /// </summary>
    private void OpenMarkdownImageViewer(Border frame)
    {
        if (_expandedImageViewer is not null
            || frame.GetValue(ImageFrameInfoProperty) is not { Image.Source: not null } opened)
        {
            return;
        }

        var items = new List<MarkdownImageFrameInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in this.GetVisualDescendants().OfType<Border>())
        {
            if (candidate.GetValue(ImageFrameInfoProperty) is { Image.Source: not null } info
                && seen.Add(info.Source.CacheKey))
            {
                items.Add(info);
            }
        }

        var index = items.FindIndex(item => item.Source.CacheKey == opened.Source.CacheKey);
        if (index < 0)
        {
            items.Insert(0, opened);
            index = 0;
        }

        var viewer = MarkdownImageViewer.Open(
            this,
            frame,
            items,
            index,
            UiLabels,
            ImageActionHost,
            (source, token) => ReadMarkdownImageBytesAsync(source, _imageDownloadGate, token));
        if (viewer is null)
            return;

        _expandedImageViewer = viewer;
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(_expandedImageViewer, viewer))
                _expandedImageViewer = null;
        };
    }

    private void RunFrameImageAction(Border frame, MarkdownImageAction action) =>
        _ = RunFrameImageActionAsync(frame, action);

    private async Task RunFrameImageActionAsync(Border frame, MarkdownImageAction action)
    {
        if (frame.GetValue(ImageFrameInfoProperty) is not { } info
            || TopLevel.GetTopLevel(frame) is not { } topLevel)
        {
            return;
        }

        var labels = UiLabels;
        var message = await MarkdownImageFiles.RunAsync(
            action,
            topLevel,
            info,
            cachedBytes: null,
            ImageActionHost,
            labels,
            (source, token) => ReadMarkdownImageBytesAsync(source, _imageDownloadGate, token));
        if (message is not null && frame.Child is Panel content)
            MarkdownImageFiles.ShowToast(content, message, "MarkdownImageFrameToast");
    }

    private void ShowMarkdownImageMenu(Border frame)
    {
        if (frame.GetValue(ImageFrameInfoProperty) is not { } info)
            return;

        var labels = UiLabels;
        var host = ImageActionHost;
        var source = info.Source;
        var offersLocation = OffersImageFileLocation(host);
        var isWebImage = source.RemoteUri is { } uri && MarkdownImageFiles.IsWebAddress(uri);
        var menu = new ContextMenu { Name = "MarkdownImageMenu" };

        menu.Items.Add(MenuItem("MarkdownImageMenuOpen", labels["imageFullScreen"], StrataIcons.Expand,
            () => OpenMarkdownImageViewer(frame)));
        if (offersLocation && source.LocalPath is not null)
        {
            menu.Items.Add(MenuItem("MarkdownImageMenuReveal", labels["imageShowInFolder"], StrataIcons.Folder,
                () => RunFrameImageAction(frame, MarkdownImageAction.Reveal)));
            if (MarkdownImageFiles.CanOpenExternally(source))
            {
                menu.Items.Add(MenuItem("MarkdownImageMenuOpenExternal", labels["imageOpenInApp"], StrataIcons.OpenExternal,
                    () => RunFrameImageAction(frame, MarkdownImageAction.Open)));
            }
        }
        else if (offersLocation && isWebImage)
        {
            menu.Items.Add(MenuItem("MarkdownImageMenuOpenExternal", labels["imageOpenInBrowser"], StrataIcons.Globe,
                () => RunFrameImageAction(frame, MarkdownImageAction.Open)));
        }

        menu.Items.Add(new Separator());
        if (CanCopyMarkdownImage(host))
        {
            menu.Items.Add(MenuItem("MarkdownImageMenuCopy", labels["imageCopy"], StrataIcons.Copy,
                () => RunFrameImageAction(frame, MarkdownImageAction.Copy)));
        }
        if (offersLocation && (source.LocalPath is not null || isWebImage))
        {
            menu.Items.Add(MenuItem(
                "MarkdownImageMenuCopyLocation",
                labels[source.LocalPath is not null ? "imageCopyPath" : "imageCopyLink"],
                source.LocalPath is not null ? StrataIcons.Document : StrataIcons.Globe,
                () => RunFrameImageAction(frame, MarkdownImageAction.CopyLocation)));
        }
        menu.Items.Add(MenuItem("MarkdownImageMenuSave", MarkdownImageFiles.SaveLabel(host, labels), StrataIcons.Download,
            () => RunFrameImageAction(frame, MarkdownImageAction.Save)));
        if (host?.CanShare == true)
        {
            menu.Items.Add(MenuItem("MarkdownImageMenuShare", labels["imageShare"], StrataIcons.Share,
                () => RunFrameImageAction(frame, MarkdownImageAction.Share)));
        }

        menu.Open(frame);

        static MenuItem MenuItem(string name, string header, Geometry icon, Action action)
        {
            var item = new MenuItem
            {
                Name = name,
                Header = header,
                Icon = new StrataLineIcon { Data = icon, Width = 16, Height = 16 },
            };
            item.Click += (_, _) => action();
            return item;
        }
    }
}

/// <summary>
/// The full-screen viewer of a message's pictures: the picture on a dark stage, its name and details,
/// what can be done with it, and the message's other pictures a swipe, an arrow key or a button away.
/// </summary>
/// <remarks>
/// It opens on the preview the transcript already shows, then swaps in the image's own resolution, read
/// once and kept for Copy, Save and Share while that image is shown. A touch hides or shows the chrome; a
/// click beside the picture, Escape, Back or a swipe up or down closes it.
/// </remarks>
internal sealed class MarkdownImageViewer
{
    private static readonly TimeSpan ToggleDelay = TimeSpan.FromMilliseconds(260);

    private readonly MarkdownImageFrameInfo[] _items;
    private readonly InteractiveLabels _labels;
    private readonly IStrataImageActionHost? _host;
    private readonly Func<MarkdownImageSource, CancellationToken, Task<byte[]>> _readBytes;
    private readonly PixelSize _decodeBounds;
    private readonly bool _showsFileLocation;
    private readonly SolidColorBrush _backdrop = new(Colors.Black);
    private readonly Image _image;
    private readonly StrataImageViewport _viewport;
    private readonly Grid _root;
    private readonly Control[] _chrome;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _counter;
    private readonly StackPanel _loading;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private readonly Button _close;
    private readonly Button? _previous;
    private readonly Button? _next;
    private readonly Button? _reveal;
    private readonly Button? _open;
    private readonly List<Button> _fileActions = [];
    private readonly StrataFullScreenOverlay? _overlay;
    private CancellationTokenSource? _load;
    private Bitmap? _fullBitmap;
    private byte[]? _bytes;
    private int _index = -1;
    private bool _chromeVisible = true;
    private bool _isActionRunning;
    private bool _isClosed;
    private IDisposable? _toggleTimer;
    private PointerType _lastPointerType = PointerType.Mouse;

    private MarkdownImageViewer(
        StrataMarkdown owner,
        Border frame,
        IReadOnlyList<MarkdownImageFrameInfo> items,
        int index,
        InteractiveLabels labels,
        IStrataImageActionHost? host,
        Func<MarkdownImageSource, CancellationToken, Task<byte[]>> readBytes)
    {
        _items = items.ToArray();
        _labels = labels;
        _host = host;
        _readBytes = readBytes;
        _showsFileLocation = StrataMarkdown.OffersImageFileLocation(host);
        var topLevel = TopLevel.GetTopLevel(frame);
        _decodeBounds = topLevel is Window window && window.Screens.ScreenFromWindow(window) is { } screen
            ? screen.Bounds.Size
            : PixelSize.FromSize(topLevel?.ClientSize ?? frame.Bounds.Size, topLevel?.RenderScaling ?? 1);
        var hasGallery = _items.Length > 1;
        var touchFirst = StrataMarkdown.IsTouchFirstPlatform;

        _image = new Image { Name = "ExpandedMarkdownImage", Stretch = Stretch.Uniform };
        _viewport = new StrataImageViewport(_image) { CanSwipeBetweenImages = hasGallery };
        _viewport.NavigateRequested += (_, step) => Navigate(step);
        _viewport.DismissRequested += (_, _) => Close();
        _viewport.DismissProgress += (_, progress) => OnDismissProgress(progress);
        _viewport.Tapped += OnViewportTapped;
        _viewport.AddHandler(InputElement.DoubleTappedEvent, (_, _) => CancelToggle(), handledEventsToo: true);

        // ── Header: what the picture is, and what can be done with it ──
        _title = new TextBlock { Name = "MarkdownImageTitle", TextTrimming = TextTrimming.CharacterEllipsis };
        _title.Classes.Add("strata-md-image-viewer-title");
        _subtitle = new TextBlock { Name = "MarkdownImageSubtitle", TextTrimming = TextTrimming.CharacterEllipsis };
        _subtitle.Classes.Add("strata-md-image-caption");
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        titles.Children.Add(_title);
        titles.Children.Add(_subtitle);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (host?.CanShare == true)
            tools.Children.Add(FileAction("MarkdownImageShare", StrataIcons.Share, labels["imageShare"], MarkdownImageAction.Share));
        tools.Children.Add(FileAction("MarkdownImageSave", StrataIcons.Download, MarkdownImageFiles.SaveLabel(host, labels), MarkdownImageAction.Save));
        if (StrataMarkdown.CanCopyMarkdownImage(host))
            tools.Children.Add(FileAction("MarkdownImageCopy", StrataIcons.Copy, labels["imageCopy"], MarkdownImageAction.Copy));
        if (StrataMarkdown.OffersImageFileLocation(host))
        {
            _reveal = FileAction("MarkdownImageReveal", StrataIcons.Folder, labels["imageShowInFolder"], MarkdownImageAction.Reveal);
            _open = FileAction("MarkdownImageOpenExternal", StrataIcons.OpenExternal, labels["imageOpenInApp"], MarkdownImageAction.Open);
            tools.Children.Add(_reveal);
            tools.Children.Add(_open);
        }
        _close = Tool("MarkdownImageClose", StrataIcons.Cross, labels["closeFullScreenShortcut"]);
        _close.Classes.Add("strata-md-image-viewer-close");
        AutomationProperties.SetName(_close, labels["closeFullScreen"]);
        _close.Click += (_, _) => Close();
        tools.Children.Add(_close);

        var headerContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        headerContent.Children.Add(titles);
        Grid.SetColumn(tools, 1);
        headerContent.Children.Add(tools);
        var header = new Border
        {
            Name = "MarkdownImageHeader",
            Child = headerContent,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(20, 12, 12, 36),
        };
        header.Classes.Add("strata-md-image-viewer-header");

        // ── Footer: where this picture sits among the message's, and the zoom ──
        _counter = new TextBlock { Name = "MarkdownImageCounter", VerticalAlignment = VerticalAlignment.Center, IsVisible = hasGallery };
        _counter.Classes.Add("strata-md-image-caption");
        var footerContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        footerContent.Children.Add(_counter);
        if (!touchFirst)
            footerContent.Children.Add(_viewport.CreateToolbar());
        var footer = new Border
        {
            Name = "MarkdownImageFooter",
            Child = footerContent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 20),
            IsVisible = hasGallery || !touchFirst,
        };
        footer.Classes.Add("strata-md-image-viewer-pill");

        _progress = new ProgressBar { IsIndeterminate = true };
        _progress.Classes.Add("strata-md-image-progress");
        _status = new TextBlock
        {
            Text = labels["imageLoadingFull"],
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.Classes.Add("strata-md-image-caption");
        _loading = new StackPanel
        {
            Name = "MarkdownImageLoading",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24, 0, 24, 80),
            IsHitTestVisible = false,
        };
        _loading.Children.Add(_progress);
        _loading.Children.Add(_status);

        _root = new Grid { Name = "MarkdownImageViewer", FlowDirection = owner.FlowDirection };
        _root.Children.Add(_viewport);
        var chrome = new List<Control> { header, footer };
        if (hasGallery && !touchFirst)
        {
            _previous = NavigationButton("MarkdownImagePrevious", StrataIcons.ChevronLeft, labels["previousImage"], HorizontalAlignment.Left, -1);
            _next = NavigationButton("MarkdownImageNext", StrataIcons.ChevronRight, labels["nextImage"], HorizontalAlignment.Right, 1);
            _root.Children.Add(_previous);
            _root.Children.Add(_next);
            chrome.Add(_previous);
            chrome.Add(_next);
        }
        _root.Children.Add(header);
        _root.Children.Add(_loading);
        _root.Children.Add(footer);
        _chrome = chrome.ToArray();
        foreach (var part in _chrome)
            part.Classes.Add("strata-md-image-viewer-chrome");

        _root.AddHandler(InputElement.PointerPressedEvent, (_, e) => _lastPointerType = e.Pointer.Type,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown);

        Show(index, 0);
        _overlay = StrataFullScreenOverlay.Open(
            owner,
            "MarkdownImageFullScreen",
            _root,
            () => _close.Focus(),
            _backdrop,
            ThemeVariant.Dark);
        if (_overlay is not null)
            _overlay.Closed += (_, _) => OnClosed();
    }

    /// <summary>Raised once the viewer has closed and let go of its images.</summary>
    public event EventHandler? Closed;

    public static MarkdownImageViewer? Open(
        StrataMarkdown owner,
        Border frame,
        IReadOnlyList<MarkdownImageFrameInfo> items,
        int index,
        InteractiveLabels labels,
        IStrataImageActionHost? host,
        Func<MarkdownImageSource, CancellationToken, Task<byte[]>> readBytes)
    {
        var viewer = new MarkdownImageViewer(owner, frame, items, index, labels, host, readBytes);
        if (viewer._overlay is not null)
            return viewer;
        viewer.OnClosed();
        return null;
    }

    /// <summary>Whether the viewer shows, or can step to, the image with this cache key.</summary>
    public bool Shows(string cacheKey) => _items.Any(item => item.Source.CacheKey == cacheKey);

    public void Close(bool restoreFocus = true) => _overlay?.Close(restoreFocus);

    private MarkdownImageFrameInfo Current => _items[_index];

    private void Show(int index, int direction)
    {
        CancelLoad();
        _index = index;
        var item = Current;
        _bytes = null;
        var previous = _fullBitmap;
        _fullBitmap = null;
        _image.Source = item.Image.Source;
        previous?.Dispose();
        _viewport.Fit();
        _viewport.SlideIn(direction);

        _title.Text = item.Label;
        _subtitle.Text = MarkdownImageFiles.Describe(item.Source, null, _showsFileLocation);
        AutomationProperties.SetName(_image, item.Label);
        _counter.Text = _labels.Format("imagePosition", index + 1, _items.Length);
        KeepFocusInViewer(_previous, _next);
        if (_previous is not null)
            _previous.IsEnabled = index > 0;
        if (_next is not null)
            _next.IsEnabled = index < _items.Length - 1;
        var isWebImage = item.Source.RemoteUri is { } uri && MarkdownImageFiles.IsWebAddress(uri);
        if (_reveal is not null)
            _reveal.IsVisible = item.Source.LocalPath is not null;
        if (_open is not null)
        {
            _open.IsVisible = MarkdownImageFiles.CanOpenExternally(item.Source);
            var openLabel = _labels[item.Source.LocalPath is not null ? "imageOpenInApp" : "imageOpenInBrowser"];
            AutomationProperties.SetName(_open, openLabel);
            ToolTip.SetTip(_open, openLabel);
        }

        _progress.IsVisible = true;
        _status.Text = _labels["imageLoadingFull"];
        AutomationProperties.SetHelpText(_status, null);
        _loading.IsVisible = true;
        var load = new CancellationTokenSource();
        _load = load;
        _ = LoadFullAsync(item, load.Token);
    }

    private async Task LoadFullAsync(MarkdownImageFrameInfo item, CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        try
        {
            var bytes = await _readBytes(item.Source, cancellationToken).ConfigureAwait(false);
            bitmap = await Task.Run(
                () => StrataMarkdown.DecodeMarkdownImageBytes(bytes, _decodeBounds),
                cancellationToken).ConfigureAwait(false);
            var shown = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                _bytes = bytes;
                _fullBitmap = shown;
                _image.Source = shown;
                bitmap = null;
                _loading.IsVisible = false;
                _subtitle.Text = MarkdownImageFiles.Describe(item.Source, bytes, _showsFileLocation);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is OperationCanceledException || StrataMarkdown.IsRecoverableMarkdownImageFailure(ex))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                _progress.IsVisible = false;
                _status.Text = _labels["imageFullUnavailable"];
                AutomationProperties.SetHelpText(_status, ex.Message);
            });
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private void Navigate(int step)
    {
        var next = _index + step;
        if (next < 0 || next >= _items.Length)
        {
            _viewport.Settle();
            return;
        }
        Show(next, step);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var commandModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var forward = _root.FlowDirection == FlowDirection.RightToLeft ? Key.Left : Key.Right;
        var back = forward == Key.Right ? Key.Left : Key.Right;
        if (e.Key == forward && e.KeyModifiers == KeyModifiers.None)
            Navigate(1);
        else if (e.Key == back && e.KeyModifiers == KeyModifiers.None)
            Navigate(-1);
        else if (e.Key == Key.C && e.KeyModifiers == commandModifier && StrataMarkdown.CanCopyMarkdownImage(_host))
            RunAction(MarkdownImageAction.Copy);
        else if (e.Key == Key.S && e.KeyModifiers == commandModifier)
            RunAction(MarkdownImageAction.Save);
        else
            return;
        e.Handled = true;
    }

    private void OnViewportTapped(object? sender, TappedEventArgs e)
    {
        if (_lastPointerType == PointerType.Mouse)
        {
            // As in a photo lightbox, a click beside the picture puts it away.
            if (!_viewport.ImageBounds.Contains(e.GetPosition(_viewport)))
                Close();
            return;
        }

        // A single touch shows or hides the chrome; it waits a moment so a double-tap can zoom instead.
        CancelToggle();
        _toggleTimer = DispatcherTimer.RunOnce(() =>
        {
            _toggleTimer = null;
            SetChromeVisible(!_chromeVisible);
        }, ToggleDelay);
    }

    private void CancelToggle()
    {
        _toggleTimer?.Dispose();
        _toggleTimer = null;
    }

    private void SetChromeVisible(bool visible)
    {
        _chromeVisible = visible;
        foreach (var part in _chrome)
        {
            part.Opacity = visible ? 1 : 0;
            part.IsHitTestVisible = visible;
        }
    }

    private void OnDismissProgress(double progress)
    {
        _backdrop.Opacity = 1 - progress * 0.7;
        if (!_chromeVisible)
            return;
        foreach (var part in _chrome)
            part.Opacity = 1 - progress;
    }

    private void RunAction(MarkdownImageAction action) => _ = RunActionAsync(action);

    private async Task RunActionAsync(MarkdownImageAction action)
    {
        if (_isActionRunning || _isClosed || TopLevel.GetTopLevel(_root) is not { } topLevel)
            return;

        _isActionRunning = true;
        KeepFocusInViewer([.. _fileActions]);
        foreach (var button in _fileActions)
            button.IsEnabled = false;
        try
        {
            var message = await MarkdownImageFiles.RunAsync(
                action,
                topLevel,
                Current,
                _bytes,
                _host,
                _labels,
                _readBytes);
            if (message is not null && !_isClosed)
                MarkdownImageFiles.ShowToast(_root, message, "MarkdownImageToast", bottomMargin: 84);
        }
        finally
        {
            _isActionRunning = false;
            foreach (var button in _fileActions)
                button.IsEnabled = true;
        }
    }

    /// <summary>
    /// A button about to be turned off gives focus to the picture first: a disabled button drops focus out
    /// of the viewer, and with it the arrow keys and shortcuts.
    /// </summary>
    private void KeepFocusInViewer(params Button?[] buttons)
    {
        if (buttons.Any(button => button?.IsFocused == true))
            _viewport.Focus();
    }

    private void CancelLoad()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
    }

    private void OnClosed()
    {
        if (_isClosed)
            return;
        _isClosed = true;
        CancelToggle();
        CancelLoad();
        _image.Source = null;
        _fullBitmap?.Dispose();
        _fullBitmap = null;
        _bytes = null;
        Closed?.Invoke(this, EventArgs.Empty);
        Closed = null;
    }

    private Button FileAction(string name, Geometry icon, string label, MarkdownImageAction action)
    {
        var button = Tool(name, icon, label);
        button.Click += (_, _) => RunAction(action);
        _fileActions.Add(button);
        return button;
    }

    private static Button Tool(string name, Geometry icon, string label)
    {
        var button = new Button
        {
            Name = name,
            Content = new StrataLineIcon { Data = icon, Width = 20, Height = 20 },
        };
        button.Classes.Add("strata-md-image-viewer-tool");
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        ToolTip.SetPlacement(button, PlacementMode.Bottom);
        return button;
    }

    private Button NavigationButton(string name, Geometry icon, string label, HorizontalAlignment side, int step)
    {
        var button = Tool(name, icon, label);
        button.Classes.Add("strata-md-image-viewer-nav");
        button.HorizontalAlignment = side;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(16, 0);
        ToolTip.SetPlacement(button, PlacementMode.Top);
        button.Click += (_, _) => Navigate(step);
        return button;
    }
}

/// <summary>What can be done with a markdown image: copy it, show or open its file, save or share it.</summary>
internal static class MarkdownImageFiles
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    public static bool IsWebAddress(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;

    /// <summary>
    /// A web picture opens in the browser; a local one in its default app only when its name says it is
    /// a picture. The viewer reads pictures by their content, and markdown is often model output, so a
    /// picture named "setup.exe" must never be launched.
    /// </summary>
    public static bool CanOpenExternally(MarkdownImageSource source) =>
        source.LocalPath is { } path
            ? ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            : source.RemoteUri is { } uri && IsWebAddress(uri);

    public static string SaveLabel(IStrataImageActionHost? host, InteractiveLabels labels) =>
        (host?.SaveStyle ?? StrataImageSaveStyle.SaveAs) switch
        {
            StrataImageSaveStyle.Download => labels["imageDownload"],
            StrataImageSaveStyle.SaveToPhotos => labels["imageSave"],
            _ => labels["imageSaveCopy"],
        };

    /// <summary>
    /// Runs an action and returns a short word about how it went, for a toast, or null when nothing needs
    /// saying: a share sheet or a folder that opened speaks for itself, and a cancelled dialog needs no reply.
    /// </summary>
    public static async Task<string?> RunAsync(
        MarkdownImageAction action,
        TopLevel topLevel,
        MarkdownImageFrameInfo image,
        byte[]? cachedBytes,
        IStrataImageActionHost? host,
        InteractiveLabels labels,
        Func<MarkdownImageSource, CancellationToken, Task<byte[]>> readBytes)
    {
        var source = image.Source;
        var bytes = cachedBytes;
        async Task<byte[]> ReadAsync(CancellationToken token) =>
            bytes ??= await readBytes(source, token).ConfigureAwait(true);

        try
        {
            switch (action)
            {
                case MarkdownImageAction.Copy:
                {
                    if (topLevel.Clipboard is not { } clipboard)
                        return labels["imageCopyFailed"];
                    var data = await ReadAsync(CancellationToken.None);
                    // The clipboard may render the picture later, when something pastes it, so it keeps it.
                    var bitmap = new Bitmap(new MemoryStream(data, writable: false));
                    var transfer = new DataTransfer();
                    transfer.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
                    await clipboard.SetDataAsync(transfer);
                    return labels["imageCopied"];
                }

                case MarkdownImageAction.CopyLocation:
                {
                    if (topLevel.Clipboard is not { } clipboard)
                        return labels["imageCopyFailed"];
                    var transfer = new DataTransfer();
                    transfer.Add(DataTransferItem.CreateText(source.LocalPath ?? source.RemoteUri?.AbsoluteUri ?? string.Empty));
                    await clipboard.SetDataAsync(transfer);
                    return labels[source.LocalPath is not null ? "imagePathCopied" : "imageLinkCopied"];
                }

                case MarkdownImageAction.Reveal:
                    return source.LocalPath is { } folderPath && await RevealInFolderAsync(topLevel, folderPath)
                        ? null
                        : labels["imageOpenFailed"];

                case MarkdownImageAction.Open:
                {
                    if (!CanOpenExternally(source))
                        return labels["imageOpenFailed"];
                    var launched = source.LocalPath is { } localPath
                        ? await topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(localPath))
                        : await topLevel.Launcher.LaunchUriAsync(source.RemoteUri!);
                    return launched ? null : labels["imageOpenFailed"];
                }

                case MarkdownImageAction.Share:
                {
                    if (host is not { CanShare: true })
                        return labels["imageShareFailed"];
                    var file = await CreateFileAsync(image, ReadAsync);
                    return await host.ShareAsync(file, CancellationToken.None) ? null : labels["imageShareFailed"];
                }

                case MarkdownImageAction.Save:
                {
                    var file = await CreateFileAsync(image, ReadAsync);
                    var style = host?.SaveStyle ?? StrataImageSaveStyle.SaveAs;
                    if (style == StrataImageSaveStyle.SaveAs)
                        return await SaveAsAsync(topLevel, file, labels);
                    if (!await host!.SaveAsync(file, CancellationToken.None))
                        return labels["imageSaveFailed"];
                    return labels[style == StrataImageSaveStyle.SaveToPhotos ? "imageSavedToPhotos" : "imageDownloaded"];
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine($"[StrataMarkdown] Image action {action} failed: {ex}");
            return labels[action switch
            {
                MarkdownImageAction.Copy or MarkdownImageAction.CopyLocation => "imageCopyFailed",
                MarkdownImageAction.Save => "imageSaveFailed",
                MarkdownImageAction.Share => "imageShareFailed",
                _ => "imageOpenFailed",
            }];
        }

        return null;
    }

    private static async Task<StrataImageFile> CreateFileAsync(
        MarkdownImageFrameInfo image,
        Func<CancellationToken, Task<byte[]>> readBytes)
    {
        var name = NameFromSource(image.Source);
        if (name is null)
        {
            var bytes = await readBytes(CancellationToken.None);
            name = NameFromLabel(image.Label, bytes);
        }
        return new StrataImageFile(name, image.Source.LocalPath, image.Source.RemoteUri, readBytes);
    }

    private static async Task<string?> SaveAsAsync(TopLevel topLevel, StrataImageFile file, InteractiveLabels labels)
    {
        if (!topLevel.StorageProvider.CanSave)
            return labels["imageSaveFailed"];

        var bytes = await file.ReadAllBytesAsync();
        var extension = Path.GetExtension(file.FileName);
        var format = FormatName(bytes) ?? extension.TrimStart('.').ToUpperInvariant();
        var destination = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = labels["imageSaveTitle"],
            SuggestedFileName = file.FileName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(format)
                {
                    Patterns = [$"*{extension}"],
                    MimeTypes = [MimeType(extension)],
                },
            ],
        });
        if (destination is null)
            return null;

        using (destination)
        {
            await using var stream = await destination.OpenWriteAsync();
            if (stream.CanSeek)
                stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        return labels["imageSaved"];
    }

    /// <summary>Shows the file selected in its folder where the platform can; otherwise opens the folder.</summary>
    private static async Task<bool> RevealInFolderAsync(TopLevel topLevel, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return false;

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = true });
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            var reveal = new ProcessStartInfo("open") { UseShellExecute = false };
            reveal.ArgumentList.Add("-R");
            reveal.ArgumentList.Add(fullPath);
            Process.Start(reveal);
            return true;
        }

        return Path.GetDirectoryName(fullPath) is { } folder
            && await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    /// <summary>A word about an action, floating low over the picture for a moment.</summary>
    public static void ShowToast(Panel host, string message, string name, double bottomMargin = 10)
    {
        foreach (var existing in host.Children.OfType<Border>().Where(child => child.Name == name).ToArray())
            host.Children.Remove(existing);

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        var toast = new Border
        {
            Name = name,
            Child = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12, 0, 12, bottomMargin),
            IsHitTestVisible = false,
        };
        toast.Classes.Add("strata-md-image-toast");
        AutomationProperties.SetName(toast, message);
        host.Children.Add(toast);
        DispatcherTimer.RunOnce(() => toast.Opacity = 0, TimeSpan.FromSeconds(1.8));
        DispatcherTimer.RunOnce(() => host.Children.Remove(toast), TimeSpan.FromSeconds(2.2));
    }

    /// <summary>"example.com · PNG · 1920 × 1080 · 2.4 MB": where it is from, and what it is once read.</summary>
    public static string Describe(MarkdownImageSource source, byte[]? bytes, bool showFolder = true)
    {
        var parts = new List<string>();
        if (source.RemoteUri is { } uri && IsWebAddress(uri))
            parts.Add(uri.Host);
        else if (showFolder && source.LocalPath is { } path && Path.GetDirectoryName(path) is { Length: > 0 } folder)
            parts.Add(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : folder);

        if (bytes is not null)
        {
            if (FormatName(bytes) is { } format)
                parts.Add(format);
            if (StrataMarkdown.TryReadMarkdownImageDimensions(bytes, out var size))
                parts.Add(string.Create(CultureInfo.CurrentCulture, $"{size.Width:N0} × {size.Height:N0}"));
            parts.Add(FormatSize(bytes.LongLength));
        }
        return string.Join(" · ", parts);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024d * 1024):0.#} MB"),
        >= 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024d:0} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
    };

    public static string? NameFromSource(MarkdownImageSource source)
    {
        var name = source.LocalPath is { } path
            ? Path.GetFileName(path)
            : source.RemoteUri is { } uri && IsWebAddress(uri)
                ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath))
                : null;
        return name is { Length: > 0 }
               && ImageExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            ? Sanitize(name)
            : null;
    }

    public static string NameFromLabel(string label, ReadOnlySpan<byte> bytes)
    {
        var stem = Sanitize(label).Trim().TrimEnd('.');
        if (stem.Length > 64)
            stem = stem[..64].TrimEnd();
        if (stem.Length == 0)
            stem = "image";
        return stem + Extension(bytes);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(Array.IndexOf(invalid, character) >= 0 || char.IsControl(character) ? '_' : character);
        return builder.ToString();
    }

    public static string Extension(ReadOnlySpan<byte> data) => FormatName(data) switch
    {
        "JPEG" => ".jpg",
        "GIF" => ".gif",
        "BMP" => ".bmp",
        "WebP" => ".webp",
        _ => ".png",
    };

    public static string? FormatName(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "PNG";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "JPEG";
        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            return "GIF";
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            return "BMP";
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return "WebP";
        return null;
    }

    public static string MimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "image/png",
    };
}
