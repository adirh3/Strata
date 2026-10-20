using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private void BuildChecklist(InteractiveChecklist checklist)
    {
        var rows = new StackPanel { Spacing = 2, Margin = new Thickness(-8, -4, -8, 0) };
        var checks = new List<CheckBox>();
        var track = new Ellipse { Width = 16, Height = 16, Classes = { "interactive-ring-track" } };
        var ring = new Arc { Width = 16, Height = 16, StartAngle = -90, Classes = { "interactive-ring" } };
        var progressText = new TextBlock();
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        progress.Children.Add(new Panel { Width = 16, Height = 16, Children = { track, ring } });
        progress.Children.Add(progressText);
        var pill = new Border { Child = progress, Classes = { "interactive-pill" } };
        SetMeta(pill);

        void Refresh()
        {
            var done = _state.CheckedIds.Count;
            var total = checklist.Items.Count;
            var complete = done == total;
            ring.SweepAngle = total == 0 ? 0 : Math.Min(359.99, 360.0 * done / total);
            ring.Classes.Set("done", complete);
            progressText.Text = complete ? _labels["allDone"] : _labels.Format("progressShort", done, total);
            AutomationProperties.SetName(pill, _labels.Format("progressShort", done, total));
            var current = checklist.Numbered ? checks.FirstOrDefault(check => check.IsChecked != true) : null;
            foreach (var check in checks)
                check.Classes.Set("current", check == current);
        }

        for (var index = 0; index < checklist.Items.Count; index++)
        {
            var item = checklist.Items[index];
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(Text(item.Label, "interactive-row-title"));
            if (item.Detail is { Length: > 0 })
                text.Children.Add(Text(item.Detail, "interactive-detail"));
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            content.Children.Add(text);
            if (item.Meta is { Length: > 0 })
            {
                var meta = Text(item.Meta, "interactive-meta");
                meta.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(meta, 1);
                content.Children.Add(meta);
            }
            var check = new CheckBox
            {
                Name = "Item_" + SafeName(item.Id),
                Content = content,
                IsChecked = _state.CheckedIds.Contains(item.Id),
                Tag = checklist.Numbered ? (index + 1).ToString(CultureInfo.CurrentCulture) : null,
                Classes = { "interactive-row" }
            };
            AutomationProperties.SetName(check, checklist.Numbered
                ? _labels.Format("stepLabel", index + 1, item.Label) : item.Label);
            check.IsCheckedChanged += (_, _) =>
            {
                _state.CheckedIds.Remove(item.Id);
                if (check.IsChecked == true) _state.CheckedIds.Add(item.Id);
                Refresh();
                CommitState();
            };
            checks.Add(check);
            rows.Children.Add(check);
        }
        _body!.Children.Add(rows);
        Refresh();
        AddSubmitAction(checklist.SubmitLabel ?? _labels[checklist.Numbered ? "nextStepHelp" : "remaining"],
            "remaining", "default");
    }

    private void BuildResults(InteractiveResults results)
    {
        TextBox? filter = null;
        if (results.Items.Count > 6)
        {
            var searchIcon = InteractiveIcons.Create(InteractiveIcons.Search, 14);
            searchIcon.Margin = new Thickness(12, 0, -4, 0);
            searchIcon.Classes.Add("interactive-adornment-icon");
            filter = new TextBox
            {
                Name = "ResultFilter", PlaceholderText = _labels["filter"], Classes = { "interactive-input" },
                InnerLeftContent = searchIcon
            };
            AutomationProperties.SetName(filter, _labels["filter"]);
            _body!.Children.Add(filter);
        }

        var list = new StackPanel { Spacing = 8 };
        var cards = new List<(InteractiveItem Item, CheckBox Card)>();
        foreach (var item in results.Items)
        {
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(Text(item.Label, "interactive-strong"));
            if (item.Meta is { Length: > 0 })
                content.Children.Add(Text(item.Meta, "interactive-hint"));
            if (item.Detail is { Length: > 0 })
            {
                var detail = Text(item.Detail, "interactive-detail");
                detail.Margin = new Thickness(0, 2, 0, 0);
                content.Children.Add(detail);
            }
            if (item.Url is { Length: > 0 })
            {
                var uri = new Uri(item.Url);
                content.Children.Add(new StrataMarkdown
                {
                    Markdown = $"[{uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase)} \u2197]({uri.AbsoluteUri})",
                    IsInline = true,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            var card = new CheckBox
            {
                Name = "Result_" + SafeName(item.Id),
                Content = content,
                IsChecked = _state.SelectedIds.Contains(item.Id),
                Classes = { "interactive-card" }
            };
            AutomationProperties.SetName(card, item.Label);
            card.IsCheckedChanged += (_, _) =>
            {
                _state.SelectedIds.Remove(item.Id);
                if (card.IsChecked == true) _state.SelectedIds.Add(item.Id);
                CommitState();
            };
            cards.Add((item, card));
            list.Children.Add(card);
        }
        _body!.Children.Add(list);

        var empty = Text(_labels["empty"], "interactive-hint");
        empty.IsVisible = false;
        _body.Children.Add(empty);
        if (filter is not null)
            filter.TextChanged += (_, _) =>
            {
                var query = filter.Text?.Trim() ?? "";
                foreach (var entry in cards)
                    entry.Card.IsVisible = query.Length == 0
                        || string.Join(" ", entry.Item.Label, entry.Item.Meta, entry.Item.Detail)
                            .Contains(query, StringComparison.OrdinalIgnoreCase);
                empty.IsVisible = cards.All(entry => !entry.Card.IsVisible);
            };

        _statusText = () => _state.SelectedIds.Count > 0 ? _labels.Format("selected", _state.SelectedIds.Count) : null;
        AddSubmitAction(results.SubmitLabel ?? _labels[IsInputRequest ? "continue" : "use"], "use");
        if (!IsInputRequest)
            AddSubmitAction(_labels["compare"], "compare", "default", InteractiveIcons.Compare);
        var clear = AddLocalAction(_labels["clear"], () =>
        {
            _state.SelectedIds.Clear();
            CommitState();
            Rebuild();
        }, "subtle", "ClearSelection");
        _refreshActions = () => clear.IsVisible = _state.SelectedIds.Count > 0;
    }
}
