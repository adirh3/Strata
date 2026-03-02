using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

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
    /// Falls back to <c>ToString()</c> for unknown types.
    /// </summary>
    public static string ExtractText(object? content)
    {
        if (content is null)
            return string.Empty;

        if (content is string text)
            return text;

        if (content is TextBlock textBlock)
            return textBlock.Text ?? string.Empty;

        if (content is StrataMarkdown markdown)
            return markdown.Markdown ?? string.Empty;

        if (content is StrataAiToolCall toolCall)
            return $"{toolCall.ToolName} | {toolCall.StatusText} | {toolCall.MoreInfo}";

        if (content is StrataAiSkill skill)
            return $"{skill.SkillName}\n{skill.Description}\n{skill.DetailMarkdown}";

        if (content is StrataAiAgent agent)
            return $"{agent.AgentName}\n{agent.Description}\n{agent.DetailMarkdown}";

        if (content is ContentControl contentControl)
            return ExtractText(contentControl.Content);

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

        return content.ToString() ?? string.Empty;
    }
}
