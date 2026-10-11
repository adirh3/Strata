using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using StrataTheme.Controls;

namespace StrataDemo;

public partial class MainWindow
{
    private void InitializeInteractiveResponses()
    {
        if (DataContext is MainViewModel model)
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Strings))
                    PopulateInteractiveResponses();
            };
        PopulateInteractiveResponses();
    }

    private void PopulateInteractiveResponses()
    {
        if (this.FindControl<StackPanel>("InteractiveResponseSamples") is not { } host)
            return;
        var strings = Localization.Strings.Instance;
        var language = strings.Culture.Name;
        var he = strings.IsRtl;
        var form = new InteractiveForm
        {
            Id = "demo_form", Title = strings["Interactive.Form"],
            Description = he ? "כמה פרטים, ואמשיך באותה משימה." : "A few details, then I'll continue the same task.",
            SubmitLabel = he ? "מצא אפשרויות" : "Find options",
            Fields =
            [
                new() { Id = "destination", Label = he ? "יעד" : "Destination", Required = true, Placeholder = he ? "לאן?" : "Where to?" },
                new() { Id = "date", Label = he ? "יציאה" : "Departure", Type = "date", Value = "2026-10-17", Required = true },
                new() { Id = "budget", Label = he ? "תקציב" : "Budget", Type = "number", Value = "1200", Minimum = 0, Unit = "USD" },
                new() { Id = "guests", Label = he ? "אורחים" : "Guests", Type = "number", Value = "2", Minimum = 1, Maximum = 12 },
                new() { Id = "pace", Label = he ? "קצב" : "Pace", Type = "select", Value = he ? "מאוזן" : "Balanced", Options = he ? ["רגוע", "מאוזן", "עמוס"] : ["Relaxed", "Balanced", "Packed"] },
                new() { Id = "interests", Label = he ? "מה הכי חשוב?" : "What matters most?", Type = "multiselect", Options = he ? ["אוכל", "טבע", "תרבות", "קניות"] : ["Food", "Nature", "Culture", "Shopping"] }
            ]
        };
        var results = new InteractiveResults
        {
            Id = "demo_results", Title = strings["Interactive.Results"],
            Description = he ? "דוגמאות להדגמה, לא מחירים אמיתיים." : "Sample options for the demo, not live prices.",
            Items =
            [
                new() { Id = "one", Label = he ? "מלון גן שקט" : "A quiet garden stay", Meta = he ? "דוגמה: $180 ללילה · רגוע" : "Sample: $180 / night · Relaxed", Detail = he ? "בסיס שליו עם הרבה ירוק." : "A peaceful base with green space and room to unwind.", Url = "https://example.com/garden" },
                new() { Id = "two", Label = he ? "דירה במרכז העיר" : "A walkable city hideaway", Meta = he ? "דוגמה: $220 ללילה · מרכזי" : "Sample: $220 / night · Central", Detail = he ? "קרוב לאוכל, לתרבות ולכל מה שרוצים לראות." : "Close to food, culture, and the places you want to explore." },
                new() { Id = "three", Label = he ? "ריטריט מעוצב" : "A design-led retreat", Meta = he ? "דוגמה: $260 ללילה · מרווח" : "Sample: $260 / night · Spacious", Detail = he ? "יותר מקום וקצב איטי לסוף שבוע מיוחד." : "More room and a slower pace for a special weekend." }
            ]
        };
        var definitions = new (string Name, InteractiveDefinition Definition, string? State)[]
        {
            ("DemoForm", form with { Id = "demo_form_answer", Title = he ? "רוצה לדייק את ההמלצות?" : "Want sharper recommendations?", Description = null }, null),
            ("DemoChecklist", new InteractiveChecklist
            {
                Id = "demo_list", Title = strings["Interactive.Checklist"],
                Items =
                [
                    new() { Id = "tomatoes", Label = he ? "עגבניות שרי" : "Cherry tomatoes", Meta = "500 g" },
                    new() { Id = "basil", Label = he ? "בזיליקום טרי" : "Fresh basil", Meta = he ? "צרור" : "1 bunch" },
                    new() { Id = "pasta", Label = he ? "פסטה ומוצרלה" : "Pasta and mozzarella", Detail = he ? "מספיק לארבעה." : "Enough for four." },
                    new() { Id = "table", Label = he ? "עריכת השולחן" : "Set the table" }
                ]
            }, new InteractiveState { CheckedIds = ["tomatoes", "basil"] }.ToJson()),
            ("DemoSteps", new InteractiveChecklist
            {
                Id = "demo_steps", Title = strings["Interactive.Steps"], Numbered = true,
                Items =
                [
                    new() { Id = "plug", Label = he ? "חבר את הנתב למודם" : "Connect the router to your modem", Detail = he ? "השתמש בכבל שבקופסה, ביציאת WAN." : "Use the cable in the box and the WAN port." },
                    new() { Id = "power", Label = he ? "הפעל והמתן לנורה ירוקה" : "Power on and wait for a solid light", Detail = he ? "לרוב פחות משתי דקות." : "Usually under two minutes." },
                    new() { Id = "app", Label = he ? "פתח את אפליקציית ההגדרה" : "Open the setup app", Detail = he ? "התחבר לרשת שעל המדבקה." : "Join the network printed on the label." },
                    new() { Id = "secure", Label = he ? "הגדר סיסמה חזקה" : "Set a strong Wi-Fi password" }
                ]
            }, new InteractiveState { CheckedIds = ["plug"] }.ToJson()),
            ("DemoResults", results, new InteractiveState { SelectedIds = ["one", "two"] }.ToJson()),
            ("DemoBill", new InteractiveCalculatorDefinition
            {
                Id = "demo_bill", Title = strings["Interactive.Calculator"], Template = "bill-split", Currency = "USD",
                Inputs = new() { ["bill"] = 240, ["tipPercent"] = 15, ["people"] = 4 }
            }, null),
            ("DemoBudget", new InteractiveCalculatorDefinition
            {
                Id = "demo_budget", Title = strings["Interactive.Budget"], Template = "budget", Currency = "USD",
                Inputs = new() { ["income"] = 5200, ["essential"] = 2600, ["flexible"] = 1100, ["savings"] = 800 }
            }, null),
            ("DemoLoan", new InteractiveCalculatorDefinition
            {
                Id = "demo_loan", Title = strings["Interactive.Loan"], Template = "loan", Currency = "USD",
                Inputs = new() { ["loanAmount"] = 350000, ["interestRate"] = 6.2, ["years"] = 30 }
            }, null),
            ("DemoRecipe", new InteractiveCalculatorDefinition
            {
                Id = "demo_recipe", Title = strings["Interactive.Recipe"], Template = "servings",
                Inputs = new() { ["baseServings"] = 4, ["servings"] = 6 },
                Ingredients =
                [
                    new() { Label = he ? "ספגטי" : "Spaghetti", Quantity = 400, Unit = "g" },
                    new() { Label = he ? "שמן זית" : "Olive oil", Quantity = 0.25, Unit = he ? "כוס" : "cup" },
                    new() { Label = he ? "שיני שום" : "Garlic cloves", Quantity = 4 },
                    new() { Label = he ? "פתיתי צ'ילי" : "Chili flakes", Quantity = 0.5, Unit = he ? "כפית" : "tsp" }
                ]
            }, null),
            ("DemoQuiz", new InteractiveQuiz
            {
                Id = "demo_quiz", Title = strings["Interactive.Quiz"],
                Questions =
                [
                    new()
                    {
                        Id = "q1", Prompt = he ? "איזו פעולה שולחת את הבחירות שלך?" : "Which action sends your choices?",
                        Options = he ? ["שינוי ערך", "לחיצה על המשך", "סימון פריט"] : ["Editing a value", "Clicking Continue", "Checking an item"],
                        CorrectIndex = 1, Explanation = he ? "העריכה מקומית; המשך שולח את הבחירה." : "Edits stay local; Continue explicitly sends them.",
                        Hint = he ? "חפש את כפתור הפעולה." : "Look for the action button."
                    },
                    new()
                    {
                        Id = "q2", Prompt = he ? "מה מציין סימון ברשימה?" : "What does a checklist checkmark mean?",
                        Options = he ? ["ש־Lumi ביצעה את המשימה", "שאתה סימנת את הפריט"] : ["Lumi did that task", "You marked it as done"],
                        CorrectIndex = 1, Explanation = he ? "הסימונים שייכים להתקדמות שלך." : "Checkmarks track your own progress."
                    }
                ]
            }, new InteractiveState { Answers = new() { ["q1"] = 1 }, CheckedAnswers = ["q1"] }.ToJson())
        };

        host.Children.Clear();
        var waitingForm = new StrataInteractiveBlock
        {
            Name = "DemoWaitingInput", Definition = form, Language = language,
            InputStatus = "Waiting", CanSubmit = true, IsHosted = true
        };
        waitingForm.Requested += (_, args) =>
        {
            waitingForm.StateJson = args.StateJson;
            if (args.Action is not null)
                waitingForm.InputStatus = args.Action == "cancel" ? "Canceled" : "Submitted";
            args.Completion = Task.FromResult(new InteractiveActionResult(true));
            args.Handled = true;
        };
        host.Children.Add(waitingForm);

        var exampleState = new InteractiveState { SelectedIds = ["one", "two"] };
        var description = InteractiveReceiptFormatter.Describe(results, exampleState, "compare", language);
        host.Children.Add(new StrataActionReceipt
        {
            Name = "DemoActionReceipt", Title = description.Title, Summary = description.Summary, Action = "compare",
            Detail = description.Detail, Language = language, Status = "Working", CanOpenSource = true
        });

        foreach (var (name, definition, state) in definitions)
        {
            var control = new StrataInteractiveBlock
            {
                Name = name, Definition = definition, StateJson = state, Language = language,
                CanSubmit = true, IsHosted = true
            };
            control.Requested += (_, args) =>
            {
                if (args.Action is not null && this.FindControl<TextBox>("InteractiveResponseReceipt") is { } receipt)
                    receipt.Text = InteractiveSubmission.Build(args.Definition, InteractiveState.Parse(args.StateJson), args.Action);
                args.Completion = Task.FromResult(new InteractiveActionResult(true));
                args.Handled = true;
            };
            host.Children.Add(control);
        }
    }
}
