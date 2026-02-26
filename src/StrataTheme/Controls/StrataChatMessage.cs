using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

/// <summary>
/// A chat message bubble with role-dependent styling, hover toolbar (Copy / Edit / Retry),
/// inline edit mode, and streaming indicator. Supports any content (text, markdown, controls).
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataChatMessage Role="User"&gt;
///     &lt;TextBlock Text="Hello, world!" TextWrapping="Wrap" /&gt;
/// &lt;/controls:StrataChatMessage&gt;
///
/// &lt;controls:StrataChatMessage Role="Assistant" IsStreaming="True"&gt;
///     &lt;controls:StrataMarkdown Markdown="{Binding ResponseMarkdown}" IsInline="True" /&gt;
/// &lt;/controls:StrataChatMessage&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Bubble (Border), PART_EditArea (Border), PART_EditBox (TextBox),
/// PART_StreamBar (Border), PART_ActionBar (StackPanel), PART_CopyButton (Button),
/// PART_EditButton (Button), PART_RegenerateButton (Button), PART_SaveButton (Button), PART_CancelButton (Button).</para>
/// <para><b>Pseudo-classes:</b> :assistant, :user, :system, :tool, :streaming, :editing, :editable, :has-meta, :has-status.</para>
/// </remarks>
public class StrataChatMessage : TemplatedControl
{
    private const int DirectionScanLimitChars = 384;

    private enum TextDirection
    {
        Neutral,
        LeftToRight,
        RightToLeft
    }

    private Border? _streamBar;
    private Border? _bubble;
    private Border? _editSeparator;
    private Border? _retrySeparator;
    private TextBox? _editBox;
    private TextBlock? _editHint;
    private Button? _editButton;
    private Button? _retryButton;
    private ContextMenu? _contextMenu;
    private readonly HashSet<TextBlock> _observedTextBlocks = new();
    private readonly HashSet<StrataMarkdown> _observedMarkdownControls = new();
    private bool _originalContentWasMarkdown;

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

    /// <summary>Message content. Can be any control, TextBlock, or StrataMarkdown.</summary>
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

    /// <summary>Text value of the edit box when editing.</summary>
    public static readonly StyledProperty<string?> EditTextProperty =
        AvaloniaProperty.Register<StrataChatMessage, string?>(nameof(EditText));

    /// <summary>When true (default), confirming an edit writes EditText back into Content.</summary>
    public static readonly StyledProperty<bool> ApplyEditToContentProperty =
        AvaloniaProperty.Register<StrataChatMessage, bool>(nameof(ApplyEditToContent), true);

    public static readonly RoutedEvent<RoutedEventArgs> CopyRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(CopyRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> RegenerateRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(RegenerateRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> EditRequestedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(EditRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> EditConfirmedEvent =
        RoutedEvent.Register<StrataChatMessage, RoutedEventArgs>(nameof(EditConfirmed), RoutingStrategies.Bubble);

    static StrataChatMessage()
    {
        RoleProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.UpdatePseudoClasses());
        ContentProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnContentChanged());
        IsStreamingProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnStreamingChanged());
        IsEditingProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.OnEditingChanged());
        IsEditableProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.UpdatePseudoClasses());
        AuthorProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.UpdatePseudoClasses());
        TimestampProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.UpdatePseudoClasses());
        StatusTextProperty.Changed.AddClassHandler<StrataChatMessage>((c, _) => c.UpdatePseudoClasses());
    }

    public event EventHandler<RoutedEventArgs>? CopyRequested
    { add => AddHandler(CopyRequestedEvent, value); remove => RemoveHandler(CopyRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? RegenerateRequested
    { add => AddHandler(RegenerateRequestedEvent, value); remove => RemoveHandler(RegenerateRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? EditRequested
    { add => AddHandler(EditRequestedEvent, value); remove => RemoveHandler(EditRequestedEvent, value); }
    public event EventHandler<RoutedEventArgs>? EditConfirmed
    { add => AddHandler(EditConfirmedEvent, value); remove => RemoveHandler(EditConfirmedEvent, value); }

    public StrataChatRole Role { get => GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public string Author { get => GetValue(AuthorProperty); set => SetValue(AuthorProperty, value); }
    public string Timestamp { get => GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    public string StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public bool IsStreaming { get => GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public bool IsEditable { get => GetValue(IsEditableProperty); set => SetValue(IsEditableProperty, value); }
    public bool IsEditing { get => GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }
    public string? EditText { get => GetValue(EditTextProperty); set => SetValue(EditTextProperty, value); }
    public bool ApplyEditToContent { get => GetValue(ApplyEditToContentProperty); set => SetValue(ApplyEditToContentProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _streamBar = e.NameScope.Find<Border>("PART_StreamBar");
        _bubble = e.NameScope.Find<Border>("PART_Bubble");
        _editBox = e.NameScope.Find<TextBox>("PART_EditBox");
        _editHint = e.NameScope.Find<TextBlock>("PART_EditHint");

        var copyBtn = e.NameScope.Find<Button>("PART_CopyButton");
        var regenBtn = e.NameScope.Find<Button>("PART_RegenerateButton");
        var editBtn = e.NameScope.Find<Button>("PART_EditButton");
        var saveBtn = e.NameScope.Find<Button>("PART_SaveButton");
        var cancelBtn = e.NameScope.Find<Button>("PART_CancelButton");
        _editSeparator = e.NameScope.Find<Border>("PART_EditSep");
        _retrySeparator = e.NameScope.Find<Border>("PART_RegenerateSep");
        _editButton = editBtn;
        _retryButton = regenBtn;

        if (copyBtn is not null)
            copyBtn.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await CopyMessageTextAsync();
                RaiseEvent(new RoutedEventArgs(CopyRequestedEvent));
            };
        if (regenBtn is not null)
            regenBtn.Click += (_, ev) => { ev.Handled = true; RaiseEvent(new RoutedEventArgs(RegenerateRequestedEvent)); };
        if (editBtn is not null)
            editBtn.Click += (_, ev) => { ev.Handled = true; BeginEdit(); };
        if (saveBtn is not null)
            saveBtn.Click += (_, ev) => { ev.Handled = true; ConfirmEdit(); };
        if (cancelBtn is not null)
            cancelBtn.Click += (_, ev) => { ev.Handled = true; CancelEdit(); };

        if (_editBox is not null)
            _editBox.KeyDown += OnEditBoxKeyDown;

        AttachContextMenu();

        UpdatePseudoClasses();
        OnContentChanged();
        UpdateActionBarLayout();
        if (IsStreaming)
            Dispatcher.UIThread.Post(StartStreamPulse, DispatcherPriority.Loaded);

        // Seed EditText when entering editing from XAML (Content may not be set
        // yet when IsEditing fires during initialization).
        if (IsEditing)
            Dispatcher.UIThread.Post(SeedEditTextFromContent, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachContentObservers();
        base.OnDetachedFromVisualTree(e);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await CopyMessageTextAsync();
            RaiseEvent(new RoutedEventArgs(CopyRequestedEvent));
        }
    }

    private void AttachContextMenu()
    {
        if (_bubble is null)
            return;

        _contextMenu ??= new ContextMenu();
        _contextMenu.Opening -= OnContextMenuOpening;
        _contextMenu.Opening += OnContextMenuOpening;

        RebuildContextMenuItems();
        _bubble.ContextMenu = _contextMenu;
        ContextMenu = _contextMenu;
    }

    private void OnContextMenuOpening(object? sender, EventArgs e)
    {
        RebuildContextMenuItems();
    }

    private void RebuildContextMenuItems()
    {
        if (_contextMenu is null)
            return;

        var items = new List<object>();

        var copyItem = new MenuItem { Header = "Copy", Icon = CreateMenuIcon("\uE8C8") };
        copyItem.Click += async (_, _) =>
        {
            await CopyMessageTextAsync();
            RaiseEvent(new RoutedEventArgs(CopyRequestedEvent));
        };
        items.Add(copyItem);

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

            _contextMenu.ItemsSource = items;
            return;
        }

        if (IsEditable && Role != StrataChatRole.System)
        {
            items.Add(new Separator());

            var editItem = new MenuItem { Header = "Edit", Icon = CreateMenuIcon("\uE70F") };
            editItem.Click += (_, _) => BeginEdit();
            items.Add(editItem);
        }

        if (!IsStreaming && Role is StrataChatRole.Assistant or StrataChatRole.Tool)
        {
            if (items.Count > 0 && items[^1] is not Separator)
                items.Add(new Separator());

            var retryItem = new MenuItem { Header = "Retry", Icon = CreateMenuIcon("\uE72C") };
            retryItem.Click += (_, _) => RaiseEvent(new RoutedEventArgs(RegenerateRequestedEvent));
            items.Add(retryItem);
        }

        _contextMenu.ItemsSource = items;
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

    private async Task CopyMessageTextAsync()
    {
        var text = ExtractCopyText();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(text);
    }

    private string ExtractCopyText()
    {
        if (IsEditing && !string.IsNullOrWhiteSpace(EditText))
            return EditText!;

        return ExtractObjectText(Content).Trim();
    }

    private static string ExtractObjectText(object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is string text)
            return text;

        if (value is TextBlock textBlock)
            return textBlock.Text ?? string.Empty;

        if (value is StrataMarkdown markdown)
            return markdown.Markdown ?? string.Empty;

        if (value is StrataAiToolCall toolCall)
            return $"{toolCall.ToolName} | {toolCall.StatusText} | {toolCall.MoreInfo}";

        if (value is StrataAiSkill skill)
            return $"{skill.SkillName}\n{skill.Description}\n{skill.DetailMarkdown}";

        if (value is StrataAiAgent agent)
            return $"{agent.AgentName}\n{agent.Description}\n{agent.DetailMarkdown}";

        if (value is ContentControl contentControl)
            return ExtractObjectText(contentControl.Content);

        if (value is Decorator decorator)
            return ExtractObjectText(decorator.Child);

        if (value is Panel panel)
        {
            var lines = panel.Children
                .Select(child => ExtractObjectText(child))
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join(Environment.NewLine, lines);
        }

        return value.ToString() ?? string.Empty;
    }

    private void BeginEdit()
    {
        // Extract text from content, handling StrataMarkdown properly
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
            EditText = ExtractObjectText(Content).Trim();
            _originalContentWasMarkdown = false;
        }

        IsEditing = true;
        RaiseEvent(new RoutedEventArgs(EditRequestedEvent));

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
        if (ApplyEditToContent && EditText is not null)
        {
            if (Content is StrataMarkdown existingMarkdown)
            {
                existingMarkdown.Markdown = EditText;
            }
            else if (Content is TextBlock existingTextBlock)
            {
                existingTextBlock.Text = EditText;
            }
            else if (_originalContentWasMarkdown)
            {
                Content = new StrataMarkdown
                {
                    Markdown = EditText,
                    IsInline = true
                };
            }
            else
            {
                Content = new TextBlock
                {
                    Text = EditText,
                    TextWrapping = TextWrapping.Wrap
                };
            }
        }

        IsEditing = false;
        RaiseEvent(new RoutedEventArgs(EditConfirmedEvent));
    }

    private void CancelEdit()
    {
        IsEditing = false;
    }

    private void OnEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        { e.Handled = true; CancelEdit(); }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { e.Handled = true; ConfirmEdit(); }
    }

    private void OnEditingChanged()
    {
        UpdatePseudoClasses();

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
            EditText = ExtractObjectText(Content).Trim();
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
    }

    private void AttachContentObserversAndApply(object? content)
    {
        if (content is null)
            return;

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
            return;
        }

        if (content is Control control)
        {
            foreach (var child in control.GetVisualDescendants().OfType<Control>())
                AttachContentObserversAndApply(child);
        }
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
        var direction = DetectLeadingDirection(textBlock.Text);
        if (direction == TextDirection.Neutral)
            return;

        var targetFlowDirection = direction == TextDirection.RightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        if (textBlock.FlowDirection != targetFlowDirection)
            textBlock.FlowDirection = targetFlowDirection;

        var targetTextAlignment = direction == TextDirection.RightToLeft
            ? TextAlignment.Right
            : TextAlignment.Left;
        if (textBlock.TextAlignment != targetTextAlignment)
            textBlock.TextAlignment = targetTextAlignment;
    }

    private static void ApplyDirectionalMarkdownAlignment(StrataMarkdown markdown)
    {
        var direction = DetectLeadingDirection(markdown.Markdown);
        if (direction == TextDirection.Neutral)
            return;

        var targetFlowDirection = direction == TextDirection.RightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        if (markdown.FlowDirection != targetFlowDirection)
            markdown.FlowDirection = targetFlowDirection;
    }

    private static TextDirection DetectLeadingDirection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TextDirection.Neutral;

        var firstStrongDirection = TextDirection.Neutral;
        var rtlStrongCount = 0;
        var ltrStrongCount = 0;
        var scannedChars = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            scannedChars += rune.Utf16SequenceLength;
            if (scannedChars > DirectionScanLimitChars)
                break;

            if (rune.Value <= 0x7F)
            {
                var ascii = (char)rune.Value;
                if (char.IsWhiteSpace(ascii) || char.IsDigit(ascii) || char.IsPunctuation(ascii) || char.IsSymbol(ascii))
                    continue;

                if ((ascii >= 'A' && ascii <= 'Z') || (ascii >= 'a' && ascii <= 'z'))
                {
                    if (firstStrongDirection == TextDirection.Neutral)
                        firstStrongDirection = TextDirection.LeftToRight;
                    ltrStrongCount++;
                    continue;
                }
            }

            var category = Rune.GetUnicodeCategory(rune);

            if (category is UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol
                or UnicodeCategory.DecimalDigitNumber)
            {
                continue;
            }

            if (IsStrongRtlRune(rune.Value))
            {
                if (firstStrongDirection == TextDirection.Neutral)
                    firstStrongDirection = TextDirection.RightToLeft;
                rtlStrongCount++;
                continue;
            }

            if (category is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter)
            {
                if (firstStrongDirection == TextDirection.Neutral)
                    firstStrongDirection = TextDirection.LeftToRight;
                ltrStrongCount++;
                continue;
            }
        }

        if (rtlStrongCount == 0 && ltrStrongCount == 0)
            return TextDirection.Neutral;

        if (rtlStrongCount == ltrStrongCount)
            return firstStrongDirection == TextDirection.Neutral
                ? TextDirection.LeftToRight
                : firstStrongDirection;

        return rtlStrongCount > ltrStrongCount
            ? TextDirection.RightToLeft
            : TextDirection.LeftToRight;
    }

    private static bool IsStrongRtlRune(int codePoint)
    {
        return (codePoint >= 0x0590 && codePoint <= 0x05FF) // Hebrew
               || (codePoint >= 0x0600 && codePoint <= 0x06FF) // Arabic
               || (codePoint >= 0x0700 && codePoint <= 0x08FF) // Syriac/Arabic supplements
               || (codePoint >= 0xFB1D && codePoint <= 0xFDFF) // Hebrew/Arabic presentation forms A
               || (codePoint >= 0xFE70 && codePoint <= 0xFEFF) // Arabic presentation forms B
               || (codePoint >= 0x1EE00 && codePoint <= 0x1EEFF); // Arabic Mathematical Alphabetic Symbols
    }

    private void OnStreamingChanged()
    {
        UpdatePseudoClasses();
        if (IsStreaming) StartStreamPulse(); else StopStreamPulse();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":assistant", Role == StrataChatRole.Assistant);
        PseudoClasses.Set(":user", Role == StrataChatRole.User);
        PseudoClasses.Set(":system", Role == StrataChatRole.System);
        PseudoClasses.Set(":tool", Role == StrataChatRole.Tool);
        PseudoClasses.Set(":streaming", IsStreaming);
        PseudoClasses.Set(":editing", IsEditing);
        PseudoClasses.Set(":editable", IsEditable);
        PseudoClasses.Set(":has-meta", !string.IsNullOrWhiteSpace(Author) || !string.IsNullOrWhiteSpace(Timestamp));
        PseudoClasses.Set(":has-status", !string.IsNullOrWhiteSpace(StatusText));

        UpdateActionBarLayout();
    }

    private void UpdateActionBarLayout()
    {
        var canShowActions = !IsEditing && Role != StrataChatRole.System;
        var showEdit = canShowActions && IsEditable;
        var showRetry = canShowActions && !IsStreaming && Role is StrataChatRole.Assistant or StrataChatRole.Tool;

        if (_editButton is not null)
            _editButton.IsVisible = showEdit;

        if (_retryButton is not null)
            _retryButton.IsVisible = showRetry;

        if (_editSeparator is not null)
            _editSeparator.IsVisible = showEdit;

        if (_retrySeparator is not null)
            _retrySeparator.IsVisible = showRetry;
    }

    private void StartStreamPulse()
    {
        if (_streamBar is null) return;
        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null) return;

        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
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
        if (_streamBar is null) return;
        var visual = ElementComposition.GetElementVisual(_streamBar);
        if (visual is null) return;

        var r = visual.Compositor.CreateScalarKeyFrameAnimation();
        r.Target = "Opacity";
        r.InsertKeyFrame(0f, 0f);
        r.Duration = TimeSpan.FromMilliseconds(1);
        r.IterationBehavior = AnimationIterationBehavior.Count;
        r.IterationCount = 1;
        visual.StartAnimation("Opacity", r);
    }
}
