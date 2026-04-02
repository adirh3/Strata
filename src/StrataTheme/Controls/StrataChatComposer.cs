using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>Lightweight chip data for agent, skill, or MCP display in the composer.</summary>
/// <param name="Name">Display label.</param>
/// <param name="Glyph">Single-character icon (default "✦").</param>
/// <param name="ErrorMessage">If set, the chip displays in an error state with this tooltip.</param>
public record StrataComposerChip(string Name, string Glyph = "✦", string? ErrorMessage = null)
{
    /// <summary>True when the chip has an error (e.g., MCP server failed to connect).</summary>
    public bool HasError => ErrorMessage is not null;
}

/// <summary>The kind of autocomplete entry or chip.</summary>
public enum ChipKind { Agent, Skill, Mcp, Project, File }

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

/// <summary>Event arguments for the file autocomplete query change.</summary>
public class FileQueryChangedEventArgs : EventArgs
{
    /// <summary>The current search query text after the # trigger.</summary>
    public string Query { get; }

    public FileQueryChangedEventArgs(string query) => Query = query;
}

/// <summary>Event arguments for when a file is selected from the autocomplete.</summary>
public class FileSelectedEventArgs : EventArgs
{
    /// <summary>The full file path that was selected.</summary>
    public string FilePath { get; }

    public FileSelectedEventArgs(string filePath) => FilePath = filePath;
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
/// PART_ModelPickerButton (Button), PART_ModelPickerPopup (Popup),
/// PART_ModelPickerList (StackPanel),
/// PART_ActionA (Button), PART_ActionB (Button), PART_ActionC (Button),
/// PART_ChipsRow (WrapPanel), PART_AgentChip (Border),
/// PART_AgentRemoveButton (Button), PART_ProjectChip (Border),
/// PART_ProjectRemoveButton (Button), PART_AutoCompletePopup (Popup),
/// PART_AutoCompletePanel (StackPanel).</para>
/// <para><b>Pseudo-classes:</b> :busy, :empty, :stop-send, :can-attach,
/// :a-empty, :b-empty, :c-empty, :has-models, :has-quality, :model-picker-open,
/// :has-agent, :has-project, :has-skills, :has-chips, :suggestions-generating.</para>
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
    private Popup? _modelPickerPopup;
    private StackPanel? _modelPickerList;
    private Avalonia.Controls.Shapes.Path? _modelPickerChevron;
    private Border? _modelPickerChevronWrap;
    private StackPanel? _effortSection;
    private bool _suppressPickerRebuild;
    private Button? _actionA;
    private Button? _actionB;
    private Button? _actionC;
    private bool _hadSuggestions;
    private readonly List<(Border Control, StrataComposerChip Chip, ChipKind Kind)> _autoCompleteEntries = new();
    private int _autoCompleteSelectedIndex = -1;
    private int _triggerIndex = -1;
    private char _triggerChar;
    private bool _suppressAutoComplete;
    private INotifyCollectionChanged? _subscribedSkillCollection;
    private INotifyCollectionChanged? _subscribedMcpCollection;
    private INotifyCollectionChanged? _subscribedAvailableMcpCollection;
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

    /// <summary>Optional item template for the model selector ComboBox.</summary>
    public static readonly StyledProperty<IDataTemplate?> ModelItemTemplateProperty =
        AvaloniaProperty.Register<StrataChatComposer, IDataTemplate?>(nameof(ModelItemTemplate));

    /// <summary>Items source for the quality/effort selector ComboBox.</summary>
    public static readonly StyledProperty<IEnumerable?> QualityLevelsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(QualityLevels));

    /// <summary>Currently selected quality level from <see cref="QualityLevels"/>.</summary>
    public static readonly StyledProperty<object?> SelectedQualityProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedQuality));

    /// <summary>Items source for the session mode selector ComboBox (e.g. Ask, Plan, Agent).</summary>
    public static readonly StyledProperty<IEnumerable?> ModesProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(Modes));

    /// <summary>Currently selected session mode from <see cref="Modes"/>.</summary>
    public static readonly StyledProperty<object?> SelectedModeProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedMode));

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

    /// <summary>Display name of the currently active project. Empty or null hides the chip.</summary>
    public static readonly StyledProperty<string?> ProjectNameProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(ProjectName));

    /// <summary>Catalog of projects available for selection via $ autocomplete.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableProjectsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableProjects));

    /// <summary>Catalog of MCP servers available for selection via the MCP button popup.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableMcpsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableMcps));

    /// <summary>
    /// Items shown in the # file autocomplete popup.
    /// Each item should be a <see cref="StrataComposerChip"/> where Name is the display path
    /// and Glyph is the file icon (e.g. "📄"). Set this in response to <see cref="FileQueryChanged"/>.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> AvailableFilesProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableFiles));

    /// <summary>Optional content displayed between the text input and the toolbar (e.g. coding project toolbar, status bar).</summary>
    public static readonly StyledProperty<object?> StatusContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(StatusContent));

    /// <summary>Optional content for displaying pending file attachments inside the composer.</summary>
    public static readonly StyledProperty<object?> AttachmentContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(AttachmentContent));

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

    /// <summary>Raised when the user removes the active project chip.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ProjectRemovedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(ProjectRemoved), RoutingStrategies.Bubble);

    /// <summary>Raised when the user removes an MCP server chip.</summary>
    public static readonly RoutedEvent<ComposerChipRemovedEventArgs> McpRemovedEvent =
        RoutedEvent.Register<StrataChatComposer, ComposerChipRemovedEventArgs>(nameof(McpRemoved), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the mention (@) button to add agents or skills.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> MentionRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(MentionRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the voice (microphone) button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> VoiceRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(VoiceRequested), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when the user pastes (Ctrl+V) and the clipboard contains an image.
    /// Hosts can handle this to attach the clipboard image to the current message.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> ClipboardImagePasteRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(ClipboardImagePasteRequested), RoutingStrategies.Bubble);

    /// <summary>When true, the voice button shows a recording indicator.</summary>
    public static readonly StyledProperty<bool> IsRecordingProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsRecording));

    /// <summary>When true, shows loading placeholders while follow-up suggestions are generated.</summary>
    public static readonly StyledProperty<bool> IsSuggestionsGeneratingProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsSuggestionsGenerating));

    /// <summary>Command executed when the user sends a prompt.</summary>
    public static readonly StyledProperty<ICommand?> SendCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(SendCommand));

    /// <summary>Optional parameter for <see cref="SendCommand"/>.</summary>
    public static readonly StyledProperty<object?> SendCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SendCommandParameter));

    /// <summary>Command executed when the user clicks the stop button.</summary>
    public static readonly StyledProperty<ICommand?> StopCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(StopCommand));

    /// <summary>Optional parameter for <see cref="StopCommand"/>.</summary>
    public static readonly StyledProperty<object?> StopCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(StopCommandParameter));

    /// <summary>Command executed when the user clicks the attach button.</summary>
    public static readonly StyledProperty<ICommand?> AttachCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(AttachCommand));

    /// <summary>Optional parameter for <see cref="AttachCommand"/>.</summary>
    public static readonly StyledProperty<object?> AttachCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(AttachCommandParameter));

    /// <summary>Command executed when the user clicks the voice button.</summary>
    public static readonly StyledProperty<ICommand?> VoiceCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(VoiceCommand));

    /// <summary>Optional parameter for <see cref="VoiceCommand"/>.</summary>
    public static readonly StyledProperty<object?> VoiceCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(VoiceCommandParameter));

    /// <summary>Command executed when the user clicks the mention button.</summary>
    public static readonly StyledProperty<ICommand?> MentionCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(MentionCommand));

    /// <summary>Optional parameter for <see cref="MentionCommand"/>.</summary>
    public static readonly StyledProperty<object?> MentionCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(MentionCommandParameter));

    /// <summary>Command executed when the user removes the active agent chip.</summary>
    public static readonly StyledProperty<ICommand?> AgentRemovedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(AgentRemovedCommand));

    /// <summary>Optional parameter for <see cref="AgentRemovedCommand"/>.</summary>
    public static readonly StyledProperty<object?> AgentRemovedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(AgentRemovedCommandParameter));

    /// <summary>Command executed when the user removes the active project chip.</summary>
    public static readonly StyledProperty<ICommand?> ProjectRemovedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(ProjectRemovedCommand));

    /// <summary>Optional parameter for <see cref="ProjectRemovedCommand"/>.</summary>
    public static readonly StyledProperty<object?> ProjectRemovedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(ProjectRemovedCommandParameter));

    /// <summary>Command executed when the user removes a skill chip. Default parameter is the chip name.</summary>
    public static readonly StyledProperty<ICommand?> SkillRemovedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(SkillRemovedCommand));

    /// <summary>Optional parameter for <see cref="SkillRemovedCommand"/>. When null, the chip name is passed.</summary>
    public static readonly StyledProperty<object?> SkillRemovedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SkillRemovedCommandParameter));

    /// <summary>Command executed when the user removes an MCP chip. Default parameter is the chip name.</summary>
    public static readonly StyledProperty<ICommand?> McpRemovedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(McpRemovedCommand));

    /// <summary>Optional parameter for <see cref="McpRemovedCommand"/>. When null, the chip name is passed.</summary>
    public static readonly StyledProperty<object?> McpRemovedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(McpRemovedCommandParameter));

    /// <summary>Command executed when the file autocomplete query changes. Default parameter is the query string.</summary>
    public static readonly StyledProperty<ICommand?> FileQueryChangedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(FileQueryChangedCommand));

    /// <summary>Optional parameter for <see cref="FileQueryChangedCommand"/>. When null, the query string is passed.</summary>
    public static readonly StyledProperty<object?> FileQueryChangedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(FileQueryChangedCommandParameter));

    /// <summary>Command executed when a file is selected from the autocomplete popup. Default parameter is the file path.</summary>
    public static readonly StyledProperty<ICommand?> FileSelectedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(FileSelectedCommand));

    /// <summary>Optional parameter for <see cref="FileSelectedCommand"/>. When null, the file path is passed.</summary>
    public static readonly StyledProperty<object?> FileSelectedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(FileSelectedCommandParameter));

    /// <summary>Command executed when the user pastes a clipboard image.</summary>
    public static readonly StyledProperty<ICommand?> ClipboardPasteCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(ClipboardPasteCommand));

    /// <summary>Optional parameter for <see cref="ClipboardPasteCommand"/>.</summary>
    public static readonly StyledProperty<object?> ClipboardPasteCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(ClipboardPasteCommandParameter));

    static StrataChatComposer()
    {
        PromptTextProperty.Changed.AddClassHandler<StrataChatComposer>((c, e) =>
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
            c.UpdateInputDirection(e.NewValue as string);
            // Defer so the TextBox has updated its CaretIndex
            Dispatcher.UIThread.Post(() => c.CheckAutoComplete(), DispatcherPriority.Input);
        });
        IsBusyProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SendWithEnterProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        CanAttachProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        IsRecordingProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        IsSuggestionsGeneratingProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.Sync();
            c.AnimateSuggestionsIfNeeded();
        });
        SuggestionAProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.Sync();
            c.AnimateSuggestionsIfNeeded();
        });
        SuggestionBProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.Sync();
            c.AnimateSuggestionsIfNeeded();
        });
        SuggestionCProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.Sync();
            c.AnimateSuggestionsIfNeeded();
        });
        AgentNameProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AgentGlyphProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        ProjectNameProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SkillItemsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnSkillItemsChanged());
        McpItemsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnMcpItemsChanged());
        AvailableMcpsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnAvailableMcpsChanged());
        AvailableFilesProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            // When file items are updated (consumer responded to FileQueryChanged),
            // refresh the popup if # trigger is active
            if (c._triggerChar == '#' && c._triggerIndex >= 0)
            {
                var text = c.PromptText ?? "";
                var caret = c._input?.CaretIndex ?? 0;
                if (c._triggerIndex < text.Length && caret <= text.Length && caret > c._triggerIndex)
                {
                    var query = text.Substring(c._triggerIndex + 1, caret - c._triggerIndex - 1);
                    c.ShowAutoCompleteItems(query);
                }
            }
        });
        ModelsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => { c.EnsureSelectedValues(); c.RefreshModelPickerIfOpen(); });
        QualityLevelsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => { c.EnsureSelectedValues(); c.RefreshModelPickerEffortIfOpen(); });
        SelectedModelProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.RefreshModelPickerSelectionIfOpen());
        SelectedQualityProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.RefreshModelPickerQualityIfOpen());
        ModesProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.EnsureSelectedValues());
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
    public event EventHandler<RoutedEventArgs>? ProjectRemoved
    { add => AddHandler(ProjectRemovedEvent, value); remove => RemoveHandler(ProjectRemovedEvent, value); }
    public event EventHandler<RoutedEventArgs>? MentionRequested
    { add => AddHandler(MentionRequestedEvent, value); remove => RemoveHandler(MentionRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? VoiceRequested
    { add => AddHandler(VoiceRequestedEvent, value); remove => RemoveHandler(VoiceRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? ClipboardImagePasteRequested
    { add => AddHandler(ClipboardImagePasteRequestedEvent, value); remove => RemoveHandler(ClipboardImagePasteRequestedEvent, value); }

    public string? PromptText { get => GetValue(PromptTextProperty); set => SetValue(PromptTextProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public IEnumerable? Models { get => GetValue(ModelsProperty); set => SetValue(ModelsProperty, value); }
    public object? SelectedModel { get => GetValue(SelectedModelProperty); set => SetValue(SelectedModelProperty, value); }
    public IDataTemplate? ModelItemTemplate { get => GetValue(ModelItemTemplateProperty); set => SetValue(ModelItemTemplateProperty, value); }
    public IEnumerable? QualityLevels{ get => GetValue(QualityLevelsProperty); set => SetValue(QualityLevelsProperty, value); }
    public object? SelectedQuality { get => GetValue(SelectedQualityProperty); set => SetValue(SelectedQualityProperty, value); }
    public IEnumerable? Modes { get => GetValue(ModesProperty); set => SetValue(ModesProperty, value); }
    public object? SelectedMode { get => GetValue(SelectedModeProperty); set => SetValue(SelectedModeProperty, value); }
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
    public string? ProjectName { get => GetValue(ProjectNameProperty); set => SetValue(ProjectNameProperty, value); }
    public IEnumerable? AvailableProjects { get => GetValue(AvailableProjectsProperty); set => SetValue(AvailableProjectsProperty, value); }
    public IEnumerable? AvailableFiles { get => GetValue(AvailableFilesProperty); set => SetValue(AvailableFilesProperty, value); }
    public bool IsRecording { get => GetValue(IsRecordingProperty); set => SetValue(IsRecordingProperty, value); }
    public object? StatusContent { get => GetValue(StatusContentProperty); set => SetValue(StatusContentProperty, value); }
    public object? AttachmentContent { get => GetValue(AttachmentContentProperty); set => SetValue(AttachmentContentProperty, value); }

    /// <summary>
    /// Raised when the # file autocomplete query changes. The consumer should
    /// search for files matching the query and set <see cref="AvailableFiles"/>.
    /// </summary>
    public event EventHandler<FileQueryChangedEventArgs>? FileQueryChanged;

    /// <summary>
    /// Raised when a file is confirmed from the # autocomplete popup.
    /// The consumer should add the file path as a pending attachment.
    /// </summary>
    public event EventHandler<FileSelectedEventArgs>? FileSelected;
    public bool IsSuggestionsGenerating { get => GetValue(IsSuggestionsGeneratingProperty); set => SetValue(IsSuggestionsGeneratingProperty, value); }
    public ICommand? SendCommand { get => GetValue(SendCommandProperty); set => SetValue(SendCommandProperty, value); }
    public object? SendCommandParameter { get => GetValue(SendCommandParameterProperty); set => SetValue(SendCommandParameterProperty, value); }
    public ICommand? StopCommand { get => GetValue(StopCommandProperty); set => SetValue(StopCommandProperty, value); }
    public object? StopCommandParameter { get => GetValue(StopCommandParameterProperty); set => SetValue(StopCommandParameterProperty, value); }
    public ICommand? AttachCommand { get => GetValue(AttachCommandProperty); set => SetValue(AttachCommandProperty, value); }
    public object? AttachCommandParameter { get => GetValue(AttachCommandParameterProperty); set => SetValue(AttachCommandParameterProperty, value); }
    public ICommand? VoiceCommand { get => GetValue(VoiceCommandProperty); set => SetValue(VoiceCommandProperty, value); }
    public object? VoiceCommandParameter { get => GetValue(VoiceCommandParameterProperty); set => SetValue(VoiceCommandParameterProperty, value); }
    public ICommand? MentionCommand { get => GetValue(MentionCommandProperty); set => SetValue(MentionCommandProperty, value); }
    public object? MentionCommandParameter { get => GetValue(MentionCommandParameterProperty); set => SetValue(MentionCommandParameterProperty, value); }
    public ICommand? AgentRemovedCommand { get => GetValue(AgentRemovedCommandProperty); set => SetValue(AgentRemovedCommandProperty, value); }
    public object? AgentRemovedCommandParameter { get => GetValue(AgentRemovedCommandParameterProperty); set => SetValue(AgentRemovedCommandParameterProperty, value); }
    public ICommand? ProjectRemovedCommand { get => GetValue(ProjectRemovedCommandProperty); set => SetValue(ProjectRemovedCommandProperty, value); }
    public object? ProjectRemovedCommandParameter { get => GetValue(ProjectRemovedCommandParameterProperty); set => SetValue(ProjectRemovedCommandParameterProperty, value); }
    public ICommand? SkillRemovedCommand { get => GetValue(SkillRemovedCommandProperty); set => SetValue(SkillRemovedCommandProperty, value); }
    public object? SkillRemovedCommandParameter { get => GetValue(SkillRemovedCommandParameterProperty); set => SetValue(SkillRemovedCommandParameterProperty, value); }
    public ICommand? McpRemovedCommand { get => GetValue(McpRemovedCommandProperty); set => SetValue(McpRemovedCommandProperty, value); }
    public object? McpRemovedCommandParameter { get => GetValue(McpRemovedCommandParameterProperty); set => SetValue(McpRemovedCommandParameterProperty, value); }
    public ICommand? FileQueryChangedCommand { get => GetValue(FileQueryChangedCommandProperty); set => SetValue(FileQueryChangedCommandProperty, value); }
    public object? FileQueryChangedCommandParameter { get => GetValue(FileQueryChangedCommandParameterProperty); set => SetValue(FileQueryChangedCommandParameterProperty, value); }
    public ICommand? FileSelectedCommand { get => GetValue(FileSelectedCommandProperty); set => SetValue(FileSelectedCommandProperty, value); }
    public object? FileSelectedCommandParameter { get => GetValue(FileSelectedCommandParameterProperty); set => SetValue(FileSelectedCommandParameterProperty, value); }
    public ICommand? ClipboardPasteCommand { get => GetValue(ClipboardPasteCommandProperty); set => SetValue(ClipboardPasteCommandProperty, value); }
    public object? ClipboardPasteCommandParameter { get => GetValue(ClipboardPasteCommandParameterProperty); set => SetValue(ClipboardPasteCommandParameterProperty, value); }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_input is not null)
            _input.RemoveHandler(KeyDownEvent, OnInputKeyDown);
        if (_autoCompletePopup is not null)
            _autoCompletePopup.Closed -= OnAutoCompletePopupClosed;

        if (_subscribedSkillCollection is not null)
        {
            _subscribedSkillCollection.CollectionChanged -= OnSkillCollectionChanged;
            _subscribedSkillCollection = null;
        }

        if (_subscribedMcpCollection is not null)
        {
            _subscribedMcpCollection.CollectionChanged -= OnMcpCollectionChanged;
            _subscribedMcpCollection = null;
        }

        if (_subscribedAvailableMcpCollection is not null)
        {
            _subscribedAvailableMcpCollection.CollectionChanged -= OnAvailableMcpCollectionChanged;
            _subscribedAvailableMcpCollection = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_input is not null)
            _input.RemoveHandler(KeyDownEvent, OnInputKeyDown);
        if (_autoCompletePopup is not null)
            _autoCompletePopup.Closed -= OnAutoCompletePopupClosed;

        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        if (_input is not null)
        {
            _input.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _input.ContextMenu = BuildInputContextMenu(_input);
        }

        Wire(e, "PART_SendButton", () => HandleSendAction());
        Wire(e, "PART_AttachButton", () =>
        {
            RaiseEvent(new RoutedEventArgs(AttachRequestedEvent));
            CommandHelper.Execute(AttachCommand, AttachCommandParameter);
        });
        Wire(e, "PART_MentionButton", () =>
        {
            ShowMentionPopup();
            CommandHelper.Execute(MentionCommand, MentionCommandParameter);
        });
        Wire(e, "PART_VoiceButton", () =>
        {
            RaiseEvent(new RoutedEventArgs(VoiceRequestedEvent));
            CommandHelper.Execute(VoiceCommand, VoiceCommandParameter);
        });
        Wire(e, "PART_AgentRemoveButton", () =>
        {
            RaiseEvent(new RoutedEventArgs(AgentRemovedEvent));
            CommandHelper.Execute(AgentRemovedCommand, AgentRemovedCommandParameter);
        });
        Wire(e, "PART_ProjectRemoveButton", () =>
        {
            RaiseEvent(new RoutedEventArgs(ProjectRemovedEvent));
            CommandHelper.Execute(ProjectRemovedCommand, ProjectRemovedCommandParameter);
        });
        _chipsRow = e.NameScope.Find<WrapPanel>("PART_ChipsRow");
        _mcpPopup = e.NameScope.Find<Popup>("PART_McpPopup");
        _mcpPopupPanel = e.NameScope.Find<StackPanel>("PART_McpPopupPanel");
        _mcpCountText = e.NameScope.Find<TextBlock>("PART_McpCount");
        _mcpButton = e.NameScope.Find<Button>("PART_McpButton");
        Wire(e, "PART_McpButton", () => ShowMcpPopup());
        _modelPickerPopup = e.NameScope.Find<Popup>("PART_ModelPickerPopup");
        _modelPickerList = e.NameScope.Find<StackPanel>("PART_ModelPickerList");
        _modelPickerChevron = e.NameScope.Find<Avalonia.Controls.Shapes.Path>("PART_ModelPickerChevron");
        _modelPickerChevronWrap = e.NameScope.Find<Border>("PART_ModelPickerChevronWrap");
        _effortSection = e.NameScope.Find<StackPanel>("PART_EffortSection");
        Wire(e, "PART_ModelPickerButton", () => ToggleModelPickerPopup());
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened += (_, _) => ConfigurePopupTranslucency(_modelPickerPopup);
            _modelPickerPopup.Closed += (_, _) =>
            {
                PseudoClasses.Set(":model-picker-open", false);
                AnimateChevron(false);
            };
        }
        _autoCompletePopup = e.NameScope.Find<Popup>("PART_AutoCompletePopup");
        _autoCompletePanel = e.NameScope.Find<StackPanel>("PART_AutoCompletePanel");
        if (_autoCompletePopup is not null)
        {
            _autoCompletePopup.PlacementTarget = _input;
            _autoCompletePopup.Closed += OnAutoCompletePopupClosed;
        }
        RebuildSkillChips();
        Wire(e, "PART_ActionA", () => Fire(SuggestionA));
        Wire(e, "PART_ActionB", () => Fire(SuggestionB));
        Wire(e, "PART_ActionC", () => Fire(SuggestionC));
        _actionA = e.NameScope.Find<Button>("PART_ActionA");
        _actionB = e.NameScope.Find<Button>("PART_ActionB");
        _actionC = e.NameScope.Find<Button>("PART_ActionC");
        _hadSuggestions = HasAnySuggestions();
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

    private void UpdateInputDirection(string? text)
    {
        if (_input is null) return;
        var direction = StrataTextDirectionDetector.Detect(text);
        var targetFlow = direction == FlowDirection.RightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        if (_input.FlowDirection != targetFlow)
            _input.FlowDirection = targetFlow;
        var targetAlignment = direction == FlowDirection.RightToLeft
            ? TextAlignment.Right
            : TextAlignment.Left;
        if (_input.TextAlignment != targetAlignment)
            _input.TextAlignment = targetAlignment;
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
            InsertTextAtSelection(textBox, text);
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

    private void OnAutoCompletePopupClosed(object? sender, EventArgs e)
    {
        _triggerIndex = -1;
        _autoCompleteSelectedIndex = -1;
    }

    private void Fire(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        PromptText = suggestion;
        HandleSendAction();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = HandlePasteFromKeyboardAsync();
            return;
        }

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

    private async Task HandlePasteFromKeyboardAsync()
    {
        var input = _input;
        var clipboard = input is not null ? TopLevel.GetTopLevel(input)?.Clipboard : null;
        if (input is null || clipboard is null)
            return;

        try
        {
            if (await TryRaiseClipboardImagePasteRequestedAsync(clipboard).ConfigureAwait(true))
                return;

            var text = await ClipboardExtensions.TryGetTextAsync(clipboard).ConfigureAwait(true);
            if (string.IsNullOrEmpty(text))
                return;

            InsertTextAtSelection(input, text);
        }
        catch
        {
            // Clipboard access can fail on some platforms/transient states.
        }
    }

    private async Task<bool> TryRaiseClipboardImagePasteRequestedAsync(IClipboard clipboard)
    {
        var dataTransfer = await clipboard.TryGetDataAsync().ConfigureAwait(true);
        if (dataTransfer is null)
            return false;

        var bitmap = await dataTransfer.TryGetBitmapAsync().ConfigureAwait(true);
        if (bitmap is null)
            return false;

        bitmap.Dispose();
        RaiseEvent(new RoutedEventArgs(ClipboardImagePasteRequestedEvent));
        CommandHelper.Execute(ClipboardPasteCommand, ClipboardPasteCommandParameter);
        return true;
    }

    private static void InsertTextAtSelection(TextBox textBox, string text)
    {
        var start = textBox.SelectionStart;
        var end = textBox.SelectionEnd;
        var lo = Math.Min(start, end);
        var hi = Math.Max(start, end);
        var current = textBox.Text ?? "";
        textBox.Text = current.Remove(lo, hi - lo).Insert(lo, text);
        textBox.CaretIndex = lo + text.Length;
    }

    private void HandleSendAction()
    {
        if (IsBusy)
        {
            if (!string.IsNullOrWhiteSpace(PromptText))
            {
                // Stop current generation and send the new message
                RaiseEvent(new RoutedEventArgs(StopRequestedEvent));
                CommandHelper.Execute(StopCommand, StopCommandParameter);
                RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
                CommandHelper.Execute(SendCommand, SendCommandParameter ?? PromptText);
            }
            else
            {
                RaiseEvent(new RoutedEventArgs(StopRequestedEvent));
                CommandHelper.Execute(StopCommand, StopCommandParameter);
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(PromptText)) return;
        RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
        CommandHelper.Execute(SendCommand, SendCommandParameter ?? PromptText);
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

            if (ch is '@' or '/' or '$' or '#')
            {
                if (i == 0 || text[i - 1] is ' ' or '\n' or '\r')
                {
                    _triggerIndex = i;
                    _triggerChar = ch;
                    var query = text.Substring(i + 1, caret - i - 1);
                    if (ch == '#')
                    {
                        // Fire event so the consumer can populate AvailableFiles.
                        // ShowAutoCompleteItems will be called when AvailableFiles is set.
                        FileQueryChanged?.Invoke(this, new FileQueryChangedEventArgs(query));
                        CommandHelper.Execute(FileQueryChangedCommand, FileQueryChangedCommandParameter ?? query);
                    }
                    else
                    {
                        ShowAutoCompleteItems(query);
                    }
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

        if (_triggerChar == '$' && AvailableProjects is not null)
        {
            var hasProjectSection = false;
            foreach (var item in AvailableProjects)
            {
                var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
                if (!string.IsNullOrEmpty(query) &&
                    !chip.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (chip.Name == ProjectName) continue;

                if (!hasProjectSection)
                {
                    _autoCompletePanel.Children.Add(CreateSectionHeader("Projects"));
                    hasProjectSection = true;
                }

                var border = CreateAutoCompleteEntry(chip, ChipKind.Project);
                _autoCompletePanel.Children.Add(border);
                _autoCompleteEntries.Add((border, chip, ChipKind.Project));
            }
        }

        if (_triggerChar == '#' && AvailableFiles is not null)
        {
            var hasFileSection = false;
            foreach (var item in AvailableFiles)
            {
                var chip = item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "", "📄");

                if (!hasFileSection)
                {
                    _autoCompletePanel.Children.Add(CreateSectionHeader("Files"));
                    hasFileSection = true;
                }

                var border = CreateAutoCompleteEntry(chip, ChipKind.File);
                _autoCompletePanel.Children.Add(border);
                _autoCompleteEntries.Add((border, chip, ChipKind.File));
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

        if (kind == ChipKind.File)
        {
            // Replace #query with #filename inline so the user sees the reference in context
            var fileName = System.IO.Path.GetFileName(chip.Name);
            var inlineRef = $"#{fileName} ";
            if (_triggerIndex >= 0 && _triggerIndex < text.Length && caret <= text.Length)
            {
                var removeLen = caret - _triggerIndex;
                _suppressAutoComplete = true;
                text = text.Remove(_triggerIndex, removeLen).Insert(_triggerIndex, inlineRef);
                PromptText = text;
                if (_input is not null)
                    _input.CaretIndex = _triggerIndex + inlineRef.Length;
                _suppressAutoComplete = false;
            }

            FileSelected?.Invoke(this, new FileSelectedEventArgs(chip.Glyph));
            CommandHelper.Execute(FileSelectedCommand, FileSelectedCommandParameter ?? chip.Glyph);
        }
        else
        {
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
                case ChipKind.Project:
                    ProjectName = chip.Name;
                    break;
            }
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

    private StrataComposerChip? FindActiveMcpChip(string name)
    {
        if (McpItems is null) return null;
        foreach (var item in McpItems)
        {
            if (item is StrataComposerChip sc && sc.Name == name) return sc;
        }
        return null;
    }

    private static Control CreateSectionHeader(string label)
    {
        var tb = new TextBlock { Text = label };
        tb.Classes.Add("autocomplete-header");
        return tb;
    }

    private Border CreateAutoCompleteEntry(StrataComposerChip chip, ChipKind kind)
    {
        var glyphDisplay = kind == ChipKind.File ? "📄" : chip.Glyph;
        var glyph = new TextBlock { Text = glyphDisplay };
        glyph.Classes.Add("autocomplete-glyph");

        var name = new TextBlock
        {
            Text = chip.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.Classes.Add("autocomplete-name");
        if (kind == ChipKind.File)
            ToolTip.SetTip(name, chip.Name);

        var kindLabel = kind switch
        {
            ChipKind.Agent => "Agent",
            ChipKind.Skill => "Skill",
            ChipKind.Mcp => "MCP",
            ChipKind.Project => "Project",
            ChipKind.File => "File",
            _ => ""
        };
        var kindText = new TextBlock { Text = kindLabel };
        kindText.Classes.Add("autocomplete-kind");

        var panel = new DockPanel();
        DockPanel.SetDock(kindText, Dock.Right);
        DockPanel.SetDock(glyph, Dock.Left);
        panel.Children.Add(kindText);
        panel.Children.Add(glyph);
        panel.Children.Add(name);

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
        PseudoClasses.Set(":stop-send", IsBusy && !string.IsNullOrWhiteSpace(PromptText));
        PseudoClasses.Set(":can-attach", CanAttach);
        PseudoClasses.Set(":a-empty", string.IsNullOrWhiteSpace(SuggestionA));
        PseudoClasses.Set(":b-empty", string.IsNullOrWhiteSpace(SuggestionB));
        PseudoClasses.Set(":c-empty", string.IsNullOrWhiteSpace(SuggestionC));
        PseudoClasses.Set(":has-models", Models is not null);
        PseudoClasses.Set(":has-quality", QualityLevels is not null);
        PseudoClasses.Set(":has-modes", Modes is not null);
        var hasAgent = !string.IsNullOrWhiteSpace(AgentName);
        var hasProject = !string.IsNullOrWhiteSpace(ProjectName);
        var hasSkills = HasAnySkills();
        var mcpCount = CountMcps();
        var totalMcpCount = CountAvailableMcps();
        PseudoClasses.Set(":has-agent", hasAgent);
        PseudoClasses.Set(":has-project", hasProject);
        PseudoClasses.Set(":has-skills", hasSkills);
        PseudoClasses.Set(":has-mcps", mcpCount > 0);
        PseudoClasses.Set(":has-chips", hasAgent || hasProject || hasSkills);
        PseudoClasses.Set(":mcp-partial", mcpCount > 0 && mcpCount < totalMcpCount);
        UpdateMcpCountText(mcpCount, totalMcpCount);
        PseudoClasses.Set(":recording", IsRecording);
        PseudoClasses.Set(":suggestions-generating", IsSuggestionsGenerating);
        PseudoClasses.Set(":has-mcp-options", HasAnyAvailableMcps());
    }

    private bool HasAnySuggestions() =>
        !string.IsNullOrWhiteSpace(SuggestionA) ||
        !string.IsNullOrWhiteSpace(SuggestionB) ||
        !string.IsNullOrWhiteSpace(SuggestionC);

    private void AnimateSuggestionsIfNeeded()
    {
        var hasSuggestions = HasAnySuggestions();
        if (!IsSuggestionsGenerating && hasSuggestions && !_hadSuggestions)
        {
            // Clean cascade reveal: slight lift + fade, staggered across chips.
            RevealSuggestionChip(_actionA, 0);
            RevealSuggestionChip(_actionB, 55);
            RevealSuggestionChip(_actionC, 110);
        }

        _hadSuggestions = hasSuggestions;
    }

    private static async void RevealSuggestionChip(Button? button, int delayMs)
    {
        if (button is null || !button.IsVisible) return;

        button.Opacity = 0;
        button.RenderTransform = TransformOperations.Parse("translateY(8px) scale(0.98)");

        button.Transitions ??= new Transitions();
        EnsureSuggestionTransition(button.Transitions, OpacityProperty, 190);
        EnsureSuggestionTransformTransition(button.Transitions, 230);

        if (delayMs > 0)
            await System.Threading.Tasks.Task.Delay(delayMs);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!button.IsVisible) return;
            button.Opacity = 1;
            button.RenderTransform = TransformOperations.Parse("translateY(0) scale(1)");
        }, DispatcherPriority.Render);
    }

    private static void EnsureSuggestionTransition(Transitions transitions, AvaloniaProperty property, int durationMs)
    {
        foreach (var transition in transitions)
        {
            if (transition is DoubleTransition doubleTransition
                && doubleTransition.Property == property)
            {
                return;
            }
        }

        transitions.Add(new DoubleTransition
        {
            Property = property,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = new CubicEaseOut(),
        });
    }

    private static void EnsureSuggestionTransformTransition(Transitions transitions, int durationMs)
    {
        foreach (var transition in transitions)
        {
            if (transition is TransformOperationsTransition transformTransition
                && transformTransition.Property == RenderTransformProperty)
            {
                return;
            }
        }

        transitions.Add(new TransformOperationsTransition
        {
            Property = RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = new CubicEaseOut(),
        });
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
        {
            _mcpCountText.Text = "";
        }
        else
        {
            // Check if any active MCP has an error
            var hasErrors = McpItems is not null && McpItems.OfType<StrataComposerChip>().Any(c => c.HasError);
            if (hasErrors)
                _mcpCountText.Text = active == total ? $"⚠ ({total})" : $"⚠ {active}/{total}";
            else if (active == total)
                _mcpCountText.Text = $"All ({total})";
            else
                _mcpCountText.Text = $"{active}/{total}";
        }
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

            // Check if the active chip has an error (e.g., MCP server failed to connect)
            var activeChip = isActive ? FindActiveMcpChip(chip.Name) : null;
            var hasError = activeChip?.HasError == true;

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

            if (hasError)
            {
                nameText.Opacity = 0.6;
                var statusDot = new Border
                {
                    Width = 6, Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E05252")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                dp.Children.Add(statusDot);
                DockPanel.SetDock(statusDot, Dock.Right);
            }

            dp.Children.Add(nameText);

            var outerPanel = new StackPanel { Spacing = 2 };
            outerPanel.Children.Add(dp);

            if (hasError && activeChip?.ErrorMessage is { } errMsg)
            {
                var errorText = new TextBlock
                {
                    Text = errMsg,
                    FontSize = 10,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E05252")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(26, 0, 0, 0),
                    Opacity = 0.9
                };
                outerPanel.Children.Add(errorText);
            }

            var border = new Border
            {
                Child = outerPanel,
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

    // ═══════════════════════════════════════════════════
    //  Model Picker popup
    // ═══════════════════════════════════════════════════

    /// <summary>Configures the popup panel background for a subtle translucent overlay.</summary>
    private void ConfigurePopupTranslucency(Popup popup)
    {
        // Keep a slight transparency effect without relying on acrylic/native blur.
        if (popup.Child is Border panel && panel.Background is Avalonia.Media.ISolidColorBrush solid)
        {
            var c = solid.Color;
            panel.Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.FromArgb(236, c.R, c.G, c.B));
        }
    }

    private void AnimateChevron(bool open)
    {
        if (_modelPickerChevronWrap is null) return;
        _modelPickerChevronWrap.RenderTransformOrigin = RelativePoint.Center;
        _modelPickerChevronWrap.RenderTransform = new RotateTransform(open ? 180 : 0);
    }

    private void ToggleModelPickerPopup()
    {
        if (_modelPickerPopup is null) return;

        if (_modelPickerPopup.IsOpen)
        {
            _modelPickerPopup.IsOpen = false;
            PseudoClasses.Set(":model-picker-open", false);
            AnimateChevron(false);
            return;
        }

        BuildModelPickerRows();
        _modelPickerPopup.IsOpen = true;
        PseudoClasses.Set(":model-picker-open", true);
        AnimateChevron(true);

        // Auto-scroll to selected model after layout
        Dispatcher.UIThread.Post(() =>
        {
            if (_modelPickerList is null) return;
            foreach (var child in _modelPickerList.Children)
            {
                if (child is Border b && b.Classes.Contains("selected"))
                {
                    b.BringIntoView();
                    break;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void RefreshModelPickerIfOpen()
    {
        if (_suppressPickerRebuild) return;
        if (_modelPickerPopup is { IsOpen: true })
            BuildModelPickerRows();
    }

    private void RefreshModelPickerSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true } || _suppressPickerRebuild)
            return;

        UpdateModelPickerSelectionVisuals(SelectedModel);
        RebuildEffortSection();
    }

    private void RefreshModelPickerEffortIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerEffortIfOpen, DispatcherPriority.Background);
            return;
        }

        RebuildEffortSection();
    }

    private void RefreshModelPickerQualityIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerQualityIfOpen, DispatcherPriority.Background);
            return;
        }

        UpdateEffortActiveState();
    }

    private void BuildModelPickerRows()
    {
        if (_modelPickerList is null) return;
        _modelPickerList.Children.Clear();

        if (Models is null) return;

        // Collect models and group them
        string? lastGroup = null;

        foreach (var model in Models)
        {
            var modelStr = model?.ToString() ?? "";
            var group = GetModelGroup(modelStr);

            // Group header when provider changes
            if (group != lastGroup)
            {
                if (lastGroup is not null)
                {
                    var sep = new Border { Height = 1, Margin = new Thickness(10, 5) };
                    sep.Classes.Add("model-picker-separator");
                    _modelPickerList.Children.Add(sep);
                }

                var header = new TextBlock
                {
                    Text = GetModelGroupLabel(group),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 0.8,
                    Margin = new Thickness(12, group == lastGroup ? 4 : 6, 12, 3)
                };
                header.Classes.Add("model-picker-group-header");
                _modelPickerList.Children.Add(header);

                lastGroup = group;
            }

            var isSelected = Equals(model, SelectedModel);
            _modelPickerList.Children.Add(CreateModelRow(model, modelStr, isSelected));
        }

        // Build the fixed effort section at the bottom
        RebuildEffortSection();
    }

    private void RebuildEffortSection()
    {
        if (_effortSection is null) return;
        _effortSection.Children.Clear();

        if (QualityLevels is null || SelectedModel is null)
            return;

        // Separator
        var sep = new Border { Height = 1, Margin = new Thickness(10, 4) };
        sep.Classes.Add("model-picker-separator");
        _effortSection.Children.Add(sep);

        // Label
        var label = new TextBlock
        {
            Text = "REASONING EFFORT",
            FontSize = 9.5,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.6,
            Margin = new Thickness(14, 4, 10, 4)
        };
        label.Classes.Add("effort-label");
        _effortSection.Children.Add(label);

        // Toggle bar
        var toggleBorder = new Border
        {
            Margin = new Thickness(8, 0, 8, 4),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3)
        };
        toggleBorder.Classes.Add("model-effort-toggle");

        var colCount = 0;
        foreach (var _ in QualityLevels) colCount++;

        var colDefs = string.Join(",", Enumerable.Range(0, colCount).Select(_ => "*"));
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(colDefs) };

        var col = 0;
        foreach (var level in QualityLevels)
        {
            var isActive = Equals(level, SelectedQuality);
            var btn = new Button
            {
                Content = level?.ToString() ?? "",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            btn.Classes.Add("effort-seg");
            if (isActive) btn.Classes.Add("active");

            var capturedLevel = level;
            btn.Click += (_, _) =>
            {
                _suppressPickerRebuild = true;
                SelectedQuality = capturedLevel;
                // Update active state in-place — just toggle CSS classes
                foreach (var child in grid.Children)
                {
                    if (child is Button b)
                    {
                        if (Equals(b.Content, capturedLevel?.ToString()))
                        {
                            if (!b.Classes.Contains("active")) b.Classes.Add("active");
                        }
                        else
                        {
                            b.Classes.Remove("active");
                        }
                    }
                }
                Dispatcher.UIThread.Post(() => _suppressPickerRebuild = false, DispatcherPriority.Background);
            };

            Grid.SetColumn(btn, col++);
            grid.Children.Add(btn);
        }

        toggleBorder.Child = grid;
        _effortSection.Children.Add(toggleBorder);
    }

    private static string GetModelGroup(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        if (lower.StartsWith("claude")) return "claude";
        if (lower.StartsWith("gpt")) return "gpt";
        if (lower.StartsWith("o1") || lower.StartsWith("o3") || lower.StartsWith("o4")) return "reasoning";
        if (lower.StartsWith("gemini")) return "gemini";
        return "other";
    }

    private static string GetModelGroupLabel(string group) => group switch
    {
        "claude" => "ANTHROPIC",
        "gpt" => "OPENAI",
        "reasoning" => "REASONING",
        "gemini" => "GOOGLE",
        _ => "OTHER"
    };

    private static string GetModelTier(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        if (lower.Contains("opus")) return "premium";
        if (lower.Contains("pro")) return "premium";
        if (lower.Contains("haiku")) return "fast";
        if (lower.Contains("mini")) return "fast";
        if (lower.Contains("codex-max") || lower.Contains("codex max")) return "max";
        if (lower.Contains("codex")) return "code";
        if (lower.Contains("1m") || lower.Contains("2m")) return "extended";
        if (IsReasoningCapable(modelId)) return "reasoning";
        return "";
    }

    private static bool IsReasoningCapable(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        return lower.StartsWith("o1") || lower.StartsWith("o3") || lower.StartsWith("o4")
            || lower.Contains("think");
    }

    private Border CreateModelRow(object model, string modelStr, bool isSelected)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("20,*,Auto") };

        // Column 0: selection indicator (accent dot)
        var dot = new Border
        {
            Width = 6, Height = 6,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = isSelected
        };
        dot.Classes.Add("model-picker-dot");
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        // Column 1: model name via template or plain text
        if (ModelItemTemplate is not null)
        {
            var presenter = new ContentPresenter
            {
                Content = model,
                ContentTemplate = ModelItemTemplate,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(presenter, 1);
            grid.Children.Add(presenter);
        }
        else
        {
            var name = new TextBlock { Text = modelStr };
            name.Classes.Add("model-name");
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
        }

        // Column 2: tier badge
        var tier = GetModelTier(modelStr);
        if (!string.IsNullOrEmpty(tier))
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = tier,
                    FontSize = 9.5,
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            badge.Classes.Add("model-tier-badge");
            if (tier is "premium" or "max" or "extended")
                badge.Classes.Add("tier-premium");
            else if (tier is "fast")
                badge.Classes.Add("tier-fast");
            else if (tier is "reasoning")
                badge.Classes.Add("tier-reasoning");
            else
                badge.Classes.Add("tier-default");
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 7, 10, 7),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        border.Classes.Add("model-picker-row");
        if (isSelected) border.Classes.Add("selected");

        var capturedModel = model;
        border.PointerPressed += (_, pe) =>
        {
            if (!pe.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            pe.Handled = true;
            // Update visual selection state in-place (no full rebuild)
            UpdateModelPickerSelection(capturedModel);
        };

        return border;
    }

    /// <summary>Updates selection dots and effort section without full list rebuild.</summary>
    private void UpdateModelPickerSelection(object newModel)
    {
        _suppressPickerRebuild = true;
        SelectedModel = newModel;

        UpdateModelPickerSelectionVisuals(newModel);

        Dispatcher.UIThread.Post(() =>
        {
            _suppressPickerRebuild = false;
            RefreshModelPickerEffortIfOpen();
        }, DispatcherPriority.Background);
    }

    private void UpdateModelPickerSelectionVisuals(object? selectedModel)
    {
        if (_modelPickerList is null)
            return;

        foreach (var child in _modelPickerList.Children)
        {
            if (child is not Border b || !b.Classes.Contains("model-picker-row"))
                continue;

            if (b.Child is not Grid g || g.Children.Count == 0 || g.Children[0] is not Border dot)
                continue;

            var isNowSelected = false;
            for (var i = 1; i < g.Children.Count; i++)
            {
                if (g.Children[i] is ContentPresenter cp && Equals(cp.Content, selectedModel))
                {
                    isNowSelected = true;
                    break;
                }

                if (g.Children[i] is TextBlock tb
                    && tb.Classes.Contains("model-name")
                    && Equals(tb.Text, selectedModel?.ToString()))
                {
                    isNowSelected = true;
                    break;
                }
            }

            dot.IsVisible = isNowSelected;
            if (isNowSelected)
            {
                if (!b.Classes.Contains("selected"))
                    b.Classes.Add("selected");
            }
            else
            {
                b.Classes.Remove("selected");
            }
        }
    }

    /// <summary>Updates only the active class on effort toggle buttons, no child add/remove.</summary>
    private void UpdateEffortActiveState()
    {
        if (_effortSection is null) return;

        // If quality levels changed and effort section is stale, rebuild once
        if (QualityLevels is not null && _effortSection.Children.Count == 0)
        {
            RebuildEffortSection();
            return;
        }
        if (QualityLevels is null && _effortSection.Children.Count > 0)
        {
            _effortSection.Children.Clear();
            return;
        }

        // Find the grid inside the effort toggle border
        foreach (var child in _effortSection.Children)
        {
            if (child is Border b && b.Classes.Contains("model-effort-toggle") && b.Child is Grid effortGrid)
            {
                var selectedQuality = SelectedQuality?.ToString();
                foreach (var gc in effortGrid.Children)
                {
                    if (gc is not Button btn) continue;
                    if (btn.Content?.ToString() == selectedQuality)
                    {
                        if (!btn.Classes.Contains("active")) btn.Classes.Add("active");
                    }
                    else
                    {
                        btn.Classes.Remove("active");
                    }
                }
                break;
            }
        }
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

        Sync();
    }

    private void OnMcpCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Sync();
    }

    private void OnAvailableMcpsChanged()
    {
        if (_subscribedAvailableMcpCollection is not null)
        {
            _subscribedAvailableMcpCollection.CollectionChanged -= OnAvailableMcpCollectionChanged;
            _subscribedAvailableMcpCollection = null;
        }

        if (AvailableMcps is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnAvailableMcpCollectionChanged;
            _subscribedAvailableMcpCollection = ncc;
        }

        Sync();
    }

    private void OnAvailableMcpCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Sync();
    }

    private void RebuildSkillChips()
    {
        if (_chipsRow is null) return;

        // Remove previous skill chips (PART_AgentChip at 0, PART_ProjectChip at 1 stay)
        while (_chipsRow.Children.Count > 2)
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
        var hasError = item is StrataComposerChip ec && ec.HasError;
        var errorMessage = item is StrataComposerChip emc ? emc.ErrorMessage : null;

        var glyphText = new TextBlock { Text = hasError ? "⚠" : glyph };
        glyphText.Classes.Add("chip-glyph");
        if (hasError) glyphText.Classes.Add("chip-error");

        var nameText = new TextBlock { Text = name };
        nameText.Classes.Add("chip-name");
        if (hasError) nameText.Classes.Add("chip-error");

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
        {
            RaiseEvent(new ComposerChipRemovedEventArgs(removedEvent, capturedItem));
            // Fire the matching command with the chip name as default parameter
            var chipName = capturedItem is StrataComposerChip cc ? cc.Name : capturedItem?.ToString() ?? "";
            if (removedEvent == SkillRemovedEvent)
                CommandHelper.Execute(SkillRemovedCommand, SkillRemovedCommandParameter ?? chipName);
            else if (removedEvent == McpRemovedEvent)
                CommandHelper.Execute(McpRemovedCommand, McpRemovedCommandParameter ?? chipName);
        };

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
        if (hasError)
        {
            border.Classes.Add("chip-error-state");
            if (!string.IsNullOrWhiteSpace(errorMessage))
                ToolTip.SetTip(border, errorMessage);
        }
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

        if (Modes is not null && SelectedMode is null)
        {
            foreach (var item in Modes)
            {
                SelectedMode = item;
                break;
            }
        }

        Sync();
    }
}
