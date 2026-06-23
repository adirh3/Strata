using System;

namespace StrataTheme.Controls;

/// <summary>
/// Pure, side-effect-free geometry for the presence field — the single source of truth for <i>where</i>
/// every lobe sits given the viewport size and a normalized focus point. Deliberately free of Avalonia
/// composition so the coordinate system can be reasoned about and unit-tested in isolation: presence
/// positions are notoriously easy to get subtly wrong across window sizes (a glow that drifts off-centre
/// on a wide monitor, an "island split" that separates vertically instead of horizontally), and routing
/// every placement through one tested module is what keeps them provable instead of hand-tuned.
/// </summary>
/// <remarks>
/// <para><b>Coordinate system.</b> Lobes are laid out <i>centre-aligned</i> in the field panel, so a
/// composition <c>Offset</c> of (0,0) places a lobe's centre at the panel centre (<c>w/2, h/2</c>). Every
/// offset returned here is in <b>pixels, relative to that centred home</b>. A normalized point
/// <c>f=(fx,fy)</c> in [0,1]² denotes a fraction of the viewport; <c>f=(0.5,0.5)</c> is dead centre.</para>
/// <para><b>Aspect-correctness.</b> X always scales with width and Y with height <i>independently</i>, so
/// the same normalized focus lands at the same <i>visible</i> fraction of the window at any size, and
/// <c>focus=(0.5,0.5)</c> is always dead centre (zero offset) regardless of the aspect ratio.</para>
/// </remarks>
internal static class PresenceGeometry
{
    /// <summary>
    /// The host <c>Offset</c> (px, from the centred home) that makes a field lobe gravitate toward the
    /// focus point. At full <paramref name="reach"/>×<paramref name="follow"/> the lobe's centre lands
    /// exactly on the focus point (<c>focus·size</c>); lower follow/reach damp that travel back toward
    /// home. <c>focus=(0.5,0.5)</c> always returns (0,0) — dead centre at any window size.
    /// </summary>
    public static (double X, double Y) FieldOffset(
        double focusX, double focusY, double follow, double reach, double w, double h)
    {
        var k = follow * reach;
        return ((Clamp01(focusX) - 0.5) * w * k, (Clamp01(focusY) - 0.5) * h * k);
    }

    /// <summary>
    /// The host <c>Offset</c> (px, from the centred home) that centres the companion pool on an island
    /// whose centre is the normalized point (<paramref name="islandX"/>, <paramref name="islandY"/>). A
    /// right-side island (<c>islandX &gt; 0.5</c>) yields a positive X — the pool travels <b>horizontally
    /// toward it</b> — with only as much Y as the island is off mid-height, so the split reads as a
    /// side-by-side separation rather than a vertical one. The companion always lands centred ON the
    /// island (full travel, no follow damping) so it visibly illuminates the opened surface.
    /// </summary>
    public static (double X, double Y) CompanionOffset(double islandX, double islandY, double w, double h)
        => ((Clamp01(islandX) - 0.5) * w, (Clamp01(islandY) - 0.5) * h);

    /// <summary>
    /// A lobe's idle-drift home offset (px) around the field centre. The spread is scaled to the
    /// <b>short edge</b> (not w/h) so on a wide window the multi-hue lobes stay clustered as one coherent
    /// pool instead of being flung apart into a horizontal smear.
    /// </summary>
    public static (double X, double Y) AnchorSpread(double anchorX, double anchorY, double w, double h)
    {
        var s = Math.Min(w, h);
        return ((anchorX - 0.5) * s, (anchorY - 0.5) * s);
    }

    /// <summary>
    /// A lobe's diameter (px). The chat canvas is typically a <i>tall, width-capped column</i>, so sizing
    /// purely off the short edge (the capped width) leaves the aura looking small in a big/tall window.
    /// The pool is therefore <b>grown toward the long edge</b> so it fills more of the canvas — but capped
    /// at <c>1.4×</c> the short edge so it never dwarfs the narrow axis and a focus glide still reads as
    /// travel (the regression that once made lobes larger than the viewport and their motion imperceptible).
    /// On a square canvas this equals the short edge exactly. <paramref name="fuller"/> (compact surfaces)
    /// keeps the established short-edge pool, just a touch fuller — long-edge growth is for large surfaces.
    /// </summary>
    public static double LobeDiameter(double sizeFactor, double w, double h, bool fuller)
    {
        var shortEdge = Math.Min(w, h);
        if (fuller)
            return sizeFactor * shortEdge * 1.2;

        var longEdge = Math.Max(w, h);
        var basis = Math.Min(0.55 * shortEdge + 0.45 * longEdge, shortEdge * 1.4);
        return sizeFactor * basis;
    }

    /// <summary>
    /// The radial-gradient origin (relative 0..1 inside a lobe) that makes its light lean <b>away</b> from
    /// the edge the field is hugging. When the pool sits low (<paramref name="focusY"/>→1) the bright core
    /// drops toward its base and the soft tail reaches <i>up</i> — so the glow reads as light cast from the
    /// presence's position out into the canvas, not a symmetric blob; high pools cast down, left pools cast
    /// right, and so on. <c>f=(0.5,0.5)</c> is a centred, symmetric origin. The lean is clamped to
    /// <paramref name="max"/> so the origin always stays well inside the lobe (the falloff never hits a
    /// hard edge), and <paramref name="bias"/> sets how strongly it leans.
    /// </summary>
    public static (double X, double Y) LightOrigin(
        double focusX, double focusY, double bias = 0.34, double max = 0.20)
    {
        var ox = 0.5 + Clamp((Clamp01(focusX) - 0.5) * 2.0 * bias, -max, max);
        var oy = 0.5 + Clamp((Clamp01(focusY) - 0.5) * 2.0 * bias, -max, max);
        return (ox, oy);
    }

    /// <summary>
    /// Inverse of <see cref="FieldOffset"/>: the normalized (0..1) viewport fraction a lobe centre lands
    /// at given its host offset. Lets tests and the headless position-probe assert that a computed offset
    /// actually places the pool where the focus point asked it to, at any window size.
    /// </summary>
    public static (double X, double Y) CenterFraction(double offsetX, double offsetY, double w, double h)
    {
        if (w <= 0 || h <= 0)
            return (0.5, 0.5);
        return ((w / 2 + offsetX) / w, (h / 2 + offsetY) / h);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
