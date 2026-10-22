namespace WidgetRail.WidgetSdk;

/// <summary>
/// Lossless UTF-16 chunk boundaries for authored text budgets. This does not
/// budget a whole view or preserve grapheme clusters across separate controls.
/// </summary>
public static class WidgetTextChunks
{
    /// <summary>
    /// Length of the next chunk, preferring a newline and optionally whitespace.
    /// Never separates a surrogate pair or CRLF. Returns zero when the remaining
    /// budget cannot fit that pair; the caller must start a new text container.
    /// </summary>
    public static int NextLength(string text, int offset, int maximumLength, bool preferWhitespace = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset < 0 || offset > text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (maximumLength < 1) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        var length = Math.Min(maximumLength, text.Length - offset);
        if (length == 0 || length == text.Length - offset) return length;
        var boundary = text.LastIndexOf('\n', offset + length - 1, length);
        if (boundary < offset && preferWhitespace)
            for (var index = offset + length - 1; index >= offset; --index)
                if (char.IsWhiteSpace(text[index])) { boundary = index; break; }
        if (boundary >= offset) length = boundary - offset + 1;
        var end = offset + length;
        if ((char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) ||
            (text[end - 1] == '\r' && text[end] == '\n')) --length;
        return length;
    }

    /// <summary>
    /// Lazily splits text without changing or dropping characters. The budget
    /// must fit at least two UTF-16 code units so every iteration makes progress.
    /// </summary>
    public static IEnumerable<string> Split(string text, int maximumLength, bool preferWhitespace = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumLength < 2) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        return Enumerate();

        IEnumerable<string> Enumerate()
        {
            for (var offset = 0; offset < text.Length;)
            {
                var length = NextLength(text, offset, maximumLength, preferWhitespace);
                yield return text.Substring(offset, length);
                offset += length;
            }
        }
    }
}
