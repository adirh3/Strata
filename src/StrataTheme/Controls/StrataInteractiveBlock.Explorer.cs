using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Animation;

namespace StrataTheme.Controls;

/// <summary>The option explorer: browse, open, filter and compare researched options.</summary>
public partial class StrataInteractiveBlock
{
    private enum ExplorerMode { Browse, Detail, Compare }

    private sealed record ExplorerToggleEntry(string Id, ToggleButton Toggle, Control? Host, string Owner);

    /// <summary>
    /// Whether an option without its own picture shows its link's preview: the picture its page
    /// shows when shared (its Open Graph or Twitter image), read from the page as messaging apps do.
    /// Off by default, so each host decides whether showing an answer may visit the pages it links.
    /// </summary>
    public static bool ShowsLinkPreviews { get; set; }

    /// <summary>
    /// Loads options' pictures in place of the control when set. A host whose views should not reach
    /// the web themselves (a phone showing a computer's answers) has the computer find each picture:
    /// the option's own image, else its link's preview when <see cref="ShowsLinkPreviews"/> is on. It
    /// returns the picture's bytes, which the control decodes as it decodes any picture, or null
    /// when the option has none. It is called on the UI thread, once per option a view shows.
    /// </summary>
    public static Func<StrataInteractiveBlock, InteractiveExplorerItem, CancellationToken, Task<byte[]?>>? ExplorerPictureLoader { get; set; }

    // Links whose previews a host's loader brought this run, so their views make room at once.
    private static readonly ConcurrentDictionary<string, byte> LoadedLinkPictures = new(StringComparer.Ordinal);
    private const int MaxLoadedLinkPictures = 512;

    // Unsaved presentation that survives a rebuild of the same definition.
    private InteractiveExplorer? _explorerSource;
    private ExplorerMode _explorerMode;
    private string? _explorerFocus;
    private bool _explorerFiltersOpen;
    private bool _explorerDifferencesOnly;
    private string? _explorerNotice;
    // A link preview arrived, so the options make room for pictures.
    private bool _explorerMediaFound;
    private readonly Dictionary<string, System.Threading.Tasks.Task<Avalonia.Media.Imaging.Bitmap?>> _explorerImages =
        new(StringComparer.Ordinal);

    // Built for the current definition, language and touch mode.
    private InteractiveExplorerModel? _explorer;
    private Panel? _explorerStage;
    private Control? _explorerBrowse;
    private TextBlock? _explorerCount;
    private readonly List<ExplorerToggleEntry> _explorerToggles = [];
    private bool _explorerSyncing;
    private DispatcherTimer? _explorerCommitTimer;
    private double _explorerWidth = 720;
    private double _explorerBuiltWidth;
    private bool _explorerNarrow;
    private bool _explorerStacked;
    private int _explorerColumns = 3;
    private int _explorerTableColumns = 4;

    private void BuildExplorer(InteractiveExplorer explorer)
    {
        if (!ReferenceEquals(_explorerSource, explorer))
        {
            _explorerSource = explorer;
            _explorerMode = ExplorerMode.Browse;
            _explorerFocus = null;
            _explorerFiltersOpen = false;
            _explorerDifferencesOnly = false;
            _explorerMediaFound = false;
        }
        _explorer = InteractiveExplorerModel.For(explorer);
        // Previews already found in this run get their room at once.
        if (!_explorer.HasMedia && !_explorerMediaFound && ShowsLinkPreviews)
            _explorerMediaFound = explorer.Items.Any(item => HasKnownLinkPicture(item.Url));
        _explorerToggles.Clear();
        _explorerCards.Clear();
        _explorerRows.Clear();
        _explorerRowsSignature = null;
        _explorerGrid = null;
        _explorerTable = null;
        _explorerTableFrame = null;
        _explorerBrowse = null;
        _explorerStage = new Panel { Name = "ExplorerStage" };
        _body!.Children.Add(_explorerStage);

        _explorerCount = new TextBlock();
        var pill = new Border { Child = _explorerCount, Classes = { "interactive-pill" } };
        SetMeta(pill);
        UpdateExplorerCount(_explorer.Apply(_state).Count);

        var decide = AddSubmitAction(_labels["decide"], "compare", "accent", InteractiveIcons.Compare);
        var use = AddSubmitAction(explorer.SubmitLabel ?? _labels["explorerUse"], "use");
        var compare = AddLocalAction(_labels["compare"], () => ShowExplorer(ExplorerMode.Compare), "default",
            "ExplorerCompare", InteractiveIcons.Compare);
        var clear = AddLocalAction(_labels["clear"], ClearExplorerSelection, "subtle", "ClearSelection");
        _statusText = ExplorerStatus;
        _refreshActions = () =>
        {
            var count = _state.SelectedIds.Count;
            var comparing = _explorerMode == ExplorerMode.Compare;
            decide.IsVisible &= comparing;
            use.Classes.Set("accent", !comparing);
            compare.IsVisible = !comparing && count > 0;
            compare.IsEnabled = count >= 2;
            var label = count >= 2 ? _labels.Format("compareCount", count) : _labels["compare"];
            compare.Content = InteractiveIcons.Labeled(InteractiveIcons.Compare, label);
            AutomationProperties.SetName(compare, label);
            clear.IsVisible = !comparing && count > 0;
        };
        _layoutForWidth = LayoutExplorer;
        LayoutExplorer(Bounds.Width > 0 ? Bounds.Width : _explorerWidth);
        ShowExplorer(_explorerMode, animate: false);
        if (!ExplorerShowsMedia && ShowsLinkPreviews)
            _ = WatchExplorerPicturesAsync(explorer);
    }

    /// <summary>Cards, rows, details and comparisons have room for pictures.</summary>
    private bool ExplorerShowsMedia => _explorer is { } model && (model.HasMedia || _explorerMediaFound);

    /// <summary>
    /// Makes room for pictures once the first link preview arrives, so options whose links have no
    /// preview keep their compact layout rather than a row of placeholder tiles.
    /// </summary>
    private async System.Threading.Tasks.Task WatchExplorerPicturesAsync(InteractiveExplorer explorer)
    {
        var pending = explorer.Items.Where(HasExplorerPicture).Select(ExplorerPicture).ToList();
        while (pending.Count > 0)
        {
            var loaded = await System.Threading.Tasks.Task.WhenAny(pending);
            pending.Remove(loaded);
            if (!loaded.IsCompletedSuccessfully || loaded.Result is null)
                continue;
            if (ReferenceEquals(_explorerSource, explorer) && !_explorerMediaFound)
            {
                _explorerMediaFound = true;
                ShowExplorerMedia();
            }
            return;
        }
    }

    private void ShowExplorerMedia()
    {
        if (_explorer is null || _explorerStage is null)
            return;
        _explorerCards.Clear();
        _explorerRows.Clear();
        _explorerRowsSignature = null;
        _explorerToggles.RemoveAll(entry => entry.Owner is "card" or "row");
        _explorerGrid?.Children.Clear();
        // The thumbnail column narrows the table.
        LayoutExplorer(_explorerWidth);
        // Browsing only rebuilds its results, so a search being typed keeps its focus.
        if (_explorerMode == ExplorerMode.Browse)
            RefreshExplorerResults(animate: false);
        else
            ShowExplorer(_explorerMode, animate: false);
    }

    private InteractiveExplorerItem? FindExplorerItem(string? id) =>
        id is null ? null : _explorer?.Definition.Items.FirstOrDefault(item => item.Id == id);

    private void ShowExplorer(ExplorerMode mode, bool animate = true)
    {
        if (_explorer is null || _explorerStage is null)
            return;
        CommitTypedEdit();
        if (mode == ExplorerMode.Compare && _state.SelectedIds.Count < 2
            || mode == ExplorerMode.Detail && FindExplorerItem(_explorerFocus) is null)
            mode = ExplorerMode.Browse;
        var previous = _explorerMode;
        var focused = FocusedExplorerControl();
        var hadFocus = _explorerStage.IsKeyboardFocusWithin;
        _explorerMode = mode;
        _explorerToggles.RemoveAll(entry => entry.Owner == "view");
        _explorerStage.Children.Clear();
        _explorerBuiltWidth = _explorerWidth;

        Control content;
        Control? landmark;
        switch (mode)
        {
            case ExplorerMode.Detail:
                content = BuildExplorerDetail(FindExplorerItem(_explorerFocus)!, out landmark);
                break;
            case ExplorerMode.Compare:
                content = BuildExplorerCompare(animate, out landmark);
                break;
            default:
                content = _explorerBrowse ??= BuildExplorerBrowse();
                RefreshExplorerResults(animate: false);
                // Back from an option's details, return to that option in whichever view is showing.
                landmark = previous == ExplorerMode.Detail && _explorerFocus is { } focus
                    ? _explorer.View(_state) == "table"
                        ? _explorerRows.GetValueOrDefault(focus)?.Row
                        : _explorerCards.GetValueOrDefault(focus)
                    : null;
                break;
        }
        _explorerStage.Children.Add(content);
        UpdateActions();
        if (animate)
        {
            SlideFadeEntrance.PlayComposited(content, 10, TimeSpan.FromMilliseconds(240));
            // A long grid may have scrolled the view away; keep the new view's start (or the option the
            // reader came back to) on screen, clear of a host's floating header.
            if (landmark is not null)
                Dispatcher.UIThread.Post(() =>
                {
                    if (landmark.IsAttachedToVisualTree())
                        landmark.BringIntoView(new Rect(0, -LandmarkClearance, landmark.Bounds.Width,
                            landmark.Bounds.Height + LandmarkClearance));
                }, DispatcherPriority.Loaded);
        }
        // Keyboard focus stays in the explorer: on the same control when a view is rebuilt in place, on a
        // new page so its arrow keys and Escape work, or back on the option the reader returns to.
        if (mode == previous)
            RestoreExplorerFocus(focused, hadFocus && mode != ExplorerMode.Browse ? content : null);
        else if (mode != ExplorerMode.Browse)
        {
            if (animate || hadFocus)
                RestoreExplorerFocus(null, content);
        }
        else if (hadFocus)
            RestoreExplorerFocus(null, landmark ?? FirstExplorerResult());
    }

    /// <summary>The named explorer control with keyboard focus, which a rebuild may replace.</summary>
    private Control? FocusedExplorerControl() =>
        _explorerStage is { IsKeyboardFocusWithin: true }
        && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control { Name.Length: > 0 } control
            ? control : null;

    /// <summary>Gives focus back after a rebuild: to the control that had it, its namesake, or the fallback.</summary>
    private void RestoreExplorerFocus(Control? focused, Control? fallback)
    {
        if (focused is null && fallback is null)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (focused is { IsFocused: true })
                return;
            var target = focused is null ? null : _explorerStage?.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => control.Name == focused.Name && control.Focusable && control.IsEffectivelyVisible);
            target ??= fallback;
            if (target is not null && target.IsAttachedToVisualTree())
                target.Focus();
        }, DispatcherPriority.Loaded);
    }

    private Control? FirstExplorerResult() => _explorer?.View(_state) == "table"
        ? _explorerTable?.Children.Skip(1).FirstOrDefault(row => row.IsVisible)
        : _explorerGrid?.Children.FirstOrDefault(card => card.IsVisible);

    private const double LandmarkClearance = 96;

    private void OpenExplorerItem(string id)
    {
        _explorerFocus = id;
        ShowExplorer(ExplorerMode.Detail);
    }

    private string? ExplorerStatus()
    {
        if (_explorerNotice is { } notice)
            return notice;
        if (_explorerMode == ExplorerMode.Compare || _explorer is null)
            return null;
        var count = _state.SelectedIds.Count;
        return count > 0 ? _labels.Format("selected", count)
            : _explorer.Definition.Items.Count >= 2 ? _labels.Format("compareHint", InteractiveExplorerModel.MaxSelected) : null;
    }

    private void ShowExplorerNotice(string notice)
    {
        _explorerNotice = notice;
        UpdateActions();
        DispatcherTimer.RunOnce(() =>
        {
            if (_explorerNotice != notice)
                return;
            _explorerNotice = null;
            UpdateActions();
        }, TimeSpan.FromSeconds(3), DispatcherPriority.Normal);
    }

    // ── Selection ──

    private void SetExplorerSelected(string id, bool selected)
    {
        if (selected == _state.SelectedIds.Contains(id))
            return;
        if (selected && _state.SelectedIds.Count >= InteractiveExplorerModel.MaxSelected)
        {
            ShowExplorerNotice(_labels.Format("compareLimit", InteractiveExplorerModel.MaxSelected));
            SyncExplorerSelection();
            return;
        }
        if (selected)
            _state.SelectedIds.Add(id);
        else
            _state.SelectedIds.Remove(id);
        SyncExplorerSelection();
        CommitState();
        if (_explorerMode == ExplorerMode.Compare)
            ShowExplorer(ExplorerMode.Compare, animate: _state.SelectedIds.Count < 2);
    }

    private void ClearExplorerSelection()
    {
        _state.SelectedIds.Clear();
        SyncExplorerSelection();
        CommitState();
        if (_explorerMode == ExplorerMode.Compare)
            ShowExplorer(ExplorerMode.Browse);
    }

    private void SyncExplorerSelection()
    {
        _explorerSyncing = true;
        try
        {
            foreach (var entry in _explorerToggles)
            {
                var on = _state.SelectedIds.Contains(entry.Id);
                entry.Toggle.IsChecked = on;
                entry.Host?.Classes.Set("selected", on);
                if (entry.Toggle.Classes.Contains("explorer-compare-wide"))
                    SetWideToggleContent(entry.Toggle, on);
            }
        }
        finally { _explorerSyncing = false; }
    }

    // ── Layout ──

    private void LayoutExplorer(double width)
    {
        if (_explorer is null || !double.IsFinite(width) || width <= 0)
            return;
        var content = Math.Max(0, width - (TouchMode ? 28 : 32));
        var narrow = width < 520;
        // Search shares its row with the tools only while it keeps a usable width beside them.
        var stacked = content < 660;
        var columns = content >= 560 ? 3 : content >= 360 ? 2 : 1;
        var media = ExplorerShowsMedia ? 52 : 0;
        var tableColumns = _explorer.Attributes.Count == 0 ? 0
            : Math.Clamp((int)((content - media - 170 - 44) / 104), 1, Math.Min(4, _explorer.Attributes.Count));
        var resized = Math.Abs(width - _explorerWidth) >= 1;
        _explorerWidth = width;

        if (columns != _explorerColumns)
        {
            _explorerColumns = columns;
            ArrangeExplorerCards();
        }
        if (narrow != _explorerNarrow || stacked != _explorerStacked)
        {
            _explorerNarrow = narrow;
            _explorerStacked = stacked;
            ArrangeExplorerToolbar();
            UpdateExplorerTools();
            if (_explorerFiltersOpen)
                UpdateExplorerFilterPanel();
        }
        if (tableColumns != _explorerTableColumns)
        {
            _explorerTableColumns = tableColumns;
            if (_explorerMode == ExplorerMode.Browse && _explorer.View(_state) == "table" && _explorerBrowse is not null)
                RefreshExplorerResults(animate: false);
        }
        // Detail and comparison layouts depend on the width they were built for.
        if (resized && _explorerMode != ExplorerMode.Browse && Math.Abs(width - _explorerBuiltWidth) >= 24)
            Dispatcher.UIThread.Post(() =>
            {
                if (_explorerMode != ExplorerMode.Browse && Math.Abs(_explorerWidth - _explorerBuiltWidth) >= 24)
                    ShowExplorer(_explorerMode, animate: false);
            });
    }

    private void ScheduleExplorerCommit()
    {
        _typedEditPending = true;
        if (_explorerCommitTimer is null)
        {
            _explorerCommitTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(450) };
            _explorerCommitTimer.Tick += (_, _) =>
            {
                _explorerCommitTimer.Stop();
                CommitTypedEdit();
            };
        }
        _explorerCommitTimer.Stop();
        _explorerCommitTimer.Start();
    }
}
