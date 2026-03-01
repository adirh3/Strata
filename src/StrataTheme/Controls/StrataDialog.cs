using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A modal dialog overlay with scrim, title bar, close button, and animated entrance.
/// Place as a child of a Panel that fills the window. Set <see cref="IsDialogOpen"/> to
/// show/hide. The dialog renders a semi-transparent scrim behind a centered card.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataDialog Title="Import Cookies"
///                         IsDialogOpen="{Binding IsDialogOpen}"&gt;
///     &lt;StackPanel Spacing="8"&gt;
///         &lt;TextBlock Text="Dialog content here" /&gt;
///     &lt;/StackPanel&gt;
/// &lt;/controls:StrataDialog&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Scrim (Border), PART_Card (Border), PART_CloseButton (Button).</para>
/// <para><b>Pseudo-classes:</b> :open.</para>
/// </remarks>
public class StrataDialog : TemplatedControl
{
    private Border? _scrim;
    private Border? _card;
    private Button? _closeButton;

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<StrataDialog, string?>(nameof(Title));

    public static readonly StyledProperty<object?> DialogContentProperty =
        AvaloniaProperty.Register<StrataDialog, object?>(nameof(DialogContent));

    public static readonly StyledProperty<bool> IsDialogOpenProperty =
        AvaloniaProperty.Register<StrataDialog, bool>(nameof(IsDialogOpen));

    public static readonly StyledProperty<double> MaxDialogWidthProperty =
        AvaloniaProperty.Register<StrataDialog, double>(nameof(MaxDialogWidth), 480);

    static StrataDialog()
    {
        IsDialogOpenProperty.Changed.AddClassHandler<StrataDialog>((d, _) => d.OnOpenChanged());
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public bool IsDialogOpen
    {
        get => GetValue(IsDialogOpenProperty);
        set => SetValue(IsDialogOpenProperty, value);
    }

    public double MaxDialogWidth
    {
        get => GetValue(MaxDialogWidthProperty);
        set => SetValue(MaxDialogWidthProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_closeButton is not null)
            _closeButton.Click -= OnCloseButtonClick;
        if (_scrim is not null)
            _scrim.PointerPressed -= OnScrimPointerPressed;

        base.OnApplyTemplate(e);

        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        _card = e.NameScope.Find<Border>("PART_Card");
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");

        if (_closeButton is not null)
            _closeButton.Click += OnCloseButtonClick;

        if (_scrim is not null)
            _scrim.PointerPressed += OnScrimPointerPressed;

        OnOpenChanged();
    }

    private void OnCloseButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => IsDialogOpen = false;

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => IsDialogOpen = false;

    private void OnOpenChanged()
    {
        var open = IsDialogOpen;
        PseudoClasses.Set(":open", open);
        IsVisible = open;
        IsHitTestVisible = open;

        if (open && _card is not null)
            Dispatcher.UIThread.Post(() => PlayEntrance(_card), DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsDialogOpen)
        {
            IsDialogOpen = false;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private static void PlayEntrance(Control card)
    {
        var visual = ElementComposition.GetElementVisual(card);
        if (visual is null) return;

        var compositor = visual.Compositor;

        var bounds = card.Bounds;
        var cx = (float)(bounds.Width / 2);
        var cy = (float)(bounds.Height / 2);
        visual.CenterPoint = new Vector3(cx, cy, 0);

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(0.95f, 0.95f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(200);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.Target = "Opacity";
        opacityAnim.InsertKeyFrame(0f, 0f);
        opacityAnim.InsertKeyFrame(0.4f, 1f);
        opacityAnim.InsertKeyFrame(1f, 1f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(200);

        var group = compositor.CreateAnimationGroup();
        group.Add(scaleAnim);
        group.Add(opacityAnim);

        visual.StartAnimationGroup(group);
    }
}
