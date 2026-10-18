using WidgetRail.PackageDistribution;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// Host-lifetime public release metadata. Widget unloads do not discard freshness or cooldowns.
/// One bounded, serial traversal serves every listing and update batch; package bytes still undergo full review.
/// </summary>
internal sealed class MarketplaceReleaseCache(HttpMessageHandler? handler, TimeProvider clock) : IDisposable
{
    internal static readonly TimeSpan Freshness = TimeSpan.FromMinutes(15);
    internal const int MaximumPages = 5; // 500 releases, at most 10 MiB of response bodies per traversal.
    private readonly GitHubReleaseClient _client = new(handler);
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Dictionary<int, GitHubReleasePage> _pages = new();
    private DateTimeOffset? _checked;
    private DateTimeOffset _nextRequest;
    private IReadOnlyList<GitHubRelease> _releases = [];
    private string? _failure;
    private int _failures;
    private bool _limited;

    internal sealed record Result(IReadOnlyList<GitHubRelease> Releases, DateTimeOffset? CheckedAtUtc, string Message, bool Failed, bool Truncated);

    internal async Task<Result> GetAsync(bool force, CancellationToken token)
    {
        await _serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var now = clock.GetUtcNow();
            if (now < _nextRequest)
                return Current(_failure is not null ? FailureMessage() : $"Using recently checked catalogue availability. Next check available after {_nextRequest.ToLocalTime():g}.");
            if (!force && _failure is null && _checked is { } checkedAt && now - checkedAt < Freshness)
                return Current("Using recently checked catalogue availability.");
            var all = new List<GitHubRelease>();
            var hasNext = false;
            try
            {
                for (var page = 1; page <= MaximumPages; page++)
                {
                    token.ThrowIfCancellationRequested();
                    now = clock.GetUtcNow();
                    if (now < _nextRequest) throw new GitHubRequestDeferredException(_nextRequest, "GitHub requested a pause.");
                    _pages.TryGetValue(page, out var cached);
                    var response = await _client.ListPageAsync(BridgeWidgetMarketplace.Repository, page,
                        cached?.ETag, cached?.LastModified, now, token).ConfigureAwait(false);
                    if (response.RetryAfterUtc is { } pause) _nextRequest = pause;
                    if (response.Releases is null)
                    {
                        if (cached is null) throw new DistributionOperationException("GitHub returned an unchanged page without cached metadata.");
                        response = response with { Releases = cached.Releases, HasNext = cached.HasNext };
                    }
                    _pages[page] = response;
                    all.AddRange(response.Releases!);
                    hasNext = response.HasNext;
                    if (!hasNext)
                    {
                        foreach (var obsolete in _pages.Keys.Where(key => key > page).ToArray()) _pages.Remove(obsolete);
                        break;
                    }
                }
                token.ThrowIfCancellationRequested();
                _releases = all.ToArray();
                _checked = clock.GetUtcNow();
                _limited = hasNext;
                _failure = null;
                _failures = 0;
                // A repeated click or another activation never creates a burst, even without server limits.
                var minimum = _checked.Value.AddSeconds(30);
                if (minimum > _nextRequest) _nextRequest = minimum;
                return Current(_limited ? "Checked the latest 500 releases; older widget availability may be incomplete."
                    : "Catalogue refreshed. Download a widget to check compatibility and review its permissions.");
            }
            catch (Exception error) when (error is GitHubRequestDeferredException or DistributionOperationException or DistributionUsageException or IOException)
            {
                _failures = Math.Min(_failures + 1, 5);
                var until = clock.GetUtcNow().AddMinutes(Math.Min(15, 1 << (_failures - 1)));
                if (error is GitHubRequestDeferredException { RetryAfterUtc: { } server } && server > until) until = server;
                if (until > _nextRequest) _nextRequest = until;
                _failure = error is GitHubRequestDeferredException ? "GitHub asked us to wait" : "The release check failed";
                return Current(FailureMessage());
            }
        }
        finally { _serial.Release(); }
    }

    private string FailureMessage() => $"{_failure}. Try again after {_nextRequest.ToLocalTime():g}. " +
        (_checked is null ? "Availability has not been checked yet." : "Showing previously checked availability; it may be out of date.");

    private Result Current(string message) => new(_releases, _checked,
        _failure is null && _limited && !message.Contains("500", StringComparison.Ordinal)
            ? message + " Discovery is limited to the latest 500 releases." : message, _failure is not null, _limited);

    public void Dispose() { _client.Dispose(); _serial.Dispose(); }
}
