namespace StrataDemo;

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
    ChatPerfScenarioResult FirstPass,
    ChatPerfScenarioResult SecondPass,
    string FirstPassText,
    string SecondPassText,
    string DeltaText,
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
    int MarkdownThrottleMs)
{
    public static ChatPerformanceRunOptions Default => new(
        MessageCount: 12000,
        ScenarioSeconds: 14,
        StreamChunkSizeChars: 220,
        StreamRenderIntervalMs: 12,
        Iterations: 1,
        MarkdownThrottleMs: 0);
}
