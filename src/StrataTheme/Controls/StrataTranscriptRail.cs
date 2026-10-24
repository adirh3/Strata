using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>A place in a chat transcript that <see cref="StrataTranscriptRail"/> offers to jump to, such as one of the user's messages.</summary>
public sealed class StrataTranscriptMark
{
    public StrataTranscriptMark(double position, string text, string? detail = null, object? tag = null)
    {
        Position = position;
        Text = text ?? string.Empty;
        Detail = detail;
        Tag = tag;
    }

    /// <summary>Where the place starts, measured from the top of the transcript's scroll content.</summary>
    public double Position { get; }

    /// <summary>What the rail previews for the place, such as how the message begins.</summary>
    public string Text { get; }

    /// <summary>A quieter second line, such as when the message was sent.</summary>
    public string? Detail { get; }

    /// <summary>The host's own handle for the place, handed back when the reader picks it.</summary>
    public object? Tag { get; }
}

/// <summary>
/// Asks the host for the transcript's marks. The rail reads them when it opens, and again whenever the
/// transcript's layout changes while it is open, so marks only cost anything while the reader uses them.
/// </summary>
public sealed class StrataTranscriptMarksRequestedEventArgs : EventArgs
{
    /// <summary>The marks to show, in any order.</summary>
    public IList<StrataTranscriptMark> Marks { get; } = new List<StrataTranscriptMark>();
}

/// <summary>Raised when the reader picks a mark on the rail.</summary>
public sealed class StrataTranscriptMarkInvokedEventArgs : EventArgs
{
    public StrataTranscriptMarkInvokedEventArgs(StrataTranscriptMark mark) => Mark = mark;

    public StrataTranscriptMark Mark { get; }

    /// <summary>Set when the host brought the place into view itself; otherwise the shell scrolls to it.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Where things sit on the rail. The thumb maps the scroll offset as a scrollbar's does. A mark sits where
/// the thumb's reading line will be once the mark is brought to the reading line (just below a floating
/// header), so the mark of the place being read rests inside the thumb, near its top, and every mark keeps
/// its place while the transcript scrolls.
/// </summary>
internal readonly record struct TranscriptRailLayout(
    double TrackTop,
    double TrackLength,
    double Extent,
    double Viewport,
    double Offset,
    double ReadingInset,
    double MinThumbLength)
{
    /// <summary>How far below the reading line a place may start and still count as the one being read.</summary>
    internal const double ReadingSlack = 16;

    public double MaxOffset => Math.Max(0, Extent - Viewport);

    public bool CanScroll => TrackLength > 1 && Viewport > 0 && MaxOffset > 0.5;

    public double ThumbLength => !CanScroll
        ? TrackLength
        : Math.Clamp(TrackLength * Viewport / Extent, Math.Min(MinThumbLength, TrackLength), TrackLength);

    public double ThumbTop => TrackTop + (CanScroll ? (TrackLength - ThumbLength) * Math.Clamp(Offset / MaxOffset, 0, 1) : 0);

    /// <summary>The rail position of a place that starts <paramref name="position"/> into the content.</summary>
    public double MarkY(double position)
    {
        if (!CanScroll)
            return TrackTop;

        var thumb = ThumbLength;
        var offset = Math.Clamp(position - ReadingInset, 0, MaxOffset);
        var withinViewport = Math.Clamp((position - offset) / Viewport, 0, 1);
        return TrackTop + (TrackLength - thumb) * offset / MaxOffset + thumb * withinViewport;
    }

    /// <summary>The scroll offset that puts the thumb's top at <paramref name="thumbTop"/>.</summary>
    public double OffsetForThumbTop(double thumbTop)
    {
        var travel = TrackLength - ThumbLength;
        return travel <= 0 || !CanScroll ? 0 : Math.Clamp((thumbTop - TrackTop) / travel, 0, 1) * MaxOffset;
    }

    /// <summary>The mark being read: the last one that starts at or just below the reading line, or -1.</summary>
    public static int IndexAtReadingLine(IReadOnlyList<StrataTranscriptMark> marks, double offset, double readingInset)
    {
        var line = offset + readingInset + ReadingSlack;
        int low = 0, high = marks.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (marks[middle].Position <= line)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>The mark nearest <paramref name="y"/> on the rail, within <paramref name="reach"/>, or -1.</summary>
    public static int NearestIndex(IReadOnlyList<double> ys, double y, double reach)
    {
        if (ys.Count == 0)
            return -1;

        int low = 0, high = ys.Count - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (ys[middle] < y)
                low = middle + 1;
            else
                high = middle;
        }

        var best = low;
        if (low > 0 && Math.Abs(ys[low - 1] - y) <= Math.Abs(ys[low] - y))
            best = low - 1;
        return Math.Abs(ys[best] - y) <= reach ? best : -1;
    }
}

/// <summary>
/// The transcript's scrollbar, made for finding your way around a conversation. At rest it is a slim thumb.
/// Hovered, it opens into a map of the transcript's marks (the user's messages): the marks near the pointer
/// grow, the nearest one is previewed beside the rail, and a click lands on it. The thumb still drags, the
/// track still jumps and the wheel still scrolls; while the thumb is dragged, the preview shows the message
/// being passed. <see cref="StrataChatShell"/> hosts it in place of the scrollbar when
/// <see cref="StrataChatShell.ShowsTranscriptRail"/> is set.
/// </summary>
/// <remarks>
/// <para>The rail covers the transcript, but only its strip at the edge and its preview take the pointer;
/// everything else passes through to the transcript. Marks are read from the shell
/// (<see cref="StrataChatShell.TranscriptMarksRequested"/>) only while the rail is open or dragged.</para>
/// <para><b>Template parts:</b> PART_Surface (<see cref="StrataTranscriptRailSurface"/> – draws the strip),
/// PART_PreviewLayer (<see cref="StrataTranscriptRailPreviewLayer"/> – places the preview beside its mark),
/// PART_Preview (Control – the preview card), PART_PreviewText (TextBlock), PART_PreviewDetail (TextBlock).</para>
/// <para><b>Pseudo-classes:</b> :open, :previewing, :pressed, :dragging.</para>
/// </remarks>
public class StrataTranscriptRail : TemplatedControl
{
    // Geometry, measured from the rail's trailing edge.
    internal const double RestingReach = 14;
    internal const double OpenReach = 44;
    internal const double TrackPadding = 6;
    internal const double MinThumbLength = 28;
    internal const double SnapReach = 22;
    private const double LineInset = 5;
    private const double ThumbWidth = 4;
    private const double OpenThumbWidth = 5;
    private const double TickEnd = 13;
    private const double TickLength = 6;
    private const double NearTickLength = 13;
    private const double FocusedTickLength = 20;
    private const double TickThickness = 2;
    private const double FocusedTickThickness = 3;
    private const double MagnifySigma = 15;
    private const double DragThreshold = 3;

    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(220);

    /// <summary>How far the rail has opened, from 0 (a slim thumb) to 1 (the map of marks). Styles animate it.</summary>
    public static readonly StyledProperty<double> OpennessProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, double>(nameof(Openness));

    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, IBrush?>(nameof(ThumbBrush));

    /// <summary>The thumb while it is pressed or dragged.</summary>
    public static readonly StyledProperty<IBrush?> ActiveThumbBrushProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, IBrush?>(nameof(ActiveThumbBrush));

    /// <summary>The line the thumb travels along, shown while the rail is open.</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, IBrush?>(nameof(MarkBrush));

    /// <summary>Marks of the places on screen (within the thumb).</summary>
    public static readonly StyledProperty<IBrush?> VisibleMarkBrushProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, IBrush?>(nameof(VisibleMarkBrush));

    /// <summary>The mark being previewed.</summary>
    public static readonly StyledProperty<IBrush?> FocusedMarkBrushProperty =
        AvaloniaProperty.Register<StrataTranscriptRail, IBrush?>(nameof(FocusedMarkBrush));

    private readonly DispatcherTimer _openTimer;
    private readonly DispatcherTimer _closeTimer;
    private StrataChatShell? _host;
    private ScrollViewer? _scrollViewer;
    private bool _isSubscribed;
    private StrataTranscriptRailSurface? _surface;
    private StrataTranscriptRailPreviewLayer? _previewLayer;
    private Control? _preview;
    private TextBlock? _previewText;
    private TextBlock? _previewDetail;
    private IReadOnlyList<StrataTranscriptMark> _marks = Array.Empty<StrataTranscriptMark>();
    private double[] _markYs = Array.Empty<double>();
    private bool _marksStale = true;
    private bool _refreshQueued;
    private bool _isOpen;
    private bool _isPointerOnStrip;
    private bool _isPointerOnPreview;
    private double _pointerY;
    private int _focusIndex = -1;
    private bool _isPreviewing;
    private bool _isPressed;
    private bool _isDragging;
    private bool _pressedOnThumb;
    private bool _pressedWithPreview;
    private double _pressY;
    private double _grabOffset;
    private double _readingInset;

    static StrataTranscriptRail()
    {
        AffectsSurface(OpennessProperty, ThumbBrushProperty, ActiveThumbBrushProperty, TrackBrushProperty,
            MarkBrushProperty, VisibleMarkBrushProperty, FocusedMarkBrushProperty);
    }

    public StrataTranscriptRail()
    {
        _openTimer = new DispatcherTimer { Interval = OpenDelay };
        _openTimer.Tick += (_, _) =>
        {
            _openTimer.Stop();
            if (_isPointerOnStrip)
                Open();
        };
        _closeTimer = new DispatcherTimer { Interval = CloseDelay };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!_isPressed && !_isPointerOnStrip && !_isPointerOnPreview)
                Close();
        };
        SizeChanged += (_, _) =>
        {
            UpdateMarkPositions();
            if (_isPreviewing)
                PositionPreview(animate: false);
            _surface?.InvalidateVisual();
        };
    }

    public double Openness { get => GetValue(OpennessProperty); set => SetValue(OpennessProperty, value); }
    public IBrush? ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }
    public IBrush? ActiveThumbBrush { get => GetValue(ActiveThumbBrushProperty); set => SetValue(ActiveThumbBrushProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? MarkBrush { get => GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }
    public IBrush? VisibleMarkBrush { get => GetValue(VisibleMarkBrushProperty); set => SetValue(VisibleMarkBrushProperty, value); }
    public IBrush? FocusedMarkBrush { get => GetValue(FocusedMarkBrushProperty); set => SetValue(FocusedMarkBrushProperty, value); }

    /// <summary>True while the rail shows its marks.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>The marks the rail shows, in transcript order (empty once it has closed).</summary>
    public IReadOnlyList<StrataTranscriptMark> Marks => _marks;

    /// <summary>The mark being previewed, if any.</summary>
    public StrataTranscriptMark? FocusedMark => _focusIndex >= 0 && _focusIndex < _marks.Count ? _marks[_focusIndex] : null;

    /// <summary>Where the reading line is below the top of the transcript's viewport (just below a floating header).</summary>
    internal double ReadingInset
    {
        get => _readingInset;
        set
        {
            if (Math.Abs(_readingInset - value) < 0.01)
                return;
            _readingInset = value;
            UpdateMarkPositions();
            _surface?.InvalidateVisual();
        }
    }

    /// <summary>Where a mark sits on the rail, in the rail's coordinates (for tests and automation).</summary>
    internal double GetMarkY(StrataTranscriptMark mark) => CurrentLayout().MarkY(mark.Position);

    internal TranscriptRailLayout CurrentLayout() => CurrentLayout(Bounds.Height);

    private TranscriptRailLayout CurrentLayout(double height)
    {
        var viewer = _scrollViewer;
        return new TranscriptRailLayout(
            TrackPadding,
            Math.Max(0, height - 2 * TrackPadding),
            viewer?.Extent.Height ?? 0,
            viewer?.Viewport.Height ?? 0,
            viewer?.Offset.Y ?? 0,
            _readingInset,
            MinThumbLength);
    }

    /// <summary>Connects the rail to the shell that supplies its marks and the transcript it scrolls.</summary>
    internal void Attach(StrataChatShell? host, ScrollViewer? scrollViewer)
    {
        Unsubscribe();
        _host = host;
        _scrollViewer = scrollViewer;
        if (TopLevel.GetTopLevel(this) is not null)
            Subscribe();
        InvalidateMarks();
        _surface?.InvalidateVisual();
    }

    /// <summary>The transcript changed: an open rail reads its marks again; a closed one reads them when it opens.</summary>
    public void InvalidateMarks()
    {
        _marksStale = true;
        if (_isOpen || _isDragging)
            QueueRefresh();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _surface = e.NameScope.Find<StrataTranscriptRailSurface>("PART_Surface");
        _previewLayer = e.NameScope.Find<StrataTranscriptRailPreviewLayer>("PART_PreviewLayer");
        _preview = e.NameScope.Find<Control>("PART_Preview");
        _previewText = e.NameScope.Find<TextBlock>("PART_PreviewText");
        _previewDetail = e.NameScope.Find<TextBlock>("PART_PreviewDetail");
        if (_surface is not null)
            _surface.Rail = this;
        if (_preview is not null)
            _preview.IsHitTestVisible = false;
        _isPreviewing = false;
        PseudoClasses.Set(":previewing", false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        EndPress();
        _isPointerOnStrip = false;
        _isPointerOnPreview = false;
        Close();
        ReleaseClosedMarks();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            Close();
            ReleaseClosedMarks();
        }
        else if (change.Property == OpennessProperty && Openness <= 0.001)
        {
            ReleaseClosedMarks();
        }
    }

    private static void AffectsSurface(params AvaloniaProperty[] properties)
    {
        foreach (var property in properties)
            property.Changed.AddClassHandler<StrataTranscriptRail>((rail, _) => rail._surface?.InvalidateVisual());
    }

    private void Subscribe()
    {
        if (_isSubscribed || _scrollViewer is null)
            return;
        _scrollViewer.ScrollChanged += OnScrollChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed || _scrollViewer is null)
            return;
        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _isSubscribed = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!IsVisible)
            return;

        if (Math.Abs(e.ExtentDelta.Y) > 0.01 || Math.Abs(e.ViewportDelta.Y) > 0.01)
        {
            // Places moved, or the thumb's size changed with the extent: read them again.
            UpdateMarkPositions();
            InvalidateMarks();
        }

        if (_isDragging)
            UpdateFocus();
        _surface?.InvalidateVisual();
    }

    // ── Opening and closing ─────────────────────────────────────────────────

    private void Open()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (_isOpen || !IsVisible)
            return;

        _isOpen = true;
        PseudoClasses.Set(":open", true);
        RefreshMarks();
        _surface?.InvalidateVisual();
    }

    private void Close()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (!_isOpen)
            return;

        _isOpen = false;
        PseudoClasses.Set(":open", false);
        SetFocus(-1);
        // The marks fold away with the rail; they are let go once it has closed (see ReleaseClosedMarks).
        _marksStale = true;
        if (Openness <= 0.001)
            ReleaseClosedMarks();
        _surface?.InvalidateVisual();
    }

    /// <summary>The marks hold the host's handles (its turns): a closed rail lets go of them until it opens again.</summary>
    private void ReleaseClosedMarks()
    {
        if (_isOpen || _isDragging || _marks.Count == 0)
            return;

        _marks = Array.Empty<StrataTranscriptMark>();
        _markYs = Array.Empty<double>();
        _surface?.InvalidateVisual();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued)
            return;

        _refreshQueued = true;
        // After layout, so the host measures its places where they now are.
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if ((_isOpen || _isDragging) && _marksStale)
                RefreshMarks();
        }, DispatcherPriority.Loaded);
    }

    private void RefreshMarks()
    {
        var focused = FocusedMark?.Tag;
        _marks = _host?.CollectTranscriptMarks() ?? Array.Empty<StrataTranscriptMark>();
        _marksStale = false;
        UpdateMarkPositions();

        // Keep previewing the same place when it is still there, wherever it moved.
        if (focused is not null && !_isDragging && !_isPointerOnStrip)
        {
            _focusIndex = -1;
            for (var i = 0; i < _marks.Count; i++)
            {
                if (Equals(_marks[i].Tag, focused))
                {
                    _focusIndex = i;
                    break;
                }
            }
        }

        UpdateFocus(force: true);
        _surface?.InvalidateVisual();
    }

    private void UpdateMarkPositions()
    {
        if (_marks.Count == 0)
        {
            _markYs = Array.Empty<double>();
            return;
        }

        var layout = CurrentLayout();
        if (_markYs.Length != _marks.Count)
            _markYs = new double[_marks.Count];
        for (var i = 0; i < _marks.Count; i++)
            _markYs[i] = layout.MarkY(_marks[i].Position);
    }

    // ── Focus and preview ───────────────────────────────────────────────────

    private void UpdateFocus(bool force = false)
    {
        int index;
        if (_isDragging)
            index = _scrollViewer is { } viewer ? TranscriptRailLayout.IndexAtReadingLine(_marks, viewer.Offset.Y, _readingInset) : -1;
        else if (_isOpen && _isPointerOnStrip)
            index = TranscriptRailLayout.NearestIndex(_markYs, _pointerY, SnapReach);
        else if (_isOpen && _isPointerOnPreview)
            index = _focusIndex < _marks.Count ? _focusIndex : -1;
        else
            index = -1;

        SetFocus(index, force);
    }

    private void SetFocus(int index, bool force = false)
    {
        if (index == _focusIndex && !force)
            return;

        _focusIndex = index;
        UpdatePreview();
        _surface?.InvalidateVisual();
    }

    private void UpdatePreview()
    {
        if (_preview is null || _previewText is null)
            return;

        if (_focusIndex < 0 || _focusIndex >= _marks.Count)
        {
            if (_isPreviewing)
            {
                _isPreviewing = false;
                _isPointerOnPreview = false;
                _preview.IsHitTestVisible = false;
                PseudoClasses.Set(":previewing", false);
            }

            return;
        }

        var mark = _marks[_focusIndex];
        _previewText.Text = mark.Text;
        if (_previewDetail is not null)
        {
            _previewDetail.Text = mark.Detail;
            _previewDetail.IsVisible = !string.IsNullOrWhiteSpace(mark.Detail);
        }

        var wasPreviewing = _isPreviewing;
        _isPreviewing = true;
        PositionPreview(animate: wasPreviewing);
        _preview.IsHitTestVisible = !_isDragging;
        PseudoClasses.Set(":previewing", true);
    }

    private void PositionPreview(bool animate)
    {
        if (_previewLayer is null || _focusIndex < 0 || _focusIndex >= _markYs.Length)
            return;

        var anchor = _markYs[_focusIndex];
        if (animate)
        {
            _previewLayer.AnchorY = anchor;
            return;
        }

        // Appearing: start beside its own mark, never glide in from where the card was last shown.
        var transitions = _previewLayer.Transitions;
        _previewLayer.Transitions = null;
        _previewLayer.AnchorY = anchor;
        _previewLayer.Transitions = transitions;
    }

    private bool IsWithinPreview(object? source)
        => _preview is not null && source is Visual visual
           && (ReferenceEquals(visual, _preview) || _preview.IsVisualAncestorOf(visual));

    private void InvokeMark(int index)
    {
        if (index < 0 || index >= _marks.Count || _host is null)
            return;

        _host.InvokeTranscriptMark(_marks[index]);
    }

    // ── Pointer ─────────────────────────────────────────────────────────────

    private bool IsOnStrip(Point point)
        => point.X >= Bounds.Width - (_isOpen || _isPressed ? OpenReach : RestingReach)
           && point.Y >= 0 && point.Y <= Bounds.Height;

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        TrackPointer(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isPressed)
        {
            DragTo(e.GetPosition(this).Y);
            return;
        }

        TrackPointer(e);
    }

    private void TrackPointer(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        _isPointerOnPreview = _isPreviewing && IsWithinPreview(e.Source);
        _isPointerOnStrip = !_isPointerOnPreview && IsOnStrip(point);
        if (_isPointerOnStrip)
            _pointerY = point.Y;

        if (_isPointerOnStrip || _isPointerOnPreview)
        {
            _closeTimer.Stop();
            if (_isOpen)
                UpdateFocus();
            else if (_isPointerOnStrip && !_openTimer.IsEnabled)
                _openTimer.Start();
        }

        _surface?.InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerOnStrip = false;
        _isPointerOnPreview = false;
        _openTimer.Stop();
        if (!_isPressed)
            _closeTimer.Start();
        _surface?.InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (IsWithinPreview(e.Source))
        {
            e.Handled = true;
            InvokeMark(_focusIndex);
            return;
        }

        var layout = CurrentLayout();
        if (!layout.CanScroll || !IsOnStrip(point.Position))
            return;

        var y = point.Position.Y;
        _isPressed = true;
        _isDragging = false;
        _pressY = y;
        // Only a click on a mark already previewed lands it; a click that opens the rail is the track's.
        _pressedWithPreview = _isPreviewing;
        _pressedOnThumb = y >= layout.ThumbTop - 3 && y <= layout.ThumbTop + layout.ThumbLength + 3;
        _grabOffset = _pressedOnThumb ? y - layout.ThumbTop : layout.ThumbLength / 2;
        e.Pointer.Capture(this);
        PseudoClasses.Set(":pressed", true);
        if (!_isOpen)
        {
            _isPointerOnStrip = true;
            _pointerY = y;
            Open();
        }

        e.Handled = true;
        _surface?.InvalidateVisual();
    }

    private void DragTo(double y)
    {
        if (!_isDragging)
        {
            if (Math.Abs(y - _pressY) < DragThreshold)
                return;

            _isDragging = true;
            PseudoClasses.Set(":dragging", true);
            _host?.BeginRailScroll();
            if (_preview is not null)
                _preview.IsHitTestVisible = false;
        }

        _pointerY = y;
        ScrollTo(CurrentLayout().OffsetForThumbTop(y - _grabOffset));
        UpdateFocus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isPressed)
            return;

        var point = e.GetPosition(this);
        var wasDragging = _isDragging;
        var pressedOnThumb = _pressedOnThumb;
        var landsMark = _pressedWithPreview && _focusIndex >= 0;
        EndPress();
        e.Pointer.Capture(null);
        e.Handled = true;

        if (!wasDragging)
        {
            if (landsMark)
            {
                InvokeMark(_focusIndex);
            }
            else if (!pressedOnThumb)
            {
                // Away from a previewed mark, the track jumps: the thumb centers where it was clicked.
                var layout = CurrentLayout();
                _host?.BeginRailScroll();
                ScrollTo(layout.OffsetForThumbTop(point.Y - layout.ThumbLength / 2));
            }
        }

        _isPointerOnStrip = IsOnStrip(point);
        if (_isPointerOnStrip)
        {
            _pointerY = point.Y;
            UpdateFocus();
        }
        else
        {
            UpdateFocus();
            _closeTimer.Start();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_isPressed)
            return;

        EndPress();
        UpdateFocus();
        if (!_isPointerOnStrip && !_isPointerOnPreview)
            _closeTimer.Start();
    }

    private void EndPress()
    {
        if (!_isPressed && !_isDragging)
            return;

        _isPressed = false;
        _isDragging = false;
        PseudoClasses.Set(":pressed", false);
        PseudoClasses.Set(":dragging", false);
        // A drag left the preview untouchable; a card still showing can be clicked again.
        if (_preview is not null)
            _preview.IsHitTestVisible = _isPreviewing;
        _surface?.InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // The rail sits beside the transcript, outside its scroller: the wheel scrolls the transcript.
        if (!e.Handled && _host is not null)
            e.Handled = _host.ForwardWheelToTranscript(e);
    }

    private void ScrollTo(double offset)
    {
        if (_scrollViewer is { } viewer && Math.Abs(viewer.Offset.Y - offset) > 0.01)
            viewer.Offset = viewer.Offset.WithY(offset);
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    internal void RenderSurface(DrawingContext context, Size size)
    {
        var layout = CurrentLayout(size.Height);
        if (!layout.CanScroll)
            return;

        var width = size.Width;
        var reach = Math.Min(width, _isOpen || _isPressed ? OpenReach : RestingReach);
        // Fill what the pointer may take, so the strip is hit-tested and everything left of it is not.
        context.FillRectangle(Brushes.Transparent, new Rect(width - reach, 0, reach, size.Height));

        var openness = Math.Clamp(Openness, 0, 1);
        var lineX = width - LineInset;
        if (openness > 0.01 && TrackBrush is { } track)
        {
            using (context.PushOpacity(openness))
                context.DrawRectangle(track, null, new RoundedRect(new Rect(lineX - 1, layout.TrackTop, 2, layout.TrackLength), 1));
        }

        var thumbBrush = _isPressed ? ActiveThumbBrush ?? ThumbBrush : ThumbBrush;
        var thumbTop = layout.ThumbTop;
        var thumbLength = layout.ThumbLength;
        if (thumbBrush is not null)
        {
            var thumbWidth = ThumbWidth + (OpenThumbWidth - ThumbWidth) * openness;
            context.DrawRectangle(thumbBrush, null,
                new RoundedRect(new Rect(lineX - thumbWidth / 2, thumbTop, thumbWidth, thumbLength), thumbWidth / 2));
        }

        var ys = _markYs;
        if (openness <= 0.01 || ys.Length == 0)
            return;

        var tickRight = width - TickEnd;
        var focus = _focusIndex >= 0 && _focusIndex < ys.Length ? _focusIndex : -1;
        // Marks swell around the pointer, or around the message being passed while the thumb is dragged.
        var hasCenter = _isDragging ? focus >= 0 : _isPointerOnStrip || focus >= 0;
        var center = _isDragging || !_isPointerOnStrip ? (focus >= 0 ? ys[focus] : 0) : _pointerY;
        // Opening, marks ripple out from where the reader is; closing, they fold back toward it.
        var origin = hasCenter ? center : thumbTop + thumbLength / 2;
        var thumbBottom = thumbTop + thumbLength;
        for (var i = 0; i < ys.Length; i++)
        {
            if (i == focus)
                continue;

            var y = ys[i];
            var reveal = Reveal(openness, y, origin, layout.TrackLength);
            var brush = y >= thumbTop && y <= thumbBottom ? VisibleMarkBrush ?? MarkBrush : MarkBrush;
            if (brush is null || reveal <= 0.01)
                continue;

            var near = hasCenter ? Math.Exp(-(y - center) * (y - center) / (2 * MagnifySigma * MagnifySigma)) : 0;
            var length = (TickLength + (NearTickLength - TickLength) * near) * (0.45 + 0.55 * reveal);
            var tick = new RoundedRect(new Rect(tickRight - length, y - TickThickness / 2, length, TickThickness), TickThickness / 2);
            if (reveal >= 0.999)
            {
                context.DrawRectangle(brush, null, tick);
                continue;
            }

            using (context.PushOpacity(reveal))
                context.DrawRectangle(brush, null, tick);
        }

        if (focus >= 0 && (FocusedMarkBrush ?? VisibleMarkBrush ?? MarkBrush) is { } focusedBrush)
        {
            var y = ys[focus];
            var length = FocusedTickLength * (0.45 + 0.55 * openness);
            var tick = new Rect(tickRight - length, y - FocusedTickThickness / 2, length, FocusedTickThickness);
            using (context.PushOpacity(openness))
            {
                // A soft halo, then the mark.
                using (context.PushOpacity(0.28))
                    context.DrawRectangle(focusedBrush, null, new RoundedRect(tick.Inflate(2.5), FocusedTickThickness / 2 + 2.5));
                context.DrawRectangle(focusedBrush, null, new RoundedRect(tick, FocusedTickThickness / 2));
            }
        }
    }

    /// <summary>How visible a mark is while the rail opens or closes: the nearer the origin, the sooner.</summary>
    internal static double Reveal(double openness, double y, double origin, double trackLength)
    {
        const double spread = 0.75;
        var distance = Math.Abs(y - origin) / Math.Max(1, trackLength);
        return Math.Clamp(openness * (1 + spread) - distance * spread, 0, 1);
    }
}

/// <summary>
/// Draws <see cref="StrataTranscriptRail"/>'s strip: the thumb, its track and the marks. Part of the rail's
/// template; it takes the pointer only over the strip the rail draws as hit-testable.
/// </summary>
public sealed class StrataTranscriptRailSurface : Control
{
    internal StrataTranscriptRail? Rail { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rail?.RenderSurface(context, Bounds.Size);
    }
}

/// <summary>
/// Lays out <see cref="StrataTranscriptRail"/>'s preview: each child at its own size against the trailing
/// edge, centered on <see cref="AnchorY"/> and kept inside the layer. Styles animate the anchor, so the
/// preview glides from mark to mark.
/// </summary>
public sealed class StrataTranscriptRailPreviewLayer : Panel
{
    private const double EdgeGap = 4;

    public static readonly StyledProperty<double> AnchorYProperty =
        AvaloniaProperty.Register<StrataTranscriptRailPreviewLayer, double>(nameof(AnchorY));

    static StrataTranscriptRailPreviewLayer()
    {
        AffectsArrange<StrataTranscriptRailPreviewLayer>(AnchorYProperty);
    }

    /// <summary>Where the preview's middle should be, from the layer's top.</summary>
    public double AnchorY { get => GetValue(AnchorYProperty); set => SetValue(AnchorYProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            var top = Math.Clamp(AnchorY - size.Height / 2, EdgeGap, Math.Max(EdgeGap, finalSize.Height - size.Height - EdgeGap));
            child.Arrange(new Rect(Math.Max(0, finalSize.Width - size.Width), top, Math.Min(size.Width, finalSize.Width), size.Height));
        }

        return finalSize;
    }
}
