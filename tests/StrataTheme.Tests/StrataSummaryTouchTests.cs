using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataSummaryTouchTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SummaryWaitsForATapAndDoesNotExpandDuringAScrollGesture(bool turnSummary) =>
        fixture.Dispatch(() =>
        {
            Control summary = turnSummary
                ? new StrataTurnSummary { Label = "Finished work", Content = "Details" }
                : new StrataCard { Header = "Summary", Summary = "A short summary", Detail = "Details" };
            var window = new Window { Width = 360, Height = 700 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = new StackPanel { Margin = new Thickness(16, 100), Children = { summary } };
            try
            {
                window.Show();
                summary.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();
                var target = turnSummary
                    ? summary.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Header")
                    : summary;
                var point = target.TranslatePoint(new Point(25, 12), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                Assert.False(IsExpanded());
                window.MouseMove(point + new Point(0, 50), RawInputModifiers.LeftMouseButton);
                window.MouseUp(point + new Point(0, 50), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.False(IsExpanded());

                window.MouseDown(point, MouseButton.Left);
                Assert.False(IsExpanded());
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.True(IsExpanded());

                summary.Focus();
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Assert.False(IsExpanded());
            }
            finally { window.Close(); }

            bool IsExpanded() => summary is StrataCard card ? card.IsExpanded : ((StrataTurnSummary)summary).IsExpanded;
        });
}
