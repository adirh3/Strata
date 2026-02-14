using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Interactivity;
using System.Collections.Generic;

namespace StrataDemo;

public partial class MainWindow : Window
{
    private readonly List<Control> _pages = new();

    public MainWindow()
    {
        InitializeComponent();

        // Cache page references
        for (int i = 0; i <= 4; i++)
        {
            var page = this.FindControl<ScrollViewer>($"Page{i}");
            if (page is not null)
                _pages.Add(page);
        }

        // Wire sidebar navigation
        var navList = this.FindControl<ListBox>("NavList");
        if (navList is not null)
            navList.SelectionChanged += OnNavSelectionChanged;

        // Wire theme toggles (sidebar)
        WireToggle("ThemeToggle", OnThemeToggleChanged);
        WireToggle("DensityToggle", OnDensityToggleChanged);

        // Wire theme toggles (settings page)
        WireToggle("ThemeToggle2", OnThemeToggleChanged);
        WireToggle("DensityToggle2", OnDensityToggleChanged);

        // Show first page
        ShowPage(0);
    }

    private void WireToggle(string name, EventHandler<RoutedEventArgs> handler)
    {
        var toggle = this.FindControl<ToggleSwitch>(name);
        if (toggle is not null)
            toggle.IsCheckedChanged += handler;
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb)
            ShowPage(lb.SelectedIndex);
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].IsVisible = i == index;
    }

    private void OnThemeToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null) return;
        var toggle = sender as ToggleSwitch;
        Application.Current.RequestedThemeVariant =
            toggle?.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;

        // Keep all theme toggles in sync
        SyncToggle("ThemeToggle", toggle?.IsChecked == true);
        SyncToggle("ThemeToggle2", toggle?.IsChecked == true);
    }

    private void OnDensityToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null) return;

        var toggle = sender as ToggleSwitch;
        var isCompact = toggle?.IsChecked == true;

        var densityUri = isCompact
            ? new System.Uri("avares://StrataTheme/Tokens/Density.Compact.axaml")
            : new System.Uri("avares://StrataTheme/Tokens/Density.Comfortable.axaml");

        var app = Application.Current;
        var dict = (Avalonia.Controls.ResourceDictionary)AvaloniaXamlLoader.Load(densityUri);

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(dict);

        // Keep all density toggles in sync
        SyncToggle("DensityToggle", isCompact);
        SyncToggle("DensityToggle2", isCompact);
    }

    private void SyncToggle(string name, bool value)
    {
        var toggle = this.FindControl<ToggleSwitch>(name);
        if (toggle is not null && toggle.IsChecked != value)
            toggle.IsChecked = value;
    }
}
