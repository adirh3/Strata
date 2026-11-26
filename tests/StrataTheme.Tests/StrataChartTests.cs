using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataChartTests(AvaloniaFixture fixture)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string RevenueChart =
        """{"type":"bar","labels":["Q1","Q2","Q3"],"series":[{"name":"Revenue","values":[1200,1850.5,900]},{"name":"Costs","values":[800,950,700]}]}""";

    [Fact]
    public Task MarkdownChart_ShowsItsDataAsATableInThePlotsPlace() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(RevenueChart);
        try
        {
            Assert.True(chart.ShowToolbar);
            Assert.True(Find<Border>(chart, "PART_Toolbar").IsVisible);
            var height = chart.Bounds.Height;

            Click(chart, "PART_TableView");
            Pump(window);
            Assert.False(Find<Panel>(chart, "PART_ChartHost").IsVisible);
            var table = Find<Panel>(chart, "PART_TableHost");
            Assert.True(table.IsVisible);
            var cells = table.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Equal(["Category", "Revenue", "Costs"], cells.Take(3));
            Assert.Contains("1,850.5", cells);
            Assert.Contains("Q3", cells);
            // The table takes the plot's height, so nothing below it moves.
            Assert.Equal(height, chart.Bounds.Height, precision: 1);

            Click(chart, "PART_ChartView");
            Pump(window);
            Assert.True(Find<Panel>(chart, "PART_ChartHost").IsVisible);
            Assert.False(table.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ChartInsideALargerView_HasNoToolbar() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var chart = new StrataChart
        {
            ChartType = StrataChartType.Line,
            Labels = ["1", "2"],
            Series = [new StrataChartSeries { Name = "Balance", Values = [1, 2] }],
            ChartHeight = 150,
        };
        var window = Show(chart);
        try
        {
            Assert.False(Find<Border>(chart, "PART_Toolbar").IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HoverValues_ShowOnGlassBesideTheHoveredBars_AndGoWithThePointer() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(RevenueChart);
        try
        {
            var canvas = Canvas(chart);
            var tip = Find<Panel>(chart, "PART_Tip");
            Assert.False(tip.IsVisible);

            canvas.GetType().GetMethod("SetHoverIndex", PrivateInstance)!.Invoke(canvas, [1]);
            Pump(window);
            Assert.True(tip.IsVisible);
            Assert.Single(tip.GetVisualDescendants().OfType<StrataBackdropBlur>());
            var texts = tip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Equal(["Q2", "Revenue", "1,850.5", "Costs", "950"], texts);

            // The pane sits beside the hovered category's bars, never over them.
            var (groupWidth, _, _, clusterWidth) = BarMetrics(canvas, 2);
            var clusterLeft = 44 + 1.5 * groupWidth - clusterWidth / 2;
            var clusterRight = clusterLeft + clusterWidth;
            var tipLeft = TipOffset(tip).X;
            Assert.True(tipLeft >= clusterRight || tipLeft + tip.Bounds.Width <= clusterLeft,
                $"The pane ({tipLeft:0.#}..{tipLeft + tip.Bounds.Width:0.#}) covers the bars ({clusterLeft:0.#}..{clusterRight:0.#}).");

            canvas.GetType().GetMethod("SetHoverIndex", PrivateInstance)!.Invoke(canvas, [-1]);
            Pump(window);
            Assert.False(tip.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Plot_TakesThePointerBetweenItsBars() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(RevenueChart);
        try
        {
            var canvas = Canvas(chart);
            using (window.CaptureRenderedFrame()) { }
            // Where the first quarter's bars end and the second's begin nothing is drawn, yet a finger lands
            // there and a mouse crosses it.
            var (groupWidth, _, _, clusterWidth) = BarMetrics(canvas, 2);
            Assert.True(clusterWidth < groupWidth);
            var gap = canvas.TranslatePoint(new Point(44 + groupWidth, canvas.Bounds.Height * 0.47), window)!.Value;
            Assert.Same(canvas, window.InputHitTest(gap));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Toolbar_StaysQuietAtRest_AndRaisesItsGlassForAMouseOrKeyboardButNeverAFinger() => fixture.Dispatch(async () =>
    {
        using var culture = new EnglishCulture();
        var markdown = new StrataMarkdown
        {
            Markdown = $"```chart\n{RevenueChart}\n```",
            IsInline = true,
            StreamingRebuildThrottleMs = 0,
        };
        var elsewhere = new Button { Content = "Elsewhere" };
        var window = Show(new StackPanel { Children = { markdown, elsewhere } });
        try
        {
            var chart = Assert.Single(markdown.GetVisualDescendants().OfType<StrataChart>());
            var glass = chart.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("chart-glass")).ToList();
            var capsule = Find<Border>(Find<Button>(chart, "PART_ChartView"), "PART_Capsule");
            Assert.Equal(2, glass.Count);
            Assert.All(glass, pane => Assert.Equal(0, pane.Opacity));
            Assert.Equal(0, capsule.Opacity);
            // The designed compact button, not the theme's taller minimum.
            Assert.Equal(26, Find<Button>(chart, "PART_CopyData").Bounds.Height, precision: 1);

            // A mouse passing over, in and out before the pause, lights nothing. (Raised directly: the headless
            // input helpers run jobs and frames until all is still, which can outlast the pause.)
            var plot = chart.TranslatePoint(new Point(chart.Bounds.Width / 2, 80), window)!.Value;
            var outside = elsewhere.TranslatePoint(new Point(4, 4), window)!.Value;
            var mouse = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
            RaisePointer(chart, InputElement.PointerEnteredEvent, mouse, window, plot);
            Assert.False(Engaged(chart));
            RaisePointer(chart, InputElement.PointerExitedEvent, mouse, window, plot);
            Assert.False(await Settle(window, () => Engaged(chart), milliseconds: 400));

            // One resting a moment raises the glass, and leaving lowers it.
            window.MouseMove(plot, RawInputModifiers.None);
            Assert.True(await Settle(window, () => Engaged(chart) && glass.All(pane => pane.Opacity == 1) && capsule.Opacity == 1));
            window.MouseMove(outside, RawInputModifiers.None);
            Assert.False(Engaged(chart));
            Assert.True(await Settle(window, () => glass.All(pane => pane.Opacity == 0)));

            // A finger never raises it.
            var finger = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Touch, true);
            RaisePointer(chart, InputElement.PointerEnteredEvent, finger, window, plot);
            Assert.False(await Settle(window, () => Engaged(chart), milliseconds: 400));

            // Keyboard focus coming in raises it at once; it settles back once focus moves on.
            Find<Button>(chart, "PART_TableView").Focus(NavigationMethod.Tab);
            Pump(window);
            Assert.True(Engaged(chart));
            elsewhere.Focus(NavigationMethod.Tab);
            Pump(window);
            Assert.False(Engaged(chart));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Bars_StayNarrowAndCentredInWideBands() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(RevenueChart);
        try
        {
            var canvas = Canvas(chart);
            var single = BarMetrics(canvas, 1, width: 540, labels: 3);
            Assert.Equal(36, single.BarWidth);
            Assert.Equal(36, single.ClusterWidth);

            var pair = BarMetrics(canvas, 2, width: 540, labels: 3);
            Assert.Equal(36, pair.BarWidth);
            Assert.True(pair.ClusterWidth < pair.GroupWidth * 0.72);

            // Narrow bands still fit their bars inside them.
            var crowded = BarMetrics(canvas, 3, width: 300, labels: 12);
            Assert.True(crowded.ClusterWidth <= crowded.GroupWidth);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(StrataChartType.Donut)]
    [InlineData(StrataChartType.Pie)]
    public Task RadialLegend_SitsRightBesideTheRing(StrataChartType type) => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var chart = new StrataChart
        {
            ChartType = type,
            ChartHeight = 280,
            Labels = ["Auth", "Processing", "Alerts", "Reports"],
            Series = [new StrataChartSeries { Name = "Requests", Values = [30, 45, 15, 10] }],
        };
        var window = Show(chart);
        try
        {
            var canvas = Canvas(chart);
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(640, 320), new Vector(96, 96)))
                bitmap.Render(chart);
            var rows = LegendRects(canvas);
            Assert.Equal(4, rows.Count);
            var legendLeft = rows.Min(row => row.Left);
            var middle = (rows.Min(row => row.Top) + rows.Max(row => row.Bottom)) / 2;

            var hitTest = canvas.GetType().GetMethod("HitTest", PrivateInstance)!;
            var ringRight = 0.0;
            for (var x = 0.0; x < legendLeft; x += 1)
                if ((int)hitTest.Invoke(canvas, [new Point(x, middle)])! >= 0)
                    ringRight = x;

            Assert.True(ringRight > 0, "The ring was not found.");
            Assert.InRange(legendLeft - ringRight, 0, 45);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("[45,20,25,10]", new[] { "Category", "Share" }, "25%")]
    [InlineData("[4.5,2,2.5,1]", new[] { "Category", "Budget", "Share" }, "2.5 · 25%")]
    public Task RingParts_ReadAsTheirShare_AndTheirValueWhenItIsNotAlreadyOne(
        string values, string[] headers, string hovered) => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(
            $$"""{"type":"donut","labels":["Engineering","Design","Marketing","Support"],"series":[{"name":"Budget","values":{{values}}}]}""");
        try
        {
            Assert.Equal(headers, chart.TableData().Headers);
            var canvas = Canvas(chart);
            canvas.GetType().GetMethod("SetHoverIndex", PrivateInstance)!.Invoke(canvas, [2]);
            Pump(window);
            var texts = Find<Panel>(chart, "PART_Tip").GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text);
            Assert.Equal(["Marketing", hovered], texts);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CopyData_PutsTheChartsOwnNumbersOnTheClipboard() => fixture.Dispatch(async () =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(
            """{"type":"line","labels":["Mon","Tue","Wed"],"series":[{"name":"Requests","values":[1850.5,1200,900]},{"name":"Error rate","values":[0.0042,0.0125,0.001234567]}]}""");
        try
        {
            // The table reads a small rate rather than rounding it away...
            Click(chart, "PART_TableView");
            Pump(window);
            var cells = Find<Panel>(chart, "PART_TableHost").GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text).ToList();
            Assert.Contains("1,850.5", cells);
            Assert.Contains("0.0042", cells);
            Assert.Contains("0.00123", cells);

            // ...and Copy data hands over every number as the chart was given it.
            Click(chart, "PART_CopyData");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var text = (await window.Clipboard!.TryGetTextAsync())?.Replace("\r\n", "\n");
            Assert.NotNull(text);
            Assert.StartsWith("Category\tRequests\tError rate\n", text);
            Assert.Contains("Mon\t1850.5\t0.0042\n", text);
            Assert.Contains("Wed\t900\t0.001234567", text);
            Assert.Contains("copied", Find<Button>(chart, "PART_CopyData").Classes);

            // Parts given in percent keep their decimals too, though the table rounds their share.
            var ring = new StrataChart
            {
                ChartType = StrataChartType.Pie,
                Labels = ["Build", "Review"],
                Series = [new StrataChartSeries { Name = "Time", Values = [40.1, 59.9] }],
            };
            Assert.Equal("40%", ring.TableData().Rows[0][1]);
            Assert.Contains("| Build | 40.1% |", ring.DataMarkdown());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Table_ScrollsFromTheKeyboard_ToRowsAndColumnsPastItsEdges() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        string[] months = ["January", "February", "March", "April", "May", "June", "July", "August", "September",
            "October", "November", "December"];
        var chart = new StrataChart
        {
            ChartType = StrataChartType.Bar,
            ShowToolbar = true,
            ChartHeight = 220,
            Labels = months,
            Series = Enumerable.Range(1, 7).Select(region => new StrataChartSeries
            {
                Name = $"Region number {region}",
                Values = months.Select((_, month) => 1000.0 * region + month).ToList(),
            }).ToList(),
        };
        var window = Show(chart);
        // The tests load Strata alone, which styles the host theme's scroll viewer rather than templating one.
        window.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>())
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollViewer>((_, scope) =>
                    new ScrollContentPresenter { Name = "PART_ContentPresenter" }.RegisterInNameScope(scope)))
            }
        });
        try
        {
            chart.SetTableView(true);
            Pump(window);
            var scroll = Find<ScrollViewer>(chart, "PART_TableScroll");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1 && scroll.Extent.Width > scroll.Viewport.Width + 1,
                $"The table should overflow both ways: {scroll.Extent} in {scroll.Viewport}.");
            Assert.True(scroll.Focus(NavigationMethod.Tab), "The table is a tab stop.");

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Pump(window);
            Assert.True(scroll.Offset.Y > 0, "Down moves the rows.");
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Pump(window);
            Assert.True(scroll.Offset.X > 0, "Right moves the columns.");
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            Pump(window);
            Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, precision: 1);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LineBelowZero_FallsUnderTheZeroLine_InsideThePlot() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var chart = new StrataChart
        {
            ChartType = StrataChartType.Line,
            ChartHeight = 220,
            ShowGrid = true,
            Labels = ["Jan", "Feb", "Mar"],
            Series = [new StrataChartSeries { Name = "Profit", Values = [100, -10, 100] }],
        };
        var window = Show(chart);
        try
        {
            var canvas = Canvas(chart);
            canvas.GetType().GetMethod("SetHoverIndex", PrivateInstance)!.Invoke(canvas, [1]);
            var shapes = Paint(canvas);
            // Grid lines and the baseline: level strokes across the plot.
            var rules = shapes.Where(shape => shape.Pen is not null && shape.Brush is null)
                .Select(shape => shape.Geometry!.Bounds)
                .Where(bounds => bounds.Height < 0.5 && bounds.Width > 100)
                .ToList();
            var zero = rules[^1].Y; // the baseline, drawn last, at zero
            var bottom = rules.Max(bounds => bounds.Y);
            // The points: small dots, and the haloed one under the pointer.
            double PointAt(double radius) => shapes
                .Where(shape => shape.Brush is not null && Math.Abs(shape.Geometry!.Bounds.Width - radius * 2) < 0.1)
                .Select(shape => shape.Geometry!.Bounds.Center.Y).First();
            var gain = PointAt(2.5);
            var loss = PointAt(4.25);

            Assert.True(gain < zero, $"The gain ({gain:0.#}) should rise above zero ({zero:0.#}).");
            Assert.True(loss > zero + 1, $"The loss ({loss:0.#}) should fall below zero ({zero:0.#}).");
            Assert.True(loss < bottom - 1, $"The loss ({loss:0.#}) should stay inside the plot ({bottom:0.#}).");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MissingValue_LeavesTheOthersOnThePane() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var chart = new StrataChart
        {
            ChartType = StrataChartType.Line,
            ChartHeight = 220,
            Labels = ["Mon", "Tue", "Wed"],
            Series =
            [
                new StrataChartSeries { Name = "Actual", Values = [40, double.NaN, 44] },
                new StrataChartSeries { Name = "Forecast", Values = [41, 42, 43] },
            ],
        };
        var window = Show(chart);
        try
        {
            var canvas = Canvas(chart);
            canvas.GetType().GetMethod("SetHoverIndex", PrivateInstance)!.Invoke(canvas, [1]);
            Pump(window);
            var tip = Find<Panel>(chart, "PART_Tip");
            Assert.True(tip.IsVisible);
            Assert.Equal(["Tue", "Forecast", "42"], tip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            var offset = TipOffset(tip);
            Assert.True(double.IsFinite(offset.X) && double.IsFinite(offset.Y), $"The pane is placed at {offset}.");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FullScreen_ShowsTheChartLarge_InTheViewItWasIn_AndCloses() => fixture.Dispatch(() =>
    {
        using var culture = new EnglishCulture();
        var (window, chart) = ShowMarkdownChart(RevenueChart);
        try
        {
            Click(chart, "PART_TableView");
            Click(chart, "PART_Expand");
            Pump(window);
            Assert.True(StrataFullScreenOverlay.IsPresented(window));
            var expanded = window.GetVisualDescendants().OfType<StrataChart>().Single(item => item.Name == "ExpandedChart");
            Assert.True(expanded.IsTableView);
            Assert.True(expanded.Bounds.Height > chart.Bounds.Height);

            Click(expanded, "PART_Expand");
            Pump(window);
            Assert.False(StrataFullScreenOverlay.IsPresented(window));
        }
        finally { window.Close(); }
    });

    private static (Window Window, StrataChart Chart) ShowMarkdownChart(string json)
    {
        var markdown = new StrataMarkdown
        {
            Markdown = $"```chart\n{json}\n```",
            IsInline = true,
            StreamingRebuildThrottleMs = 0,
        };
        var window = Show(markdown);
        var chart = Assert.Single(markdown.GetVisualDescendants().OfType<StrataChart>());
        return (window, chart);
    }

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Width = 640,
            Height = 420,
            // A bare test application: the window hosts an overlay layer, as an app's does, for full screen.
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner }
                }.RegisterInNameScope(scope)
            }),
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = content;
        window.Show();
        Pump(window);
        return window;
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Engaged(StrataChart chart) => chart.Classes.Contains(":engaged");

    private static void RaisePointer(Control target, RoutedEvent<PointerEventArgs> routedEvent, IPointer pointer, Window window, Point at) =>
        target.RaiseEvent(new PointerEventArgs(routedEvent, target, pointer, window, at, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));

    // Lets timers and transitions run, as frames pass, until the condition holds or the time is up.
    private static async Task<bool> Settle(Window window, Func<bool> done, int milliseconds = 1500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!done() && DateTime.UtcNow < deadline)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Pump(window);
            await Task.Delay(16);
        }
        return done();
    }

    private static Control Canvas(StrataChart chart) =>
        Assert.IsAssignableFrom<Control>(typeof(StrataChart).GetField("_canvas", PrivateInstance)!.GetValue(chart));

    private static (double GroupWidth, double BarWidth, double BarGap, double ClusterWidth) BarMetrics(
        Control canvas, int visible, double width = 0, int labels = 3)
    {
        var rect = width > 0 ? new Rect(0, 0, width, 160) : new Rect(44, 0, canvas.Bounds.Width - 44 - 14, 160);
        var metrics = canvas.GetType().GetMethod("BarMetrics", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [rect, labels, visible])!;
        var type = metrics.GetType();
        double Field(string name) => (double)type.GetField(name)!.GetValue(metrics)!;
        return (Field("Item1"), Field("Item2"), Field("Item3"), Field("Item4"));
    }

    private static List<Rect> LegendRects(Control canvas)
    {
        var rects = (System.Collections.IEnumerable)canvas.GetType().GetField("_legendHitRects", PrivateInstance)!.GetValue(canvas)!;
        return rects.Cast<object>().Select(item => (Rect)item.GetType().GetField("Item2")!.GetValue(item)!).ToList();
    }

    private static Point TipOffset(Panel tip)
    {
        var matrix = tip.RenderTransform!.Value;
        return new Point(matrix.M31, matrix.M32);
    }

    // What one paint of the plot draws, its entrance played out.
    private static List<GeometryDrawing> Paint(Control canvas)
    {
        canvas.GetType().GetField("_animProgress", PrivateInstance)!.SetValue(canvas, 1.0);
        canvas.GetType().GetField("_hasAnimated", PrivateInstance)!.SetValue(canvas, true);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            canvas.Render(context);
        return Flatten(drawing).OfType<GeometryDrawing>().ToList();

        static IEnumerable<Drawing> Flatten(Drawing drawing) =>
            drawing is DrawingGroup group ? group.Children.SelectMany(Flatten) : [drawing];
    }

    private static T Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Visual root, string name) =>
        Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Numbers and labels read as in English while a test runs, whatever the machine's language.</summary>
    private sealed class EnglishCulture : IDisposable
    {
        private readonly System.Globalization.CultureInfo _culture = System.Globalization.CultureInfo.CurrentCulture;
        private readonly System.Globalization.CultureInfo _uiCulture = System.Globalization.CultureInfo.CurrentUICulture;

        public EnglishCulture()
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        }

        public void Dispose()
        {
            System.Globalization.CultureInfo.CurrentCulture = _culture;
            System.Globalization.CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
