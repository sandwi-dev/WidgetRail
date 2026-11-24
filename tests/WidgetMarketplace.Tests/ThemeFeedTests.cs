using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.PlatformSettings;

internal static class ThemeFeedTests
{
    internal static async Task RemoteThemeDownload()
    {
        var root = Directory.CreateTempSubdirectory("theme-remote-");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var token = deadline.Token;
            var assembly = typeof(MarketplaceThemeFeed).Assembly;
            using var index = assembly.GetManifestResourceStream("WidgetRail.ThemeCatalogue.index.json")!;
            using var document = await JsonDocument.ParseAsync(index, cancellationToken: token);
            var bundled = document.RootElement.GetProperty("themes")[0]
                .Deserialize<CatalogueTheme>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            using var source = assembly.GetManifestResourceStream($"WidgetRail.ThemeCatalogue.{bundled.Sha256}.wrtheme")!;
            using var package = new MemoryStream();
            await source.CopyToAsync(package, token);
            // A newly published archive must go through HTTP rather than the embedded fallback.
            using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
                archive.GetEntry("theme.json")!.LastWriteTime = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
            var bytes = package.ToArray();
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Require(digest != bundled.Sha256, "The remote fixture still resolves to the bundled archive");
            var listing = bundled with { Sha256 = digest, Bytes = bytes.Length, PreviewHashes = null };
            var publication = JsonSerializer.Serialize(new { schemaVersion = 1, themes = new[] { listing },
                featured = listing.Id, collections = Array.Empty<object>() });
            var handler = new Handler((request, count, _) =>
            {
                if (request.RequestUri?.AbsoluteUri == ThemeCataloguePublication.Origin + "index.json")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(publication) });
                Require(request.RequestUri?.AbsoluteUri == ThemeCataloguePublication.Origin + $"packages/{digest}.wrtheme",
                    "The listing did not request its exact package URL");
                if (count == 2) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var response = count == 3 ? new byte[bytes.Length] : bytes;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) });
            });
            var paths = new PlatformSettingsPaths(root.FullName);
            await using var bridge = new BridgeWidgetMarketplace(new WidgetRail.WidgetCatalog.WidgetCatalog(Path.Combine(root.FullName, "widgets")),
                _ => Task.CompletedTask, definitions: [], http: handler, stateRoot: Path.Combine(root.FullName, "widget-state"));
            while ((await bridge.HandleAsync(new("themes", ThemeQuery: new()), token)).Themes?.Featured?.Sha256 != digest)
                await Task.Delay(5, token);
            var selection = new CatalogueThemeSelection(listing.Id, listing.Version, digest);
            var install = await bridge.HandleAsync(new("theme-install", Theme: selection), token);
            var result = await WaitForInstall(install);
            Require(result is { State: "failed", Message: "This package is unavailable from the catalogue. Try again later." } &&
                !Directory.Exists(paths.ThemesDirectory), "A missing archive was presented as a network disconnection");
            result = await WaitForInstall(await bridge.HandleAsync(new("theme-install", Theme: selection), token));
            Require(result.State == "failed" && !Directory.Exists(paths.ThemesDirectory),
                "A same-length archive with the wrong digest reached theme storage");
            var preview = await bridge.PreviewThemeAsync(ThemeCataloguePublication.PreviewKeyFor(listing), token);
            Require(preview.ContainsKey("tray") && !Directory.Exists(paths.ThemesDirectory),
                "The first verified remote preview failed or took an installed slot");
            result = await WaitForInstall(await bridge.HandleAsync(new("theme-install", Theme: selection), token));
            Require(result.State == "completed" && !File.Exists(paths.SettingsFile) && handler.Count == 5 &&
                (await bridge.HandleAsync(new("theme-status", Theme: selection), token)).ThemeStatus is { Installed: true, Applied: false },
                "A verified remote download did not install independently of appearance");

            async Task<MarketplaceOperation> WaitForInstall(MarketplaceSnapshot snapshot)
            {
                while (snapshot.Operation is { State: "running" })
                {
                    await Task.Delay(5, token);
                    snapshot = await bridge.HandleAsync(new("status"), token);
                }
                return snapshot.Operation!;
            }
        }
        finally { root.Delete(true); }
    }

    internal static async Task CancelInstall()
    {
        var root = Directory.CreateTempSubdirectory("theme-cancel-");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var token = deadline.Token;
            var digest = new string('a', 64);
            var listing = new CatalogueTheme("dev.test.cancel", "1.0.0", "Theme A", "dev.test", "Rail Studio",
                "Cancelled install fixture.", [], new DateOnly(2026, 11, 22), digest, 1024, new string('b', 64));
            var publication = JsonSerializer.Serialize(new { schemaVersion = 1, themes = new[] { listing }, featured = listing.Id, collections = Array.Empty<object>() });
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler(async (request, count, ct) =>
            {
                if (count == 1) return new(HttpStatusCode.OK) { Content = new StringContent(publication) };
                Require(count == 2 && request.RequestUri?.AbsolutePath.EndsWith(digest + ".wrtheme", StringComparison.Ordinal) == true,
                    "Download used the wrong listing");
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { stopped.TrySetResult(); }
                throw new Exception("Download never cancelled");
            });
            await using var bridge = new BridgeWidgetMarketplace(new WidgetRail.WidgetCatalog.WidgetCatalog(Path.Combine(root.FullName, "widgets")),
                _ => Task.CompletedTask, definitions: [], http: handler, stateRoot: Path.Combine(root.FullName, "widget-state"));
            while ((await bridge.HandleAsync(new("themes", ThemeQuery: new()), token)).Themes?.Featured?.Id != listing.Id)
                await Task.Delay(5, token);
            var selection = new CatalogueThemeSelection(listing.Id, listing.Version, listing.Sha256);
            var pending = await bridge.HandleAsync(new("theme-install-apply", Theme: selection), token);
            await started.Task.WaitAsync(token);
            try
            {
                await bridge.HandleAsync(new("theme-install", Theme: selection), token);
                throw new Exception("Concurrent catalogue install was accepted");
            }
            catch (WidgetRail.PlatformDiagnostics.PlatformDiagnosticsException error)
            { Require(error.Code == "marketplace_busy", "Concurrent install returned the wrong result"); }
            await bridge.HandleAsync(new("cancel", OperationId: pending.Operation!.Id), token);
            await stopped.Task.WaitAsync(token);
            MarketplaceSnapshot snapshot;
            do
            {
                snapshot = await bridge.HandleAsync(new("status"), token);
                if (snapshot.Operation?.State == "running") await Task.Delay(5, token);
            } while (snapshot.Operation?.State == "running");
            Require(snapshot.Operation?.State == "cancelled" && !Directory.Exists(Path.Combine(root.FullName, "themes")) &&
                !File.Exists(Path.Combine(root.FullName, "platform-settings.json")), "Cancelled operation left a partial install or applied a theme");
        }
        finally { root.Delete(true); }
    }

    internal static async Task ThemeOperations()
    {
        var root = Directory.CreateTempSubdirectory("theme-operations-");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = deadline.Token;
            var http = new Handler((request, count, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            var paths = new PlatformSettingsPaths(root.FullName);
            var store = new PlatformSettingsStore(paths);
            await using var bridge = new BridgeWidgetMarketplace(new WidgetRail.WidgetCatalog.WidgetCatalog(Path.Combine(root.FullName, "widgets")),
                _ => Task.CompletedTask, definitions: [], http: http, stateRoot: Path.Combine(root.FullName, "widget-state"));
            var browse = await bridge.HandleAsync(new("themes", ThemeQuery: new()), token);
            var theme = browse.Themes!.Featured!;
            var selection = new CatalogueThemeSelection(theme.Id, theme.Version, theme.Sha256);
            var styles = await bridge.PreviewThemeAsync(ThemeCataloguePublication.PreviewKeyFor(theme), token);
            Require(styles.ContainsKey("tray") && !File.Exists(paths.SettingsFile) && !Directory.Exists(paths.ThemesDirectory),
                "Try touched appearance or install storage");
            try
            {
                await bridge.HandleAsync(new("theme-install", Theme: selection with { Sha256 = new string('0', 64) }), token);
                throw new Exception("Changed digest was accepted");
            }
            catch (WidgetRail.PlatformDiagnostics.PlatformDiagnosticsException error)
            { Require(error.Code == "theme_listing_changed", "Wrong rejection for a stale listing"); }
            await Execute("theme-install", selection);
            Require(!File.Exists(paths.SettingsFile) && (await bridge.HandleAsync(new("theme-status", Theme: selection), token)).ThemeStatus
                is { Installed: true, Applied: false }, "Install changed appearance or failed to save");
            await Execute("theme-apply", selection);
            var appearance = (await store.LoadAsync(token)).Appearance;
            Require(appearance.ThemeId == theme.Id && appearance.ThemeVersion == theme.Version, "Apply did not select the verified version");
            var other = browse.Themes.Items.First(item => item.Id != theme.Id);
            await Execute("theme-install-apply", new(other.Id, other.Version, other.Sha256));
            Require((await store.LoadAsync(token)).Appearance.ThemeId == other.Id, "Install and apply did not select the installed theme");
            var last = browse.Themes.Items.Single(item => item.Id != theme.Id && item.Id != other.Id);
            var lastSelection = new CatalogueThemeSelection(last.Id, last.Version, last.Sha256);
            var template = Path.Combine(paths.ThemesDirectory, theme.Id, theme.Version);
            var manifest = await File.ReadAllTextAsync(Path.Combine(template, "theme.json"), token);
            for (int index = 0; index < ThemeCatalog.MaximumThemes - 2; index++)
            {
                var id = $"local.widgetrail.limit-{index:D3}";
                var dest = Directory.CreateDirectory(Path.Combine(paths.ThemesDirectory, id, theme.Version));
                foreach (var file in Directory.EnumerateFiles(template).Where(file => file.EndsWith(".wrss")))
                    File.Copy(file, Path.Combine(dest.FullName, Path.GetFileName(file)));
                await File.WriteAllTextAsync(Path.Combine(dest.FullName, "theme.json"), manifest.Replace(theme.Id, id, StringComparison.Ordinal), token);
            }
            Require((await bridge.HandleAsync(new("theme-status", Theme: lastSelection), token)).ThemeStatus
                is { Installed: false, AtCapacity: true }, "UI did not get the theme slot limit");
            await Execute("theme-install", lastSelection, "failed");
            Require(!Directory.Exists(Path.Combine(paths.ThemesDirectory, last.Id)), "Install exceeded the slot limit");
            Require((await bridge.PreviewThemeAsync(ThemeCataloguePublication.PreviewKeyFor(last), token)).ContainsKey("tray"),
                "A full theme catalogue prevents Try");

            async Task Execute(string action, CatalogueThemeSelection chosen, string expected = "completed")
            {
                var snapshot = await bridge.HandleAsync(new(action, Theme: chosen), token);
                var id = snapshot.Operation!.Id;
                while (snapshot.Operation!.State == "running")
                {
                    await Task.Delay(5, token);
                    snapshot = await bridge.HandleAsync(new("status"), token);
                }
                Require(MarketplaceValidation.SnapshotError(snapshot) is null && snapshot.Operation.Id == id &&
                    snapshot.Operation.State == expected, action + " produced the wrong result: " + snapshot.Operation.Message);
            }
        }
        finally { root.Delete(true); }
    }

    internal static async Task BrowseAndRecover()
    {
        var root = Directory.CreateTempSubdirectory("theme-feed-tests-");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var token = deadline.Token;
            var clock = new Clock();
            var firstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var rows = Enumerable.Range(0, 500).Select(i => new CatalogueTheme($"dev.test.theme-{i:D3}", "1.0.0", $"Theme {i:D3}",
                "dev.test", "Rail Studio", "A theme", [i % 2 == 0 ? "Coastal" : "Ember"], new DateOnly(2026, 11, 22),
                i.ToString("x64"), 1000, new string('c',64))).ToArray();
            var contents = JsonSerializer.Serialize(new { schemaVersion = 1, themes = rows, featured = rows[499].Id,
                collections = new[] { new { name = "Coastal", themes = rows.Take(4).Select(t => t.Id) } } });
            var responses = new Handler(async (request, count, ct) =>
            {
                Require(request.RequestUri?.AbsoluteUri == ThemeCataloguePublication.Origin + "index.json", "Unexpected feed URI");
                if (count == 1)
                {
                    await firstResponse.Task.WaitAsync(ct);
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(contents) };
                    response.Headers.ETag = new("\"first\"");
                    return response;
                }
                Require(request.Headers.IfNoneMatch.Single().Tag == (count == 6 ? "\"updated\"" : "\"first\""), "Lost ETag");
                return count switch
                {
                    2 => new(HttpStatusCode.NotModified),
                    3 => new(HttpStatusCode.ServiceUnavailable),
                    4 => new(HttpStatusCode.OK) { Content = new StringContent(contents.Replace("dev.test.theme-001", "dev.test.theme-000")) },
                    5 => new(HttpStatusCode.OK)
                    {
                        Content = new StringContent(contents.Replace("Theme 498", "Theme 498 Reconnected")),
                        Headers = { ETag = new("\"updated\"") },
                    },
                    6 => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[MarketplaceThemeFeed.MaximumIndexBytes + 1]) },
                    _ => throw new Exception("Unexpected extra request")
                };
            });
            var cache = Path.Combine(root.FullName, "cache");
            await using (var feed = new MarketplaceThemeFeed(cache, responses, clock))
            {
                var seed = await feed.BrowseAsync(new(), false, token);
                Require(seed.Total == 3 && seed.Refreshing, "First entry waited for network or lost the bundled index");
                foreach (var query in new[] { "GTA 6", "Grand Theft Auto", "afterglow inspiration" })
                    Require((await feed.BrowseAsync(new(query), false, token)).Items.Single().Id == "local.widgetrail.afterglow-fm",
                        "Theme inspiration search did not match " + query);
                var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => feed.BrowseAsync(new(), true, token).AsTask()));
                Require(parallel.All(p => p.Total == 3), "Concurrent readers saw partial publication");
                firstResponse.SetResult();
                var loaded = await Settle(feed, token);
                Require(responses.Count == 1 && loaded.Total == 500 && loaded.Items.Length == 8 && loaded.Featured?.Id == rows[499].Id &&
                    loaded.Collections.Single().Items.Length == 4, "Failed paged publication");
                var last = await feed.BrowseAsync(new("studio coastal", 124), false, token);
                Require(last.Total == 250 && last.Page == 31 && last.Items.Select(t => t.Name).SequenceEqual(["Theme 496","Theme 498"]) &&
                    last.Featured is null && last.Collections.Length == 0, "Search/page bounds lost results or curation leaked into search");
                clock.Advance(TimeSpan.FromMinutes(15));
                Require(!(await Settle(feed, token)).Offline && responses.Count == 2, "Conditional refresh lost the published feed");
                clock.Advance(TimeSpan.FromSeconds(30));
                await feed.BrowseAsync(new(), true, token);
                Require((await Settle(feed, token)) is { Offline: true, Total: 500 } && responses.Count == 3,
                    "Failed check lost the published feed");

                clock.Advance(TimeSpan.FromSeconds(59));
                Require((await Settle(feed, token)).Offline && responses.Count == 3, "Offline check bypassed the retry interval");
                clock.Advance(TimeSpan.FromSeconds(1));
                Require((await Settle(feed, token)) is { Offline: true, Total: 500 } && responses.Count == 4,
                    "Offline check did not retry at one minute or a malformed update replaced the saved feed");

                clock.Advance(TimeSpan.FromMinutes(1));
                var reconnected = await Settle(feed, token, new("studio coastal", 124));
                Require(responses.Count == 5 && !reconnected.Offline && reconnected.Total == 250 && reconnected.Page == 31 &&
                    reconnected.Items.Last().Name == "Theme 498 Reconnected", "Reconnect did not publish the updated inventory in place");
                clock.Advance(TimeSpan.FromSeconds(30));
                await feed.BrowseAsync(new(), true, token);
                Require((await Settle(feed, token)) is { Offline: true, Total: 500 } && responses.Count == 6,
                    "Oversized update replaced the saved feed");
            }
            var disconnected = new Handler((request, count, ct) => throw new HttpRequestException("Offline"));
            await using (var restarted = new MarketplaceThemeFeed(cache, disconnected, clock))
            {
                var offline = await Settle(restarted, token);
                Require(offline.Total == 500 && offline.Offline, "Last good feed lost on restart");
                var last = await restarted.BrowseAsync(new("reconnected"), false, token);
                Require(last.Items.Single().Name == "Theme 498 Reconnected", "Restart lost the reconnected publication");
            }
            await File.WriteAllTextAsync(Path.Combine(cache, "catalogue.json"), "{}", token);
            await using var recovered = new MarketplaceThemeFeed(cache, disconnected, clock);
            var fallback = await Settle(recovered, token);
            Require(fallback.Total == 3 && fallback.Offline, "Broken disk cache prevented bundled recovery");
        }
        finally { root.Delete(true); }
    }

    private static async Task<ThemeBrowsePage> Settle(MarketplaceThemeFeed feed, CancellationToken token, ThemeBrowseQuery? query = null)
    {
        while (true)
        {
            var page = await feed.BrowseAsync(query ?? new(), false, token);
            Require(MarketplaceValidation.SnapshotError(MarketplaceSnapshot.Unavailable with { Themes = page }) is null, "Invalid wire page");
            if (!page.Refreshing) return page;
            await Task.Delay(5, token);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan interval) => now += interval;
    }
    private sealed class Handler(Func<HttpRequestMessage,int,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, Interlocked.Increment(ref Count), token);
    }
}
