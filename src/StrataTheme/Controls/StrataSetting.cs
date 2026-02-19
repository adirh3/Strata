using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Metadata;

namespace StrataTheme.Controls;

/// <summary>
/// A single setting row displaying a header, description, interactive content, and optional revert button.
/// Place inside a <see cref="StrataSettingGroup"/> for grouped settings under a shared card.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataSetting Header="Dark Mode"
///                          Description="Switch between light and dark themes"
///                          IsModified="True"&gt;
///     &lt;ToggleSwitch IsChecked="True" /&gt;
/// &lt;/controls:StrataSetting&gt;
/// </code>
/// <para><b>Template parts:</b> PART_RevertButton (Button).</para>
/// <para><b>Pseudo-classes:</b> :modified.</para>
/// </remarks>
public class StrataSetting : TemplatedControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<StrataSetting, string?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<StrataSetting, string?>(nameof(Description));

    public static readonly StyledProperty<object?> SettingContentProperty =
        AvaloniaProperty.Register<StrataSetting, object?>(nameof(SettingContent));

    public static readonly StyledProperty<bool> IsModifiedProperty =
        AvaloniaProperty.Register<StrataSetting, bool>(nameof(IsModified));

    public static readonly StyledProperty<bool> ShowSeparatorProperty =
        AvaloniaProperty.Register<StrataSetting, bool>(nameof(ShowSeparator));

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<StrataSetting, bool>(nameof(IsHighlighted));

    public static readonly RoutedEvent<RoutedEventArgs> RevertedEvent =
        RoutedEvent.Register<StrataSetting, RoutedEventArgs>(nameof(Reverted), RoutingStrategies.Bubble);

    static StrataSetting()
    {
        IsModifiedProperty.Changed.AddClassHandler<StrataSetting>((s, _) => s.UpdatePseudoClasses());
        IsHighlightedProperty.Changed.AddClassHandler<StrataSetting>((s, _) => s.UpdatePseudoClasses());
    }

    /// <summary>The setting name displayed as the primary label.</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Optional description text shown below the header.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The interactive control for this setting (toggle, combo, text box, etc.).</summary>
    [Content]
    public object? SettingContent
    {
        get => GetValue(SettingContentProperty);
        set => SetValue(SettingContentProperty, value);
    }

    /// <summary>Indicates the setting has been changed from its default value. Shows the revert button.</summary>
    public bool IsModified
    {
        get => GetValue(IsModifiedProperty);
        set => SetValue(IsModifiedProperty, value);
    }

    /// <summary>Controls visibility of the top separator line. Set automatically by <see cref="StrataSettingGroup"/>.</summary>
    public bool ShowSeparator
    {
        get => GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }

    /// <summary>Highlights the setting with an accent background, used for search matches.</summary>
    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>Raised when the user clicks the revert button.</summary>
    public event EventHandler<RoutedEventArgs>? Reverted
    {
        add => AddHandler(RevertedEvent, value);
        remove => RemoveHandler(RevertedEvent, value);
    }

    private Button? _revertButton;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_revertButton is not null)
            _revertButton.Click -= OnRevertClick;

        _revertButton = e.NameScope.Find<Button>("PART_RevertButton");

        if (_revertButton is not null)
            _revertButton.Click += OnRevertClick;

        UpdatePseudoClasses();
    }

    private void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        IsModified = false;
        RaiseEvent(new RoutedEventArgs(RevertedEvent));
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":modified", IsModified);
        PseudoClasses.Set(":highlighted", IsHighlighted);
    }
}
