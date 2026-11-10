using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>A link under the pointer: its run, where it leads, and the line box it was found in.</summary>
internal readonly record struct MarkdownLinkHit(Run Run, string Target, Rect Bounds);

/// <summary>
/// How links feel under the pointer: a forgiving hit area around each link, a soft highlight while
/// it is hovered, and — after the pointer rests on it — a card that says where it leads (the page's
/// own title, summary and picture when the host lets views read linked pages), with Open and Copy.
/// </summary>
public partial class StrataMarkdown
{
    // A link takes clicks this close to its text, so short labels such as [1] are easy to hit.
    internal const double LinkHitSlopX = 6;
    internal const double LinkHitSlopY = 4;
    private const double LinkCardWidth = 340;
    private const int MaxLinkCardPictures = 8;
    private const int MaxLinkCardIcons = 32;
    private static readonly TimeSpan LinkCardOpenDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan LinkCardSwitchDelay = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan LinkCardCloseDelay = TimeSpan.FromMilliseconds(220);
    private static readonly PixelSize LinkCardPictureBounds = new(680, 340);
    private static readonly PixelSize LinkCardIconBounds = new(64, 64);
    private static readonly Dictionary<string, Task<Bitmap?>> LinkCardPictures = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Task<Bitmap?>> LinkCardIcons = new(StringComparer.Ordinal);

    private IBrush? _linkHoverBrush;
    private InlineCodeLayer? _hoveredLinkLayer;
    private Run? _suppressedLinkCardRun;
    private SelectableTextBlock? _pendingLinkCardBlock;
    private MarkdownLinkHit? _pendingLinkCard;
    private IDisposable? _linkCardOpenTimer;
    private IDisposable? _linkCardCloseTimer;
    private Popup? _linkCard;
    private SelectableTextBlock? _linkCardSource;
    private Run? _linkCardRun;
    private bool _pointerOverLinkCard;
    private int _linkCardVersion;
    private readonly List<ScrollViewer> _linkCardScrollOwners = new();

    /// <summary>
    /// The link at <paramref name="point"/> (in <paramref name="textBlock"/>'s coordinates): one whose
    /// text is under the point, else the nearest within a few pixels of it.
    /// </summary>
    internal MarkdownLinkHit? HitTestLink(SelectableTextBlock textBlock, Point point)
    {
        if (textBlock.Inlines is not { Count: > 0 } inlines || _linkRuns.Count == 0)
            return null;

        var padding = new Vector(textBlock.Padding.Left, textBlock.Padding.Top);
        var local = point - padding;
        MarkdownLinkHit? nearest = null;
        var nearestDistance = double.MaxValue;
        var position = 0;
        // Bidi/RTL text can map a point to an adjacent logical character, so links are found
        // by their rendered rectangles rather than TextLayout.HitTestPoint.
        foreach (var inline in inlines)
        {
            var length = GetInlineTextLength(inline);
            if (inline is Run run && length > 0 && _linkRuns.TryGetValue(run, out var target))
            {
                foreach (var rect in textBlock.TextLayout.HitTestTextRange(position, length))
                {
                    if (rect.Width <= 0 || rect.Height <= 0)
                        continue;
                    if (rect.Contains(local))
                        return new MarkdownLinkHit(run, target, rect.Translate(padding));

                    var dx = Math.Max(Math.Max(rect.Left - local.X, 0), local.X - rect.Right);
                    var dy = Math.Max(Math.Max(rect.Top - local.Y, 0), local.Y - rect.Bottom);
                    if (dx > LinkHitSlopX || dy > LinkHitSlopY)
                        continue;
                    var distance = dx * dx + dy * dy;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = new MarkdownLinkHit(run, target, rect.Translate(padding));
                    }
                }
            }

            position += length;
        }

        var hit = textBlock.TextLayout.HitTestPoint(local);
        if (!hit.IsInside)
            return nearest;
        var index = hit.CharacterHit.FirstCharacterIndex;
        if (LinkAtCharIndex(textBlock, index) is { } atCharacter)
            return atCharacter;
        // The margin around a link is only empty space and punctuation: a click on another word
        // (a neighbour, or the edge of the line above or below, whose boxes touch) stays plain text.
        return nearest is not null && !char.IsLetterOrDigit(CharacterAt(inlines, index)) ? nearest : null;
    }

    private static char CharacterAt(InlineCollection inlines, int charIndex)
    {
        var position = 0;
        foreach (var inline in inlines)
        {
            var length = GetInlineTextLength(inline);
            if (charIndex < position + length)
                return inline is Run { Text: { } text } && charIndex - position < text.Length ? text[charIndex - position] : ' ';
            position += length;
        }
        return ' ';
    }

    private MarkdownLinkHit? LinkAtCharIndex(SelectableTextBlock textBlock, int charIndex)
    {
        if (textBlock.Inlines is not { } inlines)
            return null;
        var position = 0;
        foreach (var inline in inlines)
        {
            var length = GetInlineTextLength(inline);
            if (inline is Run run && charIndex >= position && charIndex < position + length
                && _linkRuns.TryGetValue(run, out var target))
            {
                var bounds = textBlock.TextLayout.HitTestTextRange(position, length).FirstOrDefault();
                return new MarkdownLinkHit(run, target,
                    bounds.Translate(new Vector(textBlock.Padding.Left, textBlock.Padding.Top)));
            }
            position += length;
        }
        return null;
    }

    /// <summary>Opens a link this markdown shows, as a click on it does.</summary>
    internal void OpenLinkTarget(string target) => OpenLink(target);

    /// <summary>Whether a click on the link opens something: web and mail links, and local files where the host allows them.</summary>
    internal bool CanOpenLink(string target)
    {
        target = NormalizeLinkTarget(target);
        return Uri.TryCreate(target, UriKind.Absolute, out var uri)
            ? IsAllowedExternalUri(uri) || AllowLocalFileLinks && uri.IsFile
            : AllowLocalFileLinks && ResolveLocalPath(target) is not null;
    }

    /// <summary>Puts a link's address on the clipboard.</summary>
    internal async Task<bool> CopyLinkAsync(string target)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
                return false;
            await clipboard.SetTextAsync(NormalizeLinkTarget(target));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not SelectableTextBlock textBlock)
            return;

        var point = e.GetCurrentPoint(textBlock);
        var hit = HitTestLink(textBlock, point.Position);
        if (hit is null)
            textBlock.ClearValue(CursorProperty);
        else
            textBlock.Cursor = HandCursorLazy.Value;

        // A drag selects text, and touch has no hover: neither lights links up or previews them.
        if (point.Properties.IsLeftButtonPressed || e.Pointer.Type != PointerType.Mouse)
        {
            ShowLinkHover(null, null);
            CancelLinkCardOpen();
            return;
        }

        ShowLinkHover(textBlock, hit?.Run);
        if (hit is { } link)
            PreviewLinkSoon(textBlock, link);
        else
            LeaveLink();
    }

    private void OnTextBlockPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is SelectableTextBlock textBlock)
            textBlock.ClearValue(CursorProperty);
        ShowLinkHover(null, null);
        LeaveLink();
    }

    private void OnTextBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Acting on a link (or starting a selection) puts its card away until the pointer moves on.
        if (sender is SelectableTextBlock textBlock && HitTestLink(textBlock, e.GetPosition(textBlock)) is { } hit)
            _suppressedLinkCardRun = hit.Run;
        CancelLinkCardOpen();
        HideLinkCard();
    }

    /// <summary>Softly highlights the hovered link behind its text, as the inline code backgrounds are drawn.</summary>
    private void ShowLinkHover(SelectableTextBlock? textBlock, Run? run)
    {
        var layer = run is null ? null : textBlock?.GetVisualParent() as InlineCodeLayer;
        if (!ReferenceEquals(layer, _hoveredLinkLayer))
        {
            _hoveredLinkLayer?.SetHoveredLink(null, null);
            _hoveredLinkLayer = layer;
        }
        layer?.SetHoveredLink(run, _linkHoverBrush ??= ResolveLinkHoverBrush());
    }

    private IBrush ResolveLinkHoverBrush()
    {
        var color = (_linkBrush ??= ResolveLinkBrush()) is ISolidColorBrush solid ? solid.Color : Colors.DodgerBlue;
        return new SolidColorBrush(color, 0.14);
    }

    private void PreviewLinkSoon(SelectableTextBlock textBlock, MarkdownLinkHit hit)
    {
        if (ReferenceEquals(hit.Run, _suppressedLinkCardRun))
            return;
        _suppressedLinkCardRun = null;
        CancelLinkCardClose();

        if (_linkCard is { IsOpen: true } && ReferenceEquals(_linkCardRun, hit.Run))
        {
            CancelLinkCardOpen();
            return;
        }
        // Small movements along the same link don't push its card further away.
        if (_linkCardOpenTimer is not null && _pendingLinkCard is { } pending && ReferenceEquals(pending.Run, hit.Run))
            return;

        CancelLinkCardOpen();
        _pendingLinkCardBlock = textBlock;
        _pendingLinkCard = hit;
        if (StrataInteractiveBlock.ShowsLinkPreviews)
            _ = GetLinkPreviewAsync(hit.Target);
        var delay = _linkCard is { IsOpen: true } ? LinkCardSwitchDelay : LinkCardOpenDelay;
        _linkCardOpenTimer = DispatcherTimer.RunOnce(OpenPendingLinkCard, delay, DispatcherPriority.Normal);
    }

    private void LeaveLink()
    {
        _suppressedLinkCardRun = null;
        CancelLinkCardOpen();
        if (_linkCard is { IsOpen: true } && !_pointerOverLinkCard)
            ScheduleLinkCardClose();
    }

    private void CancelLinkCardOpen()
    {
        _linkCardOpenTimer?.Dispose();
        _linkCardOpenTimer = null;
        _pendingLinkCardBlock = null;
        _pendingLinkCard = null;
    }

    private void ScheduleLinkCardClose()
    {
        if (_linkCardCloseTimer is not null)
            return;
        _linkCardCloseTimer = DispatcherTimer.RunOnce(() =>
        {
            _linkCardCloseTimer = null;
            if (!_pointerOverLinkCard)
                HideLinkCard();
        }, LinkCardCloseDelay, DispatcherPriority.Normal);
    }

    private void CancelLinkCardClose()
    {
        _linkCardCloseTimer?.Dispose();
        _linkCardCloseTimer = null;
    }

    private void OpenPendingLinkCard()
    {
        _linkCardOpenTimer = null;
        var textBlock = _pendingLinkCardBlock;
        var pending = _pendingLinkCard;
        _pendingLinkCardBlock = null;
        _pendingLinkCard = null;
        if (textBlock is null || pending is not { } hit || !_isAttachedToVisualTree
            || !textBlock.IsAttachedToVisualTree() || !textBlock.IsEffectivelyVisible
            || !_linkRuns.ContainsKey(hit.Run) || textBlock.Inlines?.Contains(hit.Run) != true)
            return;

        var popup = EnsureLinkCard();
        var rtl = textBlock.FlowDirection == FlowDirection.RightToLeft;
        popup.PlacementAnchor = rtl ? PopupAnchor.BottomRight : PopupAnchor.BottomLeft;
        popup.PlacementGravity = rtl ? PopupGravity.BottomLeft : PopupGravity.BottomRight;
        popup.PlacementTarget = textBlock;
        popup.PlacementRect = hit.Bounds;
        popup.FlowDirection = FlowDirection;
        // The card's surface stays the popup's child; switching links swaps only what it shows,
        // since a popup whose child is replaced while open keeps drawing the old one.
        var card = (Border)popup.Child!;
        card.Child = CreateLinkCardContent(hit.Target);
        AutomationProperties.SetName(card, LinkSiteLabel(Uri.TryCreate(NormalizeLinkTarget(hit.Target), UriKind.Absolute, out var uri) ? uri : null, hit.Target));
        _linkCardRun = hit.Run;
        TrackLinkCardSource(textBlock);
        _pointerOverLinkCard = false;
        popup.IsOpen = true;
    }

    private Popup EnsureLinkCard()
    {
        if (_linkCard is not null)
            return _linkCard;

        var popup = new Popup
        {
            Name = "MarkdownLinkCard",
            Placement = PlacementMode.AnchorAndGravity,
            VerticalOffset = 6,
            // The card follows the pointer, not clicks: leaving both the link and the card puts it away.
            IsLightDismissEnabled = false,
            Focusable = false,
        };
        var card = new Border { Width = LinkCardWidth };
        card.Classes.Add("strata-md-link-card");
        card.PointerEntered += (_, _) =>
        {
            _pointerOverLinkCard = true;
            CancelLinkCardClose();
        };
        card.PointerExited += (_, _) =>
        {
            _pointerOverLinkCard = false;
            ScheduleLinkCardClose();
        };
        popup.Child = card;
        PopupAnimationHelper.SetEnableOverlayAnimation(popup, true);
        popup.Closed += OnLinkCardClosed;
        // Logically parented to the markdown so the card resolves its theme resources and styles.
        LogicalChildren.Add(popup);
        _linkCard = popup;
        return popup;
    }

    private void HideLinkCard()
    {
        CancelLinkCardClose();
        _pointerOverLinkCard = false;
        _linkCardVersion++;
        StopTrackingLinkCardSource();
        if (_linkCard is { IsOpen: true } popup)
            popup.Close();
    }

    private void OnLinkCardClosed(object? sender, EventArgs e)
    {
        _linkCardRun = null;
        _pointerOverLinkCard = false;
        StopTrackingLinkCardSource();
        if (sender is Popup popup)
        {
            // Keep the anchor through the exit animation; release the card once it is gone.
            Dispatcher.UIThread.Post(() =>
            {
                if (!popup.IsOpen && popup.Child?.IsAttachedToVisualTree() != true)
                {
                    popup.PlacementTarget = null;
                    if (popup.Child is Border card)
                        card.Child = null;
                }
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>Puts the card away and lets it go, when the markdown leaves the window.</summary>
    private void ReleaseLinkCard()
    {
        CancelLinkCardOpen();
        HideLinkCard();
        ShowLinkHover(null, null);
        _suppressedLinkCardRun = null;
        if (_linkCard is { } popup)
        {
            popup.Closed -= OnLinkCardClosed;
            popup.PlacementTarget = null;
            popup.Child = null;
            LogicalChildren.Remove(popup);
            _linkCard = null;
        }
    }

    private void TrackLinkCardSource(SelectableTextBlock textBlock)
    {
        StopTrackingLinkCardSource();
        _linkCardSource = textBlock;
        textBlock.DetachedFromVisualTree += OnLinkCardSourceDetached;
        // Popups don't follow their anchor, so the card goes as soon as anything scrolls the text.
        foreach (var scrollViewer in textBlock.GetVisualAncestors().OfType<ScrollViewer>())
        {
            scrollViewer.ScrollChanged += OnLinkCardScrollChanged;
            _linkCardScrollOwners.Add(scrollViewer);
        }
    }

    private void StopTrackingLinkCardSource()
    {
        if (_linkCardSource is { } source)
        {
            source.DetachedFromVisualTree -= OnLinkCardSourceDetached;
            _linkCardSource = null;
        }
        foreach (var scrollViewer in _linkCardScrollOwners)
            scrollViewer.ScrollChanged -= OnLinkCardScrollChanged;
        _linkCardScrollOwners.Clear();
    }

    private void OnLinkCardSourceDetached(object? sender, VisualTreeAttachmentEventArgs e) => HideLinkCard();

    private void OnLinkCardScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta != default)
            HideLinkCard();
    }

    private Control CreateLinkCardContent(string target)
    {
        target = NormalizeLinkTarget(target);
        var version = ++_linkCardVersion;
        var labels = UiLabels;
        Uri.TryCreate(target, UriKind.Absolute, out var uri);
        var isWeb = uri is not null && MarkdownImageFiles.IsWebAddress(uri);
        var canOpen = CanOpenLink(target);

        var picture = new Image { Stretch = Stretch.UniformToFill };
        picture.Classes.Add("strata-md-link-card-picture");
        var media = new Border { Child = picture, IsVisible = false, ClipToBounds = true };
        media.Classes.Add("strata-md-link-card-media");

        var monogram = new TextBlock
        {
            Text = LinkMonogram(uri, target),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        monogram.Classes.Add("strata-md-link-card-monogram");
        var iconImage = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
        var icon = new Border { Child = new Panel { Children = { monogram, iconImage } }, ClipToBounds = true };
        icon.Classes.Add("strata-md-link-card-icon");

        var site = new TextBlock { Text = LinkSiteLabel(uri, target), TextTrimming = TextTrimming.CharacterEllipsis };
        site.Classes.Add("strata-md-link-card-site");
        Grid.SetColumn(site, 1);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { icon, site } };

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
        title.Classes.Add("strata-md-link-card-title");
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
        description.Classes.Add("strata-md-link-card-description");

        var body = new StackPanel { Children = { header, title, description } };
        body.Classes.Add("strata-md-link-card-body");

        var content = new StackPanel { Children = { media, body } };
        if (canOpen)
        {
            content.Cursor = HandCursorLazy.Value;
            content.Tapped += (_, e) =>
            {
                e.Handled = true;
                HideLinkCard();
                OpenLink(target);
            };
        }

        var address = new TextBlock
        {
            Text = target,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
        };
        address.Classes.Add("strata-md-link-card-address");

        var copyIcon = new StrataLineIcon { Data = StrataIcons.Copy, Width = 14, Height = 14 };
        var copy = LinkCardAction("MarkdownLinkCardCopy", labels["linkCopy"], copyIcon);
        copy.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!await CopyLinkAsync(target))
                return;
            copyIcon.Data = StrataIcons.Check;
            AutomationProperties.SetName(copy, labels["copied"]);
            DispatcherTimer.RunOnce(() =>
            {
                copyIcon.Data = StrataIcons.Copy;
                AutomationProperties.SetName(copy, labels["linkCopy"]);
            }, TimeSpan.FromSeconds(1.4), DispatcherPriority.Normal);
        };
        var open = LinkCardAction("MarkdownLinkCardOpen", labels["linkOpen"],
            new StrataLineIcon { Data = StrataIcons.OpenExternal, Width = 14, Height = 14 });
        open.IsVisible = canOpen;
        open.Click += (_, e) =>
        {
            e.Handled = true;
            HideLinkCard();
            OpenLink(target);
        };
        Grid.SetColumn(copy, 1);
        Grid.SetColumn(open, 2);
        var footerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Children = { address, copy, open } };
        var footer = new Border { Child = footerRow };
        footer.Classes.Add("strata-md-link-card-footer");

        if (isWeb && StrataInteractiveBlock.ShowsLinkPreviews)
            _ = FillLinkCardAsync(target, version, media, picture, iconImage, monogram, site, title, description);
        return new StackPanel { Children = { content, footer } };
    }

    private static Button LinkCardAction(string name, string label, Control icon)
    {
        var button = new Button { Name = name, Content = icon, Focusable = false };
        button.Classes.Add("strata-md-link-card-action");
        AutomationProperties.SetName(button, label);
        return button;
    }

    private async Task FillLinkCardAsync(string target, int version, Border media, Image picture,
        Image iconImage, TextBlock monogram, TextBlock site, TextBlock title, TextBlock description)
    {
        var preview = await GetLinkPreviewAsync(target);
        if (preview is null || version != _linkCardVersion)
            return;

        if (preview.SiteName is { } siteName)
            site.Text = siteName;
        title.Text = preview.Title;
        title.IsVisible = preview.Title is not null;
        // A page whose summary repeats its title says nothing more.
        description.Text = preview.Description;
        description.IsVisible = preview.Description is not null
            && !string.Equals(preview.Description, preview.Title, StringComparison.OrdinalIgnoreCase);

        var iconTask = preview.Icon is { } iconUrl ? LoadLinkCardBitmap(LinkCardIcons, MaxLinkCardIcons, iconUrl, LinkCardIconBounds) : null;
        var pictureTask = preview.Image is { } imageUrl ? LoadLinkCardBitmap(LinkCardPictures, MaxLinkCardPictures, imageUrl, LinkCardPictureBounds) : null;
        if (iconTask is not null && await iconTask is { } iconBitmap && version == _linkCardVersion)
        {
            iconImage.Source = iconBitmap;
            iconImage.IsVisible = true;
            monogram.IsVisible = false;
        }
        if (pictureTask is not null && await pictureTask is { } pictureBitmap && version == _linkCardVersion)
        {
            picture.Source = pictureBitmap;
            media.IsVisible = true;
        }
    }

    /// <summary>Loads a card's picture once per run, keeping the few most recent so hovering again is instant.</summary>
    private static Task<Bitmap?> LoadLinkCardBitmap(Dictionary<string, Task<Bitmap?>> cache, int capacity, string source, PixelSize bounds)
    {
        if (cache.TryGetValue(source, out var cached))
            return cached;
        if (cache.Count >= capacity)
            cache.Clear();
        var load = Load();
        cache[source] = load;
        return load;

        async Task<Bitmap?> Load()
        {
            try
            {
                return await LoadMarkdownImageAsync(source, CancellationToken.None, bounds);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>The name a card shows for where a link leads: a site's host, an email address, or a file's name.</summary>
    internal static string LinkSiteLabel(Uri? uri, string target)
    {
        if (uri is null)
            return Path.GetFileName(target.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : target;
        if (uri.Scheme == Uri.UriSchemeMailto)
            return uri.OriginalString["mailto:".Length..].Split('?')[0];
        if (uri.IsFile)
            return Path.GetFileName(uri.LocalPath) is { Length: > 0 } fileName ? fileName : uri.LocalPath;
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }

    private static string LinkMonogram(Uri? uri, string target)
    {
        var label = LinkSiteLabel(uri, target);
        return label.Length == 0 ? "?" : StringInfo.GetNextTextElement(label).ToUpper(CultureInfo.CurrentCulture);
    }
}
