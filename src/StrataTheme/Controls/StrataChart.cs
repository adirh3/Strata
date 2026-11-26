using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Chart visualization types supported by <see cref="StrataChart"/>.
/// </summary>
public enum StrataChartType
{
    /// <summary>Line chart with smooth curve interpolation and gradient area fill.</summary>
    Line,
    /// <summary>Vertical grouped bar chart with rounded-top bars.</summary>
    Bar,
    /// <summary>Donut ring chart with labeled segments and center metric.</summary>
    Donut,
    /// <summary>Solid pie chart with labeled segments.</summary>
    Pie
}

/// <summary>
/// A named data series for use with <see cref="StrataChart"/>.
/// </summary>
public class StrataChartSeries
{
    /// <summary>Display name shown in the chart legend.</summary>
    public string Name { get; set; } = "";

    /// <summary>Numeric values indexed to match the chart's <see cref="StrataChart.Labels"/>.</summary>
    public IList<double> Values { get; set; } = Array.Empty<double>();
}

/// <summary>
/// Token-driven chart control supporting line, bar, donut, and pie visualizations.
/// Renders using Strata semantic brushes and adapts to Light, Dark, and HighContrast themes.
/// </summary>
/// <remarks>
/// <para><b>XAML usage (line chart):</b></para>
/// <code>
/// &lt;controls:StrataChart ChartType="Line" ChartHeight="200"
///                         Labels="{Binding MonthLabels}"
///                         Series="{Binding RevenueSeries}"
///                         ShowLegend="True" ShowGrid="True" /&gt;
/// </code>
/// <para><b>XAML usage (donut chart):</b></para>
/// <code>
/// &lt;controls:StrataChart ChartType="Donut" ChartHeight="180"
///                         Labels="{Binding CategoryLabels}"
///                         Series="{Binding DistributionSeries}"
///                         DonutCenterValue="100%"
///                         DonutCenterLabel="Total" /&gt;
/// </code>
/// <para>Hovering shows the values under the pointer on a pane of glass. With <see cref="ShowToolbar"/>, a
/// toolbar below the chart offers its data as a table, copying the data, and full screen. The toolbar is quiet ink
/// at rest and raises its glass while a mouse rests on the chart, keyboard focus is inside it, or in full screen.</para>
/// <para><b>Template parts:</b> PART_ChartHost (Panel), PART_TipLayer (Canvas), PART_Tip (Panel),
/// PART_TipRows (StackPanel), PART_TableHost (Panel), PART_TableScroll (ScrollViewer), and the toolbar's
/// PART_ChartView, PART_TableView, PART_CopyData, PART_CopyIcon, PART_Expand
/// and PART_ExpandIcon.</para>
/// <para><b>Pseudo-classes:</b> :table (the data as a table in place of the plot), :fullscreen, :engaged (a mouse
/// resting on the chart or keyboard focus inside it).</para>
/// </remarks>
public partial class StrataChart : TemplatedControl
{
    private Panel? _chartHost;
    private ChartCanvas? _canvas;

    // ── Styled Properties ──────────────────────────────────────

    /// <summary>Identifies the <see cref="ChartType"/> styled property.</summary>
    public static readonly StyledProperty<StrataChartType> ChartTypeProperty =
        AvaloniaProperty.Register<StrataChart, StrataChartType>(nameof(ChartType));

    /// <summary>Identifies the <see cref="Labels"/> styled property.</summary>
    public static readonly StyledProperty<IList<string>?> LabelsProperty =
        AvaloniaProperty.Register<StrataChart, IList<string>?>(nameof(Labels));

    /// <summary>Identifies the <see cref="Series"/> styled property.</summary>
    public static readonly StyledProperty<IList<StrataChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<StrataChart, IList<StrataChartSeries>?>(nameof(Series));

    /// <summary>Identifies the <see cref="ShowLegend"/> styled property.</summary>
    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<StrataChart, bool>(nameof(ShowLegend), true);

    /// <summary>Identifies the <see cref="ShowGrid"/> styled property.</summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<StrataChart, bool>(nameof(ShowGrid), true);

    /// <summary>Identifies the <see cref="ChartHeight"/> styled property.</summary>
    public static readonly StyledProperty<double> ChartHeightProperty =
        AvaloniaProperty.Register<StrataChart, double>(nameof(ChartHeight), 200);

    /// <summary>Identifies the <see cref="DonutCenterLabel"/> styled property.</summary>
    public static readonly StyledProperty<string?> DonutCenterLabelProperty =
        AvaloniaProperty.Register<StrataChart, string?>(nameof(DonutCenterLabel));

    /// <summary>Identifies the <see cref="DonutCenterValue"/> styled property.</summary>
    public static readonly StyledProperty<string?> DonutCenterValueProperty =
        AvaloniaProperty.Register<StrataChart, string?>(nameof(DonutCenterValue));

    /// <summary>Identifies the <see cref="ShowToolbar"/> styled property.</summary>
    public static readonly StyledProperty<bool> ShowToolbarProperty =
        AvaloniaProperty.Register<StrataChart, bool>(nameof(ShowToolbar));

    static StrataChart()
    {
        ChartTypeProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        LabelsProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        SeriesProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        ShowGridProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        ShowLegendProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        ChartHeightProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        DonutCenterLabelProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
        DonutCenterValueProperty.Changed.AddClassHandler<StrataChart>((c, _) => c.InvalidateChart());
    }

    /// <summary>Creates a chart whose toolbar stays quiet until it is reached for.</summary>
    public StrataChart()
    {
        ToolbarEngagement.Attach(this, PseudoClasses);
    }

    // ── Public Properties ──────────────────────────────────────

    /// <summary>Gets or sets the chart visualization type.</summary>
    public StrataChartType ChartType
    {
        get => GetValue(ChartTypeProperty);
        set => SetValue(ChartTypeProperty, value);
    }

    /// <summary>Gets or sets the X-axis labels (line/bar) or segment labels (donut).</summary>
    public IList<string>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Gets or sets the data series to plot.</summary>
    public IList<StrataChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Gets or sets whether the legend is displayed.</summary>
    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    /// <summary>Gets or sets whether horizontal grid lines are shown.</summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>Gets or sets the chart drawing area height in pixels; <see cref="double.NaN"/> fills the height it is given.</summary>
    public double ChartHeight
    {
        get => GetValue(ChartHeightProperty);
        set => SetValue(ChartHeightProperty, value);
    }

    /// <summary>Gets or sets the small label below the center value in donut charts.</summary>
    public string? DonutCenterLabel
    {
        get => GetValue(DonutCenterLabelProperty);
        set => SetValue(DonutCenterLabelProperty, value);
    }

    /// <summary>Gets or sets the main value shown in the center of donut charts.</summary>
    public string? DonutCenterValue
    {
        get => GetValue(DonutCenterValueProperty);
        set => SetValue(DonutCenterValueProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a toolbar below the chart offers a table of its data, copying
    /// the data and full screen, as a diagram's does. Off by default, for charts that are part of a larger view.
    /// </summary>
    public bool ShowToolbar
    {
        get => GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }

    // ── Layout ─────────────────────────────────────────────────

    /// <summary>
    /// Charts fill all available horizontal space so parent containers
    /// (e.g. left-aligned chat bubbles) expand to their MaxWidth.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var result = base.MeasureOverride(availableSize);
        var w = double.IsInfinity(availableSize.Width)
            ? result.Width
            : Math.Max(result.Width, availableSize.Width);
        return new Size(w, result.Height);
    }

    // ── Lifecycle ──────────────────────────────────────────────

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _chartHost = e.NameScope.Find<Panel>("PART_ChartHost");
        if (_chartHost is not null)
        {
            _canvas = new ChartCanvas(this)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            // Beneath the hover values' pane of glass.
            _chartHost.Children.Insert(0, _canvas);
        }

        ApplyToolbarTemplate(e.NameScope);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
        {
            _canvas?.InvalidateVisual(); // repaint only, no re-animation
            _canvas?.RefreshTip();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Arrow keys walk the chart only when the chart itself has focus, not one of its toolbar buttons.
        if (_canvas is null || e.Handled || !ReferenceEquals(e.Source, this) || _isTableView) return;

        var maxIdx = (Labels?.Count ?? 0) - 1;
        if (maxIdx < 0) return;

        switch (e.Key)
        {
            case Key.Left:
                _canvas.SetHoverIndex(Math.Max(0, _canvas.HoverIndex <= 0 ? 0 : _canvas.HoverIndex - 1));
                e.Handled = true;
                break;
            case Key.Right:
                _canvas.SetHoverIndex(Math.Min(maxIdx, _canvas.HoverIndex + 1));
                e.Handled = true;
                break;
            case Key.Escape when _canvas.HoverIndex >= 0:
                _canvas.SetHoverIndex(-1);
                e.Handled = true;
                break;
        }
    }

    internal void InvalidateChart()
    {
        if (_canvas is not null)
        {
            _canvas.ClearFilters();
            _canvas.InvalidateLayoutCache();
            // Only re-animate if already displayed once; initial animation
            // is handled by OnAttachedToVisualTree deferred callback.
            if (_canvas.HoverIndex < 0) // not during hover interaction
                _canvas.ResetAnimation();
            _canvas.InvalidateVisual();
            _canvas.RefreshTip();
        }

        OnChartDataChanged();
    }

    internal IBrush ResolveBrush(string key, Color fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var res) && res is IBrush b)
            return b;
        return new SolidColorBrush(fallback);
    }

    internal static readonly string[] PaletteKeys =
    {
        "Brush.Chart1",   // Indigo  — signature primary
        "Brush.Chart2",   // Violet  — signature secondary
        "Brush.Chart3",   // Purple  — spectrum midpoint
        "Brush.Chart4",   // Rose    — signature tertiary
        "Brush.Chart5",   // Sky     — cool complement
        "Brush.Chart6",   // Emerald — warm complement
    };

    internal static readonly Color[] PaletteFallbacks =
    {
        Color.Parse("#818CF8"),
        Color.Parse("#A78BFA"),
        Color.Parse("#C084FC"),
        Color.Parse("#F472B6"),
        Color.Parse("#38BDF8"),
        Color.Parse("#34D399"),
    };

    internal IBrush SeriesBrush(int index)
    {
        var idx = index % PaletteKeys.Length;
        return ResolveBrush(PaletteKeys[idx], PaletteFallbacks[idx]);
    }

    /// <summary>
    /// A value as the hover values and the table read it: in full, grouped, to two decimals at most, and under one
    /// to three significant digits, so a small rate never reads as 0.
    /// </summary>
    internal static string FormatFullValue(double value)
    {
        if (!double.IsFinite(value)) return "";
        var size = Math.Abs(value);
        if (size is > 0 and < 1)
        {
            var decimals = Math.Min(15, 2 - (int)Math.Floor(Math.Log10(size)));
            return Math.Round(value, decimals).ToString("0.###############", CultureInfo.CurrentCulture);
        }
        return value.ToString("#,0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>A value as the chart was given it, for copying: every digit, in the reader's number format.</summary>
    internal static string FormatExactValue(double value) =>
        double.IsFinite(value) ? value.ToString("R", CultureInfo.CurrentCulture) : "";

    /// <summary>A part's share of the whole, never shown as 0% while it has any.</summary>
    internal static string FormatShare(double share) =>
        share > 0 && share < 0.005
            ? "<1%"
            : (share * 100).ToString("0", CultureInfo.CurrentCulture) + "%";

    private static double Positive(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    /// <summary>Parts given in percent already (they add up to 100) read as their share alone, not as "25 · 25%".</summary>
    internal static bool ValuesAreShares(IList<double> values)
    {
        var total = 0.0;
        foreach (var value in values)
        {
            if (!double.IsFinite(value) || value < 0)
                return false;
            total += value;
        }
        return Math.Abs(total - 100) < 0.5;
    }

    /// <summary>What the hover pane says about one place on the chart, and where that place is.</summary>
    internal sealed record ChartTip(
        string? Title,
        IReadOnlyList<ChartTipRow> Rows,
        Rect Anchor,
        Vector Direction,
        bool IsRtl);

    /// <summary>One line of the hover pane: a marker in its series' colour, a name and a value.</summary>
    internal readonly record struct ChartTipRow(IBrush Marker, string Name, string Value);

    // ═══════════════════════════════════════════════════════════
    //  CHART CANVAS — all custom rendering
    // ═══════════════════════════════════════════════════════════
    private sealed class ChartCanvas : Control
    {
        private readonly StrataChart _chart;
        private int _hoverIndex = -1;
        private Point? _touchPressAt;
        private bool _legendTapped;

        private const double TouchTapSlop = 10;
        private const double LeftPad = 44;
        private const double RightPad = 14;
        private const double TopPad = 8;
        private const double BottomPad = 26;
        private const double LegendRowH = 26;
        private const int GridLines = 5;
        private const double MaxBarWidth = 36;
        private const double AxisFontSize = 10.5;
        private const double LegendFontSize = 11.5;
        private const double RadialLegendFontSize = 12;

        private static readonly Lazy<Cursor> HandCursor = new(() => new Cursor(StandardCursorType.Hand));

        // ── Legend filtering ───────────────────────────────────
        private readonly HashSet<int> _hiddenIndices = new();
        private readonly List<(int Index, Rect Bounds)> _legendHitRects = new();

        // ── Ring and legend placement, kept while the size and data stay ─────
        private RadialLayout? _radialLayout;
        private Size _radialLayoutSize;

        // ── Animation state ────────────────────────────────────
        private const double AnimDurationMs = 600;
        private readonly Stopwatch _animWatch = new();
        private DispatcherTimer? _animTimer;
        private double _animProgress; // 0 = hidden, 1 = fully visible
        private bool _hasAnimated;
        private bool _entranceReady; // true once deferred post fires after attach
        private bool _hasBeenAttached; // true after first attach (skip delay on reattach)
        private DispatcherTimer? _entranceDelayTimer;

        internal int HoverIndex => _hoverIndex;

        internal void SetHoverIndex(int idx)
        {
            if (_hoverIndex == idx) return;
            _hoverIndex = idx;
            InvalidateVisual();
            RefreshTip();
        }

        internal void ResetAnimation()
        {
            _animProgress = 0;
            _hasAnimated = false;
            _animWatch.Reset();
        }

        internal void ClearFilters() => _hiddenIndices.Clear();

        internal void InvalidateLayoutCache() => _radialLayout = null;

        /// <summary>Shows the values under the pointer on the chart's pane of glass, or hides the pane.</summary>
        internal void RefreshTip() =>
            _chart.ShowTip(_hoverIndex >= 0 && Bounds.Width >= 40 && Bounds.Height >= 40 ? BuildTip(_hoverIndex) : null);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Animation already completed — render instantly on reattach
            if (_hasAnimated)
            {
                _animProgress = 1.0;
                _entranceReady = true;
                return;
            }

            if (_hasBeenAttached)
            {
                // Reattach during streaming — skip the 150ms delay, resume animation
                _entranceReady = true;
                if (!_animWatch.IsRunning && _animProgress < 1.0)
                {
                    _animWatch.Start(); // Resume from paused position
                    _animTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimTick);
                    _animTimer.Start();
                }
                InvalidateVisual();
                return;
            }

            // First attach — use 150ms delay for initial window appearance
            _hasBeenAttached = true;
            _animProgress = 0;
            _entranceReady = false;
            _animWatch.Reset();

            _entranceDelayTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(150),
            };
            _entranceDelayTimer.Tick += OnEntranceDelayTick;
            _entranceDelayTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_entranceDelayTimer is not null)
            {
                _entranceDelayTimer.Tick -= OnEntranceDelayTick;
                _entranceDelayTimer.Stop();
                _entranceDelayTimer = null;
            }
            if (_animTimer is not null)
            {
                _animTimer.Stop();
                _animTimer = null;
            }
            _animWatch.Stop();
            _entranceReady = false;
            _hoverIndex = -1;
            _chart.ShowTip(null);
        }

        private void OnEntranceDelayTick(object? sender, EventArgs e)
        {
            _entranceDelayTimer?.Stop();
            _entranceReady = true;
            InvalidateVisual();
        }

        private void EnsureAnimation()
        {
            if (_hasAnimated) return;
            if (!_entranceReady) return;

            if (!_animWatch.IsRunning)
            {
                _animWatch.Start(); // Start or resume from paused position
                _animTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimTick);
                _animTimer.Start();
            }

            var t = Math.Min(1.0, _animWatch.ElapsedMilliseconds / AnimDurationMs);
            _animProgress = EaseOutCubic(t);

            if (t >= 1.0)
            {
                _animProgress = 1.0;
                _hasAnimated = true;
                _animWatch.Stop();
                _animTimer?.Stop();
            }
        }

        private void OnAnimTick(object? s, EventArgs e) => InvalidateVisual();

        private static double EaseOutCubic(double t)
        {
            var inv = 1.0 - t;
            return 1.0 - inv * inv * inv;
        }

        public ChartCanvas(StrataChart chart)
        {
            _chart = chart;
            ClipToBounds = true;
            IsHitTestVisible = true;
            Tapped += OnTapped;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == FlowDirectionProperty)
            {
                InvalidateVisual();
            }
            else if (change.Property == BoundsProperty && _hoverIndex >= 0)
            {
                // The size changes during layout; the pane is placed once it is done.
                Dispatcher.UIThread.Post(RefreshTip, DispatcherPriority.Render);
            }
        }

        private bool IsHostedInRtlLayout() =>
            FlowDirection == FlowDirection.RightToLeft;

        private Point GetChartPosition(Point position) =>
            IsHostedInRtlLayout()
                ? new Point(Bounds.Width - position.X, position.Y)
                : position;

        private bool IsRadial => _chart.ChartType is StrataChartType.Donut or StrataChartType.Pie;

        // ── Pointer events ─────────────────────────────────────

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            // A finger's values change only with a tap, so a swipe that scrolls past the chart leaves them be.
            if (e.Pointer.Type == PointerType.Touch) return;
            var position = GetChartPosition(e.GetPosition(this));
            Cursor = LegendIndexAt(position) >= 0 ? HandCursor.Value : null;
            SetHoverIndex(HitTest(position));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            SetHoverIndex(-1);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            // A finger acts on its tap, taken where it lifts, so a press that turns into a scroll changes nothing.
            if (e.Pointer.Type == PointerType.Touch)
            {
                _touchPressAt = e.GetPosition(this);
                _legendTapped = false;
                return;
            }
            var position = GetChartPosition(e.GetPosition(this));
            if (ToggleLegendAt(position))
            {
                e.Handled = true;
                return;
            }

            // A pen shows the values where it lands, as a mouse shows them where it hovers.
            SetHoverIndex(HitTest(position));
        }

        // Each tap of a finger counts once, here: a quick second one too, which the platform calls a double tap
        // and may or may not follow with Tapped.
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.Pointer.Type != PointerType.Touch || _touchPressAt is not { } at) return;
            _touchPressAt = null;
            var position = e.GetPosition(this);
            if (((Vector)(position - at)).Length > TouchTapSlop) return;
            _legendTapped = TapAt(position);
            if (_legendTapped)
                e.Handled = true;
        }

        // A scroll took the finger: its press was no tap.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _touchPressAt = null;
        }

        // The tap was taken as the finger lifted; one the legend took ends here, as it always has.
        private void OnTapped(object? sender, TappedEventArgs e)
        {
            if (e.Pointer.Type == PointerType.Touch && _legendTapped)
                e.Handled = true;
        }

        // A finger's tap on a legend entry hides or shows its series; elsewhere it shows the values where it lands,
        // and a second tap on them puts them away. True when the legend took it.
        private bool TapAt(Point point)
        {
            var position = GetChartPosition(point);
            if (ToggleLegendAt(position))
                return true;

            var index = HitTest(position);
            SetHoverIndex(index == _hoverIndex ? -1 : index);
            return false;
        }

        // A legend entry hides or shows its series.
        private bool ToggleLegendAt(Point position)
        {
            var legendIndex = LegendIndexAt(position);
            if (legendIndex < 0) return false;
            if (!_hiddenIndices.Remove(legendIndex))
                _hiddenIndices.Add(legendIndex);
            InvalidateVisual();
            if (_hoverIndex >= 0 && !IsRadial)
                RefreshTip();
            else
                SetHoverIndex(HitTest(position));
            return true;
        }

        private int LegendIndexAt(Point position)
        {
            foreach (var (idx, rect) in _legendHitRects)
            {
                if (rect.Contains(position))
                    return idx;
            }
            return -1;
        }

        // ── Hit testing ────────────────────────────────────────

        private int HitTest(Point pos)
        {
            var series = _chart.Series;
            var labels = _chart.Labels;
            if (series is null || series.Count == 0 || labels is null || labels.Count == 0) return -1;

            if (IsRadial)
                return HitTestRadial(pos);

            var rect = ChartRect();
            if (rect.Width <= 0 || pos.Y < rect.Top - 6 || pos.Y > rect.Bottom + BottomPad
                || pos.X < rect.Left - 8 || pos.X > rect.Right + 8)
                return -1;

            var count = labels.Count;
            if (_chart.ChartType == StrataChartType.Bar)
                return Math.Clamp((int)Math.Floor((pos.X - rect.Left) / (rect.Width / count)), 0, count - 1);

            return count == 1
                ? 0
                : Math.Clamp((int)Math.Round((pos.X - rect.Left) / (rect.Width / (count - 1))), 0, count - 1);
        }

        private int HitTestRadial(Point pos)
        {
            var series = _chart.Series;
            if (series is null || series.Count == 0) return -1;
            var vals = series[0].Values;
            if (vals is null || vals.Count == 0) return -1;

            var layout = GetRadialLayout();
            if (layout.HasLegend)
            {
                var labelCount = _chart.Labels?.Count ?? 0;
                for (int i = 0; i < labelCount; i++)
                {
                    if (LegendRow(layout, i).Contains(pos))
                        return i < vals.Count && !_hiddenIndices.Contains(i) && Positive(vals[i]) > 0 ? i : -1;
                }
            }

            var dx = pos.X - layout.Center.X;
            var dy = pos.Y - layout.Center.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var minR = layout.InnerR > 0 ? layout.InnerR - 4 : 0;
            if (dist < minR || dist > layout.OuterR + 6) return -1;

            var angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
            if (angle < 0) angle += 360;

            var total = VisibleTotal(vals);
            if (total <= 0) return -1;

            double cum = 0;
            for (int i = 0; i < vals.Count; i++)
            {
                if (_hiddenIndices.Contains(i)) continue;
                var sweep = Positive(vals[i]) / total * 360;
                if (sweep > 0 && angle >= cum && angle < cum + sweep) return i;
                cum += sweep;
            }
            return -1;
        }

        private double VisibleTotal(IList<double> vals)
        {
            var total = 0.0;
            for (int i = 0; i < vals.Count; i++)
                if (!_hiddenIndices.Contains(i))
                    total += Positive(vals[i]);
            return total;
        }

        // ── Layout helpers ─────────────────────────────────────

        private Rect ChartRect()
        {
            var b = Bounds;
            var top = _chart.ShowLegend && (_chart.Series?.Count ?? 0) > 0
                ? LegendRowH + TopPad
                : TopPad;
            return new Rect(LeftPad, top,
                Math.Max(0, b.Width - LeftPad - RightPad),
                Math.Max(0, b.Height - top - BottomPad));
        }

        /// <summary>Where a ring or pie and its legend sit: one group, centred, the legend right beside the ring.</summary>
        private readonly record struct RadialLayout(
            Point Center,
            double OuterR,
            double InnerR,
            double LegendLeft,
            double LegendTop,
            double LegendWidth,
            double RowHeight,
            double LabelWidth,
            bool IsRtl,
            bool HasLegend);

        private RadialLayout GetRadialLayout()
        {
            var size = Bounds.Size;
            if (_radialLayout is { } cached && _radialLayoutSize == size)
                return cached;

            var labels = _chart.Labels ?? Array.Empty<string>();
            var count = labels.Count;
            var hasLegend = _chart.ShowLegend && count > 0;
            var isRtl = hasLegend && GetContentFlowDirection(labels) == FlowDirection.RightToLeft;
            const double pad = 10, ringGap = 32, markerWidth = 10, markerGap = 8, shareGap = 16;

            double labelWidth = 0, shareWidth = 0;
            if (hasLegend)
            {
                foreach (var label in labels)
                    labelWidth = Math.Max(labelWidth, Txt(label, RadialLegendFontSize, Brushes.Black).Width);
                labelWidth = Math.Min(labelWidth, 220);
                shareWidth = Txt("100%", RadialLegendFontSize, Brushes.Black, FontWeight.SemiBold).Width;
            }

            double LegendWidth() => hasLegend ? markerWidth + markerGap + labelWidth + shareGap + shareWidth : 0;
            var availableWidth = Math.Max(0, size.Width - pad * 2);
            var availableHeight = Math.Max(0, size.Height - pad * 2);
            var gap = hasLegend ? ringGap : 0;
            var diameter = Math.Min(availableHeight, Math.Max(260, availableWidth * 0.45));

            // Where the group doesn't fit, the ring gives way first, down to a readable size, then the labels.
            var over = diameter + gap + LegendWidth() - availableWidth;
            if (over > 0)
                diameter = Math.Max(Math.Min(diameter, 96), diameter - over);
            over = diameter + gap + LegendWidth() - availableWidth;
            if (over > 0 && hasLegend)
                labelWidth = Math.Max(40, labelWidth - over);
            over = diameter + gap + LegendWidth() - availableWidth;
            if (over > 0)
                diameter = Math.Max(40, diameter - over);

            var legendWidth = LegendWidth();
            var left = Math.Max(pad, (size.Width - (diameter + gap + legendWidth)) / 2);
            var ringLeft = isRtl ? left + legendWidth + gap : left;
            var legendLeft = isRtl ? left : left + diameter + gap;
            // A margin around the ring leaves room for the part under the pointer to stand out.
            var outerR = Math.Max(14, diameter / 2 - 6);
            var center = new Point(ringLeft + diameter / 2, size.Height / 2);
            var innerR = _chart.ChartType == StrataChartType.Donut ? outerR * 0.62 : 0;
            var rowHeight = count > 0 ? Math.Clamp(availableHeight / count, 18, 26) : 0;
            var legendTop = Math.Max(pad, center.Y - rowHeight * count / 2);

            var layout = new RadialLayout(center, outerR, innerR, legendLeft, legendTop, legendWidth,
                rowHeight, labelWidth, isRtl, hasLegend);
            _radialLayout = layout;
            _radialLayoutSize = size;
            return layout;
        }

        private static Rect LegendRow(RadialLayout layout, int index) =>
            new(layout.LegendLeft - 6, layout.LegendTop + index * layout.RowHeight, layout.LegendWidth + 12, layout.RowHeight);

        private static Vector Direction(double degrees)
        {
            var rad = (degrees - 90) * Math.PI / 180;
            return new Vector(Math.Cos(rad), Math.Sin(rad));
        }

        // ── Render dispatch ────────────────────────────────────

        public override void Render(DrawingContext ctx)
        {
            var bounds = Bounds;
            // The whole plot takes the pointer, not only what is drawn: a finger lands between bars, and a mouse
            // crossing a gap keeps the values up.
            ctx.FillRectangle(Brushes.Transparent, new Rect(bounds.Size));
            if (IsHostedInRtlLayout())
            {
                using (ctx.PushTransform(
                           Matrix.CreateScale(-1, 1) *
                           Matrix.CreateTranslation(bounds.Width, 0)))
                {
                    RenderCore(ctx, bounds);
                }
                return;
            }

            RenderCore(ctx, bounds);
        }

        /// <summary>The theme's brushes for one paint.</summary>
        private sealed class Ink(StrataChart chart)
        {
            public IBrush Grid { get; } = chart.ResolveBrush("Brush.GlassDivider", Color.Parse("#14FFFFFF"));
            public IBrush Baseline { get; } = chart.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));
            public IBrush Label { get; } = chart.ResolveBrush("Brush.TextTertiary", Color.Parse("#8A8A8A"));
            public IBrush Secondary { get; } = chart.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            public IBrush Text { get; } = chart.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            public IBrush Band { get; } = chart.ResolveBrush("Brush.GlassRowHover", Color.Parse("#0FFFFFFF"));
            public IBrush Guide { get; } = chart.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
        }

        private void RenderCore(DrawingContext ctx, Rect b)
        {
            _legendHitRects.Clear();
            if (b.Width < 40 || b.Height < 40) return;

            var series = _chart.Series;
            if (series is null || series.Count == 0) return;

            EnsureAnimation();
            var ink = new Ink(_chart);

            switch (_chart.ChartType)
            {
                case StrataChartType.Line:
                    if (_chart.ShowLegend) DrawLegend(ctx, b, series, ink);
                    DrawLineChart(ctx, series, ink);
                    break;
                case StrataChartType.Bar:
                    if (_chart.ShowLegend) DrawLegend(ctx, b, series, ink);
                    DrawBarChart(ctx, series, ink);
                    break;
                case StrataChartType.Donut:
                case StrataChartType.Pie:
                    DrawRadialChart(ctx, series, ink);
                    break;
            }
        }

        // ── Legend ─────────────────────────────────────────────

        // A row above the plot: rounded swatches with their series' names, in reading order.
        private void DrawLegend(DrawingContext ctx, Rect bounds, IList<StrataChartSeries> series, Ink ink)
        {
            var isRtl = GetContentFlowDirection(series.Select(item => item.Name)) == FlowDirection.RightToLeft;
            const double marker = 9, markerGap = 6, itemGap = 18, rowH = 18;
            var x = isRtl ? bounds.Right - RightPad : LeftPad;

            for (int i = 0; i < series.Count; i++)
            {
                var hidden = _hiddenIndices.Contains(i);
                var ft = Txt(series[i].Name, LegendFontSize, hidden ? ink.Label : ink.Secondary);
                var itemW = marker + markerGap + ft.Width;
                var itemLeft = isRtl ? x - itemW : x;
                var markerLeft = isRtl ? x - marker : x;
                var textLeft = isRtl ? itemLeft : x + marker + markerGap;

                DrawLegendMarker(ctx, _chart.SeriesBrush(i), new Rect(markerLeft, TopPad + (rowH - marker) / 2, marker, marker), hidden);
                ctx.DrawText(ft, new Point(textLeft, TopPad + (rowH - ft.Height) / 2));
                _legendHitRects.Add((i, new Rect(itemLeft, TopPad, itemW, rowH)));
                x = isRtl ? itemLeft - itemGap : x + itemW + itemGap;
            }
        }

        // A hidden series keeps an outline of its swatch, so it can be brought back.
        private static void DrawLegendMarker(DrawingContext ctx, IBrush brush, Rect rect, bool hidden)
        {
            if (hidden)
                ctx.DrawRectangle(null, new Pen(brush, 1.5), rect.Deflate(0.75), 2.5, 2.5);
            else
                ctx.DrawRectangle(brush, null, rect, 2.5, 2.5);
        }

        private static FlowDirection GetContentFlowDirection(IEnumerable<string> values)
        {
            return MermaidTextHelper.GetFlowDirection(
                string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        // ── Grid + Axes ────────────────────────────────────────

        // Quiet guide lines and their values, on a firmer baseline at zero. Without the grid only the baseline stays.
        private void DrawGridAndAxes(DrawingContext ctx, Rect rect, ValueAxis axis, Ink ink)
        {
            if (_chart.ShowGrid)
            {
                var pen = new Pen(ink.Grid, 1);
                for (int i = 0; i <= axis.Steps; i++)
                {
                    var value = axis.Tick(i);
                    var y = axis.Y(rect, value);
                    if (i != axis.Below)
                        ctx.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));

                    var ft = Txt(FormatValue(value), AxisFontSize, ink.Label);
                    ctx.DrawText(ft, new Point(rect.Left - ft.Width - 8, y - ft.Height / 2));
                }
            }

            var zero = axis.Y(rect, 0);
            ctx.DrawLine(new Pen(ink.Baseline, 1), new Point(rect.Left, zero), new Point(rect.Right, zero));
        }

        // Labels under the plot, thinned out when they would run into each other; the hovered one is lit.
        private void DrawXLabels(DrawingContext ctx, Rect rect, IList<string> labels, Func<int, double> xOf, Ink ink)
        {
            var count = labels.Count;
            if (count == 0) return;

            var texts = new FormattedText[count];
            var widest = 0.0;
            for (int i = 0; i < count; i++)
            {
                texts[i] = Txt(labels[i], AxisFontSize, i == _hoverIndex ? ink.Text : ink.Label);
                widest = Math.Max(widest, texts[i].Width);
            }

            var spacing = count > 1 ? Math.Abs(xOf(1) - xOf(0)) : double.PositiveInfinity;
            var step = Math.Max(1, (int)Math.Ceiling((widest + 10) / Math.Max(1, spacing)));
            var width = Bounds.Width;
            for (int i = 0; i < count; i += step)
            {
                var ft = texts[i];
                var x = Math.Max(2, Math.Min(xOf(i) - ft.Width / 2, width - ft.Width - 2));
                ctx.DrawText(ft, new Point(x, rect.Bottom + 7));
            }
        }

        // ── Line Chart ────────────────────────────────────────

        private void DrawLineChart(DrawingContext ctx, IList<StrataChartSeries> series, Ink ink)
        {
            var labels = _chart.Labels;
            if (labels is null || labels.Count < 2) return;

            var rect = ChartRect();
            if (rect.Width < 10 || rect.Height < 10) return;

            var axis = LineAxis(series);
            var zero = axis.Y(rect, 0);
            var labelCount = labels.Count;
            double X(int i) => rect.Left + (double)i / (labelCount - 1) * rect.Width;

            DrawGridAndAxes(ctx, rect, axis, ink);
            DrawXLabels(ctx, rect, labels, X, ink);

            // Hover guide: a quiet dashed rule through the hovered values.
            if (_hoverIndex >= 0 && _hoverIndex < labelCount)
            {
                var gx = X(_hoverIndex);
                var guidePen = new Pen(ink.Guide, 1, dashStyle: new DashStyle(new double[] { 3, 3 }, 0));
                ctx.DrawLine(guidePen, new Point(gx, rect.Top), new Point(gx, rect.Bottom));
            }

            for (int si = 0; si < series.Count; si++)
            {
                if (_hiddenIndices.Contains(si)) continue;
                var s = series[si];
                var count = Math.Min(s.Values.Count, labelCount);
                if (count < 2) continue;

                var brush = _chart.SeriesBrush(si);
                var color = SeriesColor(si);

                // Map data to screen points (animated: grow from zero)
                var pts = new Point[count];
                for (int i = 0; i < count; i++)
                {
                    var rawY = axis.Y(rect, s.Values[i]);
                    var animY = zero + (rawY - zero) * _animProgress;
                    pts[i] = new Point(X(i), animY);
                }

                // Area fill — Strata spectrum gradient
                var areaGeo = new StreamGeometry();
                using (var gc = areaGeo.Open())
                {
                    gc.BeginFigure(new Point(pts[0].X, zero), true);
                    gc.LineTo(pts[0]);
                    for (int i = 1; i < count; i++)
                    {
                        var (cp1, cp2) = CatmullRom(pts, i - 1);
                        gc.CubicBezierTo(cp1, cp2, pts[i]);
                    }
                    gc.LineTo(new Point(pts[^1].X, zero));
                    gc.EndFigure(true);
                }

                // For the first series, use a two-tone gradient echoing the StratumLine;
                // subsequent series get a single-tone wash.
                IBrush areaFill;
                if (si == 0 && series.Count <= 2)
                {
                    var c2 = series.Count > 1 ? SeriesColor(1) : color;
                    areaFill = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = new GradientStops
                        {
                            new(Color.FromArgb(60, color.R, color.G, color.B), 0),
                            new(Color.FromArgb(20, c2.R, c2.G, c2.B), 0.7),
                            new(Color.FromArgb(0, c2.R, c2.G, c2.B), 1),
                        }
                    };
                }
                else
                {
                    areaFill = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = new GradientStops
                        {
                            new(Color.FromArgb(40, color.R, color.G, color.B), 0),
                            new(Color.FromArgb(4, color.R, color.G, color.B), 1),
                        }
                    };
                }
                ctx.DrawGeometry(areaFill, null, areaGeo);

                // Smooth line
                var lineGeo = new StreamGeometry();
                using (var gc = lineGeo.Open())
                {
                    gc.BeginFigure(pts[0], false);
                    for (int i = 1; i < count; i++)
                    {
                        var (cp1, cp2) = CatmullRom(pts, i - 1);
                        gc.CubicBezierTo(cp1, cp2, pts[i]);
                    }
                    gc.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(brush, 2.25, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), lineGeo);

                // Points: small where they are few enough to read, and a haloed dot where the pointer is.
                for (int i = 0; i < count; i++)
                {
                    if (i == _hoverIndex)
                    {
                        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(56, color.R, color.G, color.B)), null, pts[i], 8, 8);
                        ctx.DrawEllipse(brush, null, pts[i], 4.25, 4.25);
                    }
                    else if (count <= 24)
                    {
                        ctx.DrawEllipse(brush, null, pts[i], 2.5, 2.5);
                    }
                }
            }
        }

        // ── Bar Chart ──────────────────────────────────────────

        /// <summary>Where a category's bars sit: centred in its band, each at most <see cref="MaxBarWidth"/> wide.</summary>
        private static (double GroupWidth, double BarWidth, double BarGap, double ClusterWidth) BarMetrics(
            Rect rect, int labelCount, int visibleCount)
        {
            var groupWidth = rect.Width / Math.Max(1, labelCount);
            var inner = groupWidth * 0.72;
            var barGap = visibleCount > 1 ? Math.Min(4, inner * 0.06) : 0;
            var barWidth = visibleCount > 0
                ? Math.Clamp((inner - barGap * (visibleCount - 1)) / visibleCount, 2, MaxBarWidth)
                : 0;
            return (groupWidth, barWidth, barGap, barWidth * visibleCount + barGap * Math.Max(0, visibleCount - 1));
        }

        private void DrawBarChart(DrawingContext ctx, IList<StrataChartSeries> series, Ink ink)
        {
            var labels = _chart.Labels;
            if (labels is null || labels.Count == 0) return;

            var rect = ChartRect();
            if (rect.Width < 10 || rect.Height < 10) return;

            // Visible series for layout calculations
            var visibleSeriesIndices = new List<int>();
            for (int i = 0; i < series.Count; i++)
                if (!_hiddenIndices.Contains(i)) visibleSeriesIndices.Add(i);

            var maxVal = NiceMax(MaxVisibleValue(series));
            if (maxVal <= 0) maxVal = 1;

            var labelCount = labels.Count;
            var visCount = visibleSeriesIndices.Count;
            var (groupWidth, barWidth, barGap, clusterWidth) = BarMetrics(rect, labelCount, visCount);
            var cornerR = Math.Min(6, barWidth / 2.5);
            double Center(int i) => rect.Left + (i + 0.5) * groupWidth;

            // The hovered category's band, behind its bars.
            if (_hoverIndex >= 0 && _hoverIndex < labelCount)
            {
                var bandWidth = Math.Max(clusterWidth + 12, Math.Min(groupWidth - 6, clusterWidth + 28));
                var band = new Rect(Center(_hoverIndex) - bandWidth / 2, rect.Top - 4, bandWidth, rect.Height + 4);
                ctx.DrawRectangle(ink.Band, null, band, 8, 8);
            }

            DrawGridAndAxes(ctx, rect, ValueAxis.FromZero(maxVal), ink);
            DrawXLabels(ctx, rect, labels, Center, ink);

            for (int i = 0; i < labelCount; i++)
            {
                var clusterLeft = Center(i) - clusterWidth / 2;
                for (int vi = 0; vi < visCount; vi++)
                {
                    var si = visibleSeriesIndices[vi];
                    if (i >= series[si].Values.Count) continue;

                    var val = Math.Max(0, series[si].Values[i]);
                    var barH = val / maxVal * rect.Height * _animProgress;
                    if (barH < 1) continue;

                    var barRect = new Rect(clusterLeft + vi * (barWidth + barGap), rect.Bottom - barH, barWidth, barH);
                    DrawTopRoundedRect(ctx, barRect, cornerR, _chart.SeriesBrush(si));
                }
            }
        }

        private double MaxVisibleValue(IList<StrataChartSeries> series)
        {
            var max = 0.0;
            for (int si = 0; si < series.Count; si++)
            {
                if (_hiddenIndices.Contains(si)) continue;
                foreach (var value in series[si].Values)
                    if (double.IsFinite(value) && value > max) max = value;
            }
            return max;
        }

        private double MinVisibleValue(IList<StrataChartSeries> series)
        {
            var min = 0.0;
            for (int si = 0; si < series.Count; si++)
            {
                if (_hiddenIndices.Contains(si)) continue;
                foreach (var value in series[si].Values)
                    if (double.IsFinite(value) && value < min) min = value;
            }
            return min;
        }

        // A line goes below zero where its values do, a loss or a frost, and its axis reaches down to meet it.
        private ValueAxis LineAxis(IList<StrataChartSeries> series)
        {
            var max = MaxVisibleValue(series);
            var min = MinVisibleValue(series);
            if (min >= 0)
                return ValueAxis.FromZero(NiceMax(max));
            var step = NiceStep((max - min) / GridLines);
            return new ValueAxis(step, (int)Math.Ceiling(-min / step), (int)Math.Ceiling(max / step));
        }

        /// <summary>
        /// A value axis: <see cref="Below"/> even steps under zero and <see cref="Above"/> over it, so zero is
        /// always on a tick. One with nothing under zero starts there.
        /// </summary>
        private readonly record struct ValueAxis(double Step, int Below, int Above)
        {
            public static ValueAxis FromZero(double max) => new(max / GridLines, 0, GridLines);

            public int Steps => Below + Above;

            public double Tick(int i) => (i - Below) * Step;

            public double Y(Rect rect, double value) =>
                rect.Bottom - (value + Below * Step) / (Steps * Step) * rect.Height;
        }

        // ── Donut and Pie ──────────────────────────────────────

        private void DrawRadialChart(DrawingContext ctx, IList<StrataChartSeries> series, Ink ink)
        {
            var vals = series[0].Values;
            if (vals is null || vals.Count == 0) return;

            var layout = GetRadialLayout();
            var donut = _chart.ChartType == StrataChartType.Donut;
            var total = VisibleTotal(vals);

            if (total > 0)
            {
                var visibleCount = 0;
                for (int i = 0; i < vals.Count; i++)
                    if (!_hiddenIndices.Contains(i) && Positive(vals[i]) > 0) visibleCount++;

                // Even gaps between the parts, whatever lies behind the chart.
                var gap = visibleCount > 1 ? (donut ? 3.0 : 2.5) : 0;
                var budget = 360.0 * _animProgress;
                double angle = 0;
                for (int i = 0; i < vals.Count; i++)
                {
                    if (_hiddenIndices.Contains(i)) continue;
                    var sweep = Positive(vals[i]) / total * 360.0;
                    if (sweep <= 0) continue;

                    var drawn = Math.Min(sweep, Math.Max(0, budget - angle));
                    if (drawn > 0.2)
                    {
                        var center = layout.Center;
                        var outerR = layout.OuterR;
                        if (i == _hoverIndex)
                        {
                            center += Direction(angle + drawn / 2) * 4;
                            outerR += 2;
                        }
                        DrawSlice(ctx, center, layout.InnerR, outerR, angle, drawn, gap, _chart.SeriesBrush(i));
                    }
                    angle += sweep;
                }
            }

            if (donut)
                DrawDonutCenter(ctx, layout, ink);
            if (layout.HasLegend)
                DrawRadialLegend(ctx, layout, vals, total, ink);
        }

        private void DrawDonutCenter(DrawingContext ctx, RadialLayout layout, Ink ink)
        {
            var value = _chart.DonutCenterValue;
            var label = _chart.DonutCenterLabel;
            if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(label)) return;

            var maxWidth = layout.InnerR * 1.6;
            var valueText = string.IsNullOrEmpty(value)
                ? null
                : Fit(Txt(value, Math.Clamp(layout.InnerR * 0.36, 13, 26), ink.Text, FontWeight.SemiBold), maxWidth);
            var labelText = string.IsNullOrEmpty(label)
                ? null
                : Fit(Txt(label, Math.Clamp(layout.InnerR * 0.19, 10, 12.5), ink.Label), maxWidth);

            var y = layout.Center.Y - ((valueText?.Height ?? 0) + (labelText?.Height ?? 0)) / 2;
            if (valueText is not null)
            {
                ctx.DrawText(valueText, new Point(layout.Center.X - valueText.Width / 2, y));
                y += valueText.Height;
            }
            if (labelText is not null)
                ctx.DrawText(labelText, new Point(layout.Center.X - labelText.Width / 2, y));
        }

        // Each part's swatch, name and share in a column beside the ring; the hovered one is lit.
        private void DrawRadialLegend(DrawingContext ctx, RadialLayout layout, IList<double> vals, double total, Ink ink)
        {
            var labels = _chart.Labels;
            if (labels is null) return;

            for (int i = 0; i < labels.Count; i++)
            {
                var row = LegendRow(layout, i);
                var hidden = _hiddenIndices.Contains(i);
                var hovered = i == _hoverIndex && !hidden;
                if (hovered)
                    ctx.DrawRectangle(ink.Band, null, row.Deflate(new Thickness(0, 1)), 7, 7);

                var centerY = row.Y + row.Height / 2;
                var markerLeft = layout.IsRtl ? layout.LegendLeft + layout.LegendWidth - 10 : layout.LegendLeft;
                DrawLegendMarker(ctx, _chart.SeriesBrush(i), new Rect(markerLeft, centerY - 5, 10, 10), hidden);

                var name = Fit(Txt(labels[i], RadialLegendFontSize, hidden ? ink.Label : hovered ? ink.Text : ink.Secondary),
                    layout.LabelWidth);
                var nameLeft = layout.IsRtl ? markerLeft - 8 - name.Width : markerLeft + 18;
                ctx.DrawText(name, new Point(nameLeft, centerY - name.Height / 2));

                if (!hidden && i < vals.Count && total > 0)
                {
                    var share = Txt(FormatShare(Positive(vals[i]) / total), RadialLegendFontSize, ink.Text, FontWeight.SemiBold);
                    var shareLeft = layout.IsRtl ? layout.LegendLeft : layout.LegendLeft + layout.LegendWidth - share.Width;
                    ctx.DrawText(share, new Point(shareLeft, centerY - share.Height / 2));
                }

                _legendHitRects.Add((i, row));
            }
        }

        // ── Hover values ───────────────────────────────────────

        private ChartTip? BuildTip(int index)
        {
            var series = _chart.Series;
            var labels = _chart.Labels;
            if (series is null || series.Count == 0 || labels is null || index >= labels.Count) return null;

            return IsRadial ? BuildRadialTip(index, series[0].Values, labels) : BuildCartesianTip(index, series, labels);
        }

        private ChartTip? BuildCartesianTip(int index, IList<StrataChartSeries> series, IList<string> labels)
        {
            var rect = ChartRect();
            if (rect.Width < 10 || rect.Height < 10) return null;

            var rows = new List<ChartTipRow>();
            var visibleCount = 0;
            var top = rect.Bottom;
            var bar = _chart.ChartType == StrataChartType.Bar;
            var axis = bar ? ValueAxis.FromZero(NiceMax(MaxVisibleValue(series))) : LineAxis(series);
            for (int si = 0; si < series.Count; si++)
            {
                if (_hiddenIndices.Contains(si)) continue;
                visibleCount++;
                // A value that is missing marks nothing on the plot and says nothing on the pane.
                if (index >= series[si].Values.Count || !double.IsFinite(series[si].Values[index])) continue;
                var value = series[si].Values[index];
                rows.Add(new ChartTipRow(_chart.SeriesBrush(si), series[si].Name, FormatFullValue(value)));
                top = Math.Min(top, axis.Y(rect, bar ? Math.Max(0, value) : value));
            }
            if (rows.Count == 0) return null;

            double left, right;
            if (_chart.ChartType == StrataChartType.Bar)
            {
                var (groupWidth, _, _, clusterWidth) = BarMetrics(rect, labels.Count, visibleCount);
                var center = rect.Left + (index + 0.5) * groupWidth;
                left = center - clusterWidth / 2;
                right = center + clusterWidth / 2;
            }
            else
            {
                left = right = labels.Count > 1
                    ? rect.Left + (double)index / (labels.Count - 1) * rect.Width
                    : rect.Left + rect.Width / 2;
            }

            var isRtl = GetContentFlowDirection(rows.Select(row => row.Name).Prepend(labels[index]))
                == FlowDirection.RightToLeft;
            return new ChartTip(labels[index], rows, new Rect(left, top, right - left, 0), new Vector(1, 0), isRtl);
        }

        private ChartTip? BuildRadialTip(int index, IList<double> vals, IList<string> labels)
        {
            if (index >= vals.Count || _hiddenIndices.Contains(index)) return null;
            var total = VisibleTotal(vals);
            var value = Positive(vals[index]);
            if (total <= 0 || value <= 0) return null;

            var layout = GetRadialLayout();
            double start = 0;
            for (int i = 0; i < index; i++)
                if (!_hiddenIndices.Contains(i))
                    start += Positive(vals[i]) / total * 360.0;
            var direction = Direction(start + value / total * 180.0);
            var anchor = layout.Center + direction * (layout.OuterR + 10);

            var row = new ChartTipRow(_chart.SeriesBrush(index), labels[index],
                ValuesAreShares(vals)
                    ? FormatShare(value / total)
                    : $"{FormatFullValue(vals[index])} · {FormatShare(value / total)}");
            return new ChartTip(null, new[] { row }, new Rect(anchor, anchor), direction,
                MermaidTextHelper.GetFlowDirection(labels[index]) == FlowDirection.RightToLeft);
        }

        // ── Geometry helpers ───────────────────────────────────

        /// <summary>
        /// One part of a ring (or, with no inner radius, a pie), its sides kept <paramref name="gap"/> apart from
        /// its neighbours' along their whole length rather than in a wedge.
        /// </summary>
        private static void DrawSlice(DrawingContext ctx, Point c, double innerR, double outerR,
            double startDeg, double sweepDeg, double gap, IBrush brush)
        {
            if (sweepDeg >= 359.9)
            {
                if (innerR > 0)
                    ctx.DrawEllipse(null, new Pen(brush, outerR - innerR), c, (outerR + innerR) / 2, (outerR + innerR) / 2);
                else
                    ctx.DrawEllipse(brush, null, c, outerR, outerR);
                return;
            }

            var q = gap / 2;
            var startRad = (startDeg - 90) * Math.PI / 180;
            var endRad = (startDeg + sweepDeg - 90) * Math.PI / 180;
            var dStart = new Vector(Math.Cos(startRad), Math.Sin(startRad));
            var dEnd = new Vector(Math.Cos(endRad), Math.Sin(endRad));
            // Each side moves inward, toward the part, by half the gap.
            var nStart = new Vector(-Math.Sin(startRad), Math.Cos(startRad));
            var nEnd = new Vector(Math.Sin(endRad), -Math.Cos(endRad));
            // Past half the circle a part's moved sides no longer meet in front of the centre: it keeps its
            // round shape, cut only by the gaps along its sides, however thin its neighbours are.
            var reflex = sweepDeg > 180;
            var apexDistance = q > 0 && !reflex ? q / Math.Sin(sweepDeg / 2 * Math.PI / 180) : 0;
            if (apexDistance >= outerR * 0.9)
                return; // too thin to show between its gaps

            Point OnCircle(Vector along, Vector inward, double r) =>
                c + along * Math.Sqrt(Math.Max(0, r * r - q * q)) + inward * q;
            double ArcSweep(double r) => sweepDeg - (q > 0 && r > q ? 2 * Math.Asin(q / r) * 180 / Math.PI : 0);

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(OnCircle(dStart, nStart, outerR), true);
                gc.ArcTo(OnCircle(dEnd, nEnd, outerR), new Size(outerR, outerR), 0, ArcSweep(outerR) > 180,
                    SweepDirection.Clockwise);
                if (innerR > q && (reflex || apexDistance < innerR))
                {
                    gc.LineTo(OnCircle(dEnd, nEnd, innerR));
                    gc.ArcTo(OnCircle(dStart, nStart, innerR), new Size(innerR, innerR), 0, ArcSweep(innerR) > 180,
                        SweepDirection.CounterClockwise);
                }
                else if (reflex)
                {
                    // Round the centre, where the gaps on either side of it meet.
                    gc.LineTo(c + nEnd * q);
                    gc.LineTo(c + nStart * q);
                }
                else
                {
                    gc.LineTo(c + Direction(startDeg + sweepDeg / 2) * apexDistance);
                }
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        private static void DrawTopRoundedRect(DrawingContext ctx, Rect rect, double radius, IBrush brush)
        {
            if (rect.Height < 1) return;
            radius = Math.Min(radius, Math.Min(rect.Width / 2, rect.Height));

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(rect.Left, rect.Bottom), true);
                gc.LineTo(new Point(rect.Left, rect.Top + radius));
                gc.ArcTo(new Point(rect.Left + radius, rect.Top),
                    new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                gc.LineTo(new Point(rect.Right - radius, rect.Top));
                gc.ArcTo(new Point(rect.Right, rect.Top + radius),
                    new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                gc.LineTo(new Point(rect.Right, rect.Bottom));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        // ── Catmull-Rom spline ─────────────────────────────────

        private static (Point cp1, Point cp2) CatmullRom(Point[] pts, int i)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Length ? pts[i + 2] : pts[i + 1];

            const double tension = 6.0;
            return (
                new Point(p1.X + (p2.X - p0.X) / tension, p1.Y + (p2.Y - p0.Y) / tension),
                new Point(p2.X - (p3.X - p1.X) / tension, p2.Y - (p3.Y - p1.Y) / tension)
            );
        }

        // ── Series colors ──────────────────────────────────────

        private Color SeriesColor(int index)
        {
            if (_chart.SeriesBrush(index) is ISolidColorBrush solid)
                return solid.Color;
            return PaletteFallbacks[index % PaletteFallbacks.Length];
        }

        // ── Value formatting ───────────────────────────────────

        private static double NiceMax(double dataMax)
        {
            if (dataMax <= 0) return 10;
            var niceStep = NiceStep(dataMax / GridLines);
            return Math.Ceiling(dataMax / niceStep) * niceStep;
        }

        // The round step (1, 2 or 5 of a power of ten) at or just above the one given.
        private static double NiceStep(double rawStep)
        {
            var mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            var norm = rawStep / mag;

            double niceStep;
            if (norm <= 1) niceStep = 1;
            else if (norm <= 2) niceStep = 2;
            else if (norm <= 5) niceStep = 5;
            else niceStep = 10;

            return niceStep * mag;
        }

        private static string FormatValue(double value)
        {
            var abs = Math.Abs(value);
            if (abs >= 1_000_000) return Compact(value / 1_000_000) + "M";
            if (abs >= 1000) return Compact(value / 1000) + "K";
            return Compact(value);

            static string Compact(double number) =>
                number == Math.Floor(number) ? $"{number:F0}" : $"{number:F1}";
        }

        // Trims a line that is wider than it may be to an ellipsis.
        private static FormattedText Fit(FormattedText text, double maxWidth)
        {
            if (maxWidth > 0 && text.Width > maxWidth)
            {
                text.MaxTextWidth = maxWidth;
                text.MaxLineCount = 1;
                text.Trimming = TextTrimming.CharacterEllipsis;
            }
            return text;
        }

        private static FormattedText Txt(string text, double size, IBrush brush,
            FontWeight weight = FontWeight.Normal)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, MermaidTextHelper.GetFlowDirection(text),
                new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
        }
    }
}
