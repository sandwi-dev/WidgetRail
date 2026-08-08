using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsCommunityProvider;

internal interface IPrivateStateStore
{
    Task<PrivateStateSnapshotSummary> ReadAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken);
    Task<PrivateStateMutationSummary> WriteAsync(
        BrokerWidgetIdentity identity, WritePrivateStateRequest request,
        CancellationToken cancellationToken);
    Task<PrivateStateMutationSummary> ClearAsync(
        BrokerWidgetIdentity identity, ClearPrivateStateRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Windows host store for one bounded document per publisher/package authority.
/// An in-process gate limits contention; an adjacent exclusive lock file is the
/// cross-process serialization authority for read/CAS/write/clear.
/// </summary>
internal sealed class WindowsPrivateStateStore : IPrivateStateStore
{
    internal const int WriteBurstCapacity = 8;
    internal const int WriteTokenUnits = 1_000;
    internal const int RefillUnitsPerMillisecond = 1; // one write per second
    private const int MaximumEnvelopeBytes = 96 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _root;
    private readonly TimeProvider _timeProvider;

    internal WindowsPrivateStateStore(string root, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PrivateStateSnapshotSummary> ReadAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        var paths = Resolve(identity);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        var document = await LoadAsync(paths.DataPath, cancellationToken).ConfigureAwait(false);
        return document is null
            ? new PrivateStateSnapshotSummary(false, null, 0)
            : new PrivateStateSnapshotSummary(
                document.Exists, document.CanonicalJsonBase64, document.Revision);
    }

    public async Task<PrivateStateMutationSummary> WriteAsync(
        BrokerWidgetIdentity identity,
        WritePrivateStateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = PrivateStateJsonCodec.DecodeCanonicalBase64(
            request.CanonicalJsonBase64, "invalid_payload");
        var canonicalBase64 = Convert.ToBase64String(canonical);
        return await MutateAsync(identity, request.ExpectedRevision, canonicalBase64,
            exists: true, cancellationToken).ConfigureAwait(false);
    }

    public Task<PrivateStateMutationSummary> ClearAsync(
        BrokerWidgetIdentity identity,
        ClearPrivateStateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(identity, request.ExpectedRevision, canonicalJsonBase64: null,
            exists: false, cancellationToken);
    }

    private async Task<PrivateStateMutationSummary> MutateAsync(
        BrokerWidgetIdentity identity,
        long? expectedRevision,
        string? canonicalJsonBase64,
        bool exists,
        CancellationToken cancellationToken)
    {
        if (expectedRevision < 0)
            throw new BrokerException("invalid_payload", "Expected state revision is invalid.");
        var paths = Resolve(identity);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        var current = await LoadAsync(paths.DataPath, cancellationToken).ConfigureAwait(false);
        var currentRevision = current?.Revision ?? 0;
        if (expectedRevision is { } expected && expected != currentRevision)
            throw new BrokerException("state_conflict", "Private state changed before this update.");
        if (currentRevision == long.MaxValue)
            throw new BrokerException(
                "state_revision_exhausted", "Private state revision cannot advance further.");

        var now = Math.Max(0, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        var rate = Refill(current, now);
        if (rate.AvailableUnits < WriteTokenUnits)
            throw new BrokerException(
                "state_rate_limited", "Private state is being updated too frequently.");
        var next = new StoreDocument(
            SchemaVersion: 1,
            Revision: currentRevision + 1,
            Exists: exists,
            CanonicalJsonBase64: canonicalJsonBase64,
            RateUnits: rate.AvailableUnits - WriteTokenUnits,
            RateTimestampUnixMilliseconds: rate.TimestampUnixMilliseconds);
        await WriteAtomicAsync(paths.DataPath, next, cancellationToken).ConfigureAwait(false);
        return new PrivateStateMutationSummary(next.Revision);
    }

    private static RateState Refill(StoreDocument? current, long now)
    {
        var capacity = WriteBurstCapacity * WriteTokenUnits;
        if (current is null) return new RateState(capacity, now);
        var timestamp = Math.Max(current.RateTimestampUnixMilliseconds, now);
        var elapsed = Math.Max(0, now - current.RateTimestampUnixMilliseconds);
        var refill = elapsed > capacity / RefillUnitsPerMillisecond
            ? capacity
            : checked((int)elapsed * RefillUnitsPerMillisecond);
        return new RateState(Math.Min(capacity, current.RateUnits + refill), timestamp);
    }

    private async Task<AuthorityLease> AcquireAsync(
        AuthorityPaths paths, CancellationToken cancellationToken)
    {
        EnsureRoot();
        var processGate = ProcessGates.GetOrAdd(paths.DataPath, static _ => new SemaphoreSlim(1, 1));
        await processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectReparsePoint(paths.LockPath);
                try
                {
                    var stream = new FileStream(paths.LockPath, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None, 1,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    try
                    {
                        RejectReparsePoint(paths.LockPath);
                        return new AuthorityLease(processGate, stream);
                    }
                    catch
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                catch (IOException exception) when (IsSharingViolation(exception))
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            processGate.Release();
            throw;
        }
    }

    private async Task<StoreDocument?> LoadAsync(
        string path, CancellationToken cancellationToken)
    {
        RejectReparsePoint(path);
        if (!File.Exists(path)) return null;
        byte[] bytes;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumEnvelopeBytes)
                throw Corrupt();
            bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }

        try
        {
            BrokerJson.RejectDuplicateProperties(bytes);
            var document = JsonSerializer.Deserialize<StoreDocument>(bytes, JsonOptions) ??
                throw new JsonException("Private state envelope was null.");
            Validate(document);
            var canonical = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (!bytes.AsSpan().SequenceEqual(canonical)) throw new JsonException(
                "Private state envelope was not canonical.");
            return document;
        }
        catch (BrokerException exception) when (exception.Code == "invalid_backend_data")
        {
            throw Corrupt(exception);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw Corrupt(exception);
        }
    }

    private async Task WriteAtomicAsync(
        string path, StoreDocument document, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumEnvelopeBytes) throw new BrokerException(
            "state_store_unavailable", "Private state envelope exceeds its host bound.");
        var temporary = Path.Combine(_root,
            $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            RejectReparsePoint(path);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                         FileAccess.Write, FileShare.None, 16 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(path);
            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary) &&
                    (File.GetAttributes(temporary) & FileAttributes.ReparsePoint) == 0)
                    File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private AuthorityPaths Resolve(BrokerWidgetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        var authority = Encoding.UTF8.GetBytes(
            string.Concat(identity.PublisherId, "\0", identity.PackageId));
        var hash = Convert.ToHexString(SHA256.HashData(authority)).ToLowerInvariant();
        return new AuthorityPaths(
            Path.Combine(_root, $"{hash}.json"),
            Path.Combine(_root, $"{hash}.lock"));
    }

    private void EnsureRoot()
    {
        try
        {
            var existing = new DirectoryInfo(_root);
            while (existing is not null)
            {
                if (existing.Exists) RejectReparsePoint(existing.FullName);
                existing = existing.Parent;
            }
            Directory.CreateDirectory(_root);
            existing = new DirectoryInfo(_root);
            while (existing is not null)
            {
                RejectReparsePoint(existing.FullName);
                existing = existing.Parent;
            }
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }
    }

    private static void Validate(StoreDocument document)
    {
        if (document.SchemaVersion != 1 || document.Revision <= 0 ||
            document.Exists != (document.CanonicalJsonBase64 is not null) ||
            document.RateUnits is < 0 or > WriteBurstCapacity * WriteTokenUnits ||
            document.RateTimestampUnixMilliseconds < 0)
            throw Corrupt();
        if (document.CanonicalJsonBase64 is not null)
            _ = PrivateStateJsonCodec.DecodeCanonicalBase64(
                document.CanonicalJsonBase64, "invalid_backend_data");
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new BrokerException("unsafe_state_store", "Private state store path is unsafe.");
    }

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xffff) is 32 or 33;

    private static BrokerException Corrupt(Exception? inner = null) => new(
        "state_corrupt", "Private state is corrupt and was not modified.", inner);

    private static BrokerException Unavailable(Exception inner) => new(
        "state_store_unavailable", "Private state storage is unavailable.", inner);

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    private sealed record StoreDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] long Revision,
        [property: JsonRequired] bool Exists,
        string? CanonicalJsonBase64,
        [property: JsonRequired] int RateUnits,
        [property: JsonRequired] long RateTimestampUnixMilliseconds);

    private sealed record AuthorityPaths(string DataPath, string LockPath);
    private sealed record RateState(int AvailableUnits, long TimestampUnixMilliseconds);

    private sealed class AuthorityLease(
        SemaphoreSlim processGate,
        FileStream crossProcessLock) : IAsyncDisposable
    {
        private SemaphoreSlim? _processGate = processGate;
        private FileStream? _crossProcessLock = crossProcessLock;

        public async ValueTask DisposeAsync()
        {
            var stream = Interlocked.Exchange(ref _crossProcessLock, null);
            var gate = Interlocked.Exchange(ref _processGate, null);
            try
            {
                if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally { gate?.Release(); }
        }
    }
}
