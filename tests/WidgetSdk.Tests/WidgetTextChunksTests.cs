using WidgetRail.WidgetSdk;

internal static class WidgetTextChunksTests
{
    internal static Task Run()
    {
        foreach (var whitespace in new[] { false, true })
        foreach (var budget in Enumerable.Range(2, 30))
        foreach (var text in new[] { "", "abc", "a\r\nb\r\n", "😀😀x😀", "words and\r\nmore words 😀",
                     new string('x', 4095) + "😀\r\n" + new string('y', 4097) })
        {
            var chunks = WidgetTextChunks.Split(text, budget, whitespace).ToArray();
            Check(string.Concat(chunks) == text, "Chunking changed the text.");
            Check(chunks.All(chunk => chunk.Length > 0 && chunk.Length <= budget), "Invalid chunk length.");
            for (var i = 0; i + 1 < chunks.Length; ++i)
                Check(!(char.IsHighSurrogate(chunks[i][^1]) && char.IsLowSurrogate(chunks[i + 1][0])) &&
                    !(chunks[i][^1] == '\r' && chunks[i + 1][0] == '\n'), "Split a character pair.");
        }
        Check(WidgetTextChunks.NextLength("😀", 0, 1) == 0, "Insufficient span budget split an emoji.");
        Check(WidgetTextChunks.NextLength("\r\n", 0, 1) == 0, "Insufficient span budget split CRLF.");
        Check(WidgetTextChunks.NextLength("a b c", 0, 4, true) == 4, "Whitespace preference changed.");
        Check(WidgetTextChunks.NextLength("a\nbcdef", 0, 6) == 2, "Newline preference changed.");
        try { _ = WidgetTextChunks.Split("x", 1); throw new InvalidOperationException("Invalid budget accepted."); }
        catch (ArgumentOutOfRangeException) { }
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
