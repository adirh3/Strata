using System;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace StrataTheme;

/// <summary>
/// Attached property that slides the selection indicator (PART_SelectedPipe) from
/// the previous item to the newly selected item using a Composition Offset animation.
/// Opacity is left entirely to XAML styles — only Offset is animated.
/// The animation ends at the natural layout offset so there's no residual drift.
/// </summary>
public static class SelectionIndicatorAnimationHelper
{
    private const string IndicatorName = "PART_SelectedPipe";

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<SelectingItemsControl, bool>(
            "Enable", typeof(SelectionIndicatorAnimationHelper));

    static SelectionIndicatorAnimationHelper()
    {
        EnableProperty.Changed.AddClassHandler<SelectingItemsControl>(OnEnableChanged);
    }

    public static bool GetEnable(SelectingItemsControl control) =>
        control.GetValue(EnableProperty);

    public static void SetEnable(SelectingItemsControl control, bool value) =>
        control.SetValue(EnableProperty, value);

    private static void OnEnableChanged(SelectingItemsControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            control.PropertyChanged += OnPropertyChanged;
        else
            control.PropertyChanged -= OnPropertyChanged;
    }

    private static Visual? FindIndicator(TemplatedControl container) =>
        container.GetVisualDescendants()
            .FirstOrDefault(v => v is Visual { Name: IndicatorName }) as Visual;

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not SelectingItemsControl control ||
            args.Property != SelectingItemsControl.SelectedIndexProperty ||
            args.NewValue is not int newIndex || newIndex < 0)
            return;

        var newContainer = control.ContainerFromIndex(newIndex) as TemplatedControl;
        if (newContainer is null)
            return;

        var nextInd = FindIndicator(newContainer);
        if (nextInd is null)
            return;

        // If we have a previous selection, slide from old to new
        if (args.OldValue is int oldIndex && oldIndex >= 0 && oldIndex != newIndex)
        {
            var oldContainer = control.ContainerFromIndex(oldIndex) as TemplatedControl;
            if (oldContainer is not null)
            {
                var prevInd = FindIndicator(oldContainer);
                if (prevInd is not null)
                {
                    var root = nextInd.GetVisualRoot() as Visual;
                    if (root is not null)
                    {
                        var prevPos = prevInd.TranslatePoint(new Point(0, 0), root);
                        var nextPos = nextInd.TranslatePoint(new Point(0, 0), root);
                        if (prevPos is not null && nextPos is not null)
                        {
                            // Delta: how far the new indicator needs to shift to appear at the old position
                            var dx = (float)(prevPos.Value.X - nextPos.Value.X);
                            var dy = (float)(prevPos.Value.Y - nextPos.Value.Y);
                            PlaySlideAnimation(nextInd, dx, dy);
                            return;
                        }
                    }
                }
            }
        }

        // No previous selection or couldn't resolve — just do a quick scale-in
        PlayScaleIn(nextInd);
    }

    private static void PlaySlideAnimation(Visual indicator, float dx, float dy)
    {
        var visual = ElementComposition.GetElementVisual(indicator);
        if (visual is null)
            return;

        var comp = visual.Compositor;
        var bounds = indicator.Bounds;
        bool isVertical = Math.Abs(dy) > Math.Abs(dx);

        // The visual's current Offset is its layout position. We animate FROM
        // (layout + delta) back TO (layout), so the indicator slides from old → new
        // and ends exactly where layout put it.
        var layoutOffset = visual.Offset;
        var startOffset = new Vector3(
            (float)layoutOffset.X + dx,
            (float)layoutOffset.Y + dy,
            (float)layoutOffset.Z);
        var endOffset = new Vector3(
            (float)layoutOffset.X,
            (float)layoutOffset.Y,
            (float)layoutOffset.Z);

        var offsetAnim = comp.CreateVector3KeyFrameAnimation();
        offsetAnim.Target = "Offset";
        offsetAnim.InsertKeyFrame(0f, startOffset);
        offsetAnim.InsertKeyFrame(1f, endOffset);
        offsetAnim.Duration = TimeSpan.FromMilliseconds(250);

        visual.StartAnimation("Offset", offsetAnim);

        // For vertical lists (ListBox): add a subtle elongation during travel
        // that settles with a soft overshoot — refined, not cartoony.
        if (isVertical)
        {
            visual.CenterPoint = new Avalonia.Vector3D(bounds.Width / 2, bounds.Height / 2, 0);

            var scaleAnim = comp.CreateVector3KeyFrameAnimation();
            scaleAnim.Target = "Scale";
            // Depart: slightly elongate in travel direction
            scaleAnim.InsertKeyFrame(0f,    new Vector3(1f, 1.15f, 1f));
            // Mid-travel: peak stretch
            scaleAnim.InsertKeyFrame(0.3f,  new Vector3(0.9f, 1.25f, 1f));
            // Arrive: subtle squish overshoot
            scaleAnim.InsertKeyFrame(0.65f, new Vector3(1.1f, 0.9f, 1f));
            // Settle
            scaleAnim.InsertKeyFrame(1f,    new Vector3(1f, 1f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(280);

            visual.StartAnimation("Scale", scaleAnim);
        }
    }

    private static void PlayScaleIn(Visual indicator)
    {
        var visual = ElementComposition.GetElementVisual(indicator);
        if (visual is null)
            return;

        var comp = visual.Compositor;
        var bounds = indicator.Bounds;
        bool isVertical = bounds.Height > bounds.Width;

        // Set center to middle
        visual.CenterPoint = new Avalonia.Vector3D(bounds.Width / 2, bounds.Height / 2, 0);

        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        if (isVertical)
        {
            scaleAnim.InsertKeyFrame(0f, new Vector3(1, 0, 1));
            scaleAnim.InsertKeyFrame(1f, new Vector3(1, 1, 1));
        }
        else
        {
            scaleAnim.InsertKeyFrame(0f, new Vector3(0, 1, 1));
            scaleAnim.InsertKeyFrame(1f, new Vector3(1, 1, 1));
        }
        scaleAnim.Duration = TimeSpan.FromMilliseconds(250);

        visual.StartAnimation("Scale", scaleAnim);
    }
}
