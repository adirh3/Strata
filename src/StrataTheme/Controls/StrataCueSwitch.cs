using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace StrataTheme.Controls;

public enum CueMode
{
    Memory,
    Grounded,
    Tool
}

/// <summary>
/// Tiny provenance switch for generated outputs.
/// Click to cycle Memory → Grounded → Tool.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataCueSwitch Mode="Grounded" IsLocked="False" /&gt;
/// </code>
/// <para><b>Pseudo-classes:</b> :memory, :grounded, :tool, :locked.</para>
/// </remarks>
public class StrataCueSwitch : TemplatedControl
{
    public static readonly StyledProperty<CueMode> ModeProperty =
        AvaloniaProperty.Register<StrataCueSwitch, CueMode>(nameof(Mode), CueMode.Memory);

    public static readonly StyledProperty<bool> IsLockedProperty =
        AvaloniaProperty.Register<StrataCueSwitch, bool>(nameof(IsLocked));

    static StrataCueSwitch()
    {
        ModeProperty.Changed.AddClassHandler<StrataCueSwitch>((control, _) => control.UpdatePseudoClasses());
        IsLockedProperty.Changed.AddClassHandler<StrataCueSwitch>((control, _) => control.UpdatePseudoClasses());
    }

    public CueMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public bool IsLocked
    {
        get => GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdatePseudoClasses();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsLocked)
            return;

        PseudoClasses.Set(":pressed", true);
        e.Handled = true;
        CycleMode();
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

        if (IsLocked)
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            CycleMode();
        }
    }

    private void CycleMode()
    {
        Mode = Mode switch
        {
            CueMode.Memory => CueMode.Grounded,
            CueMode.Grounded => CueMode.Tool,
            _ => CueMode.Memory
        };
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":memory", Mode == CueMode.Memory);
        PseudoClasses.Set(":grounded", Mode == CueMode.Grounded);
        PseudoClasses.Set(":tool", Mode == CueMode.Tool);
        PseudoClasses.Set(":locked", IsLocked);
    }
}
