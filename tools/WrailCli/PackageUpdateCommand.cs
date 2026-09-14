using WidgetRail.PlatformSettings;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

namespace WidgetRail.WrailCli;

internal static class PackageUpdateCommand
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output, HttpMessageHandler? handler, CancellationToken token, bool theme)
    {
        var parsed = new CommandArguments(args,
            theme ? ["--repo", "--tag", "--asset", "--sha256", "--version", "--settings-root"]
                : ["--repo", "--tag", "--asset", "--sha256", "--catalog"],
            theme ? ["--apply"] : ["--apply", "--accept-full-trust"]);
        if (parsed.Positionals.Count != 1) throw new CliUsageException("Supply one installed package ID. Run wrail help for update options.");
        var id = parsed.Positionals[0];
        var apply = parsed.HasFlag("--apply");
        var expected = PackageIntegrity.ParseExpectedSha256(parsed.Option("--sha256"));
        if (apply && (expected is null || parsed.Option("--tag") is null || parsed.Option("--asset") is null))
            throw new CliUsageException("Review the update first. --apply requires the reviewed --tag, --asset and --sha256.");
        var root = theme ? ThemeSettingsRoot.Resolve(parsed.Option("--settings-root")) : CatalogPath.Resolve(parsed.Option("--catalog"));
        var extension = theme ? ".wrtheme" : ".wrwidget";
        CatalogService? catalog = null;
        CatalogWidget? widget = null;
        ThemePackageInspection? previousTheme = null;
        string version, publisher, digest;
        if (theme)
        {
            var installed = new ThemeCatalog(new PlatformSettingsPaths(root)).Discover().Themes
                .Where(entry => entry.Descriptor.Id == id && !entry.Descriptor.IsBuiltIn && entry.IsValid)
                .OrderByDescending(entry => entry.Descriptor.Version).ToArray();
            var selected = parsed.Option("--version") is { } requested
                ? installed.SingleOrDefault(entry => entry.CatalogVersion == requested) : installed.FirstOrDefault();
            if (selected is null) throw new CliUsageException("The requested installed theme version was not found.");
            previousTheme = await ThemePackage.InspectSourceAsync(Path.Combine(root, "themes", selected.CatalogId, selected.CatalogVersion), token);
            version = previousTheme.Manifest.Version;
            publisher = previousTheme.Manifest.Publisher!;
            digest = PackageSources.ThemeDigest(previousTheme);
        }
        else
        {
            catalog = new CatalogService(root);
            widget = (await catalog.DiscoverAsync(token)).Widgets.SingleOrDefault(item => item.Id == id)
                ?? throw new CliUsageException("The requested widget is not installed.");
            version = widget.ActiveVersion.Version.ToString();
            publisher = widget.ActiveVersion.Manifest.Publisher;
            digest = widget.ActiveVersion.ContentDigest;
        }
        var remembered = parsed.Option("--repo") is null ? PackageSources.Find(root, theme ? "theme" : "widget", id, version, digest) : null;
        var oldSource = remembered is null ? null : GitHubPackageSource.Parse(remembered.Source, extension);
        var repository = GitHubPackageSource.ValidateRepository(parsed.Option("--repo") ?? oldSource?.Repository
            ?? throw new CliUsageException("No matching GitHub source is recorded. Supply --repo <owner/repository> and choose an asset with wrail releases."));
        GitHubPackageSource source;
        if (apply)
            source = GitHubPackageSource.Parse($"github:{repository}@{parsed.Option("--tag")}/{parsed.Option("--asset")}", extension);
        else
        {
            using var releases = new GitHubReleaseClient(handler);
            var release = await releases.GetReleaseAsync(repository, parsed.Option("--tag"), token);
            var candidates = release.Assets.Where(asset => asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).ToArray();
            GitHubAsset? chosen;
            if (parsed.Option("--asset") is { } assetName)
                chosen = candidates.SingleOrDefault(asset => asset.Name == assetName);
            else
            {
                chosen = candidates.SingleOrDefault(asset => asset.Name == oldSource?.Asset);
                if (chosen is null)
                {
                    var matching = candidates.Where(asset => asset.Name.StartsWith(id + "-", StringComparison.Ordinal)).ToArray();
                    if (matching.Length == 1) chosen = matching[0];
                }
            }
            if (chosen is null) throw new CliUsageException("Choose the package with --asset <filename>. Run wrail releases to see available assets.");
            source = GitHubPackageSource.Parse($"github:{repository}@{release.Tag}/{chosen.Name}", extension);
            expected ??= await releases.ResolveDigestAsync(source, release, token);
        }
        using var downloader = new RemotePackageDownloader(handler, new RemoteDownloadOptions
        {
            MaximumBytes = theme ? ThemePackage.MaximumArchiveBytes : RemoteDownloadOptions.DefaultMaximumBytes,
        });
        await using var downloaded = await downloader.DownloadAsync(RemotePackageSource.Resolve(source.ToString(), extension)!, expected, token, extension);
        var hash = Convert.ToHexString(downloaded.Sha256).ToLowerInvariant();
        if (theme)
        {
            var next = await ThemePackage.InspectArchiveAsync(downloaded.PackageStream, token);
            RequireIdentity(id, publisher, next.Manifest.Id, next.Manifest.Publisher);
            if (!await ReportVersionAsync(id, version, next.Manifest.Version, source, output)) return 0;
            await output.WriteLineAsync("Theme packages contain styles and metadata; no widget permissions are added.");
            if (!apply) return await ReviewAsync(parsed, source, hash, output, theme, root, version);
            await ThemePackage.InstallAsync(next, root, token);
            await PackageSources.SaveAsync(root, "theme", new(id, next.Manifest.Version, publisher, source.ToString(), hash, PackageSources.ThemeDigest(next)), output, token);
            await output.WriteLineAsync($"Installed {id} {next.Manifest.Version}. The current theme is unchanged; choose the new version in Settings > Appearance. Older versions remain available.");
            return 0;
        }
        var inspection = await catalog!.CreateInstaller().ValidateAsync(downloaded.PackageStream, token);
        RequireIdentity(id, publisher, inspection.Id, inspection.Manifest.Publisher);
        if (!await ReportVersionAsync(id, version, inspection.Version.ToString(), source, output)) return 0;
        var previous = widget!.ActiveVersion.Manifest;
        var nextManifest = inspection.Manifest;
        await ReportPermissionsAsync("Required", previous.Permissions, nextManifest.Permissions, output);
        await ReportPermissionsAsync("Optional", previous.OptionalPermissions, nextManifest.OptionalPermissions, output);
        var oldTrust = WidgetManifestTrust.Resolve(previous);
        var newTrust = WidgetManifestTrust.Resolve(nextManifest);
        await output.WriteLineAsync($"Execution: {oldTrust} -> {newTrust}");
        await output.WriteLineAsync($"Host API required: {nextManifest.HostApi.Minimum} through major {nextManifest.HostApi.MaximumMajor}; architectures: {string.Join(", ", nextManifest.Architectures)}");
        if (oldTrust != newTrust) throw new CliOperationException("Updates cannot change the widget's execution model. Review it as a separate installation.");
        if (!WidgetHostCompatibility.Evaluate(nextManifest).IsSupported)
            throw new CliOperationException("This package is not compatible with this WidgetRail host API.");
        if (!apply) return await ReviewAsync(parsed, source, hash, output, theme, root, version, newTrust == WidgetExecutionTrust.FullTrustCurrentUser);
        if (widget.Enabled) throw new CliOperationException($"Disable {id} before applying this update. The current version has not been changed.");
        var trust = WidgetPackageTrustApproval.None;
        if (newTrust == WidgetExecutionTrust.FullTrustCurrentUser)
        {
            if (!parsed.HasFlag("--accept-full-trust")) throw new CliUsageException("This update requires --accept-full-trust after reviewing its execution model.");
            await InstallCommand.WriteFullTrustDisclosureAsync(output, id, inspection.Version.ToString());
            trust = WidgetPackageTrustApproval.FullTrustCurrentUser;
        }
        var targetHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
        var installedWidget = await catalog.UpdateFromFileAsync(downloaded.PackageStream, targetHash, trust,
            async (_, admissionToken) =>
            {
                var current = (await catalog.DiscoverAsync(admissionToken)).Widgets.SingleOrDefault(item => item.Id == id);
                if (current is null || current.Enabled || current.ActiveVersion.ContentDigest != digest || current.ActiveVersion.Version.ToString() != version)
                    throw new CliOperationException("The selected widget changed while the update was being prepared. Disable it and review the update again.");
            }, token);
        await PackageSources.SaveAsync(root, "widget", new(id, installedWidget.Version.ToString(), publisher, source.ToString(), hash, installedWidget.ContentDigest), output, token);
        await output.WriteLineAsync($"Selected {id} {installedWidget.Version} (disabled). Review permissions before enabling. Previous versions remain available through wrail version rollback.");
        return 0;
    }

    private static void RequireIdentity(string id, string publisher, string nextId, string? nextPublisher)
    {
        if (id != nextId || publisher != nextPublisher) throw new CliOperationException("The release package has a different widget/theme ID or publisher. Nothing was installed.");
    }
    private static async Task<bool> ReportVersionAsync(string id, string current, string next, GitHubPackageSource source, TextWriter output)
    {
        await output.WriteLineAsync($"{id}: installed {current}; release package {next}\nSource: {source}");
        if (Version.Parse(next) > Version.Parse(current)) return true;
        await output.WriteLineAsync("This release has no newer package version. Nothing was changed.");
        return false;
    }
    private static async Task ReportPermissionsAsync(string label, IReadOnlyList<string> previous, IReadOnlyList<string> next, TextWriter output)
    {
        var added = next.Except(previous, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var removed = previous.Except(next, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await output.WriteLineAsync($"{label} permissions added: {(added.Length == 0 ? "none" : string.Join(", ", added))}");
        await output.WriteLineAsync($"{label} permissions removed: {(removed.Length == 0 ? "none" : string.Join(", ", removed))}");
    }
    private static async Task<int> ReviewAsync(CommandArguments parsed, GitHubPackageSource source, string hash, TextWriter output, bool theme, string root, string version, bool fullTrust = false)
    {
        await output.WriteLineAsync($"SHA-256: {hash}\nReview only; nothing was installed or activated.");
        // PowerShell single-quote escaping preserves literal Windows paths.
        await output.WriteLineAsync($"Apply the reviewed bytes:\nwrail {(theme ? "theme update" : "update")} {parsed.Positionals[0]} --repo {source.Repository} --tag {source.Tag} --asset {source.Asset} --sha256 {hash} --apply " +
            (theme ? $"--version {version} --settings-root " : "--catalog ") + "'" + root.Replace("'", "''", StringComparison.Ordinal) + "'" +
            (fullTrust ? " --accept-full-trust" : ""));
        return 0;
    }
}
