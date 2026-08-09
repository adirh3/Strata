using System.Collections;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataChartRtlTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataChartRtlTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task ChartText_UsesLeadingStrongCharacterForMixedRtlContent()
    {
        return _fixture.Dispatch(() =>
        {
            var canvasType = typeof(StrataChart).GetNestedType("ChartCanvas", BindingFlags.NonPublic);
            var textFactory = canvasType?.GetMethod("Txt", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(textFactory);

            var formattedText = Assert.IsType<FormattedText>(textFactory.Invoke(
                null,
                ["מכירות API platform metrics", 11d, Brushes.White, FontWeight.Normal]));

            Assert.Equal(FlowDirection.RightToLeft, formattedText.FlowDirection);
        });
    }

    [Fact]
    public Task HorizontalLegend_PlacesFirstRtlSeriesAtTheRightEdge()
    {
        return _fixture.Dispatch(() =>
        {
            var chart = CreateCartesianChart(
                StrataChartType.Line,
                ["מכירות API platform metrics", "תחזית SLA 2026"]);

            var hitRects = RenderAndReadLegendHitRects(chart);

            Assert.Equal(2, hitRects.Count);
            Assert.True(
                hitRects[0].Left > hitRects[1].Left,
                $"Expected the first RTL series on the right, but got {hitRects[0]} then {hitRects[1]}.");
        });
    }

    [Fact]
    public Task HorizontalLegend_PreservesLeftToRightSeriesOrder()
    {
        return _fixture.Dispatch(() =>
        {
            var chart = CreateCartesianChart(
                StrataChartType.Line,
                ["Actual API", "Forecast SLA"]);

            var hitRects = RenderAndReadLegendHitRects(chart);

            Assert.Equal(2, hitRects.Count);
            Assert.True(
                hitRects[0].Left < hitRects[1].Left,
                $"Expected the first LTR series on the left, but got {hitRects[0]} then {hitRects[1]}.");
        });
    }

    [Theory]
    [InlineData(StrataChartType.Donut)]
    [InlineData(StrataChartType.Pie)]
    public Task RadialLegend_RightAlignsRtlItems(StrataChartType chartType)
    {
        return _fixture.Dispatch(() =>
        {
            var chart = CreateRadialChart(chartType);
            var hitRects = RenderAndReadLegendHitRects(chart);

            Assert.Equal(4, hitRects.Count);
            var rightEdges = hitRects.Values.Select(rect => rect.Right).ToArray();
            Assert.InRange(rightEdges.Max() - rightEdges.Min(), 0, 0.5);
        });
    }

    [Theory]
    [InlineData(StrataChartType.Line)]
    [InlineData(StrataChartType.Bar)]
    [InlineData(StrataChartType.Donut)]
    [InlineData(StrataChartType.Pie)]
    public Task MixedRtlTextAndTooltip_RenderAcrossEveryChartType(StrataChartType chartType)
    {
        return _fixture.Dispatch(() =>
        {
            var chart = chartType is StrataChartType.Donut or StrataChartType.Pie
                ? CreateRadialChart(chartType)
                : CreateCartesianChart(chartType, ["מכירות API platform metrics", "תחזית SLA 2026"]);

            RenderAndReadLegendHitRects(chart, hoverIndex: 0);
        });
    }

    [Theory]
    [InlineData(StrataChartType.Line)]
    [InlineData(StrataChartType.Bar)]
    [InlineData(StrataChartType.Donut)]
    [InlineData(StrataChartType.Pie)]
    public Task AppLevelRtl_UsesDrawingAndPointerCompensation(StrataChartType chartType)
    {
        return _fixture.Dispatch(() =>
        {
            var chart = chartType is StrataChartType.Donut or StrataChartType.Pie
                ? CreateRadialChart(chartType)
                : CreateCartesianChart(chartType, ["מכירות API platform metrics", "תחזית SLA 2026"]);

            Window? window = null;
            try
            {
                window = CreateWindow(chart, FlowDirection.RightToLeft);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var canvas = GetChartCanvas(chart);
                Assert.Equal(FlowDirection.RightToLeft, chart.FlowDirection);
                Assert.Equal(FlowDirection.RightToLeft, canvas.FlowDirection);

                var isHostedInRtlLayout = canvas.GetType().GetMethod(
                    "IsHostedInRtlLayout",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var getChartPosition = canvas.GetType().GetMethod(
                    "GetChartPosition",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.NotNull(isHostedInRtlLayout);
                Assert.NotNull(getChartPosition);
                Assert.True(Assert.IsType<bool>(isHostedInRtlLayout.Invoke(canvas, null)));

                var mirroredPosition = Assert.IsType<Point>(
                    getChartPosition.Invoke(canvas, [new Point(40, 20)]));
                Assert.Equal(canvas.Bounds.Width - 40, mirroredPosition.X);
                Assert.Equal(20, mirroredPosition.Y);

                var setHoverIndex = canvas.GetType().GetMethod(
                    "SetHoverIndex",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(setHoverIndex);
                setHoverIndex.Invoke(canvas, [0]);

                using var bitmap = new RenderTargetBitmap(new PixelSize(640, 320), new Vector(96, 96));
                Assert.Null(Record.Exception(() => bitmap.Render(window)));

                window.FlowDirection = FlowDirection.LeftToRight;
                Dispatcher.UIThread.RunJobs();
                Assert.False(Assert.IsType<bool>(isHostedInRtlLayout.Invoke(canvas, null)));

                var normalPosition = Assert.IsType<Point>(
                    getChartPosition.Invoke(canvas, [new Point(40, 20)]));
                Assert.Equal(new Point(40, 20), normalPosition);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public Task NestedRtlContainer_UsesEffectiveFlowDirection()
    {
        return _fixture.Dispatch(() =>
        {
            var chart = CreateCartesianChart(
                StrataChartType.Line,
                ["מכירות API platform metrics", "תחזית SLA 2026"]);
            var host = new Border
            {
                FlowDirection = FlowDirection.RightToLeft,
                Child = chart,
            };

            Window? window = null;
            try
            {
                window = CreateWindow(host, FlowDirection.LeftToRight);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var canvas = GetChartCanvas(chart);
                var isHostedInRtlLayout = canvas.GetType().GetMethod(
                    "IsHostedInRtlLayout",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.NotNull(isHostedInRtlLayout);
                Assert.Equal(FlowDirection.RightToLeft, chart.FlowDirection);
                Assert.Equal(FlowDirection.RightToLeft, canvas.FlowDirection);
                Assert.True(Assert.IsType<bool>(isHostedInRtlLayout.Invoke(canvas, null)));

                host.FlowDirection = FlowDirection.LeftToRight;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(FlowDirection.LeftToRight, chart.FlowDirection);
                Assert.Equal(FlowDirection.LeftToRight, canvas.FlowDirection);
                Assert.False(Assert.IsType<bool>(isHostedInRtlLayout.Invoke(canvas, null)));
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public Task ExplicitLtrChartInRtlWindow_DoesNotUseRtlCompensation()
    {
        return _fixture.Dispatch(() =>
        {
            var chart = CreateCartesianChart(
                StrataChartType.Line,
                ["Actual API", "Forecast SLA"]);
            chart.FlowDirection = FlowDirection.LeftToRight;

            Window? window = null;
            try
            {
                window = CreateWindow(chart, FlowDirection.RightToLeft);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var canvas = GetChartCanvas(chart);
                var isHostedInRtlLayout = canvas.GetType().GetMethod(
                    "IsHostedInRtlLayout",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var getChartPosition = canvas.GetType().GetMethod(
                    "GetChartPosition",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.NotNull(isHostedInRtlLayout);
                Assert.NotNull(getChartPosition);
                Assert.Equal(FlowDirection.LeftToRight, canvas.FlowDirection);
                Assert.False(Assert.IsType<bool>(isHostedInRtlLayout.Invoke(canvas, null)));
                Assert.Equal(
                    new Point(40, 20),
                    Assert.IsType<Point>(getChartPosition.Invoke(canvas, [new Point(40, 20)])));
            }
            finally
            {
                window?.Close();
            }
        });
    }

    private static StrataChart CreateCartesianChart(StrataChartType chartType, IReadOnlyList<string> seriesNames)
    {
        return new StrataChart
        {
            ChartType = chartType,
            ChartHeight = 260,
            Labels = ["ינואר API 2026", "פברואר SLA", "מרץ Q1", "אפריל 24/7"],
            Series =
            [
                new StrataChartSeries { Name = seriesNames[0], Values = [12, 24, 18, 32] },
                new StrataChartSeries { Name = seriesNames[1], Values = [10, 20, 26, 30] },
            ],
        };
    }

    private static StrataChart CreateRadialChart(StrataChartType chartType)
    {
        return new StrataChart
        {
            ChartType = chartType,
            ChartHeight = 280,
            Labels = ["אימות", "עיבוד API platform 2026", "התראות SLA", "דוחות Q4"],
            Series =
            [
                new StrataChartSeries { Name = "חלוקת בקשות API", Values = [30, 45, 15, 10] },
            ],
            DonutCenterValue = "100%",
            DonutCenterLabel = "סה״כ API 2026",
        };
    }

    private static Dictionary<int, Rect> RenderAndReadLegendHitRects(StrataChart chart, int? hoverIndex = null)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        Window? window = null;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            window = CreateWindow(chart, FlowDirection.LeftToRight);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            using var bitmap = new RenderTargetBitmap(new PixelSize(640, 320), new Vector(96, 96));
            bitmap.Render(chart);

            var canvas = GetChartCanvas(chart);

            if (hoverIndex is not null)
            {
                var setHoverIndex = canvas.GetType().GetMethod(
                    "SetHoverIndex",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(setHoverIndex);
                setHoverIndex.Invoke(canvas, [hoverIndex.Value]);
                bitmap.Render(chart);
            }

            var hitRectsField = canvas.GetType().GetField(
                "_legendHitRects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var rawHitRects = Assert.IsAssignableFrom<IEnumerable>(hitRectsField?.GetValue(canvas));
            var result = new Dictionary<int, Rect>();

            foreach (var item in rawHitRects)
            {
                Assert.NotNull(item);
                var itemType = item.GetType();
                var index = Assert.IsType<int>(itemType.GetField("Item1")?.GetValue(item));
                var bounds = Assert.IsType<Rect>(itemType.GetField("Item2")?.GetValue(item));
                result[index] = bounds;
            }

            return result;
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static Window CreateWindow(Control content, FlowDirection flowDirection)
    {
        var window = new Window
        {
            Width = 640,
            Height = 320,
            FlowDirection = flowDirection,
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml"),
        });
        window.Content = content;
        return window;
    }

    private static Control GetChartCanvas(StrataChart chart)
    {
        var canvas = typeof(StrataChart)
            .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(chart);
        return Assert.IsAssignableFrom<Control>(canvas);
    }
}
