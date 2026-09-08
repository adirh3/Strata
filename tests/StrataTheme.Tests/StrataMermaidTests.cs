using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMermaidTests(AvaloniaFixture fixture)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string WideFlow = """
        flowchart LR
            A[Old single JSON chat] --> B[New Lumi first accesses it]
            B --> C[Create segmented canonical store]
            C --> D{Valid V5 exists?}
            D -->|No| E[Build V5 once]
            D -->|Yes| F[Open immediately]
            E --> F
            F --> G[Future opens load tail and visible pages only]
            C --> H[Keep legacy JSON as safety snapshot]
        """;

    private const string ClusteredFlow = """
        flowchart TB
            subgraph Storage[Immutable storage]
                Metadata[data.json chat metadata]
                Segments[256-message segments]
                Manifest[V5 generation manifest]
                Geometry[Compact whole-chat geometry]
                Payloads[Page-local transcript payloads]
            end
            subgraph Runtime[Active chat runtime]
                History[ChatHistoryWindow]
                Catalog[TranscriptCatalogRuntime]
                Tail[Mutable loaded tail]
                Cache[Bounded hydrated-page cache]
            end
            subgraph UI[Per-window UI]
                Panel[TranscriptVirtualizingPanel]
                Fenwick[Packed Fenwick height index]
                Controls[Realized controls]
                Strata[Strata follow-tail policy]
            end
            Metadata --> Catalog
            Manifest --> Geometry
            Geometry --> Catalog
            Segments --> History
            Payloads --> Cache
            History --> Tail
            Tail --> Catalog
            Cache --> Catalog
            Catalog --> Panel
            Panel --> Fenwick
            Panel --> Controls
            Strata --> Panel
        """;

    [Fact]
    public Task WideFlow_StartsReadable_AndOffersFitAndActualSize() => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram(WideFlow, 494);
        try
        {
            Assert.InRange(Scale(canvas), 0.85, 1);
            Click(diagram, "PART_ZoomReset");
            Assert.InRange(Scale(canvas), 0.05, 0.6);
            Click(diagram, "PART_ActualSize");
            Assert.Equal(1, Scale(canvas), 6);
            Assert.InRange(canvas.Bounds.Height, 180, 480);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Diagram_IsBorderlessWithTransparentToolbarOutsideCanvas() => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram("sequenceDiagram\nA->>B: Request\nB-->>A: Response", 494);
        try
        {
            var frame = Assert.IsType<Border>(diagram.GetVisualChildren().Single());
            Assert.Equal(default, frame.BorderThickness);
            Assert.Equal(default, frame.CornerRadius);
            Assert.Null(frame.Background);
            var toolbar = Find<Border>(diagram, "PART_ZoomBar");
            Assert.Equal(default, toolbar.BorderThickness);
            Assert.Equal((byte)0, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(toolbar.Background).Color.A);
            Assert.DoesNotContain(diagram.GetVisualDescendants(), control => control.Name == "PART_GestureHint");
            var canvasBottom = canvas.TranslatePoint(new Point(0, canvas.Bounds.Height), diagram)!.Value.Y;
            var toolbarTop = toolbar.TranslatePoint(default, diagram)!.Value.Y;
            Assert.True(toolbarTop >= canvasBottom);
            Assert.Equal(1, toolbar.Opacity);
            Assert.True(Find<Button>(diagram, "PART_Expand").IsVisible);
            Assert.Equal("Fit diagram", AutomationProperties.GetName(Find<Button>(diagram, "PART_ZoomReset")));
            Assert.Equal("Actual size", AutomationProperties.GetName(Find<Button>(diagram, "PART_ActualSize")));
            Assert.Equal("Full screen", AutomationProperties.GetName(Find<Button>(diagram, "PART_Expand")));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(494)]
    [InlineData(720)]
    public Task ClusteredFlow_DirectConnectionsDoNotPassThroughOtherNodes(double width) => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram(ClusteredFlow, width);
        try
        {
            var nodes = Field<IDictionary>(canvas, "_fNodes");
            var edges = Field<IEnumerable>(canvas, "_fEdges").Cast<object>();
            foreach (var edge in edges.Where(e => PublicField<string>(e, "From") is "Geometry" or "Catalog"))
            {
                var from = PublicField<string>(edge, "From");
                var to = PublicField<string>(edge, "To");
                var route = PublicField<List<Point>>(edge, "Route");
                Assert.True(route.Count >= 2);
                foreach (DictionaryEntry entry in nodes)
                {
                    if ((string)entry.Key == from || (string)entry.Key == to) continue;
                    var node = entry.Value!;
                    var rect = new Rect(PublicField<double>(node, "X"), PublicField<double>(node, "Y"),
                        PublicField<double>(node, "W"), PublicField<double>(node, "H")).Deflate(0.1);
                    for (var i = 1; i < route.Count; i++)
                        Assert.False(Crosses(route[i - 1], route[i], rect),
                            $"{from} -> {to} crosses {entry.Key}: {route[i - 1]} -> {route[i]}");
                }
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InlineView_DoesNotStretchIntoUnusedVerticalSpace() => fixture.Dispatch(() =>
    {
        var (window, diagram, _) = CreateDiagram("flowchart LR\nA --> B", 494);
        try
        {
            ((StackPanel)window.Content!).Children.Remove(diagram);
            window.Content = new Border { Child = diagram };
            Pump(window);
            Assert.InRange(diagram.Bounds.Height, 180, 530);
            Assert.True(diagram.Bounds.Height < window.ClientSize.Height);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExpandedView_FillsWindow_TracksResize_AndRestoresFocusOnEscapeOrClose() => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram(WideFlow, 360);
        try
        {
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(canvas.Focus());
            Click(diagram, "PART_Expand");
            Pump(window);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            var layer = Assert.IsType<OverlayLayer>(OverlayLayer.GetOverlayLayer(diagram));
            var overlay = Find<Border>(window, "MermaidFullScreen");
            var expanded = Find<StrataMermaid>(window, "ExpandedMermaid");
            var expandedCanvas = Field<Control>(expanded, "_canvas");
            Assert.Equal(layer.Bounds.Size, overlay.Bounds.Size);
            Assert.Equal(window.ClientSize, layer.Bounds.Size);
            Assert.Equal(layer.Bounds.Width - expanded.Margin.Left - expanded.Margin.Right, expanded.Bounds.Width, 6);
            Assert.True(expandedCanvas.Bounds.Height > 500);
            Assert.Same(expandedCanvas, window.FocusManager!.GetFocusedElement());

            window.Width = 1040;
            window.Height = 780;
            Pump(window);
            Assert.Equal(window.ClientSize, overlay.Bounds.Size);
            Assert.Equal(layer.Bounds.Height - expanded.Margin.Top - expanded.Margin.Bottom, expanded.Bounds.Height, 6);

            PressKey(window, Key.Escape);
            Pump(window);
            Assert.Empty(layer.Children);
            Assert.DoesNotContain(window.GetVisualDescendants(), c => c.Name == "ExpandedMermaid");
            Assert.Same(canvas, window.FocusManager.GetFocusedElement());
            Assert.Equal(WindowState.Normal, window.WindowState);

            foreach (var previousState in new[] { WindowState.Maximized, WindowState.FullScreen })
            {
                window.WindowState = previousState;
                Pump(window);
                Assert.True(canvas.Focus());
                Click(diagram, "PART_Expand");
                Pump(window);
                Assert.Equal(WindowState.FullScreen, window.WindowState);
                expanded = Find<StrataMermaid>(window, "ExpandedMermaid");
                Click(expanded, "PART_Expand");
                Pump(window);
                Assert.Empty(layer.Children);
                Assert.Same(canvas, window.FocusManager.GetFocusedElement());
                Assert.Equal(previousState, window.WindowState);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExpandedView_TracksSource_AndDetachingOwnerRemovesOverlay() => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram(WideFlow, 360);
        try
        {
            window.WindowState = WindowState.Maximized;
            Click(diagram, "PART_Expand");
            Pump(window);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            var layer = Assert.IsType<OverlayLayer>(OverlayLayer.GetOverlayLayer(diagram));
            var expanded = Find<StrataMermaid>(window, "ExpandedMermaid");
            const string updated = "flowchart LR\nFresh[Updated source] --> Result[New result]";
            diagram.Source = updated;
            Pump(window);
            Assert.Equal(updated, expanded.Source);
            foreach (var currentCanvas in new[] { canvas, Field<Control>(expanded, "_canvas") })
            {
                var nodes = Field<IDictionary>(currentCanvas, "_fNodes");
                Assert.True(nodes.Contains("Fresh"));
                Assert.False(nodes.Contains("A"));
            }

            var originalContent = window.Content;
            window.Content = null;
            Pump(window);
            Assert.Empty(layer.Children);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Null(TopLevel.GetTopLevel(expanded));
            Assert.Null(Field<StrataMermaid?>(diagram, "_expandedDiagram"));
            Assert.Null(Field<Window?>(diagram, "_expandedWindow"));
            Assert.Null(Field<IInputElement?>(diagram, "_previousFocus"));

            diagram.Source = "flowchart LR\nAfter --> Close";
            Assert.Equal(updated, expanded.Source);
            window.Content = originalContent;
            Pump(window);
            Assert.Empty(layer.Children);
            Click(diagram, "PART_Expand");
            Pump(window);
            Assert.Equal(diagram.Source, Find<StrataMermaid>(window, "ExpandedMermaid").Source);
            Assert.Single(layer.Children);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BlankCanvas_HitTests_AndDragCapturesOutsideBoundsUntilRelease() => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram("flowchart LR\nA --> B", 360);
        try
        {
            var start = CanvasPoint(window, canvas, new Point(8, 8));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Same(canvas, window.InputHitTest(start));
            window.MouseDown(start, MouseButton.Left);
            var pointer = Assert.IsAssignableFrom<IPointer>(Field<IPointer?>(canvas, "_panPointer"));
            Assert.Same(canvas, pointer.Captured);
            Assert.Same(canvas, window.FocusManager!.GetFocusedElement());

            var outside = CanvasPoint(window, canvas, new Point(canvas.Bounds.Width + 30, canvas.Bounds.Height + 20));
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
            Assert.Equal(outside.X - start.X, Field<double>(canvas, "_panX"), 6);
            Assert.Equal(outside.Y - start.Y, Field<double>(canvas, "_panY"), 6);
            var pan = Pan(canvas);
            window.MouseUp(outside, MouseButton.Left);
            Assert.Null(pointer.Captured);
            Assert.False(Field<bool>(canvas, "_isPanning"));
            window.MouseMove(start);
            Assert.Equal(pan, Pan(canvas));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Pan_CaptureLossOrDetachStopsDrag_AndAllowsFreshCapture(bool detach) => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram("flowchart LR\nA --> B", 360);
        try
        {
            var start = CanvasPoint(window, canvas, new Point(8, 8));
            window.MouseDown(start, MouseButton.Left);
            var pointer = Assert.IsAssignableFrom<IPointer>(Field<IPointer?>(canvas, "_panPointer"));
            window.MouseMove(start + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
            var pan = Pan(canvas);
            var originalContent = window.Content;
            if (detach) window.Content = null;
            else pointer.Capture(null);
            Pump(window);
            Assert.Null(pointer.Captured);
            Assert.Null(Field<IPointer?>(canvas, "_panPointer"));
            Assert.False(Field<bool>(canvas, "_isPanning"));

            window.MouseMove(start + new Vector(70, 50), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(70, 50), MouseButton.Left);
            Assert.Equal(pan, Pan(canvas));
            if (detach)
            {
                window.Content = originalContent;
                Pump(window);
            }
            // A different location avoids the headless mouse double-click-to-fit path.
            start = CanvasPoint(window, canvas, new Point(100, 80));
            window.MouseDown(start, MouseButton.Left);
            Assert.Same(canvas, pointer.Captured);
            window.MouseUp(start, MouseButton.Left);
            Assert.Null(pointer.Captured);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Keyboard_ZoomPanFitAndActualSize_HandleOnlyDiagramKeys() => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram(WideFlow, 494);
        try
        {
            var parentKeys = new List<Key>();
            ((Control)window.Content!).AddHandler(InputElement.KeyDownEvent, (_, e) => parentKeys.Add(e.Key));
            Assert.True(canvas.Focus());
            var initial = Scale(canvas);
            PressKey(window, Key.OemPlus);
            Assert.True(Scale(canvas) > initial);
            PressKey(window, Key.D0);
            Assert.Equal(1, Scale(canvas), 6);
            PressKey(window, Key.Right);
            Assert.Equal(-40, Field<double>(canvas, "_panX"), 6);
            PressKey(window, Key.Down);
            Assert.Equal(-40, Field<double>(canvas, "_panY"), 6);
            PressKey(window, Key.Home);
            Assert.InRange(Scale(canvas), 0.05, 0.6);
            Assert.Equal(default, Pan(canvas));
            Assert.Equal($"{Math.Round(Scale(canvas) * 100)}%", Find<TextBlock>(diagram, "PART_ZoomLabel").Text);
            Assert.Empty(parentKeys);
            PressKey(window, Key.PageDown);
            Assert.Equal([Key.PageDown], parentKeys);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Wheel_OrdinaryScrollBubblesToParent_ModifiedWheelPansOrZooms() => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram(WideFlow, 494);
        try
        {
            var parentWheelCount = 0;
            ((Control)window.Content!).AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => parentWheelCount++);
            var at = CanvasPoint(window, canvas, new Point(8, 8));
            var initialScale = Scale(canvas);
            window.MouseWheel(at, new Vector(0, -1));
            Assert.Equal(1, parentWheelCount);
            Assert.Equal(initialScale, Scale(canvas), 6);
            Assert.Equal(default, Pan(canvas));

            window.MouseWheel(at, new Vector(0, -1), RawInputModifiers.Shift);
            Assert.Equal(-40, Field<double>(canvas, "_panX"), 6);
            Assert.Equal(1, parentWheelCount);
            window.MouseWheel(at, new Vector(1, 0));
            Assert.Equal(0, Field<double>(canvas, "_panX"), 6);
            Assert.Equal(initialScale, Scale(canvas), 6);
            Assert.Equal(1, parentWheelCount);
            var command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.MouseWheel(at, new Vector(0, 1), command);
            Assert.True(Scale(canvas) > initialScale);
            Assert.Equal(1, parentWheelCount);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("TB")]
    [InlineData("LR")]
    public Task NestedSubgraphs_KeepInnerContainersSeparateAndInsideOuterContainer(string direction) => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram($"""
            flowchart {direction}
                subgraph Outer[Outer service]
                    A[Outer entry]
                    subgraph Inner[Inner pipeline]
                        B[Parse] --> C[Render]
                    end
                    subgraph Sibling[Audit pipeline]
                        U[Record] --> V[Flush]
                    end
                    A --> B
                    A --> U
                end
                C --> D[Outside]
                V --> D
            """, 494);
        try
        {
            var groups = Field<IEnumerable>(canvas, "_fSubgraphs").Cast<object>()
                .ToDictionary(g => PublicField<string>(g, "Id"), g => PublicField<Rect>(g, "Box"));
            var outer = groups["Outer"];
            var inner = groups["Inner"];
            var sibling = groups["Sibling"];
            Assert.True(inner.Width > 0 && inner.Height > 0, $"Inner container is missing: Inner={inner}, Outer={outer}");
            Assert.True(outer.Contains(inner), $"Outer={outer} does not contain Inner={inner}");
            Assert.True(sibling.Width > 0 && sibling.Height > 0);
            Assert.True(outer.Contains(sibling));
            Assert.False(inner.Intersects(sibling), $"Nested siblings overlap: {inner}, {sibling}");
            var nodes = Field<IDictionary>(canvas, "_fNodes");
            foreach (var id in new[] { "B", "C" })
            {
                var node = nodes[id]!;
                Assert.True(inner.Contains(new Rect(PublicField<double>(node, "X"), PublicField<double>(node, "Y"),
                    PublicField<double>(node, "W"), PublicField<double>(node, "H"))));
            }
        }
        finally { window.Close(); }
    });

    private static (Window Window, StrataMermaid Diagram, Control Canvas) CreateDiagram(string source, double width)
    {
        var diagram = new StrataMermaid { Source = source, Width = width, MinHeight = 180 };
        var window = new Window
        {
            Width = 900, Height = 700,
            // AvaloniaFixture uses a bare Application: supply only the Window host;
            // the Mermaid template below is the actual shipped Strata AXAML.
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner }
                }.RegisterInNameScope(scope)
            }),
            Content = new StackPanel { Children = { diagram } }
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme.Tests/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        diagram.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataMermaid)));
        window.Show();
        window.ApplyTemplate();
        Pump(window);
        Assert.True(window.GetVisualDescendants().Contains(diagram),
            "Window did not attach diagram: " + string.Join(", ", window.GetVisualDescendants().Select(c => $"{c.GetType().Name}:{(c as Control)?.Name}")));
        var canvas = Field<Control>(diagram, "_canvas");
        Assert.True(canvas is not null, $"Diagram template missing: template={diagram.Template}, theme={diagram.Theme}, bounds={window.Bounds}");
        return (window, diagram, canvas);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Point CanvasPoint(Window window, Control canvas, Point point) =>
        canvas.TranslatePoint(point, window)!.Value;

    private static Vector Pan(Control canvas) =>
        new(Field<double>(canvas, "_panX"), Field<double>(canvas, "_panY"));

    private static void PressKey(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static bool Crosses(Point a, Point b, Rect rect) =>
        Math.Abs(a.X - b.X) < 0.01
            ? a.X > rect.Left && a.X < rect.Right && Math.Max(a.Y, b.Y) > rect.Top && Math.Min(a.Y, b.Y) < rect.Bottom
            : Math.Abs(a.Y - b.Y) < 0.01 && a.Y > rect.Top && a.Y < rect.Bottom &&
              Math.Max(a.X, b.X) > rect.Left && Math.Min(a.X, b.X) < rect.Right;

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, PrivateInstance)!.GetValue(owner)!;

    private static T PublicField<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name)!.GetValue(owner)!;

    private static double Scale(Control canvas) =>
        (double)canvas.GetType().GetProperty("EffectiveScale", PrivateInstance)!.GetValue(canvas)!;

    private static T Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Click(Visual root, string name) =>
        Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
