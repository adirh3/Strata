using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// A small pill that seamlessly expands into a full reasoning trace.
/// Designed for AI "thinking" disclosure. Collapsed shows a short label;
/// click morphs the pill into a card with animated height, corner radius,
/// and opacity — all via XAML transitions. C# only handles toggle + dot pulse.
/// </summary>
public class StrataThink : TemplatedControl
{
    private Border? _dot;
    private Border? _pill;
    private StackPanel? _headerRow;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataThink, string>(nameof(Label), "Thinking\u2026");

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataThink, object?>(nameof(Content));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataThink, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StrataThink, bool>(nameof(IsActive));

    static StrataThink()
    {
        IsActiveProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.UpdatePseudoClasses());
        IsExpandedProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.ApplyWidthForState());
        LabelProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.ApplyCollapsedWidth());
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _dot = e.NameScope.Find<Border>("PART_Dot");
        _pill = e.NameScope.Find<Border>("PART_Pill");
        _headerRow = e.NameScope.Find<StackPanel>("PART_HeaderRow");

        var pill = e.NameScope.Find<Border>("PART_Pill");
        if (pill is not null)
            pill.PointerPressed += (_, _) => IsExpanded = !IsExpanded;

        Dispatcher.UIThread.Post(() =>
        {
            ApplyWidthForState();
        }, DispatcherPriority.Loaded);

        UpdatePseudoClasses();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Parent is Control parent)
            parent.SizeChanged += OnParentSizeChanged;

        if (IsActive)
            Dispatcher.UIThread.Post(StartPulse, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Parent is Control parent)
            parent.SizeChanged -= OnParentSizeChanged;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            IsExpanded = !IsExpanded;
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":active", IsActive);
        if (IsActive)
            StartPulse();
        else
            StopPulse();
    }

    private void OnParentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (IsExpanded)
            ApplyExpandedWidth();
    }

    private void ApplyWidthForState()
    {
        if (IsExpanded)
        {
            ApplyExpandedWidth();
            return;
        }

        ApplyCollapsedWidth();
    }

    private void ApplyCollapsedWidth()
    {
        if (_pill is null || _headerRow is null || IsExpanded)
            return;

        _headerRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Max(56, _headerRow.DesiredSize.Width + 14);
        _pill.Width = width;
    }

    private void ApplyExpandedWidth()
    {
        if (_pill is null)
            return;

        var width = Bounds.Width;
        if (width < 1 && Parent is Control parent)
            width = parent.Bounds.Width;

        if (width < 1)
            width = 420;

        _pill.Width = width;
    }

    private void StartPulse()
    {
        if (_dot is null) return;
        var visual = ElementComposition.GetElementVisual(_dot);
        if (visual is null) return;

        var comp = visual.Compositor;
        var anim = comp.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.3f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(1400);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopPulse()
    {
        if (_dot is null) return;
        var visual = ElementComposition.GetElementVisual(_dot);
        if (visual is null) return;

        var comp = visual.Compositor;
        var reset = comp.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.InsertKeyFrame(1f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }
}
