using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>The role of a chat message sender.</summary>
public enum StrataChatRole
{
    /// <summary>Message from the AI assistant.</summary>
    Assistant,
    /// <summary>Message from the human user.</summary>
    User,
    /// <summary>System-level instruction or notice.</summary>
    System,
    /// <summary>Tool-call result output.</summary>
    Tool
}

/// <summary>Event arguments for <see cref="StrataChatMessage.EditConfirmed"/> carrying the edited text.</summary>
public class StrataEditConfirmedEventArgs : RoutedEventArgs
{
    /// <summary>The edited text that the user confirmed.</summary>
    public string NewText { get; }

    public StrataEditConfirmedEventArgs(RoutedEvent routedEvent, string newText) : base(routedEvent)
    {
        NewText = newText;
    }
}

/// <summary>Event arguments for <see cref="StrataChatMessage.CopyRequested"/> carrying the exact copied text.</summary>
public class StrataCopyRequestedEventArgs : RoutedEventArgs
{
    /// <summary>The text Strata copied to the clipboard before raising the event.</summary>
    public string Text { get; }

    /// <summary>True when the copy action targeted selected text instead of the whole message.</summary>
    public bool IsSelection { get; }

    public StrataCopyRequestedEventArgs(RoutedEvent routedEvent, string text, bool isSelection) : base(routedEvent)
    {
        Text = text;
        IsSelection = isSelection;
    }
}

/// <summary>
/// A chat message bubble with role-dependent styling, hover toolbar (Copy / Edit / Retry),
/// inline edit mode, and streaming indicator. Supports any content (text, markdown, controls).
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataChatMessage Role="User"&gt;
///     &lt;SelectableTextBlock Text="Hello, world!" TextWrapping="Wrap" /&gt;
/// &lt;/controls:StrataChatMessage&gt;
///
/// &lt;controls:StrataChatMessage Role="Assistant" IsStreaming="True"&gt;
///     &lt;controls:StrataMarkdown Markdown="{Binding ResponseMarkdown}" IsInline="True" /&gt;
/// &lt;/controls:StrataChatMessage&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Bubble (Border), PART_EditArea (Border), PART_EditBox (TextBox),
/// PART_StreamBar (Border), PART_ActionBar (StackPanel), PART_CopyButton (Button),
/// PART_EditButton (Button), PART_RegenerateButton (Button), PART_SaveButton (Button), PART_CancelButton (Button).</para>
/// <para><b>Pseudo-classes:</b> :assistant, :user, :system, :tool, :streaming, :editing, :editable, :host-scrolling, :has-meta, :has-status.</para>
/// </remarks>
public class StrataChatMessage : TemplatedControl
{
    private Border? _streamBar;
    private Border? _bubble;
    private Border? _actionLayer;
    private Border? _editArea;
    private Border? _editSeparator;
    private Border? _retrySeparator;
    private TextBox? _editBox;
    private TextBlock? _editHint;
    private Button? _copyButton;
    private Button? _editButton;
    private Button? _retryButton;
    private Button? _saveButton;
    private Button? _cancelButton;
    private ContextMenu? _contextMenu;
    private Control? _actionLayerChild;
    private Control? _editAreaChild;
    private readonly HashSet<TextBlock> _observedTextBlocks = new();
    private readonly HashSet<StrataMarkdown> _observedMarkdownControls = new();
    private readonly HashSet<Control> _contextMenuTargets = new();
    private bool _originalContentWasMarkdown;

    // Tracks visual-tree attachment so the deferred stream-pulse start (posted to the dispatcher)
    // never begins a Forever composition animation on an already-detached, off-tree visual.
    private bool _isAttachedToVisualTree;

    // Mirrors the streaming-pulse start/stop state machine (see IsStreamPulseActiveForTest).
    private bool _streamPulseActive;

    // ── Cached state to skip redundant updates ──
    private bool _cachedEditVisible;
    private bool _cachedRetryVisible;
    private bool _contextMenuBuilt;
    private StrataChatRole _lastMenuRole;
    private bool _lastMenuIsEditing;
    private bool _lastMenuIsEditable;
    private bool _lastMenuIsStreaming;
    private bool _lastMenuHasSelection;
    private SelectableTextBlock? _contextMenuSelectionSource;
    private int _contextMenuSelectionStart;
    private int _contextMenuSelectionEnd;
    private string? _contextMenuSelectionText;

    /// <summary>Message role. Controls alignment, colour, and available actions.</summary>
    public static readonly StyledProperty<StrataChatRole> RoleProperty =
        AvaloniaProperty.Register<StrataChatMessage, StrataChatRole>(nameof(Role), StrataChatRole.Assistant);

    /// <summary>Optional author name displayed in the metadata row.</summary>
    public static readonly StyledProperty<string> AuthorProperty =
        AvaloniaProperty.Register<StrataChatMessage, string>(nameof(Author), string.Empty);

    /// <summary>Optional timestamp displayed in the metadata row.</summary>
    public static readonly StyledProperty<string> TimestampProperty =
        AvaloniaProperty.Register<StrataChatMessage, string>(nameof(Timestamp), string.Empty);

    /// <summary>Status text shown below assistant messages (e.g. "Took 1.2 s").</summary>
    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<StrataChatMessage, string>(nameof(StatusText), string.Empty);

    /// <summary>Message content. Can be any control, SelectableTextBlock, or StrataMarkdown.</summary>
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<StrataChatMessage, object?>(nameof(Content));

    /// <summary>When true, shows a pulsing accent bar indicating content is still arriving.</summary>
    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(IsStreaming));

    /// <summary>Whether the Edit button appears in the hover toolbar.</summary>
    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(IsEditable), true);

    /// <summary>Whether the message is currently in inline-edit mode.</summary>
    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(IsEditing));

    /// <summary>When false, Edit invokes the host command without opening the inline editor.</summary>
    public static readonly StyledProperty<bool> UseInlineEditProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(UseInlineEdit), true);

    /// <summary>Text value of the edit box when editing.</summary>
    public static readonly StyledProperty<string?> EditTextProperty =
        AvaloniaProperty.Register<StrataChatMessage, string?>(nameof(EditText));

    /// <summary>When true (default), confirming an edit writes EditText back into Content.</summary>
    public static readonly StyledProperty<bool> ApplyEditToContentProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(ApplyEditToContent), true);

    /// <summary>
    /// Indicates that the containing chat shell is actively scrolling.
    /// When true, the message can simplify hover visuals to reduce scroll jank.
    /// </summary>
    public static readonly StyledProperty<bool> IsHostScrollingProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(IsHostScrolling));

    public static readonly RoutedEvent<StrataCopyRequestedEventArgs> CopyRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, StrataCopyRequestedEventArgs>(nameof(CopyRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> CopyTurnRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(CopyTurnRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> RegenerateRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(RegenerateRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> EditRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(EditRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<StrataEditConfirmedEventArgs> EditConfirmedEvent =
        RoutedEvent.Register<StrataChatMessage, StrataEditConfirmedEventArgs>(nameof(EditConfirmed), RoutingStrategies.Bubble);

    /// <summary>Command executed when the user copies message text. Parameter is the extracted text string.</summary>
    public static readonly StyledProperty<ICommand?> CopyCommandProperty =
        AvaloniaProperty.Register<StrataChatMessage, ICommand?>(nameof(CopyCommand));

    /// <summary>Optional parameter for <see cref="CopyCommand"/>.</summary>
    public static readonly StyledProperty<object?> CopyCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatMessage, object?>(nameof(CopyCommandParameter));

    /// <summary>Command executed when the user requests regeneration.</summary>
    public static readonly StyledProperty<ICommand?> RegenerateCommandProperty =
        AvaloniaProperty.Register<StrataChatMessage, ICommand?>(nameof(RegenerateCommand));

    /// <summary>Optional parameter for <see cref="RegenerateCommand"/>.</summary>
    public static readonly StyledProperty<object?> RegenerateCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatMessage, object?>(nameof(RegenerateCommandParameter));

    /// <summary>Command executed when the user begins editing.</summary>
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<StrataChatMessage, ICommand?>(nameof(EditCommand));

    /// <summary>Optional parameter for <see cref="EditCommand"/>.</summary>
    public static readonly StyledProperty<object?> EditCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatMessage, object?>(nameof(EditCommandParameter));

    /// <summary>Command executed when the user confirms an edit. Parameter is the new text string.</summary>
    public static readonly StyledProperty<ICommand?> EditConfirmedCommandProperty =
        AvaloniaProperty.Register<StrataChatMessage, ICommand?>(nameof(EditConfirmedCommand));

    /// <summary>Optional parameter for <see cref="EditConfirmedCommand"/>. When null, the edited text is passed.</summary>
    public static readonly StyledProperty<object?> EditConfirmedCommandParameterProperty =
        AvaloniaProperty.Register<StrataChatMessage, object?>(nameof(EditConfirmedCommandParameter));

    static StrataChatMessage()
    {
        RoleProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnRoleChanged());
        ContentProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnContentChanged());
        IsStreamingProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnStreamingChanged());
        IsEditingProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnEditingChanged());
        IsEditableProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnEditableChanged());
        IsHostScrollingProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnHostScrollingChanged());
        AuthorProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnMetaChanged());
        TimestampProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnMetaChanged());
        StatusTextProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnStatusChanged());
    }

    public event EventHandler<StrataCopyRequestedEventArgs>? CopyRequested
    { add => AddHandler(CopyRequestedEvent, value); remove => RemoveHandler(CopyRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? CopyTurnRequested
    { add => AddHandler(CopyTurnRequestedEvent, value); remove => RemoveHandler(CopyTurnRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? RegenerateRequested
    { add => AddHandler(RegenerateRequestedEvent, value); remove => RemoveHandler(RegenerateRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? EditRequested
    { add => AddHandler(EditRequestedEvent, value); remove => RemoveHandler(EditRequestedEvent, value); }
    public event EventHandler<StrataEditConfirmedEventArgs>? EditConfirmed
    { add => AddHandler(EditConfirmedEvent, value); remove => RemoveHandler(EditConfirmedEvent, value); }

    public ICommand? CopyCommand { get => GetValue(CopyCommandProperty); set => SetValue(CopyCommandProperty, value); }
    public object? CopyCommandParameter { get => GetValue(CopyCommandParameterProperty); set => SetValue(CopyCommandParameterProperty, value); }
    public ICommand? RegenerateCommand { get => GetValue(RegenerateCommandProperty); set => SetValue(RegenerateCommandProperty, value); }
    public object? RegenerateCommandParameter { get => GetValue(RegenerateCommandParameterProperty); set => SetValue(RegenerateCommandParameterProperty, value); }
    public ICommand? EditCommand { get => GetValue(EditCommandProperty); set => SetValue(EditCommandProperty, value); }
    public object? EditCommandParameter { get => GetValue(EditCommandParameterProperty); set => SetValue(EditCommandParameterProperty, value); }
    public ICommand? EditConfirmedCommand { get => GetValue(EditConfirmedCommandProperty); set => SetValue(EditConfirmedCommandProperty, value); }
    public object? EditConfirmedCommandParameter { get => GetValue(EditConfirmedCommandParameterProperty); set => SetValue(EditConfirmedCommandParameterProperty, value); }

    public StrataChatRole Role { get => GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public string Author { get => GetValue(AuthorProperty); set => SetValue(AuthorProperty, value); }
    public string Timestamp { get => GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    public string StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public bool IsStreaming { get => GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public bool IsEditable { get => GetValue(IsEditableProperty); set => SetValue(IsEditableProperty, value); }
    public bool IsEditing { get => GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }
    public bool UseInlineEdit { get => GetValue(UseInlineEditProperty); set => SetValue(UseInlineEditProperty, value); }
    public string? EditText { get => GetValue(EditTextProperty); set => SetValue(EditTextProperty, value); }
    public bool ApplyEditToContent { get => GetValue(ApplyEditToContentProperty); set => SetValue(ApplyEditToContentProperty, value); }

    /// <summary>
    /// Gets or sets whether the containing chat shell is currently scrolling.
    /// </summary>
    public bool IsHostScrolling { get => GetValue(IsHostScrollingProperty); set => SetValue(IsHostScrollingProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        DetachTemplatePartHandlers();

        base.OnApplyTemplate(e);
        // Stop any pulse on the outgoing stream bar before it is replaced, so a re-templated control
        // cannot leave a Forever animation running on the discarded visual.
        StopStreamPulse();
        _streamBar = e.NameScope.Find<Border>("PART_StreamBar");
        _bubble = e.NameScope.Find<Border>("PART_Bubble");
        _actionLayer = e.NameScope.Find<Border>("PART_ActionLayer");
        _editArea = e.NameScope.Find<Border>("PART_EditArea");
        _editBox = e.NameScope.Find<TextBox>("PART_EditBox");
        _editHint = e.NameScope.Find<TextBlock>("PART_EditHint");
        _actionLayerChild = _actionLayer?.Child;
        _editAreaChild = _editArea?.Child;

        _copyButton = e.NameScope.Find<Button>("PART_CopyButton");
        var regenBtn = e.NameScope.Find<Button>("PART_RegenerateButton");
        var editBtn = e.NameScope.Find<Button>("PART_EditButton");
        _saveButton = e.NameScope.Find<Button>("PART_SaveButton");
        _cancelButton = e.NameScope.Find<Button>("PART_CancelButton");
        _editSeparator = e.NameScope.Find<Border>("PART_EditSep");
        _retrySeparator = e.NameScope.Find<Border>("PART_RegenerateSep");
        _editButton = editBtn;
        _retryButton = regenBtn;

        AttachTemplatePartHandlers();

        AttachContextMenu();

        UpdateAllPseudoClasses();
        OnContentChanged();
        UpdateActionBarLayout(force: true);
        UpdateActionChromeMount();
        UpdateEditAreaMount();
        if (IsStreaming)
            Dispatcher.UIThread.Post(StartStreamPulse, DispatcherPriority.Loaded);

        // Seed EditText when entering editing from XAML (Content may not be set
        // yet when IsEditing fires during initialization).
        if (IsEditing)
            Dispatcher.UIThread.Post(SeedEditTextFromContent, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _isAttachedToVisualTree = true;

        // Transcript virtualization can detach and reattach the same already-templated message
        // instance. OnApplyTemplate is not called on that plain reattach, so any handlers cleaned up
        // during detach must be restored here.
        AttachTemplatePartHandlers();
        AttachContextMenu();
        AttachContentObserversAndApply(Content);

        if (IsStreaming)
            Dispatcher.UIThread.Post(StartStreamPulse, DispatcherPriority.Loaded);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsPointerOverProperty || change.Property == IsFocusedProperty)
            UpdateActionChromeMount();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;

        // Stop the streaming pulse while the composition visual is still attached (before base runs).
        // A Forever composition animation left running when the visual detaches keeps ticking on the
        // render thread; the compositor only deactivates it if a later parent re-sync fires, so under
        // transcript virtualization it can leak indefinitely. Over a long streaming session those
        // orphaned animations accumulate and starve the render thread (broken animations, the
        // navigation menu ceasing to composite, and global slowdown).
        StopStreamPulse();

        DetachTemplatePartHandlers();
        if (_contextMenu is not null)
        {
            _contextMenu.Opening -= OnContextMenuOpening;
            _contextMenu.Closing -= OnContextMenuClosing;
        }
        if (_bubble is not null)
        {
            _bubble.RemoveHandler(PointerPressedEvent, InterceptRightClickPress);
            _bubble.RemoveHandler(PointerReleasedEvent, InterceptRightClickRelease);
        }
        DetachContentObservers();
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachTemplatePartHandlers()
    {
        if (_copyButton is not null)
        {
            _copyButton.Click -= OnCopyButtonClick;
            _copyButton.Click += OnCopyButtonClick;
        }

        if (_retryButton is not null)
        {
            _retryButton.Click -= OnRegenerateButtonClick;
            _retryButton.Click += OnRegenerateButtonClick;
        }

        if (_editButton is not null)
        {
            _editButton.Click -= OnEditButtonClick;
            _editButton.Click += OnEditButtonClick;
        }

        if (_saveButton is not null)
        {
            _saveButton.Click -= OnSaveButtonClick;
            _saveButton.Click += OnSaveButtonClick;
        }

        if (_cancelButton is not null)
        {
            _cancelButton.Click -= OnCancelButtonClick;
            _cancelButton.Click += OnCancelButtonClick;
        }

        if (_editBox is not null)
        {
            _editBox.RemoveHandler(KeyDownEvent, OnEditBoxKeyDown);
            _editBox.AddHandler(KeyDownEvent, OnEditBoxKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private void DetachTemplatePartHandlers()
    {
        if (_copyButton is not null) _copyButton.Click -= OnCopyButtonClick;
        if (_retryButton is not null) _retryButton.Click -= OnRegenerateButtonClick;
        if (_editButton is not null) _editButton.Click -= OnEditButtonClick;
        if (_saveButton is not null) _saveButton.Click -= OnSaveButtonClick;
        if (_cancelButton is not null) _cancelButton.Click -= OnCancelButtonClick;
        if (_editBox is not null) _editBox.RemoveHandler(KeyDownEvent, OnEditBoxKeyDown);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            var copy = await CopyMessageTextAsync();
            RaiseEvent(new StrataCopyRequestedEventArgs(CopyRequestedEvent, copy.Text, copy.IsSelection));
            CommandHelper.Execute(CopyCommand, CopyCommandParameter ?? copy.Text);
        }
    }

    private async void OnCopyButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var copy = await CopyMessageTextAsync();
        RaiseEvent(new StrataCopyRequestedEventArgs(CopyRequestedEvent, copy.Text, copy.IsSelection));
        CommandHelper.Execute(CopyCommand, CopyCommandParameter ?? copy.Text);
    }

    private void OnRegenerateButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(RegenerateRequestedEvent));
        CommandHelper.Execute(RegenerateCommand, RegenerateCommandParameter);
    }

    private void OnEditButtonClick(object? sender, RoutedEventArgs e) { e.Handled = true; BeginEdit(); }
    private void OnSaveButtonClick(object? sender, RoutedEventArgs e) { e.Handled = true; ConfirmEdit(); }
    private void OnCancelButtonClick(object? sender, RoutedEventArgs e) { e.Handled = true; CancelEdit(); }

    private void AttachContextMenu()
    {
        if (_bubble is null)
            return;

        _contextMenu ??= new ContextMenu();
        _contextMenu.Opening -= OnContextMenuOpening;
        _contextMenu.Opening += OnContextMenuOpening;
        _contextMenu.Closing -= OnContextMenuClosing;
        _contextMenu.Closing += OnContextMenuClosing;

        _bubble.ContextMenu = _contextMenu;
        ContextMenu = _contextMenu;
        ApplyMessageContextMenu(Content);

        // Snapshot selected text before child controls can react to the right-click,
        // then open the message-owned menu on release so whole-message actions remain available.
        _bubble.RemoveHandler(PointerPressedEvent, InterceptRightClickPress);
        _bubble.AddHandler(PointerPressedEvent, InterceptRightClickPress, RoutingStrategies.Tunnel);
        _bubble.RemoveHandler(PointerReleasedEvent, InterceptRightClickRelease);
        _bubble.AddHandler(PointerReleasedEvent, InterceptRightClickRelease, RoutingStrategies.Tunnel);
    }

    private void InterceptRightClickPress(object? sender, PointerPressedEventArgs e)
    {
        if (_contextMenu is null || _bubble is null)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        CaptureContextMenuSelection();
    }

    private void InterceptRightClickRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (_contextMenu is null || _bubble is null)
            return;

        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        // Defer to a child that owns its own context menu/flyout (e.g. a file attachment chip)
        // so its actions aren't shadowed by the whole-message menu.
        if (TargetHasOwnContextMenu(e.Source))
            return;

        e.Handled = true;
        RestoreContextMenuSelection();
        RebuildContextMenuItems();
        _contextMenu.Open(_bubble);
    }

    /// <summary>
    /// Walks from the right-click target up to (but excluding) the message bubble, reporting whether any
    /// embedded control on the way owns its own context flyout or a distinct context menu. Used so nested
    /// controls with bespoke right-click menus (e.g. file attachment chips) keep them instead of always
    /// getting the message-level menu. Text controls (<see cref="TextBlock"/>/<see cref="TextBox"/>) are
    /// skipped so their framework-default selection flyouts don't shadow the whole-message menu.
    /// </summary>
    private bool TargetHasOwnContextMenu(object? source)
    {
        var current = source as Visual;
        while (current is not null && !ReferenceEquals(current, _bubble) && !ReferenceEquals(current, this))
        {
            if (current is Control control && current is not TextBlock && current is not TextBox)
            {
                if (control.ContextFlyout is not null)
                    return true;

                if (control.ContextMenu is not null && !ReferenceEquals(control.ContextMenu, _contextMenu))
                    return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private void OnContextMenuOpening(object? sender, EventArgs e)
    {
        RestoreContextMenuSelection();
        RebuildContextMenuItems();
    }

    private void OnContextMenuClosing(object? sender, CancelEventArgs e)
    {
        _contextMenuSelectionSource = null;
        _contextMenuSelectionText = null;
        InvalidateContextMenu();
    }

    private void CaptureContextMenuSelection()
    {
        _contextMenuSelectionSource = null;
        _contextMenuSelectionText = null;

        var selectedBlock = FindSelectedTextBlock(Content);
        if (selectedBlock is null)
            return;

        _contextMenuSelectionSource = selectedBlock;
        _contextMenuSelectionStart = selectedBlock.SelectionStart;
        _contextMenuSelectionEnd = selectedBlock.SelectionEnd;
        _contextMenuSelectionText = selectedBlock.SelectedText;
    }

    private void RestoreContextMenuSelection()
    {
        if (_contextMenuSelectionSource is null || string.IsNullOrEmpty(_contextMenuSelectionText))
            return;

        if (!string.IsNullOrEmpty(_contextMenuSelectionSource.SelectedText))
            return;

        _contextMenuSelectionSource.SelectionStart = _contextMenuSelectionStart;
        _contextMenuSelectionSource.SelectionEnd = _contextMenuSelectionEnd;
    }

    private static SelectableTextBlock? FindSelectedTextBlock(object? content)
    {
        if (content is null)
            return null;

        if (content is SelectableTextBlock stb)
            return HasTextSelection(stb) ? stb : null;

        if (content is ContentControl contentControl)
        {
            var fromContent = FindSelectedTextBlock(contentControl.Content);
            if (fromContent is not null)
                return fromContent;
        }

        if (content is Decorator decorator)
        {
            var fromChild = FindSelectedTextBlock(decorator.Child);
            if (fromChild is not null)
                return fromChild;
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                var fromChild = FindSelectedTextBlock(child);
                if (fromChild is not null)
                    return fromChild;
            }
        }

        if (content is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var fromItem = FindSelectedTextBlock(item);
                if (fromItem is not null)
                    return fromItem;
            }
        }

        if (content is Control control)
        {
            foreach (var child in control.GetVisualChildren())
            {
                var fromVisual = FindSelectedTextBlock(child);
                if (fromVisual is not null)
                    return fromVisual;
            }
        }

        return null;
    }

    private static bool HasTextSelection(SelectableTextBlock stb)
        => stb.SelectionStart != stb.SelectionEnd && !string.IsNullOrEmpty(stb.SelectedText);

    private void RebuildContextMenuItems()
    {
        if (_contextMenu is null)
            return;

        var hasSelection = HasSelectedText();

        // Skip rebuild if state hasn't changed since last build.
        if (_contextMenuBuilt &&
            _lastMenuRole == Role &&
            _lastMenuIsEditing == IsEditing &&
            _lastMenuIsEditable == IsEditable &&
            _lastMenuIsStreaming == IsStreaming &&
            _lastMenuHasSelection == hasSelection)
            return;

        _lastMenuRole = Role;
        _lastMenuIsEditing = IsEditing;
        _lastMenuIsEditable = IsEditable;
        _lastMenuIsStreaming = IsStreaming;
        _lastMenuHasSelection = hasSelection;
        _contextMenuBuilt = true;

        var items = new List<object>();

        var copyItem = new MenuItem
        {
            Header = hasSelection ? "Copy selected text" : "Copy message",
            Icon = CreateMenuIcon("\uE8C8")
        };
        copyItem.Click += async (_, _) =>
        {
            var copy = await CopyMessageTextAsync();
            RaiseEvent(new StrataCopyRequestedEventArgs(CopyRequestedEvent, copy.Text, copy.IsSelection));
            CommandHelper.Execute(CopyCommand, CopyCommandParameter ?? copy.Text);
        };
        items.Add(copyItem);

        if (!IsEditing && Role is StrataChatRole.Assistant or StrataChatRole.Tool)
        {
            var copyTurnItem = new MenuItem { Header = "Copy assistant turn", Icon = CreateMenuIcon("\uE8C8") };
            copyTurnItem.Click += (_, _) =>
            {
                RaiseEvent(new RoutedEventArgs(CopyTurnRequestedEvent));
            };
            items.Add(copyTurnItem);
        }

        if (IsEditing)
        {
            if (IsEditable)
            {
                items.Add(new Separator());

                var saveItem = new MenuItem { Header = "Save", Icon = CreateMenuIcon("\uE74E") };
                saveItem.Click += (_, _) => ConfirmEdit();
                items.Add(saveItem);

                var cancelItem = new MenuItem { Header = "Cancel", Icon = CreateMenuIcon("\uE711") };
                cancelItem.Click += (_, _) => CancelEdit();
                items.Add(cancelItem);
            }
        }

        _contextMenu.ItemsSource = items;
    }

    private void InvalidateContextMenu()
    {
        _contextMenuBuilt = false;
    }

    private static TextBlock CreateMenuIcon(string glyph)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Width = 14,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private readonly record struct MessageCopyResult(string Text, bool IsSelection);

    private async Task<MessageCopyResult> CopyMessageTextAsync()
    {
        var copy = ExtractCopyText();
        var text = copy.Text;
        if (string.IsNullOrWhiteSpace(text))
            return copy;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is not null)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(text));
            await topLevel.Clipboard.SetDataAsync(data);
        }

        return copy;
    }

    private MessageCopyResult ExtractCopyText()
    {
        if (IsEditing && !string.IsNullOrWhiteSpace(EditText))
            return new MessageCopyResult(EditText!, false);

        var selected = _contextMenuSelectionText;
        if (string.IsNullOrEmpty(selected))
            selected = ChatContentExtractor.ExtractSelectedText(Content);
        if (!string.IsNullOrEmpty(selected))
            return new MessageCopyResult(selected, true);

        return new MessageCopyResult(ChatContentExtractor.ExtractText(Content).Trim(), false);
    }

    private bool HasSelectedText()
        => !string.IsNullOrEmpty(_contextMenuSelectionText)
           || !string.IsNullOrEmpty(ChatContentExtractor.ExtractSelectedText(Content));

    private void BeginEdit()
    {
        if (!UseInlineEdit)
        {
            RaiseEvent(new RoutedEventArgs(EditRequestedEvent));
            CommandHelper.Execute(EditCommand, EditCommandParameter);
            return;
        }

        // If EditText is already populated (e.g. via MVVM binding), use it as-is.
        // Only fall back to extracting from visual content when no binding provides the text.
        if (string.IsNullOrEmpty(EditText))
        {
            if (Content is StrataMarkdown markdown)
            {
                EditText = markdown.Markdown ?? string.Empty;
                _originalContentWasMarkdown = true;
            }
            else if (Content is TextBlock tb)
            {
                EditText = tb.Text ?? string.Empty;
                _originalContentWasMarkdown = false;
            }
            else
            {
                EditText = ChatContentExtractor.ExtractText(Content).Trim();
                _originalContentWasMarkdown = false;
            }
        }

        IsEditing = true;
        RaiseEvent(new RoutedEventArgs(EditRequestedEvent));
        CommandHelper.Execute(EditCommand, EditCommandParameter);

        // Focus the edit box and place cursor at end
        Dispatcher.UIThread.Post(() =>
        {
            if (_editBox is null) return;
            _editBox.Focus();
            _editBox.CaretIndex = _editBox.Text?.Length ?? 0;
        }, DispatcherPriority.Loaded);
    }

    private void ConfirmEdit()
    {
        var newText = EditText ?? string.Empty;

        if (ApplyEditToContent)
        {
            if (Content is StrataMarkdown existingMarkdown)
            {
                existingMarkdown.Markdown = newText;
            }
            else if (Content is SelectableTextBlock existingSelectableTextBlock)
            {
                existingSelectableTextBlock.Text = newText;
            }
            else if (Content is TextBlock existingTextBlock)
            {
                Content = CreateSelectableTextBlock(newText, existingTextBlock);
            }
            else if (_originalContentWasMarkdown)
            {
                Content = new StrataMarkdown
                {
                    Markdown = newText,
                    IsInline = true
                };
            }
            else
            {
                Content = CreateSelectableTextBlock(newText);
            }
        }

        IsEditing = false;
        RaiseEvent(new StrataEditConfirmedEventArgs(EditConfirmedEvent, newText));
        CommandHelper.Execute(EditConfirmedCommand, EditConfirmedCommandParameter ?? newText);
    }

    private void CancelEdit()
    {
        IsEditing = false;
    }

    private static SelectableTextBlock CreateSelectableTextBlock(string text, TextBlock? source = null)
    {
        var textBlock = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = source?.TextWrapping ?? TextWrapping.Wrap
        };

        if (source is null)
            return textBlock;

        textBlock.FontSize = source.FontSize;
        textBlock.FontFamily = source.FontFamily;
        textBlock.FontStyle = source.FontStyle;
        textBlock.FontWeight = source.FontWeight;
        textBlock.FontStretch = source.FontStretch;
        textBlock.Foreground = source.Foreground;
        textBlock.TextAlignment = source.TextAlignment;
        textBlock.LineHeight = source.LineHeight;
        textBlock.Margin = source.Margin;
        textBlock.FlowDirection = source.FlowDirection;
        textBlock.HorizontalAlignment = source.HorizontalAlignment;
        textBlock.VerticalAlignment = source.VerticalAlignment;
        textBlock.MinWidth = source.MinWidth;
        textBlock.MaxWidth = source.MaxWidth;

        foreach (var className in source.Classes)
            textBlock.Classes.Add(className);

        return textBlock;
    }

    private void OnEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        { e.Handled = true; CancelEdit(); }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { e.Handled = true; ConfirmEdit(); }
    }

    private void OnRoleChanged()
    {
        var role = Role;
        PseudoClasses.Set(":assistant", role == StrataChatRole.Assistant);
        PseudoClasses.Set(":user", role == StrataChatRole.User);
        PseudoClasses.Set(":system", role == StrataChatRole.System);
        PseudoClasses.Set(":tool", role == StrataChatRole.Tool);
        UpdateActionBarLayout();
        UpdateActionChromeMount();
        InvalidateContextMenu();
    }

    private void OnEditableChanged()
    {
        PseudoClasses.Set(":editable", IsEditable);
        UpdateActionBarLayout();
        UpdateActionChromeMount();
        InvalidateContextMenu();
    }

    private void OnHostScrollingChanged()
    {
        PseudoClasses.Set(":host-scrolling", IsHostScrolling);
        UpdateActionChromeMount();
        if (!IsHostScrolling)
        {
            UpdateActionBarLayout();
            InvalidateContextMenu();
        }
    }

    private void OnMetaChanged()
    {
        PseudoClasses.Set(":has-meta", !string.IsNullOrWhiteSpace(Author) || !string.IsNullOrWhiteSpace(Timestamp));
    }

    private void OnStatusChanged()
    {
        PseudoClasses.Set(":has-status", !string.IsNullOrWhiteSpace(StatusText));
    }

    private void OnEditingChanged()
    {
        UpdateEditAreaMount();
        PseudoClasses.Set(":editing", IsEditing);
        UpdateActionBarLayout();
        UpdateActionChromeMount();
        InvalidateContextMenu();

        // Auto-seed EditText from Content when entering edit mode via property
        if (IsEditing)
            SeedEditTextFromContent();
    }

    private void SeedEditTextFromContent()
    {
        if (!string.IsNullOrEmpty(EditText))
            return;

        if (Content is StrataMarkdown markdown)
        {
            EditText = markdown.Markdown ?? string.Empty;
            _originalContentWasMarkdown = true;
        }
        else if (Content is TextBlock tb)
        {
            EditText = tb.Text ?? string.Empty;
            _originalContentWasMarkdown = false;
        }
        else if (Content is not null)
        {
            EditText = ChatContentExtractor.ExtractText(Content).Trim();
            _originalContentWasMarkdown = false;
        }
    }

    private void OnContentChanged()
    {
        DetachContentObservers();
        AttachContentObserversAndApply(Content);
    }

    private void DetachContentObservers()
    {
        foreach (var textBlock in _observedTextBlocks)
            textBlock.PropertyChanged -= OnObservedTextBlockPropertyChanged;
        _observedTextBlocks.Clear();

        foreach (var markdown in _observedMarkdownControls)
            markdown.PropertyChanged -= OnObservedMarkdownPropertyChanged;
        _observedMarkdownControls.Clear();

        foreach (var target in _contextMenuTargets)
        {
            if (ReferenceEquals(target.ContextMenu, _contextMenu))
                target.ContextMenu = null;
        }
        _contextMenuTargets.Clear();
    }

    private void AttachContentObserversAndApply(object? content)
    {
        if (content is null)
            return;

        ApplyMessageContextMenu(content);

        if (content is TextBlock textBlock)
        {
            if (_observedTextBlocks.Add(textBlock))
                textBlock.PropertyChanged += OnObservedTextBlockPropertyChanged;
            ApplyDirectionalTextAlignment(textBlock);
            return;
        }

        if (content is StrataMarkdown markdown)
        {
            if (_observedMarkdownControls.Add(markdown))
                markdown.PropertyChanged += OnObservedMarkdownPropertyChanged;
            ApplyDirectionalMarkdownAlignment(markdown);
            return;
        }

        if (content is ContentControl contentControl)
        {
            AttachContentObserversAndApply(contentControl.Content);
            return;
        }

        if (content is Decorator decorator)
        {
            AttachContentObserversAndApply(decorator.Child);
            return;
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
                AttachContentObserversAndApply(child);
        }

        // Removed: full visual tree scan via GetVisualDescendants().
        // Known content types (TextBlock, StrataMarkdown, Panel, Decorator,
        // ContentControl) are handled above. Unknown controls don't need
        // directional-text observation.
    }

    private void ApplyMessageContextMenu(object? content)
    {
        if (_contextMenu is null || content is not Control control)
            return;

        if (control.ContextMenu is not null && !ReferenceEquals(control.ContextMenu, _contextMenu))
            return;

        control.ContextMenu = _contextMenu;
        _contextMenuTargets.Add(control);
    }

    private void OnObservedTextBlockPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is TextBlock textBlock && e.Property == TextBlock.TextProperty)
            ApplyDirectionalTextAlignment(textBlock);
    }

    private void OnObservedMarkdownPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is StrataMarkdown markdown && e.Property == StrataMarkdown.MarkdownProperty)
            ApplyDirectionalMarkdownAlignment(markdown);
    }

    private static void ApplyDirectionalTextAlignment(TextBlock textBlock)
    {
        var direction = StrataTextDirectionDetector.Detect(textBlock.Text);
        if (direction is null)
            return;

        if (textBlock.FlowDirection != direction.Value)
            textBlock.FlowDirection = direction.Value;

        var targetTextAlignment = direction.Value == FlowDirection.RightToLeft
            ? TextAlignment.Right
            : TextAlignment.Left;
        if (textBlock.TextAlignment != targetTextAlignment)
            textBlock.TextAlignment = targetTextAlignment;
    }

    private static void ApplyDirectionalMarkdownAlignment(StrataMarkdown markdown)
    {
        var direction = StrataTextDirectionDetector.Detect(markdown.Markdown);
        if (direction is null)
            return;

        if (markdown.FlowDirection != direction.Value)
            markdown.FlowDirection = direction.Value;
    }

    private void OnStreamingChanged()
    {
        PseudoClasses.Set(":streaming", IsStreaming);
        UpdateActionBarLayout();
        InvalidateContextMenu();
        if (IsStreaming) StartStreamPulse(); else StopStreamPulse();
    }

    /// <summary>Sets all pseudo-classes. Used only during initial template application.</summary>
    private void UpdateAllPseudoClasses()
    {
        var role = Role;
        PseudoClasses.Set(":assistant", role == StrataChatRole.Assistant);
        PseudoClasses.Set(":user", role == StrataChatRole.User);
        PseudoClasses.Set(":system", role == StrataChatRole.System);
        PseudoClasses.Set(":tool", role == StrataChatRole.Tool);
        PseudoClasses.Set(":streaming", IsStreaming);
        PseudoClasses.Set(":editing", IsEditing);
        PseudoClasses.Set(":editable", IsEditable);
        PseudoClasses.Set(":host-scrolling", IsHostScrolling);
        PseudoClasses.Set(":has-meta", !string.IsNullOrWhiteSpace(Author) || !string.IsNullOrWhiteSpace(Timestamp));
        PseudoClasses.Set(":has-status", !string.IsNullOrWhiteSpace(StatusText));
    }

    private void UpdateActionBarLayout(bool force = false)
    {
        var canShowActions = !IsEditing && !IsHostScrolling && Role != StrataChatRole.System;
        var showEdit = canShowActions && IsEditable;
        var showRetry = canShowActions && !IsStreaming && Role is StrataChatRole.Assistant or StrataChatRole.Tool;

        // Skip DOM updates when values haven't changed
        if (!force && showEdit == _cachedEditVisible && showRetry == _cachedRetryVisible)
            return;

        _cachedEditVisible = showEdit;
        _cachedRetryVisible = showRetry;

        if (_editButton is not null)
            _editButton.IsVisible = showEdit;

        if (_retryButton is not null)
            _retryButton.IsVisible = showRetry;

        if (_editSeparator is not null)
            _editSeparator.IsVisible = showEdit;

        if (_retrySeparator is not null)
            _retrySeparator.IsVisible = showRetry;
    }

    private void UpdateActionChromeMount()
    {
        if (_actionLayer is null)
            return;

        // Always keep the action bar child mounted so its Auto column width
        // stays constant. Visibility is controlled purely by Opacity and
        // IsHitTestVisible via XAML styles (:pointerover, :host-scrolling, etc.).
        if (_actionLayer.Child is null && _actionLayerChild is not null)
            _actionLayer.Child = _actionLayerChild;
    }

    private void UpdateEditAreaMount()
    {
        if (_editArea is null)
            return;

        if (IsEditing)
        {
            if (_editArea.Child is null && _editAreaChild is not null)
                _editArea.Child = _editAreaChild;
            return;
        }

        if (_editArea.Child is not null)
            _editArea.Child = null;
    }

    private void StartStreamPulse()
    {
        // Guard stale/asynchronous starts. This runs from the dispatcher (posted by OnApplyTemplate
        // and OnAttachedToVisualTree) and directly from OnStreamingChanged, so by the time it executes
        // the message may have stopped streaming or been detached by transcript virtualization.
        // Starting a Forever animation on a detached (off-tree) visual would orphan it — no parent
        // re-sync will ever stop it — so only start while still streaming AND attached.
        if (!IsStreaming || !_isAttachedToVisualTree || _streamBar is null) return;

        // The start/stop state machine is engaged (guard passed). Tracked separately from compositor
        // internals so the detach/guard contract is observable to tests via IsStreamPulseActiveForTest.
        _streamPulseActive = true;

        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null) return;

        var anim = visual.Compositor.CreateStableScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 0.3f);
        anim.InsertKeyFrame(0.5f, 1f);
        anim.InsertKeyFrame(1f, 0.3f);
        anim.Duration = TimeSpan.FromMilliseconds(1400);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopStreamPulse()
    {
        _streamPulseActive = false;
        if (_streamBar is null) return;
        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null) return;

        visual.StopAnimation("Opacity");
        visual.Opacity = 0f;
    }

    /// <summary>
    /// Test-only view of the streaming-pulse state machine: <see langword="true"/> once
    /// <see cref="StartStreamPulse"/> has engaged the Forever pulse (after its streaming/attachment
    /// guard) and <see langword="false"/> after <see cref="StopStreamPulse"/>. Exposed to
    /// <c>Lumi.Tests</c> via <c>InternalsVisibleTo</c> to assert the detach-stops-and-does-not-restart
    /// contract without reaching into compositor internals.
    /// </summary>
    internal bool IsStreamPulseActiveForTest => _streamPulseActive;
}
