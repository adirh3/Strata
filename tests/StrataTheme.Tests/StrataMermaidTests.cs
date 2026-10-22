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

    private const string OwnershipFlow = """
        flowchart TB
            subgraph Runtime["Copilot owns execution"]
                MCP["MCP servers"] <--> CP["Copilot runtime"]
                EXT["Canvas extensions"] <--> CP
            end

            subgraph Lumi["Lumi owns presentation"]
                CP <--> MA["MCP Apps adapter"]
                CP <--> CA["Canvas adapter"]
                MA <--> STATE["Per-chat surface state"]
                CA <--> STATE
                STATE <--> UI["Strata Workspace and transcript cards"]
                UI <--> VIEW["Native view or isolated WebView"]
            end
        """;

    private const string LabelledLinksFlow = """
        flowchart LR
            R["Copilot runtime"] <-->|"Open, close, invoke action"| P["Canvas provider"]
            R -->|"Lifecycle events"| U["Lumi UI"]
            P <-->|"Interface and shared artifact"| U
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
            Assert.Null(Field<StrataFullScreenOverlay?>(diagram, "_expandedView"));

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
    public Task ExpandedView_SystemBackRunsBeforeHostNavigationWithoutViewerFocus() => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram(WideFlow, 360);
        try
        {
            var hostBacks = 0;
            window.BackRequested += (_, _) => hostBacks++;
            Click(diagram, "PART_Expand");
            Pump(window);
            Assert.True(StrataFullScreenOverlay.IsPresented(window));
            canvas.Focus();
            var back = new RoutedEventArgs(TopLevel.BackRequestedEvent);
            window.RaiseEvent(back);
            Pump(window);

            Assert.True(back.Handled);
            Assert.Equal(0, hostBacks);
            Assert.False(StrataFullScreenOverlay.IsPresented(window));
            Assert.Empty(OverlayLayer.GetOverlayLayer(diagram)!.Children);
            window.RaiseEvent(new RoutedEventArgs(TopLevel.BackRequestedEvent));
            Assert.Equal(1, hostBacks);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TouchPinchUsesCumulativeScaleAndStartsFreshForTheNextGesture(bool expanded) => fixture.Dispatch(() =>
    {
        var (window, diagram, canvas) = CreateDiagram(WideFlow, 494);
        try
        {
            if (expanded)
            {
                Click(diagram, "PART_Expand");
                Pump(window);
                canvas = Field<Control>(Find<StrataMermaid>(window, "ExpandedMermaid"), "_canvas");
            }
            var center = CanvasPoint(window, canvas, new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Same(canvas, window.InputHitTest(center));
            var initial = Scale(canvas);
            Pinch();
            Assert.Equal(initial * 2, Scale(canvas), 5);
            Pinch();
            Assert.Equal(initial * 4, Scale(canvas), 5);

            void Pinch()
            {
                var first = window.TouchBegin(center - new Vector(40, 0));
                var second = window.TouchBegin(center + new Vector(40, 0));
                window.TouchMove(first, center - new Vector(80, 0));
                window.TouchMove(second, center + new Vector(80, 0));
                window.TouchEnd(second, center + new Vector(80, 0));
                window.TouchEnd(first, center - new Vector(80, 0));
            }
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

    [Theory]
    [InlineData(494)]
    [InlineData(720)]
    public Task BidirectionalArchitecture_StacksLayersWithoutOverlaps(double width) => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram(OwnershipFlow, width);
        try
        {
            var edges = FlowEdges(canvas);
            Assert.Equal(8, edges.Count);
            Assert.All(edges, edge => Assert.True(edge.Bidirectional));

            var nodes = NodeRects(canvas);
            AssertNoOverlaps(nodes);
            var groups = Field<IEnumerable>(canvas, "_fSubgraphs").Cast<object>()
                .ToDictionary(g => PublicField<string>(g, "Id"), g => PublicField<Rect>(g, "Box"));
            Assert.True(groups["Runtime"].Bottom <= groups["Lumi"].Top, $"{groups["Runtime"]} / {groups["Lumi"]}");
            foreach (var id in new[] { "MCP", "EXT", "CP" }) Assert.True(groups["Runtime"].Contains(nodes[id]), id);
            foreach (var id in new[] { "MA", "CA", "STATE", "UI", "VIEW" }) Assert.True(groups["Lumi"].Contains(nodes[id]), id);

            // Mermaid's ranks: the two sources side by side above the runtime, both adapters above the shared state.
            Assert.Equal(nodes["MCP"].Center.Y, nodes["EXT"].Center.Y, 3);
            Assert.True(nodes["CP"].Top > nodes["MCP"].Bottom);
            Assert.Equal(nodes["MA"].Center.Y, nodes["CA"].Center.Y, 3);
            Assert.True(nodes["STATE"].Top > nodes["MA"].Bottom);
            Assert.True(nodes["UI"].Top > nodes["STATE"].Bottom);
            Assert.True(nodes["VIEW"].Top > nodes["UI"].Bottom);

            AssertRoutesConnectOnlyTheirNodes(edges, nodes);
            foreach (var group in Field<IEnumerable>(canvas, "_fSubgraphs").Cast<object>())
            {
                var title = PublicField<Rect>(group, "TitleRect");
                Assert.True(PublicField<Rect>(group, "Box").Contains(title));
                Assert.DoesNotContain(edges, edge => RouteHits(edge.Route, title));
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(494)]
    [InlineData(720)]
    public Task LabelledLinks_KeepEveryLabelOnceAndClearOfNodesAndOtherLabels(double width) => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram(LabelledLinksFlow, width);
        try
        {
            var edges = FlowEdges(canvas);
            Assert.Equal(new[] { "R->P", "R->U", "P->U" }, edges.Select(e => $"{e.From}->{e.To}"));
            Assert.Equal(new[] { true, false, true }, edges.Select(e => e.Bidirectional));

            var nodes = NodeRects(canvas);
            AssertNoOverlaps(nodes);
            AssertRoutesConnectOnlyTheirNodes(edges, nodes);
            var labels = edges.Select(LabelRect).ToList();
            foreach (var label in labels)
                Assert.DoesNotContain(nodes, node => node.Value.Intersects(label));
            for (var i = 0; i < labels.Count; i++)
                for (var j = i + 1; j < labels.Count; j++)
                    Assert.False(labels[i].Intersects(labels[j]), $"{labels[i]} overlaps {labels[j]}");

            Assert.True(nodes["R"].Right < nodes["P"].Left && nodes["P"].Right < nodes["U"].Left);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Cycle_BreaksAtTheLinkThatClosesTheLoop() => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram("""
            flowchart LR
                A[Request] --> B{Valid?}
                B -->|Yes| C[Process]
                B -->|No| D[Reject]
                C --> E[Respond]
                D --> E
                C -->|retry| A
            """, 720);
        try
        {
            var nodes = NodeRects(canvas);
            Assert.True(nodes["A"].Right < nodes["B"].Left && nodes["B"].Right < nodes["C"].Left);
            Assert.True(nodes["C"].Right < nodes["E"].Left);
            var retry = FlowEdges(canvas).Single(e => e.From == "C" && e.To == "A");
            Assert.True(retry.Route[^1].X <= nodes["A"].Right + 0.5, "retry must end on Request");
            AssertRoutesConnectOnlyTheirNodes(FlowEdges(canvas), nodes);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InlineLabelsAndLongArrows_DoNotBecomeNodes() => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram("""
            flowchart TD
                A[Start] -- yes --> B[Do it]
                A -- no --> C[Skip]
                B ---> D[Finish]
                C ---> D
                D -. later .-> A
            """, 494);
        try
        {
            Assert.Equal(new[] { "A", "B", "C", "D" }, NodeRects(canvas).Keys.Order());
            Assert.Equal(new[] { "A->B:yes", "A->C:no", "B->D:", "C->D:", "D->A:later" },
                FlowEdges(canvas).Select(e => $"{e.From}->{e.To}:{e.Label}"));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("flowchart TB\n    Hub --> A & B & C & D")]
    [InlineData("flowchart TB\n    Client --> GW[Gateway]\n    GW --> Users & Orders & Payments & Search & Notify")]
    [InlineData("flowchart TB\n    A & B & C & D & E & F --> Sink")]
    [InlineData("flowchart TD\n    Router{Intent?} --> Chat & Search & Code & Files & Fallback")]
    public Task FanOutAndFanIn_AttachEveryLinkToItsNodes(string source) => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram(source, 720);
        try
        {
            AssertRoutesConnectOnlyTheirNodes(FlowEdges(canvas), NodeRects(canvas));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OpenLinkChains_KeepEveryNode() => fixture.Dispatch(() =>
    {
        // "--" inside "---" must not read as the start of an inline label.
        var (window, _, canvas) = CreateDiagram("""
            flowchart LR
                A --- B --- C
                E -- note --- F
            """, 494);
        try
        {
            Assert.Equal(new[] { "A", "B", "C", "E", "F" }, NodeRects(canvas).Keys.Order());
            Assert.Equal(new[] { "A->B:", "B->C:", "E->F:note" },
                FlowEdges(canvas).Select(e => $"{e.From}->{e.To}:{e.Label}"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LayoutExtent_CoversEveryRouteAndLabel() => fixture.Dispatch(() =>
    {
        var (window, _, canvas) = CreateDiagram("""
            flowchart TD
                User([User]) --> Router{Intent?}
                Router -->|chat| Chat[Chat handler]
                Router -->|search| Search[Search service]
                Router -->|code| Code[Coding agent]
                Router -->|files| Files[File manager]
                Router -->|other| Fallback[Fallback reply]
                Chat & Search & Code & Files & Fallback --> Reply[Compose reply]
                Reply --> User
            """, 720);
        try
        {
            var edges = FlowEdges(canvas);
            var nodeExtent = NodeRects(canvas).Values.Aggregate((a, b) => a.Union(b));
            Assert.Contains(edges.SelectMany(e => e.Route), point => !nodeExtent.Contains(point));

            var extent = new Rect(0, 0, Field<double>(canvas, "_layW"), Field<double>(canvas, "_layH")).Inflate(0.01);
            foreach (var edge in edges)
            {
                Assert.All(edge.Route, point => Assert.True(extent.Contains(point), $"{edge.From} -> {edge.To} leaves the layout at {point}"));
                if (edge.DisplayLabel is not null)
                    Assert.True(extent.Contains(LabelRect(edge)), $"The label of {edge.From} -> {edge.To} leaves the layout");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DenseFlow_LaysOutEveryLinkWithoutOverlappingNodes() => fixture.Dispatch(() =>
    {
        // A tangle dense enough to pack channels shoulder to shoulder; it once threw from inside Measure.
        var random = new Random(7);
        var source = new System.Text.StringBuilder("flowchart LR\n");
        for (var i = 0; i < 150; i++)
            source.Append($"    A{random.Next(80)} -->{(random.Next(5) == 0 ? "|step|" : "")} A{random.Next(80)}\n");
        var (window, _, canvas) = CreateDiagram(source.ToString(), 720);
        try
        {
            AssertNoOverlaps(NodeRects(canvas));
            Assert.All(FlowEdges(canvas).Where(e => e.From != e.To), edge => Assert.True(edge.Route.Count >= 2));
        }
        finally { window.Close(); }
    });

    private sealed record FlowEdge(string From, string To, string? Label, string? DisplayLabel, bool Bidirectional,
        List<Point> Route, Point LabelAt);

    private static List<FlowEdge> FlowEdges(Control canvas) =>
        Field<IEnumerable>(canvas, "_fEdges").Cast<object>().Select(e => new FlowEdge(
            PublicField<string>(e, "From"), PublicField<string>(e, "To"),
            (string?)e.GetType().GetField("Label")!.GetValue(e), (string?)e.GetType().GetField("DisplayLabel")!.GetValue(e),
            PublicField<bool>(e, "Bidirectional"), PublicField<List<Point>>(e, "Route"), PublicField<Point>(e, "LabelAt"))).ToList();

    private static Dictionary<string, Rect> NodeRects(Control canvas)
    {
        var rects = new Dictionary<string, Rect>();
        foreach (DictionaryEntry entry in Field<IDictionary>(canvas, "_fNodes"))
        {
            var node = entry.Value!;
            rects[(string)entry.Key] = new Rect(PublicField<double>(node, "X"), PublicField<double>(node, "Y"),
                PublicField<double>(node, "W"), PublicField<double>(node, "H"));
        }
        return rects;
    }

    private static Rect LabelRect(FlowEdge edge)
    {
        var text = new Avalonia.Media.FormattedText(edge.DisplayLabel!, System.Globalization.CultureInfo.CurrentUICulture,
            Avalonia.Media.FlowDirection.LeftToRight, new Avalonia.Media.Typeface(Avalonia.Media.FontFamily.Default), 10,
            Avalonia.Media.Brushes.White);
        return new Rect(edge.LabelAt.X - text.Width / 2 - 6, edge.LabelAt.Y - text.Height / 2 - 2, text.Width + 12, text.Height + 4);
    }

    private static void AssertNoOverlaps(Dictionary<string, Rect> nodes)
    {
        var list = nodes.ToList();
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                Assert.False(list[i].Value.Deflate(0.5).Intersects(list[j].Value.Deflate(0.5)),
                    $"{list[i].Key} {list[i].Value} overlaps {list[j].Key} {list[j].Value}");
    }

    // Every route starts on its own From node, ends on its own To node, runs orthogonally and passes no other node.
    private static void AssertRoutesConnectOnlyTheirNodes(List<FlowEdge> edges, Dictionary<string, Rect> nodes)
    {
        foreach (var edge in edges)
        {
            Assert.True(edge.Route.Count >= 2, $"{edge.From} -> {edge.To} has no route");
            Assert.True(nodes[edge.From].Inflate(1.5).Contains(edge.Route[0]),
                $"{edge.From} -> {edge.To} starts beside {edge.From} {nodes[edge.From]} at {edge.Route[0]}");
            Assert.True(nodes[edge.To].Inflate(1.5).Contains(edge.Route[^1]),
                $"{edge.From} -> {edge.To} ends beside {edge.To} {nodes[edge.To]} at {edge.Route[^1]}");
            for (var i = 1; i < edge.Route.Count; i++)
            {
                var (start, end) = (edge.Route[i - 1], edge.Route[i]);
                Assert.True(Math.Abs(start.X - end.X) < 0.01 || Math.Abs(start.Y - end.Y) < 0.01,
                    $"{edge.From} -> {edge.To} is not orthogonal: {start} -> {end}");
                foreach (var (id, rect) in nodes)
                    if (id != edge.From && id != edge.To)
                        Assert.False(Crosses(start, end, rect.Deflate(0.1)), $"{edge.From} -> {edge.To} crosses {id}: {start} -> {end}");
            }
        }
    }

    private static bool RouteHits(List<Point> route, Rect rect)
    {
        for (var i = 1; i < route.Count; i++)
        {
            var (a, b) = (route[i - 1], route[i]);
            if (Math.Max(a.X, b.X) >= rect.Left && Math.Min(a.X, b.X) <= rect.Right &&
                Math.Max(a.Y, b.Y) >= rect.Top && Math.Min(a.Y, b.Y) <= rect.Bottom)
                return true;
        }
        return false;
    }

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
