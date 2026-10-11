using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StrataTheme.Controls;

/// <summary>Builds a visible user request from validated choices, never an executable widget command.</summary>
public static class InteractiveSubmission
{
    public static Dictionary<string, string> ValidateForm(InteractiveForm form, InteractiveState state)
    {
        var errors = new Dictionary<string, string>();
        foreach (var field in form.Fields)
        {
            var value = state.Values.GetValueOrDefault(field.Id) ?? "";
            if (field.Type == "multiselect")
            {
                if (field.Required && state.Choices.GetValueOrDefault(field.Id)?.Count is not > 0)
                    errors[field.Id] = "required";
                continue;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                if (field.Required)
                    errors[field.Id] = "required";
                continue;
            }
            switch (field.Type)
            {
                case "number":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        || !double.IsFinite(number))
                        errors[field.Id] = "number";
                    else if (field.Minimum is { } min && number < min || field.Maximum is { } max && number > max)
                        errors[field.Id] = "range";
                    break;
                case "date":
                    if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        errors[field.Id] = "date";
                    break;
                case "select":
                    if (!field.Options.Contains(value))
                        errors[field.Id] = "choice";
                    break;
                case "toggle":
                    if (value is not ("true" or "false"))
                        errors[field.Id] = "choice";
                    break;
            }
        }
        return errors;
    }

    public static string Build(InteractiveDefinition definition, InteractiveState state, string action)
    {
        state.Validate(definition);
        var text = new StringBuilder();
        switch (definition)
        {
            case InteractiveForm form when action == "submit":
                var errors = ValidateForm(form, state);
                InteractiveDefinition.Require(errors.Count == 0, "Complete the highlighted form fields before continuing.");
                text.AppendLine($"I submitted the form \"{form.Title}\". Continue using these inputs:");
                foreach (var field in form.Fields)
                {
                    var value = field.Type == "multiselect"
                        ? string.Join(", ", state.Choices.GetValueOrDefault(field.Id) ?? [])
                        : state.Values.GetValueOrDefault(field.Id) ?? "";
                    text.AppendLine($"- {field.Label}: {value}{(string.IsNullOrWhiteSpace(field.Unit) ? "" : $" {field.Unit}")}");
                }
                break;
            case InteractiveChecklist checklist when action == "remaining":
                var remaining = checklist.Items.Where(item => !state.CheckedIds.Contains(item.Id)).ToArray();
                InteractiveDefinition.Require(remaining.Length > 0, "All items are checked.");
                if (checklist.Numbered)
                {
                    var next = checklist.Items.IndexOf(remaining[0]) + 1;
                    text.AppendLine($"I'm working through the steps in \"{checklist.Title}\". Help me with step {next}, then the remaining steps:");
                }
                else
                    text.AppendLine($"Continue with the unchecked items in \"{checklist.Title}\":");
                foreach (var item in remaining)
                    text.AppendLine($"- {(checklist.Numbered ? $"Step {checklist.Items.IndexOf(item) + 1}: " : "")}{item.Label}{(item.Detail is null ? "" : $": {item.Detail}")}");
                text.AppendLine("The other checkmarks record my progress, not actions performed or verified by the assistant.");
                break;
            case InteractiveResults results when action is "use" or "compare":
                var selected = results.Items.Where(item => state.SelectedIds.Contains(item.Id)).ToArray();
                InteractiveDefinition.Require(selected.Length >= (action == "compare" ? 2 : 1),
                    action == "compare" ? "Select at least two results to compare." : "Select at least one result.");
                text.AppendLine(action == "compare"
                    ? $"Compare these selected results from \"{results.Title}\":"
                    : $"Use these selected results from \"{results.Title}\" for the next step:");
                foreach (var item in selected)
                {
                    text.AppendLine($"- {item.Label} [id: {item.Id}]");
                    if (item.Meta is { Length: > 0 }) text.AppendLine($"  {item.Meta}");
                    if (item.Detail is { Length: > 0 }) text.AppendLine($"  {item.Detail}");
                    if (item.Url is { Length: > 0 }) text.AppendLine($"  Source: {item.Url}");
                }
                break;
            case InteractiveCalculatorDefinition calculator when action == "assumptions":
                var calculation = InteractiveCalculator.Calculate(calculator, state);
                text.AppendLine($"Continue \"{calculator.Title}\" using these calculator assumptions ({calculator.Template}):");
                foreach (var input in InteractiveCalculator.GetInputs(calculator.Template))
                    text.AppendLine($"- {input.Id}: {state.Values[input.Id]}");
                foreach (var metric in calculation.Metrics)
                    text.AppendLine($"- {metric.Key}: {metric.Value.ToString("G12", CultureInfo.InvariantCulture)}{(metric.Kind == InteractiveValueKind.Money && !string.IsNullOrWhiteSpace(calculator.Currency) ? " " + calculator.Currency : "")}");
                if (calculator.Template == "savings")
                    text.AppendLine("This illustration compounds monthly, with contributions at month end; it excludes taxes, fees, and inflation.");
                else if (calculator.Template == "loan")
                    text.AppendLine("This estimate assumes a fixed rate with monthly payments; it excludes taxes, insurance, and fees.");
                break;
            case InteractiveQuiz quiz when action == "discuss":
                InteractiveDefinition.Require(quiz.Questions.All(question => state.CheckedAnswers.Contains(question.Id)),
                    "Check every answer before discussing the results.");
                text.AppendLine($"Discuss my results for \"{quiz.Title}\" and help with any mistakes:");
                foreach (var question in quiz.Questions)
                {
                    var answer = state.Answers[question.Id];
                    text.AppendLine($"- {question.Prompt}");
                    text.AppendLine($"  My answer: {question.Options[answer]} ({(answer == question.CorrectIndex ? "correct" : "incorrect")})");
                }
                break;
            default:
                throw new FormatException($"Action '{action}' is not supported by '{definition.Kind}'.");
        }

        text.AppendLine();
        text.AppendLine("This submits my choices, not approval for purchases, bookings, sending messages, or destructive changes. Keep the normal permission and review steps.");
        return text.ToString().Trim();
    }
}
