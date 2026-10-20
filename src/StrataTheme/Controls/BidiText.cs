using Avalonia;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>Keeps left-to-right text intact inside right-to-left layouts.</summary>
internal static class BidiText
{
    /// <summary>
    /// Wraps <paramref name="text"/> in a first-strong isolate while <paramref name="owner"/> lays out right to
    /// left, so a left-to-right name such as "Fix (v2)" keeps its order: without the isolate its closing
    /// parenthesis would jump to the start of the line. Otherwise returns the text as it is.
    /// </summary>
    internal static string Isolate(Visual owner, string? text)
        => string.IsNullOrEmpty(text) || owner.FlowDirection != FlowDirection.RightToLeft
            ? text ?? string.Empty
            : "\u2068" + text + "\u2069";
}
