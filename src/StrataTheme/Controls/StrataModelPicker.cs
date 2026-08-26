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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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
/// Compact model picker with grouped providers and per-model reasoning effort/context controls.
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
/// PART_ModelPickerList (StackPanel), PART_EffortSection (StackPanel), PART_ContextWindowSection (StackPanel).</para>
/// <para><b>Pseudo-classes:</b> :has-models, :has-quality, :has-context-window, :model-picker-open.</para>
/// <para><b>Rich items:</b> items implementing <see cref="IStrataModelOption"/> are identified by
/// <see cref="IStrataModelOption.ModelId"/> instead of <c>ToString()</c>, are listed pinned-first, and
/// get a pin affordance when <see cref="ModelPinCommand"/> is set. <see cref="SelectedModel"/> still
/// receives the plain id, so the host's selection property does not have to change shape.</para>
/// </remarks>
public class StrataModelPicker : TemplatedControl
{
    private Button? _modelPickerButton;
    private Popup? _modelPickerPopup;
    private StackPanel? _modelPickerList;
    private Border? _modelPickerChevronWrap;
    private StackPanel? _effortSection;
    private StackPanel? _contextWindowSection;
    private bool _suppressPickerRebuild;
    private INotifyCollectionChanged? _observedModels;
    private readonly List<INotifyPropertyChanged> _observedModelItems = [];
    private bool _isAttachedToVisualTree;
    private IDataTemplate? _effectiveSelectedModelTemplate;

    /// <summary>Synthetic group the pinned section is listed under, ahead of every provider group.</summary>
    private const string PinnedGroup = "\u0000pinned";

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

    static StrataModelPicker()
    {
        ModelsProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.ObserveModelsCollection();
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.RefreshModelPickerIfOpen();
        });
        QualityLevelsProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.RefreshModelPickerEffortIfOpen();
        });
        SelectedModelProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RefreshModelPickerSelectionIfOpen());
        ModelItemTemplateProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.UpdateEffectiveSelectedModelTemplate());
        SelectedModelTemplateProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.UpdateEffectiveSelectedModelTemplate());
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
        if (_modelPickerButton is not null)
            _modelPickerButton.Click -= OnModelPickerButtonClick;
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened -= OnModelPickerPopupOpened;
            _modelPickerPopup.Closed -= OnModelPickerPopupClosed;
        }

        base.OnApplyTemplate(e);

        _modelPickerButton = e.NameScope.Find<Button>("PART_ModelPickerButton");
        _modelPickerPopup = e.NameScope.Find<Popup>("PART_ModelPickerPopup");
        _modelPickerList = e.NameScope.Find<StackPanel>("PART_ModelPickerList");
        _modelPickerChevronWrap = e.NameScope.Find<Border>("PART_ModelPickerChevronWrap");
        _effortSection = e.NameScope.Find<StackPanel>("PART_EffortSection");
        _contextWindowSection = e.NameScope.Find<StackPanel>("PART_ContextWindowSection");

        if (_modelPickerButton is not null)
            _modelPickerButton.Click += OnModelPickerButtonClick;
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened += OnModelPickerPopupOpened;
            _modelPickerPopup.Closed += OnModelPickerPopupClosed;
        }

        EnsureSelectedValues();
        Sync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        ObserveModelsCollection();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_modelPickerButton is not null)
            _modelPickerButton.Click -= OnModelPickerButtonClick;
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened -= OnModelPickerPopupOpened;
            _modelPickerPopup.Closed -= OnModelPickerPopupClosed;
        }

        // The models collection is owned by the host view model and usually outlives this control,
        // so the subscription must be dropped or the picker would be kept alive by it.
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
            if (_observedModelItems.Any(observed => ReferenceEquals(observed, item)))
                continue;

            item.PropertyChanged += OnModelItemPropertyChanged;
            _observedModelItems.Add(item);
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
        ObserveModelItems();
        EnsureSelectedValues();
        Sync();
        RefreshModelPickerIfOpen();
    }

    private void OnModelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not IStrataModelOption
            || (!string.IsNullOrEmpty(e.PropertyName)
                && e.PropertyName != nameof(IStrataModelOption.IsPinned)))
        {
            return;
        }

        RefreshModelPickerIfOpen();
    }

    private void OnModelPickerButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleModelPickerPopup();
    }

    private void OnModelPickerPopupOpened(object? sender, EventArgs e)
    {
        if (_modelPickerPopup is not null)
            ConfigurePopupTranslucency(_modelPickerPopup);
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

    private void ConfigurePopupTranslucency(Popup popup)
    {
        if (popup.Child is Border panel && panel.Background is ISolidColorBrush solid)
        {
            var color = solid.Color;
            panel.Background = new SolidColorBrush(Color.FromArgb(236, color.R, color.G, color.B));
        }
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
            PseudoClasses.Set(":model-picker-open", false);
            AnimateChevron(false);
            return;
        }

        BuildModelPickerRows();
        _modelPickerPopup.IsOpen = true;
        PseudoClasses.Set(":model-picker-open", true);
        AnimateChevron(true);
        RaisePickerOpened();

        Dispatcher.UIThread.Post(() =>
        {
            if (_modelPickerList is null)
                return;

            foreach (var child in _modelPickerList.Children)
            {
                if (child is Border border && border.Classes.Contains("selected"))
                {
                    border.BringIntoView();
                    break;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void RefreshModelPickerIfOpen()
    {
        if (_suppressPickerRebuild)
            return;

        if (_modelPickerPopup is { IsOpen: true })
            BuildModelPickerRows();
    }

    private void RefreshModelPickerSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true } || _suppressPickerRebuild)
            return;

        UpdateModelPickerSelectionVisuals(SelectedModel);
        RebuildEffortSection();
        RebuildContextWindowSection();
    }

    private void RefreshModelPickerEffortIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerEffortIfOpen, DispatcherPriority.Background);
            return;
        }

        RebuildEffortSection();
        RebuildContextWindowSection();
    }

    private void RefreshModelPickerQualityIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerQualityIfOpen, DispatcherPriority.Background);
            return;
        }

        UpdateEffortActiveState();
    }

    private void RefreshModelPickerContextIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerContextIfOpen, DispatcherPriority.Background);
            return;
        }

        RebuildContextWindowSection();
    }

    private void RefreshModelPickerContextSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerContextSelectionIfOpen, DispatcherPriority.Background);
            return;
        }

        UpdateContextWindowActiveState();
    }

    private void BuildModelPickerRows()
    {
        if (_modelPickerList is null)
            return;

        _modelPickerList.Children.Clear();
        if (Models is null)
            return;

        var rows = Models.Cast<object?>()
            .Select((model, index) =>
            {
                var modelId = ResolveModelId(model);
                var isPinned = model is IStrataModelOption { IsPinned: true };
                return new ModelPickerRowData(model, modelId, isPinned ? PinnedGroup : GetModelGroup(modelId), isPinned, index);
            })
            .ToList();

        var groupOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.IsPinned)
                continue;
            if (!groupOrder.ContainsKey(row.Group))
                groupOrder[row.Group] = groupOrder.Count;
        }

        var selectedId = ResolveModelId(SelectedModel);
        string? lastGroup = null;
        // Pinned rows lead, in the order the host supplied them (that is the user's pin order, which
        // provider grouping and version ranking would otherwise scramble).
        foreach (var row in rows
            .OrderBy(row => row.IsPinned ? -1 : groupOrder[row.Group])
            .ThenByDescending(row => row.IsPinned ? 0 : GetModelVersionRank(row.ModelName))
            .ThenBy(row => row.OriginalIndex))
        {
            var group = row.Group;
            if (group != lastGroup)
            {
                if (lastGroup is not null)
                {
                    var separator = new Border { Height = 1, Margin = new Thickness(10, 5) };
                    separator.Classes.Add("model-picker-separator");
                    _modelPickerList.Children.Add(separator);
                }

                var header = new TextBlock
                {
                    Text = GetModelGroupLabel(group),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 0.8,
                    Margin = new Thickness(12, lastGroup is null ? 6 : 6, 12, 3)
                };
                header.Classes.Add("model-picker-group-header");
                _modelPickerList.Children.Add(header);
                lastGroup = group;
            }

            var isSelected = row.ModelName.Length > 0
                && string.Equals(row.ModelName, selectedId, StringComparison.Ordinal);
            _modelPickerList.Children.Add(CreateModelRow(row.Model, row.ModelName, isSelected));
        }

        RebuildEffortSection();
        RebuildContextWindowSection();
    }

    private sealed record ModelPickerRowData(object? Model, string ModelName, string Group, bool IsPinned, int OriginalIndex);

    /// <summary>
    /// Live parts of one segmented control (reasoning effort / context window) so a selection change
    /// can slide the thumb instead of rebuilding the row underneath the user's cursor.
    /// </summary>
    private sealed class SegmentedSection
    {
        public required Grid Grid { get; init; }
        public required Border Thumb { get; init; }
        public required TextBlock Readout { get; init; }
        public required IReadOnlyList<Button> Segments { get; init; }

        /// <summary>False until the thumb has been placed once. The first placement must not animate,
        /// or the popup would open with the thumb sliding in from the left edge.</summary>
        public bool HasPositioned { get; set; }

        public Rect LastBounds { get; set; }
    }

    private SegmentedSection? _effortSegments;
    private SegmentedSection? _contextSegments;

    private void RebuildEffortSection()
    {
        _effortSegments = null;
        if (_effortSection is null)
            return;

        _effortSection.Children.Clear();
        if (QualityLevels is null || SelectedModel is null)
            return;

        var levels = QualityLevels.Cast<object?>().ToList();
        if (levels.Count == 0)
            return;

        _effortSegments = AppendSegmentedSection(
            _effortSection,
            "REASONING EFFORT",
            levels,
            SelectedQuality,
            new Thickness(8, 0, 8, 4),
            value =>
            {
                _suppressPickerRebuild = true;
                SelectedQuality = value;
                Dispatcher.UIThread.Post(() => _suppressPickerRebuild = false, DispatcherPriority.Background);
            });
    }

    private void RebuildContextWindowSection()
    {
        _contextSegments = null;
        if (_contextWindowSection is null)
            return;

        _contextWindowSection.Children.Clear();
        if (ContextWindowTiers is null || SelectedModel is null)
            return;

        var tiers = ContextWindowTiers.Cast<object?>().ToList();
        if (tiers.Count == 0)
            return;

        _contextSegments = AppendSegmentedSection(
            _contextWindowSection,
            "CONTEXT WINDOW",
            tiers,
            SelectedContextWindowTier,
            new Thickness(8, 0, 8, 6),
            value =>
            {
                _suppressPickerRebuild = true;
                SelectedContextWindowTier = value;
                Dispatcher.UIThread.Post(() => _suppressPickerRebuild = false, DispatcherPriority.Background);
            });
    }

    /// <summary>
    /// Builds one labelled segmented control: a section header with a live readout of the current
    /// value, and a track whose accent thumb slides to the chosen segment.
    /// </summary>
    private SegmentedSection AppendSegmentedSection(
        Panel host,
        string label,
        IReadOnlyList<object?> values,
        object? selectedValue,
        Thickness trackMargin,
        Action<object?> onSelected)
    {
        var separator = new Border { Height = 1, Margin = new Thickness(10, 4) };
        separator.Classes.Add("model-picker-separator");
        host.Children.Add(separator);

        host.Children.Add(BuildSectionHeader(label, selectedValue, out var readout));

        var track = new Border
        {
            Margin = trackMargin,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(3)
        };
        track.Classes.Add("model-effort-toggle");

        var thumb = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false
        };
        thumb.Classes.Add("segment-thumb");

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse(
                string.Join(",", Enumerable.Range(0, values.Count).Select(_ => "*")))
        };

        var segments = new List<Button>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var button = new Button
            {
                Content = value?.ToString() ?? string.Empty,
                FontSize = GetSegmentFontSize(values.Count),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Classes.Add("effort-seg");
            if (Equals(value, selectedValue))
                button.Classes.Add("active");

            Grid.SetColumn(button, index);
            grid.Children.Add(button);
            segments.Add(button);
        }

        var section = new SegmentedSection
        {
            Grid = grid,
            Thumb = thumb,
            Readout = readout,
            Segments = segments
        };

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
        // after a layout pass — and change again whenever the popup is resized or the segment count
        // changes. Re-checking on layout keeps it locked to the segment without a manual measure.
        grid.LayoutUpdated += (_, _) => UpdateSegmentThumb(section);

        var layers = new Panel();
        layers.Children.Add(thumb);
        layers.Children.Add(grid);
        track.Child = layers;
        host.Children.Add(track);

        return section;
    }

    private static Control BuildSectionHeader(string label, object? selectedValue, out TextBlock readout)
    {
        var header = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            Margin = new Thickness(14, 5, 12, 5)
        };

        var marker = new Border
        {
            Width = 2,
            Height = 10,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        };
        marker.Classes.Add("section-marker");
        Grid.SetColumn(marker, 0);
        header.Children.Add(marker);

        var title = new TextBlock
        {
            Text = label,
            FontSize = 9.5,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.Classes.Add("effort-label");
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        readout = new TextBlock
        {
            Text = selectedValue?.ToString() ?? string.Empty,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        readout.Classes.Add("segment-readout");
        Grid.SetColumn(readout, 2);
        header.Children.Add(readout);

        return header;
    }

    private static void ApplySegmentSelection(SegmentedSection section, object? value)
    {
        var text = value?.ToString();
        section.Readout.Text = text ?? string.Empty;

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
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new CubicEaseOut()
            },
            new DoubleTransition
            {
                Property = Layoutable.WidthProperty,
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new CubicEaseOut()
            }
        ];
    }

    /// <summary>
    /// Segments share one row of fixed popup width, so a model that exposes many reasoning efforts
    /// (low/medium/high/xhigh/max) would otherwise clip its longest label. Step the label size down as
    /// the row gets busier instead of truncating.
    /// </summary>
    private static double GetSegmentFontSize(int segmentCount) => segmentCount switch
    {
        >= 6 => 9.5,
        5 => 10.5,
        _ => 11
    };

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

    private static string GetModelGroupLabel(string group) => group switch
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

    private Border CreateModelRow(object? model, string modelName, bool isSelected)
    {
        var option = model as IStrataModelOption;
        var showPin = option is not null && ModelPinCommand is not null;
        var contentGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("20,*,Auto") };

        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = isSelected
        };
        dot.Classes.Add("model-picker-dot");
        Grid.SetColumn(dot, 0);
        contentGrid.Children.Add(dot);

        if (ModelItemTemplate is not null)
        {
            var presenter = new ContentPresenter
            {
                Content = model,
                ContentTemplate = ModelItemTemplate,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(presenter, 1);
            contentGrid.Children.Add(presenter);
        }
        else
        {
            var name = new TextBlock { Text = modelName };
            name.Classes.Add("model-name");
            Grid.SetColumn(name, 1);
            contentGrid.Children.Add(name);
        }

        // The heuristic tier badge is derived from the id alone. A rich item renders its own,
        // metadata-backed badges through the template, so repeating a guess here is just noise.
        var tier = option is null ? GetModelTier(modelName) : string.Empty;
        if (!string.IsNullOrEmpty(tier))
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = tier,
                    FontSize = 9.5,
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            badge.Classes.Add("model-tier-badge");
            badge.Classes.Add(tier switch
            {
                "premium" or "max" or "extended" => "tier-premium",
                "fast" => "tier-fast",
                "reasoning" => "tier-reasoning",
                _ => "tier-default"
            });
            Grid.SetColumn(badge, 2);
            contentGrid.Children.Add(badge);
        }

        var selectionButton = new Button
        {
            Content = contentGrid,
            Padding = new Thickness(8, 7, showPin ? 0 : 10, 7),
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        selectionButton.Classes.Add("model-row-select");

        var capturedModel = model;
        selectionButton.Click += (_, args) =>
        {
            args.Handled = true;
            UpdateModelPickerSelection(ResolveSelectionValue(capturedModel));
        };

        // Selection and pinning are sibling buttons. A child pin button inside a pointer-selectable
        // row forces the two controls to fight over the same press/release route; siblings make each
        // hit target independent and preserve normal Button input semantics.
        var rowGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        Grid.SetColumn(selectionButton, 0);
        rowGrid.Children.Add(selectionButton);

        if (showPin)
        {
            var pinButton = CreatePinButton(model!, option!.IsPinned);
            Grid.SetColumn(pinButton, 1);
            rowGrid.Children.Add(pinButton);
        }

        var border = new Border
        {
            Child = rowGrid,
            CornerRadius = new CornerRadius(8),
            // Identity is carried on the row so selection visuals never have to reverse-engineer it
            // from the templated content, which a host template is free to render any way it likes.
            Tag = modelName
        };
        border.Classes.Add("model-picker-row");
        if (isSelected)
            border.Classes.Add("selected");

        return border;
    }

    /// <summary>
    /// Pin toggle for a rich row. It is a sibling of the row-selection button, so both controls keep
    /// their normal input semantics without any handled-event routing.
    /// </summary>
    private Button CreatePinButton(object model, bool isPinned)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            // Pin glyph: head, shaft and point.
            Data = Geometry.Parse("M9.5 2 L14.5 7 L12.6 8.1 L12.1 11.3 L9.9 9.1 L6.4 12.6 L5.7 11.9 L9.2 8.4 L7 6.2 L10.2 5.7 Z"),
            Stretch = Stretch.Uniform,
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var button = new Button
        {
            Content = icon,
            Command = ModelPinCommand,
            CommandParameter = model,
            Width = 22,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
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
        _suppressPickerRebuild = true;
        SelectedModel = newModel;
        UpdateModelPickerSelectionVisuals(newModel);

        Dispatcher.UIThread.Post(() =>
        {
            _suppressPickerRebuild = false;
            RefreshModelPickerEffortIfOpen();
            RefreshModelPickerContextIfOpen();
        }, DispatcherPriority.Background);
    }

    private void UpdateModelPickerSelectionVisuals(object? selectedModel)
    {
        if (_modelPickerList is null)
            return;

        var selectedId = ResolveModelId(selectedModel);
        foreach (var child in _modelPickerList.Children)
        {
            if (child is not Border border || !border.Classes.Contains("model-picker-row"))
                continue;
            var dot = border.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(candidate => candidate.Classes.Contains("model-picker-dot"));
            if (dot is null)
                continue;

            var isNowSelected = border.Tag is string rowId
                && rowId.Length > 0
                && string.Equals(rowId, selectedId, StringComparison.Ordinal);

            dot.IsVisible = isNowSelected;
            if (isNowSelected)
            {
                if (!border.Classes.Contains("selected"))
                    border.Classes.Add("selected");
            }
            else
            {
                border.Classes.Remove("selected");
            }
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
