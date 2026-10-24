using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>
/// Wraps the composer's context chips into rows, left to right. Where a chip widens to show its remove button
/// (its <c>chip-remove-slot</c> opens while the chip is pointed at or focused), rows are broken by the chips'
/// widths with the slot closed and keep <see cref="RevealWidth"/> free at their end: the chip widens in place
/// rather than moving to the next row, out from under the pointer that opened it, which would close it again.
/// </summary>
internal sealed class ComposerChipsPanel : Panel
{
    public static readonly StyledProperty<double> RevealWidthProperty =
        AvaloniaProperty.Register<ComposerChipsPanel, double>(nameof(RevealWidth));

    static ComposerChipsPanel() => AffectsMeasure<ComposerChipsPanel>(RevealWidthProperty);

    /// <summary>How far a chip widens to show its remove button; 0 where the button always shows.</summary>
    public double RevealWidth
    {
        get => GetValue(RevealWidthProperty);
        set => SetValue(RevealWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(availableSize);

        // By the rows' resting widths, so a chip opening changes nothing outside this panel.
        double width = 0, height = 0;
        foreach (var row in Rows(availableSize.Width))
        {
            width = Math.Max(width, row.Width);
            height += row.Height;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var row in Rows(finalSize.Width))
        {
            double x = 0;
            for (var i = row.Start; i < row.End; i++)
            {
                var child = Children[i];
                if (!child.IsVisible)
                    continue;
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, row.Height));
                x += child.DesiredSize.Width;
            }
            y += row.Height;
        }
        return finalSize;
    }

    private List<(int Start, int End, double Width, double Height)> Rows(double availableWidth)
    {
        var reveal = RevealWidth;
        var rows = new List<(int Start, int End, double Width, double Height)>();
        int start = 0;
        double rowWidth = 0, rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (!child.IsVisible)
                continue;

            var resting = child.DesiredSize.Width - Opening(child, reveal);
            // A hair of tolerance: sums of rounded widths can land a rounding error past a row they fill exactly.
            if (rowWidth > 0 && rowWidth + resting + reveal > availableWidth + 0.01)
            {
                rows.Add((start, i, rowWidth + reveal, rowHeight));
                start = i;
                rowWidth = rowHeight = 0;
            }
            rowWidth += resting;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }
        if (rowWidth > 0)
            rows.Add((start, Children.Count, rowWidth + reveal, rowHeight));
        return rows;
    }

    // How far the chip's remove slot is open right now (it animates), never more than the panel allows for.
    private static double Opening(Control chip, double reveal)
    {
        if (reveal <= 0 || chip is not Decorator { Child: Panel content })
            return 0;

        foreach (var part in content.Children)
        {
            if (part.Classes.Contains("chip-remove-slot"))
                return Math.Min(part.DesiredSize.Width, reveal);
        }
        return 0;
    }
}
