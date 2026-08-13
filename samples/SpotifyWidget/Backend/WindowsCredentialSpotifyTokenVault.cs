using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameBarAlternative.WindowsSpotifyProvider;

internal sealed class WindowsCredentialSpotifyTokenVault : ISpotifyTokenVault
{
    private const int GenericCredential = 1;
    private const int MaximumRefreshTokenUtf8Bytes = 4096;
    private const int MaximumGrantedScopes = 12;

    public Task<SpotifyRefreshCredential?> ReadAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BuildTarget(identity);
        if (!NativeMethods.CredRead(target, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return Task.FromResult<SpotifyRefreshCredential?>(null);
            throw CredentialFailure("read", error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize is <= 0 or > MaximumRefreshTokenUtf8Bytes ||
                credential.CredentialBlob == IntPtr.Zero)
                throw new SpotifyProviderException(
                    "credential_invalid", "Stored Spotify authorization is invalid.");
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                var json = new UTF8Encoding(false, true).GetString(bytes);
                var document = JsonSerializer.Deserialize<CredentialDocument>(json)
                    ?? throw new JsonException("Credential was null.");
                ValidateClientId(document.ClientId);
                ValidateToken(document.RefreshToken);
                if (document.GrantedScopes is null ||
                    document.GrantedScopes.Length > MaximumGrantedScopes ||
                    document.GrantedScopes.Any(scope => string.IsNullOrWhiteSpace(scope) ||
                        scope.Length > 128 || scope.Any(character => character is < '!' or > '~')))
                    throw new JsonException("Credential scopes were invalid.");
                return Task.FromResult<SpotifyRefreshCredential?>(new(
                    document.ClientId,
                    document.RefreshToken,
                    document.GrantedScopes.ToHashSet(StringComparer.Ordinal)));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception exception) when (exception is DecoderFallbackException or JsonException)
        {
            throw new SpotifyProviderException(
                "credential_invalid", "Stored Spotify authorization is invalid.", exception);
        }
        finally { NativeMethods.CredFree(pointer); }
    }

    public Task SaveAsync(
        SpotifyIntegrationIdentity identity, SpotifyRefreshCredential credential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(credential);
        ValidateClientId(credential.ClientId);
        ValidateToken(credential.RefreshToken);
        if (credential.GrantedScopes.Count > MaximumGrantedScopes ||
            credential.GrantedScopes.Any(scope =>
                string.IsNullOrWhiteSpace(scope) || scope.Length > 128 ||
                scope.Any(character => character is < '!' or > '~')))
            throw new SpotifyProviderException(
                "credential_invalid", "Spotify authorization scopes are invalid.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new CredentialDocument(
            credential.ClientId,
            credential.RefreshToken,
            credential.GrantedScopes.Order(StringComparer.Ordinal).ToArray()));
        if (bytes.Length > MaximumRefreshTokenUtf8Bytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new SpotifyProviderException(
                "credential_invalid", "Spotify authorization token is too large.");
        }
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var nativeCredential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = BuildTarget(identity),
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = 2,
                UserName = "Game Bar Alternative Spotify",
            };
            if (!NativeMethods.CredWrite(ref nativeCredential, 0))
                throw CredentialFailure("save", Marshal.GetLastWin32Error());
            return Task.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blob != IntPtr.Zero)
            {
                for (var index = 0; index < bytes.Length; index++)
                    Marshal.WriteByte(blob, index, 0);
                Marshal.FreeCoTaskMem(blob);
            }
        }
    }

    public Task DeleteAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredDelete(BuildTarget(identity), GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw CredentialFailure("delete", error);
        }
        return Task.CompletedTask;
    }

    internal static string BuildTarget(SpotifyIntegrationIdentity identity)
    {
        var authority = Encoding.UTF8.GetBytes(identity.Authority);
        try
        {
            return "GameBarAlternative/Spotify/v1/" +
                Convert.ToHexString(SHA256.HashData(authority)).ToLowerInvariant();
        }
        finally { CryptographicOperations.ZeroMemory(authority); }
    }

    private static void ValidateToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Length > MaximumRefreshTokenUtf8Bytes ||
            token.Any(character => character is < '!' or > '~'))
            throw new SpotifyProviderException(
                "credential_invalid", "Spotify authorization token is invalid.");
    }

    private static void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length is < 8 or > 128 ||
            clientId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new SpotifyProviderException(
                "credential_invalid", "Stored Spotify client identity is invalid.");
    }

    private static SpotifyProviderException CredentialFailure(string operation, int error) =>
        new("credential_unavailable",
            $"Windows Credential Manager could not {operation} Spotify authorization (error {error}).");

    private sealed record CredentialDocument(
        string ClientId, string RefreshToken, string[] GrantedScopes);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr credential);
    }
}
