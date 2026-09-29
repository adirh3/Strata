using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataCardTests(AvaloniaFixture fixture)
{
    private const string HebrewHeader = "שני הטורים שלך";
    private const string HebrewSummary =
        "**טור 1:** 7 · 13 · 16 · 25 · 28 · 32 — **חזק: 5**\n\n" +
        "**טור 2:** 8 · 11 · 13 · 19 · 20 · 27 — **חזק: 6**";

    [Theory]
    [InlineData(HebrewHeader, HebrewSummary)]
    [InlineData("Your two rows", "**Row 1:** 7 · 13 · 16\n\n**Row 2:** 8 · 11 · 13")]
    public Task MarkdownCard_TitleSummaryAndDetailAllowSelection(string header, string summary) =>
        fixture.Dispatch(async () =>
        {
            var markdown = CreateMarkdown(header, summary);
            var window = CreateWindow(markdown);
            try
            {
                var card = ShowMarkdownCard(window, markdown);
                var title = Assert.IsType<SelectableTextBlock>(card.Header);
                DragSelect(window, title);
                Assert.False(card.IsExpanded);
                title.SelectAll();
                Assert.Equal(header, title.SelectedText);

                var summaryText = await WaitForText(window, Assert.IsType<StrataMarkdown>(card.Summary));
                DragSelect(window, summaryText);
                Assert.False(card.IsExpanded);

                card.IsExpanded = true;
                await Task.Delay(300);
                window.UpdateLayout();
                var detailText = await WaitForText(window, Assert.IsType<StrataMarkdown>(card.Detail));
                DragSelect(window, detailText);
                Assert.True(card.IsExpanded);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task MarkdownCard_HasAlwaysVisibleCopyAction() =>
        fixture.Dispatch(() =>
        {
            var markdown = CreateMarkdown(HebrewHeader, HebrewSummary);
            var window = CreateWindow(markdown);
            try
            {
                var card = ShowMarkdownCard(window, markdown);
                var copyButton = card.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name == "PART_CopyButton");
                Assert.True(copyButton.IsEffectivelyVisible);
                Assert.Equal(1, copyButton.Opacity);
                Assert.False(card.IsPointerOver);
                Assert.True(card.CanCopy);
                Assert.Equal("Copy card", ToolTip.GetTip(copyButton));
                Assert.Equal("Copy card", AutomationProperties.GetName(copyButton));
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(HebrewHeader, HebrewSummary)]
    [InlineData("Your two rows", "**Row 1:** 7 · 13 · 16\n\n[Details](https://example.com/rows)")]
    public Task CopyButton_CopiesCardContentsNotJsonOrOnlySelection(string header, string summary) =>
        fixture.Dispatch(async () =>
        {
            const string detail = "Additional explanation.";
            var markdown = CreateMarkdown(header, summary, detail);
            var window = CreateWindow(markdown);
            try
            {
                var card = ShowMarkdownCard(window, markdown);
                var expected = string.Join(Environment.NewLine, header, summary, detail);
                var copy = FindPart<Button>(card, "PART_CopyButton");
                var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
                var title = Assert.IsType<SelectableTextBlock>(card.Header);
                title.SelectionStart = 0;
                title.SelectionEnd = 2;

                Click(window, copy);
                Assert.Equal(expected, await clipboard.TryGetTextAsync());
                Assert.False(card.IsExpanded);

                card.IsExpanded = true;
                await clipboard.SetTextAsync("sentinel");
                Assert.True(copy.Focus());
                Press(window, PhysicalKey.Enter);
                Assert.Equal(expected, await clipboard.TryGetTextAsync());
                Assert.True(card.IsExpanded);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task CopyIsOptInAndLocalizable_AndSummaryOnlyCardsHaveNoDisclosure() =>
        fixture.Dispatch(() =>
        {
            var card = new StrataCard { Header = "Title", Summary = "Summary" };
            var window = CreateWindow(card);
            try
            {
                window.Show();
                ApplyCardTheme(window, card);
                var copy = FindPart<Button>(card, "PART_CopyButton");
                Assert.False(card.CanCopy);
                Assert.False(copy.IsVisible);
                Assert.False(FindPart<Button>(card, "PART_ExpandButton").IsVisible);
                Click(window, FindPart<Border>(card, "PART_Header"));
                Assert.False(card.IsExpanded);

                card.CopyLabel = "העתקת כרטיס";
                card.CanCopy = true;
                Assert.True(copy.IsVisible);
                Assert.Equal(card.CopyLabel, ToolTip.GetTip(copy));
                Assert.Equal(card.CopyLabel, AutomationProperties.GetName(copy));
                card.CanCopy = false;
                Assert.False(copy.IsVisible);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task HeaderAndChevron_DiscloseWithPointerAndKeyboard_WithoutConsumingLinks() =>
        fixture.Dispatch(async () =>
        {
            // Exercise the link tap route without launching an external browser.
            var summary = new StrataMarkdown { Markdown = "[Documentation](test://docs)", IsInline = true };
            var card = new StrataCard { Header = "Title", Summary = summary, Detail = "Details" };
            var window = CreateWindow(card);
            try
            {
                window.Show();
                ApplyCardTheme(window, card);
                var text = await WaitForText(window, summary);
                PrepareTextHitTarget(window, text);
                var linkPoint = text.TextLayout.HitTestTextRange(0, 5).First().Center;
                Assert.Equal("test://docs", summary.GetLinkAtPoint(text, linkPoint));
                var linkHandled = false;
                text.AddHandler(InputElement.TappedEvent, (_, args) => linkHandled = args.Handled,
                    RoutingStrategies.Bubble, handledEventsToo: true);
                var point = text.TranslatePoint(linkPoint, window)!.Value;
                Assert.Same(text, window.InputHitTest(point));
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.True(linkHandled);
                Assert.False(card.IsExpanded);

                var header = FindPart<Border>(card, "PART_Header");
                point = header.TranslatePoint(new Point(2, 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                Assert.False(card.IsExpanded);
                window.MouseMove(point + new Point(0, 50), RawInputModifiers.LeftMouseButton);
                window.MouseUp(point + new Point(0, 50), MouseButton.Left);
                Assert.False(card.IsExpanded);

                window.MouseDown(point, MouseButton.Left);
                Assert.False(card.IsExpanded);
                window.MouseUp(point, MouseButton.Left);
                Assert.True(card.IsExpanded);

                var expand = FindPart<Button>(card, "PART_ExpandButton");
                Click(window, expand);
                Assert.False(card.IsExpanded);
                Assert.True(expand.Focus());
                Press(window, PhysicalKey.Space);
                Assert.True(card.IsExpanded);
                Press(window, PhysicalKey.Enter);
                Assert.False(card.IsExpanded);
                Assert.True(card.Focus());
                Press(window, PhysicalKey.Enter);
                Assert.True(card.IsExpanded);
                Press(window, PhysicalKey.Space);
                Assert.False(card.IsExpanded);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData("copy only this part")]
    [InlineData("שני הטורים שלך")]
    public Task SelectedTextKeyboardCopyInsideMessage_DoesNotCopyWholeResponse(string text) =>
        fixture.Dispatch(async () =>
        {
            var selected = new SelectableTextBlock { Text = text };
            var card = new StrataCard { Header = "Title", Summary = selected, Detail = "Details" };
            var message = new StrataChatMessage { Content = card };
            var window = CreateWindow(message);
            try
            {
                message.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataChatMessage)));
                window.Show();
                message.ApplyTemplate();
                ApplyCardTheme(window, card);
                var copies = 0;
                message.CopyRequested += (_, _) => copies++;
                Assert.True(selected.Focus());
                selected.SelectionStart = 0;
                selected.SelectionEnd = 3;
                var expected = selected.SelectedText;

                // Headless's default hotkeys are Ctrl-based even when its host OS is macOS.
                var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;
                var originalCopy = hotkeys.Copy;
                try
                {
                    hotkeys.Copy = new List<KeyGesture>
                    {
                        new(Key.C, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)
                    };
                    var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
                    window.KeyPressQwerty(PhysicalKey.C, modifiers);
                    window.KeyReleaseQwerty(PhysicalKey.C, modifiers);
                }
                finally { hotkeys.Copy = originalCopy; }

                Assert.Equal(expected, await window.Clipboard!.TryGetTextAsync());
                Assert.Equal(0, copies);
                Assert.True(selected.IsFocused);
                Assert.False(card.IsExpanded);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task ActionsSurviveRethemeAndReattach_AndOldTemplateHandlersAreRemoved() =>
        fixture.Dispatch(async () =>
        {
            var card = new StrataCard { Header = "Title", Summary = "Summary", Detail = "Details", CanCopy = true };
            var window = CreateWindow(card);
            try
            {
                window.Show();
                ApplyCardTheme(window, card);
                var oldCopy = FindPart<Button>(card, "PART_CopyButton");
                var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
                var template = card.Template;
                card.Template = new FuncControlTemplate<StrataCard>((_, _) => new Border());
                card.ApplyTemplate();
                await clipboard.SetTextAsync("sentinel");
                oldCopy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("sentinel", await clipboard.TryGetTextAsync());

                card.Template = template;
                card.ApplyTemplate();
                window.RequestedThemeVariant = ThemeVariant.Dark;
                var host = Assert.IsType<StackPanel>(window.Content);
                host.Children.Remove(card);
                host.Children.Add(card);
                ApplyCardTheme(window, card);
                var copy = FindPart<Button>(card, "PART_CopyButton");
                Assert.NotSame(oldCopy, copy);
                Click(window, copy);
                Assert.Equal(string.Join(Environment.NewLine, "Title", "Summary", "Details"),
                    await clipboard.TryGetTextAsync());
                Click(window, FindPart<Button>(card, "PART_ExpandButton"));
                Assert.True(card.IsExpanded);
            }
            finally { window.Close(); }
        });

    private static void ApplyCardTheme(Window window, StrataCard card)
    {
        card.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataCard)));
        card.ApplyTemplate();
        foreach (var button in card.GetVisualDescendants().OfType<Button>())
        {
            button.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(Button)));
            button.ApplyTemplate();
        }
        Pump(window);
    }

    private static StrataCard ShowMarkdownCard(Window window, StrataMarkdown markdown)
    {
        window.Show();
        Pump(window);
        var card = markdown.GetVisualDescendants().OfType<StrataCard>().Single();
        ApplyCardTheme(window, card);
        return card;
    }

    private static async Task<SelectableTextBlock> WaitForText(Window window, StrataMarkdown markdown)
    {
        // Reattaching a markdown child can queue a throttled rebuild.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            Pump(window);
            if (markdown.GetVisualDescendants().OfType<SelectableTextBlock>().FirstOrDefault() is { } text)
                return text;
            await Task.Delay(20);
        }
        return Assert.Single(markdown.GetVisualDescendants().OfType<SelectableTextBlock>());
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void DragSelect(Window window, SelectableTextBlock text)
    {
        PrepareTextHitTarget(window, text);
        var bounds = text.TextLayout.HitTestTextRange(0, 3).First(rect => rect.Width > 1);
        var start = text.TranslatePoint(new Point(bounds.Left + 0.1, bounds.Center.Y), window)!.Value;
        var end = text.TranslatePoint(new Point(bounds.Right - 0.1, bounds.Center.Y), window)!.Value;
        Assert.Same(text, window.InputHitTest(start));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        Assert.False(string.IsNullOrEmpty(text.SelectedText));
        Assert.True(text.IsFocused);
    }

    private static void PrepareTextHitTarget(Window window, SelectableTextBlock text)
    {
        // The fixture's stub renderer has no glyph ink for hit testing.
        text.Background = Brushes.Transparent;
        Pump(window);
    }

    private static void Click(Window window, Control control)
    {
        Pump(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    [Fact]
    public Task SummaryPadding_ShortSelectionDragDoesNotExpandCard() =>
        fixture.Dispatch(() =>
        {
            var card = new StrataCard
            {
                Header = "Title",
                Summary = new SelectableTextBlock { Text = "Select this text" },
                Detail = "Details"
            };
            var window = CreateWindow(card);
            try
            {
                window.Show();
                ApplyCardTheme(window, card);
                var summaryHost = FindPart<Border>(card, "PART_SummaryHost");
                var point = summaryHost.TranslatePoint(new Point(2, 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseMove(point + new Point(3, 0), RawInputModifiers.LeftMouseButton);
                window.MouseUp(point + new Point(3, 0), MouseButton.Left);
                Assert.False(card.IsExpanded);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(PhysicalKey.Enter)]
    [InlineData(PhysicalKey.Space)]
    public Task KeysInSelectableDetail_DoNotCollapseCard(PhysicalKey key) =>
        fixture.Dispatch(() =>
        {
            var detail = new SelectableTextBlock { Text = "Selectable detail" };
            var card = new StrataCard { Header = "Title", Detail = detail, IsExpanded = true };
            var window = CreateWindow(card);
            try
            {
                window.Show();
                ApplyCardTheme(window, card);
                Assert.True(detail.Focus());
                window.KeyPressQwerty(key, RawInputModifiers.None);
                window.KeyReleaseQwerty(key, RawInputModifiers.None);
                Assert.True(card.IsExpanded);
                Assert.True(detail.IsFocused);
            }
            finally { window.Close(); }
        });

    private static StrataMarkdown CreateMarkdown(string header, string summary, string? detail = "Additional explanation.") =>
        new() { Markdown = $"```card\n{JsonSerializer.Serialize(new { header, summary, detail })}\n```" };

    private static Window CreateWindow(Control content)
    {
        var window = new Window
        {
            Width = 700,
            Height = 500,
            Foreground = Brushes.Black,
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner }
                }.RegisterInNameScope(scope)
            }),
            Content = new StackPanel { Margin = new Thickness(20), Children = { content } }
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        return window;
    }

    private static T FindPart<T>(Control control, string name) where T : Control
    {
        var parts = control.GetVisualDescendants().OfType<T>().ToArray();
        return Assert.Single(parts, part => part.Name == name);
    }
}
