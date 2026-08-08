using Avalonia.Styling;
using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

/// <summary>
/// Guards the app-launch path when the application is set to follow the OS theme.
/// </summary>
public class StrataMarkdownUnresolvedThemeTests
{
    // Application.ActualThemeVariant is typed non-nullable but is genuinely null until the platform
    // has resolved a variant. That window exists only when RequestedThemeVariant is
    // ThemeVariant.Default -- the "follow the OS" setting -- because Avalonia then has to consult
    // platform settings rather than use a value it was handed.
    //
    // On Android those settings are not available while OnFrameworkInitializationCompleted is still
    // constructing the first view, so StrataMarkdown's constructor read a null variant, called
    // ToString() on it, and killed the process before the window appeared. Every fresh install hit
    // it, because "System" is the default. Desktop never reproduced it: its platform settings
    // resolve synchronously -- and neither does the headless harness, which always hands out a
    // variant. That is why this tests the resolution directly: a construct-the-control test passed
    // identically with and without the guard, so it proved nothing.
    [Fact]
    public void UnresolvedVariant_FallsBackInsteadOfThrowing()
    {
        var name = StrataMarkdown.ResolveThemeVariantName(null);

        Assert.Equal(ThemeVariant.Light.ToString(), name);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ResolvedVariant_IsReportedUnchanged(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.Equal(variant.ToString(), StrataMarkdown.ResolveThemeVariantName(variant));
    }
}
