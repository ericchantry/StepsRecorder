using System.Globalization;
using System.Text;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public enum TimelineCategory
{
    Keyboard,
    Mouse
}

public sealed record TimelineEntry(
    int Segment,
    double OffsetSeconds,
    TimelineCategory Category,
    string Label,
    string? Detail,
    string? RawKeys = null)
{
    public string TimeLabel => TimeFormat.Format(OffsetSeconds);

    public bool IsKeyboard => Category == TimelineCategory.Keyboard;

    public bool IsMouse => Category == TimelineCategory.Mouse;
}

/// <summary>
/// Turns raw input events into readable timeline rows: consecutive keystrokes become one
/// "Typed 'hello'" row and consecutive scroll notches become one "Scrolled down" row.
/// </summary>
public static class InputTimelineBuilder
{
    public static readonly TimeSpan TypingGap = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ScrollGap = TimeSpan.FromSeconds(1.5);
    private const int WheelDelta = 120;

    public static IReadOnlyList<TimelineEntry> Build(IEnumerable<InputEvent> events)
    {
        var ordered = events.OrderBy(e => e.Segment).ThenBy(e => e.OffsetSeconds).ToList();
        var entries = new List<TimelineEntry>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            if (current.Kind == InputEventKind.KeyText)
            {
                var end = i;
                while (end + 1 < ordered.Count && ContinuesTyping(ordered[end], ordered[end + 1], current.Masked))
                {
                    end++;
                }

                entries.Add(BuildTypingEntry(ordered, i, end));
                i = end;
                continue;
            }

            if (current.Kind is InputEventKind.Scroll or InputEventKind.HorizontalScroll)
            {
                var end = i;
                while (end + 1 < ordered.Count && ContinuesScroll(ordered[end], ordered[end + 1]))
                {
                    end++;
                }

                entries.Add(BuildScrollEntry(ordered, i, end));
                i = end;
                continue;
            }

            entries.Add(BuildSingleEntry(current));
        }

        return entries;
    }

    private static bool ContinuesTyping(InputEvent previous, InputEvent next, bool masked)
    {
        if (next.Segment != previous.Segment
            || next.OffsetSeconds - previous.OffsetSeconds > TypingGap.TotalSeconds
            || !SameWindow(previous, next))
        {
            return false;
        }

        return next.Kind == InputEventKind.KeyText && next.Masked == masked
            || next.Kind == InputEventKind.Key && next.Key == "Backspace";
    }

    private static bool ContinuesScroll(InputEvent previous, InputEvent next)
    {
        return next.Segment == previous.Segment
            && next.Kind == previous.Kind
            && Math.Sign(next.Delta ?? 0) == Math.Sign(previous.Delta ?? 0)
            && next.OffsetSeconds - previous.OffsetSeconds <= ScrollGap.TotalSeconds
            && SameWindow(previous, next);
    }

    private static bool SameWindow(InputEvent a, InputEvent b)
    {
        return string.Equals(a.WindowTitle, b.WindowTitle, StringComparison.Ordinal)
            && string.Equals(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static TimelineEntry BuildTypingEntry(List<InputEvent> events, int start, int end)
    {
        var first = events[start];
        var buffer = new TypedTextBuffer();
        var raw = new StringBuilder();
        var keyCount = 0;
        var hasBackspace = false;
        var hasUnknownText = false;

        for (var i = start; i <= end; i++)
        {
            var e = events[i];
            keyCount++;
            if (e.Kind == InputEventKind.Key)
            {
                hasBackspace = true;
                buffer.Backspace();
                raw.Append('⌫');
                continue;
            }

            if (e.Text is null)
            {
                hasUnknownText = true;
                continue;
            }

            buffer.Append(e.Text);
            raw.Append(e.Text);
        }

        string label;
        string? rawKeys = null;
        if (first.Masked)
        {
            label = $"Typed hidden text ({Plural(keyCount, "key")})";
        }
        else if (hasUnknownText)
        {
            label = $"Typed {Plural(keyCount, "key")} (text not recorded)";
        }
        else
        {
            label = $"Typed {QuoteText(buffer.Text)}";
            rawKeys = hasBackspace ? raw.ToString() : null;
        }

        return new TimelineEntry(first.Segment, first.OffsetSeconds, TimelineCategory.Keyboard, label, DescribeWindow(first), rawKeys);
    }

    private static TimelineEntry BuildScrollEntry(List<InputEvent> events, int start, int end)
    {
        var first = events[start];
        var totalDelta = 0;
        for (var i = start; i <= end; i++)
        {
            totalDelta += events[i].Delta ?? 0;
        }

        var notches = Math.Max(1, (int)Math.Round(Math.Abs(totalDelta) / (double)WheelDelta));
        var direction = first.Kind == InputEventKind.HorizontalScroll
            ? (totalDelta >= 0 ? "right" : "left")
            : (totalDelta >= 0 ? "up" : "down");
        var label = $"Scrolled {direction} {Plural(notches, "notch", "notches")}";
        return new TimelineEntry(first.Segment, first.OffsetSeconds, TimelineCategory.Mouse, label, DescribeWindow(first));
    }

    private static TimelineEntry BuildSingleEntry(InputEvent e)
    {
        var (category, label) = e.Kind switch
        {
            InputEventKind.Shortcut => (TimelineCategory.Keyboard, $"Pressed {e.Key}"),
            InputEventKind.Key => (TimelineCategory.Keyboard, $"Pressed {e.Key}"),
            InputEventKind.Click => (TimelineCategory.Mouse, $"Left click{At(e)}"),
            InputEventKind.RightClick => (TimelineCategory.Mouse, $"Right click{At(e)}"),
            InputEventKind.DoubleClick => (TimelineCategory.Mouse, $"Double-click{At(e)}"),
            InputEventKind.MiddleClick => (TimelineCategory.Mouse, $"Middle click{At(e)}"),
            InputEventKind.Drag => (TimelineCategory.Mouse, $"Dragged from {Point(e.X, e.Y)} to {Point(e.EndX, e.EndY)}"),
            _ => (TimelineCategory.Keyboard, e.Kind.ToString())
        };

        return new TimelineEntry(e.Segment, e.OffsetSeconds, category, label, DescribeWindow(e));
    }

    private static string At(InputEvent e)
    {
        return e.X.HasValue && e.Y.HasValue ? $" at {Point(e.X, e.Y)}" : string.Empty;
    }

    private static string Point(int? x, int? y)
    {
        return string.Create(CultureInfo.InvariantCulture, $"({x ?? 0}, {y ?? 0})");
    }

    private static string? DescribeWindow(InputEvent e)
    {
        var title = string.IsNullOrWhiteSpace(e.WindowTitle) ? null : e.WindowTitle.Trim();
        var process = string.IsNullOrWhiteSpace(e.ProcessName) ? null : e.ProcessName.Trim();
        if (title is null)
        {
            return process;
        }

        return process is null ? title : $"{title} ({process})";
    }

    private static string QuoteText(string text)
    {
        if (text.Length == 0)
        {
            return "nothing (all characters deleted)";
        }

        var visible = text.Replace("\r", string.Empty).Replace("\n", "⏎").Replace("\t", "⇥");
        return $"'{TypedTextBuffer.Truncate(visible, 120)}'";
    }

    private static string Plural(int count, string singular, string? plural = null)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";
    }
}
