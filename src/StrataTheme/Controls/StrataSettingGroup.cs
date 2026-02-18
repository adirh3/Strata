using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace StrataTheme.Controls;

/// <summary>
/// Groups multiple <see cref="StrataSetting"/> items under a shared card surface
/// with a common header and description. Settings within a group share the card
/// and are separated by subtle dividers rather than individual borders.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataSettingGroup Header="Appearance"
///                               Description="Visual preferences"&gt;
///     &lt;controls:StrataSetting Header="Dark Mode"&gt;
///         &lt;ToggleSwitch /&gt;
///     &lt;/controls:StrataSetting&gt;
///     &lt;controls:StrataSetting Header="Compact"&gt;
///         &lt;ToggleSwitch /&gt;
///     &lt;/controls:StrataSetting&gt;
/// &lt;/controls:StrataSettingGroup&gt;
/// </code>
/// </remarks>
public class StrataSettingGroup : ItemsControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<StrataSettingGroup, string?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<StrataSettingGroup, string?>(nameof(Description));

    /// <summary>The group title displayed at the top of the card.</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Optional description shown below the header.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (item is StrataSetting setting)
        {
            setting.ShowSeparator = index > 0;
        }
    }
}
