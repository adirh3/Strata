using System;
using Avalonia;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>
/// A live "how long has this been running" readout ("8s", "1m 04s") counted from <see cref="Since"/>.
/// It ticks once a second only while attached and <see cref="Since"/> is set, and shows nothing for
/// the first second, so a step that finishes at once never flashes a time.
/// </summary>
/// <remarks>
/// <code>&lt;controls:StrataElapsedText Since="{Binding RunningSince}" /&gt;</code>
/// </remarks>
public class StrataElapsedText : TextBlock
{
    /// <summary>When the work started. Null shows nothing.</summary>
    public static readonly StyledProperty<DateTimeOffset?> SinceProperty =
        AvaloniaProperty.Register<StrataElapsedText, DateTimeOffset?>(nameof(Since));

    private readonly RunningElapsedClock _clock;
    private bool _isAttached;

    static StrataElapsedText()
    {
        SinceProperty.Changed.AddClassHandler<StrataElapsedText>((control, _) => control.Refresh());
    }

    public StrataElapsedText()
    {
        _clock = new RunningElapsedClock(UpdateText);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public DateTimeOffset? Since
    {
        get => GetValue(SinceProperty);
        set => SetValue(SinceProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // A running timer roots the control through its tick, so it never runs while detached.
        _isAttached = false;
        _clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        if (_isAttached && Since is not null)
            _clock.Start();
        else
            _clock.Stop();

        UpdateText();
    }

    private void UpdateText()
    {
        if (Since is not { } since)
        {
            Text = null;
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - since;
        Text = elapsed.TotalSeconds >= 1 ? RunningElapsedClock.Format(elapsed) : null;
    }
}
