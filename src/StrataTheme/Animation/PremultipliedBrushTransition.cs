using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;

namespace StrataTheme.Animation;

/// <summary>
/// A drop-in replacement for <see cref="Avalonia.Animation.BrushTransition"/> that interpolates
/// brush colours using <b>premultiplied alpha</b> in linear sRGB space.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's built-in brush transition interpolates straight (non-premultiplied) ARGB. The XAML
/// keyword <c>Transparent</c> is <c>#00FFFFFF</c> — white at zero alpha — so a transition from
/// <c>Transparent</c> to an opaque colour ramps the alpha while the RGB channels stay white. The
/// composited result briefly overshoots brighter than either endpoint, producing a "fade through
/// white" flash. It is invisible over light surfaces but very noticeable over dark ones.
/// </para>
/// <para>
/// Premultiplying the colour before interpolating keeps the hue constant while only the alpha
/// animates, which removes the flash. For fully opaque endpoints the result is numerically
/// identical to the built-in transition, so this is a safe drop-in replacement everywhere.
/// </para>
/// <para><b>XAML usage:</b></para>
/// <code>
/// xmlns:sa="using:StrataTheme.Animation"
/// ...
/// &lt;Transitions&gt;
///   &lt;sa:PremultipliedBrushTransition Property="Background" Duration="0:0:0.12" /&gt;
/// &lt;/Transitions&gt;
/// </code>
/// </remarks>
public sealed class PremultipliedBrushTransition : InterpolatingTransitionBase<IBrush?>
{
    /// <inheritdoc />
    protected override IBrush? Interpolate(double progress, IBrush? from, IBrush? to)
        => InterpolateBrush(progress, from, to);

    /// <summary>
    /// Premultiplied-alpha interpolation between two brushes. Exposed for unit testing; mirrors the
    /// shape of Avalonia's <see cref="Avalonia.Animation.BrushTransition"/> (solid, gradient, and
    /// solid&lt;-&gt;gradient pairings) but interpolates colours premultiplied so transparent endpoints
    /// don't drag their RGB through the transition.
    /// </summary>
    internal static IBrush? InterpolateBrush(double progress, IBrush? from, IBrush? to)
    {
        if (from is null || to is null)
            return progress >= 0.5 ? to : from;

        // Gradient endpoints (including solid <-> gradient, matching Avalonia's BrushTransition).
        if (from is IGradientBrush || to is IGradientBrush)
            return InterpolateGradient(progress, from, to);

        if (from is ISolidColorBrush a && to is ISolidColorBrush b)
        {
            return new ImmutableSolidColorBrush(
                InterpolateColorPremultiplied(progress, a.Color, b.Color),
                Lerp(a.Opacity, b.Opacity, progress));
        }

        // Incompatible brush kinds: fall back to a hard switch like Avalonia does.
        return progress >= 0.5 ? to : from;
    }

    private static IBrush? InterpolateGradient(double progress, IBrush from, IBrush to)
    {
        // Normalise solid endpoints to a gradient that mirrors the opposite endpoint's geometry,
        // mirroring Avalonia's BrushTransition behaviour.
        if (from is IGradientBrush fromGradient && to is ISolidColorBrush toSolid)
            return InterpolateMatchingGradients(progress, fromGradient, ToGradient(fromGradient, toSolid));

        if (to is IGradientBrush toGradient && from is ISolidColorBrush fromSolid)
            return InterpolateMatchingGradients(progress, ToGradient(toGradient, fromSolid), toGradient);

        if (from is IGradientBrush fg && to is IGradientBrush tg)
            return InterpolateMatchingGradients(progress, fg, tg);

        return progress >= 0.5 ? to : from;
    }

    private static IBrush InterpolateMatchingGradients(double progress, IGradientBrush from, IGradientBrush to)
    {
        var stops = InterpolateStops(progress, from.GradientStops, to.GradientStops);
        var opacity = Lerp(from.Opacity, to.Opacity, progress);

        switch (from)
        {
            case ILinearGradientBrush fromLinear when to is ILinearGradientBrush toLinear:
                return new ImmutableLinearGradientBrush(
                    stops, opacity, InterpolateTransform(progress, from.Transform, to.Transform),
                    LerpPoint(from.TransformOrigin, to.TransformOrigin, progress), from.SpreadMethod,
                    LerpPoint(fromLinear.StartPoint, toLinear.StartPoint, progress),
                    LerpPoint(fromLinear.EndPoint, toLinear.EndPoint, progress));

            case IRadialGradientBrush fromRadial when to is IRadialGradientBrush toRadial:
                return new ImmutableRadialGradientBrush(
                    stops, opacity, InterpolateTransform(progress, from.Transform, to.Transform),
                    LerpPoint(from.TransformOrigin, to.TransformOrigin, progress), from.SpreadMethod,
                    LerpPoint(fromRadial.Center, toRadial.Center, progress),
                    LerpPoint(fromRadial.GradientOrigin, toRadial.GradientOrigin, progress),
                    LerpScalar(fromRadial.RadiusX, toRadial.RadiusX, progress),
                    LerpScalar(fromRadial.RadiusY, toRadial.RadiusY, progress));

            case IConicGradientBrush fromConic when to is IConicGradientBrush toConic:
                return new ImmutableConicGradientBrush(
                    stops, opacity, InterpolateTransform(progress, from.Transform, to.Transform),
                    LerpPoint(from.TransformOrigin, to.TransformOrigin, progress), from.SpreadMethod,
                    LerpPoint(fromConic.Center, toConic.Center, progress),
                    Lerp(fromConic.Angle, toConic.Angle, progress));

            default:
                return progress >= 0.5 ? to : from;
        }
    }

    private static IReadOnlyList<ImmutableGradientStop> InterpolateStops(
        double progress, IReadOnlyList<IGradientStop> from, IReadOnlyList<IGradientStop> to)
    {
        var count = Math.Max(from.Count, to.Count);
        var stops = new ImmutableGradientStop[count];

        for (int i = 0, fromIndex = 0, toIndex = 0; i < count; i++)
        {
            stops[i] = new ImmutableGradientStop(
                Lerp(from[fromIndex].Offset, to[toIndex].Offset, progress),
                InterpolateColorPremultiplied(progress, from[fromIndex].Color, to[toIndex].Color));

            if (fromIndex < from.Count - 1)
                fromIndex++;
            if (toIndex < to.Count - 1)
                toIndex++;
        }

        return stops;
    }

    private static IGradientBrush ToGradient(IGradientBrush template, ISolidColorBrush solid)
    {
        IReadOnlyList<ImmutableGradientStop> stops;
        if (template.GradientStops.Count == 0)
        {
            stops = new[] { new ImmutableGradientStop(0, solid.Color), new ImmutableGradientStop(1, solid.Color) };
        }
        else
        {
            var built = new ImmutableGradientStop[template.GradientStops.Count];
            for (var i = 0; i < template.GradientStops.Count; i++)
                built[i] = new ImmutableGradientStop(template.GradientStops[i].Offset, solid.Color);
            stops = built;
        }

        switch (template)
        {
            case IRadialGradientBrush radial:
                return new ImmutableRadialGradientBrush(
                    stops, solid.Opacity, CloneTransform(radial.Transform), radial.TransformOrigin, radial.SpreadMethod,
                    radial.Center, radial.GradientOrigin, radial.RadiusX, radial.RadiusY);
            case IConicGradientBrush conic:
                return new ImmutableConicGradientBrush(
                    stops, solid.Opacity, CloneTransform(conic.Transform), conic.TransformOrigin, conic.SpreadMethod,
                    conic.Center, conic.Angle);
            case ILinearGradientBrush linear:
                return new ImmutableLinearGradientBrush(
                    stops, solid.Opacity, CloneTransform(linear.Transform), linear.TransformOrigin, linear.SpreadMethod,
                    linear.StartPoint, linear.EndPoint);
            default:
                return new ImmutableLinearGradientBrush(stops, solid.Opacity);
        }
    }

    private static RelativePoint LerpPoint(RelativePoint from, RelativePoint to, double progress)
    {
        if (from.Unit != to.Unit)
            return progress >= 0.5 ? to : from;

        return new RelativePoint(
            Lerp(from.Point.X, to.Point.X, progress),
            Lerp(from.Point.Y, to.Point.Y, progress),
            from.Unit);
    }

    private static RelativeScalar LerpScalar(RelativeScalar from, RelativeScalar to, double progress)
    {
        if (from.Unit != to.Unit)
            return progress >= 0.5 ? to : from;

        return new RelativeScalar(Lerp(from.Scalar, to.Scalar, progress), from.Unit);
    }

    private static ImmutableTransform? InterpolateTransform(double progress, ITransform? from, ITransform? to)
    {
        // Mirrors Avalonia's GradientBrushAnimator.InterpolateTransform: blend two TransformOperations,
        // otherwise hold the (cloned) source transform, otherwise null. Passing null (as before) would
        // briefly drop a gradient's brush-level transform for the duration of the transition.
        if (from is TransformOperations a && to is TransformOperations b)
            return new ImmutableTransform(TransformOperations.Interpolate(a, b, progress).Value);

        return CloneTransform(from);
    }

    private static ImmutableTransform? CloneTransform(ITransform? transform)
        => transform is null ? null : new ImmutableTransform(transform.Value);

    private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;

    /// <summary>
    /// Interpolates two colours with premultiplied alpha in linear sRGB space. This matches Avalonia's
    /// gamma-correct interpolation but premultiplies first, so a transparent endpoint does not drag its
    /// (white) RGB through the transition.
    /// </summary>
    internal static Color InterpolateColorPremultiplied(double progress, Color from, Color to)
    {
        var fromA = from.A / 255d;
        var toA = to.A / 255d;

        // Linearise RGB, then premultiply by (linear) alpha.
        var fromR = SrgbToLinear(from.R / 255d) * fromA;
        var fromG = SrgbToLinear(from.G / 255d) * fromA;
        var fromB = SrgbToLinear(from.B / 255d) * fromA;

        var toR = SrgbToLinear(to.R / 255d) * toA;
        var toG = SrgbToLinear(to.G / 255d) * toA;
        var toB = SrgbToLinear(to.B / 255d) * toA;

        var a = fromA + progress * (toA - fromA);
        var r = fromR + progress * (toR - fromR);
        var g = fromG + progress * (toG - fromG);
        var b = fromB + progress * (toB - fromB);

        // Un-premultiply and convert back to gamma sRGB.
        if (a > 0d)
        {
            r /= a;
            g /= a;
            b /= a;
        }

        return new Color(
            (byte)Math.Round(Math.Clamp(a, 0d, 1d) * 255d),
            (byte)Math.Round(Math.Clamp(LinearToSrgb(r), 0d, 1d) * 255d),
            (byte)Math.Round(Math.Clamp(LinearToSrgb(g), 0d, 1d) * 255d),
            (byte)Math.Round(Math.Clamp(LinearToSrgb(b), 0d, 1d) * 255d));
    }

    private static double SrgbToLinear(double value) =>
        value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);

    private static double LinearToSrgb(double value) =>
        value <= 0.0031308d ? value * 12.92d : Math.Pow(value, 1.0d / 2.4d) * 1.055d - 0.055d;
}
