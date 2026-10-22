using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace StrataTheme.Controls;

public partial class StrataMermaid
{
    private sealed partial class MermaidCanvas
    {
        // ── Flowchart layout ───────────────────────────────────
        //
        // A layered (Sugiyama-style) layout. The diagram root and every subgraph is a level laid out as its own
        // layered graph whose units are its direct nodes and its child subgraphs, each child laid out first into
        // a box. An edge that crosses a subgraph border enters or leaves through a port on the box edge, so boxes
        // never overlap and a line never passes through a box it does not belong to.
        //
        // Ranks are doubled: units sit on even ranks and every edge spans at least two, so the rank between them
        // holds the edge's label (sized like a node) and a long edge gets a channel in every rank it passes.
        // Barycentre sweeps reduce crossings, isotonic regression places each rank towards its neighbours, and
        // each jog of an orthogonal route gets its own lane in the gap between ranks — so labels, nodes and lines
        // never sit on top of one another.

        private const double FlowHalfGap = 18, FlowHalfGapLr = 20;   // rank band to the label band between ranks
        private const double FlowNodeSep = 30, FlowNodeSepLr = 22;   // between neighbouring units in a rank
        private const double FlowEdgeSep = 14;                       // between an edge channel and its neighbours
        private const double FlowLaneStep = 8, FlowPortSep = FlowEdgeSep, FlowMinJog = 10, FlowLaneMargin = 12, FlowSnapSep = 8;
        private const double FlowLabelWrap = 140;

        private sealed class FlowLevel
        {
            public FSubgraph? Group;
            public FlowLevel? Parent;
            public readonly List<FlowUnit> Units = new();
            public readonly Dictionary<object, FlowUnit> UnitOf = new();
            public readonly List<FlowEdgeInfo> LcaEdges = new(), InEdges = new(), OutEdges = new();
            public readonly List<FlowChain> Chains = new();
            public readonly Dictionary<FEdge, double> InPorts = new(), OutPorts = new();
            public double SizeO, SizeR; // box extent along the order and rank axes
            public Rect TitleLocal;
        }

        private sealed class FlowUnit
        {
            public FNode? Node;
            public FlowLevel? Child;
            public int Rank, Appearance;
            public double LocalX, LocalY;
            public FlowItem? Item;
        }

        private sealed class FlowItem
        {
            public int Rank, Order;
            public double Pos, Size, Thick; // order-axis centre, order-axis extent, rank-axis extent
            public double? Hint;            // a border port's order outside the box, which fixes its rank's order
            public double? Tie;             // where an edge leaving or entering the box goes: breaks order ties
            public FlowUnit? Unit;
            public bool IsLabel;
            public readonly List<FlowLink> Up = new(), Down = new();
        }

        private sealed class FlowLink
        {
            public FlowItem Upper = null!, Lower = null!;
            public double UpperOff, LowerOff; // port offsets from the centre of a subgraph box
            public double UpperAt, LowerAt;   // order-axis attachment points once ports are assigned
            public int Lane = -1;
            public int Index;
        }

        private sealed class FlowChain
        {
            public readonly List<FlowItem> Items = new();
            public readonly List<FlowLink> Links = new();
            public FlowItem? Label;
            public readonly List<Point> Points = new();
            public Point LabelAt;
        }

        private sealed class FlowEdgeInfo
        {
            public FEdge Edge = null!;
            public FlowLevel Lca = null!;
            public bool Reversed; // laid out To -> From to break a cycle
            public readonly Dictionary<FlowLevel, FlowChain> Chains = new();
            public string Upper => Reversed ? Edge.To : Edge.From;
            public string Lower => Reversed ? Edge.From : Edge.To;
        }

        private void LayoutFlow(double availW)
        {
            foreach (var group in _fSubgraphs) { group.Box = default; group.TitleRect = default; }
            foreach (var edge in _fEdges) { edge.Route.Clear(); edge.LabelAt = default; edge.DisplayLabel = null; }
            if (_fNodes.Count == 0) { _layW = _layH = 0; return; }

            MeasureFlowNodes(availW);
            foreach (var edge in _fEdges)
                if (!string.IsNullOrWhiteSpace(edge.Label))
                    edge.DisplayLabel = WrapNodeText(edge.Label, FsSmall, FlowLabelWrap);

            try
            {
                LayoutFlowLayers();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A diagram must never take the window down: show its nodes in a plain grid instead.
                Avalonia.Logging.Logger.TryGet(Avalonia.Logging.LogEventLevel.Warning, "Mermaid")
                    ?.Log(this, "Flowchart layout failed; showing its nodes without links: {Error}", ex);
                LayoutFlowGrid(availW);
            }
        }

        private void LayoutFlowGrid(double availW)
        {
            foreach (var group in _fSubgraphs) { group.Box = default; group.TitleRect = default; }
            foreach (var edge in _fEdges) edge.Route.Clear();
            double x = 0, y = 0, rowH = 0;
            foreach (var node in _fNodes.Values)
            {
                if (x > 0 && x + node.W > availW - Pad * 2)
                {
                    x = 0;
                    y += rowH + FlowNodeSep;
                    rowH = 0;
                }
                node.X = x;
                node.Y = y;
                x += node.W + FlowNodeSep;
                rowH = Math.Max(rowH, node.H);
            }
            NormalizeFlowLayout();
        }

        private void LayoutFlowLayers()
        {
            var appearance = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in _fNodes.Keys) appearance[id] = appearance.Count;
            var nodeLevel = new Dictionary<string, FlowLevel>(StringComparer.Ordinal);
            var levels = new List<FlowLevel>();
            var root = new FlowLevel();
            BuildFlowLevel(root, _fNodes.Values.ToList(), levels, nodeLevel, appearance);

            var infos = new List<FlowEdgeInfo>();
            foreach (var edge in _fEdges)
            {
                if (edge.From == edge.To ||
                    !nodeLevel.TryGetValue(edge.From, out var fromLevel) ||
                    !nodeLevel.TryGetValue(edge.To, out var toLevel))
                    continue;
                var info = new FlowEdgeInfo { Edge = edge, Lca = CommonFlowLevel(fromLevel, toLevel) };
                info.Lca.LcaEdges.Add(info);
                infos.Add(info);
            }

            foreach (var level in levels)
                RankFlowLevel(level, nodeLevel);

            foreach (var info in infos)
            {
                for (var level = nodeLevel[info.Upper]; level != info.Lca; level = level.Parent!) level.OutEdges.Add(info);
                for (var level = nodeLevel[info.Lower]; level != info.Lca; level = level.Parent!) level.InEdges.Add(info);
            }

            // Children first: a parent needs its subgraph boxes' sizes and border ports. Later passes order each box's
            // entry ports by where their sources landed, top-down, so lines between boxes don't cross.
            Dictionary<(FlowLevel, FEdge), double>? hints = null;
            for (var pass = 0; pass < (_fSubgraphs.Count > 0 ? 3 : 1); pass++)
            {
                foreach (var level in levels)
                {
                    level.Chains.Clear();
                    level.InPorts.Clear();
                    level.OutPorts.Clear();
                }
                foreach (var info in infos) info.Chains.Clear();

                for (var i = levels.Count - 1; i >= 0; i--)
                    LayoutFlowLevel(levels[i], nodeLevel, hints);
                PlaceFlowLevel(root, 0, 0);

                // Entry ports follow where each edge departs at the level both ends share; exit channels break ties by
                // where it arrives there.
                hints = new Dictionary<(FlowLevel, FEdge), double>();
                foreach (var info in infos)
                {
                    var points = info.Chains[info.Lca].Points;
                    if (points.Count == 0) continue;
                    double depart = _ltr ? points[0].Y : points[0].X, arrive = _ltr ? points[^1].Y : points[^1].X;
                    for (var level = nodeLevel[info.Upper]; level != info.Lca; level = level.Parent!) hints[(level, info.Edge)] = arrive;
                    for (var level = nodeLevel[info.Lower]; level != info.Lca; level = level.Parent!) hints[(level, info.Edge)] = depart;
                }
            }

            foreach (var info in infos)
                ComposeFlowRoute(info, nodeLevel);

            NormalizeFlowLayout();
        }

        private void MeasureFlowNodes(double availW)
        {
            var maxNodeTextWidth = Math.Clamp(availW * 0.32, 120, 190);
            var lineHeight = Txt("Ag", Fs).Height;
            foreach (var node in _fNodes.Values)
            {
                node.DisplayText = WrapNodeText(node.Text, Fs, maxNodeTextWidth);
                var lines = node.DisplayText.Split('\n');
                var maxLineW = 0.0;
                foreach (var line in lines)
                    maxLineW = Math.Max(maxLineW, Txt(line, Fs).Width);

                var textH = lines.Length * lineHeight + Math.Max(0, (lines.Length - 1) * 2);
                node.W = Math.Max(NMinW, maxLineW + NPadH * 2);
                node.H = node.Shape == NShape.Diamond
                    ? Math.Max(NH + 8, textH + NPadV * 2)
                    : Math.Max(NH, textH + NPadV * 2);
                // Text must fit inside the shape, not just its bounding box.
                if (node.Shape == NShape.Diamond)
                    node.W = Math.Max(node.W, maxLineW * 1.5 + NPadH);
                else if (node.Shape == NShape.Circle)
                    node.W = node.H = Math.Max(NH, Math.Sqrt(maxLineW * maxLineW + textH * textH) + 12);
            }
        }

        // A node belongs to the first subgraph that lists it (as in Mermaid); nested subgraphs claim from their parent.
        private void BuildFlowLevel(FlowLevel level, List<FNode> members, List<FlowLevel> levels,
            Dictionary<string, FlowLevel> nodeLevel, Dictionary<string, int> appearance)
        {
            levels.Add(level);
            var memberIds = new HashSet<string>(members.Select(member => member.Id), StringComparer.Ordinal);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in _fSubgraphs)
            {
                if (!ReferenceEquals(group.Parent, level.Group)) continue;
                var own = new List<FNode>();
                foreach (var id in group.NodeOrder)
                    if (memberIds.Contains(id) && claimed.Add(id))
                        own.Add(_fNodes[id]);
                if (own.Count == 0) continue;

                var child = new FlowLevel { Group = group, Parent = level };
                var unit = new FlowUnit { Child = child, Appearance = own.Min(node => appearance[node.Id]) };
                level.Units.Add(unit);
                level.UnitOf[child] = unit;
                BuildFlowLevel(child, own, levels, nodeLevel, appearance);
            }

            foreach (var node in members)
            {
                if (claimed.Contains(node.Id)) continue;
                var unit = new FlowUnit { Node = node, Appearance = appearance[node.Id] };
                level.Units.Add(unit);
                level.UnitOf[node] = unit;
                nodeLevel[node.Id] = level;
            }

            level.Units.Sort((first, second) => first.Appearance.CompareTo(second.Appearance));
        }

        private static FlowLevel CommonFlowLevel(FlowLevel first, FlowLevel second)
        {
            var seen = new HashSet<FlowLevel>();
            for (var level = first; level is not null; level = level.Parent) seen.Add(level);
            for (var level = second; level is not null; level = level.Parent)
                if (seen.Contains(level)) return level;
            return first;
        }

        private FlowUnit FlowUnitAt(FlowLevel level, string nodeId, Dictionary<string, FlowLevel> nodeLevel)
        {
            var ancestor = nodeLevel[nodeId];
            if (ancestor == level) return level.UnitOf[_fNodes[nodeId]];
            while (ancestor.Parent != level) ancestor = ancestor.Parent!;
            return level.UnitOf[ancestor];
        }

        // Break cycles (DFS back edges are laid out reversed), then rank units by longest path with every edge
        // spanning two ranks, and pull sources down next to their first successor.
        private void RankFlowLevel(FlowLevel level, Dictionary<string, FlowLevel> nodeLevel)
        {
            var units = level.Units;
            var count = units.Count;
            var index = new Dictionary<FlowUnit, int>();
            for (var i = 0; i < count; i++) index[units[i]] = i;

            var pairs = new List<(FlowEdgeInfo Info, int From, int To)>();
            var adj = new List<int>[count];
            for (var i = 0; i < count; i++) adj[i] = new List<int>();
            foreach (var info in level.LcaEdges)
            {
                var fromIndex = index[FlowUnitAt(level, info.Edge.From, nodeLevel)];
                var toIndex = index[FlowUnitAt(level, info.Edge.To, nodeLevel)];
                pairs.Add((info, fromIndex, toIndex));
                adj[fromIndex].Add(toIndex);
            }

            var state = new byte[count];
            var back = new HashSet<(int, int)>();
            void Visit(int unit)
            {
                state[unit] = 1;
                foreach (var next in adj[unit])
                {
                    if (state[next] == 1) back.Add((unit, next));
                    else if (state[next] == 0) Visit(next);
                }
                state[unit] = 2;
            }
            for (var i = 0; i < count; i++)
                if (state[i] == 0) Visit(i);

            var succ = new List<int>[count];
            for (var i = 0; i < count; i++) succ[i] = new List<int>();
            var indeg = new int[count];
            foreach (var (info, fromIndex, toIndex) in pairs)
            {
                info.Reversed = back.Contains((fromIndex, toIndex));
                var (upper, lower) = info.Reversed ? (toIndex, fromIndex) : (fromIndex, toIndex);
                succ[upper].Add(lower);
                indeg[lower]++;
            }

            var rank = new int[count];
            var remaining = (int[])indeg.Clone();
            var queue = new Queue<int>();
            for (var i = 0; i < count; i++)
                if (remaining[i] == 0) queue.Enqueue(i);
            var topo = new List<int>(count);
            while (queue.Count > 0)
            {
                var unit = queue.Dequeue();
                topo.Add(unit);
                foreach (var next in succ[unit])
                {
                    rank[next] = Math.Max(rank[next], rank[unit] + 2);
                    if (--remaining[next] == 0) queue.Enqueue(next);
                }
            }

            foreach (var unit in topo)
                if (indeg[unit] == 0 && succ[unit].Count > 0)
                    rank[unit] = succ[unit].Min(next => rank[next]) - 2;

            var min = count > 0 ? rank.Min() : 0;
            for (var i = 0; i < count; i++) units[i].Rank = rank[i] - min;
        }

        private void LayoutFlowLevel(FlowLevel level, Dictionary<string, FlowLevel> nodeLevel,
            Dictionary<(FlowLevel, FEdge), double>? hints)
        {
            var isRoot = level.Group is null;
            var shift = isRoot ? 0 : 1;
            var rankCount = level.Units.Max(unit => unit.Rank) + 1 + (isRoot ? 0 : 2);
            var ranks = new List<FlowItem>[rankCount];
            for (var r = 0; r < rankCount; r++) ranks[r] = new List<FlowItem>();

            FlowItem Add(int rank, double size, double thick)
            {
                var item = new FlowItem { Rank = rank, Order = ranks[rank].Count, Size = size, Thick = thick };
                ranks[rank].Add(item);
                return item;
            }

            foreach (var unit in level.Units)
            {
                unit.Item = unit.Node is { } node
                    ? Add(unit.Rank + shift, _ltr ? node.H : node.W, _ltr ? node.W : node.H)
                    : Add(unit.Rank + shift, unit.Child!.SizeO, unit.Child.SizeR);
                unit.Item.Unit = unit;
            }

            var linkIndex = 0;
            void Chain(FlowEdgeInfo info, FlowItem upper, double upperOff, FlowItem lower, double lowerOff, bool labelled,
                double? tie = null)
            {
                var chain = new FlowChain();
                chain.Items.Add(upper);
                var labelRank = labelled ? upper.Rank + (lower.Rank - upper.Rank) / 2 : -1;
                for (var r = upper.Rank + 1; r < lower.Rank; r++)
                {
                    FlowItem item;
                    if (r == labelRank)
                    {
                        var pill = LabelPill(default, Txt(info.Edge.DisplayLabel!, FsSmall));
                        item = Add(r, _ltr ? pill.Height : pill.Width, _ltr ? pill.Width : pill.Height);
                        item.IsLabel = true;
                        chain.Label = item;
                    }
                    else item = Add(r, 0, 0);
                    item.Tie = tie;
                    chain.Items.Add(item);
                }
                chain.Items.Add(lower);

                for (var i = 0; i + 1 < chain.Items.Count; i++)
                {
                    var link = new FlowLink { Upper = chain.Items[i], Lower = chain.Items[i + 1], Index = linkIndex++ };
                    if (i == 0) link.UpperOff = upperOff;
                    if (i + 2 == chain.Items.Count) link.LowerOff = lowerOff;
                    link.Upper.Down.Add(link);
                    link.Lower.Up.Add(link);
                    chain.Links.Add(link);
                }
                level.Chains.Add(chain);
                info.Chains[level] = chain;
            }

            static double OutOffset(FlowUnit unit, FEdge edge) =>
                unit.Child is { } child ? child.OutPorts[edge] - child.SizeO / 2 : 0;
            static double InOffset(FlowUnit unit, FEdge edge) =>
                unit.Child is { } child ? child.InPorts[edge] - child.SizeO / 2 : 0;

            foreach (var info in level.LcaEdges)
            {
                var upperUnit = FlowUnitAt(level, info.Upper, nodeLevel);
                var lowerUnit = FlowUnitAt(level, info.Lower, nodeLevel);
                Chain(info, upperUnit.Item!, OutOffset(upperUnit, info.Edge), lowerUnit.Item!, InOffset(lowerUnit, info.Edge),
                    info.Edge.DisplayLabel is not null);
            }

            var inBorders = new Dictionary<FEdge, FlowItem>();
            var outBorders = new Dictionary<FEdge, FlowItem>();
            double? HintFor(FlowEdgeInfo info) =>
                hints is not null && hints.TryGetValue((level, info.Edge), out var hint) ? hint : null;
            foreach (var info in level.InEdges)
            {
                var lowerUnit = FlowUnitAt(level, info.Lower, nodeLevel);
                var border = Add(0, 0, 0);
                border.Hint = HintFor(info);
                inBorders[info.Edge] = border;
                Chain(info, border, 0, lowerUnit.Item!, InOffset(lowerUnit, info.Edge), false, border.Hint);
            }
            foreach (var info in level.OutEdges)
            {
                var upperUnit = FlowUnitAt(level, info.Upper, nodeLevel);
                var border = Add(rankCount - 1, 0, 0);
                border.Tie = HintFor(info);
                outBorders[info.Edge] = border;
                Chain(info, upperUnit.Item!, OutOffset(upperUnit, info.Edge), border, 0, false, border.Tie);
            }

            OrderFlowRanks(ranks);
            PositionFlowRanks(ranks);
            StraightenFlowChains(level, ranks);
            AssignFlowPorts(ranks);
            SnapFlowJogs(level, ranks);

            // Rank-axis geometry: band thickness per rank, and a gap per pair of ranks wide enough for its lanes
            // with room for an arrowhead beyond the outermost lane.
            var thick = new double[rankCount];
            for (var r = 0; r < rankCount; r++)
                thick[r] = ranks[r].Count == 0 ? 0 : ranks[r].Max(item => item.Thick);

            var gaps = Math.Max(0, rankCount - 1);
            var gap = new double[gaps];
            var laneMid = new double[gaps];
            var lanes = new int[gaps];
            var halfGap = _ltr ? FlowHalfGapLr : FlowHalfGap;
            for (var gapIndex = 0; gapIndex < gaps; gapIndex++)
            {
                lanes[gapIndex] = AssignFlowLanes(ranks[gapIndex].SelectMany(item => item.Down)
                    .Where(link => Math.Abs(link.UpperAt - link.LowerAt) > 0.5).ToList());
                double baseGap = halfGap, reserve = 0;
                if (!isRoot && gapIndex == 0)
                {
                    baseGap = _ltr ? SgPad : SgPad + SgTitleH;
                    reserve = _ltr ? 0 : SgTitleH + 2; // keep jogs out of the title band
                }
                else if (!isRoot && gapIndex == gaps - 1)
                    baseGap = SgPad;
                gap[gapIndex] = lanes[gapIndex] == 0
                    ? baseGap
                    : Math.Max(baseGap, reserve + (lanes[gapIndex] - 1) * FlowLaneStep + 2 * FlowLaneMargin);
                laneMid[gapIndex] = reserve + (gap[gapIndex] - reserve) / 2;
            }

            var titleText = level.Group is { } group && !string.IsNullOrWhiteSpace(group.Title)
                ? Txt(group.Title, FsSmall, weight: FontWeight.SemiBold)
                : null;
            var titleWidth = titleText is null ? 0 : titleText.Width + 28;
            if (_ltr && gaps > 0 && titleWidth > 0)
            {
                var extent = thick.Sum() + gap.Sum();
                if (extent < titleWidth)
                {
                    var extra = (titleWidth - extent) / 2;
                    gap[0] += extra;
                    laneMid[0] += extra / 2;
                    gap[gaps - 1] += extra;
                    laneMid[gaps - 1] += extra / 2;
                }
            }

            var start = new double[rankCount];
            var acc = 0.0;
            for (var r = 0; r < rankCount; r++)
            {
                start[r] = acc;
                acc += thick[r];
                if (r < gaps) acc += gap[r];
            }
            level.SizeR = acc;

            double minO = double.MaxValue, maxO = double.MinValue;
            foreach (var rank in ranks)
                foreach (var item in rank)
                {
                    minO = Math.Min(minO, item.Pos - item.Size / 2);
                    maxO = Math.Max(maxO, item.Pos + item.Size / 2);
                }
            double padBefore = isRoot ? 0 : (_ltr ? SgPad + SgTitleH : SgPad), padAfter = isRoot ? 0 : SgPad;
            if (!_ltr && titleWidth > maxO - minO + padBefore + padAfter)
            {
                var extra = (titleWidth - (maxO - minO + padBefore + padAfter)) / 2;
                padBefore += extra;
                padAfter += extra;
            }
            var oShift = padBefore - minO;
            level.SizeO = maxO - minO + padBefore + padAfter;

            // The title sits in the top band. In TB the edges entering the box cross that band, so it takes the
            // first spot clear of them, and the box widens on the left when no spot is wide enough.
            if (titleText is not null)
            {
                double pillW = titleText.Width + 12, pillH = titleText.Height + 4, titleX = 8;
                if (!_ltr)
                {
                    var crossings = inBorders.Values.Select(border => border.Pos + oShift).Order().ToList();
                    foreach (var crossing in crossings)
                        if (crossing >= titleX - 5 && crossing <= titleX + pillW + 5) titleX = crossing + 6;
                    if (titleX + pillW > level.SizeO - 8 && crossings.Count > 0)
                    {
                        var grow = Math.Max(0, 8 + pillW + 6 - crossings[0]);
                        oShift += grow;
                        level.SizeO += grow;
                        titleX = 8;
                    }
                }
                level.TitleLocal = new Rect(titleX, 4, pillW, pillH);
            }

            foreach (var (edge, border) in inBorders) level.InPorts[edge] = border.Pos + oShift;
            foreach (var (edge, border) in outBorders) level.OutPorts[edge] = border.Pos + oShift;

            foreach (var unit in level.Units)
            {
                var item = unit.Item!;
                var orderPos = item.Pos + oShift - item.Size / 2;
                var rankPos = start[item.Rank] + (thick[item.Rank] - item.Thick) / 2;
                unit.LocalX = _ltr ? rankPos : orderPos;
                unit.LocalY = _ltr ? orderPos : rankPos;
            }

            Point At(double orderPos, double rankPos) =>
                _ltr ? new Point(rankPos, orderPos + oShift) : new Point(orderPos + oShift, rankPos);

            double AnchorRank(FlowItem item, double at, bool lowerSide)
            {
                var centre = start[item.Rank] + thick[item.Rank] / 2;
                if (item.Unit is null) return centre;
                var half = item.Unit.Node is { } node ? RankBoundary(node, at - item.Pos) : item.Thick / 2;
                return lowerSide ? centre + half : centre - half;
            }

            foreach (var chain in level.Chains)
            {
                var first = chain.Links[0];
                var orderPos = first.UpperAt;
                chain.Points.Add(At(orderPos, AnchorRank(first.Upper, orderPos, lowerSide: true)));
                foreach (var link in chain.Links)
                {
                    if (link.Lane < 0) continue;
                    var gapIndex = link.Upper.Rank;
                    var lane = start[gapIndex] + thick[gapIndex] + laneMid[gapIndex] +
                               (link.Lane - (lanes[gapIndex] - 1) / 2.0) * FlowLaneStep;
                    chain.Points.Add(At(orderPos, lane));
                    orderPos = link.LowerAt;
                    chain.Points.Add(At(orderPos, lane));
                }
                chain.Points.Add(At(orderPos, AnchorRank(chain.Links[^1].Lower, orderPos, lowerSide: false)));
                if (chain.Label is { } label)
                    chain.LabelAt = At(label.Pos, start[label.Rank] + thick[label.Rank] / 2);
            }
        }

        // Distance from a node's centre to its border along the rank axis, at an order-axis offset from the centre.
        private double RankBoundary(FNode node, double offset)
        {
            double halfO = (_ltr ? node.H : node.W) / 2, halfR = (_ltr ? node.W : node.H) / 2;
            var distance = Math.Min(Math.Abs(offset), halfO);
            switch (node.Shape)
            {
                case NShape.Diamond:
                    return halfR * (1 - distance / Math.Max(1, halfO));
                case NShape.Circle:
                {
                    var radius = Math.Min(node.W, node.H) / 2;
                    distance = Math.Min(distance, radius);
                    return Math.Sqrt(radius * radius - distance * distance);
                }
                case NShape.Rounded or NShape.Stadium when _ltr:
                {
                    var radius = node.H / 2;
                    distance = Math.Min(distance, radius);
                    return halfR - radius + Math.Sqrt(radius * radius - distance * distance);
                }
                default:
                    return halfR;
            }
        }

        // The order-axis range (relative to the centre) where edges may attach to a node.
        private double PortReach(FNode node)
        {
            var halfO = (_ltr ? node.H : node.W) / 2;
            var inset = node.Shape switch
            {
                NShape.Diamond => halfO * 0.3,
                NShape.Circle => halfO - Math.Min(node.W, node.H) * 0.3,
                NShape.Rounded or NShape.Stadium when !_ltr => Math.Max(Math.Min(10, halfO * 0.4), node.H / 2),
                _ => Math.Min(_ltr ? 8 : 10, halfO * 0.4),
            };
            return Math.Max(0, halfO - inset);
        }

        private static void OrderFlowRanks(List<FlowItem>[] ranks)
        {
            // Border ports whose other ends are known keep that order; every other rank is free.
            var fixedRank = new bool[ranks.Length];
            for (var r = 0; r < ranks.Length; r++)
            {
                if (ranks[r].Count == 0 || ranks[r].Any(item => item.Hint is null)) continue;
                fixedRank[r] = true;
                var sorted = ranks[r].OrderBy(item => item.Hint!.Value).ToList();
                ranks[r].Clear();
                ranks[r].AddRange(sorted);
                for (var i = 0; i < sorted.Count; i++) sorted[i].Order = i;
            }

            for (var r = 1; r < ranks.Length; r++)
                if (!fixedRank[r]) SortFlowRank(ranks[r], up: true);
            Transpose(ranks, fixedRank);

            var best = ranks.Select(rank => rank.ToArray()).ToArray();
            var bestCrossings = CountCrossings(ranks);
            for (var iter = 0; iter < 12 && bestCrossings > 0; iter++)
            {
                if (iter % 2 == 0)
                {
                    for (var r = ranks.Length - 2; r >= 0; r--)
                        if (!fixedRank[r]) SortFlowRank(ranks[r], up: false);
                }
                else
                {
                    for (var r = 1; r < ranks.Length; r++)
                        if (!fixedRank[r]) SortFlowRank(ranks[r], up: true);
                }
                Transpose(ranks, fixedRank);

                var crossings = CountCrossings(ranks);
                if (crossings < bestCrossings)
                {
                    bestCrossings = crossings;
                    best = ranks.Select(rank => rank.ToArray()).ToArray();
                }
            }

            for (var r = 0; r < ranks.Length; r++)
            {
                ranks[r].Clear();
                ranks[r].AddRange(best[r]);
                for (var i = 0; i < ranks[r].Count; i++) ranks[r][i].Order = i;
            }
        }

        // Reorder a rank by the mean position of each item's neighbours in the adjacent rank; items without such
        // neighbours keep their slots.
        private static void SortFlowRank(List<FlowItem> rank, bool up)
        {
            var bary = new double[rank.Count];
            var movable = new List<(FlowItem Item, double Bary)>();
            for (var i = 0; i < rank.Count; i++)
            {
                var links = up ? rank[i].Up : rank[i].Down;
                if (links.Count == 0) { bary[i] = double.NaN; continue; }
                bary[i] = links.Average(link => (double)(up ? link.Upper.Order : link.Lower.Order));
                movable.Add((rank[i], bary[i]));
            }

            var sorted = movable.OrderBy(entry => entry.Bary).ThenBy(entry => entry.Item.Tie ?? double.PositiveInfinity)
                .ThenBy(entry => entry.Item.Order).ToList();
            var next = 0;
            for (var i = 0; i < rank.Count; i++)
                if (!double.IsNaN(bary[i]))
                    rank[i] = sorted[next++].Item;
            for (var i = 0; i < rank.Count; i++) rank[i].Order = i;
        }

        private static void Transpose(List<FlowItem>[] ranks, bool[] fixedRank)
        {
            for (var pass = 0; pass < 8; pass++)
            {
                var improved = false;
                for (var r = 0; r < ranks.Length; r++)
                {
                    if (fixedRank[r]) continue;
                    var rank = ranks[r];
                    for (var i = 0; i + 1 < rank.Count; i++)
                    {
                        var left = rank[i];
                        var right = rank[i + 1];
                        if (PairCrossings(right, left) >= PairCrossings(left, right)) continue;
                        rank[i] = right;
                        rank[i + 1] = left;
                        right.Order = i;
                        left.Order = i + 1;
                        improved = true;
                    }
                }
                if (!improved) return;
            }
        }

        private static int PairCrossings(FlowItem left, FlowItem right)
        {
            var count = 0;
            foreach (var leftLink in left.Up)
                foreach (var rightLink in right.Up)
                    if (leftLink.Upper.Order > rightLink.Upper.Order) count++;
            foreach (var leftLink in left.Down)
                foreach (var rightLink in right.Down)
                    if (leftLink.Lower.Order > rightLink.Lower.Order) count++;
            return count;
        }

        private static int CountCrossings(List<FlowItem>[] ranks)
        {
            var total = 0;
            var segments = new List<(int Upper, int Lower)>();
            foreach (var rank in ranks)
            {
                segments.Clear();
                foreach (var item in rank)
                    foreach (var link in item.Down)
                        segments.Add((link.Upper.Order, link.Lower.Order));
                for (var i = 0; i < segments.Count; i++)
                    for (var j = i + 1; j < segments.Count; j++)
                        if ((segments[i].Upper - segments[j].Upper) * (segments[i].Lower - segments[j].Lower) < 0) total++;
            }
            return total;
        }

        private double FlowSep(FlowItem left, FlowItem right)
        {
            var gap = left.Unit is not null && right.Unit is not null
                ? (_ltr ? FlowNodeSepLr : FlowNodeSep)
                : left.Unit is not null || right.Unit is not null || left.IsLabel || right.IsLabel
                    ? FlowEdgeSep + 2
                    : FlowEdgeSep;
            return (left.Size + right.Size) / 2 + gap;
        }

        private void PositionFlowRanks(List<FlowItem>[] ranks)
        {
            foreach (var rank in ranks)
            {
                var position = 0.0;
                for (var i = 0; i < rank.Count; i++)
                {
                    if (i > 0) position += FlowSep(rank[i - 1], rank[i]);
                    rank[i].Pos = position;
                }
                foreach (var item in rank) item.Pos -= position / 2;
            }

            for (var iter = 0; iter < 6; iter++)
            {
                for (var r = 1; r < ranks.Length; r++) PlaceFlowRank(ranks[r], up: true, down: false);
                for (var r = ranks.Length - 2; r >= 0; r--) PlaceFlowRank(ranks[r], up: false, down: true);
            }
            foreach (var rank in ranks) PlaceFlowRank(rank, up: true, down: true);
        }

        // Move a rank as close as possible to its neighbours (weighted least squares) while keeping its order and
        // spacing — an isotonic regression solved by pooling adjacent violators.
        private void PlaceFlowRank(List<FlowItem> rank, bool up, bool down)
        {
            var count = rank.Count;
            if (count == 0) return;
            var target = new double[count];
            var weight = new double[count];
            var offset = new double[count];
            for (var i = 0; i < count; i++)
            {
                var item = rank[i];
                double weightSum = 0, weightedSum = 0;
                if (up)
                    foreach (var link in item.Up)
                    {
                        var linkWeight = FlowLinkWeight(link);
                        weightSum += linkWeight;
                        weightedSum += linkWeight * (link.Upper.Pos + link.UpperOff - link.LowerOff);
                    }
                if (down)
                    foreach (var link in item.Down)
                    {
                        var linkWeight = FlowLinkWeight(link);
                        weightSum += linkWeight;
                        weightedSum += linkWeight * (link.Lower.Pos + link.LowerOff - link.UpperOff);
                    }
                target[i] = weightSum > 0 ? weightedSum / weightSum : item.Pos;
                weight[i] = weightSum > 0 ? weightSum : 0.05;
                if (i > 0) offset[i] = offset[i - 1] + FlowSep(rank[i - 1], item);
                target[i] -= offset[i];
            }

            var fit = Isotonic(target, weight);
            for (var i = 0; i < count; i++) rank[i].Pos = fit[i] + offset[i];
        }

        private static double FlowLinkWeight(FlowLink link) =>
            link.Upper.Unit is null && link.Lower.Unit is null ? 8 : 1;

        private static double[] Isotonic(double[] target, double[] weight)
        {
            var count = target.Length;
            var value = new double[count];
            var mass = new double[count];
            var first = new int[count];
            var top = -1;
            for (var i = 0; i < count; i++)
            {
                top++;
                value[top] = target[i];
                mass[top] = weight[i];
                first[top] = i;
                while (top > 0 && value[top - 1] >= value[top])
                {
                    var merged = mass[top - 1] + mass[top];
                    value[top - 1] = (value[top - 1] * mass[top - 1] + value[top] * mass[top]) / merged;
                    mass[top - 1] = merged;
                    top--;
                }
            }

            var fit = new double[count];
            for (var block = 0; block <= top; block++)
            {
                var end = block < top ? first[block + 1] : count;
                for (var i = first[block]; i < end; i++) fit[i] = value[block];
            }
            return fit;
        }

        // Line each edge's channel up with an end it can attach to straight, so routes have at most one jog: first
        // a position both ends reach, else the target's centre or reach, else the source's. A jog shorter than
        // FlowMinJog reads as a glitch, so a position is taken only if each end is straight or clearly offset.
        private void StraightenFlowChains(FlowLevel level, List<FlowItem>[] ranks)
        {
            foreach (var chain in level.Chains.OrderByDescending(chain => chain.Items.Count))
            {
                var free = chain.Items.Where(item => item.Unit is null).ToList();
                if (free.Count == 0) continue;
                var mean = free.Average(item => item.Pos);
                var upper = Reach(chain.Items[0], chain.Links[0].UpperOff);
                var lower = Reach(chain.Items[^1], chain.Links[^1].LowerOff);

                var candidates = new List<double>();
                if (upper is { } upperReach && lower is { } lowerReach &&
                    Math.Max(upperReach.Lo, lowerReach.Lo) <= Math.Min(upperReach.Hi, lowerReach.Hi))
                    candidates.Add(Math.Clamp(mean, Math.Max(upperReach.Lo, lowerReach.Lo), Math.Min(upperReach.Hi, lowerReach.Hi)));
                foreach (var reach in new[] { lower, upper })
                    if (reach is { } range)
                    {
                        candidates.Add((range.Lo + range.Hi) / 2);
                        candidates.Add(Math.Clamp(mean, range.Lo, range.Hi));
                    }

                bool Clean(double position) => CleanJog(upper, position) && CleanJog(lower, position);
                var pick = candidates.Where(Clean).Cast<double?>()
                    .FirstOrDefault(position => free.All(item => FitsFlowRank(ranks[item.Rank], item, position!.Value)));
                if (pick is { } at)
                    foreach (var item in free) item.Pos = at;
                else
                    AlignFlowRuns(free, ranks, upper, lower);
            }

            (double Lo, double Hi)? Reach(FlowItem anchor, double offset)
            {
                if (anchor.Unit is null) return null;
                if (anchor.Unit.Node is { } node)
                {
                    var reach = PortReach(node);
                    return (anchor.Pos - reach, anchor.Pos + reach);
                }
                return (anchor.Pos + offset, anchor.Pos + offset);
            }

            static bool CleanJog((double Lo, double Hi)? reach, double position)
            {
                if (reach is not { } range) return true;
                var jog = position < range.Lo ? range.Lo - position : position > range.Hi ? position - range.Hi : 0;
                return jog < 0.5 || jog >= FlowMinJog;
            }
        }

        // Where an edge's channel cannot run straight end to end, split it into as few straight runs as fit between
        // their neighbours, with every step between runs and ends either none or at least FlowMinJog.
        private void AlignFlowRuns(List<FlowItem> free, List<FlowItem>[] ranks,
            (double Lo, double Hi)? upper, (double Lo, double Hi)? lower)
        {
            var runs = new List<(List<FlowItem> Items, double Lo, double Hi)>();
            List<FlowItem>? current = null;
            double lo = 0, hi = 0;
            foreach (var item in free)
            {
                var (itemLo, itemHi) = FlowFreeRange(ranks[item.Rank], item);
                if (current is not null && Math.Max(lo, itemLo) <= Math.Min(hi, itemHi))
                {
                    current.Add(item);
                    lo = Math.Max(lo, itemLo);
                    hi = Math.Min(hi, itemHi);
                    continue;
                }
                if (current is not null) runs.Add((current, lo, hi));
                current = new List<FlowItem> { item };
                (lo, hi) = (itemLo, itemHi);
            }
            runs.Add((current!, lo, hi));

            double? previous = null;
            for (var runIndex = 0; runIndex < runs.Count; runIndex++)
            {
                var (items, runLo, runHi) = runs[runIndex];
                var preferred = SafeClamp(items.Average(item => item.Pos), runLo, runHi);
                var reaches = new List<(double Lo, double Hi)>();
                if (previous is { } previousPos) reaches.Add((previousPos, previousPos));
                if (runIndex == 0 && upper is { } upperReach) reaches.Add(upperReach);
                if (runIndex == runs.Count - 1 && lower is { } lowerReach) reaches.Add(lowerReach);
                var position = ChooseFlowPosition(runLo, runHi, preferred, reaches) ?? preferred;
                foreach (var item in items) item.Pos = position;
                previous = position;
            }
        }

        // The position in [lo, hi] closest to the preferred one where the jog to each reach is either none or at
        // least FlowMinJog; null when there is none.
        private static double? ChooseFlowPosition(double lo, double hi, double preferred, List<(double Lo, double Hi)> reaches)
        {
            bool Clean(double position) => reaches.All(range =>
            {
                var jog = position < range.Lo ? range.Lo - position : position > range.Hi ? position - range.Hi : 0;
                return jog < 0.5 || jog >= FlowMinJog - 0.01;
            });

            var points = new List<double> { preferred, lo, hi };
            foreach (var (rangeLo, rangeHi) in reaches)
                points.AddRange(new[] { rangeLo, rangeHi, rangeLo - FlowMinJog, rangeHi + FlowMinJog, Math.Clamp(preferred, rangeLo, rangeHi) });
            return points.Where(point => double.IsFinite(point) && point >= lo - 0.01 && point <= hi + 0.01 && Clean(point))
                .Select(point => (double?)point).OrderBy(point => Math.Abs(point!.Value - preferred)).FirstOrDefault();
        }

        // The positions an item can take without crowding its neighbours in the rank.
        private (double Lo, double Hi) FlowFreeRange(List<FlowItem> rank, FlowItem item)
        {
            var order = item.Order;
            return (order > 0 ? rank[order - 1].Pos + FlowSep(rank[order - 1], item) : double.NegativeInfinity,
                    order + 1 < rank.Count ? rank[order + 1].Pos - FlowSep(item, rank[order + 1]) : double.PositiveInfinity);
        }

        // A tightly packed item's range can come out inverted by rounding; Math.Clamp would throw on it.
        private static double SafeClamp(double value, double lo, double hi) =>
            lo > hi ? (lo + hi) / 2 : Math.Clamp(value, lo, hi);

        private bool FitsFlowRank(List<FlowItem> rank, FlowItem item, double position)
        {
            var (lo, hi) = FlowFreeRange(rank, item);
            return position >= lo - 0.01 && position <= hi + 0.01;
        }

        // Remove jogs too short to read as intentional: move the straight run on one side of the jog (its edge
        // channels and, at a chain end, the node port) onto the other side's line, provided the step beyond the run
        // stays clean. Failing that, push a channel so each of its jogs is clearly deliberate.
        private void SnapFlowJogs(FlowLevel level, List<FlowItem>[] ranks)
        {
            for (var pass = 0; pass < 4; pass++)
            {
                var changed = false;
                foreach (var chain in level.Chains)
                    for (var j = 0; j < chain.Links.Count; j++)
                    {
                        var link = chain.Links[j];
                        var jog = Math.Abs(link.LowerAt - link.UpperAt);
                        if (jog <= 0.5 || jog >= FlowMinJog) continue;
                        if (MoveRun(chain, j, below: true) || MoveRun(chain, j, below: false) ||
                            Widen(link.Lower) || Widen(link.Upper))
                            changed = true;
                    }
                if (!changed) return;
            }

            static bool Straight(FlowLink link) => Math.Abs(link.LowerAt - link.UpperAt) <= 0.5;

            bool MoveRun(FlowChain chain, int j, bool below)
            {
                var position = below ? chain.Links[j].UpperAt : chain.Links[j].LowerAt;
                int first, last;
                FlowLink? outer;
                if (below)
                {
                    first = last = j + 1;
                    while (last < chain.Links.Count && Straight(chain.Links[last])) last++;
                    outer = last < chain.Links.Count ? chain.Links[last] : null;
                }
                else
                {
                    first = last = j;
                    while (first > 0 && Straight(chain.Links[first - 1])) first--;
                    outer = first > 0 ? chain.Links[first - 1] : null;
                }

                for (var i = first; i <= last; i++)
                {
                    var item = chain.Items[i];
                    if (item.Unit is null)
                    {
                        if (!FitsSnapped(ranks[item.Rank], item, position)) return false;
                    }
                    else if (item.Unit.Node is not { } node ||
                             !PortCanMove(item, node, i == 0 ? chain.Links[0] : chain.Links[i - 1], isLowerEnd: i != 0, position))
                        return false; // a subgraph's ports are fixed
                }
                if (outer is not null)
                {
                    var step = Math.Abs((below ? outer.LowerAt : outer.UpperAt) - position);
                    if (step > 0.5 && step < FlowMinJog) return false;
                }

                for (var i = first; i <= last; i++)
                {
                    var item = chain.Items[i];
                    if (item.Unit is null)
                    {
                        item.Pos = position;
                        foreach (var link in item.Up) link.LowerAt = position;
                        foreach (var link in item.Down) link.UpperAt = position;
                    }
                    else if (i == 0) chain.Links[0].UpperAt = position;
                    else chain.Links[i - 1].LowerAt = position;
                }
                return true;
            }

            bool PortCanMove(FlowItem item, FNode node, FlowLink link, bool isLowerEnd, double position)
            {
                var reach = PortReach(node);
                if (position < item.Pos - reach - 0.01 || position > item.Pos + reach + 0.01) return false;
                var current = isLowerEnd ? link.LowerAt : link.UpperAt;
                foreach (var other in isLowerEnd ? item.Up : item.Down)
                {
                    if (other == link) continue;
                    var at = isLowerEnd ? other.LowerAt : other.UpperAt;
                    if (Math.Abs(at - position) < FlowSnapSep - 0.01 || (at - current) * (at - position) < 0) return false;
                }
                return true;
            }

            bool Widen(FlowItem item)
            {
                if (item.Unit is not null) return false;
                var (lo, hi) = FlowFreeRange(ranks[item.Rank], item);
                var reaches = item.Up.Select(link => (link.UpperAt, link.UpperAt))
                    .Concat(item.Down.Select(link => (link.LowerAt, link.LowerAt))).ToList();
                if (ChooseFlowPosition(lo, hi, item.Pos, reaches) is not { } position || Math.Abs(position - item.Pos) < 0.01)
                    return false;
                item.Pos = position;
                foreach (var link in item.Up) link.LowerAt = position;
                foreach (var link in item.Down) link.UpperAt = position;
                return true;
            }

            // Bare edge channels may close up to FlowSnapSep when that straightens a line; labels and nodes keep
            // their full clearance.
            bool FitsSnapped(List<FlowItem> rank, FlowItem item, double position)
            {
                var order = item.Order;
                double Need(FlowItem left, FlowItem right) =>
                    left.Unit is null && right.Unit is null && !left.IsLabel && !right.IsLabel ? FlowSnapSep : FlowSep(left, right);
                return (order == 0 || position >= rank[order - 1].Pos + Need(rank[order - 1], item) - 0.01) &&
                       (order + 1 >= rank.Count || position <= rank[order + 1].Pos - Need(item, rank[order + 1]) + 0.01);
            }
        }

        // Attachment points: channels and box ports are fixed; a node spreads its edges along each side, each as
        // close as possible to the channel it connects to.
        private void AssignFlowPorts(List<FlowItem>[] ranks)
        {
            foreach (var rank in ranks)
                foreach (var item in rank)
                {
                    foreach (var link in item.Down) link.UpperAt = item.Pos + link.UpperOff;
                    foreach (var link in item.Up) link.LowerAt = item.Pos + link.LowerOff;
                }

            foreach (var rank in ranks)
                foreach (var item in rank)
                {
                    if (item.Unit?.Node is not { } node) continue;
                    var reach = PortReach(node);
                    Spread(item.Down, link => link.Lower.Pos + link.LowerOff, (link, at) => link.UpperAt = at);
                    Spread(item.Up, link => link.Upper.Pos + link.UpperOff, (link, at) => link.LowerAt = at);

                    // Port i sits at fit[i] + i * sep. Ports stay at least sep apart while fit never decreases, and on
                    // the node's side while fit stays in [lo, hi - (count - 1) * sep], so the targets are clamped to that
                    // range before the isotonic fit (whose values stay within the targets' range).
                    void Spread(List<FlowLink> links, Func<FlowLink, double> other, Action<FlowLink, double> set)
                    {
                        if (links.Count == 0) return;
                        var ordered = links.OrderBy(other).ThenBy(link => link.Index).ToList();
                        var count = ordered.Count;
                        double lo = item.Pos - reach, hi = item.Pos + reach;
                        var sep = count > 1 ? Math.Min(FlowPortSep, (hi - lo) / (count - 1)) : 0;
                        var highest = Math.Max(lo, hi - (count - 1) * sep); // rounding can put it just below lo
                        var target = new double[count];
                        var weight = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            target[i] = Math.Clamp(other(ordered[i]) - i * sep, lo, highest);
                            weight[i] = 1;
                        }
                        var fit = Isotonic(target, weight);
                        for (var i = 0; i < count; i++) set(ordered[i], fit[i] + i * sep);
                    }
                }
        }

        // Give every jog in a gap its own lane where it would overlap another. Where one jog starts where another ends,
        // the starting one must turn first or their lines would merge; beyond that, same-direction jogs are ordered so
        // they do not cross, whenever that agrees with the first rule. Returns the number of lanes used.
        private static int AssignFlowLanes(List<FlowLink> jogs)
        {
            var count = jogs.Count;
            if (count == 0) return 0;
            static double Lo(FlowLink jog) => Math.Min(jog.UpperAt, jog.LowerAt);
            static double Hi(FlowLink jog) => Math.Max(jog.UpperAt, jog.LowerAt);

            var before = new List<int>[count]; // jogs that must take a lane before this one
            var after = new List<int>[count];  // the same constraints the other way round, for the cycle check
            var overlaps = new List<int>[count];
            for (var i = 0; i < count; i++) { before[i] = new List<int>(); after[i] = new List<int>(); overlaps[i] = new List<int>(); }
            var merging = new List<(int First, int Then)>();
            var crossing = new List<(int First, int Then)>();
            for (var i = 0; i < count; i++)
                for (var j = i + 1; j < count; j++)
                {
                    var one = jogs[i];
                    var other = jogs[j];
                    if (Lo(one) > Hi(other) + 2 || Lo(other) > Hi(one) + 2) continue;
                    overlaps[i].Add(j);
                    overlaps[j].Add(i);

                    if (Math.Abs(one.UpperAt - other.LowerAt) < 6) merging.Add((i, j));
                    if (Math.Abs(other.UpperAt - one.LowerAt) < 6) merging.Add((j, i));
                    bool oneRight = one.LowerAt > one.UpperAt, otherRight = other.LowerAt > other.UpperAt;
                    if (oneRight == otherRight)
                        crossing.Add((oneRight ? one.UpperAt > other.UpperAt : one.UpperAt < other.UpperAt) ? (i, j) : (j, i));
                }

            // Add a constraint only if it keeps the order acyclic.
            bool Reaches(int from, int to)
            {
                var seen = new bool[count];
                var stack = new Stack<int>();
                stack.Push(from);
                while (stack.Count > 0)
                {
                    var jog = stack.Pop();
                    if (jog == to) return true;
                    if (seen[jog]) continue;
                    seen[jog] = true;
                    foreach (var later in after[jog]) stack.Push(later);
                }
                return false;
            }
            foreach (var (first, then) in merging.Concat(crossing))
                if (!before[then].Contains(first) && !Reaches(then, first))
                {
                    before[then].Add(first);
                    after[first].Add(then);
                }

            var laneOf = Enumerable.Repeat(-1, count).ToArray();
            var laneCount = 0;
            for (var assigned = 0; assigned < count; assigned++)
            {
                var pick = -1;
                for (var i = 0; i < count && pick < 0; i++)
                    if (laneOf[i] < 0 && before[i].All(earlier => laneOf[earlier] >= 0)) pick = i;
                if (pick < 0) pick = Array.IndexOf(laneOf, -1);

                var lane = before[pick].Where(earlier => laneOf[earlier] >= 0).Select(earlier => laneOf[earlier] + 1)
                    .DefaultIfEmpty(0).Max();
                while (overlaps[pick].Any(neighbour => laneOf[neighbour] == lane)) lane++;
                laneOf[pick] = lane;
                laneCount = Math.Max(laneCount, lane + 1);
            }

            for (var i = 0; i < count; i++) jogs[i].Lane = laneOf[i];
            return laneCount;
        }

        private void PlaceFlowLevel(FlowLevel level, double x, double y)
        {
            if (level.Group is { } group)
            {
                group.Box = new Rect(x, y, _ltr ? level.SizeR : level.SizeO, _ltr ? level.SizeO : level.SizeR);
                group.TitleRect = level.TitleLocal.Translate(new Vector(x, y));
            }

            foreach (var unit in level.Units)
            {
                if (unit.Node is { } node)
                {
                    node.X = x + unit.LocalX;
                    node.Y = y + unit.LocalY;
                }
                else PlaceFlowLevel(unit.Child!, x + unit.LocalX, y + unit.LocalY);
            }

            var offset = new Vector(x, y);
            foreach (var chain in level.Chains)
            {
                for (var i = 0; i < chain.Points.Count; i++) chain.Points[i] += offset;
                chain.LabelAt += offset;
            }
        }

        // Join the edge's pieces from every level it passes through into one route, drawn From -> To.
        private static void ComposeFlowRoute(FlowEdgeInfo info, Dictionary<string, FlowLevel> nodeLevel)
        {
            var pieces = new List<FlowChain>();
            for (var level = nodeLevel[info.Upper]; level != info.Lca; level = level.Parent!) pieces.Add(info.Chains[level]);
            pieces.Add(info.Chains[info.Lca]);
            var lower = new List<FlowChain>();
            for (var level = nodeLevel[info.Lower]; level != info.Lca; level = level.Parent!) lower.Add(info.Chains[level]);
            lower.Reverse();
            pieces.AddRange(lower);

            var route = new List<Point>();
            foreach (var piece in pieces)
                foreach (var point in piece.Points)
                    AddRoutePoint(route, point);

            if (info.Reversed) route.Reverse();
            info.Edge.Route = route;
            if (info.Chains[info.Lca].Label is not null)
                info.Edge.LabelAt = info.Chains[info.Lca].LabelAt;
        }

        // Append a waypoint, skipping duplicates and merging straight runs. Joins between levels can differ by rounding
        // or a sub-pixel channel offset, so a nearly aligned point is snapped to keep every segment orthogonal.
        private static void AddRoutePoint(List<Point> route, Point point)
        {
            if (route.Count > 0)
            {
                var last = route[^1];
                if (Dist(last, point) < 0.5) return;
                if (Math.Abs(point.X - last.X) < 0.5) point = new Point(last.X, point.Y);
                else if (Math.Abs(point.Y - last.Y) < 0.5) point = new Point(point.X, last.Y);
            }
            if (route.Count >= 2)
            {
                var previous = route[^2];
                var last = route[^1];
                if ((Math.Abs(previous.X - last.X) < 0.01 && Math.Abs(last.X - point.X) < 0.01) ||
                    (Math.Abs(previous.Y - last.Y) < 0.01 && Math.Abs(last.Y - point.Y) < 0.01))
                {
                    route[^1] = point;
                    return;
                }
            }
            route.Add(point);
        }

        // Shift the whole flow to a tight top-left origin and size the layout to its real content extent. Extents are
        // accumulated directly: Rect.Union ignores empty rects, which would drop every route point.
        private void NormalizeFlowLayout()
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            void Include(Rect area)
            {
                minX = Math.Min(minX, area.Left);
                minY = Math.Min(minY, area.Top);
                maxX = Math.Max(maxX, area.Right);
                maxY = Math.Max(maxY, area.Bottom);
            }

            foreach (var node in _fNodes.Values) Include(new Rect(node.X, node.Y, node.W, node.H));
            foreach (var group in _fSubgraphs.Where(subgraph => subgraph.Box.Width > 0)) Include(group.Box);
            foreach (var edge in _fEdges)
            {
                foreach (var point in edge.Route) Include(new Rect(point, point));
                if (edge.DisplayLabel is { } label && edge.Route.Count > 0)
                    Include(LabelPill(edge.LabelAt, Txt(label, FsSmall)));
            }

            var shift = new Vector(-minX, -minY);
            foreach (var node in _fNodes.Values) { node.X += shift.X; node.Y += shift.Y; }
            foreach (var group in _fSubgraphs.Where(subgraph => subgraph.Box.Width > 0))
            {
                group.Box = group.Box.Translate(shift);
                group.TitleRect = group.TitleRect.Translate(shift);
            }
            foreach (var edge in _fEdges)
            {
                for (var i = 0; i < edge.Route.Count; i++) edge.Route[i] += shift;
                edge.LabelAt += shift;
            }

            _layW = maxX - minX;
            _layH = maxY - minY;
        }

        // The pill an edge label is drawn on, centred on the label's spot.
        private static Rect LabelPill(Point centre, FormattedText text) =>
            new(centre.X - text.Width / 2 - 6, centre.Y - text.Height / 2 - 2, text.Width + 12, text.Height + 4);
    }
}
