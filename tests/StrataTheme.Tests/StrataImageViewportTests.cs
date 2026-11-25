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
            var edge = start + new Vector(215, 0);
            window.TouchMove(touch, edge);
            Assert.Equal(0, viewport.ImageBounds.Left, 6);
            var back = edge - new Vector(12, 0);
            window.TouchMove(touch, back);
            Assert.Equal(-12, viewport.ImageBounds.Left, 6);
            window.TouchEnd(touch, back);
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
            Swipe(new Vector(-30, 0));
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

    private static (Window Window, StrataImageViewport Viewport) CreateViewport(double width = 400, double height = 400)
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
            Width = 320,
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
