using System.Globalization;
using System.Xml.Linq;

namespace StrataTheme.Tests;

public sealed class InfiniteAnimationDetachLeakTests
{
    private static readonly PulseExpectation[] ExpectedPulses =
    [
        new("Button.axaml", "Button.accent:pointerover /template/ Border#PART_Aurora", 0.55, 0.85, 1.8),
        new("CheckBox.axaml", "^:checked /template/ Path#CheckGlyph", 0.85, 1, 1.6),
        new("Expander.axaml", "^:expanded /template/ Border#HeaderMarker", 0.7, 1, 1.8),
        new("ProgressBar.axaml", "^:indeterminate /template/ Border#PART_Indicator", 0.9, 1, 1.6),
        new("RadioButton.axaml", "^:checked /template/ Ellipse#RadioDot", 0.85, 1, 1.6),
        new("TabControl.axaml", "^:selected /template/ Border#PART_SelectedPipe", 0.86, 1, 2),
        new("TextBox.axaml", "^:focus-within /template/ Border#FocusAccentBar", 0.72, 1, 1.6),
        new("StrataAiToolCall.axaml", "^:inprogress /template/ Border#PART_StateDot", 1, 0.35, 0.92, Easing: "CompositorDefault"),
        new("StrataAiToolCall.axaml", "^:inprogress /template/ Border#PART_Stratum", 0.55, 1, 1.1),
        new("StrataTerminalPreview.axaml", "^:inprogress /template/ Border#PART_StateDot", 1, 0.35, 0.92, Easing: "CompositorDefault"),
        new("StrataTerminalPreview.axaml", "^:inprogress /template/ Border#PART_Stratum", 0.55, 1, 1.1),
        new("StrataFileAttachment.axaml", "^:uploading /template/ Border#PART_StatusDot", 1, 0.35, 0.9, Easing: "CompositorDefault"),
        new("StrataFileAttachment.axaml", "^:uploading /template/ Border#PART_Stratum", 0.5, 1, 1.1),
        new("StrataOrb.axaml", "^:active /template/ Border#PART_Orb", 1, 0.5, 1.8, Easing: "CompositorDefault"),
        new("StrataPulse.axaml", "^:live /template/ Border#PART_StatusDot", 1, 0.35, 1.6, Easing: "CompositorDefault"),
        new("StrataThink.axaml", "^:active /template/ Border#PART_Dot", 1, 0.3, 1.4, Easing: "CompositorDefault"),
        new("StrataChatShell.axaml", "^:online:has-presence /template/ Border#PART_PresenceDot", 1, 0.45, 1.5, Easing: "CompositorDefault"),
        new("StrataChatShell.axaml", "^:pulse-new-content /template/ Border#PART_NewContentDot", 1, 0.4, 1.8, Easing: "CompositorDefault"),
        new("StrataChatComposer.axaml", "Border.suggestion-loading-pill.phase-a", 0.28, 0.62, 0.95, PeakAt: 0.38),
        new("StrataChatComposer.axaml", "Border.suggestion-loading-pill.phase-b", 0.28, 0.62, 0.95, HoldUntil: 0.18, PeakAt: 0.56),
        new("StrataChatComposer.axaml", "Border.suggestion-loading-pill.phase-c", 0.28, 0.62, 0.95, HoldUntil: 0.36, PeakAt: 0.74),
    ];

    [Fact]
    public void StrataXaml_HasNoInfiniteStyleAnimations()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        var violations = Directory
            .EnumerateFiles(FindStrataSourceRoot(), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path)
                .Descendants(avalonia + "Animation")
                .Where(animation => string.Equals(
                    (string?)animation.Attribute("IterationCount"),
                    "Infinite",
                    StringComparison.OrdinalIgnoreCase))
                .Select(_ => Path.GetRelativePath(FindStrataSourceRoot(), path)))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void LifecyclePulseStyles_PreserveOriginalVisualIdentity()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";

        foreach (var expected in ExpectedPulses)
        {
            var document = XDocument.Load(Path.Combine(FindControlsSourceRoot(), expected.FileName));
            var style = document
                .Descendants(avalonia + "Style")
                .Single(element =>
                    string.Equals((string?)element.Attribute("Selector"), expected.Selector, StringComparison.Ordinal) &&
                    FindSetter(element, avalonia, "LifecycleOpacityPulse.IsActive") is not null);

            Assert.Equal(expected.FromOpacity, ParseDouble(style, avalonia, "LifecycleOpacityPulse.FromOpacity"));
            Assert.Equal(expected.ToOpacity, ParseDouble(style, avalonia, "LifecycleOpacityPulse.ToOpacity"));
            Assert.Equal(
                TimeSpan.FromSeconds(expected.DurationSeconds),
                TimeSpan.Parse(
                    RequiredSetterValue(style, avalonia, "LifecycleOpacityPulse.Duration"),
                    CultureInfo.InvariantCulture));
            Assert.Equal("True", RequiredSetterValue(style, avalonia, "LifecycleOpacityPulse.IsActive"));
            Assert.Equal(
                expected.HoldUntil,
                ParseOptionalDouble(style, avalonia, "LifecycleOpacityPulse.HoldUntil", 0));
            Assert.Equal(
                expected.PeakAt,
                ParseOptionalDouble(style, avalonia, "LifecycleOpacityPulse.PeakAt", 0.5));
            Assert.Equal(
                expected.PlaybackDirection,
                FindSetter(style, avalonia, "LifecycleOpacityPulse.PlaybackDirection")?.Attribute("Value")?.Value
                    ?? "Normal");
            Assert.Equal(
                expected.Easing,
                FindSetter(style, avalonia, "LifecycleOpacityPulse.Easing")?.Attribute("Value")?.Value
                    ?? "Linear");
        }
    }

    [Fact]
    public void ForeverCompositionAnimations_AreRestrictedToLifecycleManagedImplementations()
    {
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "LifecycleOpacityPulse.cs",
            "StrataCanvas.cs",
            "StrataChatMessage.cs",
            "StrataPresence.cs",
            "StrataStream.cs",
        };

        var violations = Directory
            .EnumerateFiles(FindStrataSourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "AnimationIterationBehavior.Forever",
                StringComparison.Ordinal))
            .Where(path => !allowedFiles.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(FindStrataSourceRoot(), path))
            .ToArray();

        Assert.Empty(violations);
    }

    private static double ParseDouble(XElement style, XNamespace avalonia, string propertyName) =>
        double.Parse(RequiredSetterValue(style, avalonia, propertyName), CultureInfo.InvariantCulture);

    private static double ParseOptionalDouble(
        XElement style,
        XNamespace avalonia,
        string propertyName,
        double defaultValue)
    {
        var value = FindSetter(style, avalonia, propertyName)?.Attribute("Value")?.Value;
        return value is null
            ? defaultValue
            : double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string RequiredSetterValue(XElement style, XNamespace avalonia, string propertyName) =>
        FindSetter(style, avalonia, propertyName)?.Attribute("Value")?.Value
        ?? throw new InvalidDataException(
            $"Style '{style.Attribute("Selector")?.Value}' is missing setter '{propertyName}'.");

    private static XElement? FindSetter(XElement style, XNamespace avalonia, string propertyName) =>
        style
            .Elements(avalonia + "Setter")
            .SingleOrDefault(setter =>
                setter.Attribute("Property")?.Value.EndsWith(propertyName, StringComparison.Ordinal) == true);

    private static string FindStrataSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "StrataTheme");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate the StrataTheme source directory.");
    }

    private static string FindControlsSourceRoot() =>
        Path.Combine(FindStrataSourceRoot(), "Controls");

    private sealed record PulseExpectation(
        string FileName,
        string Selector,
        double FromOpacity,
        double ToOpacity,
        double DurationSeconds,
        string PlaybackDirection = "Normal",
        string Easing = "Linear",
        double HoldUntil = 0,
        double PeakAt = 0.5);
}
