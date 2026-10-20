using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace StrataTheme.Tests;

/// <summary>
/// Tests for <see cref="StrataCodeBlock"/> covering language resolution,
/// plain-text line break handling, and tokenization correctness.
/// </summary>
[Collection("Avalonia UI")]
public class StrataCodeBlockTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataCodeBlockTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    // ─── MapLanguageAlias ───────────────────────────────────────

    [Theory]
    [InlineData("csharp", ".cs")]
    [InlineData("c#", ".cs")]
    [InlineData("javascript", ".js")]
    [InlineData("js", ".js")]
    [InlineData("jsx", ".js")]
    [InlineData("typescript", ".ts")]
    [InlineData("ts", ".ts")]
    [InlineData("tsx", ".ts")]
    [InlineData("python", ".py")]
    [InlineData("py", ".py")]
    [InlineData("ruby", ".rb")]
    [InlineData("rb", ".rb")]
    [InlineData("rust", ".rs")]
    [InlineData("rs", ".rs")]
    [InlineData("kotlin", ".kt")]
    [InlineData("kt", ".kt")]
    [InlineData("swift", ".swift")]
    [InlineData("golang", ".go")]
    [InlineData("go", ".go")]
    [InlineData("cpp", ".cpp")]
    [InlineData("c++", ".cpp")]
    [InlineData("bash", ".sh")]
    [InlineData("shell", ".sh")]
    [InlineData("sh", ".sh")]
    [InlineData("zsh", ".sh")]
    [InlineData("powershell", ".ps1")]
    [InlineData("pwsh", ".ps1")]
    [InlineData("ps1", ".ps1")]
    [InlineData("dockerfile", ".dockerfile")]
    [InlineData("fsharp", ".fs")]
    [InlineData("f#", ".fs")]
    [InlineData("scala", ".scala")]
    [InlineData("haskell", ".hs")]
    [InlineData("hs", ".hs")]
    [InlineData("lua", ".lua")]
    [InlineData("perl", ".pl")]
    [InlineData("pl", ".pl")]
    [InlineData("r", ".r")]
    [InlineData("objc", ".m")]
    [InlineData("objective-c", ".m")]
    [InlineData("clojure", ".clj")]
    [InlineData("clj", ".clj")]
    [InlineData("elixir", ".ex")]
    [InlineData("ex", ".ex")]
    [InlineData("erlang", ".erl")]
    [InlineData("erl", ".erl")]
    [InlineData("groovy", ".groovy")]
    [InlineData("diff", ".diff")]
    [InlineData("patch", ".diff")]
    [InlineData("cmake", ".cmake")]
    [InlineData("makefile", ".makefile")]
    [InlineData("make", ".makefile")]
    [InlineData("graphql", ".graphql")]
    [InlineData("gql", ".graphql")]
    [InlineData("vue", ".vue")]
    [InlineData("svelte", ".svelte")]
    [InlineData("proto", ".proto")]
    [InlineData("protobuf", ".proto")]
    [InlineData("toml", ".toml")]
    [InlineData("ini", ".ini")]
    [InlineData("properties", ".ini")]
    public void MapLanguageAlias_ReturnsCorrectExtension(string alias, string expectedExtension)
    {
        var result = StrataCodeBlock.MapLanguageAlias(alias);
        Assert.Equal(expectedExtension, result);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("nonexistent")]
    [InlineData("brainfuck")]
    public void MapLanguageAlias_UnknownLanguage_ReturnsNull(string alias)
    {
        var result = StrataCodeBlock.MapLanguageAlias(alias);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("CSharp", ".cs")]
    [InlineData("PYTHON", ".py")]
    [InlineData("JavaScript", ".js")]
    [InlineData("GoLang", ".go")]
    public void MapLanguageAlias_CaseInsensitive(string alias, string expectedExtension)
    {
        var result = StrataCodeBlock.MapLanguageAlias(alias);
        Assert.Equal(expectedExtension, result);
    }

    // ─── ResolveGrammar ─────────────────────────────────────────

    [Theory]
    [InlineData("csharp")]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("yaml")]
    [InlineData("html")]
    [InlineData("css")]
    [InlineData("javascript")]
    [InlineData("typescript")]
    [InlineData("python")]
    [InlineData("markdown")]
    public void ResolveGrammar_KnownLanguages_ReturnsGrammar(string language)
    {
        var options = new RegistryOptions(ThemeName.DarkPlus);
        var registry = new Registry(options);

        var grammar = StrataCodeBlock.ResolveGrammar(options, registry, language);
        Assert.NotNull(grammar);
    }

    [Theory]
    [InlineData("nonexistent")]
    [InlineData("made_up_lang")]
    public void ResolveGrammar_UnknownLanguage_ReturnsNull(string language)
    {
        var options = new RegistryOptions(ThemeName.DarkPlus);
        var registry = new Registry(options);

        var grammar = StrataCodeBlock.ResolveGrammar(options, registry, language);
        Assert.Null(grammar);
    }

    [Fact]
    public void ResolveGrammar_CachedOnSecondCall()
    {
        var options = new RegistryOptions(ThemeName.DarkPlus);
        var registry = new Registry(options);

        var first = StrataCodeBlock.ResolveGrammar(options, registry, "csharp");
        var second = StrataCodeBlock.ResolveGrammar(options, registry, "csharp");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // ─── Presentation ───────────────────────────────────────────

    private const string KotlinCode =
        "fun main() = (1..100).forEach {\n" +
        "    println(when {\n" +
        "        it % 15 == 0 -> \"FizzBuzz\"\n" +
        "        it % 3 == 0 -> \"Fizz\"\n" +
        "        it % 5 == 0 -> \"Buzz\"\n" +
        "        else -> \"$it\"\n" +
        "    })\n" +
        "}";

    private static StrataCodeBlock Present(string text, string? language = null)
    {
        var block = new StrataCodeBlock { Language = language, Text = text };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block;
    }

    private static SelectableTextBlock Presenter(StrataCodeBlock block) =>
        block.GetVisualChildren().OfType<SelectableTextBlock>().Single();

    [Fact]
    public Task PlainText_IsPresentedUnchangedWithoutStyles() => _fixture.Dispatch(() =>
    {
        var block = Present("hello world");

        Assert.Equal("hello world", block.PresentedText);
        Assert.Empty(block.PresentedSpans);
    });

    [Fact]
    public Task CarriageReturns_AreRemovedFromLineEndings() => _fixture.Dispatch(() =>
    {
        var block = Present("line1\r\nline2\r\nline3\r", "csharp");

        Assert.Equal("line1\nline2\nline3", block.PresentedText);
    });

    [Theory]
    [InlineData("line1\nline2\nline3", null, 3)]
    [InlineData("a\n\nb", null, 3)]
    [InlineData("int a = 1;\n\nint b = 2;", "csharp", 3)]
    [InlineData(KotlinCode, "kotlin", 8)]
    public Task EveryLineIsLaidOut(string text, string? language, int lines) => _fixture.Dispatch(() =>
    {
        var block = Present(text, language);

        Assert.Equal(lines, Presenter(block).TextLayout.TextLines.Count);
        Assert.Equal(lines * StrataCodeBlock.CodeLineHeight, block.DesiredSize.Height, 1);
    });

    [Fact]
    public Task LongCodeDrawsOnlyNearbyLinesAndUpdatesAfterScrollJumps() => _fixture.Dispatch(() =>
    {
        var block = Present(string.Join("\n", Enumerable.Range(0, 300).Select(i => $"line {i}")));
        block.MinHeight = 300 * StrataCodeBlock.CodeLineHeight;
        var presenter = Presenter(block);
        var scroll = new ScrollContentPresenter
        {
            Content = block,
            CanVerticallyScroll = true,
            ClipToBounds = true
        };
        var window = new Window { Width = 400, Height = 180, Content = scroll };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(300, presenter.TextLayout.TextLines.Count);
            Assert.InRange(DrawGlyphs(presenter).Length, 1, 60);
            foreach (var offset in new[] { 2200d, 5000d, 0d })
            {
                scroll.Offset = new Vector(0, offset);
                window.UpdateLayout();
                Assert.Equal(offset, scroll.Offset.Y, 1);
                var glyphs = DrawGlyphs(presenter);
                Assert.InRange(glyphs.Length, 1, 60);
                Assert.Contains($"line {(int)(offset / StrataCodeBlock.CodeLineHeight)}",
                    string.Concat(glyphs.Select(glyph => glyph.GlyphRun?.Characters.ToString())));
            }
            Assert.Equal(300 * StrataCodeBlock.CodeLineHeight, block.Bounds.Height, 1);

            presenter.SelectionStart = 0;
            presenter.SelectionEnd = presenter.Text!.Length;
            Assert.Equal(presenter.Text, presenter.SelectedText);
            Assert.Equal(300, DrawGlyphs(presenter).Length);
            presenter.SelectionEnd = 0;

            window.Width = 280;
            window.Height = 540;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var resized = DrawGlyphs(presenter);
            Assert.InRange(resized.Length, 1, 100);
            Assert.Contains("line 29", string.Concat(resized.Select(glyph => glyph.GlyphRun?.Characters.ToString())));

            window.Content = null;
            Assert.Equal(300, DrawGlyphs(presenter).Length);
            window.Content = scroll;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            scroll.Offset = new Vector(0, 3000);
            window.UpdateLayout();
            var reattached = DrawGlyphs(presenter);
            Assert.InRange(reattached.Length, 1, 100);
            Assert.Contains("line 166", string.Concat(reattached.Select(glyph => glyph.GlyphRun?.Characters.ToString())));

            block.Text += "\nappended line";
            block.MinHeight = 301 * StrataCodeBlock.CodeLineHeight;
            window.UpdateLayout();
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            window.UpdateLayout();
            var appended = DrawGlyphs(presenter);
            Assert.InRange(appended.Length, 1, 100);
            Assert.Contains("appended line", string.Concat(appended.Select(glyph => glyph.GlyphRun?.Characters.ToString())));
        }
        finally
        {
            window.Close();
        }

        // A detached control used for an image/export must render the complete document.
        Assert.Equal(301, DrawGlyphs(presenter).Length);
    });

    private static GlyphRunDrawing[] DrawGlyphs(SelectableTextBlock presenter)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            presenter.Render(context);
        return Enumerate(drawing).ToArray();

        static IEnumerable<GlyphRunDrawing> Enumerate(Drawing drawing) => drawing switch
        {
            GlyphRunDrawing glyph => [glyph],
            DrawingGroup group => group.Children.SelectMany(Enumerate),
            _ => []
        };
    }

    [Fact]
    public Task HighlightedCode_IsStyledByItsTextLayoutWithoutInlineElements() => _fixture.Dispatch(() =>
    {
        const string code = "public class Sample\n{\n    // a comment\n    private int value = 42;\n}";
        var block = Present(code, "csharp");
        var presenter = Presenter(block);
        var spans = block.PresentedSpans;

        Assert.Equal(code, block.PresentedText);
        Assert.True(presenter.Inlines is null || presenter.Inlines.Count == 0);
        Assert.NotEmpty(spans);
        for (var i = 0; i < spans.Count; i++)
        {
            Assert.InRange(spans[i].Start, 0, code.Length - 1);
            Assert.InRange(spans[i].End, spans[i].Start + 1, code.Length);
            if (i > 0)
            {
                // Neighbouring tokens that share a style are merged into one range.
                Assert.True(spans[i].Start >= spans[i - 1].End);
                if (spans[i].Start == spans[i - 1].End)
                    Assert.False(spans[i].HasSameStyle(spans[i - 1]));
            }
        }

        var keyword = Assert.Single(spans, span => span.Start == 0);
        Assert.NotNull(keyword.Foreground);
        var firstRun = presenter.TextLayout.TextLines[0].TextRuns[0];
        Assert.Same(keyword.Foreground, firstRun.Properties?.ForegroundBrush);
        Assert.Equal(5, presenter.TextLayout.TextLines.Count);
    });
    // ─── Grammar availability for specific languages ────────────

    [Theory]
    [InlineData("go")]
    [InlineData("rust")]
    [InlineData("swift")]
    [InlineData("ruby")]
    [InlineData("php")]
    [InlineData("lua")]
    [InlineData("perl")]
    [InlineData("powershell")]
    [InlineData("bash")]
    [InlineData("fsharp")]
    [InlineData("diff")]
    public void ResolveGrammar_AliasedLanguages_FindsGrammar(string language)
    {
        var options = new RegistryOptions(ThemeName.DarkPlus);
        var registry = new Registry(options);

        var grammar = StrataCodeBlock.ResolveGrammar(options, registry, language);

        // If this fails, it means the language isn't available in TextMateSharp.Grammars
        // and the plain-text fallback will be used (which is now correct).
        // But ideally, we want as many languages as possible to have syntax highlighting.
        Assert.NotNull(grammar);
    }

    [Theory]
    [InlineData("kotlin")]
    [InlineData("scala")]
    public void ResolveGrammar_UnavailableInTextMateSharp_ReturnsNull(string language)
    {
        // These languages have aliases in MapLanguageAlias but TextMateSharp.Grammars
        // does not bundle their grammars. The plain-text fallback
        // handles these correctly with proper line breaks.
        var options = new RegistryOptions(ThemeName.DarkPlus);
        var registry = new Registry(options);

        var grammar = StrataCodeBlock.ResolveGrammar(options, registry, language);
        Assert.Null(grammar);
    }

    // ─── Edge cases ─────────────────────────────────────────────

    [Fact]
    public Task OnlyNewlines_PresentAsEmptyCode() => _fixture.Dispatch(() =>
    {
        var block = Present("\n\n\n");

        Assert.Equal("", block.PresentedText);
        Assert.Contains(":empty", block.Classes);
    });

    [Fact]
    public Task SingleCharacter_IsPresented() => _fixture.Dispatch(() =>
    {
        Assert.Equal("x", Present("x").PresentedText);
    });

    [Fact]
    public Task MixedLineEndings_PresentEachLineWithoutCarriageReturns() => _fixture.Dispatch(() =>
    {
        var block = Present("a\r\nb\nc");

        Assert.Equal("a\nb\nc", block.PresentedText);
        Assert.Equal(3, Presenter(block).TextLayout.TextLines.Count);
    });
    [Fact]
    public async Task MarkdownCodeBlock_AllowsVerticalScrollingWhenContentExceedsMaxHeight()
    {
        await _fixture.Dispatch(() =>
        {
            var lines = Enumerable.Range(1, 80).Select(i => $"line {i}");
            var code = string.Join('\n', lines);
            var renderer = new StrataMarkdown();
            var method = typeof(StrataMarkdown).GetMethod(
                "CreateCodeBlockControl",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var shell = Assert.IsType<Border>(method!.Invoke(renderer, new object[] { code, "text" }));
            var stack = Assert.IsType<StackPanel>(shell.Child);
            var codeScroller = Assert.IsType<ScrollViewer>(stack.Children[1]);
            var codeBlock = Assert.IsType<StrataCodeBlock>(codeScroller.Content);

            Assert.True(codeBlock.MinHeight > codeScroller.MaxHeight);
            Assert.Equal(ScrollBarVisibility.Auto, codeScroller.VerticalScrollBarVisibility);
            Assert.Equal(400, codeScroller.MaxHeight);
        });
    }
}
