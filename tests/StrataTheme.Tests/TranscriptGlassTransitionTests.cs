using System.Xml.Linq;

namespace StrataTheme.Tests;

/// <summary>
/// The transcript's glass surfaces fade their fill and rim between <c>Transparent</c> and a glass brush.
/// <c>Transparent</c> is white at zero alpha, so Avalonia's plain <c>BrushTransition</c> fades through a
/// translucent light grey on the way, a white flash over the dark transcript on hover in and out. These
/// templates must animate brushes with <c>PremultipliedBrushTransition</c> instead.
/// </summary>
public sealed class TranscriptGlassTransitionTests
{
    private static readonly string[] TranscriptTemplates =
    [
        "StrataThink.axaml",
        "StrataAiToolCall.axaml",
        "StrataTerminalPreview.axaml",
        "StrataTurnSummary.axaml",
        "StrataAgentRun.axaml",
        "StrataTypingIndicator.axaml",
        "StrataQuestionCard.axaml",
    ];

    [Fact]
    public void TranscriptTemplates_FadeFillsAndRimsWithoutFlashingWhite()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        var offenders = TranscriptTemplates
            .SelectMany(file => XDocument.Load(Path.Combine(FindControlsSourceRoot(), file))
                .Descendants(avalonia + "BrushTransition")
                .Where(transition => (string?)transition.Attribute("Property") is "Background" or "BorderBrush")
                .Select(transition => $"{file}: {transition.Attribute("Property")!.Value}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TurnSummaryHeader_FadesItsGlassPremultiplied()
    {
        XNamespace strataAnimation = "using:StrataTheme.Animation";
        var transitions = XDocument.Load(Path.Combine(FindControlsSourceRoot(), "StrataTurnSummary.axaml"))
            .Descendants(strataAnimation + "PremultipliedBrushTransition")
            .Select(transition => (string?)transition.Attribute("Property"))
            .ToArray();

        Assert.Contains("Background", transitions);
        Assert.Contains("BorderBrush", transitions);
    }

    private static string FindControlsSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "StrataTheme", "Controls");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate the StrataTheme controls directory.");
    }
}
