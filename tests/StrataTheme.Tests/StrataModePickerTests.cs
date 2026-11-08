using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataModePickerTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task FooterSwitch_KeepsThePopoverOpen_WhileAFooterButtonClosesIt() => fixture.Dispatch(() =>
    {
        var settings = new Button { Content = "Settings", Classes = { "mode-picker-link" } };
        var toggle = new ToggleButton { Classes = { "mode-picker-switch" } };
        var picker = new StrataModePicker
        {
            Items = new[] { "Full access", "Ask first" },
            SelectedItem = "Full access",
            FooterContent = new StackPanel { Children = { settings, toggle } }
        };
        var window = Show(picker);
        try
        {
            picker.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(toggle.GetVisualAncestors(), ancestor => ancestor is ContentPresenter { Name: "PART_Footer" });

            // A switch changes something in place, so the popover stays to show its new state.
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.IsOpen);

            // A button takes the user elsewhere, so the popover closes behind it.
            settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(picker.IsOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HostFooterParts_WearThePopoversOwnLook() => fixture.Dispatch(() =>
    {
        var link = new Button { Content = "Manage", Classes = { "mode-picker-link" } };
        var toggle = new ToggleButton { Classes = { "mode-picker-switch" } };
        var tile = new Border { Classes = { "mode-picker-tile", "on" }, Child = new StrataLineIcon() };
        var picker = new StrataModePicker
        {
            Items = new[] { "Full access" },
            FooterContent = new StackPanel { Children = { link, toggle, tile } }
        };
        // Outside the popover none of this applies, so nothing else pays for these rules.
        var outside = new Button { Content = "Elsewhere", Classes = { "mode-picker-link" } };
        var window = Show(new StackPanel { Children = { picker, outside } });
        try
        {
            picker.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // Strata gives every TextBlock the body size; a link's text takes its button's quieter one.
            var text = Assert.Single(link.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(12, link.FontSize);
            Assert.Equal(link.FontSize, text.FontSize);
            Assert.Equal(link.Foreground, text.Foreground);
            Assert.NotEqual(12, outside.FontSize);

            Assert.Equal(30, toggle.Bounds.Width);
            Assert.Equal(18, toggle.Bounds.Height);
            Assert.Equal(30, tile.Bounds.Width);

            // An option that is on lights its tile's icon; off, the icon goes back to the modes' grey.
            var icon = (StrataLineIcon)tile.Child!;
            var lit = icon.Foreground;
            tile.Classes.Remove("on");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(lit, icon.Foreground);
        }
        finally { window.Close(); }
    });

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Width = 420,
            Height = 480,
            Template = new FuncControlTemplate<Window>((owner, scope) =>
            {
                var layers = new VisualLayerManager
                {
                    EnableOverlayLayer = true,
                    Child = new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner },
                    }.RegisterInNameScope(scope),
                };
                SetEnablePopupOverlayLayer(layers, true);
                return layers;
            }),
        };
        // The theme goes in before the content, so the picker finds its control theme when it attaches.
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = content;
        window.Show();
        window.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_EnablePopupOverlayLayer")]
    private static extern void SetEnablePopupOverlayLayer(VisualLayerManager layers, bool value);
}
