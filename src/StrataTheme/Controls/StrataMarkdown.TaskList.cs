using System;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace StrataTheme.Controls;

public partial class StrataMarkdown
{
    private TaskMarkerBrushes? _taskMarkerBrushes;
    private IBrush? _taskDoneBrush;
    private TextDecorationCollection? _taskDoneDecorations;

    /// <summary>The checkbox before a task item's text, done or not.</summary>
    private TaskMarkerRun CreateTaskMarker(bool isChecked) =>
        new(isChecked, _taskMarkerBrushes ??= ResolveTaskMarkerBrushes());

    /// <summary>Fills a task item's text block: its checkbox, then its text, struck through and quieter once done.</summary>
    private bool PopulateTaskItem(SelectableTextBlock textBlock, string text, bool isChecked)
    {
        var inlines = new InlineCollection { CreateTaskMarker(isChecked) };
        var hasLinks = AppendFormattedInlines(textBlock, inlines, text, null, forceInlines: true);
        if (isChecked)
            MarkTaskDone(inlines, 1);
        ApplyFormattedInlines(textBlock, inlines, textBlock.FontSize * TextLineHeightRatio);
        return hasLinks;
    }

    /// <summary>
    /// A done item's text, from <paramref name="start"/> on, is struck through (code keeps its own look) and takes
    /// the quiet text colour unless it has a colour of its own, as a link does. Its checkbox is left as it is.
    /// </summary>
    private void MarkTaskDone(InlineCollection inlines, int start)
    {
        var done = _taskDoneBrush ??= ResolveApplicationBrush("Brush.TextTertiary", Color.FromArgb(150, 128, 128, 128));
        // Its own line, made on this control's thread: Avalonia's shared TextDecorations belong to whichever UI
        // thread touched them first, so another thread drawing them would fail.
        var struck = _taskDoneDecorations ??= new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Strikethrough }
        };
        for (var index = start; index < inlines.Count; index++)
        {
            if (inlines[index] is not Run run || run is TaskMarkerRun)
                continue;
            // Text struck by its own ~~ keeps markdown's decoration, which copying reads back as ~~.
            if (run is not InlineCodeRun && run.TextDecorations != TextDecorations.Strikethrough)
                run.TextDecorations = struck;
            if (!run.IsSet(TextElement.ForegroundProperty))
                run.Foreground = done;
        }
    }

    private static TaskMarkerBrushes ResolveTaskMarkerBrushes() => new(
        ResolveApplicationBrush("Brush.ControlDefault", Colors.Transparent),
        ResolveApplicationBrush("Brush.BorderStrong", Color.FromArgb(150, 128, 128, 128)),
        ResolveApplicationBrush("Brush.AccentGradient", Color.Parse("#4F46E5")),
        ResolveApplicationBrush("Brush.AccentDefault", Color.Parse("#4F46E5")),
        ResolveApplicationBrush("Brush.TextOnAccent", Colors.White));

    private static IBrush ResolveApplicationBrush(string key, Color fallback) =>
        TryGetApplicationResource(key, out var resource) && resource is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    /// <summary>
    /// The text a selection of <paramref name="inlines"/> reads as: their text, with each task checkbox read back
    /// as ☐ or ☑, which takes the same room, so selection offsets still line up.
    /// </summary>
    internal static string GetSelectionText(InlineCollection inlines)
    {
        var text = inlines.Text ?? "";
        char[]? mapped = null;
        var offset = 0;
        foreach (var inline in inlines)
        {
            if (inline is TaskMarkerRun marker
                && string.CompareOrdinal(text, offset, TaskMarkerRun.Marker, 0, TaskMarkerRun.Marker.Length) == 0)
            {
                mapped ??= text.ToCharArray();
                marker.Glyph.CopyTo(0, mapped, offset, marker.Glyph.Length);
            }
            offset += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                LineBreak => Environment.NewLine.Length,
                _ => 1,
            };
        }
        return mapped is null ? text : new string(mapped);
    }

    /// <summary>The theme's colours for task checkboxes, as Strata's own checkbox wears them.</summary>
    internal sealed record TaskMarkerBrushes(IBrush Box, IBrush Border, IBrush DoneBox, IBrush DoneBorder, IBrush Check);

    /// <summary>
    /// A markdown text block, styled as any other. Assistive technology reads a text's inlines, where a task's
    /// checkbox is only room, so this one reads each checkbox as ☐ or ☑, as a selection does.
    /// </summary>
    internal sealed class MarkdownTextBlock : SelectableTextBlock
    {
        protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

        protected override AutomationPeer OnCreateAutomationPeer() => new TaskAwarePeer(this);

        private sealed class TaskAwarePeer(MarkdownTextBlock owner) : TextBlockAutomationPeer(owner)
        {
            protected override string? GetNameCore() =>
                owner.Inlines is { } inlines && HasTaskMarker(inlines) ? GetSelectionText(inlines) : base.GetNameCore();

            private static bool HasTaskMarker(InlineCollection inlines)
            {
                foreach (var inline in inlines)
                    if (inline is TaskMarkerRun)
                        return true;
                return false;
            }
        }
    }

    /// <summary>
    /// A task item's checkbox: an em space the width of the box, which <see cref="InlineCodeLayer"/> draws as a
    /// Strata checkbox behind the line, on its baseline. Whitespace never draws, not even in the selection colour,
    /// and a selection reads it back as ☐ or ☑, so copying keeps the task's state.
    /// </summary>
    internal sealed class TaskMarkerRun : Run
    {
        internal const string Marker = "\u2003 ";

        public TaskMarkerRun(bool isChecked, TaskMarkerBrushes brushes) : base(Marker)
        {
            IsChecked = isChecked;
            Brushes = brushes;
        }

        public bool IsChecked { get; }

        public TaskMarkerBrushes Brushes { get; }

        /// <summary>The character it stands for, in the room it takes.</summary>
        public string Glyph => IsChecked ? "☑ " : "☐ ";
    }

    /// <summary>
    /// Draws a task's checkbox in the box of its em space: a rounded square sitting on the line like a capital,
    /// filled with the accent and ticked once done. <paramref name="mirrored"/> keeps the tick pointing the same
    /// way on screen when the text is drawn mirrored, as right-to-left text inside a left-to-right host is.
    /// </summary>
    internal static void DrawTaskBox(DrawingContext context, Rect space, double baseline, double fontSize,
        TaskMarkerRun marker, bool mirrored)
    {
        var size = Math.Round(fontSize * 0.88);
        var box = new Rect(
            space.X + (space.Width - size) / 2,
            baseline - fontSize * 0.34 - size / 2,
            size,
            size);
        var radius = Math.Max(3, size * 0.27);
        var brushes = marker.Brushes;
        if (!marker.IsChecked)
        {
            context.DrawRectangle(brushes.Box, new Pen(brushes.Border, 1), box.Deflate(0.5), radius, radius);
            return;
        }

        context.DrawRectangle(brushes.DoneBox, new Pen(brushes.DoneBorder, 1), box.Deflate(0.5), radius, radius);

        // Strata's checkbox tick (M1,4.5 L4.5,8 L11,1), at the same share of the box.
        var width = size * 0.58;
        var height = width * 0.7;
        var left = box.X + (size - width) / 2;
        var top = box.Y + (size - height) / 2 + size * 0.02;
        Point At(double x, double y)
        {
            var point = new Point(left + x * width, top + y * height);
            return mirrored ? new Point(box.X * 2 + size - point.X, point.Y) : point;
        }

        var tick = new StreamGeometry();
        using (var geometry = tick.Open())
        {
            geometry.BeginFigure(At(0, 0.5), false);
            geometry.LineTo(At(0.35, 1));
            geometry.LineTo(At(1, 0));
            geometry.EndFigure(false);
        }
        context.DrawGeometry(null,
            new Pen(brushes.Check, Math.Max(1.4, size * 0.12), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            tick);
    }
}
