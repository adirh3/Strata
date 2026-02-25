using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public enum BudgetDisplayMode
{
    Tokens,
    Percent
}

/// <summary>
/// Compact budget chip with one-tap display mode toggle (tokens/percent).
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataBudgetChip UsedTokens="4200" MaxTokens="8000" IsEditable="True" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Track (Border), PART_Fill (Border), PART_Label (TextBlock).</para>
/// <para><b>Pseudo-classes:</b> :safe, :warn, :danger, :editable.</para>
/// </remarks>
public class StrataBudgetChip : TemplatedControl
{
    private Border? _track;
    private Border? _fill;
    private TextBlock? _label;

    public static readonly StyledProperty<int> UsedTokensProperty =
        AvaloniaProperty.Register<StrataBudgetChip, int>(nameof(UsedTokens), 3200);

    public static readonly StyledProperty<int> MaxTokensProperty =
        AvaloniaProperty.Register<StrataBudgetChip, int>(nameof(MaxTokens), 8000);

    public static readonly StyledProperty<BudgetDisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<StrataBudgetChip, BudgetDisplayMode>(nameof(DisplayMode), BudgetDisplayMode.Tokens);

    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<StrataBudgetChip, bool>(nameof(IsEditable), true);

    static StrataBudgetChip()
    {
        UsedTokensProperty.Changed.AddClassHandler<StrataBudgetChip>((control, _) => control.UpdateVisuals());
        MaxTokensProperty.Changed.AddClassHandler<StrataBudgetChip>((control, _) => control.UpdateVisuals());
        DisplayModeProperty.Changed.AddClassHandler<StrataBudgetChip>((control, _) => control.UpdateVisuals());
        IsEditableProperty.Changed.AddClassHandler<StrataBudgetChip>((control, _) => control.UpdateVisuals());
    }

    public int UsedTokens
    {
        get => GetValue(UsedTokensProperty);
        set => SetValue(UsedTokensProperty, value);
    }

    public int MaxTokens
    {
        get => GetValue(MaxTokensProperty);
        set => SetValue(MaxTokensProperty, value < 1 ? 1 : value);
    }

    public BudgetDisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _track = e.NameScope.Find<Border>("PART_Track");
        _fill = e.NameScope.Find<Border>("PART_Fill");
        _label = e.NameScope.Find<TextBlock>("PART_Label");

        if (_track is not null)
            _track.SizeChanged += (_, _) => UpdateVisuals();

        Dispatcher.UIThread.Post(UpdateVisuals, DispatcherPriority.Loaded);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEditable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        PseudoClasses.Set(":pressed", true);
        e.Handled = true;
        ToggleDisplayMode();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        PseudoClasses.Set(":pressed", false);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        PseudoClasses.Set(":pressed", false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEditable)
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            ToggleDisplayMode();
        }
    }

    private void ToggleDisplayMode()
    {
        DisplayMode = DisplayMode == BudgetDisplayMode.Tokens
            ? BudgetDisplayMode.Percent
            : BudgetDisplayMode.Tokens;
    }

    private void UpdateVisuals()
    {
        var max = MaxTokens < 1 ? 1 : MaxTokens;
        var used = UsedTokens < 0 ? 0 : UsedTokens;
        var ratio = used / (double)max;
        var pct = ratio * 100;

        if (_label is not null)
        {
            _label.Text = DisplayMode == BudgetDisplayMode.Tokens
                ? $"{used:n0}/{max:n0}"
                : $"{pct:0}%";
        }

        PseudoClasses.Set(":safe", ratio <= 0.70);
        PseudoClasses.Set(":warn", ratio > 0.70 && ratio <= 0.90);
        PseudoClasses.Set(":danger", ratio > 0.90);
        PseudoClasses.Set(":editable", IsEditable);

        if (_track is null || _fill is null)
            return;

        var width = _track.Bounds.Width;
        if (width < 1)
            return;

        _fill.Width = Math.Clamp(width * ratio, 0, width);
    }
}
