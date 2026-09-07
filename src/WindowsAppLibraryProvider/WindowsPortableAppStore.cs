using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>
/// Provider-private package-scoped executable registrations. One canonical
/// document is serialized by an in-process gate and adjacent exclusive lock;
/// no path or file identity crosses broker IPC.
/// </summary>
internal sealed class WindowsPortableAppStore : IWindowsPortableAppStore
{
    internal const int MaximumRegistrations = 64;
    internal const int MaximumEnvelopeBytes = 2 * 1024 * 1024;
    private const int MaximumRetiredCleanupPerOperation = 4;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };
    private readonly string _root;
    private readonly Action? _beforeCrossProcessLockAttempt;

    internal WindowsPortableAppStore(string root)
        : this(root, null)
    {
    }

    internal WindowsPortableAppStore(
        string root,
        Action? beforeCrossProcessLockAttempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _beforeCrossProcessLockAttempt = beforeCrossProcessLockAttempt;
    }

    public async Task<PortableAppRegistrationSnapshot> ReadAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken)
    {
        var paths = Resolve(identity);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var document = await LoadAsync(paths.DataPath, cancellationToken)
            .ConfigureAwait(false);
        return Snapshot(document, identity.PublisherId);
    }

    public async Task<PortableAppRegistrationMutation> UpsertAsync(
        BrokerWidgetIdentity identity,
        PortableAppRegistration registration,
        CancellationToken cancellationToken)
    {
        ValidateRegistration(registration, "invalid_payload");
        var paths = Resolve(identity);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var current = await LoadAsync(paths.DataPath, cancellationToken)
            .ConfigureAwait(false);
        var items = current?.Items.ToList() ?? [];
        var index = items.FindIndex(item =>
            string.Equals(item.PublisherId, identity.PublisherId,
                StringComparison.Ordinal) &&
            string.Equals(item.Registration.SavedId,
                registration.SavedId, StringComparison.Ordinal));
        if (index >= 0 && items[index].Registration == registration)
            return new(Snapshot(current, identity.PublisherId), false);
        if (index < 0 && items.Count >= MaximumRegistrations)
            throw new BrokerException(
                "registration_capacity", "Portable app registration capacity is full.");
        if (items.Any(item =>
                string.Equals(item.PublisherId, identity.PublisherId,
                    StringComparison.Ordinal) &&
                !string.Equals(item.Registration.SavedId,
                    registration.SavedId, StringComparison.Ordinal) &&
                string.Equals(item.Registration.StableIdentity,
                    registration.StableIdentity, StringComparison.OrdinalIgnoreCase)))
            throw new BrokerException(
                "registration_conflict", "Portable app registration identity conflicts.");
        var stored = new StoredRegistration(identity.PublisherId, registration);
        if (index >= 0) items[index] = stored;
        else items.Add(stored);
        var next = Next(current, items);
        await WriteAtomicAsync(paths.DataPath, next, cancellationToken)
            .ConfigureAwait(false);
        return new(Snapshot(next, identity.PublisherId), true);
    }

    public async Task<PortableAppRegistrationMutation> RemoveAsync(
        BrokerWidgetIdentity identity,
        string savedId,
        CancellationToken cancellationToken)
    {
        ValidateSavedId(savedId, "invalid_payload");
        var paths = Resolve(identity);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var current = await LoadAsync(paths.DataPath, cancellationToken)
            .ConfigureAwait(false);
        if (current is null) return new(new(0, []), false);
        var items = current.Items.Where(item =>
            !string.Equals(item.PublisherId, identity.PublisherId,
                StringComparison.Ordinal) ||
            !string.Equals(item.Registration.SavedId,
                savedId, StringComparison.Ordinal)).ToArray();
        if (items.Length == current.Items.Count)
            return new(Snapshot(current, identity.PublisherId), false);
        var next = Next(current, items);
        await WriteAtomicAsync(paths.DataPath, next, cancellationToken)
            .ConfigureAwait(false);
        return new(Snapshot(next, identity.PublisherId), true);
    }

    public async Task ClearAsync(
        BrokerWidgetIdentity identity,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (expectedRevision < 0)
            throw new BrokerException(
                "invalid_payload", "Portable app registration revision is invalid.");
        var paths = Resolve(identity);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var current = await LoadAsync(paths.DataPath, cancellationToken)
            .ConfigureAwait(false);
        var revision = current?.Revision ?? 0;
        if (revision != expectedRevision)
            throw new BrokerException(
                "app_registration_conflict", "Portable app registrations changed.");
        if (current is null || !current.Items.Any(item => string.Equals(
                item.PublisherId, identity.PublisherId, StringComparison.Ordinal)))
            return;
        var next = Next(current, current.Items.Where(item => !string.Equals(
            item.PublisherId, identity.PublisherId, StringComparison.Ordinal)).ToArray());
        await WriteAtomicAsync(paths.DataPath, next, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PortableAppPackageRetirement> RetirePackageAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        var paths = ResolvePackage(packageId);
        await using var authority = await AcquireAsync(paths, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(paths.PackageDirectory))
            return new(
                Committed: true,
                CleanupPending: HasRetiredDirectories(paths.PackageHash));
        string retired;
        try
        {
            RejectReparsePoint(paths.PackageDirectory);
            retired = Path.Combine(
                _root, $".portable-retired-{paths.PackageHash}-{Guid.NewGuid():N}");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(paths.PackageDirectory, retired);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }

        // The namespace move is the irreversible authority commit. Do not
        // observe caller cancellation or throw routine cleanup failures now.
        var cleanupPending = !TryDeleteRetiredDirectory(retired) ||
            HasRetiredDirectories(paths.PackageHash);
        return new(Committed: true, CleanupPending: cleanupPending);
    }

    private static StoreDocument Next(
        StoreDocument? current,
        IReadOnlyList<StoredRegistration> items)
    {
        var revision = current?.Revision ?? 0;
        if (revision == long.MaxValue)
            throw new BrokerException(
                "registration_revision_exhausted",
                "Portable app registration revision cannot advance further.");
        return new StoreDocument(1, revision + 1, items
            .OrderBy(item => item.PublisherId, StringComparer.Ordinal)
            .ThenBy(item => item.Registration.SavedId, StringComparer.Ordinal)
            .ToArray());
    }

    private async Task<AuthorityLease> AcquireAsync(
        AuthorityPaths paths,
        CancellationToken cancellationToken)
    {
        EnsureRoot();
        var processGate = ProcessGates.GetOrAdd(
            paths.DataPath, static _ => new SemaphoreSlim(1, 1));
        await processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectReparsePoint(paths.LockPath);
                try
                {
                    _beforeCrossProcessLockAttempt?.Invoke();
                    var stream = new FileStream(
                        paths.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                        FileShare.None, 1,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    try
                    {
                        RejectReparsePoint(paths.LockPath);
                        TryCleanupRetiredDirectories(paths.PackageHash);
                        return new AuthorityLease(processGate, stream);
                    }
                    catch
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                catch (IOException exception) when (
                    (exception.HResult & 0xffff) is 32 or 33)
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
        string path,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(path);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumEnvelopeBytes) throw Corrupt();
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            RejectDuplicateProperties(bytes);
            var document = JsonSerializer.Deserialize<StoreDocument>(bytes, JsonOptions)
                ?? throw new JsonException("Portable app registration document was null.");
            ValidateDocument(document);
            if (!bytes.AsSpan().SequenceEqual(
                    JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions)))
                throw new JsonException("Portable app registration document was not canonical.");
            return document;
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }
        catch (Exception exception) when (exception is JsonException or
            NotSupportedException)
        {
            throw Corrupt(exception);
        }
    }

    private async Task WriteAtomicAsync(
        string path,
        StoreDocument document,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumEnvelopeBytes)
            throw new BrokerException(
                "registration_store_unavailable",
                "Portable app registrations exceed their host storage bound.");
        var directory = Path.GetDirectoryName(path)
            ?? throw new BrokerException(
                "registration_store_unavailable",
                "Portable app registration storage is unavailable.");
        var temporary = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            RejectReparsePoint(directory);
            RejectReparsePoint(path);
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(path);
            File.Move(temporary, path, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
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
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException) { }
        }
    }

    private AuthorityPaths Resolve(BrokerWidgetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        return ResolvePackage(identity.PackageId);
    }

    private AuthorityPaths ResolvePackage(string packageId)
    {
        new BrokerWidgetIdentity(packageId, packageId, "cleanup").Validate();
        var packageHash = Hash(packageId);
        var packageDirectory = Path.Combine(_root, packageHash);
        return new(
            packageHash,
            packageDirectory,
            Path.Combine(packageDirectory, "registrations.json"),
            Path.Combine(_root, packageHash + ".lock"));
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private void EnsureRoot()
    {
        try
        {
            var cursor = new DirectoryInfo(_root);
            while (cursor is not null)
            {
                if (cursor.Exists) RejectReparsePoint(cursor.FullName);
                cursor = cursor.Parent;
            }
            Directory.CreateDirectory(_root);
            cursor = new DirectoryInfo(_root);
            while (cursor is not null)
            {
                RejectReparsePoint(cursor.FullName);
                cursor = cursor.Parent;
            }
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            throw Unavailable(exception);
        }
    }

    private static void ValidateDocument(StoreDocument document)
    {
        if (document.SchemaVersion != 1 || document.Revision <= 0 ||
            document.Items is null || document.Items.Count > MaximumRegistrations ||
            document.Items.Select(item => item.PublisherId + "\0" +
                    item.Registration.SavedId)
                .Distinct(StringComparer.Ordinal).Count() != document.Items.Count ||
            document.Items.Select(item => item.PublisherId + "\0" +
                    item.Registration.StableIdentity)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Items.Count)
            throw Corrupt();
        foreach (var item in document.Items)
        {
            ValidatePublisherId(item.PublisherId);
            ValidateRegistration(item.Registration, "registration_store_corrupt");
        }
    }

    private static void ValidatePublisherId(string publisherId)
    {
        try
        {
            new BrokerWidgetIdentity(
                "dev.placeholder.registration", publisherId, "validation").Validate();
        }
        catch (BrokerException exception)
        {
            throw Corrupt(exception);
        }
    }

    private static void ValidateRegistration(
        PortableAppRegistration registration,
        string code)
    {
        if (registration is null ||
            registration.DisplayName is not { Length: > 0 and <= 120 } ||
            registration.DisplayName.Any(char.IsControl) ||
            registration.ExecutablePath is not { Length: > 0 and <=
                WindowsPortableAppRegistrationPaths.MaximumExecutablePathCharacters } ||
            !Path.IsPathFullyQualified(registration.ExecutablePath) ||
            registration.FileIdentity is null)
            throw new BrokerException(code, "Portable app registration is invalid.");
        ValidateSavedId(registration.SavedId, code);
        ValidateStableIdentity(registration.StableIdentity, code);
    }

    internal static void ValidateStableIdentity(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            value.Any(char.IsControl))
            throw new BrokerException(code, "Stable provider identity is invalid.");
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = JsonOptions.MaxDepth,
        });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString() ?? string.Empty;
                if (objects.Count == 0 || !objects.Peek().Add(name))
                    throw new JsonException("Duplicate JSON property.");
            }
        }
    }

    private static void ValidateSavedId(string savedId, string code)
    {
        if (savedId is not { Length: > 6 and <= 128 } ||
            !savedId.StartsWith("saved-", StringComparison.Ordinal) ||
            savedId.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.'))
            throw new BrokerException(code, "Saved app identifier is invalid.");
    }

    private static PortableAppRegistrationSnapshot Snapshot(
        StoreDocument? document,
        string publisherId) =>
        document is null
            ? new(0, [])
            : new(document.Revision, document.Items
                .Where(item => string.Equals(
                    item.PublisherId, publisherId, StringComparison.Ordinal))
                .Select(item => item.Registration).ToArray());

    private void TryCleanupRetiredDirectories(string packageHash)
    {
        try
        {
            foreach (var path in Directory.EnumerateDirectories(
                         _root,
                         $".portable-retired-{packageHash}-*",
                         SearchOption.TopDirectoryOnly)
                     .Take(MaximumRetiredCleanupPerOperation))
                _ = TryDeleteRetiredDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException) { }
    }

    private bool HasRetiredDirectories(string packageHash)
    {
        try
        {
            return Directory.EnumerateDirectories(
                _root,
                $".portable-retired-{packageHash}-*",
                SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool TryDeleteRetiredDirectory(string path)
    {
        try
        {
            RejectReparsePoint(path);
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or BrokerException)
        {
            return false;
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new BrokerException(
                "unsafe_registration_store",
                "Portable app registration store path is unsafe.");
    }

    private static BrokerException Corrupt(Exception? inner = null) => new(
        "registration_store_corrupt",
        "Portable app registrations are corrupt and were not modified.", inner);

    private static BrokerException Unavailable(Exception inner) => new(
        "registration_store_unavailable",
        "Portable app registration storage is unavailable.", inner);

    private sealed record StoreDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] long Revision,
        [property: JsonRequired] IReadOnlyList<StoredRegistration> Items);

    private sealed record StoredRegistration(
        [property: JsonRequired] string PublisherId,
        [property: JsonRequired] PortableAppRegistration Registration);

    private sealed record AuthorityPaths(
        string PackageHash,
        string PackageDirectory,
        string DataPath,
        string LockPath);

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
                if (stream is not null)
                    await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                gate?.Release();
            }
        }
    }
}
