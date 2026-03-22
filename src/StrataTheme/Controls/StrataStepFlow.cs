using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Interactive generation pipeline control.
/// Users can click stages to inspect progress and output per step.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataStepFlow CurrentStep="1"
///                           Step0Title="Draft" Step1Title="Review"
///                           Step2Title="Refine" Step3Title="Done"&gt;
///     &lt;controls:StrataStepFlow.Step0Content&gt;&lt;TextBlock Text="..." /&gt;&lt;/controls:StrataStepFlow.Step0Content&gt;
/// &lt;/controls:StrataStepFlow&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Track (Border), PART_Fill (Border), PART_Head (Border),
/// PART_Content0–PART_Content4 (ContentPresenter), PART_Step0–PART_Step4 (Button).</para>
/// <para><b>Pseudo-classes:</b> :s0, :s1, :s2, :s3, :s4.</para>
/// </remarks>
public class StrataStepFlow : TemplatedControl
{
    private const int MaxSteps = 6;

    private Border? _track;
    private Border? _fill;
    private Border? _head;
    private Button? _stepBtn0;
    private Button? _stepBtn1;
    private Button? _stepBtn2;
    private Button? _stepBtn3;
    private Button? _stepBtn4;
    private Button? _stepBtn5;
    private ContentPresenter? _content0;
    private ContentPresenter? _content1;
    private ContentPresenter? _content2;
    private ContentPresenter? _content3;
    private ContentPresenter? _content4;
    private ContentPresenter? _content5;
    private CancellationTokenSource? _transitionCts;

    public static readonly StyledProperty<int> CurrentStepProperty =
        AvaloniaProperty.Register<StrataStepFlow, int>(nameof(CurrentStep), 0);

    /// <summary>When true, users can only click steps ≤ CurrentStep (no skipping ahead).</summary>
    public static readonly StyledProperty<bool> DisableFutureStepsProperty =
        AvaloniaProperty.Register<StrataStepFlow, bool>(nameof(DisableFutureSteps), false);

    public static readonly StyledProperty<string> Step0TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step0Title), "Draft");

    public static readonly StyledProperty<string> Step1TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step1Title), "Grounding");

    public static readonly StyledProperty<string> Step2TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step2Title), "Reasoning");

    public static readonly StyledProperty<string> Step3TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step3Title), "Finalize");

    public static readonly StyledProperty<string> Step4TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step4Title), "Step 5");

    public static readonly StyledProperty<string> Step5TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step5Title), "Step 6");

    public static readonly StyledProperty<object?> Step0ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step0Content));

    public static readonly StyledProperty<object?> Step1ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step1Content));

    public static readonly StyledProperty<object?> Step2ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step2Content));

    public static readonly StyledProperty<object?> Step3ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step3Content));

    public static readonly StyledProperty<object?> Step4ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step4Content));

    public static readonly StyledProperty<object?> Step5ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step5Content));

    /// <summary>
    /// How many steps to display (1–6). Default is 5. Set to 6 to enable Step5.
    /// </summary>
    public static readonly StyledProperty<int> StepCountProperty =
        AvaloniaProperty.Register<StrataStepFlow, int>(nameof(StepCount), 5);

    static StrataStepFlow()
    {
        CurrentStepProperty.Changed.AddClassHandler<StrataStepFlow>((flow, _) => flow.OnCurrentStepChanged());
        StepCountProperty.Changed.AddClassHandler<StrataStepFlow>((flow, _) => flow.OnStepCountChanged());
    }

    public int CurrentStep
    {
        get => GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, Math.Clamp(value, 0, StepCount - 1));
    }

    public bool DisableFutureSteps
    {
        get => GetValue(DisableFutureStepsProperty);
        set => SetValue(DisableFutureStepsProperty, value);
    }

    public string Step0Title
    {
        get => GetValue(Step0TitleProperty);
        set => SetValue(Step0TitleProperty, value);
    }

    public string Step1Title
    {
        get => GetValue(Step1TitleProperty);
        set => SetValue(Step1TitleProperty, value);
    }

    public string Step2Title
    {
        get => GetValue(Step2TitleProperty);
        set => SetValue(Step2TitleProperty, value);
    }

    public string Step3Title
    {
        get => GetValue(Step3TitleProperty);
        set => SetValue(Step3TitleProperty, value);
    }

    public string Step4Title
    {
        get => GetValue(Step4TitleProperty);
        set => SetValue(Step4TitleProperty, value);
    }

    public string Step5Title
    {
        get => GetValue(Step5TitleProperty);
        set => SetValue(Step5TitleProperty, value);
    }

    public int StepCount
    {
        get => GetValue(StepCountProperty);
        set => SetValue(StepCountProperty, Math.Clamp(value, 1, MaxSteps));
    }

    public object? Step0Content
    {
        get => GetValue(Step0ContentProperty);
        set => SetValue(Step0ContentProperty, value);
    }

    public object? Step1Content
    {
        get => GetValue(Step1ContentProperty);
        set => SetValue(Step1ContentProperty, value);
    }

    public object? Step2Content
    {
        get => GetValue(Step2ContentProperty);
        set => SetValue(Step2ContentProperty, value);
    }

    public object? Step3Content
    {
        get => GetValue(Step3ContentProperty);
        set => SetValue(Step3ContentProperty, value);
    }

    public object? Step4Content
    {
        get => GetValue(Step4ContentProperty);
        set => SetValue(Step4ContentProperty, value);
    }

    public object? Step5Content
    {
        get => GetValue(Step5ContentProperty);
        set => SetValue(Step5ContentProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        // Unsubscribe old handlers
        if (_stepBtn0 is not null) _stepBtn0.Click -= OnStep0Click;
        if (_stepBtn1 is not null) _stepBtn1.Click -= OnStep1Click;
        if (_stepBtn2 is not null) _stepBtn2.Click -= OnStep2Click;
        if (_stepBtn3 is not null) _stepBtn3.Click -= OnStep3Click;
        if (_stepBtn4 is not null) _stepBtn4.Click -= OnStep4Click;
        if (_stepBtn5 is not null) _stepBtn5.Click -= OnStep5Click;
        if (_track is not null) _track.SizeChanged -= OnTrackSizeChanged;

        base.OnApplyTemplate(e);

        _track = e.NameScope.Find<Border>("PART_Track");
        _fill = e.NameScope.Find<Border>("PART_Fill");
        _head = e.NameScope.Find<Border>("PART_Head");

        _content0 = e.NameScope.Find<ContentPresenter>("PART_Content0");
        _content1 = e.NameScope.Find<ContentPresenter>("PART_Content1");
        _content2 = e.NameScope.Find<ContentPresenter>("PART_Content2");
        _content3 = e.NameScope.Find<ContentPresenter>("PART_Content3");
        _content4 = e.NameScope.Find<ContentPresenter>("PART_Content4");
        _content5 = e.NameScope.Find<ContentPresenter>("PART_Content5");

        _stepBtn0 = e.NameScope.Find<Button>("PART_Step0");
        _stepBtn1 = e.NameScope.Find<Button>("PART_Step1");
        _stepBtn2 = e.NameScope.Find<Button>("PART_Step2");
        _stepBtn3 = e.NameScope.Find<Button>("PART_Step3");
        _stepBtn4 = e.NameScope.Find<Button>("PART_Step4");
        _stepBtn5 = e.NameScope.Find<Button>("PART_Step5");

        if (_stepBtn0 is not null) _stepBtn0.Click += OnStep0Click;
        if (_stepBtn1 is not null) _stepBtn1.Click += OnStep1Click;
        if (_stepBtn2 is not null) _stepBtn2.Click += OnStep2Click;
        if (_stepBtn3 is not null) _stepBtn3.Click += OnStep3Click;
        if (_stepBtn4 is not null) _stepBtn4.Click += OnStep4Click;
        if (_stepBtn5 is not null) _stepBtn5.Click += OnStep5Click;

        if (_track is not null)
            _track.SizeChanged += OnTrackSizeChanged;

        Dispatcher.UIThread.Post(() =>
        {
            UpdateStepButtonVisibility();
            UpdatePseudoClasses();
            UpdateFlowProgress(animateHead: false);
            ApplyContentInstant();
        }, DispatcherPriority.Loaded);
    }

    private void GoToStep(int step)
    {
        if (DisableFutureSteps && step > CurrentStep) return;
        CurrentStep = step;
    }

    private void OnStep0Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => GoToStep(0);
    private void OnStep1Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => GoToStep(1);
    private void OnStep2Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => GoToStep(2);
    private void OnStep3Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => GoToStep(3);
    private void OnStep4Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => GoToStep(4);
    private void OnStep5Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => GoToStep(5);
    private void OnTrackSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateFlowProgress(animateHead: false);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _transitionCts?.Cancel();
        _transitionCts?.Dispose();
        _transitionCts = null;

        if (_track is not null)
            _track.SizeChanged -= OnTrackSizeChanged;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                CurrentStep = Math.Max(0, CurrentStep - 1);
                break;
            case Key.Right:
                e.Handled = true;
                CurrentStep = Math.Min(StepCount - 1, CurrentStep + 1);
                break;
            case Key.Home:
                e.Handled = true;
                CurrentStep = 0;
                break;
            case Key.End:
                e.Handled = true;
                CurrentStep = StepCount - 1;
                break;
        }
    }

    private void OnCurrentStepChanged()
    {
        UpdatePseudoClasses();
        UpdateFlowProgress(animateHead: true);
        _ = AnimateContentTransitionAsync();
    }

    private void OnStepCountChanged()
    {
        UpdateStepButtonVisibility();
        UpdateFlowProgress(animateHead: false);
    }

    private void UpdateStepButtonVisibility()
    {
        var count = StepCount;
        Button?[] buttons = [_stepBtn0, _stepBtn1, _stepBtn2, _stepBtn3, _stepBtn4, _stepBtn5];
        for (var i = 0; i < MaxSteps; i++)
        {
            if (buttons[i] is { } btn)
                btn.IsVisible = i < count;
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":s0", CurrentStep == 0);
        PseudoClasses.Set(":s1", CurrentStep == 1);
        PseudoClasses.Set(":s2", CurrentStep == 2);
        PseudoClasses.Set(":s3", CurrentStep == 3);
        PseudoClasses.Set(":s4", CurrentStep == 4);
        PseudoClasses.Set(":s5", CurrentStep == 5);
    }

    private void UpdateFlowProgress(bool animateHead)
    {
        if (_track is null || _fill is null)
            return;

        var trackWidth = _track.Bounds.Width;
        if (trackWidth < 1)
            return;

        var pct = (CurrentStep + 1) / (double)StepCount;
        var fillWidth = Math.Max(0, trackWidth * pct);
        _fill.Width = fillWidth;

        if (_head is null)
            return;

        var visual = ElementComposition.GetElementVisual(_head);
        if (visual is null)
            return;

        var headWidth = _head.Bounds.Width > 0 ? _head.Bounds.Width : 10;
        var targetX = Math.Max(0, fillWidth - headWidth / 2);

        if (!animateHead)
        {
            var current = visual.Offset;
            visual.Offset = new Avalonia.Vector3D(targetX, current.Y, current.Z);
            return;
        }

        var comp = visual.Compositor;
        var from = visual.Offset;

        var offsetAnim = comp.CreateVector3KeyFrameAnimation();
        offsetAnim.Target = "Offset";
        offsetAnim.InsertKeyFrame(0f, new Vector3((float)from.X, (float)from.Y, (float)from.Z));
        offsetAnim.InsertKeyFrame(1f, new Vector3((float)targetX, (float)from.Y, (float)from.Z));
        offsetAnim.Duration = TimeSpan.FromMilliseconds(260);
        visual.StartAnimation("Offset", offsetAnim);

        visual.CenterPoint = new Avalonia.Vector3D(headWidth / 2, headWidth / 2, 0);
        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scaleAnim.InsertKeyFrame(0.45f, new Vector3(1.22f, 1.22f, 1f));
        scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(260);
        visual.StartAnimation("Scale", scaleAnim);
    }

    private ContentPresenter? GetPresenter(int idx) => idx switch
    {
        0 => _content0,
        1 => _content1,
        2 => _content2,
        3 => _content3,
        4 => _content4,
        5 => _content5,
        _ => null
    };

    private void ApplyContentInstant()
    {
        for (var i = 0; i < MaxSteps; i++)
        {
            var p = GetPresenter(i);
            if (p is null) continue;
            var visible = i == CurrentStep;
            p.IsVisible = visible;
            p.Opacity = 1;
        }
    }

    private async Task AnimateContentTransitionAsync()
    {
        _transitionCts?.Cancel();
        _transitionCts?.Dispose();
        _transitionCts = new CancellationTokenSource();
        var token = _transitionCts.Token;

        var incoming = GetPresenter(CurrentStep);
        if (incoming is null)
            return;

        ContentPresenter? outgoing = null;
        for (var i = 0; i < MaxSteps; i++)
        {
            var p = GetPresenter(i);
            if (p is null || p == incoming) continue;
            if (p.IsVisible)
            {
                outgoing = p;
                break;
            }
        }

        incoming.IsVisible = true;
        incoming.Opacity = 1;

        var inVisual = ElementComposition.GetElementVisual(incoming);
        if (inVisual is not null)
        {
            var comp = inVisual.Compositor;
            var fadeIn = comp.CreateScalarKeyFrameAnimation();
            fadeIn.Target = "Opacity";
            fadeIn.InsertKeyFrame(0f, 0f);
            fadeIn.InsertKeyFrame(1f, 1f);
            fadeIn.Duration = TimeSpan.FromMilliseconds(180);
            inVisual.StartAnimation("Opacity", fadeIn);
        }

        if (outgoing is not null)
        {
            outgoing.IsVisible = true;
            outgoing.Opacity = 1;

            var outVisual = ElementComposition.GetElementVisual(outgoing);
            if (outVisual is not null)
            {
                var comp = outVisual.Compositor;
                var fadeOut = comp.CreateScalarKeyFrameAnimation();
                fadeOut.Target = "Opacity";
                fadeOut.InsertKeyFrame(0f, 1f);
                fadeOut.InsertKeyFrame(1f, 0f);
                fadeOut.Duration = TimeSpan.FromMilliseconds(140);
                outVisual.StartAnimation("Opacity", fadeOut);
            }
        }

        try
        {
            await Task.Delay(200, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        for (var i = 0; i < MaxSteps; i++)
        {
            var p = GetPresenter(i);
            if (p is null) continue;
            var visible = i == CurrentStep;
            p.IsVisible = visible;
            p.Opacity = 1;
        }
    }
}
