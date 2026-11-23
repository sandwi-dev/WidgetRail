using System.Net;
using System.Net.Http.Headers;

namespace WidgetRail.RemoteMediaTransport;

internal sealed record PublicMediaRange(byte[] Bytes, Uri EffectiveUri, long Length, string? Version, string? MediaType, bool Complete);

public sealed partial class PublicMediaClient
{
    internal Task<PublicMediaRange> ReadRangeAsync(Uri source, PublicMediaPolicy policy, long offset, int count,
        long maximumLength, int maximumFallbackBytes, long? expectedLength, string? version, CancellationToken token)
    {
        policy.Demand(source);
        if (offset < 0 || count is < 1 or > 1024 * 1024 || maximumLength is < 1 or > 512L * 1024 * 1024 ||
            offset >= maximumLength || maximumFallbackBytes is < 1 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var end = Math.Min(maximumLength - 1, offset + count - 1);
        return ExecuteAsync(async cancellation =>
        {
            var result = await SendAsync(source, policy, cancellation, new(offset, end), version).ConfigureAwait(false);
            using var response = result.Response;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // A changed If-Range resource must never replace a previously read
                // byte range. Non-range servers are supported only as small, owned
                // whole-file snapshots, not unbounded media buffering.
                if (offset != 0 || version is not null || expectedLength is not null)
                    throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
                var all = await ReadBodyAsync(response, (int)Math.Min(maximumLength, maximumFallbackBytes), cancellation).ConfigureAwait(false);
                return new PublicMediaRange(all, result.EffectiveUri, all.Length, null, response.Content.Headers.ContentType?.MediaType, true);
            }
            var range = response.Content.Headers.ContentRange;
            if (range?.Length > maximumLength) throw new PublicMediaException(PublicMediaFailure.TooLarge);
            if (range is not { HasRange: true, HasLength: true, From: { } start, To: { } last, Length: { } length } ||
                !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) || start != offset ||
                length < 1 || length > maximumLength || last != Math.Min(end, length - 1) || last < start ||
                expectedLength is { } expected && length != expected)
                throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
            var currentVersion = response.Headers.ETag is { IsWeak: false } etag && etag.Tag.Length <= 256 ? etag.Tag : null;
            if (version is not null && version != currentVersion)
                throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
            var expectedBytes = checked((int)(last - start + 1));
            var bytes = await ReadBodyAsync(response, expectedBytes, cancellation).ConfigureAwait(false);
            if (bytes.Length != expectedBytes) throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
            return new PublicMediaRange(bytes, result.EffectiveUri, length, currentVersion, response.Content.Headers.ContentType?.MediaType,
                offset == 0 && bytes.Length == length);
        }, token);
    }
}
