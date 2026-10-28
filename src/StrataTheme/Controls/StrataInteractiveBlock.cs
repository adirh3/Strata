using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace StrataTheme.Controls;

public readonly record struct InteractiveActionResult(bool Accepted, string? Error = null);

/// <summary>A state edit or an explicit user submission, routed to the owning chat.</summary>
public sealed class InteractiveRequestEventArgs : RoutedEventArgs
{
    public InteractiveRequestEventArgs(InteractiveDefinition definition, string stateJson, string? action = null)
        : base(StrataInteractiveBlock.RequestedEvent)
    {
        Definition = definition;
        StateJson = stateJson;
        Action = action;
    }

    public InteractiveDefinition Definition { get; }
    public string StateJson { get; }
    public string? Action { get; }
    public Task<InteractiveActionResult>? Completion { get; set; }
}

/// <summary>
/// A token-driven native response surface for forms, checklists, choices, calculators, and
/// practice quizzes. State edits never invoke an agent themselves.
/// </summary>
/// <remarks>
/// Template parts: PART_Card, PART_Stratum, PART_IconTile, PART_Icon, PART_Title, PART_Description,
/// PART_Meta, PART_Body, PART_ErrorRow, PART_Error, PART_Footer, PART_Actions, PART_Status.
/// Pseudo-classes: :waiting (a tool-owned request awaits input), :sending and :error.
/// Hosts handle Requested and acknowledge explicit actions through Completion.
/// </remarks>
public partial class StrataInteractiveBlock : TemplatedControl
{
    public static readonly StyledProperty<InteractiveDefinition?> DefinitionProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, InteractiveDefinition?>(nameof(Definition));
    public static readonly StyledProperty<string?> StateJsonProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, string?>(nameof(StateJson));
    public static readonly StyledProperty<string> LanguageProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, string>(nameof(Language), "en");
    public static readonly StyledProperty<bool> CanSubmitProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, bool>(nameof(CanSubmit));
    public static readonly StyledProperty<bool> IsHostedProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, bool>(nameof(IsHosted));
    public static readonly StyledProperty<bool> TouchModeProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, bool>(nameof(TouchMode));
    public static readonly StyledProperty<string?> InputStatusProperty =
        AvaloniaProperty.Register<StrataInteractiveBlock, string?>(nameof(InputStatus));
    public static readonly RoutedEvent<InteractiveRequestEventArgs> RequestedEvent =
        RoutedEvent.Register<StrataInteractiveBlock, InteractiveRequestEventArgs>(
            nameof(Requested), RoutingStrategies.Bubble);

    private StackPanel? _body;
    private WrapPanel? _actions;
    private Grid? _footer;
    private TextBlock? _status;
    private Grid? _errorRow;
    private TextBlock? _error;
    private Border? _iconTile;
    private PathIcon? _icon;
    private TextBlock? _titleLabel;
    private TextBlock? _descriptionLabel;
    private ContentControl? _meta;
    private readonly List<(Button Button, string Action, string Label, string? Icon)> _submitButtons = [];
    private readonly Dictionary<string, (Control Input, TextBlock Error)> _fieldControls = [];
    private InteractiveState _state = new();
    private InteractiveLabels _labels = InteractiveLabels.English;
    private Func<string?>? _statusText;
    private Action? _refreshActions;
    private Action<double>? _layoutForWidth;
    private bool _writingState;
    private string? _submittingAction;
    private string? _loadedJson;
    private int _editVersion;
    private bool _typedEditPending;

    public InteractiveDefinition? Definition { get => GetValue(DefinitionProperty); set => SetValue(DefinitionProperty, value); }
    public string? StateJson { get => GetValue(StateJsonProperty); set => SetValue(StateJsonProperty, value); }
    public string Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public bool CanSubmit { get => GetValue(CanSubmitProperty); set => SetValue(CanSubmitProperty, value); }
    public bool IsHosted { get => GetValue(IsHostedProperty); set => SetValue(IsHostedProperty, value); }
    public bool TouchMode { get => GetValue(TouchModeProperty); set => SetValue(TouchModeProperty, value); }
    /// <summary>Null for an answer; Waiting, Submitted, Canceled, or Expired for a tool-owned input request.</summary>
    public string? InputStatus { get => GetValue(InputStatusProperty); set => SetValue(InputStatusProperty, value); }
    private bool IsInputRequest => InputStatus is not null;
    private bool IsSubmitting => _submittingAction is not null;

    public event EventHandler<InteractiveRequestEventArgs> Requested
    {
        add => AddHandler(RequestedEvent, value);
        remove => RemoveHandler(RequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _body = e.NameScope.Find<StackPanel>("PART_Body");
        _actions = e.NameScope.Find<WrapPanel>("PART_Actions");
        _footer = e.NameScope.Find<Grid>("PART_Footer");
        _status = e.NameScope.Find<TextBlock>("PART_Status");
        _errorRow = e.NameScope.Find<Grid>("PART_ErrorRow");
        _error = e.NameScope.Find<TextBlock>("PART_Error");
        _iconTile = e.NameScope.Find<Border>("PART_IconTile");
        _icon = e.NameScope.Find<PathIcon>("PART_Icon");
        _titleLabel = e.NameScope.Find<TextBlock>("PART_Title");
        _descriptionLabel = e.NameScope.Find<TextBlock>("PART_Description");
        _meta = e.NameScope.Find<ContentControl>("PART_Meta");
        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DefinitionProperty || change.Property == LanguageProperty || change.Property == TouchModeProperty
            || change.Property == InputStatusProperty
            || change.Property == StateJsonProperty && !_writingState && StateJson != _loadedJson)
            Rebuild();
        else if (change.Property == CanSubmitProperty || change.Property == IsHostedProperty)
            UpdateActions();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
            _layoutForWidth?.Invoke(availableSize.Width);
        var measured = base.MeasureOverride(availableSize);
        return double.IsFinite(availableSize.Width)
            ? new Size(Math.Max(measured.Width, availableSize.Width), measured.Height)
            : measured;
    }

    private void Rebuild()
    {
        if (_body is null || _actions is null || Definition is not { } definition)
            return;

        _labels = InteractiveLabels.ForLanguage(Language);
        Classes.Set("touch", TouchMode);
        FlowDirection = _labels == InteractiveLabels.Hebrew || _labels == InteractiveLabels.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        AutomationProperties.SetAutomationId(this, "Interaction_" + SafeName(definition.Id));
        AutomationProperties.SetName(this, definition.Title);
        AutomationProperties.SetHelpText(this, _labels[KindKey(definition)]);
        PseudoClasses.Set(":waiting", InputStatus == "Waiting");
        _body.Children.Clear();
        _actions.Children.Clear();
        _submitButtons.Clear();
        _fieldControls.Clear();
        _statusText = null;
        _refreshActions = null;
        _layoutForWidth = null;
        _pendingCalculation = null;
        _calculationHost = null;
        _typedEditPending = false;
        SetError(null);
        SetHeader(definition);

        try
        {
            _state = StateJson is { Length: > 0 } json
                ? InteractiveState.Parse(json) : InteractiveState.Create(definition);
            _state.Validate(definition);
            _loadedJson = StateJson;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            _state = InteractiveState.Create(definition);
            SetError(_labels["restoreError"]);
            AddLocalAction(_labels["reset"], Reset);
            UpdateActions();
            return;
        }

        if (IsInputRequest && InputStatus != "Waiting")
        {
            BuildInputOutcome(definition);
            UpdateActions();
            return;
        }

        switch (definition)
        {
            case InteractiveForm form: BuildForm(form); break;
            case InteractiveChecklist checklist: BuildChecklist(checklist); break;
            case InteractiveResults results: BuildResults(results); break;
            case InteractiveCalculatorDefinition calculator: BuildCalculator(calculator); break;
            case InteractiveQuiz quiz: BuildQuiz(quiz); break;
        }
        if (InputStatus == "Waiting")
            AddLocalAction(_labels["cancelInput"], () => _ = SubmitAsync("cancel"), "subtle", "CancelInputRequest");
        if (double.IsFinite(Bounds.Width) && Bounds.Width > 0)
            _layoutForWidth?.Invoke(Bounds.Width);
        UpdateActions();
    }

    private void SetHeader(InteractiveDefinition definition)
    {
        var (icon, tone) = InputStatus switch
        {
            "Submitted" => (InteractiveIcons.Check, "success"),
            "Canceled" => (InteractiveIcons.Removed, "muted"),
            "Expired" => (InteractiveIcons.Clock, "muted"),
            _ => (KindIcon(definition), null)
        };
        if (_icon is not null)
            _icon.Data = Geometry.Parse(icon);
        if (_iconTile is not null)
        {
            _iconTile.Classes.Set("success", tone == "success");
            _iconTile.Classes.Set("muted", tone == "muted");
            ToolTip.SetTip(_iconTile, _labels[KindKey(definition)]);
        }
        if (_titleLabel is not null)
            _titleLabel.Text = definition.Title;
        if (_descriptionLabel is not null)
        {
            _descriptionLabel.Text = definition.Description;
            _descriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(definition.Description) && InputStatus is null or "Waiting";
        }
        SetMeta(InputStatus switch
        {
            "Waiting" => Pill(_labels["needsInput"], "accent", dot: true),
            "Submitted" => Pill(_labels["sent"], "success", InteractiveIcons.Check),
            "Canceled" => Pill(_labels["inputCanceled"]),
            "Expired" => Pill(_labels["ended"]),
            _ => null
        });
    }

    private static string KindKey(InteractiveDefinition definition) =>
        definition is InteractiveChecklist { Numbered: true } ? "steps" : definition.Kind;

    private static string KindIcon(InteractiveDefinition definition) => definition switch
    {
        InteractiveForm => InteractiveIcons.Form,
        InteractiveChecklist { Numbered: true } => InteractiveIcons.Steps,
        InteractiveChecklist => InteractiveIcons.Checklist,
        InteractiveResults => InteractiveIcons.Results,
        InteractiveCalculatorDefinition => InteractiveIcons.Calculator,
        _ => InteractiveIcons.Quiz
    };

    private void SetMeta(Control? content)
    {
        if (_meta is null) return;
        _meta.Content = content;
        _meta.IsVisible = content is not null;
    }

    private static Border Pill(string text, string? tone = null, string? icon = null, bool dot = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (dot)
            content.Children.Add(new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center });
        if (icon is not null)
            content.Children.Add(InteractiveIcons.Create(icon, 10));
        content.Children.Add(new TextBlock { Text = text });
        var pill = new Border { Child = content, Classes = { "interactive-pill" } };
        if (tone is not null)
            pill.Classes.Add(tone);
        return pill;
    }

    /// <summary>The settled state of a tool-owned request: a compact summary instead of the full form.</summary>
    private void BuildInputOutcome(InteractiveDefinition definition)
    {
        if (InputStatus == "Canceled")
        {
            _body!.Children.Add(Text(_labels["nothingSent"], "interactive-hint"));
            return;
        }

        if (InputStatus == "Submitted")
        {
            var snapshot = InteractiveReceiptFormatter.Describe(
                definition, _state, definition is InteractiveForm ? "submit" : "use", Language);
            AddSummary(snapshot.Summary, snapshot.Detail, "viewInputs", "hideInputs");
            return;
        }

        _body!.Children.Add(Text(_labels["endedHint"], "interactive-hint"));
        var draft = DraftLines(definition);
        if (draft.Count > 0)
            AddSummary(string.Join(" \u00b7 ", draft.Take(3)), string.Join('\n', draft), "viewDraft", "hideDraft");
    }

    private void AddSummary(string summary, string detail, string showKey, string hideKey)
    {
        var text = Text(summary, "interactive-detail");
        text.MaxLines = 2;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        _body!.Children.Add(text);
        if (string.IsNullOrWhiteSpace(detail) || detail == summary)
            return;
        var host = new Border { Classes = { "interactive-callout" }, IsVisible = false };
        var disclosure = new Button { Name = "PART_InputDisclosure", Classes = { "subtle", "interactive-disclosure" } };
        void Toggle(bool expanded)
        {
            host.Child = expanded ? InteractiveSnapshotView.Create(detail) : null;
            host.IsVisible = expanded;
            var label = _labels[expanded ? hideKey : showKey];
            disclosure.Content = InteractiveIcons.Labeled(expanded ? InteractiveIcons.ChevronUp : InteractiveIcons.ChevronDown, label, trailing: true);
            AutomationProperties.SetName(disclosure, label);
        }
        disclosure.Click += (_, _) => Toggle(!host.IsVisible);
        Toggle(false);
        _body.Children.Add(disclosure);
        _body.Children.Add(host);
    }

    private List<string> DraftLines(InteractiveDefinition definition) => definition switch
    {
        InteractiveForm form => form.Fields
            .Where(field => field.Type != "toggle" && (field.Type == "multiselect"
                ? _state.Choices.GetValueOrDefault(field.Id)?.Count > 0
                : !string.IsNullOrEmpty(_state.Values.GetValueOrDefault(field.Id))))
            .Select(field => $"{field.Label}: {InteractiveReceiptFormatter.FormValue(field, _state, _labels)}")
            .ToList(),
        InteractiveResults results => results.Items.Where(item => _state.SelectedIds.Contains(item.Id))
            .Select(item => item.Label).ToList(),
        _ => []
    };

    private void CommitState()
    {
        if (IsInputRequest && InputStatus != "Waiting")
            return;
        _typedEditPending = false;
        SetError(null);
        var json = _state.ToJson();
        _loadedJson = json;
        _writingState = true;
        SetCurrentValue(StateJsonProperty, json);
        _writingState = false;
        var version = ++_editVersion;
        if (Definition is { } definition)
        {
            var request = new InteractiveRequestEventArgs(definition, json);
            RaiseEvent(request);
            if (request.Completion is { } completion)
                _ = ObserveSaveAsync(completion, version);
        }
        UpdateActions();
    }

    /// <summary>Typed values update locally on every keystroke and are saved once, when editing ends.</summary>
    private void TypedEdit(string id, string value)
    {
        _state.Values[id] = value;
        ClearFieldError(id);
        SetError(null);
        _typedEditPending = true;
        UpdateActions();
    }

    private void CommitTypedEdit()
    {
        if (_typedEditPending)
            CommitState();
    }

    private async Task ObserveSaveAsync(Task<InteractiveActionResult> completion, int version)
    {
        var result = await completion;
        if (!result.Accepted && version == _editVersion)
            SetError(result.Error ?? _labels["notSent"]);
    }

    /// <summary>Validates and requests one explicit action. Hosts retain their ordinary permission flow.</summary>
    public async Task<InteractiveActionResult> SubmitAsync(string action)
    {
        if (Definition is not { } definition || !CanSubmit || IsSubmitting)
            return new(false, _labels["unavailable"]);

        if (InputStatus == "Waiting" && action == "cancel")
        {
            var cancellation = new InteractiveRequestEventArgs(definition, _state.ToJson(), "cancel");
            RaiseEvent(cancellation);
            return cancellation.Completion is { } completion
                ? await completion : new(false, _labels["unavailable"]);
        }

        if (definition is InteractiveForm form)
        {
            var errors = InteractiveSubmission.ValidateForm(form, _state);
            foreach (var field in _fieldControls)
            {
                var invalid = errors.TryGetValue(field.Key, out var error);
                field.Value.Input.Classes.Set("interactive-invalid", invalid);
                field.Value.Error.Text = invalid ? _labels[error!] : "";
                field.Value.Error.IsVisible = invalid;
            }
            if (errors.Count > 0)
            {
                SetError(_labels["invalid"]);
                _fieldControls[errors.Keys.First()].Input.Focus();
                return new(false, _labels["invalid"]);
            }
        }

        try
        {
            _ = InteractiveSubmission.Build(definition, _state, action);
        }
        catch (FormatException error)
        {
            var message = definition is InteractiveCalculatorDefinition calculator ? CalculatorError(calculator) : error.Message;
            SetError(message);
            return new(false, message);
        }

        var fingerprint = _state.Fingerprint();
        if (_state.SubmittedFingerprint == action + ":" + fingerprint)
            return new(false, _labels["sent"]);

        _submittingAction = action;
        PseudoClasses.Set(":sending", true);
        UpdateActions();
        try
        {
            var request = new InteractiveRequestEventArgs(definition, _state.ToJson(), action);
            RaiseEvent(request);
            var result = request.Completion is { } completion
                ? await completion : new InteractiveActionResult(false, _labels["unavailable"]);
            if (result.Accepted && IsInputRequest)
            {
                SetError(null);
                return result;
            }
            if (result.Accepted && _state.Fingerprint() == fingerprint)
            {
                _state.SubmittedFingerprint = action + ":" + fingerprint;
                var json = _state.ToJson();
                _loadedJson = json;
                _writingState = true;
                SetCurrentValue(StateJsonProperty, json);
                _writingState = false;
                var saved = new InteractiveRequestEventArgs(definition, json);
                RaiseEvent(saved);
                if (saved.Completion is { } save)
                    await ObserveSaveAsync(save, ++_editVersion);
            }
            else if (!result.Accepted)
                SetError(result.Error ?? _labels["notSent"]);
            return result;
        }
        finally
        {
            _submittingAction = null;
            PseudoClasses.Set(":sending", false);
            UpdateActions();
        }
    }

    private void Reset()
    {
        if (Definition is null) return;
        _state = InteractiveState.Create(Definition);
        CommitState();
        Rebuild();
    }

    private void UpdateActions()
    {
        foreach (var entry in _submitButtons)
        {
            var sent = _state.SubmittedFingerprint == entry.Action + ":" + _state.Fingerprint();
            var ready = entry.Action switch
            {
                "use" => _state.SelectedIds.Count > 0,
                "compare" => _state.SelectedIds.Count >= 2,
                "remaining" when Definition is InteractiveChecklist checklist => _state.CheckedIds.Count < checklist.Items.Count,
                "assumptions" => _calculationValid,
                _ => true
            };
            var label = _submittingAction == entry.Action ? _labels["sending"] : sent ? _labels["sent"] : entry.Label;
            entry.Button.Content = sent ? InteractiveIcons.Labeled(InteractiveIcons.Check, label)
                : entry.Icon is { } icon && !IsSubmitting ? InteractiveIcons.Labeled(icon, label) : label;
            AutomationProperties.SetName(entry.Button, label);
            entry.Button.IsEnabled = CanSubmit && !IsSubmitting && !sent && ready;
            entry.Button.IsVisible = IsHosted || CanSubmit;
        }
        _refreshActions?.Invoke();

        if (_status is not null)
        {
            _status.Text = _statusText?.Invoke() ?? (!IsHosted && !IsInputRequest ? _labels["preview"] : null);
            _status.IsVisible = !string.IsNullOrEmpty(_status.Text);
        }
        if (_footer is not null && _actions is not null)
            _footer.IsVisible = _actions.Children.Any(child => child.IsVisible) || _status?.IsVisible == true;
    }

    private Button AddSubmitAction(string label, string action, string variant = "accent", string? icon = null)
    {
        var button = Button(label, variant);
        button.Name = "Action_" + action;
        button.Click += async (_, _) => await SubmitAsync(action);
        _submitButtons.Add((button, action, label, icon));
        _actions?.Children.Add(button);
        return button;
    }

    private Button AddLocalAction(string label, Action action, string variant = "default", string? name = null,
        string? icon = null, bool trailingIcon = false)
    {
        var button = Button(label, variant);
        if (icon is not null)
            button.Content = InteractiveIcons.Labeled(icon, label, trailingIcon);
        button.Name = name;
        button.Click += (_, _) => action();
        _actions?.Children.Add(button);
        return button;
    }

    private static Button Button(string label, string variant = "default")
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        if (variant != "default")
            button.Classes.Add(variant);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private static TextBlock Text(string? value, string style = "interactive-body")
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        text.Classes.Add(style);
        return text;
    }

    private void SetError(string? message)
    {
        if (_error is not null)
            _error.Text = message;
        if (_errorRow is not null)
            _errorRow.IsVisible = !string.IsNullOrEmpty(message);
        PseudoClasses.Set(":error", !string.IsNullOrEmpty(message));
    }

    /// <summary>Places cells into a responsive grid; full-width cells span every column.</summary>
    private static void ArrangeCells(Grid grid, IReadOnlyList<(Control Cell, bool Full)> cells, int columns)
    {
        grid.ColumnDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Clear();
        var row = 0;
        var next = 0;
        foreach (var (cell, full) in cells)
        {
            if (full && next > 0)
            {
                row++;
                next = 0;
            }
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, full ? 0 : next);
            Grid.SetColumnSpan(cell, full ? columns : 1);
            if (full || ++next >= columns)
            {
                row++;
                next = 0;
            }
        }
        for (var index = 0; index < row + (next > 0 ? 1 : 0); index++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (grid.Children.Count == 0)
            foreach (var (cell, _) in cells)
                grid.Children.Add(cell);
    }

    internal static string SafeName(string id) =>
        new(id.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
