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

/// <summary>
/// The composer's + menu, its Skills, Chats and Agents lists, the chips they add, the # list that offers
/// chats ahead of files, and the send button that dictates: the paths a user takes to bring skills,
/// agents and other chats into a message.
/// </summary>
[Collection("Avalonia UI")]
public class StrataComposerPickerTests
{
    private readonly AvaloniaFixture _fixture;

    public StrataComposerPickerTests(AvaloniaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SkillPicker_ChoosesASkill_StaysOpen_AndRemovesItThroughTheHostCommand()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            var picker = host.SkillPicker;
            Open(picker);

            Assert.Equal(["Code Helper", "Code Reviewer", "Deck Builder"], RowNames(picker));
            Assert.DoesNotContain(":has-selection", picker.Classes);

            PressRow(picker, "Code Reviewer");

            var added = Assert.IsType<StrataComposerChip>(Assert.Single(host.Skills));
            Assert.Equal("Code Reviewer", added.Name);
            Assert.True(PopupOf(picker).IsOpen);
            Assert.Contains(":has-selection", picker.Classes);
            Assert.Equal("1", Part<TextBlock>(picker, "_countText").Text);
            Assert.Single(host.Composer.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("composer-skill-chip"));

            // Removing goes through the same command as the chip's remove button, so the host stays the
            // single owner of which skills are on.
            PressRow(picker, "Code Reviewer");
            Assert.Equal(1, host.SkillRemoved.ExecuteCount);
            Assert.Equal("Code Reviewer", host.SkillRemoved.LastParameter);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task SkillPicker_Search_RanksMatches_AndEnterChoosesTheBest()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            var picker = host.SkillPicker;
            Open(picker);

            picker.SearchText = "deck";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Deck Builder", RowNames(picker).First());
            Key(Part<TextBox>(picker, "_searchBox"), Avalonia.Input.Key.Enter);

            Assert.Equal("Deck Builder", Assert.IsType<StrataComposerChip>(Assert.Single(host.Skills)).Name);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task ChatPicker_RefreshesItsChats_AddsTheChosenOne_AndCloses()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            var picker = host.ChatPicker;
            Open(picker);

            Assert.Equal(1, host.ChatsRequested.ExecuteCount);
            Assert.Equal(["Trip to Japan", "Q4 budget"], RowNames(picker));

            PressRow(picker, "Q4 budget");

            var chat = Assert.IsType<StrataComposerChip>(Assert.Single(host.Chats));
            Assert.Equal(BudgetId, chat.Value);
            Assert.False(PopupOf(picker).IsOpen);
            var chip = Assert.Single(host.Composer.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("composer-chat-chip"));
            Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Q4 budget");

            // Reopened, the chat it already carries leads the list, checked.
            Open(picker);
            Assert.Equal(["Q4 budget", "Trip to Japan"], RowNames(picker));
            Assert.Contains("selected", Rows(picker)[0].Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task ChatChip_RemoveButton_TakesTheChatOut_AndRaisesChatRemoved()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Chats.Add(Budget);
            Dispatcher.UIThread.RunJobs();
            object? removed = null;
            host.Composer.ChatRemoved += (_, args) => removed = args.Item;

            var chip = host.Composer.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("composer-chat-chip"));
            var remove = chip.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal("Remove Q4 budget", Avalonia.Automation.AutomationProperties.GetName(remove));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(host.Chats);
            Assert.Same(Budget, removed);
            Assert.DoesNotContain(host.Composer.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("composer-chat-chip"));
            host.Window.Close();
        });
    }

    [Fact]
    public async Task HashReference_ListsChatsBeforeFiles_AsksForChatsOnce_AndChoosingAChatAddsItsChip()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Composer.AvailableFiles = new[] { new StrataComposerChip("budget.xlsx", "📄", Value: @"C:\docs\budget.xlsx") };
            var input = host.Composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");
            input.Focus();

            host.Window.KeyTextInput("#");
            Dispatcher.UIThread.RunJobs();
            host.Window.KeyTextInput("b");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, host.ChatsRequested.ExecuteCount);
            Assert.True(Part<Popup>(host.Composer, "_autoCompletePopup").IsOpen);
            var kinds = AutoCompleteKinds(host.Composer);
            Assert.Equal(ChipKind.Chat, kinds.First());
            Assert.Contains(ChipKind.File, kinds);
            Assert.True(kinds.IndexOf(ChipKind.File) > kinds.LastIndexOf(ChipKind.Chat));

            Key(input, Avalonia.Input.Key.Enter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BudgetId, Assert.IsType<StrataComposerChip>(Assert.Single(host.Chats)).Value);
            Assert.Equal(string.Empty, host.Composer.PromptText);
            Assert.False(Part<Popup>(host.Composer, "_autoCompletePopup").IsOpen);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task HashReference_WithoutChats_StillWaitsForTheHostsFiles()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create(withChats: false);
            var input = host.Composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");
            input.Focus();

            host.Window.KeyTextInput("#");
            Dispatcher.UIThread.RunJobs();
            Assert.False(Part<Popup>(host.Composer, "_autoCompletePopup").IsOpen);
            Assert.Equal(0, host.ChatsRequested.ExecuteCount);

            host.Composer.AvailableFiles = new[] { new StrataComposerChip("notes.md", "📄", Value: @"C:\notes.md") };
            Dispatcher.UIThread.RunJobs();

            Assert.True(Part<Popup>(host.Composer, "_autoCompletePopup").IsOpen);
            Assert.Equal([ChipKind.File], AutoCompleteKinds(host.Composer));
            host.Window.Close();
        });
    }

    [Fact]
    public async Task HashReference_KeepsWaitingForFiles_WhenItsEmptyListFinishesClosingLater()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            Assert.True(host.Input.Focus());
            host.Window.KeyTextInput("#");
            Dispatcher.UIThread.RunJobs();
            host.Window.KeyTextInput("z");
            Dispatcher.UIThread.RunJobs();
            var popup = Part<Popup>(host.Composer, "_autoCompletePopup");
            Assert.False(popup.IsOpen);

            // A popup reports Closed after its closing fade, once the list has already moved on.
            typeof(StrataChatComposer)
                .GetMethod("OnAutoCompletePopupClosed", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host.Composer, [popup, EventArgs.Empty]);

            host.Composer.AvailableFiles = new[] { new StrataComposerChip("zones.md", "📄", Value: @"C:\zones.md") };
            Dispatcher.UIThread.RunJobs();

            Assert.True(popup.IsOpen);
            Assert.Equal([ChipKind.File], AutoCompleteKinds(host.Composer));
            host.Window.Close();
        });
    }

    [Fact]
    public async Task EmptyList_DrawsItsLineIcon()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Composer.AvailableSkills = Array.Empty<StrataComposerChip>();
            Dispatcher.UIThread.RunJobs();

            host.SkillPicker.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();

            var empty = PopupOf(host.SkillPicker).GetLogicalDescendants().OfType<StackPanel>()
                .Single(panel => panel.Name == "PART_Empty");
            Assert.True(empty.IsVisible);
            var icon = Assert.Single(empty.GetLogicalDescendants().OfType<StrataLineIcon>());
            Assert.NotNull(icon.Data);
            Assert.Empty(empty.GetLogicalDescendants().OfType<PathIcon>());
            host.Window.Close();
        });
    }

    [Fact]
    public async Task NarrowComposer_ShrinksThePickersToTheirIcons()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create(width: 900);
            Assert.True(host.SkillPicker.ShowLabel);
            Assert.DoesNotContain(":icon-only", host.SkillPicker.Classes);

            host.Window.Width = 460;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(":narrow", host.Composer.Classes);
            Assert.False(host.SkillPicker.ShowLabel);
            Assert.Contains(":icon-only", host.ChatPicker.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task Pickers_HideWhenTheHostOffersNothingToPick_AndOnTouch()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create(withChats: false);
            Assert.True(host.SkillPicker.IsVisible);
            Assert.False(host.ChatPicker.IsVisible);

            host.Composer.Classes.Add("mobile");
            Dispatcher.UIThread.RunJobs();
            Assert.False(host.SkillPicker.IsVisible);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task AddMenu_ListsEverythingAMessageCanBringIn_RecentChatsFirst()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            host.Chats.Add(Budget);
            OfferEverything(host.Composer);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":has-add-options", host.Composer.Classes);

            OpenAddMenu(host);

            Assert.True(AddPopup(host).IsOpen);
            Assert.Contains(":add-open", host.Composer.Classes);
            Assert.Equal(1, host.ChatsRequested.ExecuteCount);
            // The chat already in the message is not offered again.
            Assert.Equal(
                ["Files & photos", "Trip to Japan, 5m ago", "All chats", "Skills", "Agents", "Connectors, 2 on", "Project"],
                AddMenuRowNames(host));

            Key(AddMenuRows(host)[0], Avalonia.Input.Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.False(AddPopup(host).IsOpen);
            Assert.DoesNotContain(":add-open", host.Composer.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task AddMenu_RowsAddARecentChat_OrOpenTheListTheyName()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            OfferEverything(host.Composer);
            var attach = new RecordingCommand();
            var attachRequested = 0;
            host.Composer.AttachCommand = attach;
            host.Composer.AttachRequested += (_, _) => attachRequested++;
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Trip to Japan, 5m ago");
            Assert.Equal(TripId, Assert.IsType<StrataComposerChip>(Assert.Single(host.Chats)).Value);
            Assert.False(AddPopup(host).IsOpen);

            OpenAddMenu(host);
            Assert.Contains("Q4 budget, 2h ago", AddMenuRowNames(host));
            Assert.DoesNotContain("Trip to Japan, 5m ago", AddMenuRowNames(host));
            PressAddMenuRow(host, "All chats");
            Assert.True(PopupOf(host.ChatPicker).IsOpen);
            host.ChatPicker.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Skills");
            Assert.True(PopupOf(host.SkillPicker).IsOpen);
            host.SkillPicker.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Connectors, 2 on");
            Assert.True(host.ConnectorPicker.IsDropDownOpen);
            host.ConnectorPicker.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Agents");
            Assert.True(PopupOf(host.AgentPicker).IsOpen);
            host.AgentPicker.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Project");
            Assert.Equal("$", host.Composer.PromptText);
            Assert.True(Part<Popup>(host.Composer, "_autoCompletePopup").IsOpen);
            Assert.Equal([ChipKind.Project], AutoCompleteKinds(host.Composer));
            Key(host.Input, Avalonia.Input.Key.Escape);
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Files & photos");
            Assert.Equal(1, attach.ExecuteCount);
            Assert.Equal(1, attachRequested);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task AddMenu_ArrowKeysWalkTheRows_AndEscapeReturnsToWriting()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            OfferEverything(host.Composer);
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            var rows = AddMenuRows(host);
            Assert.True(rows[0].IsFocused);

            Key(rows[0], Avalonia.Input.Key.Down);
            Assert.True(rows[1].IsFocused);
            Key(rows[1], Avalonia.Input.Key.End);
            Assert.True(rows[^1].IsFocused);
            Key(rows[^1], Avalonia.Input.Key.Down);
            Assert.True(rows[0].IsFocused);
            Key(rows[0], Avalonia.Input.Key.Up);
            Assert.True(rows[^1].IsFocused);

            Key(rows[^1], Avalonia.Input.Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.False(AddPopup(host).IsOpen);
            Assert.True(host.Input.IsFocused);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task AddMenu_KeepsTheComposerEngaged_WhileItsOpen_AndAfterAChoice()
    {
        await _fixture.Dispatch(() =>
        {
            var outside = new Button { Content = "Outside" };
            var host = Host.Create(beside: outside);
            var composer = host.Composer;
            var disengaged = 0;
            composer.Classes.CollectionChanged += (_, e) =>
            {
                if (e.OldItems?.Contains(":engaged") == true)
                    disengaged++;
            };
            Assert.True(host.Input.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":engaged", composer.Classes);

            // The menu opens in the overlay layer and takes the keyboard, outside the composer's visual tree.
            OpenAddMenu(host);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":focus-within", composer.Classes);
            Assert.Contains(":engaged", composer.Classes);

            // A choice closes the menu and hands the keyboard back without a disengaged moment in between.
            PressAddMenuRow(host, "Trip to Japan, 5m ago");
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.Input.IsFocused);
            Assert.Contains(":engaged", composer.Classes);
            Assert.Equal(0, disengaged);

            // Leaving for elsewhere while the menu is open disengages once the menu has closed.
            OpenAddMenu(host);
            Dispatcher.UIThread.RunJobs();
            Assert.True(outside.Focus());
            Assert.Contains(":engaged", composer.Classes);
            AddPopup(host).IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":engaged", composer.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task InputContextMenu_KeepsTheComposerEngaged_WhileItsOpen()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            Assert.True(host.Input.Focus());
            Dispatcher.UIThread.RunJobs();

            // The menu belongs to the input, not to the composer's template. Walking it with the keyboard
            // takes the keyboard out of the composer.
            var menu = host.Input.ContextMenu!;
            menu.Open(host.Input);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            Assert.True(menu.Items.OfType<MenuItem>().First().Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":focus-within", host.Composer.Classes);
            Assert.Contains(":engaged", host.Composer.Classes);

            menu.Close();
            Assert.True(host.Input.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":engaged", host.Composer.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public Task PlainComposer_ReplaysItsSweep_WhenTheKeyboardComesBackFromItsMenu() =>
        _fixture.Dispatch(async () =>
        {
            // The plain composer's line follows keyboard focus, so it draws again, sweep and all, on return.
            var host = Host.Create();
            var sweep = host.Composer.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_FocusSweep");
            Assert.True(host.Input.Focus());
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            Assert.True(StrataTheme.Animation.LifecycleOffsetSweep.IsRunning(sweep));

            OpenAddMenu(host);
            Dispatcher.UIThread.RunJobs();
            Assert.False(StrataTheme.Animation.LifecycleOffsetSweep.IsRunning(sweep));

            AddPopup(host).IsOpen = false;
            Assert.True(host.Input.Focus());
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            Assert.True(StrataTheme.Animation.LifecycleOffsetSweep.IsRunning(sweep));
            host.Window.Close();
        });

    [Fact]
    public async Task AddButton_HidesWithNothingToAdd_AndOnTouch()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create(withChats: false);
            var wrap = host.Composer.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "PART_AddWrap");
            Assert.True(wrap.IsVisible);

            host.Composer.AvailableSkills = null;
            host.Composer.CanAttach = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":has-add-options", host.Composer.Classes);
            Assert.False(wrap.IsVisible);

            host.Composer.CanAttach = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(wrap.IsVisible);

            host.Composer.Classes.Add("mobile");
            Dispatcher.UIThread.RunJobs();
            Assert.False(wrap.IsVisible);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task SendButton_DictatesWhileThereIsNothingToSend_AndStopsDictating()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            var composer = host.Composer;
            var voice = 0;
            var send = 0;
            composer.VoiceRequested += (_, _) => voice++;
            composer.SendRequested += (_, _) => send++;
            var button = composer.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "PART_SendButton");

            Assert.Contains(":voice", composer.Classes);
            Assert.Equal("Dictate", Avalonia.Automation.AutomationProperties.GetName(button));
            Click(button);
            Assert.Equal((1, 0), (voice, send));

            composer.IsRecording = true;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":voice", composer.Classes);
            Assert.Equal("Stop dictating", Avalonia.Automation.AutomationProperties.GetName(button));
            Click(button);
            Assert.Equal((2, 0), (voice, send));

            composer.IsRecording = false;
            composer.PromptText = "Plan my week";
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":voice", composer.Classes);
            Assert.Equal("Send", Avalonia.Automation.AutomationProperties.GetName(button));
            Click(button);
            Assert.Equal((2, 1), (voice, send));

            // Without a recognizer, or on touch, an empty composer has nothing to do.
            composer.PromptText = string.Empty;
            composer.CanVoice = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":voice", composer.Classes);
            Click(button);
            Assert.Equal((2, 1), (voice, send));

            composer.CanVoice = true;
            composer.Classes.Add("mobile");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(":voice", composer.Classes);
            host.Window.Close();
        });
    }

    [Fact]
    public async Task AgentsList_ChoosesTheAgentAsAChip_LeadsWithIt_AndChoosingItAgainRemovesIt()
    {
        await _fixture.Dispatch(() =>
        {
            var host = Host.Create();
            var composer = host.Composer;
            var agentRemoved = new RecordingCommand();
            composer.AgentRemovedCommand = agentRemoved;
            composer.AvailableAgents = Agents;
            Dispatcher.UIThread.RunJobs();

            OpenAddMenu(host);
            PressAddMenuRow(host, "Agents");
            var list = host.AgentPicker;
            Assert.True(PopupOf(list).IsOpen);
            Assert.Equal(["Coding Lumi", "Daily Lumi"], RowNames(list));
            PressRow(list, "Daily Lumi");

            // Chosen like an @ mention: the agent answers, shows as a chip, and the list closes.
            Assert.Equal("Daily Lumi", composer.AgentName);
            Assert.Equal("daily", composer.AgentValue);
            Assert.Equal("☀", composer.AgentGlyph);
            Assert.False(PopupOf(list).IsOpen);
            Assert.True(composer.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "PART_AgentChip").IsEffectivelyVisible);

            // The + menu names it, and the list leads with it, checked.
            OpenAddMenu(host);
            PressAddMenuRow(host, "Agents, Daily Lumi");
            Assert.Equal(["Daily Lumi", "Coding Lumi"], RowNames(list));
            Assert.Contains("selected", Rows(list)[0].Classes);

            // Choosing it again removes it, the same way as the chip's remove button.
            PressRow(list, "Daily Lumi");
            Assert.Equal(1, agentRemoved.ExecuteCount);
            Assert.False(PopupOf(list).IsOpen);

            // The host clears the agent by name; the @ list then offers every agent again.
            composer.AgentName = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(composer.AgentValue);
            Assert.DoesNotContain(":has-agent", composer.Classes);
            host.Window.Close();
        });
    }

    // ── Fixtures ──────────────────────────────────────────────────────

    private const string TripId = "1f0d0c1e-0000-4000-8000-000000000001";
    private const string BudgetId = "1f0d0c1e-0000-4000-8000-000000000002";
    private static readonly StrataComposerChip Budget = new("Q4 budget", "💬", SecondaryText: "Home", Value: BudgetId, SourceLabel: "2h ago");

    private static readonly StrataComposerChip[] Agents =
    [
        new("Coding Lumi", "◉", Value: "coding"),
        new("Daily Lumi", "☀", Value: "daily"),
    ];

    private static void OfferEverything(StrataChatComposer composer)
    {
        composer.AvailableAgents = Agents;
        composer.AvailableProjects = new[] { new StrataComposerChip("Lumi", "📁", Value: "lumi") };
        composer.AvailableMcps = new[]
        {
            new StrataComposerChip("GitHub", "🔌"),
            new StrataComposerChip("Slack", "🔌"),
            new StrataComposerChip("Jira", "🔌"),
        };
        composer.McpItems = new ObservableCollection<object>
        {
            new StrataComposerChip("GitHub", "🔌"),
            new StrataComposerChip("Slack", "🔌"),
        };
    }

    private static Popup AddPopup(Host host) => Part<Popup>(host.Composer, "_addPopup");

    private static void OpenAddMenu(Host host)
    {
        Click(host.Composer.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_AddButton"));
        Assert.True(AddPopup(host).IsOpen);
    }

    private static List<Button> AddMenuRows(Host host)
        => Part<StackPanel>(host.Composer, "_addMenuList").Children.OfType<Button>().ToList();

    private static List<string?> AddMenuRowNames(Host host)
        => AddMenuRows(host).Select(Avalonia.Automation.AutomationProperties.GetName).ToList();

    private static void PressAddMenuRow(Host host, string name)
        => Click(AddMenuRows(host).Single(row => Avalonia.Automation.AutomationProperties.GetName(row) == name));

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Open(StrataComposerPicker picker)
    {
        picker.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<Border> Rows(StrataComposerPicker picker)
        => Part<StackPanel>(picker, "_list").Children.OfType<Border>()
            .Where(border => border.Classes.Contains("picker-row"))
            .ToList();

    private static List<string?> RowNames(StrataComposerPicker picker)
        => Rows(picker)
            .Select(row => row.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Name == "PickerItemName").Text)
            .ToList();

    private static void PressRow(StrataComposerPicker picker, string name)
    {
        var row = Rows(picker).Single(candidate =>
            candidate.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Name == "PickerItemName" && text.Text == name));
        row.RaiseEvent(Press(row));
        Dispatcher.UIThread.RunJobs();
    }

    private static Popup PopupOf(StrataComposerPicker picker) => Part<Popup>(picker, "_popup");

    private static List<ChipKind> AutoCompleteKinds(StrataChatComposer composer)
    {
        var entries = (System.Collections.IEnumerable)typeof(StrataChatComposer)
            .GetField("_autoCompleteEntries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(composer)!;
        return entries.Cast<(Border Control, StrataComposerChip Chip, ChipKind Kind)>().Select(entry => entry.Kind).ToList();
    }

    private static T Part<T>(object owner, string fieldName)
        => (T)owner.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!;

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
        public required Window Window { get; init; }
        public required ObservableCollection<object> Skills { get; init; }
        public required ObservableCollection<object> Chats { get; init; }
        public required RecordingCommand SkillRemoved { get; init; }
        public required RecordingCommand ChatsRequested { get; init; }

        public StrataComposerPicker SkillPicker => Picker("PART_SkillPicker");
        public StrataComposerPicker ChatPicker => Picker("PART_ChatPicker");
        public StrataConnectorPicker ConnectorPicker => Composer.GetVisualDescendants().OfType<StrataConnectorPicker>().Single();
        public StrataComposerPicker AgentPicker => Picker("PART_AgentPicker");
        public TextBox Input => Composer.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_Input");

        private StrataComposerPicker Picker(string name)
            => Composer.GetVisualDescendants().OfType<StrataComposerPicker>().Single(picker => picker.Name == name);

        public static Host Create(bool withChats = true, double width = 760, Control? beside = null)
        {
            var skills = new ObservableCollection<object>();
            var chats = new ObservableCollection<object>();
            var skillRemoved = new RecordingCommand();
            var chatsRequested = new RecordingCommand();
            var composer = new StrataChatComposer
            {
                AvailableSkills = new[]
                {
                    new StrataComposerChip("Code Helper", "💻", SecondaryText: "Writes and explains code", SourceLabel: "Lumi"),
                    new StrataComposerChip("Code Reviewer", "🔍", SecondaryText: "Reviews changes", SourceLabel: "Lumi"),
                    new StrataComposerChip("Deck Builder", "📊", SecondaryText: "Builds presentations", SourceLabel: "Plugin"),
                },
                SkillItems = skills,
                SkillRemovedCommand = skillRemoved,
                ChatItems = chats,
                ChatsRequestedCommand = chatsRequested,
            };
            if (withChats)
            {
                composer.AvailableChats = new[]
                {
                    new StrataComposerChip("Trip to Japan", "💬", SecondaryText: "Travel", Value: TripId, SourceLabel: "5m ago"),
                    Budget,
                };
            }

            var window = new Window
            {
                Width = width,
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
            window.Content = beside is null ? composer : new StackPanel { Children = { composer, beside } };
            window.Show();
            window.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            composer.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            return new Host
            {
                Composer = composer,
                Window = window,
                Skills = skills,
                Chats = chats,
                SkillRemoved = skillRemoved,
                ChatsRequested = chatsRequested,
            };
        }
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
