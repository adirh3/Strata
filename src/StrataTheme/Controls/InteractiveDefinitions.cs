using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StrataTheme.Controls;

/// <summary>A native, declarative interaction embedded in a markdown answer.</summary>
public abstract record InteractiveDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? SubmitLabel { get; init; }
    [JsonIgnore] public abstract string Kind { get; }

    public static bool IsSupported(string language) =>
        language.ToLowerInvariant() is "form" or "checklist" or "results" or "calculator" or "quiz";

    /// <summary>Parses the documented response schema. Invalid definitions fail explicitly.</summary>
    public static InteractiveDefinition Parse(string language, string json)
    {
        language = language.ToLowerInvariant();
        InteractiveDefinition? definition = language switch
        {
            "form" => JsonSerializer.Deserialize(json, InteractiveJsonContext.Default.InteractiveForm),
            "checklist" => JsonSerializer.Deserialize(json, InteractiveJsonContext.Default.InteractiveChecklist),
            "results" => JsonSerializer.Deserialize(json, InteractiveJsonContext.Default.InteractiveResults),
            "calculator" => JsonSerializer.Deserialize(json, InteractiveJsonContext.Default.InteractiveCalculatorDefinition),
            "quiz" => JsonSerializer.Deserialize(json, InteractiveJsonContext.Default.InteractiveQuiz),
            _ => throw new FormatException($"Unsupported interaction '{language}'.")
        };
        if (definition is null)
            throw new FormatException("An interaction must be a JSON object.");

        Require(!string.IsNullOrWhiteSpace(definition.Id), "An interaction needs a stable id.");
        Require(!string.IsNullOrWhiteSpace(definition.Title), "An interaction needs a title.");
        Require(definition.Id.Length <= 100, "Interaction ids must be at most 100 characters.");

        switch (definition)
        {
            case InteractiveForm form:
                Require(form.Fields is { Count: > 0 and <= 16 } && form.Fields.All(field => field is not null), "A form needs 1-16 fields.");
                RequireUnique(form.Fields.Select(field => field.Id), "Field ids");
                foreach (var field in form.Fields)
                {
                    Require(!string.IsNullOrWhiteSpace(field.Label), "Each field needs a label.");
                    Require(field.Type is "text" or "number" or "date" or "select" or "multiselect" or "toggle",
                        $"Unsupported field type '{field.Type}'.");
                    Require(field.Minimum is null || double.IsFinite(field.Minimum.Value), "minimum must be finite.");
                    Require(field.Maximum is null || double.IsFinite(field.Maximum.Value), "maximum must be finite.");
                    Require(field.Minimum is null || field.Maximum is null || field.Minimum <= field.Maximum,
                        "minimum cannot exceed maximum.");
                    Require(field.Minimum is null || Math.Abs(field.Minimum.Value) <= (double)decimal.MaxValue,
                        "minimum exceeds the numeric input range.");
                    Require(field.Maximum is null || Math.Abs(field.Maximum.Value) <= (double)decimal.MaxValue,
                        "maximum exceeds the numeric input range.");
                    Require(field.Options is not null, "Field options cannot be null.");
                    if (field.Type is "select" or "multiselect")
                        RequireUnique(field.Options, $"Options for '{field.Label}'");
                }
                break;
            case InteractiveChecklist checklist:
                Require(checklist.Items is { Count: > 0 and <= 100 } && checklist.Items.All(item => item is not null), "A checklist needs 1-100 items.");
                ValidateItems(checklist.Items);
                break;
            case InteractiveResults results:
                Require(results.Items is { Count: > 0 and <= 40 } && results.Items.All(item => item is not null), "Results need 1-40 items.");
                ValidateItems(results.Items);
                foreach (var item in results.Items.Where(item => item.Url is not null))
                    Require(Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
                        && uri.Scheme is "https" or "http", "Result URLs must be absolute HTTP(S) URLs.");
                break;
            case InteractiveCalculatorDefinition calculator:
                InteractiveCalculator.ValidateDefinition(calculator);
                break;
            case InteractiveQuiz quiz:
                Require(quiz.Questions is { Count: > 0 and <= 20 } && quiz.Questions.All(question => question is not null), "A quiz needs 1-20 questions.");
                RequireUnique(quiz.Questions.Select(question => question.Id), "Question ids");
                foreach (var question in quiz.Questions)
                {
                    Require(!string.IsNullOrWhiteSpace(question.Prompt), "Each question needs a prompt.");
                    Require(question.Options is { Count: >= 2 and <= 8 }, "A question needs 2-8 options.");
                    RequireUnique(question.Options, "Question options");
                    Require(question.CorrectIndex >= 0 && question.CorrectIndex < question.Options.Count,
                        "correctIndex must identify an option.");
                    Require(!string.IsNullOrWhiteSpace(question.Explanation), "Each question needs an explanation.");
                }
                break;
        }
        return definition;
    }

    internal static void Require([DoesNotReturnIf(false)] bool condition, string error)
    {
        if (!condition)
            throw new FormatException(error);
    }

    private static void RequireUnique(IEnumerable<string> values, string name)
    {
        var items = values.ToArray();
        Require(items.Length > 0 && items.All(value => !string.IsNullOrWhiteSpace(value)),
            $"{name} cannot be empty.");
        Require(items.Distinct(StringComparer.Ordinal).Count() == items.Length, $"{name} must be unique.");
    }

    private static void ValidateItems(IReadOnlyList<InteractiveItem> items)
    {
        RequireUnique(items.Select(item => item.Id), "Item ids");
        Require(items.All(item => !string.IsNullOrWhiteSpace(item.Label)), "Each item needs a label.");
    }
}

public sealed record InteractiveForm : InteractiveDefinition
{
    public override string Kind => "form";
    public List<InteractiveField> Fields { get; set; } = [];
}

public sealed record InteractiveField
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Type { get; set; } = "text";
    public string? Value { get; init; }
    public string? Placeholder { get; init; }
    public string? Help { get; init; }
    public string? Unit { get; init; }
    public bool Required { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public List<string> Options { get; set; } = [];
}

public sealed record InteractiveItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string? Meta { get; init; }
    public string? Url { get; init; }
}

public sealed record InteractiveChecklist : InteractiveDefinition
{
    public override string Kind => "checklist";
    /// <summary>Ordered steps of a guide: rendered with step numbers and a current-step emphasis.</summary>
    public bool Numbered { get; init; }
    public List<InteractiveItem> Items { get; set; } = [];
}

public sealed record InteractiveResults : InteractiveDefinition
{
    public override string Kind => "results";
    public List<InteractiveItem> Items { get; set; } = [];
}

public sealed record InteractiveCalculatorDefinition : InteractiveDefinition
{
    public override string Kind => "calculator";
    public required string Template { get; init; }
    public string? Currency { get; init; }
    public Dictionary<string, double> Inputs { get; set; } = [];
    public List<InteractiveIngredient> Ingredients { get; set; } = [];
}

public sealed record InteractiveIngredient
{
    public required string Label { get; init; }
    public required double Quantity { get; init; }
    public string? Unit { get; init; }
}

public sealed record InteractiveQuiz : InteractiveDefinition
{
    public override string Kind => "quiz";
    public List<InteractiveQuizQuestion> Questions { get; set; } = [];
}

public sealed record InteractiveQuizQuestion
{
    public required string Id { get; init; }
    public required string Prompt { get; init; }
    public List<string> Options { get; set; } = [];
    public required int CorrectIndex { get; init; }
    public required string Explanation { get; init; }
    public string? Hint { get; init; }
}

/// <summary>User-owned state, separate from the assistant's immutable response definition.</summary>
public sealed record InteractiveState
{
    public Dictionary<string, string> Values { get; set; } = [];
    public Dictionary<string, List<string>> Choices { get; set; } = [];
    public List<string> CheckedIds { get; set; } = [];
    public List<string> SelectedIds { get; set; } = [];
    public Dictionary<string, int> Answers { get; set; } = [];
    public List<string> CheckedAnswers { get; set; } = [];
    public List<string> RevealedHints { get; set; } = [];
    public int QuestionIndex { get; set; }
    public string? SubmittedFingerprint { get; set; }

    public static InteractiveState Create(InteractiveDefinition definition)
    {
        var state = new InteractiveState();
        if (definition is InteractiveForm form)
        {
            foreach (var field in form.Fields)
                state.Values[field.Id] = field.Value ?? (field.Type == "toggle" ? "false" : "");
        }
        else if (definition is InteractiveCalculatorDefinition calculator)
        {
            foreach (var input in calculator.Inputs)
                state.Values[input.Key] = input.Value.ToString("R", CultureInfo.InvariantCulture);
        }
        return state;
    }

    public static InteractiveState Parse(string json)
    {
        var state = JsonSerializer.Deserialize(json, InteractiveJsonContext.Default.InteractiveState)
            ?? throw new FormatException("Saved interaction state must be a JSON object.");
        InteractiveDefinition.Require(state.Values is not null && state.Values.Values.All(value => value is not null)
            && state.Choices is not null && state.Choices.Values.All(value => value is not null)
            && state.CheckedIds is not null && state.SelectedIds is not null && state.Answers is not null
            && state.CheckedAnswers is not null && state.RevealedHints is not null,
            "Saved interaction collections cannot be null.");
        return state;
    }

    public string ToJson() => JsonSerializer.Serialize(this, InteractiveJsonContext.Default.InteractiveState);

    /// <summary>Identifies the meaningful choices, not a transient submission status.</summary>
    public string Fingerprint()
    {
        var json = (this with { SubmittedFingerprint = null }).ToJson();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public void Validate(InteractiveDefinition definition)
    {
        InteractiveDefinition.Require(Values.Count <= 16 && Choices.Count <= 16
            && CheckedIds.Count <= 100 && SelectedIds.Count <= 40 && Answers.Count <= 20
            && CheckedAnswers.Count <= 20 && RevealedHints.Count <= 20, "Saved interaction state exceeds this control's limits.");
        InteractiveDefinition.Require(CheckedIds.Distinct().Count() == CheckedIds.Count
            && SelectedIds.Distinct().Count() == SelectedIds.Count
            && CheckedAnswers.Distinct().Count() == CheckedAnswers.Count
            && RevealedHints.Distinct().Count() == RevealedHints.Count, "Saved selections cannot contain duplicates.");
        IEnumerable<string> values = definition switch
        {
            InteractiveForm form => form.Fields.Select(field => field.Id),
            InteractiveCalculatorDefinition calculator => calculator.Inputs.Keys,
            _ => []
        };
        InteractiveDefinition.Require(Values.Keys.All(values.Contains), "Saved values reference unknown inputs.");
        if (definition is InteractiveForm inputForm)
        {
            foreach (var choice in Choices)
            {
                var field = inputForm.Fields.FirstOrDefault(field => field.Id == choice.Key && field.Type == "multiselect");
                InteractiveDefinition.Require(field is not null && choice.Value.All(field.Options.Contains),
                    "Saved choices reference unknown options.");
            }
        }
        else
            InteractiveDefinition.Require(Choices.Count == 0, "This interaction has no multi-select fields.");

        var itemIds = definition switch
        {
            InteractiveChecklist checklist => checklist.Items.Select(item => item.Id).ToArray(),
            InteractiveResults results => results.Items.Select(item => item.Id).ToArray(),
            _ => []
        };
        InteractiveDefinition.Require(CheckedIds.All(itemIds.Contains) && SelectedIds.All(itemIds.Contains),
            "Saved selection references an unknown item.");
        if (definition is InteractiveQuiz quiz)
        {
            InteractiveDefinition.Require(QuestionIndex >= 0 && QuestionIndex < quiz.Questions.Count,
                "Saved quiz position is invalid.");
            foreach (var answer in Answers)
            {
                var question = quiz.Questions.FirstOrDefault(question => question.Id == answer.Key);
                InteractiveDefinition.Require(question is not null && answer.Value >= 0 && answer.Value < question.Options.Count,
                    "Saved answer references an unknown option.");
            }
            var ids = quiz.Questions.Select(question => question.Id).ToArray();
            InteractiveDefinition.Require(CheckedAnswers.All(ids.Contains) && RevealedHints.All(ids.Contains),
                "Saved quiz state references an unknown question.");
            InteractiveDefinition.Require(CheckedAnswers.All(Answers.ContainsKey), "Checked quiz answers must have a selection.");
        }
        else
            InteractiveDefinition.Require(Answers.Count == 0 && CheckedAnswers.Count == 0 && RevealedHints.Count == 0,
                "This interaction has no quiz answers.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InteractiveForm))]
[JsonSerializable(typeof(InteractiveChecklist))]
[JsonSerializable(typeof(InteractiveResults))]
[JsonSerializable(typeof(InteractiveCalculatorDefinition))]
[JsonSerializable(typeof(InteractiveQuiz))]
[JsonSerializable(typeof(InteractiveState))]
internal partial class InteractiveJsonContext : JsonSerializerContext;
