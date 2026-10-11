using System.Globalization;
using System.Text.Json;

namespace WidgetRail.WrailCli;

internal static class ReleasesCommand
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output, HttpMessageHandler? handler, CancellationToken token)
    {
        var parsed = new CommandArguments(args, ["--tag", "--page"], ["--include-prerelease", "--json"]);
        if (parsed.Positionals.Count != 1) throw new CliUsageException("Usage: wrail releases <owner/repository> [--tag <tag>] [--page <1-100>] [--include-prerelease] [--json]");
        var page = 1;
        if (parsed.Option("--page") is { } pageText && (!int.TryParse(pageText, out page) || page is < 1 or > 100))
            throw new CliUsageException("--page must be between 1 and 100.");
        using var client = new GitHubReleaseClient(handler);
        var repository = parsed.Positionals[0];
        var releases = parsed.Option("--tag") is { } tag
            ? new[] { await client.GetReleaseAsync(repository, tag, token) }
            : await client.ListAsync(repository, page, token);
        var visible = releases.Where(release => !release.Prerelease || parsed.HasFlag("--include-prerelease") || parsed.Option("--tag") is not null).ToArray();
        if (parsed.HasFlag("--json"))
            await output.WriteLineAsync(JsonSerializer.Serialize(new { schemaVersion = 1, repository, page, releases = visible }, DiagnosticJson.Options));
        else
        {
            foreach (var release in visible.Where(release => release.Assets.Count != 0))
            {
                await output.WriteLineAsync($"{release.Tag}{(release.Prerelease ? " (prerelease)" : "")}");
                foreach (var asset in release.Assets)
                {
                    var command = asset.Name.EndsWith(".wrtheme", StringComparison.OrdinalIgnoreCase)
                        ? "theme install" : "install";
                    var source = new GitHubPackageSource(repository, release.Tag, asset.Name);
                    await output.WriteLineAsync($"  {asset.Name} ({FormatSize(asset.Size)})");
                    await output.WriteLineAsync($"    wrail {command} \"{source}\"");
                    if (asset.Sha256 is null && release.ChecksumFile is null)
                        await output.WriteLineAsync("    No checksum published; add --sha256 <publisher-hash> when installing.");
                }
                await output.WriteLineAsync();
            }
            if (!visible.Any(release => release.Assets.Count != 0)) await output.WriteLineAsync("No widget or theme packages were found in the selected releases.");
            else await output.WriteLineAsync("Use --json for SHA-256 hashes and exact byte sizes.");
            if (parsed.Option("--tag") is null)
                await output.WriteLineAsync($"Page {page} (up to 20 releases). Use --page N, --tag TAG, or --include-prerelease for previews.");
        }
        return 0;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.##} KiB"),
        < 1024 * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.##} MiB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024 * 1024):0.##} GiB"),
    };
}
