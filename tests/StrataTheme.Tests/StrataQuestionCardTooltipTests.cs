using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

// Regression guard for the "long question text gets clipped" report. Long option labels render in a
// fixed-width button whose template clips (ClipToBounds + non-wrapping content), so the full text must
// stay reachable via a tooltip. The question text carries the same full-text tooltip. Both are asserted
// against the real applied control template, not a hand-built stand-in.
[Collection("Avalonia UI")]
public class StrataQuestionCardTooltipTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataQuestionCardTooltipTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AppliedTemplate_ExposesFullTextTooltips_OnQuestionAndOptions()
    {
        await _fixture.Dispatch(() =>
        {
            const string question =
                "Which of these debugging strategies would you like the autonomous agent to attempt next, " +
                "given that the previous three approaches each failed in subtly different ways?";
            var options = new List<string>
            {
                "Run the comprehensive end-to-end stress harness with verbose logging enabled",
                "Inspect the UI map",
            };

            var card = new StrataQuestionCard
            {
                Question = question,
                OptionsList = options,
                AllowFreeText = false,
            };

            var window = new Window { Width = 520, Height = 400 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataQuestionCard.axaml"),
            });
            window.Content = card;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var questionText = card.GetVisualDescendants()
                .OfType<StrataMarkdown>()
                .Single(m => m.Name == "PART_QuestionText");
            Assert.Equal(question, ToolTip.GetTip(questionText));

            var optionButtons = card.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => b.Classes.Contains("question-option"))
                .ToList();
            Assert.Equal(options.Count, optionButtons.Count);
            foreach (var button in optionButtons)
            {
                // Option labels wrap to two lines, so the content is a TextBlock rather than a raw string.
                var label = Assert.IsType<TextBlock>(button.Content);
                Assert.Equal(label.Text, ToolTip.GetTip(button));
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData("Should I update **`ChatView.axaml`** first?", "Should I update ChatView.axaml first?")]
    [InlineData("## Pick one\nThe rest of the details", "Pick one")]
    [InlineData("\n  - Use the [design doc](https://example.com) or *skip* it", "Use the design doc or skip it")]
    [InlineData("Keep snake_case and 2 * 3 as they are", "Keep snake_case and 2 * 3 as they are")]
    [InlineData("Should I rename `__init__.py` or search `**/*.cs`?", "Should I rename __init__.py or search **/*.cs?")]
    [InlineData("   ", null)]
    public void AnsweredLine_ReadsTheQuestionWithoutItsMarkdown(string question, string? expected)
        => Assert.Equal(expected, StrataQuestionCard.ToPlainLine(question));

    [Fact]
    public async Task AnsweredCard_FoldsToThePlainFirstLine()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataQuestionCard
            {
                Question = "Should I use **this** one?\nMore detail.",
                Options = "Yes,No",
                SelectedAnswer = "Yes",
                IsAnswered = true,
            };
            var window = new Window { Width = 520, Height = 200 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataQuestionCard.axaml"),
            });
            window.Content = card;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var row = card.GetVisualDescendants().OfType<DockPanel>().Single(panel => panel.Name == "PART_AnsweredRow");
            Assert.True(row.IsEffectivelyVisible);
            Assert.Contains("Should I use this one?", row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

            window.Close();
        });
    }

    [Fact]
    public async Task AnsweredCard_OpensToItsOptionsWithTheAnswerMarked_AndClosesAgain()
    {
        await _fixture.Dispatch(() =>
        {
            // Read back answered, as a transcript rebuilds it: the answer arrives by binding, not by a click.
            var card = new StrataQuestionCard
            {
                Question = "Which color?",
                Options = "Blue,Green",
                AllowFreeText = true,
                SelectedAnswer = "Blue",
                IsAnswered = true,
            };
            var window = new Window { Width = 520, Height = 400 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataQuestionCard.axaml"),
            });
            window.Content = card;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            T Part<T>(string name) where T : Control => card.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);
            List<Button> Rows() => card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("question-option")).ToList();
            var body = Part<StackPanel>("PART_Body");
            var expand = Part<Button>("PART_ExpandButton");
            var collapse = Part<Button>("PART_CollapseButton");
            Assert.False(body.IsEffectivelyVisible);
            Assert.True(expand.IsEffectivelyVisible);

            // The chip opens the card: the whole question and its options, settled, with the answer marked.
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsExpanded);
            Assert.True(body.IsEffectivelyVisible);
            Assert.False(expand.IsEffectivelyVisible);
            Assert.True(collapse.IsEffectivelyVisible);
            Assert.False(Part<Grid>("PART_FreeTextArea").IsEffectivelyVisible);
            Assert.All(Rows(), static row => Assert.False(row.IsEnabled));
            Assert.Equal(new[] { "Blue" }, Rows().Where(static row => row.Classes.Contains("selected")).Select(static row => row.Tag));

            // An answer in the user's own words shows as a chosen row after the options, once.
            card.SelectedAnswer = "Teal";
            card.SelectedAnswer = "Teal, please";
            Dispatcher.UIThread.RunJobs();
            var chosen = Assert.Single(Rows(), static row => row.Classes.Contains("selected"));
            Assert.Equal("Teal, please", Assert.IsType<TextBlock>(chosen.Content).Text);
            Assert.Equal(3, Rows().Count);

            // The chevron closes it back to the chip.
            collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsExpanded);
            Assert.False(body.IsEffectivelyVisible);
            Assert.True(expand.IsEffectivelyVisible);

            window.Close();
        });
    }

    [Fact]
    public async Task AnsweringByClick_MarksTheChosenOptionForWhenTheCardIsOpened()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataQuestionCard { Question = "Which color?", Options = "Blue,Green", AllowFreeText = false };
            var window = new Window { Width = 520, Height = 400 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataQuestionCard.axaml"),
            });
            window.Content = card;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            string? submitted = null;
            card.AnswerSubmitted += (_, answer) => submitted = answer;
            var rows = card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("question-option")).ToList();
            rows[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Green", submitted);
            Assert.True(card.IsAnswered);
            Assert.All(rows, static row => Assert.False(row.IsEnabled));
            Assert.Equal(new[] { false, true }, rows.Select(static row => row.Classes.Contains("selected")));

            window.Close();
        });
    }
}
