using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StrataDemo;

internal static class ChatPerformanceAutomation
{
    internal static async Task RunAsync(
        MainWindow mainWindow,
        IClassicDesktopStyleApplicationLifetime desktop,
        string reportPath,
        int targetFps)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(6));

        try
        {
            var result = await mainWindow.RunChatPerformanceBenchmarkAsync(timeoutCts.Token);

            var resolvedReportPath = ResolveReportPath(reportPath);
            var reportDirectory = Path.GetDirectoryName(resolvedReportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);

            var report = new
            {
                timestampUtc = DateTime.UtcNow,
                targetFps,
                renderEvidence = new
                {
                    pageVisible = result.PerfPageVisible,
                    shellVisible = result.ShellVisible,
                    shellWidth = result.ShellWidth,
                    shellHeight = result.ShellHeight
                },
                idle = result.IdleMetrics,
                firstPass = result.FirstPass,
                secondPass = result.SecondPass,
                deltas = BuildMetricDeltas(result.FirstPass, result.SecondPass),
                result.FirstPassText,
                result.SecondPassText,
                result.DeltaText
            };

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedReportPath, json, timeoutCts.Token);

            Console.WriteLine($"CHAT_PERF_PASS_ONE::{result.FirstPassText}");
            Console.WriteLine($"CHAT_PERF_PASS_TWO::{result.SecondPassText}");
            Console.WriteLine($"CHAT_PERF_DELTA::{result.DeltaText}");
            Console.WriteLine($"CHAT_PERF_IDLE::FPS {result.IdleMetrics.AvgFps:F1} · p95 {result.IdleMetrics.P95FrameMs:F1} ms");
            Console.WriteLine($"CHAT_PERF_RENDER::PageVisible={result.PerfPageVisible};ShellVisible={result.ShellVisible};ShellBounds={result.ShellWidth:F1}x{result.ShellHeight:F1}");
            Console.WriteLine($"CHAT_PERF_REPORT::{resolvedReportPath}");

            var pass = EvaluatePass(result.FirstPass, result.SecondPass);

            Environment.ExitCode = pass ? 0 : 2;
        }
        catch (OperationCanceledException)
        {
            Environment.ExitCode = 3;
            Console.Error.WriteLine("CHAT_PERF_ERROR::Benchmark canceled or timed out.");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine($"CHAT_PERF_ERROR::{ex}");
        }
        finally
        {
            desktop.Shutdown(Environment.ExitCode);
        }
    }

    private static string ResolveReportPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
    }

    private static object BuildMetricDeltas(ChatPerfScenarioResult firstPass, ChatPerfScenarioResult secondPass)
    {
        return new
        {
            fpsDelta = secondPass.FrameMetrics.AvgFps - firstPass.FrameMetrics.AvgFps,
            avgFrameMsDelta = secondPass.FrameMetrics.AvgFrameMs - firstPass.FrameMetrics.AvgFrameMs,
            p95FrameMsDelta = secondPass.FrameMetrics.P95FrameMs - firstPass.FrameMetrics.P95FrameMs,
            worstFrameMsDelta = secondPass.FrameMetrics.WorstFrameMs - firstPass.FrameMetrics.WorstFrameMs,
            slowFramePctDelta = secondPass.FrameMetrics.SlowFramePercent - firstPass.FrameMetrics.SlowFramePercent,
            seedMsDelta = secondPass.SeedDurationMs - firstPass.SeedDurationMs,
            realizedMessagesDelta = secondPass.RealizedMessages - firstPass.RealizedMessages,
            heapBytesDelta = secondPass.HeapBytes - firstPass.HeapBytes
        };
    }

    private static bool EvaluatePass(ChatPerfScenarioResult firstPass, ChatPerfScenarioResult secondPass)
    {
        const double maxFpsRegression = 12.0;
        const double maxP95RegressionMs = 12.0;
        const double maxSlowFrameRegressionPct = 8.0;

        var fpsDelta = secondPass.FrameMetrics.AvgFps - firstPass.FrameMetrics.AvgFps;
        var p95Delta = secondPass.FrameMetrics.P95FrameMs - firstPass.FrameMetrics.P95FrameMs;
        var slowFrameDelta = secondPass.FrameMetrics.SlowFramePercent - firstPass.FrameMetrics.SlowFramePercent;

        return fpsDelta >= -maxFpsRegression
            && p95Delta <= maxP95RegressionMs
            && slowFrameDelta <= maxSlowFrameRegressionPct;
    }
}
