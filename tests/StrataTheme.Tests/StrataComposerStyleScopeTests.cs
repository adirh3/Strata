using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataComposerStyleScopeTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComposerHelpersDoNotAttachToUnrelatedControls(bool dark) => fixture.Dispatch(() =>
    {
        var unrelated = new Border { Classes = { "add-menu-key", "autocomplete-item" } };
        var label = new TextBlock { Classes = { "autocomplete-header" }, Text = "Outside" };
        unrelated.Child = label;
        var composer = new StrataChatComposer { ProjectName = "Project" };
        var window = Host(composer, dark, unrelated);
        try
        {
            Pump(window);
            Assert.Null(unrelated.Background);
            Assert.Null(unrelated.BorderBrush);
            Assert.Equal(default, unrelated.Padding);
            var project = composer.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "PART_ProjectChip");
            Assert.Equal(new Thickness(3, 3, 4, 3), project.Padding);
            AssertBrush(window, "Brush.ComposerChipFill", project.Background);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LateChipsKeepTheirStylesAcrossThemeAndMobileChanges() => fixture.Dispatch(() =>
    {
        var skills = new ObservableCollection<StrataComposerChip>();
        var composer = new StrataChatComposer { ProjectName = "Project", SkillItems = skills };
        var window = Host(composer, false);
        try
        {
            Pump(window);
            skills.Add(new StrataComposerChip("Writer"));
            Pump(window);
            var skill = composer.GetVisualDescendants().OfType<Border>().Single(control => control.Classes.Contains("composer-skill-chip"));
            Assert.Equal(new Thickness(3, 3, 4, 3), skill.Padding);
            AssertBrush(window, "Brush.ComposerChipFill", skill.Background);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Pump(window);
            AssertBrush(window, "Brush.ComposerChipFill", skill.Background);
            composer.Classes.Add("mobile");
            Pump(window);
            var project = composer.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "PART_ProjectChip");
            Assert.Equal(new CornerRadius(16), project.CornerRadius);
            AssertBrush(window, "Brush.ComposerChipFill", skill.Background);
            composer.Classes.Remove("mobile");
            Pump(window);
            Assert.Equal(new CornerRadius(10), project.CornerRadius);
        }
        finally { window.Close(); }
    });

    private static Window Host(StrataChatComposer composer, bool dark, Control? unrelated = null)
    {
        var window = new Window
        {
            Width = 760, Height = 460,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml"),
        });
        window.Content = unrelated is null ? composer : new StackPanel { Children = { unrelated, composer } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        composer.ApplyTemplate();
        Pump(window);
        return window;
    }

    private static void AssertBrush(Window window, string key, IBrush? actual)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var expected));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
