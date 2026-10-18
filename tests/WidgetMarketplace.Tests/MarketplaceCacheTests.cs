using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Catalog = WidgetRail.WidgetCatalog.WidgetCatalog;

internal static class MarketplaceCacheTests
{
    internal static async Task FreshnessAndValidators()
    {
        var clock = new Clock();
        using var http = new Http((request, count, _) => Task.FromResult(count == 1 ? Page("v1", etag: "\"one\"") :
            request.Headers.IfNoneMatch.SingleOrDefault()?.Tag == "\"one\"" ? new(HttpStatusCode.NotModified) : throw new Exception("Missing conditional ETag")));
        using var cache = new MarketplaceReleaseCache(http, clock);
        var first = await cache.GetAsync(false, default);
        Check(first.Releases.Single().Tag == "v1" && http.Count == 1, "First lookup did not load metadata");
        for (var i = 0; i < 5; i++) await cache.GetAsync(false, default);
        await cache.GetAsync(true, default);
        Check(http.Count == 1, "Repeated activation/click generated requests inside minimum spacing");
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.GetAsync(false, default);
        Check(http.Count == 1, "Fresh activation ignored freshness");
        var manual = await cache.GetAsync(true, default);
        Check(http.Count == 2 && manual.Releases.Single().Tag == "v1" && manual.CheckedAtUtc == clock.GetUtcNow(), "Explicit conditional refresh lost cached metadata");
        clock.Advance(MarketplaceReleaseCache.Freshness);
        await cache.GetAsync(false, default);
        Check(http.Count == 3, "Expiry did not check unchanged metadata");
    }

    internal static async Task PaginationAndBound()
    {
        var clock = new Clock();
        using var http = new Http((request, count, _) =>
        {
            var page = count <= 5 ? count : count - 5;
            Check(request.RequestUri!.Query.Contains("per_page=100&page=" + page, StringComparison.Ordinal), "Pagination escaped bounded page sequence");
            if (count > 5)
            {
                Check(request.Headers.IfNoneMatch.Single().Tag == $"\"{page}\"", "Cached older page lost its validator");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            return Task.FromResult(Page("v" + page, etag: $"\"{page}\"", next: true));
        });
        using var cache = new MarketplaceReleaseCache(http, clock);
        var first = await cache.GetAsync(false, default);
        Check(http.Count == 5 && first.Releases.Count == 5 && first.Message.Contains("500"), "Pagination was not bounded or omitted its limit");
        clock.Advance(MarketplaceReleaseCache.Freshness);
        var second = await cache.GetAsync(false, default);
        Check(http.Count == 10 && second.Releases.SequenceEqual(first.Releases), "Conditional pagination lost older release results");
    }

    internal static async Task PaginationShrinks()
    {
        var clock = new Clock();
        using var http = new Http((_, count, _) => Task.FromResult(Page("v" + count, etag: $"\"{count}\"", next: count == 1)));
        using var cache = new MarketplaceReleaseCache(http, clock);
        Check((await cache.GetAsync(false, default)).Releases.Count == 2, "Older page missing");
        clock.Advance(MarketplaceReleaseCache.Freshness);
        var refreshed = await cache.GetAsync(false, default);
        Check(http.Count == 3 && refreshed.Releases.Single().Tag == "v3", "Removed pagination kept orphaned releases");
    }

    internal static async Task CooldownAndStale()
    {
        var clock = new Clock();
        using var http = new Http((_, count, _) =>
        {
            if (count != 2) return Task.FromResult(Page("v" + count));
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds().ToString());
            return Task.FromResult(response);
        });
        using var cache = new MarketplaceReleaseCache(http, clock);
        var first = await cache.GetAsync(false, default);
        clock.Advance(TimeSpan.FromMinutes(1));
        var failed = await cache.GetAsync(true, default);
        Check(failed.Failed && failed.CheckedAtUtc == first.CheckedAtUtc && failed.Releases.Single().Tag == "v1" && failed.Message.Contains("out of date"), "Rate limit destroyed stale availability or failed to identify it");
        clock.Advance(TimeSpan.FromMinutes(9));
        await cache.GetAsync(true, default);
        Check(http.Count == 2, "Explicit refresh bypassed primary reset cooldown");
        clock.Advance(TimeSpan.FromMinutes(1));
        var recovered = await cache.GetAsync(true, default);
        Check(http.Count == 3 && !recovered.Failed && recovered.Releases.Single().Tag == "v3", "Cooldown recovery failed");
    }

    internal static async Task SecondaryBackoffAndNoAutomaticRetry()
    {
        var clock = new Clock();
        using var http = new Http((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var cache = new MarketplaceReleaseCache(http, clock);
        var empty = await cache.GetAsync(false, default);
        Check(empty.Failed && empty.CheckedAtUtc is null && empty.Message.Contains("not been checked"), "Initial error implied successful availability");
        await cache.GetAsync(true, default);
        Check(http.Count == 1, "Secondary limit automatically retried");
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.GetAsync(true, default);
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.GetAsync(true, default);
        Check(http.Count == 2, "Repeated secondary failures did not back off exponentially");
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.GetAsync(true, default);
        Check(http.Count == 3, "Retry remained blocked after backoff");
    }

    internal static async Task SuccessfulExhaustionStopsPagination()
    {
        var clock = new Clock();
        using var http = new Http((_, _, _) =>
        {
            var page = Page("v1", next: true);
            page.Headers.Add("X-RateLimit-Remaining", "0");
            page.Headers.Add("X-RateLimit-Reset", clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds().ToString());
            return Task.FromResult(page);
        });
        using var cache = new MarketplaceReleaseCache(http, clock);
        var result = await cache.GetAsync(false, default);
        Check(http.Count == 1 && result.Failed && result.CheckedAtUtc is null, "Successful exhausted response requested another page or published partial results");
        await cache.GetAsync(true, default);
        Check(http.Count == 1, "Refresh bypassed successful response's reset header");
    }

    internal static async Task CoalescingAndCancellation()
    {
        var clock = new Clock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new Http(async (_, _, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return Page("v1"); });
        using var cache = new MarketplaceReleaseCache(http, clock);
        var first = cache.GetAsync(false, default);
        await entered.Task;
        using var cancelled = new CancellationTokenSource();
        var waiter = cache.GetAsync(true, cancelled.Token);
        cancelled.Cancel();
        try { await waiter; throw new Exception("Cancelled waiter completed"); } catch (OperationCanceledException) { }
        var second = cache.GetAsync(true, default);
        release.SetResult();
        await Task.WhenAll(first, second);
        Check(http.Count == 1, "Concurrent explicit checks did not share metadata or cancelling waiter cancelled owner");
    }

    internal static async Task MalformedAndBoundedResponses()
    {
        foreach (var content in new[] { "malformed", new string(' ', 2 * 1024 * 1024 + 1), JsonSerializer.Serialize(Enumerable.Range(0,101).Select(_ => new { tag_name="v1",draft=false,prerelease=false,assets=Array.Empty<object>() })) })
        {
            var clock = new Clock();
            using var http = new Http((_, count, _) => Task.FromResult(count == 1 ? Page("v1") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }));
            using var cache = new MarketplaceReleaseCache(http, clock);
            await cache.GetAsync(false, default);
            clock.Advance(MarketplaceReleaseCache.Freshness);
            var failed = await cache.GetAsync(false, default);
            Check(failed.Failed && failed.Releases.Single().Tag == "v1", "Invalid/oversized metadata replaced usable cached content");
        }
    }

    internal static async Task LastModifiedAndUnexpected304()
    {
        var clock = new Clock();
        using var http = new Http((request, count, _) =>
        {
            if (count == 1) { var page = Page("v1"); page.Content.Headers.LastModified = clock.GetUtcNow(); return Task.FromResult(page); }
            Check(request.Headers.IfModifiedSince.HasValue, "Last-Modified fallback was not conditional");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        using var cache = new MarketplaceReleaseCache(http, clock);
        await cache.GetAsync(false, default);
        clock.Advance(MarketplaceReleaseCache.Freshness);
        Check(!(await cache.GetAsync(false, default)).Failed, "Valid Last-Modified 304 failed");
        using var invalidHttp = new Http((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)));
        using var invalid = new MarketplaceReleaseCache(invalidHttp, clock);
        Check((await invalid.GetAsync(false, default)).Failed, "Unexpected 304 was accepted without a previous page");
    }

    internal static async Task BridgeAndWire()
    {
        var command = new MarketplaceCommand("refresh", ForceRefresh: true);
        Check(JsonSerializer.Deserialize<MarketplaceCommand>(JsonSerializer.Serialize(command)) == command &&
            MarketplaceValidation.CommandError(command) is null && MarketplaceValidation.CommandError(new("status", ForceRefresh: true)) is not null,
            "Forced refresh did not round-trip or escaped its action bound");
        var root = Path.Combine(Path.GetTempPath(), "catalogue-cache-" + Guid.NewGuid().ToString("N"));
        var clock = new Clock();
        using var http = new Http((_, count, _) => Task.FromResult(count == 1 ? Page("v1", asset: true) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        try
        {
            await using var bridge = new BridgeWidgetMarketplace(new Catalog(root), _ => Task.CompletedTask,
                [new("dev.test.cache", "dev.test", "Cache test", "Test fixture", [])], http, clock);
            await bridge.HandleAsync(new("refresh"), default);
            var initial = await Finished(bridge);
            Check(initial.Items.Single().AvailableVersion == "1.0.0", "Bridge omitted cached release");
            clock.Advance(TimeSpan.FromMinutes(1));
            await bridge.HandleAsync(new("refresh"), default);
            await Finished(bridge);
            Check(http.Count == 1, "Bridge automatic activation forced network");
            await bridge.HandleAsync(command, default);
            var failed = await Finished(bridge);
            Check(failed.Operation!.State == "failed" && failed.Items.Single().AvailableVersion == "1.0.0" && failed.CheckedAtUtc == initial.CheckedAtUtc,
                "Bridge error cleared cached availability or advanced last-check time");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    internal static async Task RetainedCuratedAvailability()
    {
        var root = Path.Combine(Path.GetTempPath(), "catalogue-aging-" + Guid.NewGuid().ToString("N"));
        var clock = new Clock();
        var phase = 0;
        using var http = new Http((request, _, _) =>
        {
            var page = int.Parse(request.RequestUri!.Query.Split("page=").Last());
            var next = phase == 0 ? page < 5 : phase == 1;
            // The widget initially appears at release 500, then ages beyond the discovery window.
            var entries = Enumerable.Range(0, phase == 2 ? 1 : 100).Select(index => new {
                tag_name = $"v{phase}-{page}-{index}", draft = false, prerelease = false,
                assets = phase == 0 && page == 5 && index == 99
                    ? new[] { new { name = "dev.test.cache-1.0.0.wrwidget", size = 1, digest = "sha256:" + new string('a',64) } } : [] });
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(entries)) };
            if (next) response.Headers.Add("Link", "<https://api.github.com/repos/sandwi-dev/WidgetRail/releases?page=2>; rel=\"next\"");
            return Task.FromResult(response);
        });
        try
        {
            await using var bridge = new BridgeWidgetMarketplace(new Catalog(root), _ => Task.CompletedTask,
                [new("dev.test.cache", "dev.test", "Cache test", "Test fixture", [])], http, clock);
            await bridge.HandleAsync(new("refresh"), default);
            var original = await Finished(bridge);
            Check(original.Items.Single().AvailableVersion == "1.0.0" && !original.Items.Single().RetainedAvailability && http.Count == 5,
                "Release 500 was not discovered normally");
            phase = 1;
            clock.Advance(MarketplaceReleaseCache.Freshness);
            await bridge.HandleAsync(new("refresh"), default);
            var retained = await Finished(bridge);
            Check(retained.Items.Single().AvailableVersion == "1.0.0" && retained.Items.Single().RetainedAvailability &&
                retained.Operation!.Message!.Contains("previously checked") && http.Count == 10,
                "Truncated discovery discarded or silently refreshed older curated availability");
            await bridge.HandleAsync(new("refresh"), default);
            Check((await Finished(bridge)).Items.Single().RetainedAvailability && http.Count == 10,
                "Cached reopening lost retained metadata provenance or repeated network discovery");
            phase = 2;
            clock.Advance(MarketplaceReleaseCache.Freshness);
            await bridge.HandleAsync(new("refresh"), default);
            var removed = await Finished(bridge);
            Check(removed.Items.Single().AvailableVersion is null && !removed.Items.Single().RetainedAvailability && http.Count == 11,
                "Complete discovery retained a package removed from all releases");
            try
            {
                await bridge.HandleAsync(new("download", PackageId: "dev.test.cache"), default);
                throw new Exception("Removed package retained hidden download authority");
            }
            catch (WidgetRail.PlatformDiagnostics.PlatformDiagnosticsException) { }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<MarketplaceSnapshot> Finished(BridgeWidgetMarketplace bridge)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = await bridge.HandleAsync(new("status"), timeout.Token);
            Check(MarketplaceValidation.SnapshotError(snapshot) is null, "Snapshot violates wire bounds");
            if (snapshot.Operation?.State != "running") return snapshot;
            await Task.Delay(10, timeout.Token);
        }
    }
    private static HttpResponseMessage Page(string tag, string? etag = null, bool next = false, bool asset = false)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] {
            new { tag_name = tag, draft = false, prerelease = false, assets = asset ? new[] { new { name = "dev.test.cache-1.0.0.wrwidget", size = 1, digest = "sha256:" + new string('a',64) } } : [] } })) };
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue(etag);
        if (next) response.Headers.Add("Link", "<https://api.github.com/repos/sandwi-dev/WidgetRail/releases?per_page=100&page=2>; rel=\"next\"");
        return response;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan elapsed) => _now += elapsed;
    }
    private sealed class Http(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, Interlocked.Increment(ref Count), token);
    }
}
