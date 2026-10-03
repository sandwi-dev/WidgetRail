using System.Buffers;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.GameHelp;

/// <summary>One provider association mapped into a decoded JSON field. Offsets here are UTF-16;
/// the Interactions API input offsets are UTF-8 bytes into the original JSON text.</summary>
public sealed record GameHelpCitation(string Field, int Start, int End, int SourceNumber);
internal sealed record GeminiCitation(string Title, string Url, int? Start, int? End);

internal static class GameHelpCitations
{
    internal const int MaximumAnnotations = 128;
    internal static (GameHelpSource[] Sources, GameHelpCitation[] Citations) Map(string json, IReadOnlyList<GeminiCitation> annotations)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var fields = new List<Field>();
        var reader = new Utf8JsonReader(bytes);
        reader.Read(); ReadValue(ref reader, "", fields, bytes);
        var sources = new List<GameHelpSource>();
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        var citations = new List<GameHelpCitation>();
        foreach (var annotation in annotations)
        {
            if (!numbers.TryGetValue(annotation.Url, out var number))
            {
                number = sources.Count + 1;
                sources.Add(new(annotation.Title, annotation.Url));
                numbers.Add(annotation.Url, number);
            }
            // Missing or invalid ranges remain aggregate-only. Never guess their supported text.
            if (annotation.Start is not { } start || annotation.End is not { } end || start < 0 || end <= start || end > bytes.Length) continue;
            foreach (var field in fields)
            {
                var begin = Math.Max(start, field.Begin);
                var finish = Math.Min(end, field.End);
                if (begin >= finish || !field.Boundaries.TryGetValue(begin, out var decodedStart) ||
                    !field.Boundaries.TryGetValue(finish, out var decodedEnd)) continue;
                var trimStart = field.Text.Length - field.Text.TrimStart().Length;
                var length = field.Text.Trim().Length;
                decodedStart = Math.Clamp(decodedStart - trimStart, 0, length);
                decodedEnd = Math.Clamp(decodedEnd - trimStart, 0, length);
                var text = field.Text.Trim();
                if (decodedStart < decodedEnd && Boundary(text, decodedStart) && Boundary(text, decodedEnd))
                    citations.Add(new(field.Path, decodedStart, decodedEnd, number));
            }
        }
        return (sources.ToArray(), citations.Distinct().ToArray());
    }

    private sealed record Field(string Path, string Text, int Begin, int End, Dictionary<int, int> Boundaries);
    private static void ReadValue(ref Utf8JsonReader reader, string path, List<Field> fields, byte[] json)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString()!;
                reader.Read(); ReadValue(ref reader, path.Length == 0 ? name : path + "." + name, fields, json);
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                ReadValue(ref reader, path + "." + index++, fields, json);
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var begin = checked((int)reader.TokenStartIndex + 1);
            var end = checked((int)reader.BytesConsumed - 1);
            var text = reader.GetString()!;
            var map = new Dictionary<int, int> { [begin] = 0 };
            var decoded = 0;
            for (var position = begin; position < end;)
            {
                if (json[position] == (byte)'\\')
                {
                    position += json[position + 1] == (byte)'u' ? 6 : 2;
                    ++decoded;
                }
                else
                {
                    if (Rune.DecodeFromUtf8(json.AsSpan(position, end - position), out var rune, out var consumed) != OperationStatus.Done) return;
                    position += consumed; decoded += rune.Utf16SequenceLength;
                }
                map[position] = decoded;
            }
            if (decoded == text.Length) fields.Add(new(path, text, begin, end, map));
        }
    }
    internal static bool Boundary(string text, int offset) => offset >= 0 && offset <= text.Length &&
        !(offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));

    internal static string? FieldText(GameHelpAnswer answer, string path)
    {
        if (path == "hint") return answer.Answer;
        if (path == "detailedSolution") return answer.DetailedSolution;
        if (path == "uncertainty") return answer.Uncertainty;
        var parts = path.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var index) || index < 0) return null;
        if (parts.Length == 2 && parts[0] == "observations" && index < answer.Observations.Count) return answer.Observations[index];
        if (parts.Length == 2 && parts[0] == "searchQueries" && index < answer.SearchQueries.Count) return answer.SearchQueries[index];
        if (parts.Length == 3 && parts[0] == "replyOptions" && index < answer.ReplyOptions.Count)
            return parts[2] switch { "label" => answer.ReplyOptions[index].Label, "message" => answer.ReplyOptions[index].Message, _ => null };
        return null;
    }
}
