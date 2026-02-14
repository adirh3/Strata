using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StrataDemo;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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

    public ObservableCollection<string> NavItems { get; } = new()
    {
        "Dashboard",
        "Forms",
        "Data Grid",
        "Components",
        "Settings"
    };

    public ObservableCollection<string> ComboItems { get; } = new()
    {
        "Option Alpha",
        "Option Bravo",
        "Option Charlie",
        "Option Delta"
    };

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public record SampleRow(string Invoice, string Client, string Description, decimal Amount, string Status);
