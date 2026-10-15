using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// A line icon: <see cref="Data"/> drawn on a 24-unit grid as a round-capped stroke in the inherited
/// foreground, scaled uniformly to fit the control. One stroke weight for every icon keeps a toolbar
/// reading as a single family. Set <c>Width</c> and <c>Height</c>; the icon draws nothing at zero size.
/// </summary>
public class StrataLineIcon : Control
{
    /// <summary>Side of the square grid <see cref="Data"/> is drawn on.</summary>
    public const double GridSize = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<StrataLineIcon, Geometry?>(nameof(Data));

    /// <summary>Stroke width in grid units, so it scales with the icon (1.75 is 1.3 px at 18 px).</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<StrataLineIcon, double>(nameof(StrokeThickness), 1.75);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<StrataLineIcon>();

    private IPen? _pen;

    static StrataLineIcon()
    {
        AffectsRender<StrataLineIcon>(DataProperty, StrokeThicknessProperty, ForegroundProperty);
        ForegroundProperty.Changed.AddClassHandler<StrataLineIcon>((icon, _) => icon._pen = null);
        StrokeThicknessProperty.Changed.AddClassHandler<StrataLineIcon>((icon, _) => icon._pen = null);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush)
            return;

        var size = Bounds.Size;
        var scale = Math.Min(size.Width, size.Height) / GridSize;
        if (!(scale > 0))
            return;

        _pen ??= new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var offset = Matrix.CreateTranslation(
            (size.Width - GridSize * scale) / 2,
            (size.Height - GridSize * scale) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * offset))
            context.DrawGeometry(null, _pen, data);
    }
}

/// <summary>
/// Strata's line icons, drawn on <see cref="StrataLineIcon"/>'s 24-unit grid. Every access parses a new
/// geometry: a geometry belongs to the renderer it was parsed under, so none is cached or shared.
/// </summary>
public static class StrataIcons
{
    private static Geometry Parse(string data) => StreamGeometry.Parse(data);

    public static Geometry Plus => Parse("M12 5.5v13 M5.5 12h13");
    public static Geometry ChevronDown => Parse("M7 10l5 5 5-5");
    public static Geometry ChevronRight => Parse("M10 7l5 5-5 5");
    public static Geometry ArrowUp => Parse("M12 18.5V6 M6.5 11.5 12 6l5.5 5.5");

    /// <summary>Send, then stop: an arrow over a baseline.</summary>
    public static Geometry StopSend => Parse("M12 15V5.5 M7.5 10 12 5.5l4.5 4.5 M7 19h10");

    public static Geometry Mic => Parse("M12 3.5a3 3 0 0 0-3 3V12a3 3 0 0 0 6 0V6.5a3 3 0 0 0-3-3z M6 11.5a6 6 0 0 0 12 0 M12 17.5v3");

    /// <summary>Listening: a level meter.</summary>
    public static Geometry Wave => Parse("M4.5 10.5v3 M8.25 7.5v9 M12 4.5v15 M15.75 7.5v9 M19.5 10.5v3");

    public static Geometry Paperclip => Parse("M19 11.5l-7.6 7.6a4.6 4.6 0 0 1-6.5-6.5l8.2-8.2a3 3 0 0 1 4.2 4.2l-8.1 8.1a1.5 1.5 0 0 1-2.1-2.1l7.4-7.4");

    /// <summary>Skills: a wand and its sparkles.</summary>
    public static Geometry Wand => Parse("M4.5 19.5 14 10 M12.5 8.5l3 3 M17.5 3.5v3 M16 5h3 M7 4.5v2 M6 5.5h2 M19 13v2 M18 14h2");

    /// <summary>Connectors: a plug.</summary>
    public static Geometry Plug => Parse("M9 3.5v4 M15 3.5v4 M6.5 7.5h11v3a5.5 5.5 0 0 1-11 0z M12 16v4.5");

    /// <summary>Other chats: two speech bubbles.</summary>
    public static Geometry Chats => Parse("M6 4.5h8a2 2 0 0 1 2 2V12a2 2 0 0 1-2 2H9l-3.5 3v-3H6a2 2 0 0 1-2-2V6.5a2 2 0 0 1 2-2z M16 8.5h3a2 2 0 0 1 2 2V16a2 2 0 0 1-2 2h-.5v3L15 18h-3a2 2 0 0 1-2-2v-2");

    /// <summary>One chat: a speech bubble.</summary>
    public static Geometry Chat => Parse("M6 5h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-6.5L7 20.5V17H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z");

    /// <summary>An agent: a person.</summary>
    public static Geometry Person => Parse("M12 12a3.75 3.75 0 1 0 0-7.5 3.75 3.75 0 0 0 0 7.5z M5 19.5a7 7 0 0 1 14 0");

    /// <summary>A project: a folder.</summary>
    public static Geometry Folder => Parse("M4 7.5a2 2 0 0 1 2-2h3.6l2 2.5H18a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z");
}
