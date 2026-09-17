using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using StrataTheme.Diff;
using System.Diagnostics;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataDiffViewTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData("cs", "public string Name = \"Lumi\"; // comment", "keyword", "string", "comment")]
    [InlineData("json", "{\"enabled\": true, \"count\": 42}", "string", "keyword", "number")]
    [InlineData("py", "return \"done\" # comment", "keyword", "string", "comment")]
    public void PortableHighlightingNeedsNoNativeLibraries(
        string language, string text, string firstKind, string secondKind, string thirdKind)
    {
        var tokens = PortableCodeHighlighter.Tokenize(text, language);
        Assert.Contains(tokens, token => token.Kind == firstKind);
        Assert.Contains(tokens, token => token.Kind == secondKind);
        Assert.Contains(tokens, token => token.Kind == thirdKind);
        var end = 0;
        foreach (var token in tokens)
        {
            Assert.InRange(token.Start, end, text.Length - 1);
            Assert.InRange(token.Length, 1, text.Length - token.Start);
            Assert.NotEqual(PortableCodeHighlighter.ColorFor(token.Kind, true),
                PortableCodeHighlighter.ColorFor(token.Kind, false));
            end = token.Start + token.Length;
        }
        Assert.Empty(PortableCodeHighlighter.Tokenize("Plain Text 42", "txt"));
    }

    [Fact]
    public void SharedParserPreservesRenameMetadataAndRealIncrementLines()
    {
        var rename = UnifiedDiffBuilder.BuildFromUnifiedDiff(
            "diff --git a/before.txt b/after.txt\nsimilarity index 100%\nrename from before.txt\nrename to after.txt\n");
        Assert.Empty(rename.Hunks);
        Assert.Equal(0, rename.AddedLineCount);
        Assert.Contains("rename from before.txt", rename.EmptyStateText);
        Assert.Contains("rename to after.txt", rename.EmptyStateText);

        var changed = UnifiedDiffBuilder.BuildFromUnifiedDiff("@@ -1 +1 @@\n---count;\n+++count;\n");
        Assert.Equal(1, changed.AddedLineCount);
        Assert.Equal(1, changed.RemovedLineCount);
        var lines = Assert.Single(changed.Hunks).Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal("--count;", lines[0].Text);
        Assert.Equal("++count;", lines[1].Text);
    }

    [Fact]
    public async Task TouchViewerRendersNumbersColorsAndNavigatesHunks()
    {
        await fixture.Dispatch(() =>
        {
            var view = new StrataDiffView { TouchMode = true, CodeFontSize = 14 };
            var window = Show(view);
            try
            {
                view.SetUnifiedDiffText("Test.cs",
                    "@@ -1 +1 @@\n-return 1;\n+return 2;\n@@ -90 +90 @@\n-return 3;\n+return 4;\n");
                PumpUntil(() => !view.IsRendering
                    && view.FindControl<StackPanel>("DiffContent")!.Children.Count == 6);
                var added = view.GetVisualDescendants().OfType<Border>()
                    .Where(row => row.Classes.Contains("diff-added")).ToArray();
                var removed = view.GetVisualDescendants().OfType<Border>()
                    .Where(row => row.Classes.Contains("diff-removed")).ToArray();
                Assert.Equal(2, added.Length);
                Assert.Equal(2, removed.Length);
                Assert.NotEqual(added[0].Background, removed[0].Background);
                var code = added[0].GetVisualDescendants().OfType<TextBlock>()
                    .Single(control => control.Classes.Contains("diff-code"));
                Assert.IsNotType<SelectableTextBlock>(code);
                Assert.Equal("return 2;", string.Concat(code.Inlines!.OfType<Run>().Select(run => run.Text)));
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "90");
                var next = view.FindControl<Button>("NextChangeBtn")!;
                Assert.Equal(48, next.Height);
                Assert.True(next.IsEnabled);
                next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("/", view.FindControl<TextBlock>("ChangeCountText")!.Text);
                var pan = Assert.Single(view.FindControl<StackPanel>("DiffContent")!
                    .GestureRecognizers.OfType<ScrollGestureRecognizer>());
                Assert.True(pan.CanHorizontallyScroll);
                Assert.True(pan.CanVerticallyScroll);
                view.TouchMode = false;
                Assert.False(pan.CanHorizontallyScroll);
                Assert.False(pan.CanVerticallyScroll);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task ChangingFileDuringRenderingDoesNotAppendStaleRows()
    {
        await fixture.Dispatch(() =>
        {
            var view = new StrataDiffView();
            var window = Show(view);
            try
            {
                view.SetDocument("Old.cs", () =>
                {
                    Thread.Sleep(50);
                    return UnifiedDiffBuilder.BuildFromUnifiedDiff("@@ -1 +1 @@\n-old\n+stale\n");
                });
                Dispatcher.UIThread.RunJobs();
                view.SetUnifiedDiffText("New.cs", "@@ -1 +1 @@\n-current\n+latest\n");
                PumpUntil(() => !view.IsRendering
                    && view.FindControl<StackPanel>("DiffContent")!.Children.Count == 3);
                var text = string.Concat(view.GetVisualDescendants().OfType<TextBlock>()
                    .SelectMany(control => control.Inlines?.OfType<Run>() ?? []).Select(run => run.Text));
                Assert.Contains("latest", text);
                Assert.DoesNotContain("stale", text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 420, Height = 780, Content = content };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void PumpUntil(Func<bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(predicate(), "Diff rendering did not complete.");
    }
}
