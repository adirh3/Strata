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
