using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace StrataTheme.Controls;

/// <summary>
/// Inline source-citation chip. Displays a small numbered badge (e.g. "[1]")
/// that reveals source details on hover or click. Used to ground AI-generated
/// text with provenance links.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataTrace Index="1" Title="Wikipedia" Origin="https://en.wikipedia.org"
///                         Snippet="Relevant excerpt..." Relevance="0.92" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Badge (Border).</para>
/// </remarks>
public class StrataTrace : TemplatedControl
{
    private Border? _badge;
    private string _indexText = "[1]";

    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<StrataTrace, int>(nameof(Index), 1);

    public static readonly DirectProperty<StrataTrace, string> IndexTextProperty =
        AvaloniaProperty.RegisterDirect<StrataTrace, string>(nameof(IndexText), o => o.IndexText);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrataTrace, string>(nameof(Title), "Source");

    public static readonly StyledProperty<string?> SnippetProperty =
        AvaloniaProperty.Register<StrataTrace, string?>(nameof(Snippet));

    public static readonly StyledProperty<string?> OriginProperty =
        AvaloniaProperty.Register<StrataTrace, string?>(nameof(Origin));

    public static readonly StyledProperty<double> RelevanceProperty =
        AvaloniaProperty.Register<StrataTrace, double>(nameof(Relevance), 0);

    public static readonly StyledProperty<bool> IsRevealedProperty =
        AvaloniaProperty.Register<StrataTrace, bool>(nameof(IsRevealed));

    static StrataTrace()
    {
        IndexProperty.Changed.AddClassHandler<StrataTrace>((t, _) =>
        {
            t._indexText = $"[{t.Index}]";
            t.RaisePropertyChanged(IndexTextProperty, default!, t._indexText);
        });
    }

    public int Index { get => GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
    public string IndexText => _indexText;
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Snippet { get => GetValue(SnippetProperty); set => SetValue(SnippetProperty, value); }
    public string? Origin { get => GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public double Relevance { get => GetValue(RelevanceProperty); set => SetValue(RelevanceProperty, value); }
    public bool IsRevealed { get => GetValue(IsRevealedProperty); set => SetValue(IsRevealedProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_badge is not null)
        {
            _badge.PointerEntered -= OnBadgePointerEntered;
            _badge.PointerExited -= OnBadgePointerExited;
            _badge.PointerPressed -= OnBadgePointerPressed;
        }

        base.OnApplyTemplate(e);

        _badge = e.NameScope.Find<Border>("PART_Badge");
        if (_badge is not null)
        {
            _badge.PointerEntered += OnBadgePointerEntered;
            _badge.PointerExited += OnBadgePointerExited;
            _badge.PointerPressed += OnBadgePointerPressed;
        }
    }

    private void OnBadgePointerEntered(object? sender, PointerEventArgs e) => IsRevealed = true;
    private void OnBadgePointerExited(object? sender, PointerEventArgs e) => IsRevealed = false;
    private void OnBadgePointerPressed(object? sender, PointerPressedEventArgs e) => IsRevealed = !IsRevealed;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            IsRevealed = !IsRevealed;
        }
    }
}
