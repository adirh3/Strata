using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Windows.Input;

namespace StrataTheme.Controls;

/// <summary>
/// Searchable, virtualized model picker with grouped providers and per-model reasoning/context controls.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataModelPicker Models="{Binding Models}"
///                             SelectedModel="{Binding SelectedModel, Mode=TwoWay}"
///                             QualityLevels="{Binding QualityLevels}"
///                             SelectedQuality="{Binding SelectedQuality, Mode=TwoWay}"
///                             ContextWindowTiers="{Binding ContextWindowTiers}"
///                             SelectedContextWindowTier="{Binding SelectedContextWindowTier, Mode=TwoWay}" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_ModelPickerButton (Button), PART_ModelPickerPopup (Popup),
/// PART_ModelPickerList (ListBox), PART_ModelPickerSearch (TextBox), PART_ModelPickerPanel (Border),
/// PART_ModelPickerBackdrop (Border), PART_EffortSection (StackPanel), PART_ContextWindowSection (StackPanel),
/// PART_ModelPickerNote (Border, shown while <see cref="SelectedModelNote"/> is set).</para>
/// <para><b>Pseudo-classes:</b> :has-models, :has-quality, :has-context-window, :model-picker-open,
/// :no-model-results, :can-filter-pinned, :has-model-settings.</para>
/// <para><b>Rich items:</b> items implementing <see cref="IStrataModelOption"/> are identified by
/// <see cref="IStrataModelOption.ModelId"/> instead of <c>ToString()</c>, are listed pinned-first, and
/// get a pin affordance when <see cref="ModelPinCommand"/> is set. <see cref="SelectedModel"/> still
/// receives the plain id, so the host's selection property does not have to change shape.</para>
/// </remarks>
public class StrataModelPicker : TemplatedControl
{
    private Button? _modelPickerButton;
    private Popup? _modelPickerPopup;
    private ListBox? _modelPickerList;
    private TextBox? _modelPickerSearch;
    private Border? _modelPickerPanel;
    private Border? _modelPickerChevronWrap;
    private StackPanel? _effortSection;
    private StackPanel? _contextWindowSection;
    private INotifyCollectionChanged? _observedModels;
    private readonly HashSet<INotifyPropertyChanged> _observedModelItems = new(ReferenceEqualityComparer.Instance);
    private bool _isAttachedToVisualTree;
    private IDataTemplate? _effectiveSelectedModelTemplate;
    private bool _catalogDirty = true;
    private bool _filterDirty = true;
    private bool _modelItemsDirty;
    private bool _rowsRefreshQueued;
    private bool _sectionsRefreshQueued;
    private bool _scrollRefreshQueued;
    private bool _resetNavigation;
    private int _openingVersion;
    private List<ModelPickerRowData> _catalogRows = [];
    private IReadOnlyList<ModelPickerRowData> _visibleRows = [];
    private int _filteredModelCount;
    private static readonly IDataTemplate PlainModelItemTemplate = new FuncDataTemplate<string>((_, _) =>
    {
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        name.Classes.Add("model-name");
        name.Bind(TextBlock.TextProperty, new Binding());
        return name;
    }, supportsRecycling: true);

    /// <summary>Synthetic group the pinned section is listed under, ahead of every provider group.</summary>
    private const string PinnedGroup = "\u0000pinned";
    private const string ProviderGroupPrefix = "\u0000provider:";

    public static readonly StyledProperty<IEnumerable?> ModelsProperty =
        AvaloniaProperty.Register<StrataModelPicker, IEnumerable?>(nameof(Models));

    public static readonly StyledProperty<object?> SelectedModelProperty =
        AvaloniaProperty.Register<StrataModelPicker, object?>(nameof(SelectedModel));

    public static readonly StyledProperty<IDataTemplate?> ModelItemTemplateProperty =
        AvaloniaProperty.Register<StrataModelPicker, IDataTemplate?>(nameof(ModelItemTemplate));

    /// <summary>
    /// Optional template for the collapsed picker button. Falls back to <see cref="ModelItemTemplate"/>
    /// when unset. A rich row template usually cannot render inside the button (it is a fraction of
    /// the popup's width), and the button's content is the selected <em>value</em> rather than the
    /// item, so hosts that bind rich items need a separate, value-shaped template here.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> SelectedModelTemplateProperty =
        AvaloniaProperty.Register<StrataModelPicker, IDataTemplate?>(nameof(SelectedModelTemplate));

    /// <summary>
    /// Template actually used by the collapsed picker button: <see cref="SelectedModelTemplate"/> when
    /// set, otherwise <see cref="ModelItemTemplate"/>. Exists because a template binding cannot express
    /// that fallback.
    /// </summary>
    public static readonly DirectProperty<StrataModelPicker, IDataTemplate?> EffectiveSelectedModelTemplateProperty =
        AvaloniaProperty.RegisterDirect<StrataModelPicker, IDataTemplate?>(
            nameof(EffectiveSelectedModelTemplate),
            picker => picker.EffectiveSelectedModelTemplate);

    /// <summary>
    /// Invoked with the row's item when the user activates its pin affordance. The pin button is only
    /// rendered for items implementing <see cref="IStrataModelOption"/> and only when this is set, so
    /// plain string model lists keep their original rows.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ModelPinCommandProperty =
        AvaloniaProperty.Register<StrataModelPicker, ICommand?>(nameof(ModelPinCommand));

    /// <summary>Tooltip shown on an unpinned row's pin button. Host-supplied so it can be localized.</summary>
    public static readonly StyledProperty<string?> PinToolTipProperty =
        AvaloniaProperty.Register<StrataModelPicker, string?>(nameof(PinToolTip));

    /// <summary>Tooltip shown on a pinned row's pin button.</summary>
    public static readonly StyledProperty<string?> UnpinToolTipProperty =
        AvaloniaProperty.Register<StrataModelPicker, string?>(nameof(UnpinToolTip));

    public static readonly StyledProperty<IEnumerable?> QualityLevelsProperty =
        AvaloniaProperty.Register<StrataModelPicker, IEnumerable?>(nameof(QualityLevels));

    public static readonly StyledProperty<object?> SelectedQualityProperty =
        AvaloniaProperty.Register<StrataModelPicker, object?>(nameof(SelectedQuality));

    public static readonly StyledProperty<IEnumerable?> ContextWindowTiersProperty =
        AvaloniaProperty.Register<StrataModelPicker, IEnumerable?>(nameof(ContextWindowTiers));

    public static readonly StyledProperty<object?> SelectedContextWindowTierProperty =
        AvaloniaProperty.Register<StrataModelPicker, object?>(nameof(SelectedContextWindowTier));

    /// <summary>
    /// Command executed each time the picker popup opens, before the user makes a choice. Hosts can
    /// use it to lazily refresh <see cref="Models"/> — updating the collection while the popup is
    /// open rebuilds the rows in place.
    /// </summary>
    public static readonly StyledProperty<ICommand?> PickerOpenedCommandProperty =
        AvaloniaProperty.Register<StrataModelPicker, ICommand?>(nameof(PickerOpenedCommand));

    /// <summary>Optional parameter for <see cref="PickerOpenedCommand"/>.</summary>
    public static readonly StyledProperty<object?> PickerOpenedCommandParameterProperty =
        AvaloniaProperty.Register<StrataModelPicker, object?>(nameof(PickerOpenedCommandParameter));

    /// <summary>
    /// Optional note about the selected model, shown along the bottom of the open picker (for example,
    /// which plan or account it uses). The note row is hidden while this is null or empty.
    /// </summary>
    public static readonly StyledProperty<string?> SelectedModelNoteProperty =
        AvaloniaProperty.Register<StrataModelPicker, string?>(nameof(SelectedModelNote));

    /// <summary>Label of the link beside <see cref="SelectedModelNote"/>, such as "Manage usage".</summary>
    public static readonly StyledProperty<string?> SelectedModelNoteActionLabelProperty =
        AvaloniaProperty.Register<StrataModelPicker, string?>(nameof(SelectedModelNoteActionLabel));

    /// <summary>Command run by the note's link. The link is hidden while this is null.</summary>
    public static readonly StyledProperty<ICommand?> SelectedModelNoteCommandProperty =
        AvaloniaProperty.Register<StrataModelPicker, ICommand?>(nameof(SelectedModelNoteCommand));

    /// <summary>Current search query. Search matches all words against names, ids and providers.</summary>
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(SearchText), string.Empty);

    /// <summary>Whether only pinned models are shown.</summary>
    public static readonly StyledProperty<bool> ShowPinnedOnlyProperty =
        AvaloniaProperty.Register<StrataModelPicker, bool>(nameof(ShowPinnedOnly));

    /// <summary>Localized placeholder for the search field.</summary>
    public static readonly StyledProperty<string> SearchWatermarkProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(SearchWatermark), "Search models...");

    /// <summary>Localized empty-result message.</summary>
    public static readonly StyledProperty<string> NoResultsTextProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(NoResultsText), "No models found");

    /// <summary>Localized heading for the catalog.</summary>
    public static readonly StyledProperty<string> ModelsLabelProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(ModelsLabel), "Models");

    /// <summary>Localized label for the pinned section and filter.</summary>
    public static readonly StyledProperty<string> PinnedLabelProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(PinnedLabel), "Pinned");

    /// <summary>Localized label beside the reasoning effort segments.</summary>
    public static readonly StyledProperty<string> ReasoningLabelProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(ReasoningLabel), "Reasoning");

    /// <summary>Localized label beside the context-window segments.</summary>
    public static readonly StyledProperty<string> ContextLabelProperty =
        AvaloniaProperty.Register<StrataModelPicker, string>(nameof(ContextLabel), "Context");

    /// <summary>Number of models matching the current query and pin filter.</summary>
    public static readonly DirectProperty<StrataModelPicker, int> FilteredModelCountProperty =
        AvaloniaProperty.RegisterDirect<StrataModelPicker, int>(
            nameof(FilteredModelCount), picker => picker.FilteredModelCount);

    /// <summary>Template-owned row visual retained when the framework recycles a list container.</summary>
    public static readonly AttachedProperty<Control?> RowContentProperty =
        AvaloniaProperty.RegisterAttached<StrataModelPicker, ListBoxItem, Control?>("RowContent");

    public static Control? GetRowContent(ListBoxItem container) => container.GetValue(RowContentProperty);

    public static void SetRowContent(ListBoxItem container, Control? content) => container.SetValue(RowContentProperty, content);

    static StrataModelPicker()
    {
        ModelsProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.ObserveModelsCollection();
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.InvalidateModelRows();
        });
        QualityLevelsProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.RefreshModelPickerEffortIfOpen();
        });
        SelectedModelProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RefreshModelPickerSelectionIfOpen());
        ModelItemTemplateProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.UpdateEffectiveSelectedModelTemplate();
            picker.UpdateRealizedModelRows();
        });
        SelectedModelTemplateProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.UpdateEffectiveSelectedModelTemplate());
        ModelPinCommandProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.UpdateRealizedModelRows();
            picker.InvalidateModelRows();
        });
        PinToolTipProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.UpdateRealizedModelRows());
        UnpinToolTipProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.UpdateRealizedModelRows());
        SearchTextProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.InvalidateModelFilter());
        ShowPinnedOnlyProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.InvalidateModelFilter());
        PinnedLabelProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.InvalidateModelFilter());
        ReasoningLabelProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RequestSectionsRefresh());
        ContextLabelProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RequestSectionsRefresh());
        SelectedQualityProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RefreshModelPickerQualityIfOpen());
        ContextWindowTiersProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.RefreshModelPickerContextIfOpen();
        });
        SelectedContextWindowTierProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RefreshModelPickerContextSelectionIfOpen());
    }

    public IEnumerable? Models
    {
        get => GetValue(ModelsProperty);
        set => SetValue(ModelsProperty, value);
    }

    public object? SelectedModel
    {
        get => GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
    }

    public IDataTemplate? ModelItemTemplate
    {
        get => GetValue(ModelItemTemplateProperty);
        set => SetValue(ModelItemTemplateProperty, value);
    }

    public IDataTemplate? SelectedModelTemplate
    {
        get => GetValue(SelectedModelTemplateProperty);
        set => SetValue(SelectedModelTemplateProperty, value);
    }

    public IDataTemplate? EffectiveSelectedModelTemplate
    {
        get => _effectiveSelectedModelTemplate;
        private set => SetAndRaise(EffectiveSelectedModelTemplateProperty, ref _effectiveSelectedModelTemplate, value);
    }

    public ICommand? ModelPinCommand
    {
        get => GetValue(ModelPinCommandProperty);
        set => SetValue(ModelPinCommandProperty, value);
    }

    public string? PinToolTip
    {
        get => GetValue(PinToolTipProperty);
        set => SetValue(PinToolTipProperty, value);
    }

    public string? UnpinToolTip
    {
        get => GetValue(UnpinToolTipProperty);
        set => SetValue(UnpinToolTipProperty, value);
    }

    public IEnumerable? QualityLevels
    {
        get => GetValue(QualityLevelsProperty);
        set => SetValue(QualityLevelsProperty, value);
    }

    public object? SelectedQuality
    {
        get => GetValue(SelectedQualityProperty);
        set => SetValue(SelectedQualityProperty, value);
    }

    public IEnumerable? ContextWindowTiers
    {
        get => GetValue(ContextWindowTiersProperty);
        set => SetValue(ContextWindowTiersProperty, value);
    }

    public object? SelectedContextWindowTier
    {
        get => GetValue(SelectedContextWindowTierProperty);
        set => SetValue(SelectedContextWindowTierProperty, value);
    }

    public ICommand? PickerOpenedCommand
    {
        get => GetValue(PickerOpenedCommandProperty);
        set => SetValue(PickerOpenedCommandProperty, value);
    }

    public object? PickerOpenedCommandParameter
    {
        get => GetValue(PickerOpenedCommandParameterProperty);
        set => SetValue(PickerOpenedCommandParameterProperty, value);
    }

    public string? SelectedModelNote
    {
        get => GetValue(SelectedModelNoteProperty);
        set => SetValue(SelectedModelNoteProperty, value);
    }

    public string? SelectedModelNoteActionLabel
    {
        get => GetValue(SelectedModelNoteActionLabelProperty);
        set => SetValue(SelectedModelNoteActionLabelProperty, value);
    }

    public ICommand? SelectedModelNoteCommand
    {
        get => GetValue(SelectedModelNoteCommandProperty);
        set => SetValue(SelectedModelNoteCommandProperty, value);
    }

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool ShowPinnedOnly
    {
        get => GetValue(ShowPinnedOnlyProperty);
        set => SetValue(ShowPinnedOnlyProperty, value);
    }

    public string SearchWatermark
    {
        get => GetValue(SearchWatermarkProperty);
        set => SetValue(SearchWatermarkProperty, value);
    }

    public string NoResultsText
    {
        get => GetValue(NoResultsTextProperty);
        set => SetValue(NoResultsTextProperty, value);
    }

    public string ModelsLabel
    {
        get => GetValue(ModelsLabelProperty);
        set => SetValue(ModelsLabelProperty, value);
    }

    public string PinnedLabel
    {
        get => GetValue(PinnedLabelProperty);
        set => SetValue(PinnedLabelProperty, value);
    }

    public string ReasoningLabel
    {
        get => GetValue(ReasoningLabelProperty);
        set => SetValue(ReasoningLabelProperty, value);
    }

    public string ContextLabel
    {
        get => GetValue(ContextLabelProperty);
        set => SetValue(ContextLabelProperty, value);
    }

    public int FilteredModelCount
    {
        get => _filteredModelCount;
        private set => SetAndRaise(FilteredModelCountProperty, ref _filteredModelCount, value);
    }

    public StrataModelPicker()
    {
        UpdateEffectiveSelectedModelTemplate();
        EnsureSelectedValues();
        Sync();
    }

    private void UpdateEffectiveSelectedModelTemplate()
        => EffectiveSelectedModelTemplate = SelectedModelTemplate ?? ModelItemTemplate;

    /// <summary>
    /// Identity of a model item: <see cref="IStrataModelOption.ModelId"/> for rich items, the string
    /// form otherwise. Selection, grouping and version ranking all key off this, so a rich collection
    /// behaves exactly like the equivalent list of id strings.
    /// </summary>
    private static string ResolveModelId(object? model) => model switch
    {
        IStrataModelOption option => option.ModelId ?? string.Empty,
        null => string.Empty,
        _ => model.ToString() ?? string.Empty
    };

    /// <summary>The value written to <see cref="SelectedModel"/> when a row is chosen. Rich items
    /// resolve to their id so hosts can keep a plain string selection property.</summary>
    private static object? ResolveSelectionValue(object? model)
        => model is IStrataModelOption option ? option.ModelId : model;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        WireTemplateHandlers(false);
        base.OnApplyTemplate(e);

        _modelPickerButton = e.NameScope.Find<Button>("PART_ModelPickerButton");
        _modelPickerPopup = e.NameScope.Find<Popup>("PART_ModelPickerPopup");
        _modelPickerList = e.NameScope.Find<ListBox>("PART_ModelPickerList");
        _modelPickerSearch = e.NameScope.Find<TextBox>("PART_ModelPickerSearch");
        _modelPickerPanel = e.NameScope.Find<Border>("PART_ModelPickerPanel");
        _modelPickerChevronWrap = e.NameScope.Find<Border>("PART_ModelPickerChevronWrap");
        _effortSection = e.NameScope.Find<StackPanel>("PART_EffortSection");
        _contextWindowSection = e.NameScope.Find<StackPanel>("PART_ContextWindowSection");
        _effortSegments = null;
        _contextSegments = null;
        UpdateModelSettingsState();
        _visibleRows = [];
        _filterDirty = true;
        if (_modelPickerList is not null)
        {
            _modelPickerList.ItemTemplate = new ModelPickerItemTemplate(this);
            _modelPickerList.ContainerPrepared += OnModelContainerPrepared;
        }

        WireTemplateHandlers(true);
        EnsureSelectedValues();
        Sync();
    }

    private void WireTemplateHandlers(bool attach)
    {
        if (_modelPickerButton is not null)
        {
            _modelPickerButton.Click -= OnModelPickerButtonClick;
            if (attach)
                _modelPickerButton.Click += OnModelPickerButtonClick;
        }
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Closed -= OnModelPickerPopupClosed;
            if (attach)
            {
                _modelPickerPopup.Closed += OnModelPickerPopupClosed;
            }
        }
        if (_modelPickerPanel is not null)
        {
            _modelPickerPanel.RemoveHandler(KeyDownEvent, OnModelPickerKeyDown);
            if (attach)
                _modelPickerPanel.AddHandler(KeyDownEvent, OnModelPickerKeyDown, RoutingStrategies.Tunnel);
        }
        if (_modelPickerList is not null)
        {
            _modelPickerList.RemoveHandler(PointerMovedEvent, OnModelListPointerMoved);
            if (attach)
                _modelPickerList.AddHandler(PointerMovedEvent, OnModelListPointerMoved, RoutingStrategies.Bubble, true);
            else
                _modelPickerList.ContainerPrepared -= OnModelContainerPrepared;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        WireTemplateHandlers(true);
        if (_modelPickerList is not null)
        {
            _modelPickerList.ContainerPrepared -= OnModelContainerPrepared;
            _modelPickerList.ContainerPrepared += OnModelContainerPrepared;
        }
        ObserveModelsCollection();
        InvalidateModelRows();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        WireTemplateHandlers(false);
        _isAttachedToVisualTree = false;
        DetachModelsCollection();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Tracks content changes of the bound <see cref="Models"/> collection — not just replacement of
    /// the collection itself — so models added after the picker was created (e.g. a catalog refresh
    /// discovering a newly released model) appear immediately, even while the popup is open.
    /// </summary>
    private void ObserveModelsCollection()
    {
        var collection = Models as INotifyCollectionChanged;
        if (!ReferenceEquals(collection, _observedModels))
        {
            DetachModelsCollection();

            if (collection is not null && _isAttachedToVisualTree)
            {
                _observedModels = collection;
                collection.CollectionChanged += OnModelsCollectionChanged;
            }
        }

        ObserveModelItems();
    }

    private void DetachModelsCollection()
    {
        if (_observedModels is not null)
        {
            _observedModels.CollectionChanged -= OnModelsCollectionChanged;
            _observedModels = null;
        }

        DetachModelItems();
    }

    private void ObserveModelItems()
    {
        DetachModelItems();
        if (!_isAttachedToVisualTree || Models is null)
            return;

        foreach (var item in Models.OfType<INotifyPropertyChanged>())
        {
            if (!_observedModelItems.Add(item))
                continue;

            item.PropertyChanged += OnModelItemPropertyChanged;
        }
    }

    private void DetachModelItems()
    {
        foreach (var item in _observedModelItems)
            item.PropertyChanged -= OnModelItemPropertyChanged;
        _observedModelItems.Clear();
    }

    private void OnModelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _modelItemsDirty = true;
        EnsureSelectedValues();
        Sync();
        InvalidateModelRows();
    }

    private void OnModelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not IStrataModelOption
            || (!string.IsNullOrEmpty(e.PropertyName)
                && e.PropertyName != nameof(IStrataModelOption.IsPinned)
                && e.PropertyName != nameof(IStrataModelOption.ProviderName)
                && e.PropertyName != nameof(IStrataModelOption.DisplayName)))
        {
            return;
        }

        InvalidateModelRows();
    }

    private void OnModelPickerButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleModelPickerPopup();
    }

    private void OnModelPickerPopupClosed(object? sender, EventArgs e)
    {
        PseudoClasses.Set(":model-picker-open", false);
        AnimateChevron(false);
    }

    private void RaisePickerOpened()
    {
        var command = PickerOpenedCommand;
        if (command is null)
            return;

        var parameter = PickerOpenedCommandParameter;
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private void AnimateChevron(bool open)
    {
        if (_modelPickerChevronWrap is null)
            return;

        _modelPickerChevronWrap.RenderTransformOrigin = RelativePoint.Center;
        _modelPickerChevronWrap.RenderTransform = new RotateTransform(open ? 180 : 0);
    }

    private void ToggleModelPickerPopup()
    {
        if (_modelPickerPopup is null)
            return;

        if (_modelPickerPopup.IsOpen)
        {
            _modelPickerPopup.IsOpen = false;
            return;
        }

        SetCurrentValue(SearchTextProperty, string.Empty);
        SetCurrentValue(ShowPinnedOnlyProperty, false);
        // IEnumerable and non-notifying item implementations can change while closed.
        _catalogDirty = true;
        _filterDirty = true;
        // Every opening starts on the current model, wherever the pointer left the highlight.
        _resetNavigation = true;
        BuildModelPickerRows();
        // Rows kept from the last opening may still check a model that changed while closed.
        UpdateModelPickerSelectionVisuals(SelectedModel);
        RebuildEffortSection();
        RebuildContextWindowSection();
        _modelPickerPopup.IsOpen = true;
        PseudoClasses.Set(":model-picker-open", true);
        AnimateChevron(true);
        var openingVersion = ++_openingVersion;

        Dispatcher.UIThread.Post(() =>
        {
            if (_modelPickerPopup is not { IsOpen: true } || openingVersion != _openingVersion)
                return;
            _modelPickerSearch?.Focus();
            RaisePickerOpened();
        }, DispatcherPriority.Loaded);
    }

    private void InvalidateModelRows()
    {
        _catalogDirty = true;
        _filterDirty = true;
        RequestModelRowsRefresh();
    }

    private void InvalidateModelFilter()
    {
        _filterDirty = true;
        _resetNavigation = true;
        RequestModelRowsRefresh();
    }

    private void RequestModelRowsRefresh()
    {
        if (_rowsRefreshQueued || !_isAttachedToVisualTree)
            return;

        _rowsRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rowsRefreshQueued = false;
            if (!_isAttachedToVisualTree)
                return;
            if (_modelItemsDirty)
            {
                _modelItemsDirty = false;
                ObserveModelItems();
            }
            if (_modelPickerPopup is { IsOpen: true })
                BuildModelPickerRows();
        }, DispatcherPriority.Loaded);
    }

    private void RefreshModelPickerSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        UpdateModelPickerSelectionVisuals(SelectedModel);
        RequestSectionsRefresh();
    }

    private void RefreshModelPickerEffortIfOpen()
        => RequestSectionsRefresh();

    private void RequestSectionsRefresh()
    {
        if (_sectionsRefreshQueued || _modelPickerPopup is not { IsOpen: true })
            return;

        _sectionsRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _sectionsRefreshQueued = false;
            if (_modelPickerPopup is not { IsOpen: true })
                return;
            RebuildEffortSection();
            RebuildContextWindowSection();
        }, DispatcherPriority.Loaded);
    }

    private void RefreshModelPickerQualityIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        UpdateEffortActiveState();
    }

    private void RefreshModelPickerContextIfOpen()
        => RequestSectionsRefresh();

    private void RefreshModelPickerContextSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        UpdateContextWindowActiveState();
    }

    private void BuildModelPickerRows()
    {
        if (_modelPickerList is null)
            return;

        if (_catalogDirty)
        {
            _catalogDirty = false;
            _catalogRows = OrderCatalogRows(Models);
            PseudoClasses.Set(":can-filter-pinned",
                ModelPinCommand is not null || _catalogRows.Any(row => row.IsPinned));
        }

        if (!_filterDirty)
            return;
        _filterDirty = false;

        var terms = (SearchText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeSearchText)
            .Where(term => term.Length > 0)
            .ToArray();
        var visibleRows = new List<ModelPickerRowData>();
        var count = 0;
        string? lastGroup = null;
        foreach (var row in _catalogRows)
        {
            if ((ShowPinnedOnly && !row.IsPinned)
                || terms.Any(term => !row.SearchText.Contains(term, StringComparison.Ordinal)))
                continue;

            if (row.Group != lastGroup)
            {
                var label = row.Group == PinnedGroup ? PinnedLabel.ToUpperInvariant() : GetModelGroupLabel(row.Group);
                visibleRows.Add(new ModelPickerRowData(
                    null, label, row.Group, false, -1, string.Empty, IsHeader: true, IsFirstGroup: lastGroup is null));
                lastGroup = row.Group;
            }
            visibleRows.Add(row);
            count++;
        }

        FilteredModelCount = count;
        PseudoClasses.Set(":no-model-results", count == 0);
        var rowsChanged = !_visibleRows.SequenceEqual(visibleRows);
        if (rowsChanged || _resetNavigation)
        {
            var activeId = !_resetNavigation && _modelPickerList.SelectedItem is ModelPickerRowData active
                ? active.ModelName
                : terms.Length == 0 ? ResolveModelId(SelectedModel) : null;
            if (rowsChanged)
            {
                _visibleRows = visibleRows;
                _modelPickerList.ItemsSource = visibleRows;
            }
            _modelPickerList.SelectedItem = _visibleRows.FirstOrDefault(row => !row.IsHeader && row.ModelName == activeId)
                ?? _visibleRows.FirstOrDefault(row => !row.IsHeader);
            // Opening, searching and filtering move the view to the active row; a catalog or pin
            // change keeps the user's scroll position instead of jumping to wherever a row moved.
            if (_resetNavigation)
                RequestScrollActiveRowIntoView();
        }
        _resetNavigation = false;
    }

    /// <summary>
    /// The catalog in list order: pinned items first in their own order, then each provider in the
    /// order it first appears among the unpinned items, newest versions first within a provider.
    /// </summary>
    private static List<ModelPickerRowData> OrderCatalogRows(IEnumerable? models)
    {
        var rows = (models?.Cast<object?>() ?? [])
            .Select((model, index) =>
            {
                var modelId = ResolveModelId(model);
                var option = model as IStrataModelOption;
                var isPinned = option?.IsPinned == true;
                var providerGroup = !string.IsNullOrWhiteSpace(option?.ProviderName)
                    ? ProviderGroupPrefix + option.ProviderName
                    : GetModelGroup(modelId);
                var searchText = NormalizeSearchText(
                    $"{modelId} {option?.DisplayName} {option?.ProviderName} {GetModelProviderLabel(modelId)}");
                return new ModelPickerRowData(
                    model, modelId, isPinned ? PinnedGroup : providerGroup, isPinned, index, searchText);
            }).ToList();

        var groupOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!row.IsPinned && !groupOrder.ContainsKey(row.Group))
                groupOrder[row.Group] = groupOrder.Count;
        }
        return rows
            .OrderBy(row => row.IsPinned ? -1 : groupOrder[row.Group])
            .ThenByDescending(row => row.IsPinned ? 0 : GetModelVersionRank(row.ModelName))
            .ThenBy(row => row.OriginalIndex)
            .ToList();
    }

    /// <summary>
    /// Lists <paramref name="models"/> in the picker's sections and order — pinned items first in
    /// their own order, then each provider as it first appears, newest versions first — so a host
    /// that draws the list itself, such as a phone's bottom sheet, reads exactly as this picker does.
    /// </summary>
    /// <param name="models">Model ids or <see cref="IStrataModelOption"/> items, as for <see cref="Models"/>.</param>
    /// <param name="pinnedLabel">Name of the pinned section, as for <see cref="PinnedLabel"/>.</param>
    public static IReadOnlyList<StrataModelSection> GetModelSections(IEnumerable? models, string pinnedLabel = "Pinned")
    {
        var sections = new List<StrataModelSection>();
        List<object?>? members = null;
        string? group = null;
        foreach (var row in OrderCatalogRows(models))
        {
            if (row.Group != group)
            {
                group = row.Group;
                members = [];
                sections.Add(new StrataModelSection(
                    row.IsPinned ? pinnedLabel.ToUpperInvariant() : GetModelGroupLabel(row.Group),
                    row.IsPinned,
                    members));
            }
            members!.Add(row.Model);
        }
        return sections;
    }

    /// <summary>
    /// Brings the active row into view, together with its group heading when it is the first row of
    /// its group. The list never auto-scrolls to its selection, because the selection follows the
    /// pointer and auto-scrolling would move rows out from under it.
    /// </summary>
    private void ScrollActiveRowIntoView()
    {
        if (_modelPickerList is not { SelectedIndex: >= 0 } list || list.SelectedIndex >= _visibleRows.Count)
            return;

        var index = list.SelectedIndex;
        var groupStart = index > 0 && _visibleRows[index - 1].IsHeader ? index - 1 : index;
        if (groupStart == 0)
        {
            // The first group's opening rows are on screen at the top; no need to realize anything.
            if (list.Scroll is ScrollViewer viewer)
                viewer.Offset = viewer.Offset.WithY(0);
            return;
        }

        if (groupStart != index)
            list.ScrollIntoView(groupStart);
        list.ScrollIntoView(index);
    }

    private void RequestScrollActiveRowIntoView()
    {
        if (_scrollRefreshQueued)
            return;

        _scrollRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollRefreshQueued = false;
            if (_modelPickerPopup is { IsOpen: true })
                ScrollActiveRowIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The active row follows the pointer, so hover and keyboard share one highlight.</summary>
    private void OnModelListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_modelPickerList is not { } list
            || (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { } container)
            return;

        var index = list.IndexFromContainer(container);
        if (index >= 0 && index < _visibleRows.Count && !_visibleRows[index].IsHeader && list.SelectedIndex != index)
            list.SelectedIndex = index;
    }

    private static string NormalizeSearchText(string text)
        => new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private void OnModelContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (_modelPickerList?.Items[e.Index] is not ModelPickerRowData row)
            return;
        e.Container.Focusable = !row.IsHeader;
        e.Container.IsEnabled = !row.IsHeader;
        e.Container.IsHitTestVisible = !row.IsHeader;
        if (e.Container is ListBoxItem container
            && _modelPickerList.ItemTemplate is ModelPickerItemTemplate template)
        {
            // Content/ContentTemplate are cleared by framework recycling. A separate visual slot
            // preserves badges without bypassing the framework's selection cleanup.
            SetRowContent(container, template.Build(row, GetRowContent(container)));
        }
        AutomationProperties.SetName(e.Container,
            row.Model is IStrataModelOption option ? option.DisplayName : row.ModelName);
    }

    private void OnModelPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_modelPickerPopup is not { IsOpen: true } || _modelPickerList is null)
            return;

        if (e.Key == Key.Escape)
        {
            _modelPickerPopup.IsOpen = false;
            _modelPickerButton?.Focus();
            e.Handled = true;
            return;
        }

        // A row's pin button only appears with the pointer, so the highlighted row also pins from the
        // keyboard with the platform command key + P.
        if (e.Key == Key.P && e.KeyModifiers == PinGesture.KeyModifiers && ModelPinCommand is { } pin)
        {
            BuildModelPickerRows();
            if (_modelPickerList.SelectedItem is ModelPickerRowData { IsHeader: false, Model: IStrataModelOption } pinRow
                && pin.CanExecute(pinRow.Model))
                pin.Execute(pinRow.Model);
            e.Handled = true;
            return;
        }

        // A focused button (the pinned filter, a pin, a segment) keeps its own keys, so Enter on it
        // activates it instead of choosing the highlighted model.
        if (e.Source is Control source && source.GetSelfAndVisualAncestors().OfType<Button>().Any())
            return;

        if (e.Key is Key.Up or Key.Down)
        {
            BuildModelPickerRows();
            var direction = e.Key == Key.Down ? 1 : -1;
            var index = _modelPickerList.SelectedIndex;
            if (index < 0)
                index = direction > 0 ? -1 : _visibleRows.Count;
            for (index += direction; index >= 0 && index < _visibleRows.Count; index += direction)
            {
                if (_visibleRows[index].IsHeader)
                    continue;
                _modelPickerList.SelectedIndex = index;
                ScrollActiveRowIntoView();
                break;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            BuildModelPickerRows();
            if (_modelPickerList.SelectedItem is ModelPickerRowData { IsHeader: false } row)
                UpdateModelPickerSelection(ResolveSelectionValue(row.Model));
            e.Handled = true;
        }
    }

    private sealed record ModelPickerRowData(
        object? Model, string ModelName, string Group, bool IsPinned, int OriginalIndex, string SearchText,
        bool IsHeader = false, bool IsFirstGroup = false);

    /// <summary>Left inset of row text: the row's leading padding plus its check-mark column. Group
    /// headings use it too, so headings, names and the search text share one edge.</summary>
    private const double RowTextInset = 30;

    private sealed class ModelPickerItemTemplate(StrataModelPicker owner) : IRecyclingDataTemplate
    {
        public bool Match(object? data) => data is ModelPickerRowData;

        public Control? Build(object? data) => Build(data, null);

        public Control? Build(object? data, Control? existing)
        {
            if (data is not ModelPickerRowData entry)
                return null;

            if (entry.IsHeader)
            {
                var header = existing as TextBlock ?? new TextBlock
                {
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 0.6,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                header.Classes.Set("model-picker-group-header", true);
                header.Text = entry.ModelName;
                header.Margin = new Thickness(RowTextInset, entry.IsFirstGroup ? 4 : 12, 12, 5);
                return header;
            }

            if (existing is not ModelPickerRow row)
                return owner.CreateModelRow(entry.Model, entry.ModelName);
            owner.UpdateModelRow(row, entry.Model, entry.ModelName);
            return row;
        }
    }

    /// <summary>
    /// Live parts of one segmented control (reasoning effort / context window) so a selection change
    /// can slide the thumb instead of rebuilding the row underneath the user's cursor.
    /// </summary>
    private sealed class SegmentedSection
    {
        public required Border Track { get; init; }
        public required Border Thumb { get; init; }
        public required IReadOnlyList<Button> Segments { get; init; }
        public required IReadOnlyList<object?> Values { get; init; }

        /// <summary>What the control sets ("Reasoning"). Not drawn, so the footer never sets the
        /// popup's width: it is the control's tooltip and each segment's accessible help text.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>False until the thumb has been placed once. The first placement must not animate,
        /// or the popup would open with the thumb sliding in from the left edge.</summary>
        public bool HasPositioned { get; set; }

        public Rect LastBounds { get; set; }
    }

    private SegmentedSection? _effortSegments;
    private SegmentedSection? _contextSegments;

    private void RebuildEffortSection()
    {
        if (_effortSection is null)
            return;

        var levels = SelectedModel is not null ? QualityLevels?.Cast<object?>().ToArray() ?? [] : [];
        if (_effortSegments is { } existing && existing.Values.SequenceEqual(levels))
        {
            SetSectionLabel(existing, ReasoningLabel);
            ApplySegmentSelection(existing, SelectedQuality);
        }
        else
        {
            _effortSegments = null;
            _effortSection.Children.Clear();
            if (levels.Length > 0)
            {
                _effortSegments = AppendSegmentedSection(
                    _effortSection,
                    ReasoningLabel,
                    levels,
                    SelectedQuality,
                    value => SetCurrentValue(SelectedQualityProperty, value));
            }
        }

        UpdateModelSettingsState();
    }

    private void RebuildContextWindowSection()
    {
        if (_contextWindowSection is null)
            return;

        var tiers = SelectedModel is not null ? ContextWindowTiers?.Cast<object?>().ToArray() ?? [] : [];
        if (_contextSegments is { } existing && existing.Values.SequenceEqual(tiers))
        {
            SetSectionLabel(existing, ContextLabel);
            ApplySegmentSelection(existing, SelectedContextWindowTier);
        }
        else
        {
            _contextSegments = null;
            _contextWindowSection.Children.Clear();
            if (tiers.Length > 0)
            {
                _contextSegments = AppendSegmentedSection(
                    _contextWindowSection,
                    ContextLabel,
                    tiers,
                    SelectedContextWindowTier,
                    value => SetCurrentValue(SelectedContextWindowTierProperty, value));
            }
        }

        UpdateModelSettingsState();
    }

    /// <summary>Shows the settings footer only while the current model offers a choice.</summary>
    private void UpdateModelSettingsState()
    {
        if (_effortSection is not null)
            _effortSection.IsVisible = _effortSegments is not null;
        if (_contextWindowSection is not null)
            _contextWindowSection.IsVisible = _contextSegments is not null;
        PseudoClasses.Set(":has-model-settings", _effortSegments is not null || _contextSegments is not null);
    }

    /// <summary>
    /// Builds one setting: a segmented control spanning the footer, with a thumb that slides to the
    /// chosen segment. It has no text label or icon, so the footer never sets the popup's width; what
    /// it sets is the control's tooltip and its segments' accessible help text.
    /// </summary>
    private SegmentedSection AppendSegmentedSection(
        Panel host,
        string label,
        IReadOnlyList<object?> values,
        object? selectedValue,
        Action<object?> onSelected)
    {
        var thumb = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false
        };
        thumb.Classes.Add("segment-thumb");

        var strip = new ModelPickerSegmentStrip();
        var segments = new List<Button>(values.Count);
        var fontSize = GetSegmentFontSize(values.Count);
        var padding = GetSegmentPadding(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var button = new Button
            {
                Content = value?.ToString() ?? string.Empty,
                FontSize = fontSize,
                Padding = new Thickness(padding, 0)
            };
            button.Classes.Add("effort-seg");
            if (Equals(value, selectedValue))
                button.Classes.Add("active");

            strip.Children.Add(button);
            segments.Add(button);
        }

        var layers = new Panel();
        layers.Children.Add(thumb);
        layers.Children.Add(strip);
        var track = new Border { Child = layers };
        track.Classes.Add("model-picker-segments");
        track.Classes.Add("model-setting");
        ToolTip.SetShowDelay(track, 600);

        var section = new SegmentedSection
        {
            Track = track,
            Thumb = thumb,
            Segments = segments,
            Values = values
        };
        SetSectionLabel(section, label);

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            segments[index].Click += (_, _) =>
            {
                onSelected(value);
                ApplySegmentSelection(section, value);
            };
        }

        // The thumb is sized and offset from the chosen segment's arranged bounds, which only exist
        // after a layout pass and change whenever the strip is re-arranged. Re-checking on layout
        // keeps it locked to the segment without a manual measure.
        strip.LayoutUpdated += (_, _) => UpdateSegmentThumb(section);

        host.Children.Add(track);
        ApplySegmentSelection(section, selectedValue);

        return section;
    }

    private static void SetSectionLabel(SegmentedSection section, string label)
    {
        if (section.Label == label)
            return;

        section.Label = label;
        ToolTip.SetTip(section.Track, label);
        foreach (var segment in section.Segments)
            AutomationProperties.SetHelpText(segment, label);
    }

    private static void ApplySegmentSelection(SegmentedSection section, object? value)
    {
        var text = value?.ToString();
        AutomationProperties.SetName(section.Track, $"{section.Label}: {text}");

        foreach (var segment in section.Segments)
        {
            if (Equals(segment.Content?.ToString(), text))
            {
                if (!segment.Classes.Contains("active"))
                    segment.Classes.Add("active");
            }
            else
            {
                segment.Classes.Remove("active");
            }
        }

        UpdateSegmentThumb(section);
    }

    /// <summary>Sizes and slides the thumb onto the active segment. No-ops while the segment has not
    /// been arranged yet, and skips redundant work when nothing moved.</summary>
    private static void UpdateSegmentThumb(SegmentedSection section)
    {
        var active = section.Segments.FirstOrDefault(segment => segment.Classes.Contains("active"));
        if (active is null)
        {
            section.Thumb.IsVisible = false;
            return;
        }

        var bounds = active.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        if (section.HasPositioned && bounds == section.LastBounds)
            return;

        section.LastBounds = bounds;
        section.Thumb.IsVisible = true;
        section.Thumb.Width = bounds.Width;
        section.Thumb.Height = bounds.Height;

        var offset = new TransformOperations.Builder(1);
        offset.AppendTranslate(bounds.X, bounds.Y);
        section.Thumb.RenderTransform = offset.Build();

        if (section.HasPositioned)
            return;

        // Attaching transitions only after the first placement keeps the opening frame static and
        // every later change animated.
        section.HasPositioned = true;
        section.Thumb.Transitions =
        [
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut()
            },
            new DoubleTransition
            {
                Property = Layoutable.WidthProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut()
            }
        ];
    }

    /// <summary>
    /// Segments share one row of fixed popup width, so a model that exposes many reasoning efforts
    /// (low/medium/high/xhigh/max) tightens its labels instead of pushing the control off the row.
    /// </summary>
    private static double GetSegmentFontSize(int segmentCount) => segmentCount >= 6 ? 11.5 : 12;

    private static double GetSegmentPadding(int segmentCount) => segmentCount switch
    {
        >= 6 => 7,
        5 => 8,
        _ => 10
    };

    /// <summary>
    /// Segments side by side, filling the row. Spare width is shared evenly, so labels keep even gaps
    /// and every settings row ends on the same edge; when the row is too narrow, every segment shrinks
    /// in proportion (labels trim) instead of the last ones being cut off.
    /// </summary>
    private sealed class ModelPickerSegmentStrip : Panel
    {
        private readonly List<double> _naturalWidths = [];

        protected override Size MeasureOverride(Size availableSize)
        {
            _naturalWidths.Clear();
            double width = 0;
            double height = 0;
            foreach (var child in Children)
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                _naturalWidths.Add(child.DesiredSize.Width);
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }

            if (width > availableSize.Width && width > 0)
            {
                var scale = availableSize.Width / width;
                for (var index = 0; index < Children.Count; index++)
                    Children[index].Measure(new Size(_naturalWidths[index] * scale, availableSize.Height));
                width = availableSize.Width;
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var count = Math.Min(Children.Count, _naturalWidths.Count);
            var natural = 0d;
            for (var index = 0; index < count; index++)
                natural += _naturalWidths[index];

            var spare = count > 0 ? (finalSize.Width - natural) / count : 0;
            var scale = natural > 0 ? finalSize.Width / natural : 1;
            // Edges are snapped to the pixel grid as they accumulate, so the segments tile the track
            // exactly instead of each rounding on its own and drifting a pixel past its end.
            var layoutScale = UseLayoutRounding ? LayoutHelper.GetLayoutScale(this) : 0;
            double edge = 0;
            double x = 0;
            for (var index = 0; index < count; index++)
            {
                edge += spare >= 0 ? _naturalWidths[index] + spare : _naturalWidths[index] * scale;
                var next = index == count - 1
                    ? finalSize.Width
                    : layoutScale > 0 ? LayoutHelper.RoundLayoutValue(edge, layoutScale) : edge;
                Children[index].Arrange(new Rect(x, 0, Math.Max(0, next - x), finalSize.Height));
                x = next;
            }

            return finalSize;
        }
    }

    /// <summary>Returns the desktop catalog's provider/group label for a model ID.</summary>
    public static string GetModelProviderLabel(string modelId) =>
        GetModelGroupLabel(GetModelGroup(modelId));

    private static string GetModelGroup(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        if (lower.StartsWith("claude", StringComparison.Ordinal))
            return "claude";
        if (lower.StartsWith("gpt", StringComparison.Ordinal))
            return "gpt";
        if (lower.StartsWith("o1", StringComparison.Ordinal)
            || lower.StartsWith("o3", StringComparison.Ordinal)
            || lower.StartsWith("o4", StringComparison.Ordinal))
            return "reasoning";
        if (lower.StartsWith("gemini", StringComparison.Ordinal))
            return "gemini";
        return "other";
    }

    private static string GetModelGroupLabel(string group) => group.StartsWith(ProviderGroupPrefix, StringComparison.Ordinal)
        ? group[ProviderGroupPrefix.Length..].ToUpperInvariant()
        : group switch
    {
        PinnedGroup => "PINNED",
        "claude" => "ANTHROPIC",
        "gpt" => "OPENAI",
        "reasoning" => "REASONING",
        "gemini" => "GOOGLE",
        _ => "OTHER"
    };

    private static int GetModelVersionRank(string modelId)
    {
        foreach (var segment in modelId.ToLowerInvariant().Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseVersionSegment(segment, out var rank))
                return rank;
        }

        return -1;
    }

    private static bool TryParseVersionSegment(string segment, out int rank)
    {
        rank = -1;
        var index = segment.StartsWith("o", StringComparison.Ordinal) ? 1 : 0;
        if (index >= segment.Length || !char.IsDigit(segment[index]))
            return false;

        var major = 0;
        while (index < segment.Length && char.IsDigit(segment[index]))
        {
            major = major * 10 + segment[index] - '0';
            index++;
        }

        var minor = 0;
        if (index < segment.Length && segment[index] == '.')
        {
            index++;
            while (index < segment.Length && char.IsDigit(segment[index]))
            {
                minor = minor * 10 + segment[index] - '0';
                index++;
            }
        }

        if (index < segment.Length && segment[index] == 'm')
            return false;

        rank = major * 1000 + minor;
        return true;
    }

    private static string GetModelTier(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        if (lower.Contains("opus", StringComparison.Ordinal))
            return "premium";
        if (lower.Contains("pro", StringComparison.Ordinal))
            return "premium";
        if (lower.Contains("haiku", StringComparison.Ordinal))
            return "fast";
        if (lower.Contains("mini", StringComparison.Ordinal))
            return "fast";
        if (lower.Contains("codex-max", StringComparison.Ordinal) || lower.Contains("codex max", StringComparison.Ordinal))
            return "max";
        if (lower.Contains("codex", StringComparison.Ordinal))
            return "code";
        if (lower.Contains("1m", StringComparison.Ordinal) || lower.Contains("2m", StringComparison.Ordinal))
            return "extended";
        if (IsReasoningCapable(modelId))
            return "reasoning";
        return string.Empty;
    }

    private static bool IsReasoningCapable(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        return lower.StartsWith("o1", StringComparison.Ordinal)
               || lower.StartsWith("o3", StringComparison.Ordinal)
               || lower.StartsWith("o4", StringComparison.Ordinal)
               || lower.Contains("think", StringComparison.Ordinal);
    }

    /// <summary>Leading padding of a row; the check-mark column fills the rest of <see cref="RowTextInset"/>.</summary>
    private const double RowLeadingPadding = 8;

    // Parsed on first use rather than when the type loads: parsing needs Avalonia's render platform,
    // and static members such as GetModelProviderLabel are also used before (or without) a running app.
    private static Geometry? s_checkGeometry;
    private static Geometry? s_pinGeometry;

    private static Geometry CheckGeometry => s_checkGeometry ??= Geometry.Parse("M3.5 8.5 L6.5 11.5 L12.5 4.75");

    // Push-pin: cap, body flaring to its base, and the needle.
    private static Geometry PinGeometry =>
        s_pinGeometry ??= Geometry.Parse("M5 2 H11 M6.25 2 V6 L4.25 9 H11.75 L9.75 6 V2 M8 9 V14.5");

    private Control CreateModelRow(object? model, string modelName)
    {
        var contentGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(RowTextInset - RowLeadingPadding, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            ]
        };

        var selectionButton = new Button
        {
            Content = contentGrid,
            MinHeight = 32,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        selectionButton.Classes.Add("model-row-select");

        var pinButton = CreatePinButton(model, false);
        Grid.SetColumn(pinButton, 1);

        // Every control here is restyled each time the list recycles the row, so the row is kept to
        // as few elements as the design needs: the row is the grid, and host content sits in it directly.
        var row = new ModelPickerRow
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ContentGrid = contentGrid,
            SelectionButton = selectionButton,
            PinButton = pinButton
        };
        row.Children.Add(selectionButton);
        row.Children.Add(pinButton);
        row.Classes.Add("model-picker-row");
        selectionButton.Click += (_, args) =>
        {
            args.Handled = true;
            UpdateModelPickerSelection(ResolveSelectionValue(row.Model));
        };
        UpdateModelRow(row, model, modelName);
        return row;
    }

    private sealed class ModelPickerRow : Grid
    {
        protected override Type StyleKeyOverride => typeof(Grid);

        public object? Model { get; set; }
        public IDataTemplate? ItemTemplate { get; set; }
        public Control? ModelContent { get; set; }
        public Avalonia.Controls.Shapes.Path? Check { get; set; }
        public required Grid ContentGrid { get; init; }
        public Border? TierBadge { get; set; }
        public TextBlock? TierText { get; set; }
        public required Button SelectionButton { get; init; }
        public required Button PinButton { get; init; }
    }

    /// <summary>Marks the current model's row. The check is created the first time a row needs it,
    /// so the many rows that never show one do not carry it.</summary>
    private static void SetRowChecked(ModelPickerRow row, bool isChecked)
    {
        row.Classes.Set("selected", isChecked);
        if (row.Check is null)
        {
            if (!isChecked)
                return;

            row.Check = new Avalonia.Controls.Shapes.Path
            {
                Data = CheckGeometry,
                Width = 16,
                Height = 16,
                Stretch = Stretch.None,
                StrokeThickness = 1.75,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                // A check mark is not a directional glyph; mirrored in right-to-left layouts it reads as a chevron.
                FlowDirection = FlowDirection.LeftToRight
            };
            row.Check.Classes.Add("model-picker-check");
            row.ContentGrid.Children.Add(row.Check);
        }

        row.Check.IsVisible = isChecked;
    }

    private void UpdateModelRow(ModelPickerRow row, object? model, string modelName)
    {
        var option = model as IStrataModelOption;
        var showPin = option is not null && ModelPinCommand is not null;
        row.Model = model;
        row.Tag = modelName;
        var template = ModelItemTemplate ?? PlainModelItemTemplate;
        var content = ModelItemTemplate is not null ? model : modelName;
        var previousContent = row.ModelContent;
        row.ModelContent = template is IRecyclingDataTemplate recycling
            ? recycling.Build(content, ReferenceEquals(row.ItemTemplate, template) ? previousContent : null)
            : template.Build(content);
        row.ItemTemplate = template;
        if (!ReferenceEquals(previousContent, row.ModelContent))
        {
            if (previousContent is not null)
                row.ContentGrid.Children.Remove(previousContent);
            if (row.ModelContent is { } hostContent)
            {
                Grid.SetColumn(hostContent, 1);
                row.ContentGrid.Children.Add(hostContent);
            }
        }
        if (row.ModelContent is not null)
            row.ModelContent.DataContext = content;
        row.SelectionButton.Padding = new Thickness(RowLeadingPadding, 0, showPin ? 6 : 12, 0);
        AutomationProperties.SetName(row.SelectionButton, option?.DisplayName ?? modelName);
        row.PinButton.IsVisible = showPin;
        row.PinButton.Command = ModelPinCommand;
        row.PinButton.CommandParameter = model;
        row.PinButton.Classes.Set("pinned", option?.IsPinned == true);
        var toolTip = option?.IsPinned == true ? UnpinToolTip : PinToolTip;
        var shortcut = showPin ? PinGesture.ToString("p", null) : null;
        ToolTip.SetTip(row.PinButton, string.IsNullOrWhiteSpace(toolTip) || shortcut is null
            ? toolTip
            : $"{toolTip} ({shortcut})");
        AutomationProperties.SetAcceleratorKey(row.PinButton, shortcut);
        var actionLabel = !string.IsNullOrWhiteSpace(toolTip)
            ? toolTip : option?.IsPinned == true ? "Unpin model" : "Pin model";
        AutomationProperties.SetName(row.PinButton, $"{actionLabel}: {modelName}");

        var tier = option is null ? GetModelTier(modelName) : string.Empty;
        if (tier.Length > 0 && row.TierBadge is null)
        {
            row.TierText = new TextBlock();
            row.TierText.Classes.Add("model-tier-text");
            row.TierBadge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = row.TierText
            };
            row.TierBadge.Classes.Add("model-tier-badge");
            Grid.SetColumn(row.TierBadge, 2);
            row.ContentGrid.Children.Add(row.TierBadge);
        }
        if (row.TierBadge is { } badge && row.TierText is { } tierText)
        {
            badge.IsVisible = tier.Length > 0;
            tierText.Text = tier;
            var premium = tier is "premium" or "max" or "extended";
            badge.Classes.Set("tier-premium", premium);
            tierText.Classes.Set("tier-premium", premium);
            tierText.Classes.Set("tier-fast", tier == "fast");
            tierText.Classes.Set("tier-reasoning", tier == "reasoning");
        }
        SetRowChecked(row, modelName.Length > 0 && modelName == ResolveModelId(SelectedModel));
    }

    private void UpdateRealizedModelRows()
    {
        if (_modelPickerList is null)
            return;
        foreach (var row in _modelPickerList.GetVisualDescendants().OfType<ModelPickerRow>())
            UpdateModelRow(row, row.Model, row.Tag as string ?? string.Empty);
    }

    /// <summary>Pins or unpins the highlighted row: the platform command key (Ctrl, or Cmd on macOS) + P.</summary>
    private static KeyGesture PinGesture =>
        new(Key.P, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

    /// <summary>
    /// Pin toggle for a rich row. It is a sibling of the row-selection button, so both controls keep
    /// their normal input semantics without any handled-event routing.
    /// </summary>
    private Button CreatePinButton(object? model, bool isPinned)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = PinGeometry,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            StrokeThickness = 1.3,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };

        var button = new Button
        {
            Content = icon,
            Command = ModelPinCommand,
            CommandParameter = model,
            Width = 26,
            Height = 26,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.Classes.Add("model-pin");
        if (isPinned)
            button.Classes.Add("pinned");

        var toolTip = isPinned ? UnpinToolTip : PinToolTip;
        if (!string.IsNullOrWhiteSpace(toolTip))
            ToolTip.SetTip(button, toolTip);
        var actionLabel = !string.IsNullOrWhiteSpace(toolTip)
            ? toolTip
            : isPinned ? "Unpin model" : "Pin model";
        AutomationProperties.SetName(button, $"{actionLabel}: {ResolveModelId(model)}");

        return button;
    }

    private void UpdateModelPickerSelection(object? newModel)
    {
        SetCurrentValue(SelectedModelProperty, newModel);
        if (_modelPickerList is not null)
            _modelPickerList.SelectedItem = _visibleRows.FirstOrDefault(
                row => !row.IsHeader && row.ModelName == ResolveModelId(newModel));
    }

    private void UpdateModelPickerSelectionVisuals(object? selectedModel)
    {
        if (_modelPickerList is null)
            return;

        var selectedId = ResolveModelId(selectedModel);
        foreach (var row in _modelPickerList.GetVisualDescendants().OfType<ModelPickerRow>())
        {
            var isNowSelected = row.Tag is string rowId
                && rowId.Length > 0
                && string.Equals(rowId, selectedId, StringComparison.Ordinal);
            SetRowChecked(row, isNowSelected);
        }
    }

    private void UpdateEffortActiveState()
    {
        if (_effortSection is null)
            return;

        if (QualityLevels is not null && _effortSegments is null)
        {
            RebuildEffortSection();
            return;
        }

        if (QualityLevels is null && _effortSegments is not null)
        {
            _effortSection.Children.Clear();
            _effortSegments = null;
            UpdateModelSettingsState();
            return;
        }

        if (_effortSegments is { } section)
            ApplySegmentSelection(section, SelectedQuality);
    }

    private void UpdateContextWindowActiveState()
    {
        if (_contextWindowSection is null)
            return;

        if (ContextWindowTiers is not null && _contextSegments is null)
        {
            RebuildContextWindowSection();
            return;
        }

        if (ContextWindowTiers is null && _contextSegments is not null)
        {
            _contextWindowSection.Children.Clear();
            _contextSegments = null;
            UpdateModelSettingsState();
            return;
        }

        if (_contextSegments is { } section)
            ApplySegmentSelection(section, SelectedContextWindowTier);
    }

    private void EnsureSelectedValues()
    {
        if (Models is not null && SelectedModel is null)
        {
            foreach (var item in Models)
            {
                SelectedModel = ResolveSelectionValue(item);
                break;
            }
        }

        Sync();
    }

    private void Sync()
    {
        PseudoClasses.Set(":has-models", Models is not null);
        PseudoClasses.Set(":has-quality", QualityLevels is not null);
        PseudoClasses.Set(":has-context-window", ContextWindowTiers is not null);
    }
}
