using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

/// <summary>Account-scoped bounded saved state. Serializes reads with writes so a late
/// read cannot overwrite an acknowledged write. No network work occurs in rendering.</summary>
internal sealed class SpotifySavedLibrary(ISpotifyApplicationService service)
{
    private readonly object gate = new();
    private readonly SemaphoreSlim requests = new(1, 1);
    private readonly Dictionary<string, bool> saved = new(StringComparer.Ordinal);
    private readonly Queue<string> insertionOrder = new();
    private long generation;
    private CancellationTokenSource accountLifetime = new();
    internal long Generation { get { lock (gate) return generation; } }
    internal bool? Get(string uri) { lock (gate) return saved.TryGetValue(uri, out var value) ? value : null; }
    internal void Reset()
    {
        CancellationTokenSource previous;
        lock (gate) { ++generation; saved.Clear(); insertionOrder.Clear(); previous = accountLifetime; accountLifetime = new(); }
        previous.Cancel(); previous.Dispose();
    }
    private CancellationTokenSource RequestLifetime(long expected, CancellationToken token)
    {
        lock (gate)
        {
            if (expected != generation) throw new OperationCanceledException(token);
            return CancellationTokenSource.CreateLinkedTokenSource(token, accountLifetime.Token);
        }
    }
    internal static bool IsTrack([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? uri) => uri is not null && uri.StartsWith("spotify:track:", StringComparison.Ordinal) && uri.Length > 14;
    internal async Task ReadAsync(IEnumerable<string> candidates, long expected, CancellationToken token)
    {
        using var lifetime = RequestLifetime(expected, token);
        token = lifetime.Token;
        var uris = candidates.Where(IsTrack).Distinct(StringComparer.Ordinal).Take(80).ToArray();
        await requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            foreach (var batch in uris.Chunk(40))
            {
                string[] missing;
                lock (gate)
                {
                    if (expected != generation) return;
                    missing = batch.Where(uri => !saved.ContainsKey(uri)).ToArray();
                }
                if (missing.Length == 0) continue;
                var result = await service.GetSavedItemsAsync(missing, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Count != missing.Length) throw new SpotifyApplicationException("library_invalid", "Spotify returned incomplete saved-song status.");
                lock (gate)
                {
                    if (expected != generation) return;
                    for (var i = 0; i < missing.Length; i++) Store(missing[i], result[i]);
                }
            }
        }
        finally { requests.Release(); }
    }
    internal async Task<bool> WriteAsync(string uri, bool value, long expected, CancellationToken token)
    {
        if (!IsTrack(uri)) return false;
        using var lifetime = RequestLifetime(expected, token);
        token = lifetime.Token;
        await requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (gate) if (expected != generation) return false;
            await service.SetSavedItemAsync(uri, value, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (expected != generation) return false;
                Store(uri, value); return true;
            }
        }
        finally { requests.Release(); }
    }
    private void Store(string uri, bool value)
    {
        if (!saved.ContainsKey(uri))
        {
            if (saved.Count >= 512) saved.Remove(insertionOrder.Dequeue());
            insertionOrder.Enqueue(uri);
        }
        saved[uri] = value;
    }
}

internal sealed record SpotifySavedPresentation(bool? Saved, bool Busy);

public sealed partial class SpotifyWidget
{
    private readonly SpotifySavedLibrary _savedLibrary;
    private readonly HashSet<string> _savedBusy = new(StringComparer.Ordinal);
    private (long Generation, string Uri)? _observedSaved;
    private string? _savedAccountPartition;

    private void AdoptSavedAccountLocked(string? partition)
    {
        if (_savedAccountPartition == partition) return;
        ResetSavedSongsLocked(); _savedAccountPartition = partition;
    }

    private void ObserveSavedSong()
    {
        string uri; long generation;
        lock (_gate)
        {
            if (!IsActive || _viewState != SpotifyWidgetViewState.Ready || _playback?.Item is not { } item || !SpotifySavedLibrary.IsTrack(item.Uri)) return;
            uri = item.Uri; generation = _savedLibrary.Generation;
            if (_observedSaved == (generation, uri)) return;
            _observedSaved = (generation, uri);
        }
        Operations.RunLatest("spotify.saved-read", async operation =>
        {
            try { await _savedLibrary.ReadAsync([uri], generation, operation.CancellationToken); }
            catch (Exception error) when (error is SpotifyApplicationException or OperationCanceledException) { }
            if (operation.IsCurrent && generation == _savedLibrary.Generation) Invalidate();
        }, WidgetOperationLifetime.Active);
    }

    private async ValueTask ChangeSavedSongAsync(string uri, bool value, CancellationToken token, bool notifySuccess = true)
    {
        long generation;
        lock (_gate)
        {
            if (!IsActive || _viewState != SpotifyWidgetViewState.Ready || !SpotifySavedLibrary.IsTrack(uri) || !_savedBusy.Add(uri)) return;
            generation = _savedLibrary.Generation;
        }
        Invalidate();
        try
        {
            if (await _savedLibrary.WriteAsync(uri, value, generation, token).ConfigureAwait(false) && notifySuccess)
                ShowActionToast(value ? "Saved to liked songs" : "Removed from liked songs", "Your Spotify library is updated.", ToastTone.Success);
        }
        catch (OperationCanceledException) { }
        catch (SpotifyApplicationException)
        {
            if (generation == _savedLibrary.Generation)
                ShowActionToast("Could not update liked songs", "Try again. If access is missing, reconnect Spotify in Settings.", ToastTone.Warning);
        }
        finally
        {
            lock (_gate) if (generation == _savedLibrary.Generation) _savedBusy.Remove(uri);
            Invalidate();
        }
    }

    private void ResetSavedSongsLocked()
    {
        _savedLibrary.Reset(); _savedBusy.Clear(); _observedSaved = null;
        _recentSearches.Clear(); _loadedSuggestionNames.Clear(); RefreshSuggestionCatalogLocked();
    }
}
