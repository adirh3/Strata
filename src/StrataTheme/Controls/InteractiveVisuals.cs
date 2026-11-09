using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>Icon geometry (Material Design icons, Apache License 2.0) shared by native interactions.</summary>
internal static class InteractiveIcons
{
    public const string Form = "M3 10h11v2H3zm0-2h11V6H3v2zm0 8h7v-2H3v2zm15.01-3.13.71-.71c.39-.39 1.02-.39 1.41 0l.71.71c.39.39.39 1.02 0 1.41l-.71.71-2.12-2.12zm-.71.71-5.3 5.3V21h2.12l5.3-5.3-2.12-2.12z";
    public const string Checklist = "M22 7h-9v2h9V7zm0 8h-9v2h9v-2zM5.54 11 2 7.46l1.41-1.41 2.12 2.12 4.24-4.24 1.41 1.41L5.54 11zm0 8L2 15.46l1.41-1.41 2.12 2.12 4.24-4.24 1.41 1.41L5.54 19z";
    public const string Steps = "M2 17h2v.5H3v1h1v.5H2v1h3v-4H2v1zm1-9h1V4H2v1h1v3zm-1 3h1.8L2 13.1v.9h3v-1H3.2L5 10.9V10H2v1zm5-6v2h14V5H7zm0 14h14v-2H7v2zm0-6h14v-2H7v2z";
    public const string Results = "M20 13H3c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h17c.55 0 1-.45 1-1v-6c0-.55-.45-1-1-1zm0-10H3c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h17c.55 0 1-.45 1-1V4c0-.55-.45-1-1-1z";
    public const string Calculator = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5.97 4.06L14.09 6l1.41 1.41L16.91 6l1.06 1.06-1.41 1.41 1.41 1.41-1.06 1.06-1.41-1.4-1.41 1.41-1.06-1.06 1.41-1.41-1.41-1.42zm-6.78.66h5v1.5h-5v-1.5zM11.5 16h-2v2H8v-2H6v-1.5h2v-2h1.5v2h2V16zm6.5 1.25h-5v-1.5h5v1.5zm0-2.5h-5v-1.5h5v1.5z";
    public const string Quiz = "M5 13.18v4L12 21l7-3.82v-4L12 17l-7-3.82zM12 3 1 9l11 6 9-4.91V17h2V9L12 3z";
    public const string Check = "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z";
    public const string Close = "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z";
    public const string ChevronDown = "M16.59 8.59 12 13.17 7.41 8.59 6 10l6 6 6-6z";
    public const string ChevronUp = "M12 8l-6 6 1.41 1.41L12 10.83l4.59 4.58L18 14z";
    public const string Calendar = "M20 3h-1V1h-2v2H7V1H5v2H4c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 18H4V8h16v13z";
    public const string ArrowUp = "M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z";
    public const string ArrowForward = "M12 4l-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z";
    public const string ArrowBack = "M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z";
    public const string Lightbulb = "M9 21c0 .55.45 1 1 1h4c.55 0 1-.45 1-1v-1H9v1zm3-19C8.14 2 5 5.14 5 9c0 2.38 1.19 4.47 3 5.74V17c0 .55.45 1 1 1h6c.55 0 1-.45 1-1v-2.26c1.81-1.27 3-3.36 3-5.74 0-3.86-3.14-7-7-7z";
    public const string Search = "M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z";
    public const string Compare = "M9.01 14H2v2h7.01v3L13 15l-3.99-4v3zm5.98-1v-3H22V8h-7.01V5L11 9l3.99 4z";
    public const string Send = "M2.01 21 23 12 2.01 3 2 10l15 2-15 2z";
    public const string Restart = "M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z";
    public const string Removed = "M7 11v2h10v-2H7zm5-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z";
    public const string Clock = "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z";
    public const string Action = "M4 5h9v2H4zm0 4h9v2H4zm0 4h6v2H4zm12-3 5 5-5 5v-4h-4v-2h4z";
    public const string Explorer = "M7 9H2V7h5v2zm0 3H2v2h5v-2zm13.59 7-3.83-3.83c-.8.52-1.74.83-2.76.83-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5c0 1.02-.31 1.96-.83 2.75L22 17.59 20.59 19zM17 11c0-1.65-1.35-3-3-3s-3 1.35-3 3 1.35 3 3 3 3-1.35 3-3zM2 19h10v-2H2v2z";
    public const string Tune = "M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z";
    public const string Sort = "M16 17.01V10h-2v7.01h-3L15 21l4-3.99h-3zM9 3 5 6.99h3V14h2V6.99h3L9 3z";
    public const string Cards = "M3 3v8h8V3H3zm6 6H5V5h4v4zm-6 4v8h8v-8H3zm6 6H5v-4h4v4zm4-16v8h8V3h-8zm6 6h-4V5h4v4zm-6 4v8h8v-8h-8zm6 6h-4v-4h4v4z";
    public const string Table = "M21 8H3V4h18v4zm0 2H3v4h18v-4zm0 6H3v4h18v-4z";
    public const string Star = "M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z";
    public const string Add = "M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z";
    public const string Minus = "M19 13H5v-2h14v2z";
    public const string OpenExternal = "M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z";
    public const string ChevronBack = "M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z";
    public const string ChevronForward = "M10 6 8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z";
    public const string ArrowDown = "M20 12l-1.41-1.41L13 16.17V4h-2v12.17l-5.58-5.59L4 12l8 8 8-8z";

    /// <summary>Creates an icon. Only directional arrows mirror in right-to-left layouts.</summary>
    public static PathIcon Create(string data, double size = 12)
    {
        var icon = new PathIcon { Data = Geometry.Parse(data), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (data is not (ArrowForward or ArrowBack or ChevronBack or ChevronForward))
            icon.FlowDirection = FlowDirection.LeftToRight;
        return icon;
    }

    /// <summary>Text with a leading or trailing icon for buttons and pills.</summary>
    public static Control Labeled(string data, string text, bool trailing = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (trailing)
        {
            panel.Children.Add(label);
            panel.Children.Add(Create(data));
        }
        else
        {
            panel.Children.Add(Create(data));
            panel.Children.Add(label);
        }
        return panel;
    }

    public static string ForAction(string? action) => action switch
    {
        "compare" => Compare,
        "use" => Check,
        "submit" => Send,
        "remaining" => Checklist,
        "assumptions" => Calculator,
        "discuss" => Quiz,
        "more" => Search,
        _ => Action
    };
}

/// <summary>
/// Renders a frozen receipt description as a calm, scannable list. "Key: value" lines align in a
/// shared key column; the first line of a multi-line paragraph is its title. Text stays selectable.
/// </summary>
internal static class InteractiveSnapshotView
{
    public static Control Create(string detail)
    {
        var root = new StackPanel { Spacing = 8 };
        Grid.SetIsSharedSizeScope(root, true);
        foreach (var paragraph in Paragraphs(detail))
        {
            var block = new StackPanel { Spacing = 3 };
            var keyed = paragraph.Select(KeySeparator).ToList();
            var isTable = keyed.All(separator => separator > 0);
            for (var index = 0; index < paragraph.Count; index++)
            {
                var line = paragraph[index];
                var separator = keyed[index];
                if (isTable)
                {
                    var row = new Grid { ColumnSpacing = 12 };
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "SnapshotKey" });
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    var key = new TextBlock
                    {
                        Text = line[..separator], MaxWidth = 200, TextWrapping = TextWrapping.Wrap,
                        Classes = { "interactive-snapshot-key" }
                    };
                    var value = new SelectableTextBlock
                    {
                        Text = line[(separator + 2)..], TextWrapping = TextWrapping.Wrap,
                        Classes = { "interactive-snapshot-value" }
                    };
                    Grid.SetColumn(value, 1);
                    row.Children.Add(key);
                    row.Children.Add(value);
                    block.Children.Add(row);
                }
                else
                {
                    block.Children.Add(new SelectableTextBlock
                    {
                        Text = line, TextWrapping = TextWrapping.Wrap,
                        Classes = { index == 0 && paragraph.Count > 1 ? "interactive-snapshot-title" : "interactive-snapshot-text" }
                    });
                }
            }
            root.Children.Add(block);
        }
        return root;
    }

    /// <summary>The "Key: value" separator position, or -1 when the line is prose or a link.</summary>
    private static int KeySeparator(string line)
    {
        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator is > 0 and <= 40 && !line.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? separator : -1;
    }

    private static IEnumerable<List<string>> Paragraphs(string detail)
    {
        var current = new List<string>();
        foreach (var raw in detail.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (current.Count > 0)
                    yield return current;
                current = [];
                continue;
            }
            current.Add(line);
        }
        if (current.Count > 0)
            yield return current;
    }
}
