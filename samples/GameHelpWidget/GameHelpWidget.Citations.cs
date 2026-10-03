using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

public sealed partial class GameHelpWidget
{
    // A single native paragraph owns wrapping; each inline link retains normal intent authority.
    private static IEnumerable<WidgetElement> CitedText(string text, GameHelpAnswer? answer, string field, string id, string style)
    {
        var citations = answer?.Citations.Where(citation => citation.Field == field).ToArray() ?? [];
        if (citations.Length == 0)
        {
            foreach (var (chunk, index) in Split(text).Select((chunk, index) => (chunk, index)))
                yield return UI.Text(chunk, id + "." + index).Classes(style);
            yield break;
        }
        var start = 0; var segment = 0;
        var spans = new List<WidgetElement>();
        foreach (var group in citations.GroupBy(citation => DisplayEnd(text, citation.End)).OrderBy(group => group.Key))
        {
            var chunks = Split(text[start..group.Key]);
            for (var i = 0; i < chunks.Length; ++i)
                spans.Add(UI.Text(chunks[i], id + ".span." + segment + "." + i).Classes(style));
            var groupId = id + ".citation." + segment++;
            var firstSource = true;
            foreach (var number in group.Select(citation => citation.SourceNumber).Distinct().Order())
            {
                if (!firstSource) spans.Add(UI.Text(" ", groupId + ".separator." + number));
                firstSource = false;
                var source = answer!.Sources[number - 1];
                var citation = group.First(item => item.SourceNumber == number);
                var supported = text[citation.Start..citation.End];
                var linkId = groupId + ".source." + number;
                const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
                var label = new string(number.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => digits[digit - '0']).ToArray());
                spans.Add(UI.InlineLink(label, linkId, Source(source, linkId).Intent!,
                    $"Source {number}: {source.Title}. Supports: {supported[..Math.Min(supported.Length, 240)]}"));
            }
            start = group.Key;
        }
        if (start < text.Length)
            foreach (var (chunk, index) in Split(text[start..]).Select((chunk, index) => (chunk, index)))
                spans.Add(UI.Text(chunk, id + ".tail." + index).Classes(style));
        yield return UI.RichText(id + ".paragraph", spans.ToArray()).Classes(style);
    }
    private static int DisplayEnd(string text, int end)
    {
        while (end < text.Length && ".,;:!?)]".Contains(text[end])) ++end;
        return end;
    }
}
