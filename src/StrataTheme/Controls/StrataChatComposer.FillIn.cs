using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Fill-in prompts: a host puts a prompt with blanks in the composer (such as "Plan a trip to [where?]") and the
/// composer walks the user through them. The first blank is selected, so typing replaces it; Tab and Shift+Tab move
/// between the blanks left; and the send key moves to a blank the user hasn't been shown yet before it sends, so
/// Enter reads like a form's: next field, then submit. Blanks are found by their exact text each time, so editing
/// around them never confuses the guide, and it ends by itself once none is left. A host's own editor
/// (<see cref="IStrataComposerEditor"/>, such as a phone's native text field) is guided too, through
/// <see cref="IStrataComposerEditor.FocusSelect"/>; with <see cref="AdvancesFillInOnSend"/> the send button walks the
/// blanks as the send key does, for touch, where it is the only way to move between them.
/// </summary>
public partial class StrataChatComposer
{
    /// <summary>
    /// When true, the send button moves to a blank the user hasn't been shown yet before it sends, as the send key
    /// always does, and its arrow turns to say so (<c>:fill-in-next</c>). For touch composers, whose send button is
    /// the only way to move between blanks. False by default.
    /// </summary>
    public static readonly StyledProperty<bool> AdvancesFillInOnSendProperty =
        AvaloniaProperty.Register<StrataChatComposer, bool>(nameof(AdvancesFillInOnSend));

    private string[] _fillInBlanks = [];
    private readonly HashSet<string> _visitedBlanks = new(StringComparer.Ordinal);

    /// <inheritdoc cref="AdvancesFillInOnSendProperty"/>
    public bool AdvancesFillInOnSend
    {
        get => GetValue(AdvancesFillInOnSendProperty);
        set => SetValue(AdvancesFillInOnSendProperty, value);
    }

    /// <summary>
    /// Starts guiding the user through <paramref name="blanks"/> (their exact text in <see cref="PromptText"/>, which
    /// the host has just set): focuses the input and selects the first blank, or places the caret at the end when
    /// the prompt has none.
    /// </summary>
    public void BeginFillIn(IReadOnlyList<string> blanks)
    {
        _fillInBlanks = blanks.Where(static blank => !string.IsNullOrEmpty(blank)).Distinct(StringComparer.Ordinal).ToArray();
        _visitedBlanks.Clear();

        if (EditorContent is IStrataComposerEditor)
        {
            // The host's editor takes the prompt through its own binding; by Loaded it has it.
            Dispatcher.UIThread.Post(() =>
            {
                if (SelectBlank(forward: true, unvisitedOnly: true, from: 0))
                    return;

                (EditorContent as IStrataComposerEditor)?.FocusAtEnd();
                Sync();
            }, DispatcherPriority.Loaded);
            return;
        }

        if (EditorContent is not null || _input is null)
        {
            FocusInputAtEnd();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_input is null)
                return;

            // PromptText is the host's value; the templated input may not have caught up with the binding yet.
            var text = PromptText ?? string.Empty;
            if (!string.Equals(_input.Text, text, StringComparison.Ordinal))
                _input.Text = text;

            _input.Focus();
            if (!SelectBlank(forward: true, unvisitedOnly: true, from: 0))
            {
                _input.CaretIndex = text.Length;
                _input.SelectionStart = text.Length;
                _input.SelectionEnd = text.Length;
                Sync();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Tab, Shift+Tab and the send key while blanks are left; true when the key moved to a blank.</summary>
    private bool TryHandleFillInKey(KeyEventArgs e)
    {
        if (_fillInBlanks.Length == 0 || EditorContent is not null || _input is null)
            return false;

        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
            return SelectBlank(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), unvisitedOnly: false);

        // The send key visits each blank once; after that it sends, so a blank left as it is never traps the user.
        var isSendKey = SendWithEnter
            ? e.KeyModifiers == KeyModifiers.None
            : e.KeyModifiers == CommandModifier;
        return e.Key == Key.Enter && isSendKey && SelectBlank(forward: true, unvisitedOnly: true);
    }

    private bool SelectBlank(bool forward, bool unvisitedOnly, int? from = null)
    {
        var editor = EditorContent as IStrataComposerEditor;
        if (editor is null && (EditorContent is not null || _input is null))
            return false;

        var text = FillInText();
        var blanks = FindBlanks(text);
        if (blanks.Count == 0)
        {
            _fillInBlanks = [];
            return false;
        }

        if (unvisitedOnly)
            blanks.RemoveAll(blank => _visitedBlanks.Contains(text.Substring(blank.Start, blank.Length)));
        if (blanks.Count == 0)
            return false;

        int selectionStart, selectionEnd;
        if (editor is not null)
        {
            selectionStart = selectionEnd = Math.Clamp(editor.CaretIndex, 0, text.Length);
        }
        else
        {
            selectionStart = Math.Min(_input!.SelectionStart, _input.SelectionEnd);
            selectionEnd = Math.Max(_input.SelectionStart, _input.SelectionEnd);
        }

        var origin = from ?? (forward ? selectionEnd : selectionStart);
        var (start, length) = forward
            ? blanks.FirstOrDefault(blank => blank.Start >= origin, blanks[0])
            : blanks.LastOrDefault(blank => blank.Start + blank.Length <= origin, blanks[^1]);

        _visitedBlanks.Add(text.Substring(start, length));
        if (editor is not null)
        {
            editor.FocusSelect(start, length);
        }
        else
        {
            // The send button can hold focus when it asked for the blank.
            if (!_input!.IsFocused)
                _input.Focus();
            // The caret goes to the blank's end first, as a selection made by hand leaves it; typing then replaces the blank.
            _input.CaretIndex = start + length;
            _input.SelectionStart = start;
            _input.SelectionEnd = start + length;
        }

        Sync();
        return true;
    }

    /// <summary>The prompt as the user sees it: the templated input can be ahead of <see cref="PromptText"/>.</summary>
    private string FillInText() => (EditorContent is null ? _input?.Text ?? PromptText : PromptText) ?? string.Empty;

    /// <summary>Whether the send button would move to a blank instead of sending (<see cref="AdvancesFillInOnSend"/>).</summary>
    private bool AdvancesToBlank()
    {
        if (!AdvancesFillInOnSend || _fillInBlanks.Length == 0)
            return false;

        var text = FillInText();
        foreach (var blank in _fillInBlanks)
        {
            if (!_visitedBlanks.Contains(blank) && text.Contains(blank, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The send button's turn to the next blank; true when it moved there instead of sending.</summary>
    private bool TryAdvanceFillInOnSend() =>
        AdvancesFillInOnSend && SelectBlank(forward: true, unvisitedOnly: true);

    /// <summary>Where the blanks still are in <paramref name="text"/>, in order.</summary>
    private List<(int Start, int Length)> FindBlanks(string? text)
    {
        var found = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(text))
            return found;

        foreach (var blank in _fillInBlanks)
        {
            for (var index = text.IndexOf(blank, StringComparison.Ordinal);
                 index >= 0;
                 index = text.IndexOf(blank, index + blank.Length, StringComparison.Ordinal))
            {
                found.Add((index, blank.Length));
            }
        }

        found.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    /// <summary>A cleared prompt (sent, or emptied by the user) ends the guide.</summary>
    private void EndFillInWhenCleared(string? prompt)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            _fillInBlanks = [];
            _visitedBlanks.Clear();
        }
    }
}
