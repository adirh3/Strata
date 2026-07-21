using System.Reflection;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;

namespace StrataTheme;

/// <summary>
/// Creates composition keyframe animations with a stateless default easing.
/// </summary>
/// <remarks>
/// Avalonia versions from at least 11.3.12 through 12.1.0 share one mutable spline
/// easing across a compositor. A non-finite progress value can permanently poison that
/// spline and freeze unrelated animations. These factories replace the shared default
/// before any keyframes are inserted.
/// </remarks>
public static class StableCompositionAnimations
{
    private static readonly Easing StableDefaultEasing = new StableCubicBezierEasing();
    private static readonly FieldInfo DefaultEasingField = ResolveDefaultEasingField();

    /// <summary>Creates a scalar keyframe animation protected from shared spline state.</summary>
    public static ScalarKeyFrameAnimation CreateStableScalarKeyFrameAnimation(this Compositor compositor)
    {
        EnsureStableDefaultEasing(compositor);
        return compositor.CreateScalarKeyFrameAnimation();
    }

    /// <summary>Creates a Vector3 keyframe animation protected from shared spline state.</summary>
    public static Vector3KeyFrameAnimation CreateStableVector3KeyFrameAnimation(this Compositor compositor)
    {
        EnsureStableDefaultEasing(compositor);
        return compositor.CreateVector3KeyFrameAnimation();
    }

    /// <summary>Creates a Vector3D keyframe animation protected from shared spline state.</summary>
    public static Vector3DKeyFrameAnimation CreateStableVector3DKeyFrameAnimation(this Compositor compositor)
    {
        EnsureStableDefaultEasing(compositor);
        return compositor.CreateVector3DKeyFrameAnimation();
    }

    internal static Easing EasingForTests => StableDefaultEasing;

    internal static bool IsInstalledForTests(Compositor compositor) =>
        ReferenceEquals(DefaultEasingField.GetValue(compositor), StableDefaultEasing);

    private static void EnsureStableDefaultEasing(Compositor compositor)
    {
        ArgumentNullException.ThrowIfNull(compositor);

        if (!ReferenceEquals(DefaultEasingField.GetValue(compositor), StableDefaultEasing))
            DefaultEasingField.SetValue(compositor, StableDefaultEasing);
    }

    private static FieldInfo ResolveDefaultEasingField()
    {
        var field = typeof(Compositor).GetField(
            "<DefaultEasing>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field is null || !typeof(IEasing).IsAssignableFrom(field.FieldType))
        {
            throw new MissingFieldException(
                typeof(Compositor).FullName,
                "<DefaultEasing>k__BackingField");
        }

        return field;
    }

    private sealed class StableCubicBezierEasing : Easing
    {
        private const double X1 = 0.25;
        private const double Y1 = 0.1;
        private const double X2 = 0.25;
        private const double Y2 = 1.0;
        private const int SearchIterations = 24;

        public override double Ease(double progress)
        {
            if (double.IsNaN(progress) || progress <= 0)
                return 0;

            if (progress >= 1)
                return 1;

            var lower = 0d;
            var upper = 1d;

            for (var i = 0; i < SearchIterations; i++)
            {
                var parameter = (lower + upper) / 2d;
                if (CubicCoordinate(parameter, X1, X2) < progress)
                    lower = parameter;
                else
                    upper = parameter;
            }

            return CubicCoordinate((lower + upper) / 2d, Y1, Y2);
        }

        private static double CubicCoordinate(double parameter, double first, double second)
        {
            var inverse = 1d - parameter;
            return (3d * inverse * inverse * parameter * first)
                + (3d * inverse * parameter * parameter * second)
                + (parameter * parameter * parameter);
        }
    }
}
