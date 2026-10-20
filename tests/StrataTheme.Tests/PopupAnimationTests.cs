using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class PopupAnimationTests
{
    private readonly AvaloniaFixture _fixture;

    public PopupAnimationTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public Task CustomOpeningDurationIsScopedToOnePopup() =>
        _fixture.Dispatch(() =>
        {
            var popup = new Popup();
            var other = new Popup();
            PopupAnimationHelper.SetOpenAnimationDuration(popup, TimeSpan.FromMilliseconds(180));

            Assert.Equal(TimeSpan.FromMilliseconds(180), PopupAnimationHelper.GetOpenAnimationDuration(popup));
            Assert.Equal(TimeSpan.FromMilliseconds(220), PopupAnimationHelper.GetOpenAnimationDuration(other));
            Assert.Equal(TimeSpan.FromMilliseconds(150), PopupAnimationHelper.CloseDuration);
            Assert.ThrowsAny<ArgumentException>(() =>
                PopupAnimationHelper.SetOpenAnimationDuration(popup, TimeSpan.Zero));
            Assert.ThrowsAny<ArgumentException>(() =>
                PopupAnimationHelper.SetOpenAnimationDuration(popup, TimeSpan.FromMilliseconds(-1)));
        });

    [Fact]
    public Task ChangingOpeningDurationDoesNotReplayTheEntrance() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, _) = CreatePopup(true);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                PopupAnimationHelper.SetOpenAnimationDuration(popup, TimeSpan.FromMilliseconds(180));
                RenderFrame(window);
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FirstOpenResolvesTheOriginAfterThePopupIsRendered(bool overlay) =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, target, child) = CreatePopup(overlay);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                var visual = ElementComposition.GetElementVisual(child);
                Assert.NotNull(visual);
                var center = child.PointToClient(target.PointToScreen(
                    new Point(target.Bounds.Width / 2, target.Bounds.Height / 2)));
                Assert.Equal(new Vector3D(center.X, center.Y, 0), visual.CenterPoint);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task LateContentLayoutStillPlaysThePendingEntrance() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, child) = CreatePopup(true);
            try
            {
                child.Height = 0;
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                Assert.Equal(0, PopupAnimationHelper.GetOpenAnimationCount(popup));
                Assert.Equal(0, child.Opacity);

                child.Height = 140;
                window.UpdateLayout();
                RenderFrame(window);
                RenderFrame(window);
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
                Assert.Equal(1, child.Opacity);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingKeepsThePopupAttachedUntilItsExitFinishes(bool overlay) =>
        _fixture.Dispatch(async () =>
        {
            var (window, popup, _, child) = CreatePopup(overlay);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                await Task.Delay(300);
                popup.Close();
                Assert.True(child.IsAttachedToVisualTree());
                Assert.False(child.IsHitTestVisible);
                Assert.False(child.IsEnabled);
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                Assert.False(child.IsAttachedToVisualTree());
                Assert.False(popup.IsOpen);
                Assert.True(child.IsHitTestVisible);
                Assert.True(child.IsEnabled);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task ClosingAndReopeningBeforeRenderDoesNotReplayQueuedEntrances() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, child) = CreatePopup(true);
            try
            {
                popup.Open();
                popup.Close();
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
                Assert.Equal(1, child.Opacity);
                Assert.True(popup.IsOpen);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task ReopeningDuringExitCancelsTheOldCloseAndKeepsTheSameHost() =>
        _fixture.Dispatch(async () =>
        {
            var (window, popup, _, child) = CreatePopup(true);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                var parent = child.GetVisualParent();
                popup.Close();
                RenderFrame(window);
                popup.IsOpen = true;
                RenderFrame(window);
                RenderFrame(window);
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                Assert.True(popup.IsOpen);
                Assert.True(child.IsAttachedToVisualTree());
                Assert.True(child.IsHitTestVisible);
                Assert.Same(parent, child.GetVisualParent());
                Assert.Equal(2, PopupAnimationHelper.GetOpenAnimationCount(popup));
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task PresenterStylesAndPopupPropertiesShareOneEntranceAcrossLayoutChanges() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, child) = CreatePopup(true);
            try
            {
                PopupAnimationHelper.SetEnableOverlayAnimation(popup, true);
                OverlayAnimationHelper.SetAnimateOpen(child, true);
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                PopupAnimationHelper.SetEnableOverlayAnimation(popup, false);
                PopupAnimationHelper.SetEnableOverlayAnimation(popup, true);
                OverlayAnimationHelper.SetAnimateOpen(child, false);
                OverlayAnimationHelper.SetAnimateOpen(child, true);
                child.Width = 200;
                window.UpdateLayout();
                RenderFrame(window);
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
            }
            finally
            {
                OverlayAnimationHelper.SetAnimateOpen(child, false);
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                PopupAnimationHelper.SetEnableOverlayAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task DisablingMotionCancelsAPendingEntranceAndRestoresOpacity() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, child) = CreatePopup(true);
            try
            {
                child.Opacity = 0.75;
                popup.Open();
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                RenderFrame(window);
                RenderFrame(window);
                Assert.Equal(0.75, child.Opacity);
                Assert.Equal(0, PopupAnimationHelper.GetOpenAnimationCount(popup));
                popup.Close();
                Assert.False(child.IsAttachedToVisualTree());
            }
            finally
            {
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task HidingThePlacementTargetReleasesThePopupWithoutWaitingForAnExit() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, target, child) = CreatePopup(true);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                target.IsVisible = false;
                Assert.False(popup.IsOpen);
                Assert.False(child.IsAttachedToVisualTree());
                Assert.True(child.IsHitTestVisible);
                Assert.Equal(1, child.Opacity);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task DetachingThePlacementTargetReleasesTheHostBeforeItsNextLayout(bool overlay, bool closing) =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, target, child) = CreatePopup(overlay);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                if (closing)
                    popup.Close();
                Assert.True(child.IsAttachedToVisualTree());

                Assert.IsType<Panel>(window.Content).Children.Remove(target);
                Assert.False(target.IsAttachedToVisualTree());
                child.Width += 20;
                window.UpdateLayout();
                RenderFrame(window);

                Assert.False(popup.IsOpen);
                Assert.False(child.IsAttachedToVisualTree());
                Assert.True(child.IsHitTestVisible);
                Assert.True(child.IsEnabled);
                Assert.Equal(1, child.Opacity);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task AnOwnerClosingDuringTargetDetachmentDoesNotDelayReleasingTheHost() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, target, child) = CreatePopup(true);
            target.DetachedFromVisualTree += (_, _) =>
            {
                popup.Close();
                popup.PlacementTarget = null;
            };
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);

                Assert.IsType<Panel>(window.Content).Children.Remove(target);
                child.Width += 20;
                window.UpdateLayout();
                RenderFrame(window);

                Assert.False(popup.IsOpen);
                Assert.False(child.IsAttachedToVisualTree());
                Assert.True(child.IsHitTestVisible);
                Assert.True(child.IsEnabled);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task ReopeningAtAnotherPlacementTargetObservesOnlyTheCurrentAnchor() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, target, child) = CreatePopup(true);
            var panel = Assert.IsType<Panel>(window.Content);
            var replacement = new Border { Width = 80, Height = 40 };
            panel.Children.Add(replacement);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                popup.Close();
                popup.PlacementTarget = replacement;
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                panel.Children.Remove(target);
                child.Width += 20;
                window.UpdateLayout();
                RenderFrame(window);

                Assert.True(popup.IsOpen);
                Assert.True(child.IsAttachedToVisualTree());
                Assert.Equal(2, PopupAnimationHelper.GetOpenAnimationCount(popup));

                panel.Children.Remove(replacement);
                child.Width += 20;
                window.UpdateLayout();
                RenderFrame(window);

                Assert.False(popup.IsOpen);
                Assert.False(child.IsAttachedToVisualTree());
                Assert.True(child.IsHitTestVisible);
                Assert.True(child.IsEnabled);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task TheThemeEnablesMotionForPopupsWithoutControlSpecificFlags() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, child) = CreatePopup(true, expansion: false, theme: true);
            try
            {
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                Assert.True(PopupAnimationHelper.GetEnableOverlayAnimation(popup));
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
                popup.Close();
                Assert.True(child.IsAttachedToVisualTree());
            }
            finally
            {
                PopupAnimationHelper.SetEnableOverlayAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task PresenterOnlyMotionAlsoAnimatesClosingAndCanBeDisabled() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, _, child) = CreatePopup(true, expansion: false);
            try
            {
                OverlayAnimationHelper.SetAnimateOpen(child, true);
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
                popup.Close();
                Assert.True(child.IsAttachedToVisualTree());
                OverlayAnimationHelper.SetAnimateOpen(child, false);
                Assert.False(child.IsAttachedToVisualTree());
                Assert.True(child.IsHitTestVisible);
            }
            finally
            {
                OverlayAnimationHelper.SetAnimateOpen(child, false);
                popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task InheritedScalingUsesTheActualPlacementTargetCenter() =>
        _fixture.Dispatch(() =>
        {
            var (window, popup, target, child) = CreatePopup(true);
            try
            {
                target.RenderTransform = new ScaleTransform(1.5, 1.5);
                popup.InheritsTransform = true;
                RenderFrame(window);
                popup.Open();
                RenderFrame(window);
                RenderFrame(window);
                var center = child.PointToClient(target.PointToScreen(
                    new Point(target.Bounds.Width / 2, target.Bounds.Height / 2)));
                Assert.Equal(new Vector3D(center.X, center.Y, 0),
                    ElementComposition.GetElementVisual(child)!.CenterPoint);
            }
            finally
            {
                PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                popup.Close();
                window.Close();
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComboBoxOwnersPreserveSelectionAndAnimateBothDirections(bool strata) =>
        _fixture.Dispatch(async () =>
        {
            Control combo = strata
                ? new StrataComboBox { ItemsSource = new[] { "First", "Second", "Third" } }
                : new ComboBox { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 1 };
            var window = CreateWindow(combo, theme: true);
            Popup? popup = null;
            try
            {
                combo.Theme = Assert.IsType<ControlTheme>(window.FindResource(combo.GetType()));
                ((TemplatedControl)combo).ApplyTemplate();
                window.UpdateLayout();
                if (combo is StrataComboBox custom)
                    custom.IsDropDownOpen = true;
                else
                    ((ComboBox)combo).IsDropDownOpen = true;
                RenderFrame(window);
                RenderFrame(window);
                popup = combo.GetVisualDescendants().OfType<Popup>()
                    .Single(control => control.Name == "PART_Popup");
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
                var child = popup.Child!;
                if (combo is StrataComboBox closingCustom)
                    closingCustom.IsDropDownOpen = false;
                else
                    ((ComboBox)combo).IsDropDownOpen = false;
                Assert.True(child.IsAttachedToVisualTree());
                await Task.Delay(300);
                Assert.False(child.IsAttachedToVisualTree());
                if (combo is ComboBox standard)
                    Assert.Equal(1, standard.SelectedIndex);
            }
            finally
            {
                if (popup is not null)
                {
                    PopupAnimationHelper.SetEnableOpenAnimation(popup, false);
                    PopupAnimationHelper.SetEnableOverlayAnimation(popup, false);
                    popup.Close();
                }
                window.Close();
            }
        });

    [Fact]
    public Task MovingAReopeningFlyoutToAnotherTargetReplacesTheOldHost() =>
        _fixture.Dispatch(() =>
        {
            var flyout = new Flyout { Content = new Border { Width = 120, Height = 80 } };
            var first = new Button { Content = "First target" };
            var second = new Button { Content = "Second target" };
            var window = CreateWindow(new StackPanel { Children = { first, second } }, theme: true);
            try
            {
                flyout.ShowAt(first);
                RenderFrame(window);
                RenderFrame(window);
                var child = flyout.Popup.Child!;
                var oldParent = child.GetVisualParent();
                flyout.Hide();
                flyout.ShowAt(second);
                RenderFrame(window);
                RenderFrame(window);
                Assert.True(flyout.IsOpen);
                Assert.True(child.IsAttachedToVisualTree());
                Assert.NotSame(oldParent, child.GetVisualParent());
                var center = child.PointToClient(second.PointToScreen(
                    new Point(second.Bounds.Width / 2, second.Bounds.Height / 2)));
                Assert.Equal(new Vector3D(Math.Clamp(center.X, 0, child.Bounds.Width),
                        Math.Clamp(center.Y, 0, child.Bounds.Height), 0),
                    ElementComposition.GetElementVisual(child)!.CenterPoint);
                Assert.Equal(2, PopupAnimationHelper.GetOpenAnimationCount(flyout.Popup));
            }
            finally
            {
                PopupAnimationHelper.SetEnableOverlayAnimation(flyout.Popup, false);
                if (flyout.Popup.Child is { } child)
                    OverlayAnimationHelper.SetAnimateOpen(child, false);
                flyout.Hide();
                flyout.Popup.Close();
                window.Close();
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FlyoutOwnersKeepTheirHostForExitAndCanReopenImmediately(bool menu) =>
        _fixture.Dispatch(async () =>
        {
            PopupFlyoutBase flyout = menu
                ? new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Action" } } }
                : new Flyout { Content = new Border { Width = 120, Height = 80 } };
            var target = new Button { Content = "Open", Flyout = flyout };
            var window = CreateWindow(target, theme: true);
            try
            {
                flyout.ShowAt(target);
                RenderFrame(window);
                RenderFrame(window);
                var child = flyout.Popup.Child!;
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(flyout.Popup));
                flyout.Hide();
                Assert.False(flyout.IsOpen);
                Assert.True(child.IsAttachedToVisualTree());
                flyout.ShowAt(target);
                RenderFrame(window);
                RenderFrame(window);
                await Task.Delay(300);
                Assert.True(flyout.IsOpen);
                Assert.True(child.IsAttachedToVisualTree());
                Assert.Equal(2, PopupAnimationHelper.GetOpenAnimationCount(flyout.Popup));
                flyout.Hide();
                await Task.Delay(300);
                Assert.False(child.IsAttachedToVisualTree());
            }
            finally
            {
                PopupAnimationHelper.SetEnableOverlayAnimation(flyout.Popup, false);
                if (flyout.Popup.Child is { } child)
                    OverlayAnimationHelper.SetAnimateOpen(child, false);
                flyout.Hide();
                flyout.Popup.Close();
                window.Close();
            }
        });

    [Fact]
    public Task ToolTipDismissalNeverRendersWithoutItsPlacementAndThemeContext() =>
        _fixture.Dispatch(async () =>
        {
            var target = new Button
            {
                Content = "Tooltip target",
                Margin = new Thickness(80, 60, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var tip = new ToolTip { Content = "Tooltip content" };
            ToolTip.SetTip(target, tip);
            ToolTip.SetShouldUseOverlayLayer(target, true);
            ToolTip.SetPlacement(target, PlacementMode.Top);
            ToolTip.SetVerticalOffset(target, -6);
            var window = CreateWindow(target, theme: true);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            try
            {
                ToolTip.SetIsOpen(target, true);
                RenderFrame(window);
                RenderFrame(window);
                Assert.True(ToolTip.GetIsOpen(target));
                var popup = tip.FindLogicalAncestorOfType<Popup>();
                Assert.NotNull(popup);
                Assert.True(popup.IsUsingOverlayLayer);
                Assert.Equal(1, PopupAnimationHelper.GetOpenAnimationCount(popup));
                Assert.Equal(ThemeVariant.Dark, tip.ActualThemeVariant);
                var foreground = Assert.IsAssignableFrom<IBrush>(tip.Foreground);
                Assert.Equal(PlacementMode.Top, popup.Placement);
                Assert.Equal(-6, popup.VerticalOffset);
                Assert.True(tip.Bounds.Width > 0);

                ToolTip.SetIsOpen(target, false);
                Assert.False(ToolTip.GetIsOpen(target));
                Assert.False(tip.IsAttachedToVisualTree());
                Assert.False(popup.IsOpen);
                RenderFrame(window);

                ToolTip.SetIsOpen(target, true);
                RenderFrame(window);
                RenderFrame(window);
                await Task.Delay(300);
                Assert.True(tip.IsAttachedToVisualTree());
                Assert.True(popup.IsOpen);
                Assert.Equal(ThemeVariant.Dark, tip.ActualThemeVariant);
                Assert.Equal(foreground, tip.Foreground);
                Assert.Equal(PlacementMode.Top, popup.Placement);
                Assert.Equal(-6, popup.VerticalOffset);
                Assert.Equal(2, PopupAnimationHelper.GetOpenAnimationCount(popup));

                ToolTip.SetIsOpen(target, false);
                Assert.False(tip.IsAttachedToVisualTree());
            }
            finally
            {
                ToolTip.SetIsOpen(target, false);
                window.Close();
            }
        });

    [Fact]
    public Task ToolTipOverItsTargetLetsThePointerThrough() =>
        _fixture.Dispatch(() =>
        {
            // A plain filled Border: hit-testable without a control template (the test app has no theme).
            var target = new Border
            {
                Background = Brushes.SlateGray,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var tip = new ToolTip { Content = "Tooltip content" };
            ToolTip.SetTip(target, tip);
            // Centred on its own target, the way pointer placement lands a tip under the cursor near an edge.
            ToolTip.SetPlacement(target, PlacementMode.Center);
            var window = CreateWindow(target, theme: true);
            try
            {
                ToolTip.SetIsOpen(target, true);
                RenderFrame(window);
                RenderFrame(window);
                Assert.True(tip.IsAttachedToVisualTree());
                var middle = tip.TranslatePoint(new Point(tip.Bounds.Width / 2, tip.Bounds.Height / 2), window);
                Assert.NotNull(middle);

                Assert.False(tip.IsHitTestVisible);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.True(IsWithin(window.InputHitTest(middle.Value), target),
                    "The click under an open tooltip should reach the control beneath it.");

                // Control: were the tip hit-testable, it would take that click.
                tip.IsHitTestVisible = true;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.True(IsWithin(window.InputHitTest(middle.Value), tip));
            }
            finally
            {
                ToolTip.SetIsOpen(target, false);
                window.Close();
            }

            static bool IsWithin(IInputElement? hit, Visual ancestor) =>
                hit is Visual visual && (visual == ancestor || visual.GetVisualAncestors().Contains(ancestor));
        });

    [Fact]
    public Task ContextMenuOwnerKeepsItsContentAttachedForExit() =>
        _fixture.Dispatch(async () =>
        {
            var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Action" } } };
            var target = new Button { Content = "Menu target", ContextMenu = menu };
            var window = CreateWindow(target, theme: true);
            try
            {
                menu.Open(target);
                RenderFrame(window);
                RenderFrame(window);
                menu.Close();
                Assert.True(menu.IsAttachedToVisualTree());
                await Task.Delay(300);
                Assert.False(menu.IsAttachedToVisualTree());
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        });

    [Fact]
    public Task ClosingAParentMenuDoesNotLeaveItsSubmenuOpen() =>
        _fixture.Dispatch(async () =>
        {
            var submenu = new MenuItem
            {
                Header = "More",
                ItemsSource = new[] { new MenuItem { Header = "Nested action" } },
            };
            var flyout = new MenuFlyout { ItemsSource = new[] { submenu } };
            var target = new Button { Content = "Menu target", Flyout = flyout };
            var window = CreateWindow(target, theme: true);
            try
            {
                flyout.ShowAt(target);
                RenderFrame(window);
                submenu.ApplyTemplate();
                submenu.IsSubMenuOpen = true;
                RenderFrame(window);
                RenderFrame(window);
                var popup = submenu.GetVisualDescendants().OfType<Popup>().Single();
                var child = popup.Child!;
                Assert.True(child.IsAttachedToVisualTree());
                flyout.Hide();
                await Task.Delay(300);
                Assert.False(child.IsAttachedToVisualTree());
                Assert.False(popup.IsOpen);
            }
            finally
            {
                flyout.Hide();
                window.Close();
            }
        });

    private static (Window Window, Popup Popup, Border Target, Border Child) CreatePopup(
        bool overlay, bool expansion = true, bool theme = false)
    {
        var target = new Border
        {
            Width = 80, Height = 40, Margin = new Thickness(80, 60, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Gray,
        };
        var child = new Border { Width = 180, Height = 140, Background = Brushes.White };
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Center,
            ShouldUseOverlayLayer = overlay,
            Child = child,
        };
        PopupAnimationHelper.SetEnableOpenAnimation(popup, expansion);
        var window = CreateWindow(new Panel { Children = { target, popup } }, theme);
        Assert.True(target.IsAttachedToVisualTree());
        return (window, popup, target, child);
    }

    private static Window CreateWindow(Control content, bool theme)
    {
        var window = new Window
        {
            Width = 400, Height = 300,
            Content = content,
            Template = new FuncControlTemplate<Window>((owner, scope) =>
            {
                var layers = new VisualLayerManager
                {
                    EnableOverlayLayer = true,
                    Child = new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.ContentProperty] =
                            new Binding(nameof(Window.Content)) { Source = owner },
                    }.RegisterInNameScope(scope),
                };
                SetEnablePopupOverlayLayer(layers, true);
                return layers;
            }),
        };
        if (theme)
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml"),
            });
        }
        window.Show();
        window.ApplyTemplate();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        window.UpdateLayout();
        RenderFrame(window);
        return window;
    }

    private static void RenderFrame(Window window)
    {
        using var frame = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_EnablePopupOverlayLayer")]
    private static extern void SetEnablePopupOverlayLayer(VisualLayerManager layers, bool value);
}
