using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>
/// Arranges the composer's editor and three-slot toolbar without moving or replacing controls.
/// Expansion interpolates from a shared input/action row to an editor above the toolbar.
/// </summary>
internal sealed class ComposerLayoutPanel : Panel
{
    public static readonly StyledProperty<double> ExpansionProperty =
        AvaloniaProperty.Register<ComposerLayoutPanel, double>(
            nameof(Expansion), 1, validate: value => value is >= 0 and <= 1);

    static ComposerLayoutPanel() => AffectsMeasure<ComposerLayoutPanel>(ExpansionProperty);

    public double Expansion
    {
        get => GetValue(ExpansionProperty);
        set => SetValue(ExpansionProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (editor, toolbar, leading, _, primary) = GetParts();
        toolbar.Measure(availableSize);
        var insets = CompactInsets(toolbar, leading, primary);
        var reserved = (insets.Left + insets.Right) * (1 - Expansion);
        editor.Measure(new Size(Math.Max(0, availableSize.Width - reserved), availableSize.Height));

        var collapsedHeight = Math.Max(editor.DesiredSize.Height, toolbar.DesiredSize.Height);
        var expandedHeight = editor.DesiredSize.Height + toolbar.DesiredSize.Height;
        return new Size(
            Math.Max(editor.DesiredSize.Width + reserved, toolbar.DesiredSize.Width),
            collapsedHeight + (expandedHeight - collapsedHeight) * Expansion);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (editor, toolbar, leading, tools, primary) = GetParts();
        var insets = CompactInsets(toolbar, leading, primary);
        var remaining = 1 - Expansion;
        var editorY = Math.Max(0, (toolbar.DesiredSize.Height - editor.DesiredSize.Height) / 2) * remaining;
        editor.Arrange(new Rect(
            insets.Left * remaining, editorY,
            Math.Max(0, finalSize.Width - (insets.Left + insets.Right) * remaining), editor.DesiredSize.Height));
        toolbar.Arrange(new Rect(0, Math.Max(0, finalSize.Height - toolbar.DesiredSize.Height),
            finalSize.Width, toolbar.DesiredSize.Height));

        // Reveal secondary tools only after the text has moved clear of their row.
        var reveal = Math.Clamp((Expansion - 0.4) / 0.6, 0, 1);
        tools.Opacity = reveal;
        tools.IsHitTestVisible = reveal > 0.5;
        tools.IsEnabled = reveal > 0.5;
        return finalSize;
    }

    private Thickness CompactInsets(Grid toolbar, Control leading, Control primary)
    {
        var left = leading.IsVisible ? leading.DesiredSize.Width + toolbar.Margin.Left : 0;
        var right = primary.DesiredSize.Width + toolbar.Margin.Right;
        return FlowDirection == FlowDirection.RightToLeft
            ? new Thickness(right, 0, left, 0)
            : new Thickness(left, 0, right, 0);
    }

    private (Control Editor, Grid Toolbar, Control Leading, Control Tools, Control Primary) GetParts()
    {
        if (Children.Count != 2 || Children[1] is not Grid { Children.Count: 3 } toolbar)
            throw new InvalidOperationException("Composer layout requires an editor and a leading/tools/primary toolbar.");
        return (Children[0], toolbar, toolbar.Children[0], toolbar.Children[1], toolbar.Children[2]);
    }
}
