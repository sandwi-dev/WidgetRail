using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.PlatformBroker;

public sealed record ConsentEntry(
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] string PublisherId,
    [property: JsonRequired] string CapabilityId,
    [property: JsonRequired] ConsentDecision Decision);

public sealed record ConsentDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] long Revision,
    [property: JsonRequired] IReadOnlyList<ConsentEntry> Entries)
{
    public static ConsentDocument Empty { get; } = new(1, 0, []);
}

/// <summary>
/// Host-owned, strict, atomic consent persistence. Filesystem paths remain an
/// implementation detail and are never included in public DTOs or diagnostics.
/// </summary>
public sealed class ConsentStore
{
    public const int MaximumDocumentBytes = 128 * 1024;
    public const int MaximumEntries = 1024;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(25);
    private readonly string _root;
    private readonly string _documentFile;
    private readonly string _lockFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal string RootDirectory => _root;
    internal string DocumentFile => _documentFile;

    public ConsentStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Consent root is required.", nameof(rootDirectory));
        _root = Path.GetFullPath(rootDirectory);
        _documentFile = Path.Combine(_root, "consent-v1.json");
        _lockFile = Path.Combine(_root, ".consent.lock");
    }

    public async Task<ConsentDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_documentFile)) return ConsentDocument.Empty;
        EnsureSafeExistingPath(_root);
        RejectReparsePoint(_documentFile);
        var bytes = await ReadBoundedAsync(_documentFile, cancellationToken).ConfigureAwait(false);
        try
        {
            BrokerJson.RejectDuplicateProperties(bytes);
            var document = JsonSerializer.Deserialize<ConsentDocument>(
                bytes, BrokerJson.StrictOptions) ?? throw new JsonException("Consent was null.");
            Validate(document);
            return document;
        }
        catch (BrokerException) { throw; }
        catch (JsonException exception)
        {
            throw new BrokerException("invalid_consent", "Consent document is invalid.", exception);
        }
    }

    public async Task<ConsentDocument> SetDecisionAsync(
        BrokerWidgetIdentity identity,
        string capabilityId,
        ConsentDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        if (!PlatformCapabilities.TryGet(capabilityId, out _))
            throw new BrokerException("unsupported_capability", "Capability is unsupported.");
        if (!Enum.IsDefined(decision))
            throw new BrokerException("invalid_consent", "Consent decision is invalid.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            EnsureSafeExistingPath(_root);
            await using var crossProcess = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var entries = current.Entries
                .Where(entry => !(entry.PackageId == identity.PackageId &&
                                  entry.PublisherId == identity.PublisherId &&
                                  entry.CapabilityId == capabilityId))
                .ToList();
            entries.Add(new(identity.PackageId, identity.PublisherId, capabilityId, decision));
            entries.Sort(CompareEntries);
            var updated = new ConsentDocument(1, checked(current.Revision + 1), entries);
            Validate(updated);
            await SaveAtomicAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConsentDecision?> GetDecisionAsync(
        BrokerWidgetIdentity identity,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        identity.Validate();
        if (!PlatformCapabilities.TryGet(capabilityId, out _)) return null;
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Entries.FirstOrDefault(entry =>
            entry.PackageId == identity.PackageId &&
            entry.PublisherId == identity.PublisherId &&
            entry.CapabilityId == capabilityId)?.Decision;
    }

    internal void PrepareForMonitoring()
    {
        Directory.CreateDirectory(_root);
        EnsureSafeExistingPath(_root);
        RejectReparsePoint(_documentFile);
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RejectReparsePoint(_lockFile);
                return new FileStream(_lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(LockRetry, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new BrokerException("consent_busy", "Consent store is busy.", exception);
            }
        }
    }

    private async Task SaveAtomicAsync(ConsentDocument document, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, BrokerJson.StrictOptions);
        if (bytes.Length > MaximumDocumentBytes)
            throw new BrokerException("consent_too_large", "Consent document exceeds its bound.");
        var temporary = Path.Combine(_root, $".consent.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _documentFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(ConsentDocument document)
    {
        if (document.SchemaVersion != 1 || document.Revision < 0 ||
            document.Entries is null || document.Entries.Count > MaximumEntries)
            throw new BrokerException("invalid_consent", "Consent document bounds are invalid.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            if (entry is null)
                throw new BrokerException("invalid_consent", "Consent entry is invalid.");
            new BrokerWidgetIdentity(entry.PackageId, entry.PublisherId, "consent").Validate();
            if (!PlatformCapabilities.TryGet(entry.CapabilityId, out _) ||
                !Enum.IsDefined(entry.Decision) ||
                !keys.Add(entry.PackageId + "\n" + entry.PublisherId + "\n" + entry.CapabilityId))
                throw new BrokerException("invalid_consent", "Consent entry is invalid or duplicated.");
        }
    }

    private static int CompareEntries(ConsentEntry left, ConsentEntry right)
    {
        var comparison = StringComparer.Ordinal.Compare(left.PackageId, right.PackageId);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(left.PublisherId, right.PublisherId);
        return comparison != 0 ? comparison :
            StringComparer.Ordinal.Compare(left.CapabilityId, right.CapabilityId);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaximumDocumentBytes)
                throw new BrokerException("consent_too_large", "Consent document exceeds its bound.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void EnsureSafeExistingPath(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            RejectReparsePoint(current.FullName);
            current = current.Parent;
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new BrokerException("unsafe_consent_store", "Consent store path is unsafe.");
    }
}
