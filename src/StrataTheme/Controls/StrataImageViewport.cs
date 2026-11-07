using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A touch-first image viewer. The image fits the viewport; pinch, double-tap, the mouse wheel and the
/// <c>+</c>/<c>-</c> keys zoom it (up to 8×) about the point under the gesture, a drag pans it while it is
/// zoomed, and <c>Home</c> or a second double-tap fits it again.
/// </summary>
/// <remarks>
/// While the image fits, a touch drag swipes it: sideways to the previous or next image when the host
/// offers more than one, and up or down to dismiss the viewer.
/// Set <see cref="Source"/> to show an image; a new source fits the viewport again.
/// <see cref="LoadImageAsync"/> decodes a local image file no larger than a pixel bound, so a large photo
/// does not cost its full resolution in memory.
/// </remarks>
public sealed class StrataImageViewport : Border
{
    /// <summary>Defines the <see cref="Source"/> property.</summary>
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<StrataImageViewport, IImage?>(nameof(Source));

    private readonly Image _image;
    private readonly MatrixTransform _transform = new();
    private double _zoom = 1;
    private Vector _offset;
    private double _lastPinchScale = 1;
    private IPointer? _panPointer;
    private Point _panStart;
    private Vector _panOffset;
    private IPointer? _swipePointer;
    private Point _swipeStart;
    private Vector _swipe;
    private SwipeAxis _swipeAxis;
    private ulong _swipeLastTime;
    private Vector _swipeVelocity;
    private bool _isPinching;
    private bool _swiped;
    private Point _pressAt;
    private Point? _doubleTapAt;
    private DispatcherTimer? _settleTimer;

    /// <summary>A swipe must travel this far before it counts as a swipe rather than a tap.</summary>
    private const double SwipeSlop = 10;

    /// <summary>A flick this fast (pixels per millisecond) completes a swipe however short it was.</summary>
    private const double FlickVelocity = 0.55;

    /// <summary>Even a flick must travel this far, so a quick nudge is never taken for a swipe.</summary>
    private const double FlickDistance = 40;

    private enum SwipeAxis { None, Horizontal, Vertical }

    /// <summary>Creates an empty viewer; set <see cref="Source"/> to show an image.</summary>
    public StrataImageViewport()
        : this(new Image { Stretch = Stretch.Uniform }, name: null)
    {
    }

    internal StrataImageViewport(Image image)
        : this(image, "MarkdownImageViewport")
    {
    }

    private StrataImageViewport(Image image, string? name)
    {
        if (name is not null)
            Name = name;
        _image = image;
        Child = image;
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
        image.IsHitTestVisible = false;
        image.RenderTransformOrigin = RelativePoint.TopLeft;
        image.RenderTransform = _transform;
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(InputElement.PinchEvent, OnPinch);
        AddHandler(InputElement.PinchEndedEvent, (_, _) =>
        {
            _lastPinchScale = 1;
            _isPinching = false;
        });
        // A double-tap is known on the second press but zooms on its release, so two quick swipes from
        // the same spot never zoom.
        AddHandler(InputElement.DoubleTappedEvent, (_, e) =>
        {
            _doubleTapAt = e.GetPosition(this);
            e.Handled = true;
        });
        AutomationProperties.SetName(this, InteractiveLabels.Current["imageViewer"]);
        AutomationProperties.SetHelpText(this, InteractiveLabels.Current["imageViewerHelp"]);
    }

    /// <summary>The image shown. Setting a new one fits it to the viewport.</summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The current zoom factor: 1 when the image fits the viewport, up to 8.</summary>
    public double Zoom => _zoom;
    internal Vector Offset => _offset;

    private event EventHandler? ZoomChanged;

    /// <summary>Sideways swipes go to another image; the host turns this on when there is more than one.</summary>
    internal bool CanSwipeBetweenImages { get; set; }

    /// <summary>A sideways swipe finished: -1 for the previous image, +1 for the next, in reading order.</summary>
    internal event EventHandler<int>? NavigateRequested;

    /// <summary>An up or down swipe finished far or fast enough to put the viewer away.</summary>
    internal event EventHandler? DismissRequested;

    /// <summary>How far an up or down swipe has gone toward dismissing, from 0 to 1.</summary>
    internal event EventHandler<double>? DismissProgress;

    /// <summary>Where the image is drawn now, in the viewport's coordinates.</summary>
    internal Rect ImageBounds
    {
        get
        {
            var content = FittedContent(Bounds.Size);
            return new Rect(
                content.X * _zoom + _offset.X + _swipe.X,
                content.Y * _zoom + _offset.Y + _swipe.Y,
                content.Width * _zoom,
                content.Height * _zoom);
        }
    }

    /// <summary>
    /// Decodes a local image file for display, scaled down (never up) to fit <paramref name="maxPixelSize"/>.
    /// PNG, JPEG, GIF, BMP and WebP files up to 20 MB are supported. Decoding runs off the calling thread.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a fully qualified local path.</exception>
    /// <exception cref="InvalidDataException">The file is too large or not a supported image.</exception>
    public static Task<Bitmap> LoadImageAsync(
        string path, PixelSize maxPixelSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException(InteractiveLabels.Current["imageSourceRequired"], nameof(path));

        return StrataMarkdown.LoadMarkdownImageAsync(path, cancellationToken, maxPixelSize);
    }

    internal Control CreateToolbar()
    {
        var label = new TextBlock { Name = "MarkdownImageZoomLabel", Text = "100%", VerticalAlignment = VerticalAlignment.Center };
        label.Classes.Add("strata-md-image-caption");
        ZoomChanged += (_, _) => label.Text = $"{Math.Round(_zoom * 100)}%";
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 2,
        };
        var labels = InteractiveLabels.Current;
        toolbar.Children.Add(Action("MarkdownImageZoomOut", new StrataLineIcon { Data = StrataIcons.Minus, Width = 16, Height = 16 }, labels["zoomOutShortcut"], () => ZoomBy(0.8)));
        toolbar.Children.Add(Action("MarkdownImageReset", label, labels["fitImage"], Fit));
        toolbar.Children.Add(Action("MarkdownImageZoomIn", new StrataLineIcon { Data = StrataIcons.Plus, Width = 16, Height = 16 }, labels["zoomInShortcut"], () => ZoomBy(1.25)));
        return toolbar;
    }

    private static Button Action(string name, object content, string label, Action action)
    {
        var button = new Button { Name = name, Content = content };
        button.Classes.Add("strata-md-image-viewer-action");
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Fits the whole image to the viewport again.</summary>
    public void Fit()
    {
        _zoom = 1;
        _offset = default;
        StopSettling();
        var wasPulled = _swipe.Y != 0;
        _swipe = default;
        UpdateTransform();
        if (wasPulled)
            DismissProgress?.Invoke(this, 0);
    }

    /// <summary>
    /// The image that just took the place of another slides in from the side it came from, so a gallery
    /// reads as a strip of pictures rather than a picture swapped in place.
    /// </summary>
    internal void SlideIn(int direction)
    {
        if (direction == 0 || Bounds.Width <= 0)
            return;
        _swipe = new Vector(direction * Math.Min(Bounds.Width * 0.3, 160), 0);
        UpdateTransform();
        Settle();
    }

    internal void ZoomBy(double factor, Point? anchor = null)
    {
        if (!double.IsFinite(factor) || factor <= 0)
            return;
        var point = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var zoom = Math.Clamp(_zoom * factor, 1, 8);
        _offset = (Vector)point - ((Vector)point - _offset) * (zoom / _zoom);
        _zoom = zoom;
        UpdateTransform();
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        _isPinching = true;
        CancelSwipe();
        if (double.IsFinite(e.Scale) && e.Scale > 0)
        {
            ZoomBy(e.Scale / _lastPinchScale, e.ScaleOrigin);
            _lastPinchScale = e.Scale;
        }
        e.Handled = true;
    }

    private Rect FittedContent(Size size)
    {
        if (_image.Source is not { Size.Width: > 0, Size.Height: > 0 } source)
            return new Rect(size);
        var scale = Math.Min(size.Width / source.Size.Width, size.Height / source.Size.Height);
        var fitted = source.Size * scale;
        return new Rect((size.Width - fitted.Width) / 2, (size.Height - fitted.Height) / 2, fitted.Width, fitted.Height);
    }

    private void UpdateTransform()
    {
        var size = Bounds.Size;
        var content = FittedContent(size);
        _offset = new Vector(
            Constrain(_offset.X, size.Width, content.Left, content.Width),
            Constrain(_offset.Y, size.Height, content.Top, content.Height));
        _transform.Matrix = new Matrix(_zoom, 0, 0, _zoom, _offset.X + _swipe.X, _offset.Y + _swipe.Y);
        Cursor = new Cursor(_zoom > 1 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private double Constrain(double offset, double viewport, double start, double length) =>
        length * _zoom <= viewport
            ? (viewport - length * _zoom) / 2 - start * _zoom
            : Math.Clamp(offset, viewport - (start + length) * _zoom, -start * _zoom);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _image.Source = change.GetNewValue<IImage?>();
            Fit();
        }
        else if (change.Property == BoundsProperty && change.GetOldValue<Rect>().Size != Bounds.Size)
            UpdateTransform();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _swiped = false;
        _pressAt = e.GetPosition(this);
        if (_swipePointer is not null && _swipePointer != e.Pointer)
        {
            // A second finger is a pinch: the swipe gives way, and the image goes back where it was.
            CancelSwipe();
            return;
        }
        if (_zoom <= 1 && e.Pointer.Type == PointerType.Touch && _swipePointer is null && !_isPinching)
        {
            StopSettling();
            _swipePointer = e.Pointer;
            _swipeStart = e.GetPosition(this);
            _swipeAxis = SwipeAxis.None;
            _swipeLastTime = e.Timestamp;
            _swipeVelocity = default;
            return;
        }
        if (_panPointer is not null || _zoom <= 1 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        _panPointer = e.Pointer;
        _panStart = e.GetPosition(this);
        _panOffset = _offset;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer == _swipePointer)
        {
            TrackSwipe(e);
            return;
        }
        if (e.Pointer != _panPointer)
            return;
        _offset = _panOffset + (e.GetPosition(this) - _panStart);
        UpdateTransform();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var doubleTap = _doubleTapAt;
        _doubleTapAt = null;
        var travel = e.GetPosition(this) - _pressAt;
        if (e.Pointer == _swipePointer)
        {
            TrackSwipe(e);
            FinishSwipe();
        }
        else if (e.Pointer == _panPointer)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        if (doubleTap is { } at && !_swiped && Math.Abs(travel.X) + Math.Abs(travel.Y) <= SwipeSlop)
        {
            if (_zoom > 1)
                Fit();
            else
                ZoomBy(2, at);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // A touch the platform cancelled (the notification shade, another app) never releases: the
        // swipe it started gives way, and the picture and backdrop come back.
        if (e.Pointer == _swipePointer)
            CancelSwipe();
        _panPointer = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _panPointer?.Capture(null);
        _lastPinchScale = 1;
        _isPinching = false;
        _swipePointer = null;
        StopSettling();
        base.OnDetachedFromVisualTree(e);
    }

    private void TrackSwipe(PointerEventArgs e)
    {
        var travel = e.GetPosition(this) - _swipeStart;
        if (_swipeAxis == SwipeAxis.None)
        {
            if (Math.Abs(travel.X) < SwipeSlop && Math.Abs(travel.Y) < SwipeSlop)
                return;
            _swipeAxis = Math.Abs(travel.X) > Math.Abs(travel.Y) ? SwipeAxis.Horizontal : SwipeAxis.Vertical;
            // A pull that was still easing back gives way to a sideways swipe: the backdrop is whole again.
            if (_swipeAxis == SwipeAxis.Horizontal && _swipe.Y != 0)
                DismissProgress?.Invoke(this, 0);
        }

        var next = _swipeAxis == SwipeAxis.Horizontal
            ? new Vector(CanSwipeBetweenImages ? travel.X : travel.X * 0.25, 0)
            : new Vector(0, travel.Y);
        var elapsed = e.Timestamp > _swipeLastTime ? e.Timestamp - _swipeLastTime : 0;
        if (elapsed > 0)
            _swipeVelocity = (next - _swipe) / elapsed;
        _swipeLastTime = e.Timestamp;
        _swipe = next;
        UpdateTransform();
        if (_swipeAxis == SwipeAxis.Vertical)
            DismissProgress?.Invoke(this, DismissFraction());
        e.Handled = true;
    }

    private void FinishSwipe()
    {
        _swipePointer = null;
        var axis = _swipeAxis;
        _swipeAxis = SwipeAxis.None;
        _swiped = axis != SwipeAxis.None;
        if (axis == SwipeAxis.Horizontal && CanSwipeBetweenImages
            && (Math.Abs(_swipe.X) > Bounds.Width * 0.22
                || (Math.Abs(_swipeVelocity.X) > FlickVelocity && Math.Abs(_swipe.X) > FlickDistance))
            && Math.Sign(_swipe.X) == Math.Sign(_swipeVelocity.X == 0 ? _swipe.X : _swipeVelocity.X))
        {
            // Swiping toward the start of the line brings the next image, as turning a page does. Positions
            // here are already in the line's direction, so this holds for right-to-left text as it is.
            NavigateRequested?.Invoke(this, _swipe.X < 0 ? 1 : -1);
            return;
        }
        if (axis == SwipeAxis.Vertical
            && (Math.Abs(_swipe.Y) > Math.Min(140, Bounds.Height * 0.2)
                || (Math.Abs(_swipeVelocity.Y) > FlickVelocity && Math.Abs(_swipe.Y) > FlickDistance)))
        {
            DismissRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        Settle();
    }

    private void CancelSwipe()
    {
        _swipePointer = null;
        _swipeAxis = SwipeAxis.None;
        if (_swipe == default)
            return;
        StopSettling();
        _swipe = default;
        UpdateTransform();
        DismissProgress?.Invoke(this, 0);
    }

    private double DismissFraction() =>
        Bounds.Height > 0 ? Math.Clamp(Math.Abs(_swipe.Y) / (Bounds.Height * 0.5), 0, 1) : 0;

    /// <summary>Eases a swipe that did not finish back to rest.</summary>
    internal void Settle()
    {
        StopSettling();
        if (_swipe == default)
            return;
        var from = _swipe;
        var started = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(200);
        _settleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var progress = Math.Clamp((DateTime.UtcNow - started) / duration, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            _swipe = from * (1 - eased);
            if (progress >= 1)
            {
                _swipe = default;
                StopSettling();
            }
            UpdateTransform();
            if (from.Y != 0)
                DismissProgress?.Invoke(this, DismissFraction());
        });
        _settleTimer.Start();
    }

    private void StopSettling()
    {
        _settleTimer?.Stop();
        _settleTimer = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ZoomBy(Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus: ZoomBy(1.25); break;
            case Key.Subtract:
            case Key.OemMinus: ZoomBy(0.8); break;
            case Key.Home: Fit(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }
}
