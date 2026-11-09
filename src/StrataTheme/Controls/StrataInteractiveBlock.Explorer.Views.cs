using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private Grid ExplorerTopBar(Control? middle, Control? trailing)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Name = "ExplorerTopBar" };
        var back = new Button
        {
            Name = "ExplorerBack", Classes = { "subtle", "explorer-back" },
            Content = InteractiveIcons.Labeled(InteractiveIcons.ArrowBack, _labels["allOptions"])
        };
        AutomationProperties.SetName(back, _labels["allOptions"]);
        back.Click += (_, _) => ShowExplorer(ExplorerMode.Browse);
        bar.Children.Add(back);
        if (middle is not null)
        {
            middle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(middle, 1);
            bar.Children.Add(middle);
        }
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            bar.Children.Add(trailing);
        }
        return bar;
    }

    private Button ExplorerIconButton(string name, string icon, string label, Action action)
    {
        var button = new Button { Name = name, Content = InteractiveIcons.Create(icon, 16), Classes = { "subtle", "explorer-icon-button" } };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private Button ExplorerSourceButton(InteractiveExplorerItem item, Uri uri, bool compact)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var label = _labels.Format("openSite", host);
        var button = new Button
        {
            Name = "ExplorerSource_" + SafeName(item.Id),
            Content = InteractiveIcons.Labeled(InteractiveIcons.OpenExternal, compact ? host : label, trailing: compact),
            Classes = { compact ? "explorer-link" : "explorer-source" }
        };
        if (compact)
            button.Classes.Add("subtle");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                _ = launcher.LaunchUriAsync(uri);
        };
        return button;
    }

    private static Uri? WebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;

    // ── Detail ──

    private Control BuildExplorerDetail(InteractiveExplorerItem item, out Control? landmark)
    {
        var model = _explorer!;
        IReadOnlyList<InteractiveExplorerItem> sequence = model.Apply(_state);
        if (!sequence.Contains(item))
            sequence = model.Definition.Items;
        var index = sequence.ToList().IndexOf(item);
        void Step(int delta)
        {
            _explorerFocus = sequence[(index + delta + sequence.Count) % sequence.Count].Id;
            ShowExplorer(ExplorerMode.Detail);
        }

        var root = new StackPanel { Spacing = 18, Name = "ExplorerDetail", Focusable = true };
        root.KeyDown += (_, args) =>
        {
            var reversed = FlowDirection == FlowDirection.RightToLeft;
            switch (args.Key)
            {
                case Key.Escape:
                    ShowExplorer(ExplorerMode.Browse);
                    break;
                case Key.Left when sequence.Count > 1:
                    Step(reversed ? 1 : -1);
                    break;
                case Key.Right when sequence.Count > 1:
                    Step(reversed ? -1 : 1);
                    break;
                default:
                    return;
            }
            args.Handled = true;
        };

        Control? pager = null;
        if (sequence.Count > 1)
        {
            var position = Text(_labels.Format("itemPosition", index + 1, sequence.Count), "explorer-position");
            position.TextWrapping = TextWrapping.NoWrap;
            position.VerticalAlignment = VerticalAlignment.Center;
            pager = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children =
                {
                    ExplorerIconButton("ExplorerPrevious", InteractiveIcons.ChevronBack, _labels["previousOption"], () => Step(-1)),
                    position,
                    ExplorerIconButton("ExplorerNext", InteractiveIcons.ChevronForward, _labels["nextOption"], () => Step(1))
                }
            };
        }
        var bar = ExplorerTopBar(null, pager);
        root.Children.Add(bar);
        landmark = bar;

        var info = new StackPanel { Spacing = 6 };
        if (!string.IsNullOrWhiteSpace(item.Badge))
        {
            var badge = ExplorerBadge(item.Badge!);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            info.Children.Add(badge);
        }
        info.Children.Add(Text(item.Label, "explorer-detail-title"));
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
            info.Children.Add(Text(item.Subtitle, "explorer-subtitle"));
        if (ExplorerFigures(item, hero: true) is { } figures)
        {
            figures.Margin = new Thickness(0, 6, 0, 0);
            info.Children.Add(figures);
        }
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(ExplorerToggle(item, "view", wide: true));
        if (WebAddress(item.Url) is { } address)
            actions.Children.Add(ExplorerSourceButton(item, address, compact: false));
        info.Children.Add(actions);

        if (ExplorerShowsMedia)
        {
            var media = ExplorerMedia(item, _explorerNarrow ? 196 : 228, new CornerRadius(10), 64);
            var hero = new Grid { ColumnSpacing = 22, RowSpacing = 16 };
            if (_explorerNarrow)
            {
                hero.RowDefinitions = new RowDefinitions("Auto,Auto");
                Grid.SetRow(info, 1);
            }
            else
            {
                hero.ColumnDefinitions = new ColumnDefinitions("5*,6*");
                Grid.SetColumn(info, 1);
                info.VerticalAlignment = VerticalAlignment.Center;
            }
            hero.Children.Add(media);
            hero.Children.Add(info);
            root.Children.Add(hero);
        }
        else
            root.Children.Add(info);

        if (!string.IsNullOrWhiteSpace(item.Summary))
            root.Children.Add(Text(item.Summary, "explorer-summary"));

        var points = new List<(Control Cell, bool Full)>();
        if (item.Pros.Count > 0)
            points.Add((ExplorerPoints(_labels["pros"], item.Pros, positive: true), false));
        if (item.Cons.Count > 0)
            points.Add((ExplorerPoints(_labels["cons"], item.Cons, positive: false), false));
        if (points.Count > 0)
        {
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            ArrangeCells(grid, points, !_explorerNarrow && points.Count == 2 ? 2 : 1);
            root.Children.Add(grid);
        }

        var specs = model.Attributes.Where(attribute => !model.Value(item, attribute).IsEmpty).ToList();
        if (specs.Count > 0)
        {
            var pairs = _explorerNarrow || specs.Count < 3 ? 1 : 2;
            var grid = new Grid
            {
                ColumnSpacing = 16, RowSpacing = 10,
                ColumnDefinitions = new ColumnDefinitions(pairs == 2 ? "Auto,*,Auto,*" : "Auto,*")
            };
            for (var position = 0; position < specs.Count; position++)
            {
                var row = position / pairs;
                var column = position % pairs * 2;
                if (column == 0)
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var key = Text(specs[position].Label, "explorer-spec-key");
                key.MaxWidth = 160;
                key.VerticalAlignment = VerticalAlignment.Center;
                var (view, _) = ExplorerValueView(model.Value(item, specs[position]), specs[position], compact: false);
                view.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(key, row);
                Grid.SetColumn(key, column);
                Grid.SetRow(view, row);
                Grid.SetColumn(view, column + 1);
                grid.Children.Add(key);
                grid.Children.Add(view);
            }
            var section = new StackPanel { Spacing = 10 };
            section.Children.Add(Text(_labels["details"], "explorer-section"));
            section.Children.Add(new Border { Classes = { "explorer-specs" }, Child = grid });
            root.Children.Add(section);
        }
        return root;
    }

    private Control ExplorerPoints(string title, IReadOnlyList<string> points, bool positive)
    {
        var panel = new StackPanel { Spacing = 8 };
        var icon = InteractiveIcons.Create(positive ? InteractiveIcons.Check : InteractiveIcons.Minus, 14);
        icon.Classes.Add(positive ? "explorer-yes" : "explorer-con");
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { icon, Text(title, "explorer-points-title") }
        });
        panel.Children.Add(ExplorerPointList(points, positive));
        var card = new Border { Classes = { "explorer-points" }, Child = panel };
        card.Classes.Add(positive ? "pro" : "con");
        return card;
    }

    private Control ExplorerPointList(IReadOnlyList<string> points, bool positive)
    {
        var list = new StackPanel { Spacing = 6 };
        foreach (var point in points.Where(point => !string.IsNullOrWhiteSpace(point)))
        {
            var dot = new Border
            {
                Classes = { "explorer-dot", positive ? "pro" : "con" },
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0)
            };
            var text = Text(point, "explorer-point");
            Grid.SetColumn(text, 1);
            list.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { dot, text } });
        }
        return list;
    }

    // ── Comparison ──

    private Control BuildExplorerCompare(bool animate, out Control? landmark)
    {
        var model = _explorer!;
        var items = _state.SelectedIds.Select(FindExplorerItem).OfType<InteractiveExplorerItem>().ToList();
        var root = new StackPanel { Spacing = 14, Name = "ExplorerCompareView", Focusable = true };
        root.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            ShowExplorer(ExplorerMode.Browse);
            args.Handled = true;
        };

        var differences = new ToggleButton
        {
            Name = "ExplorerDifferences", Content = _labels["onlyDifferences"], IsChecked = _explorerDifferencesOnly,
            Classes = { "interactive-chip", "multi" }
        };
        AutomationProperties.SetName(differences, _labels["onlyDifferences"]);
        differences.IsCheckedChanged += (_, _) =>
        {
            _explorerDifferencesOnly = differences.IsChecked == true;
            ShowExplorer(ExplorerMode.Compare, animate: false);
        };
        var title = Text(_labels.Format("comparingCount", items.Count), "explorer-compare-title");
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var bar = ExplorerTopBar(_explorerNarrow ? null : title, differences);
        root.Children.Add(bar);
        landmark = bar;

        var rows = new List<(string Label, Func<InteractiveExplorerItem, Control> Cell)>();
        foreach (var attribute in model.Attributes)
        {
            var values = items.Select(item => model.Value(item, attribute)).ToList();
            if (values.All(value => value.IsEmpty))
                continue;
            if (_explorerDifferencesOnly && values.Select(value => model.Format(value, attribute, _labels)).Distinct().Count() == 1)
                continue;
            var best = model.Best(attribute, items);
            rows.Add((attribute.Label, item => ExplorerCompareCell(item, attribute, items, best, animate)));
        }
        if (items.Any(item => item.Pros.Count > 0))
            rows.Add((_labels["pros"], item => ExplorerPointList(item.Pros, positive: true)));
        if (items.Any(item => item.Cons.Count > 0))
            rows.Add((_labels["cons"], item => ExplorerPointList(item.Cons, positive: false)));
        if (items.Any(item => WebAddress(item.Url) is not null))
            rows.Add((_labels["source"], item => WebAddress(item.Url) is { } address
                ? ExplorerSourceButton(item, address, compact: true) : Text("\u2014", "explorer-muted")));

        var content = Math.Max(0, _explorerWidth - (TouchMode ? 28 : 32) - 2);
        const double labelWidth = 132, minColumn = 136;
        var count = Math.Max(1, items.Count);
        // Labels sit beside the values while every option keeps a readable column. Otherwise each label heads
        // its row, so the options share the whole width, and stays in view however far the row scrolls.
        var besideLabels = !_explorerNarrow && (content - labelWidth) / count >= minColumn;
        var scrolls = !besideLabels && content / count < minColumn;
        var first = besideLabels ? 1 : 0;
        var grid = new Grid { Name = "ExplorerCompareGrid" };
        if (besideLabels)
            grid.ColumnDefinitions.Add(new ColumnDefinition(labelWidth, GridUnitType.Pixel));
        foreach (var _ in items)
            grid.ColumnDefinitions.Add(scrolls ? new ColumnDefinition(150, GridUnitType.Pixel) : new ColumnDefinition(1, GridUnitType.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < items.Count; column++)
        {
            var head = ExplorerCompareHeader(items[column]);
            if (!besideLabels)
                head.Margin = new Thickness(0, 0, 12, 12);
            Grid.SetColumn(head, column + first);
            grid.Children.Add(head);
        }
        var headings = new List<TextBlock>();
        foreach (var (labelText, cellFor) in rows)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var line = new Border { Classes = { "explorer-rule" }, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, grid.ColumnDefinitions.Count);
            grid.Children.Add(line);
            var label = Text(labelText, "explorer-compare-label");
            Grid.SetRow(label, row);
            if (besideLabels)
                label.Margin = new Thickness(0, 12, 12, 12);
            else
            {
                label.Margin = new Thickness(0, 12, 0, 0);
                label.HorizontalAlignment = HorizontalAlignment.Left;
                // Spanning a row wider than the view, a long label still wraps within what is visible.
                if (scrolls && content > 0)
                    label.MaxWidth = content;
                Grid.SetColumnSpan(label, items.Count);
                headings.Add(label);
                row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            grid.Children.Add(label);
            for (var column = 0; column < items.Count; column++)
            {
                var cell = cellFor(items[column]);
                cell.Margin = besideLabels ? new Thickness(12, 12, 8, 12) : new Thickness(0, 8, 12, 12);
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column + first);
                grid.Children.Add(cell);
            }
        }
        Control table = grid;
        if (scrolls)
        {
            var viewer = new ScrollViewer
            {
                Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            // The row headings move with the scroll, so they stay where the visible columns start.
            foreach (var heading in headings)
                heading.RenderTransform = new TranslateTransform();
            viewer.ScrollChanged += (_, _) =>
            {
                foreach (var heading in headings)
                    ((TranslateTransform)heading.RenderTransform!).X = viewer.Offset.X;
            };
            table = viewer;
        }
        root.Children.Add(table);
        if (rows.Count == 0)
            root.Children.Add(Text(_labels["noDifferences"], "interactive-hint"));
        return root;
    }

    private Control ExplorerCompareHeader(InteractiveExplorerItem item)
    {
        var head = new StackPanel { Spacing = 8, Margin = new Thickness(12, 0, 8, 12) };
        if (ExplorerShowsMedia)
            head.Children.Add(ExplorerMedia(item, 92, new CornerRadius(8), 28));
        var name = Text(item.Label, "explorer-compare-name");
        name.MaxLines = 3;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var remove = ExplorerIconButton("ExplorerRemove_" + SafeName(item.Id), InteractiveIcons.Close,
            _labels.Format("removeFromCompare", item.Label), () => SetExplorerSelected(item.Id, false));
        remove.Classes.Add("small");
        remove.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(remove, 1);
        head.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4, Children = { name, remove } });
        if (!string.IsNullOrWhiteSpace(item.Badge))
        {
            var badge = ExplorerBadge(item.Badge!, compact: true);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(badge);
        }
        return head;
    }

    private Control ExplorerCompareCell(InteractiveExplorerItem item, ExplorerAttribute attribute,
        IReadOnlyList<InteractiveExplorerItem> items, IReadOnlySet<string> best, bool animate)
    {
        var value = _explorer!.Value(item, attribute);
        var (view, text) = ExplorerValueView(value, attribute, compact: false);
        var isBest = best.Contains(item.Id);
        text?.Classes.Set("best", isBest);
        // Ties rank nothing, so their meters would only add noise.
        if (value.Number is not { } number || attribute.Better == 0 || best.Count == 0)
            return view;
        var line = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        view.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(view);
        if (isBest)
            line.Children.Add(new Border
            {
                Classes = { "explorer-best" }, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = _labels["best"] }
            });
        var cell = new StackPanel { Spacing = 7 };
        cell.Children.Add(line);
        cell.Children.Add(ExplorerMeter(_explorer.Standing(attribute, number, items), isBest, animate));
        return cell;
    }

    /// <summary>A short bar, longest for the best value, that grows in when the comparison opens.</summary>
    private static Control ExplorerMeter(double standing, bool best, bool animate)
    {
        var fraction = 0.12 + 0.88 * Math.Clamp(standing, 0, 1);
        var fill = new Border { Classes = { "explorer-meter-fill" }, RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative) };
        fill.Classes.Set("best", best);
        // A comparison rebuilt in place (a filter, a removal, a resize) shows its bars as they are.
        if (animate)
        {
            fill.RenderTransform = TransformOperations.Parse("scaleX(0.02)");
            fill.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                fill.Transitions = new Transitions
                {
                    new TransformOperationsTransition
                    {
                        Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new CubicEaseOut()
                    }
                };
                fill.RenderTransform = TransformOperations.Parse("scaleX(1)");
            }, DispatcherPriority.Loaded);
        }
        var track = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(fraction, GridUnitType.Star),
                new ColumnDefinition(1 - fraction + 0.0001, GridUnitType.Star)
            },
            Children = { fill }
        };
        return new Border { Classes = { "explorer-meter" }, Child = track, MaxWidth = 150, HorizontalAlignment = HorizontalAlignment.Stretch };
    }
}
