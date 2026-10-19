using System.Text.Json;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class MusicService
{
    public async Task<IReadOnlyList<string>> SearchSuggestionsAsync(string query, CancellationToken token)
    {
        if (query.Trim().Length < 2 || query.Length > 96 || query.Any(char.IsControl)) return [];
        await InitializeAsync(token).ConfigureAwait(false);
        long epoch, account;
        lock (_gate)
        {
            if (_ratingAccountChanging) return [];
            epoch = _ratingEpoch;
            account = _providerAccountGeneration;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        JsonElement response;
        try { response = await _backend!.CallAsync("search_suggestions", new { query, accountGeneration = account }, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return []; }
        token.ThrowIfCancellationRequested();
        lock (_gate)
            if (epoch != _ratingEpoch || _ratingAccountChanging) return [];
        if (response.ValueKind != JsonValueKind.Array) throw new IOException("Search suggestions are unavailable.");
        return response.EnumerateArray().Take(32)
            .Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && !value.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray();
    }
}
