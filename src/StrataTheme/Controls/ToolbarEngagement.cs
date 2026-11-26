using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Raises the quiet toolbar of a diagram or chart in a message to its glass look, the <c>:engaged</c> pseudo-class,
/// while a mouse or pen rests on the block or keyboard focus has come into it. The pointer must rest a moment first,
/// so one passing over the block as the conversation scrolls lights nothing. A finger never raises it: on touch the
/// toolbar keeps its quiet look.
/// </summary>
internal sealed class ToolbarEngagement
{
    /// <summary>How long a mouse rests on the block before its toolbar lights.</summary>
    internal static readonly TimeSpan HoverPause = TimeSpan.FromMilliseconds(120);

    private readonly Control _owner;
    private readonly IPseudoClasses _pseudoClasses;
    private IDisposable? _pause;
    private bool _pointerResting;
    private bool _keyboardInside;

    private ToolbarEngagement(Control owner, IPseudoClasses pseudoClasses)
    {
        _owner = owner;
        _pseudoClasses = pseudoClasses;
        owner.PointerEntered += OnPointerEntered;
        owner.PointerExited += OnPointerExited;
        owner.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        owner.PropertyChanged += OnPropertyChanged;
        owner.DetachedFromVisualTree += OnDetached;
    }

    /// <summary>Keeps <paramref name="owner"/>'s <c>:engaged</c> pseudo-class for as long as it lives.</summary>
    public static void Attach(Control owner, IPseudoClasses pseudoClasses) => _ = new ToolbarEngagement(owner, pseudoClasses);

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch || _pointerResting || _pause is not null)
            return;
        _pause = DispatcherTimer.RunOnce(() =>
        {
            _pause = null;
            _pointerResting = true;
            Update();
        }, HoverPause);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch)
            return;
        _pause?.Dispose();
        _pause = null;
        _pointerResting = false;
        Update();
    }

    // Focus a pointer gives a button stays put after the pointer leaves, so only keyboard focus raises the toolbar.
    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional) || _keyboardInside)
            return;
        _keyboardInside = true;
        Update();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != InputElement.IsKeyboardFocusWithinProperty || _owner.IsKeyboardFocusWithin || !_keyboardInside)
            return;
        _keyboardInside = false;
        Update();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _pause?.Dispose();
        _pause = null;
        _pointerResting = _keyboardInside = false;
        Update();
    }

    private void Update() => _pseudoClasses.Set(":engaged", _pointerResting || _keyboardInside);
}
