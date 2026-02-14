using Avalonia.Styling;

namespace StrataTheme;

/// <summary>
/// Custom theme variants provided by Strata UI beyond the built-in Light/Dark.
/// Use with <c>x:Static</c> in XAML or assign programmatically.
/// </summary>
public static class StrataThemeVariants
{
    /// <summary>
    /// High-contrast variant (inherits from Dark).
    /// </summary>
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);
}
