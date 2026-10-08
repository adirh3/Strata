using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;
using Xunit;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataQuestionCardReattachClickTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataQuestionCardReattachClickTests(AvaloniaFixture fixture) => _fixture = fixture;

    private static FuncControlTemplate<StrataQuestionCard> BuildQuestionTemplate() =>
        new((_, scope) =>
        {
            var options = new WrapPanel { Name = "PART_OptionsPanel" };
            var freeText = new TextBox { Name = "PART_FreeTextBox" };
            var freeTextSubmit = new Button { Name = "PART_FreeTextSubmit" };
            var multiSubmit = new Button { Name = "PART_MultiSubmit" };

            scope.Register("PART_OptionsPanel", options);
            scope.Register("PART_FreeTextBox", freeText);
            scope.Register("PART_FreeTextSubmit", freeTextSubmit);
            scope.Register("PART_MultiSubmit", multiSubmit);

            return new StackPanel
            {
                Children = { options, freeText, freeTextSubmit, multiSubmit },
            };
        });

    private static Button Option(StrataQuestionCard card, string answer) =>
        card.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains("question-option")
                              && Equals(button.Tag, answer));

    [Fact]
    public async Task ClickingOption_AfterReattaching_StillSubmitsAnswer()
    {
        var result = await _fixture.Dispatch(() =>
        {
            var card = new StrataQuestionCard
            {
                Template = BuildQuestionTemplate(),
                OptionsList = ["Red", "Blue"],
                AllowFreeText = false,
            };
            string? submittedAnswer = null;
            card.AnswerSubmitted += (_, answer) => submittedAnswer = answer;

            var host = new Border { Child = card };
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var originalButton = Option(card, "Blue");
            host.Child = null;
            Dispatcher.UIThread.RunJobs();
            host.Child = card;
            Dispatcher.UIThread.RunJobs();

            var reattachedButton = Option(card, "Blue");
            reattachedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var result = (
                SameButton: ReferenceEquals(originalButton, reattachedButton),
                card.SelectedAnswer,
                card.IsAnswered,
                SubmittedAnswer: submittedAnswer);
            window.Close();
            return result;
        });

        Assert.True(result.SameButton);
        Assert.Equal("Blue", result.SelectedAnswer);
        Assert.True(result.IsAnswered);
        Assert.Equal("Blue", result.SubmittedAnswer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HasInputTracksFreeTextAndSelectedOptions(bool multiSelect)
    {
        var result = await _fixture.Dispatch(() =>
        {
            var card = new StrataQuestionCard
            {
                Template = BuildQuestionTemplate(),
                OptionsList = ["Red", "Blue"],
                AllowFreeText = true,
                AllowMultiSelect = multiSelect
            };
            var window = new Window { Width = 400, Height = 300, Content = card };
            window.Show();
            card.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var input = card.GetVisualDescendants().OfType<TextBox>().Single();
            var empty = card.HasInput;
            input.Text = "My own answer";
            var typed = card.HasInput;
            input.Text = "";
            var cleared = card.HasInput;
            var selected = false;
            var deselected = false;
            if (multiSelect)
            {
                var button = Option(card, "Red");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                selected = card.HasInput;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                deselected = card.HasInput;
            }
            window.Close();
            return (empty, typed, cleared, selected, deselected);
        });

        Assert.False(result.empty);
        Assert.True(result.typed);
        Assert.False(result.cleared);
        if (multiSelect)
        {
            Assert.True(result.selected);
            Assert.False(result.deselected);
        }
    }
}
