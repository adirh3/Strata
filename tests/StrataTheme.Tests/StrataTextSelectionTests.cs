using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataTextSelectionTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataTextSelectionTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DisabledTouchSelectionClearsDragSelectionButLeavesMouseAlone()
    {
        await _fixture.Dispatch(() =>
        {
            var textBlock = new SelectableTextBlock { Text = "Selectable text" };
            var host = new Border { Child = textBlock };
            StrataTextSelection.SetIsTouchSelectionEnabled(host, false);
            var window = new Window { Width = 300, Height = 100, Content = host };
            window.Show();

            Assert.False(StrataTextSelection.GetIsTouchSelectionEnabled(textBlock));

            var touch = new Avalonia.Input.Pointer(
                Avalonia.Input.Pointer.GetNextFreeId(),
                PointerType.Touch,
                isPrimary: true);
            textBlock.RaiseEvent(new PointerPressedEventArgs(
                textBlock,
                touch,
                textBlock,
                new Point(10, 10),
                1_000,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None,
                1));
            textBlock.SelectionStart = 0;
            textBlock.SelectionEnd = 8;
            var touchMove = new PointerEventArgs(
                InputElement.PointerMovedEvent,
                textBlock,
                touch,
                textBlock,
                new Point(30, 10),
                1_020,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None);
            textBlock.RaiseEvent(touchMove);

            Assert.True(touchMove.Handled);
            Assert.Equal(textBlock.SelectionStart, textBlock.SelectionEnd);

            var mouse = new Avalonia.Input.Pointer(
                Avalonia.Input.Pointer.GetNextFreeId(),
                PointerType.Mouse,
                isPrimary: true);
            textBlock.SelectionStart = 0;
            textBlock.SelectionEnd = 8;
            var mouseMove = new PointerEventArgs(
                InputElement.PointerMovedEvent,
                textBlock,
                mouse,
                textBlock,
                new Point(30, 10),
                2_000,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None);
            textBlock.RaiseEvent(mouseMove);

            Assert.False(mouseMove.Handled);
            Assert.Equal(8, textBlock.SelectionEnd);
            window.Close();
        });
    }
}
