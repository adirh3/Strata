using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class EdgeDragGestureRecognizerTests
{
    private readonly AvaloniaFixture _fixture;

    public EdgeDragGestureRecognizerTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AdditionalTouchPressAndRelease_DoNotCancelOwningTouch()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            var deltas = new List<double>();
            var endedCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, e) => deltas.Add(e.Delta));
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, _) => endedCount++);

            var owner = CreatePointer(PointerType.Touch, isPrimary: true);
            var additional = CreatePointer(PointerType.Touch, isPrimary: false);

            Press(recognizer, target, owner, new Point(20, 40), 1_000);
            Press(recognizer, target, additional, new Point(30, 40), 1_001);
            Release(recognizer, target, additional, new Point(30, 40), 1_002);

            Move(recognizer, target, owner, new Point(50, 40), 1_020);
            Release(recognizer, target, owner, new Point(50, 40), 1_030);
            owner.Capture(null);

            var nextOwner = CreatePointer(PointerType.Touch, isPrimary: true);
            Press(recognizer, target, nextOwner, new Point(20, 40), 2_000);
            Move(recognizer, target, nextOwner, new Point(45, 40), 2_020);
            Release(recognizer, target, nextOwner, new Point(45, 40), 2_030);
            nextOwner.Capture(null);

            Assert.Equal([20d, 15d], deltas);
            Assert.Equal(2, endedCount);

            window.Close();
        });
    }

    [Fact]
    public async Task CaptureLost_CleansUpOwnerAndAllowsAnotherTouch()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            var endedCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, _) => endedCount++);

            var first = CreatePointer(PointerType.Touch, isPrimary: true);
            Press(recognizer, target, first, new Point(20, 40), 1_000);
            Move(recognizer, target, first, new Point(50, 40), 1_020);
            CaptureLost(recognizer, first);
            first.Capture(null);

            var second = CreatePointer(PointerType.Touch, isPrimary: true);
            Press(recognizer, target, second, new Point(20, 40), 2_000);
            Move(recognizer, target, second, new Point(45, 40), 2_020);
            Release(recognizer, target, second, new Point(45, 40), 2_030);
            second.Capture(null);

            Assert.Equal(2, endedCount);

            window.Close();
        });
    }

    [Fact]
    public async Task MousePointer_RemainsIgnoredWithoutBlockingTouch()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            var dragCount = 0;
            var endedCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, _) => dragCount++);
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, _) => endedCount++);

            var mouse = CreatePointer(PointerType.Mouse, isPrimary: true);
            Press(recognizer, target, mouse, new Point(20, 40), 1_000);
            Move(recognizer, target, mouse, new Point(50, 40), 1_020);
            Release(recognizer, target, mouse, new Point(50, 40), 1_030);

            var touch = CreatePointer(PointerType.Touch, isPrimary: true);
            Press(recognizer, target, touch, new Point(20, 40), 2_000);
            Move(recognizer, target, touch, new Point(45, 40), 2_020);
            Release(recognizer, target, touch, new Point(45, 40), 2_030);
            touch.Capture(null);

            Assert.Equal(1, dragCount);
            Assert.Equal(1, endedCount);

            window.Close();
        });
    }

    [Fact]
    public async Task RightToLeftDragStartsAtRightEdgeAndNormalizesOpeningDelta()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            recognizer.IsRightToLeft = true;
            var deltas = new List<double>();
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, e) => deltas.Add(e.Delta));
            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, pointer, new Point(280, 40), 1_000);
            Move(recognizer, target, pointer, new Point(250, 40), 1_020);
            Release(recognizer, target, pointer, new Point(250, 40), 1_030);
            pointer.Capture(null);

            Assert.Equal([20d], deltas);
            window.Close();
        });
    }

    [Fact]
    public async Task ClosedDrawerCanClaimAnOpeningSwipeFromTheMiddleWhenEnabled()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            recognizer.CanOpenFromAnywhere = true;
            var deltas = new List<double>();
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, e) => deltas.Add(e.Delta));
            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, pointer, new Point(180, 40), 1_000);
            Move(recognizer, target, pointer, new Point(220, 43), 1_020);
            Release(recognizer, target, pointer, new Point(220, 43), 1_030);

            Assert.Single(deltas);
            Assert.True(deltas[0] > 0);
            window.Close();
        });
    }

    [Fact]
    public async Task FullSurfaceOpeningSwipeStillYieldsToVerticalScrollIntent()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            recognizer.CanOpenFromAnywhere = true;
            var dragCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, _) => dragCount++);
            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, pointer, new Point(180, 40), 1_000);
            Move(recognizer, target, pointer, new Point(184, 80), 1_020);
            Release(recognizer, target, pointer, new Point(184, 80), 1_030);

            Assert.Equal(0, dragCount);
            window.Close();
        });
    }

    [Fact]
    public async Task ReleasePositionContributesFinalDeltaAndVelocity()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            var deltas = new List<double>();
            double velocity = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, e) => deltas.Add(e.Delta));
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, e) => velocity = e.Velocity);
            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, pointer, new Point(20, 40), 1_000);
            Move(recognizer, target, pointer, new Point(35, 40), 1_100);
            Release(recognizer, target, pointer, new Point(80, 40), 1_120);
            pointer.Capture(null);

            Assert.Equal([5d, 45d], deltas);
            Assert.True(velocity > 2_000);
            window.Close();
        });
    }

    [Fact]
    public async Task CancelClearsActiveOwnerAndAllowsNextTouch()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            var endedCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, _) => endedCount++);
            var first = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, first, new Point(20, 40), 1_000);
            Move(recognizer, target, first, new Point(50, 40), 1_020);
            recognizer.Cancel();
            Release(recognizer, target, first, new Point(50, 40), 1_030);

            var second = CreatePointer(PointerType.Touch, isPrimary: true);
            Press(recognizer, target, second, new Point(20, 40), 2_000);
            Move(recognizer, target, second, new Point(45, 40), 2_020);
            Release(recognizer, target, second, new Point(45, 40), 2_030);

            Assert.Equal(1, endedCount);
            Assert.Null(first.Captured);
            window.Close();
        });
    }

    [Fact]
    public void DefaultThresholdClaimsEdgeIntentBeforeOrdinaryScroll()
    {
        Assert.Equal(4, new EdgeDragGestureRecognizer().Threshold);
    }

    [Fact]
    public void FullSurfaceThresholdClaimsHorizontalIntentBeforeOrdinaryScroll()
    {
        Assert.Equal(6, new EdgeDragGestureRecognizer().AnywhereThreshold);
    }

    [Fact]
    public async Task FullSurfaceHorizontalSwipeClaimsBeforeNestedScrollViewer()
    {
        await _fixture.Dispatch(() =>
        {
            var scrollContent = new Border
            {
                Width = 300,
                Height = 1200
            };
            var recognizer = new EdgeDragGestureRecognizer
            {
                CanOpenFromAnywhere = true
            };
            var scrollPresenter = new ScrollContentPresenter
            {
                Width = 300,
                Height = 200,
                CanVerticallyScroll = true,
                Content = scrollContent
            };
            var target = new Border
            {
                Width = 300,
                Height = 200,
                Child = scrollPresenter
            };
            target.GestureRecognizers.Add(recognizer);
            var dragCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, _) => dragCount++);
            var window = new Window
            {
                Width = 300,
                Height = 200,
                Content = target
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);
            RaisePress(scrollContent, target, pointer, new Point(150, 100), 1_000);
            RaiseMove(scrollContent, target, pointer, new Point(157, 102), 1_016);

            Assert.Equal(1, dragCount);

            RaiseRelease(scrollContent, target, pointer, new Point(157, 102), 1_032);
            pointer.Capture(null);
            window.Close();
        });
    }

    [Fact]
    public async Task FullSurfaceOpeningSwipeYieldsToOffsetHorizontalScrollViewer()
    {
        await _fixture.Dispatch(() =>
        {
            var scrollContent = new Border
            {
                MinWidth = 900,
                Height = 200
            };
            var scrollPresenter = new ScrollContentPresenter
            {
                Width = 300,
                Height = 200,
                CanHorizontallyScroll = true,
                Content = scrollContent
            };
            var recognizer = new EdgeDragGestureRecognizer
            {
                CanOpenFromAnywhere = true
            };
            var target = new Border
            {
                Width = 300,
                Height = 200,
                Child = scrollPresenter
            };
            target.GestureRecognizers.Add(recognizer);
            var dragCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, _) => dragCount++);
            var window = new Window
            {
                Width = 300,
                Height = 200,
                Content = target
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            scrollPresenter.Offset = new Vector(120, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(
                scrollPresenter.Extent.Width > scrollPresenter.Viewport.Width,
                $"Extent={scrollPresenter.Extent}, Viewport={scrollPresenter.Viewport}, Content={scrollContent.Bounds}");
            Assert.True(scrollPresenter.Offset.X > 0);

            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);
            RaisePress(scrollContent, target, pointer, new Point(150, 100), 1_000);
            RaiseMove(scrollContent, target, pointer, new Point(157, 102), 1_016);

            Assert.Equal(0, dragCount);
            Assert.Null(pointer.Captured);

            RaiseRelease(scrollContent, target, pointer, new Point(157, 102), 1_032);
            window.Close();
        });
    }

    [Theory]
    [InlineData(false, Avalonia.Media.FlowDirection.LeftToRight, 0, false)]
    [InlineData(false, Avalonia.Media.FlowDirection.LeftToRight, 120, true)]
    [InlineData(false, Avalonia.Media.FlowDirection.RightToLeft, 0, true)]
    [InlineData(false, Avalonia.Media.FlowDirection.RightToLeft, 600, false)]
    [InlineData(true, Avalonia.Media.FlowDirection.LeftToRight, 0, true)]
    [InlineData(true, Avalonia.Media.FlowDirection.LeftToRight, 600, false)]
    [InlineData(true, Avalonia.Media.FlowDirection.RightToLeft, 0, false)]
    [InlineData(true, Avalonia.Media.FlowDirection.RightToLeft, 120, true)]
    public void HorizontalScrollBoundaryUsesTheNestedPresentersFlowDirection(
        bool drawerIsRightToLeft,
        Avalonia.Media.FlowDirection scrollFlowDirection,
        double offset,
        bool expected)
    {
        var actual = EdgeDragGestureRecognizer.CanHorizontalScrollConsumeOpeningGesture(
            new Vector(offset, 0),
            new Size(900, 200),
            new Size(300, 200),
            scrollFlowDirection,
            drawerIsRightToLeft);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task FullSurfaceSwipeYieldsToFocusedEditableTextBox()
    {
        await _fixture.Dispatch(() =>
        {
            var recognizer = new EdgeDragGestureRecognizer
            {
                CanOpenFromAnywhere = true
            };
            var editor = new TextBox
            {
                Text = "Move this caret",
                Width = 260
            };
            var target = new Border
            {
                Width = 300,
                Height = 120,
                Child = editor
            };
            target.GestureRecognizers.Add(recognizer);
            var dragCount = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEvent, (_, _) => dragCount++);
            var window = new Window
            {
                Width = 300,
                Height = 120,
                Content = target
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.Focus());

            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);
            RaisePress(editor, target, pointer, new Point(120, 50), 1_000);
            RaiseMove(editor, target, pointer, new Point(140, 50), 1_020);
            RaiseRelease(editor, target, pointer, new Point(140, 50), 1_040);

            Assert.Equal(0, dragCount);
            Assert.Null(pointer.Captured);
            window.Close();
        });
    }

    [Fact]
    public async Task StationaryReleasePreservesLastMeaningfulFlingVelocity()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            double velocity = 0;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, e) => velocity = e.Velocity);
            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, pointer, new Point(20, 40), 1_000);
            Move(recognizer, target, pointer, new Point(70, 40), 1_020);
            Release(recognizer, target, pointer, new Point(70, 40), 1_030);

            Assert.True(velocity > 1_000);
            window.Close();
        });
    }

    [Fact]
    public async Task FlingVelocityExpiresAfterAStationaryHold()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, target, recognizer) = CreateTarget();
            double velocity = double.NaN;
            target.AddHandler(EdgeDragGestureRecognizer.EdgeDragEndedEvent, (_, e) => velocity = e.Velocity);
            var pointer = CreatePointer(PointerType.Touch, isPrimary: true);

            Press(recognizer, target, pointer, new Point(20, 40), 1_000);
            Move(recognizer, target, pointer, new Point(70, 40), 1_020);
            Release(recognizer, target, pointer, new Point(70, 40), 2_020);

            Assert.Equal(0, velocity);
            window.Close();
        });
    }

    [Fact]
    public async Task ImmediateCloseInvalidatesAnOlderOpenSettle()
    {
        await _fixture.Dispatch(() =>
        {
            var drawer = new StrataNavigationDrawer();
            drawer.IsOpen = true;
            drawer.IsOpen = false;

            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(350);
            while (DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Assert.False(drawer.IsOpen);
            Assert.Equal(0, drawer.Progress, 3);
        });
    }

    private static (Window Window, Border Target, EdgeDragGestureRecognizer Recognizer) CreateTarget()
    {
        var recognizer = new EdgeDragGestureRecognizer
        {
            EdgeInset = 0,
            EdgeWidth = 100,
            Threshold = 10,
        };
        var target = new Border
        {
            Width = 300,
            Height = 200,
        };
        target.GestureRecognizers.Add(recognizer);

        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = target,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, target, recognizer);
    }

    private static Avalonia.Input.Pointer CreatePointer(PointerType pointerType, bool isPrimary) =>
        new(Avalonia.Input.Pointer.GetNextFreeId(), pointerType, isPrimary);

    private static void Press(
        EdgeDragGestureRecognizer recognizer,
        Control target,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        InvokeRecognizer(recognizer, "PointerPressed", new PointerPressedEventArgs(
            target,
            pointer,
            target,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));

    private static void Move(
        EdgeDragGestureRecognizer recognizer,
        Control target,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        InvokeRecognizer(recognizer, "PointerMoved", new PointerEventArgs(
            InputElement.PointerMovedEvent,
            target,
            pointer,
            target,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));

    private static void Release(
        EdgeDragGestureRecognizer recognizer,
        Control target,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        InvokeRecognizer(recognizer, "PointerReleased", new PointerReleasedEventArgs(
            target,
            pointer,
            target,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));

    private static void CaptureLost(EdgeDragGestureRecognizer recognizer, IPointer pointer) =>
        InvokeRecognizer(recognizer, "PointerCaptureLost", pointer);

    private static void RaisePress(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        source.RaiseEvent(new PointerPressedEventArgs(
            source,
            pointer,
            root,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));

    private static void RaiseMove(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        source.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            source,
            pointer,
            root,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));

    private static void RaiseRelease(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        source.RaiseEvent(new PointerReleasedEventArgs(
            source,
            pointer,
            root,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));

    private static void InvokeRecognizer(
        EdgeDragGestureRecognizer recognizer,
        string methodName,
        object argument)
    {
        var method = typeof(EdgeDragGestureRecognizer).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(recognizer, [argument]);
    }
}
