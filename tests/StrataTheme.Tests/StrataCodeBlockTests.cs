using Avalonia.Controls;
using Avalonia.Controls.Documents;
using StrataTheme.Controls;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace StrataTheme.Tests;

/// <summary>
/// Tests for <see cref="StrataCodeBlock"/> covering language resolution,
/// plain-text line break handling, and tokenization correctness.
/// </summary>
public class StrataCodeBlockTests
{
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

    // ─── AppendPlainTextLines ───────────────────────────────────

    [Fact]
    public void AppendPlainTextLines_SingleLine_OneRun()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "hello world");

        Assert.Single(inlines);
        Assert.IsType<Run>(inlines[0]);
        Assert.Equal("hello world", ((Run)inlines[0]).Text);
    }

    [Fact]
    public void AppendPlainTextLines_MultipleLines_RunsWithLineBreaks()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "line1\nline2\nline3");

        // Expected: Run("line1"), LineBreak, Run("line2"), LineBreak, Run("line3")
        Assert.Equal(5, inlines.Count);
        Assert.IsType<Run>(inlines[0]);
        Assert.Equal("line1", ((Run)inlines[0]).Text);
        Assert.IsType<LineBreak>(inlines[1]);
        Assert.IsType<Run>(inlines[2]);
        Assert.Equal("line2", ((Run)inlines[2]).Text);
        Assert.IsType<LineBreak>(inlines[3]);
        Assert.IsType<Run>(inlines[4]);
        Assert.Equal("line3", ((Run)inlines[4]).Text);
    }

    [Fact]
    public void AppendPlainTextLines_CrLf_StripsCarriageReturn()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "line1\r\nline2\r\nline3");

        Assert.Equal(5, inlines.Count);
        Assert.Equal("line1", ((Run)inlines[0]).Text);
        Assert.Equal("line2", ((Run)inlines[2]).Text);
        Assert.Equal("line3", ((Run)inlines[4]).Text);
    }

    [Fact]
    public void AppendPlainTextLines_TrailingNewline_AddsEmptyLine()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "line1\n");

        // Expected: Run("line1"), LineBreak (empty trailing line has no run, just the break)
        Assert.Equal(2, inlines.Count);
        Assert.IsType<Run>(inlines[0]);
        Assert.IsType<LineBreak>(inlines[1]);
    }

    [Fact]
    public void AppendPlainTextLines_EmptyLines_PreservesBlankLines()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "a\n\nb");

        // Expected: Run("a"), LineBreak, LineBreak, Run("b")
        Assert.Equal(4, inlines.Count);
        Assert.IsType<Run>(inlines[0]);
        Assert.Equal("a", ((Run)inlines[0]).Text);
        Assert.IsType<LineBreak>(inlines[1]);
        Assert.IsType<LineBreak>(inlines[2]);
        Assert.IsType<Run>(inlines[3]);
        Assert.Equal("b", ((Run)inlines[3]).Text);
    }

    [Fact]
    public void AppendPlainTextLines_KotlinCodeBlock_AllLinesPresent()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        var kotlinCode = "fun main() = (1..100).forEach {\n" +
                         "    println(when {\n" +
                         "        it % 15 == 0 -> \"FizzBuzz\"\n" +
                         "        it % 3 == 0 -> \"Fizz\"\n" +
                         "        it % 5 == 0 -> \"Buzz\"\n" +
                         "        else -> \"$it\"\n" +
                         "    })\n" +
                         "}";

        StrataCodeBlock.AppendPlainTextLines(inlines, kotlinCode);

        // 8 lines → 8 Runs + 7 LineBreaks = 15 inlines
        int runCount = inlines.Count(i => i is Run);
        int lineBreakCount = inlines.Count(i => i is LineBreak);

        Assert.Equal(8, runCount);
        Assert.Equal(7, lineBreakCount);
    }

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
        // does not bundle their grammars. The plain-text fallback (AppendPlainTextLines)
        // handles these correctly with proper line breaks.
        var options = new RegistryOptions(ThemeName.DarkPlus);
        var registry = new Registry(options);

        var grammar = StrataCodeBlock.ResolveGrammar(options, registry, language);
        Assert.Null(grammar);
    }

    // ─── Edge cases ─────────────────────────────────────────────

    [Fact]
    public void AppendPlainTextLines_OnlyNewlines_ProducesLineBreaksOnly()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "\n\n\n");

        // 3 newlines → 3 LineBreaks (no runs since all lines are empty)
        int lineBreakCount = inlines.Count(i => i is LineBreak);
        int runCount = inlines.Count(i => i is Run);

        Assert.Equal(3, lineBreakCount);
        Assert.Equal(0, runCount);
    }

    [Fact]
    public void AppendPlainTextLines_SingleCharacter_OneRun()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "x");

        Assert.Single(inlines);
        Assert.Equal("x", ((Run)inlines[0]).Text);
    }

    [Fact]
    public void AppendPlainTextLines_MixedLineEndings_HandledCorrectly()
    {
        var tb = new SelectableTextBlock();
        var inlines = tb.Inlines ??= new InlineCollection();

        StrataCodeBlock.AppendPlainTextLines(inlines, "a\r\nb\nc");

        int runCount = inlines.Count(i => i is Run);
        Assert.Equal(3, runCount);
        Assert.Equal("a", ((Run)inlines[0]).Text);
        Assert.Equal("b", ((Run)inlines[2]).Text);
        Assert.Equal("c", ((Run)inlines[4]).Text);

        // No \r in any run text
        foreach (var inline in inlines)
        {
            if (inline is Run run)
                Assert.DoesNotContain("\r", run.Text);
        }
    }
}
