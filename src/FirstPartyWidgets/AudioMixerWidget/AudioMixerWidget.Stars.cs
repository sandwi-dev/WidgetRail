using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.AudioMixer;

internal sealed record AudioMixerStars(string[] Applications);

public sealed partial class AudioMixerWidget
{
    // Session IDs expire when applications stop. The public audio contract exposes
    // display names, not durable application IDs: equal names intentionally share a star.
    private readonly HashSet<string> _starredApplications = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _starsGate = new(1, 1);
    private bool _starsLoaded;
    private const int MaximumStars = 256;

    private async Task LoadStarsAsync(CancellationToken token)
    {
        await _starsGate.WaitAsync(token).ConfigureAwait(false);
        try { await ReadStarsAsync(token).ConfigureAwait(false); }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or JsonException)
        { /* Audio remains available; a later activation or star action retries. */ }
        finally { _starsGate.Release(); }
    }

    // Serialized with writes so a late load cannot overwrite a user's new choice.
    private async Task ReadStarsAsync(CancellationToken token)
    {
        if (_starsLoaded) return;
        var saved = await HostServices.PrivateState.ReadAsync<AudioMixerStars>(cancellationToken: token).ConfigureAwait(false);
        lock (_stateLock)
        {
            foreach (var name in (saved.Value?.Applications ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Take(MaximumStars))
                _starredApplications.Add(NormalizeDisplayName(name));
            _starsLoaded = true;
        }
    }

    private async Task ToggleStarAsync(string sessionId, CancellationToken token)
    {
        await _starsGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await ReadStarsAsync(token).ConfigureAwait(false);
            string name;
            bool wasStarred;
            string[] next;
            lock (_stateLock)
            {
                var session = FindSessionLocked(sessionId);
                if (session is null) return;
                name = session.DisplayName;
                SelectSessionLocked(sessionId);
                wasStarred = _starredApplications.Contains(name);
                if (!wasStarred && _starredApplications.Count >= MaximumStars)
                {
                    SetControlStatusLocked("Star limit reached. Unstar an application first.");
                    return;
                }
                next = wasStarred
                    ? _starredApplications.Where(value => !StringComparer.OrdinalIgnoreCase.Equals(value, name)).ToArray()
                    : [.. _starredApplications, name];
            }
            // Keep the existing slider enabled and focused while saving. Publish the
            // reordered list only after success, so failed saves never pretend to stick.
            await HostServices.PrivateState.WriteAsync(new AudioMixerStars(next), cancellationToken: token).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (wasStarred) _starredApplications.Remove(name);
                else _starredApplications.Add(name);
                _status = wasStarred ? $"{name} unstarred" : $"{name} starred";
                _statusIsError = false;
            }
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or JsonException)
        {
            lock (_stateLock) SetControlStatusLocked("Could not save starred applications. Try again.");
        }
        finally { _starsGate.Release(); Invalidate(); }
    }
}
