using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.PackageDistribution;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

internal sealed record CuratedWidget(string Id, string Publisher, string Name, string Description, IReadOnlyList<string> Screenshots);

/// <summary>
/// Shared trusted-UI jobs. Downloads and mutations never execute in the short
/// diagnostics request. Review tokens name exact write-locked package streams.
/// </summary>
internal sealed partial class BridgeWidgetMarketplace : IAsyncDisposable
{
    internal const string Repository = "sandwi-dev/WidgetRail";
    private readonly object _gate = new();
    private readonly CatalogService _catalog;
    private readonly string _stateRoot;
    private readonly BridgeClientRegistry? _registry;
    private readonly Func<CancellationToken, Task> _reload;
    private readonly IReadOnlyList<CuratedWidget> _definitions;
    private readonly HttpMessageHandler? _http;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, ReleaseAsset> _assets = new(StringComparer.Ordinal);
    private MarketplaceSnapshot _snapshot;
    private OwnedJob? _job;
    private PendingPackage? _pending;
    private (PlatformWidgetPackageNotification Value, DateTimeOffset Expires)? _notification;
    private bool _disposed;

    internal BridgeWidgetMarketplace(CatalogService catalog, Func<CancellationToken, Task> reload,
        IReadOnlyList<CuratedWidget>? definitions = null, HttpMessageHandler? http = null, TimeProvider? clock = null, string? stateRoot = null, BridgeClientRegistry? registry = null)
    {
        _stateRoot = stateRoot ?? Path.Combine(catalog.Root, "widget-state");
        _registry = registry;
        _catalog = catalog; _reload = reload; _http = http; _clock = clock ?? TimeProvider.System;
        if (definitions is null)
        {
            using var stream = typeof(BridgeWidgetMarketplace).Assembly.GetManifestResourceStream("WidgetRail.Marketplace.catalog.json")!;
            definitions = JsonSerializer.Deserialize<CuratedWidget[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        _definitions = definitions;
        _snapshot = new(0, definitions.Select(d => new MarketplaceItem(d.Id, d.Name, d.Description, d.Screenshots)).ToArray());
        MarketplaceContract.ValidateSnapshot(_snapshot);
    }

    internal ValueTask<MarketplaceSnapshot> HandleAsync(MarketplaceCommand command, CancellationToken token)
    {
        MarketplaceContract.ValidateCommand(command); token.ThrowIfCancellationRequested();
        MarketplaceSnapshot result;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending is { } expired && expired.Expires <= _clock.GetUtcNow())
            {
                ReleaseLater([expired]); _pending = null;
                SetOperation(_snapshot.Operation! with { State = "failed", Review = null, Message = "The package review expired. Download it again to review current bytes." });
            }
            ExpireBatchReview();
            if (command.Action is "refresh" or "inventory" or "download" or "download-updates")
            {
                if (_snapshot.Operation?.State != "running" && _pending is null && _pendingBatch is null)
                {
                    if (command.Action == "download" && !_assets.ContainsKey(command.PackageId!))
                        throw new PlatformDiagnosticsException("marketplace_refresh_required");
                    if (command.Action == "download-updates") ValidateUpdateSelection(command.PackageIds!);
                    Start(command.Action, command.PackageId, ids: command.PackageIds);
                }
            }
            else if (command.Action == "cancel" && _snapshot.Operation is { } active && active.Id == command.OperationId)
            {
                if (active.State == "running")
                {
                    // Cancellation can complete the async job inline. Publish
                    // the request first so it cannot overwrite a terminal result.
                    SetOperation(active with { Message = "Cancelling…" });
                    _job?.Cancellation.Cancel();
                }
                else if (active.State == "review")
                {
                    if (_pending is { } pending) { ReleaseLater([pending]); _pending = null; }
                    if (_pendingBatch is { } batch) { ReleaseLater(batch.Packages); _pendingBatch = null; CompleteUnfinishedBatch("cancelled", "Download discarded."); }
                    SetOperation(active with { State = "cancelled", Review = null, Message = "Download discarded." });
                }
            }
            else if (command.Action == "install")
            {
                if (_snapshot.Operation is not { State: "review" } review || review.Id != command.OperationId ||
                    _pending is not { } pending || pending.Review.Token != command.ReviewToken || !pending.Review.Compatible ||
                    pending.Review.FullTrust && !command.ApproveFullTrust)
                    throw new PlatformDiagnosticsException("marketplace_review_required");
                _pending = null;
                Start("install", pending.Definition.Id, pending);
            }
            else if (command.Action == "install-updates")
            {
                if (_snapshot.Operation is not { Kind: "download-updates", State: "review" } review || review.Id != command.OperationId ||
                    _pendingBatch is not { } batch || _snapshot.UpdateBatch?.ReviewToken != command.ReviewToken ||
                    batch.Packages.Any(package => package.Review.FullTrust) && !command.ApproveFullTrust)
                    throw new PlatformDiagnosticsException("marketplace_review_required");
                _pendingBatch = null;
                Start("install-updates", null, batch: batch);
            }
            result = _snapshot;
        }
        // Retiring review authority is immediate. File cleanup belongs to the
        // job lifetime, never the short status/command acknowledgement.
        return ValueTask.FromResult(result);
    }

    private void Start(string kind, string? id, PendingPackage? package = null, IReadOnlyList<string>? ids = null, PendingBatch? batch = null)
    {
        ids = ids?.ToArray();
        var previous = _job?.Task ?? Task.CompletedTask;
        var cleanup = _cleanup;
        var job = new OwnedJob(Guid.NewGuid().ToString("N"), kind, id, CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
        _job = job;
        PrepareBatch(kind, ids);
        SetOperation(new(job.Id, kind, "running", id, Message: kind switch
        { "refresh" => "Checking available widgets…", "inventory" => "Refreshing installed versions…", "download" or "download-updates" => "Downloading for review…", _ => "Installing reviewed packages…" }));
        // A new job cannot clean cache files still owned by the preceding job's
        // disposal or by an expired/cancelled review.
        job.Task = Task.Run(() => RunAsync(job, package, ids, batch, previous, cleanup));
    }

    private async Task RunAsync(OwnedJob job, PendingPackage? package, IReadOnlyList<string>? ids, PendingBatch? batch, Task previous, Task cleanup)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await cleanup.ConfigureAwait(false);
            job.Cancellation.Token.ThrowIfCancellationRequested();
            if (job.Kind == "refresh") await RefreshAsync(job).ConfigureAwait(false);
            else if (job.Kind == "inventory")
            {
                UpdateInstalled((await _catalog.DiscoverAsync(job.Cancellation.Token).ConfigureAwait(false)).Widgets);
                Finish(job, "completed", "Installed versions refreshed.");
            }
            else if (job.Kind == "download") await DownloadAsync(job).ConfigureAwait(false);
            else if (job.Kind == "download-updates") await DownloadBatchAsync(job, ids!).ConfigureAwait(false);
            else if (job.Kind == "install-updates") await InstallBatchAsync(job, batch!).ConfigureAwait(false);
            else
            {
                var message = await InstallPackageAsync(job, package!).ConfigureAwait(false);
                lock (_gate) _notification = (new(message), _clock.GetUtcNow().AddSeconds(30));
                Finish(job, "completed", message);
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        { FinishFailedJob(job, "cancelled", "Operation cancelled. Any version already saved remains available in Widgets."); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { FinishFailedJob(job, "failed", Friendly(error)); }
        finally
        {
            if (package is not null) await package.Download.DisposeAsync().ConfigureAwait(false);
            if (batch is not null) foreach (var item in batch.Packages) await item.Download.DisposeAsync().ConfigureAwait(false);
            job.Cancellation.Dispose();
        }
    }

    private async Task RefreshAsync(OwnedJob job)
    {
        var installed = await _catalog.DiscoverAsync(job.Cancellation.Token).ConfigureAwait(false);
        UpdateInstalled(installed.Widgets);
        using var client = new GitHubReleaseClient(_http);
        var releases = await client.ListAsync(Repository, 1, job.Cancellation.Token).ConfigureAwait(false);
        var selected = new Dictionary<string, ReleaseAsset>(StringComparer.Ordinal);
        foreach (var definition in _definitions)
        {
            var prefix = definition.Id + "-";
            var candidates = releases.SelectMany(release => release.Assets
                .Where(asset => asset.Name.StartsWith(prefix, StringComparison.Ordinal) && asset.Name.EndsWith(".wrwidget", StringComparison.Ordinal))
                .Select(asset => new { release, asset, version = asset.Name[prefix.Length..^9] }))
                .Where(item => Version.TryParse(item.version, out _))
                .Select(item => new ReleaseAsset(item.release, item.asset, Version.Parse(item.version)))
                .OrderByDescending(item => item.Version).ToArray();
            if (candidates.Length != 0) selected.Add(definition.Id, candidates[0]);
        }
        lock (_gate)
        {
            if (!Current(job)) return;
            job.Cancellation.Token.ThrowIfCancellationRequested();
            _assets.Clear(); foreach (var pair in selected) _assets.Add(pair.Key, pair.Value);
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, CheckedAtUtc = _clock.GetUtcNow(),
                Items = _snapshot.Items.Select(item => selected.TryGetValue(item.Id, out var asset)
                    ? item with { AvailableVersion = asset.Version.ToString(), DownloadBytes = asset.Asset.Size, IsPrerelease = asset.Release.Prerelease,
                        AvailableVersionSaved = installed.Widgets.FirstOrDefault(w => w.Id == item.Id)?.Versions.Any(v => v.Version == asset.Version) == true }
                    : item with { AvailableVersion = null, DownloadBytes = null, IsPrerelease = false, AvailableVersionSaved = false }).ToArray() };
            Finish(job, "completed", "Marketplace refreshed. Download a widget to check compatibility and review its permissions.");
        }
    }

    private async Task DownloadAsync(OwnedJob job)
    {
        var package = await DownloadPackageAsync(job, job.PackageId!, cleanCache: true).ConfigureAwait(false);
        lock (_gate)
        {
            if (Current(job) && !job.Cancellation.IsCancellationRequested)
            {
                _pending = package;
                SetOperation(_snapshot.Operation! with { State = "review", Review = package.Review,
                    Message = "Download verified. Review the package before installing; nothing has been installed." });
                return;
            }
        }
        await package.Download.DisposeAsync().ConfigureAwait(false);
        job.Cancellation.Token.ThrowIfCancellationRequested();
    }

    private async Task<PendingPackage> DownloadPackageAsync(OwnedJob job, string packageId, bool cleanCache)
    {
        ReleaseAsset asset;
        lock (_gate) asset = _assets[packageId];
        var definition = _definitions.Single(d => d.Id == packageId);
        var source = new GitHubPackageSource(Repository, asset.Release.Tag, asset.Asset.Name);
        using var releases = new GitHubReleaseClient(_http);
        var hash = await releases.ResolveDigestAsync(source, asset.Release, job.Cancellation.Token).ConfigureAwait(false);
        var downloadRoot = Path.Combine(_catalog.Root, ".marketplace-downloads");
        if (cleanCache) CleanDownloadCache(downloadRoot);
        RemotePackageDownloader.DownloadedPackage? downloaded = null;
        try
        {
            using (var downloader = new RemotePackageDownloader(_http, new() { TemporaryDirectoryRoot = downloadRoot }))
                downloaded = await downloader.DownloadAsync(RemotePackageSource.Resolve(source.ToString())!, hash, job.Cancellation.Token,
                    progress: (received, total) =>
                    {
                        lock (_gate) if (Current(job))
                            SetOperation(_snapshot.Operation! with { DownloadedBytes = received, TotalBytes = total });
                    }).ConfigureAwait(false);
            var inspection = await _catalog.CreateInstaller().ValidateAsync(downloaded.PackageStream, job.Cancellation.Token).ConfigureAwait(false);
            if (inspection.Id != definition.Id || inspection.Manifest.Publisher != definition.Publisher || inspection.Version != asset.Version)
                throw new WidgetPackageException("marketplace_identity_changed", "The package does not match its marketplace listing.");
            var installed = (await _catalog.DiscoverAsync(job.Cancellation.Token).ConfigureAwait(false)).Widgets.SingleOrDefault(w => w.Id == definition.Id);
            var compatibility = WidgetHostCompatibility.Evaluate(inspection.Manifest);
            var required = inspection.Manifest.Permissions.ToArray();
            var optional = inspection.Manifest.OptionalPermissions.ToArray();
            var compatible = compatibility.IsSupported && required.All(c => PlatformCapabilities.TryGet(c, out _));
            var explanation = compatible ? "Compatible with this WidgetRail host and architecture." : compatibility.IsSupported
                ? "This widget requires host capabilities that are not available in this build." : compatibility.Message;
            if (installed is not null && (installed.ActiveVersion.Manifest.Publisher != definition.Publisher ||
                WidgetManifestTrust.Resolve(installed.ActiveVersion.Manifest) != WidgetManifestTrust.Resolve(inspection.Manifest)))
            { compatible = false; explanation = "The installed widget has a different publisher or execution model. Manage it in Widgets first."; }
            if (installed is not null && (inspection.Version <= installed.ActiveVersion.Version || installed.Versions.Any(v => v.Version == inspection.Version)))
            { compatible = false; explanation = "This version is already saved, or an installed version is newer. Use Widgets to manage existing versions."; }
            var previous = installed?.ActiveVersion.Manifest;
            var known = (previous?.Permissions ?? []).Concat(previous?.OptionalPermissions ?? []).ToHashSet(StringComparer.Ordinal);
            var review = new MarketplaceReview(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), definition.Id, definition.Name,
                inspection.Version.ToString(), installed?.ActiveVersion.Version.ToString(),
                WidgetManifestTrust.Resolve(inspection.Manifest) == WidgetExecutionTrust.FullTrustCurrentUser,
                compatible, explanation, required, optional, required.Concat(optional).Where(p => !known.Contains(p)).ToArray(),
                Convert.ToHexString(downloaded.Sha256));
            job.Cancellation.Token.ThrowIfCancellationRequested();
            var pending = new PendingPackage(definition, downloaded, review, source, installed?.ActiveVersion.ContentDigest, _clock.GetUtcNow().AddMinutes(10));
            downloaded = null;
            return pending;
        }
        finally { if (downloaded is not null) await downloaded.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task<string> InstallPackageAsync(OwnedJob job, PendingPackage package)
    {
        var approval = package.Review.FullTrust ? WidgetPackageTrustApproval.FullTrustCurrentUser : WidgetPackageTrustApproval.None;
        async Task AdmitReviewedBaseline(WidgetPackageInspection _, CancellationToken token)
        {
            // Invoked under the catalog mutation lock. Permission deltas and
            // version review cannot be reused after another actor changes the
            // selected installation, even if the incoming version is newer.
            var current = (await _catalog.DiscoverAsync(token).ConfigureAwait(false)).Widgets.SingleOrDefault(w => w.Id == package.Definition.Id);
            if (current?.ActiveVersion.Version.ToString() != package.Review.InstalledVersion ||
                current?.ActiveVersion.ContentDigest != package.InstalledDigest)
                throw new WidgetPackageException("marketplace_review_changed", "The reviewed installation changed.");
        }
        await using var settings = new BridgeWidgetSettingsTransfer(_stateRoot, _registry, _reload);
        InstalledWidgetVersion installed;
        if (package.Review.InstalledVersion is not null)
        {
            var idHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package.Definition.Id)));
            installed = await _catalog.UpdateFromFileAsync(package.Download.PackageStream, idHash, approval,
                AdmitReviewedBaseline, job.Cancellation.Token, settings.PrepareAsync).ConfigureAwait(false);
        }
        else installed = await _catalog.InstallReviewedAsync(package.Download.PackageStream, AdmitReviewedBaseline, job.Cancellation.Token, approval).ConfigureAwait(false);
        lock (_gate) _snapshot = _snapshot with { Revision = _snapshot.Revision + 1,
            Items = _snapshot.Items.Select(item => item.Id == installed.Id
                ? item with { InstalledVersion = installed.Version.ToString() } : item).ToArray() };
        // A durable publication is never relabelled cancelled by a late request.
        var message = package.Review.InstalledVersion is null
            ? "Widget installed and enabled. Permissions will be requested when needed."
            : "Widget updated. Your widget settings and enabled state were kept. Permissions are requested when needed.";
        using var sourceWarning = new StringWriter();
        await PackageSources.SaveAsync(_catalog.Root, "widget", new(installed.Id, installed.Version.ToString(), installed.Manifest.Publisher,
            package.Source.ToString(), package.Review.Sha256, installed.ContentDigest), sourceWarning, CancellationToken.None).ConfigureAwait(false);
        if (sourceWarning.GetStringBuilder().Length != 0)
            message += " Its CLI update-source metadata could not be saved; marketplace updates remain available.";
        try
        {
            await settings.ReloadAsync().ConfigureAwait(false);
            UpdateInstalled((await _catalog.DiscoverAsync(CancellationToken.None).ConfigureAwait(false)).Widgets);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { message = "The package was installed, but the widget list could not refresh. Open Widgets in Settings and refresh."; }
        return message;
    }

    private void UpdateInstalled(IReadOnlyList<CatalogWidget> widgets)
    {
        lock (_gate) _snapshot = _snapshot with { Revision = _snapshot.Revision + 1,
            Items = _snapshot.Items.Select(item => item with
            { InstalledVersion = widgets.FirstOrDefault(w => w.Id == item.Id)?.ActiveVersion.Version.ToString(),
                AvailableVersionSaved = Version.TryParse(item.AvailableVersion, out var available) &&
                    widgets.FirstOrDefault(w => w.Id == item.Id)?.Versions.Any(v => v.Version == available) == true }).ToArray() };
    }
    internal PlatformWidgetPackageNotification TakeNotification()
    {
        lock (_gate)
        {
            var notification = _notification; _notification = null;
            return notification is { } value && value.Expires > _clock.GetUtcNow() ? value.Value : PlatformWidgetPackageNotification.Empty;
        }
    }
    private bool Current(OwnedJob job) => !_disposed && ReferenceEquals(_job, job);
    private void SetOperation(MarketplaceOperation operation) => _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Operation = operation };
    private void Finish(OwnedJob job, string state, string message)
    { lock (_gate) if (Current(job)) SetOperation(_snapshot.Operation! with { State = state, Review = null, Message = message }); }

    private static string Friendly(Exception error) => error switch
    {
        DistributionOperationException e when e.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => "GitHub's request limit was reached. Try again later.",
        DistributionOperationException e when e.Message.Contains("SHA-256", StringComparison.Ordinal) => "The download failed its SHA-256 check. Refresh the marketplace before retrying.",
        DistributionOperationException e when e.Message.Contains("download limit", StringComparison.Ordinal) => "This package exceeds the marketplace download limit. Obtain a smaller compatible package from its author.",
        DistributionUsageException => "This release has no usable checksum or package source. It cannot be installed from the marketplace.",
        DistributionOperationException => "The download or release check failed. Check your connection and try again.",
        WidgetPackageException { Code: "update_selection_failed" } => "The new version was saved. Open Widgets → Versions to finish reviewing it.",
        WidgetPackageException { Code: "install_activation_failed" } => "The widget was saved, but could not be enabled. Open Widgets in Settings to finish installation.",
        WidgetPackageException { Code: "package_incompatible" } => "This widget is not compatible with this WidgetRail version.",
        WidgetPackageException { Code: "update_not_newer" } => "The installed version changed. Refresh the marketplace and review again.",
        WidgetPackageException { Code: "marketplace_review_changed" } => "The selected widget version changed after review. Refresh and download again to review its current permissions.",
        WidgetPackageException => "The package could not be installed or validated. Review its compatibility and your installed versions.",
        _ => "The operation could not complete. Check available disk space and try again.",
    };

    private static void CleanDownloadCache(string root)
    {
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe cache.");
        foreach (var directory in Directory.EnumerateDirectories(root).Take(65))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var file = Path.Combine(directory, "package.wrwidget");
            try
            {
                if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (Directory.EnumerateDirectories(root).Take(4).Count() >= 4) throw new IOException("Download cache is in use.");
    }

    public async ValueTask DisposeAsync()
    {
        OwnedJob? job; Task cleanup;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; job = _job;
            if (_pending is { } pending) { ReleaseLater([pending]); _pending = null; }
            if (_pendingBatch is { } batch) { ReleaseLater(batch.Packages); _pendingBatch = null; }
            cleanup = _cleanup;
        }
        _lifetime.Cancel();
        if (job is not null) await job.Task.ConfigureAwait(false);
        await cleanup.ConfigureAwait(false);
        _lifetime.Dispose();
    }
    private sealed record ReleaseAsset(GitHubRelease Release, GitHubAsset Asset, Version Version);
    private sealed record PendingPackage(CuratedWidget Definition, RemotePackageDownloader.DownloadedPackage Download, MarketplaceReview Review, GitHubPackageSource Source, string? InstalledDigest, DateTimeOffset Expires);
    private sealed class OwnedJob(string id, string kind, string? packageId, CancellationTokenSource cancellation)
    {
        internal string Id { get; } = id;
        internal string Kind { get; } = kind;
        internal string? PackageId { get; } = packageId;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal Task Task { get; set; } = Task.CompletedTask;
    }
}
