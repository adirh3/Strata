using System;
using Avalonia;
using Avalonia.Controls;

namespace StrataTheme;

/// <summary>
/// Attached property that enables shared popup open and close motion for overlay
/// controls such as ContextMenu, MenuFlyoutPresenter, FlyoutPresenter, and ToolTip.
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
        control.AttachedToVisualTree -= OnAttached;
        if (e.NewValue is true)
            control.AttachedToVisualTree += OnAttached;
        PopupAnimationHelper.RegisterOverlay(control);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;

        PopupAnimationHelper.RegisterOverlay(control);
    }
}
