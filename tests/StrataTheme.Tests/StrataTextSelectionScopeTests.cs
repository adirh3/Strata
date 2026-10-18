using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataTextSelectionScopeTests(AvaloniaFixture fixture)
{
    [Fact]
    public async Task StandaloneScope_RespectsAnExplicitOptOut()
    {
        await fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown { Markdown = "**Native** selection", IsInline = true };
            StrataTextSelection.SetIsSelectionScope(markdown, false);
            var window = Show(markdown);
            try
            {
                await Task.Delay(80);
                Dispatcher.UIThread.RunJobs();
                var text = Assert.Single(markdown.GetVisualDescendants().OfType<SelectableTextBlock>());
                text.Focus();
                text.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = CommandModifier
                });
                Assert.False(StrataTextSelection.GetIsSelectionScope(markdown));
                Assert.Null(ExistingScope(markdown));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ClickingButtonContent_DoesNotInitializeTranscriptSelection()
    {
        await fixture.Dispatch(() =>
        {
            var label = new SelectableTextBlock { Text = "An interactive card" };
            var root = new StackPanel { Children = { new Button { Content = label } } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                var pointer = NewPointer();
                label.RaiseEvent(new PointerPressedEventArgs(label, pointer, window, PointAt(window, label, 2), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None));
                Assert.Null(ExistingScope(root));
                Release(window, pointer, label, PointAt(window, label, 2));
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.C)]
    public async Task EditableCommands_DoNotInitializeTranscriptSelection(Key key)
    {
        await fixture.Dispatch(async () =>
        {
            var input = new TextBox { Text = "draft" };
            var root = new StackPanel { Children = { input, new SelectableTextBlock { Text = "Transcript" } } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                input.Focus();
                input.SelectionStart = 0;
                input.SelectionEnd = 5;
                input.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = CommandModifier
                });
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Null(ExistingScope(root));
                Assert.Equal("draft", input.SelectedText);
                if (key == Key.C)
                    Assert.Equal("draft", await window.Clipboard!.TryGetTextAsync());
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeState_IsLazyAndSharedByTheOutermostOwner(bool transcript)
    {
        await fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown { Markdown = "**Lazy** selection", IsInline = true };
            var message = new StrataChatMessage { Content = markdown };
            Control host = message;
            if (transcript)
            {
                host = new StackPanel { Children = { message } };
                StrataTextSelection.SetIsSelectionScope(host, true);
            }
            var window = Show(host);
            try
            {
                await Task.Delay(80);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(ExistingScope(host));
                Assert.Null(ExistingScope(message));
                Assert.Null(ExistingScope(markdown));
                Assert.False(StrataTextSelection.HasActiveSelection(host));
                Assert.False(StrataTextSelection.ContainsSelection(message));
                Assert.Null(StrataTextSelection.GetSelectedText(message));
                Assert.Null(ExistingScope(host));

                var text = Assert.Single(markdown.GetVisualDescendants().OfType<SelectableTextBlock>());
                text.Focus();
                text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                Assert.Null(ExistingScope(host));

                text.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = CommandModifier
                });
                var scope = ExistingScope(host);
                Assert.NotNull(scope);
                Assert.Null(ExistingScope(markdown));
                if (transcript)
                    Assert.Null(ExistingScope(message));
                Assert.Equal("Lazy selection", StrataTextSelection.GetSelectedText(message));
                Assert.Null(scope.GetType().GetField("_scrollTimer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope));
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Drag_SelectsAcrossTextCodeAndText_InEitherDirection(bool reverse)
    {
        await fixture.Dispatch(() =>
        {
            var first = new SelectableTextBlock { Text = "Alpha bravo" };
            var code = new StrataCodeBlock { Text = "Console.WriteLine(1);", Language = "csharp" };
            var last = new SelectableTextBlock { Text = "Charlie delta" };
            var message = new StrataChatMessage
            {
                Content = new StackPanel { Spacing = 12, Children = { first, code, last } }
            };
            var window = Show(message);
            try
            {
                if (reverse)
                    Drag(window, last, 7, first, 6);
                else
                    Drag(window, first, 6, last, 7);

                Assert.Equal("bravo", first.SelectedText);
                Assert.Equal("Console.WriteLine(1);",
                    Assert.Single(code.GetVisualDescendants().OfType<SelectableTextBlock>()).SelectedText);
                Assert.Equal("Charlie", last.SelectedText);
                Assert.Equal("bravo\n\nConsole.WriteLine(1);\n\nCharlie",
                    ChatContentExtractor.ExtractSelectedText(message.Content));
                Assert.Contains("```csharp\nConsole.WriteLine(1);\n```",
                    ChatClipboardData.SelectionMarkdown(StrataTextSelection.GetSelection(message)));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Drag_PastClippedContent_EndsInTheVisibleParagraph()
    {
        await fixture.Dispatch(() =>
        {
            var first = new SelectableTextBlock { Text = "Alpha bravo" };
            var hidden = new SelectableTextBlock { Text = "Clipped text overlaps the next paragraph", Height = 40 };
            var last = new SelectableTextBlock { Text = "Charlie delta", Height = 40 };
            var clipped = new Border
            {
                Height = 40,
                ClipToBounds = true,
                Child = new StackPanel
                {
                    Children = { new SelectableTextBlock { Text = "Visible inset", Height = 40 }, hidden }
                }
            };
            var root = new StackPanel { Children = { first, clipped, last } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                Assert.Equal(hidden.TranslatePoint(default, root)!.Value.Y, last.TranslatePoint(default, root)!.Value.Y);
                Drag(window, first, 6, last, 7);
                Assert.Equal("bravo", first.SelectedText);
                Assert.Equal("Charlie", last.SelectedText);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("# Keep the hash")]
    [InlineData("~~Keep the tildes~~")]
    public async Task CopyingLiteralText_PreservesMarkdownCharacters(string text)
    {
        await fixture.Dispatch(() =>
        {
            var block = new SelectableTextBlock { Text = text };
            var markdown = ChatClipboardData.SelectionMarkdown([new(block, 0, text.Length)]);
            using var data = ChatClipboardData.CreateRichText(markdown);
            Assert.Equal(text, Assert.Single(data.Items).TryGetRaw(DataFormat.Text));
        });
    }

    [Fact]
    public async Task Drag_CrossesMessageBoundaries_AndSelectAllStaysWithinTheFocusedMessage()
    {
        await fixture.Dispatch(() =>
        {
            var first = new SelectableTextBlock { Text = "Alpha bravo" };
            var second = new SelectableTextBlock { Text = "Charlie delta" };
            var firstMessage = new StrataChatMessage { Content = first };
            var secondMessage = new StrataChatMessage { Content = second };
            var root = new StackPanel { Spacing = 12, Children = { firstMessage, secondMessage } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                Drag(window, first, 6, second, 7);
                Assert.NotNull(ExistingScope(root));
                Assert.Null(ExistingScope(firstMessage));
                Assert.Null(ExistingScope(secondMessage));
                Assert.Equal("bravo\n\nCharlie", StrataTextSelection.GetSelectedText(secondMessage));

                first.Focus();
                first.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = CommandModifier
                });
                Assert.Equal(first.Text, first.SelectedText);
                Assert.True(string.IsNullOrEmpty(second.SelectedText));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task RightPress_AndFocusChange_PreserveAllHighlights()
    {
        await fixture.Dispatch(() =>
        {
            var first = new SelectableTextBlock { Text = "Alpha bravo" };
            var last = new SelectableTextBlock { Text = "Charlie delta" };
            var message = new StrataChatMessage
            {
                Content = new StackPanel { Spacing = 12, Children = { first, last } }
            };
            var window = Show(message);
            try
            {
                Drag(window, first, 6, last, 7);
                var pointer = NewPointer();
                var press = new PointerPressedEventArgs(last, pointer, window, PointAt(window, last, 3), 3,
                    new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
                    KeyModifiers.None);
                last.RaiseEvent(press);
                Assert.True(press.Handled);
                message.Focus();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("bravo", first.SelectedText);
                Assert.Equal("Charlie", last.SelectedText);
                Assert.Equal("bravo\n\nCharlie", StrataTextSelection.GetSelectedText(message));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task KeyboardCopy_CopiesTheWholeRange_WithHtmlAndReadableText()
    {
        await fixture.Dispatch(async () =>
        {
            var first = new SelectableTextBlock { Text = "Alpha bravo" };
            var last = new SelectableTextBlock { Text = "Charlie delta" };
            var root = new StackPanel { Spacing = 12, Children = { first, last } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                Drag(window, first, 6, last, 7);
                first.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = CommandModifier
                });
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal("bravo\n\nCharlie", await window.Clipboard!.TryGetTextAsync());
                using var data = await window.Clipboard.TryGetDataAsync();
                Assert.Contains(data!.Formats, format => format.Identifier == "text/html");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task StandaloneMarkdown_SelectionKeepsEmphasisLinksAndCode()
    {
        await fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = "Before **bold** [link](https://example.com).\n\n```csharp\nvar value = 1;\n```\n\nAfter",
                IsInline = true
            };
            var window = Show(markdown);
            try
            {
                await Task.Delay(80);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(ExistingScope(markdown));
                StrataTextSelection.SelectAll(markdown);
                Assert.NotNull(ExistingScope(markdown));
                var selected = ChatClipboardData.SelectionMarkdown(StrataTextSelection.GetSelection(markdown));
                Assert.Contains("**bold**", selected);
                Assert.Contains("[link](https://example.com)", selected);
                Assert.Contains("```csharp\nvar value = 1;\n```", selected);
                Assert.DoesNotContain('\u2005', selected);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ShiftClick_ExtendsTheExistingRange_AndEscapeClearsIt()
    {
        await fixture.Dispatch(() =>
        {
            var first = new SelectableTextBlock { Text = "Alpha bravo" };
            var last = new SelectableTextBlock { Text = "Charlie delta" };
            var root = new StackPanel { Spacing = 12, Children = { first, last } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                Drag(window, first, 6, first, 8);
                var pointer = NewPointer();
                last.RaiseEvent(new PointerPressedEventArgs(last, pointer, window, PointAt(window, last, 7), 3,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.Shift));
                Release(window, pointer, last, PointAt(window, last, 7));
                Assert.Equal("bravo\n\nCharlie", StrataTextSelection.GetSelectedText(root));

                first.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Assert.Null(StrataTextSelection.GetSelectedText(root));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task SelectingAcrossWidgets_ExportsTheirContentInsteadOfOmittingIt()
    {
        await fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = """
                    Before.

                    ```chart
                    {"type":"bar","labels":["One","Two"],"series":[{"name":"Values","values":[12,20]}]}
                    ```

                    ```card
                    {"header":"Included card","summary":"Summary","detail":"Collapsed detail."}
                    ```

                    After.
                    """,
                IsInline = true
            };
            var window = Show(markdown);
            try
            {
                await Task.Delay(80);
                Dispatcher.UIThread.RunJobs();
                StrataTextSelection.SelectAll(markdown);
                var selected = ChatClipboardData.SelectionMarkdown(StrataTextSelection.GetSelection(markdown));
                Assert.StartsWith("Before.", selected);
                Assert.Contains("| One | 12 |", selected);
                Assert.Contains("| Two | 20 |", selected);
                Assert.Contains("### Included card", selected);
                Assert.Contains("Collapsed detail.", selected);
                Assert.DoesNotContain("```chart", selected);
                Assert.DoesNotContain("```card", selected);
                Assert.EndsWith("After.", selected);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task SelectingATable_PreservesRowsAndColumnsInFormattedCopy()
    {
        await fixture.Dispatch(async () =>
        {
            var markdown = new StrataMarkdown
            {
                Markdown = "Before.\n\n| Name | Value |\n| --- | --- |\n| Alpha | 12 |\n| Beta | 20 |\n\nAfter.",
                IsInline = true
            };
            var window = Show(markdown);
            try
            {
                await Task.Delay(80);
                Dispatcher.UIThread.RunJobs();
                StrataTextSelection.SelectAll(markdown);
                var selected = ChatClipboardData.SelectionMarkdown(StrataTextSelection.GetSelection(markdown));
                Assert.StartsWith("Before.", selected);
                Assert.Contains("| Alpha | 12 |", selected);
                Assert.Contains("| Beta | 20 |", selected);
                Assert.EndsWith("After.", selected);
                Assert.Contains("<table", MarkdownClipboardFormatter.ToHtmlFragment(selected));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task EditableInputs_KeepTheirOwnSelectionAndCopyShortcut()
    {
        await fixture.Dispatch(async () =>
        {
            var text = new SelectableTextBlock { Text = "Transcript text" };
            var input = new TextBox { Text = "A draft message" };
            var root = new StackPanel { Children = { text, input } };
            StrataTextSelection.SetIsSelectionScope(root, true);
            var window = Show(root);
            try
            {
                StrataTextSelection.SelectAll(root);
                input.Focus();
                input.SelectionStart = 2;
                input.SelectionEnd = 7;
                input.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = CommandModifier
                });
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal("draft", await window.Clipboard!.TryGetTextAsync());
            }
            finally { window.Close(); }
        });
    }

    private static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private static IPointer NewPointer() => new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

    private static object? ExistingScope(Control owner)
    {
        var table = typeof(StrataTextSelection).GetField("Scopes",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        object?[] arguments = [owner, null];
        return (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)! ? arguments[1] : null;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 640, Height = 420 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = content;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static Point PointAt(Window window, SelectableTextBlock text, int index)
    {
        var caret = text.TextLayout.HitTestTextPosition(index);
        return text.TranslatePoint(new Point(caret.X + 0.1 + text.Padding.Left,
            caret.Center.Y + text.Padding.Top), window)!.Value;
    }

    private static void Drag(Window window, SelectableTextBlock from, int start, SelectableTextBlock to, int end)
    {
        from.Focus();
        var pointer = NewPointer();
        from.RaiseEvent(new PointerPressedEventArgs(from, pointer, window, PointAt(window, from, start), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        from.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, from, pointer, window, PointAt(window, to, end), 1,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
        Release(window, pointer, from, PointAt(window, to, end));
    }

    private static void Release(Window window, IPointer pointer, SelectableTextBlock source, Point position)
    {
        source.RaiseEvent(new PointerReleasedEventArgs(source, pointer, window, position, 2,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
        Dispatcher.UIThread.RunJobs();
    }
}
