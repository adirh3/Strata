using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using System;

namespace StrataTheme.Controls;

public enum StrataAiToolCallStatus
{
    InProgress,
    Completed,
    Failed
}

public class StrataAiToolCall : TemplatedControl
{
    private Border? _header;
    private Border? _stateDot;

    public static readonly StyledProperty<string> ToolNameProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string>(nameof(ToolName), "tool.call");

    public static readonly StyledProperty<string?> InputParametersProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(InputParameters));

    public static readonly StyledProperty<string?> MoreInfoProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(MoreInfo));

    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<StrataAiToolCall, double>(nameof(DurationMs), 0);

    public static readonly StyledProperty<StrataAiToolCallStatus> StatusProperty =
        AvaloniaProperty.Register<StrataAiToolCall, StrataAiToolCallStatus>(nameof(Status), StrataAiToolCallStatus.InProgress);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataAiToolCall, bool>(nameof(IsExpanded), false);

    public static readonly DirectProperty<StrataAiToolCall, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(StatusText), control => control.StatusText);

    public static readonly DirectProperty<StrataAiToolCall, string> DurationTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(DurationText), control => control.DurationText);

    static StrataAiToolCall()
    {
        StatusProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        InputParametersProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        MoreInfoProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        DurationMsProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
    }

    public string ToolName
    {
        get => GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    public string? InputParameters
    {
        get => GetValue(InputParametersProperty);
        set => SetValue(InputParametersProperty, value);
    }

    public string? MoreInfo
    {
        get => GetValue(MoreInfoProperty);
        set => SetValue(MoreInfoProperty, value);
    }

    public double DurationMs
    {
        get => GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
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

    public string StatusText => Status switch
    {
        StrataAiToolCallStatus.InProgress => "In progress",
        StrataAiToolCallStatus.Completed => "Completed",
        StrataAiToolCallStatus.Failed => "Failed",
        _ => "Unknown"
    };

    public string DurationText => DurationMs >= 1000
        ? $"{DurationMs / 1000d:F2}s"
        : $"{DurationMs:F0} ms";

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _header = e.NameScope.Find<Border>("PART_Header");
        _stateDot = e.NameScope.Find<Border>("PART_StateDot");

        if (_header is not null)
        {
            _header.PointerPressed += (_, pointerEvent) =>
            {
                if (pointerEvent.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    IsExpanded = !IsExpanded;
                    pointerEvent.Handled = true;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Enter or Key.Space)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsExpanded)
        {
            IsExpanded = false;
            e.Handled = true;
        }
    }

    private void UpdateState()
    {
        RaisePropertyChanged(StatusTextProperty, default!, StatusText);
        RaisePropertyChanged(DurationTextProperty, default!, DurationText);

        PseudoClasses.Set(":inprogress", Status == StrataAiToolCallStatus.InProgress);
        PseudoClasses.Set(":completed", Status == StrataAiToolCallStatus.Completed);
        PseudoClasses.Set(":failed", Status == StrataAiToolCallStatus.Failed);

        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-params", !string.IsNullOrWhiteSpace(InputParameters));
        PseudoClasses.Set(":has-info", !string.IsNullOrWhiteSpace(MoreInfo));

        if (Status == StrataAiToolCallStatus.InProgress)
            StartRunningPulse();
        else
            StopRunningPulse();
    }

    private void StartRunningPulse()
    {
        if (_stateDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null)
            return;

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
        if (_stateDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null)
            return;

        var reset = visual.Compositor.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }
}
