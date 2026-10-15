using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>Raised when the user switches an item of a <see cref="StrataComposerPicker"/> on or off.</summary>
public sealed class ComposerPickerToggledEventArgs : EventArgs
{
    public ComposerPickerToggledEventArgs(StrataComposerChip chip, bool isSelected)
    {
        Chip = chip;
        IsSelected = isSelected;
    }

    /// <summary>The item, as offered in <see cref="StrataComposerPicker.Items"/>.</summary>
    public StrataComposerChip Chip { get; }

    /// <summary>True when the user chose the item, false when they removed it.</summary>
    public bool IsSelected { get; }
}

/// <summary>
/// A composer toolbar pill that opens an in-window, searchable list of things to bring into a message,
/// such as skills or other chats. Rows show an item's glyph (or <see cref="ItemIcon"/>), its name, its
/// <see cref="StrataComposerChip.SecondaryText"/> and its <see cref="StrataComposerChip.SourceLabel"/>,
/// with a check on the ones in <see cref="SelectedItems"/>.
/// </summary>
/// <remarks>
/// <para>The picker never edits <see cref="SelectedItems"/>: choosing a row raises <see cref="ItemToggled"/>
/// and the owner decides what changes, so selection keeps a single source of truth. Items are matched by
/// <see cref="StrataComposerChip.Value"/> when both sides carry one, otherwise by name.</para>
/// <para>The order is fixed when the list opens — chosen items first — so rows never move under the
/// pointer. Typing ranks every item instead. <see cref="CloseOnSelect"/> suits one-off picks; without it
/// the list stays open so several items can be switched in a row.</para>
/// <para><b>Template parts:</b> PART_Button, PART_ButtonLabel, PART_CountText, PART_Popup, PART_Panel,
/// PART_SearchBox, PART_ListScroller, PART_List, PART_NoResultsText, PART_EmptyManageButton, PART_HintText,
/// PART_ManageButton.</para>
/// <para><b>Pseudo-classes:</b> :open, :has-selection, :no-items, :searching, :no-results, :can-manage,
/// :has-hint, :icon-only.</para>
/// </remarks>
public class StrataComposerPicker : TemplatedControl
{
    private const string CheckIconData = "M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z";

    private Button? _button;
    private TextBlock? _countText;
    private Popup? _popup;
    private Border? _panel;
    private TextBox? _searchBox;
    private ScrollViewer? _listScroller;
    private StackPanel? _list;
    private TextBlock? _noResultsText;
    private Button? _emptyManageButton;
    private Button? _manageButton;

    private INotifyCollectionChanged? _observedItems;
    private INotifyCollectionChanged? _observedSelection;
    private bool _isAttached;
    private bool _refreshQueued;
    private int _openingVersion;

    // The order is fixed when the list opens, so rows never move under the pointer while it is open.
    private readonly List<string> _order = new();
    private readonly HashSet<string> _selectedAtOpen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PickerRow> _rows = new(StringComparer.Ordinal);
    private readonly List<PickerRow> _visibleRows = new();
    private PickerRow? _activeRow;

    /// <summary>Everything the owner offers, as <see cref="StrataComposerChip"/>s.</summary>
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<StrataComposerPicker, IEnumerable?>(nameof(Items));

    /// <summary>What is chosen now. Read only: changes arrive through <see cref="ItemToggled"/>.</summary>
    public static readonly StyledProperty<IEnumerable?> SelectedItemsProperty =
        AvaloniaProperty.Register<StrataComposerPicker, IEnumerable?>(nameof(SelectedItems));

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<StrataComposerPicker, bool>(nameof(IsDropDownOpen), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Text in the list's search field. Cleared whenever the list opens.</summary>
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(SearchText), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The pill's label, such as "Skills". Hidden while <see cref="ShowLabel"/> is false.</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string?>(nameof(Label));

    /// <summary>The pill's icon.</summary>
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<StrataComposerPicker, Geometry?>(nameof(Icon));

    /// <summary>Drawn in every row's tile instead of the item's glyph, when set.</summary>
    public static readonly StyledProperty<Geometry?> ItemIconProperty =
        AvaloniaProperty.Register<StrataComposerPicker, Geometry?>(nameof(ItemIcon));

    /// <summary>False keeps the pill to its icon, for a narrow toolbar.</summary>
    public static readonly StyledProperty<bool> ShowLabelProperty =
        AvaloniaProperty.Register<StrataComposerPicker, bool>(nameof(ShowLabel), true);

    /// <summary>What the pill does, such as "Add skills (/)". Its tooltip and accessible help text.</summary>
    public static readonly StyledProperty<string?> ButtonToolTipProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string?>(nameof(ButtonToolTip));

    public static readonly StyledProperty<string> SearchWatermarkProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(SearchWatermark), "Search");

    /// <summary>Heading of the chosen items, listed first.</summary>
    public static readonly StyledProperty<string> SelectedLabelProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(SelectedLabel), "Added");

    /// <summary>Heading of everything else.</summary>
    public static readonly StyledProperty<string> ItemsLabelProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(ItemsLabel), "All");

    public static readonly StyledProperty<string> EmptyTitleProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(EmptyTitle), "Nothing here yet");

    public static readonly StyledProperty<string?> EmptyTextProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string?>(nameof(EmptyText));

    /// <summary>Shown when nothing matches the search: {0} is the search text.</summary>
    public static readonly StyledProperty<string> NoResultsFormatProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(NoResultsFormat), "Nothing matches \u201c{0}\u201d");

    /// <summary>A short tip in the footer, such as the shortcut that reaches the same list while typing.</summary>
    public static readonly StyledProperty<string?> HintTextProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string?>(nameof(HintText));

    public static readonly StyledProperty<string> ManageLabelProperty =
        AvaloniaProperty.Register<StrataComposerPicker, string>(nameof(ManageLabel), "Manage");

    /// <summary>Opens the owner's settings for these items. Choosing it closes the list.</summary>
    public static readonly StyledProperty<ICommand?> ManageCommandProperty =
        AvaloniaProperty.Register<StrataComposerPicker, ICommand?>(nameof(ManageCommand));

    /// <summary>Closes the list once an item is chosen, for one-off picks.</summary>
    public static readonly StyledProperty<bool> CloseOnSelectProperty =
        AvaloniaProperty.Register<StrataComposerPicker, bool>(nameof(CloseOnSelect));

    /// <summary>Most rows listed at once; zero lists everything. Searching still covers every item.</summary>
    public static readonly StyledProperty<int> MaxListedItemsProperty =
        AvaloniaProperty.Register<StrataComposerPicker, int>(nameof(MaxListedItems));

    /// <summary>
    /// What the list opens from; the pill itself when null. Lets an owner open the list from elsewhere,
    /// such as a menu, when the pill is hidden.
    /// </summary>
    public static readonly StyledProperty<Control?> PlacementTargetProperty =
        AvaloniaProperty.Register<StrataComposerPicker, Control?>(nameof(PlacementTarget));

    static StrataComposerPicker()
    {
        ItemsProperty.Changed.AddClassHandler<StrataComposerPicker>((picker, _) => picker.OnCollectionsReplaced());
        SelectedItemsProperty.Changed.AddClassHandler<StrataComposerPicker>((picker, _) => picker.OnCollectionsReplaced());
        IsDropDownOpenProperty.Changed.AddClassHandler<StrataComposerPicker>((picker, e) => picker.OnIsDropDownOpenChanged(e.NewValue is true));
        SearchTextProperty.Changed.AddClassHandler<StrataComposerPicker>((picker, _) => picker.OnSearchTextChanged());

        foreach (var property in new AvaloniaProperty[]
                 {
                     ButtonToolTipProperty, LabelProperty, ShowLabelProperty, HintTextProperty, ManageCommandProperty,
                 })
            property.Changed.AddClassHandler<StrataComposerPicker>((picker, _) => picker.UpdateChrome());
    }

    /// <summary>Raised when the user chooses or removes an item.</summary>
    public event EventHandler<ComposerPickerToggledEventArgs>? ItemToggled;

    /// <summary>Raised just before the list opens, so the owner can refresh <see cref="Items"/>.</summary>
    public event EventHandler? DropDownOpening;

    /// <summary>Raised when the list closes, however it closed, as soon as it starts to close.</summary>
    public event EventHandler? DropDownClosed;

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public IEnumerable? SelectedItems { get => GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }
    public bool IsDropDownOpen { get => GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }
    public string SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Geometry? ItemIcon { get => GetValue(ItemIconProperty); set => SetValue(ItemIconProperty, value); }
    public bool ShowLabel { get => GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
    public string? ButtonToolTip { get => GetValue(ButtonToolTipProperty); set => SetValue(ButtonToolTipProperty, value); }
    public string SearchWatermark { get => GetValue(SearchWatermarkProperty); set => SetValue(SearchWatermarkProperty, value); }
    public string SelectedLabel { get => GetValue(SelectedLabelProperty); set => SetValue(SelectedLabelProperty, value); }
    public string ItemsLabel { get => GetValue(ItemsLabelProperty); set => SetValue(ItemsLabelProperty, value); }
    public string EmptyTitle { get => GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }
    public string? EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string NoResultsFormat { get => GetValue(NoResultsFormatProperty); set => SetValue(NoResultsFormatProperty, value); }
    public string? HintText { get => GetValue(HintTextProperty); set => SetValue(HintTextProperty, value); }
    public string ManageLabel { get => GetValue(ManageLabelProperty); set => SetValue(ManageLabelProperty, value); }
    public ICommand? ManageCommand { get => GetValue(ManageCommandProperty); set => SetValue(ManageCommandProperty, value); }
    public bool CloseOnSelect { get => GetValue(CloseOnSelectProperty); set => SetValue(CloseOnSelectProperty, value); }
    public int MaxListedItems { get => GetValue(MaxListedItemsProperty); set => SetValue(MaxListedItemsProperty, value); }
    public Control? PlacementTarget { get => GetValue(PlacementTargetProperty); set => SetValue(PlacementTargetProperty, value); }

    // ── Template and lifetime ───────────────────────────────────────

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        WireTemplate(false);
        base.OnApplyTemplate(e);

        _button = e.NameScope.Find<Button>("PART_Button");
        _countText = e.NameScope.Find<TextBlock>("PART_CountText");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _panel = e.NameScope.Find<Border>("PART_Panel");
        _searchBox = e.NameScope.Find<TextBox>("PART_SearchBox");
        _listScroller = e.NameScope.Find<ScrollViewer>("PART_ListScroller");
        _list = e.NameScope.Find<StackPanel>("PART_List");
        _noResultsText = e.NameScope.Find<TextBlock>("PART_NoResultsText");
        _emptyManageButton = e.NameScope.Find<Button>("PART_EmptyManageButton");
        _manageButton = e.NameScope.Find<Button>("PART_ManageButton");
        _rows.Clear();
        _visibleRows.Clear();
        _activeRow = null;
        PseudoClasses.Set(":open", false);

        WireTemplate(true);
        UpdateChrome();
    }

    private void WireTemplate(bool attach)
    {
        if (_button is not null)
        {
            _button.Click -= OnButtonClick;
            if (attach)
                _button.Click += OnButtonClick;
        }

        if (_popup is not null)
        {
            _popup.PropertyChanged -= OnPopupPropertyChanged;
            if (attach)
                _popup.PropertyChanged += OnPopupPropertyChanged;
        }

        if (_panel is not null)
        {
            _panel.RemoveHandler(KeyDownEvent, OnPanelKeyDown);
            if (attach)
                _panel.AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
        }

        Wire(_manageButton, OnManageClick, attach);
        Wire(_emptyManageButton, OnManageClick, attach);
    }

    private static void Wire(Button? button, EventHandler<RoutedEventArgs> handler, bool attach)
    {
        if (button is null)
            return;
        button.Click -= handler;
        if (attach)
            button.Click += handler;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ObserveCollections();
        UpdateChrome();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        ObserveCollections();
        if (_popup is not null)
            _popup.IsOpen = false;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Follows the bound collections only while attached, so a view model that outlives this view never
    /// keeps it alive through a collection handler.
    /// </summary>
    private void ObserveCollections()
    {
        var items = _isAttached ? Items as INotifyCollectionChanged : null;
        if (!ReferenceEquals(items, _observedItems))
        {
            if (_observedItems is not null)
                _observedItems.CollectionChanged -= OnCollectionChanged;
            _observedItems = items;
            if (items is not null)
                items.CollectionChanged += OnCollectionChanged;
        }

        var selection = _isAttached ? SelectedItems as INotifyCollectionChanged : null;
        if (!ReferenceEquals(selection, _observedSelection))
        {
            if (_observedSelection is not null)
                _observedSelection.CollectionChanged -= OnCollectionChanged;
            _observedSelection = selection;
            if (selection is not null)
                selection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void OnCollectionsReplaced()
    {
        ObserveCollections();
        QueueRefresh();
    }

    // Refresh afterwards, never inside the notification, so an owner that edits the collection from its
    // handler is not re-entered.
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        UpdateChrome();
        if (_refreshQueued || _popup?.IsOpen != true)
            return;

        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            RefreshOpenList();
        });
    }

    // ── Data ────────────────────────────────────────────────────────

    /// <summary>A stable key: the item's value when it has one (two chats can share a title), else its name.</summary>
    internal static string KeyOf(StrataComposerChip chip)
        => string.IsNullOrEmpty(chip.Value) ? "n\u001f" + chip.Name : "v\u001f" + chip.Value;

    /// <summary>
    /// Whether two chips are the same item. Values decide when both carry one; otherwise names do, so a
    /// selection recorded by name alone still matches the item it came from.
    /// </summary>
    internal static bool SameItem(StrataComposerChip a, StrataComposerChip b)
        => !string.IsNullOrEmpty(a.Value) && !string.IsNullOrEmpty(b.Value)
            ? string.Equals(a.Value, b.Value, StringComparison.Ordinal)
            : string.Equals(a.Name, b.Name, StringComparison.Ordinal);

    private static StrataComposerChip AsChip(object? item)
        => item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? string.Empty);

    private List<StrataComposerChip> GetAvailable()
    {
        var result = new List<StrataComposerChip>();
        if (Items is null)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            var chip = AsChip(item);
            if (chip.Name.Length > 0 && seen.Add(KeyOf(chip)))
                result.Add(chip);
        }

        return result;
    }

    private List<StrataComposerChip> GetSelected()
    {
        var result = new List<StrataComposerChip>();
        if (SelectedItems is null)
            return result;

        foreach (var item in SelectedItems)
        {
            var chip = AsChip(item);
            if (chip.Name.Length > 0)
                result.Add(chip);
        }

        return result;
    }

    private bool IsSelected(StrataComposerChip chip)
    {
        if (SelectedItems is null)
            return false;

        foreach (var item in SelectedItems)
        {
            if (SameItem(AsChip(item), chip))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Everything the list can show: the offered items, plus chosen ones the owner no longer offers (a
    /// chat deleted since it was added), so they can still be seen and removed.
    /// </summary>
    private List<StrataComposerChip> GetListable()
    {
        var available = GetAvailable();
        foreach (var selected in GetSelected())
        {
            if (!available.Any(chip => SameItem(chip, selected)))
                available.Add(selected);
        }

        return available;
    }

    // ── Pill ────────────────────────────────────────────────────────

    /// <summary>Updates the pill and the list's chrome. Cheap: safe to call on every change.</summary>
    private void UpdateChrome()
    {
        var selected = GetSelected().Count;
        var hasItems = Items?.GetEnumerator().MoveNext() == true || selected > 0;

        PseudoClasses.Set(":has-selection", selected > 0);
        PseudoClasses.Set(":no-items", !hasItems);
        PseudoClasses.Set(":can-manage", ManageCommand is not null);
        PseudoClasses.Set(":has-hint", !string.IsNullOrWhiteSpace(HintText));
        PseudoClasses.Set(":icon-only", !ShowLabel || string.IsNullOrWhiteSpace(Label));

        if (_countText is not null)
            _countText.Text = selected > 0 ? selected.ToString(CultureInfo.CurrentCulture) : string.Empty;

        if (_button is not null)
        {
            AutomationProperties.SetName(_button, Label ?? ButtonToolTip ?? string.Empty);
            AutomationProperties.SetHelpText(_button, ButtonToolTip);
            ToolTip.SetTip(_button, string.IsNullOrWhiteSpace(ButtonToolTip) ? Label : ButtonToolTip);
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e) => SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);

    private void OnIsDropDownOpenChanged(bool open)
    {
        if (open)
            OpenList();
        else if (_popup is not null)
            _popup.IsOpen = false;
    }

    // The list is closed once its popup starts to close, not at the end of the closing fade, so whoever
    // takes the keyboard back can do so before anything around it settles.
    private void OnPopupPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Popup.IsOpenProperty || e.NewValue is not false)
            return;

        PseudoClasses.Set(":open", false);
        SetCurrentValue(IsDropDownOpenProperty, false);
        UpdateChrome();
        DropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OpenList()
    {
        if (_popup is null || _list is null)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            return;
        }

        if (_popup.IsOpen)
            return;

        // The owner may refresh what it offers (recent chats, say) before anything is listed.
        DropDownOpening?.Invoke(this, EventArgs.Empty);

        // Every opening starts fresh: no search and no row carried over from last time.
        _order.Clear();
        _selectedAtOpen.Clear();
        SetActiveRow(null);
        _rows.Clear();
        if (SearchText.Length > 0)
            SetCurrentValue(SearchTextProperty, string.Empty);
        // The field itself too: text set on it directly would otherwise outlive its binding.
        _searchBox?.SetCurrentValue(TextBox.TextProperty, string.Empty);

        // What is already chosen leads the list, so it can be reviewed and removed at a glance.
        var listable = GetListable();
        foreach (var chip in listable.Where(IsSelected))
        {
            _order.Add(KeyOf(chip));
            _selectedAtOpen.Add(KeyOf(chip));
        }
        foreach (var chip in listable.Where(chip => !IsSelected(chip)))
            _order.Add(KeyOf(chip));

        UpdateChrome();
        RebuildList(resetScroll: true);
        var focusMethod = _button?.Classes.Contains(":focus-visible") == true ? NavigationMethod.Tab : NavigationMethod.Unspecified;
        _popup.PlacementTarget = PlacementTarget ?? _button;
        _popup.IsOpen = true;
        PseudoClasses.Set(":open", true);

        var openingVersion = ++_openingVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_popup is { IsOpen: true } && openingVersion == _openingVersion)
                FocusList(focusMethod);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the keyboard in the list: in its search field, or on the empty state's action.</summary>
    private void FocusList(NavigationMethod method = NavigationMethod.Unspecified)
    {
        if (PseudoClasses.Contains(":no-items"))
            _emptyManageButton?.Focus(method);
        else
            _searchBox?.Focus();
    }

    private void OnManageClick(object? sender, RoutedEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        CommandHelper.Execute(ManageCommand, null);
    }

    /// <summary>Chooses or removes <paramref name="chip"/>, as a click on its row does.</summary>
    internal void Toggle(StrataComposerChip chip)
    {
        var select = !IsSelected(chip);
        ItemToggled?.Invoke(this, new ComposerPickerToggledEventArgs(chip, select));

        if (CloseOnSelect)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            return;
        }

        // The owner may decline the change; the queued refresh shows what actually happened.
        QueueRefresh();
    }

    // ── List ────────────────────────────────────────────────────────

    private void OnSearchTextChanged()
    {
        if (_popup?.IsOpen == true)
            RebuildList(resetScroll: true);
    }

    /// <summary>
    /// Updates the open list in place when only the selection changed, and rebuilds it when the offered
    /// items did.
    /// </summary>
    private void RefreshOpenList()
    {
        UpdateChrome();
        if (_popup?.IsOpen != true)
            return;

        var listable = GetListable();
        var listedKeys = listable.Select(KeyOf).ToList();
        if (!listedKeys.All(_rows.ContainsKey) || _rows.Keys.Any(key => !listedKeys.Contains(key)))
        {
            RebuildList(resetScroll: false);
        }
        else
        {
            foreach (var chip in listable)
            {
                if (_rows.TryGetValue(KeyOf(chip), out var row))
                    BindRow(row, chip);
            }
        }

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            FocusList();
    }

    private void RebuildList(bool resetScroll)
    {
        if (_list is null)
            return;

        var previousOffset = _listScroller?.Offset;
        var previousActive = _activeRow?.Key;
        var listable = GetListable();
        var byKey = new Dictionary<string, StrataComposerChip>(StringComparer.Ordinal);
        foreach (var chip in listable)
            byKey.TryAdd(KeyOf(chip), chip);

        foreach (var key in _rows.Keys.Where(key => !byKey.ContainsKey(key)).ToList())
            _rows.Remove(key);
        _order.RemoveAll(key => !byKey.ContainsKey(key));
        foreach (var key in byKey.Keys)
        {
            if (!_order.Contains(key))
                _order.Add(key);
        }

        _list.Children.Clear();
        _visibleRows.Clear();
        SetActiveRow(null);

        var query = SearchText.Trim();
        var searching = query.Length > 0;
        var limit = MaxListedItems > 0 ? MaxListedItems : int.MaxValue;
        PseudoClasses.Set(":searching", searching);

        if (searching)
        {
            // A search ranks every item by how well it matches, without headings. The best match is
            // ready for Enter, as in a command palette.
            var ranked = StrataChatComposerSearch.Rank(listable, query);
            foreach (var chip in ranked.Take(limit))
                AddRow(chip);

            PseudoClasses.Set(":no-results", ranked.Count == 0);
            if (_noResultsText is not null)
                _noResultsText.Text = string.Format(CultureInfo.CurrentCulture, NoResultsFormat, BidiText.Isolate(this, query));
            SetActiveRow(_visibleRows.FirstOrDefault());
        }
        else
        {
            PseudoClasses.Set(":no-results", false);
            var chosen = _order.Where(_selectedAtOpen.Contains).ToList();
            var rest = _order.Where(key => !_selectedAtOpen.Contains(key)).Take(limit).ToList();

            if (chosen.Count > 0)
            {
                AddHeader(SelectedLabel);
                foreach (var key in chosen)
                    AddRow(byKey[key]);
            }

            if (rest.Count > 0)
            {
                if (chosen.Count > 0)
                    AddHeader(ItemsLabel);
                foreach (var key in rest)
                    AddRow(byKey[key]);
            }

            // No row is active until the user moves to one, so an Enter meant for something else never
            // chooses an item.
            if (previousActive is not null)
                SetActiveRow(_visibleRows.FirstOrDefault(row => row.Key == previousActive));
        }

        if (_listScroller is not null)
        {
            if (resetScroll || previousOffset is not { } offset)
                _listScroller.Offset = default;
            else
                Dispatcher.UIThread.Post(() => _listScroller.Offset = offset, DispatcherPriority.Loaded);
        }
    }

    private void AddHeader(string label)
    {
        if (_list is null)
            return;

        var header = new TextBlock
        {
            Text = label.ToUpper(CultureInfo.CurrentCulture),
            Margin = new Thickness(10, _list.Children.Count == 0 ? 6 : 12, 10, 5),
        };
        header.Classes.Add("picker-group-header");
        _list.Children.Add(header);
    }

    private void AddRow(StrataComposerChip chip)
    {
        if (_list is null)
            return;

        var key = KeyOf(chip);
        if (!_rows.TryGetValue(key, out var row))
        {
            row = CreateRow(key);
            _rows[key] = row;
        }

        BindRow(row, chip);
        _list.Children.Add(row.Root);
        _visibleRows.Add(row);
    }

    private void SetActiveRow(PickerRow? row)
    {
        if (ReferenceEquals(_activeRow, row))
            return;

        _activeRow?.Root.Classes.Set("active", false);
        _activeRow = row;
        _activeRow?.Root.Classes.Set("active", true);
    }

    private void MoveActiveRow(int direction)
    {
        if (_visibleRows.Count == 0)
            return;

        var index = _activeRow is null ? -1 : _visibleRows.IndexOf(_activeRow);
        index = index < 0
            ? direction > 0 ? 0 : _visibleRows.Count - 1
            : Math.Clamp(index + direction, 0, _visibleRows.Count - 1);
        SetActiveRow(_visibleRows[index]);
        _visibleRows[index].Root.BringIntoView();
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_popup is not { IsOpen: true })
            return;

        if (e.Key == Key.Escape)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            _button?.Focus();
            e.Handled = true;
            return;
        }

        // A focused button (the manage link) keeps its own keys.
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any())
            return;

        if (e.Key is Key.Up or Key.Down)
        {
            MoveActiveRow(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _activeRow is { Chip: { } chip })
        {
            Toggle(chip);
            e.Handled = true;
        }
    }

    // ── Rows ────────────────────────────────────────────────────────

    private PickerRow CreateRow(string key)
    {
        var glyph = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.Classes.Add("picker-glyph");

        var icon = new StrataLineIcon
        {
            Width = 15,
            Height = 15,
            StrokeThickness = 2,
            IsVisible = false,
        };
        icon.Classes.Add("picker-icon");

        var tileContent = new Panel();
        tileContent.Children.Add(glyph);
        tileContent.Children.Add(icon);

        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center,
            Child = tileContent,
        };
        tile.Classes.Add("picker-tile");

        var nameText = new TextBlock { Name = "PickerItemName" };
        nameText.Classes.Add("picker-name");

        var caption = new TextBlock { Name = "PickerItemCaption" };
        caption.Classes.Add("picker-caption");

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        text.Children.Add(nameText);
        text.Children.Add(caption);

        var meta = new TextBlock { Name = "PickerItemMeta", VerticalAlignment = VerticalAlignment.Center };
        meta.Classes.Add("picker-meta");

        var checkIcon = new PathIcon
        {
            Data = StreamGeometry.Parse(CheckIconData),
            Width = 10,
            Height = 10,
            FlowDirection = FlowDirection.LeftToRight,
        };
        checkIcon.Classes.Add("picker-check-icon");
        var check = new Border
        {
            Name = "PickerItemCheck",
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center,
            Child = checkIcon,
        };
        check.Classes.Add("picker-check");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(meta, 2);
        Grid.SetColumn(check, 3);
        grid.Children.Add(tile);
        grid.Children.Add(text);
        grid.Children.Add(meta);
        grid.Children.Add(check);

        var root = new Border
        {
            Child = grid,
            Padding = new Thickness(7, 6, 10, 6),
            CornerRadius = new CornerRadius(10),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = false,
        };
        root.Classes.Add("picker-row");

        var row = new PickerRow(key, root, glyph, icon, nameText, caption, meta);
        root.PointerEntered += (_, _) => SetActiveRow(row);
        root.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed || row.Chip is not { } chip)
                return;

            SetActiveRow(row);
            Toggle(chip);
            e.Handled = true;
        };
        return row;
    }

    private void BindRow(PickerRow row, StrataComposerChip chip)
    {
        row.Chip = chip;
        var selected = IsSelected(chip);

        row.NameText.Text = BidiText.Isolate(this, chip.Name);

        var caption = string.IsNullOrWhiteSpace(chip.SecondaryText) ? null : chip.SecondaryText;
        row.Caption.Text = BidiText.Isolate(this, caption);
        row.Caption.IsVisible = caption is not null;

        row.Meta.Text = BidiText.Isolate(this, chip.SourceLabel);
        row.Meta.IsVisible = !string.IsNullOrWhiteSpace(chip.SourceLabel);

        if (ItemIcon is { } itemIcon)
        {
            row.Icon.Data = itemIcon;
            row.Icon.IsVisible = true;
            row.Glyph.IsVisible = false;
        }
        else
        {
            row.Glyph.Text = string.IsNullOrWhiteSpace(chip.Glyph) ? "\u2726" : chip.Glyph;
            row.Glyph.IsVisible = true;
            row.Icon.IsVisible = false;
        }

        // No tooltip: rows are rebuilt while the pointer rests on them, and a tip that closes after its
        // row left the tree crashes Avalonia.
        row.Root.Classes.Set("selected", selected);
        row.Root.Classes.Set("single", CloseOnSelect);
        AutomationProperties.SetName(row.Root, chip.Name);
        AutomationProperties.SetHelpText(row.Root, caption);
    }

    private sealed class PickerRow(
        string key,
        Border root,
        TextBlock glyph,
        StrataLineIcon icon,
        TextBlock nameText,
        TextBlock caption,
        TextBlock meta)
    {
        public string Key { get; } = key;
        public Border Root { get; } = root;
        public TextBlock Glyph { get; } = glyph;
        public StrataLineIcon Icon { get; } = icon;
        public TextBlock NameText { get; } = nameText;
        public TextBlock Caption { get; } = caption;
        public TextBlock Meta { get; } = meta;
        public StrataComposerChip? Chip { get; set; }
    }
}
