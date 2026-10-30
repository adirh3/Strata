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

namespace StrataTheme.Controls;

/// <summary>
/// A touch-first image viewer. The image fits the viewport; pinch, double-tap, the mouse wheel and the
/// <c>+</c>/<c>-</c> keys zoom it (up to 8×) about the point under the gesture, a drag pans it while it is
/// zoomed, and <c>Home</c> or a second double-tap fits it again.
/// </summary>
/// <remarks>
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
        AddHandler(InputElement.PinchEndedEvent, (_, _) => _lastPinchScale = 1);
        AddHandler(InputElement.DoubleTappedEvent, (_, e) =>
        {
            if (_zoom > 1)
                Fit();
            else
                ZoomBy(2, e.GetPosition(this));
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
            Spacing = 8,
            Margin = new Thickness(0, 8)
        };
        toolbar.Children.Add(Action("MarkdownImageZoomOut", "-", InteractiveLabels.Current["zoomOut"], () => ZoomBy(0.8)));
        toolbar.Children.Add(Action("MarkdownImageReset", InteractiveLabels.Current["fit"], InteractiveLabels.Current["fitImage"], Fit));
        toolbar.Children.Add(Action("MarkdownImageZoomIn", "+", InteractiveLabels.Current["zoomIn"], () => ZoomBy(1.25)));
        toolbar.Children.Add(label);
        return toolbar;
    }

    private static Button Action(string name, string content, string label, Action action)
    {
        var button = new Button { Name = name, Content = content };
        button.Classes.Add("strata-md-image-viewer-action");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Fits the whole image to the viewport again.</summary>
    public void Fit()
    {
        _zoom = 1;
        _offset = default;
        UpdateTransform();
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
        if (double.IsFinite(e.Scale) && e.Scale > 0)
        {
            ZoomBy(e.Scale / _lastPinchScale, e.ScaleOrigin);
            _lastPinchScale = e.Scale;
        }
        e.Handled = true;
    }

    private void UpdateTransform()
    {
        var size = Bounds.Size;
        var content = new Rect(size);
        if (_image.Source is { Size.Width: > 0, Size.Height: > 0 } source)
        {
            var scale = Math.Min(size.Width / source.Size.Width, size.Height / source.Size.Height);
            var fitted = source.Size * scale;
            content = new Rect((size.Width - fitted.Width) / 2, (size.Height - fitted.Height) / 2, fitted.Width, fitted.Height);
        }
        _offset = new Vector(
            Constrain(_offset.X, size.Width, content.Left, content.Width),
            Constrain(_offset.Y, size.Height, content.Top, content.Height));
        _transform.Matrix = new Matrix(_zoom, 0, 0, _zoom, _offset.X, _offset.Y);
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
        if (e.Pointer != _panPointer)
            return;
        _offset = _panOffset + (e.GetPosition(this) - _panStart);
        UpdateTransform();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer == _panPointer)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _panPointer = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _panPointer?.Capture(null);
        _lastPinchScale = 1;
        base.OnDetachedFromVisualTree(e);
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
