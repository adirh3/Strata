using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace StrataDemo.Localization;

/// <summary>
/// Provides localized strings with live language switching and FlowDirection awareness.
/// Use <see cref="CreateProxy"/> to get a bindable proxy for XAML.
/// </summary>
public sealed class Strings
{
    public static Strings Instance { get; } = new();

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public CultureInfo Culture
    {
        get => _culture;
        set => _culture = value;
    }

    /// <summary>Returns "RightToLeft" for RTL cultures (Hebrew, Arabic), "LeftToRight" otherwise.</summary>
    public string FlowDirection => _culture.TextInfo.IsRightToLeft ? "RightToLeft" : "LeftToRight";

    public bool IsRtl => _culture.TextInfo.IsRightToLeft;

    public string this[string key]
    {
        get
        {
            var lang = _culture.TwoLetterISOLanguageName;
            if (Resources.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var val))
                return val;
            if (Resources.TryGetValue("en", out var fallback) && fallback.TryGetValue(key, out var fb))
                return fb;
            return $"[{key}]";
        }
    }

    /// <summary>Creates a new proxy with a unique reference so Avalonia re-evaluates all indexer bindings.</summary>
    public StringsProxy CreateProxy() => new(this);

    // ──────────────────────── String tables ────────────────────────

    private static readonly Dictionary<string, Dictionary<string, string>> Resources = new()
    {
        ["en"] = new()
        {
            // App chrome
            ["AppTitle"] = "Strata UI – Design System Preview",
            ["DesignSystem"] = "Design System",
            ["Pages"] = "PAGES",

            // Navigation
            ["Nav.Dashboard"] = "Dashboard",
            ["Nav.FormControls"] = "Form Controls",
            ["Nav.DataGrid"] = "Data Grid",
            ["Nav.Components"] = "Components",
            ["Nav.UxControls"] = "UX Controls",
            ["Nav.AiControls"] = "AI Controls",
            ["Nav.Settings"] = "Settings",
            ["Nav.ChatExperience"] = "Chat Experience",

            // Sidebar toggles
            ["DarkMode"] = "Dark Mode",
            ["Compact"] = "Compact",

            // Dashboard
            ["Dashboard"] = "Dashboard",
            ["Dashboard.Subtitle"] = "System metrics, type hierarchy, and control quality at a glance.",
            ["TotalRevenue"] = "TOTAL REVENUE",
            ["RevenueUp"] = "↑ 12.5% from last month",
            ["ActiveClients"] = "ACTIVE CLIENTS",
            ["NewThisWeek"] = "4 new this week",
            ["Completion"] = "COMPLETION",
            ["TypeScale"] = "Type Scale",
            ["Buttons"] = "Buttons",
            ["Buttons.Subtitle"] = "Action hierarchy: primary, secondary, subtle, and destructive.",
            ["Primary"] = "Primary",
            ["Default"] = "Default",
            ["Subtle"] = "Subtle",
            ["Delete"] = "Delete",
            ["Disabled"] = "Disabled",

            // Forms
            ["FormControls"] = "Form Controls",
            ["FormControls.Subtitle"] = "Inputs, selections, toggles, and progress indicators.",
            ["TextInput"] = "Text Input",
            ["TextInput.Subtitle"] = "Click a field to see the focus indicator.",
            ["FullName"] = "Full name",
            ["EnterYourName"] = "Enter your name...",
            ["EmailDisabled"] = "Email (disabled)",
            ["DisabledInput"] = "Disabled input",
            ["Selection"] = "Selection",
            ["Selection.Subtitle"] = "Dropdown menus and continuous value sliders.",
            ["Region"] = "Region",
            ["ChooseOption"] = "Choose an option...",
            ["Volume"] = "Volume",
            ["CheckBox"] = "CheckBox",
            ["CheckBox.Subtitle"] = "Binary and tri-state toggles.",
            ["EnableNotifications"] = "Enable notifications",
            ["AutoSaveDrafts"] = "Auto-save drafts",
            ["Indeterminate"] = "Indeterminate",
            ["RadioButton"] = "RadioButton",
            ["RadioButton.Subtitle"] = "Mutually exclusive single-select.",
            ["Small"] = "Small",
            ["Medium"] = "Medium",
            ["Large"] = "Large",
            ["ToggleSwitch"] = "ToggleSwitch",
            ["ToggleSwitch.Subtitle"] = "On/off switches for boolean settings.",
            ["WiFi"] = "Wi-Fi",
            ["Bluetooth"] = "Bluetooth",
            ["Progress"] = "Progress",
            ["Progress.Subtitle"] = "Track and visualize completion.",

            // DataGrid
            ["DataGrid"] = "DataGrid",
            ["DataGrid.Subtitle"] = "Tabular data with sorting, selection, and resizable columns.",
            ["DataGrid.Markdown"] = "Markdown Columns",
            ["DataGrid.Markdown.Subtitle"] = "Rich inline markdown in cell templates — bold, italic, inline code, and mixed formatting.",

            // Components
            ["Components"] = "Components",
            ["Components.Subtitle"] = "Navigation patterns, disclosure controls, and layered interaction surfaces.",
            ["TabControl"] = "TabControl",
            ["TabControl.Subtitle"] = "Organize content into switchable views.",
            ["Overview"] = "Overview",
            ["Activity"] = "Activity",
            ["Analytics"] = "Analytics",
            ["Expander"] = "Expander",
            ["Expander.Subtitle"] = "Progressive disclosure — reveal details on demand.",
            ["AdvancedSettings"] = "Advanced Settings",
            ["EnableExperimental"] = "Enable experimental features",
            ["VerboseLogging"] = "Verbose logging",
            ["CustomEndpoint"] = "Custom endpoint URL...",
            ["About"] = "About",
            ["ListBox"] = "ListBox",
            ["ListBox.Subtitle"] = "Scrollable single-select item list.",
            ["OverlayPanel"] = "Overlay Panel",
            ["OverlayPanel.Subtitle"] = "Elevated dialog surface for confirmations.",
            ["ConfirmAction"] = "Confirm Action",
            ["ConfirmAction.Body"] = "This action cannot be undone. Are you sure?",
            ["Cancel"] = "Cancel",
            ["Confirm"] = "Confirm",

            // Settings
            ["Settings"] = "Settings",
            ["Settings.Subtitle"] = "Appearance, notifications, and regional configuration.",
            ["Appearance"] = "Appearance",
            ["Appearance.Subtitle"] = "Choose your preferred visual style.",
            ["CompactDensity"] = "Compact Density",
            ["Notifications"] = "Notifications",
            ["Notifications.Subtitle"] = "Manage how you receive alerts and updates.",
            ["EnableEmailNotifications"] = "Enable email notifications",
            ["EnablePushNotifications"] = "Enable push notifications",
            ["WeeklyDigest"] = "Weekly digest",
            ["RegionLanguage"] = "Region & Language",
            ["RegionLanguage.Subtitle"] = "Set your locale and display preferences.",
            ["Language"] = "Language",
            ["SelectLanguage"] = "Select language...",
            ["Timezone"] = "Timezone",
            ["SelectTimezone"] = "Select timezone...",

            // Settings descriptions
            ["DarkMode.Desc"] = "Switch between light and dark color schemes",
            ["CompactDensity.Desc"] = "Reduce spacing for information-dense layouts",
            ["EnableEmailNotifications.Desc"] = "Receive updates and alerts via email",
            ["EnablePushNotifications.Desc"] = "Get real-time browser push notifications",
            ["WeeklyDigest.Desc"] = "Receive a weekly summary of activity",
            ["Language.Desc"] = "Set the display language for the interface",
            ["Timezone.Desc"] = "Choose your local timezone for timestamps",

            // Settings – Accessibility group
            ["Accessibility"] = "Accessibility",
            ["Accessibility.Subtitle"] = "Enhance readability and interaction for all users.",
            ["HighContrast"] = "High contrast",
            ["HighContrast.Desc"] = "Increase contrast for better visibility",
            ["ReduceMotion"] = "Reduce motion",
            ["ReduceMotion.Desc"] = "Minimise animations throughout the interface",
            ["FontSize"] = "Font size",
            ["FontSize.Desc"] = "Adjust the base text size for the interface",

            // Settings – Data & Privacy group
            ["DataPrivacy"] = "Data & Privacy",
            ["DataPrivacy.Subtitle"] = "Control telemetry and data sharing preferences.",
            ["SendDiagnostics"] = "Send diagnostics",
            ["SendDiagnostics.Desc"] = "Share crash reports and usage data to improve the product",
            ["PersonalisedAds"] = "Personalised content",
            ["PersonalisedAds.Desc"] = "Allow tailored suggestions based on usage patterns",

            // Settings – Startup group
            ["Startup"] = "Startup",
            ["Startup.Subtitle"] = "Control what happens when the application launches.",
            ["RestoreSession"] = "Restore previous session",
            ["RestoreSession.Desc"] = "Reopen windows and tabs from the last session",
            ["CheckUpdates"] = "Check for updates",
            ["CheckUpdates.Desc"] = "Automatically check for new versions on startup",

            // Settings – Date & Time group
            ["DateTimeFormat"] = "Date & Time",
            ["DateTimeFormat.Subtitle"] = "Configure how dates and times are displayed.",
            ["DateFormat"] = "Date format",
            ["DateFormat.Desc"] = "Choose the format for displaying dates",
            ["Use24Hour"] = "Use 24-hour clock",
            ["Use24Hour.Desc"] = "Display time in 24-hour format instead of AM/PM",

            // Charts
            ["Chart.Revenue"] = "Revenue Trend",
            ["Chart.Revenue.Subtitle"] = "Monthly revenue with actual vs. forecast comparison.",
            ["Chart.Budget"] = "Department Budget",
            ["Chart.Budget.Subtitle"] = "Year-over-year budget allocation by department.",
            ["Chart.Distribution"] = "Project Distribution",
            ["Chart.Distribution.Subtitle"] = "Resource allocation across project categories.",
            ["Chart.Clients"] = "Client Segments",
            ["Chart.Clients.Subtitle"] = "Client distribution by segment type.",
        },

        ["he"] = new()
        {
            // App chrome
            ["AppTitle"] = "Strata UI – תצוגה מקדימה של מערכת העיצוב",
            ["DesignSystem"] = "מערכת עיצוב",
            ["Pages"] = "עמודים",

            // Navigation
            ["Nav.Dashboard"] = "לוח בקרה",
            ["Nav.FormControls"] = "פקדי טפסים",
            ["Nav.DataGrid"] = "טבלת נתונים",
            ["Nav.Components"] = "רכיבים",
            ["Nav.UxControls"] = "פקדי UX",
            ["Nav.AiControls"] = "פקדי AI",
            ["Nav.Settings"] = "הגדרות",
            ["Nav.ChatExperience"] = "חוויית צ׳אט",

            // Sidebar toggles
            ["DarkMode"] = "מצב כהה",
            ["Compact"] = "צפוף",

            // Dashboard
            ["Dashboard"] = "לוח בקרה",
            ["Dashboard.Subtitle"] = "מדדי מערכת, היררכיית טיפוסים ואיכות פקדים במבט אחד.",
            ["TotalRevenue"] = "הכנסות כוללות",
            ["RevenueUp"] = "↑ 12.5% מהחודש הקודם",
            ["ActiveClients"] = "לקוחות פעילים",
            ["NewThisWeek"] = "4 חדשים השבוע",
            ["Completion"] = "השלמה",
            ["TypeScale"] = "סולם טיפוגרפי",
            ["Buttons"] = "כפתורים",
            ["Buttons.Subtitle"] = "היררכיית פעולות: ראשי, משני, עדין והרסני.",
            ["Primary"] = "ראשי",
            ["Default"] = "ברירת מחדל",
            ["Subtle"] = "עדין",
            ["Delete"] = "מחיקה",
            ["Disabled"] = "מושבת",

            // Forms
            ["FormControls"] = "פקדי טפסים",
            ["FormControls.Subtitle"] = "שדות קלט, בחירות, מתגים ומחווני התקדמות.",
            ["TextInput"] = "קלט טקסט",
            ["TextInput.Subtitle"] = "לחצו על שדה כדי לראות את מחוון המיקוד.",
            ["FullName"] = "שם מלא",
            ["EnterYourName"] = "הזינו את שמכם...",
            ["EmailDisabled"] = "אימייל (מושבת)",
            ["DisabledInput"] = "קלט מושבת",
            ["Selection"] = "בחירה",
            ["Selection.Subtitle"] = "תפריטים נפתחים ומחוונים.",
            ["Region"] = "אזור",
            ["ChooseOption"] = "בחרו אפשרות...",
            ["Volume"] = "עוצמה",
            ["CheckBox"] = "תיבת סימון",
            ["CheckBox.Subtitle"] = "מתגי דו-מצב ותלת-מצב.",
            ["EnableNotifications"] = "הפעלת התראות",
            ["AutoSaveDrafts"] = "שמירה אוטומטית של טיוטות",
            ["Indeterminate"] = "לא מוגדר",
            ["RadioButton"] = "כפתור בחירה",
            ["RadioButton.Subtitle"] = "בחירה בודדת מתוך קבוצה.",
            ["Small"] = "קטן",
            ["Medium"] = "בינוני",
            ["Large"] = "גדול",
            ["ToggleSwitch"] = "מתג",
            ["ToggleSwitch.Subtitle"] = "מתגי הפעלה/כיבוי להגדרות.",
            ["WiFi"] = "Wi-Fi",
            ["Bluetooth"] = "Bluetooth",
            ["Progress"] = "התקדמות",
            ["Progress.Subtitle"] = "מעקב והמחשת השלמה.",

            // DataGrid
            ["DataGrid"] = "טבלת נתונים",
            ["DataGrid.Subtitle"] = "נתונים טבלאיים עם מיון, בחירה ועמודות ניתנות לשינוי גודל.",
            ["DataGrid.Markdown"] = "עמודות Markdown",
            ["DataGrid.Markdown.Subtitle"] = "Markdown עשיר בתוך תאי טבלה — מודגש, נטוי, קוד משובץ ועיצוב מעורב.",

            // Components
            ["Components"] = "רכיבים",
            ["Components.Subtitle"] = "תבניות ניווט, פקדי חשיפה ומשטחי אינטראקציה שכבתיים.",
            ["TabControl"] = "לשוניות",
            ["TabControl.Subtitle"] = "ארגון תוכן בתצוגות ניתנות למעבר.",
            ["Overview"] = "סקירה",
            ["Activity"] = "פעילות",
            ["Analytics"] = "אנליטיקה",
            ["Expander"] = "מרחיב",
            ["Expander.Subtitle"] = "חשיפה הדרגתית — הצגת פרטים לפי דרישה.",
            ["AdvancedSettings"] = "הגדרות מתקדמות",
            ["EnableExperimental"] = "הפעלת תכונות ניסיוניות",
            ["VerboseLogging"] = "רישום מפורט",
            ["CustomEndpoint"] = "כתובת endpoint מותאמת...",
            ["About"] = "אודות",
            ["ListBox"] = "רשימה",
            ["ListBox.Subtitle"] = "רשימת פריטים נגללת עם בחירה בודדת.",
            ["OverlayPanel"] = "חלונית שכבת-על",
            ["OverlayPanel.Subtitle"] = "משטח דו-שיח מורם לאישורים.",
            ["ConfirmAction"] = "אישור פעולה",
            ["ConfirmAction.Body"] = "לא ניתן לבטל פעולה זו. האם להמשיך?",
            ["Cancel"] = "ביטול",
            ["Confirm"] = "אישור",

            // Settings
            ["Settings"] = "הגדרות",
            ["Settings.Subtitle"] = "מראה, התראות והגדרות אזוריות.",
            ["Appearance"] = "מראה",
            ["Appearance.Subtitle"] = "בחרו את הסגנון החזותי המועדף.",
            ["CompactDensity"] = "צפיפות גבוהה",
            ["Notifications"] = "התראות",
            ["Notifications.Subtitle"] = "ניהול קבלת עדכונים והתראות.",
            ["EnableEmailNotifications"] = "הפעלת התראות בדוא״ל",
            ["EnablePushNotifications"] = "הפעלת התראות Push",
            ["WeeklyDigest"] = "סיכום שבועי",
            ["RegionLanguage"] = "אזור ושפה",
            ["RegionLanguage.Subtitle"] = "קביעת מיקום ותצוגה.",
            ["Language"] = "שפה",
            ["SelectLanguage"] = "בחרו שפה...",
            ["Timezone"] = "אזור זמן",
            ["SelectTimezone"] = "בחרו אזור זמן...",

            // Settings descriptions
            ["DarkMode.Desc"] = "מעבר בין ערכות צבעים בהירה וכהה",
            ["CompactDensity.Desc"] = "צמצום רווחים לממשקים צפופים",
            ["EnableEmailNotifications.Desc"] = "קבלת עדכונים והתראות בדוא״ל",
            ["EnablePushNotifications.Desc"] = "התראות Push בזמן אמת בדפדפן",
            ["WeeklyDigest.Desc"] = "קבלת סיכום שבועי של הפעילות",
            ["Language.Desc"] = "הגדרת שפת התצוגה של הממשק",
            ["Timezone.Desc"] = "בחירת אזור הזמן המקומי לחותמות זמן",

            // Settings – Accessibility group
            ["Accessibility"] = "נגישות",
            ["Accessibility.Subtitle"] = "שיפור קריאות ואינטראקציה לכלל המשתמשים.",
            ["HighContrast"] = "ניגודיות גבוהה",
            ["HighContrast.Desc"] = "הגברת הניגודיות לנראות טובה יותר",
            ["ReduceMotion"] = "צמצום תנועה",
            ["ReduceMotion.Desc"] = "הפחתת אנימציות בממשק",
            ["FontSize"] = "גודל גופן",
            ["FontSize.Desc"] = "התאמת גודל הטקסט הבסיסי בממשק",

            // Settings – Data & Privacy group
            ["DataPrivacy"] = "נתונים ופרטיות",
            ["DataPrivacy.Subtitle"] = "שליטה בהעדפות טלמטריה ושיתוף נתונים.",
            ["SendDiagnostics"] = "שליחת אבחון",
            ["SendDiagnostics.Desc"] = "שיתוף דוחות קריסה ונתוני שימוש לשיפור המוצר",
            ["PersonalisedAds"] = "תוכן מותאם אישית",
            ["PersonalisedAds.Desc"] = "הצעות מותאמות על פי דפוסי שימוש",

            // Settings – Startup group
            ["Startup"] = "הפעלה",
            ["Startup.Subtitle"] = "שליטה בהתנהגות בעת הפעלת היישום.",
            ["RestoreSession"] = "שחזור הפעלה קודמת",
            ["RestoreSession.Desc"] = "פתיחת חלונות ולשוניות מההפעלה האחרונה",
            ["CheckUpdates"] = "בדיקת עדכונים",
            ["CheckUpdates.Desc"] = "בדיקה אוטומטית של גרסאות חדשות בהפעלה",

            // Settings – Date & Time group
            ["DateTimeFormat"] = "תאריך ושעה",
            ["DateTimeFormat.Subtitle"] = "הגדרת תצוגת תאריכים ושעות.",
            ["DateFormat"] = "תבנית תאריך",
            ["DateFormat.Desc"] = "בחירת התבנית להצגת תאריכים",
            ["Use24Hour"] = "שעון 24 שעות",
            ["Use24Hour.Desc"] = "הצגת שעה בתבנית 24 שעות במקום AM/PM",

            // Charts
            ["Chart.Revenue"] = "מגמת הכנסות",
            ["Chart.Revenue.Subtitle"] = "הכנסות חודשיות עם השוואה לתחזית.",
            ["Chart.Budget"] = "תקציב מחלקות",
            ["Chart.Budget.Subtitle"] = "הקצאת תקציב שנתית לפי מחלקה.",
            ["Chart.Distribution"] = "חלוקת פרויקטים",
            ["Chart.Distribution.Subtitle"] = "הקצאת משאבים בין קטגוריות פרויקטים.",
            ["Chart.Clients"] = "פלחי לקוחות",
            ["Chart.Clients.Subtitle"] = "התפלגות לקוחות לפי סוג פלח.",
        }
    };
}

/// <summary>
/// Lightweight proxy that wraps <see cref="Strings"/> with a new object reference
/// so Avalonia's binding system re-evaluates all <c>{Binding Strings[Key]}</c> paths.
/// </summary>
public sealed class StringsProxy
{
    private readonly Strings _inner;
    internal StringsProxy(Strings inner) => _inner = inner;

    public string this[string key] => _inner[key];
    public string FlowDirection => _inner.FlowDirection;
    public bool IsRtl => _inner.IsRtl;
}
