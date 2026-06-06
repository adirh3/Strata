using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataMarkdownLinkTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataMarkdownLinkTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetLinkAtPoint_UsesHitPositionInsteadOfSelectionStart()
    {
        await _fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.NoWrap,
                Width = 400,
                SelectionStart = 0,
                SelectionEnd = 0
            };
            textBlock.Inlines = new InlineCollection();

            markdown.AppendFormattedInlines(textBlock, "Read [docs](https://example.com) now");

            var window = new Window
            {
                Width = 400,
                Height = 100,
                Content = textBlock
            };
            window.Show();

            await Task.Delay(50);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var linkBounds = Assert.Single(textBlock.TextLayout.HitTestTextRange("Read ".Length, "docs".Length));
            var linkPoint = linkBounds.Center;

            Assert.Equal("https://example.com", markdown.GetLinkAtPoint(textBlock, linkPoint));
            Assert.Null(markdown.GetLinkAtPoint(textBlock, new Point(1, linkPoint.Y)));

            window.Close();
        });
    }

    [Fact]
    public async Task GetLinkAtPoint_AccountsForLineBreaksBeforeLinkRun()
    {
        await _fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.NoWrap,
                Width = 500
            };
            textBlock.Inlines = new InlineCollection();

            markdown.AppendFormattedInlines(textBlock, "1. First link: [OneDrive](https://onedrive.live.com)", forceInlines: true);
            textBlock.Inlines.Add(new LineBreak());
            markdown.AppendFormattedInlines(textBlock, "2. Second link: [Google Takeout](https://takeout.google.com)", forceInlines: true);

            var window = new Window
            {
                Width = 500,
                Height = 120,
                Content = textBlock
            };
            window.Show();

            await Task.Delay(50);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var secondLinkOffset = "1. First link: ".Length
                + "OneDrive".Length
                + Environment.NewLine.Length
                + "2. Second link: ".Length;
            var secondLinkBounds = Assert.Single(textBlock.TextLayout.HitTestTextRange(secondLinkOffset, "Google Takeout".Length));

            Assert.Equal("https://takeout.google.com", markdown.GetLinkAtPoint(textBlock, secondLinkBounds.Center));

            window.Close();
        });
    }

    [Fact]
    public async Task GetLinkAtPoint_UsesRenderedLinkBoundsForRtlText()
    {
        await _fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.NoWrap,
                Width = 500
            };
            StrataMarkdown.ApplyDirectionalTextLayout(textBlock, FlowDirection.RightToLeft);
            textBlock.Inlines = new InlineCollection();

            const string beforeLink = "\u05e4\u05ea\u05d7 \u05d0\u05ea ";
            const string linkText = "OneDrive";
            const string afterLink = " \u05d5\u05d0\u05d6 \u05d4\u05de\u05e9\u05da";
            markdown.AppendFormattedInlines(textBlock, $"{beforeLink}[{linkText}](\u200fhttps://onedrive.live.com\u200e){afterLink}");

            var window = new Window
            {
                Width = 500,
                Height = 100,
                Content = textBlock
            };
            window.Show();

            await Task.Delay(50);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var linkBounds = Assert.Single(textBlock.TextLayout.HitTestTextRange(beforeLink.Length, linkText.Length));
            var pointsInsideLink = new[]
            {
                linkBounds.Center,
                new Point(linkBounds.Left + 1, linkBounds.Center.Y),
                new Point(linkBounds.Right - 1, linkBounds.Center.Y)
            };

            foreach (var point in pointsInsideLink)
            {
                Assert.Equal("https://onedrive.live.com", markdown.GetLinkAtPoint(textBlock, point));
            }

            window.Close();
        });
    }
}
