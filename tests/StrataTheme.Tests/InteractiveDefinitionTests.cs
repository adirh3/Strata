using System.Globalization;
using System.Text.Json;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class InteractiveDefinitionTests
{
    public static IEnumerable<object[]> Definitions()
    {
        yield return ["form", """{"id":"trip","title":"Plan a trip","fields":[{"id":"budget","label":"Budget","type":"number","required":true,"minimum":0}]}"""];
        yield return ["checklist", """{"id":"shopping","title":"Shopping","items":[{"id":"milk","label":"Milk"},{"id":"bread","label":"Bread"}]}"""];
        yield return ["results", """{"id":"hotels","title":"Hotels","items":[{"id":"a","label":"Hotel A","url":"https://example.com/a"},{"id":"b","label":"Hotel B"}]}"""];
        yield return ["calculator", """{"id":"bill","title":"Split the bill","template":"bill-split","inputs":{"bill":240,"tipPercent":10,"people":4}}"""];
        yield return ["quiz", """{"id":"practice","title":"Practice","questions":[{"id":"q1","prompt":"2 + 2?","options":["3","4"],"correctIndex":1,"explanation":"Two pairs make four.","hint":"Count two pairs."}]}"""];
        yield return ["checklist", """{"id":"setup","title":"Set up","numbered":true,"items":[{"id":"one","label":"Plug in"},{"id":"two","label":"Power on","detail":"Wait for the light."},{"id":"three","label":"Pair","meta":"2 min"}]}"""];
        yield return ["calculator", """{"id":"mortgage","title":"Mortgage","template":"loan","currency":"USD","inputs":{"loanAmount":100000,"interestRate":6,"years":30}}"""];
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void DefinitionAndStateRoundTrip(string language, string json)
    {
        var definition = InteractiveDefinition.Parse(language, json);
        Assert.Equal(language, definition.Kind);
        var state = InteractiveState.Create(definition);
        var restored = InteractiveState.Parse(state.ToJson());
        restored.Validate(definition);
        Assert.Equal(state.Fingerprint(), restored.Fingerprint());
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void ParserPreservesLanguageAndClosedBoundary(string language, string json)
    {
        var closed = MarkdownParser.Parse($"```{language}\n{json}\n```");
        Assert.Single(closed);
        Assert.Equal(MdBlockKind.Interactive, closed[0].Kind);
        Assert.True(closed[0].IsClosed);
        var open = MarkdownParser.Parse($"```{language}\n{json}");
        Assert.Single(open);
        Assert.Equal(MdBlockKind.Interactive, open[0].Kind);
        Assert.False(open[0].IsClosed);
    }

    [Theory]
    [InlineData("form", """{"id":"x","title":"Form","fields":null}""")]
    [InlineData("form", """{"id":"x","title":"Form","fields":[{"id":"a","label":"A","type":"password"}]}""")]
    [InlineData("form", """{"id":"x","title":"Form","fields":[{"id":"a","label":"A"},{"id":"a","label":"B"}]}""")]
    [InlineData("results", """{"id":"x","title":"Results","items":[{"id":"a","label":"A","url":"file:///secret.txt"}]}""")]
    [InlineData("quiz", """{"id":"x","title":"Quiz","questions":[{"id":"q","prompt":"Question","options":["A","B"],"correctIndex":3,"explanation":"Explanation"}]}""")]
    [InlineData("diagram", """{"id":"x","title":"Diagram","source":"flowchart LR\nA --> B","topics":[{"id":"A","label":"A","detail":"A"}]}""")]
    [InlineData("calculator", """{"id":"x","title":"Calculator","template":"javascript","inputs":{}}""")]
    [InlineData("calculator", """{"id":"x","title":"Loan","template":"loan","inputs":{"loanAmount":1000,"interestRate":5}}""")]
    public void InvalidSupportedDefinitionsFailExplicitly(string language, string json) =>
        Assert.Throws<FormatException>(() => InteractiveDefinition.Parse(language, json));

    [Fact]
    public void RemovedDiagramExplorerFencesRenderAsOrdinaryCode()
    {
        Assert.False(InteractiveDefinition.IsSupported("diagram"));
        Assert.Equal(MdBlockKind.CodeBlock, Assert.Single(MarkdownParser.Parse("```diagram\n{}\n```")).Kind);
    }

    [Fact]
    public void MalformedJsonDoesNotBecomeASuccessfulControlDefinition() =>
        Assert.Throws<JsonException>(() => InteractiveDefinition.Parse("form", "{"));

    [Fact]
    public void OptionalDefaultsSurviveSourceGeneratedDeserializationOnBothFrameworks()
    {
        var form = (InteractiveForm)InteractiveDefinition.Parse("form",
            """{"id":"text","title":"Text","fields":[{"id":"name","label":"Name"}]}""");
        Assert.Equal("text", form.Fields[0].Type);
        Assert.Empty(form.Fields[0].Options);
        Assert.Empty(InteractiveState.Parse("{}").Values);
    }

    [Fact]
    public void FormsValidateEveryTypedFieldBeforeSubmission()
    {
        var form = new InteractiveForm
        {
            Id = "details", Title = "Details",
            Fields =
            [
                new() { Id = "name", Label = "Name", Required = true },
                new() { Id = "budget", Label = "Budget", Type = "number", Minimum = 0, Maximum = 100 },
                new() { Id = "date", Label = "Date", Type = "date" },
                new() { Id = "choice", Label = "Choice", Type = "select", Options = ["A", "B"] },
                new() { Id = "tags", Label = "Tags", Type = "multiselect", Required = true, Options = ["One", "Two"] }
            ]
        };
        var state = InteractiveState.Create(form);
        state.Values["budget"] = "101";
        state.Values["date"] = "2026-02-30";
        state.Values["choice"] = "C";
        Assert.Equal(5, InteractiveSubmission.ValidateForm(form, state).Count);
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(form, state, "submit"));
        state.Values["name"] = "Adir";
        state.Values["budget"] = "75.5";
        state.Values["date"] = "2026-10-11";
        state.Values["choice"] = "A";
        state.Choices["tags"] = ["One"];
        Assert.Empty(InteractiveSubmission.ValidateForm(form, state));
        var request = InteractiveSubmission.Build(form, state, "submit");
        Assert.Contains("Budget: 75.5", request);
        Assert.Contains("Tags: One", request);
        Assert.Contains("not approval", request);
    }

    [Fact]
    public void ChecklistSubmissionIncludesOnlyRemainingItems()
    {
        var definition = (InteractiveChecklist)InteractiveDefinition.Parse(
            "checklist", (string)Definitions().First(item => (string)item[0] == "checklist")[1]);
        var state = InteractiveState.Create(definition);
        state.CheckedIds.Add("milk");
        var request = InteractiveSubmission.Build(definition, state, "remaining");
        Assert.Contains("Bread", request);
        Assert.DoesNotContain("- Milk", request);
        Assert.Contains("not actions performed", request);
    }

    [Fact]
    public void ComparisonsRequireTwoDistinctKnownResults()
    {
        var definition = (InteractiveResults)InteractiveDefinition.Parse(
            "results", (string)Definitions().First(item => (string)item[0] == "results")[1]);
        var state = new InteractiveState { SelectedIds = ["a"] };
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(definition, state, "compare"));
        state.SelectedIds.Add("b");
        var request = InteractiveSubmission.Build(definition, state, "compare");
        Assert.Contains("Hotel A", request);
        Assert.Contains("Hotel B", request);
        state.SelectedIds.Add("b");
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(definition, state, "compare"));
    }

    [Fact]
    public void StateCannotSmuggleUnknownOptionsOrActions()
    {
        var definition = InteractiveDefinition.Parse(
            "results", (string)Definitions().First(item => (string)item[0] == "results")[1]);
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(
            definition, new InteractiveState { SelectedIds = ["unknown"] }, "use"));
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(
            definition, new InteractiveState { SelectedIds = ["a"] }, "execute"));
        Assert.Throws<FormatException>(() => InteractiveState.Parse("""{"values":null}"""));
    }

    [Fact]
    public void SubmissionFingerprintDoesNotIncludeItsOwnReceipt()
    {
        var state = new InteractiveState { Values = new() { ["budget"] = "1500" } };
        var original = state.Fingerprint();
        state.SubmittedFingerprint = "submit:" + original;
        Assert.Equal(original, state.Fingerprint());
        state.Values["budget"] = "2000";
        Assert.NotEqual(original, state.Fingerprint());
    }

    [Fact]
    public void BillSplitUsesDecimalArithmetic()
    {
        var definition = new InteractiveCalculatorDefinition
        {
            Id = "bill", Title = "Bill", Template = "bill-split", Currency = "USD",
            Inputs = new() { ["bill"] = 240, ["tipPercent"] = 10, ["people"] = 4 }
        };
        var result = InteractiveCalculator.Calculate(definition, InteractiveState.Create(definition));
        Assert.Equal(264, result.Metrics.Single(item => item.Key == "total").Value);
        Assert.Equal(66, result.Metrics.Single(item => item.Key == "perPerson").Value);
    }

    [Theory]
    [InlineData(0, 1300)]
    [InlineData(12, 1475.6467075128)]
    public void SavingsUsesMonthlyCompoundingAndMonthEndContributions(double annualRate, double expected)
    {
        var definition = new InteractiveCalculatorDefinition
        {
            Id = "savings", Title = "Savings", Template = "savings",
            Inputs = new() { ["initial"] = 100, ["monthly"] = 50, ["annualRate"] = annualRate, ["years"] = 2 }
        };
        var result = InteractiveCalculator.Calculate(definition, InteractiveState.Create(definition));
        Assert.Equal(3, result.ChartValues.Count);
        Assert.Equal(1300, result.Contributions[^1]);
        Assert.InRange(Math.Abs(result.ChartValues[^1] - expected), 0, 0.00001);
    }

    [Fact]
    public void ServingsAndOverspentBudgetsPreserveActualValues()
    {
        var recipe = new InteractiveCalculatorDefinition
        {
            Id = "recipe", Title = "Recipe", Template = "servings",
            Inputs = new() { ["baseServings"] = 4, ["servings"] = 6 },
            Ingredients = [new() { Label = "Rice", Quantity = 200, Unit = "g" }]
        };
        Assert.Equal(300, InteractiveCalculator.Calculate(recipe, InteractiveState.Create(recipe)).Ingredients[0].Quantity);
        var budget = new InteractiveCalculatorDefinition
        {
            Id = "budget", Title = "Budget", Template = "budget",
            Inputs = new() { ["income"] = 1000, ["essential"] = 900, ["flexible"] = 200, ["savings"] = 100 }
        };
        Assert.Equal(-200, InteractiveCalculator.Calculate(budget, InteractiveState.Create(budget)).Metrics[0].Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("NaN")]
    public void InvalidCalculatorInputsNeverSilentlyDefault(string people)
    {
        var definition = (InteractiveCalculatorDefinition)InteractiveDefinition.Parse(
            "calculator", (string)Definitions().First(item => (string)item[0] == "calculator")[1]);
        var state = InteractiveState.Create(definition);
        state.Values["people"] = people;
        Assert.Throws<FormatException>(() => InteractiveCalculator.Calculate(definition, state));
    }

    [Fact]
    public void QuizResultsRequireCheckedAnswersAndPreserveHints()
    {
        var quiz = (InteractiveQuiz)InteractiveDefinition.Parse(
            "quiz", (string)Definitions().First(item => (string)item[0] == "quiz")[1]);
        var state = InteractiveState.Create(quiz);
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(quiz, state, "discuss"));
        state.Answers["q1"] = 0;
        state.CheckedAnswers.Add("q1");
        state.RevealedHints.Add("q1");
        var restored = InteractiveState.Parse(state.ToJson());
        Assert.Contains("incorrect", InteractiveSubmission.Build(quiz, restored, "discuss"));
        Assert.Contains("q1", restored.RevealedHints);
    }

    [Fact]
    public void LabelsHaveEnglishAndHebrewCoverage()
    {
        Assert.Equal("Continue", InteractiveLabels.ForLanguage("en-US")["continue"]);
        Assert.Equal("המשך", InteractiveLabels.ForLanguage("he-IL")["continue"]);
        Assert.Empty(InteractiveLabels.English.Keys.Except(InteractiveLabels.Hebrew.Keys));
        Assert.Empty(InteractiveLabels.Hebrew.Keys.Except(InteractiveLabels.English.Keys));
    }

    [Fact]
    public void LoanUsesStandardMonthlyAmortization()
    {
        var loan = (InteractiveCalculatorDefinition)InteractiveDefinition.Parse(
            "calculator", (string)Definitions().Last(item => (string)item[0] == "calculator")[1]);
        var result = InteractiveCalculator.Calculate(loan, InteractiveState.Create(loan));
        Assert.Equal("payment", result.Metrics[0].Key);
        Assert.InRange(result.Metrics[0].Value, 599.55, 599.56);
        Assert.InRange(result.Metrics.Single(metric => metric.Key == "totalPaid").Value, 215_838, 215_839);
        Assert.Equal(["principal", "interest"], result.ChartLabels);

        var interestFree = loan with { Inputs = new() { ["loanAmount"] = 12_000, ["interestRate"] = 0, ["years"] = 1 } };
        var flat = InteractiveCalculator.Calculate(interestFree, InteractiveState.Create(interestFree));
        Assert.Equal(1000, flat.Metrics[0].Value);
        Assert.Equal(0, flat.Metrics.Single(metric => metric.Key == "totalInterest").Value);
        Assert.Contains("fixed rate", InteractiveSubmission.Build(loan, InteractiveState.Create(loan), "assumptions"));
    }

    [Fact]
    public void NumberedStepsAskForHelpWithTheNextStep()
    {
        var steps = (InteractiveChecklist)InteractiveDefinition.Parse(
            "checklist", (string)Definitions().Last(item => (string)item[0] == "checklist")[1]);
        Assert.True(steps.Numbered);
        var state = new InteractiveState { CheckedIds = ["one"] };
        var request = InteractiveSubmission.Build(steps, state, "remaining");
        Assert.Contains("Help me with step 2", request);
        Assert.Contains("Step 3: Pair", request);
        Assert.DoesNotContain("Plug in", request);
        Assert.Equal("Help with step 2", InteractiveReceiptFormatter.Describe(steps, state, "remaining").Title);
        Assert.StartsWith("Step 2: Power on \u00b7 Step 3: Pair", InteractiveReceiptFormatter.Describe(steps, state, "remaining").Summary);
        Assert.StartsWith("2. Power on\nWait for the light.\n\n3. Pair", InteractiveReceiptFormatter.Describe(steps, state, "remaining").Detail);
    }

    [Fact]
    public void ValuesAreFormattedForPeopleWithoutConvertingCurrencies()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("$65.83", InteractiveFormat.Money(65.83, "USD"));
            Assert.Equal("$66", InteractiveFormat.Money(66, "usd"));
            Assert.Equal("$1,234", InteractiveFormat.Money(1234.4, "USD"));
            Assert.Equal("-\u20aa200", InteractiveFormat.Money(-200, "ILS"));
            Assert.Equal("1,200 CAD", InteractiveFormat.Money(1200, "CAD"));
            Assert.Equal("$1,200", InteractiveFormat.Number("1200", "USD"));
            Assert.Equal("2.5 kg", InteractiveFormat.Number("2.5", "kg"));
            Assert.Equal("\u215c cup", StrataInteractiveBlock.Quantity(0.375, "cup"));
            Assert.Equal("\u2153 cup", StrataInteractiveBlock.Quantity(1 / 3.0, "cup"));
            Assert.Equal("1\u00bd tsp", StrataInteractiveBlock.Quantity(1.5, "tsp"));
            Assert.Equal("2 tbsp", StrataInteractiveBlock.Quantity(1.99, "tbsp"));
            Assert.Equal("0.2 cup", StrataInteractiveBlock.Quantity(0.2, "cup"));
            Assert.Equal("600 g", StrataInteractiveBlock.Quantity(600, "g"));
            Assert.Equal("0.15 l", StrataInteractiveBlock.Quantity(0.15, "l"));
            Assert.Equal("12.5 g", StrataInteractiveBlock.Quantity(12.5, "g"));
            Assert.Equal("6", StrataInteractiveBlock.Quantity(6, null));
            Assert.Equal("0.03 tsp", StrataInteractiveBlock.Quantity(0.03, "tsp"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void CalculatorReceiptsUseReadableValues()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var bill = (InteractiveCalculatorDefinition)InteractiveDefinition.Parse("calculator",
                """{"id":"bill","title":"Bill","template":"bill-split","currency":"USD","inputs":{"bill":240,"tipPercent":10,"people":4}}""");
            var receipt = InteractiveReceiptFormatter.Describe(bill, InteractiveState.Create(bill), "assumptions");
            Assert.Contains("Tip: 10%", receipt.Detail);
            Assert.Contains("Bill: $240", receipt.Detail);
            Assert.Contains("Each person pays: $66", receipt.Detail);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
