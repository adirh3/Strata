using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
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
    Failed
}

/// <summary>
/// Displays a compact, expandable card representing a single AI tool invocation.
/// Shows a status dot, tool name, and a status pill. Clicking the header reveals
/// optional input parameters, extra info, and duration.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataAiToolCall ToolName="search_code"
///                             Status="Completed"
///                             DurationMs="340"
///                             InputParameters='{"query": "hello"}'
///                             MoreInfo="Found 12 results" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Header (Border), PART_StateDot (Border), PART_Detail (Border).</para>
/// <para><b>Pseudo-classes:</b> :expanded, :inprogress, :completed, :failed, :has-params, :has-info, :has-duration.</para>
/// </remarks>
public class StrataAiToolCall : TemplatedControl
{
    private Border? _header;
    private Border? _stateDot;
    private Border? _root;
    private StrataMarkdown? _inputMarkdown;
    private StrataMarkdown? _infoMarkdown;
    private ContextMenu? _contextMenu;

    /// <summary>Name of the tool being invoked (e.g. "search_code").</summary>
    public static readonly StyledProperty<string> ToolNameProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string>(nameof(ToolName), "tool.call");

    /// <summary>Optional JSON or plain-text input parameters shown in the detail pane.</summary>
    public static readonly StyledProperty<string?> InputParametersProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(InputParameters));

    /// <summary>Optional extra information displayed below the parameters.</summary>
    public static readonly StyledProperty<string?> MoreInfoProperty =
        AvaloniaProperty.Register<StrataAiToolCall, string?>(nameof(MoreInfo));

    /// <summary>Execution duration in milliseconds. Formatted automatically as ms or seconds.</summary>
    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<StrataAiToolCall, double>(nameof(DurationMs), 0);

    /// <summary>Current execution status. Drives the status pill colour and animation.</summary>
    public static readonly StyledProperty<StrataAiToolCallStatus> StatusProperty =
        AvaloniaProperty.Register<StrataAiToolCall, StrataAiToolCallStatus>(nameof(Status), StrataAiToolCallStatus.InProgress);

    /// <summary>Optional content displayed in the header row (e.g. a "Show diff" button).</summary>
    public static readonly StyledProperty<object?> HeaderActionProperty =
        AvaloniaProperty.Register<StrataAiToolCall, object?>(nameof(HeaderAction));

    /// <summary>Whether the detail pane (parameters, info, duration) is visible.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataAiToolCall, bool>(nameof(IsExpanded), false);

    public static readonly DirectProperty<StrataAiToolCall, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(StatusText), control => control.StatusText);

    public static readonly DirectProperty<StrataAiToolCall, string> DurationTextProperty =
        AvaloniaProperty.RegisterDirect<StrataAiToolCall, string>(nameof(DurationText), control => control.DurationText);

    static StrataAiToolCall()
    {
        StatusProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        IsExpandedProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        InputParametersProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        MoreInfoProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
        DurationMsProperty.Changed.AddClassHandler<StrataAiToolCall>((control, _) => control.UpdateState());
    }

    public string ToolName
    {
        get => GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
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
        StrataAiToolCallStatus.InProgress => "In progress",
        StrataAiToolCallStatus.Completed when DurationMs > 0 => DurationMs >= 1000
            ? $"{DurationMs / 1000d:F2}s"
            : $"{DurationMs:F0} ms",
        StrataAiToolCallStatus.Completed => "Completed",
        StrataAiToolCallStatus.Failed => "Failed",
        _ => "Unknown"
    };

    public string DurationText => DurationMs >= 1000
        ? $"Duration \u00b7 {DurationMs / 1000d:F2}s"
        : $"Duration \u00b7 {DurationMs:F0} ms";

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_header is not null)
            _header.PointerPressed -= OnHeaderPointerPressed;

        base.OnApplyTemplate(e);

        _header = e.NameScope.Find<Border>("PART_Header");
        _stateDot = e.NameScope.Find<Border>("PART_StateDot");
        _root = e.NameScope.Find<Border>("PART_Root");
        _inputMarkdown = e.NameScope.Find<StrataMarkdown>("PART_InputMarkdown");
        _infoMarkdown = e.NameScope.Find<StrataMarkdown>("PART_InfoMarkdown");

        if (_header is not null)
            _header.PointerPressed += OnHeaderPointerPressed;

        AttachContextMenu();

        UpdateState();

        Dispatcher.UIThread.Post(() =>
        {
            if (Status == StrataAiToolCallStatus.InProgress)
                StartRunningPulse();
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopRunningPulse();
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

        PseudoClasses.Set(":inprogress", Status == StrataAiToolCallStatus.InProgress);
        PseudoClasses.Set(":completed", Status == StrataAiToolCallStatus.Completed);
        PseudoClasses.Set(":failed", Status == StrataAiToolCallStatus.Failed);

        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":has-params", !string.IsNullOrWhiteSpace(InputParameters));
        PseudoClasses.Set(":has-info", !string.IsNullOrWhiteSpace(MoreInfo));
        PseudoClasses.Set(":has-duration", DurationMs > 0);

        if (_inputMarkdown is not null)
            _inputMarkdown.Markdown = InputParameters ?? "";
        if (_infoMarkdown is not null)
            _infoMarkdown.Markdown = MoreInfo ?? "";

        if (Status == StrataAiToolCallStatus.InProgress)
            StartRunningPulse();
        else
            StopRunningPulse();
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

        await topLevel.Clipboard.SetTextAsync(text);
    }

    private void StartRunningPulse()
    {
        if (_stateDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null)
            return;

        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.35f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(920);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopRunningPulse()
    {
        if (_stateDot is null)
            return;

        var visual = ElementComposition.GetElementVisual(_stateDot);
        if (visual is null)
            return;

        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }
}
