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
            ["Nav.ChatPerformance"] = "Chat Performance",

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
            ["StrataComboBox"] = "StrataComboBox",
            ["StrataComboBox.Subtitle"] = "Enhanced combo box with single-select, multi-select, and search filtering.",
            ["StrataComboBox.Single"] = "Single Select",
            ["StrataComboBox.Multi"] = "Multi Select",
            ["StrataComboBox.MultiPlaceholder"] = "Select items...",
            ["StrataComboBox.Search"] = "Searchable",
            ["StrataComboBox.SearchPlaceholder"] = "Search & select...",
            ["StrataComboBox.SearchWatermark"] = "Type to filter...",
            ["StrataComboBox.SelectAll"] = "Select all",
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

            // Flyouts
            ["Flyouts"] = "Flyouts",
            ["Flyouts.Subtitle"] = "Lightweight popups anchored to a trigger — for info, confirmation, or quick actions.",
            ["Flyouts.Simple"] = "Info flyout",
            ["Flyouts.ShowInfo"] = "Show Info",
            ["Flyouts.InfoTitle"] = "Quick tip",
            ["Flyouts.InfoBody"] = "Flyouts display contextual content without leaving the current view. They dismiss on outside click.",
            ["Flyouts.Confirm"] = "Confirm flyout",
            ["Flyouts.Delete"] = "Delete Item",
            ["Flyouts.DeleteTitle"] = "Delete this item?",
            ["Flyouts.DeleteBody"] = "This will permanently remove the item and all associated data. This action cannot be undone.",
            ["Flyouts.DeleteConfirm"] = "Yes, Delete",
            ["Flyouts.Menu"] = "Menu flyout",
            ["Flyouts.Actions"] = "Actions ▾",
            ["Flyouts.MenuItem.Edit"] = "Edit",
            ["Flyouts.MenuItem.Duplicate"] = "Duplicate",
            ["Flyouts.MenuItem.Archive"] = "Archive",
            ["Flyouts.MenuItem.Delete"] = "Delete",

            // Context Menus
            ["ContextMenus"] = "Context Menus",
            ["ContextMenus.Subtitle"] = "Right-click surfaces — edit actions, nested submenus, and keyboard shortcuts.",
            ["ContextMenus.Cut"] = "Cut",
            ["ContextMenus.Copy"] = "Copy",
            ["ContextMenus.Paste"] = "Paste",
            ["ContextMenus.SelectAll"] = "Select All",
            ["ContextMenus.EditArea"] = "Edit Area",
            ["ContextMenus.EditHint"] = "Right-click for clipboard actions",
            ["ContextMenus.ViewDetails"] = "View Details",
            ["ContextMenus.Share"] = "Share",
            ["ContextMenus.ShareEmail"] = "Email",
            ["ContextMenus.ShareLink"] = "Copy Link",
            ["ContextMenus.ShareSlack"] = "Slack",
            ["ContextMenus.Rename"] = "Rename",
            ["ContextMenus.MoveTo"] = "Move To",
            ["ContextMenus.Folder.Archive"] = "Archive",
            ["ContextMenus.Folder.Projects"] = "Projects",
            ["ContextMenus.Folder.Starred"] = "Starred",
            ["ContextMenus.Delete"] = "Delete",
            ["ContextMenus.FileCard"] = "File Card",
            ["ContextMenus.FileHint"] = "Right-click for file actions & submenus",

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

            // Mermaid Diagrams
            ["MermaidDiagrams"] = "Mermaid Diagrams",
            ["MermaidDiagrams.Subtitle"] = "Flowcharts, sequences, state machines, class diagrams, timelines, and quadrant charts rendered from standard Mermaid syntax via StrataMarkdown.",
            ["MermaidDiagrams.Footer"] = "Use ```mermaid fenced blocks with graph/flowchart, sequenceDiagram, stateDiagram-v2, classDiagram, timeline, or quadrantChart syntax. Pie and xychart-beta also supported.",

            // Charts
            ["Chart.Revenue"] = "Revenue Trend",
            ["Chart.Revenue.Subtitle"] = "Monthly revenue with actual vs. forecast comparison.",
            ["Chart.Budget"] = "Department Budget",
            ["Chart.Budget.Subtitle"] = "Year-over-year budget allocation by department.",
            ["Chart.Distribution"] = "Project Distribution",
            ["Chart.Distribution.Subtitle"] = "Resource allocation across project categories.",
            ["Chart.Clients"] = "Client Segments",
            ["Chart.Clients.Subtitle"] = "Client distribution by segment type.",

            // Chat Experience
            ["Chat.User1"] = "Review incident IR-4471 and recommend a mitigation plan.",
            ["Chat.Assistant1"] = "Allocation bursts in the serializer hot path caused GC pause inflation, degrading p95 latency by 340 ms during peak traffic windows.",
            ["Chat.Editing"] = "I'm now comparing against pre-change baselines to isolate regressions introduced by this patch.",
            ["Chat.Markdown"] = "# Suggested fix\nUse a staged rollout and cap payload size for hot serializer paths.\n\n```csharp\npublic static bool ShouldRollback(double p95Ms, double gcPauseMs)\n{\n    if (p95Ms > 250 || gcPauseMs > 80)\n        return true;\n\n    return false;\n}\n```\n\n- Gate rollout at 10%, 50%, 100%\n- Roll back immediately on threshold breach",
            ["Chat.InlineCode"] = "The config lives in `appsettings.json` under the `Serializer` section. See the file `HotPathSerializer.cs` for the allocation site, and make sure `MaxBatchBytes` is set before deploying.",
            ["Chat.ToolTitle"] = "Scale worker pool and cap batch payload",
            ["Chat.ToolDesc"] = "Setting batch_max_bytes = 262144 reduces serializer pressure.",
            ["Chat.Streaming"] = "Additionally, I'd recommend setting up an automated alert on the GC pause duration metric\u2026",
            ["Chat.System"] = "Workspace policies loaded. Grounded citations enabled.",
            ["Chat.TypingLabel1"] = "Validating behavior with targeted tests next so findings are evidence-based",
            ["Chat.TypingLabel2"] = "Generating response\u2026",
            ["Chat.TypingPaused"] = "Paused",
            ["Chat.Placeholder1"] = "Ask for follow-up changes",
            ["Chat.Placeholder2"] = "What would you like to know?",
            ["Chat.SuggestionA"] = "Summarize",
            ["Chat.SuggestionB"] = "Root cause",
            ["Chat.SuggestionC"] = "Action items",
            ["Chat.ShellHeader"] = "Incident Assistant",
            ["Chat.ShellSubtitle"] = "workspace / prod",
            ["Chat.Presence"] = "Connected",
            ["Chat.ShellUser"] = "What caused the latency spike?",
            ["Chat.ShellAssistant"] = "GC pause inflation from allocation bursts in the serializer hot path during peak traffic.",
            ["Chat.ShellTyping"] = "Checking deployment logs\u2026",
            ["Chat.FollowUp"] = "Follow up\u2026",
            ["Chat.MixedRtlLead"] = "שלום אני adir. זה משפט בדיקה ראשון כדי לוודא שהיישור פועל נכון גם כשיש מילים באנגלית באמצע. עכשיו נוסיף עוד משפט בעברית עם timestamp 09:57 ו-reference לקובץ docs/guide.md.",
            ["Chat.MixedLtrLead"] = "hello I am אדיר. This is the first verification sentence to confirm alignment stays left even when Hebrew appears inline. Next sentence adds more English context with incident IR-4471 and deployment notes.",

            // Chat Performance
            ["ChatPerf.Title"] = "Chat Performance Lab",
            ["ChatPerf.Subtitle"] = "Automated stress benchmark for chat scrolling and streaming frame-time stability.",
            ["ChatPerf.Presence"] = "Perf Lab",
            ["ChatPerf.ShellTitle"] = "Transcript Stress Harness",
            ["ChatPerf.ShellSubtitle"] = "baseline vs optimized · auto-scroll + streaming",
            ["ChatPerf.Seed"] = "Seed Transcript",
            ["ChatPerf.Run"] = "Run Benchmark",
            ["ChatPerf.Stop"] = "Stop",
            ["ChatPerf.ScenarioTitle"] = "Scenario",
            ["ChatPerf.Scenario"] = "1,200 chat messages + 10-second automated scroll sweep + large markdown streaming payload. Measures FPS, frame-time p95, worst frame, and slow-frame ratio.",
            ["ChatPerf.ResultsTitle"] = "Results",
            ["ChatPerf.StatusLabel"] = "Status",
            ["ChatPerf.StatusIdle"] = "Idle",
            ["ChatPerf.BaselineLabel"] = "Baseline (Legacy)",
            ["ChatPerf.OptimizedLabel"] = "Optimized",
            ["ChatPerf.UpliftLabel"] = "Measured Uplift",
            ["ChatPerf.NoResults"] = "No results yet.",
            ["ChatPerf.StatusSeeded"] = "Transcript seeded with many messages. Run benchmark to compare baseline vs optimized.",
            ["ChatPerf.StatusRunningBaseline"] = "Running baseline scenario profile…",
            ["ChatPerf.StatusRunningOptimized"] = "Running optimized scenario profile…",
            ["ChatPerf.StatusCompleted"] = "Benchmark complete. Optimized mode maintains higher FPS and lower frame times under streaming + scroll stress.",
            ["ChatPerf.StatusCancelled"] = "Benchmark canceled.",
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
            ["Nav.ChatPerformance"] = "ביצועי צ׳אט",

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
            ["StrataComboBox"] = "StrataComboBox",
            ["StrataComboBox.Subtitle"] = "תיבה משולבת עם בחירה בודדת, מרובה וסינון חיפוש.",
            ["StrataComboBox.Single"] = "בחירה בודדת",
            ["StrataComboBox.Multi"] = "בחירה מרובה",
            ["StrataComboBox.MultiPlaceholder"] = "בחרו פריטים...",
            ["StrataComboBox.Search"] = "ניתן לחיפוש",
            ["StrataComboBox.SearchPlaceholder"] = "חפשו ובחרו...",
            ["StrataComboBox.SearchWatermark"] = "הקלידו לסינון...",
            ["StrataComboBox.SelectAll"] = "בחר הכל",
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

            // Flyouts
            ["Flyouts"] = "חלוניות קופצות",
            ["Flyouts.Subtitle"] = "חלוניות קלות המעוגנות לפקד — למידע, אישור או פעולות מהירות.",
            ["Flyouts.Simple"] = "חלונית מידע",
            ["Flyouts.ShowInfo"] = "הצג מידע",
            ["Flyouts.InfoTitle"] = "טיפ מהיר",
            ["Flyouts.InfoBody"] = "חלוניות קופצות מציגות תוכן הקשרי ללא עזיבת התצוגה הנוכחית. הן נסגרות בלחיצה חיצונית.",
            ["Flyouts.Confirm"] = "חלונית אישור",
            ["Flyouts.Delete"] = "מחק פריט",
            ["Flyouts.DeleteTitle"] = "למחוק פריט זה?",
            ["Flyouts.DeleteBody"] = "פעולה זו תסיר לצמיתות את הפריט ואת כל הנתונים המשויכים. לא ניתן לבטלה.",
            ["Flyouts.DeleteConfirm"] = "כן, מחק",
            ["Flyouts.Menu"] = "תפריט קופץ",
            ["Flyouts.Actions"] = "פעולות ▾",
            ["Flyouts.MenuItem.Edit"] = "עריכה",
            ["Flyouts.MenuItem.Duplicate"] = "שכפול",
            ["Flyouts.MenuItem.Archive"] = "ארכיון",
            ["Flyouts.MenuItem.Delete"] = "מחיקה",

            // Context Menus
            ["ContextMenus"] = "תפריטי הקשר",
            ["ContextMenus.Subtitle"] = "משטחי לחיצה ימנית — פעולות עריכה, תפריטי משנה וקיצורי מקלדת.",
            ["ContextMenus.Cut"] = "גזור",
            ["ContextMenus.Copy"] = "העתק",
            ["ContextMenus.Paste"] = "הדבק",
            ["ContextMenus.SelectAll"] = "בחר הכל",
            ["ContextMenus.EditArea"] = "אזור עריכה",
            ["ContextMenus.EditHint"] = "לחצו ימני לפעולות לוח",
            ["ContextMenus.ViewDetails"] = "הצג פרטים",
            ["ContextMenus.Share"] = "שתף",
            ["ContextMenus.ShareEmail"] = "דוא״ל",
            ["ContextMenus.ShareLink"] = "העתק קישור",
            ["ContextMenus.ShareSlack"] = "Slack",
            ["ContextMenus.Rename"] = "שנה שם",
            ["ContextMenus.MoveTo"] = "העבר אל",
            ["ContextMenus.Folder.Archive"] = "ארכיון",
            ["ContextMenus.Folder.Projects"] = "פרויקטים",
            ["ContextMenus.Folder.Starred"] = "מסומנים בכוכב",
            ["ContextMenus.Delete"] = "מחיקה",
            ["ContextMenus.FileCard"] = "כרטיס קובץ",
            ["ContextMenus.FileHint"] = "לחצו ימני לפעולות קובץ ותפריטי משנה",

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

            // Mermaid Diagrams
            ["MermaidDiagrams"] = "תרשימי Mermaid",
            ["MermaidDiagrams.Subtitle"] = "תרשימי זרימה, רצפים, מכונות מצב, תרשימי מחלקות, צירי זמן ותרשימי רבעים מתחביר Mermaid סטנדרטי באמצעות StrataMarkdown.",
            ["MermaidDiagrams.Footer"] = "השתמשו בבלוקי ```mermaid עם graph/flowchart, sequenceDiagram, stateDiagram-v2, classDiagram, timeline או quadrantChart. תמיכה גם ב-pie ו-xychart-beta.",

            // Charts
            ["Chart.Revenue"] = "מגמת הכנסות",
            ["Chart.Revenue.Subtitle"] = "הכנסות חודשיות עם השוואה לתחזית.",
            ["Chart.Budget"] = "תקציב מחלקות",
            ["Chart.Budget.Subtitle"] = "הקצאת תקציב שנתית לפי מחלקה.",
            ["Chart.Distribution"] = "חלוקת פרויקטים",
            ["Chart.Distribution.Subtitle"] = "הקצאת משאבים בין קטגוריות פרויקטים.",
            ["Chart.Clients"] = "פלחי לקוחות",
            ["Chart.Clients.Subtitle"] = "התפלגות לקוחות לפי סוג פלח.",

            // Chat Experience
            ["Chat.User1"] = "בדוק את תקלה IR-4471 והמלץ על תוכנית מיטיגציה.",
            ["Chat.Assistant1"] = "פרצי הקצאות זיכרון במסלול החם של הסריאליזר גרמו להשהיות GC ממושכות, שהובילו לירידה של 340 מ״ש בזמן התגובה באחוזון ה-95 בחלונות התנועה השיאית.",
            ["Chat.Editing"] = "אני משווה כעת מול קווי בסיס קודמים כדי לזהות רגרסיות שנגרמו מתיקון זה.",
            ["Chat.Markdown"] = "# תיקון מוצע\nהשתמשו בפריסה הדרגתית והגבילו את גודל המטען עבור נתיבי סריאליזר חמים.\n\n```csharp\npublic static bool ShouldRollback(double p95Ms, double gcPauseMs)\n{\n    if (p95Ms > 250 || gcPauseMs > 80)\n        return true;\n\n    return false;\n}\n```\n\n- שלב ראשון: פריסה ל-10%\n- שלב שני: פריסה ל-50%, שלב שלישי: פריסה ל-100%\n- חזרה לאחור מיידית בחריגת סף",
            ["Chat.InlineCode"] = "הקונפיגורציה נמצאת ב-`appsettings.json` תחת המקטע `Serializer`. ראו את הקובץ `HotPathSerializer.cs` לזיהוי מוקד ההקצאות, וודאו ש-`MaxBatchBytes` מוגדר לפני הפריסה.",
            ["Chat.ToolTitle"] = "הגדלת מאגר Workers והגבלת גודל מטען",
            ["Chat.ToolDesc"] = "הגדרת batch_max_bytes = 262144 מפחיתה את הלחץ על הסריאליזר.",
            ["Chat.Streaming"] = "בנוסף, הייתי ממליץ להגדיר התראה אוטומטית על מדד זמן השהיית ה-GC\u2026",
            ["Chat.System"] = "מדיניות מרחב העבודה נטענה. ציטוטים מבוססים הופעלו.",
            ["Chat.TypingLabel1"] = "מאמת התנהגות עם בדיקות ממוקדות כדי שהממצאים יהיו מבוססי ראיות",
            ["Chat.TypingLabel2"] = "מייצר תשובה\u2026",
            ["Chat.TypingPaused"] = "מושהה",
            ["Chat.Placeholder1"] = "בקשו שינויים נוספים",
            ["Chat.Placeholder2"] = "מה תרצו לדעת?",
            ["Chat.SuggestionA"] = "סכם",
            ["Chat.SuggestionB"] = "סיבת שורש",
            ["Chat.SuggestionC"] = "פעולות נדרשות",
            ["Chat.ShellHeader"] = "עוזר תקלות",
            ["Chat.ShellSubtitle"] = "סביבת עבודה / ייצור",
            ["Chat.Presence"] = "מחובר",
            ["Chat.ShellUser"] = "מה גרם לעלייה החדה בזמן התגובה?",
            ["Chat.ShellAssistant"] = "השהיות GC ממושכות עקב פרצי הקצאות זיכרון במסלול החם של הסריאליזר בשעות השיא.",
            ["Chat.ShellTyping"] = "בודק יומני פריסה\u2026",
            ["Chat.FollowUp"] = "שאלת המשך\u2026",
            ["Chat.MixedRtlLead"] = "שלום אני adir. זה משפט בדיקה ראשון כדי לוודא שהיישור פועל נכון גם כשיש מילים באנגלית באמצע. עכשיו נוסיף עוד משפט בעברית עם timestamp 09:57 ו-reference לקובץ docs/guide.md.",
            ["Chat.MixedLtrLead"] = "hello I am אדיר. This is the first verification sentence to confirm alignment stays left even when Hebrew appears inline. Next sentence adds more English context with incident IR-4471 and deployment notes.",

            // Chat Performance
            ["ChatPerf.Title"] = "מעבדת ביצועי צ׳אט",
            ["ChatPerf.Subtitle"] = "בדיקת עומס אוטומטית לגלילה וסטרימינג בצ׳אט עם יציבות זמני פריים.",
            ["ChatPerf.Presence"] = "מעבדת ביצועים",
            ["ChatPerf.ShellTitle"] = "רתמת עומס לטרנסקריפט",
            ["ChatPerf.ShellSubtitle"] = "קו בסיס מול אופטימיזציה · גלילה אוטומטית + סטרימינג",
            ["ChatPerf.Seed"] = "טעינת טרנסקריפט",
            ["ChatPerf.Run"] = "הרצת בנצ׳מרק",
            ["ChatPerf.Stop"] = "עצור",
            ["ChatPerf.ScenarioTitle"] = "תרחיש",
            ["ChatPerf.Scenario"] = "1,200 הודעות צ׳אט + 10 שניות גלילה אוטומטית + מטען Markdown גדול בסטרימינג. נמדדים FPS, זמן פריים p95, פריים גרוע ביותר ושיעור פריימים איטיים.",
            ["ChatPerf.ResultsTitle"] = "תוצאות",
            ["ChatPerf.StatusLabel"] = "סטטוס",
            ["ChatPerf.StatusIdle"] = "ממתין",
            ["ChatPerf.BaselineLabel"] = "קו בסיס (Legacy)",
            ["ChatPerf.OptimizedLabel"] = "אופטימיזציה",
            ["ChatPerf.UpliftLabel"] = "שיפור נמדד",
            ["ChatPerf.NoResults"] = "אין תוצאות עדיין.",
            ["ChatPerf.StatusSeeded"] = "הטרנסקריפט נטען עם הרבה הודעות. הריצו בנצ׳מרק להשוואת קו בסיס מול אופטימיזציה.",
            ["ChatPerf.StatusRunningBaseline"] = "מריץ פרופיל קו בסיס…",
            ["ChatPerf.StatusRunningOptimized"] = "מריץ פרופיל אופטימיזציה…",
            ["ChatPerf.StatusCompleted"] = "הבנצ׳מרק הושלם. המצב הממוטב שומר על FPS גבוה יותר וזמני פריים נמוכים יותר תחת סטרימינג + גלילה.",
            ["ChatPerf.StatusCancelled"] = "הבנצ׳מרק בוטל.",
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
