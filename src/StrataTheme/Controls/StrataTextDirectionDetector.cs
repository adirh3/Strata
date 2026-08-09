using System.Globalization;
using System.Text;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// Detects the dominant writing direction (LTR / RTL) of a text string
/// by scanning leading Unicode strong characters. Reusable across any
/// Strata control that needs directional text alignment.
/// </summary>
public static class StrataTextDirectionDetector
{
    /// <summary>Maximum number of UTF-16 code units scanned before stopping.</summary>
    public const int DefaultScanLimit = 384;

    /// <summary>
    /// Detects the leading text direction by counting LTR vs. RTL strong characters
    /// in the first <paramref name="scanLimit"/> characters.
    /// </summary>
    /// <returns>
    /// <see cref="FlowDirection.RightToLeft"/> when RTL characters dominate,
    /// <see cref="FlowDirection.LeftToRight"/> when LTR characters dominate,
    /// or <c>null</c> when the text is empty / neutral (no strong characters found).
    /// </returns>
    public static FlowDirection? Detect(string? text, int scanLimit = DefaultScanLimit)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var firstStrongIsRtl = (bool?)null;
        var rtlStrongCount = 0;
        var ltrStrongCount = 0;
        var scannedChars = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            scannedChars += rune.Utf16SequenceLength;
            if (scannedChars > scanLimit)
                break;

            var directionalMark = GetDirectionalMark(rune.Value);
            if (directionalMark == FlowDirection.RightToLeft)
            {
                firstStrongIsRtl ??= true;
                rtlStrongCount++;
                continue;
            }

            if (directionalMark == FlowDirection.LeftToRight)
            {
                firstStrongIsRtl ??= false;
                ltrStrongCount++;
                continue;
            }

            if (rune.Value <= 0x7F)
            {
                var ascii = (char)rune.Value;
                if (char.IsWhiteSpace(ascii) || char.IsDigit(ascii) || char.IsPunctuation(ascii) || char.IsSymbol(ascii))
                    continue;

                if ((ascii >= 'A' && ascii <= 'Z') || (ascii >= 'a' && ascii <= 'z'))
                {
                    firstStrongIsRtl ??= false;
                    ltrStrongCount++;
                    continue;
                }

            }

            var category = Rune.GetUnicodeCategory(rune);

            if (IsDirectionNeutral(category))
                continue;

            if (IsStrongRtl(rune.Value))
            {
                firstStrongIsRtl ??= true;
                rtlStrongCount++;
                continue;
            }

            if (category is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter)
            {
                firstStrongIsRtl ??= false;
                ltrStrongCount++;
                continue;
            }
        }

        if (rtlStrongCount == 0 && ltrStrongCount == 0)
            return null;

        if (rtlStrongCount == ltrStrongCount)
        {
            return firstStrongIsRtl == true
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }

        return rtlStrongCount > ltrStrongCount
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    public static FlowDirection? DetectLeading(string? text, int scanLimit = DefaultScanLimit)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var scannedChars = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            scannedChars += rune.Utf16SequenceLength;
            if (scannedChars > scanLimit)
                break;

            var directionalMark = GetDirectionalMark(rune.Value);
            if (directionalMark is not null)
                return directionalMark;

            if (rune.Value <= 0x7F)
            {
                var ascii = (char)rune.Value;
                if (ascii is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                    return FlowDirection.LeftToRight;
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (IsDirectionNeutral(category))
                continue;

            if (IsStrongRtl(rune.Value))
                return FlowDirection.RightToLeft;

            if (category is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter)
            {
                return FlowDirection.LeftToRight;
            }
        }

        return null;
    }

    internal static string OrientFlowArrows(string text, FlowDirection flowDirection)
    {
        if (flowDirection != FlowDirection.RightToLeft || string.IsNullOrEmpty(text))
            return text;

        StringBuilder? oriented = null;
        var copyStart = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var replacement = GetOppositeFlowArrow(text[index]);
            var consumed = 1;

            if (replacement is null &&
                TryGetOppositeAsciiFlowArrow(text, index, out var asciiReplacement, out consumed))
            {
                replacement = asciiReplacement;
            }

            if (replacement is null)
                continue;

            oriented ??= new StringBuilder(text.Length);
            oriented.Append(text, copyStart, index - copyStart);
            oriented.Append(replacement);
            index += consumed - 1;
            copyStart = index + 1;
        }

        if (oriented is null)
            return text;

        oriented.Append(text, copyStart, text.Length - copyStart);
        return oriented.ToString();
    }

    private static string? GetOppositeFlowArrow(char value)
    {
        return value switch
        {
            '\u2190' => "\u2192",
            '\u2192' => "\u2190",
            '\u21A4' => "\u21A6",
            '\u21A6' => "\u21A4",
            '\u21D0' => "\u21D2",
            '\u21D2' => "\u21D0",
            '\u27F5' => "\u27F6",
            '\u27F6' => "\u27F5",
            '\u27F8' => "\u27F9",
            '\u27F9' => "\u27F8",
            '\u2B05' => "\u27A1",
            '\u27A1' => "\u2B05",
            _ => null,
        };
    }

    private static bool TryGetOppositeAsciiFlowArrow(
        string text,
        int index,
        out string? replacement,
        out int consumed)
    {
        replacement = null;
        consumed = 1;

        if (!IsFlowArrowBoundary(text, index - 1))
            return false;

        if (index + 3 <= text.Length)
        {
            var token = text.AsSpan(index, 3);
            if (token.SequenceEqual("-->"))
            {
                if (!IsFlowArrowBoundary(text, index + 3))
                    return false;
                replacement = "<--";
                consumed = 3;
                return true;
            }

            if (token.SequenceEqual("<--"))
            {
                if (!IsFlowArrowBoundary(text, index + 3))
                    return false;
                replacement = "-->";
                consumed = 3;
                return true;
            }
        }

        if (index + 2 > text.Length)
            return false;

        var shortToken = text.AsSpan(index, 2);
        if (shortToken.SequenceEqual("->"))
            replacement = "<-";
        else if (shortToken.SequenceEqual("<-"))
            replacement = "->";
        else
            return false;

        if (!IsFlowArrowBoundary(text, index + 2))
        {
            replacement = null;
            return false;
        }

        consumed = 2;
        return true;
    }

    private static bool IsFlowArrowBoundary(string text, int index)
    {
        return index < 0 || index >= text.Length || char.IsWhiteSpace(text[index]);
    }

    private static FlowDirection? GetDirectionalMark(int codePoint)
    {
        return codePoint switch
        {
            0x061C or 0x200F => FlowDirection.RightToLeft,
            0x200E => FlowDirection.LeftToRight,
            _ => null,
        };
    }

    private static bool IsDirectionNeutral(UnicodeCategory category)
    {
        return category is UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol
            or UnicodeCategory.DecimalDigitNumber;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="codePoint"/> falls in a Unicode range
    /// that contains strong right-to-left characters (Hebrew, Arabic, Syriac, etc.).
    /// </summary>
    public static bool IsStrongRtl(int codePoint)
    {
        return (codePoint >= 0x0590 && codePoint <= 0x05FF)   // Hebrew
               || (codePoint >= 0x0600 && codePoint <= 0x06FF) // Arabic
               || (codePoint >= 0x0700 && codePoint <= 0x08FF) // Syriac / Arabic supplements
               || (codePoint >= 0xFB1D && codePoint <= 0xFDFF) // Hebrew / Arabic presentation forms A
               || (codePoint >= 0xFE70 && codePoint <= 0xFEFF) // Arabic presentation forms B
               || (codePoint >= 0x1EE00 && codePoint <= 0x1EEFF); // Arabic Mathematical Alphabetic Symbols
    }
}
