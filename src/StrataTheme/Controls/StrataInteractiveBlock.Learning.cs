using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace StrataTheme.Controls;

public partial class StrataInteractiveBlock
{
    private void BuildQuiz(InteractiveQuiz quiz)
    {
        var index = _state.QuestionIndex;
        var question = quiz.Questions[index];
        var answered = _state.CheckedAnswers.Contains(question.Id);
        var complete = quiz.Questions.All(item => _state.CheckedAnswers.Contains(item.Id));
        SetMeta(Pill(_labels.Format("questionShort", index + 1, quiz.Questions.Count)));

        if (quiz.Questions.Count > 1)
        {
            var segments = new Grid { ColumnSpacing = 4 };
            for (var position = 0; position < quiz.Questions.Count; position++)
            {
                var item = quiz.Questions[position];
                segments.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                var segment = new Border { Classes = { "interactive-segment" } };
                if (_state.CheckedAnswers.Contains(item.Id))
                    segment.Classes.Add(_state.Answers[item.Id] == item.CorrectIndex ? "correct" : "incorrect");
                else if (position == index)
                    segment.Classes.Add("current");
                Grid.SetColumn(segment, position);
                segments.Children.Add(segment);
            }
            _body!.Children.Add(segments);
        }

        _body!.Children.Add(Text(question.Prompt, "interactive-question"));
        Button? check = null;
        var options = new StackPanel { Spacing = 8 };
        for (var optionIndex = 0; optionIndex < question.Options.Count; optionIndex++)
        {
            var selectedIndex = optionIndex;
            var chosen = _state.Answers.TryGetValue(question.Id, out var answer) && answer == optionIndex;
            var option = new RadioButton
            {
                Name = "QuizOption_" + optionIndex,
                Content = Text(question.Options[optionIndex]),
                Tag = ((char)('A' + optionIndex)).ToString(),
                IsChecked = chosen,
                IsEnabled = !answered,
                Classes = { "interactive-option" }
            };
            AutomationProperties.SetName(option, question.Options[optionIndex]);
            if (answered)
            {
                if (optionIndex == question.CorrectIndex)
                    option.Classes.Add("correct");
                else if (chosen)
                    option.Classes.Add("incorrect");
                else
                    option.Classes.Add("dimmed");
            }
            option.IsCheckedChanged += (_, _) =>
            {
                if (option.IsChecked != true) return;
                _state.Answers[question.Id] = selectedIndex;
                CommitState();
                if (check is not null)
                    check.IsEnabled = true;
            };
            options.Children.Add(option);
        }
        _body.Children.Add(options);

        if (_state.RevealedHints.Contains(question.Id) && question.Hint is { Length: > 0 } && !answered)
            _body.Children.Add(Callout("hint", InteractiveIcons.Lightbulb, null, question.Hint));
        if (answered)
        {
            var correct = _state.Answers[question.Id] == question.CorrectIndex;
            _body.Children.Add(Callout(correct ? "success" : "danger", correct ? InteractiveIcons.Check : InteractiveIcons.Close,
                _labels[correct ? "correct" : "incorrect"], question.Explanation));
        }
        if (complete)
            _body.Children.Add(ScoreSummary(quiz));

        if (!answered)
        {
            check = AddLocalAction(_labels["check"], () =>
            {
                if (!_state.Answers.ContainsKey(question.Id))
                {
                    SetError(_labels["chooseAnswer"]);
                    return;
                }
                _state.CheckedAnswers.Add(question.Id);
                CommitState();
                Rebuild();
            }, "accent", "QuizCheck");
            check.IsEnabled = _state.Answers.ContainsKey(question.Id);
            if (question.Hint is { Length: > 0 } && !_state.RevealedHints.Contains(question.Id))
                AddLocalAction(_labels["hint"], () =>
                {
                    _state.RevealedHints.Add(question.Id);
                    CommitState();
                    Rebuild();
                }, "subtle", "QuizHint", InteractiveIcons.Lightbulb);
        }
        else if (index < quiz.Questions.Count - 1)
            AddLocalAction(_labels["next"], () =>
            {
                _state.QuestionIndex++;
                CommitState();
                Rebuild();
            }, complete ? "default" : "accent", "QuizNext", InteractiveIcons.ArrowForward, trailingIcon: true);

        if (complete)
        {
            AddSubmitAction(quiz.SubmitLabel ?? _labels["discuss"], "discuss");
            AddLocalAction(_labels["restart"], Reset, "subtle", "QuizRestart", InteractiveIcons.Restart);
        }
        if (index > 0)
            AddLocalAction(_labels["previous"], () =>
            {
                _state.QuestionIndex--;
                CommitState();
                Rebuild();
            }, "subtle", "QuizBack", InteractiveIcons.ArrowBack);
    }

    private Border ScoreSummary(InteractiveQuiz quiz)
    {
        var score = quiz.Questions.Count(item => _state.Answers[item.Id] == item.CorrectIndex);
        var ratio = (double)score / quiz.Questions.Count;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        var value = Text($"{score}/{quiz.Questions.Count}", "interactive-hero");
        value.Name = "QuizScore";
        value.VerticalAlignment = VerticalAlignment.Center;
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(_labels[ratio >= 0.8 ? "scoreGreat" : ratio >= 0.5 ? "scoreGood" : "scoreRetry"], "interactive-strong"));
        text.Children.Add(Text(_labels.Format("score", score, quiz.Questions.Count), "interactive-detail"));
        Grid.SetColumn(text, 1);
        row.Children.Add(value);
        row.Children.Add(text);
        return new Border { Classes = { "interactive-panel" }, Child = row };
    }

    private static Border Callout(string tone, string icon, string? title, string body)
    {
        var text = new StackPanel { Spacing = 2 };
        if (title is not null)
            text.Children.Add(Text(title, "interactive-strong"));
        text.Children.Add(Text(body, title is null ? "interactive-body" : "interactive-detail"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        var glyph = InteractiveIcons.Create(icon, 14);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 2, 0, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(glyph);
        row.Children.Add(text);
        return new Border { Classes = { "interactive-callout", tone }, Child = row };
    }
}
