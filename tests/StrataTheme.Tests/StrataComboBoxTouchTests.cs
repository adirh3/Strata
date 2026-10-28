using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataComboBoxTouchTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task TouchSelectsOnTapNotOnPress() => fixture.Dispatch(() =>
    {
        var (window, combo, row) = OpenPicker();
        try
        {
            var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            var pointer = window.TouchBegin(point);
            Assert.Equal("Language 0", combo.SelectedItem);
            Assert.True(combo.IsDropDownOpen);

            window.TouchEnd(pointer, point);
            Assert.Equal(row.Item, combo.SelectedItem);
            Assert.False(combo.IsDropDownOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TouchScrollingDoesNotSelectAnOption() => fixture.Dispatch(() =>
    {
        var (window, combo, row) = OpenPicker();
        try
        {
            var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            var pointer = window.TouchBegin(point);
            window.TouchMove(pointer, point - new Vector(0, 20));
            window.TouchMove(pointer, point - new Vector(0, 55));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.TouchEnd(pointer, point - new Vector(0, 55));

            Assert.Equal("Language 0", combo.SelectedItem);
            Assert.True(combo.IsDropDownOpen);
            var scroll = window.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            Assert.True(scroll.Offset.Y > 0, $"Extent={scroll.Extent}; viewport={scroll.Viewport}; offset={scroll.Offset}");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MousePressKeepsExistingSelectionBehavior() => fixture.Dispatch(() =>
    {
        var (window, combo, row) = OpenPicker();
        try
        {
            var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            Assert.Equal(row.Item, combo.SelectedItem);
            Assert.False(combo.IsDropDownOpen);
        }
        finally { window.Close(); }
    });

    private static (Window Window, StrataComboBox Combo, StrataComboBoxItem Row) OpenPicker()
    {
        var combo = new StrataComboBox
        {
            ItemsSource = Enumerable.Range(0, 10).Select(index => $"Language {index}").ToArray(),
            SelectedItem = "Language 0",
            IsDropDownOpen = true
        };
        var rows = combo.ItemsSource!.Cast<string>().Select(item => new StrataComboBoxItem
        {
            Owner = combo, Item = item, Text = item, MinHeight = 48
        }).ToArray();
        var panel = new StackPanel();
        foreach (var row in rows)
            panel.Children.Add(row);
        var scroll = new ScrollContentPresenter
        {
            Content = panel, Height = 180, CanVerticallyScroll = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        scroll.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true });
        var window = StrataInteractiveBlockTests.Show(scroll, width: 360);
        foreach (var row in rows)
            row.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataComboBoxItem)));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, combo, rows[2]);
    }
}
