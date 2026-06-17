using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

// Regression guard for the comparison/fork control "second option does nothing" bug.
//
// StrataFork backs the markdown ```comparison``` block. StrataMarkdown caches the created
// StrataFork by JSON key and re-parents the SAME instance whenever the transcript re-renders
// (RebuildChildrenFromGroups clears and re-adds children during streaming). That re-parent
// detaches and re-attaches the fork from the visual tree.
//
// The bug: OnDetachedFromVisualTree unsubscribed the PART_OptionA/PART_OptionB Button.Click
// handlers, but OnApplyTemplate (the only place that re-wires them) is NOT called again on a
// plain re-attach because the template is already applied. So after the first re-render the
// tab buttons were dead and clicking the second option did nothing.
[Collection("Avalonia UI")]
public class StrataForkReattachClickTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataForkReattachClickTests(AvaloniaFixture fixture) => _fixture = fixture;

    // Minimal in-code template that exposes the named parts StrataFork.OnApplyTemplate looks for.
    // The bug lives entirely in StrataFork's C# lifecycle, so a synthetic template is a faithful
    // and self-contained way to exercise it without pulling in the full StrataTheme resources.
    private static FuncControlTemplate<StrataFork> BuildForkTemplate() =>
        new((_, scope) =>
        {
            var optionA = new Button { Name = "PART_OptionA" };
            var optionB = new Button { Name = "PART_OptionB" };
            var indicator = new Border { Name = "PART_Indicator" };
            var aContent = new ContentPresenter { Name = "PART_OptionAContent" };
            var bContent = new ContentPresenter { Name = "PART_OptionBContent", IsVisible = false };
            var tabHost = new Border
            {
                Name = "PART_TabHost",
                Child = new StackPanel { Children = { optionA, optionB } },
            };

            scope.Register("PART_TabHost", tabHost);
            scope.Register("PART_Indicator", indicator);
            scope.Register("PART_OptionA", optionA);
            scope.Register("PART_OptionB", optionB);
            scope.Register("PART_OptionAContent", aContent);
            scope.Register("PART_OptionBContent", bContent);

            return new DockPanel
            {
                Children =
                {
                    tabHost,
                    indicator,
                    new Panel { Children = { aContent, bContent } },
                },
            };
        });

    private static Button OptionB(StrataFork fork) =>
        fork.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_OptionB");

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public async Task ClickingOptionB_AfterReParenting_StillSwitchesSelection()
    {
        var (firstClickIndex, afterReParentIndex) = await _fixture.Dispatch(() =>
        {
            var fork = new StrataFork
            {
                Template = BuildForkTemplate(),
                OptionATitle = "A",
                OptionBTitle = "B",
                OptionAContent = new TextBlock { Text = "Alpha" },
                OptionBContent = new TextBlock { Text = "Beta" },
            };

            var host = new Border();
            var window = new Window { Width = 400, Height = 300, Content = host };
            host.Child = fork;
            window.Show();
            fork.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            // Sanity: a fresh fork switches to option B on click.
            Click(OptionB(fork));
            var firstClick = fork.SelectedIndex;

            // Reset, then reproduce the markdown cache re-parent: detach + re-attach the same
            // instance (no template re-apply on re-attach).
            fork.SelectedIndex = 0;
            host.Child = null;
            Dispatcher.UIThread.RunJobs();
            host.Child = fork;
            Dispatcher.UIThread.RunJobs();

            // The reported bug: this click was a no-op because the handler had been dropped.
            Click(OptionB(fork));
            var afterReParent = fork.SelectedIndex;

            window.Close();
            return (firstClick, afterReParent);
        });

        Assert.Equal(1, firstClickIndex);
        Assert.Equal(1, afterReParentIndex);
    }
}
