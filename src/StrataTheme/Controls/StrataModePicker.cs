using System;
using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>How strongly a <see cref="StrataModePicker"/> button draws attention to its mode.</summary>
public enum StrataModeTone
{
    /// <summary>The usual mode: a quiet, icon-only button.</summary>
    Neutral,
    /// <summary>A mode worth noticing, shown with its label in the accent colour.</summary>
    Accent,
    /// <summary>A careful mode, shown with its label in the caution colour.</summary>
    Caution
}

/// <summary>
/// A compact toolbar button for one of a few modes, such as how much an assistant may do on its
/// own. It opens an in-window glass popover listing the modes; picking one runs
/// <see cref="SelectCommand"/> with it. The host templates the modes and may add footer content.
/// </summary>
/// <remarks>
/// <para><b>Template parts:</b> PART_Button (Button), PART_Popup (Popup), PART_List (ListBox).</para>
/// <para><b>Pseudo-classes:</b> :open, :labelled, :accent, :caution.</para>
/// </remarks>
public class StrataModePicker : TemplatedControl
{
    private Button? _button;
    private Popup? _popup;
    private ListBox? _list;
    private bool _syncingSelection;

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<StrataModePicker, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StrataModePicker, string?>(nameof(Label));

    public static readonly StyledProperty<bool> ShowLabelProperty =
        AvaloniaProperty.Register<StrataModePicker, bool>(nameof(ShowLabel));

    public static readonly StyledProperty<StrataModeTone> ToneProperty =
        AvaloniaProperty.Register<StrataModePicker, StrataModeTone>(nameof(Tone));

    public static readonly StyledProperty<string?> ButtonToolTipProperty =
        AvaloniaProperty.Register<StrataModePicker, string?>(nameof(ButtonToolTip));

    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<StrataModePicker, string?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<StrataModePicker, string?>(nameof(Description));

    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<StrataModePicker, IEnumerable?>(nameof(Items));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<StrataModePicker, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<StrataModePicker, object?>(nameof(SelectedItem));

    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<StrataModePicker, ICommand?>(nameof(SelectCommand));

    public static readonly StyledProperty<object?> FooterContentProperty =
        AvaloniaProperty.Register<StrataModePicker, object?>(nameof(FooterContent));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<StrataModePicker, bool>(nameof(IsOpen));

    static StrataModePicker()
    {
        ShowLabelProperty.Changed.AddClassHandler<StrataModePicker>((picker, _) => picker.UpdatePseudoClasses());
        ToneProperty.Changed.AddClassHandler<StrataModePicker>((picker, _) => picker.UpdatePseudoClasses());
        IsOpenProperty.Changed.AddClassHandler<StrataModePicker>((picker, _) => picker.UpdatePseudoClasses());
        SelectedItemProperty.Changed.AddClassHandler<StrataModePicker>((picker, _) => picker.SyncListSelection());
    }

    public StrataModePicker() => UpdatePseudoClasses();

    /// <summary>The current mode's line icon.</summary>
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>The current mode's name.</summary>
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Shows <see cref="Label"/> beside the icon; the usual mode can stay icon-only.</summary>
    public bool ShowLabel { get => GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }

    public StrataModeTone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public string? ButtonToolTip { get => GetValue(ButtonToolTipProperty); set => SetValue(ButtonToolTipProperty, value); }

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    /// <summary>The current mode. The picker never sets it; <see cref="SelectCommand"/> does, through the host.</summary>
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    /// <summary>Runs with the mode the user picked.</summary>
    public ICommand? SelectCommand { get => GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }

    /// <summary>Host content under the modes, such as where the mode comes from and a link to settings.</summary>
    public object? FooterContent { get => GetValue(FooterContentProperty); set => SetValue(FooterContentProperty, value); }

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_button is not null)
            _button.Click -= OnButtonClick;
        if (_list is not null)
            _list.SelectionChanged -= OnListSelectionChanged;
        if (_popup is not null)
            _popup.Closed -= OnPopupClosed;

        _button = e.NameScope.Find<Button>("PART_Button");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _list = e.NameScope.Find<ListBox>("PART_List");
        // A footer action, such as opening settings, takes the user elsewhere: close behind it.
        e.NameScope.Find<ContentPresenter>("PART_Footer")?.AddHandler(
            Button.ClickEvent, (_, _) => IsOpen = false, RoutingStrategies.Bubble, handledEventsToo: true);

        if (_button is not null)
            _button.Click += OnButtonClick;
        if (_list is not null)
            _list.SelectionChanged += OnListSelectionChanged;
        if (_popup is not null)
            _popup.Closed += OnPopupClosed;
        SyncListSelection();
    }

    /// <summary>Closes the popover, for instance after a footer link opened somewhere else.</summary>
    public void Close() => IsOpen = false;

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        SyncListSelection();
        IsOpen = !IsOpen;
    }

    private void OnPopupClosed(object? sender, EventArgs e) => IsOpen = false;

    private void SyncListSelection()
    {
        if (_list is null)
            return;
        _syncingSelection = true;
        _list.SelectedItem = SelectedItem;
        _syncingSelection = false;
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _list?.SelectedItem is not { } picked)
            return;
        if (!Equals(picked, SelectedItem) && SelectCommand is { } command && command.CanExecute(picked))
            command.Execute(picked);
        IsOpen = false;
        // The host decides what is selected; show its answer, not the click.
        SyncListSelection();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":open", IsOpen);
        PseudoClasses.Set(":labelled", ShowLabel);
        PseudoClasses.Set(":accent", Tone == StrataModeTone.Accent);
        PseudoClasses.Set(":caution", Tone == StrataModeTone.Caution);
    }
}
