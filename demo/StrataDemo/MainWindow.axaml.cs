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
    private StackPanel? _liveTranscript;
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

    public MainWindow()
    {
        InitializeComponent();

        SizeChanged += (_, args) => UpdateResponsiveClasses(args.NewSize.Width);
        Opened += (_, _) => UpdateResponsiveClasses(Bounds.Width);

        // Cache page references
        for (int i = 0; i <= 7; i++)
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
        _liveTranscript = this.FindControl<StackPanel>("LiveTranscript");
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

        _liveTranscript.Children.Add(think);
        _liveTranscript.Children.Add(typing);
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

        _liveTranscript.Children.Add(assistantMessage);
        _mainChatShell?.ScrollToEnd();

        // Stream markdown in batches — accumulate words quickly, push to
        // StrataMarkdown in chunks so the user sees formatted content
        // progressively without per-word parse overhead.
        var words = fullMarkdown.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var accumulated = new System.Text.StringBuilder(fullMarkdown.Length);
        const int batchSize = 6;

        for (var i = 0; i < words.Length; i++)
        {
            token.ThrowIfCancellationRequested();

            if (accumulated.Length > 0) accumulated.Append(' ');
            accumulated.Append(words[i]);

            // Push to markdown renderer every batch
            if (i % batchSize == batchSize - 1 || i == words.Length - 1)
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

        _liveTranscript.Children.Remove(typing);

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

        _liveTranscript.Children.Add(toolsMessage);
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
            return "## Incident summary\nThe latency spike was driven by allocation bursts in serializer hot paths, amplifying GC pauses under peak load" + inlineCites + ".\n\n```csharp\npublic static bool IsSloHealthy(double p95Ms, double gcPauseMs)\n{\n    return p95Ms <= 250 && gcPauseMs <= 80;\n}\n```\n\n- Roll out in stages (10% \u2192 50% \u2192 100%)\n- Roll back immediately when thresholds are breached" + refs;
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

        _liveTranscript.Children.Add(user);
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

        _liveTranscript.Children.Add(note);
        _mainChatShell?.ScrollToEnd();
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
