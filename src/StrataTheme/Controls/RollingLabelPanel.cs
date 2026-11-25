using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// Shows its first or second child in the same place and rolls between them as <see cref="Progress"/>
/// goes from 0 to 1: the first slides up and fades out while the second rises into place. Its width
/// follows the child being shown, so a pill around it always hugs its label.
/// </summary>
internal sealed class RollingLabelPanel : Panel
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<RollingLabelPanel, double>(
            nameof(Progress), 0, validate: value => value is >= 0 and <= 1);

    static RollingLabelPanel()
    {
        AffectsMeasure<RollingLabelPanel>(ProgressProperty);
        ClipToBoundsProperty.OverrideDefaultValue<RollingLabelPanel>(true);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        if (Children.Count < 2)
            return new Size(Children.Count == 1 ? Children[0].DesiredSize.Width : 0, height);

        var from = Children[0].DesiredSize.Width;
        var to = Children[1].DesiredSize.Width;
        return new Size(from + (to - from) * Progress, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var progress = Progress;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Arrange(new Rect(0, 0, child.DesiredSize.Width, finalSize.Height));
            child.Opacity = i switch { 0 => 1 - progress, 1 => progress, _ => 0 };
            child.RenderTransform = new TranslateTransform(0, (i == 0 ? -progress : 1 - progress) * finalSize.Height);
        }

        return finalSize;
    }
}
