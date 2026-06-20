using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using StrataTheme.Animation;
using Xunit;

namespace StrataTheme.Tests;

/// <summary>
/// Guards the premultiplied brush interpolation that fixes the "fade through white" hover flash
/// (Transparent -> opaque surface) seen on dark themes.
/// </summary>
public class PremultipliedBrushTransitionTests
{
    private static readonly Color Transparent = new(0x00, 0xFF, 0xFF, 0xFF); // XAML "Transparent" == white @ 0 alpha
    private static readonly Color DarkSurface = new(0xFF, 0x2D, 0x2D, 0x2D); // Brush.Surface2 (dark theme)
    private static readonly Color DarkPage = new(0xFF, 0x16, 0x16, 0x16);    // Color.Background (dark theme)

    /// <summary>
    /// Straight (non-premultiplied) interpolation as Avalonia's built-in transition performs it — used
    /// only to demonstrate the bug this fix addresses.
    /// </summary>
    private static Color StraightLerp(double t, Color from, Color to)
    {
        byte Mix(byte f, byte g) => (byte)System.Math.Round(f + (g - f) * t);
        return new Color(Mix(from.A, to.A), Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    // Approximate "over" compositing of a (possibly translucent) colour onto an opaque background.
    private static double CompositedLuma(Color fg, Color bg)
    {
        var a = fg.A / 255d;
        double Channel(byte f, byte b) => b + (f - b) * a;
        var r = Channel(fg.R, bg.R);
        var g = Channel(fg.G, bg.G);
        var b = Channel(fg.B, bg.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    [Fact]
    public void TransparentToDark_KeepsTargetHue_NoWhiteOvershoot()
    {
        var targetLuma = CompositedLuma(DarkSurface, DarkPage);
        var pageLuma = CompositedLuma(DarkPage, DarkPage);

        double previous = pageLuma;
        for (var t = 0.05; t <= 0.95; t += 0.05)
        {
            var c = PremultipliedBrushTransition.InterpolateColorPremultiplied(t, Transparent, DarkSurface);

            // Because the transparent endpoint contributes no colour, the resolved hue stays on target
            // (mid-grey ~45) instead of drifting towards white (~255).
            Assert.InRange(c.R, 40, 52);
            Assert.InRange(c.G, 40, 52);
            Assert.InRange(c.B, 40, 52);

            // The composited brightness ramps monotonically up to the target — it never overshoots
            // brighter than the final hover colour (that overshoot is the visible white flash).
            var luma = CompositedLuma(c, DarkPage);
            Assert.True(luma <= targetLuma + 1.0, $"overshoot at t={t}: {luma} > {targetLuma}");
            Assert.True(luma >= previous - 1.0, $"non-monotonic at t={t}: {luma} < {previous}");
            previous = luma;
        }
    }

    [Fact]
    public void StraightInterpolation_DemonstratesTheFlash()
    {
        // Sanity check that the bug is real with naive interpolation: the mid-transition composite is
        // far brighter than either endpoint, which is exactly what the premultiplied path removes.
        var targetLuma = CompositedLuma(DarkSurface, DarkPage);
        var mid = StraightLerp(0.5, Transparent, DarkSurface);
        var midLuma = CompositedLuma(mid, DarkPage);

        Assert.True(midLuma > targetLuma + 20, $"expected overshoot, got mid={midLuma} target={targetLuma}");
    }

    [Fact]
    public void OpaqueEndpoints_UseGammaCorrectMidpoint()
    {
        // Both endpoints opaque => premultiply is the identity, so the result matches Avalonia's
        // gamma-correct interpolation. The 0.5 point between black and white lands near 188, not 128.
        var mid = PremultipliedBrushTransition.InterpolateColorPremultiplied(
            0.5, new Color(0xFF, 0, 0, 0), new Color(0xFF, 0xFF, 0xFF, 0xFF));

        Assert.Equal(255, mid.A);
        Assert.InRange(mid.R, 185, 191);
        Assert.Equal(mid.R, mid.G);
        Assert.Equal(mid.R, mid.B);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Endpoints_AreExact(double t)
    {
        var c = PremultipliedBrushTransition.InterpolateColorPremultiplied(t, Transparent, DarkSurface);
        var expected = t == 0.0 ? Transparent : DarkSurface;
        Assert.Equal(expected.A, c.A);
        if (t == 1.0)
        {
            Assert.InRange(c.R, 0x2C, 0x2E);
            Assert.InRange(c.G, 0x2C, 0x2E);
            Assert.InRange(c.B, 0x2C, 0x2E);
        }
    }

    // --- Gradient path (the only gradient Background transition is StrataSettingGroup's card) ---

    private static ImmutableLinearGradientBrush LinearGradient(params (double Offset, Color Color)[] stops)
    {
        var built = new ImmutableGradientStop[stops.Length];
        for (var i = 0; i < stops.Length; i++)
            built[i] = new ImmutableGradientStop(stops[i].Offset, stops[i].Color);
        return new ImmutableLinearGradientBrush(built);
    }

    [Fact]
    public void OpaqueGradients_StayOpaque_NoTransparentFlash()
    {
        // Mirrors SurfaceCard -> SurfaceCardHover: two opaque 2-stop linear gradients.
        var from = LinearGradient((0, new Color(0xFF, 0x1E, 0x1E, 0x2A)), (1, new Color(0xFF, 0x14, 0x14, 0x1C)));
        var to = LinearGradient((0, new Color(0xFF, 0x26, 0x26, 0x3A)), (1, new Color(0xFF, 0x1A, 0x1A, 0x28)));

        for (var t = 0.0; t <= 1.0; t += 0.1)
        {
            var result = Assert.IsAssignableFrom<ILinearGradientBrush>(
                PremultipliedBrushTransition.InterpolateBrush(t, from, to));
            Assert.Equal(2, result.GradientStops.Count);

            // Opaque endpoints => premultiply is the identity => every interpolated stop stays fully
            // opaque (no alpha dip that would briefly show the surface beneath the card).
            foreach (var stop in result.GradientStops)
                Assert.Equal(255, stop.Color.A);
        }
    }

    [Fact]
    public void Gradient_Endpoints_AreExact()
    {
        var from = LinearGradient((0, new Color(0xFF, 0x1E, 0x1E, 0x2A)), (1, new Color(0xFF, 0x14, 0x14, 0x1C)));
        var to = LinearGradient((0, new Color(0xFF, 0x26, 0x26, 0x3A)), (1, new Color(0xFF, 0x1A, 0x1A, 0x28)));

        var atStart = (ILinearGradientBrush)PremultipliedBrushTransition.InterpolateBrush(0.0, from, to)!;
        var atEnd = (ILinearGradientBrush)PremultipliedBrushTransition.InterpolateBrush(1.0, from, to)!;

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(from.GradientStops[i].Color, atStart.GradientStops[i].Color);
            Assert.Equal(to.GradientStops[i].Color, atEnd.GradientStops[i].Color);
        }
    }

    [Fact]
    public void NullEndpoint_SnapsInsteadOfThrowing()
    {
        var solid = new ImmutableSolidColorBrush(DarkSurface);
        Assert.Same(solid, PremultipliedBrushTransition.InterpolateBrush(0.6, null, solid));
        Assert.Same(solid, PremultipliedBrushTransition.InterpolateBrush(0.4, solid, null));
    }

    private static readonly Color GBlack = new(0xFF, 0, 0, 0);
    private static readonly Color GWhite = new(0xFF, 0xFF, 0xFF, 0xFF);

    private static ImmutableLinearGradientBrush LinearGradient(ImmutableTransform? transform, RelativePoint origin)
    {
        var stops = new[] { new ImmutableGradientStop(0, GBlack), new ImmutableGradientStop(1, GWhite) };
        return new ImmutableLinearGradientBrush(
            stops, 1, transform, origin, GradientSpreadMethod.Pad,
            new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));
    }

    [Fact]
    public void Gradient_InterpolatesTransformOrigin_NotConstant()
    {
        // Regression guard: the brush TransformOrigin must animate (matching Avalonia's stock animator),
        // not stay pinned to the 'from' value for the whole transition.
        var from = LinearGradient(null, new RelativePoint(0, 0, RelativeUnit.Relative));
        var to = LinearGradient(null, new RelativePoint(1, 1, RelativeUnit.Relative));

        var mid = (ILinearGradientBrush)PremultipliedBrushTransition.InterpolateBrush(0.5, from, to)!;

        Assert.Equal(0.5, mid.TransformOrigin.Point.X, 3);
        Assert.Equal(0.5, mid.TransformOrigin.Point.Y, 3);
        Assert.Equal(RelativeUnit.Relative, mid.TransformOrigin.Unit);
    }

    [Fact]
    public void Gradient_PreservesBrushTransform_NeverNull()
    {
        // Regression guard for the flagged divergence: a gradient carrying a brush-level Transform must
        // keep it through the transition. For non-TransformOperations transforms, Avalonia holds the
        // (cloned) source transform — it must never collapse to null mid-animation.
        var fromMatrix = new Matrix(1, 0, 0, 1, 10, 0); // translate(10,0)
        var toMatrix = new Matrix(1, 0, 0, 1, 20, 0);   // translate(20,0)
        var origin = new RelativePoint(0, 0, RelativeUnit.Relative);
        var from = LinearGradient(new ImmutableTransform(fromMatrix), origin);
        var to = LinearGradient(new ImmutableTransform(toMatrix), origin);

        foreach (var t in new[] { 0.0, 0.5, 1.0 })
        {
            var mid = (ILinearGradientBrush)PremultipliedBrushTransition.InterpolateBrush(t, from, to)!;
            Assert.NotNull(mid.Transform);
            Assert.Equal(fromMatrix, mid.Transform!.Value);
        }
    }
}
