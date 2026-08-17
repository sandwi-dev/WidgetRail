using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.PlatformBroker;

internal interface IAppLibrarySavedIdIssuer
{
    string Issue(BrokerWidgetIdentity identity, string stableProviderIdentity);
}

/// <summary>
/// Issues non-reversible app-library identities scoped to one authenticated
/// publisher/package authority. The host key is durable across host and widget
/// upgrades; the widget instance ID is intentionally excluded.
/// </summary>
internal sealed class AppLibrarySavedIdIssuer : IAppLibrarySavedIdIssuer
{
    internal const int KeyBytes = 32;
    internal const int MaximumStableProviderIdentityCharacters = 512;
    private const string Prefix = "saved-";
    private readonly object _gate = new();
    private readonly string? _keyPath;
    private byte[]? _key;

    internal static IAppLibrarySavedIdIssuer Shared { get; } =
        new AppLibrarySavedIdIssuer(DefaultKeyPath());

    internal AppLibrarySavedIdIssuer(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || !Path.IsPathFullyQualified(keyPath))
            throw new ArgumentException("A fully qualified host-key path is required.", nameof(keyPath));
        _keyPath = Path.GetFullPath(keyPath);
    }

    internal AppLibrarySavedIdIssuer(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyBytes)
            throw new ArgumentException($"The app-library host key must be {KeyBytes} bytes.", nameof(key));
        _key = key.ToArray();
    }

    public string Issue(BrokerWidgetIdentity identity, string stableProviderIdentity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        ValidateStableProviderIdentity(stableProviderIdentity);

        var authority = Encoding.UTF8.GetBytes(
            "app-library-saved-id-v1\0" + identity.PublisherId + "\0" +
            identity.PackageId + "\0" + stableProviderIdentity);
        var digest = HMACSHA256.HashData(GetKey(), authority);
        try
        {
            return Prefix + Convert.ToBase64String(digest)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
            CryptographicOperations.ZeroMemory(authority);
        }
    }

    internal static void ValidateStableProviderIdentity(
        string value, string code = "invalid_backend_data")
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumStableProviderIdentityCharacters ||
            value.Any(char.IsControl))
            throw new BrokerException(code, "App library provider identity is invalid.");
    }

    private byte[] GetKey()
    {
        lock (_gate)
        {
            if (_key is not null) return _key;
            _key = LoadOrCreateKey(_keyPath!);
            return _key;
        }
    }

    private static byte[] LoadOrCreateKey(string keyPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(keyPath)
                ?? throw new IOException("Host-key directory is unavailable.");
            Directory.CreateDirectory(directory);
            DemandOrdinaryDirectory(directory);

            if (File.Exists(keyPath)) return ReadKey(keyPath);

            var candidate = RandomNumberGenerator.GetBytes(KeyBytes);
            var temporaryPath = Path.Combine(
                directory, ".app-library-key-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, bufferSize: KeyBytes,
                           FileOptions.WriteThrough))
                {
                    stream.Write(candidate);
                    stream.Flush(flushToDisk: true);
                }
                try
                {
                    File.Move(temporaryPath, keyPath, overwrite: false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(keyPath))
                {
                    CryptographicOperations.ZeroMemory(candidate);
                    return ReadKey(keyPath);
                }
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (BrokerException) { throw; }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or System.Security.SecurityException or
            ArgumentException or NotSupportedException)
        {
            throw new BrokerException(
                "platform_unavailable", "Durable app-library identity is unavailable.", exception);
        }
    }

    private static byte[] ReadKey(string keyPath)
    {
        var attributes = File.GetAttributes(keyPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new BrokerException(
                "platform_unavailable", "Durable app-library identity is unavailable.");
        var key = File.ReadAllBytes(keyPath);
        if (key.Length == KeyBytes) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new BrokerException(
            "platform_unavailable", "Durable app-library identity is unavailable.");
    }

    private static void DemandOrdinaryDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new BrokerException(
                "platform_unavailable", "Durable app-library identity is unavailable.");
    }

    private static string DefaultKeyPath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new BrokerException(
                "platform_unavailable", "Durable app-library identity is unavailable.");
        return Path.Combine(
            localData, "GameBarAlternative", "broker", "app-library-saved-id.key");
    }
}
