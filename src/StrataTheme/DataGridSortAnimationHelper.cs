using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.VisualTree;

namespace StrataTheme;

/// <summary>
/// Attached property that plays a staggered "materialize" animation on DataGrid rows
/// after a column sort — rows scale up from 0.97 vertically and fade in, creating
/// a modern cascading reveal (similar to Linear / Notion table animations).
/// </summary>
public static class DataGridSortAnimationHelper
{
    public static readonly AttachedProperty<bool> EnableSortAnimationProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>(
            "EnableSortAnimation", typeof(DataGridSortAnimationHelper));

    static DataGridSortAnimationHelper()
    {
        EnableSortAnimationProperty.Changed.AddClassHandler<DataGrid>(OnChanged);
    }

    public static bool GetEnableSortAnimation(DataGrid grid) =>
        grid.GetValue(EnableSortAnimationProperty);

    public static void SetEnableSortAnimation(DataGrid grid, bool value) =>
        grid.SetValue(EnableSortAnimationProperty, value);

    private static void OnChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            grid.Sorting += OnSorting;
        else
            grid.Sorting -= OnSorting;
    }

    private static void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        // Schedule animation after the sort completes and layout updates
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AnimateRows(grid),
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static void AnimateRows(DataGrid grid)
    {
        // Collect rows first to avoid issues with iterator + composition calls
        var rows = new List<DataGridRow>();
        foreach (var descendant in grid.GetVisualDescendants())
        {
            if (descendant is DataGridRow row)
                rows.Add(row);
        }

        if (rows.Count == 0)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            var visual = ElementComposition.GetElementVisual(rows[i]);
            if (visual is null)
                continue;

            var compositor = visual.Compositor;
            var delay = TimeSpan.FromMilliseconds(i * 20);

            // Set CenterPoint to row center for scale from middle
            var bounds = rows[i].Bounds;
            visual.CenterPoint = new Vector3(
                (float)(bounds.Width / 2),
                (float)(bounds.Height / 2),
                0f);

            // Materialize: scale 0.97 → 1.0 vertically + fade in
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Target = "Scale";
            scaleAnim.InsertKeyFrame(0f, new Vector3(1f, 0.92f, 1f));
            scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(280);
            scaleAnim.DelayTime = delay;

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Target = "Opacity";
            opacityAnim.InsertKeyFrame(0f, 0f);
            opacityAnim.InsertKeyFrame(0.4f, 0.7f);
            opacityAnim.InsertKeyFrame(1f, 1f);
            opacityAnim.Duration = TimeSpan.FromMilliseconds(280);
            opacityAnim.DelayTime = delay;

            var group = compositor.CreateAnimationGroup();
            group.Add(scaleAnim);
            group.Add(opacityAnim);

            visual.StartAnimationGroup(group);
        }
    }
}
