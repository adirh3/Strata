using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using StrataTheme.Controls;
using StrataTheme.Diff;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class LumiSurfaceLocalizationTests(AvaloniaFixture fixture)
{
    [Theory]
    [InlineData("en-US", "Running", "Duration · 1.25s", "1m 04s", "grinning")]
    [InlineData("he-IL", "פועל", "משך · 1.25 שנ׳", "1 דק׳ 04 שנ׳", "חיוך רחב")]
    public async Task NativeChromeAndTimingFollowTheCurrentUiCulture(
        string language, string running, string duration, string elapsed, string emoji) =>
        await fixture.Dispatch(() =>
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var tool = new StrataAiToolCall { DurationMs = 1250, Status = StrataAiToolCallStatus.InProgress };

            Assert.Equal(running, tool.StatusText);
            Assert.Equal(duration, tool.DurationText);
            Assert.Equal(elapsed, RunningElapsedClock.Format(TimeSpan.FromSeconds(64)));
            Assert.Equal(InteractiveLabels.Current.Format("durationSeconds", 1.2), StrataAgentRun.FormatDuration(1200));
            Assert.Equal(InteractiveLabels.Current["selectAllOptions"], new StrataComboBox().SelectAllText);
            var saveLabel = new TextBlock();
            saveLabel.Bind(TextBlock.TextProperty, Assert.IsType<CompiledBindingExtension>(
                new InteractiveLabelExtension("save").ProvideValue(null!)));
            Assert.Equal(InteractiveLabels.Current["save"], saveLabel.Text);
            Assert.Equal(emoji, new EmojiItem("😀", "grinning").DisplayKeyword);
            Assert.Equal("grinning", new EmojiItem("😀", "grinning").Keyword);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    });

    [Fact]
    public async Task TemplateTextLoadedBeforeTheLanguageUsesTheCultureAtStyleApplication() =>
        await fixture.Dispatch(() =>
    {
        var original = CultureInfo.CurrentUICulture;
        Window? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var earlyStyle = new Style(selector => selector.OfType<TextBlock>())
            {
                Setters =
                {
                    new Setter(TextBlock.TextProperty, new InteractiveLabelExtension("edit").ProvideValue(null!))
                }
            };

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("he-IL");
            var text = new TextBlock();
            window = new Window { Content = text, Styles = { earlyStyle } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("עריכה", text.Text);
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = original;
        }
    });

    [Fact]
    public void LibraryTranslationKeysAndCompositePlaceholdersMatch()
    {
        Assert.Equal(InteractiveLabels.English.Keys.Order(), InteractiveLabels.Hebrew.Keys.Order());
        foreach (var key in InteractiveLabels.English.Keys)
        {
            static IEnumerable<string> Placeholders(string value) =>
                Regex.Matches(value, @"\{\d+(?:[^{}]*)\}").Select(match => match.Value).Order();
            Assert.Equal(Placeholders(InteractiveLabels.English[key]), Placeholders(InteractiveLabels.Hebrew[key]));
        }
    }

    [Theory]
    [InlineData("en-US", "No visible changes.", "No diff information was captured for this change.", "Unable to load diff.", "Edit 1")]
    [InlineData("he-IL", "אין שינויים להצגה.", "לא נשמר מידע על ההבדלים עבור השינוי הזה.", "לא ניתן לטעון את תצוגת השינויים.", "עריכה 1")]
    public void DiffBuilderLocalizesChromeButPreservesSourceAndProtocolMarkers(
        string language, string noChanges, string notCaptured, string unavailable, string editHeader)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            // A short snapshot is otherwise retained as a context-only hunk when the context budget covers it.
            Assert.Equal(noChanges, UnifiedDiffBuilder.BuildFromSnapshots("same", "same", contextLineCount: 0).EmptyStateText);
            Assert.Equal(notCaptured, UnifiedDiffBuilder.BuildFromEdits([], isCreate: false).EmptyStateText);
            Assert.Equal(unavailable, UnifiedDiffBuilder.BuildFromUnifiedDiff(null).EmptyStateText);

            var edits = UnifiedDiffBuilder.BuildFromEdits(
                [("old English source", "new English source"), ("before", "after")], isCreate: false);
            Assert.Equal(editHeader, edits.Hunks[0].Header);
            Assert.Contains(edits.Hunks[0].Lines, line => line.Text == "old English source");
            Assert.Contains(edits.Hunks[0].Lines, line => line.Text == "new English source");

            var parsed = UnifiedDiffBuilder.BuildFromUnifiedDiff(
                "Staged changes\nWorking tree changes\n@@ -1,1 +1,1 @@\n-old source\n+new source");
            Assert.Equal("@@ -1,1 +1,1 @@", Assert.Single(parsed.Hunks).Header);
            Assert.Equal(1, parsed.AddedLineCount);
            Assert.Equal(1, parsed.RemovedLineCount);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task MarkdownExplicitLanguageLocalizesLinkChromeWithoutChangingTheUrl() =>
        await fixture.Dispatch(() =>
    {
        var markdown = new StrataMarkdown { InteractionLanguage = "he" };
        var tooltip = typeof(StrataMarkdown).GetMethod("BuildLinkTooltip", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Equal("פתיחת https://example.test/", tooltip.Invoke(markdown, ["https://example.test/"]));
    });

    [Fact]
    public void HebrewEmojiSearchKeepsStableCategoryAndKeywordValues()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("he-IL");
            Assert.Contains(EmojiCatalog.Search("חיוך רחב"), item => item.Emoji == "😀");
            Assert.Contains("Smileys", EmojiCatalog.CategoryNames);
            Assert.Contains(EmojiCatalog.GetByCategory("Smileys"), item => item.Keyword == "grinning");
            Assert.Equal("פרצופים", EmojiCategoryNameConverter.Instance.Convert(
                "Smileys", typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
