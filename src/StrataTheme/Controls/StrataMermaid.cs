using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Renders Mermaid flowcharts, sequence diagrams, state diagrams, ER diagrams,
/// class diagrams, timelines, and quadrant charts using Strata semantic tokens
/// and custom canvas drawing.
/// </summary>
/// <remarks>
/// <para><b>Supported diagram types:</b></para>
/// <list type="bullet">
///   <item><c>graph TD/LR</c> or <c>flowchart TD/LR</c> — directed flowcharts.</item>
///   <item><c>sequenceDiagram</c> — participant interactions with messages.</item>
///   <item><c>stateDiagram-v2</c> — state machines with transitions.</item>
///   <item><c>erDiagram</c> — entity-relationship diagrams.</item>
///   <item><c>classDiagram</c> — class hierarchies with attributes, methods, and relationships.</item>
///   <item><c>timeline</c> — chronological event sequences with sections.</item>
///   <item><c>quadrantChart</c> — 2×2 quadrant scatter plots with labeled axes.</item>
/// </list>
/// <para><b>Template parts:</b> PART_DiagramHost (Panel).</para>
/// </remarks>
public class StrataMermaid : TemplatedControl
{
    private Panel? _host;
    private MermaidCanvas? _canvas;
    private Border? _zoomBar;
    private Button? _zoomIn, _zoomOut, _zoomReset;
    private TextBlock? _zoomLabel;

    /// <summary>Identifies the <see cref="Source"/> styled property.</summary>
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<StrataMermaid, string?>(nameof(Source));

    static StrataMermaid()
    {
        SourceProperty.Changed.AddClassHandler<StrataMermaid>((c, _) => c.Invalidate());
    }

    /// <summary>Gets or sets the Mermaid diagram source text.</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_zoomIn is not null) _zoomIn.Click -= OnZoomIn;
        if (_zoomOut is not null) _zoomOut.Click -= OnZoomOut;
        if (_zoomReset is not null) _zoomReset.Click -= OnZoomReset;

        _host = e.NameScope.Find<Panel>("PART_DiagramHost");
        _zoomBar = e.NameScope.Find<Border>("PART_ZoomBar");
        _zoomIn = e.NameScope.Find<Button>("PART_ZoomIn");
        _zoomOut = e.NameScope.Find<Button>("PART_ZoomOut");
        _zoomReset = e.NameScope.Find<Button>("PART_ZoomReset");
        _zoomLabel = e.NameScope.Find<TextBlock>("PART_ZoomLabel");

        if (_zoomIn is not null) _zoomIn.Click += OnZoomIn;
        if (_zoomOut is not null) _zoomOut.Click += OnZoomOut;
        if (_zoomReset is not null) _zoomReset.Click += OnZoomReset;

        if (_host is not null)
        {
            _canvas = new MermaidCanvas(this)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _host.Children.Add(_canvas);
        }
    }

    private void OnZoomIn(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _canvas?.ZoomBy(1.25);
        UpdateZoomLabel();
    }

    private void OnZoomOut(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _canvas?.ZoomBy(1.0 / 1.25);
        UpdateZoomLabel();
    }

    private void OnZoomReset(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _canvas?.ResetView();
        UpdateZoomLabel();
    }

    internal void UpdateZoomLabel()
    {
        if (_zoomLabel is null || _canvas is null) return;
        var pct = (int)Math.Round(_canvas.EffectiveScale * 100);
        _zoomLabel.Text = $"{pct}%";
        if (_zoomBar is not null)
            _zoomBar.Opacity = Math.Abs(_canvas.EffectiveScale - 1.0) > 0.01 || _canvas.HasPan ? 1.0 : 0.0;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
            _canvas?.InvalidateVisual();
    }

    private void Invalidate()
    {
        if (_canvas is null) return;
        _canvas.Reparse();
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    internal IBrush ResolveBrush(string key, Color fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var res) && res is IBrush b)
            return b;
        return new SolidColorBrush(fallback);
    }

    // ═══════════════════════════════════════════════════════════
    //  DATA MODELS
    // ═══════════════════════════════════════════════════════════

    private enum DiagramKind { Flowchart, Sequence, State, Er, Class, Timeline, Quadrant }
    private enum NShape { Rect, Rounded, Diamond, Circle, Stadium }
    private enum EStyle { Arrow, Line, Dotted, Thick }

    private sealed class FNode
    {
        public string Id = "", Text = "";
        public NShape Shape;
        public double X, Y, W, H;
        public int Rank;
    }

    private sealed class FEdge
    {
        public string From = "", To = "";
        public string? Label;
        public EStyle Style;
    }

    private sealed class FSubgraph
    {
        public string Id = "";
        public List<string> NodeOrder = new();
        public HashSet<string> NodeSet = new(StringComparer.Ordinal);

        public string? FirstNode => NodeOrder.Count > 0 ? NodeOrder[0] : null;
        public string? LastNode => NodeOrder.Count > 0 ? NodeOrder[^1] : null;
    }

    private sealed class SParticipant { public string Id = "", Label = ""; public double X; }
    private sealed class SMessage { public string From = "", To = "", Text = ""; public bool Dashed; public double Y; }

    private sealed class StNode
    {
        public string Id = "", Text = "";
        public bool IsStart, IsEnd;
        public double X, Y, W, H;
        public int Rank;
    }

    private sealed class StEdge { public string From = "", To = ""; public string? Label; }

    // ER diagram
    private sealed class ErEntity
    {
        public string Name = "";
        public List<ErField> Fields = new();
        public double X, Y, W, H;
    }

    private sealed class ErField
    {
        public string Type = "", Name = "";
        public string? Constraint; // PK, FK, UK
    }

    private sealed class ErRelation
    {
        public string From = "", To = "";
        public string LeftCard = "", RightCard = "";
        public string Label = "";
    }

    // Class diagram
    private sealed class CdClass
    {
        public string Name = "";
        public List<string> Attributes = new();
        public List<string> Methods = new();
        public double X, Y, W, H;
    }

    private enum CdRelType { Inheritance, Composition, Aggregation, Association, Dependency, Realization }

    private sealed class CdRelation
    {
        public string From = "", To = "";
        public CdRelType Type;
        public string? Label;
    }

    // Timeline
    private sealed class TlSection
    {
        public string Title = "";
        public List<TlEvent> Events = new();
        public double X, W;
        public int ColorIndex;
    }

    private sealed class TlEvent
    {
        public string TimeLabel = "";
        public string Text = "";
        public double X, Y;
    }

    // Quadrant chart
    private sealed class QdPoint
    {
        public string Label = "";
        public double X, Y;
    }

    // ═══════════════════════════════════════════════════════════
    //  CANVAS
    // ═══════════════════════════════════════════════════════════

    private sealed class MermaidCanvas : Control
    {
        private readonly StrataMermaid _owner;
        private DiagramKind _kind;
        private bool _ltr;

        // Flowchart
        private readonly Dictionary<string, FNode> _fNodes = new();
        private readonly List<FEdge> _fEdges = new();

        // Sequence
        private readonly List<SParticipant> _sParts = new();
        private readonly List<SMessage> _sMsgs = new();

        // State
        private readonly Dictionary<string, StNode> _stNodes = new();
        private readonly List<StEdge> _stEdges = new();

        // ER
        private readonly Dictionary<string, ErEntity> _erEntities = new();
        private readonly List<ErRelation> _erRelations = new();

        // Class diagram
        private readonly Dictionary<string, CdClass> _cdClasses = new();
        private readonly List<CdRelation> _cdRelations = new();

        // Timeline
        private string _tlTitle = "";
        private readonly List<TlSection> _tlSections = new();

        // Quadrant chart
        private string _qdTitle = "";
        private string _qdXLow = "", _qdXHigh = "", _qdYLow = "", _qdYHigh = "";
        private readonly string[] _qdLabels = new string[4];
        private readonly List<QdPoint> _qdPoints = new();

        private double _layW, _layH;
        private bool _parsed;
        private double _baseScale = 1.0;

        // Pan & zoom
        private double _userZoom = 1.0;
        private double _panX, _panY;
        private bool _isPanning;
        private Point _panStart;
        private double _panStartX, _panStartY;

        // Entrance animation
        private double _animProgress = 1.0;
        private DateTime _animStartTime;
        private bool _animating;
        private DispatcherTimer? _animTimer;
        private const double AnimDuration = 500.0;

        // Constants
        private const double Pad = 24;
        private const double NPadH = 16, NPadV = 10;
        private const double NMinW = 80, NH = 36;
        private const double NGapH = 50, NGapV = 56;
        private const double Fs = 12, FsSmall = 10;
        private const double ArrSz = 8;
        private const double SeqBoxH = 36, SeqMsgGap = 44, SeqMinCol = 130;

        public MermaidCanvas(StrataMermaid owner)
        {
            _owner = owner;
            ClipToBounds = true;
        }

        internal double UserZoom => _userZoom;
        internal double EffectiveScale => _baseScale * _userZoom;
        internal bool HasPan => Math.Abs(_panX) > 1 || Math.Abs(_panY) > 1;

        internal void ZoomBy(double factor)
        {
            _userZoom = Math.Clamp(_userZoom * factor, 0.5, 4.0);
            InvalidateVisual();
        }

        internal void ResetView()
        {
            _userZoom = 1.0;
            _panX = _panY = 0;
            InvalidateVisual();
        }

        internal void Reparse()
        {
            _parsed = false;
            _userZoom = 1.0;
            _panX = _panY = 0;
            StartEntranceAnimation();
        }

        // ── ANIMATION ──────────────────────────────────────────

        private void StartEntranceAnimation()
        {
            _animProgress = 0;
            _animStartTime = DateTime.UtcNow;
            _animating = true;
            _animTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimTick);
            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _animStartTime).TotalMilliseconds;
            var t = Math.Clamp(elapsed / AnimDuration, 0, 1);
            // Ease-out cubic
            t = 1.0 - Math.Pow(1.0 - t, 3);
            _animProgress = t;

            if (t >= 1.0)
            {
                _animProgress = 1.0;
                _animating = false;
                _animTimer?.Stop();
            }

            InvalidateVisual();
        }

        // ── PAN ─────────────────────────────────────────────

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    ResetView();
                    _owner.UpdateZoomLabel();
                    e.Handled = true;
                    return;
                }

                _isPanning = true;
                _panStart = e.GetPosition(this);
                _panStartX = _panX;
                _panStartY = _panY;
                e.Pointer.Capture(this);
                Cursor = new Cursor(StandardCursorType.SizeAll);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_isPanning)
            {
                var pos = e.GetPosition(this);
                _panX = _panStartX + (pos.X - _panStart.X);
                _panY = _panStartY + (pos.Y - _panStart.Y);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                e.Pointer.Capture(null);
                Cursor = null;
                _owner.UpdateZoomLabel();
                e.Handled = true;
            }
        }

        // ── MEASURE ────────────────────────────────────────────

        protected override Size MeasureOverride(Size available)
        {
            if (!_parsed) DoParse();

            var w = double.IsInfinity(available.Width) ? 600 : Math.Max(280, available.Width);
            ComputeLayout(w);

            _baseScale = 1.0;
            if (_layW + Pad * 2 > w && _layW > 0)
                _baseScale = (w - 12) / (_layW + Pad * 2);

            _owner.UpdateZoomLabel();

            return new Size(
                Math.Min(w, _layW + Pad * 2),
                Math.Max(140, (_layH + Pad * 2) * _baseScale));
        }

        // ── PARSING ────────────────────────────────────────────

        private void DoParse()
        {
            _fNodes.Clear(); _fEdges.Clear();
            _sParts.Clear(); _sMsgs.Clear();
            _stNodes.Clear(); _stEdges.Clear();
            _erEntities.Clear(); _erRelations.Clear();
            _cdClasses.Clear(); _cdRelations.Clear();
            _tlTitle = ""; _tlSections.Clear();
            _qdTitle = ""; _qdXLow = ""; _qdXHigh = ""; _qdYLow = ""; _qdYHigh = "";
            _qdLabels[0] = _qdLabels[1] = _qdLabels[2] = _qdLabels[3] = "";
            _qdPoints.Clear();
            _ltr = false; _parsed = true;

            var src = _owner.Source?.Trim();
            if (string.IsNullOrWhiteSpace(src)) return;

            var lines = src.Split('\n');
            var first = lines[0].Trim().ToLowerInvariant();

            if (first.StartsWith("sequencediagram"))
            { _kind = DiagramKind.Sequence; ParseSeq(lines); }
            else if (first.StartsWith("statediagram"))
            { _kind = DiagramKind.State; ParseState(lines); }
            else if (first.StartsWith("erdiagram"))
            { _kind = DiagramKind.Er; ParseEr(lines); }
            else if (first.StartsWith("classdiagram"))
            { _kind = DiagramKind.Class; ParseClass(lines); }
            else if (first.StartsWith("timeline"))
            { _kind = DiagramKind.Timeline; ParseTimeline(lines); }
            else if (first.StartsWith("quadrantchart") || first.StartsWith("quadrant-chart"))
            { _kind = DiagramKind.Quadrant; ParseQuadrant(lines); }
            else
            {
                _kind = DiagramKind.Flowchart;
                _ltr = first.Contains(" lr");
                ParseFlow(lines);
            }
        }

        // ── Flowchart parse ────────────────────────────────────

        private const string NodeIdPattern = @"[A-Za-z0-9_:.\-/]+";

        private static readonly (Regex rx, NShape shape)[] NodeRxs =
        {
            (new($@"({NodeIdPattern})\s*\(\((.+?)\)\)", RegexOptions.Compiled), NShape.Circle),
            (new($@"({NodeIdPattern})\s*\(\[(.+?)\]\)", RegexOptions.Compiled), NShape.Stadium),
            (new($@"({NodeIdPattern})\s*\{{(.+?)\}}", RegexOptions.Compiled), NShape.Diamond),
            (new($@"({NodeIdPattern})\s*\[([^\]]+)\]", RegexOptions.Compiled), NShape.Rect),
            (new($@"({NodeIdPattern})\s*\(([^)]+)\)", RegexOptions.Compiled), NShape.Rounded),
        };

        private static readonly Regex SubgraphStartRegex = new(
            $@"^subgraph\s+(?<id>{NodeIdPattern})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EndpointRightRegex = new(
            $@"(?<id>{NodeIdPattern})\s*$",
            RegexOptions.Compiled);

        private static readonly Regex EndpointLeftRegex = new(
            $@"^\s*(?<id>{NodeIdPattern})",
            RegexOptions.Compiled);

        private static readonly string[] Arrows = { "<-.->", "<==>", "<-->", "<--", "-.->", "==>", "-->", "---" };

        private void ParseFlow(string[] lines)
        {
            var subgraphs = new Dictionary<string, FSubgraph>(StringComparer.Ordinal);
            var subgraphStack = new Stack<FSubgraph>();

            static FSubgraph GetOrCreateSubgraph(Dictionary<string, FSubgraph> map, string id)
            {
                if (!map.TryGetValue(id, out var subgraph))
                {
                    subgraph = new FSubgraph { Id = id };
                    map[id] = subgraph;
                }

                return subgraph;
            }

            static void AddNodeToSubgraph(FSubgraph subgraph, string nodeId)
            {
                if (subgraph.NodeSet.Add(nodeId))
                    subgraph.NodeOrder.Add(nodeId);
            }

            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%") ||
                    line.StartsWith("style ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("classDef", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("class ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("click ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var subgraphMatch = SubgraphStartRegex.Match(line);
                if (subgraphMatch.Success)
                {
                    var subgraphId = subgraphMatch.Groups["id"].Value;
                    if (!string.IsNullOrWhiteSpace(subgraphId))
                    {
                        var subgraph = GetOrCreateSubgraph(subgraphs, subgraphId);
                        subgraphStack.Push(subgraph);
                    }

                    continue;
                }

                if (string.Equals(line, "end", StringComparison.OrdinalIgnoreCase))
                {
                    if (subgraphStack.Count > 0)
                        subgraphStack.Pop();
                    continue;
                }

                var lineNodeIds = new List<string>();

                // Extract nodes
                foreach (var (rx, shape) in NodeRxs)
                    foreach (Match m in rx.Matches(line))
                    {
                        if (IsInsideBracketLabel(line, m.Index))
                            continue;

                        var id = m.Groups[1].Value;
                        lineNodeIds.Add(id);
                        if (!_fNodes.ContainsKey(id))
                            _fNodes[id] = new FNode { Id = id, Text = m.Groups[2].Value.Trim(), Shape = shape };
                    }

                if (subgraphStack.Count > 0 && lineNodeIds.Count > 0)
                {
                    foreach (var subgraph in subgraphStack)
                        foreach (var nodeId in lineNodeIds)
                            AddNodeToSubgraph(subgraph, nodeId);
                }

                // Extract edges — strip bracket content first
                var stripped = Regex.Replace(line, @"\(\([^)]*\)\)|\(\[[^\]]*\]\)|\{[^}]*\}|\[[^\]]*\]|\([^)]*\)", "");

                foreach (var arrow in Arrows)
                {
                    // Handle chained: A --> B --> C
                    var parts = stripped.Split(new[] { arrow }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;

                    var isBidirectional = IsBidirectionalArrow(arrow);
                    var isRightToLeft = IsRightToLeftArrow(arrow);
                    var style = MapFlowEdgeStyle(arrow);

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        var fromId = LastWord(parts[i]);
                        var toPart = parts[i + 1].Trim();

                        string? label = null;
                        if (toPart.StartsWith('|'))
                        {
                            var end = toPart.IndexOf('|', 1);
                            if (end > 0)
                            {
                                label = toPart[1..end].Trim();
                                toPart = toPart[(end + 1)..].Trim();
                            }
                        }
                        var toId = FirstWord(toPart);
                        if (fromId.Length == 0 || toId.Length == 0) continue;

                        if (subgraphs.TryGetValue(fromId, out var fromSubgraph) && !string.IsNullOrWhiteSpace(fromSubgraph.LastNode))
                            fromId = fromSubgraph.LastNode!;

                        if (subgraphs.TryGetValue(toId, out var toSubgraph) && !string.IsNullOrWhiteSpace(toSubgraph.FirstNode))
                            toId = toSubgraph.FirstNode!;

                        if (isRightToLeft)
                            (fromId, toId) = (toId, fromId);

                        AddUniqueFlowEdge(fromId, toId, label, style);
                        if (isBidirectional)
                            AddUniqueFlowEdge(toId, fromId, label, style);
                    }
                    break; // One arrow type per line
                }
            }

            // Ensure all edge endpoints have nodes
            foreach (var e in _fEdges)
            {
                if (!_fNodes.ContainsKey(e.From))
                    _fNodes[e.From] = new FNode { Id = e.From, Text = e.From, Shape = NShape.Rect };
                if (!_fNodes.ContainsKey(e.To))
                    _fNodes[e.To] = new FNode { Id = e.To, Text = e.To, Shape = NShape.Rect };
            }

            foreach (var subgraph in subgraphs.Values)
            {
                if (subgraph.NodeOrder.Count < 2)
                    continue;

                var hasInternalEdges = _fEdges.Any(e =>
                    subgraph.NodeSet.Contains(e.From) && subgraph.NodeSet.Contains(e.To));

                if (hasInternalEdges)
                    continue;

                for (var i = 0; i < subgraph.NodeOrder.Count - 1; i++)
                    AddUniqueFlowEdge(subgraph.NodeOrder[i], subgraph.NodeOrder[i + 1], null, EStyle.Arrow);
            }
        }

        private void AddUniqueFlowEdge(string fromId, string toId, string? label, EStyle style)
        {
            if (_fEdges.Any(e =>
                string.Equals(e.From, fromId, StringComparison.Ordinal) &&
                string.Equals(e.To, toId, StringComparison.Ordinal) &&
                string.Equals(e.Label, label, StringComparison.Ordinal) &&
                e.Style == style))
                return;

            _fEdges.Add(new FEdge { From = fromId, To = toId, Label = label, Style = style });
        }

        private static bool IsBidirectionalArrow(string arrow) =>
            arrow.StartsWith('<') && arrow.EndsWith('>');

        private static bool IsRightToLeftArrow(string arrow) =>
            arrow.StartsWith('<') && !arrow.EndsWith('>');

        private static EStyle MapFlowEdgeStyle(string arrow)
        {
            if (arrow.Contains('.', StringComparison.Ordinal)) return EStyle.Dotted;
            if (arrow.Contains('=', StringComparison.Ordinal)) return EStyle.Thick;
            if (arrow.Contains("---", StringComparison.Ordinal)) return EStyle.Line;
            return EStyle.Arrow;
        }

        // ── Sequence parse ─────────────────────────────────────

        private static readonly Regex SeqPartRx = new(
            @"^(?:participant|actor)\s+(\w+)(?:\s+as\s+(.+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SeqMsgRx = new(
            @"^(\w+)\s*(->>|-->>|->|-->)\s*(\w+)\s*:\s*(.+)$", RegexOptions.Compiled);

        private void ParseSeq(string[] lines)
        {
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%")) continue;

                var pm = SeqPartRx.Match(line);
                if (pm.Success)
                {
                    var id = pm.Groups[1].Value;
                    var label = pm.Groups[2].Success ? pm.Groups[2].Value.Trim() : id;
                    if (_sParts.All(p => p.Id != id))
                        _sParts.Add(new SParticipant { Id = id, Label = label });
                    continue;
                }

                var mm = SeqMsgRx.Match(line);
                if (mm.Success)
                {
                    var from = mm.Groups[1].Value;
                    var arrow = mm.Groups[2].Value;
                    var to = mm.Groups[3].Value;
                    var text = mm.Groups[4].Value.Trim();

                    EnsurePart(from);
                    EnsurePart(to);
                    _sMsgs.Add(new SMessage { From = from, To = to, Text = text, Dashed = arrow.Contains("--") });
                }
            }
        }

        private void EnsurePart(string id)
        {
            if (_sParts.All(p => p.Id != id))
                _sParts.Add(new SParticipant { Id = id, Label = id });
        }

        // ── State parse ────────────────────────────────────────

        private static readonly Regex StTransRx = new(
            @"^(\[\*\]|\w+)\s*-->\s*(\[\*\]|\w+)(?:\s*:\s*(.+))?$", RegexOptions.Compiled);
        private static readonly Regex StDeclRx = new(
            @"^state\s+""([^""]+)""\s+as\s+(\w+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private void ParseState(string[] lines)
        {
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%")) continue;

                var dm = StDeclRx.Match(line);
                if (dm.Success)
                {
                    _stNodes[dm.Groups[2].Value] = new StNode { Id = dm.Groups[2].Value, Text = dm.Groups[1].Value };
                    continue;
                }

                var tm = StTransRx.Match(line);
                if (tm.Success)
                {
                    var fromRaw = tm.Groups[1].Value;
                    var toRaw = tm.Groups[2].Value;
                    var label = tm.Groups[3].Success ? tm.Groups[3].Value.Trim() : null;

                    var from = fromRaw == "[*]" ? "__start__" : fromRaw;
                    var to = toRaw == "[*]" ? "__end__" : toRaw;

                    if (from == "__start__" && !_stNodes.ContainsKey("__start__"))
                        _stNodes["__start__"] = new StNode { Id = "__start__", IsStart = true };
                    if (to == "__end__" && !_stNodes.ContainsKey("__end__"))
                        _stNodes["__end__"] = new StNode { Id = "__end__", IsEnd = true };
                    if (from != "__start__" && !_stNodes.ContainsKey(from))
                        _stNodes[from] = new StNode { Id = from, Text = from };
                    if (to != "__end__" && !_stNodes.ContainsKey(to))
                        _stNodes[to] = new StNode { Id = to, Text = to };

                    _stEdges.Add(new StEdge { From = from, To = to, Label = label });
                }
            }
        }

        // ── ER parse ───────────────────────────────────────────

        private static readonly Regex ErRelRx = new(
            @"^(\w+)\s+(\|[|o{}]--[|o{}]\|)\s+(\w+)\s*:\s*(.+)$", RegexOptions.Compiled);

        private void ParseEr(string[] lines)
        {
            ErEntity? current = null;

            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%")) continue;

                // Closing brace
                if (line == "}")
                {
                    current = null;
                    continue;
                }

                // Entity block start: ENTITY_NAME {
                if (line.EndsWith('{'))
                {
                    var name = line[..^1].Trim();
                    if (name.Length > 0 && !_erEntities.ContainsKey(name))
                    {
                        current = new ErEntity { Name = name };
                        _erEntities[name] = current;
                    }
                    continue;
                }

                // Inside entity block: type name PK/FK/UK
                if (current is not null)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        current.Fields.Add(new ErField
                        {
                            Type = parts[0],
                            Name = parts[1],
                            Constraint = parts.Length >= 3 ? parts[2] : null
                        });
                    }
                    continue;
                }

                // Relationship: ENTITY ||--o{ ENTITY : label
                var rm = ErRelRx.Match(line);
                if (rm.Success)
                {
                    var card = rm.Groups[2].Value;
                    var leftCard = card[..2];
                    var rightCard = card[^2..];
                    _erRelations.Add(new ErRelation
                    {
                        From = rm.Groups[1].Value,
                        To = rm.Groups[3].Value,
                        LeftCard = leftCard,
                        RightCard = rightCard,
                        Label = rm.Groups[4].Value.Trim()
                    });

                    // Ensure entities exist
                    if (!_erEntities.ContainsKey(rm.Groups[1].Value))
                        _erEntities[rm.Groups[1].Value] = new ErEntity { Name = rm.Groups[1].Value };
                    if (!_erEntities.ContainsKey(rm.Groups[3].Value))
                        _erEntities[rm.Groups[3].Value] = new ErEntity { Name = rm.Groups[3].Value };
                }
            }
        }

        // ── Class diagram parse ────────────────────────────────

        private static readonly Regex CdClassBlockRx = new(
            @"^\s*class\s+(\w+)\s*\{?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CdMemberRx = new(
            @"^\s*([+\-#~])?\s*(.+)$", RegexOptions.Compiled);
        private static readonly Regex CdRelRx = new(
            @"^(\w+)\s+(<\|--|<\|\.\.|--\*|--o|-->|\.\.>|--)\s+(\w+)(?:\s*:\s*(.+))?$", RegexOptions.Compiled);
        private static readonly Regex CdAnnotationRx = new(
            @"^\s*<<\s*(.+?)\s*>>\s*$", RegexOptions.Compiled);

        private void ParseClass(string[] lines)
        {
            CdClass? current = null;

            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%")) continue;

                if (line == "}")
                {
                    current = null;
                    continue;
                }

                // Class block: class Foo {
                var cm = CdClassBlockRx.Match(line);
                if (cm.Success)
                {
                    var name = cm.Groups[1].Value;
                    if (!_cdClasses.ContainsKey(name))
                        _cdClasses[name] = new CdClass { Name = name };
                    current = _cdClasses[name];
                    continue;
                }

                // Inside class block
                if (current is not null)
                {
                    // Skip annotations like <<interface>>
                    if (CdAnnotationRx.IsMatch(line)) continue;

                    var mm = CdMemberRx.Match(line);
                    if (mm.Success)
                    {
                        var member = mm.Groups[2].Value.Trim();
                        if (member.Contains('('))
                            current.Methods.Add(line.Trim());
                        else
                            current.Attributes.Add(line.Trim());
                    }
                    continue;
                }

                // Relationship: A <|-- B
                var rm = CdRelRx.Match(line);
                if (rm.Success)
                {
                    var arrow = rm.Groups[2].Value;
                    var relType = arrow switch
                    {
                        "<|--" => CdRelType.Inheritance,
                        "<|.." => CdRelType.Realization,
                        "--*" => CdRelType.Composition,
                        "--o" => CdRelType.Aggregation,
                        "..>" => CdRelType.Dependency,
                        _ => CdRelType.Association
                    };
                    _cdRelations.Add(new CdRelation
                    {
                        From = rm.Groups[1].Value,
                        To = rm.Groups[3].Value,
                        Type = relType,
                        Label = rm.Groups[4].Success ? rm.Groups[4].Value.Trim() : null
                    });

                    // Ensure classes exist
                    if (!_cdClasses.ContainsKey(rm.Groups[1].Value))
                        _cdClasses[rm.Groups[1].Value] = new CdClass { Name = rm.Groups[1].Value };
                    if (!_cdClasses.ContainsKey(rm.Groups[3].Value))
                        _cdClasses[rm.Groups[3].Value] = new CdClass { Name = rm.Groups[3].Value };
                    continue;
                }
            }
        }

        // ── Timeline parse ─────────────────────────────────────

        private void ParseTimeline(string[] lines)
        {
            TlSection? currentSection = null;
            int sectionIdx = 0;

            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%")) continue;

                if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                {
                    _tlTitle = line.Length > 5 ? line[5..].Trim().TrimStart(':').Trim() : "";
                    continue;
                }

                if (line.StartsWith("section", StringComparison.OrdinalIgnoreCase))
                {
                    var sectionTitle = line.Length > 7 ? line[7..].Trim() : "";
                    currentSection = new TlSection { Title = sectionTitle, ColorIndex = sectionIdx++ };
                    _tlSections.Add(currentSection);
                    continue;
                }

                // Event line: "label : event1 : event2" or just "label : event"
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    var timeLabel = line[..colonIdx].Trim();
                    var eventsStr = line[(colonIdx + 1)..];
                    var events = eventsStr.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    if (currentSection is null)
                    {
                        currentSection = new TlSection { Title = "", ColorIndex = sectionIdx++ };
                        _tlSections.Add(currentSection);
                    }

                    foreach (var evt in events)
                        currentSection.Events.Add(new TlEvent { TimeLabel = timeLabel, Text = evt });
                }
                else if (currentSection is not null)
                {
                    currentSection.Events.Add(new TlEvent { TimeLabel = "", Text = line });
                }
            }
        }

        // ── Quadrant chart parse ───────────────────────────────

        private static readonly Regex QdPointRx = new(
            @"^\s*(.+?):\s*\[([0-9.]+),\s*([0-9.]+)\]\s*$", RegexOptions.Compiled);

        private void ParseQuadrant(string[] lines)
        {
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%")) continue;

                if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                {
                    _qdTitle = line.Length > 5 ? line[5..].Trim().TrimStart(':').Trim() : "";
                    continue;
                }

                if (line.StartsWith("x-axis", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Length > 6 ? line[6..].Trim() : "";
                    var arrowIdx = val.IndexOf("-->", StringComparison.Ordinal);
                    if (arrowIdx >= 0)
                    {
                        _qdXLow = val[..arrowIdx].Trim();
                        _qdXHigh = val[(arrowIdx + 3)..].Trim();
                    }
                    else _qdXLow = val;
                    continue;
                }

                if (line.StartsWith("y-axis", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Length > 6 ? line[6..].Trim() : "";
                    var arrowIdx = val.IndexOf("-->", StringComparison.Ordinal);
                    if (arrowIdx >= 0)
                    {
                        _qdYLow = val[..arrowIdx].Trim();
                        _qdYHigh = val[(arrowIdx + 3)..].Trim();
                    }
                    else _qdYLow = val;
                    continue;
                }

                for (int q = 1; q <= 4; q++)
                {
                    var prefix = $"quadrant-{q}";
                    if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _qdLabels[q - 1] = line.Length > prefix.Length ? line[prefix.Length..].Trim() : "";
                        goto nextLine;
                    }
                }

                var pm = QdPointRx.Match(line);
                if (pm.Success)
                {
                    if (double.TryParse(pm.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) &&
                        double.TryParse(pm.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                    {
                        _qdPoints.Add(new QdPoint
                        {
                            Label = pm.Groups[1].Value.Trim(),
                            X = Math.Clamp(px, 0, 1),
                            Y = Math.Clamp(py, 0, 1)
                        });
                    }
                }

                nextLine:;
            }
        }

        // ── LAYOUT ─────────────────────────────────────────────

        private void ComputeLayout(double availW)
        {
            switch (_kind)
            {
                case DiagramKind.Flowchart: LayoutFlow(availW); break;
                case DiagramKind.Sequence: LayoutSeq(availW); break;
                case DiagramKind.State: LayoutState(); break;
                case DiagramKind.Er: LayoutEr(availW); break;
                case DiagramKind.Class: LayoutClass(availW); break;
                case DiagramKind.Timeline: LayoutTimeline(availW); break;
                case DiagramKind.Quadrant: LayoutQuadrant(availW); break;
            }
        }

        private void LayoutFlow(double availW)
        {
            if (_fNodes.Count == 0) { _layW = _layH = 0; return; }

            var maxNodeTextWidth = Math.Clamp(availW * 0.32, 120, 190);
            var lineHeight = Txt("Ag", Fs).Height;

            // Measure
            foreach (var n in _fNodes.Values)
            {
                n.Text = WrapNodeText(n.Text, Fs, maxNodeTextWidth);
                var lines = n.Text.Split('\n');
                var maxLineW = 0.0;
                foreach (var line in lines)
                {
                    var ft = Txt(line, Fs);
                    if (ft.Width > maxLineW)
                        maxLineW = ft.Width;
                }

                var textH = lines.Length * lineHeight + Math.Max(0, (lines.Length - 1) * 2);
                n.W = Math.Max(NMinW, maxLineW + NPadH * 2);
                n.H = n.Shape == NShape.Diamond
                    ? Math.Max(NH + 8, textH + NPadV * 2)
                    : Math.Max(NH, textH + NPadV * 2);
            }

            // Cycle-safe longest-path ranking (DFS finds back edges, then longest path on DAG)
            {
                var adjF = new Dictionary<string, List<string>>();
                foreach (var edge in _fEdges)
                {
                    if (!adjF.ContainsKey(edge.From)) adjF[edge.From] = new();
                    adjF[edge.From].Add(edge.To);
                }
                var backF = new HashSet<(string, string)>();
                var visitingF = new HashSet<string>();
                var doneF = new HashSet<string>();
                void DfsFlow(string id)
                {
                    if (doneF.Contains(id)) return;
                    visitingF.Add(id);
                    if (adjF.TryGetValue(id, out var nb))
                        foreach (var a in nb)
                        {
                            if (visitingF.Contains(a)) backF.Add((id, a));
                            else DfsFlow(a);
                        }
                    visitingF.Remove(id);
                    doneF.Add(id);
                }
                foreach (var nid in _fNodes.Keys) DfsFlow(nid);

                foreach (var nd in _fNodes.Values) nd.Rank = 0;
                bool chg = true;
                while (chg)
                {
                    chg = false;
                    foreach (var edge in _fEdges)
                    {
                        if (backF.Contains((edge.From, edge.To))) continue;
                        if (!_fNodes.TryGetValue(edge.From, out var sf) || !_fNodes.TryGetValue(edge.To, out var st)) continue;
                        if (st.Rank < sf.Rank + 1) { st.Rank = sf.Rank + 1; chg = true; }
                    }
                }
            }

            var ranks = _fNodes.Values.GroupBy(n => n.Rank).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

            var horizontalGap = _fNodes.Count > 10 ? 24 : NGapH;
            var rankWrapWidth = Math.Max(220, availW - Pad * 2);

            if (_ltr)
            {
                double x = 0;
                foreach (var rank in ranks)
                {
                    var maxW = rank.Max(n => n.W);
                    double y = 0;
                    for (int i = 0; i < rank.Count; i++)
                    {
                        rank[i].X = x + (maxW - rank[i].W) / 2;
                        rank[i].Y = y;
                        y += rank[i].H + NGapV * 0.6;
                    }
                    x += maxW + horizontalGap;
                }
            }
            else
            {
                double y = 0;
                foreach (var rank in ranks)
                {
                    var rows = BuildWrappedRows(rank, rankWrapWidth, horizontalGap);
                    var rowGap = Math.Max(10, NGapV * 0.35);
                    var rankBlockY = y;
                    foreach (var row in rows)
                    {
                        var rowW = row.Sum(n => n.W) + Math.Max(0, row.Count - 1) * horizontalGap;
                        var sx = Math.Max(0, (rankWrapWidth - rowW) / 2);
                        var rowH = row.Max(n => n.H);

                        for (int i = 0; i < row.Count; i++)
                        {
                            row[i].X = sx;
                            row[i].Y = rankBlockY;
                            sx += row[i].W + horizontalGap;
                        }

                        rankBlockY += rowH + rowGap;
                    }

                    if (rows.Count > 0)
                        rankBlockY -= rowGap;

                    y = rankBlockY + NGapV;
                }
            }

            _layW = _fNodes.Values.Max(n => n.X + n.W);
            _layH = _fNodes.Values.Max(n => n.Y + n.H);
        }

        private static List<List<FNode>> BuildWrappedRows(List<FNode> nodes, double maxWidth, double gap)
        {
            var rows = new List<List<FNode>>();
            var current = new List<FNode>();
            var currentWidth = 0.0;

            foreach (var node in nodes)
            {
                var nextWidth = current.Count == 0
                    ? node.W
                    : currentWidth + gap + node.W;

                if (current.Count > 0 && nextWidth > maxWidth)
                {
                    rows.Add(current);
                    current = new List<FNode> { node };
                    currentWidth = node.W;
                }
                else
                {
                    current.Add(node);
                    currentWidth = nextWidth;
                }
            }

            if (current.Count > 0)
                rows.Add(current);

            return rows;
        }

        private static string WrapNodeText(string text, double fontSize, double maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\n'))
                return text;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1)
                return text;

            var lines = new List<string>();
            var current = string.Empty;

            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(current)
                    ? word
                    : $"{current} {word}";

                if (Txt(candidate, fontSize).Width <= maxWidth || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (!string.IsNullOrEmpty(current))
                lines.Add(current);

            return string.Join("\n", lines);
        }

        private void LayoutSeq(double availW)
        {
            if (_sParts.Count == 0) { _layW = _layH = 0; return; }

            // Measure participant widths
            var widths = _sParts.Select(p => Math.Max(SeqMinCol, Txt(p.Label, Fs).Width + 32)).ToList();
            var totalW = widths.Sum() + (_sParts.Count - 1) * 20;

            // Spread evenly if space allows
            var useWidth = Math.Max(totalW, availW - Pad * 2);
            var colW = useWidth / _sParts.Count;

            for (int i = 0; i < _sParts.Count; i++)
                _sParts[i].X = i * colW + colW / 2;

            // Assign Y positions to messages
            double my = SeqBoxH + 24;
            foreach (var m in _sMsgs)
            {
                m.Y = my;
                my += SeqMsgGap;
            }

            _layW = _sParts.Count * colW;
            _layH = my + SeqBoxH + 8;
        }

        private void LayoutState()
        {
            if (_stNodes.Count == 0) { _layW = _layH = 0; return; }

            // Measure
            foreach (var n in _stNodes.Values)
            {
                if (n.IsStart || n.IsEnd) { n.W = 24; n.H = 24; }
                else
                {
                    var ft = Txt(n.Text, Fs);
                    n.W = Math.Max(NMinW, ft.Width + NPadH * 2);
                    n.H = NH;
                }
            }

            // Cycle-safe longest-path ranking
            {
                var adjS = new Dictionary<string, List<string>>();
                foreach (var edge in _stEdges)
                {
                    if (!adjS.ContainsKey(edge.From)) adjS[edge.From] = new();
                    adjS[edge.From].Add(edge.To);
                }
                var backS = new HashSet<(string, string)>();
                var visitingS = new HashSet<string>();
                var doneS = new HashSet<string>();
                void DfsState(string id)
                {
                    if (doneS.Contains(id)) return;
                    visitingS.Add(id);
                    if (adjS.TryGetValue(id, out var nb))
                        foreach (var a in nb)
                        {
                            if (visitingS.Contains(a)) backS.Add((id, a));
                            else DfsState(a);
                        }
                    visitingS.Remove(id);
                    doneS.Add(id);
                }
                foreach (var nid in _stNodes.Keys) DfsState(nid);

                foreach (var nd in _stNodes.Values) nd.Rank = 0;
                bool chg = true;
                while (chg)
                {
                    chg = false;
                    foreach (var edge in _stEdges)
                    {
                        if (backS.Contains((edge.From, edge.To))) continue;
                        if (!_stNodes.TryGetValue(edge.From, out var sf) || !_stNodes.TryGetValue(edge.To, out var st)) continue;
                        if (st.Rank < sf.Rank + 1) { st.Rank = sf.Rank + 1; chg = true; }
                    }
                }
            }

            var ranks = _stNodes.Values.GroupBy(n => n.Rank).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            var maxRankW = ranks.Max(r => r.Sum(n => n.W) + (r.Count - 1) * NGapH);

            double y = 0;
            foreach (var rank in ranks)
            {
                var rankW = rank.Sum(n => n.W) + (rank.Count - 1) * NGapH;
                var sx = (maxRankW - rankW) / 2;
                for (int i = 0; i < rank.Count; i++)
                {
                    rank[i].X = sx;
                    rank[i].Y = y;
                    sx += rank[i].W + NGapH;
                }
                y += rank.Max(n => n.H) + NGapV;
            }

            _layW = _stNodes.Values.Max(n => n.X + n.W);
            _layH = _stNodes.Values.Max(n => n.Y + n.H);

            // Add right margin for back-edge loop curves
            if (_stEdges.Any(e =>
                _stNodes.TryGetValue(e.From, out var ef) &&
                _stNodes.TryGetValue(e.To, out var et) && ef.Rank >= et.Rank))
                _layW += 60;
        }

        private void LayoutEr(double availW)
        {
            if (_erEntities.Count == 0) { _layW = _layH = 0; return; }

            const double erFieldH = 22;
            const double erHeaderH = 32;
            const double erPadH = 16;
            const double erGapH = 60;
            const double erGapV = 48;

            // Measure each entity
            foreach (var ent in _erEntities.Values)
            {
                double maxFieldW = 0;
                foreach (var f in ent.Fields)
                {
                    var fieldText = $"{f.Type} {f.Name}";
                    if (f.Constraint is not null) fieldText += $" {f.Constraint}";
                    var ft = Txt(fieldText, FsSmall);
                    maxFieldW = Math.Max(maxFieldW, ft.Width);
                }
                var headerFt = Txt(ent.Name, Fs);
                ent.W = Math.Max(headerFt.Width + erPadH * 2, maxFieldW + erPadH * 2 + 16);
                ent.W = Math.Max(ent.W, 120);
                ent.H = erHeaderH + ent.Fields.Count * erFieldH + 8;
            }

            // Layout in a grid — arrange entities in rows that fit availW
            var entities = _erEntities.Values.ToList();
            double x = 0, y = 0, rowH = 0;
            var maxW = availW - Pad * 2;
            if (maxW < 200) maxW = 600;

            foreach (var ent in entities)
            {
                if (x > 0 && x + ent.W > maxW)
                {
                    x = 0;
                    y += rowH + erGapV;
                    rowH = 0;
                }
                ent.X = x;
                ent.Y = y;
                x += ent.W + erGapH;
                rowH = Math.Max(rowH, ent.H);
            }

            _layW = entities.Max(e => e.X + e.W);
            _layH = entities.Max(e => e.Y + e.H);
        }

        private void LayoutClass(double availW)
        {
            if (_cdClasses.Count == 0) { _layW = _layH = 0; return; }

            const double cdFieldH = 20;
            const double cdHeaderH = 30;
            const double cdPadH = 14;
            const double cdGapH = 60;
            const double cdGapV = 48;
            const double cdDivH = 1;

            foreach (var cls in _cdClasses.Values)
            {
                double maxW = Txt(cls.Name, Fs).Width + cdPadH * 2;
                foreach (var attr in cls.Attributes)
                    maxW = Math.Max(maxW, Txt(attr, FsSmall).Width + cdPadH * 2);
                foreach (var meth in cls.Methods)
                    maxW = Math.Max(maxW, Txt(meth, FsSmall).Width + cdPadH * 2);
                cls.W = Math.Max(maxW, 100);

                var sections = 1; // header
                if (cls.Attributes.Count > 0 || cls.Methods.Count > 0) sections++;
                if (cls.Attributes.Count > 0 && cls.Methods.Count > 0) sections++;
                cls.H = cdHeaderH
                       + cls.Attributes.Count * cdFieldH
                       + cls.Methods.Count * cdFieldH
                       + (sections - 1) * cdDivH + 8;
            }

            // Grid layout
            var classes = _cdClasses.Values.ToList();
            double x = 0, y = 0, rowH = 0;
            var maxLayW = Math.Max(availW - Pad * 2, 400);

            foreach (var cls in classes)
            {
                if (x > 0 && x + cls.W > maxLayW)
                {
                    x = 0;
                    y += rowH + cdGapV;
                    rowH = 0;
                }
                cls.X = x;
                cls.Y = y;
                x += cls.W + cdGapH;
                rowH = Math.Max(rowH, cls.H);
            }

            _layW = classes.Max(c => c.X + c.W);
            _layH = classes.Max(c => c.Y + c.H);
        }

        private void LayoutTimeline(double availW)
        {
            if (_tlSections.Count == 0) { _layW = _layH = 0; return; }

            const double tlEventH = 28;
            const double tlEventGap = 6;
            const double tlSectionGap = 16;
            const double tlRailY = 60;

            double titleH = _tlTitle.Length > 0 ? 36 : 0;

            // Measure each section width and event positions
            double x = 0;
            foreach (var sec in _tlSections)
            {
                // Measure max event width for this section
                double secW = sec.Title.Length > 0 ? Txt(sec.Title, Fs).Width + 32 : 80;
                foreach (var ev in sec.Events)
                {
                    var labelW = ev.TimeLabel.Length > 0 ? Txt(ev.TimeLabel, 11).Width + 8 : 0;
                    var textW = Txt(ev.Text, FsSmall).Width;
                    secW = Math.Max(secW, labelW + textW + 32);
                }
                secW = Math.Max(secW, 110);

                sec.X = x;
                sec.W = secW;

                // Position events below rail
                double ey = titleH + tlRailY + 24;
                foreach (var ev in sec.Events)
                {
                    ev.X = x + secW / 2;
                    ev.Y = ey;
                    ey += tlEventH + tlEventGap;
                }

                x += secW + tlSectionGap;
            }

            _layW = Math.Max(x - tlSectionGap, 100);
            var maxEvY = _tlSections.SelectMany(s => s.Events).Select(e => e.Y + tlEventH).DefaultIfEmpty(0).Max();
            _layH = Math.Max(maxEvY + 12, titleH + tlRailY + 80);
        }

        private void LayoutQuadrant(double availW)
        {
            const double qdSize = 380;
            const double qdMarginLeft = 80;
            const double qdMarginRight = 20;
            const double qdMarginBottom = 48;
            var qdMarginTop = _qdTitle.Length > 0 ? 40.0 : 12.0;

            _layW = qdSize + qdMarginLeft + qdMarginRight;
            _layH = qdSize + qdMarginBottom + qdMarginTop;
        }

        // ── RENDER ─────────────────────────────────────────────

        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            if (b.Width < 20 || b.Height < 20) return;
            if (_layW < 1 && _layH < 1) return;

            // Entrance animation: opacity fade-in
            DrawingContext.PushedState? opState = _animProgress < 1.0
                ? ctx.PushOpacity(_animProgress)
                : null;

            // Animation scale: subtle grow from 0.96 to 1.0
            var animScale = _animating || _animProgress < 1.0
                ? 0.96 + 0.04 * _animProgress
                : 1.0;

            var effScale = _baseScale * _userZoom * animScale;

            var ox = (b.Width - _layW * effScale) / 2 + _panX;
            var oy = Pad * effScale + _panY;

            // Animation: slide up slightly
            if (_animating || _animProgress < 1.0)
                oy += 8 * (1.0 - _animProgress);

            using (ctx.PushTransform(Matrix.CreateScale(effScale, effScale) * Matrix.CreateTranslation(ox, oy)))
            {
                switch (_kind)
                {
                    case DiagramKind.Flowchart: RenderFlow(ctx); break;
                    case DiagramKind.Sequence: RenderSeq(ctx); break;
                    case DiagramKind.State: RenderState(ctx); break;
                    case DiagramKind.Er: RenderEr(ctx); break;
                    case DiagramKind.Class: RenderClass(ctx); break;
                    case DiagramKind.Timeline: RenderTimeline(ctx); break;
                    case DiagramKind.Quadrant: RenderQuadrant(ctx); break;
                }
            }

            opState?.Dispose();
        }

        // ── Flowchart render ───────────────────────────────────

        private void RenderFlow(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var text = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var lineBr = _owner.ResolveBrush("Brush.TextTertiary", Color.Parse("#777"));
            var labelBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var accent = _owner.ResolveBrush("Brush.AccentDefault", Color.Parse("#818CF8"));
            var pillBg = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));

            var borderPen = new Pen(border, 1.4);
            var accentPen = new Pen(accent, 1.6);
            var linePen = new Pen(lineBr, 1.2);
            var dottedPen = new Pen(lineBr, 1.2) { DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0) };
            var thickPen = new Pen(lineBr, 2.4);

            // Draw edges first (behind nodes)
            foreach (var e in _fEdges)
            {
                if (!_fNodes.TryGetValue(e.From, out var fn) || !_fNodes.TryGetValue(e.To, out var tn)) continue;

                var from = NodePort(fn, true, !_ltr);
                var to = NodePort(tn, false, !_ltr);

                var pen = e.Style switch
                {
                    EStyle.Dotted => dottedPen,
                    EStyle.Thick => thickPen,
                    _ => linePen
                };
                var hasArrow = e.Style != EStyle.Line;

                DrawBezierEdge(ctx, from, to, pen, hasArrow, !_ltr, lineBr);

                if (!string.IsNullOrWhiteSpace(e.Label))
                {
                    var midX = (from.X + to.X) / 2;
                    var midY = (from.Y + to.Y) / 2;
                    var ft = Txt(e.Label, FsSmall, labelBr);
                    var pillR = new Rect(midX - ft.Width / 2 - 6, midY - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 8, 8);
                    ctx.DrawText(ft, new Point(midX - ft.Width / 2, midY - ft.Height / 2));
                }
            }

            // Identify root nodes for accent treatment
            var hasIncoming = new HashSet<string>(_fEdges.Select(e => e.To));

            // Draw nodes
            foreach (var n in _fNodes.Values)
            {
                var rect = new Rect(n.X, n.Y, n.W, n.H);
                var isRoot = !hasIncoming.Contains(n.Id);
                var nodePen = isRoot ? accentPen : borderPen;

                switch (n.Shape)
                {
                    case NShape.Diamond:
                        DrawDiamond(ctx, rect, fill, nodePen);
                        break;
                    case NShape.Circle:
                        var r = Math.Min(n.W, n.H) / 2;
                        ctx.DrawEllipse(fill, nodePen, rect.Center, r, r);
                        break;
                    case NShape.Rounded:
                    case NShape.Stadium:
                        ctx.DrawRectangle(fill, nodePen, rect, n.H / 2, n.H / 2);
                        break;
                    default: // Rect
                        ctx.DrawRectangle(fill, nodePen, rect, 6, 6);
                        break;
                }

                // Accent left bar on root nodes
                if (isRoot && n.Shape == NShape.Rect)
                {
                    var barRect = new Rect(n.X + 1, n.Y + 6, 2.5, n.H - 12);
                    ctx.DrawRectangle(accent, null, barRect, 1, 1);
                }

                var ft = Txt(n.Text, Fs, text);
                ctx.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
            }
        }

        // ── Sequence render ────────────────────────────────────

        private void RenderSeq(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var textBr = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var lineBr = _owner.ResolveBrush("Brush.TextTertiary", Color.Parse("#777"));
            var labelBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var accent = _owner.ResolveBrush("Brush.AccentDefault", Color.Parse("#818CF8"));
            var subtle = _owner.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));

            var borderPen = new Pen(border, 1.4);
            var lifePen = new Pen(subtle, 1) { DashStyle = new DashStyle(new[] { 6.0, 4.0 }, 0) };
            var linePen = new Pen(lineBr, 1.4);
            var dashPen = new Pen(lineBr, 1.4) { DashStyle = new DashStyle(new[] { 6.0, 4.0 }, 0) };

            var bottomY = _layH;

            // Lifelines (behind everything)
            foreach (var p in _sParts)
                ctx.DrawLine(lifePen, new Point(p.X, SeqBoxH), new Point(p.X, bottomY - SeqBoxH));

            // Messages
            foreach (var m in _sMsgs)
            {
                var fp = _sParts.FirstOrDefault(p => p.Id == m.From);
                var tp = _sParts.FirstOrDefault(p => p.Id == m.To);
                if (fp is null || tp is null) continue;

                var fromX = fp.X;
                var toX = tp.X;
                var pen = m.Dashed ? dashPen : linePen;

                if (fp.Id == tp.Id)
                {
                    // Self-message: small loop
                    var loopW = 30.0;
                    var loopH = 16.0;
                    ctx.DrawLine(pen, new Point(fromX, m.Y), new Point(fromX + loopW, m.Y));
                    ctx.DrawLine(pen, new Point(fromX + loopW, m.Y), new Point(fromX + loopW, m.Y + loopH));
                    ctx.DrawLine(pen, new Point(fromX + loopW, m.Y + loopH), new Point(fromX, m.Y + loopH));
                    DrawArrowHead(ctx, new Point(fromX, m.Y + loopH), Math.PI, lineBr);

                    var ft = Txt(m.Text, FsSmall, labelBr);
                    ctx.DrawText(ft, new Point(fromX + loopW + 6, m.Y - ft.Height / 2 + 4));
                }
                else
                {
                    ctx.DrawLine(pen, new Point(fromX, m.Y), new Point(toX, m.Y));
                    var angle = toX > fromX ? 0 : Math.PI;
                    DrawArrowHead(ctx, new Point(toX, m.Y), angle, lineBr);

                    var ft = Txt(m.Text, FsSmall, labelBr);
                    var midX = Math.Min(fromX, toX) + Math.Abs(toX - fromX) / 2;
                    ctx.DrawText(ft, new Point(midX - ft.Width / 2, m.Y - ft.Height - 4));
                }
            }

            // Participant boxes (top and bottom)
            foreach (var p in _sParts)
            {
                var ft = Txt(p.Label, Fs, textBr, FontWeight.SemiBold);
                var bw = Math.Max(80, ft.Width + 24);

                // Top box
                var topR = new Rect(p.X - bw / 2, 0, bw, SeqBoxH);
                ctx.DrawRectangle(fill, borderPen, topR, 6, 6);

                // Accent top stripe
                using (ctx.PushClip(new RoundedRect(topR, 6)))
                    ctx.DrawRectangle(accent, null, new Rect(topR.X + 1, topR.Y + 1, topR.Width - 2, 2.5));

                ctx.DrawText(ft, new Point(p.X - ft.Width / 2, SeqBoxH / 2 - ft.Height / 2));

                // Bottom box
                var botR = new Rect(p.X - bw / 2, bottomY - SeqBoxH, bw, SeqBoxH);
                ctx.DrawRectangle(fill, borderPen, botR, 6, 6);
                ctx.DrawText(Txt(p.Label, Fs, textBr, FontWeight.SemiBold),
                    new Point(p.X - ft.Width / 2, bottomY - SeqBoxH + SeqBoxH / 2 - ft.Height / 2));
            }
        }

        // ── State render ───────────────────────────────────────

        private void RenderState(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var textBr = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var lineBr = _owner.ResolveBrush("Brush.TextTertiary", Color.Parse("#777"));
            var labelBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var pillBg = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));

            var borderPen = new Pen(border, 1.4);
            var linePen = new Pen(lineBr, 1.2);
            var dotPen = new Pen(fill, 2);

            // Draw edges (with back-edge loop routing for cycles)
            foreach (var e in _stEdges)
            {
                if (!_stNodes.TryGetValue(e.From, out var fn) || !_stNodes.TryGetValue(e.To, out var tn)) continue;

                var isBack = fn.Rank >= tn.Rank;
                Point from, to;

                if (isBack)
                {
                    // Back edge: exit right side of source, loop right, enter right side of target
                    from = new Point(fn.X + fn.W, fn.Y + fn.H / 2);
                    to = new Point(tn.X + tn.W, tn.Y + tn.H / 2);
                    DrawBackEdge(ctx, from, to, linePen, lineBr, _layW);
                }
                else
                {
                    // Forward edge: bottom of source to top of target
                    from = new Point(fn.X + fn.W / 2, fn.Y + fn.H);
                    to = new Point(tn.X + tn.W / 2, tn.Y);
                    DrawBezierEdge(ctx, from, to, linePen, true, true, lineBr);
                }

                if (!string.IsNullOrWhiteSpace(e.Label))
                {
                    double midX, midY;
                    if (isBack)
                    {
                        midX = _layW - 30;
                        midY = (from.Y + to.Y) / 2;
                    }
                    else
                    {
                        midX = (from.X + to.X) / 2;
                        midY = (from.Y + to.Y) / 2;
                    }
                    var ft = Txt(e.Label, FsSmall, labelBr);
                    var pillR = new Rect(midX - ft.Width / 2 - 6, midY - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 8, 8);
                    ctx.DrawText(ft, new Point(midX - ft.Width / 2, midY - ft.Height / 2));
                }
            }

            // Draw nodes
            foreach (var n in _stNodes.Values)
            {
                if (n.IsStart)
                {
                    // Filled circle
                    var cx = n.X + n.W / 2;
                    var cy = n.Y + n.H / 2;
                    ctx.DrawEllipse(textBr, null, new Point(cx, cy), 10, 10);
                }
                else if (n.IsEnd)
                {
                    // Double circle
                    var cx = n.X + n.W / 2;
                    var cy = n.Y + n.H / 2;
                    ctx.DrawEllipse(null, new Pen(textBr, 2), new Point(cx, cy), 10, 10);
                    ctx.DrawEllipse(textBr, null, new Point(cx, cy), 6, 6);
                }
                else
                {
                    // Rounded rectangle
                    var rect = new Rect(n.X, n.Y, n.W, n.H);
                    ctx.DrawRectangle(fill, borderPen, rect, n.H / 2, n.H / 2);

                    var ft = Txt(n.Text, Fs, textBr);
                    ctx.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
                }
            }
        }

        // ── ER render ──────────────────────────────────────────

        private void RenderEr(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var headerBg = _owner.ResolveBrush("Brush.Surface2", Color.Parse("#2D2D2D"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var textBr = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var textSec = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var lineBr = _owner.ResolveBrush("Brush.TextTertiary", Color.Parse("#777"));
            var accent = _owner.ResolveBrush("Brush.AccentDefault", Color.Parse("#818CF8"));
            var labelBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var pillBg = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));
            var constraintBr = _owner.ResolveBrush("Brush.AccentDefault", Color.Parse("#818CF8"));
            var divider = _owner.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));

            var borderPen = new Pen(border, 1.4);
            var linePen = new Pen(lineBr, 1.2);
            var divPen = new Pen(divider, 0.8);

            const double erFieldH = 22;
            const double erHeaderH = 32;
            const double erPadH = 12;

            // Draw relationships first (behind entities)
            foreach (var rel in _erRelations)
            {
                if (!_erEntities.TryGetValue(rel.From, out var fromEnt) ||
                    !_erEntities.TryGetValue(rel.To, out var toEnt)) continue;

                var fromCenter = new Point(fromEnt.X + fromEnt.W / 2, fromEnt.Y + fromEnt.H / 2);
                var toCenter = new Point(toEnt.X + toEnt.W / 2, toEnt.Y + toEnt.H / 2);

                // Find nearest edge points
                var fromPt = NearestEdgePoint(fromEnt, toCenter);
                var toPt = NearestEdgePoint(toEnt, fromCenter);

                DrawBezierEdge(ctx, fromPt, toPt, linePen, false, false, lineBr);

                // Draw cardinality symbols
                DrawCardinality(ctx, fromPt, toPt, rel.LeftCard, lineBr, textSec);
                DrawCardinality(ctx, toPt, fromPt, rel.RightCard, lineBr, textSec);

                // Label in the middle
                if (!string.IsNullOrWhiteSpace(rel.Label))
                {
                    var midX = (fromPt.X + toPt.X) / 2;
                    var midY = (fromPt.Y + toPt.Y) / 2;
                    var ft = Txt(rel.Label, FsSmall, labelBr);
                    var pillR = new Rect(midX - ft.Width / 2 - 6, midY - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 8, 8);
                    ctx.DrawText(ft, new Point(midX - ft.Width / 2, midY - ft.Height / 2));
                }
            }

            // Draw entities
            foreach (var ent in _erEntities.Values)
            {
                var rect = new Rect(ent.X, ent.Y, ent.W, ent.H);

                // Body
                ctx.DrawRectangle(fill, borderPen, rect, 8, 8);

                // Header background
                var headerRect = new Rect(ent.X, ent.Y, ent.W, erHeaderH);
                using (ctx.PushClip(new RoundedRect(rect, 8)))
                    ctx.DrawRectangle(headerBg, null, headerRect);

                // Accent top stripe
                using (ctx.PushClip(new RoundedRect(rect, 8)))
                    ctx.DrawRectangle(accent, null, new Rect(ent.X + 1, ent.Y + 1, ent.W - 2, 2.5));

                // Header text
                var headerFt = Txt(ent.Name, Fs, textBr, FontWeight.SemiBold);
                ctx.DrawText(headerFt, new Point(ent.X + erPadH, ent.Y + erHeaderH / 2 - headerFt.Height / 2));

                // Divider line
                ctx.DrawLine(divPen, new Point(ent.X + 1, ent.Y + erHeaderH), new Point(ent.X + ent.W - 1, ent.Y + erHeaderH));

                // Fields
                double fy = ent.Y + erHeaderH + 4;
                foreach (var field in ent.Fields)
                {
                    // Constraint badge
                    if (field.Constraint is not null)
                    {
                        var cft = Txt(field.Constraint, 9, constraintBr, FontWeight.SemiBold);
                        ctx.DrawText(cft, new Point(ent.X + erPadH, fy + erFieldH / 2 - cft.Height / 2));
                    }

                    // Type
                    var typeOffset = field.Constraint is not null ? 30.0 : 0;
                    var typeFt = Txt(field.Type, FsSmall, textSec);
                    ctx.DrawText(typeFt, new Point(ent.X + erPadH + typeOffset, fy + erFieldH / 2 - typeFt.Height / 2));

                    // Name
                    var nameFt = Txt(field.Name, FsSmall, textBr);
                    ctx.DrawText(nameFt, new Point(ent.X + erPadH + typeOffset + typeFt.Width + 6, fy + erFieldH / 2 - nameFt.Height / 2));

                    fy += erFieldH;
                }
            }
        }

        private static Point NearestEdgePoint(ErEntity ent, Point target)
        {
            var cx = ent.X + ent.W / 2;
            var cy = ent.Y + ent.H / 2;
            var dx = target.X - cx;
            var dy = target.Y - cy;

            if (Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1)
                return new Point(cx, ent.Y + ent.H);

            // Try each edge and pick the one closest to target direction
            var hw = ent.W / 2;
            var hh = ent.H / 2;

            // Intersect with each side
            Point best = new(cx, ent.Y); // top
            double bestDist = double.MaxValue;

            // Top
            if (dy < 0)
            {
                var ix = cx + dx * (-hh / dy);
                if (ix >= ent.X && ix <= ent.X + ent.W)
                {
                    var p = new Point(ix, ent.Y);
                    var d = Dist(p, target);
                    if (d < bestDist) { best = p; bestDist = d; }
                }
            }
            // Bottom
            if (dy > 0)
            {
                var ix = cx + dx * (hh / dy);
                if (ix >= ent.X && ix <= ent.X + ent.W)
                {
                    var p = new Point(ix, ent.Y + ent.H);
                    var d = Dist(p, target);
                    if (d < bestDist) { best = p; bestDist = d; }
                }
            }
            // Left
            if (dx < 0)
            {
                var iy = cy + dy * (-hw / dx);
                if (iy >= ent.Y && iy <= ent.Y + ent.H)
                {
                    var p = new Point(ent.X, iy);
                    var d = Dist(p, target);
                    if (d < bestDist) { best = p; bestDist = d; }
                }
            }
            // Right
            if (dx > 0)
            {
                var iy = cy + dy * (hw / dx);
                if (iy >= ent.Y && iy <= ent.Y + ent.H)
                {
                    var p = new Point(ent.X + ent.W, iy);
                    var d = Dist(p, target);
                    if (d < bestDist) { best = p; bestDist = d; }
                }
            }

            return best;
        }

        private static double Dist(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void DrawCardinality(DrawingContext ctx, Point near, Point far,
            string card, IBrush lineBrush, IBrush textBrush)
        {
            // card is 2 chars like "||" "|{" "o{" "o|" etc.
            // Draw at the 'near' end, pointing toward 'far'
            var angle = Math.Atan2(far.Y - near.Y, far.X - near.X);
            var perpX = -Math.Sin(angle);
            var perpY = Math.Cos(angle);
            var pen = new Pen(lineBrush, 1.4);

            const double off = 12; // distance from endpoint
            const double spread = 6; // how wide the symbols spread

            var bx = near.X + Math.Cos(angle) * off;
            var by = near.Y + Math.Sin(angle) * off;

            foreach (var ch in card)
            {
                switch (ch)
                {
                    case '|':
                        // Perpendicular line
                        ctx.DrawLine(pen,
                            new Point(bx + perpX * spread, by + perpY * spread),
                            new Point(bx - perpX * spread, by - perpY * spread));
                        break;
                    case 'o':
                        // Small circle
                        ctx.DrawEllipse(null, pen, new Point(bx, by), 4, 4);
                        break;
                    case '{':
                    case '}':
                        // Crow's foot (many)
                        var tipX = bx + Math.Cos(angle) * 6;
                        var tipY = by + Math.Sin(angle) * 6;
                        ctx.DrawLine(pen,
                            new Point(tipX, tipY),
                            new Point(bx + perpX * spread, by + perpY * spread));
                        ctx.DrawLine(pen,
                            new Point(tipX, tipY),
                            new Point(bx - perpX * spread, by - perpY * spread));
                        break;
                }
                // Advance along the line
                bx += Math.Cos(angle) * 8;
                by += Math.Sin(angle) * 8;
            }
        }

        // ── Class diagram render ───────────────────────────────

        private void RenderClass(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var headerBg = _owner.ResolveBrush("Brush.Surface2", Color.Parse("#2D2D2D"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var textBr = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var textSec = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var lineBr = _owner.ResolveBrush("Brush.TextTertiary", Color.Parse("#777"));
            var accent = _owner.ResolveBrush("Brush.AccentDefault", Color.Parse("#818CF8"));
            var divider = _owner.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));
            var labelBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var pillBg = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));

            var borderPen = new Pen(border, 1.4);
            var linePen = new Pen(lineBr, 1.2);
            var dashedPen = new Pen(lineBr, 1.2) { DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0) };
            var divPen = new Pen(divider, 0.8);

            const double cdFieldH = 20;
            const double cdHeaderH = 30;
            const double cdPadH = 10;

            // Draw relations first (behind classes)
            foreach (var rel in _cdRelations)
            {
                if (!_cdClasses.TryGetValue(rel.From, out var fromCls) ||
                    !_cdClasses.TryGetValue(rel.To, out var toCls)) continue;

                var fromPt = CdEdgePoint(fromCls, new Point(toCls.X + toCls.W / 2, toCls.Y + toCls.H / 2));
                var toPt = CdEdgePoint(toCls, new Point(fromCls.X + fromCls.W / 2, fromCls.Y + fromCls.H / 2));

                var pen = rel.Type is CdRelType.Dependency or CdRelType.Realization ? dashedPen : linePen;
                var hasArrow = rel.Type is CdRelType.Association or CdRelType.Dependency;

                DrawBezierEdge(ctx, fromPt, toPt, pen, hasArrow, false, lineBr);

                // Draw relationship markers
                DrawCdRelMarker(ctx, toPt, fromPt, rel.Type, lineBr, fill);

                if (!string.IsNullOrWhiteSpace(rel.Label))
                {
                    var midX = (fromPt.X + toPt.X) / 2;
                    var midY = (fromPt.Y + toPt.Y) / 2;
                    var ft = Txt(rel.Label, FsSmall, labelBr);
                    var pillR = new Rect(midX - ft.Width / 2 - 6, midY - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 8, 8);
                    ctx.DrawText(ft, new Point(midX - ft.Width / 2, midY - ft.Height / 2));
                }
            }

            // Draw classes
            foreach (var cls in _cdClasses.Values)
            {
                var rect = new Rect(cls.X, cls.Y, cls.W, cls.H);
                ctx.DrawRectangle(fill, borderPen, rect, 6, 6);

                // Header
                var headerRect = new Rect(cls.X, cls.Y, cls.W, cdHeaderH);
                using (ctx.PushClip(new RoundedRect(rect, 6)))
                    ctx.DrawRectangle(headerBg, null, headerRect);

                // Accent top stripe
                using (ctx.PushClip(new RoundedRect(rect, 6)))
                    ctx.DrawRectangle(accent, null, new Rect(cls.X + 1, cls.Y + 1, cls.W - 2, 2.5));

                // Class name
                var nameFt = Txt(cls.Name, Fs, textBr, FontWeight.SemiBold);
                ctx.DrawText(nameFt, new Point(cls.X + cls.W / 2 - nameFt.Width / 2, cls.Y + cdHeaderH / 2 - nameFt.Height / 2));

                double fy = cls.Y + cdHeaderH;

                // Attributes section
                if (cls.Attributes.Count > 0)
                {
                    ctx.DrawLine(divPen, new Point(cls.X + 1, fy), new Point(cls.X + cls.W - 1, fy));
                    fy += 4;
                    foreach (var attr in cls.Attributes)
                    {
                        var ft = Txt(attr, FsSmall, textSec);
                        ctx.DrawText(ft, new Point(cls.X + cdPadH, fy + cdFieldH / 2 - ft.Height / 2));
                        fy += cdFieldH;
                    }
                }

                // Methods section
                if (cls.Methods.Count > 0)
                {
                    ctx.DrawLine(divPen, new Point(cls.X + 1, fy), new Point(cls.X + cls.W - 1, fy));
                    fy += 4;
                    foreach (var meth in cls.Methods)
                    {
                        var ft = Txt(meth, FsSmall, textBr);
                        ctx.DrawText(ft, new Point(cls.X + cdPadH, fy + cdFieldH / 2 - ft.Height / 2));
                        fy += cdFieldH;
                    }
                }
            }
        }

        private static Point CdEdgePoint(CdClass cls, Point target)
        {
            var cx = cls.X + cls.W / 2;
            var cy = cls.Y + cls.H / 2;
            var dx = target.X - cx;
            var dy = target.Y - cy;
            if (Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1) return new Point(cx, cls.Y + cls.H);

            var hw = cls.W / 2;
            var hh = cls.H / 2;
            Point best = new(cx, cls.Y);
            double bestDist = double.MaxValue;

            if (dy < 0) { var ix = cx + dx * (-hh / dy); if (ix >= cls.X && ix <= cls.X + cls.W) { var p = new Point(ix, cls.Y); var d = Dist(p, target); if (d < bestDist) { best = p; bestDist = d; } } }
            if (dy > 0) { var ix = cx + dx * (hh / dy); if (ix >= cls.X && ix <= cls.X + cls.W) { var p = new Point(ix, cls.Y + cls.H); var d = Dist(p, target); if (d < bestDist) { best = p; bestDist = d; } } }
            if (dx < 0) { var iy = cy + dy * (-hw / dx); if (iy >= cls.Y && iy <= cls.Y + cls.H) { var p = new Point(cls.X, iy); var d = Dist(p, target); if (d < bestDist) { best = p; bestDist = d; } } }
            if (dx > 0) { var iy = cy + dy * (hw / dx); if (iy >= cls.Y && iy <= cls.Y + cls.H) { var p = new Point(cls.X + cls.W, iy); var d = Dist(p, target); if (d < bestDist) { best = p; bestDist = d; } } }

            return best;
        }

        private static void DrawCdRelMarker(DrawingContext ctx, Point at, Point from, CdRelType type,
            IBrush lineBrush, IBrush fillBrush)
        {
            var angle = Math.Atan2(from.Y - at.Y, from.X - at.X);
            var pen = new Pen(lineBrush, 1.4);

            switch (type)
            {
                case CdRelType.Inheritance:
                case CdRelType.Realization:
                {
                    // Open triangle (hollow arrowhead)
                    var p1 = new Point(at.X + 10 * Math.Cos(angle - 0.4), at.Y + 10 * Math.Sin(angle - 0.4));
                    var p2 = new Point(at.X + 10 * Math.Cos(angle + 0.4), at.Y + 10 * Math.Sin(angle + 0.4));
                    var geo = new StreamGeometry();
                    using (var gc = geo.Open()) { gc.BeginFigure(at, true); gc.LineTo(p1); gc.LineTo(p2); gc.EndFigure(true); }
                    ctx.DrawGeometry(fillBrush, pen, geo);
                    break;
                }
                case CdRelType.Composition:
                {
                    // Filled diamond
                    var p1 = new Point(at.X + 8 * Math.Cos(angle - 0.45), at.Y + 8 * Math.Sin(angle - 0.45));
                    var p2 = new Point(at.X + 14 * Math.Cos(angle), at.Y + 14 * Math.Sin(angle));
                    var p3 = new Point(at.X + 8 * Math.Cos(angle + 0.45), at.Y + 8 * Math.Sin(angle + 0.45));
                    var geo = new StreamGeometry();
                    using (var gc = geo.Open()) { gc.BeginFigure(at, true); gc.LineTo(p1); gc.LineTo(p2); gc.LineTo(p3); gc.EndFigure(true); }
                    ctx.DrawGeometry(lineBrush, null, geo);
                    break;
                }
                case CdRelType.Aggregation:
                {
                    // Hollow diamond
                    var p1 = new Point(at.X + 8 * Math.Cos(angle - 0.45), at.Y + 8 * Math.Sin(angle - 0.45));
                    var p2 = new Point(at.X + 14 * Math.Cos(angle), at.Y + 14 * Math.Sin(angle));
                    var p3 = new Point(at.X + 8 * Math.Cos(angle + 0.45), at.Y + 8 * Math.Sin(angle + 0.45));
                    var geo = new StreamGeometry();
                    using (var gc = geo.Open()) { gc.BeginFigure(at, true); gc.LineTo(p1); gc.LineTo(p2); gc.LineTo(p3); gc.EndFigure(true); }
                    ctx.DrawGeometry(fillBrush, pen, geo);
                    break;
                }
                default:
                    DrawArrowHead(ctx, at, angle + Math.PI, lineBrush);
                    break;
            }
        }

        // ── Timeline render ────────────────────────────────────

        private static readonly string[] ChartColorKeys =
            { "Brush.Chart1", "Brush.Chart2", "Brush.Chart3", "Brush.Chart4", "Brush.Chart5", "Brush.Chart6" };
        private static readonly Color[] ChartColorFallbacks =
            { Color.Parse("#818CF8"), Color.Parse("#A78BFA"), Color.Parse("#C084FC"), Color.Parse("#F472B6"), Color.Parse("#38BDF8"), Color.Parse("#34D399") };

        private void RenderTimeline(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var surface0 = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var textBr = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var textSec = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var subtle = _owner.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));

            // Resolve chart palette for sections
            var sectionBrushes = new IBrush[ChartColorKeys.Length];
            for (int i = 0; i < ChartColorKeys.Length; i++)
                sectionBrushes[i] = _owner.ResolveBrush(ChartColorKeys[i], ChartColorFallbacks[i]);

            var borderPen = new Pen(border, 1.0);
            var railPen = new Pen(subtle, 1.6);

            const double tlEventH = 28;
            const double tlRailY = 60;
            double titleH = _tlTitle.Length > 0 ? 36 : 0;

            // Title
            if (_tlTitle.Length > 0)
            {
                var ft = Txt(_tlTitle, 14, textBr, FontWeight.SemiBold);
                ctx.DrawText(ft, new Point(_layW / 2 - ft.Width / 2, 2));
            }

            // Horizontal rail line spanning all sections
            var railBaseline = titleH + tlRailY;
            if (_tlSections.Count > 0)
            {
                var firstSec = _tlSections.First();
                var lastSec = _tlSections.Last();
                var railX1 = firstSec.X + firstSec.W / 2;
                var railX2 = lastSec.X + lastSec.W / 2;
                ctx.DrawLine(railPen, new Point(railX1, railBaseline), new Point(railX2, railBaseline));
            }

            foreach (var sec in _tlSections)
            {
                var secCx = sec.X + sec.W / 2;
                var secColor = sectionBrushes[sec.ColorIndex % sectionBrushes.Length];
                var secColorPen = new Pen(secColor, 1.4);

                // Section header badge
                if (sec.Title.Length > 0)
                {
                    var hft = Txt(sec.Title, 11, textBr, FontWeight.SemiBold);
                    var badgeW = hft.Width + 20;
                    var badgeH = 24.0;
                    var badgeRect = new Rect(secCx - badgeW / 2, titleH + 12, badgeW, badgeH);
                    ctx.DrawRectangle(fill, secColorPen, badgeRect, badgeH / 2, badgeH / 2);

                    // Accent left bar inside badge
                    using (ctx.PushClip(new RoundedRect(badgeRect, badgeH / 2)))
                        ctx.DrawRectangle(secColor, null, new Rect(badgeRect.X, badgeRect.Y, 3.5, badgeRect.Height));

                    ctx.DrawText(hft, new Point(secCx - hft.Width / 2 + 2, titleH + 12 + badgeH / 2 - hft.Height / 2));
                }

                // Connector from badge to rail
                var connTop = titleH + 12 + 24;
                ctx.DrawLine(new Pen(secColor, 1.0), new Point(secCx, connTop), new Point(secCx, railBaseline));

                // Rail dot
                ctx.DrawEllipse(surface0, new Pen(secColor, 2.0), new Point(secCx, railBaseline), 5, 5);

                // Vertical connector from rail down to events
                if (sec.Events.Count > 0)
                {
                    var lastEvY = sec.Events.Last().Y + tlEventH / 2;
                    ctx.DrawLine(new Pen(secColor, 1.0) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) },
                        new Point(secCx, railBaseline + 5), new Point(secCx, lastEvY));
                }

                // Events
                foreach (var ev in sec.Events)
                {
                    var evCx = ev.X;

                    // Small dot on the vertical line
                    ctx.DrawEllipse(secColor, null, new Point(evCx, ev.Y + tlEventH / 2), 3, 3);

                    // Event card
                    var timeFt = ev.TimeLabel.Length > 0 ? Txt(ev.TimeLabel, 11, secColor, FontWeight.SemiBold) : null;
                    var textFt = Txt(ev.Text, FsSmall, textSec);

                    var cardContentW = (timeFt is not null ? timeFt.Width + 8 : 0) + textFt.Width;
                    var cardW = cardContentW + 20;
                    var cardX = evCx + 10;
                    var cardRect = new Rect(cardX, ev.Y, cardW, tlEventH);

                    ctx.DrawRectangle(fill, borderPen, cardRect, 6, 6);

                    // Content: [time label] event text
                    double tx = cardX + 10;
                    if (timeFt is not null)
                    {
                        ctx.DrawText(timeFt, new Point(tx, ev.Y + tlEventH / 2 - timeFt.Height / 2));
                        tx += timeFt.Width + 8;
                    }
                    ctx.DrawText(textFt, new Point(tx, ev.Y + tlEventH / 2 - textFt.Height / 2));
                }
            }
        }

        // ── Quadrant chart render ──────────────────────────────

        private void RenderQuadrant(DrawingContext ctx)
        {
            var fill = _owner.ResolveBrush("Brush.Surface1", Color.Parse("#252525"));
            var border = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var textBr = _owner.ResolveBrush("Brush.TextPrimary", Color.Parse("#E4E4E4"));
            var textSec = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var textTer = _owner.ResolveBrush("Brush.TextTertiary", Color.Parse("#777"));
            var accent = _owner.ResolveBrush("Brush.AccentDefault", Color.Parse("#818CF8"));
            var surface0 = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));
            var subtle = _owner.ResolveBrush("Brush.BorderSubtle", Color.Parse("#2D2D2D"));

            // Quadrant fill brushes from chart palette (low opacity)
            var q1Brush = _owner.ResolveBrush("Brush.Chart5", Color.Parse("#38BDF8"));   // Sky
            var q2Brush = _owner.ResolveBrush("Brush.Chart1", Color.Parse("#818CF8"));   // Indigo
            var q3Brush = _owner.ResolveBrush("Brush.Chart4", Color.Parse("#F472B6"));   // Rose
            var q4Brush = _owner.ResolveBrush("Brush.Chart6", Color.Parse("#34D399"));   // Emerald

            var borderPen = new Pen(border, 1.0);
            var subtlePen = new Pen(subtle, 0.6);
            var axisPen = new Pen(textTer, 1.0);

            const double qdSize = 380;
            const double qdMarginLeft = 80;

            double titleH = _qdTitle.Length > 0 ? 40 : 12;

            // Title
            if (_qdTitle.Length > 0)
            {
                var ft = Txt(_qdTitle, 14, textBr, FontWeight.SemiBold);
                ctx.DrawText(ft, new Point(qdMarginLeft + qdSize / 2 - ft.Width / 2, 4));
            }

            var ox = qdMarginLeft;
            var oy = titleH;
            var halfSize = qdSize / 2;

            // Outer background
            var outerRect = new Rect(ox, oy, qdSize, qdSize);
            ctx.DrawRectangle(surface0, borderPen, outerRect, 8, 8);

            // Quadrant fills with low-opacity tints
            // Q1=top-right(Do First), Q2=top-left(Plan), Q3=bottom-left(Delegate), Q4=bottom-right(Eliminate)
            var qRects = new[] {
                new Rect(ox + halfSize, oy, halfSize, halfSize),           // Q1
                new Rect(ox, oy, halfSize, halfSize),                       // Q2
                new Rect(ox, oy + halfSize, halfSize, halfSize),           // Q3
                new Rect(ox + halfSize, oy + halfSize, halfSize, halfSize) // Q4
            };
            var qBrushes = new[] { q1Brush, q2Brush, q3Brush, q4Brush };

            for (int i = 0; i < 4; i++)
            {
                using (ctx.PushClip(new RoundedRect(outerRect, 8)))
                using (ctx.PushOpacity(0.08))
                    ctx.DrawRectangle(qBrushes[i], null, qRects[i]);
            }

            // Quadrant labels — centered in each quadrant
            for (int i = 0; i < 4; i++)
            {
                if (_qdLabels[i].Length > 0)
                {
                    var lft = Txt(_qdLabels[i], 11, textTer, FontWeight.SemiBold);
                    ctx.DrawText(lft, new Point(
                        qRects[i].X + qRects[i].Width / 2 - lft.Width / 2,
                        qRects[i].Y + qRects[i].Height / 2 - lft.Height / 2));
                }
            }

            // Center cross lines (dashed, subtle)
            var crossPen = new Pen(subtle, 1.0) { DashStyle = new DashStyle(new[] { 6.0, 4.0 }, 0) };
            using (ctx.PushClip(new RoundedRect(outerRect, 8)))
            {
                ctx.DrawLine(crossPen, new Point(ox + halfSize, oy), new Point(ox + halfSize, oy + qdSize));
                ctx.DrawLine(crossPen, new Point(ox, oy + halfSize), new Point(ox + qdSize, oy + halfSize));
            }

            // Axes along left and bottom edges (inside the border)
            ctx.DrawLine(axisPen, new Point(ox, oy + qdSize), new Point(ox + qdSize, oy + qdSize)); // x-axis
            ctx.DrawLine(axisPen, new Point(ox, oy), new Point(ox, oy + qdSize));                   // y-axis

            // Axis arrow tips
            // X-axis right arrow
            DrawArrowHead(ctx, new Point(ox + qdSize - 1, oy + qdSize), 0, textTer);
            // Y-axis up arrow
            DrawArrowHead(ctx, new Point(ox, oy + 1), -Math.PI / 2, textTer);

            // X-axis labels
            if (_qdXLow.Length > 0)
            {
                var ft = Txt(_qdXLow, FsSmall, textSec);
                ctx.DrawText(ft, new Point(ox + 4, oy + qdSize + 8));
            }
            if (_qdXHigh.Length > 0)
            {
                var ft = Txt(_qdXHigh, FsSmall, textSec);
                ctx.DrawText(ft, new Point(ox + qdSize - ft.Width - 4, oy + qdSize + 8));
            }

            // Y-axis labels
            if (_qdYLow.Length > 0)
            {
                var ft = Txt(_qdYLow, FsSmall, textSec);
                ctx.DrawText(ft, new Point(ox - ft.Width - 8, oy + qdSize - ft.Height - 2));
            }
            if (_qdYHigh.Length > 0)
            {
                var ft = Txt(_qdYHigh, FsSmall, textSec);
                ctx.DrawText(ft, new Point(ox - ft.Width - 8, oy + 2));
            }

            // Data points
            for (int i = 0; i < _qdPoints.Count; i++)
            {
                var pt = _qdPoints[i];
                var px = ox + pt.X * qdSize;
                var py = oy + (1.0 - pt.Y) * qdSize;

                // Glow ring
                using (ctx.PushOpacity(0.25))
                    ctx.DrawEllipse(accent, null, new Point(px, py), 9, 9);

                // Solid dot
                ctx.DrawEllipse(accent, null, new Point(px, py), 5, 5);

                // Label pill
                var lft = Txt(pt.Label, FsSmall, textBr);
                var pillW = lft.Width + 12;
                var pillH = lft.Height + 6;
                var pillX = px + 10;
                var pillY = py - pillH / 2;

                // Keep pill inside chart area
                if (pillX + pillW > ox + qdSize - 4)
                    pillX = px - 10 - pillW;

                var pillRect = new Rect(pillX, pillY, pillW, pillH);
                ctx.DrawRectangle(fill, borderPen, pillRect, 4, 4);
                ctx.DrawText(lft, new Point(pillX + 6, pillY + 3));
            }
        }

        // ── Drawing helpers ────────────────────────────────────

        private static Point NodePort(FNode n, bool isSource, bool vertical)
        {
            var cx = n.X + n.W / 2;
            var cy = n.Y + n.H / 2;
            if (vertical)
                return isSource ? new Point(cx, n.Y + n.H) : new Point(cx, n.Y);
            else
                return isSource ? new Point(n.X + n.W, cy) : new Point(n.X, cy);
        }

        private static void DrawBezierEdge(DrawingContext ctx, Point from, Point to, Pen pen,
            bool hasArrow, bool vertical, IBrush arrowBrush)
        {
            var dist = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            var offset = Math.Clamp(dist * 0.35, 12, 50);

            Point cp1, cp2;
            if (vertical)
            {
                cp1 = new Point(from.X, from.Y + offset);
                cp2 = new Point(to.X, to.Y - offset);
            }
            else
            {
                cp1 = new Point(from.X + offset, from.Y);
                cp2 = new Point(to.X - offset, to.Y);
            }

            var target = to;
            double arrAngle;
            if (hasArrow)
            {
                arrAngle = Math.Atan2(to.Y - cp2.Y, to.X - cp2.X);
                target = new Point(to.X - ArrSz * 0.3 * Math.Cos(arrAngle),
                                   to.Y - ArrSz * 0.3 * Math.Sin(arrAngle));
            }
            else
            {
                arrAngle = 0;
            }

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(from, false);
                gc.CubicBezierTo(cp1, cp2, target);
                gc.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);

            if (hasArrow)
                DrawArrowHead(ctx, to, arrAngle, arrowBrush);
        }

        private static void DrawBackEdge(DrawingContext ctx, Point from, Point to, Pen pen,
            IBrush arrowBrush, double rightBound)
        {
            var curveX = rightBound - 8;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(from, false);
                gc.CubicBezierTo(
                    new Point(curveX, from.Y),
                    new Point(curveX, to.Y),
                    new Point(to.X + ArrSz * 0.5, to.Y));
                gc.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
            DrawArrowHead(ctx, to, Math.PI, arrowBrush);
        }

        private static void DrawArrowHead(DrawingContext ctx, Point tip, double angle, IBrush brush)
        {
            var p1 = new Point(tip.X - ArrSz * Math.Cos(angle - 0.35), tip.Y - ArrSz * Math.Sin(angle - 0.35));
            var p2 = new Point(tip.X - ArrSz * Math.Cos(angle + 0.35), tip.Y - ArrSz * Math.Sin(angle + 0.35));

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(tip, true);
                gc.LineTo(p1);
                gc.LineTo(p2);
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        private static void DrawDiamond(DrawingContext ctx, Rect bounds, IBrush fill, Pen pen)
        {
            var cx = bounds.Center.X;
            var cy = bounds.Center.Y;
            var hw = bounds.Width / 2;
            var hh = bounds.Height / 2;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(cx, cy - hh), true);
                gc.LineTo(new Point(cx + hw, cy));
                gc.LineTo(new Point(cx, cy + hh));
                gc.LineTo(new Point(cx - hw, cy));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(fill, pen, geo);
        }

        private static FormattedText Txt(string text, double size, IBrush? brush = null,
            FontWeight weight = FontWeight.Normal)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, weight), size,
                brush ?? Brushes.White);
        }

        private static string LastWord(string s)
        {
            var t = s.Trim().TrimEnd(';', ',');
            var match = EndpointRightRegex.Match(t);
            if (match.Success)
                return match.Groups["id"].Value;

            var idx = t.LastIndexOf(' ');
            return idx >= 0 ? t[(idx + 1)..] : t;
        }

        private static string FirstWord(string s)
        {
            var t = s.Trim().TrimStart('"', '\'', '`').TrimEnd(';', ',');
            var match = EndpointLeftRegex.Match(t);
            if (match.Success)
                return match.Groups["id"].Value;

            var idx = t.IndexOf(' ');
            return idx >= 0 ? t[..idx] : t;
        }

        private static bool IsInsideBracketLabel(string line, int index)
        {
            if (index <= 0) return false;

            var inDouble = false;
            var inSingle = false;
            var squareDepth = 0;

            for (var i = 0; i < index && i < line.Length; i++)
            {
                var ch = line[i];

                if (ch == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (inDouble || inSingle)
                    continue;

                if (ch == '[')
                    squareDepth++;
                else if (ch == ']' && squareDepth > 0)
                    squareDepth--;
            }

            return inDouble || inSingle || squareDepth > 0;
        }
    }
}
