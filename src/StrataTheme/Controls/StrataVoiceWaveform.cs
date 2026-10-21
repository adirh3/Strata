using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Animation;

namespace StrataTheme.Controls;

/// <summary>
/// A live level meter for dictation: rounded bars scroll in from the end edge while <see cref="IsActive"/>, each as
/// tall as the loudest <see cref="Level"/> of its moment, over a row of quiet dots where nothing has been heard yet.
/// Bars are drawn in <see cref="Foreground"/>; in a right-to-left layout they scroll in from the left.
/// </summary>
/// <remarks>
/// <para>The meter samples <see cref="Level"/> twenty times a second, only while it is active, attached and
/// visible, so a host can set the level as often as its audio arrives. It keeps its bars when it stops, and
/// starts a fresh row when it becomes active again.</para>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataVoiceWaveform Height="22" IsActive="{Binding IsRecording}" Level="{Binding MicrophoneLevel}" /&gt;
/// </code>
/// </remarks>
public class StrataVoiceWaveform : Control
{
    private const int Capacity = 256;
    private const double RestingOpacity = 0.35;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>The current level, from 0 (silence) to 1 (loud).</summary>
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<StrataVoiceWaveform, double>(nameof(Level), coerce: static (_, value) =>
            double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0);

    /// <summary>Whether the meter is listening: new bars scroll in only while true.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StrataVoiceWaveform, bool>(nameof(IsActive));

    /// <summary>The bars' brush, inherited like text.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<StrataVoiceWaveform>();

    /// <summary>Width of each bar, and the diameter of a quiet dot.</summary>
    public static readonly StyledProperty<double> BarWidthProperty =
        AvaloniaProperty.Register<StrataVoiceWaveform, double>(nameof(BarWidth), 3);

    /// <summary>Space between bars.</summary>
    public static readonly StyledProperty<double> BarSpacingProperty =
        AvaloniaProperty.Register<StrataVoiceWaveform, double>(nameof(BarSpacing), 3);

    private readonly double[] _levels = new double[Capacity];
    private readonly EffectiveVisibilityObserver _visibility;
    private DispatcherTimer? _timer;
    private int _newest = -1;
    private int _count;
    private double _peak;
    private bool _hasPeak;

    static StrataVoiceWaveform()
    {
        AffectsRender<StrataVoiceWaveform>(ForegroundProperty, BarWidthProperty, BarSpacingProperty);
        LevelProperty.Changed.AddClassHandler<StrataVoiceWaveform>(static (meter, _) => meter.OnLevelChanged());
        IsActiveProperty.Changed.AddClassHandler<StrataVoiceWaveform>(static (meter, _) => meter.OnIsActiveChanged());
    }

    public StrataVoiceWaveform()
    {
        _visibility = new EffectiveVisibilityObserver(this, UpdateSampling);
    }

    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double BarWidth
    {
        get => GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double BarSpacing
    {
        get => GetValue(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    /// <summary>How many bars have been heard since the meter last became active (at most the meter's capacity).</summary>
    internal int HeardCount => _count;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _visibility.Subscribe();
        UpdateSampling();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _visibility.Unsubscribe();
        StopSampling();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLevelChanged()
    {
        var level = Level;
        if (!_hasPeak || level > _peak)
            _peak = level;
        _hasPeak = true;
    }

    private void OnIsActiveChanged()
    {
        if (IsActive)
        {
            _count = 0;
            _newest = -1;
            _hasPeak = false;
            InvalidateVisual();
        }

        UpdateSampling();
    }

    private void UpdateSampling()
    {
        if (IsActive && this.IsAttachedToVisualTree() && IsEffectivelyVisible)
        {
            if (_timer is null)
            {
                // Normal priority: background work waits while Windows reports pending input, which would stall
                // the meter exactly while the user moves the pointer.
                _timer = new DispatcherTimer(SampleInterval, DispatcherPriority.Normal, (_, _) => Sample());
                _timer.Start();
            }
            return;
        }

        StopSampling();
    }

    private void StopSampling()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>Adds one bar: the loudest level since the last bar, or the current level when none arrived.</summary>
    internal void Sample()
    {
        var level = _hasPeak ? _peak : Level;
        _hasPeak = false;
        _newest = (_newest + 1) % Capacity;
        _levels[_newest] = level;
        _count = Math.Min(_count + 1, Capacity);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
            return;

        var size = Bounds.Size;
        var barWidth = Math.Max(1, BarWidth);
        var pitch = barWidth + Math.Max(0, BarSpacing);
        var slots = (int)Math.Floor((size.Width + Math.Max(0, BarSpacing)) / pitch);
        if (slots <= 0 || size.Height <= 0)
            return;

        var middle = size.Height / 2;
        var radius = barWidth / 2;
        var heard = Math.Min(_count, slots);

        // Slot 0 is the newest bar, at the end edge; quiet dots fill the slots nothing has been heard in yet.
        if (heard < slots)
        {
            using (context.PushOpacity(RestingOpacity))
            {
                for (var slot = heard; slot < slots; slot++)
                    context.DrawRectangle(brush, null, BarRect(slot, barWidth), radius, radius);
            }
        }

        for (var slot = 0; slot < heard; slot++)
        {
            var level = _levels[(_newest - slot + Capacity) % Capacity];
            context.DrawRectangle(brush, null, BarRect(slot, Math.Max(barWidth, level * size.Height)), radius, radius);
        }

        Rect BarRect(int slot, double height)
            => new(size.Width - barWidth - slot * pitch, middle - height / 2, barWidth, height);
    }
}
