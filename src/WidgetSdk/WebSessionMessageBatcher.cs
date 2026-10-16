using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Splits collections into complete JSON messages within the shared web-session byte limit.</summary>
public static class WebSessionMessageBatcher
{
    /// <summary>
    /// The pure envelope factory must include each supplied item once, in order.
    /// The returned messages preserve item order. The caller owns transaction/revision semantics.
    /// A single oversized item is rejected rather than truncated.
    /// </summary>
    public static IReadOnlyList<JsonElement> Create<T>(IEnumerable<T> items, Func<IReadOnlyList<T>, JsonElement> envelope)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(envelope);
        var result = new List<JsonElement>();
        var source = items.ToArray();
        for (var offset = 0; offset < source.Length;)
        {
            var count = Math.Min(64, source.Length-offset);
            var acceptedCount = 0;
            JsonElement accepted = default;
            while (true)
            {
                var candidate = envelope(new ArraySegment<T>(source, offset, count));
                if (JsonSerializer.SerializeToUtf8Bytes(candidate).Length > WebSessionContract.MaximumMessageBytes)
                {
                    if (acceptedCount > 0) break;
                    if (count == 1) throw new ArgumentException("A collection item exceeds the web-session message limit.", nameof(items));
                    count = Math.Max(1, count / 2);
                    continue;
                }
                accepted=candidate.Clone();acceptedCount=count;
                if (count==source.Length-offset) break;
                count=Math.Min(source.Length-offset,count*2);
            }
            result.Add(accepted);offset+=acceptedCount;
        }
        return result;
    }
}
