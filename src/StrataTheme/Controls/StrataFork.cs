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
/// StrataFork presents two generated branches (A/B) and lets users pick one.
/// It preserves continuity by animating an indicator rail and cross-fading
/// branch content instead of hard switching.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataFork OptionATitle="Plan A" OptionBTitle="Plan B" SelectedIndex="0"&gt;
///     &lt;controls:StrataFork.OptionAContent&gt;&lt;TextBlock Text="Conservative approach" /&gt;&lt;/controls:StrataFork.OptionAContent&gt;
///     &lt;controls:StrataFork.OptionBContent&gt;&lt;TextBlock Text="Aggressive refactor" /&gt;&lt;/controls:StrataFork.OptionBContent&gt;
/// &lt;/controls:StrataFork&gt;
/// </code>
/// <para><b>Template parts:</b> PART_TabHost (Border), PART_Indicator (Border),
/// PART_OptionA (Button), PART_OptionB (Button),
/// PART_OptionAContent (ContentPresenter), PART_OptionBContent (ContentPresenter).</para>
/// <para><b>Pseudo-classes:</b> :a, :b.</para>
/// </remarks>
public class StrataFork : TemplatedControl
{
    private Border? _tabHost;
    private Border? _indicator;
    private Button? _optionAButton;
    private Button? _optionBButton;
    private ContentPresenter? _optionAPresenter;
    private ContentPresenter? _optionBPresenter;
    private CancellationTokenSource? _fadeCts;

    public static readonly StyledProperty<string> OptionATitleProperty =
        AvaloniaProperty.Register<StrataFork, string>(nameof(OptionATitle), "Draft A");

    public static readonly StyledProperty<string> OptionBTitleProperty =
        AvaloniaProperty.Register<StrataFork, string>(nameof(OptionBTitle), "Draft B");

    public static readonly StyledProperty<object?> OptionAContentProperty =
        AvaloniaProperty.Register<StrataFork, object?>(nameof(OptionAContent));

    public static readonly StyledProperty<object?> OptionBContentProperty =
        AvaloniaProperty.Register<StrataFork, object?>(nameof(OptionBContent));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<StrataFork, int>(nameof(SelectedIndex), 0);

    static StrataFork()
    {
        SelectedIndexProperty.Changed.AddClassHandler<StrataFork>((fork, _) => fork.OnSelectedIndexChanged());
    }

    public string OptionATitle
    {
        get => GetValue(OptionATitleProperty);
        set => SetValue(OptionATitleProperty, value);
    }

    public string OptionBTitle
    {
        get => GetValue(OptionBTitleProperty);
        set => SetValue(OptionBTitleProperty, value);
    }

    public object? OptionAContent
    {
        get => GetValue(OptionAContentProperty);
        set => SetValue(OptionAContentProperty, value);
    }

    public object? OptionBContent
    {
        get => GetValue(OptionBContentProperty);
        set => SetValue(OptionBContentProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value < 0 ? 0 : value > 1 ? 1 : value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _tabHost = e.NameScope.Find<Border>("PART_TabHost");
        _indicator = e.NameScope.Find<Border>("PART_Indicator");
        _optionAButton = e.NameScope.Find<Button>("PART_OptionA");
        _optionBButton = e.NameScope.Find<Button>("PART_OptionB");
        _optionAPresenter = e.NameScope.Find<ContentPresenter>("PART_OptionAContent");
        _optionBPresenter = e.NameScope.Find<ContentPresenter>("PART_OptionBContent");

        if (_optionAButton is not null)
            _optionAButton.Click += (_, _) => SelectedIndex = 0;

        if (_optionBButton is not null)
            _optionBButton.Click += (_, _) => SelectedIndex = 1;

        if (_tabHost is not null)
            _tabHost.SizeChanged += (_, _) => UpdateIndicatorGeometry(animated: false);

        Dispatcher.UIThread.Post(() =>
        {
            UpdatePseudoClasses();
            UpdateIndicatorGeometry(animated: false);
            ApplyContentStateInstant();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _fadeCts = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Left)
        {
            e.Handled = true;
            SelectedIndex = 0;
        }
        else if (e.Key == Key.Right)
        {
            e.Handled = true;
            SelectedIndex = 1;
        }
    }

    private void OnSelectedIndexChanged()
    {
        UpdatePseudoClasses();
        UpdateIndicatorGeometry(animated: true);
        _ = TransitionContentAsync();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":a", SelectedIndex == 0);
        PseudoClasses.Set(":b", SelectedIndex == 1);
    }

    private void UpdateIndicatorGeometry(bool animated)
    {
        if (_indicator is null || _tabHost is null)
            return;

        var tabWidth = _tabHost.Bounds.Width;
        if (tabWidth < 2)
            return;

        var half = Math.Max(0, (tabWidth - 8) / 2.0);
        _indicator.Width = half;

        var targetX = (float)(SelectedIndex == 0 ? 0 : half);
        var visual = ElementComposition.GetElementVisual(_indicator);
        if (visual is null)
            return;

        var current = visual.Offset;

        if (!animated)
        {
            visual.Offset = new Avalonia.Vector3D(targetX, current.Y, current.Z);
            return;
        }

        var comp = visual.Compositor;
        var anim = comp.CreateVector3KeyFrameAnimation();
        anim.Target = "Offset";
        anim.InsertKeyFrame(0f, new Vector3((float)current.X, (float)current.Y, (float)current.Z));
        anim.InsertKeyFrame(1f, new Vector3(targetX, (float)current.Y, (float)current.Z));
        anim.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Offset", anim);
    }

    private void ApplyContentStateInstant()
    {
        if (_optionAPresenter is null || _optionBPresenter is null)
            return;

        var a = SelectedIndex == 0;
        _optionAPresenter.IsVisible = a;
        _optionBPresenter.IsVisible = !a;
        _optionAPresenter.Opacity = 1;
        _optionBPresenter.Opacity = 1;
    }

    private async Task TransitionContentAsync()
    {
        if (_optionAPresenter is null || _optionBPresenter is null)
            return;

        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _fadeCts = new CancellationTokenSource();
        var token = _fadeCts.Token;

        var showA = SelectedIndex == 0;
        var incoming = showA ? _optionAPresenter : _optionBPresenter;
        var outgoing = showA ? _optionBPresenter : _optionAPresenter;

        // Make both visible; keep Avalonia opacity at 1 so composition can drive it
        incoming.IsVisible = true;
        outgoing.IsVisible = true;
        incoming.Opacity = 1;
        outgoing.Opacity = 1;

        var inVisual = ElementComposition.GetElementVisual(incoming);
        var outVisual = ElementComposition.GetElementVisual(outgoing);

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

        outgoing.IsVisible = false;
        incoming.IsVisible = true;
    }
}
