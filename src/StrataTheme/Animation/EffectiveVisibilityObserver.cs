using Avalonia;
using Avalonia.VisualTree;

namespace StrataTheme.Animation;

internal sealed class EffectiveVisibilityObserver : IDisposable
{
    private readonly Visual _target;
    private readonly Action _visibilityChanged;
    private readonly List<Visual> _sources = new();

    public EffectiveVisibilityObserver(Visual target, Action visibilityChanged)
    {
        _target = target;
        _visibilityChanged = visibilityChanged;
    }

    public void Subscribe()
    {
        Unsubscribe();
        _sources.Add(_target);
        _sources.AddRange(_target.GetVisualAncestors());

        foreach (var source in _sources)
            source.PropertyChanged += OnPropertyChanged;
    }

    public void Unsubscribe()
    {
        foreach (var source in _sources)
            source.PropertyChanged -= OnPropertyChanged;

        _sources.Clear();
    }

    public void Dispose() => Unsubscribe();

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty)
            _visibilityChanged();
    }
}
