using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobModifierLine(string Kind, JsonElement Formatted);
internal sealed record PobModifierDifference(string Id, string Status, PobModifierLine[] Before, PobModifierLine[] After);
internal sealed record PobItemModifierComparison(PobModifierDifference[] Rows, bool Available)
{
    internal static PobItemModifierComparison Read(JsonElement state, string slot)
    {
        try
        {
            if(state.ValueKind!=JsonValueKind.Object||!state.TryGetProperty("itemComparisons",out _))return new([],false);
            if (state.TryGetProperty("itemComparisonUnavailable", out var unavailable) && unavailable.ValueKind == JsonValueKind.True)
                return new([], false);
            var comparison = PobAdapterData.Array(state, "itemComparisons", 128, optional: true)
                .FirstOrDefault(value => PobAdapterData.Text(value, "slot", 128) == slot);
            if (comparison.ValueKind != JsonValueKind.Object) return new([], true);
            var rows = PobAdapterData.Array(comparison, "rows", 1024).Select(row =>
            {
                var status = PobAdapterData.Text(row, "status", 16);
                if (status is not ("added" or "removed" or "changed" or "same")) throw new IOException("Unknown modifier comparison status.");
                PobModifierLine[] Lines(string side) => PobAdapterData.Array(row, side, 1024).Select(line =>
                {
                    _ = PobAdapterData.Text(line, "text", 32768);
                    return new PobModifierLine(PobAdapterData.Text(line, "kind", 32), line);
                }).ToArray();
                return new PobModifierDifference(PobAdapterData.Text(row, "id", 32), status, Lines("before"), Lines("after"));
            }).ToArray();
            PobAdapterData.Unique(rows.Select(row=>row.Id),"modifier comparison identities");
            return new(rows, true);
        }
        catch (IOException) { return new([], false); }
    }
}
