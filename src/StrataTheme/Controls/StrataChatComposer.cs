using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Animation;

namespace StrataTheme.Controls;

/// <summary>Lightweight chip data for agent, skill, or MCP display in the composer.</summary>
/// <param name="Name">Primary display label.</param>
/// <param name="Glyph">Single-character icon or fallback marker (default "✦").</param>
/// <param name="ErrorMessage">
/// If set, the chip is in an error state. Keep it short and human: it is the one-line reason the MCP
/// picker shows under the item, and the chip's tooltip.
/// </param>
/// <param name="SecondaryText">Optional supporting text shown in richer autocomplete rows and used as a secondary search field.</param>
/// <param name="Value">Optional backing value used when the display label differs from the payload.</param>
/// <param name="SourceLabel">Optional short provenance hint (for example "Lumi", "Project" or "Plugin · foo") shown as a trailing badge in autocomplete rows.</param>
/// <param name="ErrorDetails">Optional complete technical details behind <paramref name="ErrorMessage"/>, revealed only on request.</param>
/// <param name="StatusMessage">
/// Optional neutral status that is not a problem, such as "Still starting". The MCP picker shows it
/// under the item without the error state's attention cues or actions.
/// </param>
/// <param name="Group">Optional heading the MCP picker lists the item under, such as "Yours".</param>
public record StrataComposerChip(
    string Name,
    string Glyph = "✦",
    string? ErrorMessage = null,
    string? SecondaryText = null,
    string? Value = null,
    string? SourceLabel = null,
    string? ErrorDetails = null,
    string? StatusMessage = null,
    string? Group = null)
{
    /// <summary>True when the chip has an error (e.g., MCP server failed to connect).</summary>
    public bool HasError => ErrorMessage is not null;
}

/// <summary>The kind of autocomplete entry or chip.</summary>
public enum ChipKind { Agent, Skill, Mcp, Project, File, Chat }

/// <summary>Optional editor contract for hosts that replace the templated Avalonia TextBox.</summary>
public interface IStrataComposerEditor
{
    /// <summary>Current zero-based caret index in the editor text.</summary>
    int CaretIndex { get; }

    /// <summary>Focuses the editor and places its caret at the requested index.</summary>
    void FocusAt(int caretIndex);

    /// <summary>Focuses the editor and places its caret at the end of the prompt.</summary>
    void FocusAtEnd();
}

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
/// When <see cref="IsBusy"/> is true, the send button turns into a stop button. If the user types
/// while busy and <see cref="SteerWhileBusy"/> is true (default), submitting steers the running turn
/// (sends without stopping) instead of aborting generation.
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
/// <para><b>Template parts:</b> PART_Root (Border), PART_Input (TextBox),
/// PART_EditorContent (ContentPresenter),
/// PART_SendButton (Button: sends, steers, stops, and dictates while there is nothing to send),
/// PART_AddButton (Button), PART_AddPopup (Popup), PART_AddPanel (Border), PART_AddMenuList (StackPanel),
/// PART_ModelPicker (StrataModelPicker),
/// PART_ActionA (Button), PART_ActionB (Button), PART_ActionC (Button),
/// PART_ChipsRow (WrapPanel), PART_AgentChip (Border),
/// PART_AgentRemoveButton (Button), PART_ProjectChip (Border),
/// PART_ProjectRemoveButton (Button), PART_FocusLine (Border), PART_FocusSweep (Border), PART_AutoCompletePopup (Popup),
/// PART_AutoCompletePanel (StackPanel), PART_McpPanel (Panel) hosting PART_ConnectorPicker (StrataConnectorPicker),
/// PART_SkillPicker, PART_ChatPicker and PART_AgentPicker (StrataComposerPicker).</para>
/// <para><b>Pseudo-classes:</b> :busy, :empty, :steer, :stop-send, :external-editor, :editor-content-hidden, :compact,
/// :can-attach, :can-send-without-prompt,
/// :can-voice, :editing, :a-empty, :b-empty, :c-empty, :has-models, :has-quality, :model-picker-open,
/// :has-agent, :has-project, :has-skills, :has-chats, :has-chips, :has-skill-options,
/// :has-chat-options, :has-add-options, :add-open, :engaged, :voice, :recording, :narrow, :suggestions-generating.</para>
/// <para><b>Glass:</b> the <c>glass</c> class turns the surface translucent over a frosted backdrop, for hosts
/// whose content runs beneath the composer. A glass composer sits back a little at rest and comes forward,
/// whole, while the user writes (its focus depth, scaled on the render thread); <c>motion-disabled</c> and
/// <c>mobile</c> keep it at full size.</para>
/// </remarks>
public partial class StrataChatComposer : TemplatedControl
{
    private TextBox? _input;
    private Border? _focusSweep;
    private readonly EffectiveVisibilityObserver _focusSweepVisibility;
    private IDisposable? _focusSweepTimer;
    private bool _focusSweepPlayed;
    // The composer's own lists (the + menu, pickers, the model list) open in the overlay layer, so the
    // keyboard leaves the composer's visual tree while one is open. They still count as writing here.
    private readonly HashSet<Popup> _openOwnPopups = new();
    private bool _engagedHold;
    private bool _engagedHoldCheckQueued;
    private Button? _sendButton;
    private WrapPanel? _chipsRow;
    private Popup? _autoCompletePopup;
    private StackPanel? _autoCompletePanel;
    private ScrollViewer? _autoCompleteScrollViewer;
    private Popup? _modelPickerPopup;
    private StackPanel? _modelPickerList;
    private Avalonia.Controls.Shapes.Path? _modelPickerChevron;
    private Border? _modelPickerChevronWrap;
    private StackPanel? _effortSection;
    private StackPanel? _contextWindowSection;
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
    private INotifyCollectionChanged? _subscribedChatCollection;
    private INotifyCollectionChanged? _subscribedAvailableMcpCollection;
    private INotifyCollectionChanged? _subscribedAvailableFileCollection;
    private bool _fileAutoCompleteRefreshQueued;
    private StrataComposerPicker? _skillPicker;
    private StrataComposerPicker? _chatPicker;
    // The # reference being typed, kept while its list waits for the host's files: the popup closes
    // when nothing matches yet, but the reference stays live until it is dismissed or finished.
    private int _referenceTriggerIndex = -1;
    // The # list closed only because nothing matches yet. Its Closed event arrives after the closing fade,
    // so this lasts until the list shows again rather than just while it closes.
    private bool _awaitingReferenceFiles;
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

    /// <summary>
    /// Optional template for the collapsed model button, forwarded to
    /// <see cref="StrataModelPicker.SelectedModelTemplate"/>. Falls back to
    /// <see cref="ModelItemTemplate"/> when unset.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> SelectedModelTemplateProperty =
        AvaloniaProperty.Register<StrataChatComposer, IDataTemplate?>(nameof(SelectedModelTemplate));

    /// <summary>
    /// Pin/unpin command forwarded to <see cref="StrataModelPicker.ModelPinCommand"/>. Invoked with the
    /// row's item, which must implement <see cref="IStrataModelOption"/> for the pin to be rendered.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ModelPinCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(ModelPinCommand));

    /// <summary>Tooltip for an unpinned row's pin button, forwarded to <see cref="StrataModelPicker.PinToolTip"/>.</summary>
    public static readonly StyledProperty<string?> ModelPinToolTipProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(ModelPinToolTip));

    /// <summary>Tooltip for a pinned row's pin button, forwarded to <see cref="StrataModelPicker.UnpinToolTip"/>.</summary>
    public static readonly StyledProperty<string?> ModelUnpinToolTipProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(ModelUnpinToolTip));

    /// <summary>Tooltip for the MCP server button. Hosts that name MCP servers differently, such as "Connectors", set their own.</summary>
    public static readonly StyledProperty<string> McpToolTipProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(McpToolTip), "MCP Servers");

    /// <summary>
    /// Accessible name of the MCP server list, forwarded to <see cref="StrataConnectorPicker.Header"/>. Like
    /// <see cref="McpToolTip"/>, hosts may name it their way.
    /// </summary>
    public static readonly StyledProperty<string> McpHeaderProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(McpHeader), "MCP Servers");

    /// <summary>
    /// Action offered on MCP list items whose server reports an error, such as "Fix with Lumi".
    /// The parameter is the affected <see cref="StrataComposerChip"/>. Choosing it closes the list.
    /// </summary>
    public static readonly StyledProperty<ICommand?> McpErrorActionCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(McpErrorActionCommand));

    /// <summary>Label of <see cref="McpErrorActionCommand"/>. The action is hidden while this is empty.</summary>
    public static readonly StyledProperty<string?> McpErrorActionLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(McpErrorActionLabel));

    /// <summary>Label of the toggle that reveals an MCP error's <see cref="StrataComposerChip.ErrorDetails"/>.</summary>
    public static readonly StyledProperty<string> McpErrorDetailsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(McpErrorDetailsLabel), "Details");

    /// <summary>Tooltip and accessible name of the button that copies an MCP error's details.</summary>
    public static readonly StyledProperty<string> McpCopyErrorDetailsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(McpCopyErrorDetailsLabel), "Copy details");

    /// <summary>
    /// Finds new MCP servers, forwarded to <see cref="StrataConnectorPicker.BrowseCommand"/>. The parameter
    /// is the picker's search text, or null. While set, the MCP button shows even before any server
    /// exists, so the host's servers can be found from the composer.
    /// </summary>
    public static readonly StyledProperty<ICommand?> McpBrowseCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(McpBrowseCommand));

    /// <summary>Opens the host's MCP server settings, forwarded to <see cref="StrataConnectorPicker.ManageCommand"/>.</summary>
    public static readonly StyledProperty<ICommand?> McpManageCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(McpManageCommand));

    /// <summary>True while the host is still discovering MCP servers, forwarded to <see cref="StrataConnectorPicker.IsLoading"/>.</summary>
    public static readonly StyledProperty<bool> McpIsLoadingProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(McpIsLoading));

    /// <summary>Items source for the quality/effort selector ComboBox.</summary>
    public static readonly StyledProperty<IEnumerable?> QualityLevelsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(QualityLevels));

    /// <summary>Currently selected quality level from <see cref="QualityLevels"/>.</summary>
    public static readonly StyledProperty<object?> SelectedQualityProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedQuality));

    /// <summary>Items source for context window size/tier selector.</summary>
    public static readonly StyledProperty<IEnumerable?> ContextWindowTiersProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(ContextWindowTiers));

    /// <summary>Currently selected context window tier from <see cref="ContextWindowTiers"/>.</summary>
    public static readonly StyledProperty<object?> SelectedContextWindowTierProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedContextWindowTier));

    /// <summary>Items source for the session mode selector ComboBox (e.g. Ask, Plan, Agent).</summary>
    public static readonly StyledProperty<IEnumerable?> ModesProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(Modes));

    /// <summary>Currently selected session mode from <see cref="Modes"/>.</summary>
    public static readonly StyledProperty<object?> SelectedModeProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedMode));

    /// <summary>When true, the send button becomes a stop button.</summary>
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsBusy));

    /// <summary>When true (default), submitting while <see cref="IsBusy"/> steers the running turn:
    /// the draft is sent via <see cref="SendCommand"/> WITHOUT invoking <see cref="StopCommand"/>, so
    /// the in-flight turn keeps running and the message is injected into it. When false, submitting
    /// while busy sends the draft and then stops the current turn (the legacy stop-and-send).</summary>
    public static readonly StyledProperty<bool> SteerWhileBusyProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(SteerWhileBusy), true);

    /// <summary>When true, Enter sends and Shift+Enter inserts newline. When false, Ctrl+Enter sends.</summary>
    public static readonly StyledProperty<bool> SendWithEnterProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(SendWithEnter), true);

    /// <summary>Whether the + menu offers files.</summary>
    public static readonly StyledProperty<bool> CanAttachProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(CanAttach), true);

    /// <summary>Whether the send button dictates while there is nothing to send (on the desktop). False
    /// on platforms without a speech recognizer. Defaults to true.</summary>
    public static readonly StyledProperty<bool> CanVoiceProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(CanVoice), true);

    /// <summary>When true, the send command can execute even if <see cref="PromptText"/> is empty.</summary>
    public static readonly StyledProperty<bool> CanSendWithoutPromptProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(CanSendWithoutPrompt));

    /// <summary>When true, the composer is editing an existing turn. Editing takes precedence over
    /// busy/steering semantics: submit executes <see cref="SendCommand"/>, and Escape executes
    /// <see cref="CancelEditCommand"/> after any open autocomplete popup has handled Escape first.</summary>
    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsEditing));

    /// <summary>Command executed when Escape cancels the host-owned edit state.</summary>
    public static readonly StyledProperty<ICommand?> CancelEditCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(CancelEditCommand));

    /// <summary>Accessible label for the edit submit action.</summary>
    public static readonly StyledProperty<string> EditingSubmitLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(EditingSubmitLabel), "Update & resend");

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

    /// <summary>Stable backing value for the active agent when its display name is not unique.</summary>
    public static readonly StyledProperty<string?> AgentValueProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(AgentValue));

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
    /// The MCP servers that are on, as <see cref="StrataComposerChip"/>s, forwarded to
    /// <see cref="StrataConnectorPicker.SelectedItems"/>: an <see cref="IList"/> the picker edits directly.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> McpItemsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(McpItems));

    /// <summary>Display name of the currently active project. Empty or null hides the chip.</summary>
    public static readonly StyledProperty<string?> ProjectNameProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(ProjectName));

    /// <summary>Stable backing value for the active project when its display name is not unique.</summary>
    public static readonly StyledProperty<string?> ProjectValueProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(ProjectValue));

    /// <summary>Catalog of projects available for selection via $ autocomplete.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableProjectsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableProjects));

    /// <summary>Every MCP server the host offers, forwarded to <see cref="StrataConnectorPicker.Items"/>.</summary>
    public static readonly StyledProperty<IEnumerable?> AvailableMcpsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableMcps));

    /// <summary>
    /// Items shown in the # file autocomplete popup.
    /// Each item should be a <see cref="StrataComposerChip"/> where Name is the display path
    /// and Glyph is the file icon (e.g. "📄"). Set this in response to <see cref="FileQueryChanged"/>.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> AvailableFilesProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableFiles));

    /// <summary>
    /// Other chats brought into the next message, shown as chips. Must be an <see cref="IList"/>: the
    /// composer adds and removes chips in it directly. Chats are told apart by
    /// <see cref="StrataComposerChip.Value"/> (for example the chat id), since titles can repeat.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> ChatItemsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(ChatItems));

    /// <summary>
    /// Chats the user can bring into a message, most relevant first, offered by the Chats picker and
    /// above the files in the # autocomplete. Null hides both, for hosts without chat history.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> AvailableChatsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(AvailableChats));

    /// <summary>
    /// Executed before chats are offered — when the Chats picker opens or a # reference starts — so the
    /// host can refresh <see cref="AvailableChats"/> only when it is about to be seen.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ChatsRequestedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(ChatsRequestedCommand));

    /// <summary>Opens the host's skill settings from the Skills picker. Hidden while null.</summary>
    public static readonly StyledProperty<ICommand?> SkillsManageCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(SkillsManageCommand));

    /// <summary>The + button's tooltip and accessible name.</summary>
    public static readonly StyledProperty<string> AddButtonToolTipProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddButtonToolTip), "Add files, chats and more");

    /// <summary>The + menu row that attaches files (through <see cref="AttachCommand"/>).</summary>
    public static readonly StyledProperty<string> AddFilesLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddFilesLabel), "Files & photos");

    /// <summary>Heading of the most recent chats, listed in the + menu itself.</summary>
    public static readonly StyledProperty<string> AddRecentChatsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddRecentChatsLabel), "Recent chats");

    /// <summary>The + menu row that lists every chat to bring in.</summary>
    public static readonly StyledProperty<string> AddAllChatsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddAllChatsLabel), "All chats");

    public static readonly StyledProperty<string> AddSkillsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddSkillsLabel), "Skills");

    public static readonly StyledProperty<string> AddAgentsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddAgentsLabel), "Agents");

    public static readonly StyledProperty<string> AddConnectorsLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddConnectorsLabel), "Connectors");

    /// <summary>How many connectors are on, beside Connectors in the + menu: {0} is the count.</summary>
    public static readonly StyledProperty<string> AddConnectorsCountFormatProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddConnectorsCountFormat), "{0} on");

    public static readonly StyledProperty<string> AddProjectLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(AddProjectLabel), "Project");

    /// <summary>The send button's name while it would start dictation.</summary>
    public static readonly StyledProperty<string> DictateLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(DictateLabel), "Dictate");

    /// <summary>The send button's name while dictation is listening.</summary>
    public static readonly StyledProperty<string> StopDictatingLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(StopDictatingLabel), "Stop dictating");

    /// <summary>A tip at the foot of the + menu, such as the characters that reach the same lists while typing.</summary>
    public static readonly StyledProperty<string?> AddMenuHintProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(AddMenuHint), "Or type # for chats and files, / for skills, @ for agents");

    /// <summary>Optional content displayed between the text input and the toolbar (e.g. coding project toolbar, status bar).</summary>
    public static readonly StyledProperty<object?> StatusContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(StatusContent));

    /// <summary>Optional content for displaying pending file attachments inside the composer.</summary>
    public static readonly StyledProperty<object?> AttachmentContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(AttachmentContent));

    /// <summary>
    /// Optional replacement for the templated text editor. When present, the built-in TextBox is
    /// hidden but remains bound so existing composer state and autocomplete plumbing stay intact.
    /// </summary>
    public static readonly StyledProperty<object?> EditorContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(EditorContent));

    /// <summary>
    /// Temporarily hides a host-supplied editor while retaining a read-only rendering of the draft.
    /// The editor instance stays attached so native input can resume without recreation.
    /// </summary>
    public static readonly StyledProperty<bool> IsEditorContentVisibleProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsEditorContentVisible), true);

    /// <summary>
    /// Presents the mobile composer as a single input row, retaining context chips and Send/Stop.
    /// Hosts control this presentation state from their editor's focus, including native input.
    /// The default desktop presentation is unchanged.
    /// </summary>
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(IsCompact));

    /// <summary>
    /// Places autocomplete above the editor without changing the editor implementation. Mobile
    /// browser hosts use the built-in Avalonia TextBox but still need to stay clear of the keyboard.
    /// </summary>
    public static readonly StyledProperty<bool> PreferAutoCompleteAboveProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(PreferAutoCompleteAbove));

    /// <summary>
    /// Optional host content placed at the start of the toolbar row, before the built-in buttons.
    ///
    /// <para>The built-in toolbar is a desktop shape: an inline model dropdown, a modes combo and
    /// an MCP popup, all sized for a cursor. A host that needs different affordances — a phone that
    /// routes model selection through a bottom sheet, for instance — can hide those and supply its
    /// own here, rather than forking the template to move one control.</para>
    /// </summary>
    public static readonly StyledProperty<object?> ToolbarContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(ToolbarContent));

    /// <summary>Optional leading action, retained alongside Send in the compact mobile layout.</summary>
    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(LeadingContent));

    /// <summary>
    /// Additional clipboard data formats the host wants to handle itself before
    /// the composer falls back to inserting plain text.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> ClipboardPasteInterceptFormatsProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(ClipboardPasteInterceptFormats));

    /// <summary>Raised when the user sends a prompt (Enter key or send button click).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> SendRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(SendRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user clicks the stop button during a busy state.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> StopRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(StopRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user asks to stop the running turn and immediately send the draft as a
    /// fresh turn (abort + send). Distinct from steering, which injects into the running turn.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> StopAndSendRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(StopAndSendRequested), RoutingStrategies.Bubble);

    /// <summary>Raised when the user chooses to attach files from the + menu.</summary>
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

    /// <summary>Raised after the user removes a chat from the next message (its chip, or the Chats picker).</summary>
    public static readonly RoutedEvent<ComposerChipRemovedEventArgs> ChatRemovedEvent =
        RoutedEvent.Register<StrataChatComposer, ComposerChipRemovedEventArgs>(nameof(ChatRemoved), RoutingStrategies.Bubble);

    /// <summary>Raised when the user starts an @ mention from the + menu, in a template without an agent list.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> MentionRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(MentionRequested), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when the user starts or stops dictation from the send button: it dictates while there is
    /// nothing to send (and <see cref="CanVoice"/>), and finishes dictation while <see cref="IsRecording"/>.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> VoiceRequestedEvent =
        RoutedEvent.Register<StrataChatComposer, RoutedEventArgs>(nameof(VoiceRequested), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when the user pastes (Ctrl+V) and the clipboard contains an image,
    /// files, or a host-configured data format. Hosts can handle this to attach
    /// clipboard content to the current message.
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

    /// <summary>Command executed when the user chooses "stop &amp; send": abort the running turn and send
    /// the current draft as a fresh turn. Exposed as a secondary action while steering is the primary one.</summary>
    public static readonly StyledProperty<ICommand?> StopAndSendCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(StopAndSendCommand));

    /// <summary>Optional parameter for <see cref="StopAndSendCommand"/>. Defaults to the committed prompt text.</summary>
    public static readonly StyledProperty<object?> StopAndSendCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(StopAndSendCommandParameter));

    /// <summary>Command executed when the user clicks the attach button.</summary>
    public static readonly StyledProperty<ICommand?> AttachCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(AttachCommand));
    /// <summary>
    /// Command executed each time the embedded model picker popup opens. Hosts can use it to lazily
    /// refresh <see cref="Models"/> so newly available models appear without recreating the composer.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ModelPickerOpenedCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(ModelPickerOpenedCommand));

    /// <summary>Optional parameter for <see cref="ModelPickerOpenedCommand"/>.</summary>
    public static readonly StyledProperty<object?> ModelPickerOpenedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(ModelPickerOpenedCommandParameter));

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
        IsKeyboardFocusWithinProperty.Changed.AddClassHandler<StrataChatComposer>(
            static (c, _) => c.UpdateEngaged());
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>(
            static (popup, e) => OwningComposer(popup)?.OnOwnPopupOpenChanged(popup, e.NewValue is true));
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
        IsCompactProperty.Changed.AddClassHandler<StrataChatComposer>(
            static (c, _) => c.PseudoClasses.Set(":compact", c.IsCompact));
        IsEditingProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SteerWhileBusyProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SendWithEnterProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        CanAttachProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        CanVoiceProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        CanSendWithoutPromptProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        IsRecordingProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        DictateLabelProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        StopDictatingLabelProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        IsSuggestionsGeneratingProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.Sync();
            c.AnimateSuggestionsIfNeeded();
        });
        EditorContentProperty.Changed.AddClassHandler<StrataChatComposer>((c, e) =>
        {
            c.PseudoClasses.Set(":external-editor", e.NewValue is not null);
            c.CloseAutoComplete();
            c.UpdateAutoCompletePlacementTarget();
        });
        IsEditorContentVisibleProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.PseudoClasses.Set(":editor-content-hidden", !c.IsEditorContentVisible);
            if (!c.IsEditorContentVisible)
                c.CloseAutoComplete();
        });
        PreferAutoCompleteAboveProperty.Changed.AddClassHandler<StrataChatComposer>(
            static (c, _) => c.UpdateAutoCompletePlacementTarget());
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
        AgentNameProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) =>
        {
            c.ForgetStaleAgentValue();
            c.Sync();
        });
        AgentGlyphProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        ProjectNameProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        SkillItemsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnSkillItemsChanged());
        ChatItemsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnChatItemsChanged());
        AvailableSkillsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AvailableChatsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AvailableAgentsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AvailableProjectsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AvailableMcpsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.OnAvailableMcpsChanged());
        McpBrowseCommandProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.Sync());
        AvailableFilesProperty.Changed.AddClassHandler<StrataChatComposer>(
            (c, _) => c.OnAvailableFilesChanged());
        ModelsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => { c.EnsureSelectedValues(); c.RefreshModelPickerIfOpen(); });
        QualityLevelsProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => { c.EnsureSelectedValues(); c.RefreshModelPickerEffortIfOpen(); });
        SelectedModelProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.RefreshModelPickerSelectionIfOpen());
        SelectedQualityProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.RefreshModelPickerQualityIfOpen());
        ContextWindowTiersProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => { c.EnsureSelectedValues(); c.RefreshModelPickerContextIfOpen(); });
        SelectedContextWindowTierProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.RefreshModelPickerContextSelectionIfOpen());
        ModesProperty.Changed.AddClassHandler<StrataChatComposer>((c, _) => c.EnsureSelectedValues());
    }

    public StrataChatComposer()
    {
        _focusSweepVisibility = new EffectiveVisibilityObserver(this, UpdateFocusSweep);
        Classes.CollectionChanged += (_, e) =>
        {
            // Pseudo-classes live in Classes too; only the host's classes (glass, mobile, motion-disabled)
            // change anything here.
            if (!ChangesHostClass(e))
                return;

            UpdateFocusSweep();
            UpdateFocusDepth();
            Sync();
        };

        if (Models is null)
            Models = DefaultModels;

        if (QualityLevels is null)
            QualityLevels = DefaultQualityLevels;

        EnsureSelectedValues();
    }

    private static bool ChangesHostClass(NotifyCollectionChangedEventArgs e)
        => e.Action == NotifyCollectionChangedAction.Reset
           || HasHostClass(e.NewItems)
           || HasHostClass(e.OldItems);

    private static bool HasHostClass(IList? classes)
    {
        if (classes is null)
            return false;

        foreach (var name in classes)
        {
            if (name is string { Length: > 0 } text && text[0] != ':')
                return true;
        }

        return false;
    }

    public event EventHandler<RoutedEventArgs>? SendRequested
    { add => AddHandler(SendRequestedEvent, value); remove => RemoveHandler(SendRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? StopRequested
    { add => AddHandler(StopRequestedEvent, value); remove => RemoveHandler(StopRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? StopAndSendRequested
    { add => AddHandler(StopAndSendRequestedEvent, value); remove => RemoveHandler(StopAndSendRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? AttachRequested
    { add => AddHandler(AttachRequestedEvent, value); remove => RemoveHandler(AttachRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? AgentRemoved
    { add => AddHandler(AgentRemovedEvent, value); remove => RemoveHandler(AgentRemovedEvent, value); }
    public event EventHandler<ComposerChipRemovedEventArgs>? SkillRemoved
    { add => AddHandler(SkillRemovedEvent, value); remove => RemoveHandler(SkillRemovedEvent, value); }
    public event EventHandler<ComposerChipRemovedEventArgs>? McpRemoved
    { add => AddHandler(McpRemovedEvent, value); remove => RemoveHandler(McpRemovedEvent, value); }
    public event EventHandler<ComposerChipRemovedEventArgs>? ChatRemoved
    { add => AddHandler(ChatRemovedEvent, value); remove => RemoveHandler(ChatRemovedEvent, value); }
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
    public IDataTemplate? SelectedModelTemplate { get => GetValue(SelectedModelTemplateProperty); set => SetValue(SelectedModelTemplateProperty, value); }
    public ICommand? ModelPinCommand { get => GetValue(ModelPinCommandProperty); set => SetValue(ModelPinCommandProperty, value); }
    public string? ModelPinToolTip { get => GetValue(ModelPinToolTipProperty); set => SetValue(ModelPinToolTipProperty, value); }
    public string? ModelUnpinToolTip { get => GetValue(ModelUnpinToolTipProperty); set => SetValue(ModelUnpinToolTipProperty, value); }
    public string McpToolTip { get => GetValue(McpToolTipProperty); set => SetValue(McpToolTipProperty, value); }
    public string McpHeader { get => GetValue(McpHeaderProperty); set => SetValue(McpHeaderProperty, value); }
    public ICommand? McpErrorActionCommand { get => GetValue(McpErrorActionCommandProperty); set => SetValue(McpErrorActionCommandProperty, value); }
    public string? McpErrorActionLabel { get => GetValue(McpErrorActionLabelProperty); set => SetValue(McpErrorActionLabelProperty, value); }
    public string McpErrorDetailsLabel { get => GetValue(McpErrorDetailsLabelProperty); set => SetValue(McpErrorDetailsLabelProperty, value); }
    public string McpCopyErrorDetailsLabel { get => GetValue(McpCopyErrorDetailsLabelProperty); set => SetValue(McpCopyErrorDetailsLabelProperty, value); }
    public ICommand? McpBrowseCommand { get => GetValue(McpBrowseCommandProperty); set => SetValue(McpBrowseCommandProperty, value); }
    public ICommand? McpManageCommand { get => GetValue(McpManageCommandProperty); set => SetValue(McpManageCommandProperty, value); }
    public bool McpIsLoading { get => GetValue(McpIsLoadingProperty); set => SetValue(McpIsLoadingProperty, value); }

    public IEnumerable? QualityLevels{ get => GetValue(QualityLevelsProperty); set => SetValue(QualityLevelsProperty, value); }
    public object? SelectedQuality { get => GetValue(SelectedQualityProperty); set => SetValue(SelectedQualityProperty, value); }
    public IEnumerable? ContextWindowTiers { get => GetValue(ContextWindowTiersProperty); set => SetValue(ContextWindowTiersProperty, value); }
    public object? SelectedContextWindowTier { get => GetValue(SelectedContextWindowTierProperty); set => SetValue(SelectedContextWindowTierProperty, value); }
    public IEnumerable? Modes { get => GetValue(ModesProperty); set => SetValue(ModesProperty, value); }
    public object? SelectedMode { get => GetValue(SelectedModeProperty); set => SetValue(SelectedModeProperty, value); }
    public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public bool SteerWhileBusy { get => GetValue(SteerWhileBusyProperty); set => SetValue(SteerWhileBusyProperty, value); }
    public bool SendWithEnter { get => GetValue(SendWithEnterProperty); set => SetValue(SendWithEnterProperty, value); }
    public bool CanAttach { get => GetValue(CanAttachProperty); set => SetValue(CanAttachProperty, value); }
    public bool CanVoice { get => GetValue(CanVoiceProperty); set => SetValue(CanVoiceProperty, value); }
    public bool CanSendWithoutPrompt { get => GetValue(CanSendWithoutPromptProperty); set => SetValue(CanSendWithoutPromptProperty, value); }
    public string SuggestionA { get => GetValue(SuggestionAProperty); set => SetValue(SuggestionAProperty, value); }
    public string SuggestionB { get => GetValue(SuggestionBProperty); set => SetValue(SuggestionBProperty, value); }
    public string SuggestionC { get => GetValue(SuggestionCProperty); set => SetValue(SuggestionCProperty, value); }
    public string? AgentName { get => GetValue(AgentNameProperty); set => SetValue(AgentNameProperty, value); }
    public string? AgentValue { get => GetValue(AgentValueProperty); set => SetValue(AgentValueProperty, value); }
    public string AgentGlyph { get => GetValue(AgentGlyphProperty); set => SetValue(AgentGlyphProperty, value); }
    public IEnumerable? SkillItems { get => GetValue(SkillItemsProperty); set => SetValue(SkillItemsProperty, value); }
    public IEnumerable? AvailableAgents { get => GetValue(AvailableAgentsProperty); set => SetValue(AvailableAgentsProperty, value); }
    public IEnumerable? AvailableSkills { get => GetValue(AvailableSkillsProperty); set => SetValue(AvailableSkillsProperty, value); }
    public IEnumerable? McpItems { get => GetValue(McpItemsProperty); set => SetValue(McpItemsProperty, value); }
    public IEnumerable? AvailableMcps { get => GetValue(AvailableMcpsProperty); set => SetValue(AvailableMcpsProperty, value); }
    public string? ProjectName { get => GetValue(ProjectNameProperty); set => SetValue(ProjectNameProperty, value); }
    public string? ProjectValue { get => GetValue(ProjectValueProperty); set => SetValue(ProjectValueProperty, value); }
    public IEnumerable? AvailableProjects { get => GetValue(AvailableProjectsProperty); set => SetValue(AvailableProjectsProperty, value); }
    public IEnumerable? AvailableFiles { get => GetValue(AvailableFilesProperty); set => SetValue(AvailableFilesProperty, value); }
    public IEnumerable? ChatItems { get => GetValue(ChatItemsProperty); set => SetValue(ChatItemsProperty, value); }
    public IEnumerable? AvailableChats { get => GetValue(AvailableChatsProperty); set => SetValue(AvailableChatsProperty, value); }
    public ICommand? ChatsRequestedCommand { get => GetValue(ChatsRequestedCommandProperty); set => SetValue(ChatsRequestedCommandProperty, value); }
    public ICommand? SkillsManageCommand { get => GetValue(SkillsManageCommandProperty); set => SetValue(SkillsManageCommandProperty, value); }
    public string AddButtonToolTip { get => GetValue(AddButtonToolTipProperty); set => SetValue(AddButtonToolTipProperty, value); }
    public string AddFilesLabel { get => GetValue(AddFilesLabelProperty); set => SetValue(AddFilesLabelProperty, value); }
    public string AddRecentChatsLabel { get => GetValue(AddRecentChatsLabelProperty); set => SetValue(AddRecentChatsLabelProperty, value); }
    public string AddAllChatsLabel { get => GetValue(AddAllChatsLabelProperty); set => SetValue(AddAllChatsLabelProperty, value); }
    public string AddSkillsLabel { get => GetValue(AddSkillsLabelProperty); set => SetValue(AddSkillsLabelProperty, value); }
    public string AddAgentsLabel { get => GetValue(AddAgentsLabelProperty); set => SetValue(AddAgentsLabelProperty, value); }
    public string AddConnectorsLabel { get => GetValue(AddConnectorsLabelProperty); set => SetValue(AddConnectorsLabelProperty, value); }
    public string AddConnectorsCountFormat { get => GetValue(AddConnectorsCountFormatProperty); set => SetValue(AddConnectorsCountFormatProperty, value); }
    public string AddProjectLabel { get => GetValue(AddProjectLabelProperty); set => SetValue(AddProjectLabelProperty, value); }
    public string? AddMenuHint { get => GetValue(AddMenuHintProperty); set => SetValue(AddMenuHintProperty, value); }
    public string DictateLabel { get => GetValue(DictateLabelProperty); set => SetValue(DictateLabelProperty, value); }
    public string StopDictatingLabel { get => GetValue(StopDictatingLabelProperty); set => SetValue(StopDictatingLabelProperty, value); }
    public bool IsRecording { get => GetValue(IsRecordingProperty); set => SetValue(IsRecordingProperty, value); }
    public bool IsEditing { get => GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }
    public ICommand? CancelEditCommand { get => GetValue(CancelEditCommandProperty); set => SetValue(CancelEditCommandProperty, value); }
    public string EditingSubmitLabel { get => GetValue(EditingSubmitLabelProperty); set => SetValue(EditingSubmitLabelProperty, value); }
    public object? StatusContent { get => GetValue(StatusContentProperty); set => SetValue(StatusContentProperty, value); }
    public object? AttachmentContent { get => GetValue(AttachmentContentProperty); set => SetValue(AttachmentContentProperty, value); }
    public object? EditorContent { get => GetValue(EditorContentProperty); set => SetValue(EditorContentProperty, value); }
    public bool IsEditorContentVisible { get => GetValue(IsEditorContentVisibleProperty); set => SetValue(IsEditorContentVisibleProperty, value); }
    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public bool PreferAutoCompleteAbove { get => GetValue(PreferAutoCompleteAboveProperty); set => SetValue(PreferAutoCompleteAboveProperty, value); }
    public object? ToolbarContent { get => GetValue(ToolbarContentProperty); set => SetValue(ToolbarContentProperty, value); }
    public object? LeadingContent { get => GetValue(LeadingContentProperty); set => SetValue(LeadingContentProperty, value); }
    public IEnumerable? ClipboardPasteInterceptFormats { get => GetValue(ClipboardPasteInterceptFormatsProperty); set => SetValue(ClipboardPasteInterceptFormatsProperty, value); }

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
    public ICommand? StopAndSendCommand { get => GetValue(StopAndSendCommandProperty); set => SetValue(StopAndSendCommandProperty, value); }
    public object? StopAndSendCommandParameter { get => GetValue(StopAndSendCommandParameterProperty); set => SetValue(StopAndSendCommandParameterProperty, value); }
    public ICommand? AttachCommand { get => GetValue(AttachCommandProperty); set => SetValue(AttachCommandProperty, value); }
    public object? AttachCommandParameter { get => GetValue(AttachCommandParameterProperty); set => SetValue(AttachCommandParameterProperty, value); }
    public ICommand? ModelPickerOpenedCommand { get => GetValue(ModelPickerOpenedCommandProperty); set => SetValue(ModelPickerOpenedCommandProperty, value); }
    public object? ModelPickerOpenedCommandParameter { get => GetValue(ModelPickerOpenedCommandParameterProperty); set => SetValue(ModelPickerOpenedCommandParameterProperty, value); }
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _focusSweepVisibility.Subscribe();
        OnAvailableFilesChanged();
        // Detaching drops the chip collections' handlers; follow them again, and catch up on changes.
        OnSkillItemsChanged();
        OnChatItemsChanged();
        UpdateEngaged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseAddMenu();
        _openOwnPopups.Clear();
        _engagedHold = false;
        PseudoClasses.Set(":engaged", false);
        _focusSweepVisibility.Unsubscribe();
        StopFocusSweep();
        _focusSweepPlayed = false;

        if (_input is not null)
        {
            _input.RemoveHandler(KeyDownEvent, OnInputKeyDown);
            _input.TextChanged -= OnInputTextChanged;
        }
        if (_autoCompletePopup is not null)
            _autoCompletePopup.Closed -= OnAutoCompletePopupClosed;

        if (_subscribedSkillCollection is not null)
        {
            _subscribedSkillCollection.CollectionChanged -= OnSkillCollectionChanged;
            _subscribedSkillCollection = null;
        }

        if (_subscribedChatCollection is not null)
        {
            _subscribedChatCollection.CollectionChanged -= OnChatCollectionChanged;
            _subscribedChatCollection = null;
        }

        if (_subscribedAvailableMcpCollection is not null)
        {
            _subscribedAvailableMcpCollection.CollectionChanged -= OnAvailableMcpCollectionChanged;
            _subscribedAvailableMcpCollection = null;
        }
        if (_subscribedAvailableFileCollection is not null)
        {
            _subscribedAvailableFileCollection.CollectionChanged -= OnAvailableFileCollectionChanged;
            _subscribedAvailableFileCollection = null;
        }
        _fileAutoCompleteRefreshQueued = false;
        // The visual goes with the tree; the next attach brings a new one.
        _depthVisual = null;
        _depthScale = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        StopFocusSweep();
        _focusSweepPlayed = false;

        if (_input is not null)
        {
            _input.RemoveHandler(KeyDownEvent, OnInputKeyDown);
            _input.TextChanged -= OnInputTextChanged;
        }
        if (_autoCompletePopup is not null)
            _autoCompletePopup.Closed -= OnAutoCompletePopupClosed;

        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        _focusSweep = e.NameScope.Find<Border>("PART_FocusSweep");
        _sendButton = e.NameScope.Find<Button>("PART_SendButton");
        if (_input is not null)
        {
            _input.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _input.TextChanged += OnInputTextChanged;
            _input.ContextMenu = BuildInputContextMenu(_input);
        }

        Wire(e, "PART_SendButton", HandleSendButton);
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
        if (_skillPicker is not null)
            _skillPicker.ItemToggled -= OnSkillPickerToggled;
        if (_chatPicker is not null)
        {
            _chatPicker.ItemToggled -= OnChatPickerToggled;
            _chatPicker.DropDownOpening -= OnChatPickerOpening;
            _chatPicker.DropDownClosed -= OnAddListClosed;
        }
        _skillPicker = e.NameScope.Find<StrataComposerPicker>("PART_SkillPicker");
        _chatPicker = e.NameScope.Find<StrataComposerPicker>("PART_ChatPicker");
        if (_skillPicker is not null)
            _skillPicker.ItemToggled += OnSkillPickerToggled;
        if (_chatPicker is not null)
        {
            _chatPicker.ItemToggled += OnChatPickerToggled;
            _chatPicker.DropDownOpening += OnChatPickerOpening;
            _chatPicker.DropDownClosed += OnAddListClosed;
        }
        ApplyAddMenuTemplate(e);
        _modelPickerPopup = e.NameScope.Find<Popup>("PART_ModelPickerPopup");
        _modelPickerList = e.NameScope.Find<StackPanel>("PART_ModelPickerList");
        _modelPickerChevron = e.NameScope.Find<Avalonia.Controls.Shapes.Path>("PART_ModelPickerChevron");
        _modelPickerChevronWrap = e.NameScope.Find<Border>("PART_ModelPickerChevronWrap");
        _effortSection = e.NameScope.Find<StackPanel>("PART_EffortSection");
        _contextWindowSection = e.NameScope.Find<StackPanel>("PART_ContextWindowSection");
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
        _autoCompleteScrollViewer = e.NameScope.Find<ScrollViewer>("PART_AutoCompleteScrollViewer");
        if (_autoCompletePopup is not null)
        {
            UpdateAutoCompletePlacementTarget();
            _autoCompletePopup.Closed += OnAutoCompletePopupClosed;
        }
        RebuildContextChips();
        Wire(e, "PART_ActionA", () => Fire(SuggestionA));
        Wire(e, "PART_ActionB", () => Fire(SuggestionB));
        Wire(e, "PART_ActionC", () => Fire(SuggestionC));
        _actionA = e.NameScope.Find<Button>("PART_ActionA");
        _actionB = e.NameScope.Find<Button>("PART_ActionB");
        _actionC = e.NameScope.Find<Button>("PART_ActionC");
        _hadSuggestions = HasAnySuggestions();
        EnsureSelectedValues();
        Sync();
        _openOwnPopups.Clear();
        UpdateEngaged();
    }

    /// <summary>
    /// Whether the user is writing here: the keyboard is in the composer, or in one of its own lists (which
    /// open in the overlay layer, outside its visual tree), or such a list has just closed and is handing the
    /// keyboard back. Shown as <c>:engaged</c>; the focus line's sweep and the focus depth follow it, so using
    /// the + menu or a picker neither sends the composer back nor replays the line.
    /// </summary>
    private bool IsEngaged => IsKeyboardFocusWithin || _openOwnPopups.Count > 0 || _engagedHold;

    private void UpdateEngaged()
    {
        PseudoClasses.Set(":engaged", IsEngaged);
        UpdateFocusSweep();
        UpdateFocusDepth();
    }

    /// <summary>
    /// The composer whose template, or a picker in it, holds <paramref name="popup"/>. A context menu's popup
    /// belongs to the control it opened on (the input's, say), so it is followed from there.
    /// </summary>
    private static StrataChatComposer? OwningComposer(Popup popup)
    {
        var owner = popup.TemplatedParent ?? popup.Parent;
        for (var depth = 0; owner is not null && depth < 3; depth++)
        {
            if (owner is StrataChatComposer composer)
                return composer;
            owner = (owner as StyledElement)?.TemplatedParent;
        }

        return null;
    }

    private void OnOwnPopupOpenChanged(Popup popup, bool open)
    {
        if (open)
        {
            _openOwnPopups.Add(popup);
        }
        else if (_openOwnPopups.Remove(popup) && !IsKeyboardFocusWithin)
        {
            // A list that closes after a choice hands the keyboard back on a later dispatcher turn (see
            // FocusInput); wait for it rather than sending the composer back and replaying the line in between.
            _engagedHold = true;
            if (!_engagedHoldCheckQueued)
            {
                _engagedHoldCheckQueued = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _engagedHoldCheckQueued = false;
                    _engagedHold = false;
                    UpdateEngaged();
                }, DispatcherPriority.Background);
            }
        }

        UpdateEngaged();
    }

    // Glass keeps its line while the user writes, the composer's own lists included; the plain composer's line
    // follows keyboard focus, so it replays whenever focus comes back.
    private bool CanAnimateFocusSweep =>
        _focusSweep is not null
        && this.IsAttachedToVisualTree()
        && IsEffectivelyVisible
        && (Classes.Contains("glass") ? IsEngaged : IsKeyboardFocusWithin)
        && !Classes.Contains("motion-disabled");

    private void UpdateFocusSweep()
    {
        if (!CanAnimateFocusSweep)
        {
            StopFocusSweep();
            _focusSweepPlayed = false;
            return;
        }

        if (_focusSweepPlayed)
            return;

        _focusSweepPlayed = true;
        // Match the focus line's draw transition before starting the highlight.
        _focusSweepTimer = DispatcherTimer.RunOnce(
            StartFocusSweep, TimeSpan.FromMilliseconds(400), DispatcherPriority.Render);
    }

    private void StartFocusSweep()
    {
        _focusSweepTimer = null;
        if (!CanAnimateFocusSweep || _focusSweep is not { } sweep)
        {
            UpdateFocusSweep();
            return;
        }

        sweep.SetCurrentValue(OpacityProperty, 0.6d);
        LifecycleOffsetSweep.SetIsActive(sweep, true);
        _focusSweepTimer = DispatcherTimer.RunOnce(
            StopFocusSweep, LifecycleOffsetSweep.GetDuration(sweep), DispatcherPriority.Render);
    }

    private void StopFocusSweep()
    {
        _focusSweepTimer?.Dispose();
        _focusSweepTimer = null;
        if (_focusSweep is not { } sweep)
            return;

        sweep.SetCurrentValue(OpacityProperty, 0d);
        LifecycleOffsetSweep.SetIsActive(sweep, false);
    }

    /// <summary>
    /// Programmatically focuses the text input area.
    /// </summary>
    public void FocusInput()
    {
        if (EditorContent is IStrataComposerEditor editor)
        {
            Dispatcher.UIThread.Post(editor.FocusAtEnd, DispatcherPriority.Loaded);
            return;
        }

        Dispatcher.UIThread.Post(() => _input?.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Focuses the text input and places the caret at the end of the current prompt.</summary>
    public void FocusInputAtEnd()
    {
        if (EditorContent is IStrataComposerEditor editor)
        {
            Dispatcher.UIThread.Post(editor.FocusAtEnd, DispatcherPriority.Loaded);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_input is null)
                return;

            // PromptText is the host's authoritative edit value. Synchronize the templated input
            // before focusing so a pending TwoWay binding update cannot clamp the caret to index 0.
            var text = PromptText ?? string.Empty;
            if (!string.Equals(_input.Text, text, StringComparison.Ordinal))
                _input.Text = text;

            _input.Focus();
            var end = text.Length;
            _input.CaretIndex = end;
            _input.SelectionStart = end;
            _input.SelectionEnd = end;
        }, DispatcherPriority.Loaded);
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

    /// <summary>Inserts text at the current input selection, or appends if the input is not mounted.</summary>
    public void InsertTextAtSelection(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (EditorContent is IStrataComposerEditor editor)
        {
            var current = PromptText ?? string.Empty;
            var caret = Math.Clamp(editor.CaretIndex, 0, current.Length);
            PromptText = current.Insert(caret, text);
            editor.FocusAt(caret + text.Length);
            return;
        }

        if (_input is null)
        {
            PromptText = (PromptText ?? string.Empty) + text;
            return;
        }

        InsertTextAtSelection(_input, text);
    }

    private ContextMenu BuildInputContextMenu(TextBox textBox)
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

        void DoSelectAll()
        {
            textBox.SelectionStart = 0;
            textBox.SelectionEnd = textBox.Text?.Length ?? 0;
        }

        cut.Click += (_, _) => DoCut();
        copy.Click += (_, _) => DoCopy();
        paste.Click += async (_, _) => await HandlePasteFromKeyboardAsync();
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

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        CommitInputText();
    }

    private string? CommitInputText()
    {
        var text = EditorContent is null ? _input?.Text ?? PromptText : PromptText;
        if (!string.Equals(PromptText, text, StringComparison.Ordinal))
            PromptText = text;
        return text;
    }

    private void OnAutoCompletePopupClosed(object? sender, EventArgs e)
    {
        _triggerIndex = -1;
        _autoCompleteSelectedIndex = -1;
        if (!_awaitingReferenceFiles)
            _referenceTriggerIndex = -1;
    }

    private void Fire(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        PromptText = suggestion;
        HandleSendAction();
    }

    // Primary command modifier: Cmd (Meta) on macOS, Ctrl on Windows/Linux — so the composer's
    // send and paste shortcuts feel native on each platform and match Lumi's other key handlers.
    // On Windows/Linux this is exactly the previous Control check, so their behavior is unchanged.
    private static bool HasCommandModifier(KeyModifiers modifiers)
        => OperatingSystem.IsMacOS()
            ? modifiers.HasFlag(KeyModifiers.Meta)
            : modifiers.HasFlag(KeyModifiers.Control);

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && HasCommandModifier(e.KeyModifiers))
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
                    _referenceTriggerIndex = -1;
                    CloseAutoComplete();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Escape && IsEditing)
        {
            e.Handled = true;
            CommandHelper.Execute(CancelEditCommand, null);
            return;
        }

        if (e.Key == Key.Enter)
        {
            var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var isCtrl = HasCommandModifier(e.KeyModifiers);
            var isAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

            // Alt+Enter while effectively busy = abort the running turn and send this as a fresh
            // turn. Host-owned editing takes precedence over all busy/steer keyboard semantics.
            if (isAlt && IsBusy && !IsEditing && !isShift)
            {
                e.Handled = true;
                HandleStopAndSendAction();
                return;
            }

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
            var dataTransfer = await clipboard.TryGetDataAsync().ConfigureAwait(true);
            if (dataTransfer is not null)
            {
                if (await TryRaiseClipboardPasteRequestedAsync(dataTransfer).ConfigureAwait(true))
                    return;
            }

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

    private async Task<bool> TryRaiseClipboardPasteRequestedAsync(IAsyncDataTransfer dataTransfer)
    {
        var shouldRaise = dataTransfer.Formats.Contains(DataFormat.File)
            || ContainsInterceptedClipboardFormat(dataTransfer)
            || ClipboardImage.HasImageFormat(dataTransfer);

        if (!shouldRaise)
        {
            // Use the cross-platform image probe so macOS (public.tiff) is detected too — the built-in
            // TryGetBitmapAsync only decodes PNG/BMP. On Windows/Linux this resolves identically.
            var bitmap = await ClipboardImage.TryGetImageAsync(dataTransfer).ConfigureAwait(true);
            if (bitmap is null)
                return false;

            bitmap.Dispose();
        }

        RaiseEvent(new RoutedEventArgs(ClipboardImagePasteRequestedEvent));
        CommandHelper.Execute(ClipboardPasteCommand, ClipboardPasteCommandParameter);
        return true;
    }

    private bool ContainsInterceptedClipboardFormat(IAsyncDataTransfer dataTransfer)
    {
        if (ClipboardPasteInterceptFormats is null)
            return false;

        foreach (var configured in ClipboardPasteInterceptFormats)
        {
            if (configured is not DataFormat configuredFormat)
                continue;

            foreach (var availableFormat in dataTransfer.Formats)
            {
                if (availableFormat.Equals(configuredFormat))
                    return true;
            }
        }

        return false;
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

    /// <summary>
    /// The send button: finishes dictation while listening, dictates while there is nothing to send
    /// (when the host can listen), and otherwise sends, steers or stops like Enter.
    /// </summary>
    private void HandleSendButton()
    {
        // Decide on what is in the field now, which a pending binding may not have pushed yet.
        CommitInputText();
        if (ShouldDictate())
        {
            RaiseEvent(new RoutedEventArgs(VoiceRequestedEvent));
            CommandHelper.Execute(VoiceCommand, VoiceCommandParameter);
            return;
        }

        HandleSendAction();
    }

    private bool ShouldDictate() =>
        CanVoice
        && !Classes.Contains("mobile")
        && !IsEditing
        && (IsRecording
            || !IsBusy && !CanSendWithoutPrompt && string.IsNullOrWhiteSpace(PromptText));

    private void HandleSendAction()
    {
        var promptText = CommitInputText();
        var hasPromptText = !string.IsNullOrWhiteSpace(promptText);

        if (IsBusy && !IsEditing)
        {
            if (hasPromptText || CanSendWithoutPrompt)
            {
                RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
                CommandHelper.Execute(SendCommand, SendCommandParameter ?? promptText);

                // Steering: the draft is injected into the in-flight turn, so we must NOT stop it.
                if (SteerWhileBusy)
                    return;
            }

            RaiseEvent(new RoutedEventArgs(StopRequestedEvent));
            CommandHelper.Execute(StopCommand, StopCommandParameter);
            return;
        }

        if (!hasPromptText && !CanSendWithoutPrompt) return;
        RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
        CommandHelper.Execute(SendCommand, SendCommandParameter ?? promptText);
    }

    /// <summary>
    /// Abort the running turn and send the draft as a fresh turn (abort + send), instead of steering it
    /// into the running turn. Invoked via the Alt+Enter shortcut. Only meaningful while <see cref="IsBusy"/>;
    /// no-op when there is nothing to send.
    /// </summary>
    private void HandleStopAndSendAction()
    {
        var promptText = CommitInputText();
        var hasPromptText = !string.IsNullOrWhiteSpace(promptText);
        if (!hasPromptText && !CanSendWithoutPrompt) return;

        RaiseEvent(new RoutedEventArgs(StopAndSendRequestedEvent));

        // Prefer a dedicated abort+send command: it should queue the draft and stop the running turn so
        // the host drains the queued draft into a fresh turn.
        if (StopAndSendCommand is not null)
        {
            CommandHelper.Execute(StopAndSendCommand, StopAndSendCommandParameter ?? promptText);
            return;
        }

        // Fallback (no dedicated command bound): reproduce the ":stop-send" primary behavior — queue the
        // draft via Send, then Stop the current turn so the host drains the queued draft as a new turn.
        RaiseEvent(new RoutedEventArgs(SendRequestedEvent));
        CommandHelper.Execute(SendCommand, SendCommandParameter ?? promptText);
        RaiseEvent(new RoutedEventArgs(StopRequestedEvent));
        CommandHelper.Execute(StopCommand, StopCommandParameter);
    }

    // ── Inline autocomplete ────────────────────────────────────────

    private int GetEditorCaretIndex()
    {
        var textLength = PromptText?.Length ?? 0;
        var caret = EditorContent is IStrataComposerEditor editor
            ? editor.CaretIndex
            : _input?.CaretIndex ?? textLength;
        return Math.Clamp(caret, 0, textLength);
    }

    private void FocusEditorAt(int caretIndex)
    {
        var textLength = PromptText?.Length ?? 0;
        var clamped = Math.Clamp(caretIndex, 0, textLength);
        if (EditorContent is IStrataComposerEditor editor)
        {
            editor.FocusAt(clamped);
            return;
        }

        if (_input is null)
            return;
        _input.CaretIndex = clamped;
        _input.Focus();
    }

    private void ShowMentionPopup()
    {
        RaiseEvent(new RoutedEventArgs(MentionRequestedEvent));
        ShowTriggerPopup('@');
    }

    /// <summary>Starts a <paramref name="trigger"/> reference at the caret, as typing it would, and lists its choices.</summary>
    private void ShowTriggerPopup(char trigger)
    {
        if (_input is null && EditorContent is not IStrataComposerEditor)
            return;

        var text = PromptText ?? "";
        var caret = GetEditorCaretIndex();
        var prefix = caret > 0 && caret <= text.Length && text[caret - 1] is not (' ' or '\n' or '\r')
            ? " " + trigger : trigger.ToString();

        _suppressAutoComplete = true;
        PromptText = text.Insert(caret, prefix);
        _suppressAutoComplete = false;

        var newCaret = caret + prefix.Length;
        Dispatcher.UIThread.Post(() =>
        {
            FocusEditorAt(newCaret);
            _triggerIndex = newCaret - 1;
            _triggerChar = trigger;
            ShowAutoCompleteItems("");
        }, DispatcherPriority.Input);
    }

    private void CheckAutoComplete()
    {
        if (_suppressAutoComplete
            || _autoCompletePopup is null
            || (EditorContent is not null && !IsEditorContentVisible)
            || (_input is null && EditorContent is not IStrataComposerEditor))
        {
            return;
        }

        var text = PromptText ?? "";
        var caret = GetEditorCaretIndex();

        if (caret <= 0 || caret > text.Length)
        {
            _referenceTriggerIndex = -1;
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
                        // One request per reference, so the host refreshes its chats before they are
                        // offered rather than on every keystroke.
                        var isNewReference = _referenceTriggerIndex != i;
                        _referenceTriggerIndex = i;
                        if (isNewReference && AvailableChats is not null)
                            CommandHelper.Execute(ChatsRequestedCommand, null);

                        // Fire event so the consumer can populate AvailableFiles.
                        // ShowAutoCompleteItems will be called again when AvailableFiles is set.
                        FileQueryChanged?.Invoke(this, new FileQueryChangedEventArgs(query));
                        CommandHelper.Execute(FileQueryChangedCommand, FileQueryChangedCommandParameter ?? query);

                        // Chats are already here, so they show at once; files join when found.
                        if (AvailableChats is not null)
                            ShowAutoCompleteItems(query);
                    }
                    else
                    {
                        _referenceTriggerIndex = -1;
                        ShowAutoCompleteItems(query);
                    }
                    return;
                }
                break;
            }
        }

        _referenceTriggerIndex = -1;
        CloseAutoComplete();
    }

    private void ShowAutoCompleteItems(string query)
    {
        if (_autoCompletePanel is null || _autoCompletePopup is null)
            return;

        _autoCompletePanel.Children.Clear();
        _autoCompleteEntries.Clear();
        _autoCompleteSelectedIndex = -1;

        switch (_triggerChar)
        {
            case '@':
                AddAutoCompleteEntries(
                    AvailableAgents,
                    ChipKind.Agent,
                    query,
                    chip => !MatchesSelection(chip, AgentName, AgentValue),
                    rankResults: true);
                break;
            case '/':
                AddAutoCompleteEntries(
                    AvailableSkills,
                    ChipKind.Skill,
                    query,
                    chip => !IsAlreadyActiveSkill(chip),
                    rankResults: true);
                break;
            case '$':
                AddAutoCompleteEntries(
                    AvailableProjects,
                    ChipKind.Project,
                    query,
                    chip => !MatchesSelection(chip, ProjectName, ProjectValue),
                    rankResults: true);
                break;
            case '#':
                // Chats lead: they are few, resolved locally, and the reason most people reach for
                // a reference. Files follow in the order the host ranked them.
                AddAutoCompleteEntries(
                    AvailableChats,
                    ChipKind.Chat,
                    query,
                    chip => !IsAlreadyReferencedChat(chip),
                    rankResults: true,
                    maxEntries: string.IsNullOrWhiteSpace(query) ? 4 : 5);
                AddAutoCompleteEntries(
                    AvailableFiles,
                    ChipKind.File,
                    query,
                    _ => true,
                    rankResults: false);
                break;
        }

        if (_autoCompleteEntries.Count == 0)
        {
            // An empty reference list can still fill with files the host is searching for.
            _awaitingReferenceFiles = _triggerChar == '#';
            CloseAutoComplete();
            return;
        }

        _awaitingReferenceFiles = false;
        _autoCompleteSelectedIndex = 0;
        UpdateAutoCompleteHighlight();
        PositionPopupAtTrigger();
        _autoCompletePopup.IsOpen = true;
    }

    private void AddAutoCompleteEntries(
        IEnumerable? items,
        ChipKind kind,
        string query,
        Func<StrataComposerChip, bool> includePredicate,
        bool rankResults,
        int maxEntries = int.MaxValue)
    {
        if (_autoCompletePanel is null || items is null || maxEntries <= 0)
            return;

        var headerAdded = false;
        var chips = rankResults
            ? StrataChatComposerSearch.Rank(items, query, chip => !includePredicate(chip))
            : EnumerateAutoCompleteChips(items)
                .Where(includePredicate)
                .Where(chip => MatchesAutoCompleteQuery(chip, query))
                .ToList();

        foreach (var chip in chips.Take(maxEntries))
        {
            if (!headerAdded)
            {
                _autoCompletePanel.Children.Add(CreateSectionHeader(GetAutoCompleteHeader(kind)));
                headerAdded = true;
            }

            var border = CreateAutoCompleteEntry(chip, kind);
            _autoCompletePanel.Children.Add(border);
            _autoCompleteEntries.Add((border, chip, kind));
        }
    }

    private static IEnumerable<StrataComposerChip> EnumerateAutoCompleteChips(IEnumerable items)
    {
        foreach (var item in items)
            yield return item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");
    }

    internal static bool MatchesAutoCompleteQuery(StrataComposerChip chip, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return StrataChatComposerSearch.ScoreChip(chip, query) > 0;
    }

    private string GetAutoCompleteHeader(ChipKind kind)
    {
        var action = EditorContent is null ? "Enter" : "Tap";
        return kind switch
        {
            ChipKind.Agent => $"Agents · {action} to select",
            ChipKind.Skill => $"Skills · {action} to add",
            ChipKind.Project => $"Projects · {action} to switch",
            ChipKind.Chat => $"Chats · {action} to bring in",
            ChipKind.File => $"Files · {action} to attach",
            ChipKind.Mcp => "MCPs",
            _ => "Suggestions",
        };
    }

    private void PositionPopupAtTrigger()
    {
        if (_autoCompletePopup is null || _triggerIndex < 0)
            return;
        if (EditorContent is not null || PreferAutoCompleteAbove)
        {
            _autoCompletePopup.PlacementRect = default;
            return;
        }
        if (_input is null)
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

    private void UpdateAutoCompletePlacementTarget()
    {
        if (_autoCompletePopup is null)
            return;

        var externalEditor = EditorContent as Control;
        _autoCompletePopup.PlacementTarget = externalEditor ?? _input;
        if (externalEditor is not null || PreferAutoCompleteAbove)
        {
            _autoCompletePopup.Placement = PlacementMode.Top;
            _autoCompletePopup.PlacementConstraintAdjustment =
                PopupPositionerConstraintAdjustment.ResizeY
                | PopupPositionerConstraintAdjustment.SlideX;
            _autoCompletePopup.VerticalOffset = -4;
        }
        else
        {
            _autoCompletePopup.Placement = PlacementMode.AnchorAndGravity;
            _autoCompletePopup.PlacementConstraintAdjustment =
                PopupPositionerConstraintAdjustment.FlipX
                | PopupPositionerConstraintAdjustment.FlipY
                | PopupPositionerConstraintAdjustment.SlideX
                | PopupPositionerConstraintAdjustment.SlideY
                | PopupPositionerConstraintAdjustment.ResizeX
                | PopupPositionerConstraintAdjustment.ResizeY;
            _autoCompletePopup.VerticalOffset = 0;
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

        if (!TryGetActiveTriggerSpan(out var text, out var caret, out var removeLen))
        {
            CloseAutoComplete();
            FocusEditorAt(GetEditorCaretIndex());
            return;
        }

        var focusCaret = caret;

        if (kind == ChipKind.File)
        {
            // Replace #query with #filename inline so the user sees the reference in context
            var fileName = System.IO.Path.GetFileName(chip.Name);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = chip.Name;
            var inlineRef = $"#{fileName} ";
            _suppressAutoComplete = true;
            text = text.Remove(_triggerIndex, removeLen).Insert(_triggerIndex, inlineRef);
            PromptText = text;
            focusCaret = _triggerIndex + inlineRef.Length;
            _suppressAutoComplete = false;

            var filePath = !string.IsNullOrWhiteSpace(chip.Value) ? chip.Value : chip.Glyph;
            FileSelected?.Invoke(this, new FileSelectedEventArgs(filePath));
            CommandHelper.Execute(FileSelectedCommand, FileSelectedCommandParameter ?? filePath);
        }
        else
        {
            if (removeLen > 0)
            {
                var restoreCaret = _triggerIndex;
                _suppressAutoComplete = true;
                PromptText = text.Remove(_triggerIndex, removeLen);
                focusCaret = restoreCaret;
                _suppressAutoComplete = false;
            }

            switch (kind)
            {
                case ChipKind.Agent:
                    AgentValue = chip.Value;
                    AgentName = chip.Name;
                    AgentGlyph = chip.Glyph;
                    break;
                case ChipKind.Skill:
                    if (SkillItems is System.Collections.IList skillList && !IsAlreadyActiveSkill(chip))
                        skillList.Add(chip);
                    break;
                case ChipKind.Project:
                    ProjectValue = chip.Value;
                    ProjectName = chip.Name;
                    break;
                case ChipKind.Chat:
                    if (ChatItems is IList chatList && !IsAlreadyReferencedChat(chip))
                        chatList.Add(chip);
                    break;
            }
        }

        _referenceTriggerIndex = -1;
        CloseAutoComplete();
        FocusEditorAt(focusCaret);
    }

    private bool TryGetActiveTriggerSpan(
        out string text,
        out int caret,
        out int removeLength)
    {
        text = PromptText ?? "";
        caret = GetEditorCaretIndex();
        removeLength = 0;
        if (_triggerIndex < 0
            || _triggerIndex >= text.Length
            || caret <= _triggerIndex
            || caret > text.Length
            || text[_triggerIndex] != _triggerChar
            || (_triggerIndex > 0 && text[_triggerIndex - 1] is not (' ' or '\n' or '\r')))
        {
            return false;
        }

        var tokenEnd = _triggerIndex + 1;
        while (tokenEnd < text.Length && text[tokenEnd] is not (' ' or '\n' or '\r'))
            tokenEnd++;
        if (caret != tokenEnd)
            return false;

        removeLength = caret - _triggerIndex;
        return removeLength > 0;
    }

    /// <summary>
    /// <see cref="AgentValue"/> names the chosen agent's chip. When the host clears or changes the agent by
    /// name alone, the value would still point at the previous one and keep it out of the @ list.
    /// </summary>
    private void ForgetStaleAgentValue()
    {
        if (string.IsNullOrEmpty(AgentValue))
            return;

        var stale = string.IsNullOrWhiteSpace(AgentName);
        if (!stale && AvailableAgents is not null)
        {
            foreach (var item in AvailableAgents)
            {
                if (item is StrataComposerChip chip && string.Equals(chip.Value, AgentValue, StringComparison.Ordinal))
                {
                    stale = !string.Equals(chip.Name, AgentName, StringComparison.Ordinal);
                    break;
                }
            }
        }

        if (stale)
            SetCurrentValue(AgentValueProperty, null);
    }

    private static bool MatchesSelection(StrataComposerChip chip, string? name, string? value) =>
        !string.IsNullOrWhiteSpace(value)
            ? string.Equals(chip.Value, value, StringComparison.Ordinal)
            : string.Equals(chip.Name, name, StringComparison.Ordinal);

    private void MoveAutoCompleteSelection(int delta)
    {
        if (_autoCompleteEntries.Count == 0) return;
        _autoCompleteSelectedIndex = (_autoCompleteSelectedIndex + delta + _autoCompleteEntries.Count) % _autoCompleteEntries.Count;
        UpdateAutoCompleteHighlight();
    }

    private void UpdateAutoCompleteHighlight()
    {
        Border? selectedBorder = null;
        for (var i = 0; i < _autoCompleteEntries.Count; i++)
        {
            var border = _autoCompleteEntries[i].Control;
            if (i == _autoCompleteSelectedIndex)
            {
                if (!border.Classes.Contains("selected"))
                    border.Classes.Add("selected");
                selectedBorder = border;
            }
            else
            {
                border.Classes.Remove("selected");
            }
        }

        if (selectedBorder is not null && _autoCompleteScrollViewer is not null)
            Dispatcher.UIThread.Post(() => selectedBorder.BringIntoView(), DispatcherPriority.Loaded);
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

    private static StrataComposerChip AsChip(object? item)
        => item as StrataComposerChip ?? new StrataComposerChip(item?.ToString() ?? "");

    private bool IsAlreadyReferencedChat(StrataComposerChip chip)
    {
        if (ChatItems is null) return false;
        foreach (var item in ChatItems)
        {
            if (StrataComposerPicker.SameItem(AsChip(item), chip))
                return true;
        }
        return false;
    }

    /// <summary>Takes <paramref name="chip"/> out of the next message's chats and tells the host.</summary>
    private void RemoveReferencedChat(StrataComposerChip chip)
    {
        if (ChatItems is not IList list)
            return;

        for (var i = 0; i < list.Count; i++)
        {
            if (!StrataComposerPicker.SameItem(AsChip(list[i]), chip))
                continue;

            var removed = list[i];
            list.RemoveAt(i);
            RaiseEvent(new ComposerChipRemovedEventArgs(ChatRemovedEvent, removed));
            return;
        }
    }

    private void OnSkillPickerToggled(object? sender, ComposerPickerToggledEventArgs e)
    {
        if (e.IsSelected)
        {
            // The same path as choosing a skill from the / list.
            if (SkillItems is IList list && !IsAlreadyActiveSkill(e.Chip))
                list.Add(e.Chip);
            return;
        }

        // The same path as the skill chip's remove button.
        object? selected = null;
        if (SkillItems is not null)
        {
            foreach (var item in SkillItems)
            {
                if (StrataComposerPicker.SameItem(AsChip(item), e.Chip))
                {
                    selected = item;
                    break;
                }
            }
        }

        var name = selected is null ? e.Chip.Name : AsChip(selected).Name;
        RaiseEvent(new ComposerChipRemovedEventArgs(SkillRemovedEvent, selected ?? e.Chip));
        CommandHelper.Execute(SkillRemovedCommand, SkillRemovedCommandParameter ?? name);
    }

    private void OnChatPickerToggled(object? sender, ComposerPickerToggledEventArgs e)
    {
        if (e.IsSelected)
        {
            if (ChatItems is IList list && !IsAlreadyReferencedChat(e.Chip))
                list.Add(e.Chip);
        }
        else
        {
            RemoveReferencedChat(e.Chip);
        }

        // A chat is brought in to talk about it: go straight back to writing.
        FocusInput();
    }

    private void OnChatPickerOpening(object? sender, EventArgs e)
        => CommandHelper.Execute(ChatsRequestedCommand, null);

    private static Control CreateSectionHeader(string label)
    {
        var tb = new TextBlock { Text = label };
        tb.Classes.Add("autocomplete-header");
        return tb;
    }

    private Border CreateAutoCompleteEntry(StrataComposerChip chip, ChipKind kind)
    {
        Control glyph;
        if (kind == ChipKind.Chat)
        {
            var icon = new StrataLineIcon { Data = StrataIcons.Chat, Width = 14, Height = 14, StrokeThickness = 2 };
            icon.Classes.Add("autocomplete-icon");
            glyph = icon;
        }
        else
        {
            var glyphText = new TextBlock { Text = kind == ChipKind.File ? "📄" : chip.Glyph };
            glyphText.Classes.Add("autocomplete-glyph");
            glyph = glyphText;
        }

        var glyphWrap = new Border
        {
            Child = glyph,
        };
        glyphWrap.Classes.Add("autocomplete-glyph-wrap");

        var primaryText = GetAutoCompletePrimaryText(chip, kind);
        var secondaryText = GetAutoCompleteSecondaryText(chip, kind);
        var trailingText = GetAutoCompleteTrailingText(chip, kind);

        var name = new TextBlock
        {
            Text = primaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        name.Classes.Add("autocomplete-name");

        var copy = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Children.Add(name);

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondary = new TextBlock
            {
                Text = secondaryText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            secondary.Classes.Add("autocomplete-secondary");
            copy.Children.Add(secondary);
        }

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
        };

        Grid.SetColumn(glyphWrap, 0);
        layout.Children.Add(glyphWrap);

        Grid.SetColumn(copy, 1);
        layout.Children.Add(copy);

        if (!string.IsNullOrWhiteSpace(trailingText))
        {
            var trailing = new TextBlock { Text = trailingText };
            trailing.Classes.Add("autocomplete-trailing-text");

            var trailingBadge = new Border
            {
                Child = trailing,
            };
            trailingBadge.Classes.Add("autocomplete-trailing-badge");
            Grid.SetColumn(trailingBadge, 2);
            layout.Children.Add(trailingBadge);
        }

        var border = new Border
        {
            Child = layout,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        border.Classes.Add("autocomplete-item");

        var tooltip = kind switch
        {
            ChipKind.File => chip.Value ?? secondaryText ?? chip.Name,
            _ => chip.SecondaryText,
        };

        if (!string.IsNullOrWhiteSpace(tooltip))
            ToolTip.SetTip(border, tooltip);

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

    private static string GetAutoCompletePrimaryText(StrataComposerChip chip, ChipKind kind)
    {
        if (kind != ChipKind.File)
            return chip.Name;

        var fileName = System.IO.Path.GetFileName(chip.Name);
        return string.IsNullOrWhiteSpace(fileName) ? chip.Name : fileName;
    }

    private static string? GetAutoCompleteSecondaryText(StrataComposerChip chip, ChipKind kind)
    {
        if (!string.IsNullOrWhiteSpace(chip.SecondaryText))
            return chip.SecondaryText;

        if (kind != ChipKind.File)
            return null;

        var directory = System.IO.Path.GetDirectoryName(chip.Name);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        return directory.Replace('\\', '/');
    }

    private static string? GetAutoCompleteTrailingText(StrataComposerChip chip, ChipKind kind)
    {
        if (kind != ChipKind.File)
            return string.IsNullOrWhiteSpace(chip.SourceLabel) ? null : chip.SourceLabel;

        var extension = System.IO.Path.GetExtension(chip.Name).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return extension.Length <= 6 ? extension.ToUpperInvariant() : null;
    }

    private void Sync()
    {
        var hasPromptText = !string.IsNullOrWhiteSpace(PromptText);

        var effectiveBusy = IsBusy && !IsEditing;
        PseudoClasses.Set(":editing", IsEditing);
        PseudoClasses.Set(":busy", effectiveBusy);
        PseudoClasses.Set(":empty", !hasPromptText);
        var canSendWhileBusy = effectiveBusy && (hasPromptText || CanSendWithoutPrompt);
        PseudoClasses.Set(":steer", canSendWhileBusy && SteerWhileBusy);
        PseudoClasses.Set(":stop-send", canSendWhileBusy && !SteerWhileBusy);
        PseudoClasses.Set(":can-attach", CanAttach);
        PseudoClasses.Set(":can-voice", CanVoice);
        PseudoClasses.Set(":can-send-without-prompt", CanSendWithoutPrompt);
        PseudoClasses.Set(":a-empty", string.IsNullOrWhiteSpace(SuggestionA));
        PseudoClasses.Set(":b-empty", string.IsNullOrWhiteSpace(SuggestionB));
        PseudoClasses.Set(":c-empty", string.IsNullOrWhiteSpace(SuggestionC));
        PseudoClasses.Set(":has-models", Models is not null);
        PseudoClasses.Set(":has-quality", QualityLevels is not null);
        PseudoClasses.Set(":has-modes", Modes is not null);
        var hasAgent = !string.IsNullOrWhiteSpace(AgentName);
        var hasProject = !string.IsNullOrWhiteSpace(ProjectName);
        var hasSkills = HasAnySkills();
        var hasChats = HasAnyChats();
        PseudoClasses.Set(":has-agent", hasAgent);
        PseudoClasses.Set(":has-project", hasProject);
        PseudoClasses.Set(":has-skills", hasSkills);
        PseudoClasses.Set(":has-chats", hasChats);
        PseudoClasses.Set(":has-chips", hasAgent || hasProject || hasSkills || hasChats);
        PseudoClasses.Set(":has-skill-options", AvailableSkills is not null);
        PseudoClasses.Set(":has-chat-options", AvailableChats is not null);
        PseudoClasses.Set(":recording", IsRecording);
        PseudoClasses.Set(":suggestions-generating", IsSuggestionsGenerating);
        PseudoClasses.Set(":has-mcp-options", HasAnyAvailableMcps() || McpBrowseCommand is not null);
        PseudoClasses.Set(":has-add-options", HasAddOptions());
        PseudoClasses.Set(":voice", ShouldDictate() && !IsRecording);

        if (_sendButton is not null)
        {
            var actionLabel = IsEditing
                ? EditingSubmitLabel
                : CanVoice && IsRecording
                    ? StopDictatingLabel
                : ShouldDictate()
                    ? DictateLabel
                : effectiveBusy
                    ? canSendWhileBusy
                        ? SteerWhileBusy ? "Send to steer" : "Stop & send"
                        : "Stop generation"
                    : "Send";
            AutomationProperties.SetName(_sendButton, actionLabel);
            ToolTip.SetTip(_sendButton, actionLabel);
        }
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

    private bool HasAnyChats()
    {
        if (ChatItems is null) return false;
        foreach (var _ in ChatItems) return true;
        return false;
    }

    /// <summary>Below this width the toolbar's labelled pickers shrink to their icons.</summary>
    private const double NarrowWidth = 600;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PseudoClasses.Set(":narrow", e.NewSize.Width < NarrowWidth);
        UpdateFocusDepthCenter();
    }

    private bool HasAnyAvailableMcps()
    {
        if (AvailableMcps is null) return false;
        foreach (var _ in AvailableMcps) return true;
        return false;
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
        RebuildContextWindowSection();
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
        RebuildContextWindowSection();
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

    private void RefreshModelPickerContextIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerContextIfOpen, DispatcherPriority.Background);
            return;
        }

        RebuildContextWindowSection();
    }

    private void RefreshModelPickerContextSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerContextSelectionIfOpen, DispatcherPriority.Background);
            return;
        }

        UpdateContextWindowActiveState();
    }

    private void BuildModelPickerRows()
    {
        if (_modelPickerList is null) return;
        _modelPickerList.Children.Clear();

        if (Models is null) return;

        var rows = Models.Cast<object?>()
            .Select((model, index) =>
            {
                var modelStr = model?.ToString() ?? "";
                return new ModelPickerRowData(model, modelStr, GetModelGroup(modelStr), index);
            })
            .ToList();

        var groupOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!groupOrder.ContainsKey(row.Group))
                groupOrder[row.Group] = groupOrder.Count;
        }

        string? lastGroup = null;
        foreach (var row in rows
            .OrderBy(row => groupOrder[row.Group])
            .ThenByDescending(row => GetModelVersionRank(row.ModelName))
            .ThenBy(row => row.OriginalIndex))
        {
            var group = row.Group;

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

            var isSelected = Equals(row.Model, SelectedModel);
            _modelPickerList.Children.Add(CreateModelRow(row.Model, row.ModelName, isSelected));
        }

        // Build the fixed effort section at the bottom
        RebuildEffortSection();
    }

    private sealed record ModelPickerRowData(object? Model, string ModelName, string Group, int OriginalIndex);

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

    private void RebuildContextWindowSection()
    {
        if (_contextWindowSection is null) return;
        _contextWindowSection.Children.Clear();

        if (ContextWindowTiers is null || SelectedModel is null)
            return;

        var tiers = ContextWindowTiers.Cast<object?>().ToList();
        if (tiers.Count == 0)
            return;

        var sep = new Border { Height = 1, Margin = new Thickness(10, 4) };
        sep.Classes.Add("model-picker-separator");
        _contextWindowSection.Children.Add(sep);

        var label = new TextBlock
        {
            Text = "CONTEXT WINDOW",
            FontSize = 9.5,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.6,
            Margin = new Thickness(14, 4, 10, 4)
        };
        label.Classes.Add("effort-label");
        _contextWindowSection.Children.Add(label);

        var toggleBorder = new Border
        {
            Margin = new Thickness(8, 0, 8, 6),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3)
        };
        toggleBorder.Classes.Add("model-effort-toggle");

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse(string.Join(",", Enumerable.Range(0, tiers.Count).Select(_ => "*")))
        };

        for (var index = 0; index < tiers.Count; index++)
        {
            var tier = tiers[index];
            var button = new Button
            {
                Content = tier?.ToString() ?? string.Empty,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Classes.Add("effort-seg");
            if (Equals(tier, SelectedContextWindowTier))
                button.Classes.Add("active");

            var capturedTier = tier;
            button.Click += (_, _) =>
            {
                _suppressPickerRebuild = true;
                SelectedContextWindowTier = capturedTier;

                foreach (var child in grid.Children.OfType<Button>())
                {
                    if (Equals(child.Content, capturedTier?.ToString()))
                    {
                        if (!child.Classes.Contains("active"))
                            child.Classes.Add("active");
                    }
                    else
                    {
                        child.Classes.Remove("active");
                    }
                }

                Dispatcher.UIThread.Post(() => _suppressPickerRebuild = false, DispatcherPriority.Background);
            };

            Grid.SetColumn(button, index);
            grid.Children.Add(button);
        }

        toggleBorder.Child = grid;
        _contextWindowSection.Children.Add(toggleBorder);
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

    private static int GetModelVersionRank(string modelId)
    {
        foreach (var segment in modelId.ToLowerInvariant().Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseVersionSegment(segment, out var rank))
                return rank;
        }

        return -1;
    }

    private static bool TryParseVersionSegment(string segment, out int rank)
    {
        rank = -1;
        var index = segment.StartsWith("o", StringComparison.Ordinal) ? 1 : 0;
        if (index >= segment.Length || !char.IsDigit(segment[index]))
            return false;

        var major = 0;
        while (index < segment.Length && char.IsDigit(segment[index]))
        {
            major = major * 10 + segment[index] - '0';
            index++;
        }

        var minor = 0;
        if (index < segment.Length && segment[index] == '.')
        {
            index++;
            while (index < segment.Length && char.IsDigit(segment[index]))
            {
                minor = minor * 10 + segment[index] - '0';
                index++;
            }
        }

        if (index < segment.Length && segment[index] == 'm')
            return false;

        rank = major * 1000 + minor;
        return true;
    }

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

    private Border CreateModelRow(object? model, string modelStr, bool isSelected)
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
    private void UpdateModelPickerSelection(object? newModel)
    {
        _suppressPickerRebuild = true;
        SelectedModel = newModel;

        UpdateModelPickerSelectionVisuals(newModel);

        Dispatcher.UIThread.Post(() =>
        {
            _suppressPickerRebuild = false;
            RefreshModelPickerEffortIfOpen();
            RefreshModelPickerContextIfOpen();
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

    private void UpdateContextWindowActiveState()
    {
        if (_contextWindowSection is null) return;

        if (ContextWindowTiers is not null && _contextWindowSection.Children.Count == 0)
        {
            RebuildContextWindowSection();
            return;
        }

        if (ContextWindowTiers is null && _contextWindowSection.Children.Count > 0)
        {
            _contextWindowSection.Children.Clear();
            return;
        }

        foreach (var child in _contextWindowSection.Children)
        {
            if (child is not Border border || !border.Classes.Contains("model-effort-toggle") || border.Child is not Grid grid)
                continue;

            var selectedTier = SelectedContextWindowTier?.ToString();
            foreach (var button in grid.Children.OfType<Button>())
            {
                if (button.Content?.ToString() == selectedTier)
                {
                    if (!button.Classes.Contains("active"))
                        button.Classes.Add("active");
                }
                else
                {
                    button.Classes.Remove("active");
                }
            }

            break;
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

        RebuildContextChips();
    }

    private void OnSkillCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildContextChips();
    }

    private void OnChatItemsChanged()
    {
        if (_subscribedChatCollection is not null)
        {
            _subscribedChatCollection.CollectionChanged -= OnChatCollectionChanged;
            _subscribedChatCollection = null;
        }

        if (ChatItems is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnChatCollectionChanged;
            _subscribedChatCollection = ncc;
        }

        RebuildContextChips();
    }

    private void OnChatCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildContextChips();
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

    private void OnAvailableFilesChanged()
    {
        if (_subscribedAvailableFileCollection is not null)
        {
            _subscribedAvailableFileCollection.CollectionChanged -= OnAvailableFileCollectionChanged;
            _subscribedAvailableFileCollection = null;
        }

        if (AvailableFiles is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnAvailableFileCollectionChanged;
            _subscribedAvailableFileCollection = ncc;
        }

        RefreshFileAutoComplete();
    }

    private void OnAvailableFileCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_fileAutoCompleteRefreshQueued)
            return;

        _fileAutoCompleteRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fileAutoCompleteRefreshQueued = false;
            RefreshFileAutoComplete();
        }, DispatcherPriority.Background);
    }

    private void RefreshFileAutoComplete()
    {
        // The live reference, not the popup: its list closes while nothing matches yet.
        var triggerIndex = _referenceTriggerIndex;
        if (triggerIndex < 0)
            return;

        var text = PromptText ?? "";
        var caret = GetEditorCaretIndex();
        if (triggerIndex < text.Length && text[triggerIndex] == '#' && caret <= text.Length && caret > triggerIndex)
        {
            _triggerIndex = triggerIndex;
            _triggerChar = '#';
            var query = text.Substring(triggerIndex + 1, caret - triggerIndex - 1);
            ShowAutoCompleteItems(query);
        }
    }

    private void RebuildContextChips()
    {
        if (_chipsRow is null) return;

        // Remove the previous skill and chat chips (PART_AgentChip at 0, PART_ProjectChip at 1 stay)
        while (_chipsRow.Children.Count > 2)
            _chipsRow.Children.RemoveAt(_chipsRow.Children.Count - 1);

        if (SkillItems is not null)
        {
            foreach (var item in SkillItems)
                _chipsRow.Children.Add(CreateChip(item, SkillRemovedEvent, "composer-skill-chip"));
        }

        if (ChatItems is not null)
        {
            foreach (var item in ChatItems)
                _chipsRow.Children.Add(CreateChip(item, ChatRemovedEvent, "composer-chat-chip"));
        }

        Sync();
    }

    private Control CreateChip(object item, RoutedEvent<ComposerChipRemovedEventArgs> removedEvent, string cssClass)
    {
        var name = item is StrataComposerChip c ? c.Name : item?.ToString() ?? "";
        var glyph = item is StrataComposerChip sc ? sc.Glyph : "✦";
        var hasError = item is StrataComposerChip ec && ec.HasError;
        var errorMessage = item is StrataComposerChip emc ? emc.ErrorMessage : null;
        var isChat = removedEvent == ChatRemovedEvent;

        // The kind's mark sits on a small tile: a chat shows its bubble; a skill the wand on the desktop
        // and its own glyph on touch, where the chip is a larger target with room for it.
        Control mark;
        if (isChat || (!hasError && removedEvent == SkillRemovedEvent && !Classes.Contains("mobile")))
        {
            var icon = new StrataLineIcon { Data = isChat ? StrataIcons.Chat : StrataIcons.Wand };
            icon.Classes.Add("chip-icon");
            mark = icon;
        }
        else
        {
            var glyphText = new TextBlock { Text = hasError ? "⚠" : glyph };
            glyphText.Classes.Add("chip-glyph");
            if (hasError) glyphText.Classes.Add("chip-error");
            mark = glyphText;
        }

        var tile = new Border { Child = mark, VerticalAlignment = VerticalAlignment.Center };
        tile.Classes.Add("chip-tile");

        var nameText = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
        nameText.Classes.Add("chip-name");
        if (hasError) nameText.Classes.Add("chip-error");

        var removeIcon = new TextBlock { Text = "×" };
        removeIcon.Classes.Add("chip-remove-icon");

        // Size and shape come from styles, so a touch host can grow the target.
        var removeBtn = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Content = removeIcon,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        removeBtn.Classes.Add("subtle");
        removeBtn.Classes.Add("chip-remove");
        Avalonia.Automation.AutomationProperties.SetName(removeBtn, $"Remove {name}");

        var capturedItem = item;
        removeBtn.Click += (_, _) =>
        {
            if (isChat)
            {
                RemoveReferencedChat(AsChip(capturedItem));
                FocusInput();
                return;
            }

            RaiseEvent(new ComposerChipRemovedEventArgs(removedEvent, capturedItem));
            // Fire the matching command with the chip name as default parameter
            var chipName = capturedItem is StrataComposerChip cc ? cc.Name : capturedItem?.ToString() ?? "";
            if (removedEvent == SkillRemovedEvent)
                CommandHelper.Execute(SkillRemovedCommand, SkillRemovedCommandParameter ?? chipName);
            else if (removedEvent == McpRemovedEvent)
                CommandHelper.Execute(McpRemovedCommand, McpRemovedCommandParameter ?? chipName);
        };

        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6
        };
        var surface = new Border { IsHitTestVisible = false };
        surface.Classes.Add("composer-chip-surface");
        Grid.SetColumnSpan(surface, 3);
        Grid.SetColumn(nameText, 1);
        Grid.SetColumn(removeBtn, 2);
        panel.Children.Add(surface);
        panel.Children.Add(tile);
        panel.Children.Add(nameText);
        panel.Children.Add(removeBtn);

        var border = new Border { Child = panel };
        border.Classes.Add("composer-chip");
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
