using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Confidence visualization for generated outputs.
/// Presents calibrated confidence with a smooth meter and qualitative band.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataConfidence Label="Answer quality" Confidence="85" IsEditable="True" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Track (Border), PART_Fill (Border),
/// PART_PercentText (TextBlock), PART_BandText (TextBlock).</para>
/// <para><b>Pseudo-classes:</b> :high, :medium, :low, :editable.</para>
/// </remarks>
public class StrataConfidence : TemplatedControl
{
    private Border? _track;
    private Border? _fill;
    private TextBlock? _percentText;
    private TextBlock? _bandText;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataConfidence, string>(nameof(Label), "Confidence");

    public static readonly StyledProperty<double> ConfidenceProperty =
        AvaloniaProperty.Register<StrataConfidence, double>(nameof(Confidence), 72);

    public static readonly StyledProperty<object?> ExplanationProperty =
        AvaloniaProperty.Register<StrataConfidence, object?>(nameof(Explanation));

    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<StrataConfidence, bool>(nameof(IsEditable));

    static StrataConfidence()
    {
        ConfidenceProperty.Changed.AddClassHandler<StrataConfidence>((control, _) => control.UpdateGauge());
        IsEditableProperty.Changed.AddClassHandler<StrataConfidence>((control, _) => control.UpdateGauge());
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double Confidence
    {
        get => GetValue(ConfidenceProperty);
        set => SetValue(ConfidenceProperty, value);
    }

    public object? Explanation
    {
        get => GetValue(ExplanationProperty);
        set => SetValue(ExplanationProperty, value);
    }

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _track = e.NameScope.Find<Border>("PART_Track");
        _fill = e.NameScope.Find<Border>("PART_Fill");
        _percentText = e.NameScope.Find<TextBlock>("PART_PercentText");
        _bandText = e.NameScope.Find<TextBlock>("PART_BandText");

        if (_track is not null)
        {
            _track.SizeChanged += (_, _) => UpdateGauge();
            _track.PointerPressed += OnTrackPressed;
        }

        Dispatcher.UIThread.Post(UpdateGauge, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEditable)
            return;

        if (e.Key == Key.Left)
        {
            e.Handled = true;
            Confidence = Math.Max(0, Confidence - 5);
        }
        else if (e.Key == Key.Right)
        {
            e.Handled = true;
            Confidence = Math.Min(100, Confidence + 5);
        }
    }

    private void OnTrackPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEditable || _track is null)
            return;

        var point = e.GetPosition(_track);
        var width = _track.Bounds.Width;
        if (width < 1)
            return;

        var ratio = Math.Clamp(point.X / width, 0, 1);
        Confidence = ratio * 100;
        e.Handled = true;
    }

    private void UpdateGauge()
    {
        var value = Math.Clamp(Confidence, 0, 100);

        if (_percentText is not null)
            _percentText.Text = $"{value:F0}%";

        var band = value >= 80 ? "High" : value >= 55 ? "Medium" : "Low";

        if (_bandText is not null)
            _bandText.Text = band;

        PseudoClasses.Set(":high", band == "High");
        PseudoClasses.Set(":medium", band == "Medium");
        PseudoClasses.Set(":low", band == "Low");
        PseudoClasses.Set(":editable", IsEditable);

        if (_track is null || _fill is null)
            return;

        var width = _track.Bounds.Width;
        if (width < 1)
            return;

        _fill.Width = Math.Max(0, width * (value / 100.0));
    }
}
