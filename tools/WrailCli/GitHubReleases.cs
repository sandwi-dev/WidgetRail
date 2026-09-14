using System.Net;
using System.Text.Json;

namespace WidgetRail.WrailCli;

internal sealed record GitHubPackageSource(string Repository, string Tag, string Asset)
{
    public override string ToString() => $"github:{Repository}@{Tag}/{Asset}";
    internal static GitHubPackageSource Parse(string source, string extension)
    {
        if (!source.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Use a GitHub source: github:owner/repository@tag/asset" + extension);
        _ = RemotePackageSource.Resolve(source, extension);
        var at = source.IndexOf('@');
        var slash = source.IndexOf('/', at);
        return new(source[7..at], source[(at + 1)..slash], source[(slash + 1)..]);
    }
    internal static string ValidateRepository(string repository)
    {
        _ = RemotePackageSource.Resolve($"github:{repository}@v0/probe.wrwidget");
        return repository;
    }
}

internal sealed record GitHubAsset(string Name, string? Sha256, long Size);
internal sealed record GitHubRelease(string Tag, bool Prerelease, IReadOnlyList<GitHubAsset> Assets, string? ChecksumFile);

internal sealed class GitHubReleaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpMessageHandler? _handler;
    internal GitHubReleaseClient(HttpMessageHandler? handler)
    {
        _handler = handler;
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        }, disposeHandler: handler is null) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("wrail/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    internal async Task<IReadOnlyList<GitHubRelease>> ListAsync(string repository, int page, CancellationToken token)
    {
        GitHubPackageSource.ValidateRepository(repository);
        using var json = await GetAsync($"repos/{repository}/releases?per_page=20&page={page}", token);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 20)
            throw new CliOperationException("GitHub returned an invalid release list.");
        return json.RootElement.EnumerateArray().Where(item => !Boolean(item, "draft"))
            .Select(ParseRelease).ToArray();
    }

    internal async Task<GitHubRelease> GetReleaseAsync(string repository, string? tag, CancellationToken token)
    {
        GitHubPackageSource.ValidateRepository(repository);
        if (tag is not null) _ = GitHubPackageSource.Parse($"github:{repository}@{tag}/probe.wrwidget", ".wrwidget");
        using var json = await GetAsync($"repos/{repository}/releases/" + (tag is null ? "latest" : "tags/" + Uri.EscapeDataString(tag)), token);
        if (Boolean(json.RootElement, "draft")) throw new CliOperationException("Draft releases cannot be installed.");
        var release = ParseRelease(json.RootElement);
        if (tag is not null && release.Tag != tag) throw new CliOperationException("GitHub returned a different release tag than requested.");
        return release;
    }

    internal async Task<byte[]> ResolveDigestAsync(GitHubPackageSource source, CancellationToken token)
    {
        var release = await GetReleaseAsync(source.Repository, source.Tag, token);
        return await ResolveDigestAsync(source, release, token);
    }

    internal async Task<byte[]> ResolveDigestAsync(GitHubPackageSource source, GitHubRelease release, CancellationToken token)
    {
        var asset = release.Assets.SingleOrDefault(asset => asset.Name == source.Asset)
            ?? throw new CliOperationException("The named package asset was not found in this release.");
        if (asset.Sha256 is not null) return PackageIntegrity.ParseExpectedSha256(asset.Sha256)!;
        if (release.ChecksumFile is null)
            throw new CliUsageException("No published SHA-256 was found. Supply --sha256 from the publisher's checksums.");
        var packageUrl = RemotePackageSource.Resolve(source.ToString(), Path.GetExtension(source.Asset).ToLowerInvariant())!;
        var checksumUrl = new Uri(packageUrl, Uri.EscapeDataString(release.ChecksumFile));
        using var downloader = new RemotePackageDownloader(_handler);
        var text = await downloader.ReadChecksumsAsync(checksumUrl, token);
        string? hash = null;
        foreach (var line in text.Split('\n'))
        {
            var clean = line.TrimEnd('\r');
            if (clean.Length < 67 || clean[64] != ' ' || clean[65] is not ' ' and not '*') continue;
            if (clean[66..] != source.Asset) continue;
            if (hash is not null) throw new CliOperationException("Checksum manifest repeats the selected package; supply an explicit trusted --sha256.");
            hash = clean[..64];
            _ = PackageIntegrity.ParseExpectedSha256(hash);
        }
        return PackageIntegrity.ParseExpectedSha256(hash)
            ?? throw new CliUsageException("Checksum manifest does not contain this package. Supply --sha256 explicitly.");
    }

    private static GitHubRelease ParseRelease(JsonElement item)
    {
        var tag = Text(item, "tag_name");
        if (!item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new CliOperationException("GitHub returned invalid release assets.");
        if (assets.GetArrayLength() > 512) throw new CliOperationException("GitHub release has too many assets.");
        var result = new List<GitHubAsset>();
        string? checksumFile = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            if (name is "SHA256SUMS.txt" or "SHA256SUMS" or "checksums.txt")
            {
                string[] preferred = ["SHA256SUMS.txt", "SHA256SUMS", "checksums.txt"];
                if (checksumFile is null || Array.IndexOf(preferred, name) < Array.IndexOf(preferred, checksumFile)) checksumFile = name;
                continue;
            }
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is not ".wrwidget" and not ".wrtheme") continue;
            // Reconstruct downloads from validated names, never an API-supplied URL.
            _ = GitHubPackageSource.Parse($"github:validation/repository@{tag}/{name}", extension);
            var digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            var hash = digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal)
                ? digest[7..] : null;
            if (hash is not null) _ = PackageIntegrity.ParseExpectedSha256(hash);
            if (result.Any(existing => existing.Name == name)) throw new CliOperationException("GitHub release has duplicate package assets.");
            if (!asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes) || bytes < 0)
                throw new CliOperationException("GitHub returned an invalid asset size.");
            result.Add(new(name, hash?.ToLowerInvariant(), bytes));
        }
        return new(tag, Boolean(item, "prerelease"), result, checksumFile);
    }

    private static string Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new CliOperationException("GitHub returned invalid release metadata.");
    private static bool Boolean(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new CliOperationException("GitHub returned invalid release metadata.");

    private async Task<JsonDocument> GetAsync(string relative, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await _http.GetAsync(new Uri("https://api.github.com/" + relative), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new CliOperationException("GitHub denied the request or its API rate limit was reached. Try again later; no automatic retries were made.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new CliOperationException("GitHub release was not found. This command supports public repositories; verify the repository and tag.");
            if (!response.IsSuccessStatusCode) throw new CliOperationException($"GitHub release lookup returned HTTP {(int)response.StatusCode}.");
            const int maximum = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximum) throw new CliOperationException("GitHub metadata exceeds the 2 MiB limit.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (buffer.Length + count > maximum) throw new CliOperationException("GitHub metadata exceeds the 2 MiB limit.");
                buffer.Write(chunk, 0, count);
            }
            return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new CliOperationException("GitHub release lookup timed out after 20 seconds."); }
        catch (HttpRequestException) { throw new CliOperationException("GitHub release lookup failed. Check the network connection."); }
        catch (JsonException) { throw new CliOperationException("GitHub returned malformed release metadata."); }
    }

    public void Dispose() => _http.Dispose();
}

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
            foreach (var release in visible)
                foreach (var asset in release.Assets)
                    await output.WriteLineAsync($"{release.Tag}{(release.Prerelease ? " (prerelease)" : "")}  {asset.Name}  {asset.Size} bytes\n  github:{repository}@{release.Tag}/{asset.Name}\n  SHA-256: {asset.Sha256 ?? "not published; supply --sha256"}");
            if (!visible.Any(release => release.Assets.Count != 0)) await output.WriteLineAsync("No widget or theme packages were found in the selected releases.");
            if (parsed.Option("--tag") is null)
                await output.WriteLineAsync("Showing one page of up to 20 releases. Use --page to browse more, or --tag for an exact release.");
        }
        return 0;
    }
}
