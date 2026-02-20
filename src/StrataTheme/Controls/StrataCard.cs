using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Rendering.Composition;

namespace StrataTheme.Controls;

/// <summary>
/// An expanding summary card. Shows a compact summary that, when clicked,
/// smoothly expands to reveal full content with animated height, cross-fading
/// content, and a pulsing stratum accent line.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataCard&gt;
///     &lt;controls:StrataCard.Header&gt;&lt;TextBlock Text="Result" /&gt;&lt;/controls:StrataCard.Header&gt;
///     &lt;controls:StrataCard.Summary&gt;&lt;TextBlock Text="3 items found" /&gt;&lt;/controls:StrataCard.Summary&gt;
///     &lt;controls:StrataCard.Detail&gt;&lt;TextBlock Text="Item 1, 2, 3 details..." /&gt;&lt;/controls:StrataCard.Detail&gt;
/// &lt;/controls:StrataCard&gt;
/// </code>
/// <para><b>Template parts:</b> PART_StratumLine (Border), PART_ContentHost (Border),
/// PART_SummaryPresenter (ContentPresenter), PART_DetailPresenter (ContentPresenter).</para>
/// </remarks>
public class StrataCard : TemplatedControl
{
    private Border? _stratumLine;
    private Border? _contentHost;
    private ContentPresenter? _summaryPresenter;
    private ContentPresenter? _detailPresenter;

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Header));

    public static readonly StyledProperty<object?> SummaryProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Summary));

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<StrataCard, object?>(nameof(Detail));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataCard, bool>(nameof(IsExpanded));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public object? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _stratumLine = e.NameScope.Find<Border>("PART_StratumLine");
        _contentHost = e.NameScope.Find<Border>("PART_ContentHost");
        _summaryPresenter = e.NameScope.Find<ContentPresenter>("PART_SummaryPresenter");
        _detailPresenter = e.NameScope.Find<ContentPresenter>("PART_DetailPresenter");
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        Toggle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            Toggle();
        }
    }

    private void Toggle()
    {
        if (_summaryPresenter is null || _detailPresenter is null)
            return;

        IsExpanded = !IsExpanded;

        _summaryPresenter.IsVisible = !IsExpanded;
        _detailPresenter.IsVisible = IsExpanded;

        AnimateStratumLine();
    }

    private void AnimateStratumLine()
    {
        if (_stratumLine is null) return;
        var visual = ElementComposition.GetElementVisual(_stratumLine);
        if (visual is null) return;

        var comp = visual.Compositor;
        visual.CenterPoint = new Avalonia.Vector3D(
            _stratumLine.Bounds.Width / 2,
            _stratumLine.Bounds.Height / 2, 0);

        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.Target = "Scale";
        scaleAnim.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleAnim.InsertKeyFrame(0.35f, new System.Numerics.Vector3(1f, 2f, 1f));
        scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleAnim.Duration = TimeSpan.FromMilliseconds(400);
        visual.StartAnimation("Scale", scaleAnim);
    }
}

