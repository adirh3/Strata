using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataImageViewportTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task RealTouchPinchesAreCumulativeAndRestartWithoutJumping() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var center = new Point(160, 240);
            Assert.Same(viewport, window.InputHitTest(center));
            Pinch();
            Assert.Equal(2, viewport.Zoom, 5);
            Assert.Equal(new Vector(-160, -240), viewport.Offset);
            Pinch();
            Assert.Equal(4, viewport.Zoom, 5);
            viewport.Fit();
            Assert.Equal(1, viewport.Zoom);
            Assert.Equal(default, viewport.Offset);

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
    public Task PinchTracksTheMovingMidpointAndEitherFingerCanKeepDragging() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            foreach (var liftFirst in new[] { true, false })
            {
                viewport.Fit();
                viewport.ZoomBy(2);
                var start = viewport.Offset;
                var midpoint = new Point(140, 220);
                var firstAt = midpoint - new Vector(40, 0);
                var secondAt = midpoint + new Vector(40, 0);
                var first = window.TouchBegin(firstAt);
                var second = window.TouchBegin(secondAt);

                var shift = new Vector(24, 18);
                firstAt += shift - new Vector(20, 0);
                secondAt += shift + new Vector(20, 0);
                window.TouchMove(first, firstAt);
                window.TouchMove(second, secondAt);
                Assert.Equal(3, viewport.Zoom, 6);
                var afterPinch = start * 1.5 - (Vector)midpoint * 0.5 + shift;
                Assert.Equal(afterPinch.X, viewport.Offset.X, 6);
                Assert.Equal(afterPinch.Y, viewport.Offset.Y, 6);

                window.TouchEnd(liftFirst ? first : second, liftFirst ? firstAt : secondAt);
                Assert.Equal(afterPinch.X, viewport.Offset.X, 6);
                Assert.Equal(afterPinch.Y, viewport.Offset.Y, 6);
                var remaining = liftFirst ? second : first;
                var destination = (liftFirst ? secondAt : firstAt) + new Vector(-22, 30);
                window.TouchMove(remaining, destination);
                window.TouchEnd(remaining, destination);
                Assert.Equal(3, viewport.Zoom, 6);
                Assert.Equal(afterPinch.X - 22, viewport.Offset.X, 6);
                Assert.Equal(afterPinch.Y + 30, viewport.Offset.Y, 6);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MovingBothFingersAtFitDoesNotChangeTheFinalZoomOrDismiss() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var dismissals = 0;
            viewport.DismissRequested += (_, _) => dismissals++;
            var firstAt = new Point(120, 110);
            var secondAt = new Point(120, 190);
            var first = window.TouchBegin(firstAt);
            var second = window.TouchBegin(secondAt);
            var shift = new Vector(0, 40);
            window.TouchMove(first, firstAt += shift);
            window.TouchMove(second, secondAt += shift);
            Assert.Equal(1, viewport.Zoom, 6);

            window.TouchEnd(first, firstAt);
            window.TouchMove(second, secondAt += new Vector(0, 140));
            window.TouchEnd(second, secondAt);
            Assert.Equal(1, viewport.Zoom, 6);
            Assert.Equal(default, viewport.Offset);
            Assert.Equal(0, dismissals);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TouchPanReversesImmediatelyAfterReachingAnImageEdge() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            viewport.ZoomBy(2);
            var start = new Point(70, 240);
            var touch = window.TouchBegin(start);
            var edge = start + new Vector(360, 0);
            window.TouchMove(touch, edge);
            Assert.Equal(viewport.Bounds.Width / 2, viewport.ImageBounds.Left, 6);
            var back = edge - new Vector(12, 0);
            window.TouchMove(touch, back);
            Assert.Equal(viewport.Bounds.Width / 2 - 12, viewport.ImageBounds.Left, 6);
            window.TouchEnd(touch, back);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AfterLiftingBothPinchFingersAnOversizedPhotoCanMovePastTheScreenEdge() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var center = new Point(160, 300);
            var first = window.TouchBegin(center - new Vector(20, 0));
            var second = window.TouchBegin(center + new Vector(20, 0));
            window.TouchMove(first, center - new Vector(70, 0));
            window.TouchMove(second, center + new Vector(70, 0));
            window.TouchEnd(first, center - new Vector(70, 0));
            window.TouchEnd(second, center + new Vector(70, 0));

            var before = viewport.ImageBounds;
            Assert.True(before.Height > viewport.Bounds.Height * 1.5);
            Assert.True(before.Bottom > viewport.Bounds.Height);
            AssertRenderedBottom(before.Bottom);

            DragUp(new Point(160, 410), 310);
            Assert.Equal(before.Bottom - 310, viewport.ImageBounds.Bottom, 6);
            Assert.True(viewport.ImageBounds.Bottom < viewport.Bounds.Height);
            AssertRenderedBottom(before.Bottom - 310);
            DragUp(new Point(160, 300), 190);
            Assert.Equal(viewport.Bounds.Height / 2, viewport.ImageBounds.Bottom, 6);
            AssertRenderedBottom(viewport.Bounds.Height / 2);

            void AssertRenderedBottom(double expected)
            {
                var image = Assert.IsType<Image>(viewport.Child);
                var bottom = image.TranslatePoint(new Point(image.Bounds.Width / 2, image.Bounds.Height), viewport);
                Assert.NotNull(bottom);
                Assert.Equal(expected, bottom.Value.Y, 6);
            }

            void DragUp(Point start, double distance)
            {
                var finger = window.TouchBegin(start);
                var destination = start - new Vector(0, distance);
                window.TouchMove(finger, destination);
                window.TouchEnd(finger, destination);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(200)]
    [InlineData(300)]
    public Task PinchingTheLowerHalfLeavesRoomToDragThePhotoUp(double imageHeight) => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport(width: 400, height: imageHeight);
        try
        {
            var before = viewport.ImageBounds;
            var center = new Point(160, 300);
            Assert.True(before.Contains(center));
            var first = window.TouchBegin(center - new Vector(40, 0));
            var second = window.TouchBegin(center + new Vector(40, 0));
            window.TouchMove(first, center - new Vector(80, 0));
            window.TouchMove(second, center + new Vector(80, 0));
            window.TouchEnd(second, center + new Vector(80, 0));
            window.TouchEnd(first, center - new Vector(80, 0));

            var anchoredTop = center.Y + (before.Top - center.Y) * 2;
            Assert.Equal(2, viewport.Zoom, 6);
            Assert.Equal(anchoredTop, viewport.ImageBounds.Top, 6);

            var finger = window.TouchBegin(center);
            var higher = center - new Vector(0, 54);
            window.TouchMove(finger, higher);
            window.TouchEnd(finger, higher);
            Assert.Equal(anchoredTop - 54, viewport.ImageBounds.Top, 6);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ZoomedImageCanBeDraggedAndStopsOnRelease() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            viewport.ZoomBy(2);
            var before = viewport.Offset;
            var start = new Point(160, 240);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
            Assert.Equal(before + new Vector(30, 20), viewport.Offset);
            window.MouseUp(start + new Vector(30, 20), MouseButton.Left);
            var stopped = viewport.Offset;
            window.MouseMove(start + new Vector(70, 60));
            Assert.Equal(stopped, viewport.Offset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task WideImageStaysCenteredOnTheAxisThatStillFits() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport(400, 100);
        try
        {
            viewport.ZoomBy(2);
            var centeredY = viewport.Offset.Y;
            var start = new Point(160, 240);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(1000, 1000), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(1000, 1000), MouseButton.Left);
            Assert.Equal(centeredY, viewport.Offset.Y);
            Assert.InRange(viewport.Offset.X, -320, 0);
            viewport.ZoomBy(1000);
            Assert.Equal(8, viewport.Zoom);
            viewport.ZoomBy(0.0001);
            Assert.Equal(1, viewport.Zoom);
            Assert.Equal(default, viewport.Offset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DoubleTapZoomsThenFitsAndLayoutChangesPreserveZoom() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var at = new Point(160, 240);
            Tap();
            Tap();
            Assert.Equal(2, viewport.Zoom);
            Tap();
            Tap();
            Assert.Equal(1, viewport.Zoom);
            viewport.ZoomBy(3);
            window.Width = 480;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, viewport.Zoom);

            void Tap()
            {
                var pointer = window.TouchBegin(at);
                window.TouchEnd(pointer, at);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SourceShowsTheImageAndANewSourceFitsAgain() => fixture.Dispatch(() =>
    {
        var viewport = new StrataImageViewport();
        Assert.Null(viewport.Name);
        var window = new Window { Width = 320, Height = 480, Content = viewport };
        window.Show();
        try
        {
            var first = Drawing(400, 400);
            viewport.Source = first;
            window.UpdateLayout();
            var image = Assert.IsType<Image>(viewport.Child);
            Assert.Same(first, image.Source);
            viewport.ZoomBy(3);
            Assert.Equal(3, viewport.Zoom);

            var second = Drawing(200, 100);
            viewport.Source = second;
            Assert.Same(second, image.Source);
            Assert.Equal(1, viewport.Zoom);
            Assert.Equal(default, viewport.Offset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LoadImageAsyncDecodesALocalImageFileAndRejectsOtherTargets() => fixture.Dispatch(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"strata-viewport-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQMcAAAAASUVORK5CYII="));

            using var bitmap = await StrataImageViewport.LoadImageAsync(path, new PixelSize(100, 100));
            Assert.InRange(bitmap.PixelSize.Width, 1, 100);
            Assert.Throws<ArgumentException>(() =>
            {
                _ = StrataImageViewport.LoadImageAsync("relative.png", new PixelSize(100, 100));
            });
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await File.WriteAllTextAsync(path, "not an image");
                using var _ = await StrataImageViewport.LoadImageAsync(path, new PixelSize(100, 100));
            });
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public Task TouchSwipesStepBetweenImagesOnlyWhenThereIsMoreThanOne() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var steps = new List<int>();
            viewport.NavigateRequested += (_, step) => steps.Add(step);
            var start = new Point(160, 240);

            Swipe(new Vector(-120, 6));
            Assert.Empty(steps);
            viewport.CanSwipeBetweenImages = true;
            Swipe(new Vector(-120, 6));
            viewport.Fit();
            Swipe(new Vector(120, -4));
            Assert.Equal([1, -1], steps);

            // A short drag is not a swipe; the image eases back.
            Swipe(new Vector(-18, 0));
            Assert.Equal(2, steps.Count);
            Assert.Equal(default, viewport.Offset);

            // Zoomed in, a drag pans the picture instead.
            viewport.ZoomBy(2);
            Swipe(new Vector(-120, 0));
            Assert.Equal(2, steps.Count);

            // Right to left, the line starts on the right: a swipe to the right brings the next picture.
            viewport.Fit();
            viewport.FlowDirection = FlowDirection.RightToLeft;
            window.UpdateLayout();
            Swipe(new Vector(120, 0));
            viewport.Fit();
            Swipe(new Vector(-120, 0));
            Assert.Equal([1, -1, 1, -1], steps);

            void Swipe(Vector travel)
            {
                var touch = window.TouchBegin(start);
                window.TouchMove(touch, start + travel / 2);
                window.TouchMove(touch, start + travel);
                window.TouchEnd(touch, start + travel);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(412, 52, 280, true)]
    [InlineData(984, 60, 280, true)]
    [InlineData(412, 28, 50, true)]
    [InlineData(412, 28, 280, false)]
    [InlineData(412, 18, 30, false)]
    public Task GalleryAcceptsShortDragsAndFlicksWithoutTurningOnAccidentalNudges(
        double width, double distance, int durationMs, bool navigates) => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport(windowWidth: width);
        try
        {
            Assert.Equal(width, viewport.Bounds.Width);
            viewport.CanSwipeBetweenImages = true;
            var steps = new List<int>();
            viewport.NavigateRequested += (_, step) => steps.Add(step);

            var start = new Point(width / 2, 240);
            var end = start - new Vector(distance, 4);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true);
            viewport.RaiseEvent(new PointerPressedEventArgs(viewport, pointer, window, start, 1000,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            viewport.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, viewport, pointer, window, end,
                (ulong)(990 + durationMs), new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
            viewport.RaiseEvent(new PointerReleasedEventArgs(viewport, pointer, window, end, (ulong)(1000 + durationMs),
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

            Assert.Equal(navigates ? [1] : Array.Empty<int>(), steps);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task GalleryDrawsTheNextPageUnderTheGestureAndCommitsOnlyAfterRelease() => fixture.Dispatch(async () =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var next = Drawing(320, 320);
            viewport.SetGalleryNeighbors(previous: null, next: () => next);
            var steps = new List<int>();
            viewport.NavigateRequested += (_, step) => steps.Add(step);
            var start = new Point(160, 240);
            var end = start - new Vector(72, 0);
            var pointer = window.TouchBegin(start);
            window.TouchMove(pointer, end);

            window.UpdateLayout();
            var nextPage = viewport.GetVisualDescendants().OfType<Image>().Single(image => ReferenceEquals(next, image.Source));
            Assert.True(nextPage.IsEffectivelyVisible);
            Assert.InRange(nextPage.TranslatePoint(default, viewport)!.Value.X, 250, 280);
            Assert.Equal(320, nextPage.Bounds.Width);
            Assert.Empty(steps);
            window.TouchEnd(pointer, end);
            Assert.Empty(steps);

            for (var attempt = 0; attempt < 75 && steps.Count == 0; attempt++)
                await Task.Delay(20);
            Assert.Equal([1], steps);

            // Pulling toward the missing neighbor at the start of the strip cannot navigate.
            pointer = window.TouchBegin(start);
            window.TouchMove(pointer, start + new Vector(150, 0));
            window.TouchEnd(pointer, start + new Vector(150, 0));
            await Task.Delay(250);
            Assert.Equal([1], steps);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ASwipeStartedDuringAPageTurnStillReachesTheFollowingPhoto() => fixture.Dispatch(async () =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var next = Drawing(320, 320);
            viewport.SetGalleryNeighbors(previous: null, next: () => next);
            var steps = new List<int>();
            viewport.NavigateRequested += (_, step) => steps.Add(step);
            var start = new Point(160, 240);
            var end = start - new Vector(72, 0);

            var first = window.TouchBegin(start);
            window.TouchMove(first, end);
            window.TouchEnd(first, end);
            Assert.Empty(steps);

            var second = window.TouchBegin(start);
            window.TouchMove(second, end);
            window.TouchEnd(second, end);

            for (var attempt = 0; attempt < 75 && steps.Count < 2; attempt++)
                await Task.Delay(20);
            Assert.Equal([1, 1], steps);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task GalleryReattachesWithoutAnOffsetOrPendingNavigation() => fixture.Dispatch(async () =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var next = Drawing(320, 320);
            viewport.SetGalleryNeighbors(previous: null, next: () => next);
            var steps = new List<int>();
            viewport.NavigateRequested += (_, step) => steps.Add(step);
            var current = viewport.GetVisualDescendants().OfType<Image>().Single(image => !ReferenceEquals(image.Source, next)
                && image.Source is not null);
            var rest = current.TranslatePoint(default, viewport)!.Value;
            var start = new Point(160, 240);
            var end = start - new Vector(72, 0);
            var finger = window.TouchBegin(start);
            window.TouchMove(finger, end);
            window.TouchEnd(finger, end);
            Assert.Empty(steps);

            var panel = Assert.IsType<ContentPresenter>(viewport.GetVisualParent());
            window.Content = null;
            panel.UpdateChild();
            Assert.False(viewport.IsAttachedToVisualTree());
            window.Content = viewport;
            panel.UpdateChild();
            window.UpdateLayout();
            Assert.True(viewport.IsAttachedToVisualTree());

            Assert.Equal(rest, current.TranslatePoint(default, viewport)!.Value);
            await Task.Delay(250);
            Assert.Empty(steps);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SwipingUpOrDownDismissesAndReportsHowFarItWent() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            var dismissed = 0;
            var progress = new List<double>();
            viewport.DismissRequested += (_, _) => dismissed++;
            viewport.DismissProgress += (_, fraction) => progress.Add(fraction);
            var start = new Point(160, 200);

            var touch = window.TouchBegin(start);
            window.TouchMove(touch, start + new Vector(0, 60));
            window.TouchMove(touch, start + new Vector(0, 200));
            Assert.InRange(progress[^1], 0.5, 1);
            window.TouchEnd(touch, start + new Vector(0, 200));
            Assert.Equal(1, dismissed);

            // A mouse drag never dismisses: on a desktop the picture stays until it is closed.
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(0, 200), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(0, 200), MouseButton.Left);
            Assert.Equal(1, dismissed);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ASidewaysSwipeDuringAPullsReturnRestoresTheBackdrop() => fixture.Dispatch(() =>
    {
        var (window, viewport) = CreateViewport();
        try
        {
            viewport.CanSwipeBetweenImages = true;
            var progress = new List<double>();
            viewport.DismissProgress += (_, fraction) => progress.Add(fraction);
            var start = new Point(160, 240);

            // A short pull that does not dismiss starts easing back...
            var pull = window.TouchBegin(start);
            window.TouchMove(pull, start + new Vector(0, 35));
            window.TouchEnd(pull, start + new Vector(0, 35));
            Assert.True(progress[^1] > 0);

            // ...and a sideways swipe interrupts it.
            var swipe = window.TouchBegin(start);
            window.TouchMove(swipe, start + new Vector(-60, 0));
            window.TouchMove(swipe, start + new Vector(-120, 0));
            window.TouchEnd(swipe, start + new Vector(-120, 0));
            Assert.Equal(0, progress[^1]);
        }
        finally { window.Close(); }
    });

    private static DrawingImage Drawing(double width, double height) => new()
    {
        Drawing = new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = new RectangleGeometry(new Rect(0, 0, width, height))
        }
    };

    private static (Window Window, StrataImageViewport Viewport) CreateViewport(
        double width = 400, double height = 400, double windowWidth = 320)
    {
        var viewport = new StrataImageViewport(new Image
        {
            Stretch = Stretch.Uniform,
            Source = new DrawingImage
            {
                Drawing = new GeometryDrawing
                {
                    Brush = Brushes.White,
                    Geometry = new RectangleGeometry(new Rect(0, 0, width, height))
                }
            }
        });
        var window = new Window
        {
            Width = windowWidth,
            Height = 480,
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner }
                }.RegisterInNameScope(scope)
            }),
            Content = viewport
        };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (window, viewport);
    }
}
