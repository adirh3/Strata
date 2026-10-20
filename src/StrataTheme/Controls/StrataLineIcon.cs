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

    // ── An AI's work trail: what each step is, at a glance ──

    /// <summary>Reasoning: a lightbulb.</summary>
    public static Geometry Lightbulb => Parse("M9.5 18h5 M10.5 21h3 M12 3.5a6 6 0 0 0-3.6 10.8c.7.5 1.1 1.3 1.1 2.1v.1h5v-.1c0-.8.4-1.6 1.1-2.1A6 6 0 0 0 12 3.5z");

    /// <summary>Reading a file: a page with a folded corner.</summary>
    public static Geometry Document => Parse("M13.5 3.5h-6A1.5 1.5 0 0 0 6 5v14a1.5 1.5 0 0 0 1.5 1.5h9A1.5 1.5 0 0 0 18 19V8z M13.5 3.5V8H18 M9.5 12.5h5 M9.5 16h3.5");

    /// <summary>Creating a file: a page with a plus.</summary>
    public static Geometry DocumentPlus => Parse("M13.5 3.5h-6A1.5 1.5 0 0 0 6 5v14a1.5 1.5 0 0 0 1.5 1.5h9A1.5 1.5 0 0 0 18 19V8z M13.5 3.5V8H18 M12 11v6 M9 14h6");

    /// <summary>Editing: a pencil.</summary>
    public static Geometry Pencil => Parse("M15.5 4.5l4 4L9 19H5v-4z M13 7l4 4");

    /// <summary>Several files at once: two stacked pages.</summary>
    public static Geometry Files => Parse("M9.5 7h6L19 10.5V19a1.5 1.5 0 0 1-1.5 1.5h-8A1.5 1.5 0 0 1 8 19V8.5A1.5 1.5 0 0 1 9.5 7z M15.5 7v3.5H19 M5 16.5v-11A1.5 1.5 0 0 1 6.5 4H13");

    /// <summary>Searching: a magnifier.</summary>
    public static Geometry Search => Parse("M10.5 4a6.5 6.5 0 1 1 0 13 6.5 6.5 0 0 1 0-13z M15.3 15.3 20 20");

    /// <summary>The web: a globe.</summary>
    public static Geometry Globe => Parse("M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z M3.5 12h17 M12 3.5c-2.3 2.3-3.5 5.2-3.5 8.5s1.2 6.2 3.5 8.5 M12 3.5c2.3 2.3 3.5 5.2 3.5 8.5s-1.2 6.2-3.5 8.5");

    /// <summary>Reading a page: an open book.</summary>
    public static Geometry Book => Parse("M12 6.5C10 5 7.5 4.5 4 5v13c3.5-.5 6 0 8 1.5 2-1.5 4.5-2 8-1.5V5c-3.5-.5-6 0-8 1.5z M12 6.5v13");

    /// <summary>A command: a terminal window with a prompt.</summary>
    public static Geometry Terminal => Parse("M4.5 5h15A1.5 1.5 0 0 1 21 6.5v11a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5v-11A1.5 1.5 0 0 1 4.5 5z M7 9.5l2.75 2.5L7 14.5 M12.5 14.5h4.5");

    /// <summary>A plan: a checklist.</summary>
    public static Geometry Checklist => Parse("M10.5 6.5h9 M10.5 12h9 M10.5 17.5h9 M4 6.5l1.5 1.5 2.5-2.75 M4 12l1.5 1.5 2.5-2.75 M4 17.5l1.5 1.5 2.5-2.75");

    /// <summary>A screenshot: a camera.</summary>
    public static Geometry Camera => Parse("M4.5 7.5h3L9 5h6l1.5 2.5h3A1.5 1.5 0 0 1 21 9v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V9a1.5 1.5 0 0 1 1.5-1.5z M12 10a3.25 3.25 0 1 1 0 6.5 3.25 3.25 0 0 1 0-6.5z");

    /// <summary>The desktop: a monitor on its stand.</summary>
    public static Geometry Monitor => Parse("M4.5 4.5h15A1.5 1.5 0 0 1 21 6v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 15V6a1.5 1.5 0 0 1 1.5-1.5z M12 16.5V20 M8.5 20h7");

    /// <summary>Memory: a bookmark.</summary>
    public static Geometry Bookmark => Parse("M7 3.5h10a1 1 0 0 1 1 1v16l-6-4-6 4v-16a1 1 0 0 1 1-1z");

    /// <summary>Several agents: two people.</summary>
    public static Geometry People => Parse("M9 11.5a3.25 3.25 0 1 0 0-6.5 3.25 3.25 0 0 0 0 6.5z M3 19.5a6 6 0 0 1 12 0 M15.5 5.2a3.25 3.25 0 0 1 0 6.1 M17.5 13.8a6 6 0 0 1 3.5 5.7");

    /// <summary>A tool without a picture of its own: a cube.</summary>
    public static Geometry Cube => Parse("M12 3l8 4.5v9L12 21l-8-4.5v-9z M4 7.5l8 4.5 8-4.5 M12 12v9");

    /// <summary>Mixed work: stacked layers.</summary>
    public static Geometry Layers => Parse("M12 4l8.5 4.5L12 13 3.5 8.5z M3.5 12.5 12 17l8.5-4.5 M3.5 16.5 12 21l8.5-4.5");

    /// <summary>Tabular data: a database.</summary>
    public static Geometry Database => Parse("M5 6.5C5 5.1 8.1 4 12 4s7 1.1 7 2.5S15.9 9 12 9 5 7.9 5 6.5z M5 6.5v11C5 18.9 8.1 20 12 20s7-1.1 7-2.5v-11 M5 12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5");

    /// <summary>Tests: a beaker.</summary>
    public static Geometry Beaker => Parse("M9.5 3.5h5 M10.5 3.5V9l-5.3 9a1.5 1.5 0 0 0 1.3 2.5h11a1.5 1.5 0 0 0 1.3-2.5l-5.3-9V3.5 M7.5 14.5h9");

    /// <summary>A question: a circled question mark.</summary>
    public static Geometry Question => Parse("M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z M9.5 9.5a2.5 2.5 0 1 1 3.6 2.24c-.7.35-1.1 1-1.1 1.76v.5 M12 16.75v.1");

    /// <summary>Something went wrong: a warning triangle.</summary>
    public static Geometry Alert => Parse("M10.3 4.9 3.2 17.2a2 2 0 0 0 1.7 3h14.2a2 2 0 0 0 1.7-3L13.7 4.9a2 2 0 0 0-3.4 0z M12 9.5V14 M12 17.2v.1");

    /// <summary>Time: a clock.</summary>
    public static Geometry Clock => Parse("M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z M12 7.5V12l3 2");

    /// <summary>Done: a check.</summary>
    public static Geometry Check => Parse("M5 12.5l4.5 4.5L19 7.5");

    /// <summary>Finished work: a check in a circle.</summary>
    public static Geometry CheckCircle => Parse("M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z M8.5 12.2l2.4 2.4 4.6-4.9");

    /// <summary>Failed: a cross.</summary>
    public static Geometry Cross => Parse("M7 7l10 10 M17 7 7 17");

    /// <summary>Approvals and what an assistant may do: a shield.</summary>
    public static Geometry Shield => Parse("M12 3.2 5 6v5.2c0 4.4 3 8.2 7 9.6 4-1.4 7-5.2 7-9.6V6z");

    /// <summary>Checked before it runs: a shield with a check.</summary>
    public static Geometry ShieldCheck => Parse("M12 3.2 5 6v5.2c0 4.4 3 8.2 7 9.6 4-1.4 7-5.2 7-9.6V6z M9 12.2l2.2 2.2 3.9-4.2");

    /// <summary>Acting freely: a lightning bolt.</summary>
    public static Geometry Bolt => Parse("M13.2 3 5.5 13.6h5.6L10.4 21l7.7-10.6h-5.6z");

    /// <summary>Asking first: a raised hand.</summary>
    public static Geometry Hand => Parse("M8.5 12.5V6.2a1.4 1.4 0 0 1 2.8 0v5.3 M11.3 11V4.9a1.4 1.4 0 0 1 2.8 0V11 M14.1 11V6.4a1.4 1.4 0 0 1 2.8 0v6.8c0 4.2-2.6 7.3-6.4 7.3-2.3 0-3.7-.9-4.9-2.9l-2.1-3.5a1.35 1.35 0 0 1 2.2-1.5l2.3 2.5");

    /// <summary>Stopped: a square in a circle.</summary>
    public static Geometry StopCircle => Parse("M12 3.5a8.5 8.5 0 1 1 0 17 8.5 8.5 0 0 1 0-17z M9.5 9.5h5v5h-5z");

    /// <summary>Try again: a turning arrow.</summary>
    public static Geometry Retry => Parse("M4.5 12a7.5 7.5 0 1 0 2.2-5.3L4.5 9 M4.5 4.5V9H9");

    /// <summary>Changes: a split view with a minus and a plus.</summary>
    public static Geometry Diff => Parse("M5 4.5h14a1.5 1.5 0 0 1 1.5 1.5v12a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 18V6A1.5 1.5 0 0 1 5 4.5z M12 4.5v15 M6.5 12h3 M14.5 12h3 M16 10.5v3");
}
