using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// The opened details of a tool step: what it was given, as an aligned list of fields, then what it
/// reported back. Parameters written as <c>**Key:** value</c> lines become fields; anything else, such
/// as a code block or prose, renders as markdown. Progress updates also render as markdown, while a
/// failed step's result reads as its plain-text error.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataToolDetails Parameters="**Query:** best OLED TV"
///                              Result="The search service did not answer."
///                              IsFailed="True" /&gt;
/// </code>
/// <para>Content is built when the control is first measured, so a step whose details stay folded
/// away (inside a hidden or collapsed host) costs nothing, and several property changes in one layout
/// pass build it once. Values are selectable.</para>
/// </remarks>
public class StrataToolDetails : Decorator
{
    private static readonly Regex FieldLine = new(
        @"^\*\*(?<key>[^*\r\n]{1,40}?):\*\*\s*(?<value>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex InlineCode = new("`([^`]*)`", RegexOptions.CultureInvariant);

    /// <summary>What the step was given: <c>**Key:** value</c> lines, or markdown.</summary>
    public static readonly StyledProperty<string?> ParametersProperty =
        AvaloniaProperty.Register<StrataToolDetails, string?>(nameof(Parameters));

    /// <summary>What the step reported back, as plain text: its output or its error.</summary>
    public static readonly StyledProperty<string?> ResultProperty =
        AvaloniaProperty.Register<StrataToolDetails, string?>(nameof(Result));

    /// <summary>The latest progress update, rendered as markdown between the parameters and result.</summary>
    public static readonly StyledProperty<string?> ProgressProperty =
        AvaloniaProperty.Register<StrataToolDetails, string?>(nameof(Progress));

    /// <summary>The step failed, so its result is shown as the error.</summary>
    public static readonly StyledProperty<bool> IsFailedProperty =
        AvaloniaProperty.Register<StrataToolDetails, bool>(nameof(IsFailed));

    private bool _isStale = true;

    static StrataToolDetails()
    {
        ParametersProperty.Changed.AddClassHandler<StrataToolDetails>((control, _) => control.MarkStale());
        ResultProperty.Changed.AddClassHandler<StrataToolDetails>((control, _) => control.MarkStale());
        ProgressProperty.Changed.AddClassHandler<StrataToolDetails>((control, _) => control.MarkStale());
        IsFailedProperty.Changed.AddClassHandler<StrataToolDetails>((control, _) => control.MarkStale());
    }

    public string? Parameters
    {
        get => GetValue(ParametersProperty);
        set => SetValue(ParametersProperty, value);
    }

    public string? Result
    {
        get => GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    /// <summary>Gets or sets the full markdown progress update; see <see cref="ProgressProperty"/>.</summary>
    public string? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsFailed
    {
        get => GetValue(IsFailedProperty);
        set => SetValue(IsFailedProperty, value);
    }

    private void MarkStale()
    {
        _isStale = true;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_isStale)
        {
            _isStale = false;
            Child = Build(Parameters, Progress, Result, IsFailed);
        }

        return base.MeasureOverride(availableSize);
    }

    private static Control? Build(string? parameters, string? progress, string? result, bool isFailed)
    {
        var root = new StackPanel { Spacing = 9 };

        if (ParseFields(parameters) is { } fields)
            root.Children.Add(BuildFields(fields));
        else if (!string.IsNullOrWhiteSpace(parameters))
            root.Children.Add(new StrataMarkdown { Markdown = parameters, IsInline = true, FontSize = 12.5 });

        if (!string.IsNullOrWhiteSpace(progress))
        {
            if (root.Children.Count > 0)
                root.Children.Add(new Border { Classes = { "tool-detail-divider" } });

            root.Children.Add(new StrataMarkdown
            {
                Markdown = progress,
                IsInline = true,
                FontSize = 12.5,
                Classes = { "tool-detail-progress" },
            });
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            if (root.Children.Count > 0)
                root.Children.Add(new Border { Classes = { "tool-detail-divider" } });

            var text = CreateValueText(result.Trim(), "tool-detail-result");
            if (isFailed)
            {
                var error = new DockPanel { Classes = { "tool-detail-error" } };
                var icon = new StrataLineIcon { Data = StrataIcons.Alert, Classes = { "tool-detail-error-icon" } };
                DockPanel.SetDock(icon, Dock.Left);
                error.Children.Add(icon);
                error.Children.Add(text);
                root.Children.Add(error);
            }
            else
            {
                root.Children.Add(text);
            }
        }

        return root.Children.Count > 0 ? root : null;
    }

    private static Grid BuildFields(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 5, Classes = { "tool-detail-fields" } };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var row = 0; row < fields.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var key = new TextBlock { Text = fields[row].Key, Classes = { "tool-detail-key" } };
            var value = CreateValueText(fields[row].Value, "tool-detail-value");
            Grid.SetRow(key, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(key);
            grid.Children.Add(value);
        }

        return grid;
    }

    private static SelectableTextBlock CreateValueText(string text, string styleClass)
    {
        var block = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes = { styleClass },
        };
        if (StrataTextDirectionDetector.DetectLeading(text) is { } direction)
        {
            block.FlowDirection = direction;
            block.TextAlignment = direction == FlowDirection.RightToLeft
                ? TextAlignment.Right
                : TextAlignment.Left;
        }
        return block;
    }

    /// <summary>
    /// Reads <c>**Key:** value</c> lines as fields, unwrapping inline code in the values. Null when any
    /// line is something else, so prose and code keep their markdown.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, string>>? ParseFields(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var fields = new List<KeyValuePair<string, string>>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var match = FieldLine.Match(line);
            if (!match.Success)
                return null;

            var value = InlineCode.Replace(match.Groups["value"].Value, "$1").Trim();
            fields.Add(new KeyValuePair<string, string>(match.Groups["key"].Value.Trim(), value));
        }

        return fields.Count > 0 ? fields : null;
    }
}
