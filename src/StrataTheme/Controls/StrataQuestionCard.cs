using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// An interactive question card that presents the user with predefined options
/// and an optional free-text reply. Designed for AI-driven question flows.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataQuestionCard Question="What language do you prefer?"
///                                Options="C#,Python,TypeScript"
///                                AllowFreeText="True"
///                                FreeTextPlaceholder="Or type your own..." /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_QuestionText (StrataMarkdown),
/// PART_OptionsPanel (Panel), PART_FreeTextBox (TextBox),
/// PART_FreeTextSubmit (Button), PART_MultiSubmit (Button), PART_ExpandButton (Button – the answered chip,
/// which opens the card), PART_CollapseButton (Button – closes an opened answered card).</para>
/// <para><b>Pseudo-classes:</b> :answered, :expanded, :expired, :has-free-text, :multi-select, :touch.</para>
/// </remarks>
public class StrataQuestionCard : TemplatedControl
{
    private Panel? _optionsPanel;
    private TextBox? _freeTextBox;
    private Button? _freeTextSubmit;
    private Button? _multiSubmit;
    private Button? _expandButton;
    private Button? _collapseButton;
    private readonly HashSet<string> _selectedOptions = new();
    private bool _hasInput;

    public static readonly DirectProperty<StrataQuestionCard, bool> HasInputProperty =
        AvaloniaProperty.RegisterDirect<StrataQuestionCard, bool>(
            nameof(HasInput), card => card.HasInput);

    public static readonly StyledProperty<string?> QuestionProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(Question));

    public static readonly StyledProperty<string?> OptionsProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(Options));

    public static readonly StyledProperty<IList<string>?> OptionsListProperty =
        AvaloniaProperty.Register<StrataQuestionCard, IList<string>?>(nameof(OptionsList));

    public static readonly StyledProperty<bool> AllowFreeTextProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(AllowFreeText), true);

    public static readonly StyledProperty<bool> AllowMultiSelectProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(AllowMultiSelect));

    public static readonly StyledProperty<string?> FreeTextPlaceholderProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(FreeTextPlaceholder), InteractiveLabels.Current["questionPlaceholder"]);

    public static readonly StyledProperty<string?> SelectedAnswerProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(SelectedAnswer));

    public static readonly StyledProperty<bool> IsAnsweredProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(IsAnswered));

    public static readonly StyledProperty<bool> IsExpiredProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(IsExpired));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> TouchModeProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(TouchMode));

    public static readonly DirectProperty<StrataQuestionCard, string?> QuestionLineProperty =
        AvaloniaProperty.RegisterDirect<StrataQuestionCard, string?>(nameof(QuestionLine), card => card.QuestionLine);

    private static readonly Regex MarkdownLink = new(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownCode = new(@"`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownStrong = new(@"(\*\*|__)(.+?)\1", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownEmphasis = new(@"(?<![\w*])\*(?!\s)([^*\n]+?)(?<!\s)\*(?![\w*])", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLineMarker = new(@"^(?:#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)", RegexOptions.CultureInvariant);
    private const string TypedAnswerClass = "typed-answer";

    private static readonly Regex CodePlaceholder = new("\u0001(\\d+)\u0001", RegexOptions.CultureInvariant);

    private string? _questionLine;

    static StrataQuestionCard()
    {
        QuestionProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.QuestionLine = ToPlainLine(c.Question));
        OptionsProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.RebuildOptions());
        OptionsListProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.RebuildOptions());
        IsAnsweredProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) =>
        {
            c.UpdatePseudoClasses();
            c.ApplyClosedState();
        });
        IsExpiredProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) =>
        {
            c.UpdatePseudoClasses();
            c.ApplyClosedState();
        });
        SelectedAnswerProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.ApplyClosedState());
        IsExpandedProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
        AllowFreeTextProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
        AllowMultiSelectProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
        TouchModeProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.OnTouchModeChanged());
    }

    /// <summary>The question text displayed as the card header.</summary>
    public string? Question { get => GetValue(QuestionProperty); set => SetValue(QuestionProperty, value); }

    /// <summary>Comma-separated list of option labels. Prefer <see cref="OptionsList"/> for options that may contain commas.</summary>
    public string? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }

    /// <summary>List of option labels. Takes precedence over <see cref="Options"/> when set.</summary>
    public IList<string>? OptionsList { get => GetValue(OptionsListProperty); set => SetValue(OptionsListProperty, value); }

    /// <summary>Whether to show a free-text input below the options.</summary>
    public bool AllowFreeText { get => GetValue(AllowFreeTextProperty); set => SetValue(AllowFreeTextProperty, value); }

    /// <summary>Whether the user can select multiple options before confirming.</summary>
    public bool AllowMultiSelect { get => GetValue(AllowMultiSelectProperty); set => SetValue(AllowMultiSelectProperty, value); }

    /// <summary>Placeholder text for the free-text input.</summary>
    public string? FreeTextPlaceholder { get => GetValue(FreeTextPlaceholderProperty); set => SetValue(FreeTextPlaceholderProperty, value); }

    /// <summary>The answer selected by the user (option text or free text).</summary>
    public string? SelectedAnswer { get => GetValue(SelectedAnswerProperty); set => SetValue(SelectedAnswerProperty, value); }

    /// <summary>Whether the user has submitted an answer.</summary>
    public bool IsAnswered { get => GetValue(IsAnsweredProperty); set => SetValue(IsAnsweredProperty, value); }

    /// <summary>Whether the question has expired (session stopped or user moved on).</summary>
    public bool IsExpired { get => GetValue(IsExpiredProperty); set => SetValue(IsExpiredProperty, value); }

    /// <summary>
    /// Whether an answered card shows the whole question and its options, with the answer marked, rather
    /// than its one-line chip. The reader opens it from the chip and closes it from the card.
    /// </summary>
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    /// <summary>
    /// Sizes the card for a finger rather than a pointer: every option becomes a 48px target at the body
    /// size and the reply field and its buttons grow to match. Sets the <c>:touch</c> pseudo-class.
    /// </summary>
    public bool TouchMode { get => GetValue(TouchModeProperty); set => SetValue(TouchModeProperty, value); }

    /// <summary>The question's first line as plain text, for the card's one-line answered form.</summary>
    public string? QuestionLine
    {
        get => _questionLine;
        private set => SetAndRaise(QuestionLineProperty, ref _questionLine, value);
    }

    /// <summary>
    /// The first line of a markdown question without its markdown (emphasis, code ticks, links, and
    /// heading, quote or list markers), for a line too short to render it.
    /// </summary>
    internal static string? ToPlainLine(string? markdown)
    {
        var line = markdown?.Split('\n').Select(static text => text.Trim()).FirstOrDefault(static text => text.Length > 0);
        if (line is null)
            return null;

        // Code keeps its text as written (__init__.py, **/*.cs): it is set aside while the rest loses its markdown.
        var code = new List<string>();
        line = MarkdownCode.Replace(line, match =>
        {
            code.Add(match.Groups[1].Value);
            return $"\u0001{code.Count - 1}\u0001";
        });
        line = MarkdownLineMarker.Replace(line, "");
        line = MarkdownLink.Replace(line, "$1");
        line = MarkdownStrong.Replace(line, "$2");
        line = MarkdownEmphasis.Replace(line, "$1");
        line = CodePlaceholder.Replace(line, match => code[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)]);
        return line.Trim();
    }

    /// <summary>Whether the card contains free text or selected options.</summary>
    public bool HasInput => _hasInput;

    /// <summary>Raised when the user selects an option or submits free text.</summary>
    public event EventHandler<string>? AnswerSubmitted;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        // Unsubscribe from old template parts
        if (_freeTextSubmit is not null)
            _freeTextSubmit.Click -= OnFreeTextSubmitClick;
        if (_freeTextBox is not null)
        {
            _freeTextBox.KeyDown -= OnFreeTextKeyDown;
            _freeTextBox.PropertyChanged -= OnFreeTextBoxPropertyChanged;
        }
        if (_multiSubmit is not null)
            _multiSubmit.Click -= OnMultiSubmitClick;
        if (_expandButton is not null)
            _expandButton.Click -= OnExpandClick;
        if (_collapseButton is not null)
            _collapseButton.Click -= OnCollapseClick;

        base.OnApplyTemplate(e);

        _optionsPanel = e.NameScope.Find<Panel>("PART_OptionsPanel");
        _freeTextBox = e.NameScope.Find<TextBox>("PART_FreeTextBox");
        _freeTextSubmit = e.NameScope.Find<Button>("PART_FreeTextSubmit");
        _multiSubmit = e.NameScope.Find<Button>("PART_MultiSubmit");
        _expandButton = e.NameScope.Find<Button>("PART_ExpandButton");
        _collapseButton = e.NameScope.Find<Button>("PART_CollapseButton");

        if (_freeTextSubmit is not null)
            _freeTextSubmit.Click += OnFreeTextSubmitClick;

        if (_freeTextBox is not null)
            _freeTextBox.KeyDown += OnFreeTextKeyDown;

        if (_multiSubmit is not null)
            _multiSubmit.Click += OnMultiSubmitClick;

        if (_expandButton is not null)
            _expandButton.Click += OnExpandClick;

        if (_collapseButton is not null)
            _collapseButton.Click += OnCollapseClick;

        RebuildOptions();
        UpdatePseudoClasses();
        SyncFreeTextListener();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Keep handlers wired while detached. These controls are template children that share this
        // control's lifetime, and a plain re-attach does not call OnApplyTemplate again.
        base.OnDetachedFromVisualTree(e);
    }

    private void SyncFreeTextListener()
    {
        if (_freeTextBox is not null)
        {
            _freeTextBox.PropertyChanged -= OnFreeTextBoxPropertyChanged;
            _freeTextBox.PropertyChanged += OnFreeTextBoxPropertyChanged;
        }
        UpdateMultiSubmitVisibility();
    }

    private void OnFreeTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
            UpdateMultiSubmitVisibility();
    }

    private void RebuildOptions()
    {
        if (_optionsPanel is null) return;

        foreach (var child in _optionsPanel.Children)
        {
            if (child is Button btn)
                btn.Click -= OnOptionClick;
        }
        _optionsPanel.Children.Clear();

        // Prefer OptionsList (proper list) over Options (comma-separated string)
        IEnumerable<string>? items = OptionsList;
        if (items is null)
        {
            var options = Options;
            if (string.IsNullOrWhiteSpace(options)) return;
            items = options.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
        }

        foreach (var opt in items)
        {
            if (string.IsNullOrEmpty(opt)) continue;

            var btn = CreateOptionRow(opt);
            btn.Tag = opt;
            btn.Click += OnOptionClick;
            _optionsPanel.Children.Add(btn);
        }

        ApplyClosedState();
    }

    private Button CreateOptionRow(string text)
    {
        var row = new Button
        {
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
            },
            Classes = { "question-option" },
        };
        // The options are built here rather than by the template, so they carry the card's touch sizing.
        row.Classes.Set("touch", TouchMode);
        ToolTip.SetTip(row, text);
        ToolTip.SetShowDelay(row, 500);
        return row;
    }

    /// <summary>
    /// Settles a closed card (answered or expired): no option can be picked any more, and the ones the answer
    /// chose are marked — an answer typed in the user's own words as a chosen row after them — so an answered
    /// card opened again reads as it was answered, however its answer arrived.
    /// </summary>
    private void ApplyClosedState()
    {
        if (!IsAnswered && !IsExpired)
            return;

        if (_optionsPanel is not null)
        {
            for (var i = _optionsPanel.Children.Count - 1; i >= 0; i--)
            {
                if (_optionsPanel.Children[i].Classes.Contains(TypedAnswerClass))
                    _optionsPanel.Children.RemoveAt(i);
            }

            var options = _optionsPanel.Children.OfType<Button>().Select(static row => row.Tag).OfType<string>().ToHashSet();
            var chosen = IsAnswered ? ChosenAnswers(options) : [];
            foreach (var row in _optionsPanel.Children.OfType<Button>())
            {
                row.IsEnabled = false;
                row.Classes.Set("selected", row.Tag is string option && chosen.Contains(option));
            }

            var typed = chosen.Where(answer => !options.Contains(answer)).ToList();
            if (typed.Count > 0)
            {
                var row = CreateOptionRow(string.Join(", ", typed));
                row.Classes.Add("selected");
                row.Classes.Add(TypedAnswerClass);
                row.IsEnabled = false;
                _optionsPanel.Children.Add(row);
            }
        }

        if (_freeTextBox is not null)
            _freeTextBox.IsEnabled = false;
        if (_freeTextSubmit is not null)
            _freeTextSubmit.IsEnabled = false;
        // The confirm button's visibility is set locally while picking, which outranks the styles.
        UpdateMultiSubmitVisibility();
    }

    /// <summary>
    /// What the answer chose: the options picked here, or for an answer read back, the answer whole when it is
    /// an option, otherwise its parts (a multi-select answer joins them with ", ").
    /// </summary>
    private List<string> ChosenAnswers(HashSet<string> options)
    {
        if (_selectedOptions.Count > 0)
            return _selectedOptions.ToList();

        var answer = SelectedAnswer?.Trim();
        if (string.IsNullOrEmpty(answer))
            return [];
        if (options.Contains(answer))
            return [answer];
        return answer.Split(", ").Select(static part => part.Trim()).Where(static part => part.Length > 0).Distinct().ToList();
    }

    private void OnExpandClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => IsExpanded = true;

    private void OnCollapseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => IsExpanded = false;

    private void OnOptionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsAnswered || IsExpired) return;
        if (sender is not Button btn || btn.Tag is not string opt) return;

        if (AllowMultiSelect)
        {
            if (_selectedOptions.Contains(opt))
            {
                _selectedOptions.Remove(opt);
                btn.Classes.Remove("selected");
            }
            else
            {
                _selectedOptions.Add(opt);
                btn.Classes.Add("selected");
            }
            UpdateMultiSubmitVisibility();
        }
        else
        {
            SubmitAnswer(opt);
        }
    }

    private void OnMultiSubmitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsAnswered || IsExpired) return;

        // Include any pending free text that wasn't explicitly added yet
        var pendingText = _freeTextBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(pendingText))
            _selectedOptions.Add(pendingText);

        if (_selectedOptions.Count == 0) return;
        SubmitAnswer(string.Join(", ", _selectedOptions));
    }

    private bool HasPendingFreeText => !string.IsNullOrWhiteSpace(_freeTextBox?.Text);

    private void UpdateMultiSubmitVisibility()
    {
        SetAndRaise(HasInputProperty, ref _hasInput,
            _selectedOptions.Count > 0 || !string.IsNullOrEmpty(_freeTextBox?.Text));
        if (_multiSubmit is not null)
            _multiSubmit.IsVisible = AllowMultiSelect && (_selectedOptions.Count > 0 || HasPendingFreeText) && !IsAnswered && !IsExpired;
    }

    private void OnFreeTextSubmitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SubmitFreeText();
    }

    private void OnFreeTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            SubmitFreeText();
        }
    }

    private void SubmitFreeText()
    {
        if (IsAnswered || IsExpired) return;
        var text = _freeTextBox?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (AllowMultiSelect)
        {
            // In multi-select mode, free text is combined with selected options via confirm button
            _selectedOptions.Add(text);
            _freeTextBox!.Text = "";
            UpdateMultiSubmitVisibility();
        }
        else
        {
            SubmitAnswer(text);
        }
    }

    private void SubmitAnswer(string answer)
    {
        SelectedAnswer = answer;
        IsAnswered = true;
        AnswerSubmitted?.Invoke(this, answer);
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":answered", IsAnswered);
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":expired", IsExpired);
        PseudoClasses.Set(":has-free-text", AllowFreeText);
        PseudoClasses.Set(":multi-select", AllowMultiSelect);
        PseudoClasses.Set(":touch", TouchMode);
    }

    private void OnTouchModeChanged()
    {
        UpdatePseudoClasses();
        if (_optionsPanel is null) return;

        foreach (var child in _optionsPanel.Children)
        {
            if (child is Button option)
                option.Classes.Set("touch", TouchMode);
        }
    }
}
