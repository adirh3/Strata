using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using StrataTheme.Animation;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private Grid? _explorerToolbar;
    private TextBox? _explorerSearch;
    private DockPanel? _explorerTools;
    private Button? _explorerFiltersButton;
    private Button? _explorerSortButton;
    private Control? _explorerViews;
    private Border? _explorerFilterHost;
    private WrapPanel? _explorerActive;
    private Border? _explorerResults;
    private Border? _explorerCallout;
    private TextBlock? _explorerCalloutText;
    private Button? _explorerCalloutClear;

    private Control BuildExplorerBrowse()
    {
        var model = _explorer!;
        var root = new StackPanel { Spacing = 12, Name = "ExplorerBrowse" };
        _explorerSearch = null;
        _explorerFiltersButton = null;
        _explorerSortButton = null;
        _explorerViews = null;

        if (model.Definition.Items.Count > 5)
        {
            var icon = InteractiveIcons.Create(InteractiveIcons.Search, 14);
            icon.Margin = new Thickness(12, 0, -4, 0);
            icon.Classes.Add("interactive-adornment-icon");
            var search = new TextBox
            {
                Name = "ExplorerSearch", Text = _state.Values.GetValueOrDefault("query") ?? "",
                PlaceholderText = _labels["searchOptions"], InnerLeftContent = icon,
                MaxLength = InteractiveExplorerModel.MaxQueryLength, Classes = { "interactive-input" }
            };
            AutomationProperties.SetName(search, _labels["searchOptions"]);
            search.TextChanged += (_, _) =>
            {
                var text = search.Text ?? "";
                if (text.Trim().Length == 0)
                    _state.Values.Remove("query");
                else
                    _state.Values["query"] = text;
                _typedEditPending = true;
                RefreshExplorerResults();
            };
            search.LostFocus += (_, _) => CommitTypedEdit();
            search.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Enter) return;
                CommitTypedEdit();
                args.Handled = true;
            };
            _explorerSearch = search;
        }
        // Sort comes last and takes what is left of a narrow row, trimming its label rather than overlapping.
        _explorerTools = new DockPanel { HorizontalSpacing = 8 };
        if (model.Facets.Count > 0)
        {
            _explorerFiltersButton = ToolButton("ExplorerFilters");
            _explorerFiltersButton.Click += (_, _) =>
            {
                _explorerFiltersOpen = !_explorerFiltersOpen;
                UpdateExplorerFilterPanel(animate: true);
                UpdateExplorerChips();
                UpdateExplorerTools();
            };
            DockPanel.SetDock(_explorerFiltersButton, Dock.Left);
            _explorerTools.Children.Add(_explorerFiltersButton);
        }
        if (model.Sorts.Count > 0)
        {
            _explorerSortButton = ToolButton("ExplorerSort");
            _explorerSortButton.HorizontalAlignment = HorizontalAlignment.Left;
            _explorerSortButton.Click += (_, _) => OpenExplorerSort(_explorerSortButton);
            _explorerTools.Children.Add(_explorerSortButton);
        }
        if (model.Attributes.Count > 0)
            _explorerViews = ExplorerViewSwitch();
        _explorerToolbar = new Grid { ColumnSpacing = 8, RowSpacing = 8, Name = "ExplorerToolbar" };
        ArrangeExplorerToolbar();
        if (_explorerSearch is not null || _explorerTools.Children.Count > 0 || _explorerViews is not null)
            root.Children.Add(_explorerToolbar);

        _explorerFilterHost = new Border { IsVisible = false };
        root.Children.Add(_explorerFilterHost);
        _explorerActive = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Name = "ExplorerActiveFilters", IsVisible = false };
        root.Children.Add(_explorerActive);
        _explorerResults = new Border { Name = "ExplorerResults" };
        root.Children.Add(_explorerResults);
        root.Children.Add(BuildExplorerCallout());
        UpdateExplorerFilterPanel();
        UpdateExplorerTools();
        return root;
    }

    private static Button ToolButton(string name)
    {
        var button = new Button { Name = name, Classes = { "explorer-tool" } };
        return button;
    }

    private void UpdateExplorerTools()
    {
        if (_explorer is null)
            return;
        var active = _explorer.ActiveFilters(_state).Count(filter => filter.Query is null);
        if (_explorerFiltersButton is { } filters)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(InteractiveIcons.Create(InteractiveIcons.Tune, 14));
            content.Children.Add(new TextBlock { Text = _labels["filters"], VerticalAlignment = VerticalAlignment.Center });
            if (active > 0)
                content.Children.Add(new Border
                {
                    Classes = { "explorer-tool-count" },
                    Child = new TextBlock { Text = active.ToString(CultureInfo.CurrentCulture) }
                });
            filters.Content = content;
            filters.Classes.Set("active", _explorerFiltersOpen || active > 0);
            AutomationProperties.SetName(filters, active > 0 ? _labels.Format("filtersActive", active) : _labels["filters"]);
        }
        if (_explorerSortButton is { } sort)
        {
            var order = _explorer.Sort(_state);
            var label = SortLabel(order);
            sort.Content = SortButtonContent(order, label);
            sort.Classes.Set("active", order is not null);
            AutomationProperties.SetName(sort, _labels.Format("sortedBy", label));
        }
    }

    /// <summary>
    /// The sort and its label. A narrow row has no room for a label such as "Price: low to high", so it shows
    /// only the icon until there is a sort, then the sorted attribute and its direction, as the table's header
    /// does. The menu and the accessible name keep the full label.
    /// </summary>
    private Control SortButtonContent(ExplorerSort? order, string label)
    {
        var icon = InteractiveIcons.Create(InteractiveIcons.Sort, 14);
        if (_explorerNarrow && order is null)
            return icon;
        var text = new TextBlock
        {
            Text = _explorerNarrow && order is { } sorted ? SortField(sorted) : label,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis
        };
        var trailing = _explorerNarrow
            ? InteractiveIcons.Create(order!.Value.Descending ? InteractiveIcons.ArrowDown : InteractiveIcons.ArrowUp, 11)
            : InteractiveIcons.Create(InteractiveIcons.ChevronDown, 12);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(trailing, 2);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6,
            Children = { icon, text, trailing }
        };
    }

    private string SortField(ExplorerSort order) =>
        order.AttributeId.Length == 0 ? _labels["option"] : _explorer!.Attribute(order.AttributeId)!.Label;

    private void ArrangeExplorerToolbar()
    {
        if (_explorerToolbar is not { } toolbar || _explorerTools is null)
            return;
        toolbar.Children.Clear();
        toolbar.RowDefinitions.Clear();
        var narrow = _explorerStacked && _explorerSearch is not null;
        // Without a search box beside them the tools lead the row; otherwise they follow it. Only the columns
        // in use are defined, since an empty one would still take its spacing.
        var toolsLead = _explorerSearch is null || narrow;
        toolbar.ColumnDefinitions = new ColumnDefinitions(toolsLead ? "*,Auto" : "*,Auto,Auto");
        if (_explorerSearch is { } search)
        {
            Grid.SetColumnSpan(search, narrow ? 2 : 1);
            Grid.SetColumn(search, 0);
            Grid.SetRow(search, 0);
            toolbar.Children.Add(search);
        }
        if (narrow)
            toolbar.RowDefinitions = new RowDefinitions("Auto,Auto");
        var toolRow = narrow ? 1 : 0;
        Grid.SetColumn(_explorerTools, toolsLead ? 0 : 1);
        Grid.SetRow(_explorerTools, toolRow);
        _explorerTools.HorizontalAlignment = HorizontalAlignment.Left;
        toolbar.Children.Add(_explorerTools);
        if (_explorerViews is { } views)
        {
            Grid.SetColumn(views, toolsLead ? 1 : 2);
            Grid.SetRow(views, toolRow);
            views.HorizontalAlignment = HorizontalAlignment.Right;
            toolbar.Children.Add(views);
        }
    }

    private Control ExplorerViewSwitch()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        RadioButton Segment(string view, string icon, string label)
        {
            var segment = new RadioButton
            {
                Name = "ExplorerView_" + view,
                Content = InteractiveIcons.Create(icon, 14),
                IsChecked = _explorer!.View(_state) == view,
                Classes = { "explorer-segment" }
            };
            AutomationProperties.SetName(segment, label);
            segment.IsCheckedChanged += (_, _) =>
            {
                if (segment.IsChecked != true || _explorer.View(_state) == view)
                    return;
                _state.Values["view"] = view;
                RefreshExplorerResults(animate: true);
                CommitState();
            };
            return segment;
        }
        panel.Children.Add(Segment("cards", InteractiveIcons.Cards, _labels["viewCards"]));
        panel.Children.Add(Segment("table", InteractiveIcons.Table, _labels["viewTable"]));
        return new Border { Classes = { "explorer-segmented" }, Child = panel, VerticalAlignment = VerticalAlignment.Center };
    }

    // ── Sorting ──

    private string SortLabel(ExplorerSort? sort)
    {
        if (sort is not { } order)
            return _labels["sortRecommended"];
        if (order.AttributeId.Length == 0)
            return _labels.Format(order.Descending ? "sortZa" : "sortAz", _labels["option"]);
        var attribute = _explorer!.Attribute(order.AttributeId)!;
        if (attribute.Type == ExplorerAttributeType.Text)
            return _labels.Format(order.Descending ? "sortZa" : "sortAz", attribute.Label);
        return _labels.Format(order.Descending ? "sortHighLow" : "sortLowHigh", attribute.Label);
    }

    private void SetExplorerSort(ExplorerSort? sort)
    {
        if (sort is { } order)
            _state.Values["sort"] = order.Key;
        else
            _state.Values.Remove("sort");
        RefreshExplorerResults(animate: true);
        CommitState();
    }

    private void OpenExplorerSort(Button anchor)
    {
        var model = _explorer!;
        var current = model.Sort(_state);
        var options = new List<ExplorerSort?> { null };
        options.AddRange(model.Sorts.Select(sort => (ExplorerSort?)sort));
        if (current is { } custom && !model.Sorts.Contains(custom))
            options.Add(custom);
        var menu = new StackPanel
        {
            Spacing = 2, FlowDirection = FlowDirection, MinWidth = TouchMode ? 0 : 220,
            Classes = { TouchMode ? "explorer-sheet" : "explorer-menu" }
        };
        Flyout? flyout = null;
        foreach (var option in options)
        {
            var selected = option == current;
            var check = InteractiveIcons.Create(InteractiveIcons.Check, TouchMode ? 18 : 14);
            check.Classes.Add("sheet-check");
            check.IsVisible = selected;
            Grid.SetColumn(check, 1);
            var label = SortLabel(option);
            var item = new Button
            {
                Name = "ExplorerSortOption_" + SafeName(option?.Key ?? "recommended"),
                Content = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12,
                    Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, check }
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Classes = { "subtle", TouchMode ? "sheet-item" : "explorer-menu-item" }
            };
            item.Classes.Set("selected", selected);
            AutomationProperties.SetName(item, label);
            item.Click += (_, _) =>
            {
                if (flyout is not null)
                    flyout.Hide();
                else
                    ClosePicker();
                SetExplorerSort(option);
            };
            menu.Children.Add(item);
        }
        if (TouchMode)
            ShowPicker(_labels["sortBy"], menu);
        else
        {
            flyout = new Flyout { Content = menu, Placement = PlacementMode.BottomEdgeAlignedRight };
            flyout.ShowAt(anchor);
        }
    }

    private void CycleExplorerSort(string attributeId)
    {
        var current = _explorer!.Sort(_state);
        var attribute = _explorer.Attribute(attributeId);
        var natural = attribute is { IsNumeric: true } && attribute.Better >= 0;
        if (current is { } order && order.AttributeId == attributeId)
            SetExplorerSort(order.Descending == natural ? new ExplorerSort(attributeId, !natural) : null);
        else
            SetExplorerSort(new ExplorerSort(attributeId, natural));
    }

    // ── Filters ──

    private void UpdateExplorerFilterPanel(bool animate = false)
    {
        if (_explorerFilterHost is not { } host)
            return;
        host.IsVisible = _explorerFiltersOpen;
        host.Child = _explorerFiltersOpen ? BuildExplorerFilterPanel() : null;
        if (animate && _explorerFiltersOpen && host.Child is { } panel)
            SlideFadeEntrance.PlayComposited(panel, 6, TimeSpan.FromMilliseconds(200));
    }

    private Control BuildExplorerFilterPanel()
    {
        var model = _explorer!;
        var rows = new List<(string Label, Control Content)>();
        var flags = model.Facets.Where(facet => facet.Kind == ExplorerFacetKind.Flag).ToList();
        foreach (var facet in model.Facets)
        {
            switch (facet.Kind)
            {
                case ExplorerFacetKind.Choice:
                    rows.Add((facet.Attribute.Label, FilterChips(facet.Options.Select(option => (facet, option)))));
                    break;
                case ExplorerFacetKind.Flag when facet == flags[0]:
                    rows.Add((_labels["mustHave"], FilterChips(flags.Select(flag => (flag, flag.Attribute.Label)))));
                    break;
                case ExplorerFacetKind.Threshold:
                    rows.Add((facet.Attribute.Label, ThresholdFilter(facet)));
                    break;
            }
        }
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = _explorerNarrow ? 6 : 14 };
        grid.ColumnDefinitions = new ColumnDefinitions(_explorerNarrow ? "*" : "Auto,*");
        var row = 0;
        foreach (var (label, content) in rows)
        {
            var caption = Text(label, "explorer-filter-label");
            caption.VerticalAlignment = content is WrapPanel ? VerticalAlignment.Top : VerticalAlignment.Center;
            caption.Margin = content is WrapPanel && !_explorerNarrow ? new Thickness(0, 7, 0, 0) : default;
            // A label beside its filter stays narrow; one above it lines up with the filter's start.
            caption.HorizontalAlignment = HorizontalAlignment.Left;
            if (!_explorerNarrow)
                caption.MaxWidth = 160;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(caption, row);
            grid.Children.Add(caption);
            if (_explorerNarrow)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                row++;
                content.Margin = new Thickness(0, 0, 0, 8);
            }
            Grid.SetRow(content, row);
            Grid.SetColumn(content, _explorerNarrow ? 0 : 1);
            grid.Children.Add(content);
            row++;
        }
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(grid);
        if (model.ActiveFilters(_state).Any(filter => filter.Query is null))
        {
            var clear = new Button { Name = "ExplorerClearFilters", Classes = { "subtle", "explorer-link" } };
            clear.Content = _labels["clearFilters"];
            AutomationProperties.SetName(clear, _labels["clearFilters"]);
            clear.Click += (_, _) => ClearExplorerFilters(keepQuery: true);
            panel.Children.Add(clear);
        }
        return new Border { Name = "ExplorerFilterPanel", Classes = { "explorer-filters" }, Child = panel };
    }

    private WrapPanel FilterChips(IEnumerable<(ExplorerFacet Facet, string Option)> options)
    {
        var chips = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var (facet, option) in options)
        {
            var value = facet.Kind == ExplorerFacetKind.Flag ? "true" : option;
            var chip = new ToggleButton
            {
                Content = option,
                Name = "ExplorerFilter_" + SafeName(facet.Attribute.Id) + "_" + SafeName(value),
                IsChecked = _state.Choices.GetValueOrDefault(facet.Key)?.Contains(value, StringComparer.OrdinalIgnoreCase) == true,
                Classes = { "interactive-chip", "multi" }
            };
            AutomationProperties.SetName(chip, facet.Kind == ExplorerFacetKind.Flag ? option : $"{facet.Attribute.Label}: {option}");
            chip.IsCheckedChanged += (_, _) =>
            {
                if (!_state.Choices.TryGetValue(facet.Key, out var chosen))
                    _state.Choices[facet.Key] = chosen = [];
                chosen.RemoveAll(entry => string.Equals(entry, value, StringComparison.OrdinalIgnoreCase));
                if (chip.IsChecked == true)
                    chosen.Add(value);
                if (chosen.Count == 0)
                    _state.Choices.Remove(facet.Key);
                AfterExplorerFilterChange(commit: true);
            };
            chips.Children.Add(chip);
        }
        return chips;
    }

    private Control ThresholdFilter(ExplorerFacet facet)
    {
        var bound = InteractiveExplorerModel.Bound(_state, facet);
        var value = Text("", "explorer-slider-value");
        value.TextWrapping = TextWrapping.NoWrap;
        value.VerticalAlignment = VerticalAlignment.Center;
        var slider = new Slider
        {
            Name = "ExplorerBound_" + SafeName(facet.Attribute.Id),
            Minimum = facet.Minimum, Maximum = facet.Maximum,
            TickFrequency = facet.Step, IsSnapToTickEnabled = true,
            SmallChange = facet.Step, LargeChange = facet.Step * 5,
            Value = bound ?? (facet.IsUpperBound ? facet.Maximum : facet.Minimum),
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "explorer-slider" }
        };
        AutomationProperties.SetName(slider, facet.Attribute.Label);
        void Describe()
        {
            value.Text = InteractiveExplorerModel.Bound(_state, facet) is { } current
                ? _labels.Format(facet.IsUpperBound ? "upTo" : "andUp", InteractiveExplorerModel.FormatNumber(facet.Attribute, current))
                : _labels["anyValue"];
        }
        slider.ValueChanged += (_, _) =>
        {
            var snapped = InteractiveExplorerModel.Snap(slider.Value, facet.Step);
            var open = facet.IsUpperBound ? snapped >= facet.Maximum : snapped <= facet.Minimum;
            if (open)
                _state.Values.Remove(facet.Key);
            else
                _state.Values[facet.Key] = snapped.ToString("R", CultureInfo.InvariantCulture);
            Describe();
            AfterExplorerFilterChange(commit: false);
            ScheduleExplorerCommit();
        };
        Describe();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        value.MinWidth = 96;
        Grid.SetColumn(value, 1);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid;
    }

    private void AfterExplorerFilterChange(bool commit)
    {
        RefreshExplorerResults();
        UpdateExplorerTools();
        if (commit)
            CommitState();
    }

    private void ClearExplorerFilters(bool keepQuery)
    {
        foreach (var facet in _explorer!.Facets)
        {
            _state.Choices.Remove(facet.Attribute.Id);
            _state.Values.Remove(ExplorerFacet.BoundKey(facet.Attribute.Id, true));
            _state.Values.Remove(ExplorerFacet.BoundKey(facet.Attribute.Id, false));
        }
        if (!keepQuery && _explorerSearch is { } search)
        {
            _state.Values.Remove("query");
            search.Text = "";
        }
        UpdateExplorerFilterPanel();
        AfterExplorerFilterChange(commit: true);
    }

    private void RemoveExplorerFilter(ExplorerFilter filter)
    {
        if (filter.Query is not null)
        {
            _state.Values.Remove("query");
            if (_explorerSearch is { } search)
                search.Text = "";
        }
        else if (filter.Facet is { Kind: ExplorerFacetKind.Choice } choice
                 && _state.Choices.TryGetValue(choice.Key, out var chosen))
        {
            chosen.RemoveAll(option => string.Equals(option, filter.Option, StringComparison.OrdinalIgnoreCase));
            if (chosen.Count == 0)
                _state.Choices.Remove(choice.Key);
        }
        else if (filter.Facet is { Kind: ExplorerFacetKind.Flag } flag)
            _state.Choices.Remove(flag.Key);
        else if (filter.Facet is { } bound)
            _state.Values.Remove(bound.Key);
        UpdateExplorerFilterPanel();
        AfterExplorerFilterChange(commit: true);
    }

    private void UpdateExplorerChips()
    {
        if (_explorerActive is not { } active || _explorer is null)
            return;
        active.Children.Clear();
        // The search box already shows its text; chips summarize the filters behind the panel.
        var filters = _explorer.ActiveFilters(_state).Where(filter => filter.Query is null).ToList();
        active.IsVisible = filters.Count > 0 && !_explorerFiltersOpen;
        if (!active.IsVisible)
            return;
        foreach (var filter in filters)
        {
            var text = _explorer.FormatFilter(filter, _labels);
            var chip = new Button
            {
                Name = "ExplorerActive_" + active.Children.Count,
                Content = InteractiveIcons.Labeled(InteractiveIcons.Close, text, trailing: true),
                Classes = { "explorer-active-chip" }
            };
            AutomationProperties.SetName(chip, _labels.Format("removeFilter", text));
            chip.Click += (_, _) => RemoveExplorerFilter(filter);
            active.Children.Add(chip);
        }
        if (filters.Count > 1)
        {
            var clear = new Button { Name = "ExplorerClearAll", Content = _labels["clearFilters"], Classes = { "subtle", "explorer-link" } };
            clear.Click += (_, _) => ClearExplorerFilters(keepQuery: true);
            active.Children.Add(clear);
        }
    }

    private Control BuildExplorerCallout()
    {
        _explorerCalloutText = Text("", "interactive-detail");
        _explorerCalloutText.VerticalAlignment = VerticalAlignment.Center;
        var more = Button(_labels["findMore"]);
        more.Name = "Action_more";
        more.Click += async (_, _) => await SubmitAsync("more");
        _submitButtons.Add((more, "more", _labels["findMore"], InteractiveIcons.Search));
        _explorerCalloutClear = Button(_labels["clearFilters"], "subtle");
        _explorerCalloutClear.Name = "ExplorerCalloutClear";
        _explorerCalloutClear.Click += (_, _) => ClearExplorerFilters(keepQuery: false);
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        actions.Children.Add(more);
        actions.Children.Add(_explorerCalloutClear);
        var icon = InteractiveIcons.Create(InteractiveIcons.Explorer, 14);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, RowSpacing = 10 };
        layout.RowDefinitions = new RowDefinitions("Auto,Auto");
        Grid.SetColumn(_explorerCalloutText, 1);
        Grid.SetColumn(actions, 1);
        Grid.SetRow(actions, 1);
        layout.Children.Add(icon);
        layout.Children.Add(_explorerCalloutText);
        layout.Children.Add(actions);
        _explorerCallout = new Border
        {
            Name = "ExplorerMoreCallout", Child = layout, IsVisible = false,
            Classes = { "interactive-callout", "explorer-callout" }
        };
        return _explorerCallout;
    }

    /// <summary>When filters leave little to choose from, offer to research more like what is left.</summary>
    private void UpdateExplorerCallout(int matches)
    {
        if (_explorerCallout is null || _explorerCalloutText is null || _explorer is null)
            return;
        var total = _explorer.Definition.Items.Count;
        var filtered = _explorer.IsFiltered(_state);
        _explorerCallout.IsVisible = filtered && matches <= 2 && total > matches;
        _explorerCalloutText.Text = matches == 0 ? _labels["noMatches"] : _labels.Format("fewMatches", matches, total);
        if (_explorerCalloutClear is not null)
            _explorerCalloutClear.IsVisible = matches == 0;
    }
}
