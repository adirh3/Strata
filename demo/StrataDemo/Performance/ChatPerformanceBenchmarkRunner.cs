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
    private readonly StackPanel _transcript;
    private readonly ChatPerformanceRunOptions _options;
    private ScrollViewer? _transcriptScrollViewer;
    private string? _largeMarkdown;

    public ChatPerformanceBenchmarkRunner(
        TopLevel renderRoot,
        StrataChatShell chatShell,
        StackPanel transcript,
        ChatPerformanceRunOptions? options = null)
    {
        _renderRoot = renderRoot;
        _chatShell = chatShell;
        _transcript = transcript;
        _options = options ?? ChatPerformanceRunOptions.Default;
    }

    public void SeedTranscript()
    {
        _transcript.Children.Clear();
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

            _transcript.Children.Add(new StrataChatMessage
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

        ResetToOptimizedDefaults();
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
        _chatShell.ResetAutoScroll();
    }

    public static string FormatPerformanceMetrics(ChatPerfScenarioResult result)
    {
        if (result.FrameMetrics.Frames == 0)
            return "No frame samples captured.";

        return $"FPS {result.FrameMetrics.AvgFps:F1} · avg {result.FrameMetrics.AvgFrameMs:F1} ms · p95 {result.FrameMetrics.P95FrameMs:F1} ms · worst {result.FrameMetrics.WorstFrameMs:F1} ms · slow>20ms {result.FrameMetrics.SlowFramePercent:F1}% · frames {result.FrameMetrics.Frames} · stream updates {result.StreamUpdates}";
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

        return $"FPS uplift {fpsGainPct:+0.0;-0.0;0}% · p95 improvement {p95ImprovementMs:+0.0;-0.0;0} ms · slow-frame reduction {slowFrameImprovementPct:+0.0;-0.0;0}%";
    }

    private async Task<ChatPerfScenarioResult> RunScenarioAsync(ChatPerfScenarioProfile profile, CancellationToken token)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        SeedTranscript();
        await Task.Delay(220, token);

        var runLegacyMarkdownPipeline = profile == ChatPerfScenarioProfile.Baseline;
        var streamRenderIntervalMs = Math.Max(16, _options.StreamRenderIntervalMs);
        var markdownThrottleMs = runLegacyMarkdownPipeline
            ? _options.LegacyMarkdownThrottleMs
            : _options.OptimizedMarkdownThrottleMs;

        var streamingMarkdown = new StrataMarkdown
        {
            IsInline = true,
            Markdown = string.Empty,
            EnableAppendTailParsing = !runLegacyMarkdownPipeline,
            StreamingRebuildThrottleMs = Math.Max(0, markdownThrottleMs),
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

        _transcript.Children.Add(streamingMessage);
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
                    var sweep = (stopwatch.Elapsed.TotalMilliseconds / 2600d) % 2d;
                    var triangle = sweep <= 1d ? sweep : 2d - sweep;
                    ScrollToVerticalOffset(maxOffset * triangle);
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
            MessageCount: _transcript.Children.Count,
            StreamedChars: fullMarkdown.Length);
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
            StreamedChars: (int)Math.Round(Median(samples.Select(s => (double)s.StreamedChars))));
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
