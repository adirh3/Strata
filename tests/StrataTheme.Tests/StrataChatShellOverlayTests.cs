using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>
/// Layout contract of the opt-in overlay chrome. Scrolling behavior needs a themed ScrollViewer and
/// is covered by the mobile host's tests, which run the real application theme.
/// </summary>
[Collection("Avalonia UI")]
public sealed class StrataChatShellOverlayTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataChatShellOverlayTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task OverlayRunsTranscriptUnderChromeWhileRestingContentStaysClear()
    {
        await _fixture.Dispatch(() =>
        {
            var (shell, header, composer, window) = Create(overlay: true);
            try
            {
                var scroll = shell.TranscriptScrollViewer!;
                var headerChrome = Part<Border>(shell, "PART_HeaderChrome");
                var composerHost = Part<Border>(shell, "PART_ComposerHost");
                Assert.Contains(":chrome-overlay", shell.Classes);
                Assert.Equal(window.Bounds.Height, scroll.Bounds.Height, 1);
                Assert.Equal(0, scroll.TranslatePoint(default, shell)!.Value.Y, 1);
                Assert.Equal(0, headerChrome.TranslatePoint(default, shell)!.Value.Y, 1);
                Assert.Equal(window.Bounds.Height,
                    composerHost.TranslatePoint(new Point(0, composerHost.Bounds.Height), shell)!.Value.Y, 1);
                Assert.Equal(
                    new Thickness(0, headerChrome.Bounds.Height, 0, composerHost.Bounds.Height),
                    Part<Border>(shell, "PART_ScrollContent").Margin);
                Assert.Equal(12 + composerHost.Bounds.Height,
                    Part<Panel>(shell, "PART_ScrollToBottomHost").Margin.Bottom, 1);
                Assert.DoesNotContain(":content-under-header", shell.Classes);

                // Chrome that grows (a banner, the keyboard) moves the resting insets with it.
                header.Height = 100;
                composer.Height = 200;
                Pump(window);
                Assert.Equal(
                    new Thickness(0, headerChrome.Bounds.Height, 0, composerHost.Bounds.Height),
                    Part<Border>(shell, "PART_ScrollContent").Margin);

                // Turning the mode off restores the docked layout's own values.
                shell.ExtendsTranscriptBehindChrome = false;
                Pump(window);
                Assert.DoesNotContain(":chrome-overlay", shell.Classes);
                Assert.Equal(default, Part<Border>(shell, "PART_ScrollContent").Margin);
                Assert.Equal(new Thickness(0, 0, 0, 12), Part<Panel>(shell, "PART_ScrollToBottomHost").Margin);
                Assert.Equal(headerChrome.Bounds.Bottom, scroll.TranslatePoint(default, shell)!.Value.Y, 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task DockedLayoutIsUnchangedWhenOverlayIsOff()
    {
        await _fixture.Dispatch(() =>
        {
            var (shell, _, _, window) = Create(overlay: false);
            try
            {
                var scroll = shell.TranscriptScrollViewer!;
                var headerChrome = Part<Border>(shell, "PART_HeaderChrome");
                var composerHost = Part<Border>(shell, "PART_ComposerHost");
                Assert.DoesNotContain(":chrome-overlay", shell.Classes);
                Assert.Equal(headerChrome.Bounds.Bottom, scroll.TranslatePoint(default, shell)!.Value.Y, 1);
                Assert.Equal(composerHost.TranslatePoint(default, shell)!.Value.Y,
                    scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), shell)!.Value.Y, 1);
                Assert.Equal(default, Part<Border>(shell, "PART_ScrollContent").Margin);
                Assert.Equal(new Thickness(0, 0, 0, 12), Part<Panel>(shell, "PART_ScrollToBottomHost").Margin);
                Assert.DoesNotContain(":content-under-header", shell.Classes);
                Assert.DoesNotContain(":content-under-composer", shell.Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task HeaderOverlayRunsTranscriptUnderHeaderOnlyAndKeepsComposerDocked()
    {
        await _fixture.Dispatch(() =>
        {
            var (shell, header, _, window) = Create(overlay: false, headerOverlay: true);
            try
            {
                var scroll = shell.TranscriptScrollViewer!;
                var headerChrome = Part<Border>(shell, "PART_HeaderChrome");
                var composerHost = Part<Border>(shell, "PART_ComposerHost");
                Assert.Contains(":header-overlay", shell.Classes);
                Assert.DoesNotContain(":chrome-overlay", shell.Classes);
                Assert.Equal(0, scroll.TranslatePoint(default, shell)!.Value.Y, 1);
                Assert.Equal(composerHost.TranslatePoint(default, shell)!.Value.Y,
                    scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), shell)!.Value.Y, 1);
                Assert.Equal(new Thickness(0, headerChrome.Bounds.Height, 0, 0),
                    Part<Border>(shell, "PART_ScrollContent").Margin);
                Assert.Equal(new Thickness(0, 0, 0, 12), Part<Panel>(shell, "PART_ScrollToBottomHost").Margin);
                Assert.DoesNotContain(":content-under-header", shell.Classes);

                header.Height = 100;
                Pump(window);
                Assert.Equal(new Thickness(0, headerChrome.Bounds.Height, 0, 0),
                    Part<Border>(shell, "PART_ScrollContent").Margin);

                // The full canvas takes precedence over the header-only one.
                shell.ExtendsTranscriptBehindChrome = true;
                Pump(window);
                Assert.Contains(":chrome-overlay", shell.Classes);
                Assert.DoesNotContain(":header-overlay", shell.Classes);
                Assert.Equal(
                    new Thickness(0, headerChrome.Bounds.Height, 0, composerHost.Bounds.Height),
                    Part<Border>(shell, "PART_ScrollContent").Margin);

                shell.ExtendsTranscriptBehindChrome = false;
                shell.ExtendsTranscriptBehindHeader = false;
                Pump(window);
                Assert.DoesNotContain(":header-overlay", shell.Classes);
                Assert.Equal(default, Part<Border>(shell, "PART_ScrollContent").Margin);
                Assert.Equal(headerChrome.Bounds.Bottom, scroll.TranslatePoint(default, shell)!.Value.Y, 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (StrataChatShell Shell, Border Header, Border Composer, Window Window) Create(
        bool overlay,
        bool headerOverlay = false)
    {
        var header = new Border { Height = 64 };
        var composer = new Border { Height = 96 };
        var transcript = new StackPanel();
        for (var index = 0; index < 30; index++)
            transcript.Children.Add(new Border { Height = 80 });

        var shell = new StrataChatShell
        {
            Header = header,
            Composer = composer,
            Transcript = transcript,
            ExtendsTranscriptBehindChrome = overlay,
            ExtendsTranscriptBehindHeader = headerOverlay
        };
        var window = new Window { Width = 400, Height = 800 };
        window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
        {
            Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
        });
        window.Content = shell;
        window.Show();
        shell.ApplyTemplate();
        Pump(window);
        return (shell, header, composer, window);
    }

    private static T Part<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Pump(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
