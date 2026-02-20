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
/// <para><b>Template parts:</b> PART_ChartHost (Panel).</para>
/// </remarks>
public class StrataChart : TemplatedControl
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

    /// <summary>Gets or sets the chart drawing area height in pixels.</summary>
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
            _chartHost.Children.Add(_canvas);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
            _canvas?.InvalidateVisual(); // repaint only, no re-animation
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_canvas is null) return;

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
            case Key.Escape:
                _canvas.SetHoverIndex(-1);
                e.Handled = true;
                break;
        }
    }

    internal void InvalidateChart()
    {
        if (_canvas is not null)
        {
            // Only re-animate if already displayed once; initial animation
            // is handled by OnAttachedToVisualTree deferred callback.
            if (_canvas.HoverIndex < 0) // not during hover interaction
                _canvas.ResetAnimation();
            _canvas.InvalidateVisual();
        }
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

    // ═══════════════════════════════════════════════════════════
    //  CHART CANVAS — all custom rendering
    // ═══════════════════════════════════════════════════════════
    private sealed class ChartCanvas : Control
    {
        private readonly StrataChart _chart;
        private int _hoverIndex = -1;

        private const double LeftPad = 44;
        private const double RightPad = 16;
        private const double TopPad = 8;
        private const double BottomPad = 28;
        private const double LegendRowH = 24;
        private const int GridLines = 5;

        // ── Animation state ────────────────────────────────────
        private const double AnimDurationMs = 600;
        private readonly Stopwatch _animWatch = new();
        private DispatcherTimer? _animTimer;
        private double _animProgress; // 0 = hidden, 1 = fully visible
        private bool _hasAnimated;
        private bool _entranceReady; // true once deferred post fires after attach

        internal int HoverIndex => _hoverIndex;

        internal void SetHoverIndex(int idx)
        {
            if (_hoverIndex != idx) { _hoverIndex = idx; InvalidateVisual(); }
        }

        internal void ResetAnimation()
        {
            _animProgress = 0;
            _hasAnimated = false;
            _animWatch.Reset();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // Delay animation until the window is actually visible on screen.
            // DispatcherPriority.Loaded alone isn't enough — the window compositor
            // hasn't painted yet. A short timer gives the window time to appear.
            _animProgress = 0;
            _hasAnimated = false;
            _entranceReady = false;
            _animWatch.Reset();

            var delayTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(150),
            };
            delayTimer.Tick += (_, _) =>
            {
                delayTimer.Stop();
                _entranceReady = true;
                InvalidateVisual();
            };
            delayTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _animTimer?.Stop();
            _animWatch.Reset();
            _entranceReady = false;
        }

        private void EnsureAnimation()
        {
            if (_hasAnimated) return;
            if (!_entranceReady) return;

            if (!_animWatch.IsRunning)
            {
                _animWatch.Restart();
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
        }

        // ── Pointer events ─────────────────────────────────────

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var old = _hoverIndex;
            _hoverIndex = HitTest(e.GetPosition(this));
            if (old != _hoverIndex) InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_hoverIndex >= 0) { _hoverIndex = -1; InvalidateVisual(); }
        }

        // ── Hit testing ────────────────────────────────────────

        private int HitTest(Point pos)
        {
            var series = _chart.Series;
            var labels = _chart.Labels;
            if (series is null || labels is null || labels.Count == 0) return -1;

            if (_chart.ChartType is StrataChartType.Donut or StrataChartType.Pie)
                return HitTestPieOrDonut(pos);

            var rect = ChartRect();
            if (pos.Y < rect.Top - 10 || pos.Y > rect.Bottom + 10) return -1;

            var count = labels.Count;
            var step = count > 1 ? rect.Width / (count - 1) : rect.Width;
            for (int i = 0; i < count; i++)
            {
                var x = count > 1
                    ? rect.Left + (double)i / (count - 1) * rect.Width
                    : rect.Left + rect.Width / 2;
                if (Math.Abs(pos.X - x) < step * 0.45)
                    return i;
            }
            return -1;
        }

        private int HitTestPieOrDonut(Point pos)
        {
            var series = _chart.Series;
            if (series is null || series.Count == 0) return -1;
            var vals = series[0].Values;
            if (vals is null || vals.Count == 0) return -1;

            var isPie = _chart.ChartType == StrataChartType.Pie;
            var (center, outerR, innerR) = isPie ? PieMetrics() : DonutMetrics();
            var dx = pos.X - center.X;
            var dy = pos.Y - center.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var minR = isPie ? 0 : innerR - 4;
            if (dist < minR || dist > outerR + 4) return -1;

            var angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
            if (angle < 0) angle += 360;

            var total = vals.Sum();
            if (total <= 0) return -1;

            // Pie uses no angular gaps; donut uses 3°
            var gapDeg = isPie ? 0.0 : 3.0;
            var usable = 360.0 - gapDeg * vals.Count;
            double cum = 0;
            for (int i = 0; i < vals.Count; i++)
            {
                var sweep = vals[i] / total * usable;
                if (angle >= cum && angle < cum + sweep) return i;
                cum += sweep + gapDeg;
            }
            return -1;
        }

        // ── Layout helpers ─────────────────────────────────────

        private Rect ChartRect()
        {
            var b = Bounds;
            var top = _chart.ShowLegend && _chart.ChartType is not StrataChartType.Donut and not StrataChartType.Pie
                ? LegendRowH + TopPad
                : TopPad;
            return new Rect(LeftPad, top,
                Math.Max(0, b.Width - LeftPad - RightPad),
                Math.Max(0, b.Height - top - BottomPad));
        }

        private (Point center, double outerR, double innerR) DonutMetrics()
        {
            var b = Bounds;
            var top = _chart.ShowLegend ? LegendRowH + TopPad : TopPad;
            var available = b.Height - top - 8;
            var cx = b.Width * 0.38;
            var cy = top + available / 2;
            var outerR = Math.Max(20, Math.Min(cx - 24, available / 2 - 8));
            var innerR = outerR * 0.6;
            return (new Point(cx, cy), outerR, innerR);
        }

        private (Point center, double outerR, double innerR) PieMetrics()
        {
            var b = Bounds;
            var top = _chart.ShowLegend ? LegendRowH + TopPad : TopPad;
            var available = b.Height - top - 8;
            var cx = b.Width * 0.38;
            var cy = top + available / 2;
            var radius = Math.Max(20, Math.Min(cx - 24, available / 2 - 8));
            return (new Point(cx, cy), radius, 0);
        }

        // ── Render dispatch ────────────────────────────────────

        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            if (b.Width < 40 || b.Height < 40) return;

            var series = _chart.Series;
            if (series is null || series.Count == 0) return;

            EnsureAnimation();

            var gridBrush = _chart.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));
            var labelBrush = _chart.ResolveBrush("Brush.TextTertiary", Color.Parse("#8A8A8A"));
            var textBrush = _chart.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var surfaceBrush = _chart.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));

            if (_chart.ShowLegend)
                DrawLegend(ctx, b, series);

            switch (_chart.ChartType)
            {
                case StrataChartType.Line:
                    DrawLineChart(ctx, series, gridBrush, labelBrush, textBrush, surfaceBrush);
                    break;
                case StrataChartType.Bar:
                    DrawBarChart(ctx, series, gridBrush, labelBrush, textBrush, surfaceBrush);
                    break;
                case StrataChartType.Donut:
                    DrawDonutChart(ctx, b, series, labelBrush, textBrush, surfaceBrush);
                    break;
                case StrataChartType.Pie:
                    DrawPieChart(ctx, b, series, labelBrush, textBrush, surfaceBrush);
                    break;
            }
        }

        // ── Legend ─────────────────────────────────────────────

        private void DrawLegend(DrawingContext ctx, Rect bounds, IList<StrataChartSeries> series)
        {
            var labelBrush = _chart.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));

            if (_chart.ChartType is StrataChartType.Donut or StrataChartType.Pie)
            {
                // Vertical legend on the right side
                var labels = _chart.Labels;
                var vals = series.Count > 0 ? series[0].Values : null;
                var total = vals?.Sum() ?? 0;

                var center = _chart.ChartType == StrataChartType.Pie
                    ? PieMetrics().center
                    : DonutMetrics().center;
                var startX = bounds.Width * 0.65;
                var itemCount = labels?.Count ?? 0;
                var startY = center.Y - itemCount * 22.0 / 2;

                for (int i = 0; i < itemCount; i++)
                {
                    var brush = SeriesBrush(i);
                    ctx.DrawEllipse(brush, null, new Point(startX + 5, startY + 7), 5, 5);
                    var label = labels![i];
                    if (vals is not null && i < vals.Count && total > 0)
                        label += $"  {vals[i] / total * 100:F0}%";
                    ctx.DrawText(Txt(label, 11, labelBrush), new Point(startX + 16, startY));
                    startY += 22;
                }
            }
            else
            {
                // Horizontal legend above chart area
                double x = LeftPad;
                for (int i = 0; i < series.Count; i++)
                {
                    var brush = SeriesBrush(i);
                    ctx.DrawEllipse(brush, null, new Point(x + 5, TopPad + 7), 5, 5);
                    var ft = Txt(series[i].Name, 11, labelBrush);
                    ctx.DrawText(ft, new Point(x + 14, TopPad));
                    x += 14 + ft.Width + 20;
                }
            }
        }

        // ── Grid + Axes ────────────────────────────────────────

        private void DrawGridAndAxes(DrawingContext ctx, Rect rect, double minVal, double maxVal,
            IBrush gridBrush, IBrush labelBrush)
        {
            var pen = new Pen(gridBrush, 1);
            var range = maxVal - minVal;

            for (int i = 0; i <= GridLines; i++)
            {
                var frac = (double)i / GridLines;
                var y = rect.Bottom - frac * rect.Height;
                ctx.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));

                var val = minVal + frac * range;
                var ft = Txt(FormatValue(val), 10, labelBrush);
                ctx.DrawText(ft, new Point(rect.Left - ft.Width - 6, y - ft.Height / 2));
            }
        }

        private void DrawXLabels(DrawingContext ctx, Rect rect, IList<string> labels, IBrush labelBrush)
        {
            var count = labels.Count;
            for (int i = 0; i < count; i++)
            {
                var x = count > 1
                    ? rect.Left + (double)i / (count - 1) * rect.Width
                    : rect.Left + rect.Width / 2;
                var ft = Txt(labels[i], 10, labelBrush);
                ctx.DrawText(ft, new Point(x - ft.Width / 2, rect.Bottom + 6));
            }
        }

        // ── Line Chart ────────────────────────────────────────

        private void DrawLineChart(DrawingContext ctx, IList<StrataChartSeries> series,
            IBrush gridBrush, IBrush labelBrush, IBrush textBrush, IBrush surfaceBrush)
        {
            var labels = _chart.Labels;
            if (labels is null || labels.Count < 2) return;

            var rect = ChartRect();
            if (rect.Width < 10 || rect.Height < 10) return;

            double maxVal = series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max();
            maxVal = NiceMax(maxVal);
            var range = maxVal > 0 ? maxVal : 1;

            if (_chart.ShowGrid)
                DrawGridAndAxes(ctx, rect, 0, maxVal, gridBrush, labelBrush);
            DrawXLabels(ctx, rect, labels, labelBrush);

            // Hover guide line — accent-tinted
            if (_hoverIndex >= 0 && _hoverIndex < labels.Count)
            {
                var gx = rect.Left + (double)_hoverIndex / (labels.Count - 1) * rect.Width;
                var guideBrush = _chart.ResolveBrush("Brush.AccentGlowSoft", Color.Parse("#28818CF8"));
                ctx.DrawLine(new Pen(guideBrush, 1), new Point(gx, rect.Top), new Point(gx, rect.Bottom));
            }

            var dotPen = new Pen(surfaceBrush, 2);

            for (int si = 0; si < series.Count; si++)
            {
                var s = series[si];
                if (s.Values.Count < 2) continue;

                var count = Math.Min(s.Values.Count, labels.Count);
                var brush = SeriesBrush(si);
                var color = SeriesColor(si);

                // Map data to screen points (animated: grow from baseline)
                var pts = new Point[count];
                for (int i = 0; i < count; i++)
                {
                    var rawY = rect.Bottom - s.Values[i] / range * rect.Height;
                    var animY = rect.Bottom + (rawY - rect.Bottom) * _animProgress;
                    pts[i] = new Point(
                        rect.Left + (double)i / (count - 1) * rect.Width,
                        animY);
                }

                // Area fill — Strata spectrum gradient
                var areaGeo = new StreamGeometry();
                using (var gc = areaGeo.Open())
                {
                    gc.BeginFigure(new Point(pts[0].X, rect.Bottom), true);
                    gc.LineTo(pts[0]);
                    for (int i = 1; i < count; i++)
                    {
                        var (cp1, cp2) = CatmullRom(pts, i - 1);
                        gc.CubicBezierTo(cp1, cp2, pts[i]);
                    }
                    gc.LineTo(new Point(pts[^1].X, rect.Bottom));
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
                ctx.DrawGeometry(null, new Pen(brush, 2.5), lineGeo);

                // Data point dots
                for (int i = 0; i < count; i++)
                {
                    var r = i == _hoverIndex ? 5.0 : 3.0;
                    ctx.DrawEllipse(brush, dotPen, pts[i], r, r);
                }
            }

            // Tooltip
            if (_hoverIndex >= 0 && _hoverIndex < labels.Count)
            {
                double highY = rect.Bottom;
                for (int si = 0; si < series.Count; si++)
                    if (_hoverIndex < series[si].Values.Count)
                    {
                        var y = rect.Bottom - series[si].Values[_hoverIndex] / range * rect.Height;
                        if (y < highY) highY = y;
                    }

                var tx = rect.Left + (double)_hoverIndex / (labels.Count - 1) * rect.Width;
                DrawTooltip(ctx, new Point(tx, highY), series, _hoverIndex, labels, textBrush, surfaceBrush, gridBrush);
            }
        }

        // ── Bar Chart ──────────────────────────────────────────

        private void DrawBarChart(DrawingContext ctx, IList<StrataChartSeries> series,
            IBrush gridBrush, IBrush labelBrush, IBrush textBrush, IBrush surfaceBrush)
        {
            var labels = _chart.Labels;
            if (labels is null || labels.Count == 0) return;

            var rect = ChartRect();
            if (rect.Width < 10 || rect.Height < 10) return;

            double maxVal = series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max();
            maxVal = NiceMax(maxVal);
            if (maxVal <= 0) maxVal = 1;

            if (_chart.ShowGrid)
                DrawGridAndAxes(ctx, rect, 0, maxVal, gridBrush, labelBrush);

            var labelCount = labels.Count;
            var seriesCount = series.Count;
            var groupWidth = rect.Width / labelCount;
            var groupPad = groupWidth * 0.2;
            var barArea = groupWidth - groupPad;
            var barGap = seriesCount > 1 ? 2.0 : 0;
            var totalGaps = Math.Max(0, seriesCount - 1) * barGap;
            var barWidth = Math.Max(2, (barArea - totalGaps) / seriesCount);
            var cornerR = Math.Min(4, barWidth / 2);

            for (int i = 0; i < labelCount; i++)
            {
                var groupLeft = rect.Left + i * groupWidth + groupPad / 2;

                // X label centered under group
                var ft = Txt(labels[i], 10, labelBrush);
                ctx.DrawText(ft, new Point(groupLeft + barArea / 2 - ft.Width / 2, rect.Bottom + 6));

                for (int si = 0; si < seriesCount; si++)
                {
                    if (i >= series[si].Values.Count) continue;

                    var val = series[si].Values[i];
                    var barH = val / maxVal * rect.Height * _animProgress;
                    if (barH < 1) continue;

                    var bx = groupLeft + si * (barWidth + barGap);
                    var by = rect.Bottom - barH;
                    var barRect = new Rect(bx, by, barWidth, barH);
                    var brush = SeriesBrush(si);

                    DrawTopRoundedRect(ctx, barRect, cornerR, brush);

                    // Hover overlay — accent glow
                    if (i == _hoverIndex)
                    {
                        var glowBrush = _chart.ResolveBrush("Brush.AccentGlowSoft", Color.Parse("#28818CF8"));
                        DrawTopRoundedRect(ctx, barRect, cornerR, glowBrush);
                    }
                }
            }

            // Tooltip
            if (_hoverIndex >= 0 && _hoverIndex < labelCount)
            {
                double highY = rect.Bottom;
                for (int si = 0; si < seriesCount; si++)
                    if (_hoverIndex < series[si].Values.Count)
                    {
                        var y = rect.Bottom - series[si].Values[_hoverIndex] / maxVal * rect.Height;
                        if (y < highY) highY = y;
                    }

                var gCenter = rect.Left + _hoverIndex * groupWidth + groupWidth / 2;
                DrawTooltip(ctx, new Point(gCenter, highY), series, _hoverIndex, labels, textBrush, surfaceBrush, gridBrush);
            }
        }

        // ── Donut Chart ────────────────────────────────────────

        private void DrawDonutChart(DrawingContext ctx, Rect bounds, IList<StrataChartSeries> series,
            IBrush labelBrush, IBrush textBrush, IBrush surfaceBrush)
        {
            if (series.Count == 0) return;
            var vals = series[0].Values;
            if (vals is null || vals.Count == 0) return;

            var (center, outerR, innerR) = DonutMetrics();
            var total = vals.Sum();
            if (total <= 0) return;

            var gapDeg = 3.0;
            var usable = 360.0 - gapDeg * vals.Count;
            var animSweepTotal = 360.0 * _animProgress;

            double angle = 0;
            double drawnSoFar = 0;
            for (int i = 0; i < vals.Count; i++)
            {
                var sweep = vals[i] / total * usable;
                var brush = SeriesBrush(i);

                // Clamp sweep to remaining animation budget
                var availableSweep = Math.Max(0, Math.Min(sweep, animSweepTotal - drawnSoFar));
                if (availableSweep < 0.3) { angle += sweep + gapDeg; drawnSoFar += sweep + gapDeg; continue; }

                if (i == _hoverIndex)
                {
                    var midRad = (angle + availableSweep / 2 - 90) * Math.PI / 180;
                    var explode = new Point(center.X + 4 * Math.Cos(midRad), center.Y + 4 * Math.Sin(midRad));
                    DrawArcSegment(ctx, explode, innerR, outerR + 2, angle, availableSweep, brush);
                }
                else
                {
                    DrawArcSegment(ctx, center, innerR, outerR, angle, availableSweep, brush);
                }
                angle += sweep + gapDeg;
                drawnSoFar += sweep + gapDeg;
            }

            // Center circle
            ctx.DrawEllipse(surfaceBrush, null, center, innerR - 2, innerR - 2);

            // Center text
            if (_chart.DonutCenterValue is not null)
            {
                var vt = Txt(_chart.DonutCenterValue, 20, textBrush, FontWeight.SemiBold);
                ctx.DrawText(vt, new Point(center.X - vt.Width / 2, center.Y - vt.Height / 2 - 6));
            }
            if (_chart.DonutCenterLabel is not null)
            {
                var lt = Txt(_chart.DonutCenterLabel, 11, labelBrush);
                ctx.DrawText(lt, new Point(center.X - lt.Width / 2, center.Y + 8));
            }

            // Hover tooltip for donut
            if (_hoverIndex >= 0 && _hoverIndex < vals.Count)
            {
                var labels = _chart.Labels;
                if (labels is not null && _hoverIndex < labels.Count)
                {
                    // Calculate segment midpoint for tooltip anchor
                    double hAngle = 0;
                    for (int i = 0; i < _hoverIndex; i++)
                        hAngle += vals[i] / total * usable + gapDeg;
                    var hSweep = vals[_hoverIndex] / total * usable;
                    var midRad = (hAngle + hSweep / 2 - 90) * Math.PI / 180;
                    var tipAnchor = new Point(
                        center.X + (outerR + 10) * Math.Cos(midRad),
                        center.Y + (outerR + 10) * Math.Sin(midRad));

                    var pct = vals[_hoverIndex] / total * 100;
                    var tipText = $"{labels[_hoverIndex]}: {FormatValue(vals[_hoverIndex])} ({pct:F0}%)";

                    var gridBrush = _chart.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));
                    DrawSimpleTooltip(ctx, tipAnchor, tipText, textBrush, surfaceBrush, gridBrush);
                }
            }
        }

        // ── Pie Chart ──────────────────────────────────────────

        private void DrawPieChart(DrawingContext ctx, Rect bounds, IList<StrataChartSeries> series,
            IBrush labelBrush, IBrush textBrush, IBrush surfaceBrush)
        {
            if (series.Count == 0) return;
            var vals = series[0].Values;
            if (vals is null || vals.Count == 0) return;

            var (center, radius, _) = PieMetrics();
            var total = vals.Sum();
            if (total <= 0) return;

            // No angular gap — full 360°, then overlay separator lines
            var animSweepTotal = 360.0 * _animProgress;

            double angle = 0;
            double drawnSoFar = 0;
            for (int i = 0; i < vals.Count; i++)
            {
                var sweep = vals[i] / total * 360.0;
                var brush = SeriesBrush(i);

                var availableSweep = Math.Max(0, Math.Min(sweep, animSweepTotal - drawnSoFar));
                if (availableSweep < 0.3) { angle += sweep; drawnSoFar += sweep; continue; }

                if (i == _hoverIndex)
                {
                    var midRad = (angle + availableSweep / 2 - 90) * Math.PI / 180;
                    var explode = new Point(center.X + 5 * Math.Cos(midRad), center.Y + 5 * Math.Sin(midRad));
                    DrawPieSlice(ctx, explode, radius + 2, angle, availableSweep, brush);
                }
                else
                {
                    DrawPieSlice(ctx, center, radius, angle, availableSweep, brush);
                }
                angle += sweep;
                drawnSoFar += sweep;
            }

            // Draw thin separator lines over the slice boundaries
            var bgBrush = _chart.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var sepPen = new Pen(bgBrush, 2);
            angle = 0;
            for (int i = 0; i < vals.Count; i++)
            {
                var sweep = vals[i] / total * 360.0;
                // Separator at the START of each slice (draws over seam)
                var rad = (angle - 90) * Math.PI / 180;
                var edgePt = new Point(center.X + (radius + 1) * Math.Cos(rad),
                                       center.Y + (radius + 1) * Math.Sin(rad));
                ctx.DrawLine(sepPen, center, edgePt);
                angle += sweep;
            }

            // Hover tooltip
            if (_hoverIndex >= 0 && _hoverIndex < vals.Count)
            {
                var labels = _chart.Labels;
                if (labels is not null && _hoverIndex < labels.Count)
                {
                    double hAngle = 0;
                    for (int i = 0; i < _hoverIndex; i++)
                        hAngle += vals[i] / total * 360.0;
                    var hSweep = vals[_hoverIndex] / total * 360.0;
                    var midRad = (hAngle + hSweep / 2 - 90) * Math.PI / 180;
                    var tipAnchor = new Point(
                        center.X + (radius + 12) * Math.Cos(midRad),
                        center.Y + (radius + 12) * Math.Sin(midRad));

                    var pct = vals[_hoverIndex] / total * 100;
                    var tipText = $"{labels[_hoverIndex]}: {FormatValue(vals[_hoverIndex])} ({pct:F0}%)";

                    var gridBrush = _chart.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));
                    DrawSimpleTooltip(ctx, tipAnchor, tipText, textBrush, surfaceBrush, gridBrush);
                }
            }
        }

        // ── Geometry helpers ───────────────────────────────────

        private static void DrawArcSegment(DrawingContext ctx, Point center, double innerR, double outerR,
            double startDeg, double sweepDeg, IBrush brush)
        {
            if (sweepDeg < 0.5) return;

            var startRad = (startDeg - 90) * Math.PI / 180;
            var endRad = (startDeg + sweepDeg - 90) * Math.PI / 180;

            var outerStart = new Point(center.X + outerR * Math.Cos(startRad), center.Y + outerR * Math.Sin(startRad));
            var outerEnd = new Point(center.X + outerR * Math.Cos(endRad), center.Y + outerR * Math.Sin(endRad));
            var innerStart = new Point(center.X + innerR * Math.Cos(startRad), center.Y + innerR * Math.Sin(startRad));
            var innerEnd = new Point(center.X + innerR * Math.Cos(endRad), center.Y + innerR * Math.Sin(endRad));

            var isLarge = sweepDeg > 180;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(outerStart, true);
                gc.ArcTo(outerEnd, new Size(outerR, outerR), 0, isLarge, SweepDirection.Clockwise);
                gc.LineTo(innerEnd);
                gc.ArcTo(innerStart, new Size(innerR, innerR), 0, isLarge, SweepDirection.CounterClockwise);
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        private static void DrawPieSlice(DrawingContext ctx, Point center, double radius,
            double startDeg, double sweepDeg, IBrush brush)
        {
            if (sweepDeg < 0.5) return;

            var startRad = (startDeg - 90) * Math.PI / 180;
            var endRad = (startDeg + sweepDeg - 90) * Math.PI / 180;

            var arcStart = new Point(center.X + radius * Math.Cos(startRad), center.Y + radius * Math.Sin(startRad));
            var arcEnd = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));

            var isLarge = sweepDeg > 180;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(center, true);
                gc.LineTo(arcStart);
                gc.ArcTo(arcEnd, new Size(radius, radius), 0, isLarge, SweepDirection.Clockwise);
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

        // ── Tooltip ────────────────────────────────────────────

        private void DrawTooltip(DrawingContext ctx, Point anchor, IList<StrataChartSeries> series, int idx,
            IList<string> labels, IBrush textBrush, IBrush surfaceBrush, IBrush borderBrush)
        {
            var secBrush = _chart.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var overlayBrush = _chart.ResolveBrush("Brush.SurfaceOverlay", Color.Parse("#2A2A2A"));
            var tipBorderBrush = _chart.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var lines = new List<(FormattedText ft, IBrush dot)>();

            if (idx < labels.Count)
                lines.Add((Txt(labels[idx], 11, secBrush, FontWeight.SemiBold), Brushes.Transparent));

            for (int si = 0; si < series.Count; si++)
                if (idx < series[si].Values.Count)
                    lines.Add((Txt($"{series[si].Name}: {FormatValue(series[si].Values[idx])}", 11, textBrush),
                        SeriesBrush(si)));

            if (lines.Count == 0) return;

            var maxW = lines.Max(l => l.ft.Width);
            var totalH = lines.Sum(l => l.ft.Height + 2);
            const double px = 10, py = 8, topStripe = 2.5;
            var tipW = maxW + px * 2 + 16;
            var tipH = totalH + py * 2 + topStripe;

            var tx = Math.Max(2, Math.Min(anchor.X - tipW / 2, Bounds.Width - tipW - 2));
            var ty = anchor.Y - tipH - 10;
            if (ty < 2) ty = anchor.Y + 12;

            var tipRect = new Rect(tx, ty, tipW, tipH);
            ctx.DrawRectangle(overlayBrush, new Pen(tipBorderBrush, 1), tipRect, 6, 6);

            // Accent top stripe (Strata signature)
            var stripeRect = new Rect(tx + 1, ty + 1, tipW - 2, topStripe);
            using (ctx.PushClip(new RoundedRect(tipRect, 6)))
            {
                var chart1 = _chart.ResolveBrush(PaletteKeys[0], PaletteFallbacks[0]);
                ctx.DrawRectangle(chart1, null, stripeRect);
            }

            double textY = ty + py + topStripe;
            foreach (var (ft, dot) in lines)
            {
                if (dot != Brushes.Transparent)
                    ctx.DrawEllipse(dot, null, new Point(tx + px + 4, textY + ft.Height / 2), 3.5, 3.5);
                ctx.DrawText(ft, new Point(tx + px + (dot != Brushes.Transparent ? 12 : 0), textY));
                textY += ft.Height + 2;
            }
        }

        private void DrawSimpleTooltip(DrawingContext ctx, Point anchor, string text,
            IBrush textBrush, IBrush surfaceBrush, IBrush borderBrush)
        {
            var overlayBrush = _chart.ResolveBrush("Brush.SurfaceOverlay", Color.Parse("#2A2A2A"));
            var tipBorderBrush = _chart.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var ft = Txt(text, 11, textBrush);
            const double px = 8, py = 5, topStripe = 2;
            var tipW = ft.Width + px * 2;
            var tipH = ft.Height + py * 2 + topStripe;

            var tx = Math.Max(2, Math.Min(anchor.X - tipW / 2, Bounds.Width - tipW - 2));
            var ty = Math.Max(2, Math.Min(anchor.Y - tipH / 2, Bounds.Height - tipH - 2));

            var tipRect = new Rect(tx, ty, tipW, tipH);
            ctx.DrawRectangle(overlayBrush, new Pen(tipBorderBrush, 1), tipRect, 6, 6);

            using (ctx.PushClip(new RoundedRect(tipRect, 6)))
            {
                var chart1 = _chart.ResolveBrush(PaletteKeys[0], PaletteFallbacks[0]);
                ctx.DrawRectangle(chart1, null, new Rect(tx + 1, ty + 1, tipW - 2, topStripe));
            }

            ctx.DrawText(ft, new Point(tx + px, ty + py + topStripe));
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

        private IBrush SeriesBrush(int index)
        {
            var idx = index % PaletteKeys.Length;
            return _chart.ResolveBrush(PaletteKeys[idx], PaletteFallbacks[idx]);
        }

        private Color SeriesColor(int index)
        {
            if (SeriesBrush(index) is ISolidColorBrush solid)
                return solid.Color;
            return PaletteFallbacks[index % PaletteFallbacks.Length];
        }

        // ── Value formatting ───────────────────────────────────

        private static double NiceMax(double dataMax)
        {
            if (dataMax <= 0) return 10;
            var rawStep = dataMax / GridLines;
            var mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            var norm = rawStep / mag;

            double niceStep;
            if (norm <= 1) niceStep = 1;
            else if (norm <= 2) niceStep = 2;
            else if (norm <= 5) niceStep = 5;
            else niceStep = 10;

            niceStep *= mag;
            return Math.Ceiling(dataMax / niceStep) * niceStep;
        }

        private static string FormatValue(double value)
        {
            var abs = Math.Abs(value);
            if (abs >= 1000) return $"{value / 1000:F1}K";
            if (value == Math.Floor(value)) return $"{value:F0}";
            return $"{value:F1}";
        }

        private static FormattedText Txt(string text, double size, IBrush brush,
            FontWeight weight = FontWeight.Normal)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
        }
    }
}
