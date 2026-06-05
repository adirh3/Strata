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
}
