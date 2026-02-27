using System;
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
/// PART_FreeTextSubmit (Button).</para>
/// <para><b>Pseudo-classes:</b> :answered, :has-free-text.</para>
/// </remarks>
public class StrataQuestionCard : TemplatedControl
{
    private WrapPanel? _optionsPanel;
    private TextBox? _freeTextBox;
    private Button? _freeTextSubmit;

    public static readonly StyledProperty<string?> QuestionProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(Question));

    public static readonly StyledProperty<string?> OptionsProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(Options));

    public static readonly StyledProperty<bool> AllowFreeTextProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(AllowFreeText), true);

    public static readonly StyledProperty<string?> FreeTextPlaceholderProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(FreeTextPlaceholder), "Type your answer...");

    public static readonly StyledProperty<string?> SelectedAnswerProperty =
        AvaloniaProperty.Register<StrataQuestionCard, string?>(nameof(SelectedAnswer));

    public static readonly StyledProperty<bool> IsAnsweredProperty =
        AvaloniaProperty.Register<StrataQuestionCard, bool>(nameof(IsAnswered));

    static StrataQuestionCard()
    {
        OptionsProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.RebuildOptions());
        IsAnsweredProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
        AllowFreeTextProperty.Changed.AddClassHandler<StrataQuestionCard>((c, _) => c.UpdatePseudoClasses());
    }

    /// <summary>The question text displayed as the card header.</summary>
    public string? Question { get => GetValue(QuestionProperty); set => SetValue(QuestionProperty, value); }

    /// <summary>Comma-separated list of option labels.</summary>
    public string? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }

    /// <summary>Whether to show a free-text input below the options.</summary>
    public bool AllowFreeText { get => GetValue(AllowFreeTextProperty); set => SetValue(AllowFreeTextProperty, value); }

    /// <summary>Placeholder text for the free-text input.</summary>
    public string? FreeTextPlaceholder { get => GetValue(FreeTextPlaceholderProperty); set => SetValue(FreeTextPlaceholderProperty, value); }

    /// <summary>The answer selected by the user (option text or free text).</summary>
    public string? SelectedAnswer { get => GetValue(SelectedAnswerProperty); set => SetValue(SelectedAnswerProperty, value); }

    /// <summary>Whether the user has submitted an answer.</summary>
    public bool IsAnswered { get => GetValue(IsAnsweredProperty); set => SetValue(IsAnsweredProperty, value); }

    /// <summary>Raised when the user selects an option or submits free text.</summary>
    public event EventHandler<string>? AnswerSubmitted;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _optionsPanel = e.NameScope.Find<WrapPanel>("PART_OptionsPanel");
        _freeTextBox = e.NameScope.Find<TextBox>("PART_FreeTextBox");
        _freeTextSubmit = e.NameScope.Find<Button>("PART_FreeTextSubmit");

        if (_freeTextSubmit is not null)
            _freeTextSubmit.Click += OnFreeTextSubmitClick;

        if (_freeTextBox is not null)
            _freeTextBox.KeyDown += OnFreeTextKeyDown;

        RebuildOptions();
        UpdatePseudoClasses();
    }

    private void RebuildOptions()
    {
        if (_optionsPanel is null) return;
        _optionsPanel.Children.Clear();

        var options = Options;
        if (string.IsNullOrWhiteSpace(options)) return;

        foreach (var raw in options.Split(','))
        {
            var opt = raw.Trim();
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
        if (IsAnswered) return;
        if (sender is Button btn && btn.Tag is string answer)
            SubmitAnswer(answer);
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
        if (IsAnswered) return;
        var text = _freeTextBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
            SubmitAnswer(text);
    }

    private void SubmitAnswer(string answer)
    {
        SelectedAnswer = answer;
        IsAnswered = true;

        // Disable all option buttons
        if (_optionsPanel is not null)
        {
            foreach (var child in _optionsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.IsEnabled = false;
                    if (btn.Tag is string tag && tag == answer)
                        btn.Classes.Add("selected");
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

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":answered", IsAnswered);
        PseudoClasses.Set(":has-free-text", AllowFreeText);
    }
}
