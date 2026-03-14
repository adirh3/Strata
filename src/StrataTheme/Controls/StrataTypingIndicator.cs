using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Animated three-dot typing indicator with a configurable label.
/// The dots pulse with staggered timing when <see cref="IsActive"/> is true.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataTypingIndicator Label="Agent is thinking…" IsActive="True" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Dot1 (Border), PART_Dot2 (Border), PART_Dot3 (Border).</para>
/// <para><b>Pseudo-classes:</b> :active.</para>
/// </remarks>
public class StrataTypingIndicator : TemplatedControl
{
    private const double PulseCycleMs = 980d;
    private const double PulsePeakPhase = 0.35d;
    private const double PulseFadePhase = 0.7d;
    private const double PulseMinOpacity = 0.28d;
    private const double PulseMaxOpacity = 1d;
    private const double InactiveOpacity = 0.45d;

    private Border? _dot1;
    private Border? _dot2;
    private Border? _dot3;
    private bool _attached;
    private readonly Stopwatch _pulseClock = new();
    private readonly DispatcherTimer _pulseTimer;

    /// <summary>Text displayed next to the dots (e.g. "Thinking…", "Typing…").</summary>
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataTypingIndicator, string>(nameof(Label), "Thinking…");

    /// <summary>Whether the indicator is animating. When false, the dots are static and the control is dimmed.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StrataTypingIndicator, bool>(nameof(IsActive), true);

    static StrataTypingIndicator()
    {
        IsActiveProperty.Changed.AddClassHandler<StrataTypingIndicator>((c, _) => c.Refresh());
    }

    public StrataTypingIndicator()
    {
        _pulseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, OnPulseTick);
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _dot1 = e.NameScope.Find<Border>("PART_Dot1");
        _dot2 = e.NameScope.Find<Border>("PART_Dot2");
        _dot3 = e.NameScope.Find<Border>("PART_Dot3");
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        StopPulse();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        PseudoClasses.Set(":active", IsActive);

        if (IsActive && _attached && HasTemplateParts)
        {
            StartPulse();
            return;
        }

        StopPulse();
    }

    private void StartPulse()
    {
        if (_pulseTimer.IsEnabled)
        {
            UpdateDots();
            return;
        }

        _pulseClock.Restart();
        UpdateDots();
        _pulseTimer.Start();
    }

    private void StopPulse()
    {
        _pulseTimer.Stop();
        _pulseClock.Reset();
        ResetDot(_dot1);
        ResetDot(_dot2);
        ResetDot(_dot3);
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        if (!_attached || !IsActive || !HasTemplateParts)
        {
            StopPulse();
            return;
        }

        UpdateDots();
    }

    private void UpdateDots()
    {
        var elapsedMs = _pulseClock.Elapsed.TotalMilliseconds;
        ApplyDotOpacity(_dot1, elapsedMs, 0d);
        ApplyDotOpacity(_dot2, elapsedMs, 140d);
        ApplyDotOpacity(_dot3, elapsedMs, 280d);
    }

    private static void ResetDot(Border? dot)
    {
        if (dot is null) return;
        dot.Opacity = InactiveOpacity;
    }

    private static void ApplyDotOpacity(Border? dot, double elapsedMs, double delayMs)
    {
        if (dot is null)
            return;

        dot.Opacity = CalculatePulseOpacity(elapsedMs, delayMs);
    }

    private static double CalculatePulseOpacity(double elapsedMs, double delayMs)
    {
        var shiftedMs = elapsedMs - delayMs;
        while (shiftedMs < 0d)
            shiftedMs += PulseCycleMs;

        var phase = (shiftedMs % PulseCycleMs) / PulseCycleMs;
        if (phase <= PulsePeakPhase)
            return Lerp(PulseMinOpacity, PulseMaxOpacity, phase / PulsePeakPhase);

        if (phase <= PulseFadePhase)
            return Lerp(PulseMaxOpacity, PulseMinOpacity, (phase - PulsePeakPhase) / (PulseFadePhase - PulsePeakPhase));

        return PulseMinOpacity;
    }

    private static double Lerp(double from, double to, double progress)
        => from + ((to - from) * progress);

    private bool HasTemplateParts => _dot1 is not null && _dot2 is not null && _dot3 is not null;
}
