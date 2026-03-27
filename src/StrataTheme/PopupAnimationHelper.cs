using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering.Composition;

namespace StrataTheme;

/// <summary>
/// Attached property that enables Composition API expand animation on popup open.
/// Inspired by Blast's ImplicitAnimationsExtension — the only reliable way to
/// animate popups in Avalonia (PopupRoot lives in a separate visual tree).
/// </summary>
public static class PopupAnimationHelper
{
    public static readonly AttachedProperty<bool> EnableOpenAnimationProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>(
            "EnableOpenAnimation", typeof(PopupAnimationHelper));

    public static readonly AttachedProperty<bool> EnableOverlayAnimationProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>(
            "EnableOverlayAnimation", typeof(PopupAnimationHelper));

    static PopupAnimationHelper()
    {
        EnableOpenAnimationProperty.Changed.AddClassHandler<Popup>(OnEnableOpenAnimationChanged);
        EnableOverlayAnimationProperty.Changed.AddClassHandler<Popup>(OnEnableOverlayAnimationChanged);
    }

    public static bool GetEnableOpenAnimation(Popup popup) =>
        popup.GetValue(EnableOpenAnimationProperty);

    public static void SetEnableOpenAnimation(Popup popup, bool value) =>
        popup.SetValue(EnableOpenAnimationProperty, value);

    public static bool GetEnableOverlayAnimation(Popup popup) =>
        popup.GetValue(EnableOverlayAnimationProperty);

    public static void SetEnableOverlayAnimation(Popup popup, bool value) =>
        popup.SetValue(EnableOverlayAnimationProperty, value);

    private static void OnEnableOpenAnimationChanged(Popup popup, AvaloniaPropertyChangedEventArgs e)
    {
        // Always unsubscribe first to prevent accumulating duplicate handlers
        // when the property is set to true multiple times (e.g. style re-evaluation).
        popup.Opened -= OnPopupOpened;

        if (e.NewValue is true)
            popup.Opened += OnPopupOpened;
    }

    private static void OnEnableOverlayAnimationChanged(Popup popup, AvaloniaPropertyChangedEventArgs e)
    {
        popup.Opened -= OnPopupOpenedOverlay;

        if (e.NewValue is true)
            popup.Opened += OnPopupOpenedOverlay;
    }

    private static void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup { Child: Control popupChild } popup)
            return;

        var visual = ElementComposition.GetElementVisual(popupChild);
        if (visual is null)
            return;

        var compositor = visual.Compositor;

        var bounds = popupChild.Bounds;
        var w = bounds.Width > 0 ? bounds.Width : popupChild.DesiredSize.Width;
        var h = bounds.Height > 0 ? bounds.Height : popupChild.DesiredSize.Height;

        // Anchor the scale origin at the placement target (e.g. the ComboBox)
        // so the popup appears to expand directly from the control that opened it.
        float centerX = (float)(w / 2f);
        float centerY = (float)(h / 2f);

        if (popup.PlacementTarget is Visual target)
        {
            try
            {
                var targetCenter = new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
                var targetScreen = target.PointToScreen(targetCenter);
                var childScreen = popupChild.PointToScreen(new Point(0, 0));
                var scaling = TopLevel.GetTopLevel(popupChild)?.RenderScaling ?? 1.0;

                centerX = (float)((targetScreen.X - childScreen.X) / scaling);
                centerY = (float)((targetScreen.Y - childScreen.Y) / scaling);
            }
            catch
            {
                // Fallback to geometric center
            }
        }

        visual.CenterPoint = new Vector3(centerX, centerY, 0f);

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(1f, 0f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(200);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.Target = "Opacity";
        opacityAnim.InsertKeyFrame(0f, 0f);
        opacityAnim.InsertKeyFrame(0.3f, 1f);
        opacityAnim.InsertKeyFrame(1f, 1f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(200);

        var group = compositor.CreateAnimationGroup();
        group.Add(scaleAnim);
        group.Add(opacityAnim);

        visual.StartAnimationGroup(group);
    }

    private static void OnPopupOpenedOverlay(object? sender, EventArgs e)
    {
        if (sender is not Popup { Child: Control popupChild })
            return;

        var visual = ElementComposition.GetElementVisual(popupChild);
        if (visual is null)
            return;

        var compositor = visual.Compositor;

        var bounds = popupChild.Bounds;
        var w = bounds.Width > 0 ? bounds.Width : popupChild.DesiredSize.Width;

        var h = bounds.Height > 0 ? bounds.Height : popupChild.DesiredSize.Height;

        // Scale from center for a clean, uniform expansion
        visual.CenterPoint = new Vector3((float)(w / 2f), (float)(h / 2f), 0f);

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.88f, 0.88f, 1f));
        scaleAnim.InsertKeyFrame(0.55f, new Vector3(1.006f, 1.006f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(300);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.Target = "Opacity";
        opacityAnim.InsertKeyFrame(0f, 0f);
        opacityAnim.InsertKeyFrame(0.4f, 1f);
        opacityAnim.InsertKeyFrame(1f, 1f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(300);

        var group = compositor.CreateAnimationGroup();
        group.Add(scaleAnim);
        group.Add(opacityAnim);

        visual.StartAnimationGroup(group);
    }
}
