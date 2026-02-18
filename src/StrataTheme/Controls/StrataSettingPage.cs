using Avalonia;
using Avalonia.Controls;

namespace StrataTheme.Controls;

/// <summary>
/// A tabbed settings page container. Each tab organizes a category of settings,
/// typically containing <see cref="StrataSettingGroup"/> controls.
/// Derives from <see cref="TabControl"/> and adds a built-in scrollable content area
/// with optional page-level header and description.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataSettingPage Header="Settings" Description="Configure your preferences"&gt;
///     &lt;TabItem Header="Appearance"&gt;
///         &lt;controls:StrataSettingGroup Header="Theme"&gt;
///             &lt;controls:StrataSetting Header="Dark Mode"&gt;
///                 &lt;ToggleSwitch /&gt;
///             &lt;/controls:StrataSetting&gt;
///         &lt;/controls:StrataSettingGroup&gt;
///     &lt;/TabItem&gt;
/// &lt;/controls:StrataSettingPage&gt;
/// </code>
/// </remarks>
public class StrataSettingPage : TabControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<StrataSettingPage, object?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<StrataSettingPage, string?>(nameof(Description));

    /// <summary>The page title displayed above the tab strip.</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Optional subtitle displayed below the header.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
