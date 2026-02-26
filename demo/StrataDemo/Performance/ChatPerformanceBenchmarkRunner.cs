using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StrataDemo;

internal sealed class ChatPerformanceBenchmarkRunner
{
    private readonly TopLevel _renderRoot;
    private readonly StrataChatShell _chatShell;
    private readonly StrataChatTranscript _optimizedTranscript;
    private readonly StackPanel _baselineTranscript;
    private readonly ChatPerformanceRunOptions _options;
    private ScrollViewer? _transcriptScrollViewer;
    private string? _largeMarkdown;
    private object? _activeTranscript;

    public ChatPerformanceBenchmarkRunner(
        TopLevel renderRoot,
        StrataChatShell chatShell,
        StrataChatTranscript transcript,
        ChatPerformanceRunOptions? options = null)
    {
        _renderRoot = renderRoot;
        _chatShell = chatShell;
        _optimizedTranscript = transcript;
        _baselineTranscript = new StackPanel { Spacing = 6 };
        _options = options ?? ChatPerformanceRunOptions.Default;

        SetTranscriptMode(useVirtualizedTranscript: true);
    }

    public void SeedTranscript()
    {
        if (_activeTranscript is null)
            SetTranscriptMode(useVirtualizedTranscript: true);

        ClearTranscript(_baselineTranscript);
        ClearTranscript(_optimizedTranscript);
        _transcriptScrollViewer = null;
        var startTime = DateTime.Today.AddHours(9).AddMinutes(5);

        for (var i = 0; i < _options.MessageCount; i++)
        {
            var isUser = i % 3 == 0;
            var role = isUser ? StrataChatRole.User : StrataChatRole.Assistant;
            var author = isUser ? "You" : "Strata";
            var status = isUser ? "sent" : "grounded";
            var stamp = startTime.AddSeconds(i * 7).ToString("HH:mm");

            var text = isUser
                ? $"Message {i + 1}: summarize the current rollout risk and expected customer impact."
                : $"Response {i + 1}: p95 and GC pause remain within thresholds for stage {(i % 5) + 1}. Continue monitoring and keep rollback gate armed.";

            if (!isUser && i % 11 == 0)
            {
                text += " Validate canary pool, watch error budgets, and keep mitigation plan pinned for rapid rollback.";
            }

            AddTranscriptItem(new StrataChatMessage
            {
                Role = role,
                Author = author,
                Timestamp = stamp,
                StatusText = status,
                IsEditable = false,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        _chatShell.ResetAutoScroll();
        _chatShell.ScrollToEnd();
    }

    public async Task<UiFrameMetrics> MeasureIdleRenderMetricsAsync(TimeSpan duration, CancellationToken token)
    {
        using var probe = new UiFrameProbe(_renderRoot);
        var until = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < until)
            await Task.Delay(16, token);

        return probe.CaptureMetrics();
    }

    public async Task<ChatPerfScenarioResult> RunScenarioSeriesAsync(ChatPerfScenarioProfile profile, CancellationToken token)
    {
        var iterationCount = Math.Max(1, _options.Iterations);
        var samples = new List<ChatPerfScenarioResult>(iterationCount);

        for (var i = 0; i < iterationCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var sample = await RunScenarioAsync(profile, token);
            samples.Add(sample);
            await Task.Delay(180, token);
        }

        return AggregateScenarioResults(samples);
    }

    public void ResetToOptimizedDefaults()
    {
        SetTranscriptMode(useVirtualizedTranscript: true);
        _chatShell.ResetAutoScroll();
    }

    public static string FormatPerformanceMetrics(ChatPerfScenarioResult result)
    {
        if (result.FrameMetrics.Frames == 0)
            return "No frame samples captured.";

        var heapMb = result.HeapBytes / (1024d * 1024d);
        return $"FPS {result.FrameMetrics.AvgFps:F1} · avg {result.FrameMetrics.AvgFrameMs:F1} ms · p95 {result.FrameMetrics.P95FrameMs:F1} ms · worst {result.FrameMetrics.WorstFrameMs:F1} ms · slow>20ms {result.FrameMetrics.SlowFramePercent:F1}% · frames {result.FrameMetrics.Frames} · stream updates {result.StreamUpdates} · seed {result.SeedDurationMs:F0} ms · realized {result.RealizedMessages} · heap {heapMb:F1} MB";
    }

    public static string FormatUplift(ChatPerfScenarioResult baseline, ChatPerfScenarioResult optimized)
    {
        if (baseline.FrameMetrics.Frames == 0 || optimized.FrameMetrics.Frames == 0)
            return "Unable to calculate uplift.";

        var fpsGainPct = baseline.FrameMetrics.AvgFps <= 0
            ? 0
            : ((optimized.FrameMetrics.AvgFps - baseline.FrameMetrics.AvgFps) / baseline.FrameMetrics.AvgFps) * 100d;

        var p95ImprovementMs = baseline.FrameMetrics.P95FrameMs - optimized.FrameMetrics.P95FrameMs;
        var slowFrameImprovementPct = baseline.FrameMetrics.SlowFramePercent - optimized.FrameMetrics.SlowFramePercent;
        var seedImprovementMs = baseline.SeedDurationMs - optimized.SeedDurationMs;
        var realizedReductionPct = baseline.RealizedMessages <= 0
            ? 0
            : ((baseline.RealizedMessages - optimized.RealizedMessages) / (double)baseline.RealizedMessages) * 100d;
        var heapReductionPct = baseline.HeapBytes <= 0
            ? 0
            : ((baseline.HeapBytes - optimized.HeapBytes) / (double)baseline.HeapBytes) * 100d;

        return $"FPS uplift {fpsGainPct:+0.0;-0.0;0}% · p95 improvement {p95ImprovementMs:+0.0;-0.0;0} ms · slow-frame reduction {slowFrameImprovementPct:+0.0;-0.0;0}% · seed improvement {seedImprovementMs:+0;-0;0} ms · realized reduction {realizedReductionPct:+0.0;-0.0;0}% · heap reduction {heapReductionPct:+0.0;-0.0;0}%";
    }

    private async Task<ChatPerfScenarioResult> RunScenarioAsync(ChatPerfScenarioProfile profile, CancellationToken token)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var useVirtualizedTranscript = profile == ChatPerfScenarioProfile.Optimized;
        SetTranscriptMode(useVirtualizedTranscript);
        await Task.Delay(80, token);

        var seedStopwatch = System.Diagnostics.Stopwatch.StartNew();
        SeedTranscript();
        await Task.Delay(220, token);
        seedStopwatch.Stop();

        var streamRenderIntervalMs = Math.Max(16, _options.StreamRenderIntervalMs);
        var markdownThrottleMs = Math.Max(0, _options.OptimizedMarkdownThrottleMs);
        var realizedMessages = CountRealizedMessages();
        var heapBytes = GC.GetTotalMemory(forceFullCollection: false);

        var streamingMarkdown = new StrataMarkdown
        {
            IsInline = true,
            Markdown = string.Empty,
            EnableAppendTailParsing = true,
            StreamingRebuildThrottleMs = markdownThrottleMs,
        };

        var streamingMessage = new StrataChatMessage
        {
            Role = StrataChatRole.Assistant,
            Author = "Strata",
            Timestamp = DateTime.Now.ToString("HH:mm"),
            StatusText = "streaming",
            IsStreaming = true,
            IsEditable = false,
            Content = streamingMarkdown
        };

        AddTranscriptItem(streamingMessage);
        _chatShell.ScrollToEnd();
        await Task.Delay(180, token);

        var fullMarkdown = BuildLargePerformanceMarkdown();
        var cursor = 0;
        var streamUpdates = 0;
        var lastStreamRenderAt = TimeSpan.Zero;

        using var probe = new UiFrameProbe(_renderRoot);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(_options.ScenarioSeconds))
        {
            token.ThrowIfCancellationRequested();

            if (cursor < fullMarkdown.Length)
            {
                cursor = Math.Min(fullMarkdown.Length, cursor + _options.StreamChunkSizeChars);

                if (stopwatch.Elapsed - lastStreamRenderAt >= TimeSpan.FromMilliseconds(streamRenderIntervalMs) ||
                    cursor == fullMarkdown.Length)
                {
                    streamingMarkdown.Markdown = fullMarkdown[..cursor];

                    streamUpdates++;
                    lastStreamRenderAt = stopwatch.Elapsed;
                }
            }

            if (TryGetScrollMetrics(out var extentHeight, out var viewportHeight))
            {
                var maxOffset = Math.Max(0, extentHeight - viewportHeight);
                if (maxOffset > 0)
                {
                    // Realistic chat behavior: stay near recent history while streaming,
                    // with moderate up/down motion rather than full-history sweeps.
                    var recentBandStart = Math.Max(0, maxOffset - (viewportHeight * 10));
                    var recentBandRange = Math.Min(maxOffset - recentBandStart, viewportHeight * 6);
                    var wave = (Math.Sin(stopwatch.Elapsed.TotalMilliseconds / 900d) + 1d) * 0.5d;
                    var target = recentBandStart + (recentBandRange * wave);
                    ScrollToVerticalOffset(target);
                }
            }

            await Task.Delay(16, token);
        }

        streamingMessage.IsStreaming = false;
        streamingMessage.StatusText = $"{streamUpdates} updates";
        _chatShell.ScrollToEnd();
        await Task.Delay(220, token);

        var frameMetrics = probe.CaptureMetrics();
        return new ChatPerfScenarioResult(
            FrameMetrics: frameMetrics,
            StreamUpdates: streamUpdates,
            MessageCount: GetTranscriptItemCount(),
            StreamedChars: fullMarkdown.Length,
            SeedDurationMs: seedStopwatch.Elapsed.TotalMilliseconds,
            RealizedMessages: realizedMessages,
            HeapBytes: heapBytes);
    }

    private void SetTranscriptMode(bool useVirtualizedTranscript)
    {
        var transcript = useVirtualizedTranscript
            ? (object)_optimizedTranscript
            : _baselineTranscript;

        if (ReferenceEquals(_activeTranscript, transcript))
            return;

        _activeTranscript = transcript;
        _chatShell.Transcript = transcript;
        _transcriptScrollViewer = null;
    }

    private static void ClearTranscript(object transcript)
    {
        switch (transcript)
        {
            case StrataChatTranscript itemsControl:
                itemsControl.Items.Clear();
                break;
            case StackPanel panel:
                panel.Children.Clear();
                break;
        }
    }

    private void AddTranscriptItem(Control item)
    {
        switch (_activeTranscript)
        {
            case StrataChatTranscript transcript:
                transcript.Items.Add(item);
                break;
            case StackPanel panel:
                panel.Children.Add(item);
                break;
            default:
                throw new InvalidOperationException("Transcript mode is not initialized.");
        }
    }

    private int GetTranscriptItemCount()
    {
        return _activeTranscript switch
        {
            StrataChatTranscript transcript => transcript.Items.Count,
            StackPanel panel => panel.Children.Count,
            _ => 0
        };
    }

    private int CountRealizedMessages()
    {
        return _activeTranscript switch
        {
            StrataChatTranscript transcript when transcript.ItemsPanelRoot is Panel panel => panel.Children.Count,
            StackPanel panel => panel.Children.Count,
            _ => 0,
        };
    }

    private bool TryGetScrollMetrics(out double extentHeight, out double viewportHeight)
    {
        var scrollViewer = GetTranscriptScrollViewer();
        if (scrollViewer is null)
        {
            extentHeight = 0;
            viewportHeight = 0;
            return false;
        }

        extentHeight = scrollViewer.Extent.Height;
        viewportHeight = scrollViewer.Viewport.Height;
        return true;
    }

    private void ScrollToVerticalOffset(double offsetY)
    {
        var scrollViewer = GetTranscriptScrollViewer();
        if (scrollViewer is null)
            return;

        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var clampedOffset = Math.Clamp(offsetY, 0, maxOffset);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, clampedOffset);
    }

    private ScrollViewer? GetTranscriptScrollViewer()
    {
        if (_transcriptScrollViewer is not null)
            return _transcriptScrollViewer;

        _transcriptScrollViewer = _chatShell.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        return _transcriptScrollViewer;
    }

    private string BuildLargePerformanceMarkdown()
    {
        if (!string.IsNullOrWhiteSpace(_largeMarkdown))
            return _largeMarkdown;

        var sb = new StringBuilder(320_000);
        sb.AppendLine("## Streaming Stress Payload");
        sb.AppendLine();
        sb.AppendLine("This payload intentionally simulates long-form grounded responses with code blocks, bullets, and references.");
        sb.AppendLine();

        for (var i = 1; i <= 180; i++)
        {
            sb.AppendLine($"### Section {i}");
            sb.AppendLine($"- Signal window: {i * 5} seconds");
            sb.AppendLine("- Guardrails: p95 < 250ms, GC pause < 80ms");
            sb.AppendLine("- Recommended action: staged rollout with immediate rollback gate");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine("public static bool ShouldRollback(double p95Ms, double gcPauseMs, double errorRate)");
            sb.AppendLine("{");
            sb.AppendLine("    if (p95Ms > 250 || gcPauseMs > 80) return true;");
            sb.AppendLine("    return errorRate > 0.02;");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("Telemetry note: serialization allocation bursts remain the primary leading indicator for regressions in this workload.");
            sb.AppendLine();
        }

        sb.AppendLine("### References");
        sb.AppendLine("- incidents/IR-4471.md");
        sb.AppendLine("- runbooks/infra/autoscale.md");
        sb.AppendLine("- telemetry/rollout-gates.md");

        _largeMarkdown = sb.ToString();
        return _largeMarkdown;
    }

    private static ChatPerfScenarioResult AggregateScenarioResults(IReadOnlyList<ChatPerfScenarioResult> samples)
    {
        if (samples.Count == 0)
            return default;

        var aggregatedMetrics = new UiFrameMetrics(
            Frames: (int)Math.Round(Median(samples.Select(s => (double)s.FrameMetrics.Frames))),
            AvgFps: Median(samples.Select(s => s.FrameMetrics.AvgFps)),
            AvgFrameMs: Median(samples.Select(s => s.FrameMetrics.AvgFrameMs)),
            P95FrameMs: Median(samples.Select(s => s.FrameMetrics.P95FrameMs)),
            WorstFrameMs: Median(samples.Select(s => s.FrameMetrics.WorstFrameMs)),
            SlowFramePercent: Median(samples.Select(s => s.FrameMetrics.SlowFramePercent)));

        return new ChatPerfScenarioResult(
            FrameMetrics: aggregatedMetrics,
            StreamUpdates: (int)Math.Round(Median(samples.Select(s => (double)s.StreamUpdates))),
            MessageCount: (int)Math.Round(Median(samples.Select(s => (double)s.MessageCount))),
            StreamedChars: (int)Math.Round(Median(samples.Select(s => (double)s.StreamedChars))),
            SeedDurationMs: Median(samples.Select(s => s.SeedDurationMs)),
            RealizedMessages: (int)Math.Round(Median(samples.Select(s => (double)s.RealizedMessages))),
            HeapBytes: (long)Math.Round(Median(samples.Select(s => (double)s.HeapBytes))));
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(v => v).ToArray();
        if (ordered.Length == 0)
            return 0;

        if (ordered.Length % 2 == 1)
            return ordered[ordered.Length / 2];

        var right = ordered.Length / 2;
        var left = right - 1;
        return (ordered[left] + ordered[right]) / 2d;
    }
}
