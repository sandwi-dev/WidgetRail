using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed class GameLauncherSavedIdIssuer
{
    private const int KeyBytes = 32;
    private readonly object _gate = new();
    private readonly string _path;
    private byte[]? _key;

    internal GameLauncherSavedIdIssuer(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("A saved-id key path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    internal string Issue(string stableIdentity)
    {
        if (string.IsNullOrWhiteSpace(stableIdentity) || stableIdentity.Length > 512 ||
            stableIdentity.Any(char.IsControl))
            throw new InvalidDataException("A provider identity is invalid.");
        var material = Encoding.UTF8.GetBytes(
            "game-launcher-community-saved-id-v1\0" + stableIdentity);
        var digest = HMACSHA256.HashData(GetKey(), material);
        try
        {
            return "saved-" + Convert.ToBase64String(digest)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private byte[] GetKey()
    {
        lock (_gate)
        {
            if (_key is not null) return _key;
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(_path))
            {
                var stored = File.ReadAllBytes(_path);
                if (stored.Length == KeyBytes) return _key = stored;
                CryptographicOperations.ZeroMemory(stored);
                throw new InvalidDataException("The Game Launcher identity key is invalid.");
            }
            var candidate = RandomNumberGenerator.GetBytes(KeyBytes);
            var temporary = Path.Combine(directory, $".saved-id.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporary, candidate);
                try { File.Move(temporary, _path, overwrite: false); }
                catch (IOException) when (File.Exists(_path))
                {
                    CryptographicOperations.ZeroMemory(candidate);
                    var stored = File.ReadAllBytes(_path);
                    if (stored.Length != KeyBytes)
                    {
                        CryptographicOperations.ZeroMemory(stored);
                        throw new InvalidDataException(
                            "The Game Launcher identity key is invalid.");
                    }
                    return _key = stored;
                }
                return _key = candidate;
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
