using System;
using System.Linq;
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
    private Border? _streamBar;
    private Border? _bubble;
    private TextBox? _editBox;

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

        var copyBtn = e.NameScope.Find<Button>("PART_CopyButton");
        var regenBtn = e.NameScope.Find<Button>("PART_RegenerateButton");
        var editBtn = e.NameScope.Find<Button>("PART_EditButton");
        var saveBtn = e.NameScope.Find<Button>("PART_SaveButton");
        var cancelBtn = e.NameScope.Find<Button>("PART_CancelButton");

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
        if (IsStreaming)
            Dispatcher.UIThread.Post(StartStreamPulse, DispatcherPriority.Loaded);
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

        var copyItem = new MenuItem { Header = "Copy" };
        copyItem.Click += async (_, _) =>
        {
            await CopyMessageTextAsync();
            RaiseEvent(new RoutedEventArgs(CopyRequestedEvent));
        };

        var menu = new ContextMenu
        {
            ItemsSource = new object[] { copyItem }
        };

        _bubble.ContextMenu = menu;
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
        // Extract plain text from content for editing
        if (Content is TextBlock tb)
            EditText = tb.Text;
        else
            EditText = Content?.ToString() ?? string.Empty;

        IsEditing = true;
        RaiseEvent(new RoutedEventArgs(EditRequestedEvent));

        // Focus the edit box
        Dispatcher.UIThread.Post(() => _editBox?.Focus(), DispatcherPriority.Loaded);
    }

    private void ConfirmEdit()
    {
        if (ApplyEditToContent && EditText is not null)
        {
            if (Content is TextBlock existingTextBlock)
            {
                existingTextBlock.Text = EditText;
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
