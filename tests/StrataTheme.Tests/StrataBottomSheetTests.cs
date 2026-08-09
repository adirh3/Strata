using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataBottomSheetTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataBottomSheetTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SpringBack_ReleasesOwnerAndRestoresStyledTransform()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, sheetPart, handlePart) = CreateBottomSheet();
            var styledTransform = new TranslateTransform(0, 12);
            using var styleValue = SetStyledTransform(sheetPart, styledTransform);

            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 140);
            AssertDragTransform(sheetPart, expectedY: 40, styledTransform);

            Release(handlePart, bottomSheet, pointer, new Point(10, 140), 1_030);

            Assert.True(bottomSheet.IsOpen);
            Assert.Null(pointer.Captured);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task DragDismiss_CleansUpAndRaisesClosedOnce()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, sheetPart, handlePart) = CreateBottomSheet();
            var styledTransform = new TranslateTransform(0, 12);
            using var styleValue = SetStyledTransform(sheetPart, styledTransform);
            var closedCount = 0;
            bottomSheet.Closed += (_, _) => closedCount++;

            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 220);
            AssertDragTransform(sheetPart, expectedY: 120, styledTransform);

            Release(handlePart, bottomSheet, pointer, new Point(10, 220), 1_030);
            Move(handlePart, bottomSheet, pointer, new Point(10, 260), 1_040);
            Release(handlePart, bottomSheet, pointer, new Point(10, 260), 1_050);
            bottomSheet.Close();

            Assert.False(bottomSheet.IsOpen);
            Assert.Null(pointer.Captured);
            Assert.Equal(1, closedCount);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task ReleasePositionCanCrossDismissThresholdWithoutAnotherMove()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, _, handlePart) = CreateBottomSheet();
            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 150);

            Release(handlePart, bottomSheet, pointer, new Point(10, 210), 1_030);

            Assert.False(bottomSheet.IsOpen);
            Assert.Null(pointer.Captured);
            window.Close();
        });
    }

    [Fact]
    public async Task StationaryReleasePreservesFlingDismissal()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, _, handlePart) = CreateBottomSheet();
            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 150);

            Release(handlePart, bottomSheet, pointer, new Point(10, 150), 1_030);

            Assert.False(bottomSheet.IsOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task FlingExpiresAfterAStationaryHold()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, _, handlePart) = CreateBottomSheet();
            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 150);

            Release(handlePart, bottomSheet, pointer, new Point(10, 150), 2_030);

            Assert.True(bottomSheet.IsOpen);
            window.Close();
        });
    }

    [Fact]
    public void IsOpenDefaultsToTwoWayBinding()
    {
        Assert.Equal(BindingMode.TwoWay, StrataBottomSheet.IsOpenProperty.GetMetadata<StrataBottomSheet>().DefaultBindingMode);
    }

    [Fact]
    public async Task CompetingPointers_CannotMoveOrReleaseOwnerDrag()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, sheetPart, handlePart) = CreateBottomSheet();
            var styledTransform = new TranslateTransform(0, 12);
            using var styleValue = SetStyledTransform(sheetPart, styledTransform);
            var owner = CreateTouchPointer(isPrimary: true);
            var competing = CreateTouchPointer(isPrimary: false);

            Press(handlePart, bottomSheet, owner, new Point(10, 100), 1_000);
            Press(handlePart, bottomSheet, competing, new Point(10, 110), 1_001);

            Assert.Same(handlePart, owner.Captured);
            Assert.Null(competing.Captured);

            Move(handlePart, bottomSheet, owner, new Point(10, 140), 1_020);
            AssertDragTransform(sheetPart, expectedY: 40, styledTransform);

            Move(handlePart, bottomSheet, competing, new Point(10, 280), 1_021);
            Release(handlePart, bottomSheet, competing, new Point(10, 280), 1_022);

            Assert.True(bottomSheet.IsOpen);
            Assert.Same(handlePart, owner.Captured);
            AssertDragTransform(sheetPart, expectedY: 40, styledTransform);

            Release(handlePart, bottomSheet, owner, new Point(10, 140), 1_030);

            Assert.Null(owner.Captured);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task CaptureLost_CleansUpAndLateEventsCannotRestoreTransform()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, sheetPart, handlePart) = CreateBottomSheet();
            var styledTransform = new TranslateTransform(0, 12);
            using var styleValue = SetStyledTransform(sheetPart, styledTransform);
            var closedCount = 0;
            bottomSheet.Closed += (_, _) => closedCount++;

            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 160);
            AssertDragTransform(sheetPart, expectedY: 60, styledTransform);

            pointer.Capture(null);
            Move(handlePart, bottomSheet, pointer, new Point(10, 260), 1_030);
            Release(handlePart, bottomSheet, pointer, new Point(10, 260), 1_040);

            Assert.True(bottomSheet.IsOpen);
            Assert.Null(pointer.Captured);
            Assert.Equal(0, closedCount);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            var nextPointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 130);
            Release(handlePart, bottomSheet, nextPointer, new Point(10, 130), 2_030);

            Assert.Null(nextPointer.Captured);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task ProgrammaticClose_CleansUpOnceAndReopenAcceptsNewOwner()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, sheetPart, handlePart) = CreateBottomSheet();
            var styledTransform = new TranslateTransform(0, 12);
            using var styleValue = SetStyledTransform(sheetPart, styledTransform);
            var closedCount = 0;
            bottomSheet.Closed += (_, _) => closedCount++;

            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 160);
            bottomSheet.Close();
            bottomSheet.Close();
            Move(handlePart, bottomSheet, pointer, new Point(10, 260), 1_030);
            Release(handlePart, bottomSheet, pointer, new Point(10, 260), 1_040);

            Assert.False(bottomSheet.IsOpen);
            Assert.Null(pointer.Captured);
            Assert.Equal(1, closedCount);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            bottomSheet.IsOpen = true;
            var nextPointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 130);
            AssertDragTransform(sheetPart, expectedY: 30, styledTransform);
            Release(handlePart, bottomSheet, nextPointer, new Point(10, 130), 2_030);

            Assert.True(bottomSheet.IsOpen);
            Assert.Null(nextPointer.Captured);
            Assert.Equal(1, closedCount);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task SettingIsOpenFalse_CleansUpAndReopenAcceptsNewOwner()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, sheetPart, handlePart) = CreateBottomSheet();
            var styledTransform = new TranslateTransform(0, 12);
            using var styleValue = SetStyledTransform(sheetPart, styledTransform);
            var closedCount = 0;
            bottomSheet.Closed += (_, _) => closedCount++;

            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 160);
            bottomSheet.IsOpen = false;
            Move(handlePart, bottomSheet, pointer, new Point(10, 260), 1_030);
            Release(handlePart, bottomSheet, pointer, new Point(10, 260), 1_040);

            Assert.False(bottomSheet.IsOpen);
            Assert.Null(pointer.Captured);
            Assert.Equal(0, closedCount);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            bottomSheet.IsOpen = true;
            var nextPointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 220);
            Release(handlePart, bottomSheet, nextPointer, new Point(10, 220), 2_030);

            Assert.False(bottomSheet.IsOpen);
            Assert.Null(nextPointer.Captured);
            Assert.Equal(1, closedCount);
            Assert.Same(styledTransform, sheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task TemplateReplacement_CleansOldDragAndNewTemplateCanDrag()
    {
        await _fixture.Dispatch(() =>
        {
            var (window, bottomSheet, oldSheetPart, oldHandlePart) = CreateBottomSheet();
            var oldStyledTransform = new TranslateTransform(0, 12);
            using var oldStyleValue = SetStyledTransform(oldSheetPart, oldStyledTransform);

            var oldPointer = BeginDrag(oldHandlePart, bottomSheet, fromY: 100, toY: 160);
            bottomSheet.Template = BuildTemplate();
            bottomSheet.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(oldPointer.Captured);
            Assert.Same(oldStyledTransform, oldSheetPart.RenderTransform);

            var (newSheetPart, newHandlePart) = FindParts(bottomSheet);
            Assert.NotSame(oldSheetPart, newSheetPart);
            Assert.NotSame(oldHandlePart, newHandlePart);

            var newStyledTransform = new TranslateTransform(0, 24);
            using var newStyleValue = SetStyledTransform(newSheetPart, newStyledTransform);
            var newPointer = BeginDrag(newHandlePart, bottomSheet, fromY: 100, toY: 140);
            AssertDragTransform(newSheetPart, expectedY: 40, newStyledTransform);
            Release(newHandlePart, bottomSheet, newPointer, new Point(10, 140), 2_030);

            Assert.Null(newPointer.Captured);
            Assert.Same(newStyledTransform, newSheetPart.RenderTransform);

            window.Close();
        });
    }

    [Fact]
    public async Task ProductionThemeDirectDragMovesTheSheetWithoutTheSettlementTransition()
    {
        await _fixture.Dispatch(() =>
        {
            var bottomSheet = new StrataBottomSheet
            {
                IsOpen = true,
                Content = new TextBlock { Text = "Sheet content" }
            };
            var window = new Window
            {
                Width = 320,
                Height = 480,
            };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = bottomSheet;

            window.Show();
            bottomSheet.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var (sheetPart, handlePart) = FindParts(bottomSheet);
            var motionPart = bottomSheet.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "PART_SheetMotion");
            var settlementTransform = motionPart.RenderTransform;

            var pointer = BeginDrag(handlePart, bottomSheet, fromY: 100, toY: 145);

            var directTransform = Assert.IsType<TranslateTransform>(sheetPart.RenderTransform);
            Assert.Equal(45, directTransform.Y, precision: 3);
            Assert.Same(settlementTransform, motionPart.RenderTransform);

            Release(handlePart, bottomSheet, pointer, new Point(10, 145), 2_000);
            window.Close();
        });
    }

    private static (Window Window, StrataBottomSheet BottomSheet, Border SheetPart, Border HandlePart)
        CreateBottomSheet()
    {
        var bottomSheet = new StrataBottomSheet
        {
            IsOpen = true,
            Template = BuildTemplate(),
        };
        var window = new Window
        {
            Width = 320,
            Height = 480,
            Content = bottomSheet,
        };

        window.Show();
        bottomSheet.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();

        var (sheetPart, handlePart) = FindParts(bottomSheet);
        return (window, bottomSheet, sheetPart, handlePart);
    }

    private static (Border SheetPart, Border HandlePart) FindParts(StrataBottomSheet bottomSheet)
    {
        var sheetPart = bottomSheet.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "PART_Sheet");
        var handlePart = bottomSheet.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "PART_Handle");

        return (sheetPart, handlePart);
    }

    private static FuncControlTemplate<StrataBottomSheet> BuildTemplate() =>
        new((_, scope) =>
        {
            var scrim = new Border { Name = "PART_Scrim" };
            var handle = new Border
            {
                Name = "PART_Handle",
                Width = 320,
                Height = 40,
            };
            var sheet = new Border
            {
                Name = "PART_Sheet",
                Child = handle,
            };

            scope.Register("PART_Scrim", scrim);
            scope.Register("PART_Sheet", sheet);
            scope.Register("PART_Handle", handle);

            return new Panel
            {
                Children =
                {
                    scrim,
                    sheet,
                },
            };
        });

    private static IDisposable? SetStyledTransform(Border sheetPart, TranslateTransform transform) =>
        sheetPart.SetValue(
            Visual.RenderTransformProperty,
            transform,
            BindingPriority.Style);

    private static IPointer BeginDrag(
        Control source,
        Visual root,
        double fromY,
        double toY)
    {
        var pointer = CreateTouchPointer(isPrimary: true);
        Press(source, root, pointer, new Point(10, fromY), 1_000);
        Assert.Same(source, pointer.Captured);
        Move(source, root, pointer, new Point(10, toY), 1_020);
        return pointer;
    }

    private static void AssertDragTransform(
        Border sheetPart,
        double expectedY,
        TranslateTransform styledTransform)
    {
        var dragTransform = Assert.IsType<TranslateTransform>(sheetPart.RenderTransform);
        Assert.Equal(expectedY, dragTransform.Y);
        Assert.NotSame(styledTransform, dragTransform);
    }

    private static Avalonia.Input.Pointer CreateTouchPointer(bool isPrimary) =>
        new(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Touch, isPrimary);

    private static void Press(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        source.RaiseEvent(new PointerPressedEventArgs(
            source,
            pointer,
            root,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));

    private static void Move(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        source.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            source,
            pointer,
            root,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));

    private static void Release(
        Control source,
        Visual root,
        IPointer pointer,
        Point position,
        ulong timestamp) =>
        source.RaiseEvent(new PointerReleasedEventArgs(
            source,
            pointer,
            root,
            position,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));
}
