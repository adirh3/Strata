using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace StrataTheme.Controls;

public enum ExplorerAttributeType { Text, Number, Money, Rating, Boolean }

/// <summary>An attribute as the explorer presents it: declared by the definition or inferred from item values.</summary>
public sealed class ExplorerAttribute
{
    internal ExplorerAttribute(string id, string label, ExplorerAttributeType type, string? unit, int better)
    {
        Id = id;
        Label = label;
        Type = type;
        Unit = unit;
        Better = better;
    }

    public string Id { get; }
    public string Label { get; }
    public ExplorerAttributeType Type { get; }
    /// <summary>A unit such as "Hz", or a money attribute's currency code.</summary>
    public string? Unit { get; }
    /// <summary>1 when higher values are better, -1 when lower ones are, 0 when neither is.</summary>
    public int Better { get; }
    /// <summary>The top of a rating scale (5, 10 or 100), read from the ratings the items carry.</summary>
    public double Scale { get; internal set; } = 5;
    public bool IsNumeric => Type is ExplorerAttributeType.Number or ExplorerAttributeType.Money or ExplorerAttributeType.Rating;
}

/// <summary>One item's value for one attribute. A number that could not be read stays displayable text.</summary>
public readonly record struct ExplorerValue(double? Number, string? Text, bool? Flag)
{
    public bool IsEmpty => Number is null && Text is null && Flag is null;
}

public enum ExplorerFacetKind { Choice, Flag, Threshold }

/// <summary>A filter the explorer derives from an attribute's values.</summary>
public sealed class ExplorerFacet
{
    internal ExplorerFacet(ExplorerAttribute attribute, ExplorerFacetKind kind, IReadOnlyList<string> options,
        double minimum = 0, double maximum = 0, double step = 0, bool isUpperBound = false)
    {
        Attribute = attribute;
        Kind = kind;
        Options = options;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        IsUpperBound = isUpperBound;
    }

    public ExplorerAttribute Attribute { get; }
    public ExplorerFacetKind Kind { get; }
    /// <summary>A choice's options, in the order the items first use them.</summary>
    public IReadOnlyList<string> Options { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    /// <summary>True for "up to" (keeps lower values), false for "and up".</summary>
    public bool IsUpperBound { get; }
    /// <summary>The saved state key: <c>Choices[Key]</c> for choices and features, <c>Values[Key]</c> for bounds.</summary>
    public string Key => Kind == ExplorerFacetKind.Threshold ? BoundKey(Attribute.Id, IsUpperBound) : Attribute.Id;

    internal static string BoundKey(string attributeId, bool upper) => (upper ? "max:" : "min:") + attributeId;
}

/// <summary>An active filter: a chosen option, a required feature, a bound, or the search text.</summary>
public sealed record ExplorerFilter(ExplorerFacet? Facet, string? Option, double? Bound, string? Query);

/// <summary>A sort order: an attribute, or the item names when <see cref="AttributeId"/> is empty.</summary>
public readonly record struct ExplorerSort(string AttributeId, bool Descending)
{
    public string Key => AttributeId + (Descending ? ":desc" : ":asc");
}

/// <summary>
/// The presentation-independent logic of an option explorer: typed values, derived filters, sorting,
/// best values and the text describing items and filters. The control, submissions, receipts and copy
/// share it, so they always agree.
/// </summary>
/// <remarks>
/// Saved state: <c>Values["view"]</c> (cards or table), <c>Values["sort"]</c>, <c>Values["query"]</c>,
/// <c>Values["max:id"]</c>/<c>Values["min:id"]</c> bounds, <c>Choices[id]</c> chosen options
/// (<c>["true"]</c> for a required feature) and <c>SelectedIds</c>, the options picked to compare.
/// </remarks>
public sealed class InteractiveExplorerModel
{
    public const int MaxItems = 40;
    public const int MaxAttributes = 12;
    public const int MaxSelected = 4;
    public const int MaxQueryLength = 200;
    private const int MaxChoiceOptions = 8;
    private const int MaxChoiceLength = 32;
    // No option's price or spec is larger; beyond it, ranges and slider steps could overflow.
    private const double MaxMagnitude = 1e15;

    private static readonly ConditionalWeakTable<InteractiveExplorer, InteractiveExplorerModel> Models = new();
    private readonly Dictionary<string, ExplorerAttribute> _attributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, ExplorerValue>> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _searchText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _texts = new(StringComparer.Ordinal);

    private InteractiveExplorerModel(InteractiveExplorer definition)
    {
        Definition = definition;
        var items = (definition.Items ?? []).Where(item => item is not null).ToList();
        var attributes = new List<ExplorerAttribute>();
        foreach (var declared in definition.Attributes ?? [])
        {
            if (declared is null || string.IsNullOrWhiteSpace(declared.Id) || _attributes.ContainsKey(declared.Id))
                continue;
            var type = ParseType(declared.Type);
            var better = declared.Better switch
            {
                "higher" => 1,
                "lower" => -1,
                _ => type switch { ExplorerAttributeType.Money => -1, ExplorerAttributeType.Rating => 1, _ => 0 }
            };
            Add(new ExplorerAttribute(declared.Id, LabelFor(declared.Label, declared.Id), type, Clean(declared.Unit), better));
        }
        // Values the definition did not declare still show, typed by what the items carry.
        foreach (var key in items.SelectMany(item => item.Values?.Keys ?? Enumerable.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(key) || _attributes.ContainsKey(key) || attributes.Count >= MaxAttributes)
                continue;
            var type = Infer(items.Select(item => item.Values is { } values && values.TryGetValue(key, out var value)
                ? value : default));
            Add(new ExplorerAttribute(key, LabelFor(null, key), type, null, 0));
        }
        Attributes = attributes;

        foreach (var item in items)
        {
            var values = new Dictionary<string, ExplorerValue>(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                if (item.Values?.TryGetValue(attribute.Id, out var element) == true
                    && Normalize(element, attribute.Type) is { IsEmpty: false } value)
                    values[attribute.Id] = value;
            }
            _values[item.Id] = values;
            _searchText[item.Id] = string.Join('\n', new[] { item.Label, item.Subtitle, item.Badge, item.Summary }
                .Concat(item.Pros ?? []).Concat(item.Cons ?? [])
                .Concat(values.Values.Select(value => value.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        var facets = new List<ExplorerFacet>();
        foreach (var attribute in attributes)
        {
            var values = items.Select(item => Value(item, attribute)).ToList();
            _texts[attribute.Id] = new HashSet<string>(
                values.Where(value => value.Text is not null).Select(value => value.Text!), StringComparer.OrdinalIgnoreCase);
            switch (attribute.Type)
            {
                case ExplorerAttributeType.Boolean:
                    var on = values.Count(value => value.Flag == true);
                    if (on > 0 && on < values.Count)
                        facets.Add(new ExplorerFacet(attribute, ExplorerFacetKind.Flag, ["true"]));
                    break;
                case ExplorerAttributeType.Text:
                    var options = values.Where(value => value.Text is not null).Select(value => value.Text!)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (options.Count is >= 2 and <= MaxChoiceOptions && options.All(option => option.Length <= MaxChoiceLength))
                        facets.Add(new ExplorerFacet(attribute, ExplorerFacetKind.Choice, options));
                    break;
                default:
                    var numbers = values.Where(value => value.Number is not null).Select(value => value.Number!.Value).ToList();
                    if (attribute.Type == ExplorerAttributeType.Rating && numbers.Count > 0)
                        attribute.Scale = numbers.Max() <= 5 ? 5 : numbers.Max() <= 10 ? 10 : 100;
                    if (numbers.Count < 3 || numbers.Max() <= numbers.Min())
                        break;
                    var step = NiceStep((numbers.Max() - numbers.Min()) / 20);
                    if (attribute.Type == ExplorerAttributeType.Rating)
                        step = Math.Max(step, attribute.Scale >= 100 ? 1 : 0.1);
                    // Counts and years move in whole steps: there is no 1.2 of a port.
                    if (numbers.All(number => number == Math.Floor(number)))
                        step = Math.Max(step, 1);
                    facets.Add(new ExplorerFacet(attribute, ExplorerFacetKind.Threshold, [],
                        // The nudge keeps 4.3 / 0.1 = 42.999… from starting the slider a step low.
                        Snap(Math.Floor(numbers.Min() / step + 1e-9) * step, step),
                        Snap(Math.Ceiling(numbers.Max() / step - 1e-9) * step, step),
                        step, isUpperBound: attribute.Better <= 0));
                    break;
            }
        }
        Facets = facets;
        Headline = attributes.FirstOrDefault(attribute => attribute.Type == ExplorerAttributeType.Money)
            ?? attributes.FirstOrDefault(attribute => attribute.Type == ExplorerAttributeType.Number && attribute.Better != 0);
        Rating = attributes.FirstOrDefault(attribute => attribute.Type == ExplorerAttributeType.Rating);
        HasMedia = items.Any(item => !string.IsNullOrWhiteSpace(item.Image));

        var sorts = new List<ExplorerSort>();
        foreach (var attribute in attributes.Where(attribute => attribute.IsNumeric))
        {
            sorts.Add(new ExplorerSort(attribute.Id, attribute.Better >= 0));
            if (attribute.Type == ExplorerAttributeType.Money)
                sorts.Add(new ExplorerSort(attribute.Id, attribute.Better < 0));
        }
        Sorts = sorts;

        void Add(ExplorerAttribute attribute)
        {
            // The most important attributes come first; past the cap, extra columns are left out
            // rather than failing the whole answer.
            if (attributes.Count >= MaxAttributes)
                return;
            _attributes[attribute.Id] = attribute;
            attributes.Add(attribute);
        }
    }

    public static InteractiveExplorerModel For(InteractiveExplorer definition) =>
        Models.GetValue(definition, static explorer => new InteractiveExplorerModel(explorer));

    public InteractiveExplorer Definition { get; }
    public IReadOnlyList<ExplorerAttribute> Attributes { get; }
    public IReadOnlyList<ExplorerFacet> Facets { get; }
    /// <summary>Sort orders offered besides the definition's own, recommended order.</summary>
    public IReadOnlyList<ExplorerSort> Sorts { get; }
    /// <summary>The figure a card leads with: the first money attribute, else a ranked number.</summary>
    public ExplorerAttribute? Headline { get; }
    public ExplorerAttribute? Rating { get; }
    /// <summary>
    /// Whether any option has a picture of its own. Link previews may add pictures once they load;
    /// the default view follows only these, so it never changes under the reader.
    /// </summary>
    public bool HasMedia { get; }

    /// <summary>Pictures make cards worth their room; many plain options read faster as a table.</summary>
    public string DefaultView => !HasMedia && Attributes.Count >= 2 && Definition.Items.Count >= 7 ? "table" : "cards";

    public string View(InteractiveState state) =>
        state.Values.GetValueOrDefault("view") is "cards" or "table" && Attributes.Count > 0
            ? state.Values["view"] : DefaultView;

    public ExplorerAttribute? Attribute(string id) => _attributes.GetValueOrDefault(id);

    public ExplorerValue Value(InteractiveExplorerItem item, ExplorerAttribute attribute) =>
        _values.TryGetValue(item.Id, out var values) && values.TryGetValue(attribute.Id, out var value) ? value : default;

    public ExplorerFacet? Facet(string key) => Facets.FirstOrDefault(facet => facet.Key == key);

    public ExplorerSort? Sort(InteractiveState state) => ParseSort(state.Values.GetValueOrDefault("sort"));

    public ExplorerSort? ParseSort(string? key)
    {
        var separator = key?.LastIndexOf(':') ?? -1;
        if (separator < 0)
            return null;
        var id = key![..separator];
        var direction = key[(separator + 1)..];
        if (direction is not ("asc" or "desc") || id.Length > 0 && !_attributes.ContainsKey(id))
            return null;
        return new ExplorerSort(id, direction == "desc");
    }

    /// <summary>The options that pass the saved filters, in the saved order.</summary>
    /// <remarks>Options chosen in one filter combine with OR; different filters combine with AND.</remarks>
    public IReadOnlyList<InteractiveExplorerItem> Apply(InteractiveState state)
    {
        var query = state.Values.GetValueOrDefault("query")?.Trim() ?? "";
        var filters = ActiveFilters(state).Where(filter => filter.Facet is not null)
            .GroupBy(filter => filter.Facet!.Key).ToList();
        var matches = Definition.Items.Where(item =>
            (query.Length == 0 || _searchText.GetValueOrDefault(item.Id, "").Contains(query, StringComparison.CurrentCultureIgnoreCase))
            && filters.All(group => group.Any(filter => Passes(item, filter)))).ToList();
        return Sort(state) is { } sort ? Order(matches, sort) : matches;
    }

    private bool Passes(InteractiveExplorerItem item, ExplorerFilter filter)
    {
        var facet = filter.Facet!;
        var value = Value(item, facet.Attribute);
        return facet.Kind switch
        {
            ExplorerFacetKind.Flag => value.Flag == true,
            ExplorerFacetKind.Choice => value.Text is { } text && string.Equals(text, filter.Option, StringComparison.OrdinalIgnoreCase),
            _ => value.Number is { } number && (facet.IsUpperBound
                ? number <= filter.Bound!.Value + 1e-9 : number >= filter.Bound!.Value - 1e-9)
        };
    }

    private List<InteractiveExplorerItem> Order(List<InteractiveExplorerItem> items, ExplorerSort sort)
    {
        var indexed = items.Select((item, index) => (Item: item, Index: index));
        if (sort.AttributeId.Length == 0)
        {
            var byName = sort.Descending
                ? indexed.OrderByDescending(entry => entry.Item.Label, StringComparer.CurrentCultureIgnoreCase)
                : indexed.OrderBy(entry => entry.Item.Label, StringComparer.CurrentCultureIgnoreCase);
            return byName.ThenBy(entry => entry.Index).Select(entry => entry.Item).ToList();
        }
        var attribute = _attributes[sort.AttributeId];
        // Options without a value always come last, whichever way the column is sorted.
        var known = indexed.OrderBy(entry => Value(entry.Item, attribute) is { IsEmpty: true } ? 1 : 0);
        var ordered = attribute.IsNumeric
            ? sort.Descending
                ? known.ThenByDescending(entry => Value(entry.Item, attribute).Number ?? double.MinValue)
                : known.ThenBy(entry => Value(entry.Item, attribute).Number ?? double.MaxValue)
            : attribute.Type == ExplorerAttributeType.Boolean
                ? sort.Descending
                    ? known.ThenByDescending(entry => Value(entry.Item, attribute).Flag == true)
                    : known.ThenBy(entry => Value(entry.Item, attribute).Flag == true)
                : sort.Descending
                    ? known.ThenByDescending(entry => Value(entry.Item, attribute).Text ?? "", StringComparer.CurrentCultureIgnoreCase)
                    : known.ThenBy(entry => Value(entry.Item, attribute).Text ?? "", StringComparer.CurrentCultureIgnoreCase);
        return ordered.ThenBy(entry => entry.Index).Select(entry => entry.Item).ToList();
    }

    /// <summary>Every saved filter, one entry per chosen option, in the order the filters are offered.</summary>
    public IReadOnlyList<ExplorerFilter> ActiveFilters(InteractiveState state)
    {
        var filters = new List<ExplorerFilter>();
        foreach (var facet in Facets)
        {
            switch (facet.Kind)
            {
                case ExplorerFacetKind.Flag when state.Choices.GetValueOrDefault(facet.Key)?.Contains("true") == true:
                    filters.Add(new ExplorerFilter(facet, null, null, null));
                    break;
                case ExplorerFacetKind.Choice when state.Choices.GetValueOrDefault(facet.Key) is { Count: > 0 } chosen:
                    foreach (var option in facet.Options.Where(option => chosen.Contains(option, StringComparer.OrdinalIgnoreCase)))
                        filters.Add(new ExplorerFilter(facet, option, null, null));
                    break;
                case ExplorerFacetKind.Threshold when Bound(state, facet) is { } bound:
                    filters.Add(new ExplorerFilter(facet, null, bound, null));
                    break;
            }
        }
        if (state.Values.GetValueOrDefault("query")?.Trim() is { Length: > 0 } query)
            filters.Add(new ExplorerFilter(null, null, null, query));
        return filters;
    }

    /// <summary>A bound that still narrows the options; one at the end of its range filters nothing.</summary>
    public static double? Bound(InteractiveState state, ExplorerFacet facet) =>
        state.Values.TryGetValue(facet.Key, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var bound)
        && double.IsFinite(bound)
        && (facet.IsUpperBound ? bound < facet.Maximum : bound > facet.Minimum)
            ? bound : null;

    public bool IsFiltered(InteractiveState state) => ActiveFilters(state).Count > 0;

    /// <summary>
    /// Ids holding the best value among <paramref name="items"/>; empty when the attribute ranks
    /// nothing, fewer than two options have a value, or they are all the same.
    /// </summary>
    public IReadOnlySet<string> Best(ExplorerAttribute attribute, IEnumerable<InteractiveExplorerItem> items)
    {
        if (!attribute.IsNumeric || attribute.Better == 0)
            return new HashSet<string>();
        var numbers = items.Select(item => (item.Id, Value(item, attribute).Number))
            .Where(entry => entry.Number is not null).ToList();
        if (numbers.Count < 2)
            return new HashSet<string>();
        var best = attribute.Better > 0 ? numbers.Max(entry => entry.Number!.Value) : numbers.Min(entry => entry.Number!.Value);
        if (numbers.All(entry => Math.Abs(entry.Number!.Value - best) < 1e-9))
            return new HashSet<string>();
        return numbers.Where(entry => Math.Abs(entry.Number!.Value - best) < 1e-9).Select(entry => entry.Id).ToHashSet();
    }

    /// <summary>How far toward the best end of <paramref name="items"/>' range a value sits, from 0 to 1.</summary>
    public double Standing(ExplorerAttribute attribute, double value, IEnumerable<InteractiveExplorerItem> items)
    {
        var numbers = items.Select(item => Value(item, attribute).Number).OfType<double>().ToList();
        if (numbers.Count == 0 || numbers.Max() <= numbers.Min())
            return 1;
        var position = (value - numbers.Min()) / (numbers.Max() - numbers.Min());
        return attribute.Better < 0 ? 1 - position : position;
    }

    public void ValidateState(InteractiveState state)
    {
        InteractiveDefinition.Require(state.SelectedIds.Count <= MaxSelected, "Compare up to four options at a time.");
        foreach (var (key, value) in state.Values)
        {
            var valid = key switch
            {
                "view" => value is "cards" or "table",
                "query" => value.Length <= MaxQueryLength,
                "sort" => value.Length == 0 || ParseSort(value) is not null,
                _ => (key.StartsWith("max:", StringComparison.Ordinal) || key.StartsWith("min:", StringComparison.Ordinal))
                    && _attributes.TryGetValue(key[4..], out var attribute) && attribute.IsNumeric
                    && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bound)
                    && double.IsFinite(bound)
            };
            InteractiveDefinition.Require(valid, "Saved explorer settings are invalid.");
        }
        foreach (var (key, options) in state.Choices)
        {
            var valid = _attributes.TryGetValue(key, out var attribute) && attribute.Type switch
            {
                ExplorerAttributeType.Boolean => options.All(option => option == "true"),
                ExplorerAttributeType.Text => options.All(_texts[key].Contains),
                _ => false
            };
            InteractiveDefinition.Require(valid && options.Distinct(StringComparer.OrdinalIgnoreCase).Count() == options.Count,
                "Saved filters reference unknown options.");
        }
    }

    // ── Text ──

    /// <summary>A value as people read it, in the current culture.</summary>
    public string Format(ExplorerValue value, ExplorerAttribute attribute, InteractiveLabels labels)
    {
        if (value.Number is { } number)
            return FormatNumber(attribute, number);
        if (value.Flag is { } flag)
            return labels[flag ? "yes" : "no"];
        return value.Text ?? "";
    }

    public static string FormatNumber(ExplorerAttribute attribute, double number) => attribute.Type switch
    {
        ExplorerAttributeType.Money => InteractiveFormat.Money(number, attribute.Unit, exact: true),
        ExplorerAttributeType.Rating => number.ToString(attribute.Scale >= 100 ? "0" : "0.0#", CultureInfo.CurrentCulture),
        // Grouping starts at five digits, so years and other four-digit figures read as written (2025).
        _ => WithUnit(number.ToString(Math.Abs(number) < 10_000 ? "0.###" : "#,0.###", CultureInfo.CurrentCulture), attribute.Unit)
    };

    /// <summary>A rating with its scale, such as "4.7/5", where the scale is not drawn as a star.</summary>
    public static string FormatRating(ExplorerAttribute attribute, double number) =>
        FormatNumber(attribute, number) + "/" + attribute.Scale.ToString("0", CultureInfo.CurrentCulture);

    /// <summary>A value written out on its own: a rating with its scale, anything else as <see cref="Format"/> shows it.</summary>
    public string FormatWithScale(ExplorerValue value, ExplorerAttribute attribute, InteractiveLabels labels) =>
        attribute.Type == ExplorerAttributeType.Rating && value.Number is { } rating
            ? FormatRating(attribute, rating)
            : Format(value, attribute, labels);

    public string FormatFilter(ExplorerFilter filter, InteractiveLabels labels)
    {
        if (filter.Query is { } query)
            return labels.Format("searchChip", query);
        var facet = filter.Facet!;
        return facet.Kind switch
        {
            ExplorerFacetKind.Flag => facet.Attribute.Label,
            ExplorerFacetKind.Choice => filter.Option!,
            _ => labels.Format(facet.IsUpperBound ? "boundUpTo" : "boundAndUp", facet.Attribute.Label,
                FormatNumber(facet.Attribute, filter.Bound!.Value))
        };
    }

    /// <summary>Receipt lines for an option: its name, subtitle, values and source.</summary>
    public IEnumerable<string> DescribeItem(InteractiveExplorerItem item, InteractiveLabels labels)
    {
        yield return item.Label;
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
            yield return item.Subtitle!;
        foreach (var attribute in Attributes)
        {
            var value = Value(item, attribute);
            if (!value.IsEmpty)
                yield return $"{attribute.Label}: {FormatWithScale(value, attribute, labels)}";
        }
        if (!string.IsNullOrWhiteSpace(item.Url))
            yield return item.Url!;
    }

    /// <summary>An option for the model: invariant numbers with their units, never localized.</summary>
    internal IEnumerable<string> DescribeItemForModel(InteractiveExplorerItem item)
    {
        yield return $"- {item.Label} [id: {item.Id}]";
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
            yield return "  " + item.Subtitle!.Trim();
        var values = Attributes.Select(attribute => (attribute, Value(item, attribute)))
            .Where(entry => !entry.Item2.IsEmpty)
            .Select(entry => $"{entry.attribute.Label}: {DescribeForModel(entry.Item2, entry.attribute)}").ToList();
        if (values.Count > 0)
            yield return "  " + string.Join("; ", values);
        if (!string.IsNullOrWhiteSpace(item.Url))
            yield return "  Source: " + item.Url;
    }

    internal IEnumerable<string> DescribeFiltersForModel(InteractiveState state)
    {
        foreach (var group in ActiveFilters(state).GroupBy(filter => filter.Facet?.Key ?? ""))
        {
            var first = group.First();
            if (first.Query is { } query)
                yield return $"Search: \"{query}\"";
            else if (first.Facet!.Kind == ExplorerFacetKind.Flag)
                yield return $"Must have: {first.Facet.Attribute.Label}";
            else if (first.Facet.Kind == ExplorerFacetKind.Choice)
                yield return $"{first.Facet.Attribute.Label}: {string.Join(", ", group.Select(filter => filter.Option))}";
            else
                yield return $"{first.Facet.Attribute.Label}: {(first.Facet.IsUpperBound ? "up to " : "at least ")}{DescribeForModel(new ExplorerValue(first.Bound, null, null), first.Facet.Attribute)}";
        }
    }

    internal static string DescribeForModel(ExplorerValue value, ExplorerAttribute attribute)
    {
        if (value.Number is { } number)
        {
            // Exactly as given: a 6.125% rate must not reach the model as 6.13.
            var text = number.ToString("R", CultureInfo.InvariantCulture);
            return attribute.Type switch
            {
                ExplorerAttributeType.Rating => text + "/" + attribute.Scale.ToString("0", CultureInfo.InvariantCulture),
                _ => string.IsNullOrWhiteSpace(attribute.Unit) ? text : text + " " + attribute.Unit
            };
        }
        if (value.Flag is { } flag)
            return flag ? "yes" : "no";
        return value.Text ?? "";
    }

    // ── Reading definitions ──

    private static ExplorerAttributeType ParseType(string? type) => type switch
    {
        "number" => ExplorerAttributeType.Number,
        "money" => ExplorerAttributeType.Money,
        "rating" => ExplorerAttributeType.Rating,
        "boolean" => ExplorerAttributeType.Boolean,
        _ => ExplorerAttributeType.Text
    };

    private static ExplorerAttributeType Infer(IEnumerable<JsonElement> values)
    {
        var kinds = values.Select(value => value.ValueKind)
            .Where(kind => kind is not (JsonValueKind.Undefined or JsonValueKind.Null)).ToList();
        if (kinds.Count > 0 && kinds.All(kind => kind == JsonValueKind.Number))
            return ExplorerAttributeType.Number;
        return kinds.Count > 0 && kinds.All(kind => kind is JsonValueKind.True or JsonValueKind.False)
            ? ExplorerAttributeType.Boolean : ExplorerAttributeType.Text;
    }

    internal static ExplorerValue Normalize(JsonElement element, ExplorerAttributeType type)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDouble(out var number) && Math.Abs(number) < MaxMagnitude:
                return type switch
                {
                    ExplorerAttributeType.Boolean => new(null, null, number != 0),
                    ExplorerAttributeType.Text => new(null, number.ToString(CultureInfo.InvariantCulture), null),
                    _ => new(number, null, null)
                };
            case JsonValueKind.String:
                var text = element.GetString()?.Trim();
                if (string.IsNullOrEmpty(text))
                    return default;
                if (type is ExplorerAttributeType.Number or ExplorerAttributeType.Money or ExplorerAttributeType.Rating
                    && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && Math.Abs(parsed) < MaxMagnitude)
                    return new(parsed, null, null);
                if (type == ExplorerAttributeType.Boolean && ParseFlag(text) is { } flag)
                    return new(null, null, flag);
                return new(null, text, null);
            case JsonValueKind.True:
                return new(null, null, true);
            case JsonValueKind.False:
                return new(null, null, false);
            case JsonValueKind.Array:
                var joined = string.Join(", ", element.EnumerateArray()
                    .Select(part => part.ValueKind switch
                    {
                        JsonValueKind.String => part.GetString()?.Trim(),
                        JsonValueKind.Number => part.GetRawText(),
                        _ => null
                    })
                    .Where(part => !string.IsNullOrEmpty(part)));
                return joined.Length == 0 ? default : new(null, joined, null);
            default:
                return default;
        }
    }

    private static bool? ParseFlag(string text) => text.ToLowerInvariant() switch
    {
        "true" or "yes" or "y" => true,
        "false" or "no" or "n" => false,
        _ => null
    };

    private static string LabelFor(string? label, string id)
    {
        if (!string.IsNullOrWhiteSpace(label))
            return label.Trim();
        var words = id.Replace('_', ' ').Replace('-', ' ').Trim();
        return words.Length == 0 ? id : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    private static string? Clean(string? unit) => string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();

    private static string WithUnit(string number, string? unit) =>
        unit is null ? number : unit is "%" or "\u00b0" or "\u2033" or "\"" or "'" ? number + unit : number + " " + unit;

    /// <summary>A round slider step near <paramref name="raw"/>: 1, 2 or 5 times a power of ten.</summary>
    internal static double NiceStep(double raw)
    {
        if (!(raw > 0) || !double.IsFinite(raw))
            return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        return (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
    }

    internal static double Snap(double value, double step)
    {
        var decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)) + 1, 0, 10);
        return Math.Round(Math.Round(value / step) * step, decimals);
    }
}
