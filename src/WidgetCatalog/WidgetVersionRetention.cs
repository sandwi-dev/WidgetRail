using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

public sealed partial class WidgetCatalog
{
    // A maintenance receipt is not executable authority. It survives a crash
    // between package publication, selection and cleanup without changing the
    // existing catalog-state format used by installed hosts and developer tools.
    private sealed record RetentionReceipt(string Id, string Incoming, string? Previous);
    private string RetentionRoot => Path.Combine(_root, "retention");
    private string RetentionPath(string id) => Path.Combine(RetentionRoot,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");

    private InstalledWidgetVersion[] RetainedBeforeInstall(IReadOnlyList<InstalledWidgetVersion> versions, Version? selected)
    {
        var limit = Math.Min(_options.RetainedVersionsPerWidget, _options.MaximumVersionsPerWidget);
        return versions.OrderByDescending(item => item.Version == selected).ThenByDescending(item => item.Version).Take(limit).ToArray();
    }

    private async Task PrepareRetentionAsync(InstalledWidgetVersion incoming, CancellationToken token)
    {
        var all = DiscoverInstalledVersions(token);
        var versions = all.Where(item => item.Id == incoming.Id).ToArray();
        var state = await _stateStore.LoadAsync(token).ConfigureAwait(false);
        var saved = state.Widgets.SingleOrDefault(item => item.Id == incoming.Id)?.ActiveVersion;
        var selected = saved is null ? versions.OrderByDescending(item => item.Version).FirstOrDefault()?.Version : Version.Parse(saved);
        var retained = RetainedBeforeInstall(versions, selected);
        // This hook runs after archive extraction/sealing and policy approval.
        // Keep both the selected version and its existing backup until commit.
        foreach (var obsolete in versions.Except(retained))
        {
            token.ThrowIfCancellationRequested();
            await TryRetireVersionAsync(obsolete.Id, obsolete.Version).ConfigureAwait(false);
        }
        var remaining = all.Where(item => Directory.Exists(item.InstallPath)).ToArray();
        EnforceProspectiveInstallLimits(remaining, remaining.Where(item => item.Id == incoming.Id).ToArray(),
            new(incoming.Id, incoming.Version, incoming.Manifest, incoming.VerifiedEntryCount - 1,
                incoming.VerifiedTotalBytes - InstalledPackageIntegrity.MaximumMetadataBytes), selected, projectRetention: false);
        await WriteRetentionReceiptAsync(new(incoming.Id, incoming.Version.ToString(), selected?.ToString()), token).ConfigureAwait(false);
    }

    private async Task WriteRetentionReceiptAsync(RetentionReceipt receipt, CancellationToken token)
    {
        Directory.CreateDirectory(RetentionRoot);
        FileSystemSafety.EnsureNoReparsePoints(_root, RetentionRoot);
        var path = RetentionPath(receipt.Id);
        FileSystemSafety.EnsureNoReparsePoints(_root, path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(receipt), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<InstalledWidgetVersion> FinishRetentionAsync(InstalledWidgetVersion installed)
    {
        var pending = !await RecoverRetentionAsync(installed.Id).ConfigureAwait(false);
        return installed with { RetentionCleanupPending = pending };
    }

    private async Task<bool> RecoverRetentionAsync(string? onlyId = null)
    {
        if (!Directory.Exists(RetentionRoot)) return true;
        var complete = true;
        try
        {
            FileSystemSafety.EnsureNoReparsePoints(_root, RetentionRoot);
            var state = await _stateStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var receipts = onlyId is not null ? new[] { RetentionPath(onlyId) } :
                Directory.EnumerateFiles(RetentionRoot, "*.json").Take(_options.MaximumInstalledWidgetIds).ToArray();
            foreach (var path in receipts)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    FileSystemSafety.EnsureNoReparsePoints(_root, path);
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    var receipt = JsonSerializer.Deserialize<RetentionReceipt>(BoundedFileReader.ReadAll(input, 4096));
                    if (receipt is null || !WidgetManifestValidator.IsValidPackageIdentity(receipt.Id) ||
                        !string.Equals(path, RetentionPath(receipt.Id), StringComparison.OrdinalIgnoreCase) ||
                        !Version.TryParse(receipt.Incoming, out var incoming) || incoming.ToString() != receipt.Incoming ||
                        receipt.Previous is { } prior && (!Version.TryParse(prior, out var previous) || previous.ToString() != prior))
                    { complete = false; continue; }
                    var published = Path.Combine(_packagesRoot, receipt.Id, receipt.Incoming);
                    if (Directory.Exists(published))
                    {
                        var selected = state.Widgets.SingleOrDefault(item => item.Id == receipt.Id)?.ActiveVersion ?? receipt.Incoming;
                        var versions = DiscoverRepairDirectories(CancellationToken.None).Where(item => item.Id == receipt.Id).ToArray();
                        var protectedVersions = new HashSet<string>(StringComparer.Ordinal) { selected, receipt.Incoming };
                        if (receipt.Previous is not null) protectedVersions.Add(receipt.Previous);
                        var kept = versions.OrderByDescending(item => protectedVersions.Contains(item.Version.ToString()))
                            .ThenByDescending(item => item.Version).Take(Math.Max(protectedVersions.Count, _options.RetainedVersionsPerWidget))
                            .Select(item => item.Version).ToHashSet();
                        var retired = true;
                        foreach (var obsolete in versions.Where(item => !kept.Contains(item.Version)))
                            retired &= await TryRetireVersionAsync(obsolete.Id, obsolete.Version).ConfigureAwait(false);
                        if (!retired) { complete = false; continue; }
                    }
                    // Unpublished receipt: old selection and its backup were kept.
                    File.Delete(path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or WidgetPackageException or JsonException or InvalidDataException)
                { complete = false; }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or WidgetPackageException or JsonException or InvalidDataException)
        { complete = false; }
        return complete;
    }

    private async Task<bool> TryRetireVersionAsync(string id, Version version)
    {
        var source = Path.GetFullPath(Path.Combine(_packagesRoot, id, version.ToString()));
        if (!WidgetManifestValidator.IsValidPackageIdentity(id) || !FileSystemSafety.IsWithin(_packagesRoot, source)) return false;
        try
        {
            if (!Directory.Exists(source)) return true;
            FileSystemSafety.EnsureTreeContainsNoReparsePoints(_root, source, _options.MaximumInstalledEntries);
            var staging = Path.Combine(_root, "staging");
            Directory.CreateDirectory(staging);
            FileSystemSafety.EnsureNoReparsePoints(_root, staging);
            var retired = Path.Combine(staging, ".uninstall-version-" + Guid.NewGuid().ToString("N"));
            Directory.Move(source, retired);
            // Failure here is recoverable through the existing retired-tree sweep.
            return await TryDeleteRetiredTreeAsync(retired).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or WidgetPackageException)
        { return false; }
    }
}
