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
    int StreamedChars,
    double SeedDurationMs,
    int RealizedMessages,
    long HeapBytes);

internal readonly record struct ChatPerfBenchmarkResult(
    UiFrameMetrics IdleMetrics,
    ChatPerfScenarioResult Baseline,
    ChatPerfScenarioResult Optimized,
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
    int Iterations,
    int LegacyMarkdownThrottleMs,
    int OptimizedMarkdownThrottleMs)
{
    public static ChatPerformanceRunOptions Default => new(
        MessageCount: 12000,
        ScenarioSeconds: 14,
        StreamChunkSizeChars: 220,
        StreamRenderIntervalMs: 12,
        Iterations: 1,
        LegacyMarkdownThrottleMs: 0,
        OptimizedMarkdownThrottleMs: 0);
}
