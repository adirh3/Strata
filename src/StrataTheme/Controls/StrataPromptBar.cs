using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StrataTheme.Controls;

public enum PromptIntent
{
    Summary,
    FullReport,
    ActionPlan
}

/// <summary>
/// UX-first AI composer surface with intent switching, busy state, and send trigger.
/// Built for fast prompt iteration in real-time workflows.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataPromptBar PromptText="{Binding Query, Mode=TwoWay}"
///                            Intent="Summary"
///                            SendRequested="OnGenerate" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Input (TextBox), PART_SendButton (Button),
/// PART_IntentSummary (Button), PART_IntentReport (Button), PART_IntentAction (Button).</para>
/// <para><b>Pseudo-classes:</b> :summary, :report, :action, :busy, :empty.</para>
/// </remarks>
public class StrataPromptBar : TemplatedControl
{
    private TextBox? _input;
    private Button? _sendButton;

    public static readonly StyledProperty<string?> PromptTextProperty =
        AvaloniaProperty.Register<StrataPromptBar, string?>(nameof(PromptText));

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<StrataPromptBar, string>(nameof(Placeholder), "Ask anything…");

    public static readonly StyledProperty<PromptIntent> IntentProperty =
        AvaloniaProperty.Register<StrataPromptBar, PromptIntent>(nameof(Intent), PromptIntent.Summary);

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<StrataPromptBar, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string> SendTextProperty =
        AvaloniaProperty.Register<StrataPromptBar, string>(nameof(SendText), "Generate");

    public static readonly StyledProperty<string> StopTextProperty =
        AvaloniaProperty.Register<StrataPromptBar, string>(nameof(StopText), "Stop");

    public static readonly RoutedEvent<RoutedEventArgs> SendRequestedEvent =
        RoutedEvent.Register<StrataPromptBar, RoutedEventArgs>(nameof(SendRequested), RoutingStrategies.Bubble);

    static StrataPromptBar()
    {
        IntentProperty.Changed.AddClassHandler<StrataPromptBar>((bar, _) => bar.UpdatePseudoClasses());
        IsBusyProperty.Changed.AddClassHandler<StrataPromptBar>((bar, _) => bar.UpdatePseudoClasses());
        PromptTextProperty.Changed.AddClassHandler<StrataPromptBar>((bar, _) => bar.UpdatePseudoClasses());
    }

    public event EventHandler<RoutedEventArgs>? SendRequested
    {
        add => AddHandler(SendRequestedEvent, value);
        remove => RemoveHandler(SendRequestedEvent, value);
    }

    public string? PromptText
    {
        get => GetValue(PromptTextProperty);
        set => SetValue(PromptTextProperty, value);
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public PromptIntent Intent
    {
        get => GetValue(IntentProperty);
        set => SetValue(IntentProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string SendText
    {
        get => GetValue(SendTextProperty);
        set => SetValue(SendTextProperty, value);
    }

    public string StopText
    {
        get => GetValue(StopTextProperty);
        set => SetValue(StopTextProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _input = e.NameScope.Find<TextBox>("PART_Input");

        var summary = e.NameScope.Find<Button>("PART_IntentSummary");
        var report = e.NameScope.Find<Button>("PART_IntentReport");
        var action = e.NameScope.Find<Button>("PART_IntentAction");
        _sendButton = e.NameScope.Find<Button>("PART_SendButton");

        if (_input is not null)
            _input.KeyDown += OnInputKeyDown;

        if (summary is not null)
            summary.Click += (_, _) => Intent = PromptIntent.Summary;

        if (report is not null)
            report.Click += (_, _) => Intent = PromptIntent.FullReport;

        if (action is not null)
            action.Click += (_, _) => Intent = PromptIntent.ActionPlan;

        if (_sendButton is not null)
            _sendButton.Click += (_, _) =>
            {
                if (!IsBusy && string.IsNullOrWhiteSpace(PromptText))
                    return;

                RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
            };

        UpdatePseudoClasses();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.D1)
            {
                e.Handled = true;
                Intent = PromptIntent.Summary;
                return;
            }

            if (e.Key == Key.D2)
            {
                e.Handled = true;
                Intent = PromptIntent.FullReport;
                return;
            }

            if (e.Key == Key.D3)
            {
                e.Handled = true;
                Intent = PromptIntent.ActionPlan;
                return;
            }
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (IsBusy || !string.IsNullOrWhiteSpace(PromptText))
            {
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
            }
        }
    }

    private void UpdatePseudoClasses()
    {
        var empty = string.IsNullOrWhiteSpace(PromptText);
        PseudoClasses.Set(":summary", Intent == PromptIntent.Summary);
        PseudoClasses.Set(":report", Intent == PromptIntent.FullReport);
        PseudoClasses.Set(":action", Intent == PromptIntent.ActionPlan);
        PseudoClasses.Set(":busy", IsBusy);
        PseudoClasses.Set(":empty", empty);

        if (_sendButton is not null)
            _sendButton.IsEnabled = IsBusy || !empty;
    }
}
