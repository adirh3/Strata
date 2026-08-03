using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataTerminalPreviewOutputTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataTerminalPreviewOutputTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AppliedTemplate_LargeOutput_RendersBoundedTailWithoutChangingOutput()
    {
        await _fixture.Dispatch(() =>
        {
            var output = string.Join(
                '\n',
                Enumerable.Range(0, 3_000).Select(i => $"{i:D4}: {new string('x', 100)}"));
            var card = new StrataTerminalPreview
            {
                Command = "long-running-command",
                Output = output,
                IsExpanded = true,
            };
            var window = new Window
            {
                Width = 640,
                Height = 400,
            };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataTerminalPreview.axaml"),
            });
            window.Content = card;

            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                card.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();

                var renderedOutput = card.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(control => control.Name == "PART_OutputText")
                    .Text;

                Assert.NotNull(renderedOutput);
                Assert.True(renderedOutput.Length <= 8_192);
                Assert.StartsWith("[Earlier output omitted]\n", renderedOutput);
                Assert.EndsWith(output[^2_048..], renderedOutput);
                Assert.Equal(output, card.Output);

                var updatedOutput = output + "\nfinal live update";
                card.Output = updatedOutput;
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(renderedOutput = card.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(control => control.Name == "PART_OutputText")
                    .Text);
                Assert.True(renderedOutput.Length <= 8_192);
                Assert.EndsWith("final live update", renderedOutput);
                Assert.Equal(updatedOutput, card.Output);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void BuildOutputPreview_SmallOutput_IsUnchanged()
    {
        const string output = "line one\nline two";

        Assert.Same(output, StrataTerminalPreview.BuildOutputPreview(output));
    }

    [Fact]
    public void BuildOutputPreview_TruncationBoundary_DoesNotSplitSurrogatePair()
    {
        var tailLength =
            StrataTerminalPreview.MaxRenderedOutputLength -
            StrataTerminalPreview.TruncatedOutputPrefix.Length;
        var output = new string('x', tailLength + 100).ToCharArray();
        output[99] = '\uD83D';
        output[100] = '\uDE80';

        var preview = StrataTerminalPreview.BuildOutputPreview(new string(output));

        Assert.True(preview.Length <= StrataTerminalPreview.MaxRenderedOutputLength);
        Assert.StartsWith(StrataTerminalPreview.TruncatedOutputPrefix, preview);
        Assert.False(char.IsSurrogate(preview[StrataTerminalPreview.TruncatedOutputPrefix.Length]));
    }
}
