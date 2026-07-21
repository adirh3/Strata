using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A living, full-bleed ambient "presence" field designed to sit <b>behind</b> a
/// canvas (e.g. a chat transcript) and make the surface feel alive. It renders a
/// soft, drifting aurora of overlapping radial-gradient lobes (indigo / violet /
/// rose) that breathe continuously and react to <see cref="State"/> changes and
/// one-shot <see cref="Pulse(PresencePulse)"/> events.
/// </summary>
/// <remarks>
/// <para>
/// All continuous motion (breathe + drift) is driven by the Avalonia
/// <b>Composition</b> API and therefore runs on the render thread — there is no
/// per-frame UI-thread cost. State intensity is expressed through eased
/// <see cref="Visual.Opacity"/> cross-fades on the lobes (cheap, infrequent).
/// </para>
/// <para>The control is decorative only — it is hit-test invisible.</para>
/// </remarks>
public class StrataPresence : Panel, IDisposable
{
    /// <summary>Current ambient state. Drives lobe intensity and motion energy.</summary>
    public static readonly StyledProperty<PresenceState> StateProperty =
        AvaloniaProperty.Register<StrataPresence, PresenceState>(nameof(State), PresenceState.Dormant);

    /// <summary>Normalized focal point (x,y each 0..1) the field gravitates toward.
    /// (0.5, 0.5) is dead-centre; the bright "gaze" lobes lead toward it while the
    /// ambient lobes trail, so the whole aurora appears to drift to wherever attention
    /// is (the live message, the composer, an opening island).</summary>
    public static readonly StyledProperty<Point> FocusPointProperty =
        AvaloniaProperty.Register<StrataPresence, Point>(nameof(FocusPoint), new Point(0.5, 0.46));

    /// <summary>How far the field is allowed to travel toward <see cref="FocusPoint"/>
    /// (0 = stay home, 1 = full reach). Lets callers dampen the travel per surface.</summary>
    public static readonly StyledProperty<double> FocusReachProperty =
        AvaloniaProperty.Register<StrataPresence, double>(nameof(FocusReach), 1.0);

    /// <summary>Compact layout for small/narrow surfaces (e.g. a side island): scales
    /// lobes to the surface's short edge so they read as a tidy vertical glow.</summary>
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<StrataPresence, bool>(nameof(Compact));

    /// <summary>Master intensity multiplier (0..1) applied on top of every state level.</summary>
    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<StrataPresence, double>(nameof(Intensity), 1.0);

    /// <summary>When true, the field gathers a soft, slowly breathing halo around the
    /// current <see cref="FocusPoint"/> — a quiet "luminance" that makes a focal element
    /// (e.g. the Lumi mark on the welcome screen) feel alive. Independent of <see cref="State"/>.</summary>
    public static readonly StyledProperty<bool> HaloProperty =
        AvaloniaProperty.Register<StrataPresence, bool>(nameof(Halo));

    public PresenceState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public Point FocusPoint
    {
        get => GetValue(FocusPointProperty);
        set => SetValue(FocusPointProperty, value);
    }

    public double FocusReach
    {
        get => GetValue(FocusReachProperty);
        set => SetValue(FocusReachProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    public bool Halo
    {
        get => GetValue(HaloProperty);
        set => SetValue(HaloProperty, value);
    }

    static StrataPresence()
    {
        StateProperty.Changed.AddClassHandler<StrataPresence>((p, _) => p.ApplyState());
        FocusPointProperty.Changed.AddClassHandler<StrataPresence>((p, _) => p.ApplyFocus(animate: !p._resizeSnap));
        FocusReachProperty.Changed.AddClassHandler<StrataPresence>((p, _) => p.ApplyFocus());
        CompactProperty.Changed.AddClassHandler<StrataPresence>((p, _) =>
        {
            p.SizeLobes(p.Bounds.Width, p.Bounds.Height);
            p.InvalidateArrange();
            p.ApplyFocus(animate: false);
        });
        IntensityProperty.Changed.AddClassHandler<StrataPresence>((p, _) =>
        {
            p.ApplyState();
            // Rebuild the halo breath so a live intensity change is reflected on the welcome mark.
            p._haloActive = false;
            p.UpdateHalo();
        });
        HaloProperty.Changed.AddClassHandler<StrataPresence>((p, _) => p.UpdateHalo());
    }

    private enum LobeRole { Indigo, Violet, Rose, Core, Beacon, Pulse, Halo, Companion }

    private sealed class Lobe
    {
        public required LobeRole Role { get; init; }
        /// <summary>The glow itself: carries drift (Offset), breathe (Scale) and the
        /// state/ signal opacity. Nested inside <see cref="Host"/>.</summary>
        public required Border Border { get; init; }
        /// <summary>Outer wrapper that carries only the focus-follow travel (Offset),
        /// so focus motion composes on top of the glow's own drift.</summary>
        public required Border Host { get; init; }
        public CompositionVisual? Visual { get; set; }
        public CompositionVisual? HostVisual { get; set; }
        public required double AnchorX { get; init; }
        public required double AnchorY { get; init; }
        public required double SizeFactor { get; init; }
        /// <summary>0..1 — how strongly this lobe tracks <see cref="FocusPoint"/>. The
        /// gaze/signal lobes track hardest (they lead); ambient lobes trail.</summary>
        public required double Follow { get; init; }
        /// <summary>Focus-travel time constant: drives the per-lobe duration of the compositor-side
        /// implicit <c>Offset</c> animation on <see cref="Host"/>. Smaller = quicker (the gaze/signal
        /// lobes lead); larger = slower (ambient lobes trail, the companion splits slowest). This is
        /// what makes focus motion read as a soft, layered settle rather than one rigid slide.</summary>
        public required double FocusTau { get; init; }
        public required int Phase { get; init; }
        /// <summary>True for lobes whose visibility is driven by composition opacity
        /// (signals) rather than by <see cref="Control.Opacity"/> (ambient state).</summary>
        public required bool Signal { get; init; }
        public double Size { get; set; }

        // ── Glow brush source ──
        // Stored so the radial gradient can be rebuilt when EITHER the theme colour changes (resource
        // observer) OR the directional light origin moves (UpdateLightDirection), without re-subscribing.
        public byte InnerAlpha { get; init; }
        public byte MidAlpha { get; init; }
        public Color GlowColor { get; set; }

        // ── Spring focus-travel state ──
        // The live analytic trajectory that the render-thread Offset keyframes are sampled from. It is
        // re-seeded on every re-aim from the lobe's current position AND velocity (see PresenceSpring),
        // so focus motion stays C¹-continuous — momentum is never reset the way an easing tween resets it.
        // This is tracked here rather than read back from the composition visual because the visual's
        // Offset getter returns the BASE value, not the live animated value.
        public bool SpringPlaced { get; set; }
        public double SpringArmMs { get; set; }
        public double SpringTargetX { get; set; }
        public double SpringTargetY { get; set; }
        public double SpringP0X { get; set; }
        public double SpringV0X { get; set; }
        public double SpringP0Y { get; set; }
        public double SpringV0Y { get; set; }
    }

    private readonly List<Lobe> _lobes = new();
    private readonly List<IDisposable> _subscriptions = new();
    // Monotonic wall-clock for the spring: lets us read a trajectory's live (position, velocity) at any
    // instant to re-seed a re-aim, and to size the sampled keyframe horizon.
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private CompositionVisual? _selfVisual;
    private bool _ready;
    // Live visual-tree attachment. OnAttached posts InitComposition at DispatcherPriority.Loaded; if a
    // detach happens before that callback drains, this flag lets InitComposition bail instead of
    // re-arming animations/CTS on a control that is no longer in the tree.
    private bool _attached;
    private MotionLevel _motionLevel = (MotionLevel)(-1);
    private bool _motionDirty = true;
    // True only for the span of a resize-driven re-pin (ResyncFocus): makes ApplyFocus SNAP rather than
    // spring, so the controller's focus refinement during a continuous drag tracks the re-laid-out canvas
    // rigidly instead of springing (and micro-jittering) behind it. Structural placement on resize is
    // handled by ArrangeOverride (which arranges each lobe host AT its focal target); this snap only keeps
    // the FocusPoint refinement from adding spring jitter on top.
    private bool _resizeSnap;
    private System.Threading.CancellationTokenSource? _beaconCts;
    private PresenceState _beaconState = (PresenceState)(-1);
    private double _beaconIntensity = -1;
    private System.Threading.CancellationTokenSource? _haloCts;
    private bool _haloActive;
    private System.Threading.CancellationTokenSource? _companionCts;
    private bool _companionActive;
    // The companion pool's normalized (0..1) island anchor (null when merged into the chat). Setting it
    // and re-applying focus eases the companion host toward this via its implicit Offset animation, so
    // its split travel is smooth and re-aimable without ever restarting an in-flight key-frame slide.
    private Point? _companionNorm;
    // Travel "gaze" surge: while resting (Idle/Dormant) there is no beacon heartbeat, so a meaningful
    // focus move would glide invisibly. We briefly bloom the focal Beacon along the glide so the eye is
    // led to the new spot — a subtle, alive "look here" that dissolves on arrival. _lastSurgeFocus tracks
    // the last applied focus so only meaningful jumps (not micro-tracking) trigger the gesture.
    private System.Threading.CancellationTokenSource? _travelCts;
    private Point _lastSurgeFocus = new(0.5, 0.46);

    // Directional light origin (relative 0..1 inside every lobe). Leans the radial gradient's bright core
    // toward the edge the field hugs so the glow visibly casts the OTHER way (a low pool glows upward).
    // Updated from the focus point on every re-aim; (0.5,0.5) is centred / symmetric (the resting default).
    private double _lightOriginX = 0.5;
    private double _lightOriginY = 0.5;

    public StrataPresence()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Background = null;

        // Softer inner alphas keep the lobe centres diffuse so the field reads as ambient
        // light rather than solid coloured blobs. Intensity is then mostly carried by the
        // (low) state opacity, which keeps the whole presence understated and premium. Sizes are
        // a generous fraction of the SHORT edge: large enough to read as a soft, diffuse aura (not
        // tight blobs), yet contained within the viewport so a focus glide of ~0.3·h still moves a
        // clearly visible fraction of each lobe's diameter (PresenceGeometry guards the containment).
        BuildLobe(LobeRole.Indigo, "Color.AccentDefault", 0.40, 0.42, 0.68, 0.82, 0, signal: false, 162, 60);
        BuildLobe(LobeRole.Violet, "Color.AccentViolet", 0.60, 0.42, 0.64, 0.82, 1, signal: false, 156, 58);
        BuildLobe(LobeRole.Rose, "Color.AccentRose", 0.50, 0.60, 0.60, 0.78, 2, signal: false, 138, 50);
        BuildLobe(LobeRole.Core, "Color.AccentDefault", 0.50, 0.50, 0.52, 0.90, 3, signal: false, 176, 56);
        // The "gaze beacon": a focus-hugging signal lobe whose rhythmic pulse encodes whether
        // Lumi is thinking, streaming or asking for the user. Re-coloured per state at runtime.
        BuildLobe(LobeRole.Beacon, "Color.AccentDefault", 0.50, 0.46, 0.60, 0.90, 1, signal: true, 168, 60);
        BuildLobe(LobeRole.Pulse, "Color.AccentDefault", 0.50, 0.50, 0.58, 0.88, 0, signal: true, 178, 64);
        // A dedicated soft halo for "luminance" treatments (e.g. the welcome mark). It rides
        // the focal point closely so it pools right around whatever it's aimed at, and is
        // revealed only while Halo is true, via a slow, gentle opacity breath. Generously sized
        // (a large fraction of the short edge) so the "new chat" aura reads as a big, soft pool of
        // light around the mark on large screens — still localized (short-edge based), never a
        // screen-wide wash.
        BuildLobe(LobeRole.Halo, "Color.AccentDefault", 0.50, 0.50, 0.64, 0.92, 2, signal: true, 170, 62);
        // A second, persistent pool of light that splits off the main field and parks inside an
        // opened companion island (workspace / browser / diff / plan), so the canvas reads as two
        // presences: the main one tending the chat and a companion softly illuminating the island.
        // Its travel is owned entirely by SplitToIsland/Merge (never the focal point), and like the
        // halo it is sized to the short edge so it stays a localized pool, not a screen-wide wash.
        BuildLobe(LobeRole.Companion, "Color.AccentDefault", 0.50, 0.50, 0.60, 0.0, 0, signal: true, 168, 60);

        SizeChanged += (_, _) =>
        {
            // A resize changes the field diameter (it scales with the canvas) and every lobe's drift spread,
            // so the render-thread motion must be rebuilt for the new size — that is all this handler does.
            // Placement is handled STRUCTURALLY by ArrangeOverride: it arranges every lobe host AT its focal
            // target, so the resize arrange writes the CORRECT focal Offset (never centred-home). No deferred
            // focus re-pin is needed — the previous Background-deferred snap lost a starvation race against
            // the OS resize-event flood during a real continuous drag, which is exactly what made the field
            // jitter and collapse to centre. Arranging at the focal point cannot be starved.
            _motionDirty = true;
            ApplyState();
        };
    }

    /// <summary>
    /// Sets <see cref="FocusPoint"/> and re-pins the field to it WITHOUT a spring glide. The owning
    /// controller calls this on a window resize, where the field must track the re-laid-out canvas
    /// rigidly: a spring would visibly lag and jitter behind a continuous drag. The structural placement
    /// is already handled by <see cref="ArrangeOverride"/> (which arranges each lobe host at its focal
    /// target, so the resize arrange writes the focal Offset); this just re-pins the FocusPoint refinement
    /// without adding spring motion on top.
    /// </summary>
    public void ResyncFocus(Point focus)
    {
        _resizeSnap = true;
        try
        {
            var changed = FocusPoint != focus;
            // SetCurrentValue (not the CLR setter) preserves any binding; the FocusPoint changed-handler
            // reads _resizeSnap and applies without animation. If the value is unchanged the handler will
            // not fire, so snap explicitly.
            SetCurrentValue(FocusPointProperty, focus);
            if (!changed && _ready)
                ApplyFocus(animate: false);
            // Re-arrange so each lobe host's Bounds.Position — and therefore the layout-synced Offset BASE —
            // tracks the refined focal too. The controller refines FocusPoint on Background AFTER the resize
            // arrange (the composer's normalized spot shifts as the fixed-width rail re-proportions), so
            // without this the base would lag the snap by one cycle. Re-arranging keeps the structural base
            // and the snapped Offset in exact agreement, so there is no stale-base value to ever flash.
            InvalidateArrange();
        }
        finally
        {
            _resizeSnap = false;
        }
    }

    private void BuildLobe(
        LobeRole role, string colorKey, double anchorX, double anchorY,
        double sizeFactor, double follow, int phase, bool signal, byte innerAlpha, byte midAlpha)
    {
        var glow = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            // Every lobe rests fully transparent. Ambient lobes express state by
            // (eased) Opacity; signal lobes are revealed by transient opacity
            // animations. (Control.Opacity and the composition visual's Opacity are
            // the same channel in Avalonia, so opacity is always driven UI-side.)
            Opacity = 0.0,
            Width = 1,
            Height = 1,
        };

        if (!signal)
        {
            glow.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition
                {
                    Property = OpacityProperty,
                    // A long, gentle ease so the field *settles* into a chat rather than
                    // switching on — opening an existing chat should feel like light pooling
                    // in, never a glow popping into place.
                    Duration = TimeSpan.FromMilliseconds(1500),
                    Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
                },
            };
        }

        // The host carries only focus-follow travel; the glow inside keeps its own
        // drift + breathe + opacity. Their composition Offsets compose, so the lobe
        // drifts around its anchor *and* glides toward the focal point at once.
        // Stretch (not Center) so ArrangeOverride can place the host EXACTLY at its focal
        // rect with zero alignment slack — the host's Bounds.Position then equals the focal
        // point, which is what makes the composition Offset re-sync to the focal point (not
        // centred-home) on every arrange, so a window resize can never collapse the field to centre.
        var host = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Width = 1,
            Height = 1,
            Child = glow,
        };

        Children.Add(host);
        // Per-lobe inertia: the gaze/signal lobes glide quickest (they lead the eye), the bright
        // core follows close behind, and the broad ambient lobes trail slowest — so the field
        // settles in soft layers. The companion pool travels slowest of all for a graceful split.
        // Tau values are intentionally generous so the focus glide lasts ~1.2–1.5 s (ambient) — slow
        // enough that the eye reads it as a body of light *travelling*, never a snap. (OmegaFor maps
        // tau → spring stiffness; see that method.)
        var focusTau = role == LobeRole.Companion ? 0.62
            : signal ? 0.26
            : 0.50 - 0.22 * follow;
        var lobe = new Lobe
        {
            Role = role,
            Border = glow,
            Host = host,
            AnchorX = anchorX,
            AnchorY = anchorY,
            SizeFactor = sizeFactor,
            Follow = follow,
            FocusTau = focusTau,
            Phase = phase,
            Signal = signal,
            InnerAlpha = innerAlpha,
            MidAlpha = midAlpha,
        };
        _lobes.Add(lobe);

        // Rebuild the radial gradient whenever the theme colour changes; ApplyGlowBrush folds in the
        // current directional light origin so the lobe always leans the right way for its position.
        _subscriptions.Add(OnResource(glow, colorKey, value =>
        {
            if (value is Color c)
            {
                lobe.GlowColor = c;
                ApplyGlowBrush(lobe);
            }
        }));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Dispatcher.UIThread.Post(InitComposition, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _ready = false;
        _motionLevel = (MotionLevel)(-1);
        _beaconCts?.Cancel();
        _beaconCts = null;
        _beaconState = (PresenceState)(-1);
        _beaconIntensity = -1;
        _travelCts?.Cancel();
        _travelCts = null;
        _lastSurgeFocus = new Point(0.5, 0.46);
        _haloCts?.Cancel();
        _haloCts = null;
        _haloActive = false;
        _companionCts?.Cancel();
        _companionCts = null;
        _companionActive = false;
        _companionNorm = null;
        if (Find(LobeRole.Companion)?.Border is { } companionBorder)
            companionBorder.Opacity = 0;
        // Stop any running Forever composition animations while the visuals are still live.
        // Avalonia detaches the compositor (nulling CompositionVisual) only AFTER this override
        // returns, so the stored visual refs are still valid here; a virtualized/Path-B detach
        // would otherwise leave them orphaned, ticking the render thread. Re-attach re-acquires
        // visuals via InitComposition and re-applies state, so behaviour is preserved.
        StopVisualAnimations(_selfVisual);
        foreach (var lobe in _lobes)
        {
            StopVisualAnimations(lobe.Visual);
            StopVisualAnimations(lobe.HostVisual);
            lobe.Visual = null;
            lobe.HostVisual = null;
            lobe.SpringPlaced = false;
            lobe.SpringArmMs = 0;
            lobe.SpringTargetX = lobe.SpringTargetY = 0;
            lobe.SpringP0X = lobe.SpringV0X = lobe.SpringP0Y = lobe.SpringV0Y = 0;
        }
        _selfVisual = null;
    }

    private static void StopVisualAnimations(CompositionVisual? visual)
    {
        if (visual is null)
            return;
        visual.StopAnimation("Offset");
        visual.StopAnimation("Scale");
    }

    /// <summary>
    /// Permanent teardown, called by the owning controller when the presence is discarded for good
    /// (never per detach — a re-attach reuses this instance). The resource-colour subscriptions form a
    /// self-contained cycle (Border ↔ resource observable ↔ observer) the GC can already reclaim once the
    /// control is unrooted; disposing them here makes that cleanup explicit instead of relying on
    /// reachability, and cancels any animation tokens still in flight.
    /// </summary>
    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
        _beaconCts?.Cancel();
        _travelCts?.Cancel();
        _haloCts?.Cancel();
        _companionCts?.Cancel();
        GC.SuppressFinalize(this);
    }

    private void InitComposition()
    {
        // A detach may have drained between OnAttached's post and now — never resurrect a dead control.
        if (!_attached)
            return;
        _selfVisual = ElementComposition.GetElementVisual(this);
        foreach (var lobe in _lobes)
        {
            lobe.Visual = ElementComposition.GetElementVisual(lobe.Border);
            lobe.HostVisual = ElementComposition.GetElementVisual(lobe.Host);
        }

        _ready = _selfVisual is not null;
        _motionDirty = true;
        _motionLevel = (MotionLevel)(-1);
        SizeLobes(Bounds.Width, Bounds.Height);
        ApplyState();
        ApplyFocus(animate: false);
        UpdateHalo();
    }

    /// <summary>
    /// A full-bleed panel: it is always stretched to fill its grid cell, so its own desired size is
    /// irrelevant. We size the lobes from the available size here (so <see cref="ArrangeOverride"/> has a
    /// fresh <c>lobe.Size</c> to place against) and return zero.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var w = availableSize.Width;
        var h = availableSize.Height;
        if (double.IsFinite(w) && double.IsFinite(h) && w > 0 && h > 0)
            SizeLobes(w, h);

        foreach (var child in Children)
            child.Measure(availableSize);

        return default;
    }

    /// <summary>
    /// THE structural resize fix. Instead of letting Avalonia arrange each centred lobe host at the panel
    /// centre (which re-syncs the host's composition <c>Offset</c> base to centred-home on every arrange,
    /// clobbering the focal travel — the field collapsed to centre / jittered on a window resize), we
    /// arrange every host AT its focal target. The host's <c>Bounds.Position</c> then equals the focal
    /// point, so the layout-driven Offset sync writes the CORRECT focal Offset each pass — deterministic,
    /// at layout priority, impossible to starve (the old Background-deferred re-pin lost a race against the
    /// OS resize-event flood during a real drag). Smooth focus travel between states still rides the Offset
    /// spring (<see cref="ApplyFocus"/>): <see cref="FocusPoint"/> is not an arrange-affecting property, so
    /// a normal re-aim runs NO arrange and the spring animates freely; only a real resize arranges, and it
    /// cleanly snaps each host to the focal target for the new size.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = finalSize.Width;
        var h = finalSize.Height;
        if (w <= 0 || h <= 0)
        {
            foreach (var child in Children)
                child.Arrange(new Rect(finalSize));
            return finalSize;
        }

        // Keep lobe sizing in lock-step with the final arranged size (a star-sized grid cell can hand a
        // slightly different size to arrange than to measure). Unchanged sets are no-ops in Avalonia, so
        // this does not churn the layout.
        SizeLobes(w, h);

        var fp = FocusPoint;
        var reach = Math.Clamp(FocusReach, 0, 1.5);
        foreach (var lobe in _lobes)
        {
            // Arrange the host at its focal top-left so Bounds.Position == focal point (ClipToBounds is
            // false, so a lobe whose pool extends past the panel edge still renders fully).
            var target = ComputeFocusTarget(lobe, fp, reach, w, h);
            lobe.Host.Arrange(new Rect(target.X, target.Y, lobe.Size, lobe.Size));
        }

        return finalSize;
    }

    private void SizeLobes(double w, double h)
    {
        if (w <= 0 || h <= 0)
            return;

        // Wide canvases previously scaled to their LONG edge (a full-bleed aurora) — but that makes each
        // lobe LARGER than the viewport on a wide window, so translating the field toward a focal point is
        // imperceptible (a blob bigger than the screen barely appears to move). PresenceGeometry sizes every
        // lobe off the SHORT edge so the field stays a contained pool whose travel reads clearly, at any
        // aspect. The halo and companion always hug a focal element / an island, so they never take the
        // compact "fuller" bump that the ambient field does on a narrow surface.
        foreach (var lobe in _lobes)
        {
            var fuller = Compact && lobe.Role is not (LobeRole.Halo or LobeRole.Companion);
            var size = PresenceGeometry.LobeDiameter(lobe.SizeFactor, w, h, fuller);
            lobe.Size = size;
            lobe.Host.Width = size;
            lobe.Host.Height = size;
            lobe.Border.Width = size;
            lobe.Border.Height = size;
            if (lobe.Visual is { } v)
                v.CenterPoint = new Vector3((float)(size / 2), (float)(size / 2), 0);
        }
    }

    // ── State → intensity + motion ──────────────────────────────────────────

    /// <summary>Master luminance gain. The per-state ambient/beacon/halo alphas below are authored
    /// as soft, understated values; this scales the <i>whole</i> field up uniformly so it reads
    /// clearly through the translucent surfaces it sits behind — without disturbing the carefully
    /// balanced relative weights between states and lobes. <see cref="Intensity"/> is an additional
    /// consumer-facing fine multiplier layered on top of this.</summary>
    private const double Gain = 2.6;

    /// <summary>Effective luminance = the consumer <see cref="Intensity"/> (clamped) times the
    /// intrinsic <see cref="Gain"/>. Anything above this still reads correctly because every lobe
    /// opacity is clamped to [0,1] at the point of application.</summary>
    private double Luminance => Math.Clamp(Intensity, 0, 4) * Gain;

    private void ApplyState()
    {
        var state = State;
        var intensity = Luminance;

        SetAmbient(LobeRole.Indigo, AmbientLevel(state, LobeRole.Indigo) * intensity);
        SetAmbient(LobeRole.Violet, AmbientLevel(state, LobeRole.Violet) * intensity);
        SetAmbient(LobeRole.Rose, AmbientLevel(state, LobeRole.Rose) * intensity);
        SetAmbient(LobeRole.Core, AmbientLevel(state, LobeRole.Core) * intensity);

        UpdateBeacon(state, intensity);
        UpdateMotion(state);
    }

    private void SetAmbient(LobeRole role, double opacity)
    {
        var lobe = Find(role);
        if (lobe is not null)
            lobe.Border.Opacity = Math.Clamp(opacity, 0, 1);
    }

    private static double AmbientLevel(PresenceState state, LobeRole role) => role switch
    {
        // Readable but quiet at rest, calmly brighter when engaged. The resting states
        // (Dormant/Idle) keep a gentle, clearly-present glow; Thinking/Streaming lift it enough
        // that the canvas reads as "Lumi is working" without shouting, and Attention warms up
        // (never dimmer than Idle) so a request for the user is felt, not missed.
        // Attention deliberately RECEDES the cool lobes (indigo/violet/core) below their Idle
        // level so the field quiets and cools down — then a strong, warm amber Beacon gathers
        // at the focal point. The contrast (warm pool over a hushed cool field) is what makes a
        // pending request read as "look here", not merely "brighter".
        LobeRole.Indigo => state switch
        {
            PresenceState.Dormant => 0.072,
            PresenceState.Idle => 0.140,
            PresenceState.Thinking => 0.180,
            PresenceState.Streaming => 0.250,
            PresenceState.Attention => 0.080,
            _ => 0.064,
        },
        LobeRole.Violet => state switch
        {
            PresenceState.Dormant => 0.038,
            PresenceState.Idle => 0.082,
            PresenceState.Thinking => 0.128,
            PresenceState.Streaming => 0.180,
            PresenceState.Attention => 0.046,
            _ => 0.036,
        },
        LobeRole.Rose => state switch
        {
            PresenceState.Dormant => 0.00,
            PresenceState.Idle => 0.044,
            PresenceState.Thinking => 0.070,
            PresenceState.Streaming => 0.120,
            PresenceState.Attention => 0.078,
            _ => 0.013,
        },
        LobeRole.Core => state switch
        {
            PresenceState.Dormant => 0.066,
            PresenceState.Idle => 0.130,
            PresenceState.Thinking => 0.158,
            PresenceState.Streaming => 0.200,
            PresenceState.Attention => 0.046,
            _ => 0.056,
        },
        _ => 0.0,
    };

    private enum MotionLevel { Calm, Active, Intense }

    private readonly struct MotionProfile
    {
        public required double DriftAmp { get; init; }
        public required double DriftPeriodMs { get; init; }
        public required double BreatheMin { get; init; }
        public required double BreatheMax { get; init; }
        public required double BreathePeriodMs { get; init; }

        /// <summary>How much wider-than-tall the ambient pool sits. Resting states laminate the field
        /// gently along the horizontal (so an idle chat reads as light pooling <i>along</i> the composer,
        /// not a tight dot), while engaged states tighten back toward a round, focused gather.</summary>
        public required double WidthBias { get; init; }
    }

    private static MotionProfile ProfileFor(MotionLevel level) => level switch
    {
        // Rest is very slow and shallow — aliveness comes from drift, not a visible "pump"; engaged
        // states carry more energy so the field quickens when Lumi gets to work, but the breathe
        // amplitude stays small enough that it reads as ambient light, never a beating element.
        MotionLevel.Calm => new MotionProfile
        {
            DriftAmp = 0.020, DriftPeriodMs = 32000,
            BreatheMin = 0.990, BreatheMax = 1.018, BreathePeriodMs = 12000,
            // Resting wide: an idle chat settles its pool into a broad horizontal wash that lights the
            // canvas BEHIND the composer along its width, rather than a tight centred dot. (At the
            // welcome mark the ambient field is dim and the Halo carries the glow, so this generous
            // bias is felt almost entirely at the composer rest — exactly where it should read.)
            WidthBias = 1.72,
        },
        MotionLevel.Active => new MotionProfile
        {
            DriftAmp = 0.030, DriftPeriodMs = 20000,
            BreatheMin = 0.976, BreatheMax = 1.034, BreathePeriodMs = 7000,
            WidthBias = 1.08,
        },
        _ => new MotionProfile
        {
            DriftAmp = 0.046, DriftPeriodMs = 14000,
            BreatheMin = 0.962, BreatheMax = 1.052, BreathePeriodMs = 4600,
            WidthBias = 1.0,
        },
    };

    private void UpdateMotion(PresenceState state)
    {
        if (!_ready)
            return;

        var level = state switch
        {
            PresenceState.Streaming => MotionLevel.Intense,
            PresenceState.Thinking => MotionLevel.Active,
            PresenceState.Attention => MotionLevel.Active,
            _ => MotionLevel.Calm,
        };

        if (!_motionDirty && level == _motionLevel)
            return;

        _motionLevel = level;
        _motionDirty = false;

        var profile = ProfileFor(level);
        foreach (var lobe in _lobes)
        {
            if (lobe.Signal)
                continue;
            StartMotion(lobe, profile);
        }
    }

    private void StartMotion(Lobe lobe, MotionProfile profile)
    {
        if (lobe.Visual is not { } visual)
            return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        var comp = visual.Compositor;
        var minDim = Math.Min(w, h);
        // Anchor the lobe's drift around the field centre using the SHORT edge (PresenceGeometry.AnchorSpread):
        // on a wide window, normalising the spread to the full width would fling the hues hundreds of px apart
        // into a horizontal smear. Short-edge spread keeps the multi-hue lobes clustered as one coherent pool
        // that still travels as a unit toward the focal point.
        var (bx, by) = PresenceGeometry.AnchorSpread(lobe.AnchorX, lobe.AnchorY, w, h);
        var amp = profile.DriftAmp * minDim * (0.80 + 0.16 * lobe.Phase);

        // Seamless elliptical drift around the lobe's anchor.
        var drift = comp.CreateStableVector3KeyFrameAnimation();
        drift.Target = "Offset";
        drift.InsertKeyFrame(0.00f, new Vector3((float)(bx + amp), (float)by, 0));
        drift.InsertKeyFrame(0.25f, new Vector3((float)bx, (float)(by + amp * 0.7), 0));
        drift.InsertKeyFrame(0.50f, new Vector3((float)(bx - amp), (float)by, 0));
        drift.InsertKeyFrame(0.75f, new Vector3((float)bx, (float)(by - amp * 0.7), 0));
        drift.InsertKeyFrame(1.00f, new Vector3((float)(bx + amp), (float)by, 0));
        drift.Duration = TimeSpan.FromMilliseconds(profile.DriftPeriodMs * (0.85 + 0.12 * lobe.Phase));
        drift.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Offset", drift);

        // Heartbeat breathe. The core lobe breathes a touch deeper. A horizontal WidthBias laminates the
        // resting pool along the composer/hero without disturbing the (uniform) breathe rhythm.
        var depth = lobe.Role == LobeRole.Core ? 1.18 : 1.0;
        var bMin = 1.0 - (1.0 - profile.BreatheMin) * depth;
        var bMax = 1.0 + (profile.BreatheMax - 1.0) * depth;
        var wb = profile.WidthBias;
        var breathe = comp.CreateStableVector3KeyFrameAnimation();
        breathe.Target = "Scale";
        breathe.InsertKeyFrame(0.00f, new Vector3((float)(bMin * wb), (float)bMin, 1f));
        breathe.InsertKeyFrame(0.50f, new Vector3((float)(bMax * wb), (float)bMax, 1f));
        breathe.InsertKeyFrame(1.00f, new Vector3((float)(bMin * wb), (float)bMin, 1f));
        breathe.Duration = TimeSpan.FromMilliseconds(profile.BreathePeriodMs * (0.82 + 0.14 * lobe.Phase));
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Scale", breathe);
    }

    /// <summary>
    /// Drives the <see cref="LobeRole.Beacon"/> "gaze pulse" — a living rhythm of light
    /// gathered at the focal point that makes Lumi feel <i>engaged</i>. Tempo, colour and
    /// brightness encode the moment: a calm accent pulse while thinking, a quicker, brighter
    /// heartbeat while streaming, and a warm, clearly-insistent amber beckon when Lumi needs
    /// the user (a pending question). It rests fully dark in every calm state, so the rhythm
    /// only ever appears when there is genuinely something to draw the eye toward.
    /// </summary>
    private void UpdateBeacon(PresenceState state, double intensity)
    {
        var lobe = Find(LobeRole.Beacon);
        if (lobe is null)
            return;

        var (active, colorKey, lo, hi, sMin, sMax, periodMs) = state switch
        {
            PresenceState.Thinking => (true, "Color.AccentDefault", 0.070, 0.135, 0.975, 1.030, 2800.0),
            PresenceState.Streaming => (true, "Color.AccentDefault", 0.090, 0.175, 0.968, 1.040, 2000.0),
            // Attention keeps the amber hue CONSTANT (a high opacity floor) and beckons by
            // pulsing in SIZE rather than fading out — so the focal point reads unmistakably
            // warm at every instant (never momentarily cool like a fading pulse would), while
            // the deep size throb makes it breathe insistently. This is what turns "a question
            // is waiting" into a felt, warm gather the eye is drawn to.
            PresenceState.Attention => (true, "Palette.Warning400", 0.235, 0.345, 0.94, 1.12, 2100.0),
            _ => (false, "Color.AccentDefault", 0.0, 0.0, 1.0, 1.0, 0.0),
        };

        lo = Math.Clamp(lo * intensity, 0, 1);
        hi = Math.Clamp(hi * intensity, 0, 1);

        // Already pulsing for this exact state — don't restart (avoids a hitch on resize).
        if (active && _beaconState == state && Math.Abs(_beaconIntensity - intensity) < 0.001)
            return;

        _beaconCts?.Cancel();
        _beaconCts = null;
        // A beacon state change (engage or release) supersedes any resting travel surge on the same
        // Border/Visual — cancel it so the two never animate the focal lobe at once.
        _travelCts?.Cancel();
        _travelCts = null;
        _beaconState = active ? state : (PresenceState)(-1);
        _beaconIntensity = intensity;

        if (!active)
        {
            lobe.Border.Opacity = 0;
            if (lobe.Visual is { } idle)
                SettleScale(idle);
            return;
        }

        if (this.TryFindResource(colorKey, ActualThemeVariant, out var value) && value is Color c)
            lobe.Border.Background = BuildGlow(c, 168, 60);

        // Brightness throb (UI thread, cheap — restarts only on a state change).
        var anim = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(periodMs),
            IterationCount = Avalonia.Animation.IterationCount.Infinite,
            Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, lo) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0.5d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, hi) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, lo) },
                },
            },
        };
        _beaconCts = new System.Threading.CancellationTokenSource();
        _ = anim.RunAsync(lobe.Border, _beaconCts.Token);

        // Size throb (render thread) — the light gently swells and contracts in time with the
        // brightness, so the pulse reads as a living breath rather than a flat fade.
        if (lobe.Visual is { } visual)
        {
            var comp = visual.Compositor;
            var sc = comp.CreateStableVector3KeyFrameAnimation();
            sc.Target = "Scale";
            sc.InsertKeyFrame(0f, new Vector3((float)sMin, (float)sMin, 1f));
            sc.InsertKeyFrame(0.5f, new Vector3((float)sMax, (float)sMax, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
            sc.InsertKeyFrame(1f, new Vector3((float)sMin, (float)sMin, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
            sc.Duration = TimeSpan.FromMilliseconds(periodMs);
            sc.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", sc);
        }
    }

    /// <summary>Cleanly stops an infinite composition <c>Scale</c> throb by easing the visual
    /// back to its natural size and pinning the base value there.</summary>
    private static void SettleScale(CompositionVisual visual)
    {
        var comp = visual.Compositor;
        var sc = comp.CreateStableVector3KeyFrameAnimation();
        sc.Target = "Scale";
        sc.InsertKeyFrame(1f, Vector3.One, new Avalonia.Animation.Easings.CubicEaseOut());
        sc.Duration = TimeSpan.FromMilliseconds(300);
        sc.IterationBehavior = AnimationIterationBehavior.Count;
        visual.StartAnimation("Scale", sc);
        visual.Scale = Vector3.One;
    }

    /// <summary>
    /// Drives the welcome/focal <see cref="LobeRole.Halo"/> lobe: when <see cref="Halo"/>
    /// is on, a slow, gentle opacity breath makes the gathered light around the focal point
    /// read as a living luminance; when off, the lobe rests fully transparent.
    /// </summary>
    private void UpdateHalo()
    {
        var lobe = Find(LobeRole.Halo);
        if (lobe is null)
            return;

        var active = Halo;
        // Already breathing — don't restart (avoids a hitch on resize / re-attach).
        if (active && _haloActive)
            return;

        _haloCts?.Cancel();
        _haloCts = null;
        _haloActive = active;

        if (!active)
        {
            // Leaving the welcome canvas for a chat. Rather than dissolving the luminance *in place*
            // at the hero (which reads as a cross-fade — the bright pool fades out up top while the
            // dim ambient field fades in low), HOLD the halo bright while the focus glide carries it
            // DOWN from the hero to the composer, THEN dissolve once it has arrived. So the eye sees a
            // single bright body of light *pour down* into the conversation and settle, handing off to
            // the now-brightened ambient field at the bottom. FillMode.Forward holds it at 0 when done.
            var border = lobe.Border;
            var fadeCts = new System.Threading.CancellationTokenSource();
            _haloCts = fadeCts;
            var haloHigh = Math.Clamp(0.205 * Luminance, 0, 1);
            var fadeOut = new Avalonia.Animation.Animation
            {
                Duration = TimeSpan.FromMilliseconds(1300),
                Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
                FillMode = Avalonia.Animation.FillMode.Forward,
                Children =
                {
                    new Avalonia.Animation.KeyFrame
                    {
                        Cue = new Avalonia.Animation.Cue(0d),
                        Setters = { new Avalonia.Styling.Setter(OpacityProperty, haloHigh) },
                    },
                    // Stay lit through the descent (the focus glide settles the halo at the composer in
                    // ~0.9 s), so the bright pool is visible the whole way down — not just at the ends.
                    new Avalonia.Animation.KeyFrame
                    {
                        Cue = new Avalonia.Animation.Cue(0.56d),
                        Setters = { new Avalonia.Styling.Setter(OpacityProperty, haloHigh) },
                    },
                    // Then melt into the (now Idle-bright) ambient field once it has landed low.
                    new Avalonia.Animation.KeyFrame
                    {
                        Cue = new Avalonia.Animation.Cue(1d),
                        Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) },
                    },
                },
            };
            _ = fadeOut.RunAsync(border, fadeCts.Token);
            if (lobe.Visual is { } idle)
                SettleScale(idle);
            return;
        }

        // A calm "this is alive" luminance that gently swells in both brightness and size —
        // never fully extinguished, never a hard pulse — so the focal mark (e.g. the Lumi
        // icon on the welcome screen) reads as quietly breathing.
        // The halo breath keeps a HIGH floor and a SMALL swing (0.16 → 0.205) so the welcome mark
        // reads as a steady luminance that merely *shimmers* — never the ~2× opacity pump that made
        // the breathing feel like a UI element beating. Scaled by the same master luminance as the
        // ambient field, so the welcome mark's glow brightens in lock-step and stays tunable.
        var haloLo = Math.Clamp(0.160 * Luminance, 0, 1);
        var haloHi = Math.Clamp(0.205 * Luminance, 0, 1);
        var anim = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(6000),
            IterationCount = Avalonia.Animation.IterationCount.Infinite,
            Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, haloLo) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0.5d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, haloHi) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, haloLo) },
                },
            },
        };

        _haloCts = new System.Threading.CancellationTokenSource();
        _ = anim.RunAsync(lobe.Border, _haloCts.Token);

        if (lobe.Visual is { } visual)
        {
            var comp = visual.Compositor;
            var sc = comp.CreateStableVector3KeyFrameAnimation();
            sc.Target = "Scale";
            sc.InsertKeyFrame(0f, new Vector3(0.99f, 0.99f, 1f));
            sc.InsertKeyFrame(0.5f, new Vector3(1.03f, 1.03f, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
            sc.InsertKeyFrame(1f, new Vector3(0.99f, 0.99f, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
            sc.Duration = TimeSpan.FromMilliseconds(6000);
            sc.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", sc);
        }
    }

    /// <summary>Soft deceleration for focus travel — the field moves off promptly then <i>settles</i>
    /// into place, which reads as a calm, alive glide rather than a mechanical slide or a snap.</summary>
    private static readonly Easing FocusEase = new CubicEaseOut();

    /// <summary>
    /// Pushes every lobe host to its focus-travel target. The motion itself is owned by a per-lobe
    /// velocity-preserving <b>spring</b> (see <see cref="AimHostSpring"/>), sampled into render-thread
    /// <c>Offset</c> keyframes: the compositor plays the glide on the <b>render thread</b>, so it stays
    /// smooth even while the UI thread is busy laying out a transition — and a re-aim mid-glide is picked
    /// up from the host's live position AND momentum, never snapped or stalled. Pass
    /// <paramref name="animate"/> = <c>false</c> to place instantly (initial layout / compact toggle),
    /// where animating would otherwise lag behind a continuous relayout.
    /// </summary>
    private void ApplyFocus(bool animate = true)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        var fp = FocusPoint;
        var reach = Math.Clamp(FocusReach, 0, 1.5);

        foreach (var lobe in _lobes)
        {
            if (lobe.HostVisual is null)
                continue;

            var target = ComputeFocusTarget(lobe, fp, reach, w, h);
            // The first placement (and any explicit instant request — initial layout / compact toggle)
            // snaps; every other re-aim hands the new target to the velocity-preserving spring, which
            // picks the move up from the lobe's live position AND momentum (never reset to zero). That
            // continuity is what makes the travel flow instead of stutter when the target keeps moving.
            if (animate && lobe.SpringPlaced)
                AimHostSpring(lobe, target);
            else
                SnapHostSpring(lobe, target);
        }

        // Lean the ambient field's light away from the edge it now hugs (position-driven luminance).
        UpdateLightDirection(fp);

        // A meaningful resting move glides invisibly without a focal cue, so bloom the gaze along it.
        if (animate)
            MaybeTravelSurge(fp);
        else
            _lastSurgeFocus = fp; // instant placement is not a "move"; keep the surge baseline in sync.
    }

    /// <summary>DEBUG-ONLY: a snapshot of the focus pipeline so a headless harness can read the actual
    /// computed targets and live host base-offsets (not guess from rendered pixels). Not part of the
    /// public API; gated to <c>Lumi.Tests</c> via InternalsVisibleTo.</summary>
    internal string DebugFocusSnapshot()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var fp = FocusPoint;
        var reach = Math.Clamp(FocusReach, 0, 1.5);
        var fx = (Clamp01(fp.X) - 0.5) * w * reach;
        var fy = (Clamp01(fp.Y) - 0.5) * h * reach;
        var sb = new System.Text.StringBuilder();
        sb.Append($"bounds={w:F0}x{h:F0} fp={fp.X:F2},{fp.Y:F2} reach={reach:F2} fx={fx:F0} fy={fy:F0}\n");
        foreach (var lobe in _lobes)
        {
            var off = lobe.HostVisual is { } hv ? hv.Offset : default;
            var op = lobe.Border?.Opacity ?? -1;
            sb.Append($"  {lobe.Role,-9} follow={lobe.Follow:F2} sig={(lobe.Signal ? 1 : 0)} " +
                      $"size={lobe.Size:F0} op={op:F2} targetY={lobe.SpringTargetY:F0} baseOffY={off.Y:F0}\n");
        }
        return sb.ToString();
    }

    /// <summary>DEBUG-ONLY: the largest lobe diameter (px) currently laid out. A headless test asserts
    /// this stays within the surface's SHORT edge on a wide surface — the permanent guard against the
    /// "lobes scale to the long edge" regression that made the field larger than the viewport (and so its
    /// travel imperceptible). Gated to <c>Lumi.Tests</c> via InternalsVisibleTo.</summary>
    internal double DebugMaxLobeDiameter()
    {
        double max = 0;
        foreach (var lobe in _lobes)
            if (lobe.Size > max)
                max = lobe.Size;
        return max;
    }

    /// <summary>DEBUG-ONLY: the companion pool's live base host <c>Offset</c> (px, from centred home), so
    /// a headless probe can assert <see cref="SplitToIsland"/> places it horizontally toward a right-side
    /// island (rather than drifting vertically). Gated to <c>Lumi.Tests</c> via InternalsVisibleTo.</summary>
    internal (double X, double Y) DebugCompanionOffset()
    {
        var lobe = Find(LobeRole.Companion);
        if (lobe?.HostVisual is { } hv)
        {
            // Report TRAVEL from the centred home (subtract the per-lobe centring origin), so a probe
            // measures how far the pool moved toward the island rather than its absolute panel offset.
            var (ox, oy) = LobeOrigin(lobe.Size, Bounds.Width, Bounds.Height);
            return (hv.Offset.X - ox, hv.Offset.Y - oy);
        }
        return (0, 0);
    }

    /// <summary>DEBUG-ONLY: the main field's bright-centre (<see cref="LobeRole.Core"/>) live travel from
    /// its centred home, so a probe can assert the companion splits LEVEL with the field (same vertical
    /// space) rather than at the island's own height. Gated to <c>Lumi.Tests</c> via InternalsVisibleTo.</summary>
    internal (double X, double Y) DebugFieldCenterOffset()
    {
        var lobe = Find(LobeRole.Core);
        if (lobe?.HostVisual is { } hv)
        {
            var (ox, oy) = LobeOrigin(lobe.Size, Bounds.Width, Bounds.Height);
            return (hv.Offset.X - ox, hv.Offset.Y - oy);
        }
        return (0, 0);
    }

    /// <summary>
    /// The composition <c>Offset</c> that places a lobe of the given diameter <b>dead-centre</b> in the
    /// field. Setting a lobe host's <c>Offset</c> <i>replaces</i> the layout-assigned position (it is
    /// absolute from the panel's top-left), so every focus/companion target must add this origin to its
    /// travel — otherwise <c>Offset=0</c> parks the lobe at the top-left and the whole field reads as
    /// left/top-biased (worse the wider the window). This is the single source of "where centre is".
    /// </summary>
    private static (double X, double Y) LobeOrigin(double lobeSize, double w, double h)
        => ((w - lobeSize) / 2.0, (h - lobeSize) / 2.0);

    /// <summary>The follow factor that marks the field's bright visual centre (matches the
    /// <see cref="LobeRole.Core"/> lobe). The companion borrows it for its vertical placement so a split
    /// sits LEVEL with the main pool — a side-by-side separation, never one pool stacked above the other.</summary>
    private const double FieldCenterFollow = 0.90;

    /// <summary>The host-offset a lobe is currently travelling toward: its <see cref="FocusPoint"/>
    /// reach (scaled by the lobe's follow factor) for the field, or the parked island anchor for the
    /// companion pool. All placement is delegated to <see cref="PresenceGeometry"/> so the coordinate
    /// system stays single-sourced and unit-tested, then offset by the lobe's centring origin so a
    /// zero <see cref="PresenceGeometry.FieldOffset"/> sits the lobe at the panel centre.</summary>
    private Vector3 ComputeFocusTarget(Lobe lobe, Point fp, double reach, double w, double h)
    {
        var (originX, originY) = LobeOrigin(lobe.Size, w, h);

        if (lobe.Role == LobeRole.Companion)
        {
            if (_companionNorm is not { } norm)
            {
                // Merged (no island open): home onto the field's OWN bright centre (the focus point damped
                // by the field-centre follow), not a static panel centre. So closing an island RETRACTS the
                // companion in a single glide back into the live pool — the two presences visibly becoming
                // one — instead of sliding off to a centre the field left long ago.
                var (hx, hy) = PresenceGeometry.FieldOffset(fp.X, fp.Y, FieldCenterFollow, reach, w, h);
                return new Vector3((float)(originX + hx), (float)(originY + hy), 0f);
            }
            // Split SIDE-BY-SIDE: take the island's HORIZONTAL position, but place it at the SAME vertical
            // level as the main field (the focus Y damped by the field-centre follow). So the light reads
            // as one pool cleaving into two LEVEL pools, not one drifting up/down to the island's height.
            var (cx, _) = PresenceGeometry.CompanionOffset(norm.X, 0.5, w, h);
            var (_, levelY) = PresenceGeometry.FieldOffset(fp.X, fp.Y, FieldCenterFollow, reach, w, h);
            return new Vector3((float)(originX + cx), (float)(originY + levelY), 0f);
        }

        var (ox, oy) = PresenceGeometry.FieldOffset(fp.X, fp.Y, lobe.Follow, reach, w, h);
        return new Vector3((float)(originX + ox), (float)(originY + oy), 0f);
    }

    /// <summary>
    /// Re-aims a lobe host's composition <c>Offset</c> toward <paramref name="target"/> with a
    /// velocity-preserving damped spring (<see cref="PresenceSpring"/>). The in-flight trajectory's live
    /// <c>(position, velocity)</c> is read analytically and a NEW trajectory is seeded from that exact
    /// state toward the new target — so a re-aim mid-glide (the rapid focus-follow timer, an island lean,
    /// a window resize) keeps both position AND momentum continuous. Unlike an easing tween, the velocity
    /// is never reset to zero, which is precisely what makes the travel <i>flow</i> rather than stutter.
    /// The continuous curve is sampled into a finite render-thread keyframe animation, so it stays smooth
    /// even while the UI thread is busy laying out the transition.
    /// </summary>
    private void AimHostSpring(Lobe lobe, Vector3 target)
    {
        if (lobe.HostVisual is not { } hv)
            return;

        var omega = OmegaFor(lobe);
        var zeta = ZetaFor(lobe);

        // Live analytic state of the trajectory currently playing on the render thread.
        var t = Math.Max(0.0, (_clock.Elapsed.TotalMilliseconds - lobe.SpringArmMs) / 1000.0);
        var (px, vx) = PresenceSpring.Evaluate(lobe.SpringP0X, lobe.SpringV0X, omega, zeta, t);
        var (py, vy) = PresenceSpring.Evaluate(lobe.SpringP0Y, lobe.SpringV0Y, omega, zeta, t);
        var curX = lobe.SpringTargetX + px;
        var curY = lobe.SpringTargetY + py;

        // The active trajectory already heads here (sub-pixel) — let it finish rather than churn fresh
        // keyframes every 110 ms tick of a micro-moving target for no visible gain.
        if (Math.Abs(target.X - lobe.SpringTargetX) < 0.5 && Math.Abs(target.Y - lobe.SpringTargetY) < 0.5)
            return;

        SeedSpring(lobe, curX, curY, vx, vy, target);
        StartSpringTrajectory(hv, lobe, omega, zeta);
    }

    /// <summary>Instantly places a lobe host at <paramref name="target"/> (initial layout, or a compact
    /// toggle) and resets its spring to rest there, so the next animated re-aim glides from a known state.</summary>
    private void SnapHostSpring(Lobe lobe, Vector3 target)
    {
        if (lobe.HostVisual is not { } hv)
            return;

        SeedSpring(lobe, target.X, target.Y, 0, 0, target);
        lobe.SpringArmMs = _clock.Elapsed.TotalMilliseconds;
        // Instant placement: set the host's BASE Offset directly. A composition property holds its base
        // value, so the lobe rests exactly here until the next animated re-aim glides away from it.
        hv.Offset = target;
    }

    /// <summary>Seeds a lobe's spring trajectory: target plus the displacement/velocity it starts from.</summary>
    private static void SeedSpring(Lobe lobe, double curX, double curY, double velX, double velY, Vector3 target)
    {
        lobe.SpringTargetX = target.X;
        lobe.SpringTargetY = target.Y;
        lobe.SpringP0X = curX - target.X;
        lobe.SpringV0X = velX;
        lobe.SpringP0Y = curY - target.Y;
        lobe.SpringV0Y = velY;
        lobe.SpringPlaced = true;
    }

    /// <summary>
    /// Samples the seeded spring response into a finite render-thread <c>Offset</c> keyframe animation.
    /// Keyframe cues are placed proportionally to time (so playback is not time-warped) but bunched toward
    /// the start (<c>frac^1.6</c>) where velocity is highest, giving the most fidelity to the lead of the
    /// glide. Keyframe 0 is the live start state (so position is continuous by construction) and the
    /// host's BASE <c>Offset</c> is pinned to the target so the field HOLDS at the focal point when this
    /// finite animation completes — without that pin, a composition property reverts to its base (centre)
    /// the instant the animation ends, so the lobe would spring toward focus then slide back to centre.
    /// </summary>
    private void StartSpringTrajectory(CompositionVisual hv, Lobe lobe, double omega, double zeta)
    {
        lobe.SpringArmMs = _clock.Elapsed.TotalMilliseconds;

        var horizonMs = Math.Clamp(PresenceSpring.SettleSeconds(omega, zeta) * 1000.0, 220.0, 2000.0);
        var comp = hv.Compositor;
        var move = comp.CreateStableVector3KeyFrameAnimation();
        move.Target = "Offset";
        // Explicit live start (not "this.StartingValue"): we pin the base Offset to the target below, after
        // which StartingValue would read as the target and erase the from-position. The seeded start is
        // target + initial displacement (== the lobe's live rendered position at re-aim).
        var startX = lobe.SpringTargetX + lobe.SpringP0X;
        var startY = lobe.SpringTargetY + lobe.SpringP0Y;
        move.InsertKeyFrame(0f, new Vector3((float)startX, (float)startY, 0f));

        const int samples = 24;
        for (int i = 1; i <= samples; i++)
        {
            var frac = Math.Pow(i / (double)samples, 1.6);
            Vector3 pos;
            if (i == samples)
            {
                pos = new Vector3((float)lobe.SpringTargetX, (float)lobe.SpringTargetY, 0f);
            }
            else
            {
                var tt = frac * horizonMs / 1000.0;
                var (dx, _) = PresenceSpring.Evaluate(lobe.SpringP0X, lobe.SpringV0X, omega, zeta, tt);
                var (dy, _) = PresenceSpring.Evaluate(lobe.SpringP0Y, lobe.SpringV0Y, omega, zeta, tt);
                pos = new Vector3((float)(lobe.SpringTargetX + dx), (float)(lobe.SpringTargetY + dy), 0f);
            }
            move.InsertKeyFrame((float)frac, pos, LinearFocusEase);
        }

        move.Duration = TimeSpan.FromMilliseconds(horizonMs);
        move.IterationBehavior = AnimationIterationBehavior.Count;
        // Pin the resting Offset to the target so the completed (non-looping) focus animation HOLDS the
        // lobe at the focal point instead of reverting to base (centre).
        hv.Offset = new Vector3((float)lobe.SpringTargetX, (float)lobe.SpringTargetY, 0f);
        hv.StartAnimation("Offset", move);
    }

    /// <summary>Per-lobe spring stiffness ω (rad/s), derived from the lobe's focus time-constant: the
    /// gaze/signal lobes are stiffest (lead quickest), ambient lobes softer (trail behind), and the
    /// companion pool softest of all (a slow, graceful split into an island). The range is deliberately
    /// low so the focus glide reads as a tracked <i>travel</i> (~1.2–1.5 s for the field) rather than a
    /// fast snap — the previous [2,9] range settled in ~0.5 s, which is why the motion was "too fast".</summary>
    private static double OmegaFor(Lobe lobe) => Math.Clamp(1.0 / Math.Max(0.05, lobe.FocusTau), 1.6, 5.0);

    /// <summary>Per-lobe damping ratio ζ. The leading gaze/core lobes are slightly underdamped for an
    /// eager, alive arrival (a hair of overshoot); the broad ambient lobes and the companion pool are
    /// critically damped so the large, slow pools settle with no wobble.</summary>
    private static double ZetaFor(Lobe lobe) =>
        lobe.Role is LobeRole.Beacon or LobeRole.Pulse or LobeRole.Halo or LobeRole.Core ? 0.86 : 1.0;

    private static readonly Easing LinearFocusEase = new LinearEasing();

    /// <summary>Per-lobe focus-travel duration: gaze/signal lobes lead quick (~520 ms), ambient lobes
    /// trail (~885 ms), and the companion splits slowest (~1.4 s). Slow enough that the eye can track the
    /// glide as motion (not a snap). Shared by the host explicit Offset animation and the travel surge so
    /// the focal cue and the glide stay in lock-step.</summary>
    private static double FocusDurationMs(Lobe lobe) => Math.Clamp(lobe.FocusTau * 3400.0, 520.0, 1500.0);

    /// <summary>
    /// While resting (Idle/Dormant) the Beacon heartbeat is dark, so a focus move would glide with no
    /// focal light to track — perceptually "the ambient shifted", not "something moved". This blooms the
    /// focal Beacon along the glide (a soft gather → lead → dissolve) so the eye is drawn to the new spot,
    /// then released back to the calm field. Only fires on a meaningful jump and never while engaged (the
    /// active beacon already owns the focal point).
    /// </summary>
    private void MaybeTravelSurge(Point fp)
    {
        var d = Math.Abs(fp.X - _lastSurgeFocus.X) + Math.Abs(fp.Y - _lastSurgeFocus.Y);
        _lastSurgeFocus = fp;

        var resting = State is PresenceState.Idle or PresenceState.Dormant;
        if (!resting || d < 0.06)
            return;

        TravelSurge();
    }

    /// <summary>Bloom-and-dissolve the focal Beacon to lead the eye along a resting focus glide — the
    /// bloom builds as the light travels and <b>crescendos as it arrives</b> (a soft overshoot that reads
    /// as the glow gathering where it lands), then dissolves back into the calm field.</summary>
    private void TravelSurge()
    {
        if (!_ready || Find(LobeRole.Beacon) is not { } lobe || lobe.Visual is not { } visual)
            return;

        _travelCts?.Cancel();
        _travelCts = new System.Threading.CancellationTokenSource();
        var token = _travelCts.Token;

        var durationMs = FocusDurationMs(lobe) * 1.45; // the bloom rides through arrival and lingers a touch.
        var peak = Math.Clamp(0.56 * Math.Clamp(Intensity, 0, 4), 0, 0.72);

        // Opacity builds while travelling and PEAKS as the light arrives (~0.66), then dissolves — so the
        // bloom reads as the glow *gathering where it lands*, not flaring at the start of the move.
        var fade = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            IterationCount = new Avalonia.Animation.IterationCount(1),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0.66d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, peak) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) },
                },
            },
        };
        _ = fade.RunAsync(lobe.Border, token);

        // The light gathers small while travelling, then BLOOMS past full as it arrives (~0.72) and
        // relaxes back — a soft overshoot that makes the settle read as a breath of light, not a stop.
        var comp = visual.Compositor;
        var grow = comp.CreateStableVector3KeyFrameAnimation();
        grow.Target = "Scale";
        grow.InsertKeyFrame(0f, new Vector3(0.80f, 0.80f, 1f));
        grow.InsertKeyFrame(0.72f, new Vector3(1.20f, 1.20f, 1f), FocusEase);
        grow.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
        grow.Duration = TimeSpan.FromMilliseconds(durationMs);
        grow.IterationBehavior = AnimationIterationBehavior.Count;
        visual.StartAnimation("Scale", grow);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    // ── One-shot pulses ─────────────────────────────────────────────────────

    /// <summary>Fire a transient effect over the ambient field.</summary>
    public void Pulse(PresencePulse kind)
    {
        var lobe = Find(LobeRole.Pulse);
        if (lobe is null)
            return;

        var (colorKey, innerAlpha, midAlpha, peak, startScale, endScale, durationMs) = kind switch
        {
            // Every pulse is a soft "breath of light" — a brief swell of glow rather than
            // an expanding ring. Tuned to be clearly felt as a reaction, yet still elegant.
            PresencePulse.Awaken => ("Color.AccentDefault", (byte)160, (byte)60, 0.23, 0.90, 1.16, 1600.0),
            PresencePulse.Bloom => ("Palette.Success400", (byte)160, (byte)60, 0.26, 0.92, 1.18, 1550.0),
            PresencePulse.Ripple => ("Color.AccentDefault", (byte)165, (byte)62, 0.18, 0.94, 1.12, 900.0),
            // Settling into a chat: a soft, slow accent swell that rides the focus glide down from
            // the welcome mark, so opening a conversation reads as the presence *arriving* there.
            PresencePulse.Settle => ("Color.AccentDefault", (byte)146, (byte)54, 0.17, 0.95, 1.11, 1400.0),
            // Lumi produced a file — a soft green breath of "made something".
            PresencePulse.Create => ("Palette.Success400", (byte)158, (byte)58, 0.24, 0.92, 1.17, 1500.0),
            // Lumi changed the workspace — a soft accent breath.
            PresencePulse.Edit => ("Color.AccentDefault", (byte)155, (byte)58, 0.19, 0.94, 1.13, 1150.0),
            // Lumi referenced the web — a cool, quiet blue breath.
            PresencePulse.Browse => ("Palette.Accent400", (byte)152, (byte)56, 0.18, 0.94, 1.13, 1250.0),
            // A brief, tasteful alert when something needs attention (e.g. an error).
            _ => ("Palette.Danger400", (byte)175, (byte)66, 0.30, 0.92, 1.18, 1250.0),
        };

        if (this.TryFindResource(colorKey, ActualThemeVariant, out var value) && value is Color c)
            lobe.Border.Background = BuildGlow(c, innerAlpha, midAlpha);

        // Opacity envelope (UI thread, one-shot, cheap).
        var fade = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            IterationCount = new Avalonia.Animation.IterationCount(1),
            Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0.28d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, peak) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) },
                },
            },
        };
        _ = fade.RunAsync(lobe.Border);

        // Expanding ring (render thread).
        if (lobe.Visual is { } visual)
        {
            var comp = visual.Compositor;
            var scale = comp.CreateStableVector3KeyFrameAnimation();
            scale.Target = "Scale";
            scale.InsertKeyFrame(0f, new Vector3((float)startScale, (float)startScale, 1f));
            scale.InsertKeyFrame(1f, new Vector3((float)endScale, (float)endScale, 1f), new Avalonia.Animation.Easings.CubicEaseOut());
            scale.Duration = TimeSpan.FromMilliseconds(durationMs);
            scale.IterationBehavior = AnimationIterationBehavior.Count;
            visual.StartAnimation("Scale", scale);
        }
    }

    // ── Directional travel ──────────────────────────────────────────────────

    /// <summary>
    /// One-shot: the whole field leans toward <paramref name="edge"/> and settles back to
    /// rest, with a soft accompanying breath of light. Hands presence <i>off</i> toward an
    /// adjacent surface — e.g. the chat field reaching toward a workspace opening on its
    /// right, so the glow visibly travels before it "splits" across the seam. The lean is a
    /// gentle, decaying settle (not a big symmetric swing) so the split reads as a poised
    /// hand-off rather than a bounce.
    /// </summary>
    public void Emit(PresenceEdge edge)
    {
        if (!_ready || _selfVisual is not { } sv)
            return;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        var (dx, dy) = EdgeVector(edge);
        var span = edge is PresenceEdge.Left or PresenceEdge.Right ? w : h;
        // A restrained lean toward the seam (was a 0.20·span lunge that swung the whole field out and
        // all the way back — too much travel, so it read as a bounce). The companion pool carries the
        // rightward presence into the island; the field only needs to nod toward it, then settle.
        var dist = 0.11 * span;
        var peak = new Vector3((float)(dx * dist), (float)(dy * dist), 0f);

        var comp = sv.Compositor;
        var anim = comp.CreateStableVector3KeyFrameAnimation();
        anim.Target = "Offset";
        anim.InsertKeyFrame(0f, Vector3.Zero);
        // Lean out, then ease home in ever-shallower steps — the same decaying-settle shape as the
        // send/finish impulse, so the field eases to rest with no perceptible snap-back.
        anim.InsertKeyFrame(0.30f, peak, new Avalonia.Animation.Easings.CubicEaseOut());
        anim.InsertKeyFrame(0.56f, new Vector3(peak.X * 0.44f, peak.Y * 0.44f, 0f), new Avalonia.Animation.Easings.SineEaseInOut());
        anim.InsertKeyFrame(0.78f, new Vector3(peak.X * 0.16f, peak.Y * 0.16f, 0f), new Avalonia.Animation.Easings.SineEaseInOut());
        anim.InsertKeyFrame(1f, Vector3.Zero, new Avalonia.Animation.Easings.SineEaseInOut());
        anim.Duration = TimeSpan.FromMilliseconds(1280);
        anim.IterationBehavior = AnimationIterationBehavior.Count;
        sv.StartAnimation("Offset", anim);
        sv.Offset = Vector3.Zero;

        Pulse(PresencePulse.Ripple);
    }

    /// <summary>
    /// One-shot: the whole field <i>rises up</i> off its resting place and eases back down to its
    /// (newly raised) focus baseline — the presence lifting into the conversation the moment Lumi takes
    /// a message. Paired by the controller with a focus retarget upward, so sending in an existing chat
    /// reads as the glow leaving the composer and ascending to where the answer forms, not a glow that
    /// barely shifts in place.
    /// </summary>
    public void Lift() => ImpulseSettle(-0.15, 1300.0);

    /// <summary>
    /// One-shot mirror of <see cref="Lift"/>: the whole field <i>pours down</i> and eases home — the
    /// presence descending into the conversation. Paired by the controller with a focus retarget
    /// downward, so opening an existing chat from the welcome canvas (and finishing a turn) reads as the
    /// light visibly travelling <i>down</i> from the hero/answer to settle at the composer, not an
    /// instant swap.
    /// </summary>
    public void Descend() => ImpulseSettle(0.15, 1450.0);

    /// <summary>
    /// The shared "kick &amp; settle" used by <see cref="Lift"/> / <see cref="Descend"/>: a quick, smooth
    /// surge of the whole field by <paramref name="dyFraction"/>·height, then a long, monotonically
    /// decaying tail back to rest — the shape of a critically-damped impulse, so it reads as a confident
    /// move that <i>settles</i> rather than a spring that bounces back (the previous up-then-snap-back
    /// felt amateurish). The sustained new position is carried by the focus glide underneath; this is the
    /// felt kick on top of it. Animates the control's own composition visual so it composes cleanly over
    /// every per-lobe focus / drift / breathe / beacon animation, in any state.
    /// </summary>
    private void ImpulseSettle(double dyFraction, double durationMs)
    {
        if (!_ready || _selfVisual is not { } sv)
            return;
        var h = Bounds.Height;
        if (h <= 0)
            return;

        var peak = (float)(dyFraction * h);
        var comp = sv.Compositor;
        var anim = comp.CreateStableVector3KeyFrameAnimation();
        anim.Target = "Offset";
        anim.InsertKeyFrame(0f, Vector3.Zero);
        // Quick decelerating surge to the peak, then a gentle, ever-shallower decay home — each segment
        // smaller and slower than the last, so the field eases to rest with no perceptible snap-back.
        anim.InsertKeyFrame(0.28f, new Vector3(0f, peak, 0f), new Avalonia.Animation.Easings.CubicEaseOut());
        anim.InsertKeyFrame(0.52f, new Vector3(0f, peak * 0.58f, 0f), new Avalonia.Animation.Easings.SineEaseInOut());
        anim.InsertKeyFrame(0.76f, new Vector3(0f, peak * 0.24f, 0f), new Avalonia.Animation.Easings.SineEaseInOut());
        anim.InsertKeyFrame(1f, Vector3.Zero, new Avalonia.Animation.Easings.SineEaseInOut());
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        anim.IterationBehavior = AnimationIterationBehavior.Count;
        sv.StartAnimation("Offset", anim);
        sv.Offset = Vector3.Zero;
    }

    /// <summary>
    /// <paramref name="islandPoint"/> (normalized 0..1 in the field's own space), where it parks and
    /// softly illuminates an opened companion island. The first call blooms the pool up from the
    /// field; later calls (e.g. switching browser → diff) only re-aim it, so it never re-flashes.
    /// Pair with <see cref="Emit"/>(Right) for the "field surges toward the seam, then a companion
    /// separates and travels into the island" gesture; close it with <see cref="Merge"/>.
    /// </summary>
    public bool SplitToIsland(Point islandPoint)
    {
        if (!_ready)
            return false;
        var lobe = Find(LobeRole.Companion);
        if (lobe is null)
            return false;

        // Hand the travel to the velocity-preserving spring (via ApplyFocus): it eases the companion host
        // from its live position AND momentum out to the island anchor (and re-aims smoothly if the island
        // later moves or another opens), sampled onto the render thread.
        _companionNorm = islandPoint;
        ApplyFocus();

        // Already parked at an island — this is just a re-aim, so don't re-bloom.
        if (_companionActive)
            return true;

        _companionActive = true;
        // A single pool has to read on its own behind a glass island (the main field is several
        // overlapping lobes), so it carries a touch more presence than any one ambient lobe.
        var steady = Math.Clamp(0.18 * Luminance, 0, 1);
        FadeCompanion(lobe.Border, 0.0, steady, 900);

        if (lobe.Visual is { } cv)
        {
            // A slow, shallow shimmer so the parked pool reads as quietly alive, never a pump.
            var sc = cv.Compositor.CreateStableVector3KeyFrameAnimation();
            sc.Target = "Scale";
            sc.InsertKeyFrame(0f, new Vector3(0.98f, 0.98f, 1f));
            sc.InsertKeyFrame(0.5f, new Vector3(1.03f, 1.03f, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
            sc.InsertKeyFrame(1f, new Vector3(0.98f, 0.98f, 1f), new Avalonia.Animation.Easings.SineEaseInOut());
            sc.Duration = TimeSpan.FromMilliseconds(7000);
            sc.IterationBehavior = AnimationIterationBehavior.Forever;
            cv.StartAnimation("Scale", sc);
        }

        return true;
    }

    /// <summary>
    /// Retracts the companion pool back into the chat and dissolves it — the receiving end of the
    /// split when an island closes (the two presences flow back into one).
    /// </summary>
    public void Merge()
    {
        if (!_companionActive)
            return;
        _companionActive = false;

        var lobe = Find(LobeRole.Companion);
        if (lobe is null)
            return;

        // Clear the anchor and let the spring (via ApplyFocus) glide the companion host back to the
        // field's bright centre as it fades. The fade is paced to the home glide so the pool is seen
        // travelling most of the way back into the field before it dissolves — a retract, not a blink.
        _companionNorm = null;
        ApplyFocus();

        var steady = Math.Clamp(0.18 * Luminance, 0, 1);
        FadeCompanion(lobe.Border, steady, 0.0, 1080);
    }

    private void FadeCompanion(Border border, double from, double to, double durationMs)
    {
        _companionCts?.Cancel();
        _companionCts = new System.Threading.CancellationTokenSource();
        var fade = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, Math.Clamp(from, 0, 1)) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1d),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, Math.Clamp(to, 0, 1)) },
                },
            },
        };
        _ = fade.RunAsync(border, _companionCts.Token);
    }

    private static (double dx, double dy) EdgeVector(PresenceEdge edge) => edge switch
    {
        PresenceEdge.Left => (-1d, 0d),
        PresenceEdge.Right => (1d, 0d),
        PresenceEdge.Top => (0d, -1d),
        _ => (0d, 1d),
    };

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Lobe? Find(LobeRole role)
    {
        foreach (var lobe in _lobes)
            if (lobe.Role == role)
                return lobe;
        return null;
    }

    /// <summary>(Re)builds a lobe's radial-gradient glow from its stored colour + alphas and the current
    /// directional light origin. The single brush builder, so a theme-colour change (resource observer)
    /// and a directional re-lean (<see cref="UpdateLightDirection"/>) both route through one place.</summary>
    private void ApplyGlowBrush(Lobe lobe)
        => lobe.Border.Background = BuildGlow(lobe.GlowColor, lobe.InnerAlpha, lobe.MidAlpha, _lightOriginX, _lightOriginY);

    /// <summary>Leans every lobe's bright core toward the edge the field is hugging (derived from the
    /// focus point), so the aura casts its light AWAY from that edge — a low pool glows upward, a high pool
    /// downward, and so on. Thresholded so it only rebuilds the brushes on a meaningful position shift,
    /// never on the micro focus-tracking that happens while an answer streams.</summary>
    private void UpdateLightDirection(Point focus)
    {
        var (ox, oy) = PresenceGeometry.LightOrigin(focus.X, focus.Y);
        if (Math.Abs(ox - _lightOriginX) + Math.Abs(oy - _lightOriginY) < 0.02)
            return;
        _lightOriginX = ox;
        _lightOriginY = oy;
        // Only the broad ambient field lobes carry the directional lean — they are the perceived aura.
        // Signal lobes (beacon/pulse/halo/companion) are tight, transient, focus-hugging accents whose
        // brushes are driven by their own pulse code (with their own colours/alphas), so re-leaning them
        // here would clobber an in-flight pulse. They stay symmetric, centred on what they illuminate.
        foreach (var lobe in _lobes)
            if (!lobe.Signal)
                ApplyGlowBrush(lobe);
    }

    private static RadialGradientBrush BuildGlow(
        Color c, byte innerAlpha, byte midAlpha, double originX = 0.5, double originY = 0.5)
    {
        // A smooth Gaussian-style falloff avoids the concentric banding a sparse 3-stop radial
        // reveals once the field is bright. The curve is pinned to the authored inner alpha at the
        // centre and the mid alpha at half-radius (so existing tuning is preserved), then tails
        // smoothly to fully transparent at the rim. The gradient ORIGIN (bright core) is offset toward
        // the edge the field hugs so the falloff stretches the other way — a directional luminance that
        // makes the pool read as light cast into the canvas rather than a symmetric blob.
        var inner = Math.Max((double)innerAlpha, 1);
        var mid = Math.Max((double)midAlpha, 1);
        var k = Math.Max(Math.Log(inner / mid) / 0.25, 0); // alpha(0.5) == mid
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(originX, originY, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
        };
        foreach (var r in new[] { 0.0, 0.12, 0.24, 0.36, 0.5, 0.64, 0.78, 0.9, 1.0 })
        {
            var a = r >= 1.0 ? 0.0 : inner * Math.Exp(-k * r * r);
            brush.GradientStops.Add(
                new GradientStop(Color.FromArgb((byte)Math.Clamp(a, 0, 255), c.R, c.G, c.B), r));
        }
        return brush;
    }

    private static IDisposable OnResource(StyledElement element, string key, Action<object?> handler)
        => element.GetResourceObservable(key).Subscribe(new FuncObserver(handler));

    private sealed class FuncObserver : IObserver<object?>
    {
        private readonly Action<object?> _handler;
        public FuncObserver(Action<object?> handler) => _handler = handler;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(object? value) => _handler(value);
    }
}

/// <summary>Ambient states for <see cref="StrataPresence"/>.</summary>
public enum PresenceState
{
    /// <summary>No active canvas (e.g. the welcome screen) — calm and at rest.</summary>
    Dormant,
    /// <summary>A canvas is open but idle.</summary>
    Idle,
    /// <summary>The assistant is working but not yet streaming output.</summary>
    Thinking,
    /// <summary>The assistant is actively streaming — the fullest, richest aurora.</summary>
    Streaming,
    /// <summary>The assistant needs the user's attention (e.g. a pending question).</summary>
    Attention,
}

/// <summary>One-shot effects for <see cref="StrataPresence.Pulse(PresencePulse)"/>.</summary>
public enum PresencePulse
{
    /// <summary>A gentle accent bloom when a fresh canvas awakens.</summary>
    Awaken,
    /// <summary>A satisfying success bloom when work completes.</summary>
    Bloom,
    /// <summary>A quick accent ping (e.g. a canvas split or an answered prompt).</summary>
    Ripple,
    /// <summary>A soft accent "arrival" swell when settling into an existing chat — the hand-off
    /// from the welcome mark down into a conversation.</summary>
    Settle,
    /// <summary>A brief alert flare when something needs attention (e.g. an error).</summary>
    Alert,
    /// <summary>Lumi produced a file/deliverable — a soft success-green breath.</summary>
    Create,
    /// <summary>Lumi changed a workspace file — a soft accent breath.</summary>
    Edit,
    /// <summary>Lumi referenced a web source — a cool, quiet blue breath.</summary>
    Browse,
}

/// <summary>An edge of the <see cref="StrataPresence"/> surface, used by the directional
/// travel gesture <see cref="StrataPresence.Emit(PresenceEdge)"/>.</summary>
public enum PresenceEdge
{
    Left,
    Right,
    Top,
    Bottom,
}
