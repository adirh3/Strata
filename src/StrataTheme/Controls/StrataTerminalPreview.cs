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
/// <para><b>Pseudo-classes:</b> :expanded, :inprogress, :completed, :failed, :has-output.</para>
/// </remarks>
public class StrataTerminalPreview : TemplatedControl
{
    private Border? _header;
    private Border? _stateDot;
    private Border? _root;
    private TextBlock? _outputText;
    private ScrollViewer? _outputScroll;
    private StrataAiToolCallStatus _lastStatus = StrataAiToolCallStatus.InProgress;

    public static readonly StyledProperty<string> CommandProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, string>(nameof(Command), "");

    public static readonly StyledProperty<string> OutputProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, string>(nameof(Output), "");

    public static readonly StyledProperty<StrataAiToolCallStatus> StatusProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, StrataAiToolCallStatus>(nameof(Status), StrataAiToolCallStatus.InProgress);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, bool>(nameof(IsExpanded), true);

    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, double>(nameof(DurationMs), 0);

    public static readonly StyledProperty<string> ToolNameProperty =
        AvaloniaProperty.Register<StrataTerminalPreview, string>(nameof(ToolName), "Terminal");

    public static readonly DirectProperty<StrataTerminalPreview, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataTerminalPreview, string>(nameof(StatusText), c => c.StatusText);

    static StrataTerminalPreview()
    {
        StatusProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        OutputProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
        DurationMsProperty.Changed.AddClassHandler<StrataTerminalPreview>((c, _) => c.UpdateState());
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

    public string ToolName
    {
        get => GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    public string StatusText => Status switch
    {
        StrataAiToolCallStatus.InProgress => "Running",
        StrataAiToolCallStatus.Completed when DurationMs > 0 => DurationMs >= 1000
            ? $"{DurationMs / 1000d:F1}s"
            : $"{DurationMs:F0}ms",
        StrataAiToolCallStatus.Completed => "Completed",
        StrataAiToolCallStatus.Failed => "Failed",
        _ => ""
    };

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _header = e.NameScope.Find<Border>("PART_Header");
        _stateDot = e.NameScope.Find<Border>("PART_StateDot");
        _root = e.NameScope.Find<Border>("PART_Root");
        _outputText = e.NameScope.Find<TextBlock>("PART_OutputText");
        _outputScroll = e.NameScope.Find<ScrollViewer>("PART_OutputScroll");

        if (_header is not null)
        {
            _header.PointerPressed += (_, pe) =>
            {
                if (pe.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    IsExpanded = !IsExpanded;
                    pe.Handled = true;
                }
            };
        }

        UpdateState();

        Dispatcher.UIThread.Post(() =>
        {
            if (Status == StrataAiToolCallStatus.InProgress)
                StartRunningPulse();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopRunningPulse();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateState()
    {
        if (_lastStatus == StrataAiToolCallStatus.InProgress
            && Status != StrataAiToolCallStatus.InProgress)
        {
            IsExpanded = false;
        }
        _lastStatus = Status;

        RaisePropertyChanged(StatusTextProperty, default!, StatusText);

        PseudoClasses.Set(":inprogress", Status == StrataAiToolCallStatus.InProgress);
        PseudoClasses.Set(":completed", Status == StrataAiToolCallStatus.Completed);
        PseudoClasses.Set(":failed", Status == StrataAiToolCallStatus.Failed);
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-output", !string.IsNullOrEmpty(Output));

        if (_outputText is not null)
            _outputText.Text = Output;

        if (Status == StrataAiToolCallStatus.InProgress)
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
        if (_stateDot is null) return;
        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null) return;

        var reset = visual.Compositor.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }
}
