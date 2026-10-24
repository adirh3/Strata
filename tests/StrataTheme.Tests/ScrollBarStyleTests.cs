using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Tests;

/// <summary>
/// Strata's scrollbar rests as a slim thumb; its track shows, and the thumb widens, only while the pointer is on it.
/// </summary>
[Collection("Avalonia UI")]
public sealed class ScrollBarStyleTests
{
    private readonly AvaloniaFixture _fixture;

    public ScrollBarStyleTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public async Task RestsAsASlimThumbAndOpensItsTrackWhilePointedAt(Orientation orientation)
    {
        await _fixture.Dispatch(() =>
        {
            var vertical = orientation == Orientation.Vertical;
            var bar = new ScrollBar
            {
                Orientation = orientation,
                Minimum = 0,
                Maximum = 1000,
                ViewportSize = 250,
                LargeChange = 250,
                Value = 300,
                HorizontalAlignment = vertical ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
            };
            var window = new Window { Width = 400, Height = 400, Content = new Panel { Children = { bar } } };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Show();
            try
            {
                Pump(window);
                object? Resource(string key) => bar.TryFindResource(key, bar.ActualThemeVariant, out var value) ? value : null;
                double Breadth(Rect bounds) => vertical ? bounds.Width : bounds.Height;
                var line = bar.GetVisualDescendants().OfType<Border>().Single(static border => border.Name == "PART_TrackLine");
                var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single(static thumb => thumb.Name == "thumb");
                var paint = thumb.GetVisualDescendants().OfType<Border>().Single();
                var widened = vertical ? new Thickness(1, 0) : new Thickness(0, 1);

                // The bar keeps its footprint; at rest only a 4px thumb shows on it.
                Assert.Equal(8, Breadth(bar.Bounds), 1);
                Assert.Equal(4, Breadth(paint.Bounds), 1);
                Assert.Equal(0, line.GetBaseValue(Visual.OpacityProperty).Value);
                Assert.Same(Resource("Brush.ScrollbarThumb"), thumb.GetBaseValue(TemplatedControl.BackgroundProperty).Value);

                // Pointed at, the hairline track comes in and the thumb widens and brightens.
                var track = bar.GetVisualDescendants().OfType<Track>().Single();
                var beforeThumb = vertical
                    ? new Point(track.Bounds.Width / 2, track.Bounds.Height - 10)
                    : new Point(track.Bounds.Width - 10, track.Bounds.Height / 2);
                var pageAt = track.TranslatePoint(beforeThumb, window)!.Value;
                window.MouseMove(pageAt);
                Dispatcher.UIThread.RunJobs();
                Assert.True(bar.IsPointerOver);
                Assert.Equal(1, line.GetBaseValue(Visual.OpacityProperty).Value);
                Assert.Equal(widened, thumb.GetBaseValue(TemplatedControl.PaddingProperty).Value);
                Assert.Same(Resource("Brush.ScrollbarThumbHover"), thumb.GetBaseValue(TemplatedControl.BackgroundProperty).Value);

                // The track still pages.
                window.MouseDown(pageAt, MouseButton.Left);
                window.MouseUp(pageAt, MouseButton.Left);
                Pump(window);
                Assert.Equal(550, bar.Value, 1);

                // Held, the thumb takes the accent and stays wide.
                var grip = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
                window.MouseMove(grip);
                window.MouseDown(grip, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(Resource("Brush.AccentDefault"), thumb.GetBaseValue(TemplatedControl.BackgroundProperty).Value);
                Assert.Equal(widened, thumb.GetBaseValue(TemplatedControl.PaddingProperty).Value);
                window.MouseUp(grip, MouseButton.Left);

                // Left, it rests again.
                window.MouseMove(new Point(4, 4));
                Dispatcher.UIThread.RunJobs();
                Assert.False(bar.IsPointerOver);
                Assert.Equal(0, line.GetBaseValue(Visual.OpacityProperty).Value);
                Assert.Equal(vertical ? new Thickness(2, 0) : new Thickness(0, 2), thumb.GetBaseValue(TemplatedControl.PaddingProperty).Value);
                Assert.Same(Resource("Brush.ScrollbarThumb"), thumb.GetBaseValue(TemplatedControl.BackgroundProperty).Value);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("en-US", "Page up", "Page down", "Page left", "Page right", "Position")]
    [InlineData("he-IL", "עמוד למעלה", "עמוד למטה", "עמוד שמאלה", "עמוד ימינה", "מיקום")]
    public async Task ScreenReaderNamesFollowTheUiLanguage(
        string language, string pageUp, string pageDown, string pageLeft, string pageRight, string position)
    {
        await _fixture.Dispatch(() =>
        {
            var original = CultureInfo.CurrentUICulture;
            var window = new Window { Width = 400, Height = 400 };
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var vertical = new ScrollBar { Orientation = Orientation.Vertical, Maximum = 100, ViewportSize = 50, HorizontalAlignment = HorizontalAlignment.Right };
                var horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Maximum = 100, ViewportSize = 50, VerticalAlignment = VerticalAlignment.Bottom };
                window.Content = new Panel { Children = { vertical, horizontal } };
                window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
                {
                    Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
                });
                window.Show();
                Pump(window);

                string? Name(ScrollBar bar, string part) => AutomationProperties.GetName(
                    bar.GetVisualDescendants().OfType<Control>().Single(control => control.Name == part));
                Assert.Equal(pageUp, Name(vertical, "PART_PageUpButton"));
                Assert.Equal(pageDown, Name(vertical, "PART_PageDownButton"));
                Assert.Equal(position, Name(vertical, "thumb"));
                Assert.Equal(pageLeft, Name(horizontal, "PART_PageUpButton"));
                Assert.Equal(pageRight, Name(horizontal, "PART_PageDownButton"));
                Assert.Equal(position, Name(horizontal, "thumb"));
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
                window.Close();
            }
        });
    }

    private static void Pump(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
