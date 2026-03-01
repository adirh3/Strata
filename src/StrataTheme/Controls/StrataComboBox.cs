using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StrataTheme.Controls;

/// <summary>
/// Specifies the selection behavior of <see cref="StrataComboBox"/>.
/// </summary>
public enum StrataComboBoxSelectionMode
{
    /// <summary>Only one item can be selected at a time (standard combo box).</summary>
    Single,

    /// <summary>Multiple items can be selected via check boxes.</summary>
    Multiple,
}

/// <summary>
/// An enhanced combo box that supports single-select, multi-select with check boxes,
/// and an optional search/filter text box for quickly finding items in long lists.
/// </summary>
/// <remarks>
/// <para><b>Template parts:</b> PART_Popup (Popup), PART_ItemsPresenter (ItemsControl),
/// PART_SearchBox (TextBox), PART_ToggleButton (Border), PART_DisplayText (TextBlock).</para>
/// <para><b>Pseudo-classes:</b> :open, :multiselect, :searchable, :has-selection.</para>
/// </remarks>
public class StrataComboBox : TemplatedControl
{
    // ──────────────────────── Styled Properties ────────────────────────

    /// <summary>The source collection of items to display.</summary>
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<StrataComboBox, IEnumerable?>(nameof(ItemsSource));

    /// <summary>Whether the dropdown popup is open.</summary>
    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<StrataComboBox, bool>(nameof(IsDropDownOpen));

    /// <summary>The selection mode: Single or Multiple.</summary>
    public static readonly StyledProperty<StrataComboBoxSelectionMode> SelectionModeProperty =
        AvaloniaProperty.Register<StrataComboBox, StrataComboBoxSelectionMode>(nameof(SelectionMode));

    /// <summary>Whether to show the search/filter text box in the popup.</summary>
    public static readonly StyledProperty<bool> IsSearchableProperty =
        AvaloniaProperty.Register<StrataComboBox, bool>(nameof(IsSearchable));

    /// <summary>Placeholder text shown when no item is selected.</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<StrataComboBox, string?>(nameof(PlaceholderText));

    /// <summary>Watermark text for the search text box.</summary>
    public static readonly StyledProperty<string?> SearchWatermarkProperty =
        AvaloniaProperty.Register<StrataComboBox, string?>(nameof(SearchWatermark), "Search...");

    /// <summary>The currently selected item (single-select mode).</summary>
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<StrataComboBox, object?>(nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The currently selected items (multi-select mode). Read-only.</summary>
    public static readonly DirectProperty<StrataComboBox, IList> SelectedItemsProperty =
        AvaloniaProperty.RegisterDirect<StrataComboBox, IList>(
            nameof(SelectedItems), o => o.SelectedItems);

    /// <summary>Whether to show a "Select All" option in multi-select mode.</summary>
    public static readonly StyledProperty<bool> ShowSelectAllProperty =
        AvaloniaProperty.Register<StrataComboBox, bool>(nameof(ShowSelectAll), true);

    /// <summary>Text displayed for the select-all option.</summary>
    public static readonly StyledProperty<string?> SelectAllTextProperty =
        AvaloniaProperty.Register<StrataComboBox, string?>(nameof(SelectAllText), "Select all");

    /// <summary>Maximum height of the dropdown popup.</summary>
    public static readonly StyledProperty<double> MaxDropDownHeightProperty =
        AvaloniaProperty.Register<StrataComboBox, double>(nameof(MaxDropDownHeight), 420);

    /// <summary>The path of the member to display. When set, ToString() of that property is used.</summary>
    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<StrataComboBox, string?>(nameof(DisplayMemberPath));

    // ──────────────────────── Routed Events ────────────────────────

    /// <summary>Raised when the selection changes.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> SelectionChangedEvent =
        RoutedEvent.Register<StrataComboBox, RoutedEventArgs>(
            nameof(SelectionChanged), RoutingStrategies.Bubble);

    // ──────────────────────── Fields ────────────────────────

    private Popup? _popup;
    private TextBox? _searchBox;
    private StackPanel? _itemsHost;
    private Border? _toggleButton;
    private Border? _selectAllRow;
    private Border? _selectAllSeparator;
    private StrataComboBoxItem? _selectAllItem;
    private TextBlock? _displayTextBlock;
    private readonly AvaloniaList<object> _selectedItems = new();
    private string _searchText = string.Empty;

    // ──────────────────────── Static Constructor ────────────────────────

    static StrataComboBox()
    {
        ItemsSourceProperty.Changed.AddClassHandler<StrataComboBox>((c, _) => c.OnItemsSourceChanged());
        IsDropDownOpenProperty.Changed.AddClassHandler<StrataComboBox>((c, _) => c.OnDropDownOpenChanged());
        SelectionModeProperty.Changed.AddClassHandler<StrataComboBox>((c, e) =>
        {
            c.UpdatePseudoClasses();
            c.UpdateSelectAllVisibility();
        });
        IsSearchableProperty.Changed.AddClassHandler<StrataComboBox>((c, _) => c.UpdatePseudoClasses());
        SelectedItemProperty.Changed.AddClassHandler<StrataComboBox>((c, _) => c.OnSelectedItemChanged());
        ShowSelectAllProperty.Changed.AddClassHandler<StrataComboBox>((c, _) => c.UpdateSelectAllVisibility());
        FocusableProperty.OverrideDefaultValue<StrataComboBox>(true);
    }

    public StrataComboBox()
    {
        _selectedItems.CollectionChanged += OnSelectedItemsCollectionChanged;
    }

    // ──────────────────────── CLR Properties ────────────────────────

    /// <inheritdoc cref="ItemsSourceProperty"/>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <inheritdoc cref="IsDropDownOpenProperty"/>
    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <inheritdoc cref="SelectionModeProperty"/>
    public StrataComboBoxSelectionMode SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    /// <inheritdoc cref="IsSearchableProperty"/>
    public bool IsSearchable
    {
        get => GetValue(IsSearchableProperty);
        set => SetValue(IsSearchableProperty, value);
    }

    /// <inheritdoc cref="PlaceholderTextProperty"/>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <inheritdoc cref="SearchWatermarkProperty"/>
    public string? SearchWatermark
    {
        get => GetValue(SearchWatermarkProperty);
        set => SetValue(SearchWatermarkProperty, value);
    }

    /// <inheritdoc cref="SelectedItemProperty"/>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <inheritdoc cref="SelectedItemsProperty"/>
    public IList SelectedItems => _selectedItems;

    /// <inheritdoc cref="ShowSelectAllProperty"/>
    public bool ShowSelectAll
    {
        get => GetValue(ShowSelectAllProperty);
        set => SetValue(ShowSelectAllProperty, value);
    }

    /// <inheritdoc cref="SelectAllTextProperty"/>
    public string? SelectAllText
    {
        get => GetValue(SelectAllTextProperty);
        set => SetValue(SelectAllTextProperty, value);
    }

    /// <inheritdoc cref="MaxDropDownHeightProperty"/>
    public double MaxDropDownHeight
    {
        get => GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <inheritdoc cref="DisplayMemberPathProperty"/>
    public string? DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <inheritdoc cref="SelectionChangedEvent"/>
    public event EventHandler<RoutedEventArgs>? SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    // ──────────────────────── Template ────────────────────────

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        // Unhook old
        if (_toggleButton is not null)
            _toggleButton.PointerPressed -= OnTogglePressed;
        if (_searchBox is not null)
            _searchBox.TextChanged -= OnSearchTextChanged;

        base.OnApplyTemplate(e);

        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _searchBox = e.NameScope.Find<TextBox>("PART_SearchBox");
        _itemsHost = e.NameScope.Find<StackPanel>("PART_ItemsHost");
        _toggleButton = e.NameScope.Find<Border>("PART_ToggleButton");
        _selectAllRow = e.NameScope.Find<Border>("PART_SelectAllRow");
        _selectAllSeparator = e.NameScope.Find<Border>("PART_SelectAllSeparator");
        _selectAllItem = e.NameScope.Find<StrataComboBoxItem>("PART_SelectAllItem");
        _displayTextBlock = e.NameScope.Find<TextBlock>("PART_DisplayText");

        if (_toggleButton is not null)
            _toggleButton.PointerPressed += OnTogglePressed;
        if (_searchBox is not null)
            _searchBox.TextChanged += OnSearchTextChanged;
        if (_selectAllItem is not null)
            _selectAllItem.Owner = this;

        OnItemsSourceChanged();
        UpdatePseudoClasses();
        UpdateDisplayText();
        UpdateSelectAllVisibility();
    }

    // ──────────────────────── Interaction ────────────────────────

    private void OnTogglePressed(object? sender, PointerPressedEventArgs e)
    {
        IsDropDownOpen = !IsDropDownOpen;
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsDropDownOpen)
        {
            IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                if (!IsDropDownOpen)
                {
                    IsDropDownOpen = true;
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                if (IsDropDownOpen)
                {
                    IsDropDownOpen = false;
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (!IsDropDownOpen)
                {
                    IsDropDownOpen = true;
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>Called when an item in the popup is clicked.</summary>
    internal void OnItemClicked(object item)
    {
        if (SelectionMode == StrataComboBoxSelectionMode.Single)
        {
            SelectedItem = item;
            IsDropDownOpen = false;
        }
        else
        {
            // Check if this is the Select All sentinel
            if (ReferenceEquals(item, _selectAllSentinel))
            {
                ToggleSelectAll();
                return;
            }

            // Toggle selection
            if (_selectedItems.Contains(item))
                _selectedItems.Remove(item);
            else
                _selectedItems.Add(item);

            // Update check state on visible containers
            SyncItemContainerChecks();
            UpdateSelectAllCheckState();

            RaisePropertyChanged(SelectedItemsProperty, _selectedItems, _selectedItems);
            RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
            UpdateDisplayText();
            UpdatePseudoClasses();
        }
    }

    private void ToggleSelectAll()
    {
        var allItems = GetAllSourceItems();
        bool allSelected = allItems.Count > 0 && allItems.All(i => _selectedItems.Contains(i));

        _selectedItems.Clear();
        if (!allSelected)
        {
            foreach (var item in allItems)
                _selectedItems.Add(item);
        }

        SyncItemContainerChecks();
        UpdateSelectAllCheckState();

        RaisePropertyChanged(SelectedItemsProperty, _selectedItems, _selectedItems);
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
        UpdateDisplayText();
        UpdatePseudoClasses();
    }

    private System.Collections.Generic.List<object> GetAllSourceItems()
    {
        var result = new System.Collections.Generic.List<object>();
        if (ItemsSource is null) return result;
        foreach (var item in ItemsSource)
        {
            if (item is not null)
                result.Add(item);
        }
        return result;
    }

    private void SyncItemContainerChecks()
    {
        if (_itemsHost is null) return;
        foreach (var child in _itemsHost.Children)
        {
            if (child is StrataComboBoxItem container && container.Item is not null)
                container.IsItemSelected = _selectedItems.Contains(container.Item);
        }
    }

    private void UpdateSelectAllCheckState()
    {
        if (_selectAllItem is null) return;
        var allItems = GetAllSourceItems();
        _selectAllItem.IsItemSelected = allItems.Count > 0 && allItems.All(i => _selectedItems.Contains(i));
    }

    private void UpdateSelectAllVisibility()
    {
        bool show = SelectionMode == StrataComboBoxSelectionMode.Multiple && ShowSelectAll;
        if (_selectAllRow is not null)
            _selectAllRow.IsVisible = show;
        if (_selectAllSeparator is not null)
            _selectAllSeparator.IsVisible = show;
    }

    private static readonly object _selectAllSentinel = new();

    /// <summary>Returns whether an item is currently selected (for multi-select check state).</summary>
    internal bool IsItemSelected(object item) => _selectedItems.Contains(item);

    // ──────────────────────── Search / Filter ────────────────────────

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchText = _searchBox?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_itemsHost is null) return;

        _itemsHost.Children.Clear();

        if (ItemsSource is null)
            return;

        var isMulti = SelectionMode == StrataComboBoxSelectionMode.Multiple;

        foreach (var item in ItemsSource)
        {
            if (item is null) continue;

            var text = GetDisplayText(item);

            if (!string.IsNullOrEmpty(_searchText) &&
                !text.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                continue;

            var container = new StrataComboBoxItem
            {
                Item = item,
                Text = text,
                IsMultiSelect = isMulti,
                IsItemSelected = isMulti && _selectedItems.Contains(item),
                Owner = this,
            };

            _itemsHost.Children.Add(container);
        }
    }

    // ──────────────────────── Data ────────────────────────

    private void OnItemsSourceChanged()
    {
        _searchText = string.Empty;
        if (_searchBox is not null)
            _searchBox.Text = string.Empty;
        ApplyFilter();
    }

    private void OnSelectedItemChanged()
    {
        UpdateDisplayText();
        UpdatePseudoClasses();
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateDisplayText();
        UpdatePseudoClasses();
    }

    private void OnDropDownOpenChanged()
    {
        PseudoClasses.Set(":open", IsDropDownOpen);

        if (IsDropDownOpen)
        {
            _searchText = string.Empty;
            if (_searchBox is not null)
            {
                _searchBox.Text = string.Empty;
                if (IsSearchable)
                    _searchBox.Focus();
            }

            // Configure select-all item
            if (_selectAllItem is not null)
            {
                _selectAllItem.Item = _selectAllSentinel;
                _selectAllItem.Text = SelectAllText ?? "Select all";
            }
            UpdateSelectAllVisibility();

            ApplyFilter();
            UpdateSelectAllCheckState();
        }
    }

    private void UpdateDisplayText()
    {
        if (_displayTextBlock is null) return;

        string text;
        if (SelectionMode == StrataComboBoxSelectionMode.Multiple)
        {
            text = _selectedItems.Count == 0
                ? string.Empty
                : string.Join(", ", _selectedItems.Select(GetDisplayText));
        }
        else
        {
            text = SelectedItem is not null ? GetDisplayText(SelectedItem) : string.Empty;
        }

        _displayTextBlock.Text = text;
    }

    // ──────────────────────── Pseudo-classes ────────────────────────

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":multiselect", SelectionMode == StrataComboBoxSelectionMode.Multiple);
        PseudoClasses.Set(":searchable", IsSearchable);
        PseudoClasses.Set(":open", IsDropDownOpen);

        bool hasSelection = SelectionMode == StrataComboBoxSelectionMode.Multiple
            ? _selectedItems.Count > 0
            : SelectedItem is not null;
        PseudoClasses.Set(":has-selection", hasSelection);
    }

    // ──────────────────────── Display helpers ────────────────────────

    internal string GetDisplayText(object item)
    {
        if (!string.IsNullOrEmpty(DisplayMemberPath))
        {
            var prop = item.GetType().GetProperty(DisplayMemberPath);
            if (prop is not null)
                return prop.GetValue(item)?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }
}

/// <summary>
/// Container for items inside a <see cref="StrataComboBox"/> popup.
/// Handles click behavior and multi-select check-box rendering.
/// </summary>
public class StrataComboBoxItem : TemplatedControl
{
    /// <summary>The data item this container represents.</summary>
    public static readonly StyledProperty<object?> ItemProperty =
        AvaloniaProperty.Register<StrataComboBoxItem, object?>(nameof(Item));

    /// <summary>The display text for this item.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StrataComboBoxItem, string?>(nameof(Text));

    /// <summary>Whether the item is selected (for multi-select check visuals).</summary>
    public static readonly StyledProperty<bool> IsItemSelectedProperty =
        AvaloniaProperty.Register<StrataComboBoxItem, bool>(nameof(IsItemSelected));

    /// <summary>Whether the parent is in multi-select mode.</summary>
    public static readonly StyledProperty<bool> IsMultiSelectProperty =
        AvaloniaProperty.Register<StrataComboBoxItem, bool>(nameof(IsMultiSelect));

    /// <summary>Reference to the owning StrataComboBox (set during container creation).</summary>
    internal StrataComboBox? Owner { get; set; }

    static StrataComboBoxItem()
    {
        FocusableProperty.OverrideDefaultValue<StrataComboBoxItem>(true);
        IsItemSelectedProperty.Changed.AddClassHandler<StrataComboBoxItem>(
            (c, _) => c.PseudoClasses.Set(":selected", c.IsItemSelected));
    }

    /// <inheritdoc cref="ItemProperty"/>
    public object? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <inheritdoc cref="TextProperty"/>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc cref="IsItemSelectedProperty"/>
    public bool IsItemSelected
    {
        get => GetValue(IsItemSelectedProperty);
        set => SetValue(IsItemSelectedProperty, value);
    }

    /// <inheritdoc cref="IsMultiSelectProperty"/>
    public bool IsMultiSelect
    {
        get => GetValue(IsMultiSelectProperty);
        set => SetValue(IsMultiSelectProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Item is not null)
            Owner?.OnItemClicked(Item);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Enter or Key.Space && Item is not null)
        {
            Owner?.OnItemClicked(Item);
            e.Handled = true;
        }
    }
}
