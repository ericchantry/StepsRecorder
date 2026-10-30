using System.Text;

namespace OpenSteps.Core.Services;

/// <summary>
/// Rebuilds what was typed from key events: appends characters and applies Backspace.
/// Cursor movement is not tracked, so text edited with arrow keys is a best effort;
/// the raw key sequence is kept on the timeline.
/// </summary>
public sealed class TypedTextBuffer
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public int Length => _text.Length;

    public void Append(string? characters)
    {
        if (!string.IsNullOrEmpty(characters))
        {
            _text.Append(characters);
        }
    }

    public void Backspace()
    {
        if (_text.Length == 0)
        {
            return;
        }

        // Remove a whole surrogate pair rather than half of one.
        var remove = _text.Length >= 2 && char.IsLowSurrogate(_text[^1]) && char.IsHighSurrogate(_text[^2]) ? 2 : 1;
        _text.Length -= remove;
    }

    public void Clear()
    {
        _text.Clear();
    }

    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..Math.Max(0, maxLength - 1)].TrimEnd() + "…";
    }
}
