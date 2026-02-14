using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Streaming surface for generated output.
/// Shows a live stream rail, generation status, and token-rate telemetry.
/// Built for real-time AI interactions.
/// </summary>
public class StrataStream : TemplatedControl
{
    private Border? _streamTrack;
    private Border? _streamBar;
    private Border? _statusArea;

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataStream, object?>(nameof(Header));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataStream, object?>(nameof(Content));

    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<StrataStream, bool>(nameof(IsStreaming), true);

    public static readonly StyledProperty<string> StageTextProperty =
        AvaloniaProperty.Register<StrataStream, string>(nameof(StageText), "Generating");

    public static readonly StyledProperty<double> TokensPerSecondProperty =
        AvaloniaProperty.Register<StrataStream, double>(nameof(TokensPerSecond), 28.4);

    public static readonly StyledProperty<bool> IsStatusToggleEnabledProperty =
        AvaloniaProperty.Register<StrataStream, bool>(nameof(IsStatusToggleEnabled), true);

    static StrataStream()
    {
        IsStreamingProperty.Changed.AddClassHandler<StrataStream>((control, _) => control.OnStreamingChanged());
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public bool IsStreaming
    {
        get => GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public string StageText
    {
        get => GetValue(StageTextProperty);
        set => SetValue(StageTextProperty, value);
    }

    public double TokensPerSecond
    {
        get => GetValue(TokensPerSecondProperty);
        set => SetValue(TokensPerSecondProperty, value);
    }

    public bool IsStatusToggleEnabled
    {
        get => GetValue(IsStatusToggleEnabledProperty);
        set => SetValue(IsStatusToggleEnabledProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _streamTrack = e.NameScope.Find<Border>("PART_StreamTrack");
        _streamBar = e.NameScope.Find<Border>("PART_StreamBar");
        _statusArea = e.NameScope.Find<Border>("PART_StatusArea");

        if (_streamTrack is not null)
            _streamTrack.SizeChanged += (_, _) =>
            {
                if (IsStreaming)
                    StartStreamAnimation();
            };

        if (_statusArea is not null)
            _statusArea.PointerPressed += (_, pointerEvent) =>
            {
                if (!IsStatusToggleEnabled || !pointerEvent.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    return;

                pointerEvent.Handled = true;
                IsStreaming = !IsStreaming;
            };

        Dispatcher.UIThread.Post(() =>
        {
            ApplyPseudoClasses();
            if (IsStreaming)
                StartStreamAnimation();
            else
                HideStreamBar();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsStatusToggleEnabled)
            return;

        if (e.Key is Key.S or Key.Space)
        {
            e.Handled = true;
            IsStreaming = !IsStreaming;
        }
    }

    private void OnStreamingChanged()
    {
        ApplyPseudoClasses();

        if (IsStreaming)
            StartStreamAnimation();
        else
            HideStreamBar();
    }

    private void ApplyPseudoClasses()
    {
        PseudoClasses.Set(":streaming", IsStreaming);
        PseudoClasses.Set(":ready", !IsStreaming);
    }

    private void HideStreamBar()
    {
        if (_streamBar is null)
            return;

        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null)
            return;

        visual.Opacity = 0;
    }

    private void StartStreamAnimation()
    {
        if (_streamBar is null || _streamTrack is null)
            return;

        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null)
            return;

        var trackWidth = _streamTrack.Bounds.Width;
        if (trackWidth < 10)
            trackWidth = Math.Max(220, Bounds.Width - 40);

        var barWidth = _streamBar.Bounds.Width;
        if (barWidth < 4)
            barWidth = 90;

        var comp = visual.Compositor;

        var offset = comp.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        offset.InsertKeyFrame(0f, new Vector3((float)-barWidth, 0f, 0f));
        offset.InsertKeyFrame(1f, new Vector3((float)trackWidth, 0f, 0f));
        offset.Duration = TimeSpan.FromMilliseconds(1100);
        offset.IterationBehavior = AnimationIterationBehavior.Forever;

        var opacity = comp.CreateScalarKeyFrameAnimation();
        opacity.Target = "Opacity";
        opacity.InsertKeyFrame(0f, 0.28f);
        opacity.InsertKeyFrame(0.35f, 0.9f);
        opacity.InsertKeyFrame(1f, 0.28f);
        opacity.Duration = TimeSpan.FromMilliseconds(1100);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation("Offset", offset);
        visual.StartAnimation("Opacity", opacity);
    }
}
