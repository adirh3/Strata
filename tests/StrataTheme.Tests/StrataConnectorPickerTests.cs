using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public class StrataConnectorPickerTests
{
    private const string Reason = "Couldn't start: “npx” wasn't found on this computer.";
    private const string Details = "spawn npx ENOENT\n    at ChildProcess._handle.onexit (node:internal/child_process:285:19)";

    private readonly AvaloniaFixture _fixture;

    public StrataConnectorPickerTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConnectorNeedingAttention_LeadsWithAFriendlyReasonAndKeepsDetailsTucked()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Open();
            var rows = host.Rows();

            Assert.Equal(["Broken", "Healthy", "Starting"], rows.Select(RowName));
            Assert.Equal("NEEDS ATTENTION", host.Headers().First());
            Assert.Equal(Reason, Named<TextBlock>(rows[0], "McpErrorSummary").Text);
            Assert.Equal(Details, Named<SelectableTextBlock>(rows[0], "McpErrorDetailsText").Text);
            Assert.False(Named<Border>(rows[0], "McpErrorDetails").IsVisible);

            Click(Named<Button>(rows[0], "McpErrorDetailsToggle"));
            Assert.True(Named<Border>(rows[0], "McpErrorDetails").IsVisible);

            Assert.Equal("Still starting.", Named<TextBlock>(RowNamed(rows, "Starting"), "McpStatusMessage").Text);
            Assert.Single(rows.SelectMany(row => row.GetLogicalDescendants().OfType<Button>()), button => button.Name == "McpErrorAction");

            Assert.Equal("3", host.ButtonLabel.Text);
            Assert.Contains(":has-errors", host.Picker.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task ErrorAction_ClosesTheListAndReceivesTheConnector()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Open();
            Assert.True(host.Popup.IsOpen);

            var action = Named<Button>(host.Rows()[0], "McpErrorAction");
            Assert.True(action.IsEnabled);
            Click(action);

            Assert.False(host.Popup.IsOpen);
            Assert.False(host.Picker.IsDropDownOpen);
            Assert.Equal(1, host.FixCommand.ExecuteCount);
            var chip = Assert.IsType<StrataComposerChip>(host.FixCommand.LastParameter);
            Assert.Equal("Broken", chip.Name);
            Assert.Equal(Details, chip.ErrorDetails);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task StatusChangeWhileOpen_RefreshesInPlaceWithoutReordering()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Open();
            var brokenRow = host.Rows()[0];

            var index = host.Selected.IndexOf(host.Selected.OfType<StrataComposerChip>().Single(chip => chip.Name == "Broken"));
            host.Selected[index] = new StrataComposerChip("Broken", SecondaryText: "Runs on this computer");
            Dispatcher.UIThread.RunJobs();

            var rows = host.Rows();
            Assert.Same(brokenRow, rows[0]);
            Assert.Equal("Broken", RowName(rows[0]));
            Assert.DoesNotContain(rows.SelectMany(row => row.GetLogicalDescendants().OfType<Button>()), button => button.Name == "McpErrorAction");
            Assert.DoesNotContain(":has-errors", host.Picker.Classes);
            Assert.True(host.Popup.IsOpen);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task Switches_EditTheChatsSelection_AndTheSummaryFollows()
    {
        await _fixture.Dispatch(async () =>
        {
            var host = Host.Create();
            // The host's own chip may still carry an old state; a connector switched on starts clean.
            host.Composer.AvailableMcps = new[]
            {
                new StrataComposerChip("Healthy", SecondaryText: "Searches your notes", ErrorMessage: Reason, ErrorDetails: Details, StatusMessage: "Still starting."),
                new StrataComposerChip("Starting"),
                new StrataComposerChip("Broken"),
            };
            host.Open();
            Assert.Equal("3 of 3 on in this chat", host.Part<TextBlock>("_summaryText").Text);
            Assert.Equal("Turn all off", host.Part<TextBlock>("_toggleAllText").Text);

            var healthy = RowNamed(host.Rows(), "Healthy");
            Named<ToggleButton>(healthy, "ConnectorSwitch").IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(host.Selected.OfType<StrataComposerChip>(), chip => chip.Name == "Healthy");
            Assert.Contains("off", healthy.Classes);
            Assert.Equal("2 of 3 on in this chat", host.Part<TextBlock>("_summaryText").Text);
            Assert.Equal("Turn all on", host.Part<TextBlock>("_toggleAllText").Text);
            // The open list covers the pill and is centred on it: the pill's label waits, so the list never shifts.
            Assert.Equal("3", host.ButtonLabel.Text);

            Click(host.Part<Button>("_toggleAllButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Broken", "Healthy", "Starting"], host.Selected.OfType<StrataComposerChip>().Select(chip => chip.Name).Order());
            var turnedOn = host.Selected.OfType<StrataComposerChip>().Single(chip => chip.Name == "Healthy");
            Assert.Null(turnedOn.ErrorMessage);
            Assert.Null(turnedOn.ErrorDetails);
            Assert.Null(turnedOn.StatusMessage);

            Click(host.Part<Button>("_toggleAllButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Selected);
            Assert.Equal("3", host.ButtonLabel.Text);
            Assert.DoesNotContain(":has-selection", host.Picker.Classes);

            host.Picker.IsDropDownOpen = false;
            using (host.Window.CaptureRenderedFrame())
                Dispatcher.UIThread.RunJobs();
            // Still the old count while the list animates away.
            Assert.Equal("3", host.ButtonLabel.Text);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":open", host.Picker.Classes);
            Assert.Equal("Off", host.ButtonLabel.Text);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task ClickingARowSwitchesIt_ButAProblemsDetailsDoNot()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Open();

            var details = Named<TextBlock>(host.Rows()[0], "McpErrorSummary");
            details.RaiseEvent(Press(details));
            Assert.Contains(host.Selected.OfType<StrataComposerChip>(), chip => chip.Name == "Broken");

            var healthy = RowNamed(host.Rows(), "Healthy");
            var name = Named<TextBlock>(healthy, "ConnectorName");
            name.RaiseEvent(Press(name));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(host.Selected.OfType<StrataComposerChip>(), chip => chip.Name == "Healthy");
            host.Window.Close();
        });
    }

    [Fact]
    public async Task EnterDoesNothingUntilTheUserMovesToARow()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Open();
            var search = host.Part<TextBox>("_searchBox");

            Key(search, Avalonia.Input.Key.Enter);
            Assert.Equal(3, host.Selected.Count);
            Assert.DoesNotContain(host.Rows(), row => row.Classes.Contains("active"));

            Key(search, Avalonia.Input.Key.Down);
            Key(search, Avalonia.Input.Key.Down);
            Assert.Contains("active", RowNamed(host.Rows(), "Healthy").Classes);
            Key(search, Avalonia.Input.Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(host.Selected.OfType<StrataComposerChip>(), chip => chip.Name == "Healthy");

            Key(search, Avalonia.Input.Key.Escape);
            Assert.False(host.Popup.IsOpen);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task Search_RanksMatches_AndOffersToLookFurtherWhenNothingMatches()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Open();

            host.Picker.SearchText = "notes";
            var rows = host.Rows();
            Assert.Equal(["Healthy"], rows.Select(RowName));
            Assert.Empty(host.Headers());
            Assert.Contains("active", rows[0].Classes);
            Assert.Contains(":searching", host.Picker.Classes);

            host.Picker.SearchText = "jira";
            Assert.Contains(":no-results", host.Picker.Classes);
            Assert.Equal("No connectors match “jira”", host.Part<TextBlock>("_noResultsText").Text);
            Click(host.Part<Button>("_searchBrowseButton"));

            Assert.False(host.Popup.IsOpen);
            Assert.Equal("jira", host.BrowseCommand.LastParameter);

            host.Open();
            Assert.Equal(string.Empty, host.Picker.SearchText);
            Assert.DoesNotContain(":no-results", host.Picker.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task ConnectorsAreGroupedLikeTheConnectorsPage()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create(
                [
                    new StrataComposerChip("Docs", Group: "Yours"),
                    new StrataComposerChip("azure", Group: "From plugins & Copilot", SecondaryText: "From the Azure plugin"),
                    new StrataComposerChip("Notes", Group: "Yours"),
                ],
                []);
            host.Open();

            Assert.Equal(["YOURS", "FROM PLUGINS & COPILOT"], host.Headers());
            Assert.Equal(["Docs", "Notes", "azure"], host.Rows().Select(RowName));
            Assert.Equal("From the Azure plugin", Named<TextBlock>(RowNamed(host.Rows(), "azure"), "ConnectorCaption").Text);
            Assert.Equal("Off", host.ButtonLabel.Text);
            host.Window.Close();

            var single = Host.Create([new StrataComposerChip("Docs", Group: "Yours")], []);
            single.Open();
            Assert.Empty(single.Headers());
            single.Window.Close();
        });
    }

    [Fact]
    public async Task WithoutConnectors_TheButtonLeadsToFindingThem()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create([], []);
            Assert.Contains(":has-mcp-options", host.Composer.Classes);
            Assert.False(host.ButtonLabel.IsVisible);

            host.Open();
            Assert.Contains(":no-connectors", host.Picker.Classes);
            var find = host.Part<Button>("_emptyBrowseButton");
            // Ready for Enter either way; its focus ring only follows a keyboard opening.
            Assert.True(find.IsFocused);
            Assert.DoesNotContain(":focus-visible", find.Classes);
            Click(find);
            Assert.False(host.Popup.IsOpen);
            Assert.Equal(1, host.BrowseCommand.ExecuteCount);
            Assert.Null(host.BrowseCommand.LastParameter);

            host.Composer.McpIsLoading = true;
            Assert.Contains(":loading", host.Picker.Classes);

            host.Button.Focus(NavigationMethod.Tab);
            host.Open();
            Assert.Contains(":focus-visible", find.Classes);
            Click(host.Part<Button>("_emptyManageButton"));
            Assert.Equal(1, host.ManageCommand.ExecuteCount);
            host.Window.Close();

            var withoutBrowsing = Host.Create([], [], browse: false);
            Assert.DoesNotContain(":has-mcp-options", withoutBrowsing.Composer.Classes);
            withoutBrowsing.Window.Close();
        });
    }

    [Fact]
    public async Task ConnectorsArrivingWhileOpen_JoinTheListAndKeepTheKeyboardInIt()
    {
        await _fixture.Dispatch(() =>
        {
            var offered = new ObservableCollection<object>();
            var host = Host.Create([], []);
            host.Composer.AvailableMcps = offered;
            host.Composer.McpIsLoading = true;
            host.Open();
            Assert.True(host.Part<Button>("_emptyBrowseButton").IsFocused);

            // Discovery finishes while the list is open: the empty state gives way to the list, and the
            // keyboard moves to the search field instead of being dropped with the hidden button.
            offered.Add(new StrataComposerChip("Docs"));
            host.Composer.McpIsLoading = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Docs"], host.Rows().Select(RowName));
            var search = host.Part<TextBox>("_searchBox");
            Assert.True(search.IsFocused);

            // A later arrival joins the list; existing rows, and the row the user moved to, stay put.
            Key(search, Avalonia.Input.Key.Down);
            var docs = host.Rows()[0];
            offered.Add(new StrataComposerChip("Notes"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Docs", "Notes"], host.Rows().Select(RowName));
            Assert.Same(docs, host.Rows()[0]);
            Assert.Contains("active", docs.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task DetachedPicker_StopsFollowingTheHostsConnectors()
    {
        await _fixture.Dispatch(() =>
        {
            var offered = new ObservableCollection<object>();
            var host = Host.Create([], []);
            host.Composer.AvailableMcps = offered;
            Assert.Contains(":no-connectors", host.Picker.Classes);

            // Out of the window it stops listening, so a view model that outlives the view can't keep it alive…
            host.Window.Content = null;
            offered.Add(new StrataComposerChip("Docs"));
            Assert.Contains(":no-connectors", host.Picker.Classes);

            // …and it catches up when it is shown again.
            host.Window.Content = host.Composer;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":no-connectors", host.Picker.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task ButtonSaysWhatItMeans()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Composer.McpToolTip = "1 connector needs attention";

            Assert.Equal("1 connector needs attention", Avalonia.Automation.AutomationProperties.GetName(host.Button));
            Assert.Equal("1 connector needs attention", ToolTip.GetTip(host.Button));
            host.Window.Close();
        });
    }

    private static List<Border> RowsOf(StackPanel list)
        => list.Children.OfType<Border>().Where(border => border.Classes.Contains("connector-row")).ToList();

    private static string? RowName(Border row) => Named<TextBlock>(row, "ConnectorName").Text;

    private static Border RowNamed(IEnumerable<Border> rows, string name) => rows.Single(row => RowName(row) == name);

    private static T Named<T>(Control root, string name) where T : Control
        => root.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Key(Control target, Key key)
        => target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });

    private static PointerPressedEventArgs Press(Control target)
    {
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        return new PointerPressedEventArgs(
            target,
            pointer,
            TopLevel.GetTopLevel(target)!,
            default,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_EnablePopupOverlayLayer")]
    private static extern void SetEnablePopupOverlayLayer(VisualLayerManager layers, bool value);

    private sealed class Host
    {
        public required StrataChatComposer Composer { get; init; }
        public required StrataConnectorPicker Picker { get; init; }
        public required Window Window { get; init; }
        public required ObservableCollection<object> Selected { get; init; }
        public required RecordingCommand FixCommand { get; init; }
        public required RecordingCommand BrowseCommand { get; init; }
        public required RecordingCommand ManageCommand { get; init; }

        public Popup Popup => Part<Popup>("_popup");
        public Button Button => Part<Button>("_button");
        public TextBlock ButtonLabel => Part<TextBlock>("_buttonLabel");

        public static Host Create() => Create(
            [
                new StrataComposerChip("Healthy", SecondaryText: "Searches your notes"),
                new StrataComposerChip("Starting"),
                new StrataComposerChip("Broken"),
            ],
            [
                new StrataComposerChip("Healthy", SecondaryText: "Searches your notes"),
                new StrataComposerChip("Starting", StatusMessage: "Still starting."),
                new StrataComposerChip("Broken", ErrorMessage: Reason, ErrorDetails: Details),
            ]);

        public static Host Create(StrataComposerChip[] available, StrataComposerChip[] selected, bool browse = true)
        {
            var fix = new RecordingCommand();
            var browseCommand = new RecordingCommand();
            var manage = new RecordingCommand();
            var selection = new ObservableCollection<object>(selected);
            var composer = new StrataChatComposer
            {
                AvailableMcps = available,
                McpItems = selection,
                McpErrorActionCommand = fix,
                McpErrorActionLabel = "Fix with Lumi",
                McpBrowseCommand = browse ? browseCommand : null,
                McpManageCommand = manage,
            };

            var window = new Window
            {
                Width = 640,
                Height = 640,
                Template = new FuncControlTemplate<Window>((owner, scope) =>
                {
                    var layers = new VisualLayerManager
                    {
                        EnableOverlayLayer = true,
                        Child = new ContentPresenter
                        {
                            Name = "PART_ContentPresenter",
                            [!ContentPresenter.ContentProperty] = new Binding(nameof(Window.Content)) { Source = owner },
                        }.RegisterInNameScope(scope),
                    };
                    SetEnablePopupOverlayLayer(layers, true);
                    return layers;
                }),
            };
            window.Styles.Add(new StyleInclude(new Uri("avares://StrataTheme/"))
            {
                Source = new Uri("avares://StrataTheme/StrataTheme.axaml")
            });
            window.Content = composer;
            window.Show();
            window.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var picker = composer.GetVisualDescendants().OfType<StrataConnectorPicker>().Single();
            return new Host
            {
                Composer = composer,
                Picker = picker,
                Window = window,
                Selected = selection,
                FixCommand = fix,
                BrowseCommand = browseCommand,
                ManageCommand = manage,
            };
        }

        public void Open()
        {
            Picker.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
        }

        public List<Border> Rows() => RowsOf(Part<StackPanel>("_list"));

        public List<string?> Headers()
            => Part<StackPanel>("_list").Children.OfType<TextBlock>()
                .Where(text => text.Classes.Contains("connector-group-header"))
                .Select(text => text.Text)
                .ToList();

        public T Part<T>(string fieldName)
            => (T)typeof(StrataConnectorPicker)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Picker)!;
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecuteCount { get; private set; }
        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }
    }
}
