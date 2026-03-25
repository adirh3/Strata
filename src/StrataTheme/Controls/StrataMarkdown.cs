using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

public readonly record struct StrataMarkdownDiagnosticsSnapshot(
    long InstanceCount,
    long RebuildCount,
    long FullParseCount,
    long IncrementalParseCount,
    long PlainTextFastPathCount,
    long ParsedBlockCount,
    long ControlCreateCount,
    long TableRenderCount,
    long TableReuseCount,
    long TableRowCount,
    long TableCellCount,
    long TotalRebuildTicks)
{
    public double TotalRebuildMilliseconds => TotalRebuildTicks * 1000d / Stopwatch.Frequency;

    public double AverageRebuildMilliseconds => RebuildCount == 0
        ? 0
        : TotalRebuildMilliseconds / RebuildCount;

    public static StrataMarkdownDiagnosticsSnapshot operator -(
        StrataMarkdownDiagnosticsSnapshot after,
        StrataMarkdownDiagnosticsSnapshot before) => new(
            after.InstanceCount - before.InstanceCount,
            after.RebuildCount - before.RebuildCount,
            after.FullParseCount - before.FullParseCount,
            after.IncrementalParseCount - before.IncrementalParseCount,
            after.PlainTextFastPathCount - before.PlainTextFastPathCount,
            after.ParsedBlockCount - before.ParsedBlockCount,
            after.ControlCreateCount - before.ControlCreateCount,
            after.TableRenderCount - before.TableRenderCount,
            after.TableReuseCount - before.TableReuseCount,
            after.TableRowCount - before.TableRowCount,
            after.TableCellCount - before.TableCellCount,
            after.TotalRebuildTicks - before.TotalRebuildTicks);
}

/// <summary>
/// Lightweight markdown renderer. Supports headings, bullet lists, paragraphs,
/// inline links, and fenced code blocks with syntax highlighting (via TextMate).
/// </summary>
/// <remarks>
/// <para><b>XAML usage (standalone card):</b></para>
/// <code>
/// &lt;controls:StrataMarkdown Markdown="## Hello\nSome **text** here."
///                            ShowTitle="True" Title="Response" /&gt;
/// </code>
/// <para><b>XAML usage (inline in a chat message):</b></para>
/// <code>
/// &lt;controls:StrataMarkdown Markdown="{Binding Text}" IsInline="True" /&gt;
/// </code>
/// </remarks>
public class StrataMarkdown : ContentControl
{
    // ── Cached static layout values to avoid repeated heap allocations ──
    private static readonly Thickness BulletDotMargin = new(0, 7, 0, 0);
    private static readonly CornerRadius BulletDotCornerRadius = new(2.5);
    private static readonly Thickness ZeroThickness = new(0);
    private static readonly Thickness HrMargin = new(0, 4, 0, 4);

    private readonly StackPanel _root;
    private readonly Border _surface;
    private readonly TextBlock _title;
    private readonly StackPanel _contentHost;
    private IBrush? _inlineCodeBrush;
    private IBrush? _linkBrush;
    private readonly Dictionary<Run, string> _linkRuns = new();
    private string _lastThemeVariant = string.Empty;
    private double _bodyFontSize = 14;
    private readonly Dictionary<string, StrataChart> _chartCache = new();
    private readonly HashSet<string> _chartKeysUsed = new();
    private readonly Dictionary<string, StrataMermaid> _diagramCache = new();
    private readonly HashSet<string> _diagramKeysUsed = new();
    private Border? _chartPlaceholder;

    private readonly Dictionary<string, StrataConfidence> _confidenceCache = new();
    private readonly HashSet<string> _confidenceKeysUsed = new();

    private readonly Dictionary<string, StrataFork> _comparisonCache = new();
    private readonly HashSet<string> _comparisonKeysUsed = new();

    private readonly Dictionary<string, StrataCard> _cardCache = new();
    private readonly HashSet<string> _cardKeysUsed = new();

    private readonly Dictionary<string, Control> _sourcesCache = new();
    private readonly HashSet<string> _sourcesKeysUsed = new();

    private readonly Dictionary<string, MarkdownTableView> _tableCache = new();
    private readonly HashSet<string> _tableKeysUsed = new();

    private static long _diagnosticInstanceCount;
    private static long _diagnosticRebuildCount;
    private static long _diagnosticFullParseCount;
    private static long _diagnosticIncrementalParseCount;
    private static long _diagnosticPlainTextFastPathCount;
    private static long _diagnosticParsedBlockCount;
    private static long _diagnosticControlCreateCount;
    private static long _diagnosticTableRenderCount;
    private static long _diagnosticTableReuseCount;
    private static long _diagnosticTableRowCount;
    private static long _diagnosticTableCellCount;
    private static long _diagnosticTotalRebuildTicks;

    private Border? _blockPlaceholder;

    // ── Cached shared objects ──
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // ── Block group merging ──
    // Consecutive same-kind text blocks are merged into a single SelectableTextBlock
    // to reduce control count. Each MdBlockGroup describes a run of blocks rendered
    // by one control.
    private readonly record struct MdBlockGroup(int StartIndex, int Count, MdBlockKind Kind, int Level);

    private readonly List<MdBlockGroup> _groupListA = new();
    private readonly List<MdBlockGroup> _groupListB = new();
    private List<MdBlockGroup> _previousGroups;

    // ── Incremental state ──
    private readonly List<MdBlock> _blockListA = new();
    private readonly List<MdBlock> _blockListB = new();
    private List<MdBlock> _previousBlocks;
    private string? _previousMarkdownNormalized;
    private int _previousMarkdownLength;

    // ── Rebuild debouncing for rapid streaming ──
    private bool _rebuildQueued;
    private System.Threading.CancellationTokenSource? _rebuildDelayCts;
    private DateTime _lastRebuildAtUtc;

    // ── Reusable buffers to avoid per-rebuild allocations ──
    private readonly List<string> _evictBuffer = new();
    private readonly MarkdownParser _parser = new();
    private static readonly Regex PlainTextMarkdownBlockPattern = new(
        @"(^|\n)\s{0,3}(#{1,6}\s+|[-*+]\s+|\d+\.\s+|>\s+|```|~~~|[-*]\s+\[([ xX])\]\s+)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex PlainTextMarkdownInlinePattern = new(
        @"(\[[^\]\r\n]+\]\([^)\r\n]+\)|!\[[^\]\r\n]*\]\([^)\r\n]+\)|`[^`\r\n]+`|\*\*[^*\r\n]+\*\*|__[^_\r\n]+__|~~[^~\r\n]+~~|(?<!\w)_[^_\r\n]+_(?!\w))",
        RegexOptions.Compiled);
    private static readonly Regex PlainTextMarkdownTablePattern = new(
        @"^\s*\|?.+\|.+\n\s*\|?\s*:?-{3,}",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex PlainTextMarkdownRulePattern = new(
        @"(^|\n)\s{0,3}([-*_])(?:\s*\2){2,}\s*(\n|$)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Markdown source text. The control re-renders whenever this changes.</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<StrataMarkdown, string?>(nameof(Markdown));

    /// <summary>Title text shown above the rendered content when <see cref="ShowTitle"/> is true.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataMarkdown, string>(nameof(Title), "Markdown");

    /// <summary>Whether to display the <see cref="Title"/> header above the content.</summary>
    public static readonly StyledProperty<bool> ShowTitleProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(ShowTitle));

    /// <summary>When true, strips the card-like surface border for embedding inside other controls.</summary>
    public static readonly StyledProperty<bool> IsInlineProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(IsInline));

    /// <summary>
    /// Internal performance test toggle: when true, append updates reparse only the
    /// last unstable block instead of the full document.
    /// </summary>
    internal static readonly StyledProperty<bool> EnableAppendTailParsingProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(EnableAppendTailParsing), true);

    /// <summary>
    /// Internal performance test toggle: minimum delay between rebuild passes
    /// during rapid markdown updates.
    /// </summary>
    internal static readonly StyledProperty<int> StreamingRebuildThrottleMsProperty =
        AvaloniaProperty.Register<StrataMarkdown, int>(nameof(StreamingRebuildThrottleMs), 42);

    static StrataMarkdown()
    {
        MarkdownProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.ScheduleRebuild());
        TitleProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.UpdateTitle());
        ShowTitleProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.UpdateTitle());
        IsInlineProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.UpdateSurfaceMode());
        FlowDirectionProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.ScheduleRebuild());
        EnableAppendTailParsingProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.ScheduleRebuild());
        StreamingRebuildThrottleMsProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.ScheduleRebuild());
    }

    public static StrataMarkdownDiagnosticsSnapshot CaptureDiagnostics() => new(
        System.Threading.Interlocked.Read(ref _diagnosticInstanceCount),
        System.Threading.Interlocked.Read(ref _diagnosticRebuildCount),
        System.Threading.Interlocked.Read(ref _diagnosticFullParseCount),
        System.Threading.Interlocked.Read(ref _diagnosticIncrementalParseCount),
        System.Threading.Interlocked.Read(ref _diagnosticPlainTextFastPathCount),
        System.Threading.Interlocked.Read(ref _diagnosticParsedBlockCount),
        System.Threading.Interlocked.Read(ref _diagnosticControlCreateCount),
        System.Threading.Interlocked.Read(ref _diagnosticTableRenderCount),
        System.Threading.Interlocked.Read(ref _diagnosticTableReuseCount),
        System.Threading.Interlocked.Read(ref _diagnosticTableRowCount),
        System.Threading.Interlocked.Read(ref _diagnosticTableCellCount),
        System.Threading.Interlocked.Read(ref _diagnosticTotalRebuildTicks));

    public static void ResetDiagnostics()
    {
        System.Threading.Interlocked.Exchange(ref _diagnosticInstanceCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticRebuildCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticFullParseCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticIncrementalParseCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticPlainTextFastPathCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticParsedBlockCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticControlCreateCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticTableRenderCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticTableReuseCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticTableRowCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticTableCellCount, 0);
        System.Threading.Interlocked.Exchange(ref _diagnosticTotalRebuildTicks, 0);
    }

    /// <summary>
    /// Releases caches, event handlers, and disposables when this control leaves the
    /// visual tree. Without this, orphaned StrataMarkdown instances hold charts,
    /// diagrams, tables, link-handler delegates, and CancellationTokenSources in memory
    /// indefinitely.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _rebuildDelayCts?.Cancel();
        _rebuildDelayCts?.Dispose();
        _rebuildDelayCts = null;
        _rebuildQueued = false;

        // Unsubscribe link handlers from all SelectableTextBlocks in _contentHost
        DetachLinkHandlers();

        _linkRuns.Clear();

        _chartCache.Clear();
        _chartKeysUsed.Clear();
        _diagramCache.Clear();
        _diagramKeysUsed.Clear();
        _confidenceCache.Clear();
        _confidenceKeysUsed.Clear();
        _comparisonCache.Clear();
        _comparisonKeysUsed.Clear();
        _cardCache.Clear();
        _cardKeysUsed.Clear();
        _sourcesCache.Clear();
        _sourcesKeysUsed.Clear();
        _tableCache.Clear();
        _tableKeysUsed.Clear();

        _contentHost.Children.Clear();
        _previousBlocks.Clear();
        _previousMarkdownNormalized = null;
        _previousMarkdownLength = 0;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // If re-attached after being detached (e.g. control recycling), rebuild
        // from the current Markdown value since OnDetachedFromVisualTree clears state.
        if (Markdown is not null && _contentHost.Children.Count == 0)
            ScheduleRebuild();
    }

    /// <summary>Detach Tapped/PointerMoved handlers from all SelectableTextBlocks in the content host.</summary>
    private void DetachLinkHandlers()
    {
        DetachLinkHandlersFromChildren(_contentHost);
    }

    private void DetachLinkHandlersFromChildren(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is SelectableTextBlock tb)
            {
                tb.Tapped -= OnLinkTapped;
                tb.PointerMoved -= OnTextBlockPointerMoved;
            }
            else if (child is Panel nested)
            {
                DetachLinkHandlersFromChildren(nested);
            }
            else if (child is Decorator decorator && decorator.Child is Panel decoratorPanel)
            {
                DetachLinkHandlersFromChildren(decoratorPanel);
            }
        }
    }

    /// <summary>
    /// Debounces rapid Markdown changes (e.g., streaming tokens) into a single rebuild pass.
    /// This prevents multiple full parses within the same UI frame.
    /// </summary>
    private void ScheduleRebuild()
    {
        if (_rebuildQueued)
            return;

        var throttleMs = Math.Max(0, StreamingRebuildThrottleMs);
        var elapsedSinceLastRebuildMs = _lastRebuildAtUtc == default
            ? int.MaxValue
            : (int)Math.Max(0, (DateTime.UtcNow - _lastRebuildAtUtc).TotalMilliseconds);
        var delayMs = throttleMs <= 0
            ? 0
            : Math.Max(0, throttleMs - elapsedSinceLastRebuildMs);

        _rebuildQueued = true;

        _rebuildDelayCts?.Cancel();
        _rebuildDelayCts?.Dispose();
        _rebuildDelayCts = null;

        if (delayMs <= 0)
        {
            Dispatcher.UIThread.Post(FlushQueuedRebuild, DispatcherPriority.Loaded);
            return;
        }

        var cts = new System.Threading.CancellationTokenSource();
        var token = cts.Token;
        _rebuildDelayCts = cts;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            FlushQueuedRebuild();
        }, DispatcherPriority.Background);
    }

    private void FlushQueuedRebuild()
    {
        if (!_rebuildQueued)
            return;

        _rebuildQueued = false;
        _lastRebuildAtUtc = DateTime.UtcNow;
        _rebuildDelayCts?.Dispose();
        _rebuildDelayCts = null;
        Rebuild();
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowTitle
    {
        get => GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public bool IsInline
    {
        get => GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    internal bool EnableAppendTailParsing
    {
        get => GetValue(EnableAppendTailParsingProperty);
        set => SetValue(EnableAppendTailParsingProperty, value);
    }

    internal int StreamingRebuildThrottleMs
    {
        get => GetValue(StreamingRebuildThrottleMsProperty);
        set => SetValue(StreamingRebuildThrottleMsProperty, value);
    }

    public StrataMarkdown()
    {
        System.Threading.Interlocked.Increment(ref _diagnosticInstanceCount);

        _title = new TextBlock();
        _title.Classes.Add("strata-md-title");

        _contentHost = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _surface = new Border { Child = _contentHost };
        _surface.Classes.Add("strata-md-surface");

        _root = new StackPanel { Spacing = 6 };
        _root.Children.Add(_title);
        _root.Children.Add(_surface);

        Content = _root;
        _previousBlocks = _blockListA;
        _previousGroups = _groupListA;
        _lastThemeVariant = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light).ToString();
        // Skip Rebuild() — Markdown is null at construction time, so Rebuild()
        // just hits the IsNullOrWhiteSpace fast path.  The property-changed handler
        // will trigger a rebuild when Markdown is actually set.
        UpdateTitle();
        UpdateSurfaceMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (string.Equals(change.Property.Name, nameof(FontSize), StringComparison.Ordinal))
        {
            _previousBlocks.Clear();
            _previousGroups.Clear();
            _previousMarkdownNormalized = null;
            _previousMarkdownLength = 0;
            Rebuild();
            return;
        }

        if (!string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
            return;

        var currentVariant = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light).ToString();
        if (string.Equals(currentVariant, _lastThemeVariant, StringComparison.Ordinal))
            return;

        _lastThemeVariant = currentVariant;
        _inlineCodeBrush = null;
        _linkBrush = null;
        // Force full rebuild (not incremental) since brushes changed
        _previousBlocks.Clear();
        _previousGroups.Clear();
        _previousMarkdownNormalized = null;
        _previousMarkdownLength = 0;
        Rebuild();
    }

    private void UpdateTitle()
    {
        _title.Text = Title;
        _title.IsVisible = ShowTitle && !string.IsNullOrWhiteSpace(Title);
    }

    private void UpdateSurfaceMode()
    {
        if (IsInline)
        {
            _surface.Classes.Remove("strata-md-surface");
            _surface.Classes.Add("strata-md-surface-inline");
        }
        else
        {
            _surface.Classes.Remove("strata-md-surface-inline");
            _surface.Classes.Add("strata-md-surface");
        }
    }

    private double _cachedBodyFontSize;

    /// <summary>Returns the effective body font size, using the inherited FontSize if &gt; 12, otherwise resolving Font.SizeBody.</summary>
    private double GetBodyFontSize()
    {
        var fs = FontSize;
        if (fs > 12) return fs;

        if (_cachedBodyFontSize > 0) return _cachedBodyFontSize;

        // Try resolving from theme resources
        if (this.TryFindResource("Font.SizeBody", ActualThemeVariant, out var res) && res is double d)
        {
            _cachedBodyFontSize = d;
            return d;
        }

        return 14; // Strata default
    }

    private void Rebuild()
    {
        var rebuildStart = Stopwatch.GetTimestamp();

        try
        {
            _bodyFontSize = GetBodyFontSize();
            _chartKeysUsed.Clear();
            _diagramKeysUsed.Clear();
            _confidenceKeysUsed.Clear();
            _comparisonKeysUsed.Clear();
            _cardKeysUsed.Clear();
            _sourcesKeysUsed.Clear();
            _tableKeysUsed.Clear();

            var source = Markdown;
            if (string.IsNullOrWhiteSpace(source))
            {
                _contentHost.Children.Clear();
                _linkRuns.Clear();
                _previousBlocks.Clear();
                _previousGroups.Clear();
                _previousMarkdownNormalized = null;
                _previousMarkdownLength = 0;
                EvictStaleCaches();
                return;
            }

            var normalized = NormalizeLineEndings(source);

            // Fast path: identical content — skip everything
            if (normalized.Length == _previousMarkdownLength &&
                string.Equals(normalized, _previousMarkdownNormalized, StringComparison.Ordinal))
            {
                // Re-mark all cache keys as used so eviction doesn't clear them
                foreach (var block in _previousBlocks)
                    TrackCacheKeysForBlock(block);
                EvictStaleCaches();
                return;
            }

            if (!RequiresMarkdownRendering(normalized))
            {
                System.Threading.Interlocked.Increment(ref _diagnosticPlainTextFastPathCount);
                RebuildPlainText(normalized);
                return;
            }

            // Streaming fast path: new text appends to old text.
            var isStreamingAppend = _previousMarkdownNormalized is not null &&
                                    normalized.Length > _previousMarkdownLength &&
                                    normalized.AsSpan().StartsWith(_previousMarkdownNormalized.AsSpan());

            // Use the list NOT currently holding previous blocks to avoid allocation
            var newBlocks = ReferenceEquals(_previousBlocks, _blockListA) ? _blockListB : _blockListA;
            if (isStreamingAppend && EnableAppendTailParsing &&
                _previousMarkdownNormalized is not null && _previousBlocks.Count > 0)
            {
                ParseBlocksIncrementalAppendPooled(_previousMarkdownNormalized, _previousBlocks, normalized, newBlocks);
                System.Threading.Interlocked.Increment(ref _diagnosticIncrementalParseCount);
            }
            else
            {
                ParseBlocksPooled(normalized, newBlocks);
                System.Threading.Interlocked.Increment(ref _diagnosticFullParseCount);
            }

            System.Threading.Interlocked.Add(ref _diagnosticParsedBlockCount, newBlocks.Count);

            // Compute groups for the new blocks
            var newGroups = ReferenceEquals(_previousGroups, _groupListA) ? _groupListB : _groupListA;
            ComputeGroups(newBlocks, newGroups);

            // ── Incremental diff: only touch changed groups ──
            ApplyGroupsDiff(newBlocks, newGroups, isStreamingAppend);

            _previousBlocks = newBlocks;
            _previousGroups = newGroups;
            _previousMarkdownNormalized = normalized;
            _previousMarkdownLength = normalized.Length;

            EvictStaleCaches();
        }
        finally
        {
            System.Threading.Interlocked.Increment(ref _diagnosticRebuildCount);
            System.Threading.Interlocked.Add(ref _diagnosticTotalRebuildTicks, Stopwatch.GetTimestamp() - rebuildStart);
        }
    }

    private void RebuildPlainText(string normalized)
    {
        _contentHost.Children.Clear();
        _linkRuns.Clear();

        var textBlock = CreateRichText(normalized, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        textBlock.Classes.Add("strata-md-paragraph");
        _contentHost.Children.Add(textBlock);

        _previousBlocks.Clear();
        _previousGroups.Clear();
        _previousMarkdownNormalized = normalized;
        _previousMarkdownLength = normalized.Length;
        EvictStaleCaches();
    }

    private static bool RequiresMarkdownRendering(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.IndexOfAny(['`', '#', '*', '_', '[', '|', '>', '~']) < 0)
            return false;

        return PlainTextMarkdownBlockPattern.IsMatch(text)
            || PlainTextMarkdownInlinePattern.IsMatch(text)
            || PlainTextMarkdownTablePattern.IsMatch(text)
            || PlainTextMarkdownRulePattern.IsMatch(text);
    }

    /// <summary>
    /// Parses markdown text into a flat list of lightweight block descriptors.
    /// Delegates to <see cref="MarkdownParser.Parse"/>.
    /// </summary>
    internal static List<MdBlock> ParseBlocks(string normalized) => MarkdownParser.Parse(normalized);

    /// <summary>
    /// Instance-level parse that reuses cached StringBuilders and a pooled target list.
    /// </summary>
    private void ParseBlocksPooled(string normalized, List<MdBlock> target) => _parser.ParsePooled(normalized, target);

    internal static List<MdBlock> ParseBlocksIncrementalAppend(
        string previousNormalized,
        IReadOnlyList<MdBlock> previousBlocks,
        string nextNormalized) => MarkdownParser.ParseIncrementalAppend(previousNormalized, previousBlocks, nextNormalized);

    /// <summary>
    /// Instance-level incremental append that reuses cached StringBuilders and a pooled target list.
    /// </summary>
    private void ParseBlocksIncrementalAppendPooled(
        string previousNormalized,
        IReadOnlyList<MdBlock> previousBlocks,
        string nextNormalized,
        List<MdBlock> target) => _parser.ParseIncrementalAppendPooled(previousNormalized, previousBlocks, nextNormalized, target);

    private static string NormalizeLineEndings(string source) => MarkdownParser.NormalizeLineEndings(source);

    /// <summary>
    /// Checks whether two groups cover identical blocks (same kind, level, count, and content).
    /// </summary>
    private static bool GroupsEqual(
        IReadOnlyList<MdBlock> oldBlocks, MdBlockGroup oldGroup,
        IReadOnlyList<MdBlock> newBlocks, MdBlockGroup newGroup)
    {
        if (oldGroup.Kind != newGroup.Kind || oldGroup.Level != newGroup.Level || oldGroup.Count != newGroup.Count)
            return false;
        for (int i = 0; i < oldGroup.Count; i++)
        {
            if (!oldBlocks[oldGroup.StartIndex + i].Equals(newBlocks[newGroup.StartIndex + i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Group-level diff: compares old and new groups, reusing controls for unchanged groups,
    /// updating in-place where possible, and creating/removing controls as needed.
    /// Each group maps to exactly one child control in _contentHost.
    /// </summary>
    private void ApplyGroupsDiff(List<MdBlock> newBlocks, List<MdBlockGroup> newGroups, bool isStreamingAppend)
    {
        var oldGroups = _previousGroups;
        var oldBlocks = _previousBlocks;
        var oldGroupCount = oldGroups.Count;
        var newGroupCount = newGroups.Count;

        // Safety: children count must match old group count
        if (_contentHost.Children.Count != oldGroupCount)
        {
            RebuildChildrenFromGroups(newBlocks, newGroups);
            return;
        }

        // During streaming, groups before the last are likely unchanged
        var diffStart = isStreamingAppend && oldGroupCount > 0 ? oldGroupCount - 1 : 0;
        diffStart = Math.Min(diffStart, newGroupCount);

        // Track cache keys for skipped unchanged groups
        for (int g = 0; g < diffStart; g++)
        {
            var grp = newGroups[g];
            for (int i = 0; i < grp.Count; i++)
                TrackCacheKeysForBlock(newBlocks[grp.StartIndex + i]);
        }

        var minGroupCount = Math.Min(oldGroupCount, newGroupCount);

        for (int g = diffStart; g < minGroupCount; g++)
        {
            var oldGroup = oldGroups[g];
            var newGroup = newGroups[g];

            // Identical groups - skip
            if (GroupsEqual(oldBlocks, oldGroup, newBlocks, newGroup))
            {
                for (int i = 0; i < newGroup.Count; i++)
                    TrackCacheKeysForBlock(newBlocks[newGroup.StartIndex + i]);
                continue;
            }

            // Single-block non-mergeable: use existing in-place update fast paths
            if (newGroup.Count == 1 && oldGroup.Count == 1)
            {
                var oldBlock = oldBlocks[oldGroup.StartIndex];
                var newBlock = newBlocks[newGroup.StartIndex];

                // Code block in-place update
                if (oldBlock.Kind == MdBlockKind.CodeBlock &&
                    newBlock.Kind == MdBlockKind.CodeBlock &&
                    string.Equals(oldBlock.Language, newBlock.Language, StringComparison.Ordinal) &&
                    g < _contentHost.Children.Count &&
                    TryUpdateCodeBlockInPlace(_contentHost.Children[g], newBlock.Content))
                {
                    continue;
                }

                // Text block in-place update (headings, blockquotes, single paragraphs, etc.)
                if (oldBlock.Kind == newBlock.Kind &&
                    oldBlock.Level == newBlock.Level &&
                    IsTextBlockKind(newBlock.Kind) &&
                    g < _contentHost.Children.Count &&
                    TryUpdateTextBlockInPlace(_contentHost.Children[g], newBlock.Content))
                {
                    continue;
                }
            }

            // Merged group in-place update: re-render inlines into existing SelectableTextBlock.
            // Works for any mergeable group regardless of kind mix (heterogeneous groups).
            if (newGroup.Count >= 1 && IsMergeableKind(newGroup.Kind) &&
                g < _contentHost.Children.Count &&
                TryUpdateMergedGroupInPlace(_contentHost.Children[g], newBlocks, newGroup))
            {
                continue;
            }

            // Full replacement
            var replacement = newGroup.Count > 1
                ? CreateMergedTextControl(newBlocks, newGroup)
                : CreateControlForBlock(newBlocks[newGroup.StartIndex]);

            if (_contentHost.Children.Count != oldGroupCount || g >= _contentHost.Children.Count)
            {
                RebuildChildrenFromGroups(newBlocks, newGroups);
                return;
            }

            RemoveLinkRunsForControl(_contentHost.Children[g]);

            if (ReferenceEquals(replacement.Parent, _contentHost))
            {
                RebuildChildrenFromGroups(newBlocks, newGroups);
                return;
            }

            _contentHost.Children[g] = replacement;
        }

        if (_contentHost.Children.Count < minGroupCount)
        {
            RebuildChildrenFromGroups(newBlocks, newGroups);
            return;
        }

        // Append new groups
        for (int g = minGroupCount; g < newGroupCount; g++)
        {
            var grp = newGroups[g];
            var control = grp.Count > 1
                ? CreateMergedTextControl(newBlocks, grp)
                : CreateControlForBlock(newBlocks[grp.StartIndex]);
            if (ReferenceEquals(control.Parent, _contentHost))
            {
                RebuildChildrenFromGroups(newBlocks, newGroups);
                return;
            }
            _contentHost.Children.Add(control);
        }

        // Remove trailing stale groups
        for (int g = _contentHost.Children.Count - 1; g >= newGroupCount; g--)
        {
            RemoveLinkRunsForControl(_contentHost.Children[g]);
            _contentHost.Children.RemoveAt(g);
        }
    }

    private void RebuildChildrenFromGroups(IReadOnlyList<MdBlock> blocks, IReadOnlyList<MdBlockGroup> groups)
    {
        _contentHost.Children.Clear();
        _linkRuns.Clear();
        foreach (var group in groups)
        {
            var control = group.Count > 1
                ? CreateMergedTextControl(blocks, group)
                : CreateControlForBlock(blocks[group.StartIndex]);
            if (control.Parent is Panel parent)
                parent.Children.Remove(control);
            _contentHost.Children.Add(control);
        }
    }

    private void DetachFromForeignParent(Control control)
    {
        if (control.Parent is Panel oldParent && !ReferenceEquals(oldParent, _contentHost))
            oldParent.Children.Remove(control);
    }

    /// <summary>
    /// Tracks cache keys for a block that won't be rebuilt (used for cache eviction).
    /// </summary>
    private void TrackCacheKeysForBlock(MdBlock block)
    {
        switch (block.Kind)
        {
            case MdBlockKind.Chart:
                _chartKeysUsed.Add(block.Content.Trim());
                break;
            case MdBlockKind.Mermaid:
                var trimmed = block.Content.Trim();
                var trimmedSpan = trimmed.AsSpan().TrimStart();
                var nlIdx = trimmedSpan.IndexOf('\n');
                var firstLine = nlIdx >= 0 ? trimmedSpan[..nlIdx].TrimStart() : trimmedSpan;
                if (firstLine.StartsWith("graph", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("sequencediagram", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("statediagram", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("erdiagram", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("classdiagram", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("timeline", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("quadrantchart", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("quadrant-chart", StringComparison.OrdinalIgnoreCase))
                    _diagramKeysUsed.Add("mermaid-diag:" + trimmed);
                else
                    _chartKeysUsed.Add("mermaid:" + trimmed);
                break;
            case MdBlockKind.Confidence:
                _confidenceKeysUsed.Add(block.Content.Trim());
                break;
            case MdBlockKind.Comparison:
                _comparisonKeysUsed.Add(block.Content.Trim());
                break;
            case MdBlockKind.Card:
                _cardKeysUsed.Add(block.Content.Trim());
                break;
            case MdBlockKind.Sources:
                _sourcesKeysUsed.Add(block.Content.Trim());
                break;
            case MdBlockKind.Table:
                _tableKeysUsed.Add(MarkdownParser.GetTableCacheKey(block.Content));
                break;
        }
    }

    /// <summary>Creates a UI control for a parsed block descriptor.</summary>
    private Control CreateControlForBlock(MdBlock block)
    {
        System.Threading.Interlocked.Increment(ref _diagnosticControlCreateCount);

        return block.Kind switch
        {
            MdBlockKind.Paragraph => CreateParagraphControl(block.Content),
            MdBlockKind.Heading => CreateHeadingControl(block.Content, block.Level),
            MdBlockKind.Bullet => CreateBulletControl(block.Content, block.Level),
            MdBlockKind.NumberedItem => CreateNumberedItemControl(block.Content, block.Level),
            MdBlockKind.TaskItem => CreateTaskItemControl(block.Content, block.Level == 1),
            MdBlockKind.CodeBlock => CreateCodeBlockControl(block.Content, block.Language),
            MdBlockKind.HorizontalRule => CreateHorizontalRuleControl(),
            MdBlockKind.Blockquote => CreateBlockquoteControl(block.Content),
            MdBlockKind.Table => CreateTableControl(block.Content),
            MdBlockKind.Chart => CreateChartControl(block.Content),
            MdBlockKind.Mermaid => CreateMermaidControl(block.Content),
            MdBlockKind.Confidence => CreateConfidenceControl(block.Content),
            MdBlockKind.Comparison => CreateComparisonControl(block.Content),
            MdBlockKind.Card => CreateCardControl(block.Content),
            MdBlockKind.Sources => CreateSourcesControl(block.Content),
            _ => new Border(),
        };
    }

    private void EvictStaleCaches()
    {
        EvictFromCache(_chartCache, _chartKeysUsed);
        EvictFromCache(_diagramCache, _diagramKeysUsed);
        EvictFromCache(_confidenceCache, _confidenceKeysUsed);
        EvictFromCache(_comparisonCache, _comparisonKeysUsed);
        EvictFromCache(_cardCache, _cardKeysUsed);
        EvictFromCache(_sourcesCache, _sourcesKeysUsed);
        EvictFromCache(_tableCache, _tableKeysUsed);
    }

    /// <summary>Remove cache entries not in the used set, without LINQ/ToList allocations.</summary>
    private void EvictFromCache<T>(Dictionary<string, T> cache, HashSet<string> used)
    {
        if (cache.Count == 0) return;
        _evictBuffer.Clear();
        foreach (var key in cache.Keys)
            if (!used.Contains(key))
                _evictBuffer.Add(key);
        foreach (var key in _evictBuffer)
            cache.Remove(key);
    }

    private Control CreateParagraphControl(string text)
    {
        var paragraph = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        paragraph.Classes.Add("strata-md-paragraph");
        return paragraph;
    }

    private Control CreateHeadingControl(string text, int level)
    {
        var heading = CreateRichText(
            text,
            level switch
            {
                1 => _bodyFontSize * 1.28,
                2 => _bodyFontSize * 1.12,
                _ => _bodyFontSize * 1.04
            },
            _bodyFontSize * 1.6,
            TextWrapping.Wrap);
        heading.FontWeight = FontWeight.SemiBold;
        heading.Margin = new Thickness(0, level switch { 1 => 14, 2 => 10, _ => 6 }, 0, 2);
        heading.Classes.Add("strata-md-heading");
        return heading;
    }

    private Control CreateBulletControl(string text, int indentLevel = 0)
    {
        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap, "• ");
        textBlock.Classes.Add("strata-md-bullet-text");
        textBlock.Margin = indentLevel == 0 ? ZeroThickness : new Thickness(indentLevel * 16, 0, 0, 0);
        return textBlock;
    }

    private Control CreateBlockquoteControl(string text)
    {
        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        textBlock.Classes.Add("strata-md-paragraph");
        var border = new Border { Child = textBlock };
        border.Classes.Add("strata-md-blockquote");
        return border;
    }

    private Control CreateTaskItemControl(string text, bool isChecked)
    {
        var prefix = isChecked ? "☑ " : "☐ ";
        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap, prefix);
        textBlock.Classes.Add("strata-md-task-text");
        if (isChecked)
        {
            textBlock.Opacity = 0.6;
            textBlock.TextDecorations = TextDecorations.Strikethrough;
        }
        return textBlock;
    }

    private static readonly Thickness RtlTextPadding = new(0, 0, 4, 0);

    private SelectableTextBlock CreateRichText(string text, double fontSize, double lineHeight, TextWrapping wrapping, string? prefix = null)
    {
        var isRtl = FlowDirection == FlowDirection.RightToLeft;
        var textBlock = new SelectableTextBlock
        {
            FontSize = fontSize,
            LineHeight = lineHeight,
            TextWrapping = wrapping,
            TextAlignment = isRtl
                ? TextAlignment.Right
                : TextAlignment.Left,
            ClipToBounds = false,
            Padding = isRtl ? RtlTextPadding : ZeroThickness,
            Margin = ZeroThickness
        };

        var hadLinks = AppendFormattedInlines(textBlock, text, prefix);

        if (hadLinks)
        {
            textBlock.Tapped += OnLinkTapped;
            textBlock.PointerMoved += OnTextBlockPointerMoved;
        }

        return textBlock;
    }

    /// <summary>
    /// Span-based inline parser. Scans text character-by-character to find inline
    /// code, bold, italic, bold-italic, and links without allocating regex Match objects.
    /// Inspired by FastAvaloniaMarkdown's zero-allocation inline parser.
    /// Returns true if any link inlines were added.
    /// </summary>
    private bool AppendFormattedInlines(SelectableTextBlock target, string text, string? prefix = null, bool forceInlines = false)
    {
        if (!string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(text))
        {
            if (forceInlines)
                target.Inlines?.Add(new Run(prefix));
            else
                target.Text = prefix;
            return false;
        }

        if (string.IsNullOrEmpty(text))
            return false;

        var span = text.AsSpan();

        // Fast path: no special characters — set Text directly (no Inlines list)
        // Skip when forceInlines is set (merged groups need Run objects to preserve
        // previously added LineBreak/Run inlines from earlier items in the group).
        if (!forceInlines && span.IndexOfAny('`', '*', '[') < 0 && span.IndexOfAny('~', '_', '!') < 0)
        {
            target.Text = string.IsNullOrEmpty(prefix) ? text : prefix + text;
            return false;
        }

        if (!string.IsNullOrEmpty(prefix))
            target.Inlines?.Add(new Run(prefix));

        int pos = 0;
        int textStart = 0;
        bool hasLinks = false;

        while (pos < span.Length)
        {
            var c = span[pos];

            // Inline code: `code`
            if (c == '`')
            {
                int closePos = span[(pos + 1)..].IndexOf('`');
                if (closePos >= 0)
                {
                    closePos += pos + 1;
                    // Flush preceding plain text
                    if (pos > textStart)
                        target.Inlines?.Add(new Run(text[textStart..pos]));

                    var codeText = text[(pos + 1)..closePos];
                    target.Inlines?.Add(CreateInlineCode(codeText, target.FontSize));
                    pos = closePos + 1;
                    textStart = pos;
                    continue;
                }
            }

            // Bold/Italic with *
            if (c == '*')
            {
                // Count consecutive asterisks
                int starCount = 1;
                while (pos + starCount < span.Length && span[pos + starCount] == '*')
                    starCount++;

                if (starCount >= 3)
                {
                    // Try bold-italic: ***text***
                    int closeIdx = FindClosingDelimiter(span, pos + 3, '*', 3);
                    if (closeIdx >= 0)
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var innerText = text[(pos + 3)..closeIdx];
                        hasLinks |= AppendNestedCodeInlines(target, innerText, FontWeight.Bold, FontStyle.Italic);
                        pos = closeIdx + 3;
                        textStart = pos;
                        continue;
                    }
                }

                if (starCount >= 2)
                {
                    // Try bold: **text**
                    int closeIdx = FindClosingDelimiter(span, pos + 2, '*', 2);
                    if (closeIdx >= 0)
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var innerText = text[(pos + 2)..closeIdx];
                        hasLinks |= AppendNestedCodeInlines(target, innerText, FontWeight.Bold, FontStyle.Normal);
                        pos = closeIdx + 2;
                        textStart = pos;
                        continue;
                    }
                }

                if (starCount >= 1)
                {
                    // Try italic: *text*
                    int closeIdx = FindClosingDelimiter(span, pos + 1, '*', 1);
                    if (closeIdx >= 0)
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var innerText = text[(pos + 1)..closeIdx];
                        hasLinks |= AppendNestedCodeInlines(target, innerText, FontWeight.Normal, FontStyle.Italic);
                        pos = closeIdx + 1;
                        textStart = pos;
                        continue;
                    }
                }
            }

            // Link: [text](url)
            if (c == '[')
            {
                int bracketClose = FindClosingBracket(span, pos + 1);
                if (bracketClose >= 0 && bracketClose + 1 < span.Length && span[bracketClose + 1] == '(')
                {
                    int parenClose = FindClosingParen(span, bracketClose + 2);
                    if (parenClose >= 0)
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var linkLabel = text[(pos + 1)..bracketClose];
                        var linkTarget = text[(bracketClose + 2)..parenClose].Trim();

                        var linkRun = new Run(linkLabel)
                        {
                            Foreground = _linkBrush ??= ResolveLinkBrush(),
                            TextDecorations = TextDecorations.Underline,
                        };
                        _linkRuns[linkRun] = linkTarget;
                        hasLinks = true;
                        target.Inlines?.Add(linkRun);
                        pos = parenClose + 1;
                        textStart = pos;
                        continue;
                    }
                }
            }

            // Image: ![alt](url)
            if (c == '!' && pos + 1 < span.Length && span[pos + 1] == '[')
            {
                int bracketClose = FindClosingBracket(span, pos + 2);
                if (bracketClose >= 0 && bracketClose + 1 < span.Length && span[bracketClose + 1] == '(')
                {
                    int parenClose = FindClosingParen(span, bracketClose + 2);
                    if (parenClose >= 0)
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var altText = text[(pos + 2)..bracketClose];
                        var imageUrl = text[(bracketClose + 2)..parenClose].Trim();
                        target.Inlines?.Add(CreateImageInline(altText, imageUrl, target.FontSize));
                        pos = parenClose + 1;
                        textStart = pos;
                        continue;
                    }
                }
            }

            // Strikethrough: ~~text~~
            if (c == '~' && pos + 1 < span.Length && span[pos + 1] == '~')
            {
                int closeIdx = FindClosingDelimiter(span, pos + 2, '~', 2);
                if (closeIdx >= 0)
                {
                    if (pos > textStart)
                        target.Inlines?.Add(new Run(text[textStart..pos]));

                    var innerText = text[(pos + 2)..closeIdx];
                    target.Inlines?.Add(new Run(innerText)
                    {
                        TextDecorations = TextDecorations.Strikethrough,
                    });
                    pos = closeIdx + 2;
                    textStart = pos;
                    continue;
                }
            }

            // Bold/Italic with _ (only at word boundary — skip mid-word underscores)
            if (c == '_' && (pos == 0 || !char.IsLetterOrDigit(span[pos - 1])))
            {
                int underCount = 1;
                while (pos + underCount < span.Length && span[pos + underCount] == '_')
                    underCount++;

                if (underCount >= 2)
                {
                    int closeIdx = FindClosingDelimiter(span, pos + 2, '_', 2);
                    if (closeIdx >= 0 && (closeIdx + 2 >= span.Length || !char.IsLetterOrDigit(span[closeIdx + 2])))
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var innerText = text[(pos + 2)..closeIdx];
                        hasLinks |= AppendNestedCodeInlines(target, innerText, FontWeight.Bold, FontStyle.Normal);
                        pos = closeIdx + 2;
                        textStart = pos;
                        continue;
                    }
                }

                if (underCount >= 1)
                {
                    int closeIdx = FindClosingDelimiter(span, pos + 1, '_', 1);
                    if (closeIdx >= 0 && (closeIdx + 1 >= span.Length || !char.IsLetterOrDigit(span[closeIdx + 1])))
                    {
                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]));

                        var innerText = text[(pos + 1)..closeIdx];
                        hasLinks |= AppendNestedCodeInlines(target, innerText, FontWeight.Normal, FontStyle.Italic);
                        pos = closeIdx + 1;
                        textStart = pos;
                        continue;
                    }
                }
            }

            pos++;
        }

        // Flush remaining plain text
        if (textStart == 0 && pos == span.Length)
        {
            // No formatting found — set Text directly (avoids creating Inlines)
            // When forceInlines is active, always use a Run to avoid clearing
            // inlines that were already appended for prior items in a merged group.
            if (!forceInlines && (target.Inlines == null || target.Inlines.Count == 0))
                target.Text = text;
            else
                target.Inlines?.Add(new Run(text));
        }
        else if (textStart < span.Length)
        {
            target.Inlines?.Add(new Run(text[textStart..]));
        }

        return hasLinks;
    }

    /// <summary>
    /// Span-based nested inline scanner for bold/italic content. Handles inline code
    /// and links within styled text. Returns true if any link inlines were added.
    /// </summary>
    private bool AppendNestedCodeInlines(SelectableTextBlock target, string text,
        FontWeight weight, FontStyle style)
    {
        var span = text.AsSpan();
        int pos = 0;
        int textStart = 0;
        bool foundSpecial = false;
        bool hasLinks = false;

        while (pos < span.Length)
        {
            if (span[pos] == '`')
            {
                int closePos = span[(pos + 1)..].IndexOf('`');
                if (closePos >= 0)
                {
                    closePos += pos + 1;
                    foundSpecial = true;

                    if (pos > textStart)
                        target.Inlines?.Add(new Run(text[textStart..pos]) { FontWeight = weight, FontStyle = style });

                    var codeText = text[(pos + 1)..closePos];
                    target.Inlines?.Add(CreateInlineCode(codeText, target.FontSize, weight, style));
                    pos = closePos + 1;
                    textStart = pos;
                    continue;
                }
            }

            // Link: [text](url)
            if (span[pos] == '[')
            {
                int bracketClose = FindClosingBracket(span, pos + 1);
                if (bracketClose >= 0 && bracketClose + 1 < span.Length && span[bracketClose + 1] == '(')
                {
                    int parenClose = FindClosingParen(span, bracketClose + 2);
                    if (parenClose >= 0)
                    {
                        foundSpecial = true;
                        hasLinks = true;

                        if (pos > textStart)
                            target.Inlines?.Add(new Run(text[textStart..pos]) { FontWeight = weight, FontStyle = style });

                        var linkLabel = text[(pos + 1)..bracketClose];
                        var linkTarget = text[(bracketClose + 2)..parenClose].Trim();

                        var linkRun = new Run(linkLabel)
                        {
                            FontWeight = weight,
                            FontStyle = style,
                            Foreground = _linkBrush ??= ResolveLinkBrush(),
                            TextDecorations = TextDecorations.Underline,
                        };
                        _linkRuns[linkRun] = linkTarget;
                        target.Inlines?.Add(linkRun);
                        pos = parenClose + 1;
                        textStart = pos;
                        continue;
                    }
                }
            }

            // Strikethrough: ~~text~~
            if (span[pos] == '~' && pos + 1 < span.Length && span[pos + 1] == '~')
            {
                int closeIdx = FindClosingDelimiter(span, pos + 2, '~', 2);
                if (closeIdx >= 0)
                {
                    foundSpecial = true;

                    if (pos > textStart)
                        target.Inlines?.Add(new Run(text[textStart..pos]) { FontWeight = weight, FontStyle = style });

                    var innerText = text[(pos + 2)..closeIdx];
                    target.Inlines?.Add(new Run(innerText)
                    {
                        FontWeight = weight,
                        FontStyle = style,
                        TextDecorations = TextDecorations.Strikethrough,
                    });
                    pos = closeIdx + 2;
                    textStart = pos;
                    continue;
                }
            }

            pos++;
        }

        if (!foundSpecial)
        {
            target.Inlines?.Add(new Run(text) { FontWeight = weight, FontStyle = style });
        }
        else if (textStart < span.Length)
        {
            target.Inlines?.Add(new Run(text[textStart..]) { FontWeight = weight, FontStyle = style });
        }

        return hasLinks;
    }

    /// <summary>
    /// Finds closing delimiter sequence (e.g., **, ***) starting from <paramref name="start"/>.
    /// Returns index of the first char of the closing delimiter, or -1 if not found.
    /// </summary>
    private static int FindClosingDelimiter(ReadOnlySpan<char> span, int start, char delimiter, int count)
    {
        for (int i = start; i <= span.Length - count; i++)
        {
            if (span[i] == delimiter)
            {
                int matchCount = 1;
                while (matchCount < count && i + matchCount < span.Length && span[i + matchCount] == delimiter)
                    matchCount++;

                if (matchCount >= count)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>Finds matching ] for a [ starting from <paramref name="start"/>.</summary>
    private static int FindClosingBracket(ReadOnlySpan<char> span, int start)
    {
        for (int i = start; i < span.Length; i++)
        {
            if (span[i] == ']') return i;
            if (span[i] == '[') return -1; // nested brackets not supported
        }
        return -1;
    }

    /// <summary>Finds matching ) for a ( starting from <paramref name="start"/>.</summary>
    private static int FindClosingParen(ReadOnlySpan<char> span, int start)
    {
        int depth = 1;
        for (int i = start; i < span.Length; i++)
        {
            if (span[i] == '(') depth++;
            else if (span[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static void OpenLink(string linkTarget)
    {
        if (string.IsNullOrWhiteSpace(linkTarget))
            return;

        try
        {
            if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps || absoluteUri.Scheme == Uri.UriSchemeFile))
            {
                Process.Start(new ProcessStartInfo(absoluteUri.ToString()) { UseShellExecute = true });
                return;
            }

            var resolvedPath = ResolveLocalPath(linkTarget);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                Process.Start(new ProcessStartInfo(resolvedPath) { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }

    private static string BuildLinkTooltip(string linkTarget)
    {
        if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var absoluteUri))
            return $"Open {absoluteUri}";

        var resolvedPath = ResolveLocalPath(linkTarget);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            return $"Open file: {resolvedPath}";

        return $"Reference: {linkTarget}";
    }

    private static string? ResolveLocalPath(string linkTarget)
    {
        if (Path.IsPathRooted(linkTarget))
            return File.Exists(linkTarget) ? linkTarget : null;

        var current = Directory.GetCurrentDirectory();
        var candidate = Path.GetFullPath(Path.Combine(current, linkTarget));
        if (File.Exists(candidate))
            return candidate;

        var cursor = new DirectoryInfo(current);
        for (var i = 0; i < 6 && cursor is not null; i++)
        {
            candidate = Path.Combine(cursor.FullName, linkTarget);
            if (File.Exists(candidate))
                return candidate;

            cursor = cursor.Parent;
        }

        return null;
    }


    /// <summary>
    /// Updates a code block control in-place during streaming, avoiding full control
    /// re-creation. Returns false if the control structure doesn't match.
    /// </summary>
    private static bool TryUpdateCodeBlockInPlace(Control existing, string newContent)
    {
        // Navigate: Border (shell) -> StackPanel -> [1] ScrollViewer -> StrataCodeBlock
        if (existing is not Border { Child: StackPanel stack } || stack.Children.Count < 2)
            return false;

        if (stack.Children[1] is not ScrollViewer { Content: StrataCodeBlock codeBlock } scroller)
            return false;

        var normalizedCode = newContent.TrimEnd('\r', '\n');
        var displayCode = string.IsNullOrWhiteSpace(normalizedCode) ? " " : normalizedCode;

        codeBlock.Text = displayCode;
        var lineCount = CountLines(displayCode);
        codeBlock.MinHeight = lineCount * StrataCodeBlock.CodeLineHeight;
        return true;
    }

    /// <summary>Returns true for block kinds whose control contains an updatable SelectableTextBlock.</summary>
    private static bool IsTextBlockKind(MdBlockKind kind) =>
        kind is MdBlockKind.Paragraph or MdBlockKind.Heading or MdBlockKind.Bullet or MdBlockKind.NumberedItem or MdBlockKind.Blockquote or MdBlockKind.TaskItem;

    /// <summary>Returns true for block kinds that can be merged into a single SelectableTextBlock when consecutive.</summary>
    private static bool IsMergeableKind(MdBlockKind kind) =>
        kind is MdBlockKind.Paragraph or MdBlockKind.Heading or MdBlockKind.Bullet or MdBlockKind.NumberedItem;

    /// <summary>
    /// Scans blocks and produces groups of consecutive mergeable blocks.
    /// Consecutive mergeable blocks (Paragraph, Heading, Bullet, NumberedItem)
    /// are grouped heterogeneously into a single group regardless of kind.
    /// Indented bullets (Level &gt; 0) are grouped separately with same-level peers.
    /// Everything else becomes a single-block group.
    /// </summary>
    private static void ComputeGroups(IReadOnlyList<MdBlock> blocks, List<MdBlockGroup> groups)
    {
        groups.Clear();
        if (blocks.Count == 0) return;

        int i = 0;
        while (i < blocks.Count)
        {
            var block = blocks[i];
            if (!IsMergeableKind(block.Kind))
            {
                groups.Add(new MdBlockGroup(i, 1, block.Kind, block.Level));
                i++;
                continue;
            }

            // Indented bullets: group same-level together but don't mix with other kinds
            if (block.Kind == MdBlockKind.Bullet && block.Level > 0)
            {
                int start = i;
                i++;
                while (i < blocks.Count &&
                       blocks[i].Kind == MdBlockKind.Bullet &&
                       blocks[i].Level == block.Level)
                {
                    i++;
                }
                groups.Add(new MdBlockGroup(start, i - start, block.Kind, block.Level));
                continue;
            }

            // Heterogeneous mergeable group: headings, paragraphs, bullets (L0), numbered items
            int groupStart = i;
            i++;
            while (i < blocks.Count && IsMergeableKind(blocks[i].Kind))
            {
                // Indented bullets break the heterogeneous group
                if (blocks[i].Kind == MdBlockKind.Bullet && blocks[i].Level > 0)
                    break;
                i++;
            }
            groups.Add(new MdBlockGroup(groupStart, i - groupStart, blocks[groupStart].Kind, blocks[groupStart].Level));
        }
    }

    /// <summary>
    /// Updates the SelectableTextBlock inside an existing paragraph/heading/bullet/numbered
    /// control in-place. Avoids recreating the Grid/Border/SelectableTextBlock subtree.
    /// Returns false if the control structure doesn't match expectations.
    /// </summary>
    private bool TryUpdateTextBlockInPlace(Control existing, string newContent)
    {
        var tb = FindSelectableTextBlock(existing);
        if (tb is null)
            return false;

        // Remove old link runs for this text block before clearing inlines
        RemoveLinkRunsForTextBlock(tb);

        tb.Text = null;
        tb.Inlines?.Clear();
        AppendFormattedInlines(tb, newContent);

        // Re-wire link handlers if needed
        if (tb.Inlines != null)
        {
            bool hasLinks = false;
            foreach (var inline in tb.Inlines)
            {
                if (inline is Run run && _linkRuns.ContainsKey(run))
                {
                    hasLinks = true;
                    break;
                }
            }

            // Detach/attach handlers as needed
            tb.Tapped -= OnLinkTapped;
            tb.PointerMoved -= OnTextBlockPointerMoved;
            if (hasLinks)
            {
                tb.Tapped += OnLinkTapped;
                tb.PointerMoved += OnTextBlockPointerMoved;
            }
        }

        return true;
    }

    /// <summary>Finds the SelectableTextBlock inside paragraph/heading/bullet/numbered/blockquote controls.</summary>
    private static SelectableTextBlock? FindSelectableTextBlock(Control control)
    {
        // Paragraph/Heading: the control IS a SelectableTextBlock
        if (control is SelectableTextBlock stb)
            return stb;

        // Blockquote: Border with SelectableTextBlock child
        if (control is Border border && border.Child is SelectableTextBlock borderChild)
            return borderChild;

        // Bullet/NumberedItem: Grid with SelectableTextBlock at column 2
        if (control is Grid grid)
        {
            for (int i = 0; i < grid.Children.Count; i++)
            {
                if (grid.Children[i] is SelectableTextBlock child && Grid.GetColumn(child) == 2)
                    return child;
            }
        }

        return null;
    }

    /// <summary>Removes link tracking entries for Run objects belonging to a specific text block.</summary>
    private void RemoveLinkRunsForTextBlock(SelectableTextBlock tb)
    {
        if (_linkRuns.Count == 0 || tb.Inlines == null)
            return;

        foreach (var inline in tb.Inlines)
        {
            if (inline is Run run)
                _linkRuns.Remove(run);
        }
    }

    /// <summary>Cleans up link tracking for all SelectableTextBlocks inside a control being removed.</summary>
    private void RemoveLinkRunsForControl(Control control)
    {
        if (_linkRuns.Count == 0)
            return;

        var tb = FindSelectableTextBlock(control);
        if (tb is not null)
            RemoveLinkRunsForTextBlock(tb);
    }

    /// <summary>Counts newline characters in a string + 1, without allocating a Split array.</summary>
    private static int CountLines(string text)
    {
        int count = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        return count;
    }

    private Control CreateCodeBlockControl(string code, string language)
    {
        var normalizedCode = code.TrimEnd('\r', '\n');
        var displayCode = string.IsNullOrWhiteSpace(normalizedCode) ? " " : normalizedCode;
        var lineCount = CountLines(displayCode);

        var shell = new Border();
        shell.Classes.Add("strata-md-code-block");

        var langText = language?.Trim().ToLowerInvariant() ?? string.Empty;

        // Header row with language label + copy button
        var headerRow = new DockPanel
        {
            Margin = new Thickness(10, 6, 6, 0)
        };

        if (!string.IsNullOrWhiteSpace(langText))
        {
            var label = new TextBlock
            {
                Text = langText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.Classes.Add("strata-md-code-lang");
            headerRow.Children.Add(label);
        }

        var copyBtn = new Button
        {
            Content = new TextBlock
            {
                Text = "Copy",
                FontSize = 11,
                Foreground = Brushes.Gray
            },
            Padding = new Thickness(8, 2),
            MinHeight = 0,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = HandCursor
        };
        copyBtn.Classes.Add("subtle");
        DockPanel.SetDock(copyBtn, Dock.Right);
        headerRow.Children.Insert(0, copyBtn);

        // Syntax-highlighted code block.
        // MinHeight on the code block ensures the text keeps its natural height
        // even when the horizontal scrollbar appears and the ScrollViewer's
        // internal Grid must allocate an extra row for it.
        // Horizontal padding is applied as Margin on the code block (not Padding
        // on the ScrollViewer) so it is included in the scroll extent — otherwise
        // the last ~20 px of long lines would be unreachable.
        var codeBlock = new StrataCodeBlock
        {
            Text = displayCode,
            Language = langText,
            MinHeight = lineCount * StrataCodeBlock.CodeLineHeight,
            Margin = new Thickness(10, 0, 10, 0),
        };
        codeBlock.Classes.Add("strata-md-code-editor");

        var scroller = new ScrollViewer
        {
            Content = codeBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 400,
            Padding = new Thickness(0, 4, 0, 8),
        };
        scroller.Classes.Add("strata-md-code-scroll");

        // Read text from the code block directly (not a closure capture) so in-place
        // streaming updates to codeBlock.Text are reflected in the copy action.
        copyBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(copyBtn);
            if (topLevel?.Clipboard is not null)
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(codeBlock.Text ?? ""));
                await topLevel.Clipboard.SetDataAsync(data);
            }

            if (copyBtn.Content is TextBlock tb)
            {
                tb.Text = "Copied!";
                await Task.Delay(1200);
                tb.Text = "Copy";
            }
        };

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(headerRow);
        stack.Children.Add(scroller);

        shell.Child = stack;
        return shell;
    }

    private Control CreateChartControl(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return GetChartPlaceholder();

        // Reuse cached chart if JSON hasn't changed (preserves animation state during streaming)
        if (_chartCache.TryGetValue(trimmed, out var cached))
        {
            _chartKeysUsed.Add(trimmed);
            DetachFromForeignParent(cached);
            return cached;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var typeStr = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "bar";
            var chartType = typeStr?.ToLowerInvariant() switch
            {
                "line" => StrataChartType.Line,
                "bar" => StrataChartType.Bar,
                "donut" => StrataChartType.Donut,
                "pie" => StrataChartType.Pie,
                _ => StrataChartType.Bar
            };

            var labels = new List<string>();
            if (root.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
                foreach (var l in labelsProp.EnumerateArray())
                    labels.Add(l.GetString() ?? "");

            var seriesList = new List<StrataChartSeries>();
            if (root.TryGetProperty("series", out var seriesProp) && seriesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in seriesProp.EnumerateArray())
                {
                    var name = s.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    var values = new List<double>();
                    if (s.TryGetProperty("values", out var vp) && vp.ValueKind == JsonValueKind.Array)
                        foreach (var v in vp.EnumerateArray())
                            values.Add(v.TryGetDouble(out var d) ? d : 0);
                    seriesList.Add(new StrataChartSeries { Name = name, Values = values });
                }
            }

            var showLegend = !root.TryGetProperty("showLegend", out var slp) || slp.ValueKind != JsonValueKind.False;
            var showGrid = !root.TryGetProperty("showGrid", out var sgp) || sgp.ValueKind != JsonValueKind.False;
            var height = root.TryGetProperty("height", out var hp) && hp.TryGetDouble(out var hv) ? hv : 220;

            var chart = new StrataChart
            {
                ChartType = chartType,
                Labels = labels,
                Series = seriesList,
                ShowLegend = showLegend,
                ShowGrid = showGrid,
                ChartHeight = Math.Clamp(height, 120, 500),
            };

            if (root.TryGetProperty("donutCenterValue", out var dcv))
                chart.DonutCenterValue = dcv.GetString();
            if (root.TryGetProperty("donutCenterLabel", out var dcl))
                chart.DonutCenterLabel = dcl.GetString();

            _chartCache[trimmed] = chart;
            _chartKeysUsed.Add(trimmed);
            return chart;
        }
        catch
        {
            // Incomplete/malformed JSON during streaming — show placeholder
            return GetChartPlaceholder();
        }
    }

    private Control CreateMermaidControl(string mermaidText)
    {
        var trimmed = mermaidText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return GetChartPlaceholder();

        var trimmedSpan = trimmed.AsSpan();
        var nlIndex = trimmedSpan.IndexOf('\n');
        var firstLine = (nlIndex >= 0 ? trimmedSpan[..nlIndex] : trimmedSpan).TrimStart();

        // Diagram types → StrataMermaid
        if (firstLine.StartsWith("graph", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("sequencediagram", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("statediagram", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("erdiagram", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("classdiagram", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("timeline", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("quadrantchart", StringComparison.OrdinalIgnoreCase) ||
            firstLine.StartsWith("quadrant-chart", StringComparison.OrdinalIgnoreCase))
        {
            return CreateMermaidDiagramControl(trimmed);
        }

        // Chart types → StrataChart
        var cacheKey = "mermaid:" + trimmed;
        if (_chartCache.TryGetValue(cacheKey, out var cached))
        {
            _chartKeysUsed.Add(cacheKey);
            DetachFromForeignParent(cached);
            return cached;
        }

        try
        {
            StrataChart? chart = null;

            if (trimmed.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
                chart = ParseMermaidPie(trimmed);
            else if (trimmed.StartsWith("xychart-beta", StringComparison.OrdinalIgnoreCase)
                  || trimmed.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
                chart = ParseMermaidXyChart(trimmed);

            if (chart is not null)
            {
                _chartCache[cacheKey] = chart;
                _chartKeysUsed.Add(cacheKey);
                return chart;
            }
            else
            {
                return CreateCodeBlockControl(trimmed, "mermaid");
            }
        }
        catch
        {
            return GetChartPlaceholder();
        }
    }

    private Control CreateMermaidDiagramControl(string mermaidText)
    {
        var cacheKey = "mermaid-diag:" + mermaidText;
        if (_diagramCache.TryGetValue(cacheKey, out var cached))
        {
            _diagramKeysUsed.Add(cacheKey);
            DetachFromForeignParent(cached);
            return cached;
        }

        var diagram = new StrataMermaid
        {
            Source = mermaidText,
            MinHeight = 180,
        };
        _diagramCache[cacheKey] = diagram;
        _diagramKeysUsed.Add(cacheKey);
        return diagram;
    }

    private static readonly Regex MermaidPieEntryRegex = new(
        @"^\s*""(?<label>[^""]+)""\s*:\s*(?<value>[\d.]+)",
        RegexOptions.Compiled);

    private StrataChart? ParseMermaidPie(string text)
    {
        var lines = text.Split('\n');
        var labels = new List<string>();
        var values = new List<double>();
        string? title = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                title = MermaidTextHelper.NormalizeLabelText(line[6..]);
                continue;
            }

            var match = MermaidPieEntryRegex.Match(line);
            if (match.Success)
            {
                labels.Add(MermaidTextHelper.NormalizeLabelText(match.Groups["label"].Value));
                if (double.TryParse(match.Groups["value"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var val))
                    values.Add(val);
                else
                    values.Add(0);
            }
        }

        if (labels.Count == 0 || values.Count == 0)
            return null;

        var total = values.Sum();
        var chart = new StrataChart
        {
            ChartType = StrataChartType.Pie,
            Labels = labels,
            Series = new List<StrataChartSeries>
            {
                new() { Name = title ?? "", Values = values }
            },
            ShowLegend = true,
            ShowGrid = false,
            ChartHeight = 220,
        };

        return chart;
    }

    private static readonly Regex MermaidBracketListRegex = new(
        @"\[(?<items>[^\]]*)\]",
        RegexOptions.Compiled);

    private static readonly Regex MermaidQuotedListRegex = new(
        @"""(?<item>[^""]*)""|(?<bare>[\w.]+)",
        RegexOptions.Compiled);

    private StrataChart? ParseMermaidXyChart(string text)
    {
        var lines = text.Split('\n');
        string? title = null;
        var xLabels = new List<string>();
        var barSeriesValues = new List<List<double>>();
        var lineSeriesValues = new List<List<double>>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
            {
                var rest = MermaidTextHelper.NormalizeLabelText(line[5..]);
                if (!string.IsNullOrWhiteSpace(rest))
                    title = rest;
                continue;
            }

            if (line.StartsWith("x-axis", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[6..].Trim();
                xLabels = ParseMermaidBracketValues(rest);
                continue;
            }

            if (line.StartsWith("y-axis", StringComparison.OrdinalIgnoreCase))
                continue; // Informational only — StrataChart auto-scales

            if (line.StartsWith("bar", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[3..].Trim();
                var nums = ParseMermaidNumericList(rest);
                if (nums.Count > 0)
                    barSeriesValues.Add(nums);
                continue;
            }

            if (line.StartsWith("line", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[4..].Trim();
                var nums = ParseMermaidNumericList(rest);
                if (nums.Count > 0)
                    lineSeriesValues.Add(nums);
                continue;
            }
        }

        var hasBar = barSeriesValues.Count > 0;
        var hasLine = lineSeriesValues.Count > 0;
        if (!hasBar && !hasLine) return null;

        // If no explicit x-axis labels, generate numeric indices
        var maxCount = Math.Max(
            barSeriesValues.DefaultIfEmpty(new()).Max(s => s.Count),
            lineSeriesValues.DefaultIfEmpty(new()).Max(s => s.Count));
        if (xLabels.Count == 0)
            for (int i = 1; i <= maxCount; i++)
                xLabels.Add(i.ToString());

        // Determine chart type: prefer Bar when bars exist, Line when only lines
        var chartType = hasBar ? StrataChartType.Bar : StrataChartType.Line;

        var seriesList = new List<StrataChartSeries>();
        for (int i = 0; i < barSeriesValues.Count; i++)
            seriesList.Add(new StrataChartSeries
            {
                Name = barSeriesValues.Count > 1 ? $"Bar {i + 1}" : title ?? "Bar",
                Values = barSeriesValues[i]
            });
        for (int i = 0; i < lineSeriesValues.Count; i++)
            seriesList.Add(new StrataChartSeries
            {
                Name = lineSeriesValues.Count > 1 ? $"Line {i + 1}" : title ?? "Line",
                Values = lineSeriesValues[i]
            });

        // When mixed bar+line, use Line type so both are visible as overlapping series
        if (hasBar && hasLine)
            chartType = StrataChartType.Line;

        return new StrataChart
        {
            ChartType = chartType,
            Labels = xLabels,
            Series = seriesList,
            ShowLegend = seriesList.Count > 1,
            ShowGrid = true,
            ChartHeight = 220,
        };
    }

    private static List<string> ParseMermaidBracketValues(string text)
    {
        var results = new List<string>();
        var bracketMatch = MermaidBracketListRegex.Match(text);
        if (bracketMatch.Success)
        {
            var inner = bracketMatch.Groups["items"].Value;
            foreach (var part in inner.Split(','))
            {
                var val = part.Trim().Trim('"');
                val = MermaidTextHelper.NormalizeLabelText(val);
                if (!string.IsNullOrWhiteSpace(val))
                    results.Add(val);
            }
        }
        else
        {
            // Fallback: try to parse label after possible quoted title text
            var quoteIdx = text.IndexOf('"');
            if (quoteIdx >= 0)
            {
                // e.g.: "Month" Jan, Feb, Mar
                // Find closing quote, then parse remaining comma-separated values
                var closeQuote = text.IndexOf('"', quoteIdx + 1);
                if (closeQuote >= 0)
                {
                    var rest = text[(closeQuote + 1)..].Trim();
                    foreach (var part in rest.Split(','))
                    {
                        var val = part.Trim().Trim('"');
                        val = MermaidTextHelper.NormalizeLabelText(val);
                        if (!string.IsNullOrWhiteSpace(val))
                            results.Add(val);
                    }
                }
            }
        }
        return results;
    }

    private static List<double> ParseMermaidNumericList(string text)
    {
        var results = new List<double>();
        var bracketMatch = MermaidBracketListRegex.Match(text);
        if (bracketMatch.Success)
        {
            var inner = bracketMatch.Groups["items"].Value;
            foreach (var part in inner.Split(','))
            {
                var trimmed = part.Trim();
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                    results.Add(val);
            }
        }
        return results;
    }

    private Control GetChartPlaceholder()
    {
        // Reuse the cached placeholder so dot animations survive rebuilds
        if (_chartPlaceholder is null)
        {
            _chartPlaceholder = new Border
            {
                Height = 100,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _chartPlaceholder.Classes.Add("strata-md-code-block");

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
            };

            var icon = new TextBlock
            {
                Text = "\U0001F4CA",
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.35,
            };

            var dotsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
            };

            for (int i = 0; i < 3; i++)
            {
                var dot = new Border
                {
                    Width = 5,
                    Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Opacity = 0.3,
                };
                dot.Classes.Add("strata-md-bullet-dot");
                dotsRow.Children.Add(dot);
            }

            stack.Children.Add(icon);
            stack.Children.Add(dotsRow);
            _chartPlaceholder.Child = stack;

            // Start dot animations once the placeholder is in the visual tree
            _chartPlaceholder.AttachedToVisualTree += (_, _) =>
            {
                var dots = dotsRow.Children.OfType<Border>().ToList();
                for (int i = 0; i < dots.Count; i++)
                    StartDotPulse(dots[i], i * 200);
            };
        }

        // Remove from previous parent if attached to another host
        DetachFromForeignParent(_chartPlaceholder);

        return _chartPlaceholder;
    }

    private static void StartDotPulse(Border dot, int delayMs)
    {
        // Guard against double-starting
        if (dot.Tag is System.Threading.CancellationTokenSource existingCts)
        {
            if (!existingCts.IsCancellationRequested) return;
        }

        var cts = new System.Threading.CancellationTokenSource();
        dot.Tag = cts;

        async void Animate()
        {
            try
            {
                if (delayMs > 0)
                    await Task.Delay(delayMs, cts.Token);

                while (!cts.Token.IsCancellationRequested)
                {
                    dot.Opacity = 0.7;
                    await Task.Delay(400, cts.Token);
                    dot.Opacity = 0.2;
                    await Task.Delay(400, cts.Token);
                }
            }
            catch (TaskCanceledException) { }
        }

        Animate();
    }

    private Control GetBlockPlaceholder(string emoji)
    {
        if (_blockPlaceholder is null)
        {
            _blockPlaceholder = new Border
            {
                Height = 60,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _blockPlaceholder.Classes.Add("strata-md-code-block");

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Orientation = Orientation.Horizontal,
            };

            var icon = new TextBlock
            {
                Text = emoji,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.35,
            };

            var dotsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
            };

            for (int i = 0; i < 3; i++)
            {
                var dot = new Border
                {
                    Width = 5,
                    Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Opacity = 0.3,
                };
                dot.Classes.Add("strata-md-bullet-dot");
                dotsRow.Children.Add(dot);
            }

            stack.Children.Add(icon);
            stack.Children.Add(dotsRow);
            _blockPlaceholder.Child = stack;

            _blockPlaceholder.AttachedToVisualTree += (_, _) =>
            {
                var dots = dotsRow.Children.OfType<Border>().ToList();
                for (int i = 0; i < dots.Count; i++)
                    StartDotPulse(dots[i], i * 200);
            };
        }

        DetachFromForeignParent(_blockPlaceholder);

        return _blockPlaceholder;
    }

    private Control CreateConfidenceControl(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return GetBlockPlaceholder("\U0001F3AF");

        if (_confidenceCache.TryGetValue(trimmed, out var cached))
        {
            _confidenceKeysUsed.Add(trimmed);
            DetachFromForeignParent(cached);
            return cached;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var label = root.TryGetProperty("label", out var lp) ? lp.GetString() ?? "Confidence" : "Confidence";
            var value = root.TryGetProperty("value", out var vp) && vp.TryGetDouble(out var v) ? v : 72;
            var explanation = root.TryGetProperty("explanation", out var ep) ? ep.GetString() : null;

            var ctrl = new StrataConfidence
            {
                Label = label,
                Confidence = Math.Clamp(value, 0, 100),
                Margin = new Thickness(0, 4, 0, 4),
            };

            if (!string.IsNullOrWhiteSpace(explanation))
                ctrl.Explanation = new StrataMarkdown { Markdown = explanation, IsInline = true };

            _confidenceCache[trimmed] = ctrl;
            _confidenceKeysUsed.Add(trimmed);
            return ctrl;
        }
        catch
        {
            return GetBlockPlaceholder("\U0001F3AF");
        }
    }

    private Control CreateComparisonControl(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return GetBlockPlaceholder("\u2696\uFE0F");

        if (_comparisonCache.TryGetValue(trimmed, out var cached))
        {
            _comparisonKeysUsed.Add(trimmed);
            DetachFromForeignParent(cached);
            return cached;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var titleA = "Option A";
            var contentA = "";
            var titleB = "Option B";
            var contentB = "";

            if (root.TryGetProperty("optionA", out var aProp))
            {
                if (aProp.TryGetProperty("title", out var at)) titleA = at.GetString() ?? titleA;
                if (aProp.TryGetProperty("content", out var ac)) contentA = ac.GetString() ?? "";
            }

            if (root.TryGetProperty("optionB", out var bProp))
            {
                if (bProp.TryGetProperty("title", out var bt)) titleB = bt.GetString() ?? titleB;
                if (bProp.TryGetProperty("content", out var bc)) contentB = bc.GetString() ?? "";
            }

            var ctrl = new StrataFork
            {
                OptionATitle = titleA,
                OptionBTitle = titleB,
                OptionAContent = new StrataMarkdown { Markdown = contentA, IsInline = true },
                OptionBContent = new StrataMarkdown { Markdown = contentB, IsInline = true },
                Margin = new Thickness(0, 4, 0, 4),
            };

            _comparisonCache[trimmed] = ctrl;
            _comparisonKeysUsed.Add(trimmed);
            return ctrl;
        }
        catch
        {
            return GetBlockPlaceholder("\u2696\uFE0F");
        }
    }

    private Control CreateCardControl(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return GetBlockPlaceholder("\U0001F4CB");

        if (_cardCache.TryGetValue(trimmed, out var cached))
        {
            _cardKeysUsed.Add(trimmed);
            DetachFromForeignParent(cached);
            return cached;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var header = root.TryGetProperty("header", out var hp) ? hp.GetString() ?? "" : "";
            var summary = root.TryGetProperty("summary", out var sp) ? sp.GetString() ?? "" : "";
            var detail = root.TryGetProperty("detail", out var dp) ? dp.GetString() ?? "" : "";

            var ctrl = new StrataCard
            {
                Margin = new Thickness(0, 4, 0, 4),
            };

            if (!string.IsNullOrWhiteSpace(header))
            {
                ctrl.Header = new TextBlock
                {
                    Text = header,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = _bodyFontSize * 1.08,
                    TextWrapping = TextWrapping.Wrap,
                };
            }

            if (!string.IsNullOrWhiteSpace(summary))
                ctrl.Summary = new StrataMarkdown { Markdown = summary, IsInline = true };

            if (!string.IsNullOrWhiteSpace(detail))
                ctrl.Detail = new StrataMarkdown { Markdown = detail, IsInline = true };

            _cardCache[trimmed] = ctrl;
            _cardKeysUsed.Add(trimmed);
            return ctrl;
        }
        catch
        {
            return GetBlockPlaceholder("\U0001F4CB");
        }
    }

    private Control CreateSourcesControl(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return GetBlockPlaceholder("\U0001F4CE");

        if (_sourcesCache.TryGetValue(trimmed, out var cached))
        {
            _sourcesKeysUsed.Add(trimmed);
            DetachFromForeignParent(cached);
            return cached;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sources", out var sourcesProp) || sourcesProp.ValueKind != JsonValueKind.Array)
                return GetBlockPlaceholder("\U0001F4CE");

            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 4),
            };

            var index = 1;
            foreach (var src in sourcesProp.EnumerateArray())
            {
                var title = src.TryGetProperty("title", out var tp) ? tp.GetString() ?? "Source" : "Source";
                var snippet = src.TryGetProperty("snippet", out var snp) ? snp.GetString() : null;
                var origin = src.TryGetProperty("origin", out var op) ? op.GetString() : null;
                var relevance = src.TryGetProperty("relevance", out var rp) && rp.TryGetDouble(out var rv) ? rv : 0;

                var trace = new StrataTrace
                {
                    Index = index++,
                    Title = title,
                    Snippet = snippet,
                    Origin = origin,
                    Relevance = relevance,
                    Margin = new Thickness(0, 0, 4, 4),
                };

                panel.Children.Add(trace);
            }

            _sourcesCache[trimmed] = panel;
            _sourcesKeysUsed.Add(trimmed);
            return panel;
        }
        catch
        {
            return GetBlockPlaceholder("\U0001F4CE");
        }
    }

    private string? GetLinkAtCharIndex(SelectableTextBlock tb, int charIndex)
    {
        if (tb.Inlines == null) return null;

        var pos = 0;
        foreach (var inline in tb.Inlines)
        {
            if (inline is Run run)
            {
                var len = run.Text?.Length ?? 0;
                if (charIndex >= pos && charIndex < pos + len && _linkRuns.TryGetValue(run, out var url))
                    return url;
                pos += len;
            }
        }
        return null;
    }

    private void OnLinkTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not SelectableTextBlock tb) return;
        var url = GetLinkAtCharIndex(tb, tb.SelectionStart);
        if (url != null) OpenLink(url);
    }

    private void OnTextBlockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not SelectableTextBlock tb) return;

        var point = e.GetPosition(tb);
        var hit = tb.TextLayout.HitTestPoint(point);
        var isLink = hit.IsInside && GetLinkAtCharIndex(tb, hit.CharacterHit.FirstCharacterIndex) != null;
        tb.Cursor = isLink ? HandCursor : Cursor.Default;
    }

    private static IBrush ResolveLinkBrush()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Brush.AccentDefault", Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }

        return Brushes.DodgerBlue;
    }

    private Inline CreateInlineCode(string codeText, double fontSize,
        FontWeight weight = default, FontStyle style = default)
    {
        // Use a Run so that the inline code text participates directly in the
        // text layout's baseline calculation, guaranteeing alignment with
        // surrounding text.  InlineUIContainer+Border cannot achieve this
        // because the layout engine treats the Border as an opaque box and
        // has no access to the inner TextBlock's baseline.
        return new Run($"\u2005{codeText}\u2005")
        {
            FontSize = fontSize,
            FontWeight = weight == default ? FontWeight.Normal : weight,
            FontStyle = style,
            Background = _inlineCodeBrush ??= ResolveInlineCodeBrush(),
        };
    }

    private static Inline CreateImageInline(string altText, string imageUrl, double fontSize)
    {
        // Inline images are rendered as styled alt-text placeholders with a 🖼 prefix.
        // Full image loading would require async I/O which is impractical inside the
        // inline text layout pipeline; the alt text gives the user meaningful context.
        return new Run($"\U0001F5BC {altText}")
        {
            FontSize = fontSize,
            FontStyle = FontStyle.Italic,
            TextDecorations = TextDecorations.Underline,
        };
    }

    private static IBrush ResolveInlineCodeBrush()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Brush.Surface2", Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromArgb(25, 128, 128, 128));
    }

    private Control CreateNumberedItemControl(string text, int number)
    {
        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap, $"{number}. ");
        textBlock.Classes.Add("strata-md-bullet-text");
        return textBlock;
    }

    /// <summary>
    /// Creates a single SelectableTextBlock for a group of consecutive mergeable
    /// text blocks (headings, paragraphs, bullets, numbered items), using LineBreak
    /// inlines to separate items and per-Run FontSize/FontWeight for headings.
    /// For single-block groups, delegates to the existing per-block methods.
    /// </summary>
    private Control CreateMergedTextControl(IReadOnlyList<MdBlock> blocks, MdBlockGroup group)
    {
        if (group.Count == 1)
            return CreateControlForBlock(blocks[group.StartIndex]);

        var isRtl = FlowDirection == FlowDirection.RightToLeft;
        var tb = new SelectableTextBlock
        {
            FontSize = _bodyFontSize,
            LineHeight = _bodyFontSize * 1.52,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = isRtl ? TextAlignment.Right : TextAlignment.Left,
            ClipToBounds = false,
            Padding = isRtl ? RtlTextPadding : ZeroThickness,
            Margin = ZeroThickness,
        };
        tb.Classes.Add("strata-md-paragraph");

        var hasLinks = PopulateMergedInlines(tb, blocks, group);
        if (hasLinks)
        {
            tb.Tapped += OnLinkTapped;
            tb.PointerMoved += OnTextBlockPointerMoved;
        }

        return tb;
    }

    /// <summary>
    /// Updates a merged-group SelectableTextBlock in-place by clearing and re-appending
    /// all inlines. Returns false if the control isn't a compatible SelectableTextBlock.
    /// </summary>
    private bool TryUpdateMergedGroupInPlace(Control existing, IReadOnlyList<MdBlock> blocks, MdBlockGroup group)
    {
        var tb = FindSelectableTextBlock(existing);
        if (tb is null)
            return false;

        RemoveLinkRunsForTextBlock(tb);
        tb.Text = null;
        tb.Inlines?.Clear();

        var hasLinks = PopulateMergedInlines(tb, blocks, group);

        tb.Tapped -= OnLinkTapped;
        tb.PointerMoved -= OnTextBlockPointerMoved;
        if (hasLinks)
        {
            tb.Tapped += OnLinkTapped;
            tb.PointerMoved += OnTextBlockPointerMoved;
        }

        return true;
    }

    /// <summary>
    /// Appends inline content for every block in a merged group into a single
    /// SelectableTextBlock. Headings get per-Run FontSize/FontWeight post-processing.
    /// Returns true if any link inlines were added.
    /// </summary>
    private bool PopulateMergedInlines(SelectableTextBlock tb, IReadOnlyList<MdBlock> blocks, MdBlockGroup group)
    {
        var fontSize = _bodyFontSize;
        bool hasLinks = false;

        for (int i = 0; i < group.Count; i++)
        {
            var block = blocks[group.StartIndex + i];

            // Add spacing between items
            if (i > 0)
            {
                tb.Inlines?.Add(new LineBreak());
                // Extra spacing before headings only (section break).
                // After a heading the content belongs to it — keep tight.
                if (block.Kind == MdBlockKind.Heading)
                    tb.Inlines?.Add(new LineBreak());
            }

            if (block.Kind == MdBlockKind.Heading)
            {
                double headingFontSize = block.Level switch
                {
                    1 => fontSize * 1.28,
                    2 => fontSize * 1.12,
                    _ => fontSize * 1.04,
                };

                int inlinesBefore = tb.Inlines?.Count ?? 0;
                hasLinks |= AppendFormattedInlines(tb, block.Content, null, forceInlines: true);

                // Post-process: apply heading font size and weight to newly added inlines
                if (tb.Inlines != null)
                {
                    for (int j = inlinesBefore; j < tb.Inlines.Count; j++)
                    {
                        if (tb.Inlines[j] is Run run)
                        {
                            run.FontSize = headingFontSize;
                            if (run.FontWeight == FontWeight.Normal || run.FontWeight == default)
                                run.FontWeight = FontWeight.SemiBold;
                        }
                    }
                }
            }
            else
            {
                string? prefix = block.Kind switch
                {
                    MdBlockKind.Bullet => "• ",
                    MdBlockKind.NumberedItem => $"{block.Level}. ",
                    _ => null,
                };

                hasLinks |= AppendFormattedInlines(tb, block.Content, prefix, forceInlines: true);
            }
        }

        return hasLinks;
    }

    private static Control CreateHorizontalRuleControl()
    {
        var rule = new Border
        {
            Height = 1,
            Margin = HrMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        rule.Classes.Add("strata-md-hr");
        return rule;
    }

    private Control CreateTableControl(string tableContent)
    {
        var tableLines = tableContent.Split('\n');
        int lineCount = tableLines.Length;

        // Drop trailing incomplete lines (streaming: last line may be a partial row
        // with only an opening pipe and no closing pipe).
        while (lineCount > 1)
        {
            var last = tableLines[lineCount - 1].AsSpan().Trim();
            if (last.Length > 0 && MarkdownParser.CountChar(last, '|') >= 2)
                break;
            lineCount--;
        }

        if (lineCount == 0)
            return new Border();

        var headerLine = tableLines[0];
        var headerCells = MarkdownParser.SplitTableCells(headerLine);
        var headers = MarkdownParser.TrimCells(headerCells);
        if (headers.Length == 0)
            return new Border();

        // Determine where data rows start.
        // With a valid separator on line 2 this is the normal path;
        // without one (still streaming) we treat remaining lines as data.
        var dataStartIndex = 1;
        if (lineCount >= 2 && MarkdownParser.IsTableSeparator(tableLines[1]))
            dataStartIndex = 2;

        var rows = new List<string[]>();
        for (var i = dataStartIndex; i < lineCount; i++)
        {
            if (MarkdownParser.IsTableSeparator(tableLines[i]))
                continue; // skip separators that appear mid-stream
            var cells = MarkdownParser.TrimCells(MarkdownParser.SplitTableCells(tableLines[i]));
            var padded = new string[headers.Length];
            for (var j = 0; j < headers.Length; j++)
                padded[j] = j < cells.Length ? cells[j] : string.Empty;
            rows.Add(padded);
        }

        var cacheKey = headerLine.Trim();
        _tableKeysUsed.Add(cacheKey);

        // Reuse cached DataGrid when headers match — during streaming only rows change.
        if (_tableCache.TryGetValue(cacheKey, out var cached))
        {
            DetachFromForeignParent(cached);
            cached.Update(this, headers, rows);
            TrackTableDiagnostics(headers.Length, rows.Count, reused: true);
            return cached;
        }

        return CreateTableView(headers, rows, cacheKey);
    }

    private Control CreateTableView(string[] headers, List<string[]> rows, string cacheKey)
    {
        var tableView = new MarkdownTableView();
        tableView.Update(this, headers, rows);
        _tableCache[cacheKey] = tableView;
        TrackTableDiagnostics(headers.Length, rows.Count, reused: false);
        return tableView;
    }

    private static void TrackTableDiagnostics(int columnCount, int rowCount, bool reused)
    {
        System.Threading.Interlocked.Increment(ref _diagnosticTableRenderCount);
        if (reused)
            System.Threading.Interlocked.Increment(ref _diagnosticTableReuseCount);

        System.Threading.Interlocked.Add(ref _diagnosticTableRowCount, rowCount);
        System.Threading.Interlocked.Add(ref _diagnosticTableCellCount, Math.Max(0, (rowCount + 1) * columnCount));
    }

    private sealed class MarkdownTableView : Border
    {
        private readonly Grid _grid;
        private readonly ScrollViewer _scrollViewer;

        public MarkdownTableView()
        {
            _grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            _scrollViewer = new ScrollViewer
            {
                Content = _grid,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 400,
            };
            _scrollViewer.Classes.Add("strata-md-table-scroll");

            Child = _scrollViewer;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            MaxHeight = 400;
            Margin = new Thickness(0, 4, 0, 4);
            Classes.Add("strata-md-table");
        }

        public void Update(StrataMarkdown owner, string[] headers, List<string[]> rows)
        {
            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _grid.ColumnDefinitions.Clear();

            if (headers.Length == 0)
                return;

            for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                _grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (var headerIndex = 0; headerIndex < headers.Length; headerIndex++)
            {
                var headerCell = CreateCell(
                    owner,
                    headers[headerIndex],
                    isHeader: true,
                    TextWrapping.NoWrap,
                    borderThickness: GetCellBorderThickness(headers.Length, rows.Count, headerIndex, rowIndex: -1),
                    cornerRadius: GetCellCornerRadius(headers.Length, rows.Count, headerIndex, rowIndex: -1));
                Grid.SetRow(headerCell, 0);
                Grid.SetColumn(headerCell, headerIndex);
                _grid.Children.Add(headerCell);
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var row = rows[rowIndex];

                for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                {
                    var cellText = columnIndex < row.Length ? row[columnIndex] : string.Empty;
                    var cell = CreateCell(
                        owner,
                        cellText,
                        isHeader: false,
                        TextWrapping.Wrap,
                        borderThickness: GetCellBorderThickness(headers.Length, rows.Count, columnIndex, rowIndex),
                        cornerRadius: GetCellCornerRadius(headers.Length, rows.Count, columnIndex, rowIndex));
                    Grid.SetRow(cell, rowIndex + 1);
                    Grid.SetColumn(cell, columnIndex);
                    _grid.Children.Add(cell);
                }
            }
        }

        private static Border CreateCell(
            StrataMarkdown owner,
            string text,
            bool isHeader,
            TextWrapping wrapping,
            Thickness borderThickness,
            CornerRadius cornerRadius)
        {
            var content = owner.CreateRichText(text, owner._bodyFontSize, owner._bodyFontSize * 1.52, wrapping);
            if (isHeader)
                content.FontWeight = FontWeight.SemiBold;

            var border = new Border
            {
                Child = content,
                BorderThickness = borderThickness,
                CornerRadius = cornerRadius,
                ClipToBounds = cornerRadius != default,
            };
            border.Classes.Add(isHeader ? "strata-md-table-header-cell" : "strata-md-table-cell");
            return border;
        }

        private static Thickness GetCellBorderThickness(int columnCount, int rowCount, int columnIndex, int rowIndex)
        {
            var isLastColumn = columnIndex >= columnCount - 1;
            var isHeader = rowIndex < 0;
            var isLastRow = !isHeader && rowIndex >= rowCount - 1;

            return new Thickness(0, 0, isLastColumn ? 0 : 1, isLastRow ? 0 : 1);
        }

        private static CornerRadius GetCellCornerRadius(int columnCount, int rowCount, int columnIndex, int rowIndex)
        {
            var isFirstColumn = columnIndex == 0;
            var isLastColumn = columnIndex == columnCount - 1;
            var isHeader = rowIndex < 0;
            var isLastDataRow = rowIndex == rowCount - 1;
            var noDataRows = rowCount == 0;

            var topLeft = isHeader && isFirstColumn ? 7d : 0d;
            var topRight = isHeader && isLastColumn ? 7d : 0d;
            var bottomLeft = (noDataRows && isHeader && isFirstColumn) || (!isHeader && isLastDataRow && isFirstColumn) ? 7d : 0d;
            var bottomRight = (noDataRows && isHeader && isLastColumn) || (!isHeader && isLastDataRow && isLastColumn) ? 7d : 0d;
            return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
        }
    }
}
