using System.Text;
using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal readonly record struct LoopbackCapabilityRequest(
    int Port,
    bool IsPost,
    LoopbackJsonRequest Request);

internal static class LoopbackCapabilityPolicy
{
    internal static LoopbackCapabilityRequest Parse(
        string capabilityId,
        string operation,
        JsonElement payload)
    {
        if (!PlatformCapabilities.TryGetLoopbackPort(capabilityId, out var port))
            throw new BrokerException(
                "invalid_declaration", "Loopback port declaration is invalid.");
        var isPost = operation switch
        {
            PlatformCapabilities.LoopbackHttpGetJson => false,
            PlatformCapabilities.LoopbackHttpPostJson => true,
            _ => throw new BrokerException(
                "unsupported_operation", "Loopback operation is unsupported."),
        };
        var request = BrokerJson.ParsePayload<LoopbackJsonRequest>(payload);
        ValidateRequest(request, isPost);
        return new LoopbackCapabilityRequest(port, isPost, request);
    }

    internal static LoopbackJsonResponse ValidateResponse(LoopbackJsonResponse? response)
    {
        if (response is null || response.StatusCode is < 100 or > 599 ||
            response.Headers is null)
            throw new BrokerException(
                "invalid_backend_data", "Loopback HTTP response is invalid.");
        var canonicalJsonBody = ValidateAndCanonicalizeJsonBody(
            response.JsonBody,
            CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes,
            "invalid_backend_data",
            allowEmpty: response.StatusCode is 204 or 205);
        ValidateHeaders(response.Headers, "invalid_backend_data", requestHeaders: false);
        return response with
        {
            JsonBody = canonicalJsonBody,
            Headers = Array.AsReadOnly(response.Headers.ToArray()),
        };
    }

    internal static void ValidateRequest(LoopbackJsonRequest? request, bool isPost)
    {
        if (request is null || request.Headers is null ||
            request.TimeoutMilliseconds is < 1 or
                > CommunityPlatformLimits.MaximumLoopbackTimeoutMilliseconds ||
            !IsSafeOriginFormPath(request.Path) ||
            request.BearerSecretSlot is { } slot &&
                !PrivateSecretCapabilityDomain.IsValidSlot(slot) ||
            request.InvalidateBearerSecretOnUnauthorized &&
                request.BearerSecretSlot is null ||
            isPost != (request.JsonBody is not null))
            throw new BrokerException(
                "invalid_payload", "Loopback HTTP request is invalid.");
        ValidateHeaders(request.Headers, "invalid_payload", requestHeaders: true);
        if (request.JsonBody is { } json)
            ValidateJsonBody(
                json,
                CommunityPlatformLimits.MaximumLoopbackRequestBodyUtf8Bytes,
                "invalid_payload");
    }

    private static void ValidateHeaders(
        IReadOnlyList<LoopbackHttpHeader> headers,
        string errorCode,
        bool requestHeaders)
    {
        if (headers.Count > CommunityPlatformLimits.MaximumLoopbackHeaderCount)
            throw new BrokerException(errorCode, "Loopback HTTP headers are invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characters = 0;
        foreach (var header in headers)
        {
            if (header is null || string.IsNullOrEmpty(header.Name) ||
                header.Name.Length >
                    CommunityPlatformLimits.MaximumLoopbackHeaderNameCharacters ||
                header.Value is null ||
                header.Value.Length >
                    CommunityPlatformLimits.MaximumLoopbackHeaderValueCharacters ||
                !header.Name.All(IsHttpTokenCharacter) ||
                header.Value.Any(character =>
                    character is '\r' or '\n' || char.IsControl(character)) ||
                !names.Add(header.Name) ||
                requestHeaders && IsRestrictedRequestHeader(header.Name) ||
                !requestHeaders && !IsProjectedResponseHeader(header.Name))
                throw new BrokerException(errorCode, "Loopback HTTP headers are invalid.");
            characters += header.Name.Length + header.Value.Length;
            if (characters > CommunityPlatformLimits.MaximumLoopbackHeaderCharacters)
                throw new BrokerException(errorCode, "Loopback HTTP headers are invalid.");
        }
    }

    private static bool IsSafeOriginFormPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        path.Length <= CommunityPlatformLimits.MaximumLoopbackPathCharacters &&
        path[0] == '/' &&
        !path.StartsWith("//", StringComparison.Ordinal) &&
        !path.Contains('\\') &&
        !path.Contains('#') &&
        !path.Any(character => character is '\r' or '\n' || char.IsControl(character)) &&
        Uri.TryCreate(path, UriKind.Relative, out _);

    private static bool IsRestrictedRequestHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Expect", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectedResponseHeader(string name) =>
        name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ETag", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("X-RateLimit-", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) ||
        character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or
            '.' or '^' or '_' or '`' or '|' or '~';

    private static void ValidateJsonBody(
        string? json,
        int maximumUtf8Bytes,
        string errorCode,
        bool allowEmpty = false)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maximumUtf8Bytes ||
            !allowEmpty && json.Length == 0)
            throw new BrokerException(errorCode, "Loopback JSON body is invalid.");
        if (allowEmpty && json.Length == 0) return;
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BrokerJson.MaximumDepth,
            });
        }
        catch (JsonException exception)
        {
            throw new BrokerException(
                errorCode, "Loopback JSON body is invalid.", exception);
        }
    }

    private static string ValidateAndCanonicalizeJsonBody(
        string? json,
        int maximumUtf8Bytes,
        string errorCode,
        bool allowEmpty = false)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maximumUtf8Bytes ||
            !allowEmpty && json.Length == 0)
            throw new BrokerException(errorCode, "Loopback JSON body is invalid.");
        if (allowEmpty && json.Length == 0) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BrokerJson.MaximumDepth,
            });
            var canonical = JsonSerializer.Serialize(
                document.RootElement, BrokerJson.StrictOptions);
            if (Encoding.UTF8.GetByteCount(canonical) > maximumUtf8Bytes)
                throw new BrokerException(errorCode, "Loopback JSON body is invalid.");
            return canonical;
        }
        catch (JsonException exception)
        {
            throw new BrokerException(
                errorCode, "Loopback JSON body is invalid.", exception);
        }
    }
}
