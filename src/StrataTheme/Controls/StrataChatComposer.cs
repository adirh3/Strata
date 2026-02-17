using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Chat composer with borderless text input, model/quality selectors, suggestion chips,
/// and a circular accent send button. Enter sends; Shift+Enter inserts a newline.
/// When <see cref="IsBusy"/> is true, the send button turns into a stop button.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataChatComposer Placeholder="Ask anything…"
///                                SuggestionA="Explain this code"
///                                SuggestionB="Fix the bug"
///                                SendRequested="OnSend"
///                                StopRequested="OnStop" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Input (TextBox), PART_SendButton (Button),
/// PART_AttachButton (Button), PART_VoiceButton (Button),
/// PART_ModelCombo (ComboBox), PART_QualityCombo (ComboBox),
/// PART_ActionA (Button), PART_ActionB (Button), PART_ActionC (Button).</para>
/// <para><b>Pseudo-classes:</b> :busy, :empty, :can-attach,
/// :a-empty, :b-empty, :c-empty, :has-models, :has-quality.</para>
/// </remarks>
public class StrataChatComposer : TemplatedControl
{
    private TextBox? _input;
    private static readonly string[] DefaultModels = ["GPT-5.3-Codex", "GPT-4o", "o3"];
    private static readonly string[] DefaultQualityLevels = ["Medium", "High", "Extra High"];

    /// <summary>Two-way bound text of the prompt input.</summary>
    public static readonly StyledProperty<string?> PromptTextProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(PromptText));

    /// <summary>Watermark text shown when the input is empty.</summary>
    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(Placeholder), "Ask for follow-up changes");

    /// <summary>Items source for the model selector ComboBox.</summary>
    public static readonly StyledProperty<IEnumerable?> ModelsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(Models));

    /// <summary>Currently selected model from <see cref="Models"/>.</summary>
    public static readonly StyledProperty<object?> SelectedModelProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedModel));

    /// <summary>Items source for the quality/effort selector ComboBox.</summary>
    public static readonly StyledProperty<IEnumerable?> QualityLevelsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(QualityLevels));

    /// <summary>Currently selected quality level from <see cref="QualityLevels"/>.</summary>
    public static readonly StyledProperty<object?> SelectedQualityProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedQuality));

    /// <summary>When true, the send button becomes a stop button.</summary>
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsBusy));

    /// <summary>Whether to show the attach (+) button.</summary>
    public static readonly StyledProperty<bool> CanAttachProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(CanAttach), true);

    /// <summary>Text for the first quick-suggestion chip. Empty hides the chip.</summary>
    public static readonly StyledProperty<string> SuggestionAProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(SuggestionA), string.Empty);

    /// <summary>Text for the second quick-suggestion chip. Empty hides the chip.</summary>
    public static readonly StyledProperty<string> SuggestionBProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(SuggestionB), string.Empty);

    /// <summary>Text for the third quick-suggestion chip. Empty hides the chip.</summary>
    public static readonly StyledProperty<string> SuggestionCProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(SuggestionC), string.Empty);

    /// <summary>Raised when the user sends a prompt (Enter key or send button click).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> SendRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(SendRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the stop button during a busy state.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> StopRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(StopRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the attach (+) button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> AttachRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(AttachRequested), RoutingStrategies.Bubble);

    static StrataChatComposer()
    {
        PromptTextProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        IsBusyProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        CanAttachProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SuggestionAProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SuggestionBProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SuggestionCProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        ModelsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.EnsureSelectedValues());
        QualityLevelsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.EnsureSelectedValues());
    }

    public StrataChatComposer()
    {
        if (Models is null)
            Models = DefaultModels;

        if (QualityLevels is null)
            QualityLevels = DefaultQualityLevels;

        EnsureSelectedValues();
    }

    public event EventHandler<RoutedEventArgs>? SendRequested
    { add => AddHandler(SendRequestedEvent, value); remove => RemoveHandler(SendRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? StopRequested
    { add => AddHandler(StopRequestedEvent, value); remove => RemoveHandler(StopRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? AttachRequested
    { add => AddHandler(AttachRequestedEvent, value); remove => RemoveHandler(AttachRequestedEvent, value); }

    public string? PromptText { get => GetValue(PromptTextProperty); set => SetValue(PromptTextProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public IEnumerable? Models { get => GetValue(ModelsProperty); set => SetValue(ModelsProperty, value); }
    public object? SelectedModel { get => GetValue(SelectedModelProperty); set => SetValue(SelectedModelProperty, value); }
    public IEnumerable? QualityLevels { get => GetValue(QualityLevelsProperty); set => SetValue(QualityLevelsProperty, value); }
    public object? SelectedQuality { get => GetValue(SelectedQualityProperty); set => SetValue(SelectedQualityProperty, value); }
    public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public bool CanAttach { get => GetValue(CanAttachProperty); set => SetValue(CanAttachProperty, value); }
    public string SuggestionA { get => GetValue(SuggestionAProperty); set => SetValue(SuggestionAProperty, value); }
    public string SuggestionB { get => GetValue(SuggestionBProperty); set => SetValue(SuggestionBProperty, value); }
    public string SuggestionC { get => GetValue(SuggestionCProperty); set => SetValue(SuggestionCProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        if (_input is not null)
            _input.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Wire(e, "PART_SendButton", () => HandleSendAction());
        Wire(e, "PART_AttachButton", () => RaiseEvent(new RoutedEventArgs(AttachRequestedEvent)));
        Wire(e, "PART_ActionA", () => Fire(SuggestionA));
        Wire(e, "PART_ActionB", () => Fire(SuggestionB));
        Wire(e, "PART_ActionC", () => Fire(SuggestionC));
        EnsureSelectedValues();
        Sync();
    }

    /// <summary>
    /// Programmatically focuses the text input area.
    /// </summary>
    public void FocusInput()
    {
        Dispatcher.UIThread.Post(() => _input?.Focus(), DispatcherPriority.Loaded);
    }

    private static void Wire(TemplateAppliedEventArgs e, string name, System.Action action)
    {
        var btn = e.NameScope.Find<Button>(name);
        if (btn is not null) btn.Click += (_, _) => action();
    }

    private void Fire(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        PromptText = suggestion;
        HandleSendAction();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { e.Handled = true; HandleSendAction(); }
    }

    private void HandleSendAction()
    {
        if (IsBusy) { RaiseEvent(new RoutedEventArgs(StopRequestedEvent)); return; }
        if (string.IsNullOrWhiteSpace(PromptText)) return;
        RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
    }

    private void Sync()
    {
        PseudoClasses.Set(":busy", IsBusy);
        PseudoClasses.Set(":empty", string.IsNullOrWhiteSpace(PromptText));
        PseudoClasses.Set(":can-attach", CanAttach);
        PseudoClasses.Set(":a-empty", string.IsNullOrWhiteSpace(SuggestionA));
        PseudoClasses.Set(":b-empty", string.IsNullOrWhiteSpace(SuggestionB));
        PseudoClasses.Set(":c-empty", string.IsNullOrWhiteSpace(SuggestionC));
        PseudoClasses.Set(":has-models", Models is not null);
        PseudoClasses.Set(":has-quality", QualityLevels is not null);
    }

    private void EnsureSelectedValues()
    {
        if (Models is not null && SelectedModel is null)
        {
            foreach (var item in Models)
            {
                SelectedModel = item;
                break;
            }
        }

        if (QualityLevels is not null && SelectedQuality is null)
        {
            foreach (var item in QualityLevels)
            {
                SelectedQuality = item;
                break;
            }
        }

        Sync();
    }
}
