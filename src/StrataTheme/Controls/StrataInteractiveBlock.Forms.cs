using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaCalendar = Avalonia.Controls.Calendar;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private void BuildForm(InteractiveForm form)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var cells = new List<(Control Cell, bool Full)>();
        foreach (var field in form.Fields)
        {
            var value = _state.Values.GetValueOrDefault(field.Id) ?? "";
            var cell = new StackPanel { Spacing = 6 };
            var full = false;
            if (field.Type != "toggle")
                cell.Children.Add(FieldLabel(field.Label, field.Required));
            Control input;
            switch (field.Type)
            {
                case "select" when UsesChips(field):
                    input = SingleChoiceChips(field, value);
                    full = true;
                    break;
                case "select" when TouchMode:
                    input = TouchSelect(field, value);
                    break;
                case "select":
                    var select = new ComboBox
                    {
                        ItemsSource = field.Options,
                        SelectedItem = field.Options.Contains(value) ? value : null,
                        PlaceholderText = field.Placeholder ?? _labels["select"],
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    select.SelectionChanged += (_, _) =>
                    {
                        _state.Values[field.Id] = select.SelectedItem as string ?? "";
                        ClearFieldError(field.Id);
                        CommitState();
                    };
                    input = select;
                    break;
                case "multiselect":
                    input = MultiChoiceChips(field);
                    full = true;
                    break;
                case "date":
                    input = DateField(field, value);
                    break;
                case "number":
                    var numeric = Numeric(field.Id, value);
                    AdornUnit(numeric, field.Unit);
                    input = numeric;
                    break;
                case "toggle":
                    input = ToggleRow(field, value);
                    full = true;
                    break;
                default:
                    var editor = new TextBox { Text = value, PlaceholderText = field.Placeholder };
                    editor.TextChanged += (_, _) => TypedEdit(field.Id, editor.Text ?? "");
                    editor.LostFocus += (_, _) => CommitTypedEdit();
                    input = editor;
                    break;
            }
            input.Name = "Field_" + SafeName(field.Id);
            input.Classes.Add("interactive-input");
            AutomationProperties.SetName(input, field.Label);
            if (field.Help is { Length: > 0 })
                AutomationProperties.SetHelpText(input, field.Help);
            cell.Children.Add(input);
            if (field.Help is { Length: > 0 } && field.Type != "toggle")
                cell.Children.Add(Text(field.Help, "interactive-hint"));
            var error = Text("", "interactive-error");
            error.IsVisible = false;
            cell.Children.Add(error);
            _fieldControls[field.Id] = (input, error);
            cells.Add((cell, full));
        }
        var wide = (bool?)null;
        _layoutForWidth = width =>
        {
            var columns = width >= 520 ? 2 : 1;
            if (wide == (columns == 2)) return;
            wide = columns == 2;
            ArrangeCells(grid, cells, columns);
        };
        ArrangeCells(grid, cells, 1);
        _body!.Children.Add(grid);
        AddSubmitAction(form.SubmitLabel ?? _labels["continue"], "submit");
    }

    private static Control FieldLabel(string label, bool required)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        row.Children.Add(Text(label, "interactive-label"));
        if (required)
            row.Children.Add(new TextBlock { Text = "*", Classes = { "interactive-required" } });
        return row;
    }

    /// <summary>Short option sets read better as one-tap chips than as a dropdown.</summary>
    private static bool UsesChips(InteractiveField field) =>
        field.Options.Count is >= 2 and <= 5 && field.Options.All(option => option.Length <= 24)
        && field.Options.Sum(option => option.Length) <= 60;

    private WrapPanel SingleChoiceChips(InteractiveField field, string value)
    {
        var chips = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var option in field.Options)
        {
            var chip = new RadioButton
            {
                Content = option, IsChecked = option == value,
                Name = "Choice_" + SafeName(field.Id) + "_" + field.Options.IndexOf(option),
                Classes = { "interactive-chip" }
            };
            AutomationProperties.SetName(chip, option);
            chip.IsCheckedChanged += (_, _) =>
            {
                if (chip.IsChecked != true) return;
                _state.Values[field.Id] = option;
                ClearFieldError(field.Id);
                CommitState();
            };
            chips.Children.Add(chip);
        }
        return chips;
    }

    private WrapPanel MultiChoiceChips(InteractiveField field)
    {
        var chips = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var option in field.Options)
        {
            var chip = new ToggleButton
            {
                Content = option,
                IsChecked = _state.Choices.GetValueOrDefault(field.Id)?.Contains(option) == true,
                Name = "Choice_" + SafeName(field.Id) + "_" + field.Options.IndexOf(option),
                Classes = { "interactive-chip", "multi" }
            };
            AutomationProperties.SetName(chip, option);
            chip.IsCheckedChanged += (_, _) =>
            {
                if (!_state.Choices.TryGetValue(field.Id, out var selected))
                    _state.Choices[field.Id] = selected = [];
                selected.Remove(option);
                if (chip.IsChecked == true) selected.Add(option);
                ClearFieldError(field.Id);
                CommitState();
            };
            chips.Children.Add(chip);
        }
        return chips;
    }

    private Button FieldButton(string? value, string placeholder, string icon)
    {
        var text = new TextBlock
        {
            Text = value ?? placeholder, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Classes.Set("placeholder", value is null);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var glyph = InteractiveIcons.Create(icon);
        Grid.SetColumn(glyph, 1);
        content.Children.Add(text);
        content.Children.Add(glyph);
        var button = new Button { Content = content, Classes = { "interactive-field" } };
        button.Tag = text;
        return button;
    }

    private static void SetFieldButtonText(Button button, string text)
    {
        if (button.Tag is not TextBlock label) return;
        label.Text = text;
        label.Classes.Set("placeholder", false);
    }

    private Button TouchSelect(InteractiveField field, string value)
    {
        var button = FieldButton(field.Options.Contains(value) ? value : null, field.Placeholder ?? _labels["select"],
            InteractiveIcons.ChevronDown);
        button.Click += (_, _) =>
        {
            var options = new StackPanel { Spacing = 2 };
            foreach (var option in field.Options)
            {
                var selected = option == _state.Values.GetValueOrDefault(field.Id);
                var check = InteractiveIcons.Create(InteractiveIcons.Check, 18);
                check.Classes.Add("sheet-check");
                check.IsVisible = selected;
                Grid.SetColumn(check, 1);
                var optionButton = new Button
                {
                    Content = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 12,
                        Children =
                        {
                            new TextBlock { Text = option, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
                            check
                        }
                    },
                    MinHeight = 48,
                    Name = "PickerOption_" + SafeName(field.Id) + "_" + field.Options.IndexOf(option),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Classes = { "subtle", "sheet-item" }
                };
                optionButton.Classes.Set("selected", selected);
                AutomationProperties.SetName(optionButton, option);
                optionButton.Click += (_, _) =>
                {
                    _state.Values[field.Id] = option;
                    SetFieldButtonText(button, option);
                    ClearFieldError(field.Id);
                    CommitState();
                    ClosePicker();
                };
                options.Children.Add(optionButton);
            }
            ShowPicker(field.Label, options, field.Help);
        };
        return button;
    }

    private Button DateField(InteractiveField field, string value)
    {
        var current = ParseDate(value);
        var button = FieldButton(current is { } date ? InteractiveFormat.Date(date) : null,
            field.Placeholder ?? _labels["datePlaceholder"], InteractiveIcons.Calendar);
        button.Click += (_, _) =>
        {
            var calendar = new AvaloniaCalendar { SelectedDate = ParseDate(_state.Values.GetValueOrDefault(field.Id)) };
            if (calendar.SelectedDate is { } selected)
                calendar.DisplayDate = selected;
            Flyout? flyout = null;
            void Choose()
            {
                if (calendar.SelectedDate is not { } chosen) return;
                _state.Values[field.Id] = chosen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                SetFieldButtonText(button, InteractiveFormat.Date(chosen));
                ClearFieldError(field.Id);
                CommitState();
                if (flyout is not null)
                    flyout.Hide();
                else
                    ClosePicker();
            }
            // Like CalendarDatePicker: a day click or Enter/Space chooses; arrow keys only move the selection.
            calendar.AddHandler(PointerReleasedEvent, (_, args) =>
            {
                if ((args.Source as Visual)?.FindAncestorOfType<CalendarDayButton>(includeSelf: true) is not null)
                    Choose();
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            calendar.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Key is not (Key.Enter or Key.Space) || calendar.DisplayMode != CalendarMode.Month) return;
                Choose();
                args.Handled = true;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            if (TouchMode)
                ShowPicker(field.Label, calendar, field.Help);
            else
            {
                flyout = new Flyout { Content = calendar, Placement = PlacementMode.BottomEdgeAlignedLeft };
                flyout.ShowAt(button);
            }
        };
        return button;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : null;

    private Control ToggleRow(InteractiveField field, string value)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = value == "true", OnContent = null, OffContent = null,
            VerticalAlignment = VerticalAlignment.Center, MinWidth = 0
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            _state.Values[field.Id] = toggle.IsChecked == true ? "true" : "false";
            CommitState();
        };
        AutomationProperties.SetName(toggle, field.Label);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(field.Label, "interactive-body"));
        if (field.Help is { Length: > 0 })
            text.Children.Add(Text(field.Help, "interactive-hint"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(text);
        row.Children.Add(toggle);
        return row;
    }

    private NumericUpDown Numeric(string id, string value, bool integer = false)
    {
        // No Minimum/Maximum: NumericUpDown silently keeps the last in-range value, while validation names the problem.
        var editor = new NumericUpDown
        {
            Value = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null,
            Increment = 1,
            FormatString = integer ? "0" : "#,0.##",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        editor.PropertyChanged += (_, change) =>
        {
            if (change.Property != NumericUpDown.ValueProperty) return;
            TypedEdit(id, editor.Value?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (Definition is InteractiveCalculatorDefinition calculator)
                QueueCalculation(calculator);
        };
        // Typed text stays verbatim until the value changes; show the formatted value once editing ends.
        editor.LostFocus += (_, _) =>
        {
            if (editor.Value is { } current)
                editor.Text = current.ToString(editor.FormatString, CultureInfo.CurrentCulture);
            CommitTypedEdit();
        };
        return editor;
    }

    /// <summary>Places a currency symbol before the value, or any other unit after it.</summary>
    private static void AdornUnit(NumericUpDown editor, string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return;
        if (InteractiveFormat.Symbol(unit) is { } symbol)
            editor.InnerLeftContent = Adornment(symbol, leading: true);
        else
            editor.InnerRightContent = Adornment(unit.Trim(), leading: false);
    }

    private static TextBlock Adornment(string text, bool leading) => new()
    {
        Text = text, Classes = { "interactive-adornment" },
        Margin = leading ? new Thickness(8, 0, -3, 0) : new Thickness(4, 0, 8, 0)
    };

    private void ClearFieldError(string id)
    {
        if (!_fieldControls.TryGetValue(id, out var field)) return;
        field.Input.Classes.Remove("interactive-invalid");
        field.Error.IsVisible = false;
    }
}
