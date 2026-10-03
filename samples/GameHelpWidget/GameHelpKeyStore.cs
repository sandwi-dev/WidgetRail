using System.Security.Cryptography;
using System.Text;
namespace WidgetRail.Samples.GameHelp;

/// <summary>The full-trust application owns its encrypted user credential; no key is stored in widget state.</summary>
internal sealed class GameHelpKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WidgetRail.GameHelp.Gemini.v1");
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "applications", "game-help", "gemini-key.dat");
    internal bool Exists(CancellationToken token) { token.ThrowIfCancellationRequested(); return File.Exists(path); }
    internal string? Read(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4096) throw Failure();
        byte[]? clear = null;
        try
        {
            clear = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(clear); Validate(key); return key;
        }
        catch (CryptographicException) { throw Failure(); }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }
    internal void Save(string key, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        token.ThrowIfCancellationRequested(); Validate(key);
        var clear = Encoding.UTF8.GetBytes(key);
        try
        {
            var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, path, overwrite: true); }
            finally { File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    internal void Delete(CancellationToken token) { token.ThrowIfCancellationRequested(); File.Delete(path); }
    private static void Validate(string key)
    {
        if (key.Length is < 20 or > 96 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new GameHelpException("invalid_key", "Enter a valid Gemini API key.");
    }
    private static GameHelpException Failure() => new("key_unavailable", "The saved API key could not be read. Save it again in Game Help settings.");
}
