using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMarkdownStyleScopeTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData("Light", false)]
    [InlineData("Dark", false)]
    [InlineData("Light", true)]
    [InlineData("Dark", true)]
    public async Task MarkdownHelpersStyleTheirContentWithoutAttachingToUnrelatedControls(string theme, bool derived)
    {
        await fixture.Dispatch(() =>
        {
            var unrelated = new Border { Classes = { "strata-md-blockquote" } };
            var markdown = CreateMarkdown(derived);
            var window = CreateWindow(new StackPanel { Children = { unrelated, markdown } }, theme);
            try
            {
                Pump(window);
                var quote = FindBorder(markdown, "strata-md-blockquote");
                Assert.Equal(new Thickness(3, 0, 0, 0), quote.BorderThickness);
                AssertBrush(window, "Brush.AccentDefault", quote.BorderBrush);
                var table = FindBorder(markdown, "strata-md-table");
                Assert.Equal(new CornerRadius(8), table.CornerRadius);
                AssertBrush(window, "Brush.Surface1", table.Background);
                Assert.Equal(FlowDirection.LeftToRight, FindBorder(markdown, "strata-md-code-block").FlowDirection);

                Assert.Null(unrelated.Background);
                Assert.Null(unrelated.BorderBrush);
                Assert.Equal(default, unrelated.BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task FullScreenImageHelpersKeepTheirStylesOutsideMarkdown(string theme)
    {
        await fixture.Dispatch(() =>
        {
            var caption = new TextBlock { Text = "Image caption", Classes = { "strata-md-image-caption" } };
            var progress = new ProgressBar { Classes = { "strata-md-image-progress" } };
            // The viewer lives in the overlay layer, outside any markdown, under its own root.
            var close = new Button { Classes = { "strata-md-image-viewer-tool", "strata-md-image-viewer-close" } };
            var viewer = new Grid { Name = "MarkdownImageViewer", Children = { close } };
            var window = CreateWindow(new StackPanel { Children = { caption, progress, viewer } }, theme);
            try
            {
                Pump(window);
                AssertBrush(window, "Brush.TextSecondary", caption.Foreground);
                Assert.Equal(88, progress.Width);
                Assert.Equal(3, progress.Height);
                Assert.Equal(40, close.Height);
                Assert.Equal(40, close.MinWidth);
                AssertBrush(window, "Brush.TextPrimary", close.Foreground);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task RetainedMarkdownKeepsItsStylesAfterReparentingAndThemeChange()
    {
        await fixture.Dispatch(() =>
        {
            var markdown = CreateMarkdown();
            markdown.RetainContentOnDetach = true;
            var first = new Border { Child = markdown };
            var second = new Border();
            var window = CreateWindow(new StackPanel { Children = { first, second } }, "Light");
            try
            {
                Pump(window);
                AssertBrush(window, "Brush.Surface1", FindBorder(markdown, "strata-md-table").Background);

                first.Child = null;
                window.RequestedThemeVariant = ThemeVariant.Dark;
                second.Child = markdown;
                Pump(window);

                AssertBrush(window, "Brush.Surface1", FindBorder(markdown, "strata-md-table").Background);
                AssertBrush(window, "Brush.AccentDefault", FindBorder(markdown, "strata-md-blockquote").BorderBrush);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task AnAppsBorderStylesNeverReachTheMarkdownsOwnBorders()
    {
        await fixture.Dispatch(() =>
        {
            // An app styles its own borders by type; each such rule would otherwise attach to every cell, quote
            // and code shell of every markdown it shows, which made long documents slow to build.
            var unrelated = new Border { Classes = { "app-card" } };
            var markdown = CreateMarkdown();
            var window = CreateWindow(new StackPanel { Children = { unrelated, markdown } }, "Light");
            window.Styles.Add(new Style(selector => selector.OfType<Border>())
            {
                Setters = { new Setter(Control.TagProperty, "app") },
            });
            try
            {
                Pump(window);
                Assert.Equal("app", unrelated.Tag);
                var owned = markdown.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Any(name => name.StartsWith("strata-md-", StringComparison.Ordinal)))
                    .ToList();
                Assert.Contains(owned, border => border.Classes.Contains("strata-md-table-cell"));
                Assert.Contains(owned, border => border.Classes.Contains("strata-md-blockquote"));
                Assert.All(owned, border => Assert.Null(border.Tag));
                // Their own rules still apply.
                Assert.Equal(new Thickness(3, 0, 0, 0), FindBorder(markdown, "strata-md-blockquote").BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static StrataMarkdown CreateMarkdown(bool derived = false)
    {
        StrataMarkdown markdown = derived ? new DerivedMarkdown() : new StrataMarkdown();
        markdown.IsInline = true;
        markdown.StreamingRebuildThrottleMs = 0;
        markdown.Markdown = "> A styled quote\n\n| First | Second |\n| --- | --- |\n| One | Two |\n\n```csharp\nvar value = 1;\n```";
        return markdown;
    }

    private sealed class DerivedMarkdown : StrataMarkdown;

    private static Window CreateWindow(Control content, string theme)
    {
        var window = new Window
        {
            Width = 700,
            Height = 600,
            RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner },
                }.RegisterInNameScope(scope),
            }),
            Content = content,
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme.Tests/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml"),
        });
        window.Show();
        return window;
    }

    private static Border FindBorder(Control root, string className) =>
        root.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains(className));

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
    }
}
