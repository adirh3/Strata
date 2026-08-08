using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class TextBoxFocusVisualTests
{
    private readonly AvaloniaFixture _fixture;

    public TextBoxFocusVisualTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FocusAccentFollowsTheRoundedTextBoxOutline()
    {
        await _fixture.Dispatch(() =>
        {
            var textBox = new TextBox
            {
                Width = 260,
                CornerRadius = new CornerRadius(16)
            };
            var window = new Window { Width = 320, Height = 120 };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = textBox;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            textBox.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var accent = textBox.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "FocusAccentBar");

            Assert.Equal(textBox.CornerRadius, accent.CornerRadius);
            Assert.Equal(new Thickness(2), accent.BorderThickness);
            Assert.True(double.IsNaN(accent.Height));
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, accent.VerticalAlignment);
            window.Close();
        });
    }

    [Fact]
    public async Task EmbeddedChatEditorsUseTheirIntendedFocusTreatment()
    {
        await _fixture.Dispatch(() =>
        {
            var composer = new StrataChatComposer();
            var message = new StrataChatMessage
            {
                Content = "Editable message",
                EditText = "Editable message",
                IsEditing = true
            };
            var window = new Window
            {
                Width = 420,
                Height = 320
            };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = new StackPanel
            {
                Children =
                {
                    composer,
                    message
                }
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            message.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var composerEditor = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(textBox => textBox.Classes.Contains("composer-embed"));
            composerEditor.ApplyTemplate();
            var composerAccent = composerEditor.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "FocusAccentBar");
            Assert.Equal(2, composerAccent.Height);
            Assert.Equal(new Thickness(0), composerAccent.BorderThickness);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, composerAccent.VerticalAlignment);

            var messageEditor = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(textBox => textBox.Classes.Contains("message-edit-embed"));
            messageEditor.ApplyTemplate();
            var messageAccent = messageEditor.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "FocusAccentBar");
            Assert.False(messageAccent.IsVisible);

            window.Close();
        });
    }
}
