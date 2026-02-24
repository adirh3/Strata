namespace StrataDemo;

internal enum ChatPerfScenarioProfile
{
    Baseline,
    Optimized,
}

internal readonly record struct ChatPerfScenarioResult(
    UiFrameMetrics FrameMetrics,
    int StreamUpdates,
    int MessageCount,
    int StreamedChars);

internal readonly record struct ChatPerfBenchmarkResult(
    UiFrameMetrics IdleMetrics,
    UiFrameMetrics BaselineMetrics,
    UiFrameMetrics OptimizedMetrics,
    string BaselineText,
    string OptimizedText,
    string UpliftText,
    bool PerfPageVisible,
    bool ShellVisible,
    double ShellWidth,
    double ShellHeight);

internal readonly record struct ChatPerformanceRunOptions(
    int MessageCount,
    int ScenarioSeconds,
    int StreamChunkSizeChars,
    int StreamRenderIntervalMs,
    int Iterations)
{
    public static ChatPerformanceRunOptions Default => new(
        MessageCount: 1200,
        ScenarioSeconds: 10,
        StreamChunkSizeChars: 950,
        StreamRenderIntervalMs: 85,
        Iterations: 2);
}
