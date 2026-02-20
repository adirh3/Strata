using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using StrataDemo.Localization;
using StrataTheme.Controls;

namespace StrataDemo;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Localized string proxy for XAML bindings. New reference each language switch.</summary>
    private StringsProxy _strings = Localization.Strings.Instance.CreateProxy();
    public StringsProxy Strings
    {
        get => _strings;
        private set { _strings = value; OnPropertyChanged(); }
    }

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set { _isDarkTheme = value; OnPropertyChanged(); }
    }

    private bool _isCompactDensity;
    public bool IsCompactDensity
    {
        get => _isCompactDensity;
        set { _isCompactDensity = value; OnPropertyChanged(); }
    }

    private int _selectedLanguageIndex;
    public int SelectedLanguageIndex
    {
        get => _selectedLanguageIndex;
        set
        {
            if (_selectedLanguageIndex == value) return;
            _selectedLanguageIndex = value;
            OnPropertyChanged();
            ApplyLanguage(value);
        }
    }

    public ObservableCollection<string> Languages { get; } = new()
    {
        "English",
        "עברית (Hebrew)"
    };

    private void ApplyLanguage(int index)
    {
        var culture = index switch
        {
            1 => new CultureInfo("he-IL"),
            _ => new CultureInfo("en-US")
        };
        Localization.Strings.Instance.Culture = culture;
        Strings = Localization.Strings.Instance.CreateProxy();
    }

    private string _sampleText = "Sample input text";
    public string SampleText
    {
        get => _sampleText;
        set { _sampleText = value; OnPropertyChanged(); }
    }

    private double _sliderValue = 65;
    public double SliderValue
    {
        get => _sliderValue;
        set { _sliderValue = value; OnPropertyChanged(); }
    }

    private double _progressValue = 42;
    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private int _selectedNavIndex;
    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set { _selectedNavIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<SampleRow> GridData { get; } = new()
    {
        new SampleRow("INV-001", "Acme Corp", "Consulting Services", 12500.00m, "Paid"),
        new SampleRow("INV-002", "Globex Inc", "License Renewal", 4200.00m, "Pending"),
        new SampleRow("INV-003", "Initech", "Support Contract", 8750.00m, "Paid"),
        new SampleRow("INV-004", "Umbrella Co", "Cloud Hosting", 3100.00m, "Overdue"),
        new SampleRow("INV-005", "Stark Ind", "API Integration", 15000.00m, "Paid"),
        new SampleRow("INV-006", "Wayne Ent", "Security Audit", 9800.00m, "Pending"),
        new SampleRow("INV-007", "Cyberdyne", "Data Migration", 6400.00m, "Paid"),
        new SampleRow("INV-008", "Oscorp", "UI Redesign", 11200.00m, "Draft"),
        new SampleRow("INV-009", "LexCorp", "Infrastructure", 7300.00m, "Overdue"),
        new SampleRow("INV-010", "Wonka Ltd", "Maintenance", 2800.00m, "Paid"),
    };

    public ObservableCollection<string> ComboItems { get; } = new()
    {
        "Option Alpha",
        "Option Bravo",
        "Option Charlie",
        "Option Delta"
    };

    public ObservableCollection<string> AiModels { get; } = new()
    {
        "GPT-5.3-Codex",
        "GPT-4o",
        "GPT-4o-mini",
        "o3",
        "Claude Opus 4.6"
    };

    public ObservableCollection<string> AiQualityLevels { get; } = new()
    {
        "Low",
        "Medium",
        "High",
        "Extra High"
    };

    public ObservableCollection<StrataComposerChip> AiSkills { get; } = new()
    {
        new StrataComposerChip("Code Search", "⌕"),
        new StrataComposerChip("Web Browse", "⫶"),
    };

    public ObservableCollection<StrataComposerChip> AvailableAgents { get; } = new()
    {
        new StrataComposerChip("Code Reviewer", "◉"),
        new StrataComposerChip("Bug Triager", "◎"),
        new StrataComposerChip("Doc Writer", "◈"),
    };

    public ObservableCollection<StrataComposerChip> AvailableSkills { get; } = new()
    {
        new StrataComposerChip("Code Search", "⌕"),
        new StrataComposerChip("Web Browse", "⫶"),
        new StrataComposerChip("File Read", "📄"),
        new StrataComposerChip("Terminal", "▸"),
        new StrataComposerChip("Code Edit", "✎"),
    };

    public ObservableCollection<StrataComposerChip> LiveAiSkills { get; } = new();

    // ── Chart demo data ──

    public IList<string> ChartMonthLabels { get; } = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public IList<StrataChartSeries> RevenueSeries { get; } = new List<StrataChartSeries>
    {
        new() { Name = "Actual", Values = new double[] { 42, 49, 55, 58, 63, 68, 72, 76, 74, 79, 82, 84 } },
        new() { Name = "Forecast", Values = new double[] { 40, 45, 50, 56, 60, 65, 70, 75, 78, 81, 85, 90 } },
    };

    public IList<string> DeptLabels { get; } = new[] { "Eng", "Sales", "Mktg", "Ops", "HR", "Legal" };

    public IList<StrataChartSeries> DeptSeries { get; } = new List<StrataChartSeries>
    {
        new() { Name = "FY25", Values = new double[] { 180, 120, 85, 95, 60, 45 } },
        new() { Name = "FY24", Values = new double[] { 160, 105, 78, 88, 55, 42 } },
    };

    public IList<string> ProjectLabels { get; } = new[] { "Engineering", "Marketing", "Sales", "Operations" };

    public IList<StrataChartSeries> ProjectSeries { get; } = new List<StrataChartSeries>
    {
        new() { Name = "Allocation", Values = new double[] { 45, 22, 18, 15 } },
    };

    public IList<string> ClientLabels { get; } = new[] { "Enterprise", "SMB", "Startup", "Government" };

    public IList<StrataChartSeries> ClientSeries { get; } = new List<StrataChartSeries>
    {
        new() { Name = "Clients", Values = new double[] { 38, 27, 20, 15 } },
    };

    // ── Markdown DataGrid demo data ──

    public ObservableCollection<MarkdownRow> MarkdownGridData { get; } = new()
    {
        new MarkdownRow(
            "POST /api/incidents",
            "Creates a new incident record. Requires **admin** or **operator** role. Returns the created resource with `id` and `status` fields.",
            "*Stable*",
            "`Bearer` token with `incidents:write` scope"),
        new MarkdownRow(
            "GET /api/incidents/{id}",
            "Retrieves incident details including **root cause**, **timeline**, and linked *evidence* artifacts.",
            "*Stable*",
            "`Bearer` token with `incidents:read` scope"),
        new MarkdownRow(
            "PATCH /api/incidents/{id}",
            "Partially updates an incident. Supports `status`, `severity`, and `assignee` fields. Use **bulk update** endpoint for batch operations.",
            "**Beta**",
            "`Bearer` token with `incidents:write` scope"),
        new MarkdownRow(
            "POST /api/rollout/stage",
            "Advances rollout to the next stage. Validates **p95 < `250ms`** and **GC pause < `80ms`** gates before proceeding.",
            "**Beta**",
            "`Bearer` token with `rollout:execute` scope"),
        new MarkdownRow(
            "GET /api/metrics/summary",
            "Returns aggregated metrics: `p95_latency`, `error_rate`, `throughput`, and `gc_pause`. Supports *time range* and **granularity** parameters.",
            "*Stable*",
            "`Bearer` token with `metrics:read` scope"),
        new MarkdownRow(
            "DELETE /api/incidents/{id}",
            "Soft-deletes an incident. Requires **admin** role. Deleted records are retained for `90 days` before purge.",
            "*Deprecated*",
            "`Bearer` token with `incidents:admin` scope"),
    };

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public record SampleRow(string Invoice, string Client, string Description, decimal Amount, string Status);

public record MarkdownRow(string Endpoint, string Description, string Status, string Auth);
