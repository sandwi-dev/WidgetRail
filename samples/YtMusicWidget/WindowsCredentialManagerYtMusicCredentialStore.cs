using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.Samples.YtMusicWidget;

/// <summary>
/// Persists the YTMDesktop2 bearer token as a per-user Windows Generic
/// Credential. The endpoint is hashed into the target name so a credential is
/// never sent to a different loopback companion configuration.
/// </summary>
public sealed class WindowsCredentialManagerYtMusicCredentialStore
    : IYtMusicCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2560;
    private readonly string _targetName;

    public WindowsCredentialManagerYtMusicCredentialStore(string endpoint)
    {
        var normalizedEndpoint = YtmDesktopApiClient.ValidateEndpoint(endpoint).AbsoluteUri;
        var endpointHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEndpoint)));
        _targetName = $"GameBarAlternative/YtMusic/{endpointHash}";
    }

    public string? LoadToken()
    {
        EnsureWindows();
        if (!CredRead(_targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, "Windows Credential Manager could not read the YT Music credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;
            if (credential.CredentialBlobSize > MaximumCredentialBlobBytes)
                throw new InvalidOperationException("The stored YT Music credential is invalid.");

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return NormalizeToken(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void SaveToken(string token)
    {
        EnsureWindows();
        var normalized = NormalizeToken(token) ??
            throw new ArgumentException("A non-empty token is required.", nameof(token));
        var bytes = Encoding.UTF8.GetBytes(normalized);
        if (bytes.Length > MaximumCredentialBlobBytes)
            throw new ArgumentOutOfRangeException(nameof(token), "The YT Music credential is too large.");

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = _targetName,
                CredentialBlobSize = checked((uint)bytes.Length),
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = CredentialPersistLocalMachine,
                UserName = "api-token",
            };
            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Windows Credential Manager could not save the YT Music credential.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            handle.Free();
        }
    }

    public void ClearToken()
    {
        EnsureWindows();
        if (CredDelete(_targetName, CredentialTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error, "Windows Credential Manager could not remove the YT Music credential.");
    }

    private static string? NormalizeToken(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("YT Music credential persistence requires Windows Credential Manager.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
