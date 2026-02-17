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
