using System;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A small self-contained "how long has this been running" clock used by the live tool-call and
/// terminal controls. It ticks on the UI thread while active and reports a humanized elapsed string
/// (e.g. "8s", "1m 04s", "1h 12m") so an in-progress operation reads as visibly alive rather than
/// frozen. Start/Stop are idempotent, so callers can safely re-arm it on every state refresh.
/// </summary>
internal sealed class RunningElapsedClock : IDisposable
{
    private readonly DispatcherTimer _timer;
    private DateTime _startUtc;

    public RunningElapsedClock(Action onTick, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        // Construct STOPPED. The 3-arg DispatcherTimer(interval, priority, callback) ctor auto-starts the
        // timer (IsEnabled=true) in Avalonia, which would leave it ticking before Start() is ever called.
        // Since Stop() is guarded by IsRunning, a clock that never calls Start() (e.g. a completed /
        // non-running card) could then never be stopped, and its tick closure would root the owning
        // control forever. The priority-only ctor leaves the timer stopped, so it ticks strictly between
        // explicit Start()/Stop() calls.
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = interval ?? TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => onTick();
    }

    /// <summary>True while the clock is ticking.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Time elapsed since the clock was (re)started; zero when idle.</summary>
    public TimeSpan Elapsed => IsRunning ? DateTime.UtcNow - _startUtc : TimeSpan.Zero;

    /// <summary>Begins ticking from now. No-op if already running (the start time is preserved).</summary>
    public void Start()
    {
        if (IsRunning)
            return;

        _startUtc = DateTime.UtcNow;
        IsRunning = true;
        _timer.Start();
    }

    /// <summary>Stops ticking. No-op if already idle.</summary>
    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _timer.Stop();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Formats an elapsed span as a compact, human-friendly readout. Seconds under a minute
    /// ("8s"), minutes-and-seconds under an hour ("1m 04s"), hours-and-minutes beyond ("1h 12m").
    /// </summary>
    public static string Format(TimeSpan elapsed)
    {
        var totalSeconds = (long)Math.Max(0, Math.Floor(elapsed.TotalSeconds));

        if (totalSeconds < 60)
            return $"{totalSeconds}s";

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        if (minutes < 60)
            return $"{minutes}m {seconds:D2}s";

        var hours = minutes / 60;
        minutes %= 60;
        return $"{hours}h {minutes:D2}m";
    }
}
