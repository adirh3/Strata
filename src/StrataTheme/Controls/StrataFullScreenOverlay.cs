using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace StrataTheme.Controls;

internal sealed class StrataFullScreenOverlay
{
    private readonly Control _owner;
    private readonly OverlayLayer _layer;
    private readonly Border _overlay;
    private readonly IInputElement? _previousFocus;
    private readonly Window? _window;
    private readonly WindowState _previousWindowState;
    private bool _isOpen = true;

    private StrataFullScreenOverlay(
        Control owner,
        OverlayLayer layer,
        string name,
        Control content,
        Action focusContent)
    {
        _owner = owner;
        _layer = layer;
        _previousFocus = TopLevel.GetTopLevel(owner)?.FocusManager?.GetFocusedElement();
        _window = TopLevel.GetTopLevel(owner) as Window;
        _previousWindowState = _window?.WindowState ?? WindowState.Normal;
        _overlay = new Border
        {
            Name = name,
            Background = owner.TryFindResource("Brush.Background", owner.ActualThemeVariant, out var resource)
                && resource is IBrush brush
                    ? brush
                    : new SolidColorBrush(Color.Parse("#161616")),
            Child = new ThemeVariantScope
            {
                RequestedThemeVariant = owner.ActualThemeVariant,
                Child = content
            },
            ZIndex = int.MaxValue
        };
        _overlay.Bind(Control.WidthProperty, new Binding("Bounds.Width") { Source = layer });
        _overlay.Bind(Control.HeightProperty, new Binding("Bounds.Height") { Source = layer });
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);
        _overlay.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _owner.DetachedFromVisualTree += OnOwnerDetached;
        layer.Children.Add(_overlay);
        if (_window is not null)
            _window.WindowState = WindowState.FullScreen;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isOpen)
                focusContent();
        }, DispatcherPriority.Loaded);
    }

    public event EventHandler? Closed;

    public static StrataFullScreenOverlay? Open(
        Control owner,
        string name,
        Control content,
        Action focusContent)
    {
        var layer = OverlayLayer.GetOverlayLayer(owner);
        if (layer is null)
        {
            Avalonia.Logging.Logger.TryGet(Avalonia.Logging.LogEventLevel.Warning, "Control")
                ?.Log(owner, "The full-screen viewer requires a top-level overlay layer.");
            return null;
        }

        return new StrataFullScreenOverlay(owner, layer, name, content, focusContent);
    }

    public void Close(bool restoreFocus = true)
    {
        if (!_isOpen)
            return;
        _isOpen = false;
        _owner.DetachedFromVisualTree -= OnOwnerDetached;
        _overlay.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _layer.Children.Remove(_overlay);
        _overlay.Child = null;
        if (_window is { WindowState: WindowState.FullScreen } window)
            window.WindowState = _previousWindowState;
        Closed?.Invoke(this, EventArgs.Empty);
        Closed = null;
        if (restoreFocus)
            _previousFocus?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Close();
        e.Handled = true;
    }

    private void OnOwnerDetached(object? sender, VisualTreeAttachmentEventArgs e) =>
        Close(restoreFocus: false);
}
