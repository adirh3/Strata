using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A rich-content canvas panel designed to sit alongside a chat shell.
/// Hosts arbitrary controls (code editors, documents, previews, diagrams)
/// with an elegant title bar, toolbar actions, and smooth open/close animations.
/// Inspired by modern AI canvas/artifact patterns.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataCanvas Title="Generated Code"
///                        Subtitle="Python · 42 lines"
///                        IsOpen="True"&gt;
///     &lt;controls:StrataCanvas.ToolBar&gt;
///         &lt;StackPanel Orientation="Horizontal" Spacing="4"&gt;
///             &lt;Button Classes="subtle" Content="Copy" /&gt;
///         &lt;/StackPanel&gt;
///     &lt;/controls:StrataCanvas.ToolBar&gt;
///     &lt;controls:StrataCanvas.Content&gt;
///         &lt;TextBlock Text="print('hello')" /&gt;
///     &lt;/controls:StrataCanvas.Content&gt;
/// &lt;/controls:StrataCanvas&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_StratumLine (Border),
/// PART_TitleBar (Border), PART_CloseButton (Button), PART_ContentHost (Border).</para>
/// <para><b>Pseudo-classes:</b> :open, :closed, :has-subtitle, :has-toolbar, :has-icon.</para>
/// </remarks>
public class StrataCanvas : TemplatedControl
{
    private Border? _root;
    private Border? _stratumLine;
    private Button? _closeButton;
    private Border? _contentHost;

    /// <summary>Title displayed in the canvas title bar.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataCanvas, string>(nameof(Title), string.Empty);

    /// <summary>Optional subtitle shown next to the title (e.g. file type, line count).</summary>
    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<StrataCanvas, string>(nameof(Subtitle), string.Empty);

    /// <summary>Optional icon content displayed before the title.</summary>
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(Icon));

    /// <summary>The main content hosted inside the canvas.</summary>
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(Content));

    /// <summary>Optional toolbar content displayed in the title bar (right-aligned).</summary>
    public static readonly StyledProperty<object?> ToolBarProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(ToolBar));

    /// <summary>Optional footer content displayed below the main content.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<StrataCanvas, object?>(nameof(Footer));

    /// <summary>Whether the canvas is open and visible.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<StrataCanvas, bool>(nameof(IsOpen), true);

    /// <summary>Whether to show the built-in close button in the title bar.</summary>
    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<StrataCanvas, bool>(nameof(ShowCloseButton), true);

    /// <summary>Raised when the close button is clicked.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
        RoutedEvent.Register<StrataCanvas, RoutedEventArgs>(nameof(CloseRequested), RoutingStrategies.Bubble);

    static StrataCanvas()
    {
        IsOpenProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.OnIsOpenChanged());
        SubtitleProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        ToolBarProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        IconProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
        FooterProperty.Changed.AddClassHandler<StrataCanvas>((c, _) => c.UpdatePseudoClasses());
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public object? ToolBar { get => GetValue(ToolBarProperty); set => SetValue(ToolBarProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public bool ShowCloseButton { get => GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }

    public event EventHandler<RoutedEventArgs>? CloseRequested
    { add => AddHandler(CloseRequestedEvent, value); remove => RemoveHandler(CloseRequestedEvent, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _root = e.NameScope.Find<Border>("PART_Root");
        _stratumLine = e.NameScope.Find<Border>("PART_StratumLine");
        _contentHost = e.NameScope.Find<Border>("PART_ContentHost");
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");

        if (_closeButton is not null)
            _closeButton.Click += (_, ev) =>
            {
                ev.Handled = true;
                IsOpen = false;
                RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
            };

        UpdatePseudoClasses();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsOpen)
        {
            e.Handled = true;
            IsOpen = false;
            RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
        }
    }

    private void OnIsOpenChanged()
    {
        UpdatePseudoClasses();

        if (IsOpen)
            AnimateOpen();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":open", IsOpen);
        PseudoClasses.Set(":closed", !IsOpen);
        PseudoClasses.Set(":has-subtitle", !string.IsNullOrWhiteSpace(Subtitle));
        PseudoClasses.Set(":has-toolbar", ToolBar is not null);
        PseudoClasses.Set(":has-icon", Icon is not null);
        PseudoClasses.Set(":has-footer", Footer is not null);
    }

    private void AnimateOpen()
    {
        if (_root is null)
            return;

        var visual = ElementComposition.GetElementVisual(_root);
        if (visual is null)
            return;

        var comp = visual.Compositor;

        // Subtle scale-up entrance
        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.97f, 0.97f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(250);
        visual.CenterPoint = new Avalonia.Vector3D(
            _root.Bounds.Width / 2,
            _root.Bounds.Height / 2, 0);
        visual.StartAnimation("Scale", scaleAnim);

        // Fade in
        var fadeAnim = comp.CreateScalarKeyFrameAnimation();
        fadeAnim.Target = "Opacity";
        fadeAnim.InsertKeyFrame(0f, 0f);
        fadeAnim.InsertKeyFrame(1f, 1f);
        fadeAnim.Duration = TimeSpan.FromMilliseconds(200);
        visual.StartAnimation("Opacity", fadeAnim);

        // Stratum line pulse
        AnimateStratumLine();
    }

    private void AnimateStratumLine()
    {
        if (_stratumLine is null)
            return;

        var visual = ElementComposition.GetElementVisual(_stratumLine);
        if (visual is null)
            return;

        var comp = visual.Compositor;
        visual.CenterPoint = new Avalonia.Vector3D(
            _stratumLine.Bounds.Width / 2,
            _stratumLine.Bounds.Height / 2, 0);

        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.6f, 1f, 1f));
        scaleAnim.InsertKeyFrame(0.5f, new Vector3(1f, 1.5f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation("Scale", scaleAnim);
    }
}
