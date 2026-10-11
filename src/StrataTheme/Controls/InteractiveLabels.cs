using System;
using System.Collections.Generic;
using System.Globalization;

namespace StrataTheme.Controls;

/// <summary>Shared English and Hebrew UI labels for native interactions.</summary>
public sealed class InteractiveLabels
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private InteractiveLabels(IReadOnlyDictionary<string, string> values) => _values = values;

    internal IEnumerable<string> Keys => _values.Keys;

    public string this[string key] => _values.TryGetValue(key, out var value)
        ? value : English._values.TryGetValue(key, out value) ? value : key;

    public string Format(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, this[key], values);

    public static InteractiveLabels ForLanguage(string? language) =>
        language?.StartsWith("he", StringComparison.OrdinalIgnoreCase) == true ? Hebrew : English;

    public static InteractiveLabels English { get; } = new(new Dictionary<string, string>
    {
        // Kinds (accessible names and tooltips)
        ["form"] = "Form", ["checklist"] = "Checklist", ["steps"] = "Steps", ["results"] = "Choices",
        ["calculator"] = "Calculator", ["quiz"] = "Practice",

        // Actions
        ["continue"] = "Continue", ["remaining"] = "Help with the rest", ["nextStepHelp"] = "Help with the next step",
        ["compare"] = "Compare", ["use"] = "Use selected", ["clear"] = "Clear", ["assumptions"] = "Use these numbers",
        ["discuss"] = "Discuss results", ["check"] = "Check answer", ["hint"] = "Hint", ["next"] = "Next",
        ["previous"] = "Back", ["restart"] = "Start over", ["cancelInput"] = "Cancel", ["reset"] = "Reset",
        ["sending"] = "Sending\u2026", ["sent"] = "Sent",

        // States
        ["needsInput"] = "Needs your input", ["inputCanceled"] = "Canceled",
        ["inputExpired"] = "This request has ended", ["ended"] = "Ended", ["nothingSent"] = "Nothing was sent.",
        ["endedHint"] = "This request ended before it was sent. Your draft is kept.",
        ["preview"] = "Preview", ["allDone"] = "All done", ["progressShort"] = "{0}/{1}",
        ["selected"] = "{0} selected", ["questionShort"] = "{0} of {1}",

        // Inputs and validation
        ["filter"] = "Filter options", ["empty"] = "No options match.", ["select"] = "Select\u2026",
        ["datePlaceholder"] = "Pick a date", ["invalid"] = "Check the highlighted fields.",
        ["required"] = "This field is required.", ["number"] = "Enter a valid number.", ["date"] = "Choose a valid date.",
        ["choice"] = "Choose one of the available options.", ["range"] = "Use a value within the allowed range.",
        ["between"] = "Use a number from {0} to {1}.", ["wholeBetween"] = "Use a whole number from {0} to {1}.",
        ["chooseAnswer"] = "Choose an answer first.",
        ["unavailable"] = "This action isn't available here.", ["notSent"] = "Not sent. Your input is still here.",
        ["restoreError"] = "Couldn't restore this interaction. Reset to use the original inputs.",
        ["definitionError"] = "This interactive response couldn't be displayed.",
        ["yes"] = "Yes", ["no"] = "No", ["notProvided"] = "Not provided",
        ["sourceUnavailable"] = "The original control is no longer in this chat. The submitted snapshot is still available.",

        // Practice
        ["correct"] = "Correct", ["incorrect"] = "Not quite", ["score"] = "{0} of {1} correct",
        ["scoreGreat"] = "Great work", ["scoreGood"] = "Good progress", ["scoreRetry"] = "Worth another try",

        // Calculator inputs and results
        ["bill"] = "Bill", ["tipPercent"] = "Tip", ["people"] = "People",
        ["initial"] = "Starting amount", ["monthly"] = "Monthly deposit", ["annualRate"] = "Annual return", ["years"] = "Years",
        ["baseServings"] = "Recipe serves", ["servings"] = "Servings",
        ["income"] = "Monthly income", ["essential"] = "Essentials", ["flexible"] = "Flexible", ["savings"] = "Savings",
        ["loanAmount"] = "Loan amount", ["interestRate"] = "Interest rate",
        ["perPerson"] = "Each person pays", ["total"] = "Total", ["tip"] = "Tip",
        ["projected"] = "Projected balance", ["contributed"] = "Your deposits", ["growth"] = "Growth",
        ["multiplier"] = "Scale", ["remainingMetric"] = "Left over", ["allocated"] = "Allocated",
        ["payment"] = "Monthly payment", ["totalInterest"] = "Total interest", ["totalPaid"] = "Total paid",
        ["principal"] = "Principal", ["interest"] = "Interest", ["balance"] = "Balance", ["contributions"] = "Deposits",
        ["yearsValue"] = "{0} yrs", ["yearsUnit"] = "yrs", ["overBudgetLabel"] = "Over budget by",
        ["ingredientsFor"] = "For {0} servings",
        ["savingsNote"] = "Estimate only \u2014 monthly compounding, month-end deposits; excludes taxes, fees, and inflation.",
        ["loanNote"] = "Estimate only \u2014 fixed rate, monthly payments; excludes taxes, insurance, and fees.",

        // Receipts
        ["receiptForm"] = "Shared my preferences", ["receiptCompare"] = "Compare {0} options",
        ["receiptUse"] = "Continue with {0} selected", ["receiptCalculator"] = "Use these numbers",
        ["receiptRemaining"] = "Help with {0} remaining", ["receiptNextStep"] = "Help with step {0}",
        ["stepLabel"] = "Step {0}: {1}", ["stepItem"] = "{0}. {1}", ["receiptQuiz"] = "Discuss my practice results",
        ["moreInputs"] = " \u00b7 +{0} more", ["viewInputs"] = "View inputs", ["hideInputs"] = "Hide inputs",
        ["viewDraft"] = "View draft", ["hideDraft"] = "Hide draft", ["showOriginal"] = "Show original",
        ["submitted"] = "Sent", ["working"] = "Working\u2026", ["replyReady"] = "Reply ready",
        ["failed"] = "Needs attention", ["stopped"] = "Stopped",
    });

    public static InteractiveLabels Hebrew { get; } = new(new Dictionary<string, string>
    {
        ["form"] = "טופס", ["checklist"] = "רשימת משימות", ["steps"] = "שלבים", ["results"] = "אפשרויות",
        ["calculator"] = "מחשבון", ["quiz"] = "תרגול",

        ["continue"] = "המשך", ["remaining"] = "עזרה בשאר", ["nextStepHelp"] = "עזרה בשלב הבא",
        ["compare"] = "השוואה", ["use"] = "שימוש בנבחרים", ["clear"] = "ניקוי", ["assumptions"] = "שימוש במספרים האלה",
        ["discuss"] = "נדבר על התוצאות", ["check"] = "בדיקת תשובה", ["hint"] = "רמז", ["next"] = "הבא",
        ["previous"] = "הקודם", ["restart"] = "התחלה מחדש", ["cancelInput"] = "ביטול", ["reset"] = "איפוס",
        ["sending"] = "שולח\u2026", ["sent"] = "נשלח",

        ["needsInput"] = "ממתין לפרטים שלך", ["inputCanceled"] = "בוטל",
        ["inputExpired"] = "הבקשה הסתיימה", ["ended"] = "הסתיים", ["nothingSent"] = "לא נשלח דבר.",
        ["endedHint"] = "הבקשה הסתיימה לפני השליחה. הטיוטה נשמרה.",
        ["preview"] = "תצוגה מקדימה", ["allDone"] = "הכול בוצע", ["progressShort"] = "{0}/{1}",
        ["selected"] = "{0} נבחרו", ["questionShort"] = "{0} מתוך {1}",

        ["filter"] = "סינון אפשרויות", ["empty"] = "אין אפשרויות תואמות.", ["select"] = "בחירה\u2026",
        ["datePlaceholder"] = "בחירת תאריך", ["invalid"] = "יש לבדוק את השדות המסומנים.",
        ["required"] = "יש למלא את השדה.", ["number"] = "יש להזין מספר תקין.", ["date"] = "יש לבחור תאריך תקין.",
        ["choice"] = "יש לבחור באחת האפשרויות.", ["range"] = "יש להשתמש בערך בטווח המותר.",
        ["between"] = "יש להזין מספר בין {0} ל־{1}.", ["wholeBetween"] = "יש להזין מספר שלם בין {0} ל־{1}.",
        ["chooseAnswer"] = "יש לבחור תשובה תחילה.",
        ["unavailable"] = "הפעולה אינה זמינה כאן.", ["notSent"] = "לא נשלח. הפרטים שלך עדיין כאן.",
        ["restoreError"] = "לא ניתן לשחזר את הפעולה. אפס כדי להשתמש בפרטים המקוריים.",
        ["definitionError"] = "לא ניתן להציג את התשובה האינטראקטיבית.",
        ["yes"] = "כן", ["no"] = "לא", ["notProvided"] = "לא צוין",
        ["sourceUnavailable"] = "הפעולה המקורית כבר אינה בצ'אט. הפרטים שנשלחו עדיין זמינים.",

        ["correct"] = "נכון", ["incorrect"] = "לא בדיוק", ["score"] = "{0} מתוך {1} נכונות",
        ["scoreGreat"] = "עבודה מצוינת", ["scoreGood"] = "התקדמות יפה", ["scoreRetry"] = "שווה לנסות שוב",

        ["bill"] = "חשבון", ["tipPercent"] = "טיפ", ["people"] = "אנשים",
        ["initial"] = "סכום התחלתי", ["monthly"] = "הפקדה חודשית", ["annualRate"] = "תשואה שנתית", ["years"] = "שנים",
        ["baseServings"] = "המתכון מיועד ל־", ["servings"] = "מנות",
        ["income"] = "הכנסה חודשית", ["essential"] = "הוצאות חיוניות", ["flexible"] = "הוצאות גמישות", ["savings"] = "חיסכון",
        ["loanAmount"] = "סכום ההלוואה", ["interestRate"] = "ריבית שנתית",
        ["perPerson"] = "כל אחד משלם", ["total"] = "סך הכול", ["tip"] = "טיפ",
        ["projected"] = "יתרה צפויה", ["contributed"] = "ההפקדות שלך", ["growth"] = "צמיחה",
        ["multiplier"] = "יחס", ["remainingMetric"] = "נותר", ["allocated"] = "הוקצה",
        ["payment"] = "החזר חודשי", ["totalInterest"] = "סך הריבית", ["totalPaid"] = "סך ההחזר",
        ["principal"] = "קרן", ["interest"] = "ריבית", ["balance"] = "יתרה", ["contributions"] = "הפקדות",
        ["yearsValue"] = "{0} שנים", ["yearsUnit"] = "שנים", ["overBudgetLabel"] = "חריגה מהתקציב",
        ["ingredientsFor"] = "ל־{0} מנות",
        ["savingsNote"] = "הערכה בלבד \u2014 ריבית דריבית חודשית והפקדות בסוף החודש, ללא מסים, עמלות ואינפלציה.",
        ["loanNote"] = "הערכה בלבד \u2014 ריבית קבועה והחזרים חודשיים, ללא מסים, ביטוח ועמלות.",

        ["receiptForm"] = "שיתפתי את ההעדפות שלי", ["receiptCompare"] = "השוואת {0} אפשרויות",
        ["receiptUse"] = "המשך עם {0} שנבחרו", ["receiptCalculator"] = "שימוש במספרים האלה",
        ["receiptRemaining"] = "עזרה ב־{0} שנותרו", ["receiptNextStep"] = "עזרה בשלב {0}",
        ["stepLabel"] = "שלב {0}: {1}", ["stepItem"] = "{0}. {1}", ["receiptQuiz"] = "דיון בתוצאות התרגול שלי",
        ["moreInputs"] = " \u00b7 ועוד {0}", ["viewInputs"] = "הצגת הפרטים", ["hideInputs"] = "הסתרת הפרטים",
        ["viewDraft"] = "הצגת הטיוטה", ["hideDraft"] = "הסתרת הטיוטה", ["showOriginal"] = "הצגת המקור",
        ["submitted"] = "נשלח", ["working"] = "בעבודה\u2026", ["replyReady"] = "התשובה מוכנה",
        ["failed"] = "דורש תשומת לב", ["stopped"] = "נעצר",
    });
}
