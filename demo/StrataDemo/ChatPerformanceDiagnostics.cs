using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace StrataDemo;

internal readonly record struct UiFrameMetrics(
    int Frames,
    double AvgFps,
    double AvgFrameMs,
    double P95FrameMs,
    double WorstFrameMs,
    double SlowFramePercent);

internal sealed class UiFrameProbe : IDisposable
{
    private readonly TopLevel _topLevel;
    private readonly object _gate = new();
    private readonly List<double> _frameTimesMs = new(1024);
    private TimeSpan? _lastFrameTimestamp;
    private bool _disposed;

    public UiFrameProbe(TopLevel topLevel)
    {
        _topLevel = topLevel;
        RequestNextFrame();
    }

    private void RequestNextFrame()
    {
        if (_disposed)
            return;

        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            if (_lastFrameTimestamp is TimeSpan last)
            {
                var deltaMs = (timestamp - last).TotalMilliseconds;
                if (deltaMs > 0 && deltaMs < 1000)
                    _frameTimesMs.Add(deltaMs);
            }

            _lastFrameTimestamp = timestamp;
        }

        RequestNextFrame();
    }

    public UiFrameMetrics CaptureMetrics(double slowFrameThresholdMs = 20)
    {
        lock (_gate)
        {
            if (_frameTimesMs.Count == 0)
                return new UiFrameMetrics(0, 0, 0, 0, 0, 0);

            var ordered = _frameTimesMs.ToArray();
            Array.Sort(ordered);

            var count = ordered.Length;
            var sum = ordered.Sum();
            var avgFrameMs = sum / count;
            var avgFps = avgFrameMs <= 0 ? 0 : 1000d / avgFrameMs;
            var p95Index = Math.Clamp((int)Math.Floor((count - 1) * 0.95d), 0, count - 1);
            var p95FrameMs = ordered[p95Index];
            var worstFrameMs = ordered[^1];
            var slowCount = ordered.Count(frameMs => frameMs > slowFrameThresholdMs);
            var slowFramePercent = slowCount * 100d / count;

            return new UiFrameMetrics(
                Frames: count,
                AvgFps: avgFps,
                AvgFrameMs: avgFrameMs,
                P95FrameMs: p95FrameMs,
                WorstFrameMs: worstFrameMs,
                SlowFramePercent: slowFramePercent);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
