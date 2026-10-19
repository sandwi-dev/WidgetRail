using System.Globalization;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Controller edits use Unicode text elements; SDK length limits remain UTF-16 units.</summary>
internal static class TextEntryEditing
{
    internal readonly record struct Edit(string Value, int Caret);

    internal static int Move(string value, int start, int length, int direction)
    {
        var boundaries = Boundaries(value);
        start = Math.Clamp(start, 0, value.Length);
        var end = start + Math.Clamp(length, 0, value.Length - start);
        if (direction == 0) return Floor(boundaries, start);
        if (end > start) return direction < 0 ? Floor(boundaries, start) : Ceiling(boundaries, end);
        return direction < 0
            ? boundaries.LastOrDefault(position => position < start)
            : boundaries.FirstOrDefault(position => position > start, value.Length);
    }

    internal static int MoveWord(string value, int start, int length, int direction, IReadOnlyList<int> words)
    {
        var boundaries = Boundaries(value);
        var (first, end) = Selection(value, start, length);
        if (length > 0) return direction < 0 ? first : end;
        var target = direction < 0 ? words.LastOrDefault(position => position < first)
            : words.FirstOrDefault(position => position > end, value.Length);
        return direction < 0 ? Floor(boundaries, Math.Clamp(target, 0, value.Length)) : Ceiling(boundaries, Math.Clamp(target, 0, value.Length));
    }

    internal static Edit BackspaceWord(string value, int start, int length, IReadOnlyList<int> words)
    {
        if (length > 0) return Backspace(value, start, length);
        var end = Ceiling(Boundaries(value), Math.Clamp(start, 0, value.Length));
        var first = MoveWord(value, end, 0, -1, words);
        return new(value.Remove(first, end - first), first);
    }

    internal static Edit? Insert(string value, int start, int length, string inserted, int maximumLength)
    {
        var (first, end) = Selection(value, start, length);
        if ((long)value.Length - (end - first) + inserted.Length > maximumLength) return null;
        return new(value.Remove(first, end - first).Insert(first, inserted), first + inserted.Length);
    }

    internal static Edit Backspace(string value, int start, int length)
    {
        var (first, end) = Selection(value, start, length);
        if (length <= 0)
        {
            var boundaries = Boundaries(value);
            end = Ceiling(boundaries, Math.Clamp(start, 0, value.Length));
            first = boundaries.LastOrDefault(position => position < end);
        }
        return new(value.Remove(first, end - first), first);
    }

    internal static Edit Paste(string value, int start, int length, string inserted, int maximumLength)
    {
        var (first, end) = Selection(value, start, length);
        var available = Math.Max(0, maximumLength - value.Length + end - first);
        var count = inserted.Length <= available ? inserted.Length : Floor(Boundaries(inserted), available);
        if (count == 0) return new(value, first);
        return new(value.Remove(first, end - first).Insert(first, inserted[..count]), first + count);
    }

    private static (int Start, int End) Selection(string value, int start, int length)
    {
        var boundaries = Boundaries(value);
        start = Math.Clamp(start, 0, value.Length);
        var end = start + Math.Clamp(length, 0, value.Length - start);
        var first = Floor(boundaries, start);
        return (first, end == start ? first : Ceiling(boundaries, end));
    }
    private static int[] Boundaries(string value) => [.. StringInfo.ParseCombiningCharacters(value), value.Length];
    private static int Floor(int[] boundaries, int position) => boundaries.Last(value => value <= position);
    private static int Ceiling(int[] boundaries, int position) => boundaries.First(value => value >= position);
}
