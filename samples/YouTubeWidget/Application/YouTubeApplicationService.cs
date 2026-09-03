using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace WidgetRail.Samples.YouTubeWidget;

internal sealed class YouTubeApplicationService : IYouTubeApplicationService
{
    private const int MaximumResponseBytes = 512 * 1024;
    private const int MaximumCachedPages = 12;
    private static readonly Uri ApiBase = new("https://www.googleapis.com/youtube/v3/");
    private static readonly Uri ConsoleUri = new("https://console.cloud.google.com/apis/library/youtube.googleapis.com");
    private static readonly Uri WatchBase = new("https://www.youtube.com/watch");
    private readonly HttpClient _http;
    private readonly WindowsCredentialYouTubeApiKeyStore _keyStore;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, YouTubeSearchPage> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();

    private YouTubeApplicationService(HttpClient http, WindowsCredentialYouTubeApiKeyStore keyStore)
    {
        _http = http;
        _keyStore = keyStore;
    }

    internal static YouTubeApplicationService CreateDefault() => new(
        new HttpClient { BaseAddress = ApiBase, Timeout = TimeSpan.FromSeconds(12) },
        new WindowsCredentialYouTubeApiKeyStore());

    public async ValueTask<YouTubeConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken) =>
        new(await _keyStore.ExistsAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask ConfigureApiKeyAsync(
        string apiKey, CancellationToken cancellationToken)
    {
        WindowsCredentialYouTubeApiKeyStore.Validate(apiKey);
        await SendAsync(
            "videos?part=id&id=M7lc1UVf-VE&fields=items(id)",
            apiKey,
            validationRequest: true,
            cancellationToken).ConfigureAwait(false);
        await _keyStore.SaveAsync(apiKey, cancellationToken).ConfigureAwait(false);
        lock (_cacheGate)
        {
            _cache.Clear();
            _cacheOrder.Clear();
        }
    }

    public async ValueTask DeleteApiKeyAsync(CancellationToken cancellationToken)
    {
        await _keyStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
        lock (_cacheGate)
        {
            _cache.Clear();
            _cacheOrder.Clear();
        }
    }

    public ValueTask OpenGoogleCloudConsoleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Process.Start(new ProcessStartInfo(ConsoleUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new YouTubeApplicationException(
                "console_unavailable", "Google Cloud Console could not be opened.", exception);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenVideoInYouTubeAsync(
        string videoId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsVideoId(videoId))
            throw new YouTubeApplicationException(
                "video_invalid", "The current YouTube video ID is invalid.");
        try
        {
            var target = new UriBuilder(WatchBase) { Query = "v=" + videoId }.Uri;
            Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                               System.ComponentModel.Win32Exception)
        {
            throw new YouTubeApplicationException(
                "youtube_unavailable", "YouTube could not be opened.", exception);
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<YouTubeSearchPage> SearchAsync(
        string query,
        string? pageToken,
        int pageSize,
        CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 96 || query.Any(char.IsControl))
            throw new YouTubeApplicationException("query_invalid", "Enter a shorter search query.");
        if (pageSize is < 1 or > 25)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageToken is { Length: > 256 } || pageToken?.Any(char.IsControl) == true)
            throw new YouTubeApplicationException("page_invalid", "The search page token is invalid.");

        var cacheKey = query + "\n" + (pageToken ?? string.Empty);
        lock (_cacheGate)
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        var apiKey = await _keyStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (apiKey is null)
            throw new YouTubeApplicationException(
                "setup_required", "Configure a YouTube Data API key before searching.");
        try
        {
            var searchPath = "search?part=snippet&type=video&maxResults=" + pageSize +
                "&q=" + Uri.EscapeDataString(query) +
                "&fields=nextPageToken,pageInfo(totalResults),items(id(videoId),snippet(title,channelTitle,thumbnails(medium(url))))" +
                (pageToken is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(pageToken));
            var searchBytes = await SendAsync(searchPath, apiKey, false, cancellationToken)
                .ConfigureAwait(false);
            var search = JsonSerializer.Deserialize<SearchResponse>(searchBytes)
                ?? throw InvalidResponse();
            var raw = search.Items ?? [];
            var priorIds = new HashSet<string>(StringComparer.Ordinal);
            if (pageToken is not null)
            {
                lock (_cacheGate)
                    foreach (var prior in _cache.Where(entry =>
                                 entry.Key.StartsWith(query + "\n", StringComparison.Ordinal)))
                    foreach (var item in prior.Value.Items)
                        priorIds.Add(item.VideoId);
            }
            var ids = raw.Select(item => item.Id?.VideoId)
                .Where(IsVideoId)
                .Cast<string>()
                .Where(id => !priorIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .Take(pageSize)
                .ToArray();
            var durations = ids.Length == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await LoadDurationsAsync(ids, apiKey, cancellationToken).ConfigureAwait(false);
            var items = new List<YouTubeSearchItem>(ids.Length);
            var seen = new HashSet<string>(priorIds, StringComparer.Ordinal);
            foreach (var entry in raw)
            {
                var id = entry.Id?.VideoId;
                if (!IsVideoId(id) || !seen.Add(id!)) continue;
                var snippet = entry.Snippet;
                if (snippet is null) continue;
                var title = BoundedText(WebUtility.HtmlDecode(snippet.Title), 120, "Untitled video");
                var channel = BoundedText(WebUtility.HtmlDecode(snippet.ChannelTitle), 80, "Unknown channel");
                var thumbnail = snippet.Thumbnails?.Medium?.Url;
                if (!TryThumbnail(thumbnail, out var safeThumbnail)) continue;
                items.Add(new(id!, title, channel,
                    durations.GetValueOrDefault(id!, "Duration unavailable"), safeThumbnail));
                if (items.Count == pageSize) break;
            }
            var page = new YouTubeSearchPage(
                items,
                BoundedToken(search.NextPageToken),
                search.PageInfo?.TotalResults is >= 0
                    ? Math.Min(search.PageInfo.TotalResults, 10_000) : null);
            lock (_cacheGate)
            {
                if (!_cache.ContainsKey(cacheKey)) _cacheOrder.Enqueue(cacheKey);
                _cache[cacheKey] = page;
                while (_cacheOrder.Count > MaximumCachedPages)
                    _cache.Remove(_cacheOrder.Dequeue());
            }
            return page;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(Encoding.UTF8.GetBytes(apiKey));
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Dictionary<string, string>> LoadDurationsAsync(
        IReadOnlyList<string> ids, string apiKey, CancellationToken cancellationToken)
    {
        var path = "videos?part=contentDetails&id=" + string.Join(',', ids) +
            "&fields=items(id,contentDetails(duration))";
        var bytes = await SendAsync(path, apiKey, false, cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<VideosResponse>(bytes) ?? throw InvalidResponse();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in response.Items ?? [])
        {
            if (!IsVideoId(item.Id)) continue;
            var value = item.ContentDetails?.Duration;
            if (value is null) continue;
            try
            {
                var duration = XmlConvert.ToTimeSpan(value);
                result[item.Id!] = duration.TotalHours >= 1
                    ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
                    : $"{duration.Minutes}:{duration.Seconds:00}";
            }
            catch (FormatException) { }
        }
        return result;
    }

    private async Task<byte[]> SendAsync(
        string path, string apiKey, bool validationRequest, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new YouTubeApplicationException(
                "youtube_offline", "YouTube could not be reached. Check the network and retry.", exception);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized => "api_key_invalid",
                    HttpStatusCode.Forbidden when validationRequest => "api_key_invalid",
                    HttpStatusCode.Forbidden => "quota_or_key_rejected",
                    HttpStatusCode.TooManyRequests => "youtube_quota_reached",
                    _ => "youtube_request_failed",
                };
                var message = code switch
                {
                    "api_key_invalid" => "Google rejected this API key or its restrictions.",
                    "youtube_quota_reached" => "YouTube search quota is temporarily exhausted.",
                    "quota_or_key_rejected" => "YouTube rejected the key or its quota is exhausted.",
                    _ => "YouTube search could not be completed. Try again.",
                };
                throw new YouTubeApplicationException(code, message);
            }
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                throw InvalidResponse();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length > MaximumResponseBytes) throw InvalidResponse();
            return bytes;
        }
    }

    private static bool TryThumbnail(string? value, out string result)
    {
        result = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "i.ytimg.com", StringComparison.OrdinalIgnoreCase) ||
            value!.Length > 512) return false;
        result = uri.AbsoluteUri;
        return true;
    }

    private static bool IsVideoId(string? value) =>
        value is { Length: 11 } && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string BoundedText(string? value, int maximum, string fallback)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return fallback;
        var visible = new string(value.Where(character => !char.IsControl(character)).ToArray());
        return visible.Length <= maximum ? visible : visible[..maximum];
    }

    private static string? BoundedToken(string? token) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 256 || token.Any(char.IsControl)
            ? null : token;

    private static YouTubeApplicationException InvalidResponse() =>
        new("youtube_response_invalid", "YouTube returned an invalid response. Try again.");

    private sealed record SearchResponse(
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
        [property: JsonPropertyName("pageInfo")] PageInfo? PageInfo,
        [property: JsonPropertyName("items")] SearchEntry[]? Items);
    private sealed record PageInfo([property: JsonPropertyName("totalResults")] int TotalResults);
    private sealed record SearchEntry(
        [property: JsonPropertyName("id")] SearchId? Id,
        [property: JsonPropertyName("snippet")] SearchSnippet? Snippet);
    private sealed record SearchId([property: JsonPropertyName("videoId")] string? VideoId);
    private sealed record SearchSnippet(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("channelTitle")] string? ChannelTitle,
        [property: JsonPropertyName("thumbnails")] SearchThumbnails? Thumbnails);
    private sealed record SearchThumbnails([property: JsonPropertyName("medium")] SearchImage? Medium);
    private sealed record SearchImage([property: JsonPropertyName("url")] string? Url);
    private sealed record VideosResponse([property: JsonPropertyName("items")] VideoEntry[]? Items);
    private sealed record VideoEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("contentDetails")] VideoContentDetails? ContentDetails);
    private sealed record VideoContentDetails([property: JsonPropertyName("duration")] string? Duration);
}

internal sealed class WindowsCredentialYouTubeApiKeyStore
{
    private const int GenericCredential = 1;
    private const int MaximumApiKeyCharacters = 96;
    private const string Target = "WidgetRail/YouTubeDataApi/v1/widgetrail.samples.youtube-video";

    internal Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (NativeMethods.CredRead(Target, GenericCredential, 0, out var pointer))
        {
            NativeMethods.CredFree(pointer);
            return Task.FromResult(true);
        }
        var error = Marshal.GetLastWin32Error();
        if (error == 1168) return Task.FromResult(false);
        throw Failure("read", error);
    }

    internal Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredRead(Target, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return Task.FromResult<string?>(null);
            throw Failure("read", error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize is < 1 or > MaximumApiKeyCharacters * 4 ||
                credential.CredentialBlob == IntPtr.Zero)
                throw new YouTubeApplicationException("credential_invalid", "The stored API key is invalid.");
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                var value = new UTF8Encoding(false, true).GetString(bytes);
                Validate(value);
                return Task.FromResult<string?>(value);
            }
            catch (DecoderFallbackException exception)
            {
                throw new YouTubeApplicationException(
                    "credential_invalid", "The stored API key is invalid.", exception);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { NativeMethods.CredFree(pointer); }
    }

    internal Task SaveAsync(string apiKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(apiKey);
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = Target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = 2,
                UserName = "WidgetRail YouTube API",
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw Failure("save", Marshal.GetLastWin32Error());
            return Task.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    internal Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredDelete(Target, GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw Failure("delete", error);
        }
        return Task.CompletedTask;
    }

    internal static void Validate(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length is < 20 or > MaximumApiKeyCharacters ||
            apiKey.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new YouTubeApplicationException(
                "api_key_invalid", "Enter a valid bounded Google API key.");
    }

    private static YouTubeApplicationException Failure(string operation, int error) =>
        new("credential_unavailable",
            $"Windows Credential Manager could not {operation} the YouTube API key (error {error}).");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr credential);
    }
}
