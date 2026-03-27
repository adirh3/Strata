using System;
using System.Numerics;
using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Tracks controls that already have an animation queued via Dispatcher.Post
    /// to prevent the same entrance animation from being scheduled twice.
    /// Both <see cref="OnChanged"/> and <see cref="OnAttached"/> can trigger an
    /// animation (depending on whether styles resolve before or after visual-tree
    /// attachment), so the table ensures only the first caller wins.
    /// </summary>
    private static readonly ConditionalWeakTable<Control, object> _pendingAnimation = new();

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
        // Always unsubscribe first to prevent accumulating duplicate handlers
        // when the property is set to true multiple times (e.g. style re-evaluation).
        control.AttachedToVisualTree -= OnAttached;

        if (e.NewValue is true)
        {
            control.AttachedToVisualTree += OnAttached;

            // If the property is set while the control is already inside a popup
            // (style evaluation can happen after visual-tree attachment), play now.
            if (IsInsidePopup(control))
                SchedulePlayEntrance(control);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;

        // Only animate when appearing inside a popup (native or overlay)
        if (e.RootVisual is not PopupRoot && !IsInsidePopup(control))
            return;

        SchedulePlayEntrance(control);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the control is currently inside a
    /// PopupRoot (native popup) or an OverlayPopupHost (overlay popup).
    /// </summary>
    private static bool IsInsidePopup(Control control)
    {
        Visual? v = control.GetVisualParent();
        while (v is not null)
        {
            if (v is PopupRoot or OverlayPopupHost)
                return true;
            v = v.GetVisualParent();
        }
        return false;
    }

    /// <summary>
    /// Posts a single <see cref="PlayEntrance"/> call for <paramref name="control"/>,
    /// de-duplicating when both <see cref="OnChanged"/> and <see cref="OnAttached"/>
    /// fire during the same popup-open cycle.  The guard entry stays in the table
    /// until the control detaches (popup closes) so any late re-fires are also blocked.
    /// </summary>
    private static void SchedulePlayEntrance(Control control)
    {
        if (_pendingAnimation.TryGetValue(control, out _))
            return;

        _pendingAnimation.Add(control, new object());

        // Clear the guard when the popup closes (control detaches from visual tree)
        // so the next open cycle can animate again.
        control.DetachedFromVisualTree += OnDetachedClearGuard;

        Dispatcher.UIThread.Post(() => PlayEntrance(control), DispatcherPriority.Loaded);
    }

    private static void OnDetachedClearGuard(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;

        control.DetachedFromVisualTree -= OnDetachedClearGuard;
        _pendingAnimation.Remove(control);
    }

    private static void PlayEntrance(Control control)
    {
        var visual = ElementComposition.GetElementVisual(control);
        if (visual is null)
            return;

        var compositor = visual.Compositor;

        var bounds = control.Bounds;
        var w = bounds.Width > 0 ? bounds.Width : control.DesiredSize.Width;
        var h = bounds.Height > 0 ? bounds.Height : control.DesiredSize.Height;

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
