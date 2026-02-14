using System;
using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Live activity heartbeat ribbon. Displays a row of vertical bars
/// representing recent activity intensity. Bars animate in height
/// on update and the active bar pulses. Conveys "the system is alive."
/// </summary>
public class StrataPulse : TemplatedControl
{
    private StackPanel? _barHost;
    private TextBlock? _rateText;
    private Border? _statusDot;
    private readonly double[] _values = new double[16];
    private int _cursor;

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StrataPulse, string>(nameof(Label), "Activity");

    public static readonly StyledProperty<double> RateProperty =
        AvaloniaProperty.Register<StrataPulse, double>(nameof(Rate), 0);

    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<StrataPulse, bool>(nameof(IsLive), true);

    public static readonly StyledProperty<double> HeightMaxProperty =
        AvaloniaProperty.Register<StrataPulse, double>(nameof(HeightMax), 28);

    static StrataPulse()
    {
        IsLiveProperty.Changed.AddClassHandler<StrataPulse>((p, _) => p.UpdatePseudoClasses());
        RateProperty.Changed.AddClassHandler<StrataPulse>((p, _) => p.OnRateChanged());
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Rate { get => GetValue(RateProperty); set => SetValue(RateProperty, value); }
    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }
    public double HeightMax { get => GetValue(HeightMaxProperty); set => SetValue(HeightMaxProperty, value); }

    /// <summary>Push a new activity sample (0..1 normalized). Shifts the bar graph left.</summary>
    public void Push(double value)
    {
        value = Math.Clamp(value, 0, 1);
        _values[_cursor % _values.Length] = value;
        _cursor++;
        UpdateBars();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _barHost = e.NameScope.Find<StackPanel>("PART_BarHost");
        _rateText = e.NameScope.Find<TextBlock>("PART_RateText");
        _statusDot = e.NameScope.Find<Border>("PART_StatusDot");

        // Seed with some initial values
        var rng = new Random(42);
        for (int i = 0; i < _values.Length; i++)
            _values[i] = rng.NextDouble() * 0.6 + 0.1;
        _cursor = _values.Length;

        BuildBars();
        UpdatePseudoClasses();

        if (IsLive)
            Dispatcher.UIThread.Post(StartLivePulse, DispatcherPriority.Loaded);
    }

    private void BuildBars()
    {
        if (_barHost is null) return;
        _barHost.Children.Clear();

        for (int i = 0; i < _values.Length; i++)
        {
            var bar = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Height = Math.Max(2, _values[i] * HeightMax),
                Opacity = GetBarOpacity(i),
            };
            bar.Bind(Border.BackgroundProperty,
                this.GetResourceObservable("Brush.AccentDefault"));
            bar.Transitions = new Transitions
            {
                new DoubleTransition { Property = Border.HeightProperty, Duration = TimeSpan.FromMilliseconds(240) },
                new DoubleTransition { Property = Border.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) },
            };
            _barHost.Children.Add(bar);
        }
    }

    private void UpdateBars()
    {
        if (_barHost is null || _barHost.Children.Count != _values.Length) return;

        for (int i = 0; i < _values.Length; i++)
        {
            var idx = (_cursor - _values.Length + i + _values.Length * 2) % _values.Length;
            if (_barHost.Children[i] is Border bar)
            {
                bar.Height = Math.Max(2, _values[idx] * HeightMax);
                bar.Opacity = GetBarOpacity(i);
            }
        }
    }

    private double GetBarOpacity(int displayIndex)
    {
        // Fade older bars
        var age = (double)(_values.Length - 1 - displayIndex) / (_values.Length - 1);
        return 0.25 + 0.75 * (1.0 - age);
    }

    private void OnRateChanged()
    {
        if (_rateText is not null)
            _rateText.Text = Rate > 0 ? $"{Rate:F1}/s" : "—";
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":live", IsLive);
        PseudoClasses.Set(":paused", !IsLive);
    }

    private void StartLivePulse()
    {
        if (_statusDot is null) return;
        var visual = ElementComposition.GetElementVisual(_statusDot);
        if (visual is null) return;

        var comp = visual.Compositor;
        var anim = comp.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.35f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(1600);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }
}
