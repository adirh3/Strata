using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataMarkdownLinkCardTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task RestingOnALink_LightsItUpAndShowsWhereItLeads_ThenLeavingPutsTheCardAway() => fixture.Dispatch(async () =>
    {
        var picture = Path.Combine(Path.GetTempPath(), $"strata-link-card-{Guid.NewGuid():N}.png");
        SavePicture(picture);
        var showedPreviews = StrataInteractiveBlock.ShowsLinkPreviews;
        StrataInteractiveBlock.ShowsLinkPreviews = true;
        StrataMarkdown.LinkPreviewPageReader = page => Task.FromResult<LinkPreview?>(new LinkPreview(
            "Avalonia UI documentation",
            "Build cross-platform apps with .NET: controls, styling, data binding and everything else in one place.",
            "Avalonia Docs", picture, null));

        var markdown = new StrataMarkdown { Markdown = "Start with the [Avalonia docs](https://docs.avaloniaui.net/) and read on." };
        var window = new Window { Width = 620, Height = 460, RequestedThemeVariant = ThemeVariant.Dark, Padding = new Thickness(24) };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = markdown;
        try
        {
            window.Show();
            await SettleAsync(200);

            var text = markdown.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(block => block.Inlines?.OfType<Run>().Any(run => markdown.GetLinkTarget(run) is not null) == true);
            var link = text.Inlines!.OfType<Run>().Single(run => markdown.GetLinkTarget(run) is not null);
            var hit = markdown.HitTestLink(text, new Point(text.Padding.Left + 1, text.Bounds.Height / 2));
            Assert.Null(hit);
            var linkPoint = text.TranslatePoint(LinkCenter(text, "Start with the ".Length, "Avalonia docs".Length), window)!.Value;
            Assert.Same(link, markdown.HitTestLink(text, window.TranslatePoint(linkPoint, text)!.Value)?.Run);
            var under = window.InputHitTest(linkPoint) as Visual;
            Assert.Same(text, under);

            window.MouseMove(linkPoint);
            await SettleAsync(80);
            var layer = Assert.IsType<StrataMarkdown.InlineCodeLayer>(text.GetVisualParent());
            Assert.Same(link, layer.HoveredLink);
            Assert.DoesNotContain(markdown.GetLogicalChildren().OfType<Popup>(), popup => popup.IsOpen);

            await SettleAsync(700);
            var card = markdown.GetLogicalChildren().OfType<Popup>().Single();
            Assert.True(card.IsOpen);
            var shown = card.Child!.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
            Assert.Contains("Avalonia UI documentation", shown);
            Assert.Contains("Avalonia Docs", shown);
            Assert.Contains("https://docs.avaloniaui.net/", shown);

            window.MouseMove(new Point(4, window.Bounds.Height - 4));
            await SettleAsync(600);
            Assert.False(card.IsOpen);
            Assert.Null(layer.HoveredLink);
        }
        finally
        {
            window.Close();
            StrataMarkdown.LinkPreviewPageReader = null;
            StrataInteractiveBlock.ShowsLinkPreviews = showedPreviews;
            File.Delete(picture);
        }
    });

    private static Point LinkCenter(SelectableTextBlock text, int start, int length)
    {
        var rect = text.TextLayout.HitTestTextRange(start, length).First();
        return rect.Center + new Vector(text.Padding.Left, text.Padding.Top);
    }

    private static void SavePicture(string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(680, 340));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#7C5CFF"), 0), new GradientStop(Color.Parse("#1FB6FF"), 1) },
            }, new Rect(0, 0, 680, 340));
        }
        bitmap.Save(path);
    }

    private static async Task SettleAsync(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16);
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
