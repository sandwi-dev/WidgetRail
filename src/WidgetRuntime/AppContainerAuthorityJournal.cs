using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CA1416 // Created only by the WindowsAppContainer admission path.

namespace GameBarAlternative.WidgetRuntime;

internal interface IAppContainerAuthorityJournal
{
    IAppContainerAuthorityJournalLease Acquire(string profileName);
}

internal interface IAppContainerAuthorityJournalLease : IDisposable
{
    IReadOnlyList<AppContainerAuthoritySnapshot>? ReadPending();
    void WritePending(IReadOnlyList<AppContainerAuthoritySnapshot> snapshots);
    void ClearPending();
}

internal sealed class FileAppContainerAuthorityJournal : IAppContainerAuthorityJournal
{
    private const int SchemaVersion = 1;
    private const int MaximumTargets = 2_049;
    private const int MaximumPathCharacters = 32_767;
    private const int MaximumDescriptorCharacters = 65_536;
    private const int MaximumDocumentBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Lazy<FileAppContainerAuthorityJournal> DefaultValue =
        new(CreateDefaultCore, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly string _rootPath;
    private readonly TimeSpan _lockTimeout;

    internal FileAppContainerAuthorityJournal(
        string rootPath,
        TimeSpan? lockTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
        if (_lockTimeout <= TimeSpan.Zero || _lockTimeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(lockTimeout));
    }

    internal static FileAppContainerAuthorityJournal Default => DefaultValue.Value;
    internal string RootPath => _rootPath;

    public IAppContainerAuthorityJournalLease Acquire(string profileName)
    {
        ValidateProfileName(profileName);
        string lockPath;
        string pendingPath;
        try
        {
            EnsureProtectedRoot();
            lockPath = Path.Combine(_rootPath, ".authority.lock");
            pendingPath = Path.Combine(_rootPath, ".authority.pending.json");
            RejectUnsafeEntry(lockPath);
            RejectUnsafeEntry(pendingPath);
        }
        catch (AppContainerAuthorityJournalException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new AppContainerAuthorityJournalException(
                "The content-authority journal root is unavailable.", exception);
        }

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
                try
                {
                    RejectUnsafeEntry(lockPath);
                    return new Lease(stream, pendingPath, profileName);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException) when (stopwatch.Elapsed < _lockTimeout)
            {
                Thread.Sleep(LockRetry);
            }
            catch (IOException exception)
            {
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal is busy or unavailable.", exception);
            }
        }
    }

    private void EnsureProtectedRoot()
    {
        RejectExistingAncestorReparsePoints(_rootPath);
        Directory.CreateDirectory(_rootPath);
        RejectExistingAncestorReparsePoints(_rootPath);

        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new AppContainerAuthorityJournalException(
                "The desktop host identity is unavailable.");
        var security = new DirectoryInfo(_rootPath)
            .GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: false,
                     targetType: typeof(SecurityIdentifier)))
        {
            security.RemoveAccessRuleSpecific(rule);
        }
        security.SetAccessRule(FullControl(identity));
        security.SetAccessRule(FullControl(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        security.SetAccessRule(FullControl(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
        new DirectoryInfo(_rootPath).SetAccessControl(security);
    }

    private static FileSystemAccessRule FullControl(SecurityIdentifier identity) =>
        new(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

    private static void ValidateProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) || profileName.Length > 128 ||
            profileName.Any(character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
                    >= '0' and <= '9' or '.' or '-' or '_')))
        {
            throw new AppContainerAuthorityJournalException(
                "The content-authority journal profile name is invalid.");
        }
    }

    private static void RejectUnsafeEntry(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        if ((attributes & FileAttributes.Directory) != 0)
            throw new AppContainerAuthorityJournalException(
                "A content-authority journal entry has an invalid shape.");
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new AppContainerAuthorityJournalException(
                "Content-authority journal entries cannot be reparse points.");
        }
    }

    private static void RejectExistingAncestorReparsePoints(string path)
    {
        var existing = Path.GetFullPath(path);
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            existing = Path.GetDirectoryName(existing)
                ?? throw new AppContainerAuthorityJournalException(
                    "The content-authority journal path has no existing root.");
        }
        for (var current = new DirectoryInfo(existing);
             current is not null;
             current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal path cannot contain a reparse point.");
        }
    }

    private static FileAppContainerAuthorityJournal CreateDefaultCore()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new AppContainerAuthorityJournalException(
                "The host Local Application Data directory is unavailable.");
        return new FileAppContainerAuthorityJournal(
            Path.Combine(localAppData, "GameBarAlternative", "AuthorityTransactions"));
    }

    private sealed class Lease(
        FileStream lockStream,
        string pendingPath,
        string profileName) : IAppContainerAuthorityJournalLease
    {
        private FileStream? _lockStream = lockStream;

        public IReadOnlyList<AppContainerAuthoritySnapshot>? ReadPending()
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            RejectUnsafeEntry(pendingPath);
            if (!File.Exists(pendingPath)) return null;
            try
            {
                using var stream = new FileStream(
                    pendingPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    FileOptions.SequentialScan);
                if (stream.Length is <= 0 or > MaximumDocumentBytes)
                    throw new InvalidDataException(
                        "The content-authority journal has an invalid size.");
                var bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                var document = JsonSerializer.Deserialize<JournalDocument>(bytes, JsonOptions)
                    ?? throw new InvalidDataException(
                        "The content-authority journal is empty.");
                ValidateDocument(document, expectedProfileName: null);
                return document.Snapshots.AsReadOnly();
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException and
                not AppContainerAuthorityJournalException)
            {
                throw new AppContainerAuthorityJournalException(
                    "The pending content-authority journal is invalid.", exception);
            }
        }

        public void WritePending(IReadOnlyList<AppContainerAuthoritySnapshot> snapshots)
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            ArgumentNullException.ThrowIfNull(snapshots);
            var document = new JournalDocument(
                SchemaVersion, profileName, snapshots.ToList());
            ValidateDocument(document, profileName);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (bytes.Length > MaximumDocumentBytes)
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal exceeds its byte limit.");
            RejectUnsafeEntry(pendingPath);
            if (File.Exists(pendingPath))
                throw new AppContainerAuthorityJournalException(
                    "A pending content-authority transaction already exists.");
            try
            {
                using var stream = new FileStream(
                    pendingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    FileOptions.WriteThrough);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException and
                not AppContainerAuthorityJournalException)
            {
                throw new AppContainerAuthorityJournalException(
                    "The pending content-authority journal could not be persisted.",
                    exception);
            }
        }

        public void ClearPending()
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            RejectUnsafeEntry(pendingPath);
            try
            {
                File.Delete(pendingPath);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException and
                not AppContainerAuthorityJournalException)
            {
                throw new AppContainerAuthorityJournalException(
                    "The pending content-authority journal could not be cleared.",
                    exception);
            }
        }

        public void Dispose() => Interlocked.Exchange(ref _lockStream, null)?.Dispose();

        private static void ValidateDocument(
            JournalDocument document,
            string? expectedProfileName)
        {
            if (document.Version != SchemaVersion)
            {
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal version is invalid.");
            }
            ValidateProfileName(document.ProfileName);
            if (expectedProfileName is not null &&
                !string.Equals(
                    document.ProfileName, expectedProfileName, StringComparison.Ordinal))
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal owner is invalid.");
            if (document.Snapshots is null ||
                document.Snapshots.Count is 0 or > MaximumTargets)
            {
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal target count is invalid.");
            }

            var unique = new HashSet<AppContainerAuthorityTarget>();
            long maximumSerializedBytes = 512L + document.ProfileName.Length * 6L;
            foreach (var snapshot in document.Snapshots)
            {
                if (!Enum.IsDefined(snapshot.Target.Kind) ||
                    string.IsNullOrWhiteSpace(snapshot.Target.Path) ||
                    snapshot.Target.Path.Length > MaximumPathCharacters ||
                    !Path.IsPathFullyQualified(snapshot.Target.Path) ||
                    !string.Equals(
                        snapshot.Target.Path,
                        Path.GetFullPath(snapshot.Target.Path),
                        StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(snapshot.AccessDescriptor) ||
                    snapshot.AccessDescriptor.Length > MaximumDescriptorCharacters ||
                    !unique.Add(snapshot.Target))
                {
                    throw new AppContainerAuthorityJournalException(
                        "The content-authority journal contains an invalid target.");
                }
                maximumSerializedBytes = checked(
                    maximumSerializedBytes +
                    (snapshot.Target.Path.Length + snapshot.AccessDescriptor.Length) * 6L +
                    256L);
                if (maximumSerializedBytes > MaximumDocumentBytes)
                    throw new AppContainerAuthorityJournalException(
                        "The content-authority journal exceeds its byte limit.");
            }
        }
    }

    private sealed record JournalDocument(
        int Version,
        string ProfileName,
        List<AppContainerAuthoritySnapshot> Snapshots);
}

internal sealed class AppContainerAuthorityJournalException : Exception
{
    internal AppContainerAuthorityJournalException(string message) : base(message) { }
    internal AppContainerAuthorityJournalException(string message, Exception innerException)
        : base(message, innerException) { }
}
