using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StrataTheme.Controls;

/// <summary>
/// Controls touch and pen text selection without changing desktop mouse selection.
/// </summary>
public sealed class StrataTextSelection
{
    private const double DragThreshold = 4;
    private static readonly ConditionalWeakTable<SelectableTextBlock, GuardState> States = new();

    public static readonly AttachedProperty<bool> IsTouchSelectionEnabledProperty =
        AvaloniaProperty.RegisterAttached<StrataTextSelection, StyledElement, bool>(
            "IsTouchSelectionEnabled",
            true,
            inherits: true);

    static StrataTextSelection()
    {
        IsTouchSelectionEnabledProperty.Changed.AddClassHandler<SelectableTextBlock>(
            static (textBlock, _) => Update(textBlock));
    }

    private StrataTextSelection()
    {
    }

    public static bool GetIsTouchSelectionEnabled(StyledElement element) =>
        element.GetValue(IsTouchSelectionEnabledProperty);

    public static void SetIsTouchSelectionEnabled(StyledElement element, bool value) =>
        element.SetValue(IsTouchSelectionEnabledProperty, value);

    private static void Update(SelectableTextBlock textBlock)
    {
        var state = States.GetValue(textBlock, static target => new GuardState(target));
        state.SetSuppressing(!GetIsTouchSelectionEnabled(textBlock));
    }

    private sealed class GuardState
    {
        private readonly SelectableTextBlock _textBlock;
        private IPointer? _pointer;
        private Point _origin;
        private bool _dragging;
        private bool _isSuppressing;

        public GuardState(SelectableTextBlock textBlock) => _textBlock = textBlock;

        public void SetSuppressing(bool suppress)
        {
            if (_isSuppressing == suppress)
                return;

            _isSuppressing = suppress;
            if (suppress)
            {
                _textBlock.AddHandler(
                    InputElement.PointerPressedEvent,
                    OnPointerPressed,
                    RoutingStrategies.Tunnel,
                    handledEventsToo: true);
                _textBlock.AddHandler(
                    InputElement.PointerMovedEvent,
                    OnPointerMoved,
                    RoutingStrategies.Tunnel,
                    handledEventsToo: true);
                _textBlock.AddHandler(
                    InputElement.PointerReleasedEvent,
                    OnPointerReleased,
                    RoutingStrategies.Tunnel,
                    handledEventsToo: true);
                _textBlock.AddHandler(
                    InputElement.PointerCaptureLostEvent,
                    OnPointerCaptureLost,
                    RoutingStrategies.Direct,
                    handledEventsToo: true);
                ClearSelection();
                return;
            }

            _textBlock.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            _textBlock.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            _textBlock.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            _textBlock.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
            ResetPointer();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Pointer.Type is not (PointerType.Touch or PointerType.Pen))
                return;

            _pointer = e.Pointer;
            _origin = e.GetPosition(_textBlock);
            _dragging = false;
            ClearSelection();
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!ReferenceEquals(_pointer, e.Pointer))
                return;

            var position = e.GetPosition(_textBlock);
            var dx = position.X - _origin.X;
            var dy = position.Y - _origin.Y;
            _dragging |= (dx * dx) + (dy * dy) >= DragThreshold * DragThreshold;
            if (!_dragging)
                return;

            ClearSelection();
            e.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!ReferenceEquals(_pointer, e.Pointer))
                return;

            if (_dragging)
            {
                ClearSelection();
                e.Handled = true;
            }

            ResetPointer();
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!ReferenceEquals(_pointer, e.Pointer))
                return;

            ClearSelection();
            ResetPointer();
        }

        private void ClearSelection()
        {
            var length = _textBlock.Text?.Length ?? 0;
            var caret = Math.Clamp(Math.Min(_textBlock.SelectionStart, _textBlock.SelectionEnd), 0, length);
            _textBlock.SelectionStart = caret;
            _textBlock.SelectionEnd = caret;
        }

        private void ResetPointer()
        {
            _pointer = null;
            _dragging = false;
        }
    }
}
