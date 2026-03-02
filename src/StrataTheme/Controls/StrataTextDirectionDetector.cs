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

            if (category is UnicodeCategory.SpaceSeparator
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
                or UnicodeCategory.DecimalDigitNumber)
            {
                continue;
            }

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
