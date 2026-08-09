using System.ComponentModel;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using GameBarAlternative.PlatformBroker;
using Microsoft.Win32.SafeHandles;

#pragma warning disable CA1416 // Every entrypoint is internal and OpenOrCreate fails before use off Windows.

namespace GameBarAlternative.WidgetRuntime;

/// <summary>Capability-free AppContainer derived from a host-owned package identity.</summary>
internal sealed class WindowsAppContainer : IDisposable
{
    private const string ProfilePrefix = "GameBarAlternative.Widget.";
    private const string AuthorityQuarantineFile = ".gbar-content-authority-quarantined";
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);
    private static readonly ConcurrentDictionary<string, byte> QuarantinedProfiles =
        new(StringComparer.Ordinal);
    private readonly SafeSidHandle _sid;
    private readonly SecurityIdentifier _identity;
    private readonly string _profileName;
    private bool _disposed;

    private WindowsAppContainer(SafeSidHandle sid, string profileName)
    {
        _sid = sid;
        _identity = new SecurityIdentifier(sid.DangerousGetHandle());
        _profileName = profileName;
        Sid = _identity.Value;
    }

    public string Sid { get; }
    public string ProfilePath { get; private set; } = string.Empty;
    internal IntPtr SidPointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sid.DangerousGetHandle();
        }
    }

    public static WindowsAppContainer OpenOrCreate(string isolationKey)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppContainer worker isolation requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(isolationKey);
        var profileName = ProfileNameFor(isolationKey);
        if (QuarantinedProfiles.ContainsKey(profileName))
            throw new WidgetProcessAdmissionException(
                "Worker content authority is quarantined after rollback failure.");

        var result = NativeMethods.CreateAppContainerProfile(
            profileName,
            "Game Bar Alternative community widgets",
            "Capability-free worker isolation for community widgets",
            IntPtr.Zero,
            0,
            out var sid);
        if (result == ErrorAlreadyExistsHResult)
            result = NativeMethods.DeriveAppContainerSidFromAppContainerName(profileName, out sid);
        if (result < 0 || sid.IsInvalid)
        {
            sid?.Dispose();
            throw new Win32Exception(result, "Could not establish the community-worker AppContainer profile.");
        }
        var container = new WindowsAppContainer(sid, profileName);
        var folderResult = NativeMethods.GetAppContainerFolderPath(container.Sid, out var folder);
        if (folderResult < 0 || folder == IntPtr.Zero)
        {
            container.Dispose();
            throw new Win32Exception(
                folderResult, "Could not resolve the community-worker AppContainer profile folder.");
        }
        try
        {
            var profilePath = Marshal.PtrToStringUni(folder)
                ?? throw new WidgetProcessException("The AppContainer profile path is unavailable.");
            var temporaryPath = Path.Combine(profilePath, "Temp");
            var quarantinePath = Path.Combine(profilePath, AuthorityQuarantineFile);
            if (File.Exists(quarantinePath))
            {
                QuarantinedProfiles.TryAdd(profileName, 0);
                container.Dispose();
                throw new WidgetProcessAdmissionException(
                    "Worker content authority is quarantined after rollback failure.");
            }
            Directory.CreateDirectory(temporaryPath);
            container.ProfilePath = profilePath;
            return container;
        }
        finally
        {
            Marshal.FreeCoTaskMem(folder);
        }
    }

    public void GrantReadAndExecute(IEnumerable<string> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var value in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(value))
            {
                var directory = new DirectoryInfo(value);
                var security = directory.GetAccessControl(AccessControlSections.Access);
                security.AddAccessRule(new FileSystemAccessRule(
                    _identity,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                directory.SetAccessControl(security);
            }
            else if (File.Exists(value))
            {
                var file = new FileInfo(value);
                var security = file.GetAccessControl(AccessControlSections.Access);
                security.AddAccessRule(new FileSystemAccessRule(
                    _identity,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    AccessControlType.Allow));
                file.SetAccessControl(security);
            }
            else
            {
                throw new FileNotFoundException("An AppContainer read-only path was not found.", value);
            }
        }
    }

    /// <summary>
    /// Replaces any prior inheriting grant on a content root with direct grants
    /// for the verified directory and file set. Later files therefore do not
    /// inherit this AppContainer's authority.
    /// </summary>
    internal void ReplaceReadAndExecuteGrant(
        IEnumerable<string> authorityRoots,
        IEnumerable<string> directories,
        IEnumerable<string> files,
        IAppContainerAuthorityOperations? operations = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var targets = authorityRoots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new AppContainerAuthorityTarget(
                path, AppContainerAuthorityTargetKind.AuthorityRoot))
            .Concat(directories
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new AppContainerAuthorityTarget(
                    path, AppContainerAuthorityTargetKind.VerifiedDirectory)))
            .Concat(files
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new AppContainerAuthorityTarget(
                    path, AppContainerAuthorityTargetKind.VerifiedFile)))
            .ToArray();
        try
        {
            AppContainerAuthorityTransaction.Apply(
                targets, operations ?? new WindowsAuthorityOperations(_identity));
        }
        catch (AppContainerAuthorityRollbackException exception)
        {
            QuarantinedProfiles.TryAdd(_profileName, 0);
            try
            {
                var markerPath = Path.Combine(ProfilePath, AuthorityQuarantineFile);
                using var marker = new FileStream(
                    markerPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 256,
                    FileOptions.WriteThrough);
                marker.Write("quarantined"u8);
                marker.Flush(flushToDisk: true);
            }
            catch (Exception markerException) when (markerException is not OutOfMemoryException)
            {
                throw new WidgetProcessAdmissionException(
                    "Worker content authority rollback failed and quarantine could not be persisted.",
                    new AggregateException(exception, markerException));
            }
            throw new WidgetProcessAdmissionException(
                "Worker content authority is quarantined after rollback failure.",
                exception);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new WidgetProcessAdmissionException(
                "Worker content authority could not be established.", exception);
        }
    }

    public NamedPipeServerStream CreatePipe(string pipeName, int bufferSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return WindowsIsolatedPipeFactory.Create(pipeName, Sid, bufferSize);
    }

    public EnvironmentBlock CreateEnvironmentBlock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[]
        {
            "SystemRoot", "WINDIR",
            "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS",
            "DOTNET_ROOT", "DOTNET_ROOT(x86)",
        })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) variables[name] = value;
        }
        // AppContainer filesystem virtualization maps these conventional user
        // locations into this profile. Passing the already-physical profile
        // path would virtualize it a second time.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temporary = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var bundleExtraction = Path.Combine(temporary, "GameBarAlternative", "WidgetRuntime", ".net");
        variables["LOCALAPPDATA"] = localAppData;
        variables["TEMP"] = temporary;
        variables["TMP"] = temporary;
        variables["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = bundleExtraction;

        var text = string.Join('\0', variables.Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
        var bytes = Encoding.Unicode.GetBytes(text);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new EnvironmentBlock(pointer);
    }

    public static void VerifyPipeClientProcess(NamedPipeServerStream pipe, int expectedProcessId)
    {
        try
        {
            WindowsIsolatedPipeFactory.VerifyClientProcess(pipe, expectedProcessId);
        }
        catch (InvalidOperationException exception)
        {
            throw new WidgetProcessException(
                "An unexpected process connected to the widget pipe.", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sid.Dispose();
    }

    internal static void ForgetInMemoryQuarantineForTesting(string isolationKey) =>
        QuarantinedProfiles.TryRemove(ProfileNameFor(isolationKey), out _);

    private static string ProfileNameFor(string isolationKey) =>
        ProfilePrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(isolationKey)).AsSpan(0, 16));

    internal sealed class EnvironmentBlock : IDisposable
    {
        private bool _disposed;

        internal EnvironmentBlock(IntPtr pointer) => Pointer = pointer;

        public IntPtr Pointer { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    private sealed class SafeSidHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSidHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle() => NativeMethods.FreeSid(handle) == IntPtr.Zero;
    }

    private sealed class WindowsAuthorityOperations(SecurityIdentifier identity)
        : IAppContainerAuthorityOperations
    {
        public AppContainerAuthoritySnapshot Capture(AppContainerAuthorityTarget target)
        {
            var security = target.Kind == AppContainerAuthorityTargetKind.VerifiedFile
                ? GetFileSecurity(target.Path)
                : GetDirectorySecurity(target.Path);
            return new AppContainerAuthoritySnapshot(
                target,
                security.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }

        public void Apply(AppContainerAuthoritySnapshot snapshot)
        {
            if (snapshot.Target.Kind == AppContainerAuthorityTargetKind.VerifiedFile)
            {
                var security = CreateFileSecurity(snapshot.AccessDescriptor);
                security.PurgeAccessRules(identity);
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    AccessControlType.Allow));
                new FileInfo(snapshot.Target.Path).SetAccessControl(security);
                return;
            }

            var directorySecurity = CreateDirectorySecurity(snapshot.AccessDescriptor);
            directorySecurity.PurgeAccessRules(identity);
            if (snapshot.Target.Kind == AppContainerAuthorityTargetKind.VerifiedDirectory)
            {
                directorySecurity.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
            new DirectoryInfo(snapshot.Target.Path).SetAccessControl(directorySecurity);
        }

        public void Restore(AppContainerAuthoritySnapshot snapshot)
        {
            if (snapshot.Target.Kind == AppContainerAuthorityTargetKind.VerifiedFile)
            {
                new FileInfo(snapshot.Target.Path).SetAccessControl(
                    CreateFileSecurity(snapshot.AccessDescriptor));
                return;
            }
            new DirectoryInfo(snapshot.Target.Path).SetAccessControl(
                CreateDirectorySecurity(snapshot.AccessDescriptor));
        }

        private static FileSystemSecurity GetDirectorySecurity(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(
                    "An AppContainer verified directory was not found.");
            return new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        }

        private static FileSystemSecurity GetFileSecurity(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "An AppContainer verified file was not found.", path);
            return new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        }

        private static DirectorySecurity CreateDirectorySecurity(string descriptor)
        {
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm(
                descriptor, AccessControlSections.Access);
            return security;
        }

        private static FileSecurity CreateFileSecurity(string descriptor)
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorSddlForm(
                descriptor, AccessControlSections.Access);
            return security;
        }
    }

    private static class NativeMethods
    {
        [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
        internal static extern int CreateAppContainerProfile(
            string appContainerName,
            string displayName,
            string description,
            IntPtr capabilities,
            uint capabilityCount,
            out SafeSidHandle appContainerSid);

        [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
        internal static extern int DeriveAppContainerSidFromAppContainerName(
            string appContainerName,
            out SafeSidHandle appContainerSid);

        [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetAppContainerFolderPath(
            string appContainerSid,
            out IntPtr path);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr FreeSid(IntPtr sid);

    }
}
