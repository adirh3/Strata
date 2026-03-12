using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StrataTheme.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StrataDemo;

public partial class MainWindow : Window
{
    private readonly List<Control> _pages = new();
    private ItemsControl? _liveTranscript;
    private StrataChatComposer? _liveComposer;
    private StrataChatShell? _mainChatShell;
    private StrataConfidence? _liveConfRootCause;
    private StrataConfidence? _liveConfMitigation;
    private StrataConfidence? _liveConfImpact;
    private StrataPulse? _livePulse;
    private StrataBudgetChip? _liveBudget;
    private CancellationTokenSource? _generationCts;
    private StrataCanvas? _chatExperienceCanvas;
    private bool _isChatCanvasOpen;
    private ItemsControl? _perfTranscript;
    private StrataChatShell? _perfChatShell;
    private TextBlock? _perfStatusText;
    private TextBlock? _perfFirstPassText;
    private TextBlock? _perfSecondPassText;
    private TextBlock? _perfDeltaText;
    private Button? _perfSeedButton;
    private Button? _perfRunButton;
    private Button? _perfStopButton;
    private CancellationTokenSource? _perfRunCts;
    private bool _perfInitialized;
    private ChatPerformanceBenchmarkRunner? _perfRunner;

    public MainWindow()
    {
        InitializeComponent();

        SizeChanged += (_, args) => UpdateResponsiveClasses(args.NewSize.Width);
        Opened += (_, _) => UpdateResponsiveClasses(Bounds.Width);

        // Cache page references
        for (int i = 0; i <= 8; i++)
        {
            var page = this.FindControl<Control>($"Page{i}");
            if (page is not null)
                _pages.Add(page);
        }

        // Wire sidebar navigation
        var navList = this.FindControl<ListBox>("NavList");
        if (navList is not null)
            navList.SelectionChanged += OnNavSelectionChanged;

        // Wire theme toggles (sidebar)
        WireToggle("ThemeToggle", OnThemeToggleChanged);
        WireToggle("DensityToggle", OnDensityToggleChanged);

        // Wire theme toggles (settings page)
        WireToggle("ThemeToggle2", OnThemeToggleChanged);
        WireToggle("DensityToggle2", OnDensityToggleChanged);

        // Wire settings revert buttons
        WireSettingRevert("EmailNotifSetting", "EmailNotifCheck");
        WireSettingRevert("DiagnosticsSetting", "DiagnosticsToggle");

        // Show first page
        ShowPage(0);

        // Interactive chat page wiring
        _liveTranscript = this.FindControl<ItemsControl>("LiveTranscript");
        _liveComposer = this.FindControl<StrataChatComposer>("LiveComposer");
        _mainChatShell = this.FindControl<StrataChatShell>("MainChatShell");
        _liveConfRootCause = this.FindControl<StrataConfidence>("LiveConfRootCause");
        _liveConfMitigation = this.FindControl<StrataConfidence>("LiveConfMitigation");
        _liveConfImpact = this.FindControl<StrataConfidence>("LiveConfImpact");
        _livePulse = this.FindControl<StrataPulse>("LivePulse");
        _liveBudget = this.FindControl<StrataBudgetChip>("LiveBudget");
        _chatExperienceCanvas = this.FindControl<StrataCanvas>("ChatExperienceCanvas");
        if (_chatExperienceCanvas is not null)
            _chatExperienceCanvas.CloseRequested += OnChatCanvasCloseRequested;

        var populateButton = this.FindControl<Button>("PopulateChatButton");
        if (populateButton is not null)
            populateButton.Click += (_, _) => PopulateChatWithSampleConversation();

        if (_liveComposer is not null)
        {
            _liveComposer.SendRequested += OnLiveComposerSendRequested;
            _liveComposer.StopRequested += OnLiveComposerStopRequested;
            _liveComposer.AgentRemoved += (s, _) =>
            {
                if (s is StrataChatComposer c) c.AgentName = null;
            };
            _liveComposer.SkillRemoved += (_, e) =>
            {
                if (DataContext is MainViewModel vm && e.Item is StrataComposerChip chip)
                    vm.LiveAiSkills.Remove(chip);
            };
        }

        var demoComposer = this.FindControl<StrataChatComposer>("DemoComposer");
        if (demoComposer is not null)
        {
            demoComposer.AgentRemoved += (s, _) =>
            {
                if (s is StrataChatComposer c) c.AgentName = null;
            };
            demoComposer.SkillRemoved += (s, e) =>
            {
                if (DataContext is MainViewModel vm && e.Item is StrataComposerChip chip)
                    vm.AiSkills.Remove(chip);
            };
        }

        // Chat performance page wiring
        _perfTranscript = this.FindControl<ItemsControl>("PerfTranscript");

        // Wire flyout icon picker to update button preview and close flyout
        var flyoutPicker = this.FindControl<StrataIconPicker>("FlyoutIconPicker");
        var flyoutBtn = this.FindControl<Button>("IconPickerFlyoutBtn");
        var flyoutPreview = this.FindControl<TextBlock>("FlyoutIconPreview");
        if (flyoutPicker is not null && flyoutBtn is not null && flyoutPreview is not null)
        {
            flyoutPicker.IconSelected += (_, _) =>
            {
                flyoutPreview.Text = flyoutPicker.SelectedIcon ?? "🎯";
                flyoutBtn.Flyout?.Hide();
            };
        }
        _perfChatShell = this.FindControl<StrataChatShell>("PerfChatShell");
        _perfStatusText = this.FindControl<TextBlock>("PerfStatusText");
        _perfFirstPassText = this.FindControl<TextBlock>("PerfFirstPassText");
        _perfSecondPassText = this.FindControl<TextBlock>("PerfSecondPassText");
        _perfDeltaText = this.FindControl<TextBlock>("PerfDeltaText");

        _perfSeedButton = this.FindControl<Button>("PerfSeedButton");
        if (_perfSeedButton is not null)
            _perfSeedButton.Click += OnPerfSeedRequested;

        _perfRunButton = this.FindControl<Button>("PerfRunButton");
        if (_perfRunButton is not null)
            _perfRunButton.Click += OnPerfRunRequested;

        _perfStopButton = this.FindControl<Button>("PerfStopButton");
        if (_perfStopButton is not null)
        {
            _perfStopButton.Click += OnPerfStopRequested;
            _perfStopButton.IsEnabled = false;
        }

        if (_perfChatShell is not null && _perfTranscript is not null)
            _perfRunner = new ChatPerformanceBenchmarkRunner(this, _perfChatShell, _perfTranscript);
    }

    private void UpdateResponsiveClasses(double width)
    {
        var compact = width < 1360;
        var narrow = width < 1120;

        SetWindowClass("compact", compact);
        SetWindowClass("narrow", narrow);

        ApplyPageResponsiveLayouts(compact, narrow);
        ApplyChatResponsiveLayout(compact, narrow);
    }

    private void ApplyPageResponsiveLayouts(bool compact, bool narrow)
    {
        ConfigureThreeColumnGrid("DashboardCardsGrid", compact, narrow, 16, 12, 12);

        ConfigureTwoColumnGrid("FormTextGrid", compact, narrow, 24, 12, 12);
        ConfigureTwoColumnGrid("FormSelectionGrid", compact, narrow, 24, 12, 12);
        ConfigureTwoColumnGrid("FormChecksGrid", compact, narrow, 16, 12, 12);
        ConfigureTwoColumnGrid("FormToggleGrid", compact, narrow, 16, 12, 12);

        ConfigureTwoColumnGrid("ComponentsPairGrid", compact, narrow, 16, 12, 12);

        ConfigureTwoColumnGrid("AiForkGrid", compact, narrow, 16, 12, 12);
        ConfigureTwoColumnGrid("AiTraceGrid", compact, narrow, 16, 12, 12);
        ConfigureTwoColumnGrid("AiStepGrid", compact, narrow, 16, 12, 12);
        ConfigureTwoColumnGrid("AiMicroGrid", compact, narrow, 24, 12, 12);
    }

    private void ConfigureTwoColumnGrid(string name, bool compact, bool narrow, int wideGap, int compactGap, int stackGap)
    {
        var grid = this.FindControl<Grid>(name);
        if (grid is null)
            return;

        var children = grid.Children.OfType<Control>().ToList();
        if (children.Count == 0)
            return;

        if (narrow)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions(BuildRowDefinitions(children.Count, stackGap));

            for (var i = 0; i < children.Count; i++)
            {
                Grid.SetColumn(children[i], 0);
                Grid.SetRow(children[i], i * 2);
            }

            return;
        }

        var gap = compact ? compactGap : wideGap;
        grid.ColumnDefinitions = new ColumnDefinitions($"*,{gap},*");
        grid.RowDefinitions = new RowDefinitions("Auto");

        for (var i = 0; i < children.Count; i++)
        {
            Grid.SetColumn(children[i], i * 2);
            Grid.SetRow(children[i], 0);
        }
    }

    private void ConfigureThreeColumnGrid(string name, bool compact, bool narrow, int wideGap, int compactGap, int stackGap)
    {
        var grid = this.FindControl<Grid>(name);
        if (grid is null)
            return;

        var children = grid.Children.OfType<Control>().ToList();
        if (children.Count == 0)
            return;

        if (narrow)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions(BuildRowDefinitions(children.Count, stackGap));

            for (var i = 0; i < children.Count; i++)
            {
                Grid.SetColumn(children[i], 0);
                Grid.SetRow(children[i], i * 2);
            }

            return;
        }

        var gap = compact ? compactGap : wideGap;
        grid.ColumnDefinitions = new ColumnDefinitions($"*,{gap},*,{gap},*");
        grid.RowDefinitions = new RowDefinitions("Auto");

        for (var i = 0; i < children.Count; i++)
        {
            Grid.SetColumn(children[i], i * 2);
            Grid.SetRow(children[i], 0);
        }
    }

    private static string BuildRowDefinitions(int itemCount, int gap)
    {
        if (itemCount <= 0)
            return "Auto";

        var sb = new StringBuilder("Auto");
        for (var i = 1; i < itemCount; i++)
        {
            sb.Append(',').Append(gap).Append(",Auto");
        }

        return sb.ToString();
    }

    private void ApplyChatResponsiveLayout(bool compact, bool narrow)
    {
        var chatLayout = this.FindControl<Grid>("ChatLayout");
        var chatMainCard = this.FindControl<Border>("ChatMainCard");
        var chatSideHost = this.FindControl<ScrollViewer>("ChatSideHost");
        var mainChatShell = this.FindControl<StrataChatShell>("MainChatShell");

        if (chatLayout is null || chatMainCard is null || chatSideHost is null || mainChatShell is null)
            return;

        // Position canvas when it's open
        if (_isChatCanvasOpen && _chatExperienceCanvas is not null)
        {
            chatSideHost.IsVisible = false;
            if (narrow)
            {
                Grid.SetColumn(_chatExperienceCanvas, 0);
                Grid.SetRow(_chatExperienceCanvas, 2);
            }
            else
            {
                Grid.SetColumn(_chatExperienceCanvas, 2);
                Grid.SetRow(_chatExperienceCanvas, 0);
            }
        }
        else
        {
            chatSideHost.IsVisible = true;
        }

        if (narrow)
        {
            chatLayout.ColumnDefinitions = new ColumnDefinitions("*");
            chatLayout.RowDefinitions = new RowDefinitions("*,12,*");

            Grid.SetColumn(chatMainCard, 0);
            Grid.SetRow(chatMainCard, 0);

            Grid.SetColumn(chatSideHost, 0);
            Grid.SetRow(chatSideHost, 2);

            mainChatShell.MinHeight = 0;
            return;
        }

        chatLayout.RowDefinitions = new RowDefinitions("*");

        Grid.SetRow(chatMainCard, 0);
        Grid.SetRow(chatSideHost, 0);

        Grid.SetColumn(chatMainCard, 0);
        Grid.SetColumn(chatSideHost, 2);

        chatLayout.ColumnDefinitions = compact
            ? new ColumnDefinitions("2.2*,12,1.2*")
            : new ColumnDefinitions("2.6*,16,1.4*");

        mainChatShell.MinHeight = 0;
    }

    private void SetWindowClass(string className, bool enabled)
    {
        if (enabled)
        {
            if (!Classes.Contains(className))
                Classes.Add(className);
        }
        else
        {
            Classes.Remove(className);
        }
    }

    private void WireToggle(string name, EventHandler<RoutedEventArgs> handler)
    {
        var toggle = this.FindControl<ToggleSwitch>(name);
        if (toggle is not null)
            toggle.IsCheckedChanged += handler;
    }

    private void WireSettingRevert(string settingName, string controlName)
    {
        var setting = this.FindControl<StrataSetting>(settingName);
        if (setting is null) return;

        setting.Reverted += (_, _) =>
        {
            var control = this.FindControl<Control>(controlName);
            switch (control)
            {
                case ToggleSwitch toggle:
                    toggle.IsChecked = false;
                    break;
                case CheckBox check:
                    check.IsChecked = false;
                    break;
            }
        };
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb)
            ShowPage(lb.SelectedIndex);
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].IsVisible = i == index;

        // Auto-focus the composer when switching to the Chat Experience page
        if (index == 7)
            _liveComposer?.FocusInput();

        if (index == 8)
            EnsurePerformanceDemoSeeded(forceReset: false);
    }

    private void OnThemeToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null) return;
        var toggle = sender as ToggleSwitch;
        Application.Current.RequestedThemeVariant =
            toggle?.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;

        // Keep all theme toggles in sync
        SyncToggle("ThemeToggle", toggle?.IsChecked == true);
        SyncToggle("ThemeToggle2", toggle?.IsChecked == true);
    }

    private void OnDensityToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null) return;

        var toggle = sender as ToggleSwitch;
        var isCompact = toggle?.IsChecked == true;

        var densityUri = isCompact
            ? new System.Uri("avares://StrataTheme/Tokens/Density.Compact.axaml")
            : new System.Uri("avares://StrataTheme/Tokens/Density.Comfortable.axaml");

        var app = Application.Current;
        var dict = (Avalonia.Controls.ResourceDictionary)AvaloniaXamlLoader.Load(densityUri);

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(dict);

        // Keep all density toggles in sync
        SyncToggle("DensityToggle", isCompact);
        SyncToggle("DensityToggle2", isCompact);
    }

    private void SyncToggle(string name, bool value)
    {
        var toggle = this.FindControl<ToggleSwitch>(name);
        if (toggle is not null && toggle.IsChecked != value)
            toggle.IsChecked = value;
    }

    private async void OnLiveComposerSendRequested(object? sender, RoutedEventArgs e)
    {
        if (_liveComposer is null || _liveTranscript is null)
            return;

        var prompt = _liveComposer.PromptText?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        CancelGeneration();
        AddUserMessage(prompt);
        _liveComposer.PromptText = string.Empty;
        _liveComposer.IsBusy = true;
        _mainChatShell?.ResetAutoScroll();

        _generationCts = new CancellationTokenSource();
        var token = _generationCts.Token;

        try
        {
            await RunMockAssistantAsync(prompt, token);
        }
        catch (OperationCanceledException)
        {
            AddSystemNote("Generation stopped.");
        }
        finally
        {
            if (_liveComposer is not null)
                _liveComposer.IsBusy = false;

            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    private void OnLiveComposerStopRequested(object? sender, RoutedEventArgs e)
    {
        CancelGeneration();
        if (_liveComposer is not null)
            _liveComposer.IsBusy = false;
    }

    private void CancelGeneration()
    {
        if (_generationCts is not null && !_generationCts.IsCancellationRequested)
            _generationCts.Cancel();
    }

    private async Task RunMockAssistantAsync(string prompt, CancellationToken token)
    {
        if (_liveTranscript is null)
            return;

        var rng = new Random(prompt.GetHashCode(StringComparison.OrdinalIgnoreCase));

        if (_livePulse is not null)
            _livePulse.Rate = 10 + rng.NextDouble() * 18;

        if (_liveBudget is not null)
            _liveBudget.UsedTokens += 120 + rng.Next(60, 160);

        UpdateConfidenceForPrompt(prompt, rng);

        var thinkContent = new StackPanel { Spacing = 4 };
        thinkContent.Children.Add(new TextBlock
        {
            Text = "Intent detected: summarize + actionable guidance.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ResolveBrush("Brush.TextSecondary", Brushes.Gray)
        });
        thinkContent.Children.Add(new TextBlock
        {
            Text = "Plan: produce concise answer, then include practical next steps.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ResolveBrush("Brush.TextSecondary", Brushes.Gray)
        });
        thinkContent.Children.Add(new TextBlock
        {
            Text = "Grounding mode: mock telemetry and runbook evidence.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ResolveBrush("Brush.TextSecondary", Brushes.Gray)
        });

        var think = new StrataThink
        {
            Label = "Thinking…",
            IsActive = true,
            IsExpanded = true,
            Content = thinkContent
        };

        var typing = new StrataTypingIndicator
        {
            IsActive = true,
            Label = "Composing response…"
        };

        _liveTranscript.Items.Add(think);
        _liveTranscript.Items.Add(typing);
        _mainChatShell?.ScrollToEnd();

        // Clear suggestions after first use
        if (_liveComposer is not null)
        {
            _liveComposer.SuggestionA = string.Empty;
            _liveComposer.SuggestionB = string.Empty;
            _liveComposer.SuggestionC = string.Empty;
        }

        var toolBatch = await AddParallelToolCallsAsync(rng, token);

        await Task.Delay(700, token);

        var fullMarkdown = BuildMockMarkdown(prompt);
        var streamingMd = new StrataMarkdown { IsInline = true, Markdown = "" };

        var assistantMessage = new StrataChatMessage
        {
            Role = StrataChatRole.Assistant,
            Author = "Strata",
            Timestamp = DateTime.Now.ToString("HH:mm"),
            StatusText = "streaming",
            IsStreaming = true,
            IsEditable = false,
            Content = streamingMd
        };

        _liveTranscript.Items.Add(assistantMessage);
        _mainChatShell?.ScrollToEnd();

        // Stream markdown token-by-token. Split on spaces but also break on
        // embedded newlines so table rows / headings arrive as distinct tokens
        // with visible delay between lines.
        var tokens = TokenizeForStreaming(fullMarkdown);
        var accumulated = new System.Text.StringBuilder(fullMarkdown.Length);
        const int batchSize = 6;

        for (var i = 0; i < tokens.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var tok = tokens[i];
            if (tok == "\n")
                accumulated.Append('\n');
            else
            {
                if (accumulated.Length > 0 && accumulated[^1] != '\n')
                    accumulated.Append(' ');
                accumulated.Append(tok);
            }

            // Push to markdown renderer every batch
            if (i % batchSize == batchSize - 1 || i == tokens.Count - 1)
            {
                streamingMd.Markdown = accumulated.ToString();
                _mainChatShell?.ScrollToEnd();
            }

            if (i % 6 == 0 && _livePulse is not null)
            {
                _livePulse.Rate = 12 + rng.NextDouble() * 22;
                _livePulse.Push(0.25 + rng.NextDouble() * 0.75);
            }

            if (i % 12 == 0 && _liveBudget is not null)
                _liveBudget.UsedTokens += 22 + rng.Next(10, 35);

            await Task.Delay(18, token);
        }

        assistantMessage.IsStreaming = false;
        assistantMessage.StatusText = "Grounded · 2 references";
        streamingMd.Markdown = fullMarkdown;

        think.IsActive = false;
        think.IsExpanded = false;
        think.Label = "Thought process";

        _liveTranscript.Items.Remove(typing);

        await CompleteToolCallsAsync(toolBatch.Calls, rng, token);
        CollapseToolCalls(toolBatch.Calls);
        if (toolBatch.Message is not null)
            toolBatch.Message.StatusText = "4 calls · completed";

        _mainChatShell?.ScrollToEnd();
        // Open canvas for relevant prompts
        var pl = prompt.ToLowerInvariant();
        if (pl.Contains("checklist") || pl.Contains("rollout") || pl.Contains("plan"))
            ShowChatCanvas("Rollout Checklist", "IR-4471 \u00b7 Staged", BuildChecklistCanvasContent(), true);
        else if (pl.Contains("code") || pl.Contains("analyze"))
            ShowChatCanvas("Generated Code", "Python \u00b7 incident_analyzer.py", BuildCodeCanvasContent(), true);
        else if (pl.Contains("summary") || pl.Contains("summarize") || pl.Contains("incident"))
            ShowChatCanvas("Incident Summary", "IR-4471 \u00b7 Auto-generated", BuildSummaryCanvasContent(), false);    }

    private async Task<(List<StrataAiToolCall> Calls, StrataChatMessage? Message)> AddParallelToolCallsAsync(Random rng, CancellationToken token)
    {
        if (_liveTranscript is null)
            return (new List<StrataAiToolCall>(), null);

        var calls = new List<StrataAiToolCall>
        {
            new()
            {
                ToolName = "monitor.query",
                Status = StrataAiToolCallStatus.InProgress,
                DurationMs = 0,
                InputParameters = "{ query: p95, window: 30m }",
                MoreInfo = "Scanning service latency and error envelope.",
                IsExpanded = false
            },
            new()
            {
                ToolName = "runbook.fetch",
                Status = StrataAiToolCallStatus.InProgress,
                DurationMs = 0,
                InputParameters = "{ id: autoscale.md }",
                MoreInfo = "Retrieving staged rollout guidance and rollback gates.",
                IsExpanded = false
            },
            new()
            {
                ToolName = "risk.score",
                Status = StrataAiToolCallStatus.InProgress,
                DurationMs = 0,
                InputParameters = "{ plan: mitigation-v3 }",
                MoreInfo = "Estimating blast radius and confidence score.",
                IsExpanded = false
            },
            new()
            {
                ToolName = "status.draft",
                Status = StrataAiToolCallStatus.InProgress,
                DurationMs = 0,
                InputParameters = "{ audience: incident-room }",
                MoreInfo = "Preparing concise stakeholder update draft.",
                IsExpanded = false
            }
        };

        var toolsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,10,*"),
            RowDefinitions = new RowDefinitions("Auto,8,Auto")
        };

        toolsGrid.Children.Add(calls[0]);
        Grid.SetColumn(calls[0], 0);
        Grid.SetRow(calls[0], 0);

        toolsGrid.Children.Add(calls[1]);
        Grid.SetColumn(calls[1], 2);
        Grid.SetRow(calls[1], 0);

        toolsGrid.Children.Add(calls[2]);
        Grid.SetColumn(calls[2], 0);
        Grid.SetRow(calls[2], 2);

        toolsGrid.Children.Add(calls[3]);
        Grid.SetColumn(calls[3], 2);
        Grid.SetRow(calls[3], 2);

        var toolsMessage = new StrataChatMessage
        {
            Role = StrataChatRole.Tool,
            Author = "tool.parallel",
            Timestamp = DateTime.Now.ToString("HH:mm"),
            StatusText = "running",
            IsEditable = false,
            Content = toolsGrid
        };

        _liveTranscript.Items.Add(toolsMessage);
        _mainChatShell?.ScrollToEnd();

        if (_livePulse is not null)
            _livePulse.Rate = 14 + rng.NextDouble() * 14;

        if (_liveBudget is not null)
            _liveBudget.UsedTokens += 90 + rng.Next(30, 80);

        await Task.Delay(320, token);
        return (calls, toolsMessage);
    }

    private async Task CompleteToolCallsAsync(List<StrataAiToolCall> calls, Random rng, CancellationToken token)
    {
        if (calls.Count == 0)
            return;

        var order = new[] { 1, 0, 3, 2 };
        for (var i = 0; i < order.Length; i++)
        {
            token.ThrowIfCancellationRequested();

            var call = calls[order[i]];
            call.Status = StrataAiToolCallStatus.Completed;
            call.DurationMs = 80 + rng.Next(40, 260);
            call.IsExpanded = false;

            if (_liveBudget is not null)
                _liveBudget.UsedTokens += 28 + rng.Next(8, 24);

            if (_livePulse is not null)
            {
                _livePulse.Rate = 10 + rng.NextDouble() * 16;
                _livePulse.Push(0.45 + rng.NextDouble() * 0.5);
            }

            await Task.Delay(180, token);
        }
    }

    private static void CollapseToolCalls(IEnumerable<StrataAiToolCall> calls)
    {
        foreach (var call in calls)
            call.IsExpanded = false;
    }

    private void UpdateConfidenceForPrompt(string prompt, Random rng)
    {
        var lower = prompt.ToLowerInvariant();
        var root = 74 + rng.NextDouble() * 20;
        var plan = 70 + rng.NextDouble() * 22;
        var impact = 62 + rng.NextDouble() * 26;

        if (lower.Contains("incident") || lower.Contains("root cause"))
            root = 84 + rng.NextDouble() * 12;

        if (lower.Contains("plan") || lower.Contains("rollout") || lower.Contains("checklist"))
            plan = 82 + rng.NextDouble() * 12;

        if (lower.Contains("customer") || lower.Contains("impact") || lower.Contains("stakeholder"))
            impact = 80 + rng.NextDouble() * 14;

        if (_liveConfRootCause is not null) _liveConfRootCause.Confidence = Math.Clamp(root, 0, 100);
        if (_liveConfMitigation is not null) _liveConfMitigation.Confidence = Math.Clamp(plan, 0, 100);
        if (_liveConfImpact is not null) _liveConfImpact.Confidence = Math.Clamp(impact, 0, 100);
    }

    private string BuildMockMarkdown(string prompt)
    {
        var p = prompt.ToLowerInvariant();

        const string refs = "\n\n### References\n- [incidents/IR-4471.md](incidents/IR-4471.md)\n- [runbooks/infra/autoscale.md](runbooks/infra/autoscale.md)";
        const string inlineCites = " [1](incidents/IR-4471.md) [2](runbooks/infra/autoscale.md)";

        if (p.Contains("summary") || p.Contains("incident"))
        {
            return "## Incident summary\nThe latency spike was driven by allocation bursts in serializer hot paths, amplifying GC pauses under peak load" + inlineCites + ".\n\n| Metric | Before | After | Delta |\n| --- | --- | --- | --- |\n| p95 latency | 120 ms | 460 ms | +340 ms |\n| GC pause | 18 ms | 97 ms | +79 ms |\n| Error rate | 0.02% | 1.8% | +1.78% |\n| Alloc/sec | 1.2 M | 4.7 M | +3.5 M |\n\n```csharp\npublic static bool IsSloHealthy(double p95Ms, double gcPauseMs)\n{\n    return p95Ms <= 250 && gcPauseMs <= 80;\n}\n```\n\n- Roll out in stages (10% \u2192 50% \u2192 100%)\n- Roll back immediately when thresholds are breached" + refs;
        }

        if (p.Contains("email") || p.Contains("update") || p.Contains("stakeholder"))
        {
            return "## Stakeholder update\nWe identified the likely root cause and prepared a low-risk staged mitigation rollout with explicit rollback gates" + inlineCites + ".\n\n```csharp\nvar rolloutPlan = new[] { 10, 50, 100 };\nforeach (var stage in rolloutPlan)\n{\n    Console.WriteLine($\"Deploying {stage}%\");\n}\n```\n\nCustomer impact is expected to be low, with monitoring and checkpoints at every stage." + refs;
        }

        if (p.Contains("checklist") || p.Contains("plan") || p.Contains("rollout"))
        {
            return "## Rollout checklist\n- Define SLO guardrails\n- Enable feature flag at 10%\n- Observe for 30 minutes\n- Expand to 50% and validate\n- Complete 100% rollout\n\n```csharp\nif (!IsSloHealthy(p95Ms, gcPauseMs))\n{\n    TriggerRollback();\n}\n```" + refs;
        }

        return "## Response\nI can help with that. I will break this down into summary, root cause, and next actions with measurable gates and fallback steps" + inlineCites + ".\n\n```csharp\npublic sealed class Guardrail\n{\n    public double P95Ms { get; init; }\n    public double GcPauseMs { get; init; }\n\n    public bool IsHealthy() => P95Ms <= 250 && GcPauseMs <= 80;\n}\n```" + refs;
    }

    /// <summary>
    /// Splits markdown into streaming tokens: words separated by spaces,
    /// with embedded newlines extracted as individual "\n" tokens so that
    /// table rows and headings arrive line-by-line with visible delay.
    /// </summary>
    private static List<string> TokenizeForStreaming(string markdown)
    {
        var result = new List<string>(markdown.Length / 4);
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            if (li > 0)
                result.Add("\n");

            var words = lines[li].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
                result.Add(w);
        }
        return result;
    }

    private static string StripMarkdown(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var inCode = false;

        foreach (var raw in lines)
        {
            var line = raw;

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode)
                continue;

            if (line.StartsWith("#", StringComparison.Ordinal))
                line = line.TrimStart('#', ' ');
            else if (line.StartsWith("- ", StringComparison.Ordinal))
                line = line[2..];

            if (line.Length > 0)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(line);
            }
        }

        return sb.ToString();
    }

    private void AddUserMessage(string text)
    {
        if (_liveTranscript is null)
            return;

        var user = new StrataChatMessage
        {
            Role = StrataChatRole.User,
            Author = "You",
            Timestamp = DateTime.Now.ToString("HH:mm"),
            StatusText = "sent",
            IsEditable = true,
            Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
        };

        _liveTranscript.Items.Add(user);
        _mainChatShell?.ScrollToEnd();
    }

    private void AddSystemNote(string text)
    {
        if (_liveTranscript is null)
            return;

        var note = new StrataChatMessage
        {
            Role = StrataChatRole.System,
            Author = "System",
            Timestamp = DateTime.Now.ToString("HH:mm"),
            IsEditable = false,
            Content = new SelectableTextBlock
            {
                Text = text,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap
            }
        };

        _liveTranscript.Items.Add(note);
        _mainChatShell?.ScrollToEnd();
    }

    private void PopulateChatWithSampleConversation()
    {
        if (_liveTranscript is null)
            return;

        _liveTranscript.Items.Clear();

        var baseTime = DateTime.Now.AddMinutes(-47);
        int msgIdx = 0;

        void AddUser(string text)
        {
            var ts = baseTime.AddMinutes(msgIdx * 2);
            _liveTranscript.Items.Add(new StrataChatMessage
            {
                Role = StrataChatRole.User,
                Author = "You",
                Timestamp = ts.ToString("HH:mm"),
                StatusText = "sent",
                IsEditable = true,
                Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            });
            msgIdx++;
        }

        void AddAssistant(string markdown, string? status = null)
        {
            var ts = baseTime.AddMinutes(msgIdx * 2);
            _liveTranscript.Items.Add(new StrataChatMessage
            {
                Role = StrataChatRole.Assistant,
                Author = "Strata",
                Timestamp = ts.ToString("HH:mm"),
                StatusText = status ?? "Grounded · 2 references",
                IsEditable = false,
                Content = new StrataMarkdown { IsInline = true, Markdown = markdown }
            });
            msgIdx++;
        }

        void AddSystem(string text)
        {
            var ts = baseTime.AddMinutes(msgIdx * 2);
            _liveTranscript.Items.Add(new StrataChatMessage
            {
                Role = StrataChatRole.System,
                Author = "System",
                Timestamp = ts.ToString("HH:mm"),
                IsEditable = false,
                Content = new SelectableTextBlock
                {
                    Text = text,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                }
            });
            msgIdx++;
        }

        void AddTool(string text, string? status = null)
        {
            var ts = baseTime.AddMinutes(msgIdx * 2);
            _liveTranscript.Items.Add(new StrataChatMessage
            {
                Role = StrataChatRole.Tool,
                Author = "Tool",
                Timestamp = ts.ToString("HH:mm"),
                StatusText = status ?? "completed",
                IsEditable = false,
                Content = new StrataMarkdown { IsInline = true, Markdown = text }
            });
            msgIdx++;
        }

        // ── Turn 1: Opening question ──
        AddUser("We're seeing elevated latency on the checkout service since this morning. Can you pull up the latest metrics?");

        AddAssistant(
            "## Checkout Service — Current State\n\n" +
            "I've queried the monitoring stack for the last 6 hours. Here's the summary:\n\n" +
            "| Metric | 6h Ago | Now | Delta |\n" +
            "| --- | --- | --- | --- |\n" +
            "| p50 latency | 42 ms | 68 ms | +26 ms |\n" +
            "| p95 latency | 120 ms | 460 ms | +340 ms |\n" +
            "| p99 latency | 310 ms | 1,240 ms | +930 ms |\n" +
            "| Error rate | 0.02% | 1.8% | +1.78% |\n" +
            "| Requests/sec | 14,200 | 13,800 | −400 |\n\n" +
            "The spike correlates with a deployment at **08:14 UTC** (`checkout-v2.17.3`). The error rate increase " +
            "is heavily concentrated on the `/api/checkout/confirm` endpoint.\n\n" +
            "### References\n" +
            "- [incidents/IR-4471.md](incidents/IR-4471.md)\n" +
            "- [dashboards/checkout-latency.json](dashboards/checkout-latency.json)");

        // ── Turn 2: Follow-up ──
        AddUser("What changed in that deployment?");

        AddAssistant(
            "## Changes in `checkout-v2.17.3`\n\n" +
            "The release included 3 PRs:\n\n" +
            "1. **PR #1842** — Switched payment serializer from `Newtonsoft.Json` to `System.Text.Json`\n" +
            "2. **PR #1847** — Added retry logic for inventory reservation calls\n" +
            "3. **PR #1850** — Updated TLS cert rotation schedule\n\n" +
            "PR #1842 is the most likely culprit. The new serializer path allocates heavily for polymorphic " +
            "types, which we see reflected in the GC pause data:\n\n" +
            "```csharp\n" +
            "// Hot path in PaymentSerializer — causes boxing allocations\n" +
            "public string Serialize<T>(T value)\n" +
            "{\n" +
            "    // This allocates a new JsonSerializerOptions on every call\n" +
            "    var options = new JsonSerializerOptions\n" +
            "    {\n" +
            "        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,\n" +
            "        WriteIndented = false\n" +
            "    };\n" +
            "    return JsonSerializer.Serialize(value, options);\n" +
            "}\n" +
            "```\n\n" +
            "The fix is to cache `JsonSerializerOptions` as a static field.",
            "3 sources");

        // ── Turn 3: Fix request ──
        AddUser("Can you show me the corrected version?");

        AddAssistant(
            "## Corrected Serializer\n\n" +
            "Here's the allocation-free version with cached options:\n\n" +
            "```csharp\n" +
            "public sealed class PaymentSerializer\n" +
            "{\n" +
            "    private static readonly JsonSerializerOptions CachedOptions = new()\n" +
            "    {\n" +
            "        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,\n" +
            "        WriteIndented = false,\n" +
            "        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull\n" +
            "    };\n" +
            "\n" +
            "    public string Serialize<T>(T value)\n" +
            "        => JsonSerializer.Serialize(value, CachedOptions);\n" +
            "\n" +
            "    public T? Deserialize<T>(string json)\n" +
            "        => JsonSerializer.Deserialize<T>(json, CachedOptions);\n" +
            "}\n" +
            "```\n\n" +
            "This eliminates per-call allocations and should reduce GC pressure significantly.\n\n" +
            "You should also add a benchmark to prevent regressions:\n\n" +
            "```csharp\n" +
            "[MemoryDiagnoser]\n" +
            "public class SerializerBenchmarks\n" +
            "{\n" +
            "    private readonly PaymentSerializer _serializer = new();\n" +
            "    private readonly PaymentRequest _request = new()\n" +
            "    {\n" +
            "        OrderId = \"ORD-123456\",\n" +
            "        Amount = 99.99m,\n" +
            "        Currency = \"USD\"\n" +
            "    };\n" +
            "\n" +
            "    [Benchmark]\n" +
            "    public string SerializePayment()\n" +
            "        => _serializer.Serialize(_request);\n" +
            "}\n" +
            "```",
            "Grounded · 1 reference");

        // ── Turn 4: Broader question ──
        AddUser("What's your recommended rollout plan for this fix?");

        AddAssistant(
            "## Staged Rollout Plan\n\n" +
            "Given that this is a serialization hot-path change, I recommend a cautious staged rollout " +
            "with explicit SLO gates at each stage.\n\n" +
            "### Stage 1 — Canary (10%)\n" +
            "- Deploy to canary ring only\n" +
            "- Monitor for **30 minutes**\n" +
            "- Gate: p95 ≤ 200ms, error rate ≤ 0.1%\n\n" +
            "### Stage 2 — Expansion (50%)\n" +
            "- Expand to half of production fleet\n" +
            "- Monitor for **1 hour**\n" +
            "- Gate: p99 ≤ 500ms, GC pause ≤ 40ms\n\n" +
            "### Stage 3 — Full rollout (100%)\n" +
            "- Complete rollout to all regions\n" +
            "- Keep rollback automation active for 24 hours\n\n" +
            "### Rollback Trigger\n\n" +
            "```csharp\n" +
            "public static bool ShouldRollback(ServiceMetrics metrics)\n" +
            "{\n" +
            "    return metrics.P95LatencyMs > 250\n" +
            "        || metrics.GcPauseMs > 80\n" +
            "        || metrics.ErrorRate > 0.005;\n" +
            "}\n" +
            "```\n\n" +
            "| Stage | Traffic | Duration | Rollback Threshold |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Canary | 10% | 30 min | p95 > 200ms |\n" +
            "| Expansion | 50% | 1 hour | p99 > 500ms |\n" +
            "| Full | 100% | 24h soak | error rate > 0.5% |\n");

        // ── System note ──
        AddSystem("Agent context refreshed — incident IR-4471 linked.");

        // ── Turn 5: Performance comparison ──
        AddUser("Show me a before/after comparison of memory allocations.");

        AddAssistant(
            "## Memory Allocation Comparison\n\n" +
            "I ran the benchmark suite against both versions. Results:\n\n" +
            "| Benchmark | Before (v2.17.3) | After (fix) | Improvement |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Serialize (alloc) | 4,720 B/op | 0 B/op | **100%** |\n" +
            "| Serialize (time) | 2.4 μs | 1.1 μs | **54%** |\n" +
            "| Deserialize (alloc) | 3,200 B/op | 0 B/op | **100%** |\n" +
            "| Deserialize (time) | 3.1 μs | 1.8 μs | **42%** |\n" +
            "| GC Gen0 collections | 847/sec | 12/sec | **98.6%** |\n\n" +
            "The GC pressure reduction is the primary driver of the latency improvement. " +
            "With cached options, we eliminate **all** per-call managed allocations in the " +
            "hot path.\n\n" +
            "### Heap Profile Summary\n\n" +
            "```\n" +
            "BEFORE (v2.17.3):\n" +
            "  System.Text.Json.JsonSerializerOptions    4,720 B  x14,200/sec = 67 MB/sec\n" +
            "  System.Text.Json.Metadata.JsonTypeInfo      960 B  x14,200/sec = 13 MB/sec\n" +
            "  System.String (intermediate)                 480 B  x14,200/sec =  6 MB/sec\n" +
            "  ────────────────────────────────────────────────────────────────────────\n" +
            "  Total hot-path allocation rate:                                86 MB/sec\n" +
            "\n" +
            "AFTER (fix):\n" +
            "  (no per-request allocations)\n" +
            "  Total hot-path allocation rate:                                 0 MB/sec\n" +
            "```");

        // ── Turn 6: Stakeholder email ──
        AddUser("Draft a stakeholder update email for the VP of Engineering.");

        AddAssistant(
            "## Stakeholder Update — Incident IR-4471\n\n" +
            "Here's a draft you can send directly:\n\n" +
            "---\n\n" +
            "**Subject:** Checkout Latency Incident IR-4471 — Root Cause Identified, Fix In Progress\n\n" +
            "Hi team,\n\n" +
            "We identified the root cause of the checkout latency spike that began at 08:14 UTC today. " +
            "A serializer change in `checkout-v2.17.3` introduced per-request allocations that increased " +
            "GC pause times by **5.4x** under production load.\n\n" +
            "**Impact:**\n" +
            "- p95 latency increased from 120ms to 460ms\n" +
            "- Error rate rose from 0.02% to 1.8%\n" +
            "- Approximately 2,400 checkout failures over the incident window\n\n" +
            "**Resolution:**\n" +
            "- Fix has been validated in benchmark (100% allocation reduction)\n" +
            "- Staged rollout beginning at canary (10%) with 30-min soak\n" +
            "- Full deployment expected within 3 hours\n" +
            "- Automated rollback gates in place at every stage\n\n" +
            "**Customer Impact:**\n" +
            "- No data loss occurred\n" +
            "- Failed checkouts are retryable; recovery is automatic once fix is live\n" +
            "- Customer support has been notified with talking points\n\n" +
            "I'll send a final all-clear once full rollout is confirmed.\n\n" +
            "Best,\n" +
            "SRE Team",
            "Grounded · 4 references");

        // ── Turn 7: Infrastructure query ──
        AddUser("What does our autoscaler configuration look like right now? Is it contributing to the problem?");

        AddTool(
            "```json\n" +
            "{\n" +
            "  \"cluster\": \"checkout-prod-us-east\",\n" +
            "  \"autoscaler\": {\n" +
            "    \"minReplicas\": 4,\n" +
            "    \"maxReplicas\": 32,\n" +
            "    \"targetCpuUtilization\": 70,\n" +
            "    \"scaleUpCooldown\": \"60s\",\n" +
            "    \"scaleDownCooldown\": \"300s\"\n" +
            "  },\n" +
            "  \"currentReplicas\": 28,\n" +
            "  \"status\": \"ScalingLimited\"\n" +
            "}\n" +
            "```",
            "kubectl query · completed");

        AddAssistant(
            "## Autoscaler Analysis\n\n" +
            "The autoscaler is **not** the root cause, but it's masking the problem. Here's why:\n\n" +
            "- The cluster scaled from 4 to **28 replicas** (near the max of 32) trying to compensate " +
            "for the increased latency\n" +
            "- CPU is elevated due to GC overhead, not actual workload growth\n" +
            "- Status is `ScalingLimited` — it can't scale further\n\n" +
            "Once the serializer fix lands, replica count should stabilize back to **6–8** under normal load.\n\n" +
            "### Recommendation\n\n" +
            "After the fix, consider adding a **GC-aware** scaling metric:\n\n" +
            "```yaml\n" +
            "# kube-hpa-config.yaml\n" +
            "metrics:\n" +
            "  - type: Pods\n" +
            "    pods:\n" +
            "      metric:\n" +
            "        name: dotnet_gc_pause_ratio\n" +
            "      target:\n" +
            "        type: AverageValue\n" +
            "        averageValue: \"0.05\"\n" +
            "  - type: Resource\n" +
            "    resource:\n" +
            "      name: cpu\n" +
            "      target:\n" +
            "        type: Utilization\n" +
            "        averageUtilization: 70\n" +
            "```\n\n" +
            "This prevents the autoscaler from burning budget on GC-induced CPU pressure.");

        // ── Turn 8: SQL analysis ──
        AddUser("Can you check if there are any slow database queries related to checkout?");

        AddAssistant(
            "## Slow Query Analysis\n\n" +
            "I scanned the query store for the `checkout-db` instance. Found 3 queries exceeding the 100ms threshold:\n\n" +
            "| Query ID | Avg Duration | Executions/min | Table |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Q-8842 | 340 ms | 120 | `orders` |\n" +
            "| Q-9001 | 180 ms | 85 | `inventory_locks` |\n" +
            "| Q-9105 | 150 ms | 42 | `payment_tokens` |\n\n" +
            "The slowest query is the order confirmation join:\n\n" +
            "```sql\n" +
            "-- Q-8842: Missing index on orders.checkout_session_id\n" +
            "SELECT o.id, o.total, o.status, p.method, p.last_four\n" +
            "FROM orders o\n" +
            "INNER JOIN payments p ON p.order_id = o.id\n" +
            "WHERE o.checkout_session_id = @sessionId\n" +
            "  AND o.created_at > DATEADD(hour, -24, GETUTCDATE())\n" +
            "ORDER BY o.created_at DESC;\n" +
            "```\n\n" +
            "**Fix:** Add a covering index:\n\n" +
            "```sql\n" +
            "CREATE NONCLUSTERED INDEX IX_orders_checkout_session\n" +
            "ON orders (checkout_session_id, created_at DESC)\n" +
            "INCLUDE (id, total, status);\n" +
            "```\n\n" +
            "This should bring Q-8842 down to ~15ms. However, note that the **primary latency issue** " +
            "is still the serializer — the DB queries are a secondary optimization.",
            "query-store · 3 results");

        // ── Turn 9: Short follow-up ──
        AddUser("Makes sense. Let's also add monitoring alerts for this.");

        AddAssistant(
            "## Recommended Alert Rules\n\n" +
            "Here are alert definitions for Prometheus/Alertmanager:\n\n" +
            "```yaml\n" +
            "groups:\n" +
            "  - name: checkout-slo\n" +
            "    rules:\n" +
            "      - alert: CheckoutP95LatencyHigh\n" +
            "        expr: |\n" +
            "          histogram_quantile(0.95,\n" +
            "            rate(checkout_request_duration_seconds_bucket[5m])\n" +
            "          ) > 0.25\n" +
            "        for: 5m\n" +
            "        labels:\n" +
            "          severity: warning\n" +
            "        annotations:\n" +
            "          summary: \"Checkout p95 latency above 250ms\"\n" +
            "\n" +
            "      - alert: CheckoutErrorRateHigh\n" +
            "        expr: |\n" +
            "          rate(checkout_errors_total[5m])\n" +
            "          / rate(checkout_requests_total[5m]) > 0.005\n" +
            "        for: 3m\n" +
            "        labels:\n" +
            "          severity: critical\n" +
            "        annotations:\n" +
            "          summary: \"Checkout error rate above 0.5%\"\n" +
            "\n" +
            "      - alert: GCPauseRatioHigh\n" +
            "        expr: dotnet_gc_pause_ratio > 0.08\n" +
            "        for: 5m\n" +
            "        labels:\n" +
            "          severity: warning\n" +
            "        annotations:\n" +
            "          summary: \"GC pause ratio exceeds 8% — potential allocation regression\"\n" +
            "```\n\n" +
            "These cover the three failure modes we saw today:\n" +
            "1. **Latency** — catches tail-latency regressions early\n" +
            "2. **Errors** — catches checkout failures before customers notice\n" +
            "3. **GC pressure** — catches allocation regressions at the source",
            "Grounded · 2 references");

        // ── Turn 10: Wrap-up with different languages ──
        AddUser("One more thing — can you show me how to write a quick health check endpoint in Python? Our status page uses a Python microservice.");

        AddAssistant(
            "## Health Check Endpoint (Python)\n\n" +
            "Here's a lightweight health check using FastAPI that queries the " +
            "checkout service metrics:\n\n" +
            "```python\n" +
            "from fastapi import FastAPI, Response\n" +
            "from pydantic import BaseModel\n" +
            "import httpx\n" +
            "import asyncio\n" +
            "\n" +
            "app = FastAPI()\n" +
            "\n" +
            "class HealthStatus(BaseModel):\n" +
            "    service: str\n" +
            "    healthy: bool\n" +
            "    p95_latency_ms: float\n" +
            "    error_rate: float\n" +
            "    replica_count: int\n" +
            "\n" +
            "async def fetch_metrics() -> dict:\n" +
            "    async with httpx.AsyncClient(timeout=5.0) as client:\n" +
            "        resp = await client.get(\"http://checkout-svc:9090/metrics\")\n" +
            "        resp.raise_for_status()\n" +
            "        return resp.json()\n" +
            "\n" +
            "@app.get(\"/health\", response_model=HealthStatus)\n" +
            "async def health_check():\n" +
            "    metrics = await fetch_metrics()\n" +
            "    healthy = (\n" +
            "        metrics[\"p95_latency_ms\"] <= 250\n" +
            "        and metrics[\"error_rate\"] <= 0.005\n" +
            "    )\n" +
            "    return HealthStatus(\n" +
            "        service=\"checkout\",\n" +
            "        healthy=healthy,\n" +
            "        p95_latency_ms=metrics[\"p95_latency_ms\"],\n" +
            "        error_rate=metrics[\"error_rate\"],\n" +
            "        replica_count=metrics[\"replica_count\"],\n" +
            "    )\n" +
            "```\n\n" +
            "And for your status page, a simple HTML badge generator:\n\n" +
            "```python\n" +
            "@app.get(\"/badge\")\n" +
            "async def status_badge():\n" +
            "    status = await health_check()\n" +
            "    color = \"#22c55e\" if status.healthy else \"#ef4444\"\n" +
            "    label = \"operational\" if status.healthy else \"degraded\"\n" +
            "    svg = f\"\"\"\n" +
            "    <svg xmlns='http://www.w3.org/2000/svg' width='140' height='20'>\n" +
            "      <rect width='140' height='20' rx='3' fill='{color}'/>\n" +
            "      <text x='70' y='14' text-anchor='middle'\n" +
            "            font-family='monospace' font-size='11' fill='white'>\n" +
            "        checkout: {label}\n" +
            "      </text>\n" +
            "    </svg>\"\"\"\n" +
            "    return Response(content=svg, media_type=\"image/svg+xml\")\n" +
            "```",
            "Grounded · 1 reference");

        // ── Turn 11: Summary request ──
        AddUser("Great work. Can you give me a final summary of everything we've covered?");

        AddAssistant(
            "## Session Summary — Incident IR-4471\n\n" +
            "### Root Cause\n" +
            "Serializer migration in `checkout-v2.17.3` (PR #1842) introduced per-request " +
            "`JsonSerializerOptions` allocations, causing **86 MB/sec** of GC pressure on the hot path.\n\n" +
            "### Impact\n" +
            "- p95 latency: 120ms → 460ms (+283%)\n" +
            "- Error rate: 0.02% → 1.8%\n" +
            "- ~2,400 failed checkouts during incident window\n" +
            "- Autoscaler saturated at 28/32 replicas\n\n" +
            "### Resolution\n" +
            "1. **Serializer fix** — cache `JsonSerializerOptions` as static field (100% allocation reduction)\n" +
            "2. **Database index** — covering index on `orders.checkout_session_id` (Q-8842: 340ms → ~15ms)\n" +
            "3. **Monitoring** — added Prometheus alerts for p95 latency, error rate, and GC pause ratio\n" +
            "4. **Autoscaler** — recommended GC-aware scaling metric for future resilience\n\n" +
            "### Deployment Plan\n\n" +
            "| Stage | Traffic | Soak Time | Gate |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Canary | 10% | 30 min | p95 ≤ 200ms |\n" +
            "| Expansion | 50% | 1 hour | p99 ≤ 500ms |\n" +
            "| Full | 100% | 24h | error rate ≤ 0.5% |\n\n" +
            "### Files Modified\n" +
            "- `src/Checkout/Serialization/PaymentSerializer.cs`\n" +
            "- `infra/kube-hpa-config.yaml`\n" +
            "- `monitoring/alerts/checkout-slo.yaml`\n" +
            "- `sql/migrations/004_add_checkout_session_index.sql`\n\n" +
            "### Next Steps\n" +
            "- [ ] Merge serializer fix PR\n" +
            "- [ ] Begin canary rollout\n" +
            "- [ ] Apply database index during maintenance window\n" +
            "- [ ] Deploy alert rules to production Alertmanager\n" +
            "- [ ] Post-incident review scheduled for Friday",
            "Grounded · 6 references");

        // ── Turn 12: New topic — logging ──
        AddUser("Actually, while we're at it — our logging is a mess. Can you suggest a structured logging pattern we should adopt across services?");

        AddAssistant(
            "## Structured Logging Standard\n\n" +
            "Here's a consistent pattern using `Microsoft.Extensions.Logging` with Serilog sinks. " +
            "The key is to use **semantic properties** instead of string interpolation.\n\n" +
            "### ❌ What to avoid\n\n" +
            "```csharp\n" +
            "// Anti-pattern: unstructured string interpolation\n" +
            "_logger.LogInformation($\"Order {orderId} processed for customer {customerId} in {elapsed}ms\");\n" +
            "```\n\n" +
            "### ✅ Recommended pattern\n\n" +
            "```csharp\n" +
            "// Structured: properties are indexed separately\n" +
            "_logger.LogInformation(\n" +
            "    \"Order {OrderId} processed for customer {CustomerId} in {ElapsedMs}ms\",\n" +
            "    orderId,\n" +
            "    customerId,\n" +
            "    elapsed);\n" +
            "```\n\n" +
            "### Correlation setup\n\n" +
            "```csharp\n" +
            "public sealed class CorrelationMiddleware\n" +
            "{\n" +
            "    private readonly RequestDelegate _next;\n" +
            "\n" +
            "    public CorrelationMiddleware(RequestDelegate next) => _next = next;\n" +
            "\n" +
            "    public async Task InvokeAsync(HttpContext context)\n" +
            "    {\n" +
            "        var correlationId = context.Request.Headers[\"X-Correlation-Id\"]\n" +
            "            .FirstOrDefault() ?? Guid.NewGuid().ToString(\"N\");\n" +
            "\n" +
            "        using (LogContext.PushProperty(\"CorrelationId\", correlationId))\n" +
            "        using (LogContext.PushProperty(\"Service\", \"checkout\"))\n" +
            "        {\n" +
            "            context.Response.Headers[\"X-Correlation-Id\"] = correlationId;\n" +
            "            await _next(context);\n" +
            "        }\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### Log level guidelines\n\n" +
            "| Level | Use for | Example |\n" +
            "| --- | --- | --- |\n" +
            "| Trace | Frame-level diagnostics | Cache hit/miss |\n" +
            "| Debug | Developer troubleshooting | Serialization path taken |\n" +
            "| Information | Business events | Order placed, payment confirmed |\n" +
            "| Warning | Recoverable anomalies | Retry succeeded, fallback used |\n" +
            "| Error | Failures needing attention | Payment gateway timeout |\n" +
            "| Critical | Service-down scenarios | Database unreachable |\n",
            "Grounded · 3 references");

        // ── Turn 13: Config management ──
        AddUser("We also need to clean up our configuration. Half our services use env vars, half use appsettings. What's the best approach?");

        AddAssistant(
            "## Configuration Unification Strategy\n\n" +
            "Use the built-in `IConfiguration` layered model with a clear priority order:\n\n" +
            "1. **appsettings.json** — defaults and non-sensitive values\n" +
            "2. **appsettings.{Environment}.json** — environment overrides\n" +
            "3. **Environment variables** — deployment-specific and secrets\n" +
            "4. **Azure Key Vault / Secret Manager** — sensitive credentials\n\n" +
            "### Standard configuration class\n\n" +
            "```csharp\n" +
            "public sealed class CheckoutOptions\n" +
            "{\n" +
            "    public const string SectionName = \"Checkout\";\n" +
            "\n" +
            "    public required string PaymentGatewayUrl { get; init; }\n" +
            "    public int MaxRetryAttempts { get; init; } = 3;\n" +
            "    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);\n" +
            "    public double SloP95ThresholdMs { get; init; } = 250;\n" +
            "    public double SloErrorRateThreshold { get; init; } = 0.005;\n" +
            "}\n" +
            "```\n\n" +
            "### Registration\n\n" +
            "```csharp\n" +
            "builder.Services\n" +
            "    .AddOptionsWithValidateOnStart<CheckoutOptions>()\n" +
            "    .BindConfiguration(CheckoutOptions.SectionName)\n" +
            "    .ValidateDataAnnotations();\n" +
            "```\n\n" +
            "### appsettings.json\n\n" +
            "```json\n" +
            "{\n" +
            "  \"Checkout\": {\n" +
            "    \"PaymentGatewayUrl\": \"https://pay.internal.corp/v2\",\n" +
            "    \"MaxRetryAttempts\": 3,\n" +
            "    \"RetryDelay\": \"00:00:01\",\n" +
            "    \"SloP95ThresholdMs\": 250,\n" +
            "    \"SloErrorRateThreshold\": 0.005\n" +
            "  }\n" +
            "}\n" +
            "```\n\n" +
            "### Environment variable override\n\n" +
            "```bash\n" +
            "# Docker / K8s — doubles underscores replace section nesting\n" +
            "Checkout__PaymentGatewayUrl=https://pay.staging.corp/v2\n" +
            "Checkout__MaxRetryAttempts=5\n" +
            "```\n\n" +
            "This gives you a single source of truth with clear override semantics.",
            "Grounded · 2 references");

        // ── Turn 14: Testing question ──
        AddUser("What about integration tests for all of this? We barely have any.");

        AddAssistant(
            "## Integration Testing Strategy\n\n" +
            "For a service like checkout, I recommend three layers of integration tests:\n\n" +
            "### 1. API-level tests with `WebApplicationFactory`\n\n" +
            "```csharp\n" +
            "public sealed class CheckoutApiTests : IClassFixture<WebApplicationFactory<Program>>\n" +
            "{\n" +
            "    private readonly HttpClient _client;\n" +
            "\n" +
            "    public CheckoutApiTests(WebApplicationFactory<Program> factory)\n" +
            "    {\n" +
            "        _client = factory.WithWebHostBuilder(builder =>\n" +
            "        {\n" +
            "            builder.ConfigureServices(services =>\n" +
            "            {\n" +
            "                services.AddSingleton<IPaymentGateway, FakePaymentGateway>();\n" +
            "                services.AddSingleton<IInventoryService, FakeInventoryService>();\n" +
            "            });\n" +
            "        }).CreateClient();\n" +
            "    }\n" +
            "\n" +
            "    [Fact]\n" +
            "    public async Task Checkout_WithValidCart_ReturnsConfirmation()\n" +
            "    {\n" +
            "        var cart = new { Items = new[] { new { Sku = \"WIDGET-1\", Qty = 2 } } };\n" +
            "\n" +
            "        var response = await _client.PostAsJsonAsync(\"/api/checkout/confirm\", cart);\n" +
            "\n" +
            "        response.EnsureSuccessStatusCode();\n" +
            "        var result = await response.Content.ReadFromJsonAsync<OrderConfirmation>();\n" +
            "        Assert.NotNull(result);\n" +
            "        Assert.Equal(\"confirmed\", result.Status);\n" +
            "    }\n" +
            "\n" +
            "    [Fact]\n" +
            "    public async Task Checkout_WithEmptyCart_Returns400()\n" +
            "    {\n" +
            "        var cart = new { Items = Array.Empty<object>() };\n" +
            "\n" +
            "        var response = await _client.PostAsJsonAsync(\"/api/checkout/confirm\", cart);\n" +
            "\n" +
            "        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### 2. Database tests with Testcontainers\n\n" +
            "```csharp\n" +
            "public sealed class OrderRepositoryTests : IAsyncLifetime\n" +
            "{\n" +
            "    private readonly MsSqlContainer _container = new MsSqlBuilder()\n" +
            "        .WithImage(\"mcr.microsoft.com/mssql/server:2022-latest\")\n" +
            "        .Build();\n" +
            "\n" +
            "    public Task InitializeAsync() => _container.StartAsync();\n" +
            "    public Task DisposeAsync() => _container.DisposeAsync().AsTask();\n" +
            "\n" +
            "    [Fact]\n" +
            "    public async Task GetBySessionId_WithIndex_ReturnsUnder20ms()\n" +
            "    {\n" +
            "        await using var db = new CheckoutDbContext(_container.GetConnectionString());\n" +
            "        await db.Database.MigrateAsync();\n" +
            "        // seed 100k rows, then benchmark the query\n" +
            "        var sw = Stopwatch.StartNew();\n" +
            "        var order = await db.Orders\n" +
            "            .Where(o => o.CheckoutSessionId == \"test-session\")\n" +
            "            .FirstOrDefaultAsync();\n" +
            "        sw.Stop();\n" +
            "        Assert.True(sw.ElapsedMilliseconds < 20);\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### 3. Contract tests for downstream services\n\n" +
            "| Test Type | Scope | Speed | Confidence |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Unit | Single class | < 1ms | Logic correctness |\n" +
            "| API integration | Full HTTP pipeline | ~50ms | Route + middleware + DI |\n" +
            "| DB integration | Real SQL Server | ~200ms | Query + migration correctness |\n" +
            "| Contract | Consumer/provider | ~100ms | API compatibility |\n" +
            "| E2E | Full checkout flow | ~2s | Business workflow |\n",
            "Grounded · 2 references");

        // ── System note ──
        AddSystem("Canary deployment initiated — monitoring checkout-v2.17.4 at 10% traffic.");

        // ── Turn 15: Deployment status ──
        AddUser("How's the canary looking so far?");

        AddTool(
            "```json\n" +
            "{\n" +
            "  \"deployment\": \"checkout-v2.17.4\",\n" +
            "  \"stage\": \"canary\",\n" +
            "  \"trafficPercent\": 10,\n" +
            "  \"uptimeMinutes\": 18,\n" +
            "  \"metrics\": {\n" +
            "    \"p50LatencyMs\": 38,\n" +
            "    \"p95LatencyMs\": 95,\n" +
            "    \"p99LatencyMs\": 180,\n" +
            "    \"errorRate\": 0.001,\n" +
            "    \"gcPauseMs\": 8.2,\n" +
            "    \"allocRateMbPerSec\": 0.4,\n" +
            "    \"replicaCount\": 5\n" +
            "  },\n" +
            "  \"gates\": {\n" +
            "    \"p95Gate\": { \"threshold\": 200, \"status\": \"PASS\" },\n" +
            "    \"errorGate\": { \"threshold\": 0.001, \"status\": \"PASS\" },\n" +
            "    \"gcGate\": { \"threshold\": 40, \"status\": \"PASS\" }\n" +
            "  }\n" +
            "}\n" +
            "```",
            "monitor.query · completed");

        AddAssistant(
            "## Canary Status — Looking Great ✅\n\n" +
            "18 minutes in at 10% traffic. All gates are passing:\n\n" +
            "| Metric | Current | Gate | Status |\n" +
            "| --- | --- | --- | --- |\n" +
            "| p95 latency | 95 ms | ≤ 200 ms | ✅ Pass |\n" +
            "| Error rate | 0.1% | ≤ 0.1% | ✅ Pass |\n" +
            "| GC pause | 8.2 ms | ≤ 40 ms | ✅ Pass |\n" +
            "| Alloc rate | 0.4 MB/s | — | 🟢 Normal |\n" +
            "| Replicas | 5 | — | 🟢 Stable |\n\n" +
            "Compare that to the incident metrics:\n\n" +
            "| Metric | During Incident | Canary Now | Improvement |\n" +
            "| --- | --- | --- | --- |\n" +
            "| p95 latency | 460 ms | 95 ms | **79% better** |\n" +
            "| GC pause | 97 ms | 8.2 ms | **92% better** |\n" +
            "| Alloc rate | 86 MB/s | 0.4 MB/s | **99.5% reduction** |\n\n" +
            "I'd recommend waiting the full 30 minutes before promoting to 50%, but " +
            "these numbers are very encouraging. The serializer fix is clearly effective.",
            "Grounded · live metrics");

        // ── Turn 16: Security question ──
        AddUser("While we wait — we got a security audit report last week. They flagged our JWT validation. What should we check?");

        AddAssistant(
            "## JWT Validation Checklist\n\n" +
            "Common audit findings and their fixes:\n\n" +
            "### 1. Algorithm confusion attack\n\n" +
            "```csharp\n" +
            "// ❌ Vulnerable: accepts any algorithm\n" +
            "var parameters = new TokenValidationParameters\n" +
            "{\n" +
            "    ValidateIssuerSigningKey = true,\n" +
            "    IssuerSigningKey = signingKey\n" +
            "};\n" +
            "\n" +
            "// ✅ Fixed: explicitly restrict algorithms\n" +
            "var parameters = new TokenValidationParameters\n" +
            "{\n" +
            "    ValidateIssuerSigningKey = true,\n" +
            "    IssuerSigningKey = signingKey,\n" +
            "    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },\n" +
            "    ValidateIssuer = true,\n" +
            "    ValidIssuer = \"https://auth.corp.internal\",\n" +
            "    ValidateAudience = true,\n" +
            "    ValidAudience = \"checkout-api\",\n" +
            "    ValidateLifetime = true,\n" +
            "    ClockSkew = TimeSpan.FromMinutes(2)\n" +
            "};\n" +
            "```\n\n" +
            "### 2. Token lifetime and refresh\n\n" +
            "| Token Type | Max Lifetime | Storage | Rotation |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Access token | 15 min | Memory only | On expiry |\n" +
            "| Refresh token | 7 days | Secure HTTP-only cookie | On use (rotate) |\n" +
            "| API key | 90 days | Secret manager | Manual |\n\n" +
            "### 3. Claims validation middleware\n\n" +
            "```csharp\n" +
            "builder.Services.AddAuthorization(options =>\n" +
            "{\n" +
            "    options.AddPolicy(\"CheckoutAccess\", policy =>\n" +
            "    {\n" +
            "        policy.RequireAuthenticatedUser();\n" +
            "        policy.RequireClaim(\"scope\", \"checkout.write\");\n" +
            "        policy.RequireClaim(\"tenant_id\");\n" +
            "    });\n" +
            "\n" +
            "    options.AddPolicy(\"AdminOnly\", policy =>\n" +
            "    {\n" +
            "        policy.RequireAuthenticatedUser();\n" +
            "        policy.RequireRole(\"admin\", \"sre\");\n" +
            "        policy.RequireClaim(\"mfa_verified\", \"true\");\n" +
            "    });\n" +
            "});\n" +
            "```\n\n" +
            "### 4. Key audit findings to remediate\n\n" +
            "- [ ] Enforce algorithm restriction on all token validation\n" +
            "- [ ] Reduce access token lifetime to 15 minutes\n" +
            "- [ ] Implement refresh token rotation\n" +
            "- [ ] Add `aud` and `iss` validation everywhere\n" +
            "- [ ] Migrate API keys to short-lived service tokens\n" +
            "- [ ] Add rate limiting on token endpoint",
            "Grounded · security audit");

        // ── Turn 17: Docker/containerization ──
        AddUser("Good list. Can you also review our Dockerfile? I think it's suboptimal.");

        AddAssistant(
            "## Dockerfile Review & Optimization\n\n" +
            "Here's an optimized multi-stage Dockerfile with best practices:\n\n" +
            "```dockerfile\n" +
            "# Stage 1: Restore (cacheable layer)\n" +
            "FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS restore\n" +
            "WORKDIR /src\n" +
            "COPY *.sln Directory.Build.props Directory.Packages.props ./\n" +
            "COPY src/Checkout/Checkout.csproj src/Checkout/\n" +
            "COPY src/Shared/Shared.csproj src/Shared/\n" +
            "RUN dotnet restore src/Checkout/Checkout.csproj\n" +
            "\n" +
            "# Stage 2: Build + publish\n" +
            "FROM restore AS publish\n" +
            "COPY . .\n" +
            "RUN dotnet publish src/Checkout/Checkout.csproj \\\n" +
            "    -c Release \\\n" +
            "    -o /app \\\n" +
            "    --no-restore \\\n" +
            "    -p:PublishTrimmed=true \\\n" +
            "    -p:PublishSingleFile=true\n" +
            "\n" +
            "# Stage 3: Runtime (minimal image)\n" +
            "FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS final\n" +
            "RUN adduser -D -h /app appuser\n" +
            "USER appuser\n" +
            "WORKDIR /app\n" +
            "COPY --from=publish /app .\n" +
            "ENV DOTNET_EnableDiagnostics=0\n" +
            "ENV ASPNETCORE_URLS=http://+:8080\n" +
            "EXPOSE 8080\n" +
            "HEALTHCHECK --interval=15s --timeout=3s --retries=3 \\\n" +
            "    CMD wget -qO- http://localhost:8080/health || exit 1\n" +
            "ENTRYPOINT [\"./Checkout\"]\n" +
            "```\n\n" +
            "### Key improvements\n\n" +
            "| Aspect | Before | After |\n" +
            "| --- | --- | --- |\n" +
            "| Base image | `sdk:8.0` (780 MB) | `runtime-deps:8.0-alpine` (12 MB) |\n" +
            "| Final image size | ~820 MB | ~45 MB |\n" +
            "| Layer caching | Single stage | 3-stage with restore cache |\n" +
            "| User | root | Non-root `appuser` |\n" +
            "| Health check | None | Built-in HTTP check |\n" +
            "| Publish | Framework-dependent | Trimmed single-file |\n",
            "Grounded · 1 reference");

        // ── Turn 18: Observability ──
        AddUser("What observability stack do you recommend for tracing across our microservices?");

        AddAssistant(
            "## Distributed Tracing with OpenTelemetry\n\n" +
            "OpenTelemetry is the standard. Here's a production-ready setup:\n\n" +
            "### Service registration\n\n" +
            "```csharp\n" +
            "builder.Services.AddOpenTelemetry()\n" +
            "    .ConfigureResource(r => r\n" +
            "        .AddService(\"checkout-api\")\n" +
            "        .AddAttributes(new Dictionary<string, object>\n" +
            "        {\n" +
            "            [\"deployment.environment\"] = builder.Environment.EnvironmentName,\n" +
            "            [\"service.version\"] = Assembly.GetExecutingAssembly()\n" +
            "                .GetName().Version?.ToString() ?? \"unknown\"\n" +
            "        }))\n" +
            "    .WithTracing(tracing => tracing\n" +
            "        .AddAspNetCoreInstrumentation(o =>\n" +
            "        {\n" +
            "            o.RecordException = true;\n" +
            "            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments(\"/health\");\n" +
            "        })\n" +
            "        .AddHttpClientInstrumentation()\n" +
            "        .AddSqlClientInstrumentation(o => o.SetDbStatementForText = true)\n" +
            "        .AddOtlpExporter())\n" +
            "    .WithMetrics(metrics => metrics\n" +
            "        .AddAspNetCoreInstrumentation()\n" +
            "        .AddHttpClientInstrumentation()\n" +
            "        .AddRuntimeInstrumentation()\n" +
            "        .AddProcessInstrumentation()\n" +
            "        .AddOtlpExporter());\n" +
            "```\n\n" +
            "### Custom business spans\n\n" +
            "```csharp\n" +
            "private static readonly ActivitySource Source = new(\"Checkout.Business\");\n" +
            "\n" +
            "public async Task<OrderResult> ProcessCheckoutAsync(Cart cart)\n" +
            "{\n" +
            "    using var activity = Source.StartActivity(\"ProcessCheckout\");\n" +
            "    activity?.SetTag(\"cart.item_count\", cart.Items.Count);\n" +
            "    activity?.SetTag(\"cart.total\", cart.Total);\n" +
            "\n" +
            "    var reservation = await ReserveInventoryAsync(cart);\n" +
            "    activity?.AddEvent(new ActivityEvent(\"InventoryReserved\"));\n" +
            "\n" +
            "    var payment = await ChargePaymentAsync(cart);\n" +
            "    activity?.AddEvent(new ActivityEvent(\"PaymentCharged\"));\n" +
            "\n" +
            "    return new OrderResult(reservation, payment);\n" +
            "}\n" +
            "```\n\n" +
            "### Observability stack comparison\n\n" +
            "| Component | Option A | Option B | Our Recommendation |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Traces | Jaeger | Tempo + Grafana | Tempo (scales better) |\n" +
            "| Metrics | Prometheus | Azure Monitor | Prometheus (K8s native) |\n" +
            "| Logs | ELK Stack | Loki + Grafana | Loki (label-based, cheaper) |\n" +
            "| Dashboards | Kibana | Grafana | Grafana (unified view) |\n" +
            "| Alerting | PagerDuty | Alertmanager | Alertmanager (Prometheus native) |\n",
            "Grounded · 3 references");

        // ── Turn 19: Quick question ──
        AddUser("Is there a way to automatically detect N+1 query problems?");

        AddAssistant(
            "## N+1 Query Detection\n\n" +
            "Yes — use EF Core's built-in query analysis combined with a custom interceptor:\n\n" +
            "```csharp\n" +
            "public sealed class NPlus1Interceptor : DbCommandInterceptor\n" +
            "{\n" +
            "    private static readonly AsyncLocal<QueryTracker> Tracker = new();\n" +
            "\n" +
            "    public static IDisposable BeginScope() =>\n" +
            "        new TrackerScope(Tracker);\n" +
            "\n" +
            "    public override InterceptionResult<DbDataReader> ReaderExecuting(\n" +
            "        DbCommand command,\n" +
            "        CommandEventData eventData,\n" +
            "        InterceptionResult<DbDataReader> result)\n" +
            "    {\n" +
            "        var tracker = Tracker.Value;\n" +
            "        if (tracker is not null)\n" +
            "        {\n" +
            "            tracker.QueryCount++;\n" +
            "            var normalized = NormalizeSql(command.CommandText);\n" +
            "            tracker.Patterns.AddOrUpdate(\n" +
            "                normalized,\n" +
            "                1,\n" +
            "                (_, count) => count + 1);\n" +
            "        }\n" +
            "        return result;\n" +
            "    }\n" +
            "\n" +
            "    private sealed class TrackerScope : IDisposable\n" +
            "    {\n" +
            "        private readonly AsyncLocal<QueryTracker> _local;\n" +
            "\n" +
            "        public TrackerScope(AsyncLocal<QueryTracker> local)\n" +
            "        {\n" +
            "            _local = local;\n" +
            "            _local.Value = new QueryTracker();\n" +
            "        }\n" +
            "\n" +
            "        public void Dispose()\n" +
            "        {\n" +
            "            var tracker = _local.Value;\n" +
            "            if (tracker?.QueryCount > 10)\n" +
            "            {\n" +
            "                var worst = tracker.Patterns\n" +
            "                    .OrderByDescending(kv => kv.Value)\n" +
            "                    .First();\n" +
            "                Log.Warning(\n" +
            "                    \"Potential N+1: {Count} queries, pattern repeated {Repeats}x\",\n" +
            "                    tracker.QueryCount,\n" +
            "                    worst.Value);\n" +
            "            }\n" +
            "            _local.Value = null;\n" +
            "        }\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "Also consider adding `.AsSplitQuery()` for complex includes:\n\n" +
            "```csharp\n" +
            "var orders = await context.Orders\n" +
            "    .Include(o => o.LineItems)\n" +
            "    .Include(o => o.Payment)\n" +
            "    .Include(o => o.ShippingAddress)\n" +
            "    .AsSplitQuery()\n" +
            "    .Where(o => o.CustomerId == customerId)\n" +
            "    .ToListAsync();\n" +
            "```",
            "");

        // ── Turn 20: Deployment update ──
        AddSystem("Canary gate passed — auto-promoting to 50% traffic.");

        AddUser("Awesome, the canary passed! Let's talk about something else while we wait. How should we handle feature flags?");

        AddAssistant(
            "## Feature Flag Architecture\n\n" +
            "Use a centralized feature management system. Here's the pattern " +
            "with `Microsoft.FeatureManagement`:\n\n" +
            "### Basic setup\n\n" +
            "```csharp\n" +
            "builder.Services.AddFeatureManagement()\n" +
            "    .AddFeatureFilter<PercentageFilter>()\n" +
            "    .AddFeatureFilter<TimeWindowFilter>()\n" +
            "    .AddFeatureFilter<TargetingFilter>();\n" +
            "```\n\n" +
            "### Using flags in controllers\n\n" +
            "```csharp\n" +
            "[FeatureGate(\"NewCheckoutFlow\")]\n" +
            "[HttpPost(\"confirm-v2\")]\n" +
            "public async Task<IActionResult> ConfirmV2([FromBody] CartRequest cart)\n" +
            "{\n" +
            "    // This endpoint only activates when NewCheckoutFlow is enabled\n" +
            "    var result = await _checkoutService.ProcessV2Async(cart);\n" +
            "    return Ok(result);\n" +
            "}\n" +
            "```\n\n" +
            "### Percentage-based rollout (like our canary)\n\n" +
            "```json\n" +
            "{\n" +
            "  \"FeatureManagement\": {\n" +
            "    \"NewCheckoutFlow\": {\n" +
            "      \"EnabledFor\": [\n" +
            "        {\n" +
            "          \"Name\": \"Percentage\",\n" +
            "          \"Parameters\": {\n" +
            "            \"Value\": 50\n" +
            "          }\n" +
            "        }\n" +
            "      ]\n" +
            "    },\n" +
            "    \"DarkMode\": {\n" +
            "      \"EnabledFor\": [\n" +
            "        {\n" +
            "          \"Name\": \"Targeting\",\n" +
            "          \"Parameters\": {\n" +
            "            \"Audience\": {\n" +
            "              \"Groups\": [\n" +
            "                { \"Name\": \"beta-testers\", \"RolloutPercentage\": 100 },\n" +
            "                { \"Name\": \"internal\", \"RolloutPercentage\": 50 }\n" +
            "              ],\n" +
            "              \"DefaultRolloutPercentage\": 0\n" +
            "            }\n" +
            "          }\n" +
            "        }\n" +
            "      ]\n" +
            "    }\n" +
            "  }\n" +
            "}\n" +
            "```\n\n" +
            "### Flag hygiene rules\n\n" +
            "| Rule | Rationale |\n" +
            "| --- | --- |\n" +
            "| Max lifetime: 30 days | Prevent permanent flags |\n" +
            "| Owner assigned | Someone must clean up |\n" +
            "| Remove after 100% | Dead code accumulates |\n" +
            "| Test both paths | CI must cover on AND off |\n" +
            "| Audit log changes | Track who toggled what |\n",
            "Grounded · 2 references");

        // ── Turn 21: Wrapping up ──
        AddUser("This has been incredibly helpful. Let me know when the 50% stage clears.");

        AddAssistant(
            "Will do! To summarize the extra items we covered beyond the incident:\n\n" +
            "- **Structured logging** — semantic properties with correlation IDs\n" +
            "- **Configuration** — layered `IConfiguration` with options pattern\n" +
            "- **Integration testing** — WebApplicationFactory + Testcontainers\n" +
            "- **JWT security** — algorithm restriction, token lifetime, claims policies\n" +
            "- **Docker optimization** — multi-stage build, 820MB → 45MB\n" +
            "- **Observability** — OpenTelemetry with Tempo/Loki/Grafana\n" +
            "- **N+1 detection** — custom EF Core interceptor\n" +
            "- **Feature flags** — Microsoft.FeatureManagement with targeting\n\n" +
            "I'll ping you the moment the 50% gate completes. " +
            "Based on the canary numbers, I'm optimistic it'll pass cleanly. 🚀",
            "");

        // ── Turn 22: New incident emerges ──
        AddSystem("Alert: Elevated error rate detected on payments-gateway-eu (region: eu-west-1).");

        AddUser("Wait, what's happening in EU now? Is this related to our checkout fix?");

        AddTool(
            "```json\n" +
            "{\n" +
            "  \"service\": \"payments-gateway-eu\",\n" +
            "  \"region\": \"eu-west-1\",\n" +
            "  \"alertFired\": \"2026-03-02T15:42:00Z\",\n" +
            "  \"metrics\": {\n" +
            "    \"errorRate\": 3.2,\n" +
            "    \"p95LatencyMs\": 890,\n" +
            "    \"activeConnections\": 14200,\n" +
            "    \"connectionPoolExhausted\": true,\n" +
            "    \"healthCheckStatus\": \"degraded\"\n" +
            "  },\n" +
            "  \"recentDeployments\": [],\n" +
            "  \"dnsResolution\": \"ok\",\n" +
            "  \"upstreamDeps\": [\n" +
            "    { \"name\": \"stripe-eu\", \"status\": \"degraded\", \"latencyMs\": 4200 },\n" +
            "    { \"name\": \"checkout-api\", \"status\": \"healthy\", \"latencyMs\": 68 }\n" +
            "  ]\n" +
            "}\n" +
            "```",
            "monitor.query · completed");

        AddAssistant(
            "## EU Gateway Alert — Independent Issue\n\n" +
            "**This is NOT related to our checkout fix.** The checkout-api is reporting healthy at 68ms " +
            "in eu-west-1. The problem is upstream.\n\n" +
            "### Root Cause: Stripe EU Degradation\n\n" +
            "Stripe's EU endpoint is responding at **4,200ms** (normally ~80ms). " +
            "This is causing:\n\n" +
            "| Effect | Value | Normal |\n" +
            "| --- | --- | --- |\n" +
            "| Connection pool | **Exhausted** | 20% utilized |\n" +
            "| Active connections | 14,200 | ~2,000 |\n" +
            "| Error rate | 3.2% | 0.02% |\n" +
            "| p95 latency | 890 ms | 95 ms |\n\n" +
            "The slow Stripe responses are tying up HTTP connections, exhausting the pool, " +
            "and causing cascading timeouts.\n\n" +
            "### Immediate Mitigation\n\n" +
            "```csharp\n" +
            "// Reduce Stripe timeout to shed slow requests faster\n" +
            "services.AddHttpClient(\"StripeEU\", client =>\n" +
            "{\n" +
            "    client.BaseAddress = new Uri(\"https://api.stripe.com/\");\n" +
            "    client.Timeout = TimeSpan.FromSeconds(5); // was 30s\n" +
            "})\n" +
            ".AddPolicyHandler(Policy.Handle<HttpRequestException>()\n" +
            "    .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)\n" +
            "    .CircuitBreakerAsync(\n" +
            "        handledEventsAllowedBeforeBreaking: 5,\n" +
            "        durationOfBreak: TimeSpan.FromSeconds(30)));\n" +
            "```\n\n" +
            "### Circuit Breaker State Machine\n\n" +
            "```\n" +
            "  ┌─────────┐    5 failures     ┌──────┐    30s cooldown    ┌───────────┐\n" +
            "  │ CLOSED  │ ───────────────► │ OPEN │ ──────────────► │ HALF-OPEN │\n" +
            "  │(normal) │                  │(fail)│                  │ (testing)  │\n" +
            "  └─────────┘                  └──────┘                  └───────────┘\n" +
            "       ▲                                                       │\n" +
            "       │              success on test request                  │\n" +
            "       └──────────────────────────────────────────────────────┘\n" +
            "```\n\n" +
            "I'd also recommend enabling **fallback to Stripe US** for EU customers during the outage.",
            "Grounded · 3 references");

        // ── Turn 23: Multi-region failover ──
        AddUser("Good call on the circuit breaker. How should we set up cross-region failover for payment gateways in general?");

        AddAssistant(
            "## Cross-Region Payment Gateway Failover\n\n" +
            "Here's a production-grade failover design with health-aware routing:\n\n" +
            "### Architecture\n\n" +
            "```\n" +
            "                    ┌──────────────────┐\n" +
            "                    │  Load Balancer    │\n" +
            "                    │  (health-aware)   │\n" +
            "                    └────────┬─────────┘\n" +
            "                             │\n" +
            "              ┌──────────────┼──────────────┐\n" +
            "              ▼              ▼              ▼\n" +
            "      ┌──────────────┐ ┌──────────────┐ ┌──────────────┐\n" +
            "      │ Gateway: EU  │ │ Gateway: US  │ │ Gateway: APAC│\n" +
            "      │ (Stripe EU)  │ │ (Stripe US)  │ │ (Adyen APAC) │\n" +
            "      │ Priority: 1  │ │ Priority: 2  │ │ Priority: 3  │\n" +
            "      └──────────────┘ └──────────────┘ └──────────────┘\n" +
            "```\n\n" +
            "### Gateway selector implementation\n\n" +
            "```csharp\n" +
            "public sealed class PaymentGatewaySelector\n" +
            "{\n" +
            "    private readonly IReadOnlyList<GatewayEndpoint> _endpoints;\n" +
            "    private readonly IHealthCheckService _health;\n" +
            "    private readonly ILogger<PaymentGatewaySelector> _logger;\n" +
            "\n" +
            "    public PaymentGatewaySelector(\n" +
            "        IOptions<PaymentOptions> options,\n" +
            "        IHealthCheckService health,\n" +
            "        ILogger<PaymentGatewaySelector> logger)\n" +
            "    {\n" +
            "        _endpoints = options.Value.Gateways\n" +
            "            .OrderBy(g => g.Priority)\n" +
            "            .ToList();\n" +
            "        _health = health;\n" +
            "        _logger = logger;\n" +
            "    }\n" +
            "\n" +
            "    public async Task<GatewayEndpoint> SelectAsync(string region)\n" +
            "    {\n" +
            "        // Prefer regional gateway\n" +
            "        var regional = _endpoints.FirstOrDefault(g => g.Region == region);\n" +
            "        if (regional is not null && await IsHealthyAsync(regional))\n" +
            "            return regional;\n" +
            "\n" +
            "        // Failover to next healthy gateway by priority\n" +
            "        foreach (var gateway in _endpoints)\n" +
            "        {\n" +
            "            if (await IsHealthyAsync(gateway))\n" +
            "            {\n" +
            "                _logger.LogWarning(\n" +
            "                    \"Regional gateway {Region} unhealthy, failing over to {Failover}\",\n" +
            "                    region, gateway.Region);\n" +
            "                return gateway;\n" +
            "            }\n" +
            "        }\n" +
            "\n" +
            "        throw new PaymentGatewayUnavailableException(\n" +
            "            $\"No healthy gateways available for region {region}\");\n" +
            "    }\n" +
            "\n" +
            "    private async Task<bool> IsHealthyAsync(GatewayEndpoint gw)\n" +
            "    {\n" +
            "        var report = await _health.CheckHealthAsync(\n" +
            "            r => r.Tags.Contains(gw.HealthTag));\n" +
            "        return report.Status == HealthStatus.Healthy;\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### Failover decision matrix\n\n" +
            "| Scenario | Primary | Failover | Latency Impact | Data Residency |\n" +
            "| --- | --- | --- | --- | --- |\n" +
            "| EU healthy | Stripe EU | — | Baseline | ✅ EU data stays in EU |\n" +
            "| EU degraded | Stripe US | Stripe EU (retry) | +40ms | ⚠️ Temporary US routing |\n" +
            "| EU + US down | Adyen APAC | — | +120ms | ⚠️ APAC routing |\n" +
            "| All degraded | Circuit open | Queue for retry | N/A | ✅ No data leaves |\n\n" +
            "### Configuration\n\n" +
            "```json\n" +
            "{\n" +
            "  \"Payment\": {\n" +
            "    \"Gateways\": [\n" +
            "      {\n" +
            "        \"Region\": \"eu-west-1\",\n" +
            "        \"Provider\": \"Stripe\",\n" +
            "        \"BaseUrl\": \"https://api.stripe.com/eu/\",\n" +
            "        \"Priority\": 1,\n" +
            "        \"HealthTag\": \"payment-stripe-eu\",\n" +
            "        \"TimeoutSeconds\": 5,\n" +
            "        \"CircuitBreakerThreshold\": 5\n" +
            "      },\n" +
            "      {\n" +
            "        \"Region\": \"us-east-1\",\n" +
            "        \"Provider\": \"Stripe\",\n" +
            "        \"BaseUrl\": \"https://api.stripe.com/us/\",\n" +
            "        \"Priority\": 2,\n" +
            "        \"HealthTag\": \"payment-stripe-us\",\n" +
            "        \"TimeoutSeconds\": 5,\n" +
            "        \"CircuitBreakerThreshold\": 5\n" +
            "      },\n" +
            "      {\n" +
            "        \"Region\": \"ap-southeast-1\",\n" +
            "        \"Provider\": \"Adyen\",\n" +
            "        \"BaseUrl\": \"https://checkout.adyen.com/apac/\",\n" +
            "        \"Priority\": 3,\n" +
            "        \"HealthTag\": \"payment-adyen-apac\",\n" +
            "        \"TimeoutSeconds\": 8,\n" +
            "        \"CircuitBreakerThreshold\": 3\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}\n" +
            "```",
            "Grounded · 4 references");

        // ── Turn 24: Database migration strategy ──
        AddUser("Speaking of resilience — we need to plan a database migration from SQL Server to PostgreSQL. The checkout DB is about 2TB. How should we approach this without downtime?");

        AddAssistant(
            "## Zero-Downtime SQL Server → PostgreSQL Migration\n\n" +
            "For a 2TB checkout database, I recommend a **dual-write + shadow-read** strategy " +
            "with a phased cutover.\n\n" +
            "### Phase 1: Schema Migration (Week 1-2)\n\n" +
            "Key type mappings:\n\n" +
            "| SQL Server | PostgreSQL | Notes |\n" +
            "| --- | --- | --- |\n" +
            "| `NVARCHAR(MAX)` | `TEXT` | No length limit |\n" +
            "| `DATETIME2` | `TIMESTAMPTZ` | Always use timezone-aware |\n" +
            "| `UNIQUEIDENTIFIER` | `UUID` | Native support |\n" +
            "| `MONEY` | `NUMERIC(19,4)` | Exact decimal |\n" +
            "| `BIT` | `BOOLEAN` | Direct mapping |\n" +
            "| `VARBINARY(MAX)` | `BYTEA` | Binary data |\n" +
            "| `IDENTITY` | `GENERATED ALWAYS AS IDENTITY` | Standard SQL |\n" +
            "| `NONCLUSTERED INDEX` | `CREATE INDEX` | B-tree default |\n" +
            "| `CLUSTERED INDEX` | N/A (use `CLUSTER`) | PG tables are heap |\n\n" +
            "### Phase 2: Dual-Write Abstraction (Week 2-3)\n\n" +
            "```csharp\n" +
            "public sealed class DualWriteOrderRepository : IOrderRepository\n" +
            "{\n" +
            "    private readonly SqlServerOrderRepository _primary;\n" +
            "    private readonly PostgresOrderRepository _shadow;\n" +
            "    private readonly IFeatureManager _features;\n" +
            "    private readonly ILogger _logger;\n" +
            "\n" +
            "    public async Task<Order> CreateAsync(Order order)\n" +
            "    {\n" +
            "        // Always write to SQL Server (source of truth)\n" +
            "        var result = await _primary.CreateAsync(order);\n" +
            "\n" +
            "        // Shadow-write to PostgreSQL (async, non-blocking)\n" +
            "        _ = Task.Run(async () =>\n" +
            "        {\n" +
            "            try\n" +
            "            {\n" +
            "                await _shadow.CreateAsync(order);\n" +
            "            }\n" +
            "            catch (Exception ex)\n" +
            "            {\n" +
            "                _logger.LogWarning(ex,\n" +
            "                    \"Shadow write failed for order {OrderId}\", order.Id);\n" +
            "            }\n" +
            "        });\n" +
            "\n" +
            "        return result;\n" +
            "    }\n" +
            "\n" +
            "    public async Task<Order?> GetByIdAsync(Guid orderId)\n" +
            "    {\n" +
            "        if (await _features.IsEnabledAsync(\"ReadFromPostgres\"))\n" +
            "        {\n" +
            "            // Shadow-read: compare results for validation\n" +
            "            var pgResult = await _shadow.GetByIdAsync(orderId);\n" +
            "            var sqlResult = await _primary.GetByIdAsync(orderId);\n" +
            "\n" +
            "            if (!OrderComparer.AreEqual(pgResult, sqlResult))\n" +
            "            {\n" +
            "                _logger.LogError(\n" +
            "                    \"Data mismatch for order {OrderId}\", orderId);\n" +
            "            }\n" +
            "\n" +
            "            return sqlResult; // Always return SQL Server result\n" +
            "        }\n" +
            "\n" +
            "        return await _primary.GetByIdAsync(orderId);\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### Phase 3: Data Backfill (Week 3-4)\n\n" +
            "```sql\n" +
            "-- PostgreSQL: Create partitioned table for efficient backfill\n" +
            "CREATE TABLE orders (\n" +
            "    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),\n" +
            "    checkout_session_id TEXT NOT NULL,\n" +
            "    customer_id UUID NOT NULL,\n" +
            "    total NUMERIC(19, 4) NOT NULL,\n" +
            "    currency TEXT NOT NULL DEFAULT 'USD',\n" +
            "    status TEXT NOT NULL DEFAULT 'pending',\n" +
            "    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),\n" +
            "    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()\n" +
            ") PARTITION BY RANGE (created_at);\n" +
            "\n" +
            "-- Create monthly partitions\n" +
            "CREATE TABLE orders_2025_01 PARTITION OF orders\n" +
            "    FOR VALUES FROM ('2025-01-01') TO ('2025-02-01');\n" +
            "CREATE TABLE orders_2025_02 PARTITION OF orders\n" +
            "    FOR VALUES FROM ('2025-02-01') TO ('2025-03-01');\n" +
            "-- ... continue for all months\n" +
            "\n" +
            "-- Indexes (created after bulk load for speed)\n" +
            "CREATE INDEX CONCURRENTLY idx_orders_session\n" +
            "    ON orders (checkout_session_id, created_at DESC)\n" +
            "    INCLUDE (id, total, status);\n" +
            "\n" +
            "CREATE INDEX CONCURRENTLY idx_orders_customer\n" +
            "    ON orders (customer_id, created_at DESC);\n" +
            "```\n\n" +
            "### Phase 4: Cutover (Week 5)\n\n" +
            "| Step | Action | Rollback |\n" +
            "| --- | --- | --- |\n" +
            "| 1 | Enable shadow reads (10%) | Disable feature flag |\n" +
            "| 2 | Validate mismatch rate < 0.01% | Revert to SQL-only reads |\n" +
            "| 3 | Shadow reads at 100% for 24h | Same |\n" +
            "| 4 | Promote PostgreSQL to primary | Swap flag to SQL Server |\n" +
            "| 5 | Dual-write reversed (PG → SQL) | Already writing both |\n" +
            "| 6 | Decommission SQL Server (Week 8) | Restore from backup |\n\n" +
            "### Timeline\n\n" +
            "```\n" +
            "Week 1-2    Week 2-3    Week 3-4     Week 5       Week 6-7     Week 8\n" +
            "─────────┬───────────┬───────────┬────────────┬────────────┬──────────\n" +
            " Schema  │Dual-Write │ Backfill  │  Cutover   │   Soak     │Decomm SQL\n" +
            " mapping │abstraction│ 2TB data  │ PG primary │ monitoring │  Server\n" +
            "─────────┴───────────┴───────────┴────────────┴────────────┴──────────\n" +
            "```",
            "Grounded · 5 references");

        // ── Turn 25: Performance tuning in PostgreSQL ──
        AddUser("What about PostgreSQL-specific performance tuning? Our DBA is more of a SQL Server person.");

        AddAssistant(
            "## PostgreSQL Performance Tuning Guide\n\n" +
            "Here are the key settings for a 2TB checkout database with high write throughput:\n\n" +
            "### postgresql.conf — Critical Settings\n\n" +
            "```ini\n" +
            "# ── Memory ──\n" +
            "shared_buffers = 16GB              # 25% of RAM (64GB server)\n" +
            "effective_cache_size = 48GB         # 75% of RAM\n" +
            "work_mem = 256MB                    # Per-sort/hash operation\n" +
            "maintenance_work_mem = 2GB          # For VACUUM, index builds\n" +
            "huge_pages = on                     # Reduce TLB misses\n" +
            "\n" +
            "# ── WAL & Checkpoints ──\n" +
            "wal_level = replica\n" +
            "max_wal_size = 8GB                  # Reduce checkpoint frequency\n" +
            "min_wal_size = 2GB\n" +
            "checkpoint_completion_target = 0.9  # Spread I/O\n" +
            "wal_compression = zstd              # Reduce WAL volume (PG 15+)\n" +
            "wal_buffers = 64MB\n" +
            "\n" +
            "# ── Parallelism ──\n" +
            "max_worker_processes = 16\n" +
            "max_parallel_workers_per_gather = 4\n" +
            "max_parallel_workers = 12\n" +
            "max_parallel_maintenance_workers = 4\n" +
            "\n" +
            "# ── Planner ──\n" +
            "random_page_cost = 1.1              # SSD storage\n" +
            "effective_io_concurrency = 200      # NVMe drives\n" +
            "jit = on                            # JIT for complex queries\n" +
            "jit_above_cost = 100000\n" +
            "\n" +
            "# ── Autovacuum (aggressive for OLTP) ──\n" +
            "autovacuum_max_workers = 6\n" +
            "autovacuum_naptime = 10s\n" +
            "autovacuum_vacuum_threshold = 50\n" +
            "autovacuum_vacuum_scale_factor = 0.02  # 2% changed rows\n" +
            "autovacuum_analyze_threshold = 50\n" +
            "autovacuum_analyze_scale_factor = 0.01\n" +
            "```\n\n" +
            "### SQL Server vs PostgreSQL — DBA Cheat Sheet\n\n" +
            "| SQL Server Concept | PostgreSQL Equivalent | Key Difference |\n" +
            "| --- | --- | --- |\n" +
            "| Clustered index | `CLUSTER` command | PG tables are always heap |\n" +
            "| `INCLUDE` columns | `INCLUDE` (PG 11+) | Same syntax |\n" +
            "| `NOLOCK` hint | `SET transaction_isolation` | No dirty reads in PG |\n" +
            "| Execution plan | `EXPLAIN (ANALYZE, BUFFERS)` | More detail than SSMS |\n" +
            "| `sp_who2` | `pg_stat_activity` | View for active queries |\n" +
            "| `DBCC CHECKDB` | `amcheck` extension | Corruption detection |\n" +
            "| Wait stats | `pg_stat_wait_event` | Similar concept |\n" +
            "| TempDB | `temp_tablespace` | Per-session temp tables |\n" +
            "| SQL Agent | `pg_cron` extension | Scheduled jobs |\n" +
            "| AlwaysOn AG | Streaming replication | Built-in |\n" +
            "| SSMS | pgAdmin / DBeaver | IDE equivalent |\n\n" +
            "### Monitoring query: top slow queries\n\n" +
            "```sql\n" +
            "-- Requires pg_stat_statements extension\n" +
            "SELECT\n" +
            "    LEFT(query, 80) AS query_preview,\n" +
            "    calls,\n" +
            "    round(mean_exec_time::numeric, 2) AS avg_ms,\n" +
            "    round(total_exec_time::numeric, 2) AS total_ms,\n" +
            "    rows,\n" +
            "    round((shared_blks_hit * 100.0 /\n" +
            "        NULLIF(shared_blks_hit + shared_blks_read, 0))::numeric, 1)\n" +
            "        AS cache_hit_pct\n" +
            "FROM pg_stat_statements\n" +
            "ORDER BY total_exec_time DESC\n" +
            "LIMIT 20;\n" +
            "```\n\n" +
            "### Monitoring query: table bloat\n\n" +
            "```sql\n" +
            "SELECT\n" +
            "    schemaname || '.' || relname AS table_name,\n" +
            "    pg_size_pretty(pg_total_relation_size(relid)) AS total_size,\n" +
            "    n_live_tup AS live_rows,\n" +
            "    n_dead_tup AS dead_rows,\n" +
            "    round(n_dead_tup * 100.0 /\n" +
            "        NULLIF(n_live_tup + n_dead_tup, 0), 1) AS dead_pct,\n" +
            "    last_autovacuum\n" +
            "FROM pg_stat_user_tables\n" +
            "WHERE n_live_tup > 10000\n" +
            "ORDER BY n_dead_tup DESC\n" +
            "LIMIT 15;\n" +
            "```",
            "Grounded · 3 references");

        // ── Turn 26: API versioning ──
        AddUser("We've also been discussing API versioning. Our public checkout API has external partners consuming v1 and we need to ship v2 with breaking changes. Best approach?");

        AddAssistant(
            "## API Versioning Strategy\n\n" +
            "Use **URL-based versioning** for public APIs (clearest for external consumers) " +
            "combined with a compatibility layer.\n\n" +
            "### Versioned controller setup\n\n" +
            "```csharp\n" +
            "// V1 — existing contract, maintained for backwards compatibility\n" +
            "[ApiController]\n" +
            "[Route(\"api/v1/checkout\")]\n" +
            "public sealed class CheckoutV1Controller : ControllerBase\n" +
            "{\n" +
            "    private readonly ICheckoutService _checkout;\n" +
            "\n" +
            "    [HttpPost(\"confirm\")]\n" +
            "    public async Task<ActionResult<V1.OrderConfirmation>> Confirm(\n" +
            "        [FromBody] V1.CartRequest request)\n" +
            "    {\n" +
            "        // Map V1 request to internal model\n" +
            "        var internalCart = V1Mapper.ToInternal(request);\n" +
            "        var result = await _checkout.ProcessAsync(internalCart);\n" +
            "        return Ok(V1Mapper.ToV1Response(result));\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "// V2 — new contract with expanded capabilities\n" +
            "[ApiController]\n" +
            "[Route(\"api/v2/checkout\")]\n" +
            "public sealed class CheckoutV2Controller : ControllerBase\n" +
            "{\n" +
            "    private readonly ICheckoutService _checkout;\n" +
            "\n" +
            "    [HttpPost(\"confirm\")]\n" +
            "    public async Task<ActionResult<V2.OrderConfirmation>> Confirm(\n" +
            "        [FromBody] V2.CartRequest request)\n" +
            "    {\n" +
            "        // V2 request maps directly to internal model\n" +
            "        var result = await _checkout.ProcessAsync(request.ToInternal());\n" +
            "        return Ok(V2.OrderConfirmation.FromResult(result));\n" +
            "    }\n" +
            "\n" +
            "    [HttpPost(\"preview\")]  // New in V2\n" +
            "    public async Task<ActionResult<V2.OrderPreview>> Preview(\n" +
            "        [FromBody] V2.CartRequest request)\n" +
            "    {\n" +
            "        var preview = await _checkout.PreviewAsync(request.ToInternal());\n" +
            "        return Ok(preview);\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### Breaking changes between V1 → V2\n\n" +
            "| Change | V1 | V2 | Migration |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Cart items | `Items[]` (flat) | `LineItems[]` (nested) | Auto-map in V1 controller |\n" +
            "| Currency | Implicit USD | Required `currency` field | Default to USD in V1 |\n" +
            "| Response | `{ orderId, status }` | `{ order: { id, status, total, items } }` | V1 returns flattened |\n" +
            "| Auth | API key | OAuth 2.0 + API key | V1 continues API key |\n" +
            "| Idempotency | Optional header | Required `Idempotency-Key` | V1 generates if missing |\n" +
            "| Pagination | `offset/limit` | Cursor-based `after/first` | V1 translates |\n\n" +
            "### Deprecation timeline\n\n" +
            "```\n" +
            "Today         +3 months     +6 months    +9 months    +12 months\n" +
            "──────────────┬─────────────┬────────────┬────────────┬──────────\n" +
            " V2 launch    │ V1 sunset   │ V1 warns   │ V1 read-   │ V1\n" +
            " V1 supported │ announced   │ in headers │ only mode  │ removed\n" +
            "──────────────┴─────────────┴────────────┴────────────┴──────────\n" +
            "```\n\n" +
            "### Deprecation header middleware\n\n" +
            "```csharp\n" +
            "public sealed class ApiDeprecationMiddleware\n" +
            "{\n" +
            "    private readonly RequestDelegate _next;\n" +
            "\n" +
            "    public async Task InvokeAsync(HttpContext context)\n" +
            "    {\n" +
            "        if (context.Request.Path.StartsWithSegments(\"/api/v1\"))\n" +
            "        {\n" +
            "            context.Response.Headers.Append(\n" +
            "                \"Deprecation\", \"2026-09-01T00:00:00Z\");\n" +
            "            context.Response.Headers.Append(\n" +
            "                \"Sunset\", \"2027-03-01T00:00:00Z\");\n" +
            "            context.Response.Headers.Append(\n" +
            "                \"Link\",\n" +
            "                \"</api/v2/checkout>; rel=\\\"successor-version\\\"\");\n" +
            "        }\n" +
            "        await _next(context);\n" +
            "    }\n" +
            "}\n" +
            "```",
            "Grounded · 3 references");

        // ── Turn 27: Load testing ──
        AddUser("Before we launch v2, we should load test it. What tools and approach do you recommend?");

        AddAssistant(
            "## Load Testing Strategy\n\n" +
            "Use a combination of **k6** for scripted load tests and **NBomber** for .NET-native scenarios.\n\n" +
            "### k6 Script — Checkout Flow\n\n" +
            "```javascript\n" +
            "import http from 'k6/http';\n" +
            "import { check, sleep } from 'k6';\n" +
            "import { Rate, Trend } from 'k6/metrics';\n" +
            "\n" +
            "const errorRate = new Rate('checkout_errors');\n" +
            "const checkoutDuration = new Trend('checkout_duration');\n" +
            "\n" +
            "export const options = {\n" +
            "  scenarios: {\n" +
            "    ramp_up: {\n" +
            "      executor: 'ramping-vus',\n" +
            "      startVUs: 10,\n" +
            "      stages: [\n" +
            "        { duration: '2m', target: 100 },   // Warm up\n" +
            "        { duration: '5m', target: 500 },   // Ramp to target\n" +
            "        { duration: '10m', target: 500 },  // Sustained load\n" +
            "        { duration: '3m', target: 1000 },  // Spike test\n" +
            "        { duration: '5m', target: 500 },   // Back to normal\n" +
            "        { duration: '2m', target: 0 },     // Cool down\n" +
            "      ],\n" +
            "    },\n" +
            "  },\n" +
            "  thresholds: {\n" +
            "    http_req_duration: ['p(95)<250', 'p(99)<500'],\n" +
            "    checkout_errors: ['rate<0.01'],\n" +
            "    checkout_duration: ['p(95)<300'],\n" +
            "  },\n" +
            "};\n" +
            "\n" +
            "const BASE_URL = __ENV.BASE_URL || 'https://checkout-staging.corp.internal';\n" +
            "\n" +
            "export default function () {\n" +
            "  const cart = {\n" +
            "    lineItems: [\n" +
            "      { sku: `WIDGET-${Math.floor(Math.random() * 1000)}`, quantity: 1 },\n" +
            "      { sku: `GADGET-${Math.floor(Math.random() * 500)}`, quantity: 2 },\n" +
            "    ],\n" +
            "    currency: 'USD',\n" +
            "    idempotencyKey: `k6-${__VU}-${__ITER}-${Date.now()}`,\n" +
            "  };\n" +
            "\n" +
            "  const response = http.post(\n" +
            "    `${BASE_URL}/api/v2/checkout/confirm`,\n" +
            "    JSON.stringify(cart),\n" +
            "    { headers: { 'Content-Type': 'application/json' } }\n" +
            "  );\n" +
            "\n" +
            "  const success = check(response, {\n" +
            "    'status is 200': (r) => r.status === 200,\n" +
            "    'has order id': (r) => JSON.parse(r.body).order?.id !== undefined,\n" +
            "  });\n" +
            "\n" +
            "  errorRate.add(!success);\n" +
            "  checkoutDuration.add(response.timings.duration);\n" +
            "\n" +
            "  sleep(Math.random() * 2 + 0.5);\n" +
            "}\n" +
            "```\n\n" +
            "### NBomber — .NET Integration Test\n\n" +
            "```csharp\n" +
            "var scenario = Scenario.Create(\"checkout_v2\", async context =>\n" +
            "{\n" +
            "    var cart = GenerateRandomCart();\n" +
            "    var json = JsonSerializer.Serialize(cart);\n" +
            "    var content = new StringContent(json, Encoding.UTF8, \"application/json\");\n" +
            "\n" +
            "    var response = await _httpClient.PostAsync(\n" +
            "        \"/api/v2/checkout/confirm\", content);\n" +
            "\n" +
            "    return response.IsSuccessStatusCode\n" +
            "        ? Response.Ok(statusCode: (int)response.StatusCode)\n" +
            "        : Response.Fail(statusCode: (int)response.StatusCode);\n" +
            "})\n" +
            ".WithLoadSimulations(\n" +
            "    Simulation.RampingInject(\n" +
            "        rate: 100, interval: TimeSpan.FromSeconds(1),\n" +
            "        during: TimeSpan.FromMinutes(5)),\n" +
            "    Simulation.Inject(\n" +
            "        rate: 500, interval: TimeSpan.FromSeconds(1),\n" +
            "        during: TimeSpan.FromMinutes(10)));\n" +
            "\n" +
            "NBomberRunner\n" +
            "    .RegisterScenarios(scenario)\n" +
            "    .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)\n" +
            "    .Run();\n" +
            "```\n\n" +
            "### Test types and targets\n\n" +
            "| Test Type | VUs | Duration | Goal |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Smoke | 5 | 1 min | Verify test script works |\n" +
            "| Load | 500 | 10 min | Validate SLOs under normal peak |\n" +
            "| Stress | 2,000 | 15 min | Find breaking point |\n" +
            "| Spike | 100 → 2,000 → 100 | 5 min | Autoscaler response |\n" +
            "| Soak | 300 | 4 hours | Memory leaks, connection exhaustion |\n" +
            "| Breakpoint | Ramp to failure | ~30 min | Determine max capacity |\n",
            "Grounded · 2 references");

        // ── Turn 28: Rate limiting ──
        AddUser("We should add rate limiting to v2 as well. What do you suggest?");

        AddAssistant(
            "## Rate Limiting for Checkout API v2\n\n" +
            "Use ASP.NET Core's built-in rate limiter with tiered policies:\n\n" +
            "### Configuration\n\n" +
            "```csharp\n" +
            "builder.Services.AddRateLimiter(options =>\n" +
            "{\n" +
            "    // Global: 1000 requests/minute per IP\n" +
            "    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(\n" +
            "        context =>\n" +
            "        {\n" +
            "            var ip = context.Connection.RemoteIpAddress?.ToString() ?? \"unknown\";\n" +
            "            return RateLimitPartition.GetFixedWindowLimiter(ip,\n" +
            "                _ => new FixedWindowRateLimiterOptions\n" +
            "                {\n" +
            "                    PermitLimit = 1000,\n" +
            "                    Window = TimeSpan.FromMinutes(1),\n" +
            "                    QueueLimit = 10\n" +
            "                });\n" +
            "        });\n" +
            "\n" +
            "    // Partner tier: token bucket for sustained throughput\n" +
            "    options.AddPolicy(\"partner\", context =>\n" +
            "    {\n" +
            "        var apiKey = context.Request.Headers[\"X-Api-Key\"].ToString();\n" +
            "        return RateLimitPartition.GetTokenBucketLimiter(apiKey,\n" +
            "            _ => new TokenBucketRateLimiterOptions\n" +
            "            {\n" +
            "                TokenLimit = 200,\n" +
            "                TokensPerPeriod = 50,\n" +
            "                ReplenishmentPeriod = TimeSpan.FromSeconds(10),\n" +
            "                QueueLimit = 25,\n" +
            "                AutoReplenishment = true\n" +
            "            });\n" +
            "    });\n" +
            "\n" +
            "    // Checkout confirm: sliding window for burst protection\n" +
            "    options.AddPolicy(\"checkout-confirm\", context =>\n" +
            "    {\n" +
            "        var customerId = context.User.FindFirst(\"sub\")?.Value ?? \"anon\";\n" +
            "        return RateLimitPartition.GetSlidingWindowLimiter(customerId,\n" +
            "            _ => new SlidingWindowRateLimiterOptions\n" +
            "            {\n" +
            "                PermitLimit = 10,\n" +
            "                Window = TimeSpan.FromMinutes(1),\n" +
            "                SegmentsPerWindow = 6,\n" +
            "                QueueLimit = 2\n" +
            "            });\n" +
            "    });\n" +
            "\n" +
            "    options.OnRejected = async (context, ct) =>\n" +
            "    {\n" +
            "        context.HttpContext.Response.StatusCode = 429;\n" +
            "        if (context.Lease.TryGetMetadata(\n" +
            "            MetadataName.RetryAfter, out var retryAfter))\n" +
            "        {\n" +
            "            context.HttpContext.Response.Headers.RetryAfter =\n" +
            "                ((int)retryAfter.TotalSeconds).ToString();\n" +
            "        }\n" +
            "        await context.HttpContext.Response.WriteAsJsonAsync(new\n" +
            "        {\n" +
            "            error = \"rate_limit_exceeded\",\n" +
            "            message = \"Too many requests. Please retry after the indicated period.\",\n" +
            "            retryAfterSeconds = retryAfter.TotalSeconds\n" +
            "        }, ct);\n" +
            "    };\n" +
            "});\n" +
            "```\n\n" +
            "### Rate limit tiers\n\n" +
            "| Tier | Limit | Window | Algorithm | Use Case |\n" +
            "| --- | --- | --- | --- | --- |\n" +
            "| Anonymous | 100/min | Fixed | Fixed window | Public browse |\n" +
            "| Authenticated | 1,000/min | Fixed | Fixed window | Logged-in users |\n" +
            "| Partner Basic | 200 bucket | Token bucket | Sustained throughput | Small partners |\n" +
            "| Partner Premium | 2,000 bucket | Token bucket | High throughput | Enterprise partners |\n" +
            "| Checkout confirm | 10/min | Sliding | Per-customer burst protection | Anti-fraud |\n\n" +
            "### Response headers (always included)\n\n" +
            "```\n" +
            "X-RateLimit-Limit: 1000\n" +
            "X-RateLimit-Remaining: 847\n" +
            "X-RateLimit-Reset: 2026-03-02T15:50:00Z\n" +
            "Retry-After: 12   (only on 429)\n" +
            "```",
            "Grounded · 2 references");

        // ── System note ──
        AddSystem("50% deployment gate passed. All metrics within SLO thresholds.");

        // ── Turn 29: Good news ──
        AddUser("50% looks good! Go ahead and promote to 100%.");

        AddAssistant(
            "## Promoting to 100% — Full Rollout Initiated ✅\n\n" +
            "I've triggered the promotion to full traffic. Here's the current state:\n\n" +
            "| Stage | Status | Duration | Metrics |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Canary (10%) | ✅ Completed | 32 min | p95: 95ms, errors: 0.1% |\n" +
            "| Expansion (50%) | ✅ Completed | 68 min | p95: 102ms, errors: 0.08% |\n" +
            "| Full (100%) | 🔄 Rolling out | — | Pending |\n\n" +
            "### Post-deployment monitoring dashboard\n\n" +
            "I've set up real-time alerts for the 24-hour soak period:\n\n" +
            "```yaml\n" +
            "# soak-monitoring.yaml\n" +
            "alerts:\n" +
            "  - name: PostDeployP95Regression\n" +
            "    query: |\n" +
            "      histogram_quantile(0.95,\n" +
            "        rate(checkout_duration_seconds_bucket[5m])) > 0.15\n" +
            "    for: 3m\n" +
            "    severity: page\n" +
            "    runbook: runbooks/post-deploy-regression.md\n" +
            "\n" +
            "  - name: PostDeployMemoryLeak\n" +
            "    query: |\n" +
            "      delta(process_resident_memory_bytes[1h]) > 500e6\n" +
            "    for: 10m\n" +
            "    severity: warning\n" +
            "    runbook: runbooks/memory-leak-investigation.md\n" +
            "\n" +
            "  - name: PostDeployConnectionExhaustion\n" +
            "    query: |\n" +
            "      dotnet_pool_active_connections\n" +
            "        / dotnet_pool_max_connections > 0.85\n" +
            "    for: 5m\n" +
            "    severity: warning\n" +
            "    runbook: runbooks/connection-pool.md\n" +
            "```\n\n" +
            "I'll keep watching. The 24-hour soak clock starts now. " +
            "If everything holds, we can close out IR-4471 tomorrow.",
            "Grounded · live metrics");

        // ── Turn 30: Architecture discussion ──
        AddUser("While we wait on the soak, let's talk about something I've been thinking about. Should we break checkout into smaller microservices?");

        AddAssistant(
            "## Checkout Service Decomposition Analysis\n\n" +
            "Great question. Let me analyze the current monolith and propose a bounded-context split.\n\n" +
            "### Current Checkout Monolith\n\n" +
            "```\n" +
            "┌─────────────────────────────────────────────────────┐\n" +
            "│                 checkout-api                         │\n" +
            "│                                                     │\n" +
            "│  ┌───────────┐  ┌──────────────┐  ┌─────────────┐  │\n" +
            "│  │   Cart     │  │   Payment    │  │  Inventory  │  │\n" +
            "│  │  Manager   │  │  Processor   │  │  Reserver   │  │\n" +
            "│  └───────────┘  └──────────────┘  └─────────────┘  │\n" +
            "│  ┌───────────┐  ┌──────────────┐  ┌─────────────┐  │\n" +
            "│  │  Pricing   │  │  Fulfillment │  │ Notification│  │\n" +
            "│  │  Engine    │  │  Orchestrator│  │   Sender    │  │\n" +
            "│  └───────────┘  └──────────────┘  └─────────────┘  │\n" +
            "└─────────────────────────────────────────────────────┘\n" +
            "```\n\n" +
            "### Proposed Decomposition\n\n" +
            "```\n" +
            "┌──────────────┐    ┌───────────────┐    ┌──────────────┐\n" +
            "│  Cart API    │───▶│ Checkout       │───▶│  Payment     │\n" +
            "│              │    │ Orchestrator   │    │  Service     │\n" +
            "│ • CRUD items │    │                │    │ • Charge     │\n" +
            "│ • Price calc │    │ • Saga coord.  │    │ • Refund     │\n" +
            "│ • Tax rules  │    │ • Idempotency  │    │ • Gateway    │\n" +
            "└──────────────┘    └───────┬───────┘    └──────────────┘\n" +
            "                           │\n" +
            "              ┌─────────────┼─────────────┐\n" +
            "              ▼             ▼             ▼\n" +
            "    ┌──────────────┐ ┌────────────┐ ┌──────────────┐\n" +
            "    │  Inventory   │ │ Fulfillment│ │ Notification │\n" +
            "    │  Service     │ │  Service   │ │   Service    │\n" +
            "    │ • Reserve    │ │ • Ship     │ │ • Email      │\n" +
            "    │ • Release    │ │ • Track    │ │ • SMS        │\n" +
            "    │ • Stock sync │ │ • Returns  │ │ • Webhooks   │\n" +
            "    └──────────────┘ └────────────┘ └──────────────┘\n" +
            "```\n\n" +
            "### Saga pattern for the checkout flow\n\n" +
            "```csharp\n" +
            "public sealed class CheckoutSaga\n" +
            "{\n" +
            "    private readonly IInventoryService _inventory;\n" +
            "    private readonly IPaymentService _payment;\n" +
            "    private readonly IFulfillmentService _fulfillment;\n" +
            "    private readonly INotificationService _notifications;\n" +
            "\n" +
            "    public async Task<CheckoutResult> ExecuteAsync(\n" +
            "        CheckoutRequest request, CancellationToken ct)\n" +
            "    {\n" +
            "        var sagaId = Guid.NewGuid();\n" +
            "        var compensations = new Stack<Func<Task>>();\n" +
            "\n" +
            "        try\n" +
            "        {\n" +
            "            // Step 1: Reserve inventory\n" +
            "            var reservation = await _inventory.ReserveAsync(\n" +
            "                request.Items, sagaId, ct);\n" +
            "            compensations.Push(() =>\n" +
            "                _inventory.ReleaseAsync(reservation.Id, ct));\n" +
            "\n" +
            "            // Step 2: Charge payment\n" +
            "            var charge = await _payment.ChargeAsync(\n" +
            "                request.PaymentMethod, reservation.Total, sagaId, ct);\n" +
            "            compensations.Push(() =>\n" +
            "                _payment.RefundAsync(charge.Id, ct));\n" +
            "\n" +
            "            // Step 3: Create fulfillment order\n" +
            "            var fulfillment = await _fulfillment.CreateAsync(\n" +
            "                request.ShippingAddress, reservation, sagaId, ct);\n" +
            "            compensations.Push(() =>\n" +
            "                _fulfillment.CancelAsync(fulfillment.Id, ct));\n" +
            "\n" +
            "            // Step 4: Send confirmation (no compensation needed)\n" +
            "            await _notifications.SendConfirmationAsync(\n" +
            "                request.CustomerEmail, fulfillment, ct);\n" +
            "\n" +
            "            return CheckoutResult.Success(fulfillment.OrderId);\n" +
            "        }\n" +
            "        catch (Exception ex)\n" +
            "        {\n" +
            "            // Compensate in reverse order\n" +
            "            while (compensations.Count > 0)\n" +
            "            {\n" +
            "                try { await compensations.Pop()(); }\n" +
            "                catch (Exception compEx)\n" +
            "                {\n" +
            "                    Log.Error(compEx,\n" +
            "                        \"Compensation failed in saga {SagaId}\", sagaId);\n" +
            "                }\n" +
            "            }\n" +
            "            return CheckoutResult.Failed(ex.Message);\n" +
            "        }\n" +
            "    }\n" +
            "}\n" +
            "```\n\n" +
            "### Split vs. keep — decision matrix\n\n" +
            "| Factor | Keep Monolith | Split to Microservices |\n" +
            "| --- | --- | --- |\n" +
            "| Team size | < 8 developers | > 8, multiple teams |\n" +
            "| Deploy cadence | Weekly | Multiple per day |\n" +
            "| Scaling needs | Uniform | Components scale differently |\n" +
            "| Data coupling | Tight | Loose / event-driven |\n" +
            "| Operational maturity | Low | High (K8s, observability, CI/CD) |\n\n" +
            "### My recommendation\n\n" +
            "Given your current team size and operational maturity after this incident, " +
            "I'd suggest a **modular monolith first** approach:\n" +
            "1. Enforce module boundaries in the monolith (separate assemblies)\n" +
            "2. Communicate via internal interfaces, not direct DB access\n" +
            "3. Extract Payment and Notification as services first (highest independence)\n" +
            "4. Keep Cart + Inventory + Fulfillment together until team grows\n",
            "Grounded · 5 references");

        // ── Turn 31: CI/CD pipeline ──
        AddUser("That makes sense — modular monolith first. Can you show me what the CI/CD pipeline should look like for this?");

        AddAssistant(
            "## CI/CD Pipeline Design\n\n" +
            "Here's a GitHub Actions pipeline with quality gates, security scanning, " +
            "and staged deployment:\n\n" +
            "### Pipeline YAML\n\n" +
            "```yaml\n" +
            "name: checkout-ci-cd\n" +
            "\n" +
            "on:\n" +
            "  push:\n" +
            "    branches: [main]\n" +
            "  pull_request:\n" +
            "    branches: [main]\n" +
            "\n" +
            "env:\n" +
            "  REGISTRY: ghcr.io\n" +
            "  IMAGE_NAME: corp/checkout-api\n" +
            "  DOTNET_VERSION: '8.0'\n" +
            "\n" +
            "jobs:\n" +
            "  build-and-test:\n" +
            "    runs-on: ubuntu-latest\n" +
            "    steps:\n" +
            "      - uses: actions/checkout@v4\n" +
            "\n" +
            "      - uses: actions/setup-dotnet@v4\n" +
            "        with:\n" +
            "          dotnet-version: ${{ env.DOTNET_VERSION }}\n" +
            "\n" +
            "      - name: Restore\n" +
            "        run: dotnet restore\n" +
            "\n" +
            "      - name: Build\n" +
            "        run: dotnet build -c Release --no-restore\n" +
            "\n" +
            "      - name: Unit Tests\n" +
            "        run: dotnet test -c Release --no-build\n" +
            "             --logger trx\n" +
            "             --collect:\"XPlat Code Coverage\"\n" +
            "\n" +
            "      - name: Integration Tests\n" +
            "        run: dotnet test tests/Integration -c Release\n" +
            "             --no-build --logger trx\n" +
            "        env:\n" +
            "          TESTCONTAINERS_RYUK_DISABLED: true\n" +
            "\n" +
            "      - name: Coverage Gate\n" +
            "        uses: codecov/codecov-action@v4\n" +
            "        with:\n" +
            "          fail_ci_if_error: true\n" +
            "          token: ${{ secrets.CODECOV_TOKEN }}\n" +
            "\n" +
            "  security-scan:\n" +
            "    runs-on: ubuntu-latest\n" +
            "    needs: build-and-test\n" +
            "    steps:\n" +
            "      - uses: actions/checkout@v4\n" +
            "\n" +
            "      - name: Dependency Audit\n" +
            "        run: dotnet list package --vulnerable --include-transitive\n" +
            "\n" +
            "      - name: SAST Scan\n" +
            "        uses: github/codeql-action/analyze@v3\n" +
            "        with:\n" +
            "          languages: csharp\n" +
            "\n" +
            "      - name: Container Scan\n" +
            "        uses: aquasecurity/trivy-action@master\n" +
            "        with:\n" +
            "          image-ref: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}\n" +
            "          severity: 'CRITICAL,HIGH'\n" +
            "          exit-code: '1'\n" +
            "\n" +
            "  deploy-staging:\n" +
            "    runs-on: ubuntu-latest\n" +
            "    needs: [build-and-test, security-scan]\n" +
            "    if: github.ref == 'refs/heads/main'\n" +
            "    environment: staging\n" +
            "    steps:\n" +
            "      - name: Deploy to Staging\n" +
            "        run: |\n" +
            "          kubectl set image deployment/checkout-api \\\n" +
            "            checkout=${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }} \\\n" +
            "            --namespace=staging\n" +
            "          kubectl rollout status deployment/checkout-api \\\n" +
            "            --namespace=staging --timeout=300s\n" +
            "\n" +
            "      - name: Smoke Tests\n" +
            "        run: |\n" +
            "          k6 run tests/load/smoke.js \\\n" +
            "            -e BASE_URL=https://checkout-staging.corp.internal\n" +
            "\n" +
            "  deploy-production:\n" +
            "    runs-on: ubuntu-latest\n" +
            "    needs: deploy-staging\n" +
            "    environment: production\n" +
            "    steps:\n" +
            "      - name: Canary (10%%)\n" +
            "        run: |\n" +
            "          kubectl argo rollouts set-weight checkout-api 10 \\\n" +
            "            --namespace=production\n" +
            "\n" +
            "      - name: Wait for Canary Validation\n" +
            "        run: sleep 1800  # 30-minute soak\n" +
            "\n" +
            "      - name: Promote to Full\n" +
            "        run: |\n" +
            "          kubectl argo rollouts promote checkout-api \\\n" +
            "            --namespace=production\n" +
            "```\n\n" +
            "### Pipeline stage visualization\n\n" +
            "```\n" +
            "PR Open ─── Build ─── Unit Tests ─── Integration Tests ─── Coverage Gate\n" +
            "                                                                │\n" +
            "Merge to ── Build ─── Tests ─── Security Scan ─── Staging ─── Smoke Tests\n" +
            "main                     │         │                              │\n" +
            "                    SAST scan  Dep audit              Deploy Canary (10%)\n" +
            "                              Container scan              │\n" +
            "                                                    30-min Soak\n" +
            "                                                          │\n" +
            "                                                    Promote (100%)\n" +
            "```\n\n" +
            "### Quality gates summary\n\n" +
            "| Gate | Threshold | Blocks |\n" +
            "| --- | --- | --- |\n" +
            "| Unit test coverage | ≥ 80% | PR merge |\n" +
            "| Integration tests | All pass | PR merge |\n" +
            "| SAST findings | 0 critical/high | Staging deploy |\n" +
            "| Container vulnerabilities | 0 critical | Staging deploy |\n" +
            "| Staging smoke tests | All pass | Production deploy |\n" +
            "| Canary metrics | SLO compliant | Full rollout |\n",
            "Grounded · 4 references");

        // ── Turn 32: Short question ──
        AddUser("Quick question — what's the best way to handle graceful shutdown in our services?");

        AddAssistant(
            "## Graceful Shutdown Pattern\n\n" +
            "```csharp\n" +
            "var builder = WebApplication.CreateBuilder(args);\n" +
            "\n" +
            "// Configure shutdown timeout\n" +
            "builder.Host.ConfigureHostOptions(options =>\n" +
            "{\n" +
            "    options.ShutdownTimeout = TimeSpan.FromSeconds(30);\n" +
            "});\n" +
            "\n" +
            "var app = builder.Build();\n" +
            "\n" +
            "// Health check transitions to unhealthy on shutdown signal\n" +
            "var shutdownRequested = false;\n" +
            "var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();\n" +
            "\n" +
            "lifetime.ApplicationStopping.Register(() =>\n" +
            "{\n" +
            "    shutdownRequested = true;\n" +
            "    Log.Information(\"Shutdown signal received, draining requests...\");\n" +
            "});\n" +
            "\n" +
            "app.MapHealthChecks(\"/health/ready\", new()\n" +
            "{\n" +
            "    Predicate = _ => !shutdownRequested\n" +
            "});\n" +
            "\n" +
            "app.MapHealthChecks(\"/health/live\");\n" +
            "```\n\n" +
            "### Kubernetes pod spec\n\n" +
            "```yaml\n" +
            "spec:\n" +
            "  terminationGracePeriodSeconds: 45\n" +
            "  containers:\n" +
            "    - name: checkout\n" +
            "      lifecycle:\n" +
            "        preStop:\n" +
            "          exec:\n" +
            "            command: [\"/bin/sh\", \"-c\", \"sleep 5\"]\n" +
            "      readinessProbe:\n" +
            "        httpGet:\n" +
            "          path: /health/ready\n" +
            "          port: 8080\n" +
            "        periodSeconds: 5\n" +
            "        failureThreshold: 2\n" +
            "      livenessProbe:\n" +
            "        httpGet:\n" +
            "          path: /health/live\n" +
            "          port: 8080\n" +
            "        periodSeconds: 10\n" +
            "        failureThreshold: 3\n" +
            "```\n\n" +
            "### Shutdown sequence\n\n" +
            "```\n" +
            "SIGTERM received\n" +
            "  │\n" +
            "  ├─ preStop hook: sleep 5s (let load balancer deregister)\n" +
            "  │\n" +
            "  ├─ Readiness probe fails (stops receiving new traffic)\n" +
            "  │\n" +
            "  ├─ ApplicationStopping fires\n" +
            "  │   └─ In-flight requests drain (up to 30s)\n" +
            "  │\n" +
            "  ├─ Background services stop (IHostedService.StopAsync)\n" +
            "  │\n" +
            "  └─ Process exits\n" +
            "```",
            "Grounded · 2 references");

        // ── Turn 33: Caching strategy ──
        AddUser("We've been talking about a lot of backend stuff. One more: how should we design our caching layer? We're using Redis but inconsistently.");

        AddAssistant(
            "## Unified Caching Architecture\n\n" +
            "### Layered cache strategy\n\n" +
            "```\n" +
            "  Request flow:\n" +
            "\n" +
            "  Client ──► CDN/Edge ──► L1 (In-Memory) ──► L2 (Redis) ──► Database\n" +
            "              │              │                  │               │\n" +
            "           60s TTL       30s TTL            5 min TTL      Source of\n" +
            "           (static)     (per-pod)          (shared)         truth\n" +
            "```\n\n" +
            "### Cache abstraction\n\n" +
            "```csharp\n" +
            "public sealed class TieredCache : ICache\n" +
            "{\n" +
            "    private readonly IMemoryCache _l1;\n" +
            "    private readonly IDistributedCache _l2;\n" +
            "    private readonly ILogger<TieredCache> _logger;\n" +
            "\n" +
            "    public async Task<T?> GetOrSetAsync<T>(\n" +
            "        string key,\n" +
            "        Func<CancellationToken, Task<T>> factory,\n" +
            "        CachePolicy policy,\n" +
            "        CancellationToken ct = default)\n" +
            "    {\n" +
            "        // L1: In-memory (fastest)\n" +
            "        if (_l1.TryGetValue(key, out T? cached))\n" +
            "            return cached;\n" +
            "\n" +
            "        // L2: Redis (shared across pods)\n" +
            "        var distributed = await _l2.GetAsync(key, ct);\n" +
            "        if (distributed is not null)\n" +
            "        {\n" +
            "            var value = Deserialize<T>(distributed);\n" +
            "            _l1.Set(key, value, policy.L1Expiry);\n" +
            "            return value;\n" +
            "        }\n" +
            "\n" +
            "        // Cache miss: fetch from source\n" +
            "        var fresh = await factory(ct);\n" +
            "        if (fresh is not null)\n" +
            "        {\n" +
            "            _l1.Set(key, fresh, policy.L1Expiry);\n" +
            "            await _l2.SetAsync(key, Serialize(fresh),\n" +
            "                new() { AbsoluteExpirationRelativeToNow = policy.L2Expiry }, ct);\n" +
            "        }\n" +
            "        return fresh;\n" +
            "    }\n" +
            "\n" +
            "    public async Task InvalidateAsync(string key, CancellationToken ct = default)\n" +
            "    {\n" +
            "        _l1.Remove(key);\n" +
            "        await _l2.RemoveAsync(key, ct);\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "public sealed record CachePolicy(\n" +
            "    TimeSpan L1Expiry,\n" +
            "    TimeSpan L2Expiry,\n" +
            "    bool AllowStale = false);\n" +
            "```\n\n" +
            "### Cache key conventions\n\n" +
            "```csharp\n" +
            "public static class CacheKeys\n" +
            "{\n" +
            "    public static string Order(Guid id) => $\"order:{id}\";\n" +
            "    public static string CustomerOrders(Guid customerId) =>\n" +
            "        $\"customer:{customerId}:orders\";\n" +
            "    public static string ProductPrice(string sku) => $\"price:{sku}\";\n" +
            "    public static string InventoryCount(string sku) => $\"inventory:{sku}:count\";\n" +
            "    public static string FeatureFlag(string flag) => $\"flag:{flag}\";\n" +
            "}\n" +
            "```\n\n" +
            "### Cache policies by data type\n\n" +
            "| Data | L1 (Memory) | L2 (Redis) | Invalidation |\n" +
            "| --- | --- | --- | --- |\n" +
            "| Product catalog | 5 min | 30 min | On catalog update event |\n" +
            "| Price | 1 min | 5 min | On price change event |\n" +
            "| Inventory count | 10 sec | 30 sec | On reservation/release |\n" +
            "| Order (confirmed) | 30 sec | 10 min | Immutable after confirm |\n" +
            "| Feature flags | 30 sec | 2 min | On flag toggle event |\n" +
            "| User session | — | 30 min | On logout/expiry |\n" +
            "| Exchange rates | 10 min | 1 hour | Scheduled refresh |\n\n" +
            "### Cache stampede prevention\n\n" +
            "```csharp\n" +
            "// Use SemaphoreSlim to prevent thundering herd on cache miss\n" +
            "private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();\n" +
            "\n" +
            "public async Task<T?> GetOrSetWithLockAsync<T>(\n" +
            "    string key, Func<CancellationToken, Task<T>> factory,\n" +
            "    CachePolicy policy, CancellationToken ct)\n" +
            "{\n" +
            "    // Fast path: L1 hit\n" +
            "    if (_l1.TryGetValue(key, out T? cached))\n" +
            "        return cached;\n" +
            "\n" +
            "    var @lock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));\n" +
            "    await @lock.WaitAsync(ct);\n" +
            "    try\n" +
            "    {\n" +
            "        // Double-check after acquiring lock\n" +
            "        if (_l1.TryGetValue(key, out cached))\n" +
            "            return cached;\n" +
            "\n" +
            "        return await GetOrSetAsync(key, factory, policy, ct);\n" +
            "    }\n" +
            "    finally\n" +
            "    {\n" +
            "        @lock.Release();\n" +
            "    }\n" +
            "}\n" +
            "```",
            "Grounded · 3 references");

        // ── Turn 34: Final wrap-up ──
        AddUser("Amazing depth on all of this. I think we're in great shape. Let me know when the 100% soak completes tomorrow.");

        AddAssistant(
            "## Session Complete — Open Items Tracker\n\n" +
            "Here's everything from today organized by priority and status:\n\n" +
            "### 🔴 Active — In Progress\n" +
            "| Item | Status | ETA |\n" +
            "| --- | --- | --- |\n" +
            "| Checkout v2.17.4 — 100% rollout soak | 🔄 Monitoring | Tomorrow 3:50 PM |\n" +
            "| Stripe EU — waiting for recovery | ⏳ External | Stripe status page |\n\n" +
            "### 🟡 Planned — This Sprint\n" +
            "| Item | Owner | Priority |\n" +
            "| --- | --- | --- |\n" +
            "| Database index on `checkout_session_id` | DBA team | P1 |\n" +
            "| JWT validation hardening | Security | P1 |\n" +
            "| Prometheus alert rules deployment | SRE | P1 |\n" +
            "| Structured logging migration | Platform | P2 |\n" +
            "| Dockerfile optimization | DevOps | P2 |\n" +
            "| Configuration unification | Platform | P2 |\n\n" +
            "### 🟢 Planned — Next Sprint\n" +
            "| Item | Notes |\n" +
            "| --- | --- |\n" +
            "| PostgreSQL migration — Phase 1 (schema) | DBA + Platform |\n" +
            "| OpenTelemetry rollout | Tracing + Metrics |\n" +
            "| API v2 public launch | After load testing |\n" +
            "| Rate limiting implementation | Before v2 launch |\n" +
            "| Cross-region failover setup | After Stripe incident |\n" +
            "| Feature flag system rollout | Platform team |\n\n" +
            "### 🔵 Future — Backlog\n" +
            "| Item | Notes |\n" +
            "| --- | --- |\n" +
            "| Modular monolith restructure | Q3 target |\n" +
            "| CI/CD pipeline overhaul | After observability |\n" +
            "| N+1 query detection in CI | After EF interceptor |\n" +
            "| Integration test coverage push | Target 80% |\n" +
            "| Cache architecture unification | After Redis audit |\n\n" +
            "I'll send the soak completion notification tomorrow. " +
            "Great session — we covered incident response, 12 architecture topics, and " +
            "set up a solid roadmap. Have a good evening! 🌙",
            "Grounded · session summary");

        // ── Turn 35: Very last exchange ──
        AddUser("Incredible work today. See you tomorrow! 🙏");

        AddAssistant(
            "Thank you! It was a productive session. I'll keep watching the rollout overnight " +
            "and have a status report ready when you're back. Sleep well — the checkout service is in good hands. 🛡️",
            "");

        _mainChatShell?.ScrollToEnd();
    }

    private void OnPerfSeedRequested(object? sender, RoutedEventArgs e)
    {
        EnsurePerformanceDemoSeeded(forceReset: true);
    }

    private async void OnPerfRunRequested(object? sender, RoutedEventArgs e)
    {
        if (_perfRunner is null)
            return;

        CancelPerformanceRun();
        _perfRunCts?.Dispose();
        _perfRunCts = new CancellationTokenSource();

        try
        {
            await RunChatPerformanceBenchmarkAsync(_perfRunCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _perfRunCts?.Dispose();
            _perfRunCts = null;
        }
    }

    internal async Task<ChatPerfBenchmarkResult> RunChatPerformanceBenchmarkAsync(CancellationToken token = default)
    {
        if (_perfRunner is null || _perfChatShell is null)
            throw new InvalidOperationException("Chat performance controls are not available.");

        SetPerformanceButtons(isRunning: true);

        try
        {
            ShowPage(8);
            var navList = this.FindControl<ListBox>("NavList");
            if (navList is not null && navList.SelectedIndex != 8)
                navList.SelectedIndex = 8;

            await Task.Delay(220, token);

            var perfPage = this.FindControl<Control>("Page8");
            var perfPageVisible = perfPage?.IsVisible == true;
            var shellVisible = _perfChatShell.IsVisible;
            var shellWidth = _perfChatShell.Bounds.Width;
            var shellHeight = _perfChatShell.Bounds.Height;

            SetPerformanceStatus("Calibrating idle render ceiling…");
            var idleMetrics = await _perfRunner.MeasureIdleRenderMetricsAsync(TimeSpan.FromSeconds(3), token);

            EnsurePerformanceDemoSeeded(forceReset: true);

            SetPerformanceStatus(L("ChatPerf.StatusRunningFirstPass", "Running first stress pass…"));
            var firstPass = await _perfRunner.RunScenarioSeriesAsync(token);
            var firstPassText = ChatPerformanceBenchmarkRunner.FormatPerformanceMetrics(firstPass);
            _perfFirstPassText?.SetCurrentValue(TextBlock.TextProperty, firstPassText);

            SetPerformanceStatus(L("ChatPerf.StatusRunningSecondPass", "Running second stress pass…"));
            var secondPass = await _perfRunner.RunScenarioSeriesAsync(token);
            var secondPassText = ChatPerformanceBenchmarkRunner.FormatPerformanceMetrics(secondPass);
            _perfSecondPassText?.SetCurrentValue(TextBlock.TextProperty, secondPassText);

            var deltaText = ChatPerformanceBenchmarkRunner.FormatComparison(firstPass, secondPass);
            _perfDeltaText?.SetCurrentValue(TextBlock.TextProperty, deltaText);
            SetPerformanceStatus(L("ChatPerf.StatusCompleted", "Benchmark complete. Review the two passes for steady-state drift under streaming + scroll stress."));

            return new ChatPerfBenchmarkResult(
                IdleMetrics: idleMetrics,
                FirstPass: firstPass,
                SecondPass: secondPass,
                FirstPassText: firstPassText,
                SecondPassText: secondPassText,
                DeltaText: deltaText,
                PerfPageVisible: perfPageVisible,
                ShellVisible: shellVisible,
                ShellWidth: shellWidth,
                ShellHeight: shellHeight);
        }
        catch (OperationCanceledException)
        {
            SetPerformanceStatus(L("ChatPerf.StatusCancelled", "Benchmark canceled."));
            throw;
        }
        finally
        {
            _perfRunner.ResetToDefaults();

            SetPerformanceButtons(isRunning: false);
        }
    }

    private void OnPerfStopRequested(object? sender, RoutedEventArgs e)
    {
        CancelPerformanceRun();
        SetPerformanceStatus(L("ChatPerf.StatusCancelled", "Benchmark canceled."));
    }

    private void CancelPerformanceRun()
    {
        if (_perfRunCts is not null && !_perfRunCts.IsCancellationRequested)
            _perfRunCts.Cancel();
    }

    private void SetPerformanceButtons(bool isRunning)
    {
        if (_perfSeedButton is not null)
            _perfSeedButton.IsEnabled = !isRunning;
        if (_perfRunButton is not null)
            _perfRunButton.IsEnabled = !isRunning;
        if (_perfStopButton is not null)
            _perfStopButton.IsEnabled = isRunning;
    }

    private void EnsurePerformanceDemoSeeded(bool forceReset)
    {
        if (_perfRunner is null)
            return;

        if (_perfInitialized && !forceReset)
            return;

        _perfRunner.SeedTranscript();

        _perfInitialized = true;
        _perfFirstPassText?.SetCurrentValue(TextBlock.TextProperty, L("ChatPerf.NoResults", "No results yet."));
        _perfSecondPassText?.SetCurrentValue(TextBlock.TextProperty, L("ChatPerf.NoResults", "No results yet."));
        _perfDeltaText?.SetCurrentValue(TextBlock.TextProperty, L("ChatPerf.NoResults", "No results yet."));
        SetPerformanceStatus(L("ChatPerf.StatusSeeded", "Transcript seeded with many messages. Run benchmark to record two stress passes."));
    }

    private void SetPerformanceStatus(string text)
    {
        _perfStatusText?.SetCurrentValue(TextBlock.TextProperty, text);
    }

    private string L(string key, string fallback)
    {
        if (DataContext is MainViewModel vm)
            return vm.Strings[key];

        return fallback;
    }

    private void OnChatCanvasCloseRequested(object? sender, RoutedEventArgs e)
    {
        _isChatCanvasOpen = false;
        var chatSideHost = this.FindControl<ScrollViewer>("ChatSideHost");
        if (chatSideHost is not null)
            chatSideHost.IsVisible = true;
        UpdateResponsiveClasses(Bounds.Width);
    }

    private void ShowChatCanvas(string title, string subtitle, object content, bool isGenerating)
    {
        if (_chatExperienceCanvas is null) return;
        _chatExperienceCanvas.Title = title;
        _chatExperienceCanvas.Subtitle = subtitle;
        _chatExperienceCanvas.Content = content;
        _chatExperienceCanvas.IsGenerating = isGenerating;
        _isChatCanvasOpen = true;

        var chatSideHost = this.FindControl<ScrollViewer>("ChatSideHost");
        if (chatSideHost is not null)
            chatSideHost.IsVisible = false;

        _chatExperienceCanvas.IsOpen = true;
        UpdateResponsiveClasses(Bounds.Width);

        if (isGenerating)
        {
            _ = Task.Delay(3000).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (_chatExperienceCanvas?.IsGenerating == true)
                        _chatExperienceCanvas.IsGenerating = false;
                }));
        }
    }

    private static object BuildChecklistCanvasContent()
    {
        return new StrataMarkdown
        {
            IsInline = true,
            Markdown = "## Rollout Checklist\n\n### Stage 1 — Canary\n- Validate autoscale policy thresholds\n- Validate GC tuning parameters\n- Deploy to canary pool (5%)\n- Observe p95 for 10 minutes\n\n### Stage 2 — Expansion\n- Expand to 25% with rollback gate\n- Expand to 50% with rollback gate\n- Send stakeholder status update\n\n### Stage 3 — Full rollout\n- Complete full rollout with sign-off\n- Close incident IR-4471"
        };
    }

    private static object BuildCodeCanvasContent()
    {
        return new SelectableTextBlock
        {
            Text = "import asyncio\n\nasync def analyze_incident(incident_id: str):\n    events = await fetch_correlated_events(incident_id, window=\"24h\")\n    hypothesis = rank_hypotheses(events)[0]\n\n    return {\n        \"root_cause\": hypothesis.label,\n        \"confidence\": hypothesis.score,\n        \"next_step\": \"stage rollout with rollback gates\"\n    }",
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResolveBrush("Brush.TextPrimary", Brushes.Black)
        };
    }

    private static object BuildSummaryCanvasContent()
    {
        return new StrataMarkdown
        {
            IsInline = true,
            Markdown = "## Incident Summary\n\n### Root Cause\nGC pause inflation from allocation bursts in serializer hot paths during peak traffic on worker pool C.\n\n### Mitigation\n- Stage rollout (25% → 50% → 100%)\n- Use p95 and GC pause rollback gates\n- Publish stakeholder updates at each gate\n\n### Impact\n~12% of requests affected at peak, no data loss, normalized within 8 minutes of mitigation deployment."
        };
    }

    private static IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}
