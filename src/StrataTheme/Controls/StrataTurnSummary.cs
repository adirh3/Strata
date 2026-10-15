using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// A lightweight, borderless disclosure that collapses completed AI turn blocks
/// (tool-call groups and reasoning traces) into a single summary line.
/// Collapsed: a thin inline row with status dot and label.
/// Expanded: the original blocks beneath a subtle left accent bar. A reader's own toggle reveals them
/// from the top while they fade in, and folds them away the same way.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataTurnSummary Label="Reasoned · 4 actions"&gt;
///     &lt;controls:StrataTurnSummary.Content&gt;
///         &lt;StackPanel&gt;
///             &lt;controls:StrataAiToolCall … /&gt;
///             &lt;controls:StrataAiToolCall … /&gt;
///         &lt;/StackPanel&gt;
///     &lt;/controls:StrataTurnSummary.Content&gt;
/// &lt;/controls:StrataTurnSummary&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Header (Border), PART_ContentHost (TurnSummaryRevealHost).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :has-failures.</para>
/// <para><b>Classes:</b> <c>motion-disabled</c> opens and closes at once.</para>
/// <para><b>Folded content</b> reads <see cref="IsWithinSummaryProperty"/> as true: a
/// <see cref="StrataChatMessage"/> progress update folded here reads in the quieter voice of finished work.</para>
/// </remarks>
public class StrataTurnSummary : TemplatedControl
{
    private const double OpenDurationMs = 220;
    private const double CloseDurationMs = 170;
    private static readonly Easing MotionEasing = new CubicEaseOut();

    private TapReleaseHandler? _tapHandler;
    private object? _displayedContent;
    private TurnSummaryRevealHost? _contentHost;
    private bool _isReaderToggle;
    private bool _isMoving;
    private int _motionVersion;
    private long _motionStart;
    private double _motionFrom;
    private double _motionTo;
    private double _motionDurationMs;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataTurnSummary, string>(nameof(Label), "Finished");

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataTurnSummary, object?>(nameof(Content));

    public static readonly DirectProperty<StrataTurnSummary, object?> DisplayedContentProperty =
        AvaloniaProperty.RegisterDirect<StrataTurnSummary, object?>(nameof(DisplayedContent), control => control.DisplayedContent);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataTurnSummary, bool>(
            nameof(IsExpanded), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> HasFailuresProperty =
        AvaloniaProperty.Register<StrataTurnSummary, bool>(nameof(HasFailures));

    /// <summary>
    /// True for everything a summary folds. It is inherited, so content reads it however and whenever it
    /// attaches: a <see cref="StrataChatMessage"/> progress update folded into a summary reads in the
    /// quieter voice of finished work, while an update outside one keeps the answer's voice.
    /// </summary>
    public static readonly AttachedProperty<bool> IsWithinSummaryProperty =
        AvaloniaProperty.RegisterAttached<StrataTurnSummary, Control, bool>("IsWithinSummary", inherits: true);

    public static bool GetIsWithinSummary(Control control) => control.GetValue(IsWithinSummaryProperty);

    public static void SetIsWithinSummary(Control control, bool value) => control.SetValue(IsWithinSummaryProperty, value);

    static StrataTurnSummary()
    {
        IsExpandedProperty.Changed.AddClassHandler<StrataTurnSummary>((c, _) =>
        {
            c.UpdatePseudoClasses();
            c.ApplyExpandedState(animate: c._isReaderToggle);
        });
        HasFailuresProperty.Changed.AddClassHandler<StrataTurnSummary>((c, _) => c.UpdatePseudoClasses());
        ContentProperty.Changed.AddClassHandler<StrataTurnSummary>((c, _) =>
        {
            if (c.IsExpanded)
                c.DisplayedContent = c.Content;
        });
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public object? DisplayedContent
    {
        get => _displayedContent;
        private set => SetAndRaise(DisplayedContentProperty, ref _displayedContent, value);
    }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool HasFailures { get => GetValue(HasFailuresProperty); set => SetValue(HasFailuresProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _tapHandler?.Dispose();
        _tapHandler = null;
        StopMotion();

        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Header") is { } header)
            _tapHandler = new TapReleaseHandler(header, ToggleByReader);

        _contentHost = e.NameScope.Find<TurnSummaryRevealHost>("PART_ContentHost");
        ApplyExpandedState(animate: false);
        UpdatePseudoClasses();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // A row leaving the transcript mid-motion settles at once, so it never returns half open.
        if (_isMoving)
            ApplyExpandedState(animate: false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            ToggleByReader();
            e.Handled = true;
        }
    }

    /// <summary>
    /// A reader's toggle animates and keeps the row under the pointer: the chat stops following its
    /// tail, so the opening or closing content moves what lies below the row instead of the row itself.
    /// Other changes, such as opening the row to show a search hit, apply at once so that positions
    /// inside it are already final.
    /// </summary>
    private void ToggleByReader()
    {
        this.FindAncestorOfType<StrataChatShell>()?.PreserveViewport();
        _isReaderToggle = true;
        try
        {
            SetCurrentValue(IsExpandedProperty, !IsExpanded);
        }
        finally
        {
            _isReaderToggle = false;
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-failures", HasFailures);
    }

    /// <summary>
    /// Opens or closes the content. An animated change reveals the content from the top while it fades
    /// in, or folds it away the same way, so the transcript below glides rather than jumps. Interrupting
    /// a change continues from wherever the content is.
    /// </summary>
    private void ApplyExpandedState(bool animate)
    {
        StopMotion();
        if (_contentHost is not { } host)
        {
            DisplayedContent = IsExpanded ? Content : null;
            return;
        }

        var target = IsExpanded ? 1d : 0d;
        if (IsExpanded)
        {
            DisplayedContent = Content;
            host.IsVisible = true;
        }

        if (!animate
            || Math.Abs(host.Reveal - target) < 0.001
            || Bounds.Width <= 0
            || !IsEffectivelyVisible
            || Classes.Contains("motion-disabled")
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            Settle(host);
            return;
        }

        _motionFrom = host.Reveal;
        _motionTo = target;
        _motionDurationMs = (IsExpanded ? OpenDurationMs : CloseDurationMs) * Math.Max(0.4, Math.Abs(target - _motionFrom));
        _motionStart = 0;
        _isMoving = true;
        var version = _motionVersion;
        topLevel.RequestAnimationFrame(_ => OnMotionFrame(topLevel, version));
    }

    // Each step lands in the frame being composed, and the last one settles the row in that same frame.
    private void OnMotionFrame(TopLevel topLevel, int version)
    {
        if (version != _motionVersion || _contentHost is not { } host)
            return;

        // The clock starts once the content has been laid out. A first layout that takes a while, such
        // as building the steps in view, then delays the motion instead of using it up.
        if (_motionStart == 0)
        {
            if (host.Child is { IsMeasureValid: true })
                _motionStart = Stopwatch.GetTimestamp();
            topLevel.RequestAnimationFrame(_ => OnMotionFrame(topLevel, version));
            return;
        }

        var t = Math.Clamp(Stopwatch.GetElapsedTime(_motionStart).TotalMilliseconds / _motionDurationMs, 0, 1);
        if (t >= 1)
        {
            Settle(host);
            return;
        }

        SetReveal(host, _motionFrom + (_motionTo - _motionFrom) * MotionEasing.Ease(t));
        topLevel.RequestAnimationFrame(_ => OnMotionFrame(topLevel, version));
    }

    private void Settle(TurnSummaryRevealHost host)
    {
        _isMoving = false;
        if (IsExpanded)
        {
            SetReveal(host, 1);
            return;
        }

        host.IsVisible = false;
        SetReveal(host, 0);
        DisplayedContent = null;
    }

    private static void SetReveal(TurnSummaryRevealHost host, double reveal)
    {
        reveal = Math.Clamp(reveal, 0, 1);
        host.Reveal = reveal;
        host.Opacity = reveal;
    }

    private void StopMotion()
    {
        _motionVersion++;
        _isMoving = false;
    }
}
