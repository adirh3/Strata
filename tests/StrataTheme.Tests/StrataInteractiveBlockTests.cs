using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Input;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataInteractiveBlockTests(AvaloniaFixture fixture)
{
    internal static Window Show(Control content, double width = 760)
    {
        var window = new Window
        {
            Width = width, Height = 800, Content = content,
            Template = new FuncControlTemplate<Window>((owner, scope) => new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner }
                }.RegisterInNameScope(scope)
            })
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        if (content is StrataInteractiveBlock interactive)
            interactive.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataInteractiveBlock)));
        if (content is StrataActionReceipt receipt)
            receipt.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataActionReceipt)));
        foreach (var nested in content.GetLogicalDescendants().OfType<StrataInteractiveBlock>())
            nested.Theme = Assert.IsType<ControlTheme>(window.FindResource(typeof(StrataInteractiveBlock)));
        window.Show();
        window.ApplyTemplate();
        window.Measure(new Size(width, 800));
        window.Arrange(new Rect(0, 0, width, 800));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.GetVisualDescendants().Contains(content),
            "Window did not attach the interaction: " + string.Join(", ", window.GetVisualDescendants().Select(element => element.GetType().Name)));
        if (content is TemplatedControl templated)
        {
            templated.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
        }
        return window;
    }

    [Theory]
    [MemberData(nameof(InteractiveDefinitionTests.Definitions), MemberType = typeof(InteractiveDefinitionTests))]
    public async Task EachDefinitionRendersNativeControls(string language, string json)
    {
        await fixture.Dispatch(() =>
        {
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse(language, json), IsHosted = true, CanSubmit = true
            };
            var window = Show(control);
            try
            {
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == control.Definition.Title);
                Assert.True(control.Bounds.Width > 0);
                Assert.NotEmpty(control.GetVisualDescendants().OfType<Button>());
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ChecklistEditsPersistWithoutAnAgentAction()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "checklist")[1];
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("checklist", json), IsHosted = true };
            var actions = 0;
            string? saved = null;
            control.Requested += (_, request) =>
            {
                if (request.Action is not null) actions++;
                saved = request.StateJson;
                request.Completion = Task.FromResult(new InteractiveActionResult(true));
            };
            var window = Show(control);
            try
            {
                control.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = true;
                Assert.Equal(0, actions);
                Assert.Contains("milk", InteractiveState.Parse(saved!).CheckedIds);
                var replacement = new StrataInteractiveBlock { Definition = control.Definition, StateJson = saved };
                window.Content = replacement;
                Dispatcher.UIThread.RunJobs();
                Assert.True(replacement.GetVisualDescendants().OfType<CheckBox>().First().IsChecked);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task InvalidFormCannotSubmitAndDisplaysAFieldError()
    {
        await fixture.Dispatch(async () =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "form")[1];
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("form", json), IsHosted = true, CanSubmit = true };
            var actions = 0;
            control.Requested += (_, request) => { if (request.Action is not null) actions++; };
            var window = Show(control);
            try
            {
                Assert.False((await control.SubmitAsync("submit")).Accepted);
                Assert.Equal(0, actions);
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsVisible && text.Text == InteractiveLabels.English["required"]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ExplicitSubmissionIsAcknowledgedAndCannotDoubleSend()
    {
        await fixture.Dispatch(async () =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "results")[1];
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("results", json),
                StateJson = new InteractiveState { SelectedIds = ["a"] }.ToJson(),
                IsHosted = true, CanSubmit = true
            };
            var actions = 0;
            var acknowledgement = new TaskCompletionSource<InteractiveActionResult>();
            control.Requested += (_, request) =>
            {
                if (request.Action is null) return;
                actions++;
                request.Completion = acknowledgement.Task;
            };
            var window = Show(control);
            try
            {
                var pending = control.SubmitAsync("use");
                Assert.False((await control.SubmitAsync("use")).Accepted);
                acknowledgement.SetResult(new InteractiveActionResult(true));
                Assert.True((await pending).Accepted);
                Assert.False((await control.SubmitAsync("use")).Accepted);
                Assert.Equal(1, actions);
                Assert.NotNull(InteractiveState.Parse(control.StateJson!).SubmittedFingerprint);

                // Changing the selection and changing it back is the same request, so it stays sent.
                var use = control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Action_use");
                var other = control.GetVisualDescendants().OfType<CheckBox>().Single(card => card.Name == "Result_b");
                other.IsChecked = true;
                Assert.True(use.IsEnabled);
                other.IsChecked = false;
                Assert.False(use.IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task QuizHintAndAnswerStayLocalAndRestore()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "quiz")[1];
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("quiz", json) };
            var actions = 0;
            control.Requested += (_, request) => { if (request.Action is not null) actions++; };
            var window = Show(control);
            try
            {
                control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "QuizHint")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                control.GetVisualDescendants().OfType<RadioButton>().Last().IsChecked = true;
                control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "QuizCheck")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var state = InteractiveState.Parse(control.StateJson!);
                Assert.Contains("q1", state.RevealedHints);
                Assert.Contains("q1", state.CheckedAnswers);
                Assert.Equal(1, state.Answers["q1"]);
                Assert.Equal(0, actions);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ShortSelectsAndMultiselectsUseChipsThatSaveLocally()
    {
        await fixture.Dispatch(() =>
        {
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("form",
                    """{"id":"prefs","title":"Prefs","fields":[{"id":"pace","label":"Pace","type":"select","options":["Relaxed","Balanced","Packed"]},{"id":"tags","label":"Tags","type":"multiselect","options":["Food","Nature"]}]}""")
            };
            var actions = 0;
            control.Requested += (_, request) => { if (request.Action is not null) actions++; };
            var window = Show(control);
            try
            {
                Assert.Empty(control.GetVisualDescendants().OfType<ComboBox>());
                control.GetVisualDescendants().OfType<RadioButton>().Single(chip => chip.Name == "Choice_pace_1").IsChecked = true;
                control.GetVisualDescendants().OfType<ToggleButton>().Single(chip => chip.Name == "Choice_tags_0").IsChecked = true;
                var state = InteractiveState.Parse(control.StateJson!);
                Assert.Equal("Balanced", state.Values["pace"]);
                Assert.Equal(["Food"], state.Choices["tags"]);
                Assert.Equal(0, actions);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task NumberedStepsHighlightOnlyTheNextUncheckedStep()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().Last(item => (string)item[0] == "checklist")[1];
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("checklist", json) };
            var window = Show(control);
            try
            {
                var steps = control.GetVisualDescendants().OfType<CheckBox>().ToList();
                Assert.Equal(["1", "2", "3"], steps.Select(step => step.Tag as string));
                Assert.Contains("current", steps[0].Classes);
                steps[0].IsChecked = true;
                Assert.DoesNotContain("current", steps[0].Classes);
                Assert.Contains("current", steps[1].Classes);
                Assert.Single(steps, step => step.Classes.Contains("current"));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task CheckedQuizAnswersShowCorrectAndIncorrectStates()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "quiz")[1];
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("quiz", json),
                StateJson = new InteractiveState { Answers = new() { ["q1"] = 0 }, CheckedAnswers = ["q1"] }.ToJson()
            };
            var window = Show(control);
            try
            {
                var options = control.GetVisualDescendants().OfType<RadioButton>().ToList();
                Assert.Contains("incorrect", options[0].Classes);
                Assert.Contains("correct", options[1].Classes);
                Assert.All(options, option => Assert.False(option.IsEnabled));
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "0/1");
                Assert.Contains(control.GetVisualDescendants().OfType<Button>(), button => button.Name == "Action_discuss");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ExpiredRequestKeepsAFriendlyDraftSummaryInsteadOfTheFullForm()
    {
        await fixture.Dispatch(() =>
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var form = InteractiveDefinition.Parse("form",
                """{"id":"trip","title":"Trip","fields":[{"id":"city","label":"City","required":true},{"id":"budget","label":"Budget","type":"number","unit":"USD"}]}""");
            var state = InteractiveState.Create(form);
            state.Values["budget"] = "1200";
            var control = new StrataInteractiveBlock { Definition = form, StateJson = state.ToJson(), InputStatus = "Expired" };
            var window = Show(control);
            try
            {
                Assert.Empty(control.GetVisualDescendants().OfType<TextBox>());
                Assert.Empty(control.GetVisualDescendants().OfType<NumericUpDown>());
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Budget: $1,200");
            }
            finally
            {
                window.Close();
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        });
    }

    [Fact]
    public async Task OverBudgetHeadlineNamesTheShortfall()
    {
        await fixture.Dispatch(() =>
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("calculator",
                    """{"id":"budget","title":"Budget","template":"budget","currency":"USD","inputs":{"income":1000,"essential":900,"flexible":300,"savings":100}}""")
            };
            var window = Show(control);
            try
            {
                var headline = control.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CalculatorHeadline");
                Assert.Equal("$300", headline.Text);
                Assert.Contains("negative", headline.Classes);
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Over budget by");
            }
            finally
            {
                window.Close();
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        });
    }

    [Fact]
    public async Task OnlyDirectionalIconsMirrorInRightToLeftLayouts()
    {
        await fixture.Dispatch(() =>
        {
            Assert.Equal(Avalonia.Media.FlowDirection.LeftToRight, InteractiveIcons.Create(InteractiveIcons.Check).FlowDirection);
            Assert.False(InteractiveIcons.Create(InteractiveIcons.ArrowForward).IsSet(Visual.FlowDirectionProperty));
        });
    }

    [Fact]
    public async Task StreamingAppendKeepsTheEditedWidgetInstance()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "checklist")[1];
            var source = $"```checklist\n{json}\n```";
            var markdown = new StrataMarkdown { Markdown = source, StreamingRebuildThrottleMs = 0 };
            var window = Show(markdown);
            try
            {
                var original = markdown.GetVisualDescendants().OfType<StrataInteractiveBlock>().Single();
                original.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = true;
                markdown.Markdown = source + "\n\nSome more explanation.";
                Dispatcher.UIThread.RunJobs();
                var updated = markdown.GetVisualDescendants().OfType<StrataInteractiveBlock>().Single();
                Assert.Same(original, updated);
                Assert.Contains("milk", InteractiveState.Parse(updated.StateJson!).CheckedIds);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task PartialStreamingJsonNeverCreatesAnActiveForm()
    {
        await fixture.Dispatch(() =>
        {
            var markdown = new StrataMarkdown { Markdown = "```form\n{\"id\":\"unfinished\"", StreamingRebuildThrottleMs = 0 };
            var window = Show(markdown);
            try { Assert.Empty(markdown.GetVisualDescendants().OfType<StrataInteractiveBlock>()); }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task TouchPickerStaysInsideItsLocalDeviceHostAndUsesTouchSizedActions()
    {
        await fixture.Dispatch(() =>
        {
            var host = new Grid { Width = 412, Height = 650 };
            var control = new StrataInteractiveBlock
            {
                Definition = new InteractiveForm
                {
                    Id = "touch", Title = "Touch form",
                    Fields = [new() { Id = "pace", Label = "Pace", Type = "select",
                        Options = ["Relaxed", "Balanced", "Packed", "Spontaneous", "Cultural", "Outdoorsy"] }]
                },
                TouchMode = true, PickerHost = host
            };
            host.Children.Add(control);
            var window = Show(host, 760);
            try
            {
                var input = control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Field_pace");
                Assert.True(input.Bounds.Height >= 48);
                input.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var picker = Assert.Single(host.Children.OfType<StrataBottomSheet>());
                Assert.Equal(412, picker.Width);
                Assert.Equal(650, picker.Height);
                Assert.True(picker.IsOpen);
                var option = picker.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content as string == "Balanced");
                option.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(host.Children.OfType<StrataBottomSheet>());
                Assert.Equal("Balanced", InteractiveState.Parse(control.StateJson!).Values["pace"]);
                input.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Single(host.Children.OfType<StrataBottomSheet>());
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Empty(host.Children.OfType<StrataBottomSheet>());
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task CalculatorWithABlankInputRestoresAndNamesTheField()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "calculator")[1];
            var definition = InteractiveDefinition.Parse("calculator", json);
            var state = InteractiveState.Create(definition);
            state.Values["tipPercent"] = "";
            var control = new StrataInteractiveBlock { Definition = definition, StateJson = state.ToJson(), IsHosted = true, CanSubmit = true };
            var window = Show(control);
            try
            {
                var error = control.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Error");
                Assert.Equal($"{InteractiveLabels.English["tipPercent"]}: {InteractiveLabels.English["number"]}", error.Text);
                Assert.Equal(240m, control.GetVisualDescendants().OfType<NumericUpDown>().Single(editor => editor.Name == "Input_bill").Value);
                Assert.False(control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Action_assumptions").IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task OutOfRangeNumbersAreKeptAndReportedInsteadOfReplaced()
    {
        await fixture.Dispatch(async () =>
        {
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("form",
                    """{"id":"trip","title":"Trip","fields":[{"id":"budget","label":"Budget","type":"number","minimum":0,"maximum":5000}]}"""),
                IsHosted = true, CanSubmit = true
            };
            var actions = 0;
            control.Requested += (_, request) => { if (request.Action is not null) actions++; };
            var window = Show(control);
            try
            {
                var editor = control.GetVisualDescendants().OfType<NumericUpDown>().Single(input => input.Name == "Field_budget");
                editor.Value = 8000;
                Assert.False((await control.SubmitAsync("submit")).Accepted);
                Assert.Equal(0, actions);
                Assert.Equal(8000m, editor.Value);
                Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsVisible && text.Text == InteractiveLabels.English["range"]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task TypedTextIsSavedOnceWhenEditingEnds()
    {
        await fixture.Dispatch(() =>
        {
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("form",
                    """{"id":"trip","title":"Trip","fields":[{"id":"city","label":"City"},{"id":"note","label":"Note"}]}"""),
                IsHosted = true
            };
            var saves = new List<string>();
            control.Requested += (_, request) => { if (request.Action is null) saves.Add(request.StateJson); };
            var window = Show(control);
            try
            {
                var city = control.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Field_city");
                Assert.True(city.Focus());
                city.Text = "L";
                city.Text = "Li";
                city.Text = "Lisbon";
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(saves);
                Assert.True(control.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Field_note").Focus());
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Lisbon", InteractiveState.Parse(Assert.Single(saves)).Values["city"]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task MovingACachedInteractionDuringAFullRebuildKeepsTheControl()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "checklist")[1];
            var checklist = $"```checklist\n{json}\n```";
            var markdown = new StrataMarkdown { Markdown = "Intro\n\n```text\nold\n```\n\n" + checklist, StreamingRebuildThrottleMs = 0 };
            var window = Show(markdown);
            try
            {
                var original = markdown.GetVisualDescendants().OfType<StrataInteractiveBlock>().Single();
                markdown.Markdown = "Intro\n\n" + checklist;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(original, markdown.GetVisualDescendants().OfType<StrataInteractiveBlock>().Single());
                Assert.DoesNotContain(markdown.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == InteractiveLabels.English["definitionError"]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task CardsStartAtTheLeadingEdgeOfWideColumns()
    {
        await fixture.Dispatch(() =>
        {
            var json = (string)InteractiveDefinitionTests.Definitions().First(item => (string)item[0] == "checklist")[1];
            var control = new StrataInteractiveBlock { Definition = InteractiveDefinition.Parse("checklist", json) };
            var column = new StackPanel { Width = 1000, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            column.Children.Add(control);
            var window = Show(column, 1100);
            try
            {
                Assert.Equal(0, control.Bounds.X);
                Assert.Equal(720, control.Bounds.Width);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task DatePickerSelectionMovesUntilADayIsChosen()
    {
        await fixture.Dispatch(() =>
        {
            var host = new Grid { Width = 412, Height = 650 };
            var control = new StrataInteractiveBlock
            {
                Definition = InteractiveDefinition.Parse("form",
                    """{"id":"trip","title":"Trip","fields":[{"id":"date","label":"Departure","type":"date","value":"2026-10-23"}]}"""),
                TouchMode = true, PickerHost = host
            };
            var saves = 0;
            control.Requested += (_, request) => { if (request.Action is null) saves++; };
            host.Children.Add(control);
            var window = Show(host, 760);
            try
            {
                control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Field_date")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var calendar = host.GetVisualDescendants().OfType<Avalonia.Controls.Calendar>().Single();
                calendar.SelectedDate = new DateTime(2026, 10, 24); // what an arrow key does
                Assert.Equal(0, saves);
                Assert.Single(host.Children.OfType<StrataBottomSheet>());
                calendar.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal(1, saves);
                Assert.Equal("2026-10-24", InteractiveState.Parse(control.StateJson!).Values["date"]);
                Assert.Empty(host.Children.OfType<StrataBottomSheet>());
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task DetachingThePickerHostWithAnOpenSheetDoesNotThrow()
    {
        await fixture.Dispatch(() =>
        {
            var host = new Grid { Width = 412, Height = 650 };
            var control = new StrataInteractiveBlock
            {
                Definition = new InteractiveForm
                {
                    Id = "touch", Title = "Touch form",
                    Fields = [new() { Id = "pace", Label = "Pace", Type = "select",
                        Options = ["Relaxed", "Balanced", "Packed", "Spontaneous", "Cultural", "Outdoorsy"] }]
                },
                TouchMode = true, PickerHost = host
            };
            host.Children.Add(control);
            var window = Show(host, 760);
            try
            {
                control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Field_pace")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Single(host.Children.OfType<StrataBottomSheet>());
                window.Content = new Border();
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(host.Children.OfType<StrataBottomSheet>());
            }
            finally { window.Close(); }
        });
    }
}
