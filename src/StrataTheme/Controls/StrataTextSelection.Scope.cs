using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

public sealed partial class StrataTextSelection
{
    /// <summary>Joins descendant text blocks into one mouse selection, excluding editable controls.</summary>
    public static readonly AttachedProperty<bool> IsSelectionScopeProperty =
        AvaloniaProperty.RegisterAttached<StrataTextSelection, Control, bool>("IsSelectionScope");

    private static readonly ConditionalWeakTable<Control, SelectionScope> Scopes = new();

    public static bool GetIsSelectionScope(Control control) => control.GetValue(IsSelectionScopeProperty);
    public static void SetIsSelectionScope(Control control, bool value) => control.SetValue(IsSelectionScopeProperty, value);

    internal static void UpdateSelectionScope(Control owner)
    {
        owner.RemoveHandler(InputElement.PointerPressedEvent, OnScopePointerPressed);
        owner.RemoveHandler(InputElement.KeyDownEvent, OnScopeKeyDown);
        if (GetIsSelectionScope(owner) && ReferenceEquals(FindScopeRoot(owner), owner))
        {
            owner.AddHandler(InputElement.PointerPressedEvent, OnScopePointerPressed,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            owner.AddHandler(InputElement.KeyDownEvent, OnScopeKeyDown, RoutingStrategies.Tunnel);
        }
        else
        {
            if (Scopes.TryGetValue(owner, out var scope))
                scope.Clear();
        }
    }

    private static void OnScopePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control owner || !ReferenceEquals(FindScopeRoot(owner), owner))
            return;
        if (Scopes.TryGetValue(owner, out var scope))
        {
            scope.OnPressed(sender, e);
            return;
        }
        var block = e.Source as SelectableTextBlock
            ?? (e.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>();
        var properties = e.GetCurrentPoint(owner).Properties;
        if (e.Pointer.Type == PointerType.Mouse
            && (properties.IsLeftButtonPressed || properties.IsRightButtonPressed)
            && block is not null && IsSelectableSource(block, owner))
            Scopes.GetValue(owner, static root => new SelectionScope(root)).OnPressed(sender, e);
    }

    private static void OnScopeKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control owner || !ReferenceEquals(FindScopeRoot(owner), owner) || e.Source is TextBox)
            return;
        if (Scopes.TryGetValue(owner, out var scope))
        {
            scope.OnKeyDown(sender, e);
            return;
        }
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers == command && e.Key is Key.A or Key.C)
            Scopes.GetValue(owner, static root => new SelectionScope(root)).OnKeyDown(sender, e);
    }

    internal readonly record struct TextRange(SelectableTextBlock Block, int Start, int End)
    {
        public string Text
        {
            get
            {
                var text = Block.Inlines is { Count: > 0 } inlines ? inlines.Text : Block.Text ?? "";
                var start = Math.Clamp(Math.Min(Start, End), 0, text.Length);
                var end = Math.Clamp(Math.Max(Start, End), start, text.Length);
                return CleanSelectionText(text[start..end]);
            }
        }
    }

    internal static string CleanSelectionText(string text) => text
        .Replace("\u2005", "").Replace("\uFFFC", "").Replace("\r", "")
        .Replace("\u2066", "").Replace("\u2067", "").Replace("\u2068", "").Replace("\u2069", "");

    internal static IReadOnlyList<TextRange> GetSelection(Control owner)
    {
        var scope = FindScope(owner);
        if (scope is not null && scope.Selection.Any(range =>
                ReferenceEquals(range.Block, owner) || owner.IsVisualAncestorOf(range.Block)))
            return scope.Selection;
        return TextBlocks(owner).Where(HasSelection)
            .Select(block => new TextRange(block, block.SelectionStart, block.SelectionEnd)).ToArray();
    }

    internal static string? GetSelectedText(Control owner)
    {
        var selection = GetSelection(owner);
        return selection.Count == 0 ? null : string.Join("\n\n", selection.Select(range => range.Text));
    }

    internal static string? GetActiveSelectedText(Control owner)
    {
        var scope = FindScope(owner);
        return scope is not null && scope.Selection.Any(range => owner.IsVisualAncestorOf(range.Block))
            ? string.Join("\n\n", scope.Selection.Select(range => range.Text))
            : null;
    }

    internal static void SelectAll(Control owner)
    {
        if (FindScopeRoot(owner) is { } root)
            Scopes.GetValue(root, static target => new SelectionScope(target)).SelectAll(owner);
    }

    internal static void PreserveSelection(Control owner) => FindScope(owner)?.RestoreSelection();
    internal static Control GetSelectionRoot(Control owner) => FindScopeRoot(owner) ?? owner;
    internal static bool HasActiveSelection(Control owner) => FindScope(owner)?.HasActiveSelection == true;

    internal static bool ContainsSelection(Control owner)
    {
        var scope = FindScope(owner);
        return scope is not null && scope.ContainsSelection(owner);
    }

    private static SelectionScope? FindScope(Control owner)
    {
        return FindScopeRoot(owner) is { } root && Scopes.TryGetValue(root, out var scope) ? scope : null;
    }

    private static Control? FindScopeRoot(Control owner)
    {
        Control? result = null;
        for (Visual? current = owner; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && GetIsSelectionScope(control))
                result = control;
        }
        return result;
    }

    private static bool HasSelection(SelectableTextBlock block) =>
        block.SelectionStart != block.SelectionEnd && !string.IsNullOrEmpty(block.SelectedText);

    private static bool IsSelectableSource(SelectableTextBlock block, Control root) =>
        !block.GetVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, root))
            .Any(ancestor => ancestor is TextBox or Button or ToggleButton or Slider);

    private static List<SelectableTextBlock> TextBlocks(Control root) =>
        root.GetVisualDescendants().Prepend(root).OfType<SelectableTextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Bounds.Width > 0 && block.Bounds.Height > 0
                && IsSelectableSource(block, root))
            .ToList();

    private sealed class SelectionScope
    {
        private readonly Control _root;
        private readonly List<TextRange> _selection = new();
        private List<SelectableTextBlock>? _blocks;
        private DispatcherTimer? _scrollTimer;
        private SelectableTextBlock? _anchor;
        private int _anchorIndex;
        private IPointer? _pointer;
        private Point _pointerPosition;
        private bool _dragging;
        private ScrollViewer? _scroll;

        public SelectionScope(Control root)
        {
            _root = root;
            root.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            root.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            root.AddHandler(InputElement.PointerReleasedEvent, OnNativeReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
            root.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            root.DetachedFromVisualTree += (_, _) => Clear();
            root.DataContextChanged += (_, _) => Clear();
            root.LayoutUpdated += (_, _) => _blocks = null;
        }

        public IReadOnlyList<TextRange> Selection => _selection;
        public bool HasActiveSelection => _pointer is not null || _selection.Count > 0;
        private bool IsActive => ReferenceEquals(FindScope(_root), this);
        private List<SelectableTextBlock> Blocks => _blocks ??= TextBlocks(_root);

        public void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsActive || e.Pointer.Type != PointerType.Mouse)
                return;
            var properties = e.GetCurrentPoint(_root).Properties;
            if (properties.IsRightButtonPressed)
            {
                if (_selection.Count == 0)
                    CaptureSelection();
                return;
            }
            if (!properties.IsLeftButtonPressed)
                return;

            var block = e.Source as SelectableTextBlock
                ?? (e.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>();
            if (block is null || !Blocks.Contains(block))
            {
                if (e.Source is Visual source && !source.GetVisualAncestors().Prepend(source)
                        .TakeWhile(ancestor => !ReferenceEquals(ancestor, _root))
                        .Any(ancestor => ancestor is Button or TextBox or MenuItem or ScrollBar))
                    Clear();
                return;
            }

            _pointerPosition = e.GetPosition(_root);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _anchor is not null)
            {
                _pointer = e.Pointer;
                _dragging = true;
                e.Pointer.Capture(_root);
                UpdateRange(_pointerPosition, block);
                e.Handled = true;
            }
            else
            {
                Clear(includeUntracked: true);
                SetAnchor(block);
                _anchorIndex = IndexAt(block, e.GetPosition(block));
                _pointer = e.Pointer;
            }
            _scroll = FindScrollOwner(block);
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            if (!IsActive || !ReferenceEquals(_pointer, e.Pointer) || _anchor is null
                || !e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed)
                return;
            var position = e.GetPosition(_root);
            if (position == _pointerPosition)
                return;
            _pointerPosition = position;
            var target = ClosestBlock(Blocks, _pointerPosition);
            if (target is not null)
                _scroll = FindScrollOwner(target);
            StartAutoScroll();
            if (!_dragging && ReferenceEquals(target, _anchor))
                return;
            _dragging = true;
            e.Pointer.Capture(_root);
            UpdateRange(_pointerPosition, target);
            e.Handled = true;
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!IsActive || !ReferenceEquals(_pointer, e.Pointer) || !_dragging)
                return;
            var position = e.GetPosition(_root);
            var end = ClosestBlock(Blocks, position);
            UpdateRange(position, end);
            e.Handled = true;
            StopDragging();

            var message = _anchor?.FindAncestorOfType<StrataChatMessage>();
            if (message is not null && end is not null
                && _selection.All(range => message.IsVisualAncestorOf(range.Block)))
            {
                var releasePoint = e.GetPosition(end);
                Dispatcher.UIThread.Post(() => message.ShowSelectionReply(end, releasePoint), DispatcherPriority.Background);
            }
        }

        private void OnNativeReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!IsActive || !ReferenceEquals(_pointer, e.Pointer) || e.InitialPressMouseButton != MouseButton.Left)
                return;
            CaptureSelection();
            StopDragging();
        }

        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (IsActive && _selection.Count > 0 && _pointer is null)
                RestoreSelection();
        }

        private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (ReferenceEquals(_pointer, e.Pointer))
                StopDragging();
        }

        public async void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!IsActive || e.Source is TextBox)
                return;
            var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.KeyModifiers == command && e.Key == Key.A)
            {
                var message = e.Source as StrataChatMessage
                    ?? (e.Source as Visual)?.FindAncestorOfType<StrataChatMessage>();
                SelectAll(message ?? _root);
                e.Handled = true;
            }
            else if (e.KeyModifiers == command && e.Key == Key.C && GetSelection(_root).Count > 0)
            {
                e.Handled = true;
                try
                {
                    var clipboard = TopLevel.GetTopLevel(_root)?.Clipboard
                        ?? throw new InvalidOperationException("The system clipboard is unavailable.");
                    using var data = ChatClipboardData.CreateRichText(SelectionMarkdown(), _root);
                    await clipboard.SetDataAsync(data);
                }
                catch (Exception error)
                {
                    Logger.TryGet(LogEventLevel.Error, "Clipboard")?.Log(
                        _root, "Could not copy selected text: {Error}", error.Message);
                }
            }
            else if (e.Key == Key.Escape && _selection.Count > 0)
            {
                Clear(includeUntracked: true);
                e.Handled = true;
            }
        }

        private string SelectionMarkdown() => ChatClipboardData.SelectionMarkdown(GetSelection(_root));

        public void SelectAll(Control owner)
        {
            Clear(includeUntracked: true);
            var blocks = TextBlocks(owner);
            if (blocks.Count == 0)
                return;
            blocks[0].Focus();
            SetAnchor(blocks[0]);
            _anchorIndex = 0;
            foreach (var block in blocks)
            {
                var end = TextLength(block);
                block.SelectionStart = 0;
                block.SelectionEnd = end;
                _selection.Add(new TextRange(block, 0, end));
            }
        }

        public void RestoreSelection()
        {
            foreach (var range in _selection)
            {
                var length = TextLength(range.Block);
                range.Block.SelectionStart = Math.Clamp(range.Start, 0, length);
                range.Block.SelectionEnd = Math.Clamp(range.End, 0, length);
            }
        }

        public bool ContainsSelection(Control owner) =>
            HasActiveSelection
            && (_anchor is not null && owner.IsVisualAncestorOf(_anchor)
                || _selection.Any(range => owner.IsVisualAncestorOf(range.Block)));

        private void SetAnchor(SelectableTextBlock? block)
        {
            if (_anchor is not null)
                _anchor.DetachedFromVisualTree -= OnAnchorDetached;
            _anchor = block;
            if (_anchor is not null)
                _anchor.DetachedFromVisualTree += OnAnchorDetached;
        }

        private void OnAnchorDetached(object? sender, VisualTreeAttachmentEventArgs e) => Clear();

        private void CaptureSelection()
        {
            _selection.Clear();
            foreach (var block in Blocks.Where(HasSelection))
                _selection.Add(new TextRange(block, block.SelectionStart, block.SelectionEnd));
        }

        private void UpdateRange(Point position, SelectableTextBlock? target)
        {
            var blocks = Blocks;
            var anchorIndex = _anchor is null ? -1 : blocks.IndexOf(_anchor);
            var targetIndex = target is null ? -1 : blocks.IndexOf(target);
            if (anchorIndex < 0 || targetIndex < 0)
                return;
            var caret = IndexAt(target!, _root.TranslatePoint(position, target!) ?? default);
            var forward = anchorIndex < targetIndex || anchorIndex == targetIndex && _anchorIndex <= caret;
            var first = Math.Min(anchorIndex, targetIndex);
            var last = Math.Max(anchorIndex, targetIndex);
            for (var index = 0; index < blocks.Count; index++)
            {
                if ((index < first || index > last) && HasSelection(blocks[index]))
                {
                    blocks[index].SelectionStart = 0;
                    blocks[index].SelectionEnd = 0;
                }
            }
            _selection.Clear();
            for (var index = first; index <= last; index++)
            {
                var block = blocks[index];
                var start = index == anchorIndex ? _anchorIndex : forward ? 0 : TextLength(block);
                var end = index == targetIndex ? caret : forward ? TextLength(block) : 0;
                block.SelectionStart = Math.Clamp(start, 0, TextLength(block));
                block.SelectionEnd = Math.Clamp(end, 0, TextLength(block));
                if (HasSelection(block))
                    _selection.Add(new TextRange(block, block.SelectionStart, block.SelectionEnd));
            }
        }

        private SelectableTextBlock? ClosestBlock(List<SelectableTextBlock> blocks, Point position)
        {
            SelectableTextBlock? closest = null;
            var minimum = double.MaxValue;
            foreach (var block in blocks)
            {
                if (HitBounds(block) is not { } bounds)
                    continue;
                var dx = Math.Max(Math.Max(bounds.Left - position.X, 0), position.X - bounds.Right);
                var dy = Math.Max(Math.Max(bounds.Top - position.Y, 0), position.Y - bounds.Bottom);
                var distance = dx * dx + dy * dy;
                if (distance < minimum)
                {
                    minimum = distance;
                    closest = block;
                }
            }
            return closest;
        }

        private Rect? HitBounds(SelectableTextBlock block)
        {
            if (block.TranslatePoint(default, _root) is not { } origin)
                return null;
            var bounds = new Rect(origin, block.Bounds.Size);
            foreach (var ancestor in block.GetVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, _root)))
            {
                if (!ancestor.ClipToBounds)
                    continue;
                if (ancestor.TranslatePoint(default, _root) is not { } clipOrigin)
                    return null;
                bounds = bounds.Intersect(new Rect(clipOrigin, ancestor.Bounds.Size));
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    return null;
            }
            return bounds;
        }

        private ScrollViewer? FindScrollOwner(SelectableTextBlock block) =>
            block.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(scroll =>
                scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                && scroll.Viewport.Height > 0 && scroll.Extent.Height > scroll.Viewport.Height)
            ?? _root.FindAncestorOfType<ScrollViewer>();

        private static int IndexAt(SelectableTextBlock block, Point point)
        {
            var hit = block.TextLayout.HitTestPoint(point - new Vector(block.Padding.Left, block.Padding.Top));
            return Math.Clamp(hit.TextPosition, 0, TextLength(block));
        }

        private static int TextLength(SelectableTextBlock block) =>
            block.Inlines is { Count: > 0 } inlines ? inlines.Text.Length : block.Text?.Length ?? 0;

        private void StartAutoScroll()
        {
            if (_scroll is null)
                return;
            if (_scrollTimer is null)
            {
                _scrollTimer = new DispatcherTimer(DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _scrollTimer.Tick += (_, _) => AutoScroll();
            }
            _scrollTimer.Start();
        }

        private void AutoScroll()
        {
            if (_pointer is null || _scroll is null || _anchor is null
                || _root.TranslatePoint(_pointerPosition, _scroll) is not { } position)
                return;
            const double edge = 24;
            var delta = position.Y < edge ? Math.Max(-28, (position.Y - edge) / 2)
                : position.Y > _scroll.Viewport.Height - edge
                    ? Math.Min(28, (position.Y - _scroll.Viewport.Height + edge) / 2) : 0;
            var offset = Math.Clamp(_scroll.Offset.Y + delta, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height));
            if (Math.Abs(offset - _scroll.Offset.Y) < 0.1)
                return;
            _dragging = true;
            _pointer.Capture(_root);
            if (_scroll.IsVisualAncestorOf(_root))
                _pointerPosition = new Point(_pointerPosition.X, _pointerPosition.Y + offset - _scroll.Offset.Y);
            _scroll.Offset = new Vector(_scroll.Offset.X, offset);
            _blocks = null;
            UpdateRange(_pointerPosition, ClosestBlock(Blocks, _pointerPosition));
        }

        private void StopDragging()
        {
            _scrollTimer?.Stop();
            var pointer = _pointer;
            _pointer = null;
            _scroll = null;
            _dragging = false;
            if (ReferenceEquals(pointer?.Captured, _root))
                pointer.Capture(null);
        }

        public void Clear(bool includeUntracked = false)
        {
            StopDragging();
            foreach (var range in _selection)
            {
                range.Block.SelectionStart = 0;
                range.Block.SelectionEnd = 0;
            }
            _selection.Clear();
            SetAnchor(null);
            if (includeUntracked)
            {
                foreach (var block in Blocks.Where(HasSelection))
                {
                    block.SelectionStart = 0;
                    block.SelectionEnd = 0;
                }
            }
            _blocks = null;
        }
    }
}
