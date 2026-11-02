using SkiaSharp;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public sealed class StrataBackdropBlurTests
{
    [Fact]
    public void UniformFrostDoesNotNeedAFadeMask()
    {
        Assert.Null(StrataBackdropBlur.CreateFadeMask(new SKRect(0, 0, 200, 100),
            StrataBackdropFade.None, 0, 0));
    }

    [Fact]
    public void TopAndSideFadesBlendTogetherInTheBackdropsOwnCoordinates()
    {
        using var bitmap = new SKBitmap(240, 140);
        using var canvas = new SKCanvas(bitmap);
        using var mask = StrataBackdropBlur.CreateFadeMask(new SKRect(20, 20, 220, 120),
            StrataBackdropFade.Top, 40, 32);
        using var paint = new SKPaint { Shader = mask };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawRect(new SKRect(20, 20, 220, 120), paint);

        Assert.InRange(bitmap.GetPixel(120, 20).Alpha, 0, 8);
        Assert.Equal(255, bitmap.GetPixel(120, 70).Alpha);
        Assert.InRange(bitmap.GetPixel(20, 70).Alpha, 0, 8);
        Assert.InRange(bitmap.GetPixel(219, 70).Alpha, 0, 8);
        // Halfway through the side's eased ramp, as halfway through the top's.
        Assert.InRange(bitmap.GetPixel(36, 70).Alpha, 141, 161);
        Assert.InRange(bitmap.GetPixel(120, 40).Alpha, 141, 161);
        Assert.Equal(255, bitmap.GetPixel(52, 70).Alpha);
        Assert.True(bitmap.GetPixel(36, 40).Alpha < bitmap.GetPixel(120, 40).Alpha);
        Assert.True(bitmap.GetPixel(120, 30).Alpha < bitmap.GetPixel(120, 40).Alpha);
        Assert.True(bitmap.GetPixel(120, 40).Alpha < bitmap.GetPixel(120, 50).Alpha);
    }

    [Theory]
    [InlineData(StrataBackdropFade.Top, 0, 99)]
    [InlineData(StrataBackdropFade.Bottom, 99, 0)]
    public void ExistingVerticalFadeKeepsItsDirectionWithoutSideFading(
        StrataBackdropFade fade, int clearY, int solidY)
    {
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);
        using var mask = StrataBackdropBlur.CreateFadeMask(new SKRect(0, 0, 200, 100), fade, 40, 0);
        using var paint = new SKPaint { Shader = mask };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawRect(new SKRect(0, 0, 200, 100), paint);

        Assert.InRange(bitmap.GetPixel(100, clearY).Alpha, 0, 8);
        Assert.Equal(255, bitmap.GetPixel(0, solidY).Alpha);
        Assert.Equal(255, bitmap.GetPixel(199, solidY).Alpha);
    }

    [Fact]
    public void SideFadesMeetAtTheCenterOnANarrowBackdrop()
    {
        using var bitmap = new SKBitmap(20, 10);
        using var canvas = new SKCanvas(bitmap);
        using var mask = StrataBackdropBlur.CreateFadeMask(new SKRect(0, 0, 20, 10),
            StrataBackdropFade.None, 0, 32);
        using var paint = new SKPaint { Shader = mask };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawRect(new SKRect(0, 0, 20, 10), paint);

        Assert.InRange(bitmap.GetPixel(0, 5).Alpha, 0, 16);
        Assert.InRange(bitmap.GetPixel(9, 5).Alpha, 235, 255);
        Assert.Equal(bitmap.GetPixel(0, 5).Alpha, bitmap.GetPixel(19, 5).Alpha);
    }

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
    public void GlassThatSamplesBeyondItsEdgesCapturesAMarginWithinTheRedrawnPixels()
    {
        var glass = new SKRectI(100, 200, 400, 260);
        var everything = new SKRectI(0, 0, 1000, 1000);

        // By default the glass captures exactly what lies beneath it.
        Assert.Equal(glass, StrataBackdropBlur.CaptureRegion(glass, 0, everything));
        Assert.Equal(new SKRectI(100, 200, 400, 230), StrataBackdropBlur.CaptureRegion(glass, 0, new SKRectI(0, 0, 1000, 230)));
        // Sampling beyond its edges takes in a margin all round, as far as this frame's redrawn pixels reach.
        Assert.Equal(new SKRectI(58, 158, 442, 302), StrataBackdropBlur.CaptureRegion(glass, 42, everything));
        Assert.Equal(new SKRectI(58, 158, 442, 280), StrataBackdropBlur.CaptureRegion(glass, 42, new SKRectI(0, 0, 1000, 280)));
        // A redraw beside the glass that misses the glass itself captures nothing.
        Assert.True(StrataBackdropBlur.CaptureRegion(glass, 42, new SKRectI(0, 0, 1000, 190)).IsEmpty);
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

    [Fact]
    public void RetiredGpuStateWaitsForARenderUnderItsOwnContext()
    {
        var retired = new StrataBackdropBlur.RetiredGpuResources();
        var context = new object();
        var frost = new Resource();
        var backdrop = new Resource();
        retired.Retire(context, frost);
        retired.Retire(context, backdrop);
        retired.Retire(context, null);

        // The compositor disposes a replaced frost before its context is current, and a render under another
        // context is no better. Releasing a texture then would leave it allocated in the driver for good.
        retired.ReleaseFor(new object(), NeverLost);
        Assert.Equal(0, frost.Disposals + backdrop.Disposals);
        Assert.Equal(2, retired.Count);

        retired.ReleaseFor(context, NeverLost);
        Assert.Equal(1, frost.Disposals);
        Assert.Equal(1, backdrop.Disposals);
        Assert.Equal(0, retired.Count);

        retired.ReleaseFor(context, NeverLost);
        Assert.Equal(1, frost.Disposals);
    }

    [Fact]
    public void RetiredGpuStateOfALostContextIsReleasedUnderAnyContext()
    {
        var retired = new StrataBackdropBlur.RetiredGpuResources();
        var lost = new object();
        var live = new object();
        var orphan = new Resource();
        var waiting = new Resource();
        retired.Retire(lost, orphan);
        retired.Retire(live, waiting);

        // A lost context's objects no longer reach the driver, so they need not wait for it.
        retired.ReleaseFor(new object(), context => ReferenceEquals(context, lost));

        Assert.Equal(1, orphan.Disposals);
        Assert.Equal(0, waiting.Disposals);
        Assert.Equal(1, retired.Count);
    }

    private static bool NeverLost(object context) => false;

    private sealed class Resource : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }
}
