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

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:user@example.com", true)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("file://attacker/share/report.pdf", false)]
    [InlineData("javascript:alert(1)", false)]
    public void ExternalLinkSchemesRejectLocalAndExecutableTargets(string target, bool expected)
    {
        Assert.Equal(expected, StrataMarkdown.IsAllowedExternalUri(new Uri(target)));
        Assert.False(new StrataMarkdown().AllowLocalFileLinks);
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

            var linkBounds = Assert.Single(textBlock.TextLayout.HitTestTextRange(beforeLink.Length, linkText.Length))
                .Translate(new Vector(textBlock.Padding.Left, textBlock.Padding.Top));
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

    [Fact]
    public async Task HitTestLink_TakesClicksJustBesideALinkButNotFarFromIt()
    {
        await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock { FontSize = 14, TextWrapping = TextWrapping.NoWrap };
            markdown.AppendFormattedInlines(textBlock, "See source [a](https://example.com/one)tail and more");
            var window = new Window { Width = 500, Height = 100, Content = textBlock };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var bounds = Assert.Single(textBlock.TextLayout.HitTestTextRange("See source ".Length, 1));

                var beforeIt = new Point(bounds.Left - StrataMarkdown.LinkHitSlopX + 1, bounds.Center.Y);
                var above = new Point(bounds.Center.X, bounds.Top - StrataMarkdown.LinkHitSlopY + 1);
                Assert.Equal("https://example.com/one", markdown.GetLinkAtPoint(textBlock, beforeIt));
                Assert.Equal("https://example.com/one", markdown.GetLinkAtPoint(textBlock, above));
                // The word right after it is its own text, however close.
                Assert.Null(markdown.GetLinkAtPoint(textBlock, new Point(bounds.Right + 2, bounds.Center.Y)));
                Assert.Null(markdown.GetLinkAtPoint(textBlock, new Point(bounds.Right + 40, bounds.Center.Y)));

                var hit = markdown.HitTestLink(textBlock, bounds.Center);
                Assert.NotNull(hit);
                Assert.Equal(bounds, hit.Value.Bounds);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("Read https://example.com/docs.", "https://example.com/docs", "Read |.")]
    [InlineData("See (https://en.wikipedia.org/wiki/Foo_(bar)) now", "https://en.wikipedia.org/wiki/Foo_(bar)", "See (|) now")]
    [InlineData("Go to <https://example.com/a?b=1&c=2> today", "https://example.com/a?b=1&c=2", "Go to | today")]
    [InlineData("HTTPS://EXAMPLE.COM, then", "HTTPS://EXAMPLE.COM", "|, then")]
    [InlineData("**Docs: https://example.com/x_y**", "https://example.com/x_y", "Docs: |")]
    [InlineData("官网：https://example.com/docs。欢迎访问", "https://example.com/docs", "官网：|。欢迎访问")]
    public Task BareWebAddressesBecomeLinks(string markdownText, string expectedUrl, string surroundingText) => _fixture.Dispatch(() =>
    {
        var markdown = new StrataMarkdown();
        var textBlock = new SelectableTextBlock();
        Assert.True(markdown.AppendFormattedInlines(textBlock, markdownText));

        var inlines = Assert.IsType<InlineCollection>(textBlock.Inlines);
        var link = Assert.Single(inlines.OfType<Run>(), run => markdown.GetLinkTarget(run) is not null);
        Assert.Equal(expectedUrl, link.Text);
        Assert.Equal(expectedUrl, markdown.GetLinkTarget(link));
        var rest = string.Concat(inlines.OfType<Run>().Select(run => ReferenceEquals(run, link) ? "|" : run.Text));
        Assert.Equal(surroundingText, rest);
    });

    [Theory]
    [InlineData("Use `https://example.com` literally")]
    [InlineData("A scheme alone: https:// is not a link")]
    [InlineData("ftp://example.com and file://share are not web links")]
    [InlineData("xhttps://example.com glued to a word")]
    [InlineData("[label](https://example.com)")]
    public Task TextThatIsNotABareWebAddressGainsNoAutolink(string markdownText) => _fixture.Dispatch(() =>
    {
        var markdown = new StrataMarkdown();
        var textBlock = new SelectableTextBlock();
        markdown.AppendFormattedInlines(textBlock, markdownText);

        var links = textBlock.Inlines?.OfType<Run>().Where(run => markdown.GetLinkTarget(run) is not null).ToList() ?? [];
        Assert.Equal(markdownText.StartsWith('[') ? 1 : 0, links.Count);
    });

    [Fact]
    public void LinkPreview_ReadsTitleSummarySiteAndAUsableIcon()
    {
        var page = new Uri("https://example.com/articles/one");
        const string html = """
            <html><head>
            <title>Fallback &amp; title</title>
            <meta property="og:site_name" content="Example News">
            <meta name="description" content="Plain description">
            <meta property="og:description" content="  Shared   summary  ">
            <meta property="og:image" content="/img/share.png">
            <link rel="icon" type="image/svg+xml" href="/favicon.svg">
            <link rel="icon" href="/favicon-32.png">
            <link rel="apple-touch-icon" href="https://cdn.example.com/touch.png">
            </head><body></body></html>
            """;

        var preview = StrataMarkdown.FindLinkPreview(html, page);

        Assert.Equal("Fallback & title", preview.Title);
        Assert.Equal("Shared summary", preview.Description);
        Assert.Equal("Example News", preview.SiteName);
        Assert.Equal("https://example.com/img/share.png", preview.Image);
        Assert.Equal("https://cdn.example.com/touch.png", preview.Icon);

        var shared = StrataMarkdown.FindLinkPreview(
            """<meta property="og:title" content="Shared title"><title>Tab title</title><link rel="icon" href="/i.svg">""", page);
        Assert.Equal("Shared title", shared.Title);
        Assert.Null(shared.Icon);
    }

    [Theory]
    [InlineData("https://www.example.com/a", "example.com")]
    [InlineData("mailto:someone@example.com?subject=hi", "someone@example.com")]
    public void LinkSiteLabel_NamesWhereALinkLeads(string target, string expected)
    {
        Assert.Equal(expected, StrataMarkdown.LinkSiteLabel(new Uri(target), target));
    }

    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public async Task GetLinkAtPoint_AccountsForTheTextContentPadding(FlowDirection direction)
    {
        await _fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown();
            var textBlock = new SelectableTextBlock
            {
                FlowDirection = direction,
                TextAlignment = direction == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
                FontSize = 14,
                Padding = new Thickness(30, 12),
                TextWrapping = TextWrapping.NoWrap,
            };
            markdown.AppendFormattedInlines(textBlock, "[docs](https://example.com)");
            var window = new Window { Width = 300, Height = 100, Content = textBlock };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var bounds = Assert.Single(textBlock.TextLayout.HitTestTextRange(0, 4))
                    .Translate(new Vector(textBlock.Padding.Left, textBlock.Padding.Top));

                Assert.Equal("https://example.com", markdown.GetLinkAtPoint(textBlock, bounds.Center));
                Assert.Null(markdown.GetLinkAtPoint(textBlock, new Point(bounds.Center.X, 1)));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
