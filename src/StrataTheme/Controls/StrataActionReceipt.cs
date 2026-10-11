using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// A compact, source-linked record of an action the user took on an interactive answer. It reads as
/// the user's turn (a right-aligned bubble), reveals the frozen submitted inputs on demand, and shows
/// the reply lifecycle quietly beneath it.
/// </summary>
/// <remarks>Template parts: PART_Bubble, PART_Icon, PART_Disclosure, PART_DetailHost, PART_Source,
/// PART_StatusRow, PART_Status, PART_Error. Pseudo-classes: :expanded, :working, :failed, :replied.
/// All actions are keyboard-accessible; Escape collapses the snapshot.</remarks>
public sealed class StrataActionReceipt : TemplatedControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string>(nameof(Title), "");
    public static readonly StyledProperty<string> SummaryProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string>(nameof(Summary), "");
    public static readonly StyledProperty<string> DetailProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string>(nameof(Detail), "");
    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string>(nameof(Status), "Submitted");
    public static readonly StyledProperty<string?> ErrorProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string?>(nameof(Error));
    /// <summary>The submitted action (use, compare, submit, remaining, assumptions, discuss); selects the icon.</summary>
    public static readonly StyledProperty<string?> ActionProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string?>(nameof(Action));
    public static readonly StyledProperty<string> LanguageProperty =
        AvaloniaProperty.Register<StrataActionReceipt, string>(nameof(Language), "en");
    public static readonly StyledProperty<bool> CanOpenSourceProperty =
        AvaloniaProperty.Register<StrataActionReceipt, bool>(nameof(CanOpenSource), true);
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<StrataActionReceipt, bool>(nameof(IsExpanded));
    public static readonly StyledProperty<bool> TouchModeProperty =
        AvaloniaProperty.Register<StrataActionReceipt, bool>(nameof(TouchMode));
    public static readonly RoutedEvent<RoutedEventArgs> SourceRequestedEvent =
        RoutedEvent.Register<StrataActionReceipt, RoutedEventArgs>(nameof(SourceRequested), RoutingStrategies.Bubble);

    private Button? _disclosure;
    private Button? _source;
    private TextBlock? _status;
    private TextBlock? _error;
    private PathIcon? _icon;
    private Border? _detailHost;

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Summary { get => GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public string Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public string? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public string Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public bool CanOpenSource { get => GetValue(CanOpenSourceProperty); set => SetValue(CanOpenSourceProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool TouchMode { get => GetValue(TouchModeProperty); set => SetValue(TouchModeProperty, value); }
    public event EventHandler<RoutedEventArgs> SourceRequested
    {
        add => AddHandler(SourceRequestedEvent, value);
        remove => RemoveHandler(SourceRequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_disclosure is not null) _disclosure.Click -= OnDisclosure;
        if (_source is not null) _source.Click -= OnSource;
        base.OnApplyTemplate(e);
        _disclosure = e.NameScope.Find<Button>("PART_Disclosure");
        _source = e.NameScope.Find<Button>("PART_Source");
        _status = e.NameScope.Find<TextBlock>("PART_Status");
        _error = e.NameScope.Find<TextBlock>("PART_Error");
        _icon = e.NameScope.Find<PathIcon>("PART_Icon");
        _detailHost = e.NameScope.Find<Border>("PART_DetailHost");
        if (_disclosure is not null) _disclosure.Click += OnDisclosure;
        if (_source is not null) _source.Click += OnSource;
        Update();
    }

    private void OnDisclosure(object? sender, RoutedEventArgs args) => SetCurrentValue(IsExpandedProperty, !IsExpanded);
    private void OnSource(object? sender, RoutedEventArgs args) => RaiseEvent(new RoutedEventArgs(SourceRequestedEvent));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty || change.Property == LanguageProperty || change.Property == IsExpandedProperty
            || change.Property == DetailProperty || change.Property == SummaryProperty || change.Property == TouchModeProperty
            || change.Property == ErrorProperty || change.Property == ActionProperty)
            Update();
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Escape && IsExpanded)
        {
            IsExpanded = false;
            _disclosure?.Focus();
            args.Handled = true;
        }
        base.OnKeyDown(args);
    }

    private void Update()
    {
        var labels = InteractiveLabels.ForLanguage(Language);
        FlowDirection = Language.StartsWith("he", StringComparison.OrdinalIgnoreCase)
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":working", Status == "Working");
        PseudoClasses.Set(":failed", Status == "Failed");
        PseudoClasses.Set(":replied", Status == "ReplyReady");
        Classes.Set("touch", TouchMode);
        var status = labels[Status switch
        {
            "Working" => "working", "ReplyReady" => "replyReady", "Failed" => "failed",
            "Stopped" => "stopped", _ => "submitted"
        }];
        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetHelpText(this, status);
        if (_icon is not null)
            _icon.Data = Geometry.Parse(InteractiveIcons.ForAction(Action));
        if (_disclosure is not null)
        {
            var label = labels[IsExpanded ? "hideInputs" : "viewInputs"];
            _disclosure.Content = InteractiveIcons.Labeled(IsExpanded ? InteractiveIcons.ChevronUp : InteractiveIcons.ChevronDown,
                label, trailing: true);
            AutomationProperties.SetName(_disclosure, label);
            _disclosure.IsVisible = !string.IsNullOrWhiteSpace(Detail) && Detail.Trim() != Summary.Trim();
        }
        if (_source is not null)
        {
            _source.Content = InteractiveIcons.Labeled(InteractiveIcons.ArrowUp, labels["showOriginal"]);
            AutomationProperties.SetName(_source, labels["showOriginal"]);
        }
        if (_status is not null)
            _status.Text = status;
        if (_error is not null)
            _error.IsVisible = !string.IsNullOrWhiteSpace(Error);
        if (_detailHost is not null)
        {
            _detailHost.Child = IsExpanded ? InteractiveSnapshotView.Create(Detail) : null;
            _detailHost.IsVisible = IsExpanded;
        }
    }
}
