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
                baseline = result.BaselineMetrics,
                optimized = result.OptimizedMetrics,
                result.BaselineText,
                result.OptimizedText,
                result.UpliftText
            };

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedReportPath, json, timeoutCts.Token);

            Console.WriteLine($"CHAT_PERF_BASELINE::{result.BaselineText}");
            Console.WriteLine($"CHAT_PERF_OPTIMIZED::{result.OptimizedText}");
            Console.WriteLine($"CHAT_PERF_UPLIFT::{result.UpliftText}");
            Console.WriteLine($"CHAT_PERF_IDLE::FPS {result.IdleMetrics.AvgFps:F1} · p95 {result.IdleMetrics.P95FrameMs:F1} ms");
            Console.WriteLine($"CHAT_PERF_RENDER::PageVisible={result.PerfPageVisible};ShellVisible={result.ShellVisible};ShellBounds={result.ShellWidth:F1}x{result.ShellHeight:F1}");
            Console.WriteLine($"CHAT_PERF_REPORT::{resolvedReportPath}");

            var pass = result.OptimizedMetrics.AvgFps >= result.BaselineMetrics.AvgFps &&
                       result.OptimizedMetrics.P95FrameMs <= result.BaselineMetrics.P95FrameMs;

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
}
