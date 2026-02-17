using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public class StrataTypingIndicator : TemplatedControl
{
    private Border? _dot1;
    private Border? _dot2;
    private Border? _dot3;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataTypingIndicator, string>(nameof(Label), "Thinking…");

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StrataTypingIndicator, bool>(nameof(IsActive), true);

    static StrataTypingIndicator()
    {
        IsActiveProperty.Changed.AddClassHandler<StrataTypingIndicator>((c, _) => c.Refresh());
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _dot1 = e.NameScope.Find<Border>("PART_Dot1");
        _dot2 = e.NameScope.Find<Border>("PART_Dot2");
        _dot3 = e.NameScope.Find<Border>("PART_Dot3");
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Loaded);
    }

    private void Refresh()
    {
        PseudoClasses.Set(":active", IsActive);
        if (IsActive) StartPulse(); else StopPulse();
    }

    private void StartPulse()
    {
        AnimateDot(_dot1, 0);
        AnimateDot(_dot2, 140);
        AnimateDot(_dot3, 280);
    }

    private void StopPulse()
    {
        ResetDot(_dot1);
        ResetDot(_dot2);
        ResetDot(_dot3);
    }

    private static void AnimateDot(Border? dot, int delayMs)
    {
        if (dot is null) return;

        var visual = ElementComposition.GetElementVisual(dot);
        if (visual is null) return;

        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 0.28f);
        anim.InsertKeyFrame(0.35f, 1f);
        anim.InsertKeyFrame(0.7f, 0.28f);
        anim.InsertKeyFrame(1f, 0.28f);
        anim.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        anim.Duration = TimeSpan.FromMilliseconds(980);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private static void ResetDot(Border? dot)
    {
        if (dot is null) return;

        var visual = ElementComposition.GetElementVisual(dot);
        if (visual is null) return;

        var reset = visual.Compositor.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 0.45f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }
}
