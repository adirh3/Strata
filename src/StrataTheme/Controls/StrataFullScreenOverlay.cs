using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace StrataTheme.Controls;

/// <summary>Hosts a full-screen viewer above its owner's window, including system Back handling.</summary>
public sealed class StrataFullScreenOverlay
{
    private static readonly ConditionalWeakTable<TopLevel, List<StrataFullScreenOverlay>> Presented = new();
    private readonly Control _owner;
    private readonly OverlayLayer _layer;
    private readonly Border _overlay;
    private readonly IInputElement? _previousFocus;
    private readonly TopLevel? _topLevel;
    private readonly IInsetsManager? _insets;
    private readonly Window? _window;
    private readonly WindowState _previousWindowState;
    private bool _isOpen = true;

    static StrataFullScreenOverlay()
    {
        // Class handlers run before the host's navigation handler, regardless of subscription order.
        TopLevel.BackRequestedEvent.AddClassHandler<TopLevel>((topLevel, e) =>
        {
            if (!e.Handled && CloseTopmost(topLevel))
                e.Handled = true;
        });
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((topLevel, e) =>
        {
            if (!e.Handled && e.Key == Key.Escape && CloseTopmost(topLevel))
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private StrataFullScreenOverlay(
        Control owner,
        OverlayLayer layer,
        string name,
        Control content,
        Action focusContent)
    {
        _owner = owner;
        _layer = layer;
        _topLevel = TopLevel.GetTopLevel(owner);
        _previousFocus = _topLevel?.FocusManager?.GetFocusedElement();
        _window = _topLevel as Window;
        _insets = _topLevel?.InsetsManager;
        _previousWindowState = _window?.WindowState ?? WindowState.Normal;
        _overlay = new Border
        {
            Name = name,
            Padding = _insets?.SafeAreaPadding ?? default,
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
        _owner.DetachedFromVisualTree += OnOwnerDetached;
        if (_insets is not null)
            _insets.SafeAreaChanged += OnSafeAreaChanged;
        layer.Children.Add(_overlay);
        if (_topLevel is not null)
            Presented.GetOrCreateValue(_topLevel).Add(this);
        if (_window is not null)
            _window.WindowState = WindowState.FullScreen;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isOpen)
                focusContent();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Raised once after the viewer releases its content.</summary>
    public event EventHandler? Closed;

    /// <summary>Whether a full-screen viewer covers the visual's top level.</summary>
    public static bool IsPresented(Visual visual) =>
        (visual as TopLevel ?? TopLevel.GetTopLevel(visual)) is { } topLevel
        && Presented.TryGetValue(topLevel, out var viewers) && viewers.Count > 0;

    /// <summary>Opens a viewer in the owner's overlay layer, or reports a missing layer through logging.</summary>
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

    /// <summary>Closes the viewer and optionally returns focus to the control that opened it.</summary>
    public void Close(bool restoreFocus = true)
    {
        if (!_isOpen)
            return;
        _isOpen = false;
        _owner.DetachedFromVisualTree -= OnOwnerDetached;
        if (_insets is not null)
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
        if (_topLevel is not null && Presented.TryGetValue(_topLevel, out var viewers))
        {
            viewers.Remove(this);
            if (viewers.Count == 0)
                Presented.Remove(_topLevel);
        }
        _layer.Children.Remove(_overlay);
        _overlay.Child = null;
        if (_window is { WindowState: WindowState.FullScreen } window)
            window.WindowState = _previousWindowState;
        Closed?.Invoke(this, EventArgs.Empty);
        Closed = null;
        if (restoreFocus)
            _previousFocus?.Focus();
    }

    private static bool CloseTopmost(TopLevel topLevel)
    {
        if (!Presented.TryGetValue(topLevel, out var viewers) || viewers.Count == 0)
            return false;
        viewers[viewers.Count - 1].Close();
        return true;
    }

    private void OnSafeAreaChanged(object? sender, EventArgs e) =>
        _overlay.Padding = _insets?.SafeAreaPadding ?? default;

    private void OnOwnerDetached(object? sender, VisualTreeAttachmentEventArgs e) =>
        Close(restoreFocus: false);
}
