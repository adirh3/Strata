using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

/// <summary>Execution status of a tool call.</summary>
public enum StrataAiToolCallStatus
{
    /// <summary>Tool is currently executing.</summary>
    InProgress,
    /// <summary>Tool finished successfully.</summary>
    Completed,
    /// <summary>Tool encountered an error.</summary>
    Failed,
    /// <summary>Tool was stopped before it completed.</summary>
    Stopped
}

/// <summary>
/// Displays one AI tool invocation as a quiet, expandable row: a line icon that says what the tool is, its
/// name, one glanceable detail and how long it took. Clicking the header opens a recessed well with what the
/// step was given (<see cref="InputParameters"/>, as fields when written as <c>**Key:** value</c> lines) and
/// what it reported (<see cref="MoreInfo"/>, read as the error when the step failed).
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataAiToolCall ToolName="Searching the web"
///                             Detail="best OLED TV"
///                             Icon="{x:Static controls:StrataIcons.Search}"
///                             Status="Completed"
///                             DurationMs="340"
///                             InputParameters="**Query:** best OLED TV"
///                             MoreInfo="Found 12 results" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_Header (Border), PART_StateDot (Border),
/// PART_Icon (StrataLineIcon), PART_StatusText (TextBlock), PART_Detail (Border),
/// PART_Details (<see cref="StrataToolDetails"/>).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :inprogress, :completed, :failed, :stopped, :has-params, :has-info,
/// :has-duration, :has-icon, :has-detail.</para>
/// </remarks>
public class StrataAiToolCall : TemplatedControl
{
    private Border? _header;
    private Border? _root;
    private StrataToolDetails? _details;
    private ContextMenu? _contextMenu;
    private readonly RunningElapsedClock _elapsedClock;
    private bool _isAttached;

    /// <summary>Name of the tool being invoked (e.g. "search_code").</summary>
    public static readonly StyledProperty<string> ToolNameProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string>(nameof(ToolName), "tool.call");

    /// <summary>Optional line icon (24-unit grid) that says what kind of tool this is.</summary>
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<StrataAiToolCall, Geometry?>(nameof(Icon));

    /// <summary>Optional one-line detail shown beside the name, such as the file or query the tool used.</summary>
    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(Detail));

    /// <summary>Optional JSON or plain-text input parameters shown in the detail pane.</summary>
    public static readonly StyledProperty<string?> InputParametersProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(InputParameters));

    /// <summary>Optional extra information displayed below the parameters.</summary>
    public static readonly StyledProperty<string?> MoreInfoProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(MoreInfo));

    /// <summary>Execution duration in milliseconds. Formatted automatically as ms or seconds.</summary>
    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<StrataAiToolCall, double>(nameof(DurationMs), 0);

    /// <summary>
    /// The instant this tool call actually started (its authoritative start time). When set, the live
    /// elapsed readout is computed from this fixed point rather than from when the control loaded, so
    /// the clock stays correct across control recreation (chat switch, list virtualization) instead of
    /// resetting to zero. Leave unset to fall back to a control-local clock.
    /// </summary>
    public static readonly StyledProperty<DateTimeOffset?> RunningSinceProperty =
        AvaloniaProperty.Register<StrataAiToolCall, DateTimeOffset?>(nameof(RunningSince));

    /// <summary>Current execution status: it colours the row's icon and says how the step ended.</summary>
    public static readonly StyledProperty<StrataAiToolCallStatus> StatusProperty =
        AvaloniaProperty.Register<StrataAiToolCall, StrataAiToolCallStatus>(nameof(Status), StrataAiToolCallStatus.InProgress);

    /// <summary>Optional content displayed in the header row (e.g. a "Show diff" button).</summary>
    public static readonly StyledProperty<object?> HeaderActionProperty =
        AvaloniaProperty.Register<StrataAiToolCall, object?>(nameof(HeaderAction));

    /// <summary>Whether the detail pane (parameters, info, duration) is visible.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataAiToolCall, bool>(
            nameof(IsExpanded), false, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<StrataAiToolCall, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(StatusText), control => control.StatusText);

    public static readonly DirectProperty<StrataAiToolCall, string> DurationTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(DurationText), control => control.DurationText);

    /// <summary>How long a completed call took, for its row ("42 ms", "1.2s"). Empty while it runs, when it
    /// did not complete, or when no duration was measured.</summary>
    public static readonly DirectProperty<StrataAiToolCall, string> CompactDurationTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(CompactDurationText), control => control.CompactDurationText);

    /// <summary>What the row says about its state at a glance: how long it has been running, how long it
    /// took, or the word for a call that failed or stopped.</summary>
    public static readonly DirectProperty<StrataAiToolCall, string> GlanceTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(GlanceText), control => control.GlanceText);

    /// <summary>Live "how long it has been running" readout, shown while the tool is in progress
    /// (e.g. "8s", "1m 04s"). Empty until the tool has been running for at least a second, and
    /// cleared the moment it finishes so the row says how it ended instead.</summary>
    public static readonly DirectProperty<StrataAiToolCall, string> ElapsedTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(ElapsedText), control => control.ElapsedText);

    static StrataAiToolCall()
    {
        StatusProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        InputParametersProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        MoreInfoProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        DurationMsProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        IconProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        DetailProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        RunningSinceProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.OnElapsedTick());
    }

    public StrataAiToolCall()
    {
        _elapsedClock = new RunningElapsedClock(OnElapsedTick);
    }

    public string ToolName
    {
        get => GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    /// <summary>Gets or sets the tool's line icon; see <see cref="IconProperty"/>.</summary>
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Gets or sets the glanceable detail; see <see cref="DetailProperty"/>.</summary>
    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string? InputParameters
    {
        get => GetValue(InputParametersProperty);
        set => SetValue(InputParametersProperty, value);
    }

    public string? MoreInfo
    {
        get => GetValue(MoreInfoProperty);
        set => SetValue(MoreInfoProperty, value);
    }

    public double DurationMs
    {
        get => GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    public DateTimeOffset? RunningSince
    {
        get => GetValue(RunningSinceProperty);
        set => SetValue(RunningSinceProperty, value);
    }

    public object? HeaderAction
    {
        get => GetValue(HeaderActionProperty);
        set => SetValue(HeaderActionProperty, value);
    }

    public StrataAiToolCallStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public string StatusText => Status switch
    {
        StrataAiToolCallStatus.InProgress => "Running",
        StrataAiToolCallStatus.Completed => "Completed",
        StrataAiToolCallStatus.Failed => "Failed",
        StrataAiToolCallStatus.Stopped => "Stopped",
        _ => "Unknown"
    };

    public string DurationText => DurationMs >= 1000
        ? $"Duration \u00b7 {DurationMs / 1000d:F2}s"
        : $"Duration \u00b7 {DurationMs:F0} ms";

    public string CompactDurationText => Status == StrataAiToolCallStatus.Completed
        ? RunningElapsedClock.FormatDuration(DurationMs)
        : "";

    public string GlanceText => Status switch
    {
        StrataAiToolCallStatus.InProgress => ElapsedText,
        StrataAiToolCallStatus.Completed => CompactDurationText,
        _ => StatusText,
    };

    private string _elapsedText = "";

    public string ElapsedText
    {
        get => _elapsedText;
        private set
        {
            if (SetAndRaise(ElapsedTextProperty, ref _elapsedText, value))
                RaisePropertyChanged(GlanceTextProperty, default!, GlanceText);
        }
    }

    private void OnElapsedTick()
    {
        // Only in-flight calls show a live readout; a terminal call reports its frozen DurationText.
        if (Status != StrataAiToolCallStatus.InProgress)
        {
            ElapsedText = "";
            return;
        }

        // Prefer the authoritative start time when supplied: it's a fixed instant, so the readout is
        // immune to control recreation (chat switch, virtualization) that would otherwise reset a
        // freshly-started local clock back to zero. Fall back to the local clock when unset.
        var elapsed = RunningSince is { } since
            ? DateTimeOffset.UtcNow - since
            : _elapsedClock.Elapsed;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        ElapsedText = elapsed.TotalSeconds >= 1 ? RunningElapsedClock.Format(elapsed) : "";
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_header is not null)
            _header.PointerPressed -= OnHeaderPointerPressed;

        base.OnApplyTemplate(e);

        _header = e.NameScope.Find<Border>("PART_Header");
        _root = e.NameScope.Find<Border>("PART_Root");
        _details = e.NameScope.Find<StrataToolDetails>("PART_Details");

        if (_header is not null)
            _header.PointerPressed += OnHeaderPointerPressed;

        AttachContextMenu();

        UpdateState();

        Dispatcher.UIThread.Post(() =>
        {
            if (Status == StrataAiToolCallStatus.InProgress)
                StartRunningActivity();
        }, DispatcherPriority.Loaded);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs pointerEvent)
    {
        if (pointerEvent.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            IsExpanded = !IsExpanded;
            pointerEvent.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        // Resume the live pulse when a recycled/virtualized container comes back still in progress
        // (OnApplyTemplate's deferred start does not re-fire on an already-templated container).
        if (Status == StrataAiToolCallStatus.InProgress)
            StartRunningActivity();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopRunningActivity();

        if (_contextMenu is not null)
        {
            _contextMenu.Opening -= OnContextMenuOpening;
            _contextMenu = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Enter or Key.Space)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsExpanded)
        {
            IsExpanded = false;
            e.Handled = true;
        }
    }

    private void UpdateState()
    {
        RaisePropertyChanged(StatusTextProperty, default!, StatusText);
        RaisePropertyChanged(DurationTextProperty, default!, DurationText);
        RaisePropertyChanged(CompactDurationTextProperty, default!, CompactDurationText);
        RaisePropertyChanged(GlanceTextProperty, default!, GlanceText);

        PseudoClasses.Set(":inprogress", Status == StrataAiToolCallStatus.InProgress);
        PseudoClasses.Set(":completed", Status == StrataAiToolCallStatus.Completed);
        PseudoClasses.Set(":failed", Status == StrataAiToolCallStatus.Failed);
        PseudoClasses.Set(":stopped", Status == StrataAiToolCallStatus.Stopped);

        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-params", !string.IsNullOrWhiteSpace(InputParameters));
        PseudoClasses.Set(":has-info", !string.IsNullOrWhiteSpace(MoreInfo));
        PseudoClasses.Set(":has-duration", DurationMs > 0);
        PseudoClasses.Set(":has-icon", Icon is not null);
        PseudoClasses.Set(":has-detail", !string.IsNullOrWhiteSpace(Detail));

        if (_details is not null)
        {
            _details.Parameters = InputParameters;
            _details.Result = MoreInfo;
            _details.IsFailed = Status == StrataAiToolCallStatus.Failed;
        }

        if (Status == StrataAiToolCallStatus.InProgress)
            StartRunningActivity();
        else
            StopRunningActivity();
    }

    private void AttachContextMenu()
    {
        if (_root is null)
            return;

        _contextMenu ??= new ContextMenu();
        _contextMenu.Opening -= OnContextMenuOpening;
        _contextMenu.Opening += OnContextMenuOpening;

        RebuildContextMenuItems();
        _root.ContextMenu = _contextMenu;
        ContextMenu = _contextMenu;
    }

    private void OnContextMenuOpening(object? sender, EventArgs e)
    {
        RebuildContextMenuItems();
    }

    private void RebuildContextMenuItems()
    {
        if (_contextMenu is null)
            return;

        var items = new List<object>();

        var copySummaryItem = new MenuItem { Header = "Copy summary", Icon = CreateMenuIcon("\uE8C8") };
        copySummaryItem.Click += async (_, _) => await CopyToClipboardAsync(GetSummaryText());
        items.Add(copySummaryItem);

        if (!string.IsNullOrWhiteSpace(InputParameters))
        {
            var copyDetailsItem = new MenuItem { Header = "Copy details", Icon = CreateMenuIcon("\uE8A7") };
            copyDetailsItem.Click += async (_, _) => await CopyToClipboardAsync(InputParameters!);
            items.Add(copyDetailsItem);
        }

        if (!string.IsNullOrWhiteSpace(MoreInfo))
        {
            var copyInfoItem = new MenuItem { Header = "Copy info", Icon = CreateMenuIcon("\uE946") };
            copyInfoItem.Click += async (_, _) => await CopyToClipboardAsync(MoreInfo!);
            items.Add(copyInfoItem);
        }

        if (items.Count > 0)
            items.Add(new Separator());

        var toggleItem = new MenuItem
        {
            Header = IsExpanded ? "Collapse" : "Expand",
            Icon = CreateMenuIcon(IsExpanded ? "\uE70E" : "\uE70D")
        };
        toggleItem.Click += (_, _) => IsExpanded = !IsExpanded;
        items.Add(toggleItem);

        _contextMenu.ItemsSource = items;
    }

    private static TextBlock CreateMenuIcon(string glyph)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Width = 14,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private string GetSummaryText()
    {
        var lines = new List<string> { $"{ToolName} ({StatusText})" };

        if (DurationMs > 0)
            lines.Add(DurationText);

        return string.Join(Environment.NewLine, lines);
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await topLevel.Clipboard.SetDataAsync(data);
    }

    private void StartRunningActivity()
    {
        // A running DispatcherTimer roots this control via its tick closure, so never arm it while the
        // control is out of the visual tree — e.g. the OnApplyTemplate Loaded-priority post firing after
        // the container was recycled during a fast chat switch — or the detached card leaks and keeps
        // ticking forever. OnAttachedToVisualTree restarts it when the control is realized again.
        if (!_isAttached)
            return;

        _elapsedClock.Start();
        OnElapsedTick();
    }

    private void StopRunningActivity()
    {
        _elapsedClock.Stop();
        ElapsedText = "";
    }
}
