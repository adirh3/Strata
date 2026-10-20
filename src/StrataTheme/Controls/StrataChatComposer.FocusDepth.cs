using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

// Focus depth: the glass composer sits back a little at rest and comes forward while the user writes. The whole
// composer (glass, text and tools together) scales about its bottom edge on the render thread, so nothing is laid
// out again, nothing around it moves, and the motion stays smooth while the UI thread works. Writing happens at
// full size, where the text is crisp and the composer's own lists line up with what opened them.
public partial class StrataChatComposer
{
    /// <summary>The size the glass composer sits back to while nobody writes in it.</summary>
    internal const double RestingDepthScale = 0.98;

    private static readonly TimeSpan ComeForwardDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SettleBackDuration = TimeSpan.FromMilliseconds(350);
    // Both ways start gently and land softly, so the size reads as one continuous motion rather than a jump;
    // forward leans on its landing, back is even.
    private static readonly Easing ComeForwardEasing = new StableCubicBezierEasing(0.2, 0, 0, 1);
    private static readonly Easing SettleBackEasing = new StableCubicBezierEasing(0.4, 0, 0.2, 1);

    private CompositionVisual? _depthVisual;
    // The scale last given to _depthVisual; null until it has one from here.
    private double? _depthScale;
    private bool _depthEnabled;

    /// <summary>The scale the composer is at, or moving to.</summary>
    internal double FocusDepthScale => _depthScale ?? 1;

    /// <summary>Whether the composer moved to <see cref="FocusDepthScale"/>, rather than taking it up at once.</summary>
    internal bool FocusDepthAnimated { get; private set; }

    private bool HasFocusDepth =>
        Classes.Contains("glass") && !Classes.Contains("mobile") && !Classes.Contains("motion-disabled");

    private void UpdateFocusDepth()
    {
        var visual = this.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(this) : null;
        if (!ReferenceEquals(visual, _depthVisual))
        {
            // Every attach brings a new visual, at full size.
            _depthVisual = visual;
            _depthScale = null;
        }

        var enabled = HasFocusDepth;
        var wasEnabled = _depthEnabled;
        _depthEnabled = enabled;
        // A composer that never had a depth keeps whatever scale its host gives it.
        if (visual is null || (_depthScale is null && !enabled))
            return;

        var target = enabled && !IsEngaged ? RestingDepthScale : 1d;
        if (_depthScale == target)
            return;

        // Only writing moves the composer; a new look (glass, touch, motion off) is taken up at once.
        var animate = _depthScale is not null && enabled && wasEnabled;
        var forward = target > _depthScale;
        _depthScale = target;
        FocusDepthAnimated = animate;
        UpdateFocusDepthCenter();

        var scale = new Vector3D(target, target, 1);
        var animation = visual.Compositor.CreateStableVector3DKeyFrameAnimation();
        animation.Target = nameof(CompositionVisual.Scale);
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        if (animate)
        {
            var easing = forward ? ComeForwardEasing : SettleBackEasing;
            animation.Duration = forward ? ComeForwardDuration : SettleBackDuration;
            // The starting value is read on the render thread, so a change of mind turns round mid-motion.
            animation.InsertExpressionKeyFrame(0, "this.StartingValue", easing);
            animation.InsertKeyFrame(1, scale, easing);
        }
        else
        {
            // Setting Scale directly is skipped when the visual already holds that value here, though an
            // animation may have moved it since; a keyframe always lands.
            animation.Duration = TimeSpan.FromMilliseconds(1);
            animation.InsertKeyFrame(0, scale);
            animation.InsertKeyFrame(1, scale);
        }

        visual.StartAnimation(nameof(CompositionVisual.Scale), animation);
    }

    private void UpdateFocusDepthCenter()
    {
        if (_depthVisual is { } visual && _depthScale is not null)
            visual.CenterPoint = new Vector3D(Bounds.Width / 2, Bounds.Height, 0);
    }
}
