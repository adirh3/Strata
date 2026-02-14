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

    static PopupAnimationHelper()
    {
        EnableOpenAnimationProperty.Changed.AddClassHandler<Popup>(OnEnableOpenAnimationChanged);
    }

    public static bool GetEnableOpenAnimation(Popup popup) =>
        popup.GetValue(EnableOpenAnimationProperty);

    public static void SetEnableOpenAnimation(Popup popup, bool value) =>
        popup.SetValue(EnableOpenAnimationProperty, value);

    private static void OnEnableOpenAnimationChanged(Popup popup, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            popup.Opened += OnPopupOpened;
        else
            popup.Opened -= OnPopupOpened;
    }

    private static void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup { Host: PopupRoot popupRoot })
            return;

        var visual = ElementComposition.GetElementVisual(popupRoot);
        if (visual is null)
            return;

        var compositor = visual.Compositor;

        var bounds = popupRoot.Bounds;
        var w = bounds.Width > 0 ? bounds.Width : popupRoot.DesiredSize.Width;
        var h = bounds.Height > 0 ? bounds.Height : popupRoot.DesiredSize.Height;

        // Scale from center — looks like the combobox itself is expanding
        visual.CenterPoint = new Vector3((float)(w / 2f), (float)(h / 2f), 0f);

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
}
