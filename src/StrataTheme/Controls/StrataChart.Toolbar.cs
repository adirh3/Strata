using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public partial class StrataChart
{
    private Panel? _tip;
    private StackPanel? _tipRows;
    private bool _tipShown;
    private ScrollViewer? _tableScroll;
    private bool _tableBuilt;
    private bool _isTableView;
    private Button? _chartView, _tableView, _copyData, _expand;
    private StrataLineIcon? _copyIcon, _expandIcon;
    private StrataFullScreenOverlay? _expandedView;
    private StrataChart? _expandedChart, _expandedOwner;
    private int _copyFeedbackVersion;

    private static readonly FontFeatureCollection TabularFigures = new() { FontFeature.Parse("tnum") };

    /// <summary>Whether the chart shows its data as a table in place of the plot.</summary>
    internal bool IsTableView => _isTableView;

    private void ApplyToolbarTemplate(INameScope scope)
    {
        if (_chartView is not null) _chartView.Click -= OnChartView;
        if (_tableView is not null) _tableView.Click -= OnTableView;
        if (_copyData is not null) _copyData.Click -= OnCopyData;
        if (_expand is not null) _expand.Click -= OnExpand;

        _tip = scope.Find<Panel>("PART_Tip");
        _tipRows = scope.Find<StackPanel>("PART_TipRows");
        _tipShown = false;
        if (_tableScroll is not null)
        {
            _tableScroll.ScrollChanged -= OnTableScrollChanged;
            _tableScroll.KeyDown -= OnTableKeyDown;
        }
        _tableScroll = scope.Find<ScrollViewer>("PART_TableScroll");
        if (_tableScroll is not null)
        {
            _tableScroll.ScrollChanged += OnTableScrollChanged;
            _tableScroll.KeyDown += OnTableKeyDown;
        }
        _tableBuilt = false;
        _chartView = scope.Find<Button>("PART_ChartView");
        _tableView = scope.Find<Button>("PART_TableView");
        _copyData = scope.Find<Button>("PART_CopyData");
        _expand = scope.Find<Button>("PART_Expand");
        _copyIcon = scope.Find<StrataLineIcon>("PART_CopyIcon");
        _expandIcon = scope.Find<StrataLineIcon>("PART_ExpandIcon");

        if (_chartView is not null) _chartView.Click += OnChartView;
        if (_tableView is not null) _tableView.Click += OnTableView;
        if (_copyData is not null) _copyData.Click += OnCopyData;
        if (_expand is not null)
        {
            _expand.Click += OnExpand;
            if (_expandedOwner is not null)
            {
                PseudoClasses.Set(":fullscreen", true);
                if (_expandIcon is not null)
                    _expandIcon.Data = StrataIcons.Cross;
                ToolTip.SetTip(_expand, InteractiveLabels.Current["closeFullScreenShortcut"]);
                AutomationProperties.SetName(_expand, InteractiveLabels.Current["closeFullScreen"]);
            }
        }

        PseudoClasses.Set(":table", _isTableView);
        if (_isTableView)
            EnsureTable();
    }

    // The data changed: a table shown is built again, and full screen follows.
    private void OnChartDataChanged()
    {
        _tableBuilt = false;
        if (_isTableView)
            EnsureTable();
        if (_expandedChart is not null)
            CopyChartTo(_expandedChart);
    }

    // ── Chart | Table ──────────────────────────────────────────

    private void OnChartView(object? sender, RoutedEventArgs e) => SetTableView(false);

    private void OnTableView(object? sender, RoutedEventArgs e) => SetTableView(true);

    internal void SetTableView(bool table)
    {
        if (_isTableView == table) return;
        _isTableView = table;
        PseudoClasses.Set(":table", table);
        if (table)
        {
            _canvas?.SetHoverIndex(-1);
            EnsureTable();
        }
        else
        {
            _canvas?.InvalidateVisual();
        }
    }

    private void EnsureTable()
    {
        if (_tableBuilt || _tableScroll is null) return;
        _tableBuilt = true;
        _tableScroll.Content = BuildTable();
    }

    // The table fills the well's width; wider than the well, it scrolls.
    private void OnTableScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_tableScroll?.Content is Grid grid && e.ViewportDelta.X != 0)
            grid.MinWidth = _tableScroll.Viewport.Width;
    }

    // The arrows move the table a line at a time, the way they point on screen; Home and End go to its ends.
    private void OnTableKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tableScroll is not { } scroll || e.KeyModifiers != KeyModifiers.None) return;
        var mirrored = scroll.FlowDirection == FlowDirection.RightToLeft;
        switch (e.Key)
        {
            case Key.Up: scroll.LineUp(); break;
            case Key.Down: scroll.LineDown(); break;
            case Key.Left when mirrored:
            case Key.Right when !mirrored:
                scroll.LineRight();
                break;
            case Key.Left:
            case Key.Right:
                scroll.LineLeft();
                break;
            case Key.Home: scroll.ScrollToHome(); break;
            case Key.End: scroll.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// The chart's data as a table: a row per category, with a column per series, or each part's value and
    /// share for a ring or pie. Numbers line up on the right.
    /// </summary>
    private Grid? BuildTable()
    {
        var (headers, rows) = TableData();
        if (headers.Count == 0) return null;

        var radial = ChartType is StrataChartType.Donut or StrataChartType.Pie;
        var grid = new Grid { Name = "PART_TableGrid", MinWidth = _tableScroll?.Viewport.Width ?? 0 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (int column = 1; column < headers.Count; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int column = 0; column < headers.Count; column++)
        {
            // A series' column carries its colour, so the table reads with the chart.
            IBrush? marker = !radial && column > 0 ? SeriesBrush(column - 1) : null;
            var header = Cell(headers[column], column > 0, header: true, marker);
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid.SetColumn(header, column);
            grid.Children.Add(header);
        }

        for (int row = 0; row < rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < headers.Count; column++)
            {
                IBrush? marker = radial && column == 0 ? SeriesBrush(row) : null;
                var cell = Cell(column < rows[row].Count ? rows[row][column] : "", column > 0, header: false, marker);
                cell.BorderThickness = new Thickness(0, 0, 0, row < rows.Count - 1 ? 1 : 0);
                Grid.SetRow(cell, row + 1);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private static Border Cell(string text, bool numeric, bool header, IBrush? marker)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = numeric ? TextWrapping.NoWrap : TextWrapping.Wrap,
            TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = MermaidTextHelper.GetFlowDirection(text),
        };
        if (numeric && !header)
            label.FontFeatures = TabularFigures;
        // At the host's caption size, which a phone raises to its own and scales with its font size.
        Themed(label, TextBlock.FontSizeProperty, "Font.SizeCaption");
        Themed(label, TextBlock.ForegroundProperty, header ? "Brush.TextSecondary" : "Brush.TextPrimary");

        Control content = label;
        if (marker is not null)
        {
            var swatch = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2.5),
                Background = marker,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Children = { swatch, label },
            };
        }

        // Built here, outside the template, so styled here: the theme's rules reach only the template's own parts.
        var cell = new Border
        {
            Child = content,
            Padding = header ? new Thickness(12, 8) : new Thickness(12, 6),
        };
        Themed(cell, Border.BorderBrushProperty, "Brush.GlassDivider");
        if (numeric)
            content.HorizontalAlignment = HorizontalAlignment.Right;
        return cell;
    }

    private static void Themed(Control control, AvaloniaProperty property, string key) =>
        control.Bind(property, control.GetResourceObservable(key));

    /// <summary>
    /// The table's headers and rows: its numbers as the table shows them, or, <paramref name="exact"/>, as the chart
    /// was given them, where a part given in percent keeps its own decimals.
    /// </summary>
    internal (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) TableData(bool exact = false)
    {
        var labels = Labels;
        var series = Series;
        var headers = new List<string>();
        var rows = new List<IReadOnlyList<string>>();
        if (labels is null || series is null || series.Count == 0)
            return (headers, rows);

        Func<double, string> number = exact ? FormatExactValue : FormatFullValue;
        var ui = InteractiveLabels.Current;
        headers.Add(ui["chartCategory"]);
        if (ChartType is StrataChartType.Donut or StrataChartType.Pie)
        {
            var values = series[0].Values;
            var total = 0.0;
            foreach (var value in values)
                total += Positive(value);
            // Values in percent already are their share: one column says it.
            var shares = ValuesAreShares(values);
            if (!shares)
                headers.Add(string.IsNullOrWhiteSpace(series[0].Name) ? ui["chartValue"] : series[0].Name);
            headers.Add(ui["chartShare"]);
            for (int i = 0; i < labels.Count; i++)
            {
                var value = i < values.Count ? values[i] : double.NaN;
                var share = total > 0 && double.IsFinite(value)
                    ? exact && shares ? number(value) + "%" : FormatShare(Positive(value) / total)
                    : "";
                rows.Add(shares ? new[] { labels[i], share } : new[] { labels[i], number(value), share });
            }
        }
        else
        {
            for (int s = 0; s < series.Count; s++)
                headers.Add(string.IsNullOrWhiteSpace(series[s].Name)
                    ? series.Count > 1 ? $"{ui["chartValue"]} {s + 1}" : ui["chartValue"]
                    : series[s].Name);
            for (int i = 0; i < labels.Count; i++)
            {
                var row = new List<string> { labels[i] };
                foreach (var item in series)
                    row.Add(i < item.Values.Count ? number(item.Values[i]) : "");
                rows.Add(row);
            }
        }

        return (headers, rows);
    }

    // ── Copy ───────────────────────────────────────────────────

    /// <summary>The chart's data as a markdown table: the table's rows, with every number as the chart was given it.</summary>
    internal string DataMarkdown()
    {
        var (headers, rows) = TableData(exact: true);
        if (headers.Count == 0) return "";
        var markdown = new StringBuilder();
        MarkdownClipboardFormatter.AppendRow(markdown, headers);
        MarkdownClipboardFormatter.AppendRow(markdown, Repeat("---", headers.Count));
        foreach (var row in rows)
            MarkdownClipboardFormatter.AppendRow(markdown, row);
        return markdown.ToString().TrimEnd();
    }

    private static IEnumerable<string> Repeat(string value, int count)
    {
        for (int i = 0; i < count; i++)
            yield return value;
    }

    private async void OnCopyData(object? sender, RoutedEventArgs e)
    {
        if (_copyData is null) return;
        var markdown = DataMarkdown();
        if (markdown.Length == 0) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            using var data = ChatClipboardData.CreateRichText(markdown);
            await clipboard.SetDataAsync(data);
        }
        catch
        {
            return;
        }

        var version = ++_copyFeedbackVersion;
        _copyData.Classes.Add("copied");
        if (_copyIcon is not null) _copyIcon.Data = StrataIcons.Check;
        ToolTip.SetTip(_copyData, InteractiveLabels.Current["copied"]);
        DispatcherTimer.RunOnce(() =>
        {
            if (version != _copyFeedbackVersion || _copyData is null) return;
            _copyData.Classes.Remove("copied");
            if (_copyIcon is not null) _copyIcon.Data = StrataIcons.Copy;
            ToolTip.SetTip(_copyData, InteractiveLabels.Current["chartCopyData"]);
        }, TimeSpan.FromMilliseconds(1400));
    }

    // ── Full screen ────────────────────────────────────────────

    private void OnExpand(object? sender, RoutedEventArgs e)
    {
        if (_expandedOwner is not null)
        {
            _expandedOwner.CloseExpandedView();
            return;
        }
        if (_expandedView is not null) return;

        var expanded = new StrataChart
        {
            Name = "ExpandedChart",
            ShowToolbar = true,
            ChartHeight = double.NaN,
            Theme = Theme,
            Margin = new Thickness(24),
            VerticalAlignment = VerticalAlignment.Stretch,
            _expandedOwner = this,
        };
        CopyChartTo(expanded);
        expanded.SetTableView(_isTableView);
        _expandedChart = expanded;
        _expandedView = StrataFullScreenOverlay.Open(this, "ChartFullScreen", expanded, () => _expandedChart?.Focus());
        if (_expandedView is null)
        {
            _expandedChart = null;
            return;
        }
        _expandedView.Closed += (_, _) =>
        {
            _expandedView = null;
            _expandedChart = null;
        };
    }

    private void CopyChartTo(StrataChart target)
    {
        target.ChartType = ChartType;
        target.Labels = Labels;
        target.Series = Series;
        target.ShowLegend = ShowLegend;
        target.ShowGrid = ShowGrid;
        target.DonutCenterLabel = DonutCenterLabel;
        target.DonutCenterValue = DonutCenterValue;
    }

    private void CloseExpandedView() => _expandedView?.Close();

    // ── Hover values ───────────────────────────────────────────

    /// <summary>
    /// Shows <paramref name="tip"/> on the pane of glass beside the place it describes, gliding there from where
    /// it was, or hides the pane.
    /// </summary>
    internal void ShowTip(ChartTip? tip)
    {
        if (_tip is null || _tipRows is null || _chartHost is null)
            return;

        if (tip is null || _isTableView)
        {
            _tip.IsVisible = false;
            _tipShown = false;
            return;
        }

        _tipRows.Children.Clear();
        _tipRows.FlowDirection = tip.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (!string.IsNullOrEmpty(tip.Title))
        {
            var title = new TextBlock
            {
                Text = tip.Title,
                FontWeight = FontWeight.SemiBold,
                MaxWidth = 260,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Themed(title, TextBlock.FontSizeProperty, "Font.SizeCaption");
            Themed(title, TextBlock.ForegroundProperty, "Brush.TextPrimary");
            _tipRows.Children.Add(title);
        }

        var grid = new Grid { ColumnSpacing = 7, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < tip.Rows.Count; i++)
        {
            var row = tip.Rows[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var marker = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2.5),
                Background = row.Marker,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var name = new TextBlock
            {
                Text = row.Name,
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Themed(name, TextBlock.FontSizeProperty, "Font.SizeCaption");
            Themed(name, TextBlock.ForegroundProperty, "Brush.TextSecondary");
            var value = new TextBlock
            {
                Text = row.Value,
                FontWeight = FontWeight.SemiBold,
                FontFeatures = TabularFigures,
                Margin = new Thickness(14, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Themed(value, TextBlock.FontSizeProperty, "Font.SizeCaption");
            Themed(value, TextBlock.ForegroundProperty, "Brush.TextPrimary");
            Grid.SetRow(marker, i);
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(value, 2);
            grid.Children.Add(marker);
            grid.Children.Add(name);
            grid.Children.Add(value);
        }
        _tipRows.Children.Add(grid);

        _tip.IsVisible = true;
        _tip.Measure(Size.Infinity);
        var size = _tip.DesiredSize;
        var host = _chartHost.Bounds.Size;
        var anchor = tip.Anchor;
        double x, y;
        if (tip.Direction == new Vector(1, 0))
        {
            // Beside the hovered values, where there is room, so they stay in sight.
            x = anchor.Right + 12;
            if (x + size.Width > host.Width - 4)
                x = anchor.Left - 12 - size.Width;
            y = anchor.Top - size.Height / 2;
        }
        else
        {
            // Outward from the ring, past the hovered part.
            x = anchor.X + tip.Direction.X * (size.Width / 2 + 6) - size.Width / 2;
            y = anchor.Y + tip.Direction.Y * (size.Height / 2 + 6) - size.Height / 2;
        }
        x = Math.Max(4, Math.Min(x, host.Width - size.Width - 4));
        y = Math.Max(4, Math.Min(y, host.Height - size.Height - 4));

        var place = TransformOperations.CreateBuilder(1);
        place.AppendTranslate(Math.Round(x), Math.Round(y));
        var transform = place.Build();
        if (_tipShown)
        {
            _tip.RenderTransform = transform;
            return;
        }

        // Appearing, the pane fades in where it belongs rather than gliding from where it last was.
        var transitions = _tip.Transitions;
        _tip.Transitions = null;
        _tip.RenderTransform = transform;
        _tip.Opacity = 0;
        _tip.Transitions = transitions;
        _tip.Opacity = 1;
        _tipShown = true;
    }
}
