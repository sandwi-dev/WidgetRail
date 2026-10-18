using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.PackageDistribution;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeWidgetMarketplace
{
    private const long MaximumRetainedUpdateBytes = WidgetPackageLimits.MaximumRetainedUpdateBytes;
    // Leave room in the broker response for catalog metadata and item results.
    private const int MaximumBatchReviewBytes = 128 * 1024;
    private sealed record PendingBatch(IReadOnlyList<PendingPackage> Packages, DateTimeOffset Expires);
    private PendingBatch? _pendingBatch;
    private Task _cleanup = Task.CompletedTask;

    // Called under _gate. File disposal runs outside the control-plane lock;
    // the next job waits for it before reusing the download cache.
    private void ReleaseLater(IEnumerable<PendingPackage> packages)
    {
        var previous = _cleanup;
        var owned = packages.ToArray();
        _cleanup = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            foreach (var package in owned) await package.Download.DisposeAsync().ConfigureAwait(false);
        });
    }

    private void ValidateUpdateSelection(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            var item = _snapshot.Items.FirstOrDefault(item => item.Id == id);
            if (item is null || !_assets.TryGetValue(id, out var asset) ||
                !Version.TryParse(item.InstalledVersion, out var installed) || asset.Version <= installed)
                throw new PlatformDiagnosticsException("marketplace_refresh_required");
        }
    }

    private void PrepareBatch(string kind, IReadOnlyList<string>? ids)
    {
        if (kind == "download-updates")
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, UpdateBatch = new(ids!.Select(id =>
                new MarketplaceUpdateItem(id, _definitions.Single(item => item.Id == id).Name, _assets[id].Version.ToString(), "queued")).ToArray()) };
        else if (kind == "install-updates")
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, UpdateBatch = _snapshot.UpdateBatch! with
            {
                ReviewToken = null,
                Items = _snapshot.UpdateBatch!.Items.Select(item => item.State == "review"
                    ? item with { State = "queued", Review = null, Message = "Waiting to install." } : item).ToArray(),
            } };
    }

    private void ExpireBatchReview()
    {
        if (_pendingBatch is not { } expired || expired.Expires > _clock.GetUtcNow()) return;
        _pendingBatch = null; ReleaseLater(expired.Packages);
        const string message = "The update review expired. Download again to review current packages.";
        CompleteUnfinishedBatch("failed", message);
        SetOperation(_snapshot.Operation! with { State = "failed", Message = message });
    }

    private async Task DownloadBatchAsync(OwnedJob job, IReadOnlyList<string> ids)
    {
        using var releases = new GitHubReleaseClient(_http); // One checksum manifest fetch per release in this batch.
        var packages = new List<PendingPackage>();
        var transferred = false;
        long retained = 0;
        var retainedReviewBytes = 0;
        try
        {
            CleanDownloadCache(Path.Combine(_catalog.Root, ".marketplace-downloads"));
            foreach (var id in ids)
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                BeginBatchItem(job, id, "downloading", "Downloading and verifying…");
                PendingPackage? package = null;
                try
                {
                    ReleaseAsset asset;
                    lock (_gate) asset = _assets[id];
                    if (asset.Asset.Size > MaximumRetainedUpdateBytes - retained)
                    {
                        UpdateBatchItem(job, id, "failed", "This update exceeds the space reserved for this batch. Update it separately.");
                        continue;
                    }
                    package = await DownloadPackageAsync(job, id, cleanCache: false, releases).ConfigureAwait(false);
                    if (!package.Review.Compatible || package.Review.InstalledVersion is null)
                    {
                        UpdateBatchItem(job, id, "failed", package.Review.InstalledVersion is null
                            ? "This widget is no longer installed. Refresh the list." : package.Review.Compatibility);
                        continue;
                    }
                    if (package.Download.PackageStream.Length > MaximumRetainedUpdateBytes - retained)
                    {
                        UpdateBatchItem(job, id, "failed", "This update exceeds the space reserved for this batch. Update it separately.");
                        continue;
                    }
                    var reviewBytes = JsonSerializer.SerializeToUtf8Bytes(package.Review).Length;
                    if (reviewBytes > MaximumBatchReviewBytes - retainedReviewBytes)
                    {
                        UpdateBatchItem(job, id, "failed", "This update needs an individual review. Update it separately.");
                        continue;
                    }
                    retainedReviewBytes += reviewBytes;
                    retained += package.Download.PackageStream.Length;
                    packages.Add(package);
                    UpdateBatchItem(job, id, "review", "Verified and ready for review.", package.Review);
                    package = null;
                }
                catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { UpdateBatchItem(job, id, "failed", Friendly(error)); }
                finally { if (package is not null) await package.Download.DisposeAsync().ConfigureAwait(false); }
            }
            lock (_gate)
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                if (!Current(job)) return;
                if (packages.Count == 0)
                { Finish(job, "failed", "No updates are ready to install. Review the results and retry."); return; }
                _pendingBatch = new(packages.ToArray(), _clock.GetUtcNow().AddMinutes(10));
                transferred = true;
                _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, UpdateBatch = _snapshot.UpdateBatch! with
                    { ReviewToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) } };
                SetOperation(_snapshot.Operation! with { State = "review", PackageId = null, DownloadedBytes = 0, TotalBytes = null,
                    Message = $"{packages.Count} updates verified. Review before installing; nothing has been changed." });
            }
        }
        finally
        {
            if (!transferred) foreach (var package in packages) await package.Download.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task InstallBatchAsync(OwnedJob job, PendingBatch batch)
    {
        foreach (var package in batch.Packages)
        {
            job.Cancellation.Token.ThrowIfCancellationRequested();
            var id = package.Definition.Id;
            BeginBatchItem(job, id, "installing", "Installing the reviewed package…");
            try
            {
                var message = await InstallPackageAsync(job, package).ConfigureAwait(false);
                // A late cancellation cannot relabel a durable catalog commit.
                UpdateBatchItem(job, id, "completed", message);
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { UpdateBatchItem(job, id, "failed", Friendly(error)); }
        }
        lock (_gate)
        {
            if (!Current(job)) return;
            var items = _snapshot.UpdateBatch!.Items;
            var completed = items.Count(item => item.State == "completed");
            var failed = items.Count(item => item.State == "failed");
            var message = $"{completed} {(completed == 1 ? "widget" : "widgets")} updated" +
                (failed == 0 ? "." : $"; {failed} {(failed == 1 ? "needs" : "need")} attention. Review the results and retry.");
            _notification = (new(message), _clock.GetUtcNow().AddSeconds(30));
            Finish(job, completed == 0 ? "failed" : "completed", message);
        }
    }

    private void BeginBatchItem(OwnedJob job, string id, string state, string message)
    {
        lock (_gate)
        {
            if (!Current(job)) return;
            SetOperation(_snapshot.Operation! with { PackageId = id, DownloadedBytes = 0, TotalBytes = null, Message = message });
            UpdateBatchItem(job, id, state, message);
        }
    }

    private void UpdateBatchItem(OwnedJob job, string id, string state, string message, MarketplaceReview? review = null)
    {
        lock (_gate)
        {
            if (!Current(job)) return;
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, UpdateBatch = _snapshot.UpdateBatch! with
                { Items = _snapshot.UpdateBatch.Items.Select(item => item.PackageId == id ? item with { State = state, Message = message, Review = review } : item).ToArray() } };
        }
    }

    private void CompleteUnfinishedBatch(string state, string message)
    {
        if (_snapshot.UpdateBatch is not { } batch) return;
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, UpdateBatch = batch with { ReviewToken = null,
            Items = batch.Items.Select(item => item.State is "queued" or "downloading" or "review" or "installing"
                ? item with { State = state, Message = message, Review = null } : item).ToArray() } };
    }

    private void FinishFailedJob(OwnedJob job, string state, string message)
    {
        lock (_gate)
        {
            if (!Current(job)) return;
            if (job.Kind is "download-updates" or "install-updates") CompleteUnfinishedBatch(state, message);
            Finish(job, state, message);
        }
    }
}
