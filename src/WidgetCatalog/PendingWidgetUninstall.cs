using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

internal sealed record WidgetUninstallAuthorityCommit(
    bool Committed,
    bool CleanupPending);

internal interface IWidgetUninstallAuthorityParticipant
{
    Task<WidgetUninstallAuthorityCommit> RetirePackageAsync(
        string packageId,
        CancellationToken cancellationToken);
}

internal sealed record PendingWidgetUninstall(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] IReadOnlyList<string> PublisherAuthorities);

internal static class PendingWidgetUninstallStore
{
    internal const int MaximumPendingRecords = 16;
    private const int MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    internal static string MarkerPath(string retiredDirectory) =>
        retiredDirectory + ".pending.json";

    internal static async Task WriteAsync(
        string markerPath,
        PendingWidgetUninstall pending,
        CancellationToken cancellationToken)
    {
        Validate(pending);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pending, Options);
        if (bytes.Length > MaximumBytes)
            throw new WidgetPackageException(
                "pending_uninstall_invalid",
                "Pending uninstall evidence exceeds its bound.");
        var temporary = markerPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, markerPath, overwrite: false);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException) { }
        }
    }

    internal static async Task<PendingWidgetUninstall> ReadAsync(
        string markerPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            markerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumBytes)
            throw Invalid();
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        try
        {
            RejectDuplicateProperties(bytes);
            var pending = JsonSerializer.Deserialize<PendingWidgetUninstall>(
                    bytes, Options)
                ?? throw Invalid();
            Validate(pending);
            if (!bytes.AsSpan().SequenceEqual(
                    JsonSerializer.SerializeToUtf8Bytes(pending, Options)))
                throw Invalid();
            return pending;
        }
        catch (JsonException exception)
        {
            throw Invalid(exception);
        }
    }

    internal static void Validate(PendingWidgetUninstall pending)
    {
        if (pending.SchemaVersion != 1 ||
            pending.PublisherAuthorities is null ||
            pending.PublisherAuthorities.Count is < 1 or > 16 ||
            pending.PublisherAuthorities.Distinct(StringComparer.Ordinal).Count() !=
                pending.PublisherAuthorities.Count ||
            pending.PublisherAuthorities.Any(value =>
                value is not { Length: 73 } ||
                !value.StartsWith("unsigned.", StringComparison.Ordinal) ||
                !value[9..].All(Uri.IsHexDigit)))
            throw Invalid();
        if (!WidgetManifestValidator.IsValidPackageIdentity(pending.PackageId))
            throw Invalid();
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = Options.MaxDepth,
        });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName &&
                     (objects.Count == 0 ||
                      !objects.Peek().Add(reader.GetString() ?? string.Empty)))
                throw new JsonException("Duplicate pending-uninstall property.");
        }
    }

    private static WidgetPackageException Invalid(Exception? inner = null) => new(
        "pending_uninstall_invalid",
        "Pending uninstall evidence is invalid.",
        inner);
}
