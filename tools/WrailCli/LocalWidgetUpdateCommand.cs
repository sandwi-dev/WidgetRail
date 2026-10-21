using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WrailCli;

internal static class LocalWidgetUpdateCommand
{
    internal static async Task<int> RunAsync(CommandArguments parsed, TextWriter output, CancellationToken token)
    {
        if (new[] { "--repo", "--tag", "--asset" }.Any(option => parsed.Option(option) is not null))
            throw new CliUsageException("Use --file for a local package or --repo for a release, not both.");
        var id = parsed.Positionals.Single();
        var root = CatalogPath.Resolve(parsed.Option("--catalog"));
        var catalog = new WidgetCatalog.WidgetCatalog(root);
        var current = (await catalog.DiscoverAsync(token)).Widgets.SingleOrDefault(item => item.Id == id)
            ?? throw new CliUsageException("The requested widget is not installed.");
        var path = Path.GetFullPath(parsed.Option("--file")!);
        if (!Path.GetExtension(path).Equals(".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Choose a .wrwidget file.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var expected = PackageIntegrity.ParseExpectedSha256(parsed.Option("--sha256"));
        var digest = await SHA256.HashDataAsync(stream, token);
        if (expected is not null && !CryptographicOperations.FixedTimeEquals(expected, digest))
            throw new CliOperationException("The package does not match the reviewed SHA-256. Nothing was installed.");
        var next = await catalog.CreateInstaller().ValidateAsync(stream, token);
        if (next.Id != id || next.Manifest.Publisher != current.ActiveVersion.Manifest.Publisher ||
            WidgetManifestTrust.Resolve(next.Manifest) != WidgetManifestTrust.Resolve(current.ActiveVersion.Manifest))
            throw new CliOperationException("The update must have the same widget ID, publisher and execution model.");
        if (next.Version <= current.ActiveVersion.Version || !WidgetHostCompatibility.Evaluate(next.Manifest).IsSupported)
            throw new CliOperationException("Choose a newer version compatible with this WidgetRail host.");
        var fullTrust = WidgetManifestTrust.Resolve(next.Manifest) == WidgetExecutionTrust.FullTrustCurrentUser;
        await output.WriteLineAsync($"{id}: {current.ActiveVersion.Version} -> {next.Version}\nSHA-256: {Convert.ToHexString(digest).ToLowerInvariant()}");
        await output.WriteLineAsync($"Execution: {WidgetManifestTrust.Resolve(next.Manifest)}\nRequired permissions: {string.Join(", ", next.Manifest.Permissions)}\nOptional permissions: {string.Join(", ", next.Manifest.OptionalPermissions)}");
        await output.WriteLineAsync("Widget settings will be kept, including saved app references. Existing permission decisions are kept for matching permissions; new permissions need approval. Credentials are not transferred.");
        if (!parsed.HasFlag("--apply"))
        {
            static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            await output.WriteLineAsync($"Review only; nothing was installed. Apply the reviewed package:\nwrail update {id} --file {Quote(path)} --sha256 {Convert.ToHexString(digest).ToLowerInvariant()} --catalog {Quote(root)} --apply" +
                (fullTrust ? " --accept-full-trust" : "") + (parsed.Option("--settings-root") is { } profile ? " --settings-root " + Quote(profile) : ""));
            return 0;
        }
        if (expected is null) throw new CliUsageException("Review first; --apply requires --sha256 from the review.");
        if (current.Enabled) throw new CliOperationException($"Disable {id} before applying this CLI update, or use Marketplace/Settings to update it while enabled.");
        if (fullTrust && !parsed.HasFlag("--accept-full-trust")) throw new CliUsageException("Review full access and supply --accept-full-trust.");
        if (fullTrust) await InstallCommand.WriteFullTrustDisclosureAsync(output, id, next.Version.ToString());
        var target = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        using var settings = new CliWidgetSettingsTransfer(CliWidgetSettingsTransfer.ResolveRoot(parsed, root));
        var installed = await catalog.UpdateFromFileAsync(stream, target,
            fullTrust ? WidgetPackageTrustApproval.FullTrustCurrentUser : WidgetPackageTrustApproval.None,
            async (_, ct) =>
            {
                var latest = (await catalog.DiscoverAsync(ct)).Widgets.SingleOrDefault(item => item.Id == id);
                if (latest is null || latest.Enabled || latest.ActiveVersion.ContentDigest != current.ActiveVersion.ContentDigest)
                    throw new CliOperationException("The selected widget changed. Review the update again.");
            }, token, settings.PrepareAsync);
        await output.WriteLineAsync($"Selected {id} {installed.Version} (disabled). Widget settings were kept. Enable the widget when ready.");
        if (installed.RetentionCleanupPending) await output.WriteLineAsync("Older files are still in use. Cleanup will retry on the next catalog change.");
        return 0;
    }
}
