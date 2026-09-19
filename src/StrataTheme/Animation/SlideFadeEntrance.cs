using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace StrataTheme.Animation;

/// <summary>Reveals an attached entrance host without changing its measured layout.</summary>
public static class SlideFadeEntrance
{
    private static readonly TransformOperations RestingTransform =
        TransformOperations.Parse("translateY(0px)");

    /// <summary>
    /// Plays a short rise and fade on a host whose opacity, transform and transitions are owned
    /// by the entrance. Repeated calls replace the previous motion; detaching leaves the final state.
    /// </summary>
    public static void Play(Control host, double offsetY = 12, TimeSpan? duration = null)
    {
        if (!host.IsAttachedToVisualTree() || !host.IsEffectivelyVisible)
            return;

        var motionDuration = duration ?? TimeSpan.FromMilliseconds(260);
        host.Transitions = null;
        host.SetCurrentValue(Visual.OpacityProperty, 0d);
        host.SetCurrentValue(Visual.RenderTransformProperty,
            TransformOperations.Parse(FormattableString.Invariant($"translateY({offsetY}px)")));

        host.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = motionDuration * 0.7,
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = motionDuration,
                Easing = new CubicEaseOut(),
            },
        };

        // Keep the resting values underneath the transitions so interruption never strands a host.
        host.SetCurrentValue(Visual.OpacityProperty, 1d);
        host.SetCurrentValue(Visual.RenderTransformProperty, RestingTransform);
    }
}
