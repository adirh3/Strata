using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

/// <summary>
/// Displays a terminal-like preview card showing a running command and its output.
/// Used to visualize PowerShell/terminal tool executions in the chat transcript.
/// </summary>
/// <remarks>
/// <para><b>Template parts:</b> PART_Root, PART_Header, PART_StateDot, PART_OutputText.</para>
/// <para><b>Pseudo-classes:</b> :expanded, :inprogress, :completed, :failed, :running-bg, :has-output.</para>
/// </remarks>
public class StrataTerminalPreview : TemplatedControl
{
    private Border? _header;
    private Border? _stateDot;
    private Border? _root;
    private TextBlock? _outputText;
    private ScrollViewer? _outputScroll;
    private bool _wasRunning = true;
    private bool _isAttached;
    private readonly RunningElapsedClock _elapsedClock;

    public static readonly StyledProperty<string> CommandProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, string>(nameof(Command), "");

    public static readonly StyledProperty<string> OutputProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, string>(nameof(Output), "");

    public static readonly StyledProperty<StrataAiToolCallStatus> StatusProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, StrataAiToolCallStatus>(nameof(Status), StrataAiToolCallStatus.InProgress);

    /// <summary>
    /// True when the underlying tool call has finished (the launch returned) but the OS process it
    /// started is still running in the background — e.g. an <c>async</c> shell the agent left running
    /// after ending its turn. The card keeps its live "running" affordance (pulsing dot + elapsed
    /// clock, labelled "Running in background") instead of falsely collapsing to "Completed", so a
    /// long-lived background process never looks stuck or finished before it actually is.
    /// </summary>
    public static readonly StyledProperty<bool> IsRunningInBackgroundProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, bool>(nameof(IsRunningInBackground), false);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, bool>(
            nameof(IsExpanded), true, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, double>(nameof(DurationMs), 0);

    /// <summary>
    /// The instant the background process actually started (its authoritative start time). When set,
    /// the live "running in background" elapsed readout is computed from this fixed point rather than
    /// from when the control loaded, so the clock stays correct across control recreation (chat
    /// switch, list virtualization) and manual collapse instead of resetting to zero.
    /// </summary>
    public static readonly StyledProperty<DateTimeOffset?> RunningSinceProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, DateTimeOffset?>(nameof(RunningSince));

    public static readonly StyledProperty<string> ToolNameProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, string>(nameof(ToolName), "Terminal");

    public static readonly DirectProperty<StrataTerminalPreview, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataTerminalPreview, string>(nameof(StatusText), c => c.StatusText);

    /// <summary>Live "how long it has been running" readout, shown while the command is in
    /// progress (e.g. "8s", "1m 04s"). Empty until it has been running for at least a second, and
    /// cleared the moment it finishes.</summary>
    public static readonly DirectProperty<StrataTerminalPreview, string> ElapsedTextProperty =
        AvaloniaProperty.RegisterDirect<StrataTerminalPreview, string>(nameof(ElapsedText), c => c.ElapsedText);

    static StrataTerminalPreview()
    {
        StatusProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        IsRunningInBackgroundProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        OutputProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        DurationMsProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        RunningSinceProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.OnElapsedTick());
    }

    public StrataTerminalPreview()
    {
        _elapsedClock = new RunningElapsedClock(OnElapsedTick);
    }

    public string Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Output
    {
        get => GetValue(OutputProperty);
        set => SetValue(OutputProperty, value);
    }

    public StrataAiToolCallStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool IsRunningInBackground
    {
        get => GetValue(IsRunningInBackgroundProperty);
        set => SetValue(IsRunningInBackgroundProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public double DurationMs
    {
        get => GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    public DateTimeOffset? RunningSince
    {
        get => GetValue(RunningSinceProperty);
        set => SetValue(RunningSinceProperty, value);
    }

    public string ToolName
    {
        get => GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    public string StatusText => IsRunningInBackground && Status != StrataAiToolCallStatus.InProgress
        ? "Running in background"
        : Status switch
        {
            StrataAiToolCallStatus.InProgress => "Running",
            StrataAiToolCallStatus.Completed => "Completed",
            StrataAiToolCallStatus.Failed => "Failed",
            StrataAiToolCallStatus.Stopped => "Stopped",
            _ => ""
        };

    private string _elapsedText = "";

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetAndRaise(ElapsedTextProperty, ref _elapsedText, value);
    }

    private void OnElapsedTick()
    {
        // Prefer the authoritative start time when supplied: it's a fixed instant, so the readout is
        // immune to control recreation / restarts (chat switch, collapse) that would otherwise reset
        // a freshly-started local clock back to zero. Fall back to the local clock when unset.
        var elapsed = RunningSince is { } since
            ? DateTimeOffset.UtcNow - since
            : _elapsedClock.Elapsed;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        ElapsedText = elapsed.TotalSeconds >= 1 ? RunningElapsedClock.Format(elapsed) : "";
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_header is not null)
            _header.PointerPressed -= OnHeaderPointerPressed;

        base.OnApplyTemplate(e);

        _header = e.NameScope.Find<Border>("PART_Header");
        _stateDot = e.NameScope.Find<Border>("PART_StateDot");
        _root = e.NameScope.Find<Border>("PART_Root");
        _outputText = e.NameScope.Find<TextBlock>("PART_OutputText");
        _outputScroll = e.NameScope.Find<ScrollViewer>("PART_OutputScroll");

        if (_header is not null)
            _header.PointerPressed += OnHeaderPointerPressed;

        UpdateState();

        Dispatcher.UIThread.Post(() =>
        {
            if (Status == StrataAiToolCallStatus.InProgress || IsRunningInBackground)
                StartRunningPulse();
        }, DispatcherPriority.Loaded);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs pe)
    {
        if (pe.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            IsExpanded = !IsExpanded;
            pe.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        // Resume the live affordance when a recycled/virtualized container comes back while still
        // running (OnApplyTemplate's deferred start does not re-fire on an already-templated container).
        if (Status == StrataAiToolCallStatus.InProgress || IsRunningInBackground)
            StartRunningPulse();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopRunningPulse();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateState()
    {
        // "Running" is true while the tool call is in progress OR while the OS process it launched
        // is still alive in the background. This keeps the live affordance (pulse + elapsed clock)
        // going and defers the collapse-to-completed until the process has actually finished.
        var running = Status == StrataAiToolCallStatus.InProgress || IsRunningInBackground;

        if (_wasRunning && !running)
            IsExpanded = false;
        _wasRunning = running;

        RaisePropertyChanged(StatusTextProperty, default!, StatusText);

        PseudoClasses.Set(":inprogress", running);
        PseudoClasses.Set(":completed", !running && Status == StrataAiToolCallStatus.Completed);
        PseudoClasses.Set(":failed", !running && Status == StrataAiToolCallStatus.Failed);
        PseudoClasses.Set(":stopped", !running && Status == StrataAiToolCallStatus.Stopped);
        PseudoClasses.Set(":running-bg", IsRunningInBackground && Status != StrataAiToolCallStatus.InProgress);
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-output", !string.IsNullOrEmpty(Output));

        if (_outputText is not null)
            _outputText.Text = Output;

        if (running)
            StartRunningPulse();
        else
            StopRunningPulse();

        // Auto-scroll output to bottom when new content arrives
        if (_outputScroll is not null && !string.IsNullOrEmpty(Output))
        {
            Dispatcher.UIThread.Post(() =>
            {
                _outputScroll.ScrollToEnd();
            }, DispatcherPriority.Loaded);
        }
    }

    private void StartRunningPulse()
    {
        // Never arm the elapsed-clock timer on a control that is not in the visual tree. A running
        // DispatcherTimer is a GC root (its tick closure captures this control), so a detached control
        // whose pulse got started here — e.g. the OnApplyTemplate Loaded-priority post firing after the
        // container was already recycled during a fast chat switch — would be retained forever and keep
        // ticking every second. OnAttachedToVisualTree restarts it when the control comes back.
        if (!_isAttached)
            return;

        _elapsedClock.Start();
        OnElapsedTick();

        if (_stateDot is null) return;
        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null) return;

        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.35f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(920);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopRunningPulse()
    {
        _elapsedClock.Stop();
        ElapsedText = "";

        if (_stateDot is null) return;
        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null) return;

        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }
}
