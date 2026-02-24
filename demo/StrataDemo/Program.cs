using Avalonia;
using AvaloniaMcp.Diagnostics;
using System;
using System.IO;

namespace StrataDemo;

class Program
{
    public static bool RunChatPerfBenchmark { get; private set; }
    public static string ChatPerfReportPath { get; private set; } = Path.Combine("artifacts", "chat-perf-report.json");
    public static int ChatPerfTargetFps { get; private set; } = 120;

    [STAThread]
    public static void Main(string[] args)
    {
        ParseArguments(args);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseMcpDiagnostics()
            .LogToTrace();

        if (RunChatPerfBenchmark)
        {
            builder.AfterPlatformServicesSetup(_ =>
            {
                var targetFps = Math.Clamp(ChatPerfTargetFps, 30, 360);
                RenderTimerConfigurator.TryConfigure(targetFps);
            });
        }

        return builder;
    }

    private static void ParseArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--chat-perf", StringComparison.OrdinalIgnoreCase))
            {
                RunChatPerfBenchmark = true;
                continue;
            }

            if (arg.StartsWith("--chat-perf-report=", StringComparison.OrdinalIgnoreCase))
            {
                ChatPerfReportPath = arg["--chat-perf-report=".Length..];
                continue;
            }

            if (arg.StartsWith("--chat-perf-target-fps=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = arg["--chat-perf-target-fps=".Length..];
                if (int.TryParse(raw, out var parsed))
                    ChatPerfTargetFps = parsed;
                continue;
            }

            if (arg.Equals("--chat-perf-report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                ChatPerfReportPath = args[++i];
                continue;
            }

            if (arg.Equals("--chat-perf-target-fps", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsed))
                    ChatPerfTargetFps = parsed;
            }
        }
    }

}
