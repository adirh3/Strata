using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// The + menu: everything a message can bring in (files, other chats, skills, agents, connectors and a
/// project) in one place. The Skills and Connectors pickers beside it keep the two used most a click away.
/// </summary>
public partial class StrataChatComposer
{
    /// <summary>How many recent chats the + menu offers itself, ahead of "All chats".</summary>
    private const int AddMenuRecentChatCount = 3;

    private Button? _addButton;
    private Popup? _addPopup;
    private Border? _addPanel;
    private StackPanel? _addMenuList;
    private StrataComposerPicker? _agentPicker;
    private StrataConnectorPicker? _connectorPicker;
    private bool _addMenuOpenedFromKeyboard;

    private void ApplyAddMenuTemplate(TemplateAppliedEventArgs e)
    {
        if (_addButton is not null)
            _addButton.Click -= OnAddButtonClick;
        if (_addPopup is not null)
            _addPopup.PropertyChanged -= OnAddPopupPropertyChanged;
        _addPanel?.RemoveHandler(KeyDownEvent, OnAddMenuKeyDown);
        if (_agentPicker is not null)
        {
            _agentPicker.ItemToggled -= OnAgentPickerToggled;
            _agentPicker.DropDownClosed -= OnAddListClosed;
        }

        _addButton = e.NameScope.Find<Button>("PART_AddButton");
        _addPopup = e.NameScope.Find<Popup>("PART_AddPopup");
        _addPanel = e.NameScope.Find<Border>("PART_AddPanel");
        _addMenuList = e.NameScope.Find<StackPanel>("PART_AddMenuList");
        _agentPicker = e.NameScope.Find<StrataComposerPicker>("PART_AgentPicker");
        _connectorPicker = e.NameScope.Find<StrataConnectorPicker>("PART_ConnectorPicker");
        PseudoClasses.Set(":add-open", false);

        if (_addButton is not null)
            _addButton.Click += OnAddButtonClick;
        if (_addPopup is not null)
            _addPopup.PropertyChanged += OnAddPopupPropertyChanged;
        _addPanel?.AddHandler(KeyDownEvent, OnAddMenuKeyDown, RoutingStrategies.Tunnel);
        if (_agentPicker is not null)
        {
            _agentPicker.ItemToggled += OnAgentPickerToggled;
            _agentPicker.DropDownClosed += OnAddListClosed;
        }
    }

    /// <summary>Whether the + menu has anything to offer; without, the + button hides.</summary>
    private bool HasAddOptions() =>
        CanAttach
        || AvailableChats is not null
        || AvailableSkills is not null
        || HasAny(AvailableAgents)
        || HasAnyAvailableMcps()
        || McpBrowseCommand is not null
        || HasAny(AvailableProjects);

    private static bool HasAny(IEnumerable? items)
    {
        if (items is null)
            return false;
        foreach (var _ in items)
            return true;
        return false;
    }

    /// <summary>Whether the + menu is open.</summary>
    private bool IsAddMenuOpen => _addPopup?.IsOpen == true;

    private void OnAddButtonClick(object? sender, RoutedEventArgs e)
    {
        if (IsAddMenuOpen)
            CloseAddMenu();
        else
            OpenAddMenu();
    }

    /// <summary>Opens the + menu with the keyboard on its first row.</summary>
    private void OpenAddMenu()
    {
        if (_addPopup is null || _addMenuList is null || IsAddMenuOpen)
            return;

        // A menu over the list of matches would hide one or the other.
        CloseAutoComplete();
        _addMenuOpenedFromKeyboard = _addButton?.Classes.Contains(":focus-visible") == true;

        // The host may refresh its recent chats before any are listed.
        if (AvailableChats is not null)
            CommandHelper.Execute(ChatsRequestedCommand, null);

        RebuildAddMenu();
        _addPopup.IsOpen = true;

        var method = _addMenuOpenedFromKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsAddMenuOpen)
                GetAddMenuRows().FirstOrDefault()?.Focus(method);
        }, DispatcherPriority.Loaded);
    }

    private void CloseAddMenu()
    {
        if (_addPopup is { IsOpen: true })
            _addPopup.IsOpen = false;
    }

    // The + button follows the menu's open state, not the end of its closing fade.
    private void OnAddPopupPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Popup.IsOpenProperty)
            PseudoClasses.Set(":add-open", IsAddMenuOpen);
    }

    private List<Button> GetAddMenuRows()
        => _addMenuList?.Children.OfType<Button>().Where(row => row.IsEffectivelyVisible && row.IsEffectivelyEnabled).ToList()
           ?? [];

    private void OnAddMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsAddMenuOpen)
            return;

        if (e.Key == Key.Escape)
        {
            CloseAddMenu();
            if (_addMenuOpenedFromKeyboard)
                _addButton?.Focus(NavigationMethod.Tab);
            else
                FocusInput();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
            return;

        var rows = GetAddMenuRows();
        if (rows.Count == 0)
            return;

        var current = rows.FindIndex(row => row.IsFocused);
        var next = e.Key switch
        {
            Key.Home => 0,
            Key.End => rows.Count - 1,
            Key.Down => current < 0 ? 0 : (current + 1) % rows.Count,
            _ => current < 0 ? rows.Count - 1 : (current - 1 + rows.Count) % rows.Count,
        };
        rows[next].Focus(NavigationMethod.Directional);
        rows[next].BringIntoView();
        e.Handled = true;
    }

    // ── Rows ────────────────────────────────────────────────────────

    private void RebuildAddMenu()
    {
        if (_addMenuList is null)
            return;

        var rows = _addMenuList.Children;
        rows.Clear();

        if (CanAttach)
            rows.Add(CreateAddMenuRow(StrataIcons.Paperclip, AddFilesLabel, ChooseFiles));

        if (AvailableChats is not null)
        {
            var recent = GetRecentChatsToAdd();
            if (recent.Count > 0)
            {
                rows.Add(CreateAddMenuHeader(AddRecentChatsLabel, "#"));
                foreach (var chat in recent)
                    rows.Add(CreateRecentChatRow(chat));
            }

            rows.Add(CreateAddMenuRow(StrataIcons.Chats, AddAllChatsLabel, OpenAllChats,
                key: recent.Count > 0 ? null : "#", opensList: true));
        }

        var more = new List<Control>();
        if (AvailableSkills is not null)
            more.Add(CreateAddMenuRow(StrataIcons.Wand, AddSkillsLabel, OpenSkills, key: "/"));
        if (HasAny(AvailableAgents))
            more.Add(CreateAddMenuRow(StrataIcons.Person, AddAgentsLabel, ChooseAgent, meta: AgentName, key: "@"));
        if (HasAnyAvailableMcps() || McpBrowseCommand is not null)
            more.Add(CreateAddMenuRow(StrataIcons.Plug, AddConnectorsLabel, OpenConnectors, meta: GetConnectorsMeta(), opensList: true));
        if (HasAny(AvailableProjects))
            more.Add(CreateAddMenuRow(StrataIcons.Folder, AddProjectLabel, ChooseProject, meta: ProjectName, key: "$"));

        if (rows.Count > 0 && more.Count > 0)
        {
            var separator = new Border();
            separator.Classes.Add("add-menu-separator");
            rows.Add(separator);
        }

        rows.AddRange(more);
    }

    /// <summary>The most recent chats not already brought in, in the host's order.</summary>
    private List<StrataComposerChip> GetRecentChatsToAdd()
    {
        var result = new List<StrataComposerChip>();
        if (AvailableChats is null)
            return result;

        foreach (var item in AvailableChats)
        {
            var chip = AsChip(item);
            if (chip.Name.Length == 0 || IsAlreadyReferencedChat(chip))
                continue;
            result.Add(chip);
            if (result.Count == AddMenuRecentChatCount)
                break;
        }

        return result;
    }

    private string? GetConnectorsMeta()
    {
        if (_connectorPicker is null)
            return null;

        var (on, available) = _connectorPicker.GetCounts();
        if (available == 0)
            return null;
        return on == 0
            ? _connectorPicker.OffLabel
            : string.Format(CultureInfo.CurrentCulture, AddConnectorsCountFormat, on);
    }

    private Button CreateAddMenuRow(
        Geometry icon,
        string label,
        Action action,
        string? meta = null,
        string? key = null,
        bool opensList = false)
    {
        var glyph = new StrataLineIcon { Data = icon, Width = 18, Height = 18 };
        glyph.Classes.Add("add-menu-icon");
        var glyphWrap = new Border
        {
            Width = 26,
            Height = 26,
            Child = glyph,
        };
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;

        return CreateRow(glyphWrap, label, meta, key, opensList, action);
    }

    private Button CreateRecentChatRow(StrataComposerChip chat)
    {
        var glyph = new StrataLineIcon
        {
            Data = StrataIcons.Chat,
            Width = 14,
            Height = 14,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tile = new Border { Child = glyph };
        tile.Classes.Add("add-menu-tile");

        return CreateRow(tile, chat.Name, chat.SourceLabel, key: null, opensList: false, () => AddRecentChat(chat));
    }

    private Button CreateRow(Control glyph, string label, string? meta, string? key, bool opensList, Action action)
    {
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
        };
        layout.Children.Add(glyph);

        var text = new TextBlock { Text = BidiText.Isolate(this, label) };
        text.Classes.Add("add-menu-label");
        Grid.SetColumn(text, 1);
        layout.Children.Add(text);

        if (!string.IsNullOrWhiteSpace(meta))
        {
            var metaText = new TextBlock { Text = BidiText.Isolate(this, meta), MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis };
            metaText.Classes.Add("add-menu-meta");
            Grid.SetColumn(metaText, 2);
            layout.Children.Add(metaText);
        }

        Control? trailing = null;
        if (!string.IsNullOrEmpty(key))
        {
            trailing = CreateKeyHint(key);
        }
        else if (opensList)
        {
            var chevron = new StrataLineIcon { Data = StrataIcons.ChevronRight, Width = 14, Height = 14, StrokeThickness = 2 };
            chevron.Classes.Add("add-menu-chevron");
            trailing = chevron;
        }

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 3);
            layout.Children.Add(trailing);
        }

        // No tooltip: the rows are rebuilt each time the menu opens.
        var row = new Button { Content = layout };
        row.Classes.Add("subtle");
        row.Classes.Add("add-menu-row");
        AutomationProperties.SetName(row, string.IsNullOrWhiteSpace(meta) ? label : $"{label}, {meta}");
        if (!string.IsNullOrEmpty(key))
            AutomationProperties.SetAcceleratorKey(row, key);
        row.Click += (_, e) =>
        {
            e.Handled = true;
            CloseAddMenu();
            action();
        };
        return row;
    }

    private Control CreateAddMenuHeader(string label, string key)
    {
        var header = new TextBlock { Text = label };
        header.Classes.Add("add-menu-header");

        var hint = CreateKeyHint(key);
        Grid.SetColumn(hint, 1);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(10, 8, 10, 4),
        };
        layout.Children.Add(header);
        layout.Children.Add(hint);
        return layout;
    }

    /// <summary>The character that reaches the same list while typing.</summary>
    private static Border CreateKeyHint(string key)
    {
        var hint = new Border { Child = new TextBlock { Text = key } };
        hint.Classes.Add("add-menu-key");
        return hint;
    }

    // ── Actions ─────────────────────────────────────────────────────

    private void ChooseFiles()
    {
        RaiseEvent(new RoutedEventArgs(AttachRequestedEvent));
        CommandHelper.Execute(AttachCommand, AttachCommandParameter);
    }

    private void AddRecentChat(StrataComposerChip chat)
    {
        if (ChatItems is IList list && !IsAlreadyReferencedChat(chat))
            list.Add(chat);

        // A chat is brought in to talk about it: go straight back to writing.
        FocusInput();
    }

    private void OpenAllChats() => _chatPicker?.SetCurrentValue(StrataComposerPicker.IsDropDownOpenProperty, true);

    private void OpenSkills() => _skillPicker?.SetCurrentValue(StrataComposerPicker.IsDropDownOpenProperty, true);

    private void OpenConnectors() => _connectorPicker?.SetCurrentValue(StrataConnectorPicker.IsDropDownOpenProperty, true);

    /// <summary>Lists the agents, the one answering now checked first; without an agent list, an @ mention does.</summary>
    private void ChooseAgent()
    {
        if (_agentPicker is not null)
        {
            _agentPicker.SetCurrentValue(StrataComposerPicker.SelectedItemsProperty, CurrentAgentSelection());
            _agentPicker.SetCurrentValue(StrataComposerPicker.IsDropDownOpenProperty, true);
            return;
        }

        ShowMentionPopup();
        CommandHelper.Execute(MentionCommand, MentionCommandParameter);
    }

    /// <summary>The agent answering now, as the agent list knows it: the offered chip when there is one.</summary>
    private object[] CurrentAgentSelection()
    {
        if (string.IsNullOrWhiteSpace(AgentName))
            return [];

        if (AvailableAgents is not null)
        {
            foreach (var item in AvailableAgents)
            {
                if (item is StrataComposerChip chip && MatchesSelection(chip, AgentName, AgentValue))
                    return [chip];
            }
        }

        return [new StrataComposerChip(AgentName, string.IsNullOrEmpty(AgentGlyph) ? "◉" : AgentGlyph, Value: AgentValue)];
    }

    private void ChooseProject() => ShowTriggerPopup('$');

    private void OnAgentPickerToggled(object? sender, ComposerPickerToggledEventArgs e)
    {
        if (e.IsSelected)
        {
            // The same path as choosing an agent from the @ list.
            AgentValue = e.Chip.Value;
            AgentName = e.Chip.Name;
            AgentGlyph = e.Chip.Glyph;
        }
        else
        {
            // The same path as the agent chip's remove button.
            RaiseEvent(new RoutedEventArgs(AgentRemovedEvent));
            CommandHelper.Execute(AgentRemovedCommand, AgentRemovedCommandParameter);
        }

        FocusInput();
    }

    /// <summary>
    /// Returns the keyboard to writing when the Chats or Agents list closes with nowhere to go: their own
    /// pills are hidden, so a list cannot hand the keyboard back to its button. A list reports closing as
    /// it starts to fade, while its own search field may still hold the keyboard; that counts as nowhere.
    /// </summary>
    private void OnAddListClosed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is not Visual visual
                || !visual.IsEffectivelyVisible
                || !visual.IsAttachedToVisualTree()
                || (sender is ILogical list && focused is ILogical logical && list.IsLogicalAncestorOf(logical)))
                FocusInput();
        }, DispatcherPriority.Loaded);
    }
}
