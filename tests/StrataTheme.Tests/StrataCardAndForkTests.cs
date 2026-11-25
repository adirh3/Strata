using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataCardAndForkTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task Card_ReadsAsItsSummary_AndReadMoreOpensTheRest() => fixture.Dispatch(() =>
    {
        var card = new StrataCard { Header = "Weather", Summary = "Sunny, 22°C", Detail = Lines(5) };
        var window = Show(card, 420);
        try
        {
            var detail = Find<Control>(card, "PART_Detail");
            var more = Find<Button>(card, "PART_MoreButton");
            var label = Find<Control>(card, "PART_MoreLabel");
            var readMore = Find<TextBlock>(card, "PART_ReadMoreLabel");
            var showLess = Find<TextBlock>(card, "PART_ShowLessLabel");
            Assert.True(more.IsEffectivelyVisible);
            Assert.False(detail.IsVisible);
            Assert.Equal(InteractiveLabels.Current["cardReadMore"], AutomationProperties.GetName(more));
            Assert.Equal(1, readMore.Opacity);
            Assert.Equal(readMore.DesiredSize.Width, label.Bounds.Width, 1);

            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.True(card.IsExpanded);
            Assert.True(detail.IsEffectivelyVisible && detail.Bounds.Height > 40);
            Assert.True(Find<ContentPresenter>(card, "PART_SummaryPresenter").IsEffectivelyVisible,
                "The open card keeps the summary above the detail.");
            Assert.Equal(InteractiveLabels.Current["cardShowLess"], AutomationProperties.GetName(more));
            Assert.Equal(0, readMore.Opacity);
            Assert.Equal(1, showLess.Opacity);
            Assert.Equal(showLess.DesiredSize.Width, label.Bounds.Width, 1);
            Assert.True(more.TranslatePoint(default, card)!.Value.Y >= detail.TranslatePoint(new Point(0, detail.Bounds.Height), card)!.Value.Y,
                "Show less follows the detail.");

            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.False(card.IsExpanded);
            Assert.False(detail.IsVisible);
            Assert.Equal(1, readMore.Opacity);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Card_ReadMoreKeepsItsContrastWhenPointedAt_InHighContrast() => fixture.Dispatch(() =>
    {
        var card = new StrataCard { Header = "Weather", Summary = "Sunny, 22°C", Detail = Lines(3) };
        var window = Show(card, 420);
        try
        {
            window.RequestedThemeVariant = StrataThemeVariants.HighContrast;
            var header = Find<Border>(card, "PART_Header");
            window.MouseMove(header.TranslatePoint(new Point(20, 10), window)!.Value, RawInputModifiers.None);
            Settle(window);
            Assert.Contains(":pointerover", card.Classes);

            static Avalonia.Media.Color ColorOf(Avalonia.Media.IBrush? brush) =>
                Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(brush).Color;
            Assert.NotEqual(ColorOf(Find<TextBlock>(card, "PART_ReadMoreLabel").Foreground),
                ColorOf(Find<Border>(card, "PART_MoreSurface").Background));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Card_WithoutDetail_HasNothingToOpen() => fixture.Dispatch(() =>
    {
        var card = new StrataCard { Header = "Done", Summary = "All checks passed" };
        var window = Show(card, 420);
        try
        {
            Assert.False(Find<Button>(card, "PART_MoreButton").IsVisible);
            Tap(window, Find<Border>(card, "PART_Header"));
            card.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.False(card.IsExpanded);
            Assert.False(Find<Control>(card, "PART_Detail").IsVisible);
        }
        finally { window.Close(); }
    });

    private static TextBlock Lines(int count) =>
        new() { Text = string.Join("\n", Enumerable.Range(1, count).Select(i => $"Detail line {i}")) };

    [Fact]
    public Task Fork_ShowsBothOptionsWhenWide() => fixture.Dispatch(() =>
    {
        var fork = NewFork();
        var window = Show(fork, 640);
        try
        {
            Assert.True(fork.IsSideBySide);
            var a = Find<ContentPresenter>(fork, "PART_OptionAContent");
            var b = Find<ContentPresenter>(fork, "PART_OptionBContent");
            Assert.True(a.IsVisible && b.IsVisible);
            Assert.True(b.Bounds.X > a.Bounds.Right, "Option B sits beside option A, not over it.");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Fork_WhenNarrow_ShowsOneOptionAndSlidesTheThumbToTheChosenOne() => fixture.Dispatch(() =>
    {
        var fork = NewFork();
        var window = Show(fork, 360);
        try
        {
            Assert.False(fork.IsSideBySide);
            var a = Find<ContentPresenter>(fork, "PART_OptionAContent");
            var b = Find<ContentPresenter>(fork, "PART_OptionBContent");
            Assert.True(a.IsVisible);
            Assert.False(b.IsVisible);

            Find<Button>(fork, "PART_OptionB").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.Equal(1, fork.SelectedIndex);
            Assert.False(a.IsVisible);
            Assert.True(b.IsVisible);

            var indicator = Find<Border>(fork, "PART_Indicator");
            var thumbX = indicator.TranslatePoint(new Point(indicator.Bounds.Width / 2, 0), fork)!.Value.X;
            Assert.True(thumbX > fork.Bounds.Width / 2, "The thumb sits over the chosen option.");
        }
        finally { window.Close(); }
    });

    private static StrataFork NewFork() => new()
    {
        OptionATitle = "LG C4",
        OptionBTitle = "Samsung S90D",
        OptionAContent = new TextBlock { Text = "OLED evo panel" },
        OptionBContent = new TextBlock { Text = "QD-OLED panel" },
    };

    private static Window Show(Control control, double width)
    {
        var window = new Window { Width = width + 32, Height = 600 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = new StackPanel { Margin = new Thickness(16), Children = { control } };
        window.Show();
        Pump(window);
        return window;
    }

    private static void Tap(Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(8, 8), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    // Headless motion follows the real clock, so real time passes while the timer is ticked.
    private static void Settle(Window window)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 400)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }
        Pump(window);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static T Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
