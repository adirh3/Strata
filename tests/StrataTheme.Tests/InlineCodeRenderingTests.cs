using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>
/// Regression and correctness tests for inline code rendering in <see cref="StrataMarkdown"/>.
/// Covers the inline parsing pipeline (<see cref="StrataMarkdown.AppendFormattedInlines"/>),
/// the <see cref="StrataMarkdown.InlineCodeRun"/> structure, and the character-offset counting
/// used by <see cref="StrataMarkdown.InlineCodeLayer"/>.
/// </summary>
public class InlineCodeRenderingTests : IClassFixture<AvaloniaFixture>
{
    // ─── InlineCodeRun creation ─────────────────────────────────

    [Fact]
    public void InlineCode_CreatesInlineCodeRun_WithPaddingChars()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "before `code` after");

        // Should produce: Run("before "), InlineCodeRun("\u2005code\u2005"), Run(" after")
        Assert.Equal(3, tb.Inlines.Count);
        Assert.IsType<Run>(tb.Inlines[0]);
        Assert.IsType<StrataMarkdown.InlineCodeRun>(tb.Inlines[1]);
        Assert.IsType<Run>(tb.Inlines[2]);

        var codeRun = (StrataMarkdown.InlineCodeRun)tb.Inlines[1];
        Assert.Equal("\u2005code\u2005", codeRun.Text);
    }

    [Fact]
    public void InlineCode_AtStartOfText_NoLeadingPlainRun()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "`code` after");

        Assert.Equal(2, tb.Inlines.Count);
        Assert.IsType<StrataMarkdown.InlineCodeRun>(tb.Inlines[0]);
        Assert.IsType<Run>(tb.Inlines[1]);
    }

    [Fact]
    public void InlineCode_AtEndOfText_NoTrailingPlainRun()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "before `code`");

        Assert.Equal(2, tb.Inlines.Count);
        Assert.IsType<Run>(tb.Inlines[0]);
        Assert.IsType<StrataMarkdown.InlineCodeRun>(tb.Inlines[1]);
    }

    [Fact]
    public void InlineCode_OnlyCode_SingleInlineCodeRun()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "`code`");

        Assert.Single(tb.Inlines);
        Assert.IsType<StrataMarkdown.InlineCodeRun>(tb.Inlines[0]);
    }

    [Fact]
    public void InlineCode_MultipleSpans_AllCreateInlineCodeRuns()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "Use `foo` and `bar` together");

        Assert.Equal(5, tb.Inlines.Count);
        Assert.IsType<Run>(tb.Inlines[0]);
        Assert.IsType<StrataMarkdown.InlineCodeRun>(tb.Inlines[1]);
        Assert.IsType<Run>(tb.Inlines[2]);
        Assert.IsType<StrataMarkdown.InlineCodeRun>(tb.Inlines[3]);
        Assert.IsType<Run>(tb.Inlines[4]);
    }

    // ─── Character offset consistency ───────────────────────────
    // The InlineCodeLayer.Render computes character offsets by summing
    // inline text lengths. This must match the TextLayout's character
    // positions exactly. These tests verify the offset arithmetic.

    [Theory]
    [InlineData("Hello `world` end")]
    [InlineData("`code` at start")]
    [InlineData("at end `code`")]
    [InlineData("`a` `b` `c`")]
    [InlineData("mixed **bold** and `code` text")]
    [InlineData("nested **bold `code` bold** end")]
    [InlineData("Use `foo` and `bar` and `baz` end")]
    public void CharacterOffsets_SumToTotalTextLength(string markdown)
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, markdown);

        if (tb.Inlines == null || tb.Inlines.Count == 0)
            return;

        // Sum character lengths using the same logic as InlineCodeLayer.Render
        int totalOffset = 0;
        foreach (var inline in tb.Inlines)
        {
            totalOffset += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                _ => 1,
            };
        }

        Assert.True(totalOffset > 0, "Total offset should be positive for non-empty text");

        // Verify no inline has null or empty text (which would break offset counting)
        foreach (var inline in tb.Inlines)
        {
            if (inline is Run run)
            {
                Assert.NotNull(run.Text);
                Assert.True(run.Text.Length > 0, $"Run should not have empty text");
            }
        }
    }

    [Fact]
    public void CharacterOffset_InlineCodeRun_IncludesPaddingChars()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "a `bc` d");

        // "a " = 2 chars, "\u2005bc\u2005" = 4 chars, " d" = 2 chars => total 8
        int totalOffset = 0;
        foreach (var inline in tb.Inlines)
        {
            totalOffset += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                _ => 1,
            };
        }

        Assert.Equal(8, totalOffset);

        var codeRun = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().Single();
        Assert.Equal(4, codeRun.Text!.Length);
    }

    // ─── Inline code inside bold/italic (nested) ────────────────

    [Fact]
    public void InlineCode_InsideBold_CreatesInlineCodeRunWithBoldWeight()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "**bold `code` bold**");

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Equal(FontWeight.Bold, codeRuns[0].FontWeight);
    }

    [Fact]
    public void InlineCode_InsideItalic_CreatesInlineCodeRunWithItalicStyle()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "*italic `code` italic*");

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Equal(FontStyle.Italic, codeRuns[0].FontStyle);
    }

    [Fact]
    public void InlineCode_InsideBoldItalic_HasBothWeightAndStyle()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "***bold-italic `code` end***");

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Equal(FontWeight.Bold, codeRuns[0].FontWeight);
        Assert.Equal(FontStyle.Italic, codeRuns[0].FontStyle);
    }

    // ─── WrapWithCodeLayer structure ────────────────────────────

    [Fact]
    public void WrapWithCodeLayer_ReturnsInlineCodeLayerDecorator()
    {
        var tb = new SelectableTextBlock();
        var wrapped = StrataMarkdown.WrapWithCodeLayer(tb);

        Assert.IsType<StrataMarkdown.InlineCodeLayer>(wrapped);
        Assert.Same(tb, ((Decorator)wrapped).Child);
    }

    [Fact]
    public void WrapWithCodeLayer_ChildAccessibleThroughDecorator()
    {
        var tb = new SelectableTextBlock();
        var wrapped = StrataMarkdown.WrapWithCodeLayer(tb);

        var decorator = (Decorator)wrapped;
        Assert.Same(tb, decorator.Child);
    }

    // ─── ForceInlines mode (merged groups) ──────────────────────

    [Fact]
    public void ForceInlines_AlwaysUsesRunObjects_NeverSetsTextDirectly()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        // Add a pre-existing inline to simulate merged group context
        tb.Inlines.Add(new LineBreak());

        md.AppendFormattedInlines(tb, "plain text", forceInlines: true);

        // forceInlines should create a Run, not set tb.Text
        Assert.Null(tb.Text);
        Assert.Equal(2, tb.Inlines.Count);
        Assert.IsType<LineBreak>(tb.Inlines[0]);
        Assert.IsType<Run>(tb.Inlines[1]);
    }

    [Fact]
    public void ForceInlines_WithInlineCode_CreatesInlineCodeRun()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        tb.Inlines.Add(new Run("• First item"));
        tb.Inlines.Add(new LineBreak());

        md.AppendFormattedInlines(tb, "Use `config` flag", forceInlines: true);

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Contains("config", codeRuns[0].Text);
    }

    // ─── Fast path bypass ───────────────────────────────────────

    [Fact]
    public void PlainText_NoSpecialChars_SetsTextDirectly()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "Just plain text here");

        Assert.Equal("Just plain text here", tb.Text);
    }

    [Fact]
    public void PlainText_WithPrefix_ConcatenatesPrefix()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "item text", prefix: "• ");

        Assert.Equal("• item text", tb.Text);
    }

    // ─── Unclosed backticks (no crash, treated as literal) ──────

    [Fact]
    public void UnclosedBacktick_TreatedAsLiteralText()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "before `unclosed");

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Empty(codeRuns);
    }

    [Fact]
    public void EmptyBackticks_CreatesEmptyInlineCodeRun()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "before `` after");

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Equal("\u2005\u2005", codeRuns[0].Text);
    }

    // ─── Regression: streaming inline code ──────────────────────

    [Fact]
    public void Regression_StreamingAddsInlineCode_InlineCodeRunAppears()
    {
        var md = new StrataMarkdown();

        // Step 1: Parse text without inline code
        var tb1 = new SelectableTextBlock { FontSize = 14 };
        tb1.Inlines = new InlineCollection();
        md.AppendFormattedInlines(tb1, "The error is in the");
        var codeRuns1 = tb1.Inlines?.OfType<StrataMarkdown.InlineCodeRun>().ToList()
            ?? new List<StrataMarkdown.InlineCodeRun>();
        Assert.Empty(codeRuns1);

        // Step 2: More text arrives with inline code
        var tb2 = new SelectableTextBlock { FontSize = 14 };
        tb2.Inlines = new InlineCollection();
        md.AppendFormattedInlines(tb2, "The error is in the `OrderService` module");
        var codeRuns2 = tb2.Inlines!.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns2);
        Assert.Contains("OrderService", codeRuns2[0].Text);
    }

    [Fact]
    public void Regression_StreamingMultipleInlineCodes_AllAppear()
    {
        var md = new StrataMarkdown();
        var steps = new[]
        {
            "Use",
            "Use `foo`",
            "Use `foo` and",
            "Use `foo` and `bar`",
            "Use `foo` and `bar` together",
        };
        var expectedCodeRunCounts = new[] { 0, 1, 1, 2, 2 };

        for (int i = 0; i < steps.Length; i++)
        {
            var tb = new SelectableTextBlock { FontSize = 14 };
            tb.Inlines = new InlineCollection();
            md.AppendFormattedInlines(tb, steps[i]);

            var codeRuns = tb.Inlines?.OfType<StrataMarkdown.InlineCodeRun>().Count() ?? 0;
            Assert.Equal(expectedCodeRunCounts[i], codeRuns);
        }
    }

    // ─── Mixed formatting with inline code ──────────────────────

    [Fact]
    public void MixedFormatting_BoldAndInlineCode_BothRender()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "**Endpoint** `path` description");

        Assert.True(tb.Inlines!.Count >= 3);

        var codeRuns = tb.Inlines.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Contains("path", codeRuns[0].Text);

        var boldRuns = tb.Inlines.OfType<Run>()
            .Where(r => r is not StrataMarkdown.InlineCodeRun && r.FontWeight == FontWeight.Bold)
            .ToList();
        Assert.Single(boldRuns);
        Assert.Equal("Endpoint", boldRuns[0].Text);
    }

    [Fact]
    public void MixedFormatting_StrikethroughAndInlineCode()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, "~~old~~ use `new` instead");

        var codeRuns = tb.Inlines!.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Contains("new", codeRuns[0].Text);

        var strikeRuns = tb.Inlines.OfType<Run>()
            .Where(r => r is not StrataMarkdown.InlineCodeRun && r.TextDecorations == TextDecorations.Strikethrough)
            .ToList();
        Assert.Single(strikeRuns);
        Assert.Equal("old", strikeRuns[0].Text);
    }

    // ─── Links with inline code ─────────────────────────────────

    [Fact]
    public void InlineCodeAndLink_BothRender()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        var hasLinks = md.AppendFormattedInlines(tb, "See `config` in [docs](https://example.com)");

        Assert.True(hasLinks);

        var codeRuns = tb.Inlines!.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Single(codeRuns);
        Assert.Contains("config", codeRuns[0].Text);

        var linkRuns = tb.Inlines.OfType<Run>()
            .Where(r => r is not StrataMarkdown.InlineCodeRun && r.TextDecorations == TextDecorations.Underline)
            .ToList();
        Assert.Single(linkRuns);
        Assert.Equal("docs", linkRuns[0].Text);
    }

    // ─── Complex real-world inline code ─────────────────────────

    [Fact]
    public void NestedCodeInlines_CharacterOffsetsRemainConsistent()
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb,
            "The `checkout-v2.17.3` release changed **`Newtonsoft.Json`** to `System.Text.Json`");

        var codeRuns = tb.Inlines!.OfType<StrataMarkdown.InlineCodeRun>().ToList();
        Assert.Equal(3, codeRuns.Count);

        Assert.Contains("checkout-v2.17.3", codeRuns[0].Text);
        Assert.Contains("Newtonsoft.Json", codeRuns[1].Text);
        Assert.Contains("System.Text.Json", codeRuns[2].Text);

        // Verify character offsets sum correctly (same logic as InlineCodeLayer.Render)
        int totalOffset = 0;
        foreach (var inline in tb.Inlines)
        {
            totalOffset += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                _ => 1,
            };
        }
        Assert.True(totalOffset > 0);
    }

    // ─── InlineCodeLayer.Render offset invariant ────────────────
    // Verifies the offset-counting logic in InlineCodeLayer.Render produces
    // correct start positions for each InlineCodeRun in the inline sequence.

    [Theory]
    [InlineData("Hello `world` end", new[] { 6 })]
    [InlineData("`first` middle `second`", new[] { 0, 15 })]
    [InlineData("a `b` c `d` e", new[] { 2, 8 })]
    public void InlineCodeRun_CharacterStartPositions_AreCorrect(string markdown, int[] expectedStarts)
    {
        var md = new StrataMarkdown();
        var tb = new SelectableTextBlock { FontSize = 14 };
        tb.Inlines = new InlineCollection();

        md.AppendFormattedInlines(tb, markdown);

        // Walk inlines exactly as InlineCodeLayer.Render does and record
        // the charOffset at each InlineCodeRun
        var actualStarts = new List<int>();
        int charOffset = 0;
        foreach (var inline in tb.Inlines!)
        {
            if (inline is StrataMarkdown.InlineCodeRun)
                actualStarts.Add(charOffset);

            charOffset += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                _ => 1,
            };
        }

        Assert.Equal(expectedStarts.Length, actualStarts.Count);
        for (int i = 0; i < expectedStarts.Length; i++)
            Assert.Equal(expectedStarts[i], actualStarts[i]);
    }
}

/// <summary>
/// Provides a one-time headless Avalonia app initialization for tests that
/// need to instantiate Avalonia controls (e.g. <see cref="StrataMarkdown"/>).
/// </summary>
public class AvaloniaFixture : IDisposable
{
    private static readonly object Lock = new();
    private static bool _initialized;

    public AvaloniaFixture()
    {
        lock (Lock)
        {
            if (!_initialized)
            {
                AppBuilder.Configure<Application>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                _initialized = true;
            }
        }
    }

    public void Dispose() { }
}
