using System;
using System.Collections;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

/// <summary>
/// Extracts a plain-text representation from arbitrary chat message content.
/// Walks the visual/content tree recursively, handling known Strata control types.
/// Reusable for copy-to-clipboard, accessibility, or search indexing.
/// </summary>
public static class ChatContentExtractor
{
    /// <summary>
    /// Recursively extracts human-readable text from <paramref name="content"/>.
    /// Handles <see cref="string"/>, <see cref="TextBlock"/>, <see cref="StrataMarkdown"/>,
    /// <see cref="StrataAiToolCall"/>, <see cref="StrataAiSkill"/>, <see cref="StrataAiAgent"/>,
    /// <see cref="ContentControl"/>, <see cref="Decorator"/>, and <see cref="Panel"/>.
    /// Unknown controls are searched visually before being skipped.
    /// </summary>
    public static string ExtractText(object? content)
    {
        if (content is null)
            return string.Empty;

        if (content is string text)
            return text;

        if (content is TextBlock textBlock)
            return textBlock.Text ?? string.Empty;

        if (content is SelectableTextBlock selectableTextBlock)
            return selectableTextBlock.Text ?? string.Empty;

        if (content is StrataMarkdown markdown)
            return markdown.Markdown ?? string.Empty;

        if (content is StrataFileAttachment fileAttachment)
        {
            if (string.IsNullOrWhiteSpace(fileAttachment.FileSize))
                return fileAttachment.FileName;

            return $"{fileAttachment.FileName} ({fileAttachment.FileSize})";
        }

        if (content is StrataAiToolCall toolCall)
            return $"{toolCall.ToolName} | {toolCall.StatusText} | {toolCall.MoreInfo}";

        if (content is StrataAiSkill skill)
            return $"{skill.SkillName}\n{skill.Description}\n{skill.DetailMarkdown}";

        if (content is StrataAiAgent agent)
            return $"{agent.AgentName}\n{agent.Description}\n{agent.DetailMarkdown}";

        if (content is ContentControl contentControl)
        {
            var directText = ExtractText(contentControl.Content);
            if (!string.IsNullOrWhiteSpace(directText))
                return directText;

            return ExtractTextFromVisualChildren(contentControl);
        }

        if (content is Decorator decorator)
            return ExtractText(decorator.Child);

        if (content is Panel panel)
        {
            var children = panel.Children;
            if (children.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (var i = 0; i < children.Count; i++)
            {
                var line = ExtractText(children[i]);
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (sb.Length > 0)
                    sb.Append(Environment.NewLine);
                sb.Append(line);
            }
            return sb.ToString();
        }

        if (content is IEnumerable enumerable)
            return ExtractTextFromEnumerable(enumerable);

        if (content is Control control)
            return ExtractTextFromVisualChildren(control);

        var toString = content.GetType().GetMethod(nameof(ToString), Type.EmptyTypes);
        if (toString?.DeclaringType == typeof(object))
            return string.Empty;

        return content.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Walks the content tree looking for <see cref="SelectableTextBlock"/>s with active
    /// text selections. Returns the concatenated selected text, or <c>null</c> if nothing
    /// is selected anywhere in the tree.
    /// </summary>
    public static string? ExtractSelectedText(object? content)
    {
        if (content is null)
            return null;

        if (content is SelectableTextBlock stb)
            return HasSelection(stb) ? stb.SelectedText : null;

        if (content is StrataMarkdown markdown)
            return CollectSelectedTextFromVisual(markdown);

        if (content is ContentControl cc)
        {
            var directSelection = ExtractSelectedText(cc.Content);
            if (!string.IsNullOrEmpty(directSelection))
                return directSelection;

            return CollectSelectedTextFromVisual(cc);
        }

        if (content is Decorator dec)
            return ExtractSelectedText(dec.Child);

        if (content is Panel panel)
        {
            StringBuilder? sb = null;
            for (var i = 0; i < panel.Children.Count; i++)
            {
                var sel = ExtractSelectedText(panel.Children[i]);
                if (string.IsNullOrEmpty(sel)) continue;
                sb ??= new StringBuilder();
                if (sb.Length > 0) sb.Append(Environment.NewLine);
                sb.Append(sel);
            }
            return sb?.ToString();
        }

        if (content is Control control)
            return CollectSelectedTextFromVisual(control);

        return null;
    }

    private static string ExtractTextFromEnumerable(IEnumerable enumerable)
    {
        StringBuilder? sb = null;
        foreach (var item in enumerable)
        {
            var line = ExtractText(item);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            sb ??= new StringBuilder();
            if (sb.Length > 0)
                sb.Append(Environment.NewLine);
            sb.Append(line);
        }

        return sb?.ToString() ?? string.Empty;
    }

    private static string ExtractTextFromVisualChildren(Control root)
    {
        StringBuilder? sb = null;
        CollectVisualText(root, ref sb);
        return sb?.ToString() ?? string.Empty;
    }

    private static void CollectVisualText(Control control, ref StringBuilder? sb)
    {
        foreach (var child in control.GetVisualChildren())
        {
            if (child is not Control childControl)
                continue;

            var text = ExtractText(childControl);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            sb ??= new StringBuilder();
            if (sb.Length > 0)
                sb.Append(Environment.NewLine);
            sb.Append(text);
        }
    }

    private static string? CollectSelectedTextFromVisual(Control root)
    {
        StringBuilder? sb = null;
        CollectSelected(root, ref sb);
        return sb?.ToString();
    }

    private static void CollectSelected(Control control, ref StringBuilder? sb)
    {
        if (control is SelectableTextBlock stb && HasSelection(stb))
        {
            sb ??= new StringBuilder();
            if (sb.Length > 0) sb.Append(Environment.NewLine);
            sb.Append(stb.SelectedText);
            return;
        }

        var count = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(control);
        foreach (var child in count)
        {
            if (child is Control c)
                CollectSelected(c, ref sb);
        }
    }

    private static bool HasSelection(SelectableTextBlock stb) =>
        stb.SelectionStart != stb.SelectionEnd && !string.IsNullOrEmpty(stb.SelectedText);
}
