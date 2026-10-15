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
