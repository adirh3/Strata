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
            var close = new Button { Classes = { "strata-md-image-viewer-close" } };
            var window = CreateWindow(new StackPanel { Children = { caption, progress, close } }, theme);
            try
            {
                Pump(window);
                AssertBrush(window, "Brush.TextSecondary", caption.Foreground);
                Assert.Equal(88, progress.Width);
                Assert.Equal(3, progress.Height);
                Assert.True(close.Width > 0);
                Assert.Equal(close.Width, close.Height);
                AssertBrush(window, "Brush.Surface1", close.Background);
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
