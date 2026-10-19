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

// TouchMode sizes the card for a finger: the options the card builds itself, and the reply field and
// its send button in its template, become 48px targets; off again, the pointer-sized card returns.
[Collection("Avalonia UI")]
public class StrataQuestionCardTouchModeTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataQuestionCardTouchModeTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TouchMode_MakesOptionsAndReplyFingerTargets_AndRevertsWhenOff()
    {
        await _fixture.Dispatch(() =>
        {
            var card = new StrataQuestionCard
            {
                Question = "Where should I install the test build?",
                OptionsList = new List<string> { "On my Fold", "On the emulator first" },
                AllowFreeText = true,
            };

            var window = new Window { Width = 412, Height = 600 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/Controls/StrataQuestionCard.axaml"),
            });
            window.Content = card;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            card.ApplyTemplate();
            window.UpdateLayout();

            List<Button> Options() => card.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("question-option")).ToList();
            var reply = card.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_FreeTextBox");
            var send = card.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_FreeTextSubmit");

            Assert.All(Options(), option => Assert.True(option.Bounds.Height < 48, $"{option.Bounds.Height}px"));
            Assert.True(send.Bounds.Height < 48);

            card.TouchMode = true;
            window.UpdateLayout();
            Assert.Contains(":touch", card.Classes);
            Assert.All(Options(), option =>
            {
                Assert.Contains("touch", option.Classes);
                Assert.True(option.Bounds.Height >= 48, $"{option.Bounds.Height}px");
            });
            Assert.True(reply.Bounds.Height >= 48, $"{reply.Bounds.Height}px");
            Assert.Equal(48, send.Bounds.Width);
            Assert.Equal(48, send.Bounds.Height);

            // Options the card rebuilds while in touch mode carry it too.
            card.OptionsList = new List<string> { "Yes", "No", "Ask me later" };
            window.UpdateLayout();
            Assert.Equal(3, Options().Count);
            Assert.All(Options(), option => Assert.True(option.Bounds.Height >= 48, $"{option.Bounds.Height}px"));

            card.TouchMode = false;
            window.UpdateLayout();
            Assert.DoesNotContain(":touch", card.Classes);
            Assert.All(Options(), option =>
            {
                Assert.DoesNotContain("touch", option.Classes);
                Assert.True(option.Bounds.Height < 48, $"{option.Bounds.Height}px");
            });
            Assert.True(send.Bounds.Height < 48);

            window.Close();
        });
    }
}
