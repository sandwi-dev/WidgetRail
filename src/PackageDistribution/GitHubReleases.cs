using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WidgetRail.PackageDistribution;

public sealed record GitHubPackageSource(string Repository, string Tag, string Asset)
{
    public override string ToString() => $"github:{Repository}@{Tag}/{Asset}";
    public static GitHubPackageSource Parse(string source, string extension)
    {
        if (!source.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
            throw new DistributionUsageException("Use a GitHub source: github:owner/repository@tag/asset" + extension);
        _ = RemotePackageSource.Resolve(source, extension);
        var at = source.IndexOf('@');
        var slash = source.IndexOf('/', at);
        return new(source[7..at], source[(at + 1)..slash], source[(slash + 1)..]);
    }
    public static string ValidateRepository(string repository)
    {
        _ = RemotePackageSource.Resolve($"github:{repository}@v0/probe.wrwidget");
        return repository;
    }
}

public sealed record GitHubAsset(string Name, string? Sha256, long Size);
public sealed record GitHubRelease(string Tag, bool Prerelease, IReadOnlyList<GitHubAsset> Assets, string? ChecksumFile, string Notes = "");

public sealed class GitHubReleaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _includeInstallers;
    private readonly HttpMessageHandler? _handler;
    public GitHubReleaseClient(HttpMessageHandler? handler, bool includeInstallers = false)
    {
        _handler = handler;
        _includeInstallers = includeInstallers;
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        }, disposeHandler: handler is null) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("wrail/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<IReadOnlyList<GitHubRelease>> ListAsync(string repository, int page, CancellationToken token)
    {
        if (page is < 1 or > 100) throw new DistributionUsageException("Release page must be between 1 and 100.");
        GitHubPackageSource.ValidateRepository(repository);
        using var json = await GetAsync($"repos/{repository}/releases?per_page=20&page={page}", token);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 20)
            throw new DistributionOperationException("GitHub returned an invalid release list.");
        return json.RootElement.EnumerateArray().Where(item => !Boolean(item, "draft"))
            .Select(item => ParseRelease(item, _includeInstallers)).ToArray();
    }

    public async Task<GitHubRelease> GetReleaseAsync(string repository, string? tag, CancellationToken token)
    {
        GitHubPackageSource.ValidateRepository(repository);
        if (tag is not null) _ = GitHubPackageSource.Parse($"github:{repository}@{tag}/probe.wrwidget", ".wrwidget");
        using var json = await GetAsync($"repos/{repository}/releases/" + (tag is null ? "latest" : "tags/" + Uri.EscapeDataString(tag)), token);
        if (Boolean(json.RootElement, "draft")) throw new DistributionOperationException("Draft releases cannot be installed.");
        var release = ParseRelease(json.RootElement, _includeInstallers);
        if (tag is not null && release.Tag != tag) throw new DistributionOperationException("GitHub returned a different release tag than requested.");
        return release;
    }

    public async Task<byte[]> ResolveDigestAsync(GitHubPackageSource source, CancellationToken token)
    {
        var release = await GetReleaseAsync(source.Repository, source.Tag, token);
        return await ResolveDigestAsync(source, release, token);
    }

    public async Task<byte[]> ResolveDigestAsync(GitHubPackageSource source, GitHubRelease release, CancellationToken token)
    {
        var asset = release.Assets.SingleOrDefault(asset => asset.Name == source.Asset)
            ?? throw new DistributionOperationException("The named package asset was not found in this release.");
        if (asset.Sha256 is not null) return PackageIntegrity.ParseExpectedSha256(asset.Sha256)!;
        if (release.ChecksumFile is null)
            throw new DistributionUsageException("No published SHA-256 was found. Supply --sha256 from the publisher's checksums.");
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
            if (hash is not null) throw new DistributionOperationException("Checksum manifest repeats the selected package; supply an explicit trusted --sha256.");
            hash = clean[..64];
            _ = PackageIntegrity.ParseExpectedSha256(hash);
        }
        return PackageIntegrity.ParseExpectedSha256(hash)
            ?? throw new DistributionUsageException("Checksum manifest does not contain this package. Supply --sha256 explicitly.");
    }

    private static GitHubRelease ParseRelease(JsonElement item, bool includeInstallers)
    {
        var tag = Text(item, "tag_name");
        if (!item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new DistributionOperationException("GitHub returned invalid release assets.");
        if (assets.GetArrayLength() > 512) throw new DistributionOperationException("GitHub release has too many assets.");
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
            if (extension is not ".wrwidget" and not ".wrtheme" && !(includeInstallers && extension == ".exe")) continue;
            // Reconstruct downloads from validated names, never an API-supplied URL.
            _ = GitHubPackageSource.Parse($"github:validation/repository@{tag}/{name}", extension);
            var digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            var hash = digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal)
                ? digest[7..] : null;
            if (hash is not null) _ = PackageIntegrity.ParseExpectedSha256(hash);
            if (result.Any(existing => existing.Name == name)) throw new DistributionOperationException("GitHub release has duplicate package assets.");
            if (!asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes) || bytes < 0)
                throw new DistributionOperationException("GitHub returned an invalid asset size.");
            result.Add(new(name, hash?.ToLowerInvariant(), bytes));
        }
        return new(tag, Boolean(item, "prerelease"), result, checksumFile, item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? (body.GetString() ?? string.Empty)[..Math.Min(8000, body.GetString()!.Length)] : string.Empty);
    }

    private static string Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new DistributionOperationException("GitHub returned invalid release metadata.");
    private static bool Boolean(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new DistributionOperationException("GitHub returned invalid release metadata.");

    private async Task<JsonDocument> GetAsync(string relative, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await _http.GetAsync(new Uri("https://api.github.com/" + relative), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new DistributionOperationException("GitHub denied the request or its API rate limit was reached. Try again later; no automatic retries were made.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DistributionOperationException("GitHub release was not found. This command supports public repositories; verify the repository and tag.");
            if (!response.IsSuccessStatusCode) throw new DistributionOperationException($"GitHub release lookup returned HTTP {(int)response.StatusCode}.");
            const int maximum = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximum) throw new DistributionOperationException("GitHub metadata exceeds the 2 MiB limit.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (buffer.Length + count > maximum) throw new DistributionOperationException("GitHub metadata exceeds the 2 MiB limit.");
                buffer.Write(chunk, 0, count);
            }
            return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new DistributionOperationException("GitHub release lookup timed out after 20 seconds."); }
        catch (HttpRequestException) { throw new DistributionOperationException("GitHub release lookup failed. Check the network connection."); }
        catch (JsonException) { throw new DistributionOperationException("GitHub returned malformed release metadata."); }
    }

    public void Dispose() => _http.Dispose();
}
