using System;
using Avalonia;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>
/// Shows the top <see cref="Reveal"/> share of its content and clips the rest. The content is always
/// laid out at its natural height, so opening and closing never squeezes or reflows it, and a
/// scrolling area inside keeps its full size instead of showing a scroll bar mid-motion.
/// </summary>
internal sealed class TurnSummaryRevealHost : Decorator
{
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<TurnSummaryRevealHost, double>(
            nameof(Reveal), 1, validate: value => value is >= 0 and <= 1);

    private double _naturalHeight;

    static TurnSummaryRevealHost()
    {
        AffectsMeasure<TurnSummaryRevealHost>(RevealProperty);
        ClipToBoundsProperty.OverrideDefaultValue<TurnSummaryRevealHost>(true);
    }

    public double Reveal
    {
        get => GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var natural = base.MeasureOverride(availableSize.WithHeight(double.PositiveInfinity));
        _naturalHeight = natural.Height;
        return natural.WithHeight(natural.Height * Reveal);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(finalSize.WithHeight(Math.Max(finalSize.Height, _naturalHeight)));
        return finalSize;
    }
}
