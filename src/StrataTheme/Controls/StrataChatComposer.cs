using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public class StrataChatComposer : TemplatedControl
{
    private TextBox? _input;
    private static readonly string[] DefaultModels = ["GPT-5.3-Codex", "GPT-4o", "o3"];
    private static readonly string[] DefaultQualityLevels = ["Medium", "High", "Extra High"];

    public static readonly StyledProperty<string?> PromptTextProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(PromptText));

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(Placeholder), "Ask for follow-up changes");

    // Model selector
    public static readonly StyledProperty<IEnumerable?> ModelsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(Models));

    public static readonly StyledProperty<object?> SelectedModelProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedModel));

    // Quality selector
    public static readonly StyledProperty<IEnumerable?> QualityLevelsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(QualityLevels));

    public static readonly StyledProperty<object?> SelectedQualityProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedQuality));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsBusy));

    public static readonly StyledProperty<bool> CanAttachProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(CanAttach), true);

    public static readonly StyledProperty<string> SuggestionAProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(SuggestionA), string.Empty);

    public static readonly StyledProperty<string> SuggestionBProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(SuggestionB), string.Empty);

    public static readonly StyledProperty<string> SuggestionCProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(SuggestionC), string.Empty);

    public static readonly RoutedEvent<RoutedEventArgs> SendRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(SendRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> StopRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(StopRequested), RoutingStrategies.Bubble);

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
