using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace StrataTheme.Controls;

/// <summary>
/// A clean status indicator dot with state-specific color and a subtle
/// breathe animation for the Active state.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataOrb State="Active" Label="Processing" IsInteractive="True" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Orb (Border).</para>
/// <para><b>Pseudo-classes:</b> :active, :idle, :success, :warning, :error, :interactive.</para>
/// </remarks>
public class StrataOrb : TemplatedControl
{
    private Border? _orb;
    private CompositionVisual? _orbVisual;

    /// <summary>Current visual state of the orb.</summary>
    public static readonly StyledProperty<OrbState> StateProperty =
        AvaloniaProperty.Register<StrataOrb, OrbState>(nameof(State), OrbState.Idle);

    /// <summary>Optional text label displayed next to the dot.</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StrataOrb, string?>(nameof(Label));

    /// <summary>When true, clicking or pressing Enter/Space cycles through states.</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<StrataOrb, bool>(nameof(IsInteractive));

    public OrbState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    static StrataOrb()
    {
        StateProperty.Changed.AddClassHandler<StrataOrb>((orb, _) => orb.OnStateChanged());
        IsInteractiveProperty.Changed.AddClassHandler<StrataOrb>((orb, _) => orb.UpdateInteractiveClass());
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _orb = e.NameScope.Find<Border>("PART_Orb");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(ApplyAnimation, Avalonia.Threading.DispatcherPriority.Loaded);
        UpdateInteractiveClass();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsInteractive || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        CycleState();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInteractive)
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            CycleState();
        }
    }

    private void OnStateChanged()
    {
        PseudoClasses.Set(":active", State == OrbState.Active);
        PseudoClasses.Set(":idle", State == OrbState.Idle);
        PseudoClasses.Set(":success", State == OrbState.Success);
        PseudoClasses.Set(":warning", State == OrbState.Warning);
        PseudoClasses.Set(":error", State == OrbState.Error);

        if (_orb is not null)
            ApplyAnimation();
    }

    private void UpdateInteractiveClass()
    {
        PseudoClasses.Set(":interactive", IsInteractive);
    }

    private void CycleState()
    {
        State = State switch
        {
            OrbState.Idle => OrbState.Active,
            OrbState.Active => OrbState.Success,
            OrbState.Success => OrbState.Warning,
            OrbState.Warning => OrbState.Error,
            _ => OrbState.Idle
        };
    }

    private void ApplyAnimation()
    {
        if (_orb is null) return;
        _orbVisual = ElementComposition.GetElementVisual(_orb);
        if (_orbVisual is null) return;

        var comp = _orbVisual.Compositor;

        if (State == OrbState.Active)
        {
            // Subtle opacity breathe for active state only
            var anim = comp.CreateScalarKeyFrameAnimation();
            anim.Target = "Opacity";
            anim.InsertKeyFrame(0f, 1f);
            anim.InsertKeyFrame(0.5f, 0.5f);
            anim.InsertKeyFrame(1f, 1f);
            anim.Duration = TimeSpan.FromMilliseconds(1800);
            anim.IterationBehavior = AnimationIterationBehavior.Forever;
            _orbVisual.StartAnimation("Opacity", anim);
        }
        else
        {
            // Static — reset opacity to full
            _orbVisual.StopAnimation("Opacity");
            _orbVisual.Opacity = 1f;
        }
    }
}

public enum OrbState
{
    Idle,
    Active,
    Success,
    Warning,
    Error
}
