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
    private enum FSide { Top, Bottom, Left, Right }

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

        // Orthogonal route computed per render: ordered waypoints from source port to target port,
        // plus the sides each end exits/enters and a label anchor on the route's longest run.
        public Point FromPort, ToPort;
        public FSide ExitSide, EntrySide;
        public List<Point> Route = new();
        public Point LabelAt;
    }

    private sealed class FSubgraph
    {
        public string Id = "";
        public string Title = "";
        public List<string> NodeOrder = new();
        public HashSet<string> NodeSet = new(StringComparer.Ordinal);

        // Container bounds computed during layout (includes padding + title band).
        public Rect Box;

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
        public double ColTypeW, ColKeyW;
        public bool HasKeyCol;
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
        private readonly List<FSubgraph> _fSubgraphs = new();

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
        private const double SgPad = 14, SgTitleH = 20; // subgraph container inner padding + title band
        private const double SeqBoxH = 36, SeqMsgGap = 44, SeqMinCol = 130;

        public MermaidCanvas(StrataMermaid owner)
        {
            _owner = owner;
            ClipToBounds = true;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _animTimer?.Stop();
            base.OnDetachedFromVisualTree(e);
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
            var w = double.IsInfinity(available.Width) ? 600 : Math.Max(280, available.Width);

            if (!_parsed) DoParse();

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
            _fNodes.Clear(); _fEdges.Clear(); _fSubgraphs.Clear();
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
            var subgraphOrder = new List<FSubgraph>();
            var subgraphStack = new Stack<FSubgraph>();

            FSubgraph GetOrCreateSubgraph(string id, string title)
            {
                if (!subgraphs.TryGetValue(id, out var subgraph))
                {
                    subgraph = new FSubgraph { Id = id, Title = title };
                    subgraphs[id] = subgraph;
                    subgraphOrder.Add(subgraph);
                }
                else if (!string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(subgraph.Title))
                {
                    subgraph.Title = title;
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
                    line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("click ", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase) &&
                    (line.Length == 8 || char.IsWhiteSpace(line[8])))
                {
                    var rest = line.Length > 8 ? line[8..].Trim() : "";
                    string sgId, sgTitle;
                    var titled = Regex.Match(rest, @"^(?<id>\S+)\s*\[(?<t>[^\]]+)\]$");
                    if (titled.Success)
                    {
                        sgId = titled.Groups["id"].Value;
                        sgTitle = MermaidTextHelper.NormalizeLabelText(titled.Groups["t"].Value);
                    }
                    else if (rest.Length >= 2 && rest.StartsWith('"') && rest.EndsWith('"'))
                    {
                        sgTitle = MermaidTextHelper.NormalizeLabelText(rest.Trim('"'));
                        sgId = sgTitle;
                    }
                    else
                    {
                        var sp = rest.IndexOf(' ');
                        sgId = sp > 0 ? rest[..sp] : rest;
                        sgTitle = MermaidTextHelper.NormalizeLabelText(rest);
                    }

                    if (string.IsNullOrWhiteSpace(sgId))
                        sgId = $"_sg{subgraphOrder.Count}";

                    subgraphStack.Push(GetOrCreateSubgraph(sgId, sgTitle));
                    continue;
                }

                if (string.Equals(line, "end", StringComparison.OrdinalIgnoreCase))
                {
                    if (subgraphStack.Count > 0)
                        subgraphStack.Pop();
                    continue;
                }

                // Extract nodes (bracketed definitions carry shape + label)
                foreach (var (rx, shape) in NodeRxs)
                    foreach (Match m in rx.Matches(line))
                    {
                        if (IsInsideBracketLabel(line, m.Index))
                            continue;

                        var id = m.Groups[1].Value;
                        if (!_fNodes.ContainsKey(id))
                            _fNodes[id] = new FNode { Id = id, Text = MermaidTextHelper.NormalizeLabelText(m.Groups[2].Value), Shape = shape };
                    }

                // Extract edges — strip bracket content first
                var stripped = Regex.Replace(line, @"\(\([^)]*\)\)|\(\[[^\]]*\]\)|\{[^}]*\}|\[[^\]]*\]|\([^)]*\)", "");

                // Subgraph membership: every node referenced on a line inside a subgraph belongs to it —
                // bracketed definitions, bare id listings (e.g. a lone "GW"), and in-subgraph edge endpoints.
                if (subgraphStack.Count > 0)
                {
                    foreach (var nodeId in CollectLineNodeRefs(stripped))
                    {
                        if (!_fNodes.ContainsKey(nodeId))
                            _fNodes[nodeId] = new FNode { Id = nodeId, Text = nodeId, Shape = NShape.Rect };
                        foreach (var subgraph in subgraphStack)
                            AddNodeToSubgraph(subgraph, nodeId);
                    }
                }

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
                        var toPart = parts[i + 1].Trim();

                        string? label = null;
                        if (toPart.StartsWith('|'))
                        {
                            var end = toPart.IndexOf('|', 1);
                            if (end > 0)
                            {
                                label = MermaidTextHelper.NormalizeLabelText(toPart[1..end]);
                                toPart = toPart[(end + 1)..].Trim();
                            }
                        }

                        // Mermaid "&" groups multiple endpoints: A & B --> C & D
                        var fromIds = ResolveEndpoints(parts[i], subgraphs, trailing: true);
                        var toIds = ResolveEndpoints(toPart, subgraphs, trailing: false);

                        foreach (var rawFrom in fromIds)
                        foreach (var rawTo in toIds)
                        {
                            var fromId = rawFrom;
                            var toId = rawTo;
                            if (fromId.Length == 0 || toId.Length == 0 || fromId == toId) continue;

                            if (isRightToLeft)
                                (fromId, toId) = (toId, fromId);

                            AddUniqueFlowEdge(fromId, toId, label, style);
                            if (isBidirectional)
                                AddUniqueFlowEdge(toId, fromId, label, style);
                        }
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

            // Keep subgraphs that contain laid-out nodes; rendered as visual containers.
            // (Intentionally no synthetic sequential edges — that turned grouped
            // architecture diagrams into a bogus linked-list chain.)
            foreach (var subgraph in subgraphOrder)
            {
                subgraph.NodeOrder.RemoveAll(id => !_fNodes.ContainsKey(id));
                if (subgraph.NodeOrder.Count > 0)
                    _fSubgraphs.Add(subgraph);
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
                    var label = pm.Groups[2].Success ? MermaidTextHelper.NormalizeLabelText(pm.Groups[2].Value) : id;
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
                    var text = MermaidTextHelper.NormalizeLabelText(mm.Groups[4].Value);

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
                    _stNodes[dm.Groups[2].Value] = new StNode { Id = dm.Groups[2].Value, Text = MermaidTextHelper.NormalizeLabelText(dm.Groups[1].Value) };
                    continue;
                }

                var tm = StTransRx.Match(line);
                if (tm.Success)
                {
                    var fromRaw = tm.Groups[1].Value;
                    var toRaw = tm.Groups[2].Value;
                    var label = tm.Groups[3].Success ? MermaidTextHelper.NormalizeLabelText(tm.Groups[3].Value) : null;

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
            @"^([A-Za-z0-9_-]+)\s+([|}o]{2}(?:--|\.\.)[|o{]{2})\s+([A-Za-z0-9_-]+)(?:\s*:\s*(.+))?$", RegexOptions.Compiled);

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
                    if (name.Length > 0)
                    {
                        // Reuse an entity already created by a relationship so its attribute
                        // block is captured instead of dropped.
                        if (!_erEntities.TryGetValue(name, out var ent))
                            _erEntities[name] = ent = new ErEntity { Name = name };
                        current = ent;
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
                        Label = MermaidTextHelper.NormalizeLabelText(rm.Groups[4].Value)
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
        private static readonly Regex CdInlineMemberRx = new(
            @"^(\w+)\s*:\s*(.+)$", RegexOptions.Compiled);

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
                        Label = rm.Groups[4].Success ? MermaidTextHelper.NormalizeLabelText(rm.Groups[4].Value) : null
                    });

                    // Ensure classes exist
                    if (!_cdClasses.ContainsKey(rm.Groups[1].Value))
                        _cdClasses[rm.Groups[1].Value] = new CdClass { Name = rm.Groups[1].Value };
                    if (!_cdClasses.ContainsKey(rm.Groups[3].Value))
                        _cdClasses[rm.Groups[3].Value] = new CdClass { Name = rm.Groups[3].Value };
                    continue;
                }

                // Inline member: ClassName : +type member  /  ClassName : +method()
                var im = CdInlineMemberRx.Match(line);
                if (im.Success)
                {
                    var cname = im.Groups[1].Value;
                    if (!_cdClasses.TryGetValue(cname, out var cls))
                        _cdClasses[cname] = cls = new CdClass { Name = cname };
                    var member = im.Groups[2].Value.Trim();
                    if (member.Contains('('))
                        cls.Methods.Add(member);
                    else
                        cls.Attributes.Add(member);
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
                    var timeLabel = MermaidTextHelper.NormalizeLabelText(line[..colonIdx]);
                    var eventsStr = line[(colonIdx + 1)..];
                    var events = eventsStr.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    if (currentSection is null)
                    {
                        currentSection = new TlSection { Title = "", ColorIndex = sectionIdx++ };
                        _tlSections.Add(currentSection);
                    }

                    foreach (var evt in events)
                        currentSection.Events.Add(new TlEvent { TimeLabel = timeLabel, Text = MermaidTextHelper.NormalizeLabelText(evt) });
                }
                else if (currentSection is not null)
                {
                    currentSection.Events.Add(new TlEvent { TimeLabel = "", Text = MermaidTextHelper.NormalizeLabelText(line) });
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
                            Label = MermaidTextHelper.NormalizeLabelText(pm.Groups[1].Value),
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

            // Subgraph diagrams (architecture) use a dedicated clustered layer-band layout.
            if (_fSubgraphs.Count > 0)
            {
                LayoutFlowClustered(availW);
                return;
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

            var horizontalGap = _fNodes.Count > 10 ? 24.0 : NGapH;
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

            NormalizeFlowLayout();
        }

        private sealed class Cluster
        {
            public FSubgraph? Subgraph;
            public List<FNode> Nodes = new();
            public int Rank;
            public double X, Y, W, H; // content origin + content size (absolute after arrange)
        }

        // Architecture layout: each subgraph becomes a compact layer "band" (and each loose node
        // its own band), ordered by inter-cluster dependency depth. Members are packed inside the
        // band, so related nodes stay grouped and the diagram is balanced — instead of a global
        // rank layout whose subgraph bounding boxes overlap and stretch into a tall ribbon.
        private void LayoutFlowClustered(double availW)
        {
            var clusters = new List<Cluster>();
            var nodeCluster = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int s = 0; s < _fSubgraphs.Count; s++)
            {
                var c = new Cluster { Subgraph = _fSubgraphs[s] };
                foreach (var id in _fSubgraphs[s].NodeOrder)
                {
                    if (!_fNodes.TryGetValue(id, out var n) || nodeCluster.ContainsKey(id)) continue;
                    nodeCluster[id] = clusters.Count;
                    c.Nodes.Add(n);
                }
                if (c.Nodes.Count > 0) clusters.Add(c);
            }

            foreach (var n in _fNodes.Values)
            {
                if (nodeCluster.ContainsKey(n.Id)) continue;
                nodeCluster[n.Id] = clusters.Count;
                clusters.Add(new Cluster { Nodes = { n } });
            }

            if (clusters.Count == 0) { _layW = _layH = 0; return; }

            RankClusters(clusters, nodeCluster);
            MinimizeClusterCrossings(clusters);

            var targetInner = Math.Clamp(availW - Pad * 2 - SgPad * 2, 200, 760);
            foreach (var c in clusters)
                LayoutClusterMembers(c, targetInner);

            ArrangeClusterBands(clusters, availW);

            foreach (var c in clusters)
            {
                if (c.Subgraph is null) continue;
                c.Subgraph.Box = new Rect(c.X - SgPad, c.Y - SgPad - SgTitleH,
                                          c.W + SgPad * 2, c.H + SgPad * 2 + SgTitleH);
            }

            NormalizeFlowLayout();
        }

        // Rank clusters by inter-cluster dependency depth (cycle-safe longest path).
        private void RankClusters(List<Cluster> clusters, Dictionary<string, int> nodeCluster)
        {
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var e in _fEdges)
            {
                if (!nodeCluster.TryGetValue(e.From, out var cu) || !nodeCluster.TryGetValue(e.To, out var cv)) continue;
                if (cu == cv) continue;
                if (!adj.TryGetValue(cu, out var set)) adj[cu] = set = new();
                set.Add(cv);
            }

            var back = new HashSet<(int, int)>();
            var visiting = new HashSet<int>();
            var done = new HashSet<int>();
            void Dfs(int u)
            {
                if (done.Contains(u)) return;
                visiting.Add(u);
                if (adj.TryGetValue(u, out var nb))
                    foreach (var v in nb)
                    {
                        if (visiting.Contains(v)) back.Add((u, v));
                        else Dfs(v);
                    }
                visiting.Remove(u);
                done.Add(u);
            }
            for (int i = 0; i < clusters.Count; i++) Dfs(i);

            for (int i = 0; i < clusters.Count; i++) clusters[i].Rank = 0;
            bool chg = true;
            while (chg)
            {
                chg = false;
                foreach (var (u, set) in adj)
                    foreach (var v in set)
                    {
                        if (back.Contains((u, v))) continue;
                        if (clusters[v].Rank < clusters[u].Rank + 1)
                        { clusters[v].Rank = clusters[u].Rank + 1; chg = true; }
                    }
            }
        }

        // Order nodes within a cluster by intra-cluster dependency (sources before targets) — used as
        // the starting order before cross-band crossing minimisation.
        private List<FNode> IntraOrder(Cluster c)
        {
            if (c.Nodes.Count < 2) return c.Nodes;

            var memberSet = new HashSet<string>(c.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            var intraAdj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in _fEdges)
            {
                if (e.From == e.To || !memberSet.Contains(e.From) || !memberSet.Contains(e.To)) continue;
                if (!intraAdj.TryGetValue(e.From, out var l)) intraAdj[e.From] = l = new();
                l.Add(e.To);
            }

            var mrank = c.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
            bool chg = true; int guard = 0;
            while (chg && guard++ <= c.Nodes.Count)
            {
                chg = false;
                foreach (var (from, tos) in intraAdj)
                    foreach (var to in tos)
                        if (mrank[to] < mrank[from] + 1) { mrank[to] = mrank[from] + 1; chg = true; }
            }

            return c.Nodes.OrderBy(n => mrank[n.Id]).ToList();
        }

        // Reduce edge crossings with the iterative barycenter heuristic: repeatedly reorder the nodes
        // (and clusters) within each band by the average cross-axis position of their neighbours in the
        // other bands, so connected nodes line up and the connecting lines stop tangling.
        private void MinimizeClusterCrossings(List<Cluster> clusters)
        {
            foreach (var c in clusters)
                c.Nodes = IntraOrder(c);

            var bands = clusters.GroupBy(c => c.Rank).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            if (bands.Count < 2) return;

            var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            void AddAdj(string a, string b)
            {
                if (!adj.TryGetValue(a, out var l)) adj[a] = l = new();
                l.Add(b);
            }
            foreach (var e in _fEdges)
            {
                if (e.From == e.To) continue;
                AddAdj(e.From, e.To);
                AddAdj(e.To, e.From);
            }

            var bandOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int bi = 0; bi < bands.Count; bi++)
                foreach (var c in bands[bi])
                    foreach (var n in c.Nodes)
                        bandOf[n.Id] = bi;

            var pos = new Dictionary<string, double>(StringComparer.Ordinal);
            void Reflow()
            {
                foreach (var band in bands)
                {
                    var flat = band.SelectMany(c => c.Nodes).ToList();
                    var denom = Math.Max(1, flat.Count - 1);
                    for (int i = 0; i < flat.Count; i++)
                        pos[flat[i].Id] = (double)i / denom;
                }
            }
            Reflow();

            double Bary(FNode n, int selfBand)
            {
                double sum = 0; int cnt = 0;
                if (adj.TryGetValue(n.Id, out var nb))
                    foreach (var m in nb)
                        if (bandOf.TryGetValue(m, out var mb) && mb != selfBand && pos.TryGetValue(m, out var p))
                        { sum += p; cnt++; }
                return cnt > 0 ? sum / cnt : pos[n.Id];
            }

            for (int iter = 0; iter < 4; iter++)
            {
                var seq = iter % 2 == 0
                    ? Enumerable.Range(0, bands.Count)
                    : Enumerable.Range(0, bands.Count).Reverse();

                foreach (var bi in seq)
                {
                    var band = bands[bi];
                    var bary = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (var c in band)
                        foreach (var n in c.Nodes)
                            bary[n.Id] = Bary(n, bi);

                    band.Sort((a, b) => MeanBary(a, bary).CompareTo(MeanBary(b, bary)));
                    foreach (var c in band)
                        c.Nodes = c.Nodes.OrderBy(n => bary[n.Id]).ToList();

                    Reflow();
                }
            }

            var reordered = bands.SelectMany(b => b).ToList();
            clusters.Clear();
            clusters.AddRange(reordered);
        }

        private static double MeanBary(Cluster c, Dictionary<string, double> bary)
        {
            if (c.Nodes.Count == 0) return 0;
            double sum = 0;
            foreach (var n in c.Nodes) sum += bary[n.Id];
            return sum / c.Nodes.Count;
        }

        // Intra-cluster dependency ranks (sources before targets), cycle-safe. Drives the layered
        // placement inside a subgraph so e.g. a parent view-model sits in the row above its children.
        private Dictionary<string, int> ComputeIntraRanks(Cluster c)
        {
            var memberSet = new HashSet<string>(c.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            var rank = c.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
            if (c.Nodes.Count < 2) return rank;

            var intraAdj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in _fEdges)
            {
                if (e.From == e.To || !memberSet.Contains(e.From) || !memberSet.Contains(e.To)) continue;
                if (!intraAdj.TryGetValue(e.From, out var l)) intraAdj[e.From] = l = new();
                l.Add(e.To);
            }

            // Break cycles so the relaxation terminates.
            var back = new HashSet<(string, string)>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            void Dfs(string u)
            {
                if (done.Contains(u)) return;
                visiting.Add(u);
                if (intraAdj.TryGetValue(u, out var nb))
                    foreach (var v in nb)
                    {
                        if (visiting.Contains(v)) back.Add((u, v));
                        else Dfs(v);
                    }
                visiting.Remove(u);
                done.Add(u);
            }
            foreach (var n in c.Nodes) Dfs(n.Id);

            bool chg = true; int guard = 0;
            while (chg && guard++ <= c.Nodes.Count + 1)
            {
                chg = false;
                foreach (var (from, tos) in intraAdj)
                    foreach (var to in tos)
                    {
                        if (back.Contains((from, to))) continue;
                        if (rank[to] < rank[from] + 1) { rank[to] = rank[from] + 1; chg = true; }
                    }
            }
            return rank;
        }

        // Lay a cluster's members in a compact mini layered layout relative to (0,0): one row per
        // intra-rank for TB (one column per rank for LR), preserving the crossing-minimised cross order
        // within each rank and centring the rows/columns. This keeps intra-subgraph edges short and
        // mostly straight instead of looping siblings around a single shared row.
        private void LayoutClusterMembers(Cluster c, double targetInner)
        {
            const double gap = 18;
            const double rankGap = 30;

            var ranks = ComputeIntraRanks(c);
            // c.Nodes is already in crossing-minimised cross order; group by rank preserving that order.
            var rows = c.Nodes
                .GroupBy(n => ranks[n.Id])
                .OrderBy(g => g.Key)
                .Select(g => g.ToList())
                .ToList();

            if (!_ltr)
            {
                var widths = rows.Select(r => r.Sum(n => n.W) + Math.Max(0, r.Count - 1) * gap).ToList();
                var maxW = widths.Count > 0 ? widths.Max() : 0;
                double y = 0;
                for (int r = 0; r < rows.Count; r++)
                {
                    var rowH = rows[r].Max(n => n.H);
                    double x = (maxW - widths[r]) / 2;
                    foreach (var n in rows[r])
                    {
                        n.X = x; n.Y = y;
                        x += n.W + gap;
                    }
                    y += rowH + rankGap;
                }
                c.W = maxW;
                c.H = rows.Count > 0 ? y - rankGap : 0;
            }
            else
            {
                var heights = rows.Select(r => r.Sum(n => n.H) + Math.Max(0, r.Count - 1) * gap).ToList();
                var maxH = heights.Count > 0 ? heights.Max() : 0;
                double x = 0;
                for (int col = 0; col < rows.Count; col++)
                {
                    var colW = rows[col].Max(n => n.W);
                    double y = (maxH - heights[col]) / 2;
                    foreach (var n in rows[col])
                    {
                        n.X = x; n.Y = y;
                        y += n.H + gap;
                    }
                    x += colW + rankGap;
                }
                c.W = rows.Count > 0 ? x - rankGap : 0;
                c.H = maxH;
            }
        }

        // Place each cluster as a layer band. Within a rank, clusters are packed into rows that fit
        // the available width, so they sit side-by-side when there's room but stack vertically when
        // the diagram is squeezed (e.g. a chat bubble) — keeping it readable instead of shrunk.
        private void ArrangeClusterBands(List<Cluster> clusters, double availW)
        {
            var byRank = clusters.GroupBy(c => c.Rank).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

            double BoxPadX(Cluster c) => c.Subgraph != null ? SgPad : 0;
            double BoxPadY(Cluster c) => c.Subgraph != null ? SgPad + SgTitleH : 0;
            double OuterW(Cluster c) => c.W + (c.Subgraph != null ? SgPad * 2 : 0);
            double OuterH(Cluster c) => c.H + (c.Subgraph != null ? SgPad * 2 + SgTitleH : 0);

            const double bandGap = 60;
            const double clusterGap = 30;

            if (!_ltr)
            {
                var maxRowW = Math.Max(availW - Pad * 2, 240);
                double y = 0;
                foreach (var rankClusters in byRank)
                {
                    var i = 0;
                    while (i < rankClusters.Count)
                    {
                        var row = new List<Cluster>();
                        double rowW = 0;
                        while (i < rankClusters.Count)
                        {
                            var cw = OuterW(rankClusters[i]);
                            var next = row.Count == 0 ? cw : rowW + clusterGap + cw;
                            if (row.Count > 0 && next > maxRowW) break;
                            row.Add(rankClusters[i]);
                            rowW = next;
                            i++;
                        }

                        var rowH = row.Max(OuterH);
                        double x = -rowW / 2;
                        foreach (var c in row)
                        {
                            var ox = x + BoxPadX(c);
                            var oy = y + (rowH - OuterH(c)) / 2 + BoxPadY(c);
                            foreach (var n in c.Nodes) { n.X += ox; n.Y += oy; n.Rank = c.Rank; }
                            c.X = ox; c.Y = oy;
                            x += OuterW(c) + clusterGap;
                        }
                        y += rowH + bandGap;
                    }
                }
            }
            else
            {
                double x = 0;
                foreach (var band in byRank)
                {
                    var totalH = band.Sum(OuterH) + Math.Max(0, band.Count - 1) * clusterGap;
                    var bandW = band.Max(OuterW);
                    double y = -totalH / 2;
                    foreach (var c in band)
                    {
                        var ox = x + (bandW - OuterW(c)) / 2 + BoxPadX(c);
                        var oy = y + BoxPadY(c);
                        foreach (var n in c.Nodes) { n.X += ox; n.Y += oy; n.Rank = c.Rank; }
                        c.X = ox; c.Y = oy;
                        y += OuterH(c) + clusterGap;
                    }
                    x += bandW + bandGap;
                }
            }
        }

        // Shift the whole flow to a tight top-left origin and size the layout to the real content
        // extent (nodes + containers). Fixes the centred render transform being biased rightwards
        // when a rank is narrower than the wrap width, and prevents container clipping.
        private void NormalizeFlowLayout()
        {
            if (_fNodes.Count == 0) { _layW = _layH = 0; return; }

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in _fNodes.Values)
            {
                minX = Math.Min(minX, n.X);
                minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.X + n.W);
                maxY = Math.Max(maxY, n.Y + n.H);
            }
            foreach (var sg in _fSubgraphs)
            {
                if (sg.Box.Width <= 0) continue;
                minX = Math.Min(minX, sg.Box.X);
                minY = Math.Min(minY, sg.Box.Y);
                maxX = Math.Max(maxX, sg.Box.Right);
                maxY = Math.Max(maxY, sg.Box.Bottom);
            }

            double dx = -minX, dy = -minY;
            if (dx != 0 || dy != 0)
            {
                foreach (var n in _fNodes.Values) { n.X += dx; n.Y += dy; }
                foreach (var sg in _fSubgraphs)
                    if (sg.Box.Width > 0)
                        sg.Box = sg.Box.Translate(new Vector(dx, dy));
            }

            _layW = maxX - minX;
            _layH = maxY - minY;
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

            // Build forward adjacency, detecting back edges so cycles stay layout-safe.
            var adjS = new Dictionary<string, List<string>>();
            foreach (var edge in _stEdges)
            {
                if (!adjS.ContainsKey(edge.From)) adjS[edge.From] = new();
                adjS[edge.From].Add(edge.To);
            }
            var backS = new HashSet<(string, string)>();
            {
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
            }

            var fwd = _stEdges.Where(e => !backS.Contains((e.From, e.To))
                                          && _stNodes.ContainsKey(e.From) && _stNodes.ContainsKey(e.To)
                                          && e.From != e.To).ToList();

            // Longest path from a source = critical-path length.
            var lo = _stNodes.Keys.ToDictionary(k => k, _ => 0);
            for (int iter = 0; iter < _stNodes.Count; iter++)
            {
                bool chg = false;
                foreach (var e in fwd)
                    if (lo[e.To] < lo[e.From] + 1) { lo[e.To] = lo[e.From] + 1; chg = true; }
                if (!chg) break;
            }
            int maxRank = lo.Values.Count > 0 ? lo.Values.Max() : 0;

            // Bottom-justify by longest path to a sink, so terminal states settle on the last
            // row beside the sink instead of dangling near their parents (dagre-like look).
            var toSink = _stNodes.Keys.ToDictionary(k => k, _ => 0);
            for (int iter = 0; iter < _stNodes.Count; iter++)
            {
                bool chg = false;
                foreach (var e in fwd)
                    if (toSink[e.From] < toSink[e.To] + 1) { toSink[e.From] = toSink[e.To] + 1; chg = true; }
                if (!chg) break;
            }
            foreach (var n in _stNodes.Values) n.Rank = maxRank - toSink[n.Id];

            // Non-back parents per node (crossing reduction + skip detection).
            var parents = _stNodes.Keys.ToDictionary(k => k, _ => new List<string>());
            foreach (var e in fwd) parents[e.To].Add(e.From);

            var rows = _stNodes.Values.GroupBy(n => n.Rank).OrderBy(g => g.Key)
                               .Select(g => g.Select(n => n.Id).ToList()).ToList();

            // Order each row: barycenter against the previous row to reduce crossings, then push
            // skip-edge targets (a parent more than one row up) to the row's outer edges so their
            // long connectors drop down clear outer columns instead of through central nodes.
            var orderIdx = new Dictionary<string, int>();
            for (int r = 0; r < rows.Count; r++)
            {
                if (r > 0)
                {
                    int rr = r;
                    var keyed = rows[r].Select((id, i) =>
                    {
                        var ps = parents[id].Where(p => orderIdx.ContainsKey(p) && _stNodes[p].Rank == rr - 1)
                                            .Select(p => (double)orderIdx[p]).ToList();
                        double bary = ps.Count > 0 ? ps.Average() : i;
                        bool skip = parents[id].Any(p => _stNodes[p].Rank < rr - 1);
                        return (id, bary, skip, i);
                    }).ToList();

                    var central = keyed.Where(t => !t.skip).OrderBy(t => t.bary).ThenBy(t => t.i)
                                       .Select(t => t.id).ToList();
                    var skips = keyed.Where(t => t.skip).OrderBy(t => t.bary).ThenBy(t => t.i)
                                     .Select(t => t.id).ToList();
                    var left = new List<string>();
                    var right = new List<string>();
                    for (int s = 0; s < skips.Count; s++)
                        (s % 2 == 0 ? left : right).Add(skips[s]);
                    right.Reverse();
                    rows[r] = left.Concat(central).Concat(right).ToList();
                }
                for (int i = 0; i < rows[r].Count; i++) orderIdx[rows[r][i]] = i;
            }

            // Position rows, centered.
            var rowNodes = rows.Select(r => r.Select(id => _stNodes[id]).ToList()).ToList();
            var maxRankW = rowNodes.Max(r => r.Sum(n => n.W) + (r.Count - 1) * NGapH);

            double y = 0;
            foreach (var rank in rowNodes)
            {
                var rankW = rank.Sum(n => n.W) + (rank.Count - 1) * NGapH;
                var sx = (maxRankW - rankW) / 2;
                foreach (var n in rank)
                {
                    n.X = sx;
                    n.Y = y;
                    sx += n.W + NGapH;
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

            const double erCellPadH = 11;
            const double erFieldH = 24;
            const double erHeaderH = 32;
            const double erGapH = 64;
            const double erGapV = 76;

            foreach (var ent in _erEntities.Values)
            {
                double colType = 0, colName = 0, colKey = 0;
                bool hasKey = false;
                foreach (var f in ent.Fields)
                {
                    colType = Math.Max(colType, Txt(f.Type, FsSmall).Width);
                    colName = Math.Max(colName, Txt(f.Name, FsSmall).Width);
                    if (!string.IsNullOrEmpty(f.Constraint))
                    {
                        hasKey = true;
                        colKey = Math.Max(colKey, Txt(f.Constraint, FsSmall, null, FontWeight.SemiBold).Width);
                    }
                }
                ent.ColTypeW = colType;
                ent.ColKeyW = colKey;
                ent.HasKeyCol = hasKey;

                double bodyW = 0;
                if (ent.Fields.Count > 0)
                {
                    bodyW = (colType + erCellPadH * 2) + (colName + erCellPadH * 2);
                    if (hasKey) bodyW += colKey + erCellPadH * 2;
                }
                var headerFt = Txt(ent.Name, Fs, null, FontWeight.SemiBold);
                ent.W = Math.Max(Math.Max(bodyW, headerFt.Width + erCellPadH * 2 + 8), 96);
                ent.H = erHeaderH + ent.Fields.Count * erFieldH;
            }

            // Hierarchical ranking: From entity sits above its To entity.
            var names = _erEntities.Keys.ToList();
            var rank = names.ToDictionary(n => n, _ => 0);
            var edges = _erRelations
                .Where(r => _erEntities.ContainsKey(r.From) && _erEntities.ContainsKey(r.To) && r.From != r.To)
                .ToList();
            for (int iter = 0; iter < names.Count; iter++)
            {
                bool changed = false;
                foreach (var e in edges)
                    if (rank[e.To] < rank[e.From] + 1) { rank[e.To] = rank[e.From] + 1; changed = true; }
                if (!changed) break;
            }

            var rows = rank.GroupBy(kv => kv.Value).OrderBy(g => g.Key)
                           .Select(g => g.Select(kv => kv.Key).ToList()).ToList();
            for (int r = 1; r < rows.Count; r++)
            {
                var prev = rows[r - 1];
                var prevIdx = new Dictionary<string, int>();
                for (int i = 0; i < prev.Count; i++) prevIdx[prev[i]] = i;
                rows[r] = rows[r].Select((n, i) =>
                {
                    var ps = edges.Where(e => e.To == n && prevIdx.ContainsKey(e.From))
                                  .Select(e => (double)prevIdx[e.From]).ToList();
                    return (n, bary: ps.Count > 0 ? ps.Average() : i, i);
                }).OrderBy(t => t.bary).ThenBy(t => t.i).Select(t => t.n).ToList();
            }

            var rowH = rows.Select(row => row.Max(n => _erEntities[n].H)).ToArray();
            var rowW = rows.Select(row => row.Sum(n => _erEntities[n].W) + (row.Count - 1) * erGapH).ToArray();
            double totalW = rowW.Max();

            double y = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                double x = (totalW - rowW[r]) / 2;
                foreach (var n in rows[r])
                {
                    var ent = _erEntities[n];
                    ent.X = x;
                    ent.Y = y;
                    x += ent.W + erGapH;
                }
                y += rowH[r] + erGapV;
            }

            _layW = totalW;
            _layH = y - erGapV;
        }

        private void LayoutClass(double availW)
        {
            if (_cdClasses.Count == 0) { _layW = _layH = 0; return; }

            const double cdHeaderH = 32;
            const double cdRowH = 19;
            const double cdSecPad = 6;
            const double cdPadH = 13;
            const double cdGapH = 56;
            const double cdGapV = 58;
            const double cdDivH = 1;

            foreach (var cls in _cdClasses.Values)
            {
                double maxW = Txt(cls.Name, Fs, null, FontWeight.SemiBold).Width + cdPadH * 2;
                foreach (var attr in cls.Attributes)
                    maxW = Math.Max(maxW, Txt(attr, FsSmall).Width + cdPadH * 2);
                foreach (var meth in cls.Methods)
                    maxW = Math.Max(maxW, Txt(meth, FsSmall).Width + cdPadH * 2);
                cls.W = Math.Max(maxW, 108);

                double h = cdHeaderH;
                if (cls.Attributes.Count > 0)
                    h += cdDivH + cdSecPad * 2 + cls.Attributes.Count * cdRowH;
                if (cls.Methods.Count > 0)
                    h += cdDivH + cdSecPad * 2 + cls.Methods.Count * cdRowH;
                cls.H = h;
            }

            // Hierarchical ranking: parent (relation From) sits above child (To).
            var names = _cdClasses.Keys.ToList();
            var rank = names.ToDictionary(n => n, _ => 0);
            var edges = _cdRelations
                .Where(r => _cdClasses.ContainsKey(r.From) && _cdClasses.ContainsKey(r.To) && r.From != r.To)
                .ToList();
            for (int iter = 0; iter < names.Count; iter++)
            {
                bool changed = false;
                foreach (var e in edges)
                    if (rank[e.To] < rank[e.From] + 1) { rank[e.To] = rank[e.From] + 1; changed = true; }
                if (!changed) break;
            }

            // Group into ordered ranks (rows), then reduce crossings via parent barycenter.
            var rows = rank.GroupBy(kv => kv.Value).OrderBy(g => g.Key)
                           .Select(g => g.Select(kv => kv.Key).ToList()).ToList();
            for (int r = 1; r < rows.Count; r++)
            {
                var prev = rows[r - 1];
                var prevIdx = new Dictionary<string, int>();
                for (int i = 0; i < prev.Count; i++) prevIdx[prev[i]] = i;
                var cur = rows[r];
                var keyed = cur.Select((n, i) =>
                {
                    var ps = edges.Where(e => e.To == n && prevIdx.ContainsKey(e.From))
                                  .Select(e => (double)prevIdx[e.From]).ToList();
                    double bary = ps.Count > 0 ? ps.Average() : i;
                    return (n, bary, i);
                }).OrderBy(t => t.bary).ThenBy(t => t.i).Select(t => t.n).ToList();
                rows[r] = keyed;
            }

            var rowH = rows.Select(row => row.Max(n => _cdClasses[n].H)).ToArray();
            var rowW = rows.Select(row => row.Sum(n => _cdClasses[n].W) + (row.Count - 1) * cdGapH).ToArray();
            double totalW = rowW.Max();

            double y = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                double x = (totalW - rowW[r]) / 2;
                foreach (var n in rows[r])
                {
                    var cls = _cdClasses[n];
                    cls.X = x;
                    cls.Y = y;
                    x += cls.W + cdGapH;
                }
                y += rowH[r] + cdGapV;
            }

            _layW = totalW;
            _layH = y - cdGapV;
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

            var contentW = _layW;
            var ox = (b.Width - contentW * effScale) / 2 + _panX;
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
            var linePen = new Pen(lineBr, 1.3) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var dottedPen = new Pen(lineBr, 1.3) { DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0), LineCap = PenLineCap.Round };
            var thickPen = new Pen(lineBr, 2.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

            // Casing colour = the diagram backdrop, so crossing edges read as a clean weave.
            var casingBr = _owner.ResolveBrush("Brush.Background", Color.Parse("#161616"));
            var casingPen = new Pen(casingBr, 5.0) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

            // Draw subgraph containers behind everything (outermost first so nested boxes show).
            if (_fSubgraphs.Count > 0)
            {
                var sgFill = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1E1E22"));
                var sgBorder = _owner.ResolveBrush("Brush.BorderSubtle", Color.Parse("#3A3A40"));
                var sgPen = new Pen(sgBorder, 1.0);

                foreach (var sg in _fSubgraphs
                             .Where(s => s.Box.Width > 0)
                             .OrderByDescending(s => s.Box.Width * s.Box.Height))
                {
                    ctx.DrawRectangle(sgFill, sgPen, sg.Box, 8, 8);
                }
            }

            RouteFlowEdges();

            // Draw edges first (behind nodes)
            foreach (var e in _fEdges)
            {
                if (e.Route.Count < 2) continue;

                var pen = e.Style switch
                {
                    EStyle.Dotted => dottedPen,
                    EStyle.Thick => thickPen,
                    _ => linePen
                };
                var hasArrow = e.Style != EStyle.Line;

                // Dotted edges keep their dashes legible without a casing.
                var casing = e.Style == EStyle.Dotted ? null : casingPen;
                DrawOrthEdge(ctx, e.Route, pen, casing, hasArrow, lineBr);

                if (!string.IsNullOrWhiteSpace(e.Label))
                {
                    var mid = e.LabelAt;
                    var ft = Txt(e.Label, FsSmall, labelBr);
                    var pillR = new Rect(mid.X - ft.Width / 2 - 6, mid.Y - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 8, 8);
                    ctx.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
                }
            }

            // Accent only TRUE source roots (have outgoing edges but no incoming). Edgeless/orphan
            // nodes are NOT highlighted — they were rendering as misleading purple "entry points".
            var hasIncoming = new HashSet<string>(_fEdges.Select(e => e.To));
            var hasOutgoing = new HashSet<string>(_fEdges.Select(e => e.From));

            // Draw nodes
            foreach (var n in _fNodes.Values)
            {
                var rect = new Rect(n.X, n.Y, n.W, n.H);
                var isRoot = !hasIncoming.Contains(n.Id) && hasOutgoing.Contains(n.Id);
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

            // Subgraph titles last, on a masking background so crossing edges never overstrike them.
            if (_fSubgraphs.Count > 0)
            {
                var sgFill = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1E1E22"));
                var sgTitleBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
                foreach (var sg in _fSubgraphs.Where(s => s.Box.Width > 0 && !string.IsNullOrWhiteSpace(s.Title)))
                {
                    var tt = Txt(sg.Title, FsSmall, sgTitleBr, FontWeight.SemiBold);
                    var bg = new Rect(sg.Box.X + 8, sg.Box.Y + 4, tt.Width + 12, tt.Height + 4);
                    ctx.DrawRectangle(sgFill, null, bg, 4, 4);
                    ctx.DrawText(tt, new Point(sg.Box.X + 14, sg.Box.Y + 6));
                }
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
            var casingPen = new Pen(pillBg, 4.5) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var dotPen = new Pen(fill, 2);

            // Edge labels are collected and drawn in a second pass so connector lines never
            // strike through label text.
            var stLabels = new List<(Point At, string Text)>();

            // Draw edges (with back-edge loop routing for cycles)
            foreach (var e in _stEdges)
            {
                if (!_stNodes.TryGetValue(e.From, out var fn) || !_stNodes.TryGetValue(e.To, out var tn)) continue;

                var isBack = fn.Rank >= tn.Rank;
                var isSkip = !isBack && tn.Rank - fn.Rank > 1;
                Point from, to;
                Point labelAt;

                if (isBack)
                {
                    // Back edge: exit right side of source, loop right, enter right side of target
                    from = new Point(fn.X + fn.W, fn.Y + fn.H / 2);
                    to = new Point(tn.X + tn.W, tn.Y + tn.H / 2);
                    DrawBackEdge(ctx, from, to, linePen, lineBr, _layW);
                    labelAt = new Point(_layW - 30, (from.Y + to.Y) / 2);
                }
                else if (isSkip)
                {
                    // Long "skip" edge spanning multiple rows: orthogonal route — drop into the
                    // clear gap below the source, jog across, then straight down the target's
                    // (outer) column. Keeps long connectors off the intermediate nodes.
                    from = new Point(fn.X + fn.W / 2, fn.Y + fn.H);
                    to = new Point(tn.X + tn.W / 2, tn.Y);
                    double jogY = fn.Y + fn.H + NGapV * 0.5;
                    var pts = new List<Point>
                    {
                        from,
                        new(from.X, jogY),
                        new(to.X, jogY),
                        new(to.X, to.Y - ArrSz * 0.3),
                    };
                    var geo = RoundedPolyline(pts, 12);
                    ctx.DrawGeometry(null, casingPen, geo);
                    ctx.DrawGeometry(null, linePen, geo);
                    DrawArrowHead(ctx, to, Math.PI / 2, lineBr);
                    labelAt = new Point(to.X, (jogY + to.Y) / 2);
                }
                else
                {
                    // Forward edge: bottom of source to top of target
                    from = new Point(fn.X + fn.W / 2, fn.Y + fn.H);
                    to = new Point(tn.X + tn.W / 2, tn.Y);
                    DrawBezierEdge(ctx, from, to, linePen, true, true, lineBr);
                    labelAt = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
                }

                if (!string.IsNullOrWhiteSpace(e.Label))
                    stLabels.Add((labelAt, e.Label!));
            }

            // Label pass — drawn over all edges for legibility.
            foreach (var (at, text) in stLabels)
            {
                var ft = Txt(text, FsSmall, labelBr);
                var pillR = new Rect(at.X - ft.Width / 2 - 6, at.Y - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                ctx.DrawRectangle(pillBg, null, pillR, 8, 8);
                ctx.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
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

            var borderPen = new Pen(border, 1.4);
            var linePen = new Pen(lineBr, 1.3) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var gridPen = new Pen(border, 1.0);
            var zebraBr = new SolidColorBrush(Color.FromArgb(7, 255, 255, 255));
            var casingPen = new Pen(pillBg, 4.5) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

            const double erFieldH = 24;
            const double erHeaderH = 32;
            const double erCellPadH = 11;

            // Draw relationships first (behind entities), orthogonal rounded routing.
            foreach (var rel in _erRelations)
            {
                if (!_erEntities.TryGetValue(rel.From, out var fromEnt) ||
                    !_erEntities.TryGetValue(rel.To, out var toEnt) || ReferenceEquals(fromEnt, toEnt))
                    continue;

                bool fromAbove = fromEnt.Y + fromEnt.H <= toEnt.Y + 2;
                bool toAbove = toEnt.Y + toEnt.H <= fromEnt.Y + 2;

                List<Point> pts;
                if (fromAbove || toAbove)
                {
                    var upper = fromAbove ? fromEnt : toEnt;
                    var lower = fromAbove ? toEnt : fromEnt;
                    double upX = Math.Clamp(lower.X + lower.W / 2, upper.X + 18, upper.X + upper.W - 18);
                    double loX = Math.Clamp(upper.X + upper.W / 2, lower.X + 18, lower.X + lower.W - 18);
                    double midY = (upper.Y + upper.H + lower.Y) / 2;
                    pts = new List<Point>
                    {
                        new(upX, upper.Y + upper.H),
                        new(upX, midY),
                        new(loX, midY),
                        new(loX, lower.Y)
                    };
                    if (!fromAbove) pts.Reverse();
                }
                else
                {
                    var left = fromEnt.X <= toEnt.X ? fromEnt : toEnt;
                    var right = ReferenceEquals(left, fromEnt) ? toEnt : fromEnt;
                    double ly = left.Y + left.H / 2, ry = right.Y + right.H / 2;
                    double midX = (left.X + left.W + right.X) / 2;
                    pts = new List<Point>
                    {
                        new(left.X + left.W, ly),
                        new(midX, ly),
                        new(midX, ry),
                        new(right.X, ry)
                    };
                    if (!ReferenceEquals(left, fromEnt)) pts.Reverse();
                }

                var geo = RoundedPolyline(pts, 10);
                ctx.DrawGeometry(null, casingPen, geo);
                ctx.DrawGeometry(null, linePen, geo);

                // Cardinality glyphs: pts[0] = From end, pts[^1] = To end. The glyph nearest
                // each entity is the token character adjacent to that entity, so the To-end
                // token is read in reverse.
                DrawCardinality(ctx, pts[0], pts[1], rel.LeftCard, lineBr, textSec);
                DrawCardinality(ctx, pts[^1], pts[^2], Reverse2(rel.RightCard), lineBr, textSec);

                if (!string.IsNullOrWhiteSpace(rel.Label))
                {
                    var mid = pts[pts.Count / 2];
                    var ft = Txt(rel.Label, FsSmall, labelBr);
                    var pillR = new Rect(mid.X - ft.Width / 2 - 6, mid.Y - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 6, 6);
                    ctx.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
                }
            }

            // Draw entities as proper tables: header row + (type | name | key) grid.
            foreach (var ent in _erEntities.Values)
            {
                var rect = new Rect(ent.X, ent.Y, ent.W, ent.H);

                // Body + outer border
                ctx.DrawRectangle(fill, borderPen, rect, 8, 8);

                // Header background
                var headerRect = new Rect(ent.X, ent.Y, ent.W, erHeaderH);
                using (ctx.PushClip(new RoundedRect(rect, 8)))
                    ctx.DrawRectangle(headerBg, null, headerRect);

                // Accent top stripe
                using (ctx.PushClip(new RoundedRect(rect, 8)))
                    ctx.DrawRectangle(accent, null, new Rect(ent.X + 1, ent.Y + 1, ent.W - 2, 2.5));

                // Header text (centered)
                var headerFt = Txt(ent.Name, Fs, textBr, FontWeight.SemiBold);
                ctx.DrawText(headerFt, new Point(ent.X + (ent.W - headerFt.Width) / 2, ent.Y + erHeaderH / 2 - headerFt.Height / 2));

                if (ent.Fields.Count == 0) continue;

                // Column geometry — the name column absorbs any slack so cells fill the width.
                double typeColW = ent.ColTypeW + erCellPadH * 2;
                double keyColW = ent.HasKeyCol ? ent.ColKeyW + erCellPadH * 2 : 0;
                double xType = ent.X;
                double xName = ent.X + typeColW;
                double xKey = ent.X + ent.W - keyColW;
                double bodyTop = ent.Y + erHeaderH;
                double bodyBottom = ent.Y + ent.H;

                using (ctx.PushClip(new RoundedRect(rect, 8)))
                {
                    // Zebra striping for odd rows
                    for (int fi = 0; fi < ent.Fields.Count; fi++)
                        if ((fi & 1) == 1)
                            ctx.DrawRectangle(zebraBr, null, new Rect(ent.X, bodyTop + fi * erFieldH, ent.W, erFieldH));

                    // Header divider
                    ctx.DrawLine(gridPen, new Point(ent.X, bodyTop), new Point(ent.X + ent.W, bodyTop));

                    // Field rows
                    for (int fi = 0; fi < ent.Fields.Count; fi++)
                    {
                        var field = ent.Fields[fi];
                        double rowTop = bodyTop + fi * erFieldH;
                        double rowMidY = rowTop + erFieldH / 2;

                        var typeFt = Txt(field.Type, FsSmall, textSec);
                        ctx.DrawText(typeFt, new Point(xType + erCellPadH, rowMidY - typeFt.Height / 2));

                        var nameFt = Txt(field.Name, FsSmall, textBr);
                        ctx.DrawText(nameFt, new Point(xName + erCellPadH, rowMidY - nameFt.Height / 2));

                        if (ent.HasKeyCol && !string.IsNullOrEmpty(field.Constraint))
                        {
                            var keyFt = Txt(field.Constraint, FsSmall, constraintBr, FontWeight.SemiBold);
                            ctx.DrawText(keyFt, new Point(xKey + erCellPadH, rowMidY - keyFt.Height / 2));
                        }

                        if (fi < ent.Fields.Count - 1)
                            ctx.DrawLine(gridPen, new Point(ent.X, rowTop + erFieldH), new Point(ent.X + ent.W, rowTop + erFieldH));
                    }

                    // Column dividers
                    ctx.DrawLine(gridPen, new Point(xName, bodyTop), new Point(xName, bodyBottom));
                    if (ent.HasKeyCol)
                        ctx.DrawLine(gridPen, new Point(xKey, bodyTop), new Point(xKey, bodyBottom));
                }
            }
        }

        private static string Reverse2(string s) =>
            s.Length == 2 ? new string(new[] { s[1], s[0] }) : s;

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
            var divider = _owner.ResolveBrush("Brush.BorderDefault", Color.Parse("#3A3A3A"));
            var labelBr = _owner.ResolveBrush("Brush.TextSecondary", Color.Parse("#B0B0B0"));
            var pillBg = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#1A1A1A"));
            var hollow = _owner.ResolveBrush("Brush.Surface0", Color.Parse("#161618"));

            var borderPen = new Pen(border, 1.4);
            var linePen = new Pen(lineBr, 1.3);
            var dashedPen = new Pen(lineBr, 1.3) { DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0) };
            var divPen = new Pen(divider, 1.0);
            var casingPen = new Pen(hollow, 4.5) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

            const double cdHeaderH = 32;
            const double cdRowH = 19;
            const double cdSecPad = 6;
            const double cdPadH = 13;

            // ── Relations: orthogonal rounded routing with correct end markers ──
            foreach (var rel in _cdRelations)
            {
                if (!_cdClasses.TryGetValue(rel.From, out var fromCls) ||
                    !_cdClasses.TryGetValue(rel.To, out var toCls) || ReferenceEquals(fromCls, toCls))
                    continue;

                bool fromAbove = fromCls.Y + fromCls.H <= toCls.Y + 2;
                bool toAbove = toCls.Y + toCls.H <= fromCls.Y + 2;

                // The marker (triangle/diamond/arrow) belongs at the parent for
                // inheritance/realization, otherwise at the To end.
                var markerNode = rel.Type is CdRelType.Inheritance or CdRelType.Realization ? fromCls : toCls;
                double markerLen = rel.Type switch
                {
                    CdRelType.Inheritance or CdRelType.Realization => 11,
                    CdRelType.Composition or CdRelType.Aggregation => 15,
                    CdRelType.Association or CdRelType.Dependency => ArrSz * 0.9,
                    _ => 0
                };
                var pen = rel.Type is CdRelType.Dependency or CdRelType.Realization ? dashedPen : linePen;

                List<Point> pts;
                if (fromAbove || toAbove)
                {
                    var upper = fromAbove ? fromCls : toCls;
                    var lower = fromAbove ? toCls : fromCls;
                    double childX = Math.Clamp(upper.X + upper.W / 2,
                        lower.X + 16, lower.X + lower.W - 16);
                    double parentX = Math.Clamp(lower.X + lower.W / 2,
                        upper.X + 16, upper.X + upper.W - 16);
                    double childTop = lower.Y;
                    double parentBot = upper.Y + upper.H;
                    double midY = (childTop + parentBot) / 2;
                    // Ordered start(lower top) → end(upper bottom)
                    pts = new List<Point>
                    {
                        new(childX, childTop),
                        new(childX, midY),
                        new(parentX, midY),
                        new(parentX, parentBot)
                    };
                    // Orient so index 0 = From, last = To for marker logic below.
                    if (fromAbove) pts.Reverse();
                }
                else
                {
                    var left = fromCls.X <= toCls.X ? fromCls : toCls;
                    var right = ReferenceEquals(left, fromCls) ? toCls : fromCls;
                    double ly = left.Y + left.H / 2, ry = right.Y + right.H / 2;
                    double midX = (left.X + left.W + right.X) / 2;
                    pts = new List<Point>
                    {
                        new(left.X + left.W, ly),
                        new(midX, ly),
                        new(midX, ry),
                        new(right.X, ry)
                    };
                    if (!ReferenceEquals(left, fromCls)) pts.Reverse();
                }

                // pts[0] == From end, pts[^1] == To end.
                bool markerAtEnd = ReferenceEquals(markerNode, toCls);
                var draw = new List<Point>(pts);
                if (markerLen > 0)
                {
                    if (markerAtEnd)
                        draw[^1] = TrimToward(pts[^1], pts[^2], markerLen);
                    else
                        draw[0] = TrimToward(pts[0], pts[1], markerLen);
                }

                var geo = RoundedPolyline(draw, 9);
                ctx.DrawGeometry(null, casingPen, geo);
                ctx.DrawGeometry(null, pen, geo);

                if (markerLen > 0)
                {
                    var at = markerAtEnd ? pts[^1] : pts[0];
                    var toward = markerAtEnd ? pts[^2] : pts[1];
                    DrawCdRelMarker(ctx, at, toward, rel.Type, lineBr, hollow);
                }

                if (!string.IsNullOrWhiteSpace(rel.Label))
                {
                    var mid = draw[draw.Count / 2];
                    var ft = Txt(rel.Label, FsSmall, labelBr);
                    var pillR = new Rect(mid.X - ft.Width / 2 - 6, mid.Y - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
                    ctx.DrawRectangle(pillBg, null, pillR, 6, 6);
                    ctx.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
                }
            }

            // ── Class boxes ──
            foreach (var cls in _cdClasses.Values)
            {
                var rect = new Rect(cls.X, cls.Y, cls.W, cls.H);
                ctx.DrawRectangle(fill, borderPen, rect, 6, 6);

                var headerRect = new Rect(cls.X, cls.Y, cls.W, cdHeaderH);
                using (ctx.PushClip(new RoundedRect(rect, 6)))
                    ctx.DrawRectangle(headerBg, null, headerRect);
                using (ctx.PushClip(new RoundedRect(rect, 6)))
                    ctx.DrawRectangle(accent, null, new Rect(cls.X + 1, cls.Y + 1, cls.W - 2, 2.5));

                var nameFt = Txt(cls.Name, Fs, textBr, FontWeight.SemiBold);
                ctx.DrawText(nameFt, new Point(cls.X + cls.W / 2 - nameFt.Width / 2, cls.Y + cdHeaderH / 2 - nameFt.Height / 2));

                double fy = cls.Y + cdHeaderH;

                if (cls.Attributes.Count > 0)
                {
                    ctx.DrawLine(divPen, new Point(cls.X, fy), new Point(cls.X + cls.W, fy));
                    fy += cdSecPad;
                    foreach (var attr in cls.Attributes)
                    {
                        var ft = Txt(attr, FsSmall, textSec);
                        ctx.DrawText(ft, new Point(cls.X + cdPadH, fy + cdRowH / 2 - ft.Height / 2));
                        fy += cdRowH;
                    }
                    fy += cdSecPad;
                }

                if (cls.Methods.Count > 0)
                {
                    ctx.DrawLine(divPen, new Point(cls.X, fy), new Point(cls.X + cls.W, fy));
                    fy += cdSecPad;
                    foreach (var meth in cls.Methods)
                    {
                        var ft = Txt(meth, FsSmall, textBr);
                        ctx.DrawText(ft, new Point(cls.X + cdPadH, fy + cdRowH / 2 - ft.Height / 2));
                        fy += cdRowH;
                    }
                    fy += cdSecPad;
                }
            }
        }

        private static Point TrimToward(Point end, Point neighbor, double d)
        {
            var len = Dist(end, neighbor);
            if (len < 1e-3) return end;
            return new Point(end.X + (neighbor.X - end.X) / len * d,
                             end.Y + (neighbor.Y - end.Y) / len * d);
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

        // Distribute each node's edges across its border (fan-out / fan-in) instead of a single
        // centre port, ordered by the opposite endpoint's position to minimise crossings.
        // Compute orthogonal routes for every flowchart edge: classify each edge's exit/entry sides by
        // geometry, distribute ports along each side (fan-out/fan-in), separate parallel runs into lanes,
        // and emit rounded-polyline waypoints. This replaces the old diagonal port-to-port beziers that
        // cut across node bands and tangled — giving clean vertical-dominant connectors like mermaid.js.
        private void RouteFlowEdges()
        {
            var live = new List<FEdge>();
            foreach (var e in _fEdges)
            {
                e.Route.Clear();
                if (!_fNodes.TryGetValue(e.From, out var s) || !_fNodes.TryGetValue(e.To, out var t)) continue;
                ClassifyEdgeSides(e, s, t);
                live.Add(e);
            }

            // Distribute ports per (node, side) so multiple edges fan out across the border, ordered by
            // the cross-axis position of the opposite endpoint to keep their lines from crossing.
            var exitGroups = new Dictionary<(string, FSide), List<FEdge>>();
            var entryGroups = new Dictionary<(string, FSide), List<FEdge>>();
            foreach (var e in live)
            {
                AddToGroup(exitGroups, (e.From, e.ExitSide), e);
                AddToGroup(entryGroups, (e.To, e.EntrySide), e);
            }

            foreach (var ((id, side), list) in exitGroups)
            {
                if (!_fNodes.TryGetValue(id, out var n)) continue;
                list.Sort((a, b) => OtherCross(a, true, side).CompareTo(OtherCross(b, true, side)));
                for (int i = 0; i < list.Count; i++)
                    list[i].FromPort = PortPoint(n, side, i, list.Count);
            }
            foreach (var ((id, side), list) in entryGroups)
            {
                if (!_fNodes.TryGetValue(id, out var n)) continue;
                list.Sort((a, b) => OtherCross(a, false, side).CompareTo(OtherCross(b, false, side)));
                for (int i = 0; i < list.Count; i++)
                    list[i].ToPort = PortPoint(n, side, i, list.Count);
            }

            AssignLanesAndRoute(live);
        }

        private static void AddToGroup(Dictionary<(string, FSide), List<FEdge>> map, (string, FSide) key, FEdge e)
        {
            if (!map.TryGetValue(key, out var l)) map[key] = l = new();
            l.Add(e);
        }

        // Decide which border each edge leaves and enters from the relative node positions: forward
        // edges flow along the layout axis (bottom->top for TB), same-rank edges connect facing sides,
        // and back edges loop out of a side.
        private void ClassifyEdgeSides(FEdge e, FNode s, FNode t)
        {
            double sx = s.X + s.W / 2, sy = s.Y + s.H / 2;
            double tx = t.X + t.W / 2, ty = t.Y + t.H / 2;

            if (!_ltr)
            {
                if (ty >= sy + 8) { e.ExitSide = FSide.Bottom; e.EntrySide = FSide.Top; }
                else if (ty <= sy - 8)
                {
                    var right = (sx + tx) / 2 >= _layW / 2;
                    e.ExitSide = e.EntrySide = right ? FSide.Right : FSide.Left;
                }
                else if (tx >= sx) { e.ExitSide = FSide.Right; e.EntrySide = FSide.Left; }
                else { e.ExitSide = FSide.Left; e.EntrySide = FSide.Right; }
            }
            else
            {
                if (tx >= sx + 8) { e.ExitSide = FSide.Right; e.EntrySide = FSide.Left; }
                else if (tx <= sx - 8)
                {
                    var bottom = (sy + ty) / 2 >= _layH / 2;
                    e.ExitSide = e.EntrySide = bottom ? FSide.Bottom : FSide.Top;
                }
                else if (ty >= sy) { e.ExitSide = FSide.Bottom; e.EntrySide = FSide.Top; }
                else { e.ExitSide = FSide.Top; e.EntrySide = FSide.Bottom; }
            }
        }

        private double OtherCross(FEdge e, bool isExit, FSide side)
        {
            var otherId = isExit ? e.To : e.From;
            if (!_fNodes.TryGetValue(otherId, out var n)) return 0;
            return side is FSide.Top or FSide.Bottom ? n.X + n.W / 2 : n.Y + n.H / 2;
        }

        private static Point PortPoint(FNode n, FSide side, int index, int count)
        {
            var frac = (index + 1.0) / (count + 1.0);
            switch (side)
            {
                case FSide.Top:
                case FSide.Bottom:
                {
                    var inset = Math.Min(14, n.W * 0.30);
                    var x = n.X + inset + (n.W - 2 * inset) * frac;
                    return new Point(x, side == FSide.Bottom ? n.Y + n.H : n.Y);
                }
                default:
                {
                    var inset = Math.Min(12, n.H * 0.30);
                    var y = n.Y + inset + (n.H - 2 * inset) * frac;
                    return new Point(side == FSide.Right ? n.X + n.W : n.X, y);
                }
            }
        }

        // Build each edge's waypoint route, separating parallel connector runs into distinct lanes so
        // they read as a clean bus instead of overlapping. Forward edges jog through a lane in the gap
        // between bands; same-rank edges jog through a lane between the two nodes; back edges loop out
        // past the content on one side.
        private void AssignLanesAndRoute(List<FEdge> live)
        {
            const double laneStep = 8.0;

            // Forward edges: group by the inter-band channel (rounded mid coordinate) and spread their
            // jog lanes around the channel centre. Rank-skipping edges are handled separately below so
            // they don't run straight down a column occupied by an intermediate-rank node.
            var forward = live.Where(e => IsForward(e) && !IsLongForward(e)).ToList();
            var fGroups = forward.GroupBy(e => (int)Math.Round(MidAlong(e) / 14.0));
            foreach (var g in fGroups)
            {
                var items = g.OrderBy(e => CrossOf(e.FromPort)).ToList();
                var mid = items.Average(MidAlong);
                for (int i = 0; i < items.Count; i++)
                {
                    var lane = mid + (i - (items.Count - 1) / 2.0) * laneStep;
                    BuildForwardRoute(items[i], lane);
                }
            }

            // Rank-skipping forward edges: route each around the nodes in the intermediate rank(s)
            // through its own clear lane. Index per side so several don't stack on the same line.
            var longForward = live.Where(IsLongForward).ToList();
            int longBeforeIdx = 0, longAfterIdx = 0;
            foreach (var e in longForward.OrderBy(MidAlong))
                BuildLongForwardRoute(e, LongTargetIsBefore(e) ? longBeforeIdx++ : longAfterIdx++);

            // Same-rank edges: lane between the facing sides.
            var side = live.Where(IsSide).ToList();
            var sGroups = side.GroupBy(e => (int)Math.Round(MidAlong(e) / 14.0));
            foreach (var g in sGroups)
            {
                var items = g.OrderBy(e => CrossOf(e.FromPort)).ToList();
                var mid = items.Average(MidAlong);
                for (int i = 0; i < items.Count; i++)
                {
                    var lane = mid + (i - (items.Count - 1) / 2.0) * laneStep;
                    BuildForwardRoute(items[i], lane);
                }
            }

            // Back edges: loop out past the content on the chosen side, indexed so multiple don't overlap.
            var back = live.Where(IsBack).ToList();
            int leftIdx = 0, rightIdx = 0, topIdx = 0, botIdx = 0;
            foreach (var e in back)
            {
                int idx = e.ExitSide switch
                {
                    FSide.Right => rightIdx++,
                    FSide.Left => leftIdx++,
                    FSide.Top => topIdx++,
                    _ => botIdx++,
                };
                BuildBackRoute(e, idx);
            }

            foreach (var e in live)
                if (e.Route.Count == 0)
                    e.Route = new List<Point> { e.FromPort, e.ToPort };
        }

        private bool IsForward(FEdge e) => !_ltr
            ? e.ExitSide == FSide.Bottom && e.EntrySide == FSide.Top
            : e.ExitSide == FSide.Right && e.EntrySide == FSide.Left;

        private bool IsSide(FEdge e) => !_ltr
            ? (e.ExitSide is FSide.Left or FSide.Right) && (e.EntrySide is FSide.Left or FSide.Right)
            : (e.ExitSide is FSide.Top or FSide.Bottom) && (e.EntrySide is FSide.Top or FSide.Bottom);

        private bool IsBack(FEdge e) => e.ExitSide == e.EntrySide;

        // The coordinate of the lane channel an edge jogs through (Y for TB, X for LR).
        private double MidAlong(FEdge e) => _ltr
            ? (e.FromPort.X + e.ToPort.X) / 2
            : (e.FromPort.Y + e.ToPort.Y) / 2;

        private double CrossOf(Point p) => _ltr ? p.Y : p.X;

        private void BuildForwardRoute(FEdge e, double lane)
        {
            var f = e.FromPort;
            var t = e.ToPort;
            if (!_ltr)
            {
                lane = Math.Clamp(lane, Math.Min(f.Y, t.Y) + 4, Math.Max(f.Y, t.Y) - 4);
                if (Math.Abs(f.X - t.X) < 1.5)
                    e.Route = new List<Point> { f, t };
                else
                    e.Route = new List<Point> { f, new(f.X, lane), new(t.X, lane), t };
            }
            else
            {
                lane = Math.Clamp(lane, Math.Min(f.X, t.X) + 4, Math.Max(f.X, t.X) - 4);
                if (Math.Abs(f.Y - t.Y) < 1.5)
                    e.Route = new List<Point> { f, t };
                else
                    e.Route = new List<Point> { f, new(lane, f.Y), new(lane, t.Y), t };
            }
            e.LabelAt = LongestSegMid(e.Route);
        }

        // A forward edge that skips one or more ranks (TB: source two-plus rows above target) would,
        // with the plain forward route, run straight down the source or target column and pass *behind*
        // whatever node occupies the intermediate rank. Treat those like dagre's virtual-node edges:
        // detect them by rank span so they can be lifted into their own obstacle-free lane.
        private bool IsLongForward(FEdge e)
        {
            if (_fSubgraphs.Count > 0) return false; // clustered (architecture) layout routes its own way
            if (!IsForward(e)) return false;
            if (!_fNodes.TryGetValue(e.From, out var s) || !_fNodes.TryGetValue(e.To, out var t)) return false;
            return Math.Abs(t.Rank - s.Rank) > 1;
        }

        // True when the long edge's target sits before its source on the cross axis (left for TB, up for
        // LR) — i.e. which side of the obstacle column the routed lane should hug.
        private bool LongTargetIsBefore(FEdge e)
        {
            if (!_fNodes.TryGetValue(e.From, out var s) || !_fNodes.TryGetValue(e.To, out var t)) return true;
            return !_ltr
                ? (t.X + t.W / 2) <= (s.X + s.W / 2)
                : (t.Y + t.H / 2) <= (s.Y + s.H / 2);
        }

        // Route a rank-skipping forward edge around the intermediate-rank nodes: leave the source, run
        // down (TB) / across (LR) a lane positioned to clear every obstacle, then jog into the target.
        // The lane prefers the target column, then the source column, else steps just outside the
        // obstacles on the side the target is on (offset by idx so stacked long edges stay distinct).
        private void BuildLongForwardRoute(FEdge e, int idx)
        {
            if (!_fNodes.TryGetValue(e.From, out var s) || !_fNodes.TryGetValue(e.To, out var t)) return;
            var f = e.FromPort;
            var tp = e.ToPort;
            const double margin = 14, step = 14;
            int loR = Math.Min(s.Rank, t.Rank), hiR = Math.Max(s.Rank, t.Rank);
            var inter = _fNodes.Values.Where(n => n.Rank > loR && n.Rank < hiR).ToList();

            if (!_ltr)
            {
                double sCx = s.X + s.W / 2, tCx = t.X + t.W / 2;
                bool Clear(double x) => inter.All(n => x < n.X - margin || x > n.X + n.W + margin);
                double laneX;
                if (idx == 0 && Clear(tp.X)) laneX = tp.X;
                else if (idx == 0 && Clear(f.X)) laneX = f.X;
                else
                {
                    bool before = tCx <= sCx;
                    double edge = before
                        ? (inter.Count > 0 ? inter.Min(n => n.X) - margin : Math.Min(f.X, tp.X) - margin)
                        : (inter.Count > 0 ? inter.Max(n => n.X + n.W) + margin : Math.Max(f.X, tp.X) + margin);
                    laneX = edge + (before ? -1 : 1) * idx * step;
                }

                double interTop = inter.Count > 0 ? inter.Min(n => n.Y) : tp.Y;
                double interBot = inter.Count > 0 ? inter.Max(n => n.Y + n.H) : f.Y;
                double yA = (f.Y + interTop) / 2;
                double yB = (interBot + tp.Y) / 2;
                if (yB <= yA + 4) { yA = f.Y + 12; yB = Math.Max(yA + 8, tp.Y - 12); }

                var pts = new List<Point> { f };
                if (Math.Abs(laneX - f.X) > 1) pts.Add(new Point(f.X, yA));
                pts.Add(new Point(laneX, yA));
                pts.Add(new Point(laneX, yB));
                if (Math.Abs(laneX - tp.X) > 1) pts.Add(new Point(tp.X, yB));
                pts.Add(tp);
                e.Route = pts;
            }
            else
            {
                double sCy = s.Y + s.H / 2, tCy = t.Y + t.H / 2;
                bool Clear(double y) => inter.All(n => y < n.Y - margin || y > n.Y + n.H + margin);
                double laneY;
                if (idx == 0 && Clear(tp.Y)) laneY = tp.Y;
                else if (idx == 0 && Clear(f.Y)) laneY = f.Y;
                else
                {
                    bool before = tCy <= sCy;
                    double edge = before
                        ? (inter.Count > 0 ? inter.Min(n => n.Y) - margin : Math.Min(f.Y, tp.Y) - margin)
                        : (inter.Count > 0 ? inter.Max(n => n.Y + n.H) + margin : Math.Max(f.Y, tp.Y) + margin);
                    laneY = edge + (before ? -1 : 1) * idx * step;
                }

                double interLeft = inter.Count > 0 ? inter.Min(n => n.X) : tp.X;
                double interRight = inter.Count > 0 ? inter.Max(n => n.X + n.W) : f.X;
                double xA = (f.X + interLeft) / 2;
                double xB = (interRight + tp.X) / 2;
                if (xB <= xA + 4) { xA = f.X + 12; xB = Math.Max(xA + 8, tp.X - 12); }

                var pts = new List<Point> { f };
                if (Math.Abs(laneY - f.Y) > 1) pts.Add(new Point(xA, f.Y));
                pts.Add(new Point(xA, laneY));
                pts.Add(new Point(xB, laneY));
                if (Math.Abs(laneY - tp.Y) > 1) pts.Add(new Point(xB, tp.Y));
                pts.Add(tp);
                e.Route = pts;
            }
            e.LabelAt = LongestSegMid(e.Route);
        }

        private void BuildBackRoute(FEdge e, int idx)
        {
            var f = e.FromPort;
            var t = e.ToPort;
            const double margin = 18;
            if (e.ExitSide == FSide.Right)
            {
                var laneX = Math.Max(f.X, t.X) + margin + idx * 12;
                e.Route = new List<Point> { f, new(laneX, f.Y), new(laneX, t.Y), t };
            }
            else if (e.ExitSide == FSide.Left)
            {
                var laneX = Math.Min(f.X, t.X) - margin - idx * 12;
                e.Route = new List<Point> { f, new(laneX, f.Y), new(laneX, t.Y), t };
            }
            else if (e.ExitSide == FSide.Bottom)
            {
                var laneY = Math.Max(f.Y, t.Y) + margin + idx * 12;
                e.Route = new List<Point> { f, new(f.X, laneY), new(t.X, laneY), t };
            }
            else
            {
                var laneY = Math.Min(f.Y, t.Y) - margin - idx * 12;
                e.Route = new List<Point> { f, new(f.X, laneY), new(t.X, laneY), t };
            }
            e.LabelAt = LongestSegMid(e.Route);
        }

        private static Point LongestSegMid(List<Point> pts)
        {
            if (pts.Count < 2) return pts.Count == 1 ? pts[0] : default;
            double best = -1; Point mid = default;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var d = Dist(pts[i], pts[i + 1]);
                if (d > best) { best = d; mid = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2); }
            }
            return mid;
        }

        // Draw an orthogonal edge as a rounded polyline with an optional background casing (so crossings
        // read as a weave) and an arrowhead aligned to the final segment.
        private static void DrawOrthEdge(DrawingContext ctx, List<Point> pts, Pen pen, Pen? casing,
            bool hasArrow, IBrush arrowBrush)
        {
            if (pts.Count < 2) return;

            var draw = new List<Point>(pts);
            double arrAngle = 0;
            if (hasArrow)
            {
                var a = draw[^2]; var b = draw[^1];
                arrAngle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                var segLen = Dist(a, b);
                var pull = Math.Min(ArrSz * 0.8, segLen * 0.5);
                draw[^1] = new Point(b.X - pull * Math.Cos(arrAngle), b.Y - pull * Math.Sin(arrAngle));
            }

            var geo = RoundedPolyline(draw, 9);
            if (casing != null)
                ctx.DrawGeometry(null, casing, geo);
            ctx.DrawGeometry(null, pen, geo);

            if (hasArrow)
                DrawArrowHead(ctx, pts[^1], arrAngle, arrowBrush);
        }

        // A polyline with its interior corners rounded by up to <paramref name="radius"/>, using a
        // quadratic through each corner so right-angle connectors look smooth, not boxy.
        private static StreamGeometry RoundedPolyline(List<Point> pts, double radius)
        {
            var geo = new StreamGeometry();
            using var gc = geo.Open();
            gc.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var prev = pts[i - 1];
                var cur = pts[i];
                var next = pts[i + 1];
                var len1 = Dist(prev, cur);
                var len2 = Dist(cur, next);
                var r = Math.Min(radius, Math.Min(len1, len2) / 2);
                if (r < 0.5) { gc.LineTo(cur); continue; }
                var d1x = (cur.X - prev.X) / Math.Max(len1, 0.001);
                var d1y = (cur.Y - prev.Y) / Math.Max(len1, 0.001);
                var d2x = (next.X - cur.X) / Math.Max(len2, 0.001);
                var d2y = (next.Y - cur.Y) / Math.Max(len2, 0.001);
                gc.LineTo(new Point(cur.X - d1x * r, cur.Y - d1y * r));
                gc.QuadraticBezierTo(cur, new Point(cur.X + d2x * r, cur.Y + d2y * r));
            }
            gc.LineTo(pts[^1]);
            gc.EndFigure(false);
            return geo;
        }

        // Midpoint (t=0.5) of the same cubic bezier DrawBezierEdge draws, for label placement.
        private Point BezierMid(Point from, Point to, double offsetFactor = 0.35, double maxOffset = 50)
        {
            var dist = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            var offset = Math.Clamp(dist * offsetFactor, 12, maxOffset);
            Point cp1, cp2;
            if (!_ltr)
            {
                cp1 = new Point(from.X, from.Y + offset);
                cp2 = new Point(to.X, to.Y - offset);
            }
            else
            {
                cp1 = new Point(from.X + offset, from.Y);
                cp2 = new Point(to.X - offset, to.Y);
            }
            return new Point(
                (from.X + 3 * cp1.X + 3 * cp2.X + to.X) / 8,
                (from.Y + 3 * cp1.Y + 3 * cp2.Y + to.Y) / 8);
        }

        private static void DrawBezierEdge(DrawingContext ctx, Point from, Point to, Pen pen,
            bool hasArrow, bool vertical, IBrush arrowBrush, Pen? casing = null,
            double offsetFactor = 0.35, double maxOffset = 50)
        {
            var dist = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            var offset = Math.Clamp(dist * offsetFactor, 12, maxOffset);

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

            // Background-coloured casing drawn first so later edges visually "cut" earlier ones at
            // crossings (a weave effect), turning a tangle of overlapping lines into legible strands.
            if (casing != null)
                ctx.DrawGeometry(null, casing, geo);
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
            return new FormattedText(text, CultureInfo.CurrentUICulture, MermaidTextHelper.GetFlowDirection(text),
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

        // Resolve one side of an edge into node ids, expanding mermaid "&" groups
        // (A & B) and remapping any subgraph reference to its first/last member.
        private static List<string> ResolveEndpoints(string segment, Dictionary<string, FSubgraph> subgraphs, bool trailing)
        {
            var ids = new List<string>();
            foreach (var piece in segment.Split('&'))
            {
                var id = trailing ? LastWord(piece) : FirstWord(piece);
                if (id.Length == 0) continue;

                if (subgraphs.TryGetValue(id, out var sg))
                {
                    var mapped = trailing ? sg.LastNode : sg.FirstNode;
                    if (!string.IsNullOrWhiteSpace(mapped)) id = mapped!;
                }

                ids.Add(id);
            }
            return ids;
        }

        private static readonly Regex BareNodeIdRegex = new($"^{NodeIdPattern}$", RegexOptions.Compiled);

        // All node ids referenced on a (bracket-stripped) line: bracketed definitions, bare id
        // listings, and edge endpoints. Used to assign subgraph membership.
        private static IEnumerable<string> CollectLineNodeRefs(string stripped)
        {
            var s = Regex.Replace(stripped, @"\|[^|]*\|", " ");                       // drop edge labels
            s = Regex.Replace(s, @"<-\.->|<==>|<-->|<--|-\.->|==>|-->|---|&", " ");   // drop arrows / &
            foreach (var tok in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Any(char.IsLetterOrDigit) && BareNodeIdRegex.IsMatch(tok))
                    yield return tok;
            }
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
