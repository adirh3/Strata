using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Logging;

namespace StrataTheme.Controls;

internal static partial class MarkdownClipboardFormatter
{
    internal static string ToPortableMarkdown(
        string markdown,
        IReadOnlyDictionary<string, string>? interactionStates = null)
    {
        var source = MarkdownParser.NormalizeLineEndings(markdown);
        var blocks = MarkdownParser.Parse(source);
        StringBuilder? result = null;
        var copiedUntil = 0;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (!block.IsClosed || block.Kind is not (MdBlockKind.Chart or MdBlockKind.Confidence
                or MdBlockKind.Comparison or MdBlockKind.Card or MdBlockKind.Sources or MdBlockKind.Interactive))
                continue;

            string replacement;
            try
            {
                replacement = block.Kind == MdBlockKind.Interactive
                    ? ExportInteraction(block, interactionStates)
                    : ExportVisualization(block);
            }
            catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                // Keep invalid content verbatim, rather than silently dropping part of the answer.
                Logger.TryGet(LogEventLevel.Warning, "Clipboard")?.Log(
                    null, "Could not export {Kind}: {Error}", block.Language, error.Message);
                continue;
            }

            result ??= new StringBuilder(source.Length);
            result.Append(source, copiedUntil, block.SourceStart - copiedUntil);
            result.Append(replacement.Trim()).Append("\n\n");
            copiedUntil = index + 1 < blocks.Count ? blocks[index + 1].SourceStart : source.Length;
        }

        return result is null
            ? source
            : result.Append(source, copiedUntil, source.Length - copiedUntil).ToString().TrimEnd();
    }

    internal static string ToPlainText(string markdown)
    {
        var parts = new List<string>();
        foreach (var block in MarkdownParser.Parse(ToPortableMarkdown(markdown)))
        {
            var text = block.Kind switch
            {
                MdBlockKind.CodeBlock or MdBlockKind.Mermaid => block.Content,
                MdBlockKind.HorizontalRule => "---",
                MdBlockKind.Bullet => "- " + InlineToPlainText(block.Content),
                MdBlockKind.NumberedItem => $"{block.Level}. " + InlineToPlainText(block.Content),
                MdBlockKind.TaskItem => (block.Level == 1 ? "[x] " : "[ ] ") + InlineToPlainText(block.Content),
                MdBlockKind.Image => $"{block.Content} ({block.Language})",
                MdBlockKind.Table => string.Join("\n", block.Content.Split('\n')
                    .Where(line => !MarkdownParser.IsTableSeparator(line) && !string.IsNullOrWhiteSpace(line))
                    .Select(line => string.Join("\t", MarkdownParser.TrimCells(MarkdownParser.SplitTableCells(line))
                        .Select(InlineToPlainText)))),
                _ => InlineToPlainText(block.Content)
            };
            parts.Add(text);
        }
        return string.Join("\n\n", parts).Trim();
    }

    private static string InlineToPlainText(string text)
    {
        var html = new StringBuilder();
        AppendInlineHtml(html, text);
        var fragment = Regex.Replace(html.ToString(), @"<br\s*/?>", "\n");
        fragment = Regex.Replace(fragment, "<img[^>]*alt=\"([^\"]*)\"[^>]*>", "$1");
        return WebUtility.HtmlDecode(Regex.Replace(fragment, "<[^>]*>", ""));
    }

    private static string ExportVisualization(MdBlock block)
    {
        using var document = StrataMarkdown.ParseVizJson(block.Content.Trim(), allowRepair: true);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("A visualization must be an object.");
        return block.Kind switch
        {
            MdBlockKind.Card => JoinSections(Heading(String(root, "header")), String(root, "summary"), String(root, "detail")),
            MdBlockKind.Confidence => JoinSections(
                $"**{Escape(String(root, "label", "Confidence"))}: {root.GetProperty("value").GetDouble().ToString("G", CultureInfo.InvariantCulture)}%**",
                String(root, "explanation")),
            MdBlockKind.Comparison => JoinSections(
                ExportOption(root.GetProperty("optionA")), ExportOption(root.GetProperty("optionB"))),
            MdBlockKind.Chart => ExportChart(root),
            MdBlockKind.Sources => ExportSources(root),
            _ => throw new FormatException("Unsupported visualization.")
        };
    }

    private static string ExportOption(JsonElement option) =>
        JoinSections(Heading(String(option, "title")), String(option, "content"));

    private static string ExportChart(JsonElement root)
    {
        var labels = root.GetProperty("labels").EnumerateArray().Select(label => label.GetString() ?? "").ToArray();
        var series = root.GetProperty("series").EnumerateArray().ToArray();
        var table = new StringBuilder();
        AppendRow(table, new[] { "Category" }.Concat(series.Select(item => String(item, "name"))));
        AppendRow(table, Enumerable.Repeat("---", series.Length + 1));
        for (var index = 0; index < labels.Length; index++)
        {
            var row = new List<string> { labels[index] };
            foreach (var item in series)
            {
                var values = item.GetProperty("values");
                if (index >= values.GetArrayLength())
                    throw new FormatException("Chart values must match its labels.");
                row.Add(values[index].GetDouble().ToString("G", CultureInfo.InvariantCulture));
            }
            AppendRow(table, row);
        }
        return JoinSections(String(root, "donutCenterLabel"), String(root, "donutCenterValue"), table.ToString());
    }

    private static string ExportSources(JsonElement root)
    {
        var result = new StringBuilder();
        foreach (var source in root.GetProperty("sources").EnumerateArray())
        {
            var title = String(source, "title", "Source");
            var target = String(source, "url", String(source, "origin"));
            result.Append("- ").Append(Link(title, target)).Append('\n');
            var snippet = String(source, "snippet");
            if (snippet.Length > 0)
                result.Append("  ").Append(snippet.Replace("\n", "\n  ")).Append('\n');
        }
        return result.ToString();
    }

    private static string ExportInteraction(MdBlock block, IReadOnlyDictionary<string, string>? states)
    {
        var definition = InteractiveDefinition.Parse(block.Language, block.Content);
        var state = states?.TryGetValue(definition.Id, out var saved) == true
            ? InteractiveState.Parse(saved)
            : InteractiveState.Create(definition);
        var labels = InteractiveLabels.ForLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var result = new StringBuilder(JoinSections(Heading(definition.Title), definition.Description));
        result.Append("\n\n");
        switch (definition)
        {
            case InteractiveChecklist checklist:
                for (var index = 0; index < checklist.Items.Count; index++)
                {
                    var item = checklist.Items[index];
                    result.Append(checklist.Numbered ? $"{index + 1}. " : "- ");
                    result.Append(state.CheckedIds.Contains(item.Id) ? "[x] " : "[ ] ");
                    AppendItem(result, item);
                }
                break;
            case InteractiveResults results:
                foreach (var item in results.Items)
                {
                    result.Append("- ");
                    if (state.SelectedIds.Contains(item.Id))
                        result.Append("[x] ");
                    AppendItem(result, item);
                }
                break;
            case InteractiveForm form:
                AppendRow(result, ["Field", "Value"]);
                AppendRow(result, ["---", "---"]);
                foreach (var field in form.Fields)
                    AppendRow(result, [field.Label, InteractiveReceiptFormatter.FormValue(field, state, labels)]);
                break;
            case InteractiveCalculatorDefinition calculator:
                AppendRow(result, ["Input", "Value"]);
                AppendRow(result, ["---", "---"]);
                foreach (var input in InteractiveCalculator.GetInputs(calculator.Template))
                    AppendRow(result, [labels[input.Id],
                        InteractiveFormat.Input(input, state.Values.GetValueOrDefault(input.Id) ?? "", calculator.Currency, labels)]);
                if (InteractiveCalculator.FindInvalidInput(calculator, state) is { } invalid)
                {
                    result.Append('\n').Append(labels[invalid.Input.Id]).Append(": ")
                        .Append(labels[invalid.Problem == "number" ? "number" : "range"]);
                    break;
                }
                result.Append('\n');
                AppendRow(result, ["Result", "Value"]);
                AppendRow(result, ["---", "---"]);
                var calculation = InteractiveCalculator.Calculate(calculator, state);
                foreach (var metric in calculation.Metrics)
                    AppendRow(result, [labels[InteractiveReceiptFormatter.MetricLabelKey(metric.Key)],
                        InteractiveFormat.Value(metric.Value, metric.Kind, calculator.Currency, labels)]);
                foreach (var ingredient in calculation.Ingredients)
                    AppendRow(result, [ingredient.Label,
                        $"{ingredient.Quantity.ToString("G", CultureInfo.InvariantCulture)} {ingredient.Unit}".TrimEnd()]);
                if (calculator.Template is "savings" or "loan")
                    result.Append('\n').Append(labels[calculator.Template == "savings" ? "savingsNote" : "loanNote"]);
                break;
            case InteractiveQuiz quiz:
                for (var index = 0; index < quiz.Questions.Count; index++)
                {
                    var question = quiz.Questions[index];
                    result.Append(index + 1).Append(". ").Append(question.Prompt).Append("\n\n");
                    for (var option = 0; option < question.Options.Count; option++)
                    {
                        var selected = state.Answers.TryGetValue(question.Id, out var answer) && answer == option;
                        result.Append(selected ? "   - [x] " : "   - [ ] ")
                            .Append(Escape(question.Options[option])).Append('\n');
                    }
                    if (state.CheckedAnswers.Contains(question.Id))
                        result.Append("\n   ").Append(question.Explanation.Replace("\n", "\n   ")).Append('\n');
                    result.Append('\n');
                }
                break;
        }
        return result.ToString();
    }

    private static void AppendItem(StringBuilder result, InteractiveItem item)
    {
        result.Append(Link(item.Label, item.Url));
        if (!string.IsNullOrEmpty(item.Meta))
            result.Append(" (").Append(Escape(item.Meta)).Append(')');
        result.Append('\n');
        if (!string.IsNullOrEmpty(item.Detail))
            result.Append("  ").Append(item.Detail.Replace("\n", "\n  ")).Append('\n');
    }

    private static string String(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;

    private static string Link(string label, string? target) =>
        !string.IsNullOrEmpty(target) && TryGetSafeUri(target, out var uri)
            ? $"[{Escape(label)}]({uri.AbsoluteUri.Replace("(", "%28").Replace(")", "%29")})"
            : string.IsNullOrWhiteSpace(target) ? Escape(label) : $"{Escape(label)} ({Escape(target)})";

    private static string Heading(string text) => string.IsNullOrWhiteSpace(text) ? "" : "### " + Escape(text);
    private static string JoinSections(params string?[] sections) =>
        string.Join("\n\n", sections.Where(section => !string.IsNullOrWhiteSpace(section)).Select(section => section!.Trim()));

    private static string Escape(string text) =>
        Regex.Replace(text.Replace("\r", "").Replace("\n", " "), @"([`*_\[\]<>#])", @"\$1");

    internal static void AppendRow(StringBuilder result, IEnumerable<string> cells) =>
        result.Append("| ").AppendJoin(" | ", cells.Select(EscapeTableCell)).Append(" |\n");

    internal static string EscapeTableCell(string text)
    {
        var normalized = text.Replace("\r", "").Replace("\n", " / ");
        var result = new StringBuilder(normalized.Length);
        var inCode = false;
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '`' && (inCode || index == 0 || normalized[index - 1] != '\\'))
                inCode = !inCode;
            if (character == '|' || character == '\\' && !inCode)
                result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }
}
