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
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// Chooses the connectors (MCP servers) a chat can use: a compact pill for a composer toolbar and an
/// in-window list with search, a switch per connector, its state, and the host's actions to fix a failing
/// connector, find new ones and manage them.
/// </summary>
/// <remarks>
/// <para><see cref="Items"/> lists every connector the host offers. <see cref="SelectedItems"/> holds the
/// ones that are on; it must be an <see cref="IList"/>, and the picker adds and removes chips in it
/// directly. State lives on the selected chips: an error (<see cref="StrataComposerChip.ErrorMessage"/>
/// with <see cref="StrataComposerChip.ErrorDetails"/>) or a neutral
/// <see cref="StrataComposerChip.StatusMessage"/>. A row shows a connector's
/// <see cref="StrataComposerChip.SecondaryText"/> under its name and lists it under its
/// <see cref="StrataComposerChip.Group"/>.</para>
/// <para><b>Template parts:</b> PART_Button, PART_ButtonLabel, PART_Popup, PART_Panel, PART_SearchBox,
/// PART_SummaryText, PART_ToggleAllButton, PART_ToggleAllText, PART_ListScroller, PART_List,
/// PART_NoResultsText, PART_SearchBrowseButton, PART_SearchBrowseText, PART_EmptyBrowseButton,
/// PART_EmptyManageButton, PART_BrowseButton, PART_ManageButton.</para>
/// <para><b>Pseudo-classes:</b> :open, :has-selection, :has-errors, :no-connectors, :loading, :searching,
/// :no-results, :can-browse, :can-manage.</para>
/// </remarks>
public class StrataConnectorPicker : TemplatedControl
{
    private const string WarningIconData = "M12 2 1 21h22L12 2zm-1 7h2v6h-2V9zm0 8h2v2h-2v-2z";
    private const string RepairIconData = "M12 2l2.4 7.6L22 12l-7.6 2.4L12 22l-2.4-7.6L2 12l7.6-2.4L12 2z";
    private const string ChevronIconData = "M7.4 8.6 12 13.2l4.6-4.6L18 10l-6 6-6-6z";
    private const string CopyIconData = "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z";
    private const string CopiedIconData = "M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z";
    private const string PlugIconData = "M17 6.1h3V8h-3v2.1a5 5 0 0 1-4 4.9V18h2v2H9v-2h2v-3a5 5 0 0 1-4-4.9V8H4V6.1h3V4h2v2.1h4V4h2v2.1zM9 8v2.1a3 3 0 0 0 6 0V8H9z";
    private static readonly StringComparer NameComparer = StringComparer.Ordinal;

    private Button? _button;
    private TextBlock? _buttonLabel;
    private Popup? _popup;
    private Border? _panel;
    private TextBox? _searchBox;
    private TextBlock? _summaryText;
    private Button? _toggleAllButton;
    private TextBlock? _toggleAllText;
    private ScrollViewer? _listScroller;
    private StackPanel? _list;
    private TextBlock? _noResultsText;
    private Button? _searchBrowseButton;
    private TextBlock? _searchBrowseText;
    private Button? _emptyBrowseButton;
    private Button? _emptyManageButton;
    private Button? _browseButton;
    private Button? _manageButton;

    private INotifyCollectionChanged? _observedItems;
    private INotifyCollectionChanged? _observedSelection;
    private bool _isAttached;
    private bool _refreshQueued;
    private bool _bindingRow;
    private int _openingVersion;

    // The list's order is fixed when it opens, so rows never move under the pointer while it is open.
    private readonly List<string> _order = new();
    private readonly HashSet<string> _attentionAtOpen = new(NameComparer);
    private readonly Dictionary<string, ConnectorRow> _rows = new(NameComparer);
    private readonly List<string> _listedNames = new();
    private readonly List<ConnectorRow> _visibleRows = new();
    private readonly HashSet<string> _expandedDetails = new(NameComparer);
    private ConnectorRow? _activeRow;

    /// <summary>Every connector the host offers, as <see cref="StrataComposerChip"/>s.</summary>
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, IEnumerable?>(nameof(Items));

    /// <summary>The connectors that are on. Must be an <see cref="IList"/>: the picker edits it directly.</summary>
    public static readonly StyledProperty<IEnumerable?> SelectedItemsProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, IEnumerable?>(nameof(SelectedItems));

    /// <summary>Whether the list is open.</summary>
    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, bool>(nameof(IsDropDownOpen), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Text in the list's search field. Cleared whenever the list opens.</summary>
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(SearchText), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// What the pill means right now, such as "3 of 4 connectors on". Its tooltip and accessible name,
    /// so a screen reader hears the state rather than a bare count.
    /// </summary>
    public static readonly StyledProperty<string?> ButtonToolTipProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string?>(nameof(ButtonToolTip));

    /// <summary>Accessible name of the list, and of the pill while <see cref="ButtonToolTip"/> is empty.</summary>
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(Header), "Connectors");

    /// <summary>
    /// True while the host is still discovering connectors. With none found yet, the list says it is
    /// looking instead of inviting the user to add their first one.
    /// </summary>
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, bool>(nameof(IsLoading));

    public static readonly StyledProperty<string> SearchWatermarkProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(SearchWatermark), "Search connectors");

    /// <summary>Heading of the connectors that need attention, listed first.</summary>
    public static readonly StyledProperty<string> AttentionLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(AttentionLabel), "Needs attention");

    /// <summary>Line under the search field: {0} connectors on, of {1}.</summary>
    public static readonly StyledProperty<string> SummaryFormatProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(SummaryFormat), "{0} of {1} on in this chat");

    public static readonly StyledProperty<string> TurnAllOnLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(TurnAllOnLabel), "Turn all on");

    public static readonly StyledProperty<string> TurnAllOffLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(TurnAllOffLabel), "Turn all off");

    /// <summary>The pill's label while every connector is off.</summary>
    public static readonly StyledProperty<string> OffLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(OffLabel), "Off");

    public static readonly StyledProperty<string> BrowseLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(BrowseLabel), "Find more");

    public static readonly StyledProperty<string> ManageLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(ManageLabel), "Manage");

    /// <summary>Shown when no connector matches the search: {0} is the search text.</summary>
    public static readonly StyledProperty<string> NoResultsFormatProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(NoResultsFormat), "No connectors match \u201c{0}\u201d");

    /// <summary>Offers <see cref="BrowseCommand"/> for a search with no match: {0} is the search text.</summary>
    public static readonly StyledProperty<string> SearchBrowseFormatProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(SearchBrowseFormat), "Find \u201c{0}\u201d connectors");

    public static readonly StyledProperty<string> EmptyTitleProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(EmptyTitle), "No connectors yet");

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(EmptyText), "Connectors let the assistant use your apps, files and services.");

    public static readonly StyledProperty<string> EmptyActionLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(EmptyActionLabel), "Find connectors");

    /// <summary>Takes the empty list's title while <see cref="IsLoading"/>.</summary>
    public static readonly StyledProperty<string> LoadingTextProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(LoadingText), "Looking for connectors\u2026");

    /// <summary>Label of <see cref="ErrorActionCommand"/>, such as "Fix with Lumi". The action is hidden while empty.</summary>
    public static readonly StyledProperty<string?> ErrorActionLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string?>(nameof(ErrorActionLabel));

    public static readonly StyledProperty<string> ErrorDetailsLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(ErrorDetailsLabel), "Details");

    public static readonly StyledProperty<string> CopyErrorDetailsLabelProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, string>(nameof(CopyErrorDetailsLabel), "Copy details");

    /// <summary>Offered on a connector that reports an error. The parameter is its chip; choosing it closes the list.</summary>
    public static readonly StyledProperty<ICommand?> ErrorActionCommandProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, ICommand?>(nameof(ErrorActionCommand));

    /// <summary>Finds new connectors. The parameter is the search text, or null. Choosing it closes the list.</summary>
    public static readonly StyledProperty<ICommand?> BrowseCommandProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, ICommand?>(nameof(BrowseCommand));

    /// <summary>Opens the host's connector settings. Choosing it closes the list.</summary>
    public static readonly StyledProperty<ICommand?> ManageCommandProperty =
        AvaloniaProperty.Register<StrataConnectorPicker, ICommand?>(nameof(ManageCommand));

    static StrataConnectorPicker()
    {
        ItemsProperty.Changed.AddClassHandler<StrataConnectorPicker>((picker, _) => picker.OnCollectionsReplaced());
        SelectedItemsProperty.Changed.AddClassHandler<StrataConnectorPicker>((picker, _) => picker.OnCollectionsReplaced());
        IsDropDownOpenProperty.Changed.AddClassHandler<StrataConnectorPicker>((picker, e) => picker.OnIsDropDownOpenChanged(e.NewValue is true));
        SearchTextProperty.Changed.AddClassHandler<StrataConnectorPicker>((picker, _) => picker.OnSearchTextChanged());

        foreach (var property in new AvaloniaProperty[]
                 {
                     ButtonToolTipProperty, HeaderProperty, IsLoadingProperty, BrowseCommandProperty,
                     ManageCommandProperty, OffLabelProperty, SummaryFormatProperty, TurnAllOnLabelProperty,
                     TurnAllOffLabelProperty,
                 })
            property.Changed.AddClassHandler<StrataConnectorPicker>((picker, _) => picker.UpdateChrome());
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public IEnumerable? SelectedItems { get => GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }
    public bool IsDropDownOpen { get => GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }
    public string SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string? ButtonToolTip { get => GetValue(ButtonToolTipProperty); set => SetValue(ButtonToolTipProperty, value); }
    public string Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string SearchWatermark { get => GetValue(SearchWatermarkProperty); set => SetValue(SearchWatermarkProperty, value); }
    public string AttentionLabel { get => GetValue(AttentionLabelProperty); set => SetValue(AttentionLabelProperty, value); }
    public string SummaryFormat { get => GetValue(SummaryFormatProperty); set => SetValue(SummaryFormatProperty, value); }
    public string TurnAllOnLabel { get => GetValue(TurnAllOnLabelProperty); set => SetValue(TurnAllOnLabelProperty, value); }
    public string TurnAllOffLabel { get => GetValue(TurnAllOffLabelProperty); set => SetValue(TurnAllOffLabelProperty, value); }
    public string OffLabel { get => GetValue(OffLabelProperty); set => SetValue(OffLabelProperty, value); }
    public string BrowseLabel { get => GetValue(BrowseLabelProperty); set => SetValue(BrowseLabelProperty, value); }
    public string ManageLabel { get => GetValue(ManageLabelProperty); set => SetValue(ManageLabelProperty, value); }
    public string NoResultsFormat { get => GetValue(NoResultsFormatProperty); set => SetValue(NoResultsFormatProperty, value); }
    public string SearchBrowseFormat { get => GetValue(SearchBrowseFormatProperty); set => SetValue(SearchBrowseFormatProperty, value); }
    public string EmptyTitle { get => GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string EmptyActionLabel { get => GetValue(EmptyActionLabelProperty); set => SetValue(EmptyActionLabelProperty, value); }
    public string LoadingText { get => GetValue(LoadingTextProperty); set => SetValue(LoadingTextProperty, value); }
    public string? ErrorActionLabel { get => GetValue(ErrorActionLabelProperty); set => SetValue(ErrorActionLabelProperty, value); }
    public string ErrorDetailsLabel { get => GetValue(ErrorDetailsLabelProperty); set => SetValue(ErrorDetailsLabelProperty, value); }
    public string CopyErrorDetailsLabel { get => GetValue(CopyErrorDetailsLabelProperty); set => SetValue(CopyErrorDetailsLabelProperty, value); }
    public ICommand? ErrorActionCommand { get => GetValue(ErrorActionCommandProperty); set => SetValue(ErrorActionCommandProperty, value); }
    public ICommand? BrowseCommand { get => GetValue(BrowseCommandProperty); set => SetValue(BrowseCommandProperty, value); }
    public ICommand? ManageCommand { get => GetValue(ManageCommandProperty); set => SetValue(ManageCommandProperty, value); }

    // ── Template and lifetime ───────────────────────────────────────

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        WireTemplate(false);
        base.OnApplyTemplate(e);

        _button = e.NameScope.Find<Button>("PART_Button");
        _buttonLabel = e.NameScope.Find<TextBlock>("PART_ButtonLabel");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _panel = e.NameScope.Find<Border>("PART_Panel");
        _searchBox = e.NameScope.Find<TextBox>("PART_SearchBox");
        _summaryText = e.NameScope.Find<TextBlock>("PART_SummaryText");
        _toggleAllButton = e.NameScope.Find<Button>("PART_ToggleAllButton");
        _toggleAllText = e.NameScope.Find<TextBlock>("PART_ToggleAllText");
        _listScroller = e.NameScope.Find<ScrollViewer>("PART_ListScroller");
        _list = e.NameScope.Find<StackPanel>("PART_List");
        _noResultsText = e.NameScope.Find<TextBlock>("PART_NoResultsText");
        _searchBrowseButton = e.NameScope.Find<Button>("PART_SearchBrowseButton");
        _searchBrowseText = e.NameScope.Find<TextBlock>("PART_SearchBrowseText");
        _emptyBrowseButton = e.NameScope.Find<Button>("PART_EmptyBrowseButton");
        _emptyManageButton = e.NameScope.Find<Button>("PART_EmptyManageButton");
        _browseButton = e.NameScope.Find<Button>("PART_BrowseButton");
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
            _popup.Closed -= OnPopupClosed;
            if (attach)
                _popup.Closed += OnPopupClosed;
        }

        if (_panel is not null)
        {
            _panel.RemoveHandler(KeyDownEvent, OnPanelKeyDown);
            if (attach)
                _panel.AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
        }

        Wire(_toggleAllButton, OnToggleAllClick, attach);
        Wire(_searchBrowseButton, OnSearchBrowseClick, attach);
        Wire(_emptyBrowseButton, OnBrowseClick, attach);
        Wire(_emptyManageButton, OnManageClick, attach);
        Wire(_browseButton, OnBrowseClick, attach);
        Wire(_manageButton, OnManageClick, attach);
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
    /// Follows the bound collections only while attached, so a chat view model that outlives this view
    /// never keeps it alive through a collection handler.
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

    // The selection also changes from the list's own switches. Refresh afterwards, never inside the
    // notification, so a host that edits the collection in its handler is not re-entered.
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

    private static string NameOf(object? item) => item is StrataComposerChip chip ? chip.Name : item?.ToString() ?? string.Empty;

    private List<StrataComposerChip> GetAvailable()
    {
        var result = new List<StrataComposerChip>();
        if (Items is null)
            return result;

        var seen = new HashSet<string>(NameComparer);
        foreach (var item in Items)
        {
            var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? string.Empty);
            if (chip.Name.Length > 0 && seen.Add(chip.Name))
                result.Add(chip);
        }

        return result;
    }

    private StrataComposerChip? FindSelected(string name)
    {
        if (SelectedItems is null)
            return null;

        foreach (var item in SelectedItems)
        {
            if (NameComparer.Equals(NameOf(item), name))
                return item as StrataComposerChip ?? new StrataComposerChip(name);
        }

        return null;
    }

    private bool IsSelected(string name) => FindSelected(name) is not null;

    private void SetConnectorOn(StrataComposerChip chip, bool on)
    {
        if (SelectedItems is not IList list)
            return;

        var index = -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (NameComparer.Equals(NameOf(list[i]), chip.Name))
            {
                index = i;
                break;
            }
        }

        if (on && index < 0)
            list.Add(chip with { ErrorMessage = null, ErrorDetails = null, StatusMessage = null });
        else if (!on && index >= 0)
            list.RemoveAt(index);
    }

    /// <summary>
    /// Keeps left-to-right text, such as a connector named "GitHub (work)", intact inside a right-to-left
    /// layout: without an isolate its closing parenthesis would jump to the start of the line.
    /// </summary>
    private string Isolate(string? text)
        => string.IsNullOrEmpty(text) || FlowDirection != FlowDirection.RightToLeft
            ? text ?? string.Empty
            : "\u2068" + text + "\u2069";

    // ── Pill ────────────────────────────────────────────────────────

    /// <summary>Updates the pill and the list's chrome. Cheap: safe to call on every change.</summary>
    private void UpdateChrome()
    {
        var available = GetAvailable();
        var on = available.Count(chip => IsSelected(chip.Name));
        var hasErrors = available.Any(chip => FindSelected(chip.Name)?.HasError == true);

        PseudoClasses.Set(":has-selection", on > 0);
        PseudoClasses.Set(":has-errors", hasErrors);
        PseudoClasses.Set(":no-connectors", available.Count == 0);
        PseudoClasses.Set(":loading", IsLoading);
        PseudoClasses.Set(":can-browse", BrowseCommand is not null);
        PseudoClasses.Set(":can-manage", ManageCommand is not null);

        if (_buttonLabel is not null && !PseudoClasses.Contains(":open"))
        {
            // A count keeps the pill small. The open list covers the pill and is centred on it, so the label
            // waits until the list has closed: a new width now ("4" → "Off") would shift the list under the pointer.
            _buttonLabel.Text = available.Count == 0
                ? string.Empty
                : on == 0
                    ? OffLabel
                    : on.ToString(CultureInfo.CurrentCulture);
            _buttonLabel.IsVisible = _buttonLabel.Text.Length > 0;
        }

        if (_button is not null)
            AutomationProperties.SetName(_button, string.IsNullOrWhiteSpace(ButtonToolTip) ? Header : ButtonToolTip);
        if (_summaryText is not null)
            _summaryText.Text = string.Format(CultureInfo.CurrentCulture, SummaryFormat, on, available.Count);
        if (_toggleAllText is not null)
            _toggleAllText.Text = available.Count > 0 && on == available.Count ? TurnAllOffLabel : TurnAllOnLabel;
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e) => SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);

    private void OnIsDropDownOpenChanged(bool open)
    {
        if (open)
            OpenList();
        else if (_popup is not null)
            _popup.IsOpen = false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        PseudoClasses.Set(":open", false);
        SetCurrentValue(IsDropDownOpenProperty, false);
        UpdateChrome();
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

        // Every opening starts fresh: no search, disclosures closed, no row carried over from last time.
        _order.Clear();
        _attentionAtOpen.Clear();
        _expandedDetails.Clear();
        SetActiveRow(null);
        _rows.Clear();
        if (SearchText.Length > 0)
            SetCurrentValue(SearchTextProperty, string.Empty);
        // The field itself too: text set on it directly would otherwise outlive its binding.
        _searchBox?.SetCurrentValue(TextBox.TextProperty, string.Empty);

        // Connectors that need attention lead the list when it opens, so a problem is never scrolled out
        // of sight; the order then holds while the list is open.
        foreach (var chip in GetAvailable())
        {
            _order.Add(chip.Name);
            if (FindSelected(chip.Name)?.HasError == true)
                _attentionAtOpen.Add(chip.Name);
        }

        UpdateChrome();
        RebuildList(resetScroll: true);
        // Opened from the keyboard, the empty state's action shows its focus ring; after a click it does not.
        var focusMethod = _button?.Classes.Contains(":focus-visible") == true ? NavigationMethod.Tab : NavigationMethod.Unspecified;
        _popup.IsOpen = true;
        PseudoClasses.Set(":open", true);

        var openingVersion = ++_openingVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_popup is { IsOpen: true } && openingVersion == _openingVersion)
                FocusList(focusMethod);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the keyboard in the list: in its search field, or on the empty state's first action.</summary>
    private void FocusList(NavigationMethod method = NavigationMethod.Unspecified)
    {
        if (PseudoClasses.Contains(":no-connectors"))
            (_emptyBrowseButton is { IsEffectivelyVisible: true } ? _emptyBrowseButton : _emptyManageButton)?.Focus(method);
        else
            _searchBox?.Focus();
    }

    private void CloseAndExecute(ICommand? command, object? parameter)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        CommandHelper.Execute(command, parameter);
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e) => CloseAndExecute(BrowseCommand, null);

    private void OnSearchBrowseClick(object? sender, RoutedEventArgs e)
    {
        var query = SearchText.Trim();
        CloseAndExecute(BrowseCommand, query.Length == 0 ? null : query);
    }

    private void OnManageClick(object? sender, RoutedEventArgs e) => CloseAndExecute(ManageCommand, null);

    private void OnToggleAllClick(object? sender, RoutedEventArgs e)
    {
        var available = GetAvailable();
        var turnOn = available.Any(chip => !IsSelected(chip.Name));
        foreach (var chip in available)
            SetConnectorOn(chip, turnOn);
    }

    // ── List ────────────────────────────────────────────────────────

    private void OnSearchTextChanged()
    {
        if (_popup?.IsOpen == true)
            RebuildList(resetScroll: true);
    }

    /// <summary>
    /// Updates the open list in place: a connector's state can change while the list is open (it failed,
    /// recovered or was switched). Rows are only rebuilt when the offered connectors change.
    /// </summary>
    private void RefreshOpenList()
    {
        UpdateChrome();
        if (_popup?.IsOpen != true)
            return;

        var available = GetAvailable();
        if (!available.Select(chip => chip.Name).SequenceEqual(_listedNames, NameComparer))
        {
            RebuildList(resetScroll: false);
        }
        else
        {
            foreach (var chip in available)
            {
                if (_rows.TryGetValue(chip.Name, out var row))
                    BindRow(row, chip);
            }
        }

        // A refresh can hide or replace the focused control (the empty state's action once connectors
        // arrive, a switch whose row is rebuilt); keep the keyboard in the list rather than dropping it.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            FocusList();
    }

    private void RebuildList(bool resetScroll)
    {
        if (_list is null)
            return;

        var previousOffset = _listScroller?.Offset;
        var previousActive = _activeRow?.Name;
        var available = GetAvailable();
        var byName = available.ToDictionary(chip => chip.Name, NameComparer);
        _listedNames.Clear();
        _listedNames.AddRange(available.Select(chip => chip.Name));

        foreach (var name in _rows.Keys.Where(name => !byName.ContainsKey(name)).ToList())
            _rows.Remove(name);
        _order.RemoveAll(name => !byName.ContainsKey(name));
        foreach (var chip in available)
        {
            if (!_order.Contains(chip.Name))
                _order.Add(chip.Name);
        }

        _list.Children.Clear();
        _visibleRows.Clear();
        SetActiveRow(null);

        var query = SearchText.Trim();
        var searching = query.Length > 0;
        PseudoClasses.Set(":searching", searching);

        if (searching)
        {
            // A search ranks every connector by how well it matches, without headings. The best match is
            // ready for Enter, as in a command palette.
            var ranked = StrataChatComposerSearch.RankConnectors(available, query);
            foreach (var chip in ranked)
                AddRow(byName[chip.Name]);

            PseudoClasses.Set(":no-results", ranked.Count == 0);
            if (_noResultsText is not null)
                _noResultsText.Text = string.Format(CultureInfo.CurrentCulture, NoResultsFormat, Isolate(query));
            if (_searchBrowseText is not null)
                _searchBrowseText.Text = string.Format(CultureInfo.CurrentCulture, SearchBrowseFormat, Isolate(query));
            SetActiveRow(_visibleRows.FirstOrDefault());
        }
        else
        {
            PseudoClasses.Set(":no-results", false);
            var attention = _order.Where(_attentionAtOpen.Contains).ToList();
            var groups = _order
                .Where(name => !_attentionAtOpen.Contains(name))
                .GroupBy(name => byName[name].Group ?? string.Empty, NameComparer)
                .ToList();
            var labelGroups = groups.Count > 1 || (attention.Count > 0 && groups.Any(group => group.Key.Length > 0));

            if (attention.Count > 0)
            {
                AddHeader(AttentionLabel);
                foreach (var name in attention)
                    AddRow(byName[name]);
            }

            foreach (var group in groups)
            {
                if (labelGroups && group.Key.Length > 0)
                    AddHeader(group.Key);
                else if (_list.Children.Count > 0)
                    _list.Children.Add(new Border { Height = 8 });

                foreach (var name in group)
                    AddRow(byName[name]);
            }

            // No row is active until the user moves to one, so an Enter meant for something else never
            // switches a connector.
            if (previousActive is not null)
                SetActiveRow(_visibleRows.FirstOrDefault(row => NameComparer.Equals(row.Name, previousActive)));
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
            Margin = new Thickness(10, _list.Children.Count == 0 ? 6 : 14, 10, 6),
        };
        header.Classes.Add("connector-group-header");
        _list.Children.Add(header);
    }

    private void AddRow(StrataComposerChip chip)
    {
        if (_list is null)
            return;

        if (!_rows.TryGetValue(chip.Name, out var row))
        {
            row = CreateRow(chip.Name);
            _rows[chip.Name] = row;
        }

        BindRow(row, chip);
        _list.Children.Add(row.Root);
        _visibleRows.Add(row);
    }

    private void SetActiveRow(ConnectorRow? row)
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

        // A focused button (a switch, an action, a link) keeps its own keys.
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any())
            return;

        if (e.Key is Key.Up or Key.Down)
        {
            MoveActiveRow(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _activeRow is { } row)
        {
            row.Switch.IsChecked = row.Switch.IsChecked != true;
            e.Handled = true;
        }
    }

    // ── Rows ────────────────────────────────────────────────────────

    private ConnectorRow CreateRow(string name)
    {
        // A plug has no direction: it must not mirror in a right-to-left layout.
        var glyph = new PathIcon
        {
            Data = StreamGeometry.Parse(PlugIconData),
            Width = 14,
            Height = 14,
            FlowDirection = FlowDirection.LeftToRight,
        };
        glyph.Classes.Add("connector-glyph");
        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
            Child = glyph,
        };
        tile.Classes.Add("connector-tile");

        var attentionDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -3, -3),
            IsVisible = false,
        };
        attentionDot.Classes.Add("connector-attention-dot");

        var tileHost = new Panel { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
        tileHost.Children.Add(tile);
        tileHost.Children.Add(attentionDot);

        var nameText = new TextBlock { Name = "ConnectorName" };
        nameText.Classes.Add("connector-name");

        var caption = new TextBlock { Name = "ConnectorCaption" };
        caption.Classes.Add("connector-caption");

        var extras = new StackPanel { Spacing = 6, IsVisible = false, Margin = new Thickness(0, 3, 0, 1) };

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, MinHeight = 30 };
        text.Children.Add(nameText);
        text.Children.Add(caption);
        text.Children.Add(extras);

        var toggle = new ToggleButton
        {
            Name = "ConnectorSwitch",
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0),
        };
        toggle.Classes.Add("connector-switch");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 11 };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(tileHost);
        grid.Children.Add(text);
        grid.Children.Add(toggle);

        var root = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 7, 10, 7),
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        root.Classes.Add("connector-row");

        var row = new ConnectorRow(name, root, attentionDot, nameText, caption, extras, toggle);
        root.PointerEntered += (_, _) => SetActiveRow(row);
        root.PointerPressed += (_, e) => OnRowPressed(row, e);
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (_bindingRow)
                return;
            SetConnectorOn(row.Available, toggle.IsChecked == true);
            // The host may decline the change; the queued refresh shows what actually happened.
            QueueRefresh();
        };
        return row;
    }

    private void OnRowPressed(ConnectorRow row, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(row.Root).Properties.IsLeftButtonPressed)
            return;

        // A problem's reason, actions and selectable details never switch the connector.
        if (e.Source is Visual source && (ReferenceEquals(source, row.Extras) || row.Extras.IsVisualAncestorOf(source)))
            return;

        SetActiveRow(row);
        row.Switch.IsChecked = row.Switch.IsChecked != true;
        e.Handled = true;
    }

    private void BindRow(ConnectorRow row, StrataComposerChip available)
    {
        var active = FindSelected(available.Name);
        var issue = active?.HasError == true ? active : null;
        var status = issue is null && !string.IsNullOrWhiteSpace(active?.StatusMessage) ? active!.StatusMessage : null;
        var caption = string.IsNullOrWhiteSpace(available.SecondaryText) ? null : available.SecondaryText;

        row.Available = available;
        row.NameText.Text = Isolate(available.Name);

        // A problem or a status takes the caption's place: it is what matters right now.
        row.Caption.Text = Isolate(caption);
        row.Caption.IsVisible = caption is not null && issue is null && status is null;
        ToolTip.SetTip(row.Caption, caption);

        _bindingRow = true;
        try
        {
            row.Switch.IsChecked = active is not null;
        }
        finally
        {
            _bindingRow = false;
        }

        row.Root.Classes.Set("off", active is null);
        row.AttentionDot.IsVisible = issue is not null;

        AutomationProperties.SetName(row.Switch, available.Name);
        AutomationProperties.SetHelpText(row.Switch, issue?.ErrorMessage ?? status ?? caption);

        var extrasKey = issue is not null
            ? $"issue\u001f{issue.ErrorMessage}\u001f{issue.ErrorDetails}\u001f{ErrorActionLabel}\u001f{ErrorActionCommand?.GetHashCode()}"
            : status is not null
                ? $"status\u001f{status}"
                : string.Empty;
        if (extrasKey == row.ExtrasKey)
            return;

        row.ExtrasKey = extrasKey;
        row.Extras.Children.Clear();
        if (issue is not null)
            row.Extras.Children.Add(CreateIssuePanel(issue));
        else if (status is not null)
            row.Extras.Children.Add(CreateStatusLine(status));
        row.Extras.IsVisible = row.Extras.Children.Count > 0;
    }

    /// <summary>A neutral state that needs nothing from the user, such as "Still starting".</summary>
    private static Control CreateStatusLine(string status)
    {
        var text = new TextBlock
        {
            Name = "McpStatusMessage",
            Text = status,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
        };
        text.Classes.Add("connector-status");
        return text;
    }

    /// <summary>
    /// The calm presentation of a connector that reports an error: its one-line reason, the host's action
    /// (for example "Fix with Lumi"), and the complete technical details behind a disclosure.
    /// </summary>
    private Control CreateIssuePanel(StrataComposerChip issue)
    {
        var panel = new StackPanel { Name = "ConnectorIssue", Spacing = 7 };

        var warningIcon = new PathIcon
        {
            Data = StreamGeometry.Parse(WarningIconData),
            Width = 11,
            Height = 11,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2.5, 6, 0),
            FlowDirection = FlowDirection.LeftToRight,
        };
        warningIcon.Classes.Add("connector-issue-icon");

        var summary = new TextBlock
        {
            Name = "McpErrorSummary",
            Text = issue.ErrorMessage,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
        };
        summary.Classes.Add("connector-issue-summary");
        ToolTip.SetTip(summary, issue.ErrorMessage);

        var summaryRow = new DockPanel();
        DockPanel.SetDock(warningIcon, Dock.Left);
        summaryRow.Children.Add(warningIcon);
        summaryRow.Children.Add(summary);
        panel.Children.Add(summaryRow);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };

        var actionLabel = ErrorActionLabel;
        var actionCommand = ErrorActionCommand;
        if (!string.IsNullOrWhiteSpace(actionLabel) && actionCommand is not null)
        {
            var fix = CreateIssueButton("McpErrorAction", RepairIconData, actionLabel, iconAfterLabel: false);
            fix.Classes.Add("primary");
            fix.IsEnabled = actionCommand.CanExecute(issue);
            AutomationProperties.SetName(fix, $"{actionLabel}: {issue.Name}");
            fix.Click += (_, _) => CloseAndExecute(actionCommand, issue);
            actions.Children.Add(fix);
        }

        Border? details = null;
        if (!string.IsNullOrWhiteSpace(issue.ErrorDetails))
        {
            var isExpanded = _expandedDetails.Contains(issue.Name);
            var toggle = CreateIssueButton("McpErrorDetailsToggle", ChevronIconData, ErrorDetailsLabel, iconAfterLabel: true);
            var chevron = (PathIcon)((StackPanel)toggle.Content!).Children[1];
            chevron.RenderTransformOrigin = RelativePoint.Center;
            chevron.RenderTransform = new RotateTransform(isExpanded ? 180 : 0);
            AutomationProperties.SetName(toggle, $"{ErrorDetailsLabel}: {issue.Name}");
            actions.Children.Add(toggle);

            details = CreateIssueDetails(issue);
            details.IsVisible = isExpanded;
            var capturedDetails = details;
            toggle.Click += (_, _) =>
            {
                var expand = !capturedDetails.IsVisible;
                capturedDetails.IsVisible = expand;
                chevron.RenderTransform = new RotateTransform(expand ? 180 : 0);
                if (expand)
                    _expandedDetails.Add(issue.Name);
                else
                    _expandedDetails.Remove(issue.Name);
            };
        }

        if (actions.Children.Count > 0)
            panel.Children.Add(actions);
        if (details is not null)
            panel.Children.Add(details);

        return panel;
    }

    private Border CreateIssueDetails(StrataComposerChip issue)
    {
        var text = new SelectableTextBlock
        {
            Name = "McpErrorDetailsText",
            Text = issue.ErrorDetails,
            TextWrapping = TextWrapping.Wrap,
            // Technical output reads left to right whatever the interface language.
            FlowDirection = FlowDirection.LeftToRight,
        };
        text.Classes.Add("connector-issue-details-text");

        var scroller = new ScrollViewer
        {
            MaxHeight = 110,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = text,
        };

        var copyIcon = new PathIcon { Data = StreamGeometry.Parse(CopyIconData), Width = 11, Height = 11 };
        var copy = new Button
        {
            Name = "McpCopyErrorDetails",
            Content = copyIcon,
            Padding = new Thickness(5),
            MinWidth = 0,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 0, 0, 0),
        };
        copy.Classes.Add("subtle");
        copy.Classes.Add("connector-issue-copy");
        ToolTip.SetTip(copy, CopyErrorDetailsLabel);
        AutomationProperties.SetName(copy, $"{CopyErrorDetailsLabel}: {issue.Name}");
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(copy)?.Clipboard;
            if (clipboard is null || string.IsNullOrEmpty(issue.ErrorDetails))
                return;

            try
            {
                await clipboard.SetTextAsync(issue.ErrorDetails);
            }
            catch
            {
                // Clipboard access can fail transiently; the details stay selectable.
                return;
            }

            copyIcon.Data = StreamGeometry.Parse(CopiedIconData);
            DispatcherTimer.RunOnce(
                () => copyIcon.Data = StreamGeometry.Parse(CopyIconData),
                TimeSpan.FromSeconds(1.2));
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(copy, 1);
        grid.Children.Add(scroller);
        grid.Children.Add(copy);

        var details = new Border { Name = "McpErrorDetails", Child = grid };
        details.Classes.Add("connector-issue-details");
        return details;
    }

    private static Button CreateIssueButton(string name, string iconData, string label, bool iconAfterLabel)
    {
        var icon = new PathIcon { Data = StreamGeometry.Parse(iconData), Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (iconAfterLabel)
        {
            content.Children.Add(text);
            content.Children.Add(icon);
        }
        else
        {
            content.Children.Add(icon);
            content.Children.Add(text);
        }

        var button = new Button { Name = name, Content = content };
        button.Classes.Add("subtle");
        button.Classes.Add("connector-issue-action");
        return button;
    }

    private sealed class ConnectorRow(
        string name,
        Border root,
        Ellipse attentionDot,
        TextBlock nameText,
        TextBlock caption,
        StackPanel extras,
        ToggleButton toggle)
    {
        public string Name { get; } = name;
        public Border Root { get; } = root;
        public Ellipse AttentionDot { get; } = attentionDot;
        public TextBlock NameText { get; } = nameText;
        public TextBlock Caption { get; } = caption;
        public StackPanel Extras { get; } = extras;
        public ToggleButton Switch { get; } = toggle;
        public StrataComposerChip Available { get; set; } = new(name);
        public string? ExtrasKey { get; set; }
    }
}
