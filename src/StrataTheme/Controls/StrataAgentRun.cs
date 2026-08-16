using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace StrataTheme.Controls;

/// <summary>
/// A delegated agent run rendered as one self-contained card: identity (avatar, role, model),
/// what it is doing right now, live status with an elapsed clock, optional progress, and a
/// persistent header action.
/// </summary>
/// <remarks>
/// <para>
/// Where <see cref="StrataAiToolCall"/> represents a single tool invocation, this represents a whole
/// sub-agent conversation. It deliberately shows only a summary — the agent's actual output belongs
/// in a dedicated transcript surface that the host opens from <see cref="HeaderAction"/>.
/// </para>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataAgentRun AgentName="Explore agent"
///                           Title="Inspect the transcript pipeline"
///                           Initial="E"
///                           ModelName="Claude Haiku 4.5"
///                           ModeLabel="Background"
///                           Activity="Running command · dotnet build"
///                           Status="InProgress" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_FocusRing (Border), PART_Stratum (Border),
/// PART_Header (Border), PART_Avatar (Border), PART_StateDot (Border), PART_Progress (ProgressBar).</para>
/// <para><b>Pseudo-classes:</b> :inprogress, :completed, :failed, :stopped, :has-activity,
/// :has-progress, :progress-unknown, :has-model, :has-mode, :has-byline.</para>
/// </remarks>
public class StrataAgentRun : TemplatedControl
{
    private readonly RunningElapsedClock _elapsedClock;
    private bool _isAttached;

    /// <summary>The agent's role or persona name (e.g. "Explore agent").</summary>
    public static readonly StyledProperty<string> AgentNameProperty =
        AvaloniaProperty.Register<StrataAgentRun, string>(nameof(AgentName), "Agent");

    /// <summary>What this run is doing — its task or current intent. The card's headline.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataAgentRun, string>(nameof(Title), "");

    /// <summary>Single glyph shown in the agent avatar.</summary>
    public static readonly StyledProperty<string> InitialProperty =
        AvaloniaProperty.Register<StrataAgentRun, string>(nameof(Initial), "\u2022");

    /// <summary>Avatar fill. Hosts give each parallel agent a distinct, stable colour.</summary>
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<StrataAgentRun, IBrush?>(nameof(AccentBrush));

    /// <summary>Model the agent is running on, shown in the byline.</summary>
    public static readonly StyledProperty<string?> ModelNameProperty =
        AvaloniaProperty.Register<StrataAgentRun, string?>(nameof(ModelName));

    /// <summary>Execution mode chip (e.g. "Background").</summary>
    public static readonly StyledProperty<string?> ModeLabelProperty =
        AvaloniaProperty.Register<StrataAgentRun, string?>(nameof(ModeLabel));

    /// <summary>The agent's newest step, shown as a live one-line readout while it works.</summary>
    public static readonly StyledProperty<string?> ActivityProperty =
        AvaloniaProperty.Register<StrataAgentRun, string?>(nameof(Activity));

    /// <summary>Run status. Drives the stratum line, status pill and state dot.</summary>
    public static readonly StyledProperty<StrataAiToolCallStatus> StatusProperty =
        AvaloniaProperty.Register<StrataAgentRun, StrataAiToolCallStatus>(
            nameof(Status), StrataAiToolCallStatus.InProgress);

    /// <summary>Overrides the status pill wording (e.g. localized "Working").</summary>
    public static readonly StyledProperty<string?> StatusLabelProperty =
        AvaloniaProperty.Register<StrataAgentRun, string?>(nameof(StatusLabel));

    /// <summary>Final duration in milliseconds, shown once the run reaches a terminal status.</summary>
    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<StrataAgentRun, double>(nameof(DurationMs), 0);

    /// <summary>
    /// The instant the run actually started. When set, the live elapsed readout is derived from this
    /// fixed point, so it stays correct across control recreation instead of restarting at zero.
    /// </summary>
    public static readonly StyledProperty<DateTimeOffset?> RunningSinceProperty =
        AvaloniaProperty.Register<StrataAgentRun, DateTimeOffset?>(nameof(RunningSince));

    /// <summary>Completion percentage (0-100). Negative means "running, amount unknown".</summary>
    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<StrataAgentRun, double>(nameof(ProgressValue), -1);

    /// <summary>Persistent header content, typically the action that opens the full run.</summary>
    public static readonly StyledProperty<object?> HeaderActionProperty =
        AvaloniaProperty.Register<StrataAgentRun, object?>(nameof(HeaderAction));

    public static readonly DirectProperty<StrataAgentRun, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAgentRun, string>(nameof(StatusText), control => control.StatusText);

    public static readonly DirectProperty<StrataAgentRun, string> BylineProperty =
        AvaloniaProperty.RegisterDirect<StrataAgentRun, string>(nameof(Byline), control => control.Byline);

    /// <summary>Live elapsed readout while running, or the frozen duration once finished.</summary>
    public static readonly DirectProperty<StrataAgentRun, string> TimingTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAgentRun, string>(nameof(TimingText), control => control.TimingText);

    static StrataAgentRun()
    {
        StatusProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());
        StatusLabelProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());

        ActivityProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());

        ProgressValueProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());
        ModelNameProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());
        ModeLabelProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());
        AgentNameProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.UpdateState());
        DurationMsProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.OnElapsedTick());
        RunningSinceProperty.Changed.AddClassHandler<StrataAgentRun>((control, _) => control.OnElapsedTick());
    }

    public StrataAgentRun()
    {
        _elapsedClock = new RunningElapsedClock(OnElapsedTick);
    }

    public string AgentName
    {
        get => GetValue(AgentNameProperty);
        set => SetValue(AgentNameProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Initial
    {
        get => GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public string? ModelName
    {
        get => GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value);
    }

    public string? ModeLabel
    {
        get => GetValue(ModeLabelProperty);
        set => SetValue(ModeLabelProperty, value);
    }

    public string? Activity
    {
        get => GetValue(ActivityProperty);
        set => SetValue(ActivityProperty, value);
    }

    public StrataAiToolCallStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? StatusLabel
    {
        get => GetValue(StatusLabelProperty);
        set => SetValue(StatusLabelProperty, value);
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

    public double ProgressValue
    {
        get => GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public object? HeaderAction
    {
        get => GetValue(HeaderActionProperty);
        set => SetValue(HeaderActionProperty, value);
    }

    public string StatusText => string.IsNullOrWhiteSpace(StatusLabel)
        ? Status switch
        {
            StrataAiToolCallStatus.InProgress => "Working",
            StrataAiToolCallStatus.Completed => "Done",
            StrataAiToolCallStatus.Failed => "Failed",
            StrataAiToolCallStatus.Stopped => "Stopped",
            _ => "Unknown"
        }
        : StatusLabel!;

    /// <summary>"Role · Model · Mode" identity line under the run's headline.</summary>
    public string Byline
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (!string.IsNullOrWhiteSpace(AgentName))
                parts.Add(AgentName);
            if (!string.IsNullOrWhiteSpace(ModelName))
                parts.Add(ModelName!);
            if (!string.IsNullOrWhiteSpace(ModeLabel))
                parts.Add(ModeLabel!);
            return string.Join(" \u00b7 ", parts);
        }
    }

    private string _timingText = "";

    public string TimingText
    {
        get => _timingText;
        private set => SetAndRaise(TimingTextProperty, ref _timingText, value);
    }

    private void OnElapsedTick()
    {
        if (Status != StrataAiToolCallStatus.InProgress)
        {
            TimingText = DurationMs <= 0
                ? ""
                : DurationMs >= 1000
                    ? $"{DurationMs / 1000d:F1}s"
                    : $"{DurationMs:F0} ms";
            return;
        }

        var elapsed = RunningSince is { } since
            ? DateTimeOffset.UtcNow - since
            : _elapsedClock.Elapsed;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        TimingText = elapsed.TotalSeconds >= 1 ? RunningElapsedClock.Format(elapsed) : "";
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        UpdateState();

        Dispatcher.UIThread.Post(() =>
        {
            if (Status == StrataAiToolCallStatus.InProgress)
                StartRunningActivity();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        if (Status == StrataAiToolCallStatus.InProgress)
            StartRunningActivity();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopRunningActivity();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateState()
    {
        RaisePropertyChanged(StatusTextProperty, default!, StatusText);
        RaisePropertyChanged(BylineProperty, default!, Byline);

        var isRunning = Status == StrataAiToolCallStatus.InProgress;
        PseudoClasses.Set(":inprogress", isRunning);
        PseudoClasses.Set(":completed", Status == StrataAiToolCallStatus.Completed);
        PseudoClasses.Set(":failed", Status == StrataAiToolCallStatus.Failed);
        PseudoClasses.Set(":stopped", Status == StrataAiToolCallStatus.Stopped);

        // The activity line is a live readout: it only earns its space while the agent is working.
        PseudoClasses.Set(":has-activity", isRunning && !string.IsNullOrWhiteSpace(Activity));
        PseudoClasses.Set(":has-progress", isRunning);
        // Running with no measurable progress (no todo plan yet) reads as an indeterminate rail
        // rather than a bar stuck at zero.
        PseudoClasses.Set(":progress-unknown", isRunning && ProgressValue < 0);
        PseudoClasses.Set(":has-model", !string.IsNullOrWhiteSpace(ModelName));
        PseudoClasses.Set(":has-mode", !string.IsNullOrWhiteSpace(ModeLabel));
        PseudoClasses.Set(":has-byline", !string.IsNullOrWhiteSpace(Byline));

        OnElapsedTick();

        if (isRunning)
            StartRunningActivity();
        else
            StopRunningActivity();
    }

    private void StartRunningActivity()
    {
        // A running DispatcherTimer roots this control through its tick closure, so it is never armed
        // while detached — otherwise a recycled card leaks and keeps ticking forever.
        if (!_isAttached)
            return;

        _elapsedClock.Start();
        OnElapsedTick();
    }

    private void StopRunningActivity()
    {
        _elapsedClock.Stop();
        OnElapsedTick();
    }
}
