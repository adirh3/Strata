using Avalonia;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>
/// Shows the end of its child: when the child is taller than the panel may grow (its <c>MaxHeight</c>), the
/// child's start is cut off instead of its end, so the newest lines of growing text stay in view. Shown as
/// <c>:clipped</c> while it cuts.
/// </summary>
internal sealed class ComposerTailPanel : Decorator
{
    public ComposerTailPanel() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
            return default;

        Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Math.Min(Child.DesiredSize.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null)
            return finalSize;

        var height = Child.DesiredSize.Height;
        Child.Arrange(new Rect(0, finalSize.Height - height, finalSize.Width, height));
        PseudoClasses.Set(":clipped", height > finalSize.Height + 0.5);
        return finalSize;
    }
}
