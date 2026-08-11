using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;
using Microsoft.Win32.SafeHandles;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Resolves only allowlisted Steam library-cache files beneath an already
/// trusted Steam root. Catalog registration is path-normalization only; cache
/// discovery, object metadata, bytes, decode, and revalidation happen only
/// after an opaque artwork demand.
/// </summary>
internal sealed class WindowsSteamArtworkSource
{
    internal const int MaximumSourceBytes = 1024 * 1024;
    internal const uint MaximumSourceDimension = 4_096;
    internal const long MaximumSourcePixels = 16_777_216;
    internal const int MaximumCacheEntries = 64;
    internal const int MaximumLocatorRegistrations =
        WindowsSteamApplicationSource.MaximumManifests;
    internal static readonly TimeSpan DecodeDeadline = TimeSpan.FromMilliseconds(250);

    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagSequentialScan = 0x08000000;
    private const int FileBasicInfoClass = 0;
    private const int FileStandardInfoClass = 1;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;
    private const uint MaximumFinalPathCharacters = 32_767;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly string[] CandidateSuffixes = ["_icon.png", "_icon.jpg", "_icon.jpeg"];

    private readonly object _cacheGate = new();
    private readonly Func<byte[], CancellationToken, string?> _decode;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = [];
    private readonly Dictionary<string, SteamArtworkLocator> _locators =
        new(StringComparer.Ordinal);
    private int _fileProbeCalls;

    internal WindowsSteamArtworkSource() : this(
        (bytes, cancellationToken) =>
            DecodeToPngAsync(bytes, cancellationToken).GetAwaiter().GetResult())
    {
    }

    internal WindowsSteamArtworkSource(
        Func<byte[], CancellationToken, string?> decode) =>
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));

    internal int CacheCount
    {
        get { lock (_cacheGate) return _cache.Count; }
    }

    internal int FileProbeCalls => Volatile.Read(ref _fileProbeCalls);
    internal int LocatorCount
    {
        get { lock (_cacheGate) return _locators.Count; }
    }

    internal IReadOnlyDictionary<string, SteamArtworkRegistration> RegisterCatalog(
        IEnumerable<string> trustedSteamRoots,
        IEnumerable<string> steamAppIds)
    {
        ArgumentNullException.ThrowIfNull(steamAppIds);
        var roots = NormalizeRoots(trustedSteamRoots);
        var appIds = roots.Count == 0
            ? []
            : steamAppIds
                .Where(WindowsSteamApplicationSource.IsValidAppId)
                .Distinct(StringComparer.Ordinal)
                .Take(MaximumLocatorRegistrations + 1)
                .ToArray();
        if (appIds.Length > MaximumLocatorRegistrations)
            throw new InvalidOperationException("Steam artwork locator catalog is too large.");
        var keys = appIds.ToDictionary(
            appId => appId,
            appId => LocatorKey(roots, appId),
            StringComparer.Ordinal);
        lock (_cacheGate)
        {
            var retainedKeys = keys.Values.ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _locators.Keys
                         .Where(key => !retainedKeys.Contains(key))
                         .ToArray())
            {
                _locators[stale].Retire();
                _locators.Remove(stale);
            }
            var result = new Dictionary<string, SteamArtworkRegistration>(
                appIds.Length, StringComparer.Ordinal);
            foreach (var appId in appIds)
            {
                var key = keys[appId];
                if (!_locators.TryGetValue(key, out var locator))
                    _locators.Add(key, locator = new SteamArtworkLocator(roots, appId));
                result.Add(appId, locator.Snapshot());
            }
            return result;
        }
    }

    internal SteamArtworkRegistration? Register(
        IEnumerable<string> trustedSteamRoots,
        string steamAppId)
    {
        ArgumentNullException.ThrowIfNull(trustedSteamRoots);
        if (!OperatingSystem.IsWindows() ||
            !WindowsSteamApplicationSource.IsValidAppId(steamAppId)) return null;
        try
        {
            var roots = NormalizeRoots(trustedSteamRoots);
            if (roots.Count == 0) return null;
            var key = LocatorKey(roots, steamAppId);
            lock (_cacheGate)
            {
                return _locators.TryGetValue(key, out var current)
                    ? current.Snapshot()
                    : new SteamArtworkLocator(roots, steamAppId).Snapshot();
            }
        }
        catch (Exception exception) when (IsExpectedFileFailure(exception))
        {
            return null;
        }
    }

    internal string? Load(
        SteamArtworkRegistration expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        var discovered = DiscoverCurrent(expected, cancellationToken);
        var currentRevision = discovered?.Revision ?? "missing";
        var matchesExpectedGeneration = expected.Locator.Observe(
            expected.Revision, currentRevision, discovered is not null);
        if (!matchesExpectedGeneration)
            return null;
        var current = discovered!;
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(current.Revision, out var cached))
            {
                if (!expected.Locator.IsCurrent(expected.Revision)) return null;
                TouchLocked(current.Revision, cached);
                return cached.PngBase64;
            }
        }

        try
        {
            using var root = ProbeDirectory(current.TrustedSteamRoot);
            if (root is null) return null;
            var cacheRoot = Path.Combine(
                current.TrustedSteamRoot, "appcache", "librarycache");
            using var cache = ProbeDirectory(cacheRoot);
            if (cache is null) return null;
            var finalRoot = GetFinalPath(root);
            var finalCache = GetFinalPath(cache);
            if (finalRoot is null || finalCache is null ||
                !PathEquals(finalCache, Path.Combine(finalRoot, "appcache", "librarycache")))
                return null;

            using var file = ProbeArtworkFile(current.FilePath);
            if (file is null) return null;
            var finalFile = GetFinalPath(file);
            if (finalFile is null ||
                !PathEquals(Path.GetDirectoryName(finalFile), finalCache) ||
                !IsAllowlistedExtension(finalFile) ||
                !string.Equals(CaptureRevision(file), current.Revision,
                    StringComparison.Ordinal))
                return null;

            using var stream = new FileStream(file, FileAccess.Read, 16 * 1024, isAsync: false);
            if (stream.Length is <= 0 or > MaximumSourceBytes) return null;
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(CaptureRevision(stream.SafeFileHandle), current.Revision,
                    StringComparison.Ordinal) ||
                !IsAllowlistedPayload(bytes))
                return null;

            var png = _decode(bytes, cancellationToken);
            if (png is null || !expected.Locator.IsCurrent(expected.Revision)) return null;
            lock (_cacheGate)
            {
                if (!expected.Locator.IsCurrent(expected.Revision)) return null;
                if (_cache.TryGetValue(current.Revision, out var raced))
                {
                    TouchLocked(current.Revision, raced);
                    return raced.PngBase64;
                }
                var node = _recency.AddLast(current.Revision);
                _cache.Add(current.Revision, new CacheEntry(png, node));
                while (_cache.Count > MaximumCacheEntries)
                {
                    var oldest = _recency.First!;
                    _recency.RemoveFirst();
                    _cache.Remove(oldest.Value);
                }
            }
            return png;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFileFailure(exception) ||
            exception is InvalidOperationException or COMException or OutOfMemoryException)
        {
            return null;
        }
    }

    private DiscoveredArtwork? DiscoverCurrent(
        SteamArtworkRegistration registration,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var trustedRoot in registration.Locator.TrustedSteamRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cacheRoot = Path.Combine(trustedRoot, "appcache", "librarycache");
                using var root = ProbeDirectory(trustedRoot);
                if (root is null) continue;
                using var cache = ProbeDirectory(cacheRoot);
                if (cache is null) continue;
                var finalRoot = GetFinalPath(root);
                var finalCache = GetFinalPath(cache);
                if (finalRoot is null || finalCache is null ||
                    !PathEquals(finalCache,
                        Path.Combine(finalRoot, "appcache", "librarycache")))
                    continue;

                foreach (var suffix in CandidateSuffixes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = Path.Combine(
                        cacheRoot, registration.Locator.SteamAppId + suffix);
                    using var file = ProbeArtworkFile(path);
                    if (file is null) continue;
                    var finalFile = GetFinalPath(file);
                    if (finalFile is null ||
                        !PathEquals(Path.GetDirectoryName(finalFile), finalCache) ||
                        !IsAllowlistedExtension(finalFile))
                        continue;
                    var revision = CaptureRevision(file);
                    if (revision is not null)
                        return new(trustedRoot, path, revision);
                }
            }
        }
        catch (Exception exception) when (IsExpectedFileFailure(exception))
        {
        }
        return null;
    }

    private static string LocatorKey(
        IReadOnlyList<string> roots,
        string steamAppId) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(steamAppId + "\0" + string.Join('\0', roots))));

    private static IReadOnlyList<string> NormalizeRoots(
        IEnumerable<string> trustedSteamRoots) => trustedSteamRoots
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .Take(WindowsSteamApplicationSource.MaximumLibraries)
        .ToArray();

    private SafeFileHandle? ProbeDirectory(string path)
    {
        Interlocked.Increment(ref _fileProbeCalls);
        return OpenDirectory(path);
    }

    private SafeFileHandle? ProbeArtworkFile(string path)
    {
        Interlocked.Increment(ref _fileProbeCalls);
        return OpenArtworkFile(path);
    }

    private void TouchLocked(string revision, CacheEntry entry)
    {
        _recency.Remove(entry.Node);
        entry.Node = _recency.AddLast(revision);
    }

    private static async Task<string?> DecodeToPngAsync(
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DecodeDeadline);
        try
        {
            using var source = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(source))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask(timeout.Token).ConfigureAwait(false);
                writer.DetachStream();
            }
            source.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(source).AsTask(timeout.Token)
                .ConfigureAwait(false);
            if (decoder.PixelWidth is 0 or > MaximumSourceDimension ||
                decoder.PixelHeight is 0 or > MaximumSourceDimension ||
                checked((long)decoder.PixelWidth * decoder.PixelHeight) > MaximumSourcePixels)
                return null;

            var scale = Math.Min(
                (double)AppLibraryImageLimits.MaximumPixelDimension / decoder.PixelWidth,
                (double)AppLibraryImageLimits.MaximumPixelDimension / decoder.PixelHeight);
            scale = Math.Min(1, scale);
            var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
            var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
            var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height };
            var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(timeout.Token).ConfigureAwait(false);
            var rgba = pixels.DetachPixelData();
            if (rgba.Length != checked((int)(width * height * 4))) return null;

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output)
                .AsTask(timeout.Token).ConfigureAwait(false);
            encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
                width, height, 96, 96, rgba);
            await encoder.FlushAsync().AsTask(timeout.Token).ConfigureAwait(false);
            if (output.Size is 0 or > AppLibraryImageLimits.MaximumPngBytes) return null;
            output.Seek(0);
            using var reader = new DataReader(output.GetInputStreamAt(0));
            var loaded = await reader.LoadAsync((uint)output.Size).AsTask(timeout.Token)
                .ConfigureAwait(false);
            if (loaded != output.Size) return null;
            var result = new byte[loaded];
            reader.ReadBytes(result);
            return Convert.ToBase64String(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsAllowlistedPayload(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(PngSignature) ||
        bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;

    private static bool IsAllowlistedExtension(string path) =>
        Path.GetExtension(path) is { } extension &&
        (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    private static SafeFileHandle? OpenDirectory(string path) =>
        Open(path, FileReadAttributes, FileFlagBackupSemantics,
            FileShare.Read | FileShare.Write | FileShare.Delete);

    private static SafeFileHandle? OpenArtworkFile(string path) =>
        Open(path, GenericRead, FileFlagSequentialScan, FileShare.Read);

    private static SafeFileHandle? Open(
        string path,
        uint access,
        uint additionalFlags,
        FileShare share)
    {
        var handle = CreateFile(
            path,
            access,
            (uint)share,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | additionalFlags,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        if (!GetFileAttributeTagInfo(handle, FileAttributeTagInfoClass,
                out var attributes, Marshal.SizeOf<FileAttributeTagInfo>()) ||
            (attributes.FileAttributes & FileAttributes.ReparsePoint) != 0 ||
            ((attributes.FileAttributes & FileAttributes.Directory) != 0) !=
                ((additionalFlags & FileFlagBackupSemantics) != 0))
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    private static string? CaptureRevision(SafeFileHandle file)
    {
        if (!GetFileIdInfo(file, FileIdInfoClass, out var identity,
                Marshal.SizeOf<FileIdInfo>()) ||
            !GetFileBasicInfo(file, FileBasicInfoClass, out var basic,
                Marshal.SizeOf<FileBasicInfo>()) ||
            !GetFileStandardInfo(file, FileStandardInfoClass, out var standard,
                Marshal.SizeOf<FileStandardInfo>()) ||
            standard.Directory != 0 || standard.EndOfFile is <= 0 or > MaximumSourceBytes)
            return null;
        var evidence = string.Join('\0',
            identity.VolumeSerialNumber.ToString("X16"),
            identity.FileId.Low.ToString("X16"),
            identity.FileId.High.ToString("X16"),
            standard.EndOfFile.ToString("X16"),
            basic.ChangeTime.ToString("X16"),
            basic.LastWriteTime.ToString("X16"));
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(evidence)));
    }

    private static string? GetFinalPath(SafeFileHandle handle)
    {
        var length = GetFinalPathNameByHandle(handle, null, 0, 0);
        if (length == 0 || length > MaximumFinalPathCharacters) return null;
        var buffer = new StringBuilder(checked((int)length + 1));
        var written = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (written == 0 || written >= buffer.Capacity) return null;
        var value = buffer.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            value = @"\\" + value[8..];
        else if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool PathEquals(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException or
            NotSupportedException;

    private sealed class CacheEntry(string pngBase64, LinkedListNode<string> node)
    {
        internal string PngBase64 { get; } = pngBase64;
        internal LinkedListNode<string> Node { get; set; } = node;
    }

    private sealed record DiscoveredArtwork(
        string TrustedSteamRoot,
        string FilePath,
        string Revision);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal FileAttributes FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        internal ulong Low;
        internal ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal FileId128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        internal long CreationTime;
        internal long LastAccessTime;
        internal long LastWriteTime;
        internal long ChangeTime;
        internal uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        internal long AllocationSize;
        internal long EndOfFile;
        internal uint NumberOfLinks;
        internal byte DeletePending;
        internal byte Directory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileAttributeTagInfo(
        SafeFileHandle file, int informationClass,
        out FileAttributeTagInfo information, int bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileIdInfo(
        SafeFileHandle file, int informationClass,
        out FileIdInfo information, int bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileBasicInfo(
        SafeFileHandle file, int informationClass,
        out FileBasicInfo information, int bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileStandardInfo(
        SafeFileHandle file, int informationClass,
        out FileStandardInfo information, int bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder? filePath,
        uint filePathLength,
        uint flags);
}

internal sealed class SteamArtworkLocator(
    IReadOnlyList<string> trustedSteamRoots,
    string steamAppId)
{
    private readonly object _gate = new();
    private string? _observedRevision;
    private string _generation = "undiscovered";
    private bool _active = true;

    internal IReadOnlyList<string> TrustedSteamRoots { get; } = trustedSteamRoots;
    internal string SteamAppId { get; } = steamAppId;

    internal SteamArtworkRegistration Snapshot()
    {
        lock (_gate) return new(this, _generation);
    }

    internal bool Observe(
        string expectedGeneration,
        string currentRevision,
        bool exists)
    {
        lock (_gate)
        {
            if (!_active) return false;
            var priorRevision = _observedRevision;
            if (priorRevision is not null &&
                !string.Equals(priorRevision, currentRevision, StringComparison.Ordinal))
                _generation = currentRevision;
            _observedRevision = currentRevision;
            return exists &&
                (priorRevision is null || string.Equals(
                    priorRevision, currentRevision, StringComparison.Ordinal)) &&
                string.Equals(expectedGeneration, _generation, StringComparison.Ordinal);
        }
    }

    internal bool IsCurrent(string expectedGeneration)
    {
        lock (_gate)
            return _active && string.Equals(
                expectedGeneration, _generation, StringComparison.Ordinal);
    }

    internal void Retire()
    {
        lock (_gate) _active = false;
    }
}
