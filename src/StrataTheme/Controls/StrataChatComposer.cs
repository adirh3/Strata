using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>Lightweight chip data for agent, skill, or MCP display in the composer.</summary>
/// <param name="Name">Display label.</param>
/// <param name="Glyph">Single-character icon (default "✦").</param>
public record StrataComposerChip(string Name, string Glyph = "✦");

/// <summary>The kind of autocomplete entry or chip.</summary>
public enum ChipKind { Agent, Skill, Mcp }

/// <summary>Event arguments carrying a removed skill chip item.</summary>
public class ComposerChipRemovedEventArgs : RoutedEventArgs
{
    /// <summary>The skill item that was removed.</summary>
    public object? Item { get; }

    public ComposerChipRemovedEventArgs(RoutedEvent routedEvent, object? item) : base(routedEvent)
    {
        Item = item;
    }
}

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
/// PART_AttachButton (Button), PART_MentionButton (Button), PART_VoiceButton (Button),
/// PART_ModelCombo (ComboBox), PART_QualityCombo (ComboBox),
/// PART_ActionA (Button), PART_ActionB (Button), PART_ActionC (Button),
/// PART_ChipsRow (WrapPanel), PART_AgentChip (Border),
/// PART_AgentRemoveButton (Button), PART_AutoCompletePopup (Popup),
/// PART_AutoCompletePanel (StackPanel).</para>
/// <para><b>Pseudo-classes:</b> :busy, :empty, :can-attach,
/// :a-empty, :b-empty, :c-empty, :has-models, :has-quality,
/// :has-agent, :has-skills, :has-chips.</para>
/// </remarks>
public class StrataChatComposer : TemplatedControl
{
    private TextBox? _input;
    private WrapPanel? _chipsRow;
    private Popup? _autoCompletePopup;
    private StackPanel? _autoCompletePanel;
    private Popup? _mcpPopup;
    private StackPanel? _mcpPopupPanel;
    private TextBlock? _mcpCountText;
    private Button? _mcpButton;
    private readonly List<(Border Control, StrataComposerChip Chip, ChipKind Kind)> _autoCompleteEntries = new();
    private int _autoCompleteSelectedIndex = -1;
    private int _triggerIndex = -1;
    private char _triggerChar;
    private bool _suppressAutoComplete;
    private INotifyCollectionChanged? _subscribedSkillCollection;
    private INotifyCollectionChanged? _subscribedMcpCollection;
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

    /// <summary>When true, Enter sends and Shift+Enter inserts newline. When false, Ctrl+Enter sends.</summary>
    public static readonly StyledProperty<bool> SendWithEnterProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(SendWithEnter), true);

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

    /// <summary>Display name of the currently active agent. Empty or null hides the chip.</summary>
    public static readonly StyledProperty<string?> AgentNameProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(AgentName));

    /// <summary>Icon glyph shown in the agent chip.</summary>
    public static readonly StyledProperty<string> AgentGlyphProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AgentGlyph), "◉");

    /// <summary>
    /// Items source for skill chips displayed in the composer.
    /// Use <see cref="StrataComposerChip"/> items for icon+name display,
    /// or any object whose <c>ToString()</c> provides the label.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> SkillItemsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(SkillItems));

    /// <summary>Catalog of agents available for selection via @ autocomplete.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableAgentsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableAgents));

    /// <summary>Catalog of skills available for selection via / autocomplete.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableSkillsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableSkills));

    /// <summary>
    /// Items source for MCP server chips displayed in the composer.
    /// Use <see cref="StrataComposerChip"/> items for icon+name display.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> McpItemsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(McpItems));

    /// <summary>Catalog of MCP servers available for selection via the MCP button popup.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableMcpsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableMcps));

    /// <summary>Raised when the user sends a prompt (Enter key or send button click).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> SendRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(SendRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the stop button during a busy state.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> StopRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(StopRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the attach (+) button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> AttachRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(AttachRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user removes the active agent chip.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> AgentRemovedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(AgentRemoved), RoutingStrategies.Bubble);

    /// <summary>Raised when the user removes a skill chip. <see cref="ComposerChipRemovedEventArgs.Item"/> carries the removed item.</summary>
    public static readonly RoutedEvent<ComposerChipRemovedEventArgs> SkillRemovedEvent =
        RoutedEvent.Register<StrataChatComposer, ComposerChipRemovedEventArgs>(nameof(SkillRemoved), RoutingStrategies.Bubble);

    /// <summary>Raised when the user removes an MCP server chip.</summary>
    public static readonly RoutedEvent<ComposerChipRemovedEventArgs> McpRemovedEvent =
        RoutedEvent.Register<StrataChatComposer, ComposerChipRemovedEventArgs>(nameof(McpRemoved), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the mention (@) button to add agents or skills.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> MentionRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(MentionRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the voice (microphone) button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> VoiceRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(VoiceRequested), RoutingStrategies.Bubble);

    /// <summary>When true, the voice button shows a recording indicator.</summary>
    public static readonly StyledProperty<bool> IsRecordingProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsRecording));

    static StrataChatComposer()
    {
        PromptTextProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            // Clamp selection to new text length to prevent Avalonia crash in
            // TextPresenter.Render when text shrinks while a selection exists.
            if (c._input is not null)
            {
                var len = c.PromptText?.Length ?? 0;
                if (c._input.SelectionStart > len)
                    c._input.SelectionStart = len;
                if (c._input.SelectionEnd > len)
                    c._input.SelectionEnd = len;
            }
            c.Sync();
            // Defer so the TextBox has updated its CaretIndex
            Dispatcher.UIThread.Post(() => c.CheckAutoComplete(), DispatcherPriority.Input);
        });
        IsBusyProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SendWithEnterProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        CanAttachProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        IsRecordingProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SuggestionAProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SuggestionBProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SuggestionCProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AgentNameProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AgentGlyphProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SkillItemsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnSkillItemsChanged());
        McpItemsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnMcpItemsChanged());
        AvailableMcpsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
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
    public event EventHandler<RoutedEventArgs>? AgentRemoved
    { add => AddHandler(AgentRemovedEvent, value); remove => RemoveHandler(AgentRemovedEvent, value); }
    public event EventHandler<ComposerChipRemovedEventArgs>? SkillRemoved
    { add => AddHandler(SkillRemovedEvent, value); remove => RemoveHandler(SkillRemovedEvent, value); }
    public event EventHandler<ComposerChipRemovedEventArgs>? McpRemoved
    { add => AddHandler(McpRemovedEvent, value); remove => RemoveHandler(McpRemovedEvent, value); }
    public event EventHandler<RoutedEventArgs>? MentionRequested
    { add => AddHandler(MentionRequestedEvent, value); remove => RemoveHandler(MentionRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? VoiceRequested
    { add => AddHandler(VoiceRequestedEvent, value); remove => RemoveHandler(VoiceRequestedEvent, value); }

    public string? PromptText { get => GetValue(PromptTextProperty); set => SetValue(PromptTextProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public IEnumerable? Models { get => GetValue(ModelsProperty); set => SetValue(ModelsProperty, value); }
    public object? SelectedModel { get => GetValue(SelectedModelProperty); set => SetValue(SelectedModelProperty, value); }
    public IEnumerable? QualityLevels { get => GetValue(QualityLevelsProperty); set => SetValue(QualityLevelsProperty, value); }
    public object? SelectedQuality { get => GetValue(SelectedQualityProperty); set => SetValue(SelectedQualityProperty, value); }
    public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public bool SendWithEnter { get => GetValue(SendWithEnterProperty); set => SetValue(SendWithEnterProperty, value); }
    public bool CanAttach { get => GetValue(CanAttachProperty); set => SetValue(CanAttachProperty, value); }
    public string SuggestionA { get => GetValue(SuggestionAProperty); set => SetValue(SuggestionAProperty, value); }
    public string SuggestionB { get => GetValue(SuggestionBProperty); set => SetValue(SuggestionBProperty, value); }
    public string SuggestionC { get => GetValue(SuggestionCProperty); set => SetValue(SuggestionCProperty, value); }
    public string? AgentName { get => GetValue(AgentNameProperty); set => SetValue(AgentNameProperty, value); }
    public string AgentGlyph { get => GetValue(AgentGlyphProperty); set => SetValue(AgentGlyphProperty, value); }
    public IEnumerable? SkillItems { get => GetValue(SkillItemsProperty); set => SetValue(SkillItemsProperty, value); }
    public IEnumerable? AvailableAgents { get => GetValue(AvailableAgentsProperty); set => SetValue(AvailableAgentsProperty, value); }
    public IEnumerable? AvailableSkills { get => GetValue(AvailableSkillsProperty); set => SetValue(AvailableSkillsProperty, value); }
    public IEnumerable? McpItems { get => GetValue(McpItemsProperty); set => SetValue(McpItemsProperty, value); }
    public IEnumerable? AvailableMcps { get => GetValue(AvailableMcpsProperty); set => SetValue(AvailableMcpsProperty, value); }
    public bool IsRecording { get => GetValue(IsRecordingProperty); set => SetValue(IsRecordingProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        if (_input is not null)
        {
            _input.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _input.ContextMenu = BuildInputContextMenu(_input);
        }

        Wire(e, "PART_SendButton", () => HandleSendAction());
        Wire(e, "PART_AttachButton", () => RaiseEvent(new RoutedEventArgs(AttachRequestedEvent)));
        Wire(e, "PART_MentionButton", () => ShowMentionPopup());
        Wire(e, "PART_VoiceButton", () => RaiseEvent(new RoutedEventArgs(VoiceRequestedEvent)));
        Wire(e, "PART_AgentRemoveButton", () => RaiseEvent(new RoutedEventArgs(AgentRemovedEvent)));
        _chipsRow = e.NameScope.Find<WrapPanel>("PART_ChipsRow");
        _mcpPopup = e.NameScope.Find<Popup>("PART_McpPopup");
        _mcpPopupPanel = e.NameScope.Find<StackPanel>("PART_McpPopupPanel");
        _mcpCountText = e.NameScope.Find<TextBlock>("PART_McpCount");
        _mcpButton = e.NameScope.Find<Button>("PART_McpButton");
        Wire(e, "PART_McpButton", () => ShowMcpPopup());
        _autoCompletePopup = e.NameScope.Find<Popup>("PART_AutoCompletePopup");
        _autoCompletePanel = e.NameScope.Find<StackPanel>("PART_AutoCompletePanel");
        if (_autoCompletePopup is not null)
        {
            _autoCompletePopup.PlacementTarget = _input;
            _autoCompletePopup.Closed += (_, _) =>
            {
                _triggerIndex = -1;
                _autoCompleteSelectedIndex = -1;
            };
        }
        RebuildSkillChips();
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

    private static ContextMenu BuildInputContextMenu(TextBox textBox)
    {
        var cut = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
        var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        var paste = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        var selectAll = new MenuItem { Header = "Select All", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };

        async void DoCut()
        {
            var clip = TopLevel.GetTopLevel(textBox)?.Clipboard;
            if (clip is null || string.IsNullOrEmpty(textBox.SelectedText)) return;
            await clip.SetTextAsync(textBox.SelectedText);
            var start = textBox.SelectionStart;
            var end = textBox.SelectionEnd;
            var lo = Math.Min(start, end);
            var hi = Math.Max(start, end);
            textBox.Text = textBox.Text?.Remove(lo, hi - lo);
            textBox.CaretIndex = lo;
        }

        async void DoCopy()
        {
            var clip = TopLevel.GetTopLevel(textBox)?.Clipboard;
            if (clip is null || string.IsNullOrEmpty(textBox.SelectedText)) return;
            await clip.SetTextAsync(textBox.SelectedText);
        }

        async void DoPaste()
        {
            var clip = TopLevel.GetTopLevel(textBox)?.Clipboard;
            if (clip is null) return;
            var text = await ClipboardExtensions.TryGetTextAsync(clip);
            if (string.IsNullOrEmpty(text)) return;
            var start = textBox.SelectionStart;
            var end = textBox.SelectionEnd;
            var lo = Math.Min(start, end);
            var hi = Math.Max(start, end);
            var current = textBox.Text ?? "";
            textBox.Text = current.Remove(lo, hi - lo).Insert(lo, text);
            textBox.CaretIndex = lo + text.Length;
        }

        void DoSelectAll()
        {
            textBox.SelectionStart = 0;
            textBox.SelectionEnd = textBox.Text?.Length ?? 0;
        }

        cut.Click += (_, _) => DoCut();
        copy.Click += (_, _) => DoCopy();
        paste.Click += (_, _) => DoPaste();
        selectAll.Click += (_, _) => DoSelectAll();

        var menu = new ContextMenu
        {
            Items = { cut, copy, paste, new Separator(), selectAll }
        };

        menu.Opening += (_, _) =>
        {
            var hasSelection = !string.IsNullOrEmpty(textBox.SelectedText);
            cut.IsEnabled = hasSelection;
            copy.IsEnabled = hasSelection;
        };

        return menu;
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
        if (_autoCompletePopup?.IsOpen == true)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveAutoCompleteSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveAutoCompleteSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.Tab:
                    if (_autoCompleteEntries.Count > 0)
                    {
                        ConfirmAutoComplete();
                        e.Handled = true;
                        return;
                    }
                    break;
                case Key.Escape:
                    CloseAutoComplete();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Enter)
        {
            var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (SendWithEnter)
            {
                if (!isShift)
                {
                    e.Handled = true;
                    HandleSendAction();
                }
            }
            else if (isCtrl)
            {
                e.Handled = true;
                HandleSendAction();
            }
        }
    }

    private void HandleSendAction()
    {
        if (IsBusy) { RaiseEvent(new RoutedEventArgs(StopRequestedEvent)); return; }
        if (string.IsNullOrWhiteSpace(PromptText)) return;
        RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
    }

    // ── Inline autocomplete ────────────────────────────────────────

    private void ShowMentionPopup()
    {
        RaiseEvent(new RoutedEventArgs(MentionRequestedEvent));
        if (_input is null) return;

        var text = PromptText ?? "";
        var caret = _input.CaretIndex;
        var prefix = caret > 0 && caret <= text.Length && text[caret - 1] is not (' ' or '\n' or '\r')
            ? " @" : "@";

        _suppressAutoComplete = true;
        PromptText = text.Insert(caret, prefix);
        _suppressAutoComplete = false;

        var newCaret = caret + prefix.Length;
        Dispatcher.UIThread.Post(() =>
        {
            if (_input is null) return;
            _input.CaretIndex = newCaret;
            _input.Focus();
            _triggerIndex = newCaret - 1;
            _triggerChar = '@';
            ShowAutoCompleteItems("");
        }, DispatcherPriority.Input);
    }

    private void CheckAutoComplete()
    {
        if (_suppressAutoComplete || _input is null || _autoCompletePopup is null)
            return;

        var text = PromptText ?? "";
        var caret = _input.CaretIndex;

        if (caret <= 0 || caret > text.Length)
        {
            CloseAutoComplete();
            return;
        }

        for (var i = caret - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch is ' ' or '\n' or '\r')
                break;

            if (ch is '@' or '/')
            {
                if (i == 0 || text[i - 1] is ' ' or '\n' or '\r')
                {
                    _triggerIndex = i;
                    _triggerChar = ch;
                    var query = text.Substring(i + 1, caret - i - 1);
                    ShowAutoCompleteItems(query);
                    return;
                }
                break;
            }
        }

        CloseAutoComplete();
    }

    private void ShowAutoCompleteItems(string query)
    {
        if (_autoCompletePanel is null || _autoCompletePopup is null)
            return;

        _autoCompletePanel.Children.Clear();
        _autoCompleteEntries.Clear();
        _autoCompleteSelectedIndex = -1;

        var hasAgentSection = false;
        var hasSkillSection = false;

        if (_triggerChar == '@' && AvailableAgents is not null)
        {
            foreach (var item in AvailableAgents)
            {
                var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
                if (!string.IsNullOrEmpty(query) &&
                    !chip.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (chip.Name == AgentName) continue;

                if (!hasAgentSection)
                {
                    _autoCompletePanel.Children.Add(CreateSectionHeader("Agents"));
                    hasAgentSection = true;
                }

                var border = CreateAutoCompleteEntry(chip, ChipKind.Agent);
                _autoCompletePanel.Children.Add(border);
                _autoCompleteEntries.Add((border, chip, ChipKind.Agent));
            }
        }

        if (_triggerChar == '/' && AvailableSkills is not null)
        {
            foreach (var item in AvailableSkills)
            {
                var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
                if (!string.IsNullOrEmpty(query) &&
                    !chip.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsAlreadyActiveSkill(chip)) continue;

                if (!hasSkillSection)
                {
                    _autoCompletePanel.Children.Add(CreateSectionHeader("Skills"));
                    hasSkillSection = true;
                }

                var border = CreateAutoCompleteEntry(chip, ChipKind.Skill);
                _autoCompletePanel.Children.Add(border);
                _autoCompleteEntries.Add((border, chip, ChipKind.Skill));
            }
        }

        if (_autoCompleteEntries.Count == 0)
        {
            CloseAutoComplete();
            return;
        }

        _autoCompleteSelectedIndex = 0;
        UpdateAutoCompleteHighlight();
        PositionPopupAtTrigger();
        _autoCompletePopup.IsOpen = true;
    }

    private void PositionPopupAtTrigger()
    {
        if (_autoCompletePopup is null || _input is null || _triggerIndex < 0)
            return;

        var presenter = _input.GetVisualDescendants()
            .OfType<TextPresenter>()
            .FirstOrDefault();

        if (presenter is null)
            return;

        try
        {
            var charRect = presenter.TextLayout.HitTestTextPosition(_triggerIndex);
            // Translate from TextPresenter coords to the popup's PlacementTarget (_input)
            var presenterOrigin = presenter.TranslatePoint(new Point(0, 0), _input);
            var offsetX = (presenterOrigin?.X ?? 0) + charRect.Left;
            var offsetY = (presenterOrigin?.Y ?? 0) + charRect.Bottom;
            _autoCompletePopup.PlacementRect = new Rect(offsetX, offsetY, 1, 1);
        }
        catch
        {
            // Fallback: no custom placement, use default
        }
    }

    private void CloseAutoComplete()
    {
        if (_autoCompletePopup is not null)
            _autoCompletePopup.IsOpen = false;
        _triggerIndex = -1;
        _autoCompleteSelectedIndex = -1;
    }

    private void ConfirmAutoComplete()
    {
        if (_autoCompleteSelectedIndex < 0 || _autoCompleteSelectedIndex >= _autoCompleteEntries.Count)
            return;

        var (_, chip, kind) = _autoCompleteEntries[_autoCompleteSelectedIndex];

        var text = PromptText ?? "";
        var caret = _input?.CaretIndex ?? text.Length;
        if (_triggerIndex >= 0 && _triggerIndex < text.Length && caret <= text.Length)
        {
            var removeLen = caret - _triggerIndex;
            if (removeLen > 0)
            {
                var restoreCaret = _triggerIndex;
                _suppressAutoComplete = true;
                PromptText = text.Remove(_triggerIndex, removeLen);
                if (_input is not null)
                    _input.CaretIndex = restoreCaret;
                _suppressAutoComplete = false;
            }
        }

        switch (kind)
        {
            case ChipKind.Agent:
                AgentName = chip.Name;
                AgentGlyph = chip.Glyph;
                break;
            case ChipKind.Skill:
                if (SkillItems is System.Collections.IList skillList && !IsAlreadyActiveSkill(chip))
                    skillList.Add(chip);
                break;
        }

        CloseAutoComplete();
        _input?.Focus();
    }

    private void MoveAutoCompleteSelection(int delta)
    {
        if (_autoCompleteEntries.Count == 0) return;
        _autoCompleteSelectedIndex = (_autoCompleteSelectedIndex + delta + _autoCompleteEntries.Count) % _autoCompleteEntries.Count;
        UpdateAutoCompleteHighlight();
    }

    private void UpdateAutoCompleteHighlight()
    {
        for (var i = 0; i < _autoCompleteEntries.Count; i++)
        {
            var border = _autoCompleteEntries[i].Control;
            if (i == _autoCompleteSelectedIndex)
            {
                if (!border.Classes.Contains("selected"))
                    border.Classes.Add("selected");
            }
            else
            {
                border.Classes.Remove("selected");
            }
        }
    }

    private bool IsAlreadyActiveSkill(StrataComposerChip chip)
    {
        if (SkillItems is null) return false;
        foreach (var item in SkillItems)
        {
            if (item is StrataComposerChip sc && sc.Name == chip.Name) return true;
            if (item?.ToString() == chip.Name) return true;
        }
        return false;
    }

    private bool IsAlreadyActiveMcp(StrataComposerChip chip)
    {
        if (McpItems is null) return false;
        foreach (var item in McpItems)
        {
            if (item is StrataComposerChip sc && sc.Name == chip.Name) return true;
            if (item?.ToString() == chip.Name) return true;
        }
        return false;
    }

    private static Control CreateSectionHeader(string label)
    {
        var tb = new TextBlock { Text = label };
        tb.Classes.Add("autocomplete-header");
        return tb;
    }

    private Border CreateAutoCompleteEntry(StrataComposerChip chip, ChipKind kind)
    {
        var glyph = new TextBlock { Text = chip.Glyph };
        glyph.Classes.Add("autocomplete-glyph");

        var name = new TextBlock { Text = chip.Name };
        name.Classes.Add("autocomplete-name");

        var kindLabel = kind switch
        {
            ChipKind.Agent => "Agent",
            ChipKind.Skill => "Skill",
            ChipKind.Mcp => "MCP",
            _ => ""
        };
        var kindText = new TextBlock { Text = kindLabel };
        kindText.Classes.Add("autocomplete-kind");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(glyph);
        panel.Children.Add(name);
        panel.Children.Add(kindText);

        var border = new Border
        {
            Child = panel,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        border.Classes.Add("autocomplete-item");

        border.PointerPressed += (_, pe) =>
        {
            if (!pe.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            for (var i = 0; i < _autoCompleteEntries.Count; i++)
            {
                if (_autoCompleteEntries[i].Control == border)
                {
                    _autoCompleteSelectedIndex = i;
                    break;
                }
            }
            ConfirmAutoComplete();
            pe.Handled = true;
        };

        border.PointerEntered += (_, _) =>
        {
            for (var i = 0; i < _autoCompleteEntries.Count; i++)
            {
                if (_autoCompleteEntries[i].Control == border)
                {
                    _autoCompleteSelectedIndex = i;
                    UpdateAutoCompleteHighlight();
                    break;
                }
            }
        };

        return border;
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
        var hasAgent = !string.IsNullOrWhiteSpace(AgentName);
        var hasSkills = HasAnySkills();
        var mcpCount = CountMcps();
        var totalMcpCount = CountAvailableMcps();
        PseudoClasses.Set(":has-agent", hasAgent);
        PseudoClasses.Set(":has-skills", hasSkills);
        PseudoClasses.Set(":has-mcps", mcpCount > 0);
        PseudoClasses.Set(":has-chips", hasAgent || hasSkills);
        PseudoClasses.Set(":mcp-partial", mcpCount > 0 && mcpCount < totalMcpCount);
        UpdateMcpCountText(mcpCount, totalMcpCount);
        PseudoClasses.Set(":recording", IsRecording);
        PseudoClasses.Set(":has-mcp-options", HasAnyAvailableMcps());
    }

    private bool HasAnySkills()
    {
        if (SkillItems is null) return false;
        foreach (var _ in SkillItems) return true;
        return false;
    }

    private int CountMcps()
    {
        if (McpItems is null) return 0;
        var count = 0;
        foreach (var _ in McpItems) count++;
        return count;
    }

    private int CountAvailableMcps()
    {
        if (AvailableMcps is null) return 0;
        var count = 0;
        foreach (var _ in AvailableMcps) count++;
        return count;
    }

    private bool HasAnyAvailableMcps()
    {
        if (AvailableMcps is null) return false;
        foreach (var _ in AvailableMcps) return true;
        return false;
    }

    private void UpdateMcpCountText(int active, int total)
    {
        if (_mcpCountText is null) return;
        if (total == 0)
            _mcpCountText.Text = "";
        else if (active == total)
            _mcpCountText.Text = $"All ({total})";
        else
            _mcpCountText.Text = $"{active}/{total}";
    }

    // ── MCP button popup ───────────────────────────────────────────

    private void ShowMcpPopup()
    {
        if (_mcpPopup is null || _mcpPopupPanel is null || AvailableMcps is null)
            return;

        _mcpPopupPanel.Children.Clear();

        var checkboxes = new List<(CheckBox Cb, StrataComposerChip Chip)>();

        foreach (var item in AvailableMcps)
        {
            var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
            var isActive = IsAlreadyActiveMcp(chip);

            var cb = new CheckBox
            {
                IsChecked = isActive,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameText = new TextBlock
            {
                Text = chip.Name,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            };

            // Build row with DockPanel for better layout
            var dp = new DockPanel { LastChildFill = true };
            dp.Children.Add(cb);
            DockPanel.SetDock(cb, Dock.Left);
            nameText.Margin = new Thickness(10, 0, 0, 0);
            dp.Children.Add(nameText);

            var border = new Border
            {
                Child = dp,
                Padding = new Thickness(10, 7),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            border.Classes.Add("mcp-popup-item");

            var capturedChip = chip;
            var capturedCb = cb;
            checkboxes.Add((cb, chip));

            border.PointerPressed += (_, pe) =>
            {
                if (!pe.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
                capturedCb.IsChecked = !capturedCb.IsChecked;
                pe.Handled = true;
            };

            cb.IsCheckedChanged += (_, _) =>
            {
                if (cb.IsChecked == true)
                {
                    if (McpItems is System.Collections.IList list && !IsAlreadyActiveMcp(capturedChip))
                        list.Add(capturedChip);
                }
                else
                {
                    if (McpItems is System.Collections.IList list)
                    {
                        for (var i = list.Count - 1; i >= 0; i--)
                        {
                            var existing = list[i];
                            var existingName = existing is StrataComposerChip sc ? sc.Name : existing?.ToString() ?? "";
                            if (existingName == capturedChip.Name)
                            {
                                list.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
                Sync();
            };

            _mcpPopupPanel.Children.Add(border);
        }

        // Separator line before action buttons
        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(4, 4)
        };
        separator.Classes.Add("mcp-popup-separator");
        _mcpPopupPanel.Children.Add(separator);

        // Select All / Deselect All buttons at the bottom
        var selectAll = new Button { Content = "Select All", Padding = new Thickness(10, 4), MinHeight = 0, MinWidth = 0, FontSize = 11 };
        selectAll.Classes.Add("subtle");
        selectAll.Click += (_, _) =>
        {
            foreach (var (cb, _) in checkboxes)
                cb.IsChecked = true;
        };

        var deselectAll = new Button { Content = "Deselect All", Padding = new Thickness(10, 4), MinHeight = 0, MinWidth = 0, FontSize = 11 };
        deselectAll.Classes.Add("subtle");
        deselectAll.Click += (_, _) =>
        {
            foreach (var (cb, _) in checkboxes)
                cb.IsChecked = false;
        };

        var actionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(4, 0, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actionsRow.Children.Add(selectAll);
        actionsRow.Children.Add(deselectAll);
        _mcpPopupPanel.Children.Add(actionsRow);

        _mcpPopup.IsOpen = true;
    }

    private void OnSkillItemsChanged()
    {
        if (_subscribedSkillCollection is not null)
        {
            _subscribedSkillCollection.CollectionChanged -= OnSkillCollectionChanged;
            _subscribedSkillCollection = null;
        }

        if (SkillItems is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnSkillCollectionChanged;
            _subscribedSkillCollection = ncc;
        }

        RebuildSkillChips();
    }

    private void OnSkillCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSkillChips();
    }

    private void OnMcpItemsChanged()
    {
        if (_subscribedMcpCollection is not null)
        {
            _subscribedMcpCollection.CollectionChanged -= OnMcpCollectionChanged;
            _subscribedMcpCollection = null;
        }

        if (McpItems is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnMcpCollectionChanged;
            _subscribedMcpCollection = ncc;
        }

        RebuildSkillChips();
    }

    private void OnMcpCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSkillChips();
    }

    private void RebuildSkillChips()
    {
        if (_chipsRow is null) return;

        // Remove previous skill chips (PART_AgentChip stays at index 0)
        while (_chipsRow.Children.Count > 1)
            _chipsRow.Children.RemoveAt(_chipsRow.Children.Count - 1);

        if (SkillItems is not null)
        {
            foreach (var item in SkillItems)
                _chipsRow.Children.Add(CreateChip(item, SkillRemovedEvent, "composer-skill-chip"));
        }

        Sync();
    }

    private Control CreateChip(object item, RoutedEvent<ComposerChipRemovedEventArgs> removedEvent, string cssClass)
    {
        var name = item is StrataComposerChip c ? c.Name : item?.ToString() ?? "";
        var glyph = item is StrataComposerChip sc ? sc.Glyph : "✦";

        var glyphText = new TextBlock { Text = glyph };
        glyphText.Classes.Add("chip-glyph");

        var nameText = new TextBlock { Text = name };
        nameText.Classes.Add("chip-name");

        var removeIcon = new TextBlock { Text = "×" };
        removeIcon.Classes.Add("chip-remove-icon");

        var removeBtn = new Button
        {
            Width = 16, Height = 16,
            Padding = new Thickness(0),
            MinHeight = 0, MinWidth = 0,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Content = removeIcon,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        removeBtn.Classes.Add("subtle");
        removeBtn.Classes.Add("chip-remove");

        var capturedItem = item;
        removeBtn.Click += (_, _) =>
            RaiseEvent(new ComposerChipRemovedEventArgs(removedEvent, capturedItem));

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };
        panel.Children.Add(glyphText);
        panel.Children.Add(nameText);
        panel.Children.Add(removeBtn);

        var border = new Border { Child = panel };
        border.Classes.Add(cssClass);
        return border;
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
