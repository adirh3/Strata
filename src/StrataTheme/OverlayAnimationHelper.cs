using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme;

/// <summary>
/// Attached property that enables a Composition API entrance animation for overlay
/// controls such as ContextMenu, MenuFlyoutPresenter, FlyoutPresenter, and ToolTip.
/// Plays a subtle scale + fade animation each time the control appears inside a PopupRoot.
/// </summary>
public static class OverlayAnimationHelper
{
    public static readonly AttachedProperty<bool> AnimateOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "AnimateOpen", typeof(OverlayAnimationHelper));

    static OverlayAnimationHelper()
    {
        AnimateOpenProperty.Changed.AddClassHandler<Control>(OnChanged);
    }

    public static bool GetAnimateOpen(Control control) =>
        control.GetValue(AnimateOpenProperty);

    public static void SetAnimateOpen(Control control, bool value) =>
        control.SetValue(AnimateOpenProperty, value);

    private static void OnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            control.AttachedToVisualTree += OnAttached;
        else
            control.AttachedToVisualTree -= OnAttached;
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;

        // Only animate when appearing inside a popup (native or overlay)
        if (e.Root is not PopupRoot && !IsInsideOverlayPopup(control))
            return;

        Dispatcher.UIThread.Post(() => PlayEntrance(control), DispatcherPriority.Loaded);
    }

    private static bool IsInsideOverlayPopup(Control control)
    {
        Visual? v = control.GetVisualParent();
        while (v is not null)
        {
            if (v is OverlayPopupHost)
                return true;
            v = v.GetVisualParent();
        }
        return false;
    }

    private static void PlayEntrance(Control control)
    {
        var visual = ElementComposition.GetElementVisual(control);
        if (visual is null)
            return;

        var compositor = visual.Compositor;

        var bounds = control.Bounds;
        var w = bounds.Width > 0 ? bounds.Width : control.DesiredSize.Width;

        // Scale from top-center — popup appears to emerge from trigger point
        visual.CenterPoint = new Vector3((float)(w / 2f), 0f, 0f);

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.97f, 0.95f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(160);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.Target = "Opacity";
        opacityAnim.InsertKeyFrame(0f, 0f);
        opacityAnim.InsertKeyFrame(0.35f, 1f);
        opacityAnim.InsertKeyFrame(1f, 1f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(160);

        var group = compositor.CreateAnimationGroup();
        group.Add(scaleAnim);
        group.Add(opacityAnim);

        visual.StartAnimationGroup(group);
    }
}
