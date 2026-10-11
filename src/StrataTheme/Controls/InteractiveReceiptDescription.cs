using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StrataTheme.Controls;

/// <summary>A frozen, human-readable description of the exact submitted choices.</summary>
public sealed record InteractiveReceiptDescription(string Title, string Summary, string Detail);

public static class InteractiveReceiptFormatter
{
    public static InteractiveReceiptDescription Describe(
        InteractiveDefinition definition, InteractiveState state, string action, string language = "en")
    {
        _ = InteractiveSubmission.Build(definition, state, action);
        var labels = InteractiveLabels.ForLanguage(language);
        var lines = new List<string>();
        var summary = new List<string>();
        string title;
        switch (definition)
        {
            case InteractiveForm form:
                title = labels["receiptForm"];
                foreach (var field in form.Fields)
                {
                    var value = FormValue(field, state, labels);
                    lines.Add($"{field.Label}: {value}");
                    summary.Add($"{field.Label}: {value}");
                }
                break;
            case InteractiveResults results:
                var selected = results.Items.Where(item => state.SelectedIds.Contains(item.Id)).ToArray();
                title = labels.Format(action == "compare" ? "receiptCompare" : "receiptUse", selected.Length);
                foreach (var item in selected)
                {
                    summary.Add(item.Label);
                    lines.Add(item.Label);
                    if (item.Meta is { Length: > 0 }) lines.Add(item.Meta);
                    if (item.Detail is { Length: > 0 }) lines.Add(item.Detail);
                    if (item.Url is { Length: > 0 }) lines.Add(item.Url);
                    lines.Add("");
                }
                break;
            case InteractiveCalculatorDefinition calculator:
                title = labels["receiptCalculator"];
                foreach (var input in InteractiveCalculator.GetInputs(calculator.Template))
                {
                    var value = InteractiveFormat.Input(input, state.Values[input.Id], calculator.Currency, labels);
                    lines.Add($"{labels[input.Id]}: {value}");
                    summary.Add($"{labels[input.Id]}: {value}");
                }
                lines.Add("");
                foreach (var metric in InteractiveCalculator.Calculate(calculator, state).Metrics)
                    lines.Add($"{labels[MetricLabelKey(metric.Key)]}: {InteractiveFormat.Value(metric.Value, metric.Kind, calculator.Currency, labels)}");
                break;
            case InteractiveChecklist checklist:
                var remaining = checklist.Items.Where(item => !state.CheckedIds.Contains(item.Id)).ToArray();
                title = checklist.Numbered && remaining.Length > 0
                    ? labels.Format("receiptNextStep", checklist.Items.IndexOf(remaining[0]) + 1)
                    : labels.Format("receiptRemaining", remaining.Length);
                foreach (var item in remaining)
                {
                    var number = checklist.Items.IndexOf(item) + 1;
                    summary.Add(checklist.Numbered ? labels.Format("stepLabel", number, item.Label) : item.Label);
                    lines.Add(checklist.Numbered ? labels.Format("stepItem", number, item.Label) : item.Label);
                    if (item.Detail is { Length: > 0 }) lines.Add(item.Detail);
                    lines.Add("");
                }
                break;
            case InteractiveQuiz quiz:
                title = labels["receiptQuiz"];
                summary.Add(labels.Format("score", quiz.Questions.Count(question => state.Answers[question.Id] == question.CorrectIndex), quiz.Questions.Count));
                foreach (var question in quiz.Questions)
                {
                    lines.Add(question.Prompt);
                    lines.Add(question.Options[state.Answers[question.Id]]);
                    lines.Add(labels[state.Answers[question.Id] == question.CorrectIndex ? "correct" : "incorrect"]);
                    lines.Add("");
                }
                break;
            default:
                throw new FormatException("This interaction does not support a receipt.");
        }
        var compact = string.Join(" \u00b7 ", summary.Take(3));
        if (summary.Count > 3)
            compact += labels.Format("moreInputs", summary.Count - 3);
        return new(title, compact, string.Join('\n', lines).Trim());
    }

    /// <summary>Metric keys that share a name with an input use a distinct label.</summary>
    public static string MetricLabelKey(string key) => key == "remaining" ? "remainingMetric" : key;

    /// <summary>A form field's value as people read it: friendly dates, money, units, and Yes/No.</summary>
    public static string FormValue(InteractiveField field, InteractiveState state, InteractiveLabels labels)
    {
        var value = field.Type == "multiselect"
            ? string.Join(", ", state.Choices.GetValueOrDefault(field.Id) ?? [])
            : state.Values.GetValueOrDefault(field.Id) ?? "";
        if (field.Type == "toggle")
            return labels[value == "true" ? "yes" : "no"];
        if (value.Length == 0)
            return labels["notProvided"];
        if (field.Type == "date" && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return InteractiveFormat.Date(date);
        return field.Type == "number" ? InteractiveFormat.Number(value, field.Unit) : value;
    }
}
