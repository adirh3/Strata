using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class InteractiveExplorerTests(AvaloniaFixture fixture)
{
    internal const string Json = """
        {"id":"tvs","title":"65-inch TVs","description":"Sample prices.","attributes":[
          {"id":"price","label":"Price","type":"money","unit":"USD","better":"lower"},
          {"id":"rating","label":"Rating","type":"rating"},
          {"id":"panel","label":"Panel","type":"text"},
          {"id":"hz","label":"Refresh rate","type":"number","unit":"Hz","better":"higher"},
          {"id":"dv","label":"Dolby Vision","type":"boolean"}],
         "items":[
          {"id":"lg","label":"LG C4","subtitle":"65\" OLED","badge":"Best overall","summary":"A great all-rounder.","pros":["Perfect blacks"],"cons":["Glossy screen"],"url":"https://example.com/lg","values":{"price":1599.99,"rating":4.7,"panel":"OLED","hz":144,"dv":true}},
          {"id":"samsung","label":"Samsung S90D","values":{"price":1399,"rating":4.6,"panel":"QD-OLED","hz":144,"dv":false,"year":2024}},
          {"id":"tcl","label":"TCL QM8","values":{"price":"999","rating":4.4,"panel":"Mini-LED","hz":120,"dv":true}},
          {"id":"sony","label":"Sony Bravia 8","values":{"price":1799,"rating":4.5,"panel":"OLED","hz":120}},
          {"id":"hisense","label":"Hisense U8","values":{"price":1099,"rating":4.3,"panel":"Mini-LED","hz":144,"dv":true}}]}
        """;

    private static InteractiveExplorer Definition() => (InteractiveExplorer)InteractiveDefinition.Parse("explorer", Json);

    private static IEnumerable<string> Ids(IEnumerable<InteractiveExplorerItem> items) => items.Select(item => item.Id);

    [Fact]
    public void AttributesAreTypedDeclaredOrInferredAndOfferFittingFilters()
    {
        var model = InteractiveExplorerModel.For(Definition());
        Assert.Equal(["price", "rating", "panel", "hz", "dv", "year"], model.Attributes.Select(attribute => attribute.Id));
        var year = model.Attribute("year")!;
        Assert.Equal(ExplorerAttributeType.Number, year.Type);
        Assert.Equal("Year", year.Label);
        Assert.Same(model.Attribute("price"), model.Headline);
        Assert.Same(model.Attribute("rating"), model.Rating);
        Assert.Equal(5, model.Rating!.Scale);
        // A number sent as text still sorts and filters as a number.
        Assert.Equal(999, model.Value(Definition().Items[2], model.Attribute("price")!).Number);

        Assert.Equal(ExplorerFacetKind.Choice, model.Facet("panel")!.Kind);
        Assert.Equal(["OLED", "QD-OLED", "Mini-LED"], model.Facet("panel")!.Options);
        Assert.Equal(ExplorerFacetKind.Flag, model.Facet("dv")!.Kind);
        Assert.True(model.Facet("max:price")!.IsUpperBound);
        Assert.False(model.Facet("min:hz")!.IsUpperBound);
        Assert.False(model.Facet("min:rating")!.IsUpperBound);
        Assert.Null(model.Facets.FirstOrDefault(facet => facet.Attribute.Id == "year"));
        Assert.Equal("cards", model.DefaultView);
    }

    [Fact]
    public void FiltersCombineChosenOptionsWithOrAndDifferentFiltersWithAnd()
    {
        var model = InteractiveExplorerModel.For(Definition());
        var state = new InteractiveState();
        state.Choices["panel"] = ["OLED", "QD-OLED"];
        Assert.Equal(["lg", "samsung", "sony"], Ids(model.Apply(state)));
        state.Values["max:price"] = "1600";
        Assert.Equal(["lg", "samsung"], Ids(model.Apply(state)));
        state.Choices["dv"] = ["true"];
        Assert.Equal(["lg"], Ids(model.Apply(state)));

        var search = new InteractiveState { Values = { ["query"] = "bravia" } };
        Assert.Equal(["sony"], Ids(model.Apply(search)));
        var floor = new InteractiveState { Values = { ["min:hz"] = "144" } };
        Assert.Equal(["lg", "samsung", "hisense"], Ids(model.Apply(floor)));
        // A bound at the end of its range filters nothing.
        var open = new InteractiveState { Values = { ["max:price"] = model.Facet("max:price")!.Maximum.ToString(CultureInfo.InvariantCulture) } };
        Assert.Empty(model.ActiveFilters(open));
    }

    [Fact]
    public void SortingKeepsOptionsWithoutAValueLastAndTiesInTheirOrder()
    {
        var model = InteractiveExplorerModel.For(Definition());
        Assert.Equal(["tcl", "hisense", "samsung", "lg", "sony"],
            Ids(model.Apply(new InteractiveState { Values = { ["sort"] = "price:asc" } })));
        Assert.Equal(["lg", "samsung", "hisense", "tcl", "sony"],
            Ids(model.Apply(new InteractiveState { Values = { ["sort"] = "hz:desc" } })));
        Assert.Equal(["lg", "tcl", "hisense", "samsung", "sony"],
            Ids(model.Apply(new InteractiveState { Values = { ["sort"] = "dv:desc" } })));
        Assert.Equal(["hisense", "lg", "samsung", "sony", "tcl"],
            Ids(model.Apply(new InteractiveState { Values = { ["sort"] = ":asc" } })));
        Assert.Contains(new ExplorerSort("price", false), model.Sorts);
        Assert.Contains(new ExplorerSort("price", true), model.Sorts);
        Assert.Contains(new ExplorerSort("rating", true), model.Sorts);
    }

    [Fact]
    public void BestValuesFollowWhichDirectionIsBetter()
    {
        var definition = Definition();
        var model = InteractiveExplorerModel.For(definition);
        Assert.Equal(["tcl"], model.Best(model.Attribute("price")!, definition.Items));
        Assert.Equal(["hisense", "lg", "samsung"], model.Best(model.Attribute("hz")!, definition.Items).Order());
        Assert.Empty(model.Best(model.Attribute("panel")!, definition.Items));
        Assert.Empty(model.Best(model.Attribute("hz")!, [definition.Items[0], definition.Items[1]]));
        Assert.Equal(1, model.Standing(model.Attribute("price")!, 999, definition.Items));
        Assert.Equal(0, model.Standing(model.Attribute("price")!, 1799, definition.Items));
    }

    [Theory]
    [InlineData("""{"values":{"view":"grid"}}""")]
    [InlineData("""{"values":{"max:panel":"3"}}""")]
    [InlineData("""{"values":{"max:price":"cheap"}}""")]
    [InlineData("""{"values":{"sort":"color:asc"}}""")]
    [InlineData("""{"choices":{"panel":["Plasma"]}}""")]
    [InlineData("""{"choices":{"dv":["false"]}}""")]
    [InlineData("""{"choices":{"price":["999"]}}""")]
    [InlineData("""{"selectedIds":["lg","samsung","tcl","sony","hisense"]}""")]
    [InlineData("""{"selectedIds":["unknown"]}""")]
    public void SavedStateCannotReferenceUnknownSettingsOrExceedTheComparison(string json)
    {
        var definition = Definition();
        Assert.Throws<FormatException>(() => InteractiveState.Parse(json).Validate(definition));
    }

    [Fact]
    public void SavedFiltersAndSelectionsRoundTrip()
    {
        var definition = Definition();
        var state = new InteractiveState { SelectedIds = ["lg", "sony"] };
        state.Values["view"] = "table";
        state.Values["sort"] = "price:desc";
        state.Values["query"] = "oled";
        state.Values["max:price"] = "1600";
        state.Choices["panel"] = ["oled"];
        state.Choices["dv"] = ["true"];
        var restored = InteractiveState.Parse(state.ToJson());
        restored.Validate(definition);
        Assert.Equal(state.Fingerprint(), restored.Fingerprint());
    }

    [Fact]
    public void SubmissionsDescribeOptionsAndFiltersForTheModel()
    {
        var definition = Definition();
        var use = InteractiveSubmission.Build(definition, new InteractiveState { SelectedIds = ["lg"] }, "use");
        Assert.Contains("Continue with my selection from \"65-inch TVs\":", use);
        Assert.Contains("- LG C4 [id: lg]", use);
        Assert.Contains("Price: 1599.99 USD; Rating: 4.7/5; Panel: OLED; Refresh rate: 144 Hz; Dolby Vision: yes", use);
        Assert.Contains("Source: https://example.com/lg", use);
        Assert.Contains("not approval for purchases", use);

        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(definition, new InteractiveState { SelectedIds = ["lg"] }, "compare"));
        var filtered = new InteractiveState { SelectedIds = ["sony", "lg"] };
        filtered.Choices["panel"] = ["OLED"];
        var compare = InteractiveSubmission.Build(definition, filtered, "compare");
        Assert.Contains("Help me decide between these options from \"65-inch TVs\":", compare);
        Assert.True(compare.IndexOf("Sony Bravia 8", StringComparison.Ordinal) < compare.IndexOf("LG C4", StringComparison.Ordinal));
        Assert.Contains("While comparing, I filtered for: Panel: OLED", compare);

        filtered.Values["max:price"] = "1600";
        filtered.Choices["dv"] = ["true"];
        var more = InteractiveSubmission.Build(definition, filtered, "more");
        Assert.Contains("Find more options for \"65-inch TVs\" that match my filters:", more);
        Assert.Contains("- Panel: OLED", more);
        Assert.Contains("- Must have: Dolby Vision", more);
        Assert.Contains("- Price: up to 1600 USD", more);
        Assert.Contains("Skip the 5 options already listed.", more);
        Assert.Throws<FormatException>(() => InteractiveSubmission.Build(definition, filtered, "execute"));
    }

    [Fact]
    public void ReceiptsAndCopiesUseReadableValues()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // Copied tables take their headings from the UI language.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var definition = Definition();
            var state = new InteractiveState { SelectedIds = ["lg", "tcl"] };
            var decide = InteractiveReceiptFormatter.Describe(definition, state, "compare");
            Assert.Equal("Help me choose from 2", decide.Title);
            Assert.Equal("LG C4 \u00b7 TCL QM8", decide.Summary);
            Assert.Contains("Price: $1,599.99", decide.Detail);
            Assert.Contains("Rating: 4.7/5", decide.Detail);
            Assert.Contains("Price: $999", decide.Detail);
            Assert.Equal("Continue with 2 selected", InteractiveReceiptFormatter.Describe(definition, state, "use").Title);

            state.Values["max:price"] = "1500";
            var more = InteractiveReceiptFormatter.Describe(definition, state, "more");
            Assert.Equal("Find more options", more.Title);
            Assert.Equal("Price up to $1,500", more.Summary);

            var markdown = MarkdownClipboardFormatter.ToPortableMarkdown($"```explorer\n{Json}\n```",
                new Dictionary<string, string> { ["tvs"] = state.ToJson() });
            Assert.Contains("| Option | Price | Rating | Panel | Refresh rate | Dolby Vision | Year |", markdown);
            Assert.Contains("| \u2713 LG C4 (Best overall) | $1,599.99 | 4.7/5 | OLED | 144 Hz | Yes | \u2014 |", markdown);
            Assert.Contains("- [LG C4](https://example.com/lg): A great all-rounder.", markdown);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void SpecHeavyAnswersKeepTheirFirstTwelveAttributesInsteadOfFailing()
    {
        var attributes = string.Join(",", Enumerable.Range(1, 10).Select(index => $$"""{"id":"a{{index}}","label":"A{{index}}","type":"number"}"""));
        var json = $$$"""{"id":"laptops","title":"Laptops","attributes":[{{{attributes}}}],"items":[{"id":"x","label":"X","values":{"a1":1,"extra1":"Red","extra2":true,"extra3":3}}]}""";
        var model = InteractiveExplorerModel.For((InteractiveExplorer)InteractiveDefinition.Parse("explorer", json));
        Assert.Equal(InteractiveExplorerModel.MaxAttributes, model.Attributes.Count);
        Assert.Equal("a1", model.Attributes[0].Id);
        Assert.Equal(["extra1", "extra2"], model.Attributes.Skip(10).Select(attribute => attribute.Id));
    }

    [Fact]
    public void WholeNumbersFilterInWholeStepsAndYearsReadAsWritten()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var model = InteractiveExplorerModel.For((InteractiveExplorer)InteractiveDefinition.Parse("explorer", """
                {"id":"tv","title":"TVs","attributes":[{"id":"hdmi","label":"HDMI ports","type":"number","better":"higher"},{"id":"year","label":"Year","type":"number"},{"id":"mah","label":"Battery","type":"number","unit":"mAh"}],
                 "items":[{"id":"a","label":"A","values":{"hdmi":0,"year":2024,"mah":12000}},{"id":"b","label":"B","values":{"hdmi":2,"year":2025,"mah":5000}},{"id":"c","label":"C","values":{"hdmi":4,"year":2025,"mah":4500}}]}
                """));
            Assert.Equal(1, model.Facet("min:hdmi")!.Step);
            Assert.Equal(1, model.Facet("max:year")!.Step);
            Assert.Equal("2025", InteractiveExplorerModel.FormatNumber(model.Attribute("year")!, 2025));
            Assert.Equal("12,000 mAh", InteractiveExplorerModel.FormatNumber(model.Attribute("mah")!, 12000));
            Assert.Equal("5000 mAh", InteractiveExplorerModel.FormatNumber(model.Attribute("mah")!, 5000));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void NumbersKeepTheirPrecisionAndSlidersStartAtTheLowestValue()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var definition = (InteractiveExplorer)InteractiveDefinition.Parse("explorer", """
                {"id":"plans","title":"Plans","attributes":[{"id":"rate","label":"Rate","type":"number","unit":"%","better":"lower"},{"id":"fuel","label":"Fuel","type":"money","unit":"USD","better":"lower"},{"id":"rating","label":"Rating","type":"rating","better":"higher"},{"id":"huge","label":"Huge","type":"number"}],
                 "items":[{"id":"a","label":"A","values":{"rate":6.125,"fuel":3.459,"rating":4.3,"huge":1e308}},{"id":"b","label":"B","values":{"rate":5.875,"fuel":3.5,"rating":4.5,"huge":-1e308}},{"id":"c","label":"C","values":{"rate":6.5,"fuel":3.29,"rating":4.7}}]}
                """);
            var model = InteractiveExplorerModel.For(definition);
            // The model gets values exactly as given, and people see them without their last digit rounded away.
            var forModel = string.Join("\n", model.DescribeItemForModel(definition.Items[0]));
            Assert.Contains("Rate: 6.125 %", forModel);
            Assert.Contains("Fuel: 3.459 USD", forModel);
            Assert.Equal("6.125%", InteractiveExplorerModel.FormatNumber(model.Attribute("rate")!, 6.125));
            Assert.Equal("$3.459", InteractiveExplorerModel.FormatNumber(model.Attribute("fuel")!, 3.459));
            Assert.Equal("$3.50", InteractiveExplorerModel.FormatNumber(model.Attribute("fuel")!, 3.5));
            // 4.3 / 0.1 is 42.999… in floating point; the slider still starts at the lowest rating.
            Assert.Equal(4.3, model.Facet("min:rating")!.Minimum);
            // Figures no option has would overflow ranges and steps, so they count as missing.
            Assert.True(model.Value(definition.Items[0], model.Attribute("huge")!).IsEmpty);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void MonogramsKeepShortBrandNamesWhole()
    {
        Assert.Equal("LG", StrataInteractiveBlock.Monogram("LG C4"));
        Assert.Equal("SB", StrataInteractiveBlock.Monogram("Sony Bravia 8"));
        Assert.Equal("H", StrataInteractiveBlock.Monogram("Hilton"));
        Assert.InRange(StrataInteractiveBlock.Tone("lg"), 1, 6);
        Assert.Equal(StrataInteractiveBlock.Tone("lg"), StrataInteractiveBlock.Tone("lg"));
    }

    // ── Rendered ──

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static bool Has(Control root, string name) =>
        root.GetVisualDescendants().OfType<Control>().Any(control => control.Name == name && control.IsEffectivelyVisible);

    private static void Click(Button button)
    {
        var top = TopLevel.GetTopLevel(button);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(top);
    }

    /// <summary>Runs the layout pass a frame would, so templated content of new controls exists.</summary>
    private static void Settle(TopLevel? top)
    {
        top?.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        top?.UpdateLayout();
    }

    [Fact]
    public async Task CardsOpenDetailsAndPickedOptionsCompareSideBySide()
    {
        await fixture.Dispatch(async () =>
        {
            var control = new StrataInteractiveBlock { Definition = Definition(), IsHosted = true, CanSubmit = true };
            var actions = new List<string>();
            control.Requested += (_, request) =>
            {
                if (request.Action is not null) actions.Add(request.Action);
                request.Completion = Task.FromResult(new InteractiveActionResult(true));
            };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "5 options");
                Assert.Equal(5, control.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("explorer-card")));
                Assert.Equal(new Size(28, 28), Find<ToggleButton>(control, "ExplorerCompare_lg").Bounds.Size);

                Click(Find<Button>(control, "ExplorerCard_lg"));
                Assert.True(Has(control, "ExplorerDetail"));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Perfect blacks");
                Click(Find<Button>(control, "ExplorerNext"));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Samsung S90D" && text.Classes.Contains("explorer-detail-title"));
                Find<ToggleButton>(control, "ExplorerDetailCompare_samsung").IsChecked = true;
                Click(Find<Button>(control, "ExplorerBack"));
                Assert.False(Has(control, "ExplorerDetail"));

                Find<ToggleButton>(control, "ExplorerCompare_tcl").IsChecked = true;
                Assert.Equal(["samsung", "tcl"], InteractiveState.Parse(control.StateJson!).SelectedIds);
                Assert.Contains("selected", Find<Button>(control, "ExplorerCard_tcl").Classes);
                var compare = Find<Button>(control, "ExplorerCompare");
                Assert.True(compare.IsEnabled);
                Click(compare);
                Assert.True(Has(control, "ExplorerCompareView"));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Best" && text.IsEffectivelyVisible);
                Assert.True(Find<Button>(control, "Action_compare").IsEffectivelyVisible);
                Assert.Empty(actions);

                Assert.True((await control.SubmitAsync("compare")).Accepted);
                Assert.Equal(["compare"], actions);
                Click(Find<Button>(control, "ExplorerRemove_tcl"));
                Assert.False(Has(control, "ExplorerCompareView"));
                Assert.Equal(["samsung"], InteractiveState.Parse(control.StateJson!).SelectedIds);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task FiltersSortAndViewsSaveLocallyWithoutAnAgentAction()
    {
        await fixture.Dispatch(() =>
        {
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var control = new StrataInteractiveBlock { Definition = Definition(), IsHosted = true };
            var actions = 0;
            control.Requested += (_, request) => { if (request.Action is not null) actions++; };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                Click(Find<Button>(control, "ExplorerFilters"));
                Assert.True(Has(control, "ExplorerFilterPanel"));
                Find<ToggleButton>(control, "ExplorerFilter_panel_OLED").IsChecked = true;
                Assert.Equal(["lg", "sony"], control.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("explorer-card") && button.IsEffectivelyVisible)
                    .Select(button => (string)button.Tag!));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "2 of 5");
                Assert.Equal(["OLED"], InteractiveState.Parse(control.StateJson!).Choices["panel"]);

                Find<RadioButton>(control, "ExplorerView_table").IsChecked = true;
                Settle(window);
                Assert.True(Has(control, "ExplorerTable"));
                Assert.Equal("table", InteractiveState.Parse(control.StateJson!).Values["view"]);
                Assert.Equal(2, control.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("explorer-row")));
                var cheapest = Find<Button>(control, "ExplorerRow_lg").GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("explorer-cell") && text.Text!.Contains("1,599.99"));
                Assert.Contains("best", cheapest.Classes);

                Click(Find<Button>(control, "ExplorerHeader_price"));
                Assert.Equal("price:asc", InteractiveState.Parse(control.StateJson!).Values["sort"]);
                Assert.Equal(0, actions);

                var replacement = new StrataInteractiveBlock { Definition = control.Definition, StateJson = control.StateJson };
                window.Content = replacement;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(Has(replacement, "ExplorerTable"));
                Assert.Equal(2, replacement.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("explorer-row")));
            }
            finally
            {
                window.Close();
                CultureInfo.CurrentCulture = culture;
            }
        });
    }

    [Fact]
    public async Task ComparisonHoldsFourAndNarrowFiltersOfferToFindMore()
    {
        await fixture.Dispatch(async () =>
        {
            var state = new InteractiveState();
            state.Choices["panel"] = ["QD-OLED"];
            var control = new StrataInteractiveBlock
            {
                Definition = Definition(), StateJson = state.ToJson(), IsHosted = true, CanSubmit = true
            };
            var actions = new List<string>();
            control.Requested += (_, request) =>
            {
                if (request.Action is not null) actions.Add(request.Action);
                request.Completion = Task.FromResult(new InteractiveActionResult(true));
            };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                Assert.True(Has(control, "ExplorerMoreCallout"));
                // A compare pip sits on its card; its click must not also open the card.
                Find<ToggleButton>(control, "ExplorerCompare_samsung").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(Has(control, "ExplorerDetail"));
                Assert.True(Find<Button>(control, "Action_more").IsEnabled);
                Assert.True((await control.SubmitAsync("more")).Accepted);
                Assert.Equal(["more"], actions);

                Click(Find<Button>(control, "ExplorerActive_0"));
                Assert.False(Has(control, "ExplorerMoreCallout"));
                foreach (var id in new[] { "lg", "samsung", "tcl", "sony" })
                    Find<ToggleButton>(control, "ExplorerCompare_" + id).IsChecked = true;
                var fifth = Find<ToggleButton>(control, "ExplorerCompare_hisense");
                fifth.IsChecked = true;
                Settle(window);
                Assert.False(fifth.IsChecked);
                Assert.Equal(4, InteractiveState.Parse(control.StateJson!).SelectedIds.Count);
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == InteractiveLabels.English.Format("compareLimit", 4));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task PhonesGetOneColumnTouchTargetsAndAScrollingComparison()
    {
        await fixture.Dispatch(() =>
        {
            var state = new InteractiveState { SelectedIds = ["lg", "samsung", "tcl", "sony"], Values = { ["sort"] = "hz:desc" } };
            var control = new StrataInteractiveBlock
            {
                Definition = Definition(), StateJson = state.ToJson(), TouchMode = true, IsHosted = true
            };
            // A phone's theme makes every control taller and its smallest text larger; pieces with a set size keep
            // their shape, and no words fall below the phone's caption size.
            control.Resources["Size.ControlHeightM"] = 52d;
            control.Resources["Font.SizeCaption"] = 13d;
            var window = StrataInteractiveBlockTests.Show(control, width: 380);
            void AssertReadable(string surface)
            {
                var small = control.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) && text.FontSize < 13)
                    .Select(text => $"'{text.Text}' {text.FontSize}px").ToList();
                Assert.True(small.Count == 0, $"{surface}: {string.Join(", ", small)}");
            }
            try
            {
                var cards = control.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("explorer-card")).ToList();
                Assert.Equal(5, cards.Count);
                Assert.All(cards, card => Assert.Equal(0, Grid.GetColumn(card)));
                Assert.Equal(new Size(40, 40), Find<ToggleButton>(control, "ExplorerCompare_lg").Bounds.Size);
                // A narrow row names only the sorted attribute and its direction, beside a view switch as tall as it.
                var sort = Find<Button>(control, "ExplorerSort");
                Assert.Equal(["Refresh rate"], sort.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                Assert.Equal("Sorted by Refresh rate: high to low", Avalonia.Automation.AutomationProperties.GetName(sort));
                var views = Find<RadioButton>(control, "ExplorerView_cards").GetVisualAncestors().OfType<Border>()
                    .First(border => border.Classes.Contains("explorer-segmented"));
                Assert.Equal(sort.Bounds.Height, views.Bounds.Height);
                var sortEnd = sort.TranslatePoint(new Point(sort.Bounds.Width, 0), control)!.Value.X;
                var viewsStart = views.TranslatePoint(default, control)!.Value.X;
                Assert.True(sortEnd <= viewsStart + 0.5, $"Sort ends at {sortEnd}; the view switch starts at {viewsStart}.");

                // Above their filters, labels start where the filters do.
                Click(Find<Button>(control, "ExplorerFilters"));
                var labels = Find<Border>(control, "ExplorerFilterPanel").GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.Classes.Contains("explorer-filter-label")).ToList();
                Assert.NotEmpty(labels);
                Assert.All(labels, label => Assert.Equal(0, label.Bounds.X));
                Find<ToggleButton>(control, "ExplorerFilter_panel_OLED").IsChecked = true;
                Settle(window);
                AssertReadable("filters");
                Find<ToggleButton>(control, "ExplorerFilter_panel_OLED").IsChecked = false;
                Find<RadioButton>(control, "ExplorerView_table").IsChecked = true;
                Settle(window);
                AssertReadable("table");
                Find<RadioButton>(control, "ExplorerView_cards").IsChecked = true;
                Settle(window);

                Click(Find<Button>(control, "ExplorerCompare"));
                Assert.True(Has(control, "ExplorerCompareView"));
                AssertReadable("comparison");
                Assert.Contains(control.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => viewer.Content is Grid { Name: "ExplorerCompareGrid" });
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task PhoneComparisonsGiveOptionsTheWidthAndKeepRowLabelsInView()
    {
        await fixture.Dispatch(() =>
        {
            foreach (var language in new[] { "en", "he" })
            {
                var state = new InteractiveState { SelectedIds = ["lg", "samsung"] };
                var control = new StrataInteractiveBlock
                {
                    Definition = Definition(), StateJson = state.ToJson(), TouchMode = true, IsHosted = true, Language = language
                };
                var window = StrataInteractiveBlockTests.Show(control, width: 380);
                // The tests load Strata alone, which styles the host theme's scroll viewer rather than templating one.
                window.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>())
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollViewer>((_, scope) =>
                            new ScrollContentPresenter { Name = "PART_ContentPresenter" }.RegisterInNameScope(scope)))
                    }
                });
                Settle(window);
                try
                {
                    // Before a sort is chosen, a narrow row's sort is its icon alone, a full touch target.
                    var sort = Find<Button>(control, "ExplorerSort");
                    Assert.Empty(sort.GetVisualDescendants().OfType<TextBlock>());
                    Assert.True(sort.Bounds.Width >= 48 && sort.Bounds.Height >= 48, $"Sort is {sort.Bounds.Size}.");

                    // Two options share the phone's width, each row's label heading its values.
                    Click(Find<Button>(control, "ExplorerCompare"));
                    var grid = Find<Grid>(control, "ExplorerCompareGrid");
                    Assert.Equal("ExplorerCompareView", ((Control)grid.GetVisualParent()!).Name);
                    Assert.Equal(2, grid.ColumnDefinitions.Count);
                    var headings = Headings(grid);
                    Assert.NotEmpty(headings);
                    Assert.All(headings, heading => Assert.Equal(2, Grid.GetColumnSpan(heading)));

                    // Four need more room than a phone has: they scroll, and the labels stay where the columns start.
                    Click(Find<Button>(control, "ExplorerBack"));
                    foreach (var id in new[] { "tcl", "sony" })
                        Find<ToggleButton>(control, "ExplorerCompare_" + id).IsChecked = true;
                    Settle(window);
                    Click(Find<Button>(control, "ExplorerCompare"));
                    grid = Find<Grid>(control, "ExplorerCompareGrid");
                    var viewer = grid.GetVisualAncestors().OfType<ScrollViewer>().First();
                    viewer.Offset = new Vector(viewer.Extent.Width - viewer.Viewport.Width, 0);
                    Settle(window);
                    Assert.True(viewer.Offset.X > 100, $"{language}: scrolled {viewer.Offset.X} of {viewer.Extent} in {viewer.Viewport}.");
                    Assert.All(Headings(grid), heading =>
                    {
                        // Either corner may come first: a label can read in another direction than its row.
                        var start = Math.Min(heading.TranslatePoint(default, viewer)!.Value.X,
                            heading.TranslatePoint(new Point(heading.Bounds.Width, 0), viewer)!.Value.X);
                        Assert.True(Math.Abs(start) < 0.5, $"{language}: '{heading.Text}' starts at {start}.");
                    });
                }
                finally { window.Close(); }
            }
        });

        static List<TextBlock> Headings(Grid grid) =>
            grid.Children.OfType<TextBlock>().Where(text => text.Classes.Contains("explorer-compare-label")).ToList();
    }

    [Fact]
    public async Task ANarrowTableTrimsSubtitlesWithinTheirColumn()
    {
        await fixture.Dispatch(() =>
        {
            var json = Json.Replace("\"subtitle\":\"65\\\" OLED\"", "\"subtitle\":\"65-inch OLED with a glossy, low-reflection screen\"");
            var state = new InteractiveState { Values = { ["view"] = "table" } };
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("explorer", json), StateJson = state.ToJson(), TouchMode = true, IsHosted = true
            };
            var window = StrataInteractiveBlockTests.Show(control, width: 380);
            try
            {
                var row = Find<Button>(control, "ExplorerRow_lg");
                var subtitle = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("explorer-subtitle"));
                var price = row.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("explorer-cell"));
                var subtitleEnd = subtitle.TranslatePoint(new Point(subtitle.Bounds.Width, 0), row)!.Value.X;
                var priceStart = price.TranslatePoint(default, row)!.Value.X;
                Assert.True(subtitleEnd <= priceStart, $"The subtitle ends at {subtitleEnd}; the price starts at {priceStart}.");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task HebrewExplorerReadsRightToLeft()
    {
        await fixture.Dispatch(() =>
        {
            var control = new StrataInteractiveBlock { Definition = Definition(), Language = "he", IsHosted = true };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, control.FlowDirection);
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "5 אפשרויות");
                Click(Find<Button>(control, "ExplorerCard_sony"));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "כל האפשרויות");

                // A rebuild while the details stay open (a new language, or an edit from another device)
                // keeps the details and still counts the options.
                control.Language = "en";
                Settle(window);
                Assert.True(Has(control, "ExplorerDetail"));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "5 options");
            }
            finally { window.Close(); }
        });
    }

    // ── Sending and browsing ──

    [Fact]
    public async Task ASentActionStaysSentWhileTheReaderOnlyChangesHowOptionsAreShown()
    {
        await fixture.Dispatch(async () =>
        {
            var control = new StrataInteractiveBlock { Definition = Definition(), IsHosted = true, CanSubmit = true };
            control.Requested += (_, request) => request.Completion = Task.FromResult(new InteractiveActionResult(true));
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                Find<ToggleButton>(control, "ExplorerCompare_lg").IsChecked = true;
                Assert.True((await control.SubmitAsync("use")).Accepted);
                Settle(window);
                Assert.False(Find<Button>(control, "Action_use").IsEnabled);

                // Another view and order send the same picks, so the action stays sent.
                Find<RadioButton>(control, "ExplorerView_table").IsChecked = true;
                Settle(window);
                Click(Find<Button>(control, "ExplorerHeader_price"));
                Assert.False(Find<Button>(control, "Action_use").IsEnabled);

                Find<ToggleButton>(control, "ExplorerRowCompare_tcl").IsChecked = true;
                Settle(window);
                Assert.True(Find<Button>(control, "Action_use").IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task FilteringKeepsBuiltCardsAndFocusReturnsToTheOpenedOption()
    {
        await fixture.Dispatch(() =>
        {
            var control = new StrataInteractiveBlock { Definition = Definition(), IsHosted = true };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                var samsung = Find<Button>(control, "ExplorerCard_samsung");
                Click(Find<Button>(control, "ExplorerFilters"));
                var oled = Find<ToggleButton>(control, "ExplorerFilter_panel_OLED");
                oled.IsChecked = true;
                Settle(window);
                // Filtering hides the cards it leaves out rather than rebuilding the grid.
                Assert.False(samsung.IsEffectivelyVisible);
                oled.IsChecked = false;
                Settle(window);
                Assert.Same(samsung, Find<Button>(control, "ExplorerCard_samsung"));
                Assert.True(samsung.IsEffectivelyVisible);

                Click(Find<Button>(control, "ExplorerCard_sony"));
                Click(Find<Button>(control, "ExplorerBack"));
                Assert.Same(Find<Button>(control, "ExplorerCard_sony"), TopLevel.GetTopLevel(control)!.FocusManager!.GetFocusedElement());
            }
            finally { window.Close(); }
        });
    }

    // ── Link previews ──

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQMcAAAAASUVORK5CYII=");

    private static string TempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"strata-explorer-picture-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, TinyPng);
        return path;
    }

    private static async Task WaitFor(TopLevel top, Func<bool> ready)
    {
        for (var attempt = 0; attempt < 150 && !ready(); attempt++)
        {
            Settle(top);
            await Task.Delay(20);
        }
        Settle(top);
        Assert.True(ready(), "The explorer did not reach its expected state.");
    }

    private static Image? Picture(Control root, string id) =>
        Find<Button>(root, "ExplorerCard_" + id).GetVisualDescendants().OfType<Image>()
            .FirstOrDefault(image => image.Classes.Contains("explorer-image"));

    private static int MediaCount(Control root) =>
        root.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("explorer-media"));

    [Fact]
    public void LinkPreviewImageIsThePagesSharePicture()
    {
        var page = new Uri("https://shop.example/tvs/65?ref=1");
        string? Preview(string html) => StrataMarkdown.FindLinkPreviewImage(html, page);

        Assert.Equal("https://cdn.example/tv.jpg", Preview(
            """<head><meta name="twitter:image" content="https://cdn.example/card.jpg"><meta content="https://cdn.example/tv.jpg" property="og:image"></head>"""));
        Assert.Equal("https://cdn.example/secure.jpg", Preview(
            """<meta property="og:image" content="http://cdn.example/plain.jpg"><meta property="og:image:secure_url" content="https://cdn.example/secure.jpg">"""));
        Assert.Equal("https://shop.example/images/og.png", Preview("<meta property='og:image' content='/images/og.png'>"));
        Assert.Equal("https://img.example/a.png", Preview("""<meta property="og:image" content="//img.example/a.png">"""));
        Assert.Equal("https://linear.app/api/og?title=Pricing&v=5",
            Preview("""<meta property="og:image" content="https://linear.app/api/og?title=Pricing&amp;v=5" />"""));
        Assert.Equal("https://cdn.example/card.jpg", Preview("""<META NAME="twitter:image" CONTENT="https://cdn.example/card.jpg">"""));
        Assert.Equal("https://cdn.example/link.jpg", Preview("""<link rel="image_src" href="https://cdn.example/link.jpg">"""));
        // An empty tag would point back at the page itself; the next name stands in.
        Assert.Equal("https://cdn.example/card.jpg", Preview(
            """<meta property="og:image" content=" "><meta name="twitter:image" content="https://cdn.example/card.jpg">"""));
        Assert.Null(Preview("""<meta property="og:image" content="data:image/png;base64,AAAA"><meta property="og:title" content="TV">"""));
        Assert.Null(Preview("""<html><head><title>TV</title><link rel="icon" href="/favicon.ico"></head></html>"""));
    }

    [Fact]
    public void AHostilePageHeadIsReadInLinearTime()
    {
        var page = new Uri("https://shop.example/");
        var longNames = "<head>" + string.Concat(Enumerable.Repeat("<meta " + new string('a', 2000) + ">", 200)) + "</head>";
        var unclosed = "<head>" + string.Concat(Enumerable.Repeat("<meta ", 50_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(StrataMarkdown.FindLinkPreviewImage(longNames, page));
        Assert.Null(StrataMarkdown.FindLinkPreviewImage(unclosed, page));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Reading took {watch.Elapsed}.");
    }

    [Fact]
    public async Task LinkPreviewsStayOffUnlessTheHostTurnsThemOn()
    {
        var asked = 0;
        await fixture.Dispatch(() =>
        {
            StrataMarkdown.LinkPreviewReader = _ =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromResult<string?>(null);
            };
            var control = new StrataInteractiveBlock { Definition = Definition(), IsHosted = true };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                Assert.Equal(0, MediaCount(control));
                Assert.Equal(0, asked);
            }
            finally
            {
                window.Close();
                StrataMarkdown.LinkPreviewReader = null;
            }
        });
    }

    [Fact]
    public async Task OptionsMakeRoomForPicturesOnceTheFirstLinkPreviewArrives()
    {
        var picture = TempPng();
        var preview = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Dispatch(async () =>
        {
            StrataInteractiveBlock.ShowsLinkPreviews = true;
            StrataMarkdown.LinkPreviewReader = page =>
                page.AbsoluteUri == "https://example.com/lg" ? preview.Task : Task.FromResult<string?>(null);
            var control = new StrataInteractiveBlock { Definition = Definition(), IsHosted = true };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                // Until a picture arrives the options keep their compact cards.
                Assert.Equal(0, MediaCount(control));
                var filters = Find<Button>(control, "ExplorerFilters");
                filters.Focus();
                Settle(window);

                preview.SetResult(picture);
                await WaitFor(window, () => Picture(control, "lg")?.Source is not null);
                // Every card makes room; an option without a link keeps its initials.
                Assert.Equal(5, MediaCount(control));
                Assert.Null(Picture(control, "samsung"));
                Assert.Equal(5, control.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("explorer-card")));
                // Only the results were rebuilt: the toolbar, and whatever had focus in it, stayed.
                Assert.Same(filters, Find<Button>(control, "ExplorerFilters"));
                Assert.True(filters.IsFocused);

                Click(Find<Button>(control, "ExplorerCard_lg"));
                Assert.Contains(Find<Control>(control, "ExplorerDetail").GetVisualDescendants().OfType<Image>(),
                    image => image.Source is not null);

                // The table gains a thumbnail column too.
                Click(Find<Button>(control, "ExplorerBack"));
                Find<RadioButton>(control, "ExplorerView_table").IsChecked = true;
                Settle(window);
                Assert.Contains(Find<Button>(control, "ExplorerRow_lg").GetVisualDescendants().OfType<Image>(),
                    image => image.Source is not null);
                Assert.Equal(5, MediaCount(control));
            }
            finally
            {
                window.Close();
                StrataInteractiveBlock.ShowsLinkPreviews = false;
                StrataMarkdown.LinkPreviewReader = null;
                File.Delete(picture);
            }
        });
    }

    [Fact]
    public async Task AnOptionsOwnPictureWinsAndItsLinkStandsInWhenThatFails()
    {
        var own = TempPng();
        var linked = TempPng();
        var missing = Path.Combine(Path.GetTempPath(), $"strata-explorer-missing-{Guid.NewGuid():N}.png");
        var asked = new List<string>();
        var json = $$"""
            {"id":"stays","title":"Stays","items":[
              {"id":"own","label":"Own Picture","image":{{System.Text.Json.JsonSerializer.Serialize(own)}},"url":"https://stays.example/own"},
              {"id":"broken","label":"Broken Picture","image":{{System.Text.Json.JsonSerializer.Serialize(missing)}},"url":"https://stays.example/broken"}]}
            """;
        await fixture.Dispatch(async () =>
        {
            StrataInteractiveBlock.ShowsLinkPreviews = true;
            StrataMarkdown.LinkPreviewReader = page =>
            {
                lock (asked)
                    asked.Add(page.AbsoluteUri);
                return Task.FromResult<string?>(linked);
            };
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("explorer", json), IsHosted = true };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                await WaitFor(window, () => Picture(control, "own")?.Source is not null
                                            && Picture(control, "broken")?.Source is not null);
                lock (asked)
                    Assert.Equal(["https://stays.example/broken"], asked);
            }
            finally
            {
                window.Close();
                StrataInteractiveBlock.ShowsLinkPreviews = false;
                StrataMarkdown.LinkPreviewReader = null;
                File.Delete(own);
                File.Delete(linked);
            }
        });
    }

    [Fact]
    public async Task AHostCanBringPicturesSoTheControlReadsNoPagesItself()
    {
        var pagesRead = 0;
        var link = "https://shop.example/" + Guid.NewGuid().ToString("N");
        var mixed = $$"""
            {"id":"mixed","title":"TVs","items":[
              {"id":"own","label":"Own Picture","image":"https://cdn.example/own.png","url":"https://shop.example/own"},
              {"id":"linked","label":"Linked Picture","url":"{{link}}"},
              {"id":"missing","label":"Missing Picture","url":"https://shop.example/none"},
              {"id":"bare","label":"No Picture"}]}
            """;
        var linksOnly = $$"""
            {"id":"links","title":"TVs","items":[
              {"id":"linked","label":"Linked Picture","url":"{{link}}"},
              {"id":"other","label":"Other Picture","url":"https://shop.example/other"}]}
            """;
        await fixture.Dispatch(async () =>
        {
            var asked = new List<string>();
            StrataInteractiveBlock? asker = null;
            var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            StrataInteractiveBlock.ShowsLinkPreviews = true;
            StrataMarkdown.LinkPreviewReader = _ =>
            {
                Interlocked.Increment(ref pagesRead);
                return Task.FromResult<string?>(null);
            };
            StrataInteractiveBlock.ExplorerPictureLoader = (block, item, _) =>
            {
                asker = block;
                asked.Add(block.Definition!.Id + "/" + item.Id);
                return item.Id switch
                {
                    "missing" => Task.FromResult<byte[]?>(null),
                    "other" => pending.Task,
                    _ => Task.FromResult<byte[]?>(TinyPng)
                };
            };
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("explorer", mixed), IsHosted = true };
            var window = StrataInteractiveBlockTests.Show(control);
            try
            {
                await WaitFor(window, () => Picture(control, "own")?.Source is not null
                                            && Picture(control, "linked")?.Source is not null);
                Assert.Same(control, asker);
                Assert.Equal(["mixed/own", "mixed/linked", "mixed/missing"], asked);
                // An option the host has no picture for keeps its initials; one without a link has nothing to ask.
                Assert.Null(Picture(control, "missing")!.Source);
                Assert.Null(Picture(control, "bare"));

                // The host found that link's preview, so another answer showing the link makes room at once.
                asked.Clear();
                var later = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("explorer", linksOnly), IsHosted = true };
                window.Content = later;
                Settle(window);
                Assert.Equal(2, MediaCount(later));
                Assert.Equal(["links/linked", "links/other"], asked);
                Assert.Equal(0, pagesRead);
            }
            finally
            {
                window.Close();
                StrataInteractiveBlock.ExplorerPictureLoader = null;
                StrataInteractiveBlock.ShowsLinkPreviews = false;
                StrataMarkdown.LinkPreviewReader = null;
                pending.TrySetResult(null);
            }
        });
    }
}
