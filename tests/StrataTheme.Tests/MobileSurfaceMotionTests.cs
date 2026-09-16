using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class MobileSurfaceMotionTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public Task MobileComposerArrangesOneRowThenRevealsASeparateToolbar(FlowDirection direction) =>
        fixture.Dispatch(async () =>
        {
            var attach = new Button { Content = "+", Width = 48, Height = 48 };
            var model = new Button { Content = "Model", Width = 120, Height = 48 };
            var composer = new StrataChatComposer
            {
                IsCompact = true, CanVoice = false, LeadingContent = attach, ToolbarContent = model,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, FlowDirection = direction
            };
            composer.Classes.Add("mobile");
            var window = CreateWindow(composer);
            try
            {
                var input = composer.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.Name == "PART_Input");
                var send = composer.GetVisualDescendants().OfType<Button>()
                    .Single(control => control.Name == "PART_SendButton");
                var toolbar = composer.GetVisualDescendants().OfType<Grid>()
                    .Single(control => control.Name == "PART_Toolbar");
                Rect BoundsInComposer(Control control) =>
                    new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(composer)!.Value);
                var compactInput = BoundsInComposer(input);
                var leading = BoundsInComposer(attach);
                var primary = BoundsInComposer(send);
                Assert.True(compactInput.Right <= leading.Left + 0.1 || compactInput.Left >= leading.Right - 0.1);
                Assert.True(compactInput.Right <= primary.Left + 0.1 || compactInput.Left >= primary.Right - 0.1);
                Assert.False(model.IsEffectivelyEnabled);
                var compactHeight = composer.Bounds.Height;
                var actionBottom = send.TranslatePoint(new Point(0, send.Bounds.Height), window)!.Value.Y;

                composer.IsCompact = false;
                await Task.Delay(90);
                Dispatcher.UIThread.RunJobs();
                Assert.InRange(composer.Bounds.Height, compactHeight + 1, compactHeight + 47);
                Assert.Equal(actionBottom, send.TranslatePoint(new Point(0, send.Bounds.Height), window)!.Value.Y, precision: 1);
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                Assert.True(model.IsEffectivelyEnabled);
                Assert.True(input.Bounds.Width > compactInput.Width + 48);
                Assert.True(BoundsInComposer(toolbar).Top >= BoundsInComposer(input).Bottom);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task ExternalEditorCanShowAReadOnlyDraftWithoutReplacingItsHost() =>
        fixture.Dispatch(() =>
        {
            var editor = new Border { Height = 48 };
            var composer = new StrataChatComposer
            {
                EditorContent = editor,
                PromptText = "A draft that remains visible beneath an overlay",
                CanVoice = false
            };
            var window = CreateWindow(composer);
            try
            {
                var input = composer.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.Name == "PART_Input");
                Assert.True(composer.IsEditorContentVisible);
                Assert.False(input.IsVisible);
                composer.IsEditorContentVisible = false;
                Dispatcher.UIThread.RunJobs();
                Assert.True(input.IsEffectivelyVisible);
                Assert.True(input.IsReadOnly);
                Assert.False(input.IsHitTestVisible);
                Assert.Equal(composer.PromptText, input.Text);
                Assert.Same(editor, composer.EditorContent);
                composer.IsEditorContentVisible = true;
                Dispatcher.UIThread.RunJobs();
                Assert.False(input.IsVisible);
                Assert.Same(editor, composer.EditorContent);
                composer.EditorContent = null;
                Dispatcher.UIThread.RunJobs();
                Assert.True(input.IsVisible);
                Assert.False(input.IsReadOnly);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompactComposerIsOptInAndDoesNotChangeTheDesktopPresentation(bool mobile) =>
        fixture.Dispatch(async () =>
        {
            var composer = new StrataChatComposer
            {
                PromptText = "Keep the whole draft",
                CanVoice = false,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom
            };
            if (mobile)
                composer.Classes.Add("mobile");
            var window = CreateWindow(composer);
            try
            {
                Assert.False(composer.IsCompact);
                var expandedHeight = composer.Bounds.Height;
                var input = composer.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.Name == "PART_Input");
                composer.IsCompact = true;
                await Task.Delay(280);
                Dispatcher.UIThread.RunJobs();
                if (mobile)
                    Assert.InRange(composer.Bounds.Height, 48, expandedHeight - 24);
                else
                    Assert.Equal(expandedHeight, composer.Bounds.Height);
                Assert.Same(input, composer.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.Name == "PART_Input"));
                Assert.Equal("Keep the whole draft", input.Text);
                composer.IsCompact = false;
                await Task.Delay(280);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(expandedHeight, composer.Bounds.Height, precision: 1);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public Task DrawerUsesAShortSlowStrokeWithoutRemeasuringOrAllocatingPerMove(FlowDirection direction) =>
        fixture.Dispatch(async () =>
        {
            var payload = new MeasuredBorder { Height = 600, Background = Brushes.Gray };
            var drawer = new StrataNavigationDrawer
            {
                PanelWidth = 320, Panel = payload, Content = new Border(), FlowDirection = direction
            };
            var window = CreateWindow(drawer);
            try
            {
                var panel = drawer.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_Panel");
                Assert.True(panel.IsVisible);
                Assert.False(panel.IsHitTestVisible);
                Assert.True(payload.Measures > 0);
                var initialMeasures = payload.Measures;
                var transform = panel.RenderTransform;

                for (var i = 0; i < 8; i++)
                    drawer.RaiseEvent(new EdgeDragEventArgs(EdgeDragGestureRecognizer.EdgeDragEvent, 4, 30 + i * 4));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(initialMeasures, payload.Measures);
                Assert.Same(transform, panel.RenderTransform);
                Assert.Equal(32d / 320, drawer.Progress, precision: 6);
                Assert.Equal((direction == FlowDirection.RightToLeft ? 1 : -1) * 288,
                    Assert.IsType<TranslateTransform>(panel.RenderTransform).X, precision: 3);
                drawer.RaiseEvent(new EdgeDragEndedEventArgs(EdgeDragGestureRecognizer.EdgeDragEndedEvent, 0));
                Assert.True(drawer.IsOpen);
                await Task.Delay(300);
                Assert.Equal(1, drawer.Progress, precision: 3);

                drawer.RaiseEvent(new EdgeDragEventArgs(EdgeDragGestureRecognizer.EdgeDragEvent, -32, 10));
                drawer.RaiseEvent(new EdgeDragEndedEventArgs(EdgeDragGestureRecognizer.EdgeDragEndedEvent, 0));
                Assert.False(drawer.IsOpen);
                await Task.Delay(300);
                Assert.Equal(0, drawer.Progress, precision: 3);
                Assert.False(panel.IsHitTestVisible);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task DrawerLayoutResetCancelsPendingAnimationFrames() =>
        fixture.Dispatch(async () =>
        {
            var drawer = new StrataNavigationDrawer { Panel = new Border(), PanelWidth = 320 };
            var window = CreateWindow(drawer);
            try
            {
                drawer.IsOpen = true;
                drawer.PanelWidth = 240;
                Assert.Equal(1, drawer.Progress);
                var progressAfterReset = new List<double>();
                drawer.PropertyChanged += (_, change) =>
                {
                    if (change.Property == StrataNavigationDrawer.ProgressProperty)
                        progressAfterReset.Add(drawer.Progress);
                };

                await Task.Delay(280);

                Assert.DoesNotContain(progressAfterReset, progress => progress < 1);
                var panel = drawer.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_Panel");
                Assert.Equal(0, Assert.IsType<TranslateTransform>(panel.RenderTransform).X);
                Assert.Equal(240, panel.Bounds.Width);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task DrawerTinyDriftCancelsAndAShortFlickStillOpens() =>
        fixture.Dispatch(() =>
        {
            var drawer = new StrataNavigationDrawer { Panel = new Border(), PanelWidth = 320 };
            var window = CreateWindow(drawer);
            drawer.RaiseEvent(new EdgeDragEventArgs(EdgeDragGestureRecognizer.EdgeDragEvent, 18, 48));
            drawer.RaiseEvent(new EdgeDragEndedEventArgs(EdgeDragGestureRecognizer.EdgeDragEndedEvent, 0));
            Assert.False(drawer.IsOpen);
            drawer.RaiseEvent(new EdgeDragEventArgs(EdgeDragGestureRecognizer.EdgeDragEvent, 20, 68));
            drawer.RaiseEvent(new EdgeDragEndedEventArgs(EdgeDragGestureRecognizer.EdgeDragEndedEvent, 600));
            Assert.True(drawer.IsOpen);
            window.Close();
        });

    [Fact]
    public Task ClosedDrawerIsSkippedByKeyboardAndCannotActivateItsPreviousFocus() =>
        fixture.Dispatch(async () =>
        {
            var outside = new Button { Content = "Conversation action" };
            var inside = new Button { Content = "Drawer action" };
            var clicks = 0;
            inside.Click += (_, _) => clicks++;
            var drawer = new StrataNavigationDrawer { Content = outside, Panel = inside };
            var window = CreateWindow(drawer);
            try
            {
                outside.Focus();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.NotSame(inside, window.FocusManager!.GetFocusedElement());
                Assert.False(inside.Focus());

                drawer.IsOpen = true;
                await Task.Delay(280);
                Assert.True(inside.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.Equal(1, clicks);

                drawer.IsOpen = false;
                await Task.Delay(280);
                Assert.False(inside.IsEffectivelyEnabled);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.Equal(1, clicks);
                Assert.True(inside.IsEffectivelyVisible);
                Assert.True(inside.Bounds.Width > 0);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task SheetClosedWhileDetachedReturnsWithoutStaleChromeAndCanReopen() =>
        fixture.Dispatch(async () =>
        {
            var sheet = new StrataBottomSheet
            {
                Title = "Details", Content = new Border { Height = 200, Background = Brushes.Gray }
            };
            var host = new Panel { Children = { sheet } };
            var window = CreateWindow(host);
            try
            {
                sheet.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(280);
                var motion = sheet.GetVisualDescendants().OfType<Border>()
                    .Single(control => control.Name == "PART_SheetMotion");
                var presenter = sheet.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_ContentPresenter");
                Assert.Equal(1, motion.Opacity, precision: 2);

                host.Children.Remove(sheet);
                sheet.IsOpen = false;
                host.Children.Add(sheet);
                Dispatcher.UIThread.RunJobs();
                Assert.False(presenter.IsVisible);
                Assert.Equal(0, motion.Opacity);
                Assert.False(motion.IsVisible);

                sheet.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(280);
                Assert.True(presenter.IsVisible);
                Assert.True(presenter.IsHitTestVisible);
                Assert.Equal(1, motion.Opacity, precision: 2);
                Assert.True(motion.IsVisible);
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task SheetKeepsItsPayloadThroughExitAndRapidReopenCancelsTheOldClose() =>
        fixture.Dispatch(async () =>
        {
            var sheet = new StrataBottomSheet
            {
                Title = "Model", Content = new Border { Height = 240, Background = Brushes.Gray }
            };
            var window = CreateWindow(sheet);
            try
            {
                var presenter = sheet.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_ContentPresenter");
                Assert.False(presenter.IsVisible);
                Assert.False(sheet.IsPresented);
                sheet.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(280);
                Assert.True(presenter.IsVisible);
                Assert.True(sheet.IsPresented);
                var motion = sheet.GetVisualDescendants().OfType<Border>()
                    .Single(control => control.Name == "PART_SheetMotion");
                Assert.Equal(2, motion.Transitions!.Count);
                var transitions = motion.Transitions;
                sheet.IsOpen = false;
                Assert.Same(transitions, motion.Transitions);
                Assert.True(presenter.IsVisible);
                Assert.False(presenter.IsHitTestVisible);
                await Task.Delay(60);
                sheet.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(transitions, motion.Transitions);
                await Task.Delay(300);
                Assert.True(presenter.IsVisible);
                Assert.True(presenter.IsHitTestVisible);
                Assert.Equal(1, motion.Opacity, precision: 3);
                sheet.IsOpen = false;
                await Task.Delay(240);
                Assert.False(presenter.IsVisible);
                Assert.False(sheet.IsPresented);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData("claude-opus-5", "ANTHROPIC")]
    [InlineData("GPT-6-astra", "OPENAI")]
    [InlineData("gemini-3-pro", "GOOGLE")]
    [InlineData("o3", "REASONING")]
    [InlineData("custom-model", "OTHER")]
    public void ProviderLabelsAreSharedAcrossFormFactors(string model, string label) =>
        Assert.Equal(label, StrataModelPicker.GetModelProviderLabel(model));

    private static Window CreateWindow(Control content)
    {
        var window = new Window { Width = 360, Height = 780 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = content;
        window.Show();
        content.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private sealed class MeasuredBorder : Border
    {
        public int Measures { get; private set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            Measures++;
            return base.MeasureOverride(availableSize);
        }
    }
}
