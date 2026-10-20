using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

public sealed class InteractivePickerEventArgs(bool isPresented) : RoutedEventArgs(StrataInteractiveBlock.PickerPresentationChangedEvent)
{
    public bool IsPresented { get; } = isPresented;
}

public partial class StrataInteractiveBlock
{
    public static readonly StyledProperty<Panel?> PickerHostProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, Panel?>(nameof(PickerHost));
    /// <summary>An optional local modal layer, such as a simulated device surface.</summary>
    public Panel? PickerHost { get => GetValue(PickerHostProperty); set => SetValue(PickerHostProperty, value); }
    public static readonly RoutedEvent<InteractivePickerEventArgs> PickerPresentationChangedEvent =
        RoutedEvent.Register<StrataInteractiveBlock, InteractivePickerEventArgs>(
            "PickerPresentationChanged", RoutingStrategies.Bubble);
    private StrataBottomSheet? _picker;
    private Panel? _pickerLayer;
    private IInputElement? _pickerPreviousFocus;

    private void ShowPicker(string title, Control content, string? description = null)
    {
        var layer = PickerHost ?? OverlayLayer.GetOverlayLayer(this);
        if (layer is null)
        {
            SetError(_labels["unavailable"]);
            return;
        }
        ClosePicker();
        var top = TopLevel.GetTopLevel(this);
        var safe = top?.InsetsManager?.SafeAreaPadding ?? default;
        _pickerPreviousFocus = top?.FocusManager?.GetFocusedElement();
        _pickerLayer = layer;
        var picker = new StrataBottomSheet
        {
            Name = "InteractionPicker",
            Classes = { "touch-picker" },
            Title = title,
            Description = description,
            ShowCloseButton = true,
            CloseLabel = _labels["close"],
            Width = layer.Bounds.Width,
            Height = layer.Bounds.Height,
            ZIndex = int.MaxValue,
            SheetMargin = new Thickness(12 + safe.Left, 12 + safe.Top, 12 + safe.Right, 12 + safe.Bottom),
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, layer.Bounds.Height * 0.65) }
        };
        _picker = picker;
        if (layer is Grid grid)
        {
            Grid.SetRowSpan(_picker, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumnSpan(_picker, Math.Max(1, grid.ColumnDefinitions.Count));
        }
        layer.SizeChanged += OnPickerLayerSizeChanged;
        picker.PresentationChanged += (_, _) =>
        {
            if (ReferenceEquals(_picker, picker))
                RaiseEvent(new InteractivePickerEventArgs(picker.IsPresented));
        };
        picker.Closed += (_, _) => ClosePicker();
        layer.AddHandler(KeyDownEvent, OnPickerKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyboardNavigation.SetTabNavigation(_picker, KeyboardNavigationMode.Cycle);
        layer.Children.Add(_picker);
        _picker.IsOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_picker is not null)
                content.GetVisualDescendants().OfType<Control>()
                    .FirstOrDefault(control => control.Focusable && control.IsEffectivelyVisible)?.Focus();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnPickerLayerSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_picker is null) return;
        _picker.Width = args.NewSize.Width;
        _picker.Height = args.NewSize.Height;
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        ClosePicker();
        args.Handled = true;
    }

    /// <summary>Closes this control's open picker sheet, if any, for example on system back navigation.</summary>
    public void ClosePicker()
    {
        if (_picker is not { } picker) return;
        var layer = _pickerLayer!;
        _picker = null;
        _pickerLayer = null;
        picker.IsOpen = false;
        layer.SizeChanged -= OnPickerLayerSizeChanged;
        layer.RemoveHandler(KeyDownEvent, OnPickerKeyDown);
        if (picker.IsPresented)
            picker.PresentationChanged += RemoveAfterExit;
        else
            layer.Children.Remove(picker);
        RaiseEvent(new InteractivePickerEventArgs(false));
        _pickerPreviousFocus?.Focus();
        _pickerPreviousFocus = null;

        void RemoveAfterExit(object? sender, RoutedEventArgs args)
        {
            if (picker.IsPresented)
                return;
            picker.PresentationChanged -= RemoveAfterExit;
            if (layer.IsAttachedToVisualTree())
                layer.Children.Remove(picker);
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => layer.Children.Remove(picker));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // A picker layer that is detaching with this control is iterating its children right now,
        // so the sheet is removed once that pass completes.
        if (_pickerLayer?.IsAttachedToVisualTree() == false)
            Avalonia.Threading.Dispatcher.UIThread.Post(ClosePicker);
        else
            ClosePicker();
        base.OnDetachedFromVisualTree(e);
    }
}
