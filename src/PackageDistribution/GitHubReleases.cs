using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
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

/// <summary>A conditional release page. A 304 retains the caller's previous page.</summary>
public sealed record GitHubReleasePage(IReadOnlyList<GitHubRelease>? Releases, string? ETag,
    DateTimeOffset? LastModified, bool HasNext, DateTimeOffset? RetryAfterUtc);

public sealed class GitHubRequestDeferredException(DateTimeOffset? retryAfterUtc, string message) : Exception(message)
{
    public DateTimeOffset? RetryAfterUtc { get; } = retryAfterUtc;
}

public sealed class GitHubReleaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _includeInstallers;
    private readonly HttpMessageHandler? _handler;
    private readonly SemaphoreSlim _checksumGate = new(1, 1);
    private readonly Dictionary<Uri, (string? Text, ExceptionDispatchInfo? Failure)> _checksumManifests = new();
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

    /// <summary>Bounded metadata lookup with validators; never retries or follows server-supplied download URLs.</summary>
    public async Task<GitHubReleasePage> ListPageAsync(string repository, int page, string? etag,
        DateTimeOffset? lastModified, DateTimeOffset now, CancellationToken token)
    {
        if (page is < 1 or > 100) throw new DistributionUsageException("Release page must be between 1 and 100.");
        GitHubPackageSource.ValidateRepository(repository);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}");
        if (System.Net.Http.Headers.EntityTagHeaderValue.TryParse(etag, out var validator)) request.Headers.IfNoneMatch.Add(validator);
        else if (lastModified is { } modified) request.Headers.IfModifiedSince = modified;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var retryAfter = RetryAfter(response, now);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new GitHubRequestDeferredException(retryAfter, "GitHub is temporarily unavailable or has limited requests.");
            if (response.StatusCode == HttpStatusCode.NotModified)
                return new(null, response.Headers.ETag?.ToString() ?? etag, response.Content.Headers.LastModified ?? lastModified, false, retryAfter);
            if (!response.IsSuccessStatusCode) throw new DistributionOperationException($"GitHub release lookup returned HTTP {(int)response.StatusCode}.");
            using var json = await ReadDocumentAsync(response, timeout.Token).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 100)
                throw new DistributionOperationException("GitHub returned an invalid release list.");
            var releases = json.RootElement.EnumerateArray().Where(item => !Boolean(item, "draft"))
                .Select(item => ParseRelease(item, _includeInstallers)).ToArray();
            // Only the relation is used: pagination stays on our validated repository and bounded page numbers.
            var hasNext = response.Headers.TryGetValues("Link", out var links) && links.Any(link =>
                link.Split(',').Any(part => part.Contains("rel=\"next\"", StringComparison.Ordinal)));
            return new(releases, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, hasNext, retryAfter);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new DistributionOperationException("GitHub release lookup timed out after 20 seconds."); }
        catch (HttpRequestException) { throw new DistributionOperationException("GitHub release lookup failed. Check the network connection."); }
        catch (JsonException) { throw new DistributionOperationException("GitHub returned malformed release metadata."); }
    }

    private static DateTimeOffset? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        DateTimeOffset? until = response.Headers.RetryAfter?.Date;
        if (response.Headers.RetryAfter?.Delta is { } delay)
            until = delay > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay;
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0" &&
            response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) &&
            long.TryParse(resets.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
            seconds is >= 0 and <= 253402300799)
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (until is null || reset > until) until = reset;
        }
        return until > now ? until : null;
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
        var text = await ReadChecksumManifestAsync(checksumUrl, token).ConfigureAwait(false);
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

    private async Task<string> ReadChecksumManifestAsync(Uri source, CancellationToken token)
    {
        await _checksumGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_checksumManifests.TryGetValue(source, out var cached))
            {
                cached.Failure?.Throw();
                return cached.Text!;
            }
            // A client is scoped to one review batch. Keep other callers bounded too: 16 x 256 KiB.
            if (_checksumManifests.Count == 16) _checksumManifests.Remove(_checksumManifests.Keys.First());
            try
            {
                using var downloader = new RemotePackageDownloader(_handler);
                var text = await downloader.ReadChecksumsAsync(source, token).ConfigureAwait(false);
                _checksumManifests.Add(source, (text, null));
                return text;
            }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
            {
                // Do not retry a failed release manifest once per widget in the same batch.
                _checksumManifests.Add(source, (null, ExceptionDispatchInfo.Capture(error)));
                throw;
            }
        }
        finally { _checksumGate.Release(); }
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
            return await ReadDocumentAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new DistributionOperationException("GitHub release lookup timed out after 20 seconds."); }
        catch (HttpRequestException) { throw new DistributionOperationException("GitHub release lookup failed. Check the network connection."); }
        catch (JsonException) { throw new DistributionOperationException("GitHub returned malformed release metadata."); }
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken token)
    {
        const int maximum = 2 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new DistributionOperationException("GitHub metadata exceeds the 2 MiB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > maximum) throw new DistributionOperationException("GitHub metadata exceeds the 2 MiB limit.");
            buffer.Write(chunk, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
    }

    public void Dispose() { _http.Dispose(); _checksumGate.Dispose(); }
}
