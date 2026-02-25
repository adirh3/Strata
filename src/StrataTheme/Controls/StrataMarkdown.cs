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
using AvaloniaEdit;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

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
    // ── Block model for incremental diffing ──
    internal enum MdBlockKind
    {
        Paragraph,
        Heading,
        Bullet,
        NumberedItem,
        CodeBlock,
        HorizontalRule,
        Table,
        Chart,
        Mermaid,
        Confidence,
        Comparison,
        Card,
        Sources,
    }

    internal readonly struct MdBlock : IEquatable<MdBlock>
    {
        public MdBlockKind Kind { get; init; }
        public string Content { get; init; }
        public int Level { get; init; }       // heading level, indent, item number
        public string Language { get; init; }  // code block language
        public int SourceStart { get; init; }

        public bool Equals(MdBlock other) =>
            Kind == other.Kind && Level == other.Level &&
            string.Equals(Content, other.Content, StringComparison.Ordinal) &&
            string.Equals(Language, other.Language, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is MdBlock b && Equals(b);
        public override int GetHashCode() => HashCode.Combine(Kind, Content, Level, Language);
    }

    private static readonly Regex LinkRegex = new(@"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.Compiled);

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

    private Border? _blockPlaceholder;

    // ── Incremental state ──
    private List<MdBlock> _previousBlocks = new();
    private string? _previousMarkdownNormalized;
    private int _previousMarkdownLength;

    // ── Rebuild debouncing for rapid streaming ──
    private bool _rebuildQueued;
    private System.Threading.CancellationTokenSource? _rebuildDelayCts;
    private DateTime _lastRebuildAtUtc;

    // ── Shared TextMate registries (expensive to create) ──
    private static RegistryOptions? _darkRegistry;
    private static RegistryOptions? _lightRegistry;

    // ── Reusable buffer to avoid per-rebuild allocations ──
    private readonly List<string> _evictBuffer = new();

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
        _rebuildDelayCts = cts;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
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
        _lastThemeVariant = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light).ToString();
        Rebuild();
        UpdateTitle();
        UpdateSurfaceMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (string.Equals(change.Property.Name, nameof(FontSize), StringComparison.Ordinal))
        {
            _previousBlocks.Clear();
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

    /// <summary>Returns the effective body font size, using the inherited FontSize if &gt; 12, otherwise resolving Font.SizeBody.</summary>
    private double GetBodyFontSize()
    {
        var fs = FontSize;
        if (fs > 12) return fs;

        // Try resolving from theme resources
        if (this.TryFindResource("Font.SizeBody", ActualThemeVariant, out var res) && res is double d)
            return d;

        return 14; // Strata default
    }

    private void Rebuild()
    {
        _linkRuns.Clear();
        _bodyFontSize = GetBodyFontSize();
        _chartKeysUsed.Clear();
        _diagramKeysUsed.Clear();
        _confidenceKeysUsed.Clear();
        _comparisonKeysUsed.Clear();
        _cardKeysUsed.Clear();
        _sourcesKeysUsed.Clear();

        var source = Markdown;
        if (string.IsNullOrWhiteSpace(source))
        {
            _contentHost.Children.Clear();
            _previousBlocks.Clear();
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

        // Streaming fast path: new text appends to old text.
        var isStreamingAppend = _previousMarkdownNormalized is not null &&
                                normalized.Length > _previousMarkdownLength &&
                                normalized.AsSpan().StartsWith(_previousMarkdownNormalized.AsSpan());

        List<MdBlock> newBlocks;
        if (isStreamingAppend && EnableAppendTailParsing &&
            _previousMarkdownNormalized is not null && _previousBlocks.Count > 0)
        {
            newBlocks = ParseBlocksIncrementalAppend(_previousMarkdownNormalized, _previousBlocks, normalized);
        }
        else
        {
            newBlocks = ParseBlocks(normalized);
        }

        // ── Incremental diff: only touch changed blocks ──
        ApplyBlocksDiff(newBlocks, isStreamingAppend);

        _previousBlocks = newBlocks;
        _previousMarkdownNormalized = normalized;
        _previousMarkdownLength = normalized.Length;

        EvictStaleCaches();
    }

    /// <summary>
    /// Parses markdown text into a flat list of lightweight block descriptors.
    /// Pure function — no UI side-effects.
    /// </summary>
    internal static List<MdBlock> ParseBlocks(string normalized)
    {
        var blocks = new List<MdBlock>();
        ParseBlocksCore(normalized.AsSpan(), blocks, new StringBuilder(), new StringBuilder(), new List<string>(), 0);
        return blocks;
    }

    internal static List<MdBlock> ParseBlocksIncrementalAppend(
        string previousNormalized,
        IReadOnlyList<MdBlock> previousBlocks,
        string nextNormalized)
    {
        if (previousBlocks.Count == 0 ||
            string.IsNullOrEmpty(previousNormalized) ||
            nextNormalized.Length <= previousNormalized.Length ||
            !nextNormalized.AsSpan().StartsWith(previousNormalized.AsSpan()))
        {
            return ParseBlocks(nextNormalized);
        }

        var lastBlockStart = previousBlocks[^1].SourceStart;
        if (lastBlockStart < 0 || lastBlockStart > nextNormalized.Length)
            return ParseBlocks(nextNormalized);

        var merged = new List<MdBlock>(Math.Max(previousBlocks.Count + 8, 16));
        for (var i = 0; i < previousBlocks.Count - 1; i++)
            merged.Add(previousBlocks[i]);

        ParseBlocksCore(
            nextNormalized.AsSpan(lastBlockStart),
            merged,
            new StringBuilder(),
            new StringBuilder(),
            new List<string>(),
            lastBlockStart);

        return merged;
    }

    /// <summary>
    /// Core line-by-line parser. Enumerates lines from <paramref name="source"/> via span
    /// slicing and appends parsed block descriptors to <paramref name="blocks"/>.
    /// </summary>
    private static void ParseBlocksCore(
        ReadOnlySpan<char> source, List<MdBlock> blocks,
        StringBuilder paragraphBuffer, StringBuilder codeBuffer, List<string> tableBuffer,
        int baseOffset)
    {
        paragraphBuffer.Clear();
        codeBuffer.Clear();
        tableBuffer.Clear();
        var inCodeBlock = false;
        var codeLanguage = string.Empty;

        var paragraphStart = -1;
        var codeStart = -1;
        var tableStart = -1;

        // Enumerate lines using span slicing — no Split allocation
        var remaining = source;
        var localOffset = 0;
        while (remaining.Length > 0)
        {
            int nlPos = remaining.IndexOf('\n');
            ReadOnlySpan<char> lineSpan;
            int consumedLength;
            if (nlPos >= 0)
            {
                lineSpan = remaining[..nlPos];
                consumedLength = nlPos + 1;
                remaining = remaining[consumedLength..];
            }
            else
            {
                lineSpan = remaining;
                consumedLength = remaining.Length;
                remaining = ReadOnlySpan<char>.Empty;
            }

            var absoluteLineStart = baseOffset + localOffset;
            localOffset += consumedLength;

            // Trim trailing \r if present
            if (lineSpan.Length > 0 && lineSpan[^1] == '\r')
                lineSpan = lineSpan[..^1];

            if (lineSpan.Length >= 3 && lineSpan[0] == '`' && lineSpan[1] == '`' && lineSpan[2] == '`')
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                FlushTableBlock(tableBuffer, blocks, tableStart);
                tableStart = -1;

                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLanguage = lineSpan.Length > 3 ? lineSpan[3..].Trim().ToString() : string.Empty;
                    codeBuffer.Clear();
                    codeStart = absoluteLineStart;
                }
                else
                {
                    var code = codeBuffer.ToString();
                    var kind = MapCodeLanguageToBlockKind(codeLanguage);
                    blocks.Add(new MdBlock
                    {
                        Kind = kind,
                        Content = code,
                        Language = codeLanguage,
                        SourceStart = codeStart >= 0 ? codeStart : absoluteLineStart,
                    });
                    inCodeBlock = false;
                    codeLanguage = string.Empty;
                    codeBuffer.Clear();
                    codeStart = -1;
                }
                continue;
            }

            if (inCodeBlock)
            {
                if (codeBuffer.Length > 0)
                    codeBuffer.Append('\n');
                codeBuffer.Append(lineSpan);
                continue;
            }

            if (IsTableLine(lineSpan))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                if (tableBuffer.Count == 0)
                    tableStart = absoluteLineStart;
                tableBuffer.Add(lineSpan.ToString());
                continue;
            }

            if (tableBuffer.Count > 0)
            {
                FlushTableBlock(tableBuffer, blocks, tableStart);
                tableStart = -1;
            }

            if (TryParseHeading(lineSpan, out var level, out var headingText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.Heading,
                    Content = headingText,
                    Level = level,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            // HR must be checked before bullet: "- - -" is a valid HR that also matches
            // the bullet prefix ("- "). IsHorizontalRule rejects lines with mixed chars
            // or non-rule content, so genuine bullets like "- Item" fall through safely.
            if (IsHorizontalRule(lineSpan))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.HorizontalRule,
                    Content = string.Empty,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            if (TryParseBullet(lineSpan, out var bulletText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                var indentLevel = GetIndentLevel(lineSpan);
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.Bullet,
                    Content = bulletText,
                    Level = indentLevel,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            if (TryParseNumberedItem(lineSpan, out var number, out var numText))
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.NumberedItem,
                    Content = numText,
                    Level = number,
                    SourceStart = absoluteLineStart,
                });
                continue;
            }

            if (lineSpan.IsWhiteSpace())
            {
                FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, absoluteLineStart);
                paragraphStart = -1;
                continue;
            }

            if (paragraphStart < 0)
                paragraphStart = absoluteLineStart;
            if (paragraphBuffer.Length > 0)
                paragraphBuffer.Append('\n');
            paragraphBuffer.Append(lineSpan);
        }

        var endOffset = baseOffset + source.Length;

        // Handle unclosed code block
        if (inCodeBlock)
        {
            var code = codeBuffer.ToString();
            var kind = MapCodeLanguageToBlockKind(codeLanguage);
            blocks.Add(new MdBlock
            {
                Kind = kind,
                Content = code,
                Language = codeLanguage,
                SourceStart = codeStart >= 0 ? codeStart : endOffset,
            });
        }

        FlushTableBlock(tableBuffer, blocks, tableStart);
        FlushParagraphBlock(paragraphBuffer, blocks, paragraphStart, endOffset);
    }

    private static MdBlockKind MapCodeLanguageToBlockKind(string language)
    {
        if (language.Equals("chart", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Chart;
        if (language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Mermaid;
        if (language.Equals("confidence", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Confidence;
        if (language.Equals("comparison", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Comparison;
        if (language.Equals("card", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Card;
        if (language.Equals("sources", StringComparison.OrdinalIgnoreCase)) return MdBlockKind.Sources;
        return MdBlockKind.CodeBlock;
    }

    private static void FlushParagraphBlock(StringBuilder buffer, List<MdBlock> blocks, int paragraphStart, int fallbackStart)
    {
        var text = buffer.ToString().Trim();
        buffer.Clear();
        if (!string.IsNullOrWhiteSpace(text))
        {
            blocks.Add(new MdBlock
            {
                Kind = MdBlockKind.Paragraph,
                Content = text,
                SourceStart = paragraphStart >= 0 ? paragraphStart : fallbackStart,
            });
        }
    }

    private static void FlushTableBlock(List<string> tableLines, List<MdBlock> blocks, int tableStart)
    {
        if (tableLines.Count == 0) return;
        // Store the full table as joined lines
        blocks.Add(new MdBlock
        {
            Kind = MdBlockKind.Table,
            Content = string.Join("\n", tableLines),
            SourceStart = tableStart,
        });
        tableLines.Clear();
    }

    private static string NormalizeLineEndings(string source)
    {
        return source.IndexOf('\r') >= 0
            ? source.Replace("\r\n", "\n")
            : source;
    }

    /// <summary>
    /// Applies block diff: reuses unchanged controls, updates changed ones, adds/removes as needed.
    /// During streaming append, blocks before the last old block are guaranteed unchanged
    /// (the text is a strict prefix), so we skip straight to the divergence point.
    /// </summary>
    private void ApplyBlocksDiff(List<MdBlock> newBlocks, bool isStreamingAppend)
    {
        var oldBlocks = _previousBlocks;
        var oldCount = oldBlocks.Count;
        var newCount = newBlocks.Count;

        // Safety: if children count is out of sync with tracked blocks, do a full rebuild
        if (_contentHost.Children.Count != oldCount)
        {
            RebuildChildrenFromBlocks(newBlocks);
            return;
        }

        // During streaming, text is a strict prefix so blocks before the last are unchanged.
        var diffStart = isStreamingAppend && oldCount > 0 ? oldCount - 1 : 0;
        diffStart = Math.Min(diffStart, newCount);

        // Track cache keys for skipped unchanged blocks
        for (int i = 0; i < diffStart; i++)
            TrackCacheKeysForBlock(newBlocks[i]);

        var minCount = Math.Min(oldCount, newCount);

        // Update/skip from diffStart through the overlap range
        for (int i = diffStart; i < minCount; i++)
        {
            if (oldBlocks[i].Equals(newBlocks[i]))
            {
                TrackCacheKeysForBlock(newBlocks[i]);
                continue;
            }

            var replacement = CreateControlForBlock(newBlocks[i]);

            // Some cached controls are stateful and can be attached elsewhere.
            // If creating a replacement mutated the host's child collection, fall back
            // to a full rebuild to avoid out-of-range indexed assignments.
            if (_contentHost.Children.Count != oldCount || i >= _contentHost.Children.Count)
            {
                RebuildChildrenFromBlocks(newBlocks);
                return;
            }

            _contentHost.Children[i] = replacement;
        }

        if (_contentHost.Children.Count < minCount)
        {
            RebuildChildrenFromBlocks(newBlocks);
            return;
        }

        // Append new blocks
        for (int i = minCount; i < newCount; i++)
            _contentHost.Children.Add(CreateControlForBlock(newBlocks[i]));

        // Remove trailing stale blocks (iterate from end to avoid index shift)
        for (int i = _contentHost.Children.Count - 1; i >= newCount; i--)
            _contentHost.Children.RemoveAt(i);
    }

    private void RebuildChildrenFromBlocks(IReadOnlyList<MdBlock> blocks)
    {
        _contentHost.Children.Clear();
        foreach (var block in blocks)
            _contentHost.Children.Add(CreateControlForBlock(block));
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
                var firstLine = trimmed.Split('\n')[0].TrimStart().ToLowerInvariant();
                if (firstLine.StartsWith("graph") || firstLine.StartsWith("flowchart") ||
                    firstLine.StartsWith("sequencediagram") || firstLine.StartsWith("statediagram") ||
                    firstLine.StartsWith("erdiagram") || firstLine.StartsWith("classdiagram") ||
                    firstLine.StartsWith("timeline") || firstLine.StartsWith("quadrantchart") ||
                    firstLine.StartsWith("quadrant-chart"))
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
        }
    }

    /// <summary>Creates a UI control for a parsed block descriptor.</summary>
    private Control CreateControlForBlock(MdBlock block) => block.Kind switch
    {
        MdBlockKind.Paragraph => CreateParagraphControl(block.Content),
        MdBlockKind.Heading => CreateHeadingControl(block.Content, block.Level),
        MdBlockKind.Bullet => CreateBulletControl(block.Content, block.Level),
        MdBlockKind.NumberedItem => CreateNumberedItemControl(block.Content, block.Level),
        MdBlockKind.CodeBlock => CreateCodeBlockControl(block.Content, block.Language),
        MdBlockKind.HorizontalRule => CreateHorizontalRuleControl(),
        MdBlockKind.Table => CreateTableControl(block.Content),
        MdBlockKind.Chart => CreateChartControl(block.Content),
        MdBlockKind.Mermaid => CreateMermaidControl(block.Content),
        MdBlockKind.Confidence => CreateConfidenceControl(block.Content),
        MdBlockKind.Comparison => CreateComparisonControl(block.Content),
        MdBlockKind.Card => CreateCardControl(block.Content),
        MdBlockKind.Sources => CreateSourcesControl(block.Content),
        _ => new Border(),
    };

    private void EvictStaleCaches()
    {
        EvictFromCache(_chartCache, _chartKeysUsed);
        EvictFromCache(_diagramCache, _diagramKeysUsed);
        EvictFromCache(_confidenceCache, _confidenceKeysUsed);
        EvictFromCache(_comparisonCache, _comparisonKeysUsed);
        EvictFromCache(_cardCache, _cardKeysUsed);
        EvictFromCache(_sourcesCache, _sourcesKeysUsed);
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
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*"),
            Margin = new Thickness(indentLevel * 16, 0, 0, 0)
        };

        var dot = new Border
        {
            Width = 5,
            Height = 5,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(2.5),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 0, 0)
        };
        dot.Classes.Add("strata-md-bullet-dot");

        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        textBlock.Classes.Add("strata-md-bullet-text");

        Grid.SetColumn(dot, 0);
        Grid.SetColumn(textBlock, 2);
        row.Children.Add(dot);
        row.Children.Add(textBlock);

        return row;
    }

    private SelectableTextBlock CreateRichText(string text, double fontSize, double lineHeight, TextWrapping wrapping)
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
            Padding = isRtl
                ? new Thickness(0, 0, 4, 0)
                : new Thickness(0),
            Margin = new Thickness(0)
        };

        AppendFormattedInlines(textBlock, text);

        if (textBlock.Inlines != null)
        {
            foreach (var inline in textBlock.Inlines)
            {
                if (inline is Run run && _linkRuns.ContainsKey(run))
                {
                    textBlock.Tapped += OnLinkTapped;
                    textBlock.PointerMoved += OnTextBlockPointerMoved;
                    break;
                }
            }
        }

        return textBlock;
    }

    private static readonly Regex InlineRegex = new(
        @"(?<code>`[^`]+`)|(?<bolditalic>\*\*\*(?<bi_text>.+?)\*\*\*)|(?<bold>\*\*(?<b_text>.+?)\*\*)|(?<italic>\*(?<i_text>.+?)\*)|(?<link>\[(?<l_text>[^\]]+)\]\((?<l_url>[^)]+)\))",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private void AppendFormattedInlines(SelectableTextBlock target, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var matches = InlineRegex.Matches(text);
        if (matches.Count == 0)
        {
            target.Text = text;
            return;
        }

        var lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
                target.Inlines?.Add(new Run(text[lastIndex..match.Index]));

            if (match.Groups["code"].Success)
            {
                var codeText = match.Value[1..^1]; // strip backticks
                target.Inlines?.Add(new Run(" " + codeText + " ")
                {
                    FontFamily = ResolveMonoFont(),
                    FontSize = target.FontSize > 1 ? target.FontSize - 1 : target.FontSize,
                    Background = _inlineCodeBrush ??= ResolveInlineCodeBrush()
                });
            }
            else if (match.Groups["bolditalic"].Success)
            {
                target.Inlines?.Add(new Run(match.Groups["bi_text"].Value)
                {
                    FontWeight = FontWeight.Bold,
                    FontStyle = FontStyle.Italic
                });
            }
            else if (match.Groups["bold"].Success)
            {
                target.Inlines?.Add(new Run(match.Groups["b_text"].Value)
                {
                    FontWeight = FontWeight.Bold
                });
            }
            else if (match.Groups["italic"].Success)
            {
                target.Inlines?.Add(new Run(match.Groups["i_text"].Value)
                {
                    FontStyle = FontStyle.Italic
                });
            }
            else if (match.Groups["link"].Success)
            {
                var linkLabel = match.Groups["l_text"].Value;
                var linkTarget = match.Groups["l_url"].Value.Trim();

                var linkRun = new Run(linkLabel)
                {
                    Foreground = _linkBrush ??= ResolveLinkBrush(),
                    TextDecorations = TextDecorations.Underline,
                };
                _linkRuns[linkRun] = linkTarget;
                target.Inlines?.Add(linkRun);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            target.Inlines?.Add(new Run(text[lastIndex..]));
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


    private Control CreateCodeBlockControl(string code, string language)
    {
        var normalizedCode = code.TrimEnd('\r', '\n');
        var displayCode = string.IsNullOrWhiteSpace(normalizedCode) ? " " : normalizedCode;
        var lineCount = displayCode.Split('\n').Length;

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
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        copyBtn.Classes.Add("subtle");
        DockPanel.SetDock(copyBtn, Dock.Right);
        headerRow.Children.Insert(0, copyBtn);

        var capturedCode = displayCode;
        copyBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(copyBtn);
            if (topLevel?.Clipboard is not null)
                await topLevel.Clipboard.SetTextAsync(capturedCode);

            if (copyBtn.Content is TextBlock tb)
            {
                tb.Text = "Copied!";
                await Task.Delay(1200);
                tb.Text = "Copy";
            }
        };

        // Syntax-highlighted editor
        var editor = new TextEditor
        {
            Text = displayCode,
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontSize = 12,
            FontFamily = ResolveMonoFont(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 4, 10, 8),
            MinHeight = Math.Min(lineCount * 18 + 12, 400),
            MaxHeight = 400
        };
        editor.Classes.Add("strata-md-code-editor");

        ApplyTextMateHighlighting(editor, langText);

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(headerRow);
        stack.Children.Add(editor);

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

        var firstLine = trimmed.Split('\n')[0].TrimStart().ToLowerInvariant();

        // Diagram types → StrataMermaid
        if (firstLine.StartsWith("graph") || firstLine.StartsWith("flowchart") ||
            firstLine.StartsWith("sequencediagram") || firstLine.StartsWith("statediagram") ||
            firstLine.StartsWith("erdiagram") || firstLine.StartsWith("classdiagram") ||
            firstLine.StartsWith("timeline") || firstLine.StartsWith("quadrantchart") ||
            firstLine.StartsWith("quadrant-chart"))
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

        var diagram = new StrataMermaid { Source = mermaidText };
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
                title = line[6..].Trim().Trim('"');
                continue;
            }

            var match = MermaidPieEntryRegex.Match(line);
            if (match.Success)
            {
                labels.Add(match.Groups["label"].Value);
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
                var rest = line[5..].Trim().Trim('"');
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

    private static void ApplyTextMateHighlighting(TextEditor editor, string language)
    {
        try
        {
            var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

            // Reuse cached RegistryOptions — creating one is expensive (~50ms)
            RegistryOptions registryOptions;
            if (isDark)
            {
                _darkRegistry ??= new RegistryOptions(ThemeName.DarkPlus);
                registryOptions = _darkRegistry;
            }
            else
            {
                _lightRegistry ??= new RegistryOptions(ThemeName.LightPlus);
                registryOptions = _lightRegistry;
            }

            var installation = editor.InstallTextMate(registryOptions);

            if (!string.IsNullOrWhiteSpace(language))
            {
                var lang = registryOptions.GetLanguageByExtension("." + language)
                        ?? registryOptions.GetLanguageByExtension(language == "csharp" ? ".cs" : "." + language);

                if (lang is not null)
                {
                    installation.SetGrammar(registryOptions.GetScopeByLanguageId(lang.Id));
                }
            }
        }
        catch
        {
            // Graceful fallback: no highlighting
        }
    }

    private FontFamily ResolveMonoFont()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Font.FamilyMono", Application.Current.ActualThemeVariant, out var font) &&
            font is FontFamily mono)
        {
            return mono;
        }

        return FontFamily.Default;
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
        tb.Cursor = isLink ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
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

    private static IBrush ResolveInlineCodeBrush()
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource("Brush.AccentSubtle", Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }

        return Brushes.LightGray;
    }

    private static bool TryParseHeading(ReadOnlySpan<char> line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#')
            return false;

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
            i++;

        if (i is < 1 or > 6)
            return false;

        if (i >= trimmed.Length || trimmed[i] != ' ')
            return false;

        level = Math.Min(i, 3);
        text = trimmed[(i + 1)..].Trim().ToString();
        return text.Length > 0;
    }

    private static bool TryParseBullet(ReadOnlySpan<char> line, out string text)
    {
        text = string.Empty;
        var trimmed = line.TrimStart();

        if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*') && trimmed[1] == ' ')
        {
            text = trimmed[2..].Trim().ToString();
            return text.Length > 0;
        }

        return false;
    }

    private static bool TryParseNumberedItem(ReadOnlySpan<char> line, out int number, out string text)
    {
        number = 0;
        text = string.Empty;
        var trimmed = line.TrimStart();

        // Look for "N. " pattern (1-3 digit number followed by ". ")
        var dotIndex = trimmed.IndexOf(stackalloc char[] { '.', ' ' });
        if (dotIndex is < 1 or > 3) return false;

        // Verify digits before the dot
        var numSpan = trimmed[..dotIndex];
        number = 0;
        foreach (var c in numSpan)
        {
            if (c < '0' || c > '9') return false;
            number = number * 10 + (c - '0');
        }

        if (dotIndex + 2 > trimmed.Length) return false;
        text = trimmed[(dotIndex + 2)..].Trim().ToString();
        return text.Length > 0;
    }

    private static bool IsHorizontalRule(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3) return false;

        var first = trimmed[0];
        if (first is not ('-' or '*' or '_')) return false;

        // Count rule chars, skipping spaces
        var ruleChars = 0;
        foreach (var c in trimmed)
        {
            if (c == first) ruleChars++;
            else if (c != ' ') return false;
        }
        return ruleChars >= 3;
    }

    private static int GetIndentLevel(ReadOnlySpan<char> line)
    {
        var spaces = 0;
        foreach (var c in line)
        {
            if (c == ' ') spaces++;
            else if (c == '\t') spaces += 4;
            else break;
        }
        return Math.Min(spaces / 2, 3);
    }

    private Control CreateNumberedItemControl(string text, int number)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,6,*"),
        };

        var numBlock = new SelectableTextBlock
        {
            Text = $"{number}.",
            FontSize = _bodyFontSize,
            LineHeight = _bodyFontSize * 1.52,
            VerticalAlignment = VerticalAlignment.Top
        };
        numBlock.Classes.Add("strata-md-number");

        var textBlock = CreateRichText(text, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
        textBlock.Classes.Add("strata-md-bullet-text");

        Grid.SetColumn(numBlock, 0);
        Grid.SetColumn(textBlock, 2);
        row.Children.Add(numBlock);
        row.Children.Add(textBlock);

        return row;
    }

    private static Control CreateHorizontalRuleControl()
    {
        var rule = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        rule.Classes.Add("strata-md-hr");
        return rule;
    }

    private static bool IsTableLine(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 1 && trimmed[0] == '|' && trimmed[1..].IndexOf('|') >= 0;
    }

    private static bool IsTableSeparator(string line)
    {
        var cells = SplitTableCells(line);
        return cells.Length > 0 && cells.All(c => Regex.IsMatch(c.Trim(), @"^:?-+:?$"));
    }

    private static string[] SplitTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|');
    }

    private Control CreateTableControl(string tableContent)
    {
        var tableLines = tableContent.Split('\n').ToList();

        if (tableLines.Count < 2 || !IsTableSeparator(tableLines[1]))
        {
            var stack = new StackPanel { Spacing = 4 };
            foreach (var line in tableLines)
            {
                var para = CreateRichText(line, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
                para.Classes.Add("strata-md-paragraph");
                stack.Children.Add(para);
            }
            return stack;
        }

        var headers = SplitTableCells(tableLines[0]).Select(h => h.Trim()).ToArray();
        var rows = new List<string[]>();

        for (var i = 2; i < tableLines.Count; i++)
        {
            var cells = SplitTableCells(tableLines[i]).Select(c => c.Trim()).ToArray();
            var padded = new string[headers.Length];
            for (var j = 0; j < headers.Length; j++)
                padded[j] = j < cells.Length ? cells[j] : string.Empty;
            rows.Add(padded);
        }

        return CreateDataGridForTable(headers, rows);
    }

    private Control CreateDataGridForTable(string[] headers, List<string[]> rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            MaxHeight = 400,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var colIndex = i;
            dataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = CreateRichText(headers[i], _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.NoWrap),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = new FuncDataTemplate<List<string>>((row, _) =>
                {
                    var cellText = row is not null && colIndex < row.Count ? row[colIndex] : string.Empty;
                    return CreateRichText(cellText, _bodyFontSize, _bodyFontSize * 1.52, TextWrapping.Wrap);
                })
            });
        }

        var items = rows.Select(r => r.ToList()).ToList();
        dataGrid.ItemsSource = items;

        var estimatedHeight = 40 + (rows.Count * 36) + 4;
        dataGrid.Height = Math.Min(estimatedHeight, 400);

        return dataGrid;
    }
}
