using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CA1416 // Created only by the WindowsAppContainer admission path.

namespace WidgetRail.WidgetRuntime;

internal interface IAppContainerAuthorityJournal
{
    IAppContainerAuthorityJournalLease Acquire(string profileName);
}

internal interface IAppContainerAuthorityJournalLease : IDisposable
{
    AppContainerAuthorityPendingTransaction? ReadPending();
    void WritePending(IReadOnlyList<AppContainerAuthoritySnapshot> snapshots);
    void ClearPending();
}

internal sealed record AppContainerAuthorityPendingTransaction(
    string ConfirmationToken,
    string ProfileName,
    IReadOnlyList<AppContainerAuthoritySnapshot> Snapshots,
    bool IsLegacy);

internal sealed record AppContainerAuthorityRecoveryCandidate(
    string ConfirmationToken,
    string ProfileName,
    int TargetCount,
    bool IsLegacy);

internal sealed class FileAppContainerAuthorityJournal : IAppContainerAuthorityJournal
{
    private const int SchemaVersion = 3;
    private const int LegacySchemaVersion = 2;
    // Schema-two records created before the root-directory role was consolidated
    // may contain one root plus 1,024 directory and 1,024 file snapshots.
    private const int MaximumTargets = 2_049;
    private const int MaximumPathCharacters = 32_767;
    private const int MaximumDescriptorCharacters = 65_536;
    private const int MaximumDocumentBytes = 16 * 1024 * 1024;
    private const int MaximumPendingRecords = 256;
    private const string LegacyPendingFileName = ".authority.pending.json";
    private const string PendingDirectoryName = "pending";
    private const string PendingExtension = ".json";
    private const uint MoveFileWriteThrough = 0x00000008;
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

    internal IReadOnlyList<AppContainerAuthorityPendingTransaction> ListPending()
    {
        using var lease = (Lease)Acquire("GameBarAlternative.AuthorityInspection");
        return lease.ListPending();
    }

    public IAppContainerAuthorityJournalLease Acquire(string profileName)
    {
        ValidateProfileName(profileName);
        string lockPath;
        string pendingPath;
        string legacyPendingPath;
        try
        {
            EnsureProtectedRoot();
            lockPath = Path.Combine(_rootPath, ".authority.lock");
            pendingPath = Path.Combine(
                _rootPath, PendingDirectoryName, profileName + PendingExtension);
            legacyPendingPath = Path.Combine(_rootPath, LegacyPendingFileName);
            RejectUnsafeEntry(lockPath);
            RejectUnsafeEntry(pendingPath);
            RejectUnsafeEntry(legacyPendingPath);
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
                    CleanupOrphanTemporaryFiles();
                    return new Lease(
                        this, stream, pendingPath, legacyPendingPath, profileName);
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

        var pendingDirectory = Path.Combine(_rootPath, PendingDirectoryName);
        Directory.CreateDirectory(pendingDirectory);
        if ((File.GetAttributes(pendingDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new AppContainerAuthorityJournalException(
                "The content-authority pending directory cannot be a reparse point.");
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

    private void CleanupOrphanTemporaryFiles()
    {
        var pendingDirectory = Path.Combine(_rootPath, PendingDirectoryName);
        var entries = Directory.EnumerateFileSystemEntries(pendingDirectory)
            .Take(MaximumPendingRecords * 2 + 1)
            .ToArray();
        if (entries.Length > MaximumPendingRecords * 2)
            throw new AppContainerAuthorityJournalException(
                "The content-authority pending inventory is out of bounds.");
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            if (!IsTemporaryName(name)) continue;
            RejectUnsafeEntry(path);
            try { File.Delete(path); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new AppContainerAuthorityJournalException(
                    "An orphaned content-authority publication could not be cleared.",
                    exception);
            }
        }
    }

    private IReadOnlyList<string> EnumeratePendingPaths()
    {
        var pendingDirectory = Path.Combine(_rootPath, PendingDirectoryName);
        var paths = Directory.EnumerateFileSystemEntries(pendingDirectory)
            .Take(MaximumPendingRecords + 1)
            .ToArray();
        if (paths.Length > MaximumPendingRecords)
            throw new AppContainerAuthorityJournalException(
                "The content-authority pending inventory is out of bounds.");
        long aggregateBytes = 0;
        foreach (var path in paths)
        {
            RejectUnsafeEntry(path);
            var name = Path.GetFileName(path);
            if (!name.EndsWith(PendingExtension, StringComparison.Ordinal) ||
                IsTemporaryName(name))
                throw new AppContainerAuthorityJournalException(
                    "The content-authority pending inventory contains an invalid entry.");
            ValidateProfileName(name[..^PendingExtension.Length]);
            aggregateBytes = checked(aggregateBytes + new FileInfo(path).Length);
            if (aggregateBytes > MaximumDocumentBytes)
                throw new AppContainerAuthorityJournalException(
                    "The content-authority pending inventory exceeds its byte limit.");
        }
        return paths;
    }

    private static bool IsTemporaryName(string name) =>
        name.Length == ".pending-.tmp".Length + 32 &&
        name.StartsWith(".pending-", StringComparison.Ordinal) &&
        name.EndsWith(".tmp", StringComparison.Ordinal) &&
        name.AsSpan(9, 32).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private void EnsureNoConflictingPending(
        string profileName,
        IReadOnlyList<AppContainerAuthoritySnapshot> snapshots)
    {
        var legacyPath = Path.Combine(_rootPath, LegacyPendingFileName);
        RejectUnsafeEntry(legacyPath);
        if (File.Exists(legacyPath))
            throw new AppContainerAuthorityJournalException(
                "A legacy content-authority transaction requires recovery.");
        foreach (var path in EnumeratePendingPaths())
        {
            var otherProfile = Path.GetFileName(path)[..^PendingExtension.Length];
            if (string.Equals(otherProfile, profileName, StringComparison.Ordinal)) continue;
            var other = ReadTransaction(path, otherProfile, isLegacy: false);
            if (snapshots.Any(current => other.Snapshots.Any(prior =>
                    current.ObjectIdentity == prior.ObjectIdentity ||
                    PathsOverlap(current.Target.Path, prior.Target.Path))))
                throw new AppContainerAuthorityJournalException(
                    "A conflicting content-authority transaction requires recovery.");
        }
    }

    private static bool PathsOverlap(string left, string right)
    {
        var normalizedLeft = Path.TrimEndingDirectorySeparator(left);
        var normalizedRight = Path.TrimEndingDirectorySeparator(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase) ||
            normalizedLeft.StartsWith(
                normalizedRight + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedRight.StartsWith(
                normalizedLeft + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static AppContainerAuthorityPendingTransaction ReadTransaction(
        string path,
        string? expectedProfileName,
        bool isLegacy)
    {
        try
        {
            RejectUnsafeEntry(path);
            using var stream = new FileStream(
                path,
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
            ValidateDocument(document, expectedProfileName, allowLegacy: isLegacy);
            var confirmationToken = document.Version == SchemaVersion
                ? document.TransactionId!
                : Convert.ToHexString(SHA256.HashData(bytes));
            return new AppContainerAuthorityPendingTransaction(
                confirmationToken,
                document.ProfileName,
                document.Snapshots.AsReadOnly(),
                isLegacy);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and
            not AppContainerAuthorityJournalException)
        {
            throw new AppContainerAuthorityJournalException(
                "The pending content-authority journal is invalid.", exception);
        }
    }

    private static void PublishPending(string temporaryPath, string pendingPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Move(temporaryPath, pendingPath);
            return;
        }
        if (!MoveFileEx(temporaryPath, pendingPath, MoveFileWriteThrough))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The pending content-authority transaction could not be published.");
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
        FileAppContainerAuthorityJournal journal,
        FileStream lockStream,
        string pendingPath,
        string legacyPendingPath,
        string profileName) : IAppContainerAuthorityJournalLease
    {
        private FileStream? _lockStream = lockStream;
        private string? _activePendingPath;

        internal IReadOnlyList<AppContainerAuthorityPendingTransaction> ListPending()
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            var pending = new List<AppContainerAuthorityPendingTransaction>();
            RejectUnsafeEntry(legacyPendingPath);
            if (File.Exists(legacyPendingPath))
                pending.Add(ReadTransaction(
                    legacyPendingPath, expectedProfileName: null, isLegacy: true));
            pending.AddRange(journal.EnumeratePendingPaths().Select(path =>
            {
                var owner = Path.GetFileName(path)[..^PendingExtension.Length];
                return ReadTransaction(path, owner, isLegacy: false);
            }));
            return pending
                .OrderBy(transaction => transaction.ProfileName, StringComparer.Ordinal)
                .ToArray();
        }

        public AppContainerAuthorityPendingTransaction? ReadPending()
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            RejectUnsafeEntry(legacyPendingPath);
            if (File.Exists(legacyPendingPath))
            {
                _activePendingPath = legacyPendingPath;
                return ReadTransaction(
                    legacyPendingPath, expectedProfileName: null, isLegacy: true);
            }
            RejectUnsafeEntry(pendingPath);
            if (!File.Exists(pendingPath)) return null;
            _activePendingPath = pendingPath;
            return ReadTransaction(pendingPath, profileName, isLegacy: false);
        }

        public void WritePending(IReadOnlyList<AppContainerAuthoritySnapshot> snapshots)
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            ArgumentNullException.ThrowIfNull(snapshots);
            var document = new JournalDocument(
                SchemaVersion,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                profileName,
                snapshots.ToList());
            ValidateDocument(document, profileName, allowLegacy: false);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (bytes.Length > MaximumDocumentBytes)
                throw new AppContainerAuthorityJournalException(
                    "The content-authority journal exceeds its byte limit.");
            journal.EnsureNoConflictingPending(profileName, snapshots);
            RejectUnsafeEntry(pendingPath);
            if (File.Exists(pendingPath))
                throw new AppContainerAuthorityJournalException(
                    "A pending content-authority transaction already exists.");
            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(pendingPath)!, $".pending-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 16 * 1024,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                PublishPending(temporaryPath, pendingPath);
                _activePendingPath = pendingPath;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException and
                not AppContainerAuthorityJournalException)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception cleanupFailure) when (
                    cleanupFailure is IOException or UnauthorizedAccessException) { }
                throw new AppContainerAuthorityJournalException(
                    "The pending content-authority journal could not be persisted.",
                    exception);
            }
        }

        public void ClearPending()
        {
            ObjectDisposedException.ThrowIf(_lockStream is null, this);
            var path = _activePendingPath ?? pendingPath;
            RejectUnsafeEntry(path);
            try
            {
                File.Delete(path);
                _activePendingPath = null;
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
    }

    private static void ValidateDocument(
        JournalDocument document,
        string? expectedProfileName,
        bool allowLegacy)
    {
        if (document.Version != SchemaVersion &&
            !(allowLegacy && document.Version == LegacySchemaVersion))
        {
            throw new AppContainerAuthorityJournalException(
                "The content-authority journal version is invalid.");
        }
        if ((document.Version == SchemaVersion &&
             (document.TransactionId is not { Length: 32 } ||
              !document.TransactionId.All(character =>
                  character is >= '0' and <= '9' or >= 'A' and <= 'F'))) ||
            (document.Version == LegacySchemaVersion && document.TransactionId is not null))
            throw new AppContainerAuthorityJournalException(
                "The content-authority journal transaction ID is invalid.");
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
                !snapshot.ObjectIdentity.HasValidFormat() ||
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

    private sealed record JournalDocument(
        int Version,
        string? TransactionId,
        string ProfileName,
        List<AppContainerAuthoritySnapshot> Snapshots);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string newFileName,
        uint flags);
}

internal sealed class AppContainerAuthorityJournalException : Exception
{
    internal AppContainerAuthorityJournalException(string message) : base(message) { }
    internal AppContainerAuthorityJournalException(string message, Exception innerException)
        : base(message, innerException) { }
}
