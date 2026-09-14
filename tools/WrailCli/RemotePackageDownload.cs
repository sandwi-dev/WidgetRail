using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace WidgetRail.WrailCli;

internal sealed record RemoteDownloadOptions
{
    public const long DefaultMaximumBytes = 72L * 1024 * 1024;

    public long MaximumBytes { get; init; } = DefaultMaximumBytes;
    public int MaximumRedirects { get; init; } = 5;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(120);
    internal string? TemporaryDirectoryRoot { get; init; }
}

internal sealed class RemotePackageDownloader : IDisposable
{
    private readonly HttpClient _client;
    private readonly RemoteDownloadOptions _options;

    public RemotePackageDownloader(HttpMessageHandler? handler = null, RemoteDownloadOptions? options = null)
    {
        _options = options ?? new RemoteDownloadOptions();
        ValidateOptions(_options);
        var disposeHandler = handler is null;
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = _options.ConnectTimeout,
        };
        _client = new HttpClient(handler, disposeHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("wrail", "1.0"));
    }

    public async Task<DownloadedPackage> DownloadAsync(
        Uri source,
        byte[]? expectedSha256,
        CancellationToken cancellationToken,
        string temporaryExtension = ".wrwidget")
    {
        ValidateRemoteUri(source, "Remote package URL");
        if (temporaryExtension is not ".wrwidget" and not ".wrtheme")
            throw new ArgumentException("Temporary package extension is unsupported.", nameof(temporaryExtension));
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(_options.OverallTimeout);

        var downloadRoot = _options.TemporaryDirectoryRoot ??
            Path.Combine(Path.GetTempPath(), "WidgetRail", "downloads");
        var directory = Path.Combine(downloadRoot, Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(directory, "package" + temporaryExtension);
        Directory.CreateDirectory(directory);
        FileStream? integrityGuard = null;

        try
        {
            using var response = await FollowRedirectsAsync(source, overall.Token);
            if (response.Content.Headers.ContentLength is { } contentLength && contentLength > _options.MaximumBytes)
                throw new CliOperationException(
                    $"Remote package exceeds the {_options.MaximumBytes}-byte download limit (Content-Length: {contentLength}).");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (response.Content.Headers.ContentEncoding.Count != 0)
                throw new CliOperationException("Remote package response must use identity content encoding.");
            await using var input = await response.Content.ReadAsStreamAsync(overall.Token);
            integrityGuard = new FileStream(
                packagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long total = 0;
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, overall.Token);
                if (read == 0) break;
                total = checked(total + read);
                if (total > _options.MaximumBytes)
                    throw new CliOperationException(
                        $"Remote package exceeds the {_options.MaximumBytes}-byte download limit.");
                hash.AppendData(buffer, 0, read);
                await integrityGuard.WriteAsync(buffer.AsMemory(0, read), overall.Token);
            }
            if (total == 0)
                throw new CliOperationException("Remote package response was empty.");
            await integrityGuard.FlushAsync(overall.Token);
            var actualSha256 = hash.GetHashAndReset();
            PackageIntegrity.Verify(expectedSha256, actualSha256);
            return new DownloadedPackage(directory, packagePath, actualSha256, integrityGuard);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            integrityGuard?.Dispose();
            TryDelete(directory, packagePath);
            throw new CliOperationException(
                $"Remote package download exceeded the {_options.OverallTimeout.TotalSeconds:0}-second overall timeout.", exception);
        }
        catch
        {
            integrityGuard?.Dispose();
            TryDelete(directory, packagePath);
            throw;
        }
    }

    private async Task<HttpResponseMessage> FollowRedirectsAsync(Uri source, CancellationToken overallToken)
    {
        var current = source;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            HttpResponseMessage response;
            using (var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(overallToken))
            {
                responseTimeout.CancelAfter(_options.ResponseTimeout);
                try
                {
                    response = await _client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, responseTimeout.Token);
                }
                catch (OperationCanceledException exception) when (!overallToken.IsCancellationRequested)
                {
                    throw new CliOperationException(
                        $"Remote package request exceeded the {_options.ResponseTimeout.TotalSeconds:0}-second response timeout.",
                        exception);
                }
                catch (HttpRequestException exception)
                {
                    throw new CliOperationException(
                        "Remote package request failed. Check the HTTPS address, network connection, and server certificate.",
                        exception);
                }
            }

            if (!IsRedirect(response.StatusCode))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    response.Dispose();
                    throw new CliOperationException($"Remote package request returned HTTP {statusCode}.");
                }
                return response;
            }

            if (redirectCount == _options.MaximumRedirects)
            {
                response.Dispose();
                throw new CliOperationException(
                    $"Remote package request exceeded the {_options.MaximumRedirects}-redirect limit.");
            }
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new CliOperationException("Remote package redirect did not include a Location header.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            ValidateRemoteUri(current, "Remote package redirect URL");
        }
    }

    // Checksum manifests are metadata, not executable packages. They use the
    // same HTTPS/redirect rules but a much smaller independently bounded body.
    internal async Task<string> ReadChecksumsAsync(Uri source, CancellationToken token)
    {
        ValidateRemoteUri(source, "Checksum manifest URL");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await FollowRedirectsAsync(source, timeout.Token);
            const int maximum = 256 * 1024;
            if (response.Content.Headers.ContentLength > maximum || response.Content.Headers.ContentEncoding.Count != 0)
                throw new CliOperationException("Checksum manifest is oversized or encoded.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var result = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (result.Length + count > maximum) throw new CliOperationException("Checksum manifest exceeds 256 KiB.");
                result.Write(buffer, 0, count);
            }
            return new System.Text.UTF8Encoding(false, true).GetString(result.ToArray());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new CliOperationException("Checksum manifest download timed out."); }
        catch (System.Text.DecoderFallbackException)
        { throw new CliOperationException("Checksum manifest is not valid UTF-8 text."); }
    }

    public void Dispose() => _client.Dispose();

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    internal static void ValidateRemoteUri(Uri uri, string label)
    {
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new CliUsageException($"{label} must be an absolute HTTPS URL.");
        if (uri.Port != 443)
            throw new CliUsageException($"{label} must use the standard HTTPS port 443.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new CliUsageException($"{label} must not contain credentials.");
        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new CliUsageException($"{label} must not contain a fragment.");
        RejectLocalLiteralHost(uri, label);
    }

    private static void RejectLocalLiteralHost(Uri uri, string label)
    {
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException($"{label} must not target localhost.");
        if (!IPAddress.TryParse(uri.IdnHost, out var address)) return;
        if (IPAddress.IsLoopback(address) || IsPrivateAddress(address))
            throw new CliUsageException($"{label} must not target a loopback, private, or link-local address literal.");
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 || bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 0;
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return true;
        if (address.IsIPv4MappedToIPv6) return IsPrivateAddress(address.MapToIPv4());
        var first = address.GetAddressBytes();
        return address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None) ||
               address.IsIPv6LinkLocal || (first[0] & 0xFE) == 0xFC;
    }

    private static void ValidateOptions(RemoteDownloadOptions options)
    {
        if (options.MaximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(options.MaximumBytes));
        if (options.MaximumRedirects < 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumRedirects));
        if (options.ConnectTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.ConnectTimeout));
        if (options.ResponseTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.ResponseTimeout));
        if (options.OverallTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.OverallTimeout));
        if (options.TemporaryDirectoryRoot is not null && !Path.IsPathFullyQualified(options.TemporaryDirectoryRoot))
            throw new ArgumentException("Temporary download root must be an absolute path.", nameof(options));
    }

    private static void TryDelete(string directory, string packagePath)
    {
        try
        {
            if (File.Exists(packagePath)) File.Delete(packagePath);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal sealed class DownloadedPackage : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly FileStream _integrityGuard;

        public DownloadedPackage(string directory, string packagePath, byte[] sha256, FileStream integrityGuard)
        {
            _directory = directory;
            PackagePath = packagePath;
            Sha256 = sha256;
            _integrityGuard = integrityGuard;
        }

        public string PackagePath { get; }
        public byte[] Sha256 { get; }
        public Stream PackageStream => _integrityGuard;

        public ValueTask DisposeAsync()
        {
            _integrityGuard.Dispose();
            TryDelete(_directory, PackagePath);
            return ValueTask.CompletedTask;
        }
    }
}

internal static partial class RemotePackageSource
{
    private static readonly Regex GitHubSegment = GitHubSegmentRegex();

    public static Uri? Resolve(string source, string requiredExtension = ".wrwidget")
    {
        if (requiredExtension is not ".wrwidget" and not ".wrtheme")
            throw new ArgumentException("Remote package extension is unsupported.", nameof(requiredExtension));
        if (source.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
            return ResolveGitHub(source["github:".Length..], requiredExtension);

        if (source.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
                throw new CliUsageException("Remote package URL is invalid.");
            RemotePackageDownloader.ValidateRemoteUri(uri, "Remote package URL");
            return uri;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var other) &&
            (other.Scheme is "http" or "ftp" || source.Contains("://", StringComparison.Ordinal)))
            throw new CliUsageException("Remote package URL must use HTTPS.");
        return null;
    }

    private static Uri ResolveGitHub(string shorthand, string requiredExtension)
    {
        var slash = shorthand.IndexOf('/');
        var at = shorthand.IndexOf('@');
        var assetSlash = at < 0 ? -1 : shorthand.IndexOf('/', at + 1);
        if (slash <= 0 || at <= slash + 1 || assetSlash <= at + 1 || assetSlash == shorthand.Length - 1)
            throw GitHubUsage(requiredExtension);

        var owner = shorthand[..slash];
        var repository = shorthand[(slash + 1)..at];
        var tag = shorthand[(at + 1)..assetSlash];
        var asset = shorthand[(assetSlash + 1)..];
        if (!IsSafeGitHubSegment(owner) || !IsSafeGitHubSegment(repository) ||
            !IsSafeGitHubSegment(tag) || !IsSafeGitHubSegment(asset) ||
            !asset.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            throw GitHubUsage(requiredExtension);

        var url = $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}" +
                  $"/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(asset)}";
        return new Uri(url, UriKind.Absolute);
    }

    private static bool IsSafeGitHubSegment(string value) =>
        value.Length is > 0 and <= 128 && GitHubSegment.IsMatch(value) && value is not "." and not "..";

    private static CliUsageException GitHubUsage(string extension) => new(
        $"GitHub source must use github:owner/repository@tag/asset{extension} with safe, non-empty segments.");

    [GeneratedRegex("\\A[A-Za-z0-9._-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubSegmentRegex();
}

internal static class PackageIntegrity
{
    public static byte[]? ParseExpectedSha256(string? value)
    {
        if (value is null) return null;
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new CliUsageException("--sha256 requires exactly 64 hexadecimal characters.");
        return Convert.FromHexString(value);
    }

    public static async Task<byte[]> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    public static async Task<byte[]> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new CliOperationException("Package hash streams must be readable and seekable.");
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            return await SHA256.HashDataAsync(stream, cancellationToken);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    public static void Verify(byte[]? expected, byte[] actual)
    {
        if (expected is not null && !CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new CliOperationException(
                $"SHA-256 mismatch: expected {Convert.ToHexString(expected).ToLowerInvariant()}, " +
                $"received {Convert.ToHexString(actual).ToLowerInvariant()}.");
    }
}
