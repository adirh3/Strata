using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataComposerChipTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task DesktopChip_EndsAtItsName_AndWidensToShowItsRemoveButtonWhilePointedAt() => fixture.Dispatch(async () =>
    {
        var (window, composer) = CreateComposer();
        try
        {
            var project = Part<Border>(composer, "PART_ProjectChip");
            var skill = SkillChip(composer);
            var name = Part<TextBlock>(composer, "PART_ProjectName");
            var projectAtRest = project.Bounds;
            var skillAtRest = skill.Bounds;

            // At rest nothing is kept for the button: the chip ends with its padding after the name
            // (6 px apart, 4 px of padding and the 1 px border), and the button past its edge can't be hit.
            // Each row keeps the button's width free, so a chip can widen where it is.
            Assert.Equal(18, Part<ComposerChipsPanel>(composer, "PART_ChipsRow").RevealWidth);
            Assert.Equal(0, Slot(project).Bounds.Width);
            Assert.Equal(0, RemoveButton(project).Opacity);
            var nameEnd = name.TranslatePoint(new Point(name.Bounds.Width, 0), project)!.Value.X;
            Assert.Equal(11, project.Bounds.Width - nameEnd, 1);
            Assert.False(Hits(window, RemoveButton(project)));

            // Pointed at, it widens by the button and shows it; the chip beside it moves aside on its row.
            window.MouseMove(project.TranslatePoint(new Point(8, project.Bounds.Height / 2), window)!.Value);
            await SettleAsync();
            Assert.Equal(18, Slot(project).Bounds.Width, 1);
            Assert.Equal(projectAtRest.Width + 18, project.Bounds.Width, 1);
            Assert.Equal(1, RemoveButton(project).Opacity);
            Assert.True(Hits(window, RemoveButton(project)));
            Assert.Equal(skillAtRest.X + 18, skill.Bounds.X, 1);
            Assert.Equal(skillAtRest.Y, skill.Bounds.Y);

            // Left, it closes again.
            window.MouseMove(new Point(4, window.Bounds.Height - 4));
            await SettleAsync();
            Assert.Equal(projectAtRest, project.Bounds);
            Assert.Equal(skillAtRest, skill.Bounds);
            Assert.Equal(0, RemoveButton(project).Opacity);

            // The keyboard reaching the button opens its chip too.
            Assert.True(RemoveButton(skill).Focus());
            await SettleAsync();
            Assert.Equal(skillAtRest.Width + 18, skill.Bounds.Width, 1);
            Assert.Equal(1, RemoveButton(skill).Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task MobileChip_KeepsItsRemoveButtonShowing() => fixture.Dispatch(() =>
    {
        var (window, composer) = CreateComposer("mobile");
        try
        {
            var project = Part<Border>(composer, "PART_ProjectChip");
            Assert.Equal(48, Slot(project).Bounds.Width, 1);
            Assert.Equal(1, RemoveButton(project).Opacity);
            Assert.Equal(0, Part<ComposerChipsPanel>(composer, "PART_ChipsRow").RevealWidth);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task DesktopChip_WithMotionOff_OpensAtOnce() => fixture.Dispatch(() =>
    {
        var (window, composer) = CreateComposer("motion-disabled");
        try
        {
            var project = Part<Border>(composer, "PART_ProjectChip");
            var atRest = project.Bounds.Width;
            window.MouseMove(project.TranslatePoint(new Point(8, project.Bounds.Height / 2), window)!.Value);
            window.UpdateLayout();
            Assert.Equal(18, Slot(project).Bounds.Width, 1);
            Assert.Equal(atRest + 18, project.Bounds.Width, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public async Task ChipRemovedWhileOpening_IsReleased()
    {
        var result = await fixture.Dispatch(() =>
        {
            var (window, composer) = CreateComposer();
            var skill = SkillChip(composer);

            // Pointed at, its slot heads for 18 px, and the skills change before the clock moves it there.
            window.MouseMove(skill.TranslatePoint(new Point(8, skill.Bounds.Height / 2), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(18, Slot(skill).GetBaseValue(Layoutable.WidthProperty).Value);
            Assert.True(Slot(skill).Width < 18);
            var removed = new[]
            {
                new WeakReference(skill),
                new WeakReference(Slot(skill)),
                new WeakReference(RemoveButton(skill)),
            };

            composer.SkillItems = new ObservableCollection<object> { new StrataComposerChip("Web Researcher", "🔍") };
            window.MouseMove(new Point(4, window.Bounds.Height - 4));
            for (var i = 0; i < 3; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Dispatcher.UIThread.RunJobs();
            }
            return (removed, window);
        });

        try
        {
            for (var i = 0; i < 5; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
            Assert.All(result.removed, reference => Assert.False(reference.IsAlive));
        }
        finally
        {
            await fixture.Dispatch(() => result.window.Close());
        }
    }

    [Fact]
    public Task ChipsRow_KeepsRoomAtTheEndOfEachRow_SoAnOpeningChipMovesNothingToAnotherRow() => fixture.Dispatch(() =>
    {
        // Like the composer's chips: content, then a remove slot that is shut until the chip opens.
        var chips = new[] { 100.0, 100, 95, 60 }.Select(width => Chip(width, slot: 0)).ToArray();
        var row = new ComposerChipsPanel { RevealWidth = 18 };
        foreach (var chip in chips)
            row.Children.Add(chip);
        var window = Host(row, width: 300);
        try
        {
            // 100 + 100 + 95 fits 300 only without room for one of them to open: the third starts a row.
            var atRest = chips.Select(chip => chip.Bounds).ToArray();
            Assert.Equal([0.0, 0, 28, 28], atRest.Select(bounds => bounds.Y));
            var desired = row.DesiredSize;

            for (var open = 0; open < chips.Length; open++)
            {
                Slot(chips[open]).Width = 18;
                window.UpdateLayout();

                Assert.Equal(atRest[open].Width + 18, chips[open].Bounds.Width);
                for (var i = 0; i < chips.Length; i++)
                {
                    var pushed = i > open && atRest[i].Y == atRest[open].Y ? 18 : 0;
                    Assert.Equal(atRest[i].Y, chips[i].Bounds.Y);
                    Assert.Equal(atRest[i].X + pushed, chips[i].Bounds.X);
                    Assert.True(chips[i].Bounds.Right <= 300, $"Chip {i} ended at {chips[i].Bounds.Right}");
                }
                // Nothing outside the row is measured again while a chip opens.
                Assert.Equal(desired, row.DesiredSize);

                Slot(chips[open]).Width = 0;
                window.UpdateLayout();
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ChipsRow_WhereButtonsAlwaysShow_WrapsAsAWrapPanelDoes() => fixture.Dispatch(() =>
    {
        var widths = new[] { 100.0, 100, 95, 60, 130 };
        var row = new ComposerChipsPanel();
        var wrap = new WrapPanel();
        foreach (var width in widths)
        {
            row.Children.Add(Chip(width, slot: 48));
            wrap.Children.Add(Chip(width, slot: 48));
        }
        var rowWindow = Host(row, width: 320);
        var wrapWindow = Host(wrap, width: 320);
        try
        {
            Assert.Equal(wrap.Children.Select(chip => chip.Bounds), row.Children.Select(chip => chip.Bounds));
            Assert.Equal(wrap.DesiredSize, row.DesiredSize);
        }
        finally
        {
            rowWindow.Close();
            wrapWindow.Close();
        }
    });

    private static (Window Window, StrataChatComposer Composer) CreateComposer(params string[] classes)
    {
        var composer = new StrataChatComposer
        {
            ProjectName = "Lumi",
            SkillItems = new ObservableCollection<object> { new StrataComposerChip("Code Helper", "💻") },
        };
        composer.Classes.AddRange(classes);

        var window = new Window { Width = 760, Height = 320 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = composer;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        composer.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return (window, composer);
    }

    private static Window Host(Control content, double width)
    {
        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = new Border
            {
                Width = width,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = content
            }
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Border Chip(double width, double slot)
    {
        var removeSlot = new Panel { Width = slot };
        removeSlot.Classes.Add("chip-remove-slot");
        return new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new Border { Width = width, Height = 28 }, removeSlot }
            }
        };
    }

    private static T Part<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Border SkillChip(Control composer)
        => composer.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("composer-skill-chip"));

    private static Panel Slot(Border chip)
        => chip.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Classes.Contains("chip-remove-slot"));

    private static Button RemoveButton(Border chip)
        => chip.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("chip-remove"));

    private static bool Hits(Window window, Button button)
    {
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        return window.InputHitTest(center) is Visual hit && (hit == button || button.IsVisualAncestorOf(hit));
    }

    private static async Task SettleAsync()
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 320)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16);
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
