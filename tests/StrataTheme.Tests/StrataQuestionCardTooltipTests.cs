using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
                var label = Assert.IsType<string>(button.Content);
                Assert.Equal(label, ToolTip.GetTip(button));
            }

            window.Close();
        });
    }
}
