using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StrataTheme.Controls;

/// <summary>How a calculator value is presented and adorned.</summary>
public enum InteractiveValueKind { Number, Money, Percent, Years, Multiplier }

public readonly record struct InteractiveCalculatorInput(
    string Id, double Minimum, double Maximum, bool Integer = false, InteractiveValueKind Kind = InteractiveValueKind.Number);

/// <summary>A calculated result. The first metric of a calculation is its headline.</summary>
public readonly record struct InteractiveMetric(string Key, double Value, InteractiveValueKind Kind = InteractiveValueKind.Number);

public sealed record InteractiveCalculation
{
    public List<InteractiveMetric> Metrics { get; init; } = [];
    public List<InteractiveIngredient> Ingredients { get; init; } = [];
    public List<string> ChartLabels { get; init; } = [];
    public List<double> ChartValues { get; init; } = [];
    public List<double> Contributions { get; init; } = [];
}

/// <summary>Deterministic local calculations. No expressions, scripts, network access, or model calls.</summary>
public static class InteractiveCalculator
{
    public static IReadOnlyList<InteractiveCalculatorInput> GetInputs(string template) => template switch
    {
        "bill-split" =>
        [
            new("bill", 0, 1_000_000, Kind: InteractiveValueKind.Money),
            new("tipPercent", 0, 100, Kind: InteractiveValueKind.Percent),
            new("people", 1, 1000, true)
        ],
        "savings" =>
        [
            new("initial", 0, 100_000_000, Kind: InteractiveValueKind.Money),
            new("monthly", 0, 1_000_000, Kind: InteractiveValueKind.Money),
            new("annualRate", 0, 100, Kind: InteractiveValueKind.Percent),
            new("years", 1, 100, true, InteractiveValueKind.Years)
        ],
        "servings" => [new("baseServings", 1, 1000, true), new("servings", 1, 1000, true)],
        "budget" =>
        [
            new("income", 0, 100_000_000, Kind: InteractiveValueKind.Money),
            new("essential", 0, 100_000_000, Kind: InteractiveValueKind.Money),
            new("flexible", 0, 100_000_000, Kind: InteractiveValueKind.Money),
            new("savings", 0, 100_000_000, Kind: InteractiveValueKind.Money)
        ],
        "loan" =>
        [
            new("loanAmount", 0, 100_000_000, Kind: InteractiveValueKind.Money),
            new("interestRate", 0, 100, Kind: InteractiveValueKind.Percent),
            new("years", 1, 50, true, InteractiveValueKind.Years)
        ],
        _ => throw new FormatException($"Unsupported calculator template '{template}'.")
    };

    public static void ValidateDefinition(InteractiveCalculatorDefinition definition)
    {
        var inputs = GetInputs(definition.Template);
        InteractiveDefinition.Require(definition.Inputs is not null && definition.Inputs.Count == inputs.Count
            && inputs.All(input => definition.Inputs.ContainsKey(input.Id)),
            $"'{definition.Template}' needs these inputs: {string.Join(", ", inputs.Select(input => input.Id))}.");
        if (definition.Template == "servings")
        {
            InteractiveDefinition.Require(definition.Ingredients is { Count: > 0 and <= 50 }
                && definition.Ingredients.All(ingredient => ingredient is not null), "Servings need 1-50 ingredients.");
            foreach (var ingredient in definition.Ingredients)
                InteractiveDefinition.Require(!string.IsNullOrWhiteSpace(ingredient.Label)
                    && double.IsFinite(ingredient.Quantity) && ingredient.Quantity >= 0,
                    "Ingredient quantities must be finite and non-negative.");
        }
        Calculate(definition, InteractiveState.Create(definition));
    }

    /// <summary>The first input that is missing or not a number ("number"), or outside its allowed range ("range").</summary>
    public static (InteractiveCalculatorInput Input, string Problem)? FindInvalidInput(
        InteractiveCalculatorDefinition definition, InteractiveState state)
    {
        foreach (var input in GetInputs(definition.Template))
        {
            if (!state.Values.TryGetValue(input.Id, out var text)
                || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
                return (input, "number");
            if (value < input.Minimum || value > input.Maximum || input.Integer && value != Math.Truncate(value))
                return (input, "range");
        }
        return null;
    }

    public static InteractiveCalculation Calculate(InteractiveCalculatorDefinition definition, InteractiveState state)
    {
        if (FindInvalidInput(definition, state) is { } invalid)
            throw new FormatException(invalid.Problem == "number"
                ? $"Enter a number for '{invalid.Input.Id}'."
                : $"'{invalid.Input.Id}' must be {(invalid.Input.Integer ? "a whole number " : "")}between {invalid.Input.Minimum} and {invalid.Input.Maximum}.");
        var values = GetInputs(definition.Template).ToDictionary(
            input => input.Id,
            input => double.Parse(state.Values[input.Id], NumberStyles.Float, CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        const InteractiveValueKind money = InteractiveValueKind.Money;
        switch (definition.Template)
        {
            case "bill-split":
            {
                var bill = (decimal)values["bill"];
                var tip = bill * (decimal)values["tipPercent"] / 100;
                var total = bill + tip;
                return new InteractiveCalculation
                {
                    Metrics =
                    [
                        new("perPerson", (double)(total / (decimal)values["people"]), money),
                        new("total", (double)total, money),
                        new("tip", (double)tip, money)
                    ]
                };
            }
            case "savings":
            {
                var initial = values["initial"];
                var monthly = values["monthly"];
                var rate = values["annualRate"] / 1200;
                var years = (int)values["years"];
                var balance = initial;
                var labels = new List<string> { "0" };
                var balances = new List<double> { balance };
                var contributions = new List<double> { initial };
                for (var month = 1; month <= years * 12; month++)
                {
                    balance = balance * (1 + rate) + monthly;
                    InteractiveDefinition.Require(double.IsFinite(balance), "These assumptions exceed the calculator's numeric range.");
                    if (month % 12 == 0)
                    {
                        labels.Add((month / 12).ToString(CultureInfo.InvariantCulture));
                        balances.Add(balance);
                        contributions.Add(initial + monthly * month);
                    }
                }
                var contributed = initial + monthly * years * 12;
                return new InteractiveCalculation
                {
                    Metrics = [new("projected", balance, money), new("contributed", contributed, money), new("growth", balance - contributed, money)],
                    ChartLabels = labels,
                    ChartValues = balances,
                    Contributions = contributions
                };
            }
            case "servings":
            {
                var multiplier = values["servings"] / values["baseServings"];
                return new InteractiveCalculation
                {
                    Metrics = [new("servings", values["servings"]), new("multiplier", multiplier, InteractiveValueKind.Multiplier)],
                    Ingredients = definition.Ingredients.Select(ingredient => ingredient with { Quantity = ingredient.Quantity * multiplier }).ToList()
                };
            }
            case "budget":
            {
                var spent = values["essential"] + values["flexible"] + values["savings"];
                return new InteractiveCalculation
                {
                    Metrics = [new("remaining", values["income"] - spent, money), new("allocated", spent, money)],
                    ChartLabels = ["essential", "flexible", "savings"],
                    ChartValues = [values["essential"], values["flexible"], values["savings"]]
                };
            }
            case "loan":
            {
                var principal = values["loanAmount"];
                var months = (int)values["years"] * 12;
                var rate = values["interestRate"] / 1200;
                var payment = rate == 0 ? principal / months : principal * rate / (1 - Math.Pow(1 + rate, -months));
                InteractiveDefinition.Require(double.IsFinite(payment), "These assumptions exceed the calculator's numeric range.");
                var totalPaid = payment * months;
                return new InteractiveCalculation
                {
                    Metrics = [new("payment", payment, money), new("totalInterest", totalPaid - principal, money), new("totalPaid", totalPaid, money)],
                    ChartLabels = ["principal", "interest"],
                    ChartValues = [principal, totalPaid - principal]
                };
            }
            default:
                throw new FormatException($"Unsupported calculator template '{definition.Template}'.");
        }
    }
}

/// <summary>Culture-aware presentation of calculator values. Currency codes label money; no conversion happens.</summary>
public static class InteractiveFormat
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = "$", ["EUR"] = "\u20ac", ["GBP"] = "\u00a3", ["ILS"] = "\u20aa",
        ["JPY"] = "\u00a5", ["INR"] = "\u20b9", ["KRW"] = "\u20a9"
    };

    /// <summary>The symbol drawn before an amount, or null when the currency is shown by code.</summary>
    public static string? Symbol(string? currency) =>
        currency is { Length: > 0 } && Symbols.TryGetValue(currency, out var symbol) ? symbol : null;

    public static string Money(double value, string? currency)
    {
        var rounded = Math.Round(value, 2);
        var decimals = Math.Abs(rounded) >= 1000 || rounded == Math.Truncate(rounded) ? 0 : 2;
        var amount = Math.Abs(rounded).ToString("N" + decimals, CultureInfo.CurrentCulture);
        var sign = rounded < 0 ? "-" : "";
        if (Symbol(currency) is { } symbol)
            return sign + symbol + amount;
        return string.IsNullOrWhiteSpace(currency) ? sign + amount : sign + amount + " " + currency.Trim().ToUpperInvariant();
    }

    public static string Value(double value, InteractiveValueKind kind, string? currency, InteractiveLabels labels) => kind switch
    {
        InteractiveValueKind.Money => Money(value, currency),
        InteractiveValueKind.Percent => value.ToString("0.##", CultureInfo.CurrentCulture) + "%",
        InteractiveValueKind.Years => labels.Format("yearsValue", value.ToString("0", CultureInfo.CurrentCulture)),
        InteractiveValueKind.Multiplier => value.ToString("0.##", CultureInfo.CurrentCulture) + "\u00d7",
        _ => value.ToString("#,0.##", CultureInfo.CurrentCulture)
    };

    public static string Input(InteractiveCalculatorInput input, string raw, string? currency, InteractiveLabels labels) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Value(value, input.Kind, currency, labels) : raw;

    /// <summary>A form number with its unit; a currency code unit is presented as money.</summary>
    public static string Number(string raw, string? unit)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return raw;
        if (unit is { Length: 3 } && unit.All(char.IsLetter) && unit.ToUpperInvariant() == unit)
            return Money(value, unit);
        var text = value.ToString("#,0.##", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(unit) ? text : text + " " + unit.Trim();
    }

    public static string Date(DateTime date) =>
        date.ToString(date.Year == DateTime.Today.Year ? "ddd, MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);
}
