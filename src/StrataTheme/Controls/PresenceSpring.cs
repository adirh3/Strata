using System;

namespace StrataTheme.Controls;

/// <summary>
/// A pure, deterministic critically-/under-/over-damped spring response — the analytic solution of
/// <c>p'' + 2·ζ·ω·p' + ω²·p = 0</c> evaluated at an arbitrary time, where <c>p</c> is the signed
/// displacement from the target. It is the motion <i>model</i> that drives <see cref="StrataPresence"/>'s
/// focus travel.
/// </summary>
/// <remarks>
/// <para>
/// This type exists so the presence's "aliveness" is <b>objectively assessable</b>. Keyframe/easing
/// tweens reset velocity to zero whenever their target is re-aimed mid-flight (the rapid focus-follow
/// timer does this many times per second), which reads as a stuttery "teleport". A spring instead
/// <i>preserves velocity</i> across re-targets: re-seeding a new trajectory from the live
/// <c>(position, velocity)</c> keeps the motion C¹-continuous, so it flows.
/// </para>
/// <para>
/// Because the response is a closed-form function of time (no per-frame integrator state), the live
/// <c>(position, velocity)</c> can be read at any instant — both to re-seed a re-aim and to sample the
/// curve into render-thread keyframes — and the continuity guarantee can be unit-tested deterministically
/// without rendering a single pixel.
/// </para>
/// </remarks>
internal static class PresenceSpring
{
    /// <summary>
    /// Evaluates the damped-spring response at time <paramref name="t"/> (seconds) for a mass released
    /// from displacement <paramref name="p0"/> (position − target) with initial velocity
    /// <paramref name="v0"/>, returning the displacement-from-target and velocity at that time.
    /// </summary>
    /// <param name="p0">Initial displacement from the target.</param>
    /// <param name="v0">Initial velocity.</param>
    /// <param name="omega">Undamped angular frequency ω (rad/s); larger = stiffer/quicker.</param>
    /// <param name="zeta">Damping ratio ζ. &lt;1 underdamped (slight overshoot), 1 critical, &gt;1 overdamped.</param>
    /// <param name="t">Elapsed time in seconds (values ≤ 0 return the initial state).</param>
    public static (double Displacement, double Velocity) Evaluate(double p0, double v0, double omega, double zeta, double t)
    {
        if (omega <= 0 || t <= 0)
            return (p0, v0);

        if (zeta < 1.0 - 1e-6)
        {
            // Underdamped: a decaying sinusoid.
            var wd = omega * Math.Sqrt(1.0 - zeta * zeta);
            var e = Math.Exp(-zeta * omega * t);
            var c = Math.Cos(wd * t);
            var s = Math.Sin(wd * t);
            var p = e * (p0 * c + (v0 + zeta * omega * p0) / wd * s);
            var v = e * (v0 * c - (omega * omega * p0 + zeta * omega * v0) / wd * s);
            return (p, v);
        }

        if (zeta <= 1.0 + 1e-6)
        {
            // Critically damped: fastest settle with no overshoot.
            var e = Math.Exp(-omega * t);
            var b = v0 + omega * p0;
            var p = (p0 + b * t) * e;
            var v = (v0 - omega * b * t) * e;
            return (p, v);
        }

        // Overdamped: two real exponential modes.
        var disc = omega * Math.Sqrt(zeta * zeta - 1.0);
        var r1 = -zeta * omega + disc;
        var r2 = -zeta * omega - disc;
        var a = (v0 - r2 * p0) / (r1 - r2);
        var bb = p0 - a;
        var e1 = Math.Exp(r1 * t);
        var e2 = Math.Exp(r2 * t);
        return (a * e1 + bb * e2, a * r1 * e1 + bb * r2 * e2);
    }

    /// <summary>
    /// Time (seconds) for the response to decay to a negligible fraction of its initial displacement —
    /// used to size a finite sampling horizon for the render-thread keyframes. The envelope decays as
    /// <c>e^(−ζ·ω·t)</c> (using the slower real mode when overdamped), so <c>t ≈ 7/(ζ·ω)</c> reaches
    /// roughly 0.1%.
    /// </summary>
    public static double SettleSeconds(double omega, double zeta)
    {
        if (omega <= 0)
            return 0;

        // Effective decay rate of the slowest mode.
        var rate = zeta >= 1.0
            ? zeta * omega - omega * Math.Sqrt(Math.Max(0.0, zeta * zeta - 1.0)) // slower overdamped root
            : zeta * omega;
        rate = Math.Max(rate, 1e-3);
        return 7.0 / rate;
    }
}
