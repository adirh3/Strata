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
public class StrataStepFlow : TemplatedControl
{
    private Border? _track;
    private Border? _fill;
    private Border? _head;
    private ContentPresenter? _content0;
    private ContentPresenter? _content1;
    private ContentPresenter? _content2;
    private ContentPresenter? _content3;
    private CancellationTokenSource? _transitionCts;

    public static readonly StyledProperty<int> CurrentStepProperty =
        AvaloniaProperty.Register<StrataStepFlow, int>(nameof(CurrentStep), 0);

    public static readonly StyledProperty<string> Step0TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step0Title), "Draft");

    public static readonly StyledProperty<string> Step1TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step1Title), "Grounding");

    public static readonly StyledProperty<string> Step2TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step2Title), "Reasoning");

    public static readonly StyledProperty<string> Step3TitleProperty =
        AvaloniaProperty.Register<StrataStepFlow, string>(nameof(Step3Title), "Finalize");

    public static readonly StyledProperty<object?> Step0ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step0Content));

    public static readonly StyledProperty<object?> Step1ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step1Content));

    public static readonly StyledProperty<object?> Step2ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step2Content));

    public static readonly StyledProperty<object?> Step3ContentProperty =
        AvaloniaProperty.Register<StrataStepFlow, object?>(nameof(Step3Content));

    static StrataStepFlow()
    {
        CurrentStepProperty.Changed.AddClassHandler<StrataStepFlow>((flow, _) => flow.OnCurrentStepChanged());
    }

    public int CurrentStep
    {
        get => GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, Math.Clamp(value, 0, 3));
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _track = e.NameScope.Find<Border>("PART_Track");
        _fill = e.NameScope.Find<Border>("PART_Fill");
        _head = e.NameScope.Find<Border>("PART_Head");

        _content0 = e.NameScope.Find<ContentPresenter>("PART_Content0");
        _content1 = e.NameScope.Find<ContentPresenter>("PART_Content1");
        _content2 = e.NameScope.Find<ContentPresenter>("PART_Content2");
        _content3 = e.NameScope.Find<ContentPresenter>("PART_Content3");

        var b0 = e.NameScope.Find<Button>("PART_Step0");
        var b1 = e.NameScope.Find<Button>("PART_Step1");
        var b2 = e.NameScope.Find<Button>("PART_Step2");
        var b3 = e.NameScope.Find<Button>("PART_Step3");

        if (b0 is not null) b0.Click += (_, _) => CurrentStep = 0;
        if (b1 is not null) b1.Click += (_, _) => CurrentStep = 1;
        if (b2 is not null) b2.Click += (_, _) => CurrentStep = 2;
        if (b3 is not null) b3.Click += (_, _) => CurrentStep = 3;

        if (_track is not null)
            _track.SizeChanged += (_, _) => UpdateFlowProgress(animateHead: false);

        Dispatcher.UIThread.Post(() =>
        {
            UpdatePseudoClasses();
            UpdateFlowProgress(animateHead: false);
            ApplyContentInstant();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _transitionCts?.Cancel();
        _transitionCts?.Dispose();
        _transitionCts = null;
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
                CurrentStep = Math.Min(3, CurrentStep + 1);
                break;
            case Key.Home:
                e.Handled = true;
                CurrentStep = 0;
                break;
            case Key.End:
                e.Handled = true;
                CurrentStep = 3;
                break;
        }
    }

    private void OnCurrentStepChanged()
    {
        UpdatePseudoClasses();
        UpdateFlowProgress(animateHead: true);
        _ = AnimateContentTransitionAsync();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":s0", CurrentStep == 0);
        PseudoClasses.Set(":s1", CurrentStep == 1);
        PseudoClasses.Set(":s2", CurrentStep == 2);
        PseudoClasses.Set(":s3", CurrentStep == 3);
    }

    private void UpdateFlowProgress(bool animateHead)
    {
        if (_track is null || _fill is null)
            return;

        var trackWidth = _track.Bounds.Width;
        if (trackWidth < 1)
            return;

        var pct = (CurrentStep + 1) / 4.0;
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
        _ => null
    };

    private void ApplyContentInstant()
    {
        for (var i = 0; i < 4; i++)
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
        for (var i = 0; i < 4; i++)
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

        for (var i = 0; i < 4; i++)
        {
            var p = GetPresenter(i);
            if (p is null) continue;
            var visible = i == CurrentStep;
            p.IsVisible = visible;
            p.Opacity = 1;
        }
    }
}
