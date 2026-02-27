using System;
using Avalonia;
using Avalonia.Animation;
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
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataThink Label="Reasoning…" IsActive="True"&gt;
///     &lt;controls:StrataThink.Content&gt;
///         &lt;TextBlock TextWrapping="Wrap" Text="Step 1: analyse the query..." /&gt;
///     &lt;/controls:StrataThink.Content&gt;
/// &lt;/controls:StrataThink&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Dot (Border), PART_Pill (Border), PART_HeaderRow (StackPanel).</para>
/// <para><b>Pseudo-classes:</b> :active.</para>
/// </remarks>
public class StrataThink : TemplatedControl
{
    private Border? _dot;
    private Border? _pill;
    private Control? _headerRow;
    private Transitions? _savedPillTransitions;
    private bool _initialWidthTransitionSuppressed;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataThink, string>(nameof(Label), "Thinking\u2026");

    public static readonly StyledProperty<string?> MetaProperty =
        AvaloniaProperty.Register<StrataThink, string?>(nameof(Meta));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataThink, object?>(nameof(Content));

    /// <summary>
    /// Optional progress percentage (0-100) shown as a compact progress line under the header.
    /// Set to a negative value to hide.
    /// </summary>
    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<StrataThink, double>(nameof(ProgressValue), -1);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataThink, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StrataThink, bool>(nameof(IsActive));

    static StrataThink()
    {
        IsActiveProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.UpdatePseudoClasses());
        IsExpandedProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.ApplyWidthForState());
        LabelProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.ApplyCollapsedWidth());
        MetaProperty.Changed.AddClassHandler<StrataThink>((t, _) =>
        {
            t.UpdatePseudoClasses();
            t.ApplyCollapsedWidth();
        });
        ProgressValueProperty.Changed.AddClassHandler<StrataThink>((t, _) => t.UpdatePseudoClasses());
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Meta { get => GetValue(MetaProperty); set => SetValue(MetaProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public double ProgressValue { get => GetValue(ProgressValueProperty); set => SetValue(ProgressValueProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _dot = e.NameScope.Find<Border>("PART_Dot");
        _pill = e.NameScope.Find<Border>("PART_Pill");
        _headerRow = e.NameScope.Find<Control>("PART_HeaderRow");

        var pill = e.NameScope.Find<Border>("PART_Pill");
        if (pill is not null)
            pill.PointerPressed += (_, _) => IsExpanded = !IsExpanded;

        if (_pill is not null && !IsExpanded)
        {
            // Seed with a safe compact width so first paint is never full-width.
            _pill.Width = 56;
            SuppressInitialWidthTransition();
        }

        // Apply immediately to avoid one-frame full-width flash on initial render.
        ApplyWidthForState();

        // Run one corrective pass after layout settles.
        Dispatcher.UIThread.Post(() =>
        {
            RestoreWidthTransitionIfNeeded();
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
        PseudoClasses.Set(":has-meta", !string.IsNullOrWhiteSpace(Meta));
        PseudoClasses.Set(":has-progress", ProgressValue >= 0);
        PseudoClasses.Set(":complete", ProgressValue >= 99.999);

        if (IsActive)
            StartPulse();
        else
            StopPulse();
    }

    private void OnParentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (IsExpanded)
            ApplyExpandedWidth();
        else
            ApplyCollapsedWidth();
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
        var horizontalPadding = _pill.Padding.Left + _pill.Padding.Right;
        var width = Math.Max(56, _headerRow.DesiredSize.Width + horizontalPadding);

        if (Parent is Control parent && parent.Bounds.Width > 1)
        {
            // Keep collapsed chip inside available transcript width.
            width = Math.Min(width, parent.Bounds.Width);
        }

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

        // Cap expanded width so the pill doesn't stretch across the full chat area.
        width = Math.Min(width, 480);

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

    private void SuppressInitialWidthTransition()
    {
        if (_pill is null || _pill.Transitions is not Transitions transitions || transitions.Count == 0)
            return;

        var filtered = new Transitions();
        var removedWidthTransition = false;

        foreach (var transition in transitions)
        {
            if (transition is DoubleTransition dt && dt.Property == WidthProperty)
            {
                removedWidthTransition = true;
                continue;
            }

            filtered.Add(transition);
        }

        if (!removedWidthTransition)
            return;

        _savedPillTransitions = transitions;
        _pill.Transitions = filtered;
        _initialWidthTransitionSuppressed = true;
    }

    private void RestoreWidthTransitionIfNeeded()
    {
        if (!_initialWidthTransitionSuppressed || _pill is null)
            return;

        _pill.Transitions = _savedPillTransitions;
        _savedPillTransitions = null;
        _initialWidthTransitionSuppressed = false;
    }
}
