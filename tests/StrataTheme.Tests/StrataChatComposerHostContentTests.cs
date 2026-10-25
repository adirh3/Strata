using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>What a host adds to the composer: chips about the project, and an action paired with send.</summary>
[Collection("Avalonia UI")]
public sealed class StrataChatComposerHostContentTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task ContextChips_StayBesideTheProject_WhileSkillChipsChange() => fixture.Dispatch(() =>
    {
        var skills = new ObservableCollection<StrataComposerChip>
        {
            new("Writer", "✎"),
            new("Reviewer", "✓"),
        };
        var composer = new StrataChatComposer
        {
            ProjectName = "Lumi",
            SkillItems = skills,
            ContextChipsContent = new Border { Width = 80, Height = 28 },
        };
        var window = Show(composer);
        try
        {
            var row = Part<ComposerChipsPanel>(composer, "PART_ChipsRow");
            var project = Part<Border>(composer, "PART_ProjectChip");
            var host = Part<ContentPresenter>(composer, "PART_ContextChipsContent");
            Assert.True(project.IsEffectivelyVisible);
            Assert.True(host.IsEffectivelyVisible);
            Assert.Equal(row.Children.IndexOf(project) + 1, row.Children.IndexOf(host));
            Assert.Equal(5, row.Children.Count);

            // Skill chips are rebuilt after the fixed chips; the host's chips are one of them.
            skills.RemoveAt(0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, row.Children.Count);
            Assert.Equal(row.Children.IndexOf(project) + 1, row.Children.IndexOf(host));
            Assert.True(host.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task SendAccessory_SitsBesideSend_AndLeavesNoGapWhenHidden() => fixture.Dispatch(() =>
    {
        var composer = new StrataChatComposer();
        var window = Show(composer);
        try
        {
            var actions = Part<StackPanel>(composer, "PART_PrimaryActions");
            var send = Part<Panel>(composer, "PART_SendWrap");
            var bare = actions.Bounds.Width;

            var accessory = new Button { Width = 34, Height = 34, Margin = new Thickness(0, 0, 6, 0) };
            composer.SendAccessoryContent = accessory;
            window.UpdateLayout();
            Assert.Equal(bare + 40, actions.Bounds.Width, 1);
            var accessoryRight = accessory.TranslatePoint(new Point(accessory.Bounds.Width, 0), composer)!.Value.X;
            var sendLeft = send.TranslatePoint(default, composer)!.Value.X;
            Assert.Equal(6, sendLeft - accessoryRight, 1);

            // Hidden, it takes no room at all: the actions keep their usual spacing.
            accessory.IsVisible = false;
            window.UpdateLayout();
            Assert.Equal(bare, actions.Bounds.Width, 1);
        }
        finally
        {
            window.Close();
        }
    });

    private static Window Show(StrataChatComposer composer)
    {
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
        window.UpdateLayout();
        return window;
    }

    private static T Part<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
