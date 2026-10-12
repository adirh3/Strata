using SkiaSharp;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public sealed class StrataBackdropBlurTests
{
    [Fact]
    public void FullRedrawsRecomputeTheFrost()
    {
        var bounds = new SKRectI(0, 0, 400, 120);

        Assert.True(StrataBackdropBlur.ShouldRecompute(hasCache: false, default, bounds));
        Assert.True(StrataBackdropBlur.ShouldRecompute(hasCache: true, bounds, bounds));
        // More of the glass became visible than was frosted before.
        Assert.True(StrataBackdropBlur.ShouldRecompute(hasCache: true, new SKRectI(0, 40, 400, 120), bounds));
    }

    [Fact]
    public void PartialRedrawsReuseTheFrostInsteadOfSamplingTheGlassForeground()
    {
        var frosted = new SKRectI(0, 0, 400, 120);

        // A caret blink or a pressed button on the glass repaints only a small part of it.
        Assert.False(StrataBackdropBlur.ShouldRecompute(hasCache: true, frosted, new SKRectI(180, 40, 182, 64)));
        Assert.False(StrataBackdropBlur.ShouldRecompute(hasCache: true, frosted, new SKRectI(340, 60, 400, 120)));
    }

    [Fact]
    public void SaturationMatrixPreservesNeutralsAndIsIdentityAtOne()
    {
        var identity = StrataBackdropBlur.CreateSaturationMatrix(1);
        Assert.Equal(
        [
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 1, 0
        ], identity.Select(value => (float)Math.Round(value, 4)));

        var boosted = StrataBackdropBlur.CreateSaturationMatrix(1.8);
        for (var row = 0; row < 3; row++)
            Assert.Equal(1, boosted[row * 5] + boosted[row * 5 + 1] + boosted[row * 5 + 2], 4);
    }
}
