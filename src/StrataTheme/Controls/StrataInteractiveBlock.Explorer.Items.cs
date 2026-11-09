using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using StrataTheme.Animation;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private sealed record ExplorerRow(Button Row, Dictionary<string, TextBlock> Cells);

    private readonly Dictionary<string, Button> _explorerCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExplorerRow> _explorerRows = new(StringComparer.Ordinal);
    private string? _explorerRowsSignature;
    private string? _explorerTableSort;
    private Grid? _explorerGrid;
    private StackPanel? _explorerTable;
    private Border? _explorerTableFrame;

    /// <summary>Applies the saved filters and order to the cards or table and everything that counts them.</summary>
    private void RefreshExplorerResults(bool animate = true)
    {
        if (_explorer is not { } model || _explorerResults is not { } results)
            return;
        var focused = FocusedExplorerControl();
        var items = model.Apply(_state);
        UpdateExplorerCount(items.Count);
        if (model.View(_state) == "table")
            ShowExplorerTable(results, items);
        else
            ShowExplorerCards(results, items, animate);
        UpdateExplorerCallout(items.Count);
        UpdateExplorerChips();
        UpdateExplorerTools();
        // A new order rebuilds the table's header and arriving pictures rebuild the cards.
        RestoreExplorerFocus(focused, null);
    }

    /// <summary>The header pill: every option, or how many of them pass the filters.</summary>
    private void UpdateExplorerCount(int matches)
    {
        if (_explorerCount is null || _explorer is null)
            return;
        var total = _explorer.Definition.Items.Count;
        _explorerCount.Text = matches == total
            ? _labels.Format("optionCount", total) : _labels.Format("matchCount", matches, total);
    }

    private void ShowExplorerCards(Border results, IReadOnlyList<InteractiveExplorerItem> items, bool animate)
    {
        var grid = _explorerGrid ??= new Grid { ColumnSpacing = 12, RowSpacing = 12, Name = "ExplorerCards" };
        var shown = grid.Children.Where(card => card.IsVisible).Select(card => card.Tag as string).ToHashSet();
        // Built cards stay in the grid and filtering only shows, hides and reorders them: putting a card back
        // into the tree would restyle everything in it, on every keystroke of a search.
        var cards = items.Select(ExplorerCard).ToList();
        foreach (var card in cards)
        {
            if (card.Parent is null)
                grid.Children.Add(card);
        }
        var visible = cards.ToHashSet<Control>();
        foreach (var child in grid.Children)
            child.IsVisible = visible.Contains(child);
        for (var index = 0; index < cards.Count; index++)
        {
            var at = grid.Children.IndexOf(cards[index]);
            if (at != index)
                grid.Children.Move(at, index);
        }
        ArrangeExplorerCards();
        var switched = !ReferenceEquals(results.Child, grid);
        results.Child = grid;
        if (!animate)
            return;
        var delay = 0;
        foreach (var card in cards)
        {
            if (!switched && shown.Contains(card.Tag as string))
                continue;
            SlideFadeEntrance.PlayComposited(card, 8, TimeSpan.FromMilliseconds(220), TimeSpan.FromMilliseconds(Math.Min(delay, 180)));
            delay += 24;
        }
    }

    /// <summary>Places the shown cards in reading order, a row at a time.</summary>
    private void ArrangeExplorerCards()
    {
        if (_explorerGrid is not { } grid)
            return;
        var columns = Math.Max(1, _explorerColumns);
        if (grid.ColumnDefinitions.Count != columns)
        {
            grid.ColumnDefinitions.Clear();
            for (var column = 0; column < columns; column++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        var shown = grid.Children.Where(card => card.IsVisible).ToList();
        var rows = (shown.Count + columns - 1) / columns;
        while (grid.RowDefinitions.Count > rows)
            grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        while (grid.RowDefinitions.Count < rows)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var index = 0; index < shown.Count; index++)
        {
            Grid.SetRow(shown[index], index / columns);
            Grid.SetColumn(shown[index], index % columns);
        }
    }

    private Button ExplorerCard(InteractiveExplorerItem item)
    {
        if (_explorerCards.TryGetValue(item.Id, out var cached))
            return cached;
        var model = _explorer!;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var toggle = ExplorerToggle(item, "card");
        var info = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8, Margin = new Thickness(14, 12, 14, 14) };
        var heading = new StackPanel { Spacing = 3 };
        if (ExplorerShowsMedia)
        {
            var media = new Panel();
            media.Children.Add(ExplorerMedia(item, 132, new CornerRadius(9, 9, 0, 0), 40));
            if (!string.IsNullOrWhiteSpace(item.Badge))
            {
                var badge = ExplorerBadge(item.Badge!);
                badge.Margin = new Thickness(10);
                badge.HorizontalAlignment = HorizontalAlignment.Left;
                badge.VerticalAlignment = VerticalAlignment.Top;
                media.Children.Add(badge);
            }
            toggle.Margin = new Thickness(8);
            toggle.HorizontalAlignment = HorizontalAlignment.Right;
            toggle.VerticalAlignment = VerticalAlignment.Top;
            media.Children.Add(toggle);
            layout.Children.Add(media);
            heading.Children.Add(CardTitle(item.Label));
        }
        else
        {
            // Without pictures the compare pip shares the title's row; it overhangs the row so a
            // one-line title keeps its subtitle close.
            var top = new Panel();
            var titles = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 36, 0) };
            if (!string.IsNullOrWhiteSpace(item.Badge))
            {
                var badge = ExplorerBadge(item.Badge!);
                badge.HorizontalAlignment = HorizontalAlignment.Left;
                titles.Children.Add(badge);
            }
            titles.Children.Add(CardTitle(item.Label));
            toggle.HorizontalAlignment = HorizontalAlignment.Right;
            toggle.VerticalAlignment = VerticalAlignment.Top;
            toggle.Margin = new Thickness(0, -5, -4, -12);
            top.Children.Add(titles);
            top.Children.Add(toggle);
            heading.Children.Add(top);
        }
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
        {
            var subtitle = Text(item.Subtitle, "explorer-subtitle");
            subtitle.MaxLines = 2;
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            heading.Children.Add(subtitle);
        }
        info.Children.Add(heading);
        var footer = new StackPanel { Spacing = 8 };
        if (ExplorerFigures(item, hero: false) is { } figures)
            footer.Children.Add(figures);
        if (ExplorerSpecs(item) is { } specs)
            footer.Children.Add(specs);
        Grid.SetRow(footer, 2);
        info.Children.Add(footer);
        Grid.SetRow(info, 1);
        layout.Children.Add(info);

        var card = new Button
        {
            Name = "ExplorerCard_" + SafeName(item.Id), Tag = item.Id, Content = layout,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Classes = { "explorer-card" }
        };
        card.Classes.Set("selected", _state.SelectedIds.Contains(item.Id));
        AutomationProperties.SetName(card, item.Label);
        AutomationProperties.SetHelpText(card, _labels["openOption"]);
        card.Click += (_, args) =>
        {
            if (ReferenceEquals(args.Source, card))
                OpenExplorerItem(item.Id);
        };
        AttachToggleHost(toggle, card);
        _explorerCards[item.Id] = card;
        return card;
    }

    private void AttachToggleHost(ToggleButton toggle, Control host)
    {
        var index = _explorerToggles.FindIndex(entry => ReferenceEquals(entry.Toggle, toggle));
        if (index >= 0)
            _explorerToggles[index] = _explorerToggles[index] with { Host = host };
    }

    private static TextBlock CardTitle(string label)
    {
        var title = Text(label, "explorer-card-title");
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        return title;
    }

    // ── Table ──

    private void ShowExplorerTable(Border results, IReadOnlyList<InteractiveExplorerItem> items)
    {
        var model = _explorer!;
        var columns = model.Attributes.Take(Math.Max(1, _explorerTableColumns)).ToList();
        var signature = string.Join("|", columns.Select(column => column.Id));
        var table = _explorerTable ??= new StackPanel { Name = "ExplorerTable" };
        Grid.SetIsSharedSizeScope(table, true);
        if (signature != _explorerRowsSignature)
        {
            _explorerRows.Clear();
            _explorerToggles.RemoveAll(entry => entry.Owner == "row");
            _explorerRowsSignature = signature;
            table.Children.Clear();
        }
        // Like cards, built rows stay put and filtering only shows, hides and reorders them. The header shows the
        // order, so it is rebuilt when the order changes.
        var sort = model.Sort(_state)?.Key ?? "";
        if (table.Children.Count == 0)
            table.Children.Add(ExplorerTableHeader(columns));
        else if (sort != _explorerTableSort)
            table.Children[0] = ExplorerTableHeader(columns);
        _explorerTableSort = sort;
        var rows = items.Select(item => ExplorerTableRow(item, columns)).ToList();
        foreach (var row in rows)
        {
            if (row.Row.Parent is null)
                table.Children.Add(row.Row);
        }
        var visible = rows.Select(row => row.Row).ToHashSet<Control>();
        for (var index = 1; index < table.Children.Count; index++)
            table.Children[index].IsVisible = visible.Contains(table.Children[index]);
        for (var index = 0; index < rows.Count; index++)
        {
            var at = table.Children.IndexOf(rows[index].Row);
            if (at != index + 1)
                table.Children.Move(at, index + 1);
        }
        var best = columns.ToDictionary(column => column.Id, column => model.Best(column, items));
        for (var index = 0; index < rows.Count; index++)
        {
            foreach (var (id, cell) in rows[index].Cells)
                cell.Classes.Set("best", best[id].Contains(items[index].Id));
        }
        results.Child = _explorerTableFrame ??= new Border { Classes = { "explorer-table" }, Child = table };
    }

    private Grid ExplorerTableGrid(IReadOnlyList<ExplorerAttribute> columns)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        if (ExplorerShowsMedia)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "ExplorerThumb" });
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 120 });
        for (var index = 0; index < columns.Count; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "ExplorerColumn" + index, MinWidth = 56 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "ExplorerToggle" });
        return grid;
    }

    private Control ExplorerTableHeader(IReadOnlyList<ExplorerAttribute> columns)
    {
        var grid = ExplorerTableGrid(columns);
        var sort = _explorer!.Sort(_state);
        var offset = ExplorerShowsMedia ? 1 : 0;
        Button Header(string id, string label, bool numeric)
        {
            var sorted = sort is { } order && order.AttributeId == id;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            content.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140 });
            if (sorted)
                content.Children.Add(InteractiveIcons.Create(sort!.Value.Descending ? InteractiveIcons.ArrowDown : InteractiveIcons.ArrowUp, 11));
            var header = new Button
            {
                Name = "ExplorerHeader_" + (id.Length == 0 ? "name" : SafeName(id)), Content = content,
                HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Classes = { "explorer-header" }
            };
            header.Classes.Set("sorted", sorted);
            AutomationProperties.SetName(header, _labels.Format("sortByColumn", label));
            header.Click += (_, _) => CycleExplorerSort(id);
            return header;
        }
        // The name heading also spans the thumbnail column.
        var name = Header("", _labels["option"], numeric: false);
        Grid.SetColumnSpan(name, offset + 1);
        grid.Children.Add(name);
        for (var index = 0; index < columns.Count; index++)
        {
            var header = Header(columns[index].Id, columns[index].Label, columns[index].IsNumeric);
            Grid.SetColumn(header, offset + 1 + index);
            grid.Children.Add(header);
        }
        return new Border { Classes = { "explorer-table-head" }, Child = grid };
    }

    private ExplorerRow ExplorerTableRow(InteractiveExplorerItem item, IReadOnlyList<ExplorerAttribute> columns)
    {
        if (_explorerRows.TryGetValue(item.Id, out var cached))
            return cached;
        var model = _explorer!;
        var grid = ExplorerTableGrid(columns);
        var column = 0;
        if (ExplorerShowsMedia)
        {
            var thumb = ExplorerMedia(item, 40, new CornerRadius(8), 15);
            thumb.Width = 40;
            thumb.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(thumb);
            column++;
        }
        var name = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(item.Label, "explorer-row-title");
        title.MaxLines = 1;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Children.Add(title);
        // Each piece is offered only the room left, so a long subtitle trims instead of running into the values.
        var second = new DockPanel { HorizontalSpacing = 6 };
        if (!string.IsNullOrWhiteSpace(item.Badge))
        {
            var badge = ExplorerBadge(item.Badge!, compact: true);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            badge.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(badge, Dock.Left);
            second.Children.Add(badge);
        }
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
        {
            var subtitle = Text(item.Subtitle, "explorer-subtitle");
            subtitle.TextWrapping = TextWrapping.NoWrap;
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            subtitle.VerticalAlignment = VerticalAlignment.Center;
            second.Children.Add(subtitle);
        }
        if (second.Children.Count > 0)
            name.Children.Add(second);
        Grid.SetColumn(name, column++);
        grid.Children.Add(name);

        var cells = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        foreach (var attribute in columns)
        {
            var value = model.Value(item, attribute);
            var (cell, text) = ExplorerValueView(value, attribute, compact: true);
            cell.HorizontalAlignment = attribute.IsNumeric ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            cell.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cell, column++);
            grid.Children.Add(cell);
            if (text is not null && attribute.IsNumeric)
                cells[attribute.Id] = text;
        }
        var toggle = ExplorerToggle(item, "row");
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, column);
        grid.Children.Add(toggle);

        var row = new Button
        {
            Name = "ExplorerRow_" + SafeName(item.Id), Tag = item.Id, Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Classes = { "explorer-row" }
        };
        row.Classes.Set("selected", _state.SelectedIds.Contains(item.Id));
        AutomationProperties.SetName(row, item.Label);
        AutomationProperties.SetHelpText(row, _labels["openOption"]);
        row.Click += (_, args) =>
        {
            if (ReferenceEquals(args.Source, row))
                OpenExplorerItem(item.Id);
        };
        AttachToggleHost(toggle, row);
        var entry = new ExplorerRow(row, cells);
        _explorerRows[item.Id] = entry;
        return entry;
    }

    // ── Pieces ──

    /// <summary>A value as a small view: a check for features, a star for ratings, text otherwise.</summary>
    private (Control View, TextBlock? Text) ExplorerValueView(ExplorerValue value, ExplorerAttribute attribute, bool compact)
    {
        if (value.IsEmpty)
            return (Text("\u2014", "explorer-muted"), null);
        if (value.Flag is { } flag)
        {
            var icon = InteractiveIcons.Create(flag ? InteractiveIcons.Check : InteractiveIcons.Minus, 14);
            icon.Classes.Add(flag ? "explorer-yes" : "explorer-no");
            AutomationProperties.SetName(icon, _labels[flag ? "yes" : "no"]);
            if (compact)
                return (icon, null);
            var labeled = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            labeled.Children.Add(icon);
            labeled.Children.Add(Text(_labels[flag ? "yes" : "no"], "explorer-value"));
            return (labeled, null);
        }
        var text = Text(_explorer!.Format(value, attribute, _labels), compact ? "explorer-cell" : "explorer-value");
        text.Classes.Set("numeric", value.Number is not null);
        if (compact)
        {
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.MaxWidth = 150;
        }
        if (attribute.Type != ExplorerAttributeType.Rating || value.Number is null)
            return (text, text);
        var rating = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (attribute.Scale == 5)
        {
            var star = InteractiveIcons.Create(InteractiveIcons.Star, 12);
            star.Classes.Add("explorer-star");
            rating.Children.Add(star);
            rating.Children.Add(text);
        }
        else
        {
            // A score out of 10 or 100 reads as the score, with its scale kept quiet beside it.
            var scale = new TextBlock
            {
                Text = "/" + attribute.Scale.ToString("0", CultureInfo.CurrentCulture),
                VerticalAlignment = VerticalAlignment.Center, Classes = { "explorer-scale" }
            };
            rating.Spacing = 1;
            rating.Children.Add(text);
            rating.Children.Add(scale);
        }
        return (rating, text);
    }

    /// <summary>The headline figure and rating a card or detail page leads with.</summary>
    private Control? ExplorerFigures(InteractiveExplorerItem item, bool hero)
    {
        var model = _explorer!;
        var row = new WrapPanel { ItemSpacing = 12, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (model.Headline is { } headline && model.Value(item, headline) is { IsEmpty: false } figure)
        {
            var text = Text(model.Format(figure, headline, _labels), hero ? "explorer-hero" : "explorer-figure");
            text.TextWrapping = TextWrapping.NoWrap;
            text.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(text, $"{headline.Label}: {text.Text}");
            row.Children.Add(text);
        }
        if (model.Rating is { } rating && model.Value(item, rating) is { Number: not null } score)
        {
            var (view, _) = ExplorerValueView(score, rating, compact: false);
            view.VerticalAlignment = VerticalAlignment.Center;
            view.Classes.Add("explorer-rating");
            AutomationProperties.SetName(view, $"{rating.Label}: {InteractiveExplorerModel.FormatRating(rating, score.Number!.Value)}");
            row.Children.Add(view);
        }
        return row.Children.Count > 0 ? row : null;
    }

    /// <summary>Up to three short facts beyond the headline: features it has, short values with units.</summary>
    private Control? ExplorerSpecs(InteractiveExplorerItem item)
    {
        var model = _explorer!;
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var attribute in model.Attributes)
        {
            if (chips.Children.Count == 3)
                break;
            if (attribute == model.Headline || attribute == model.Rating)
                continue;
            var value = model.Value(item, attribute);
            string? text = value switch
            {
                { Flag: true } => attribute.Label,
                { Number: { } number } when attribute.Unit is not null && attribute.Type != ExplorerAttributeType.Money =>
                    InteractiveExplorerModel.FormatNumber(attribute, number),
                { Number: { } number } => $"{attribute.Label}: {InteractiveExplorerModel.FormatNumber(attribute, number)}",
                { Text: { Length: <= 26 } shortText } => shortText,
                _ => null
            };
            if (text is null || text.Length > 34)
                continue;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (value.Flag == true)
                content.Children.Add(InteractiveIcons.Create(InteractiveIcons.Check, 10));
            content.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis });
            var chip = new Border { Classes = { "explorer-spec" }, Child = content };
            AutomationProperties.SetName(chip, value.Flag == true ? text : $"{attribute.Label}: {model.Format(value, attribute, _labels)}");
            chips.Children.Add(chip);
        }
        return chips.Children.Count > 0 ? chips : null;
    }

    private static Border ExplorerBadge(string text, bool compact = false)
    {
        var badge = new Border
        {
            Classes = { "explorer-badge" },
            Child = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 }
        };
        badge.Classes.Set("compact", compact);
        return badge;
    }

    private ToggleButton ExplorerToggle(InteractiveExplorerItem item, string owner, bool wide = false)
    {
        var selected = _state.SelectedIds.Contains(item.Id);
        var toggle = new ToggleButton
        {
            Name = (wide ? "ExplorerDetailCompare_" : owner == "row" ? "ExplorerRowCompare_" : owner == "view" ? "ExplorerViewCompare_" : "ExplorerCompare_")
                + SafeName(item.Id),
            IsChecked = selected,
            Classes = { wide ? "explorer-compare-wide" : "explorer-compare" }
        };
        if (wide)
            SetWideToggleContent(toggle, selected);
        else
        {
            var add = InteractiveIcons.Create(InteractiveIcons.Add, 14);
            add.Classes.Add("explorer-compare-add");
            var check = InteractiveIcons.Create(InteractiveIcons.Check, 14);
            check.Classes.Add("explorer-compare-check");
            toggle.Content = new Panel { Children = { add, check } };
        }
        AutomationProperties.SetName(toggle, _labels.Format("compareItem", item.Label));
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (!_explorerSyncing)
                SetExplorerSelected(item.Id, toggle.IsChecked == true);
        };
        // Click bubbles: picking an option to compare must not also open the card or row it sits on.
        toggle.Click += (_, args) => args.Handled = true;
        _explorerToggles.Add(new ExplorerToggleEntry(item.Id, toggle, null, owner));
        return toggle;
    }

    private void SetWideToggleContent(ToggleButton toggle, bool selected) =>
        toggle.Content = InteractiveIcons.Labeled(selected ? InteractiveIcons.Check : InteractiveIcons.Add,
            _labels[selected ? "inComparison" : "addToCompare"]);

    // ── Media ──

    /// <summary>
    /// An option's picture, cropped to fill. Until it loads, or when it cannot, a soft tinted tile
    /// with the option's initials stands in, so a grid never shows broken images.
    /// </summary>
    private Border ExplorerMedia(InteractiveExplorerItem item, double height, CornerRadius corners, double glyph)
    {
        var tone = "tone" + Tone(item.Id);
        var layers = new Panel();
        layers.Children.Add(new Border { Classes = { "explorer-tint", tone } });
        layers.Children.Add(new TextBlock
        {
            Text = Monogram(item.Label), FontSize = glyph,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Classes = { "explorer-monogram", tone }
        });
        if (HasExplorerPicture(item))
        {
            var image = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, Classes = { "explorer-image" } };
            AutomationProperties.SetName(image, item.Label);
            layers.Children.Add(image);
            _ = ShowExplorerPictureAsync(item, image);
        }
        return new Border
        {
            Height = height, CornerRadius = corners, ClipToBounds = true, Child = layers,
            Classes = { "explorer-media" }
        };
    }

    private static bool HasExplorerPicture(InteractiveExplorerItem item) =>
        !string.IsNullOrWhiteSpace(item.Image) || ShowsLinkPreviews && WebAddress(item.Url) is not null;

    private async Task ShowExplorerPictureAsync(InteractiveExplorerItem item, Image image)
    {
        if (await ExplorerPicture(item) is not { } bitmap)
            return;
        image.Source = bitmap;
        image.Opacity = 1;
    }

    private Task<Bitmap?> ExplorerPicture(InteractiveExplorerItem item)
    {
        var url = ShowsLinkPreviews ? item.Url : null;
        var key = item.Image + "\n" + url;
        if (!_explorerImages.TryGetValue(key, out var load))
            _explorerImages[key] = load = ExplorerPictureLoader is { } loader
                ? LoadHostPictureAsync(loader, item)
                : LoadExplorerPictureAsync(item.Image, url);
        return load;
    }

    /// <summary>A picture the host found for the option, decoded as any picture is.</summary>
    private async Task<Bitmap?> LoadHostPictureAsync(
        Func<StrataInteractiveBlock, InteractiveExplorerItem, CancellationToken, Task<byte[]?>> loader,
        InteractiveExplorerItem item)
    {
        try
        {
            if (await loader(this, item, CancellationToken.None).ConfigureAwait(false) is not { Length: > 0 } bytes)
                return null;
            var picture = await Task.Run(() => StrataMarkdown.DecodeMarkdownImage(bytes, ExplorerPictureBounds))
                .ConfigureAwait(false);
            // Without a picture of its own, the option showed its link's preview.
            if (string.IsNullOrWhiteSpace(item.Image))
                RememberLinkPicture(item.Url);
            return picture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void RememberLinkPicture(string? url)
    {
        if (WebAddress(url) is not { } page)
            return;
        if (LoadedLinkPictures.Count >= MaxLoadedLinkPictures)
            LoadedLinkPictures.Clear();
        LoadedLinkPictures.TryAdd(page.AbsoluteUri, 0);
    }

    /// <summary>A link whose preview was already found in this run, here or by the host's loader.</summary>
    private static bool HasKnownLinkPicture(string? url) =>
        StrataMarkdown.HasKnownLinkPreviewImage(url)
        || WebAddress(url) is { } page && LoadedLinkPictures.ContainsKey(page.AbsoluteUri);

    /// <summary>
    /// The model's own picture wins; the link's preview stands in when it gives none, or when its
    /// picture fails to load.
    /// </summary>
    private static async Task<Bitmap?> LoadExplorerPictureAsync(string? image, string? url)
    {
        if (!string.IsNullOrWhiteSpace(image) && await LoadExplorerImageAsync(image).ConfigureAwait(false) is { } chosen)
            return chosen;
        try
        {
            return await StrataMarkdown.GetLinkPreviewImageAsync(url).ConfigureAwait(false) is { } preview
                ? await LoadExplorerImageAsync(preview).ConfigureAwait(false)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<Bitmap?> LoadExplorerImageAsync(string source)
    {
        try
        {
            return await StrataMarkdown.LoadMarkdownImageAsync(source, CancellationToken.None, ExplorerPictureBounds)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Pictures decode no larger than the biggest place a view shows them.
    private static readonly PixelSize ExplorerPictureBounds = new(480, 360);

    internal static string Monogram(string label)
    {
        var words = label.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "?";
        // Short brand words such as "LG" or "HP" read best whole.
        if (words[0].Length <= 3 && words[0].All(char.IsLetter) && words[0].All(char.IsUpper))
            return words[0];
        var first = StringInfo.GetNextTextElement(words[0]);
        var second = words.Length > 1 && char.IsLetterOrDigit(words[1][0]) ? StringInfo.GetNextTextElement(words[1]) : "";
        return (first + second).ToUpper(CultureInfo.CurrentCulture);
    }

    internal static int Tone(string id)
    {
        uint hash = 17;
        foreach (var character in id)
            hash = hash * 31 + character;
        return (int)(hash % 6) + 1;
    }
}
