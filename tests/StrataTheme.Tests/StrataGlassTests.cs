using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>Opt-in glass surfaces: frost only where a host asks for it.</summary>
[Collection("Avalonia UI")]
public sealed class StrataGlassTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataGlassTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GlassButtonFrostsBeforeItsSurfaceAndMatchesItsGeometry()
    {
        await _fixture.Dispatch(() =>
        {
            var window = CreateWindow();
            try
            {
                Assert.True(window.TryFindResource("StrataGlassButton", window.ActualThemeVariant, out var resource));
                var button = new Button
                {
                    Theme = Assert.IsType<ControlTheme>(resource),
                    CornerRadius = new CornerRadius(22),
                    Width = 120,
                    Height = 44,
                    Content = new TextBlock { Text = "Glass" }
                };
                window.Content = button;
                Pump(window);

                var surface = Part<Border>(button, "PART_Surface");
                var frost = Part<StrataBackdropBlur>(button, "PART_Backdrop");
                Assert.True(frost.IsVisible);
                Assert.Same(surface.GetVisualParent(), frost.GetVisualParent());
                Assert.Equal(button.CornerRadius, frost.CornerRadius);
                var layers = Assert.IsAssignableFrom<Panel>(frost.GetVisualParent());
                Assert.True(layers.Children.IndexOf(frost) < layers.Children.IndexOf(surface));
                Assert.Contains(surface.GetVisualDescendants(), child => child is ContentPresenter);
                Assert.Equal(surface.Bounds, frost.Bounds);

                surface.Margin = new Thickness(2);
                surface.RenderTransform = new ScaleTransform(0.98, 0.98);
                Pump(window);
                Assert.Equal(surface.Margin, frost.Margin);
                Assert.Equal(surface.Bounds, frost.Bounds);
                Assert.Equal(surface.CornerRadius, frost.CornerRadius);
                Assert.Same(surface.RenderTransform, frost.RenderTransform);
                Assert.Equal(surface.RenderTransformOrigin, frost.RenderTransformOrigin);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task PlainButtonsAndTheDefaultComposerCarryNoVisibleFrost()
    {
        await _fixture.Dispatch(() =>
        {
            var window = CreateWindow();
            try
            {
                var button = new Button { Content = "Plain" };
                var composer = new StrataChatComposer();
                window.Content = new StackPanel { Children = { button, composer } };
                Pump(window);

                Assert.Empty(button.GetVisualDescendants().OfType<StrataBackdropBlur>());
                var frost = Part<StrataBackdropBlur>(composer, "PART_Backdrop");
                Assert.False(frost.IsVisible);
                Assert.Equal(Part<Border>(composer, "PART_Root").CornerRadius, frost.CornerRadius);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SheetGlassIsOptInAndFrostsOnlyBeneathTheSheetSurface()
    {
        await _fixture.Dispatch(async () =>
        {
            var window = CreateWindow();
            try
            {
                window.Styles.Add(new Style(selector => selector.OfType<StrataBottomSheet>().Class("glass")
                    .Template().OfType<StrataBackdropBlur>().Name("PART_Backdrop"))
                {
                    Setters = { new Setter(Visual.IsVisibleProperty, true) }
                });
                var sheet = new StrataBottomSheet
                {
                    IsOpen = true,
                    Title = "Model",
                    Content = new Border { Height = 120 }
                };
                window.Content = sheet;
                Pump(window);
                await Task.Delay(260);
                Pump(window);

                var surface = Part<Border>(sheet, "PART_Sheet");
                var motion = Part<Border>(sheet, "PART_SheetMotion");
                var frost = Part<StrataBackdropBlur>(sheet, "PART_Backdrop");
                Assert.False(frost.IsVisible);
                Assert.Contains(motion, frost.GetVisualAncestors());
                var layers = Assert.IsAssignableFrom<Panel>(frost.GetVisualParent());
                Assert.Same(layers, surface.GetVisualParent());
                Assert.True(layers.Children.IndexOf(frost) < layers.Children.IndexOf(surface));

                sheet.Classes.Add("glass");
                Pump(window);

                Assert.True(frost.IsEffectivelyVisible);
                Assert.Equal(surface.Bounds, frost.Bounds);
                Assert.Equal(surface.CornerRadius, frost.CornerRadius);
                Assert.True(frost.Bounds.Height < sheet.Bounds.Height / 2,
                    $"The frost must cover the sheet ({frost.Bounds}), not the window ({sheet.Bounds}).");

                sheet.Close();
                await Task.Delay(StrataBottomSheet.CloseDuration + TimeSpan.FromMilliseconds(60));
                Pump(window);
                Assert.False(frost.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateWindow()
    {
        var window = new Window { Width = 400, Height = 400 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Show();
        return window;
    }

    private static T Part<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Pump(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
