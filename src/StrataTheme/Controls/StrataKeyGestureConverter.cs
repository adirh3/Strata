using System;
using System.Globalization;
using Avalonia.Controls.Converters;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace StrataTheme.Controls;

/// <summary>
/// Shows a menu item's shortcut in the platform's own notation, as <see cref="PlatformKeyGestureConverter"/>
/// does, but names the Enter key as each platform's keyboard does: "Enter" on Windows and Linux, ↩ on macOS
/// (Avalonia knows it by its other name, Return).
/// </summary>
public sealed class StrataKeyGestureConverter : IValueConverter
{
    public static StrataKeyGestureConverter Instance { get; } = new();

    public static string Format(KeyGesture gesture)
    {
        var text = PlatformKeyGestureConverter.ToPlatformString(gesture);
        const string avaloniaName = nameof(Key.Return);
        if (gesture.Key != Key.Enter || !text.EndsWith(avaloniaName, StringComparison.Ordinal))
            return text;

        return text[..^avaloniaName.Length] + (OperatingSystem.IsMacOS() ? "↩" : "Enter");
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is KeyGesture gesture ? Format(gesture) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
