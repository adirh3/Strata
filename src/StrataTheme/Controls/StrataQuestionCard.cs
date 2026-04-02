using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

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
/// <para><b>Template parts:</b> PART_Root (Border), PART_Stratum (Border),
/// PART_OptionsPanel (WrapPanel), PART_FreeTextBox (TextBox),
/// PART_FreeTextSubmit (Button), PART_MultiSubmit (Button).</para>
/// <para><b>Pseudo-classes:</b> :answered, :expired, :has-free-text, :multi-select.</para>
/// </remarks>
public class StrataQuestionCard : TemplatedControl
{
    private WrapPanel? _optionsPanel;
    private TextBox? _freeTextBox;
    private Button? _freeTextSubmit;
    private Button? _multiSubmit;
    private readonly HashSet<string> _selectedOptions = new();

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
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(FreeTextPlaceholder), "Type your answer...");

    public static readonly StyledProperty<string?> SelectedAnswerProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(SelectedAnswer));

    public static readonly StyledProperty<bool> IsAnsweredProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(IsAnswered));

    public static readonly StyledProperty<bool> IsExpiredProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(IsExpired));

    static StrataQuestionCard()
    {
        OptionsProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.RebuildOptions());
        OptionsListProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.RebuildOptions());
        IsAnsweredProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
        IsExpiredProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.OnIsExpiredChanged());
        AllowFreeTextProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
        AllowMultiSelectProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
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

        base.OnApplyTemplate(e);

        _optionsPanel = e.NameScope.Find<WrapPanel>("PART_OptionsPanel");
        _freeTextBox = e.NameScope.Find<TextBox>("PART_FreeTextBox");
        _freeTextSubmit = e.NameScope.Find<Button>("PART_FreeTextSubmit");
        _multiSubmit = e.NameScope.Find<Button>("PART_MultiSubmit");

        if (_freeTextSubmit is not null)
            _freeTextSubmit.Click += OnFreeTextSubmitClick;

        if (_freeTextBox is not null)
            _freeTextBox.KeyDown += OnFreeTextKeyDown;

        if (_multiSubmit is not null)
            _multiSubmit.Click += OnMultiSubmitClick;

        RebuildOptions();
        UpdatePseudoClasses();
        SyncFreeTextListener();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_freeTextSubmit is not null)
            _freeTextSubmit.Click -= OnFreeTextSubmitClick;
        if (_freeTextBox is not null)
        {
            _freeTextBox.KeyDown -= OnFreeTextKeyDown;
            _freeTextBox.PropertyChanged -= OnFreeTextBoxPropertyChanged;
        }
        if (_multiSubmit is not null)
            _multiSubmit.Click -= OnMultiSubmitClick;
        if (_optionsPanel is not null)
        {
            foreach (var child in _optionsPanel.Children)
            {
                if (child is Button btn)
                    btn.Click -= OnOptionClick;
            }
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void SyncFreeTextListener()
    {
        if (_freeTextBox is not null)
        {
            _freeTextBox.PropertyChanged -= OnFreeTextBoxPropertyChanged;
            if (AllowMultiSelect)
                _freeTextBox.PropertyChanged += OnFreeTextBoxPropertyChanged;
        }
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

            var btn = new Button
            {
                Content = opt,
                Classes = { "question-option" },
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Tag = opt,
            };
            btn.Click += OnOptionClick;
            _optionsPanel.Children.Add(btn);
        }
    }

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
        if (_multiSubmit is not null)
            _multiSubmit.IsVisible = AllowMultiSelect && (_selectedOptions.Count > 0 || HasPendingFreeText) && !IsAnswered;
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

        // Disable all option buttons and highlight selected ones
        if (_optionsPanel is not null)
        {
            foreach (var child in _optionsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.IsEnabled = false;
                    if (btn.Tag is string tag)
                    {
                        if (AllowMultiSelect ? _selectedOptions.Contains(tag) : tag == answer)
                            btn.Classes.Add("selected");
                    }
                }
            }
        }

        // Disable free text
        if (_freeTextBox is not null)
            _freeTextBox.IsEnabled = false;
        if (_freeTextSubmit is not null)
            _freeTextSubmit.IsEnabled = false;

        AnswerSubmitted?.Invoke(this, answer);
    }

    private void OnIsExpiredChanged()
    {
        if (!IsExpired) return;

        // Disable all option buttons
        if (_optionsPanel is not null)
        {
            foreach (var child in _optionsPanel.Children)
            {
                if (child is Button btn)
                    btn.IsEnabled = false;
            }
        }

        // Disable free text
        if (_freeTextBox is not null)
            _freeTextBox.IsEnabled = false;
        if (_freeTextSubmit is not null)
            _freeTextSubmit.IsEnabled = false;
        if (_multiSubmit is not null)
            _multiSubmit.IsVisible = false;

        UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":answered", IsAnswered);
        PseudoClasses.Set(":expired", IsExpired);
        PseudoClasses.Set(":has-free-text", AllowFreeText);
        PseudoClasses.Set(":multi-select", AllowMultiSelect);
    }
}
