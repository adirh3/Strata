using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private StackPanel? _calculationHost;
    private bool _calculationQueued;
    private bool _calculationValid;
    private InteractiveCalculatorDefinition? _pendingCalculation;

    private void BuildCalculator(InteractiveCalculatorDefinition calculator)
    {
        var inputs = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        var cells = new List<(Control Cell, bool Full)>();
        foreach (var input in InteractiveCalculator.GetInputs(calculator.Template))
        {
            var cell = new StackPanel { Spacing = 6 };
            cell.Children.Add(Text(_labels[input.Id], "interactive-label"));
            var editor = Numeric(input.Id, _state.Values.GetValueOrDefault(input.Id) ?? "", input.Integer);
            editor.Name = "Input_" + input.Id;
            editor.Classes.Add("interactive-input");
            AdornInput(editor, input, calculator.Currency);
            AutomationProperties.SetName(editor, _labels[input.Id]);
            cell.Children.Add(editor);
            cells.Add((cell, false));
        }

        _calculationHost = new StackPanel { Spacing = 16 };
        var panel = new Border { Classes = { "interactive-panel" }, Child = _calculationHost };
        var layout = new Grid { ColumnSpacing = 20, RowSpacing = 16 };
        layout.Children.Add(inputs);
        layout.Children.Add(panel);
        int? mode = null;
        _layoutForWidth = width =>
        {
            var next = width >= 620 ? 2 : width >= 380 ? 1 : 0;
            if (mode == next) return;
            mode = next;
            layout.ColumnDefinitions = next == 2 ? new ColumnDefinitions("5*,6*") : new ColumnDefinitions("*");
            layout.RowDefinitions = next == 2 ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
            Grid.SetColumn(panel, next == 2 ? 1 : 0);
            Grid.SetRow(panel, next == 2 ? 0 : 1);
            ArrangeCells(inputs, cells, next == 1 ? 2 : 1);
        };
        _layoutForWidth(0);
        _body!.Children.Add(layout);
        UpdateCalculation(calculator);
        AddSubmitAction(calculator.SubmitLabel ?? _labels["assumptions"], "assumptions", "default");
    }

    private void AdornInput(NumericUpDown editor, InteractiveCalculatorInput input, string? currency)
    {
        switch (input.Kind)
        {
            case InteractiveValueKind.Money when InteractiveFormat.Symbol(currency) is { } symbol:
                editor.InnerLeftContent = Adornment(symbol, leading: true);
                break;
            case InteractiveValueKind.Money when !string.IsNullOrWhiteSpace(currency):
                editor.InnerRightContent = Adornment(currency.Trim().ToUpperInvariant(), leading: false);
                break;
            case InteractiveValueKind.Percent:
                editor.InnerRightContent = Adornment("%", leading: false);
                break;
            case InteractiveValueKind.Years:
                editor.InnerRightContent = Adornment(_labels["yearsUnit"], leading: false);
                break;
        }
    }

    private void UpdateCalculation(InteractiveCalculatorDefinition calculator)
    {
        if (_calculationHost is null) return;
        InteractiveCalculation calculation;
        try
        {
            calculation = InteractiveCalculator.Calculate(calculator, _state);
        }
        catch (FormatException)
        {
            _calculationValid = false;
            SetError(CalculatorError(calculator));
            UpdateActions();
            return;
        }

        _calculationValid = true;
        SetError(null);
        UpdateActions();
        _calculationHost.Children.Clear();
        string Format(double value, InteractiveValueKind kind) => InteractiveFormat.Value(value, kind, calculator.Currency, _labels);
        string Label(string key) => _labels[InteractiveReceiptFormatter.MetricLabelKey(key)];

        if (calculation.Ingredients.Count > 0)
        {
            var scale = calculation.Metrics.Single(metric => metric.Key == "multiplier");
            _calculationHost.Children.Add(Ingredients(calculation.Ingredients, calculation.Metrics[0].Value,
                Format(scale.Value, scale.Kind)));
            return;
        }

        var headline = calculation.Metrics[0];
        var hero = new StackPanel { Spacing = 2 };
        var overBudget = calculator.Template == "budget" && headline.Value < 0;
        hero.Children.Add(Text(overBudget ? _labels["overBudgetLabel"] : Label(headline.Key), "interactive-label"));
        var heroValue = Text(Format(overBudget ? -headline.Value : headline.Value, headline.Kind), "interactive-hero");
        heroValue.Name = "CalculatorHeadline";
        heroValue.Classes.Set("negative", headline.Value < 0);
        hero.Children.Add(heroValue);
        _calculationHost.Children.Add(hero);
        if (calculation.Metrics.Count > 1)
            _calculationHost.Children.Add(Stats(calculation.Metrics.Skip(1).Select(metric => (Label(metric.Key), Format(metric.Value, metric.Kind)))));

        switch (calculator.Template)
        {
            case "savings":
                _calculationHost.Children.Add(new StrataChart
                {
                    ChartType = StrataChartType.Line,
                    Labels = calculation.ChartLabels,
                    Series =
                    [
                        new StrataChartSeries { Name = _labels["balance"], Values = calculation.ChartValues },
                        new StrataChartSeries { Name = _labels["contributions"], Values = calculation.Contributions }
                    ],
                    ShowLegend = true,
                    ChartHeight = 150,
                });
                break;
            case "budget":
            {
                var income = double.Parse(_state.Values["income"], CultureInfo.InvariantCulture);
                var parts = new List<(string, double, string)>
                {
                    (_labels["essential"], calculation.ChartValues[0], "chart1"),
                    (_labels["flexible"], calculation.ChartValues[1], "chart4"),
                    (_labels["savings"], calculation.ChartValues[2], "chart6")
                };
                if (headline.Value > 0)
                    parts.Add((_labels["remainingMetric"], headline.Value, "chart-rest"));
                _calculationHost.Children.Add(Allocation(parts, Math.Max(income, calculation.ChartValues.Sum()), calculator.Currency));
                break;
            }
            case "loan":
                _calculationHost.Children.Add(Allocation(
                    [(_labels["principal"], calculation.ChartValues[0], "chart1"), (_labels["interest"], calculation.ChartValues[1], "chart4")],
                    calculation.ChartValues.Sum(), calculator.Currency));
                break;
        }

        var note = calculator.Template switch { "savings" => "savingsNote", "loan" => "loanNote", _ => null };
        if (note is not null)
            _calculationHost.Children.Add(Text(_labels[note], "interactive-hint"));
    }

    /// <summary>Names the input to fix, in the user's language.</summary>
    private string CalculatorError(InteractiveCalculatorDefinition calculator)
    {
        if (InteractiveCalculator.FindInvalidInput(calculator, _state) is not { } invalid)
            return _labels["range"];
        var input = invalid.Input;
        var problem = invalid.Problem == "number" ? _labels["number"]
            : _labels.Format(input.Integer ? "wholeBetween" : "between",
                input.Minimum.ToString("#,0.##", CultureInfo.CurrentCulture), input.Maximum.ToString("#,0.##", CultureInfo.CurrentCulture));
        return $"{_labels[input.Id]}: {problem}";
    }

    private static Grid Stats(IEnumerable<(string Label, string Value)> stats)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        foreach (var (label, value) in stats)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(Text(label, "interactive-label"));
            cell.Children.Add(Text(value, "interactive-stat"));
            Grid.SetColumn(cell, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(cell);
        }
        return grid;
    }

    /// <summary>A proportional bar with a legend: how a total divides into its parts.</summary>
    private static Control Allocation(IReadOnlyList<(string Label, double Value, string Swatch)> parts, double total, string? currency)
    {
        var panel = new StackPanel { Spacing = 10 };
        var bar = new Grid { ColumnSpacing = 2 };
        foreach (var part in parts.Where(part => part.Value > 0))
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(part.Value, GridUnitType.Star));
            var segment = new Border { Classes = { part.Swatch } };
            Grid.SetColumn(segment, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(segment);
        }
        if (total > parts.Sum(part => Math.Max(0, part.Value)))
            bar.ColumnDefinitions.Add(new ColumnDefinition(total - parts.Sum(part => Math.Max(0, part.Value)), GridUnitType.Star));
        panel.Children.Add(new Border { Classes = { "interactive-track" }, Child = bar });

        var legend = new StackPanel { Spacing = 6 };
        foreach (var part in parts)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            var dot = new Border { Classes = { "interactive-dot", part.Swatch } };
            var label = Text(part.Label, "interactive-detail");
            var value = Text(InteractiveFormat.Money(part.Value, currency), "interactive-body");
            value.FontWeight = FontWeight.Medium;
            var share = Text(total > 0 ? (part.Value / total).ToString("P0", CultureInfo.CurrentCulture) : "", "interactive-hint");
            share.MinWidth = 36;
            share.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(label, 1);
            Grid.SetColumn(value, 2);
            Grid.SetColumn(share, 3);
            row.Children.Add(dot);
            row.Children.Add(label);
            row.Children.Add(value);
            row.Children.Add(share);
            legend.Children.Add(row);
        }
        panel.Children.Add(legend);
        return panel;
    }

    private Control Ingredients(IReadOnlyList<InteractiveIngredient> ingredients, double servings, string scale)
    {
        var panel = new StackPanel();
        Grid.SetIsSharedSizeScope(panel, true);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(Text(_labels.Format("ingredientsFor", servings.ToString("0", CultureInfo.CurrentCulture)), "interactive-strong"));
        var factor = new Border { Classes = { "interactive-pill" }, Child = new TextBlock { Text = scale } };
        AutomationProperties.SetName(factor, _labels["multiplier"] + " " + scale);
        Grid.SetColumn(factor, 1);
        header.Children.Add(factor);
        panel.Children.Add(header);
        for (var index = 0; index < ingredients.Count; index++)
        {
            if (index > 0)
                panel.Children.Add(new Border { Classes = { "interactive-divider" } });
            var ingredient = ingredients[index];
            var row = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "Quantity" });
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var quantity = Text(Quantity(ingredient.Quantity, ingredient.Unit), "interactive-strong");
            quantity.TextWrapping = TextWrapping.NoWrap;
            var label = Text(ingredient.Label);
            Grid.SetColumn(label, 1);
            row.Children.Add(quantity);
            row.Children.Add(label);
            panel.Children.Add(row);
        }
        return panel;
    }

    /// <summary>Kitchen-friendly quantities: exact common fractions for cups and spoons, decimals otherwise.</summary>
    internal static string Quantity(double quantity, string? unit)
    {
        var metric = unit?.Trim().ToLowerInvariant() is "g" or "kg" or "mg" or "ml" or "l" or "cl" or "dl";
        string text;
        if (metric || quantity >= 20)
            text = quantity.ToString(quantity >= 100 ? "#,0" : quantity >= 10 ? "0.#" : "0.##", CultureInfo.CurrentCulture);
        else
        {
            var whole = Math.Floor(quantity);
            var nearest = KitchenFractions.MinBy(mark => Math.Abs(mark.Value - (quantity - whole)));
            if (Math.Abs(nearest.Value - (quantity - whole)) > 0.03)
                text = quantity.ToString("0.##", CultureInfo.CurrentCulture);
            else if (nearest.Value == 1)
                text = (whole + 1).ToString("0", CultureInfo.CurrentCulture);
            else if (whole == 0 && nearest.Glyph.Length == 0)
                text = quantity.ToString("0.##", CultureInfo.CurrentCulture);
            else
                text = (whole > 0 ? whole.ToString("0", CultureInfo.CurrentCulture) : "") + nearest.Glyph;
        }
        return string.IsNullOrWhiteSpace(unit) ? text : text + " " + unit.Trim();
    }

    private static readonly (double Value, string Glyph)[] KitchenFractions =
    [
        (0, ""), (0.125, "\u215b"), (0.25, "\u00bc"), (1 / 3.0, "\u2153"), (0.375, "\u215c"), (0.5, "\u00bd"),
        (0.625, "\u215d"), (2 / 3.0, "\u2154"), (0.75, "\u00be"), (0.875, "\u215e"), (1, "")
    ];

    private void QueueCalculation(InteractiveCalculatorDefinition calculator)
    {
        _pendingCalculation = calculator;
        if (_calculationQueued) return;
        _calculationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _calculationQueued = false;
            if (Definition == _pendingCalculation && _pendingCalculation is { } pending)
                UpdateCalculation(pending);
            _pendingCalculation = null;
        }, DispatcherPriority.Background);
    }
}
