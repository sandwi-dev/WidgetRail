using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using WidgetRail.PlatformBroker;
using Microsoft.Win32.SafeHandles;

#pragma warning disable CA1416 // Every entrypoint is internal and OpenOrCreate fails before use off Windows.

namespace WidgetRail.WidgetRuntime;

/// <summary>Capability-free AppContainer derived from a host-owned package identity.</summary>
internal sealed class WindowsAppContainer : IDisposable
{
    private const string ProfilePrefix = "WidgetRail.Widget.";
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);
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

        var result = NativeMethods.CreateAppContainerProfile(
            profileName,
            "WidgetRail community widgets",
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
            Directory.CreateDirectory(temporaryPath);
            container.ProfilePath = profilePath;
            return container;
        }
        finally
        {
            Marshal.FreeCoTaskMem(folder);
        }
    }

    internal static WindowsAppContainer OpenExistingProfile(string profileName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppContainer recovery requires Windows.");
        if (!IsProfileName(profileName))
            throw new ArgumentException("AppContainer profile name is invalid.", nameof(profileName));
        var result = NativeMethods.DeriveAppContainerSidFromAppContainerName(
            profileName, out var sid);
        if (result < 0 || sid.IsInvalid)
        {
            sid?.Dispose();
            throw new Win32Exception(
                result, "Could not resolve the community-worker AppContainer profile.");
        }
        return new WindowsAppContainer(sid, profileName);
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
        IEnumerable<AppContainerAuthorityExpectedTarget> expectedTargets,
        IAppContainerAuthorityOperations? operations = null,
        IAppContainerAuthorityJournal? journal = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var ownedAuthorityOperations = operations is null
            ? new WindowsAuthorityOperations(_identity)
            : null;
        var authorityOperations = operations ?? ownedAuthorityOperations!;
        try
        {
            using var journalLease = (journal ?? FileAppContainerAuthorityJournal.Default)
                .Acquire(_profileName);
            var pending = journalLease.ReadPending();
            if (pending is not null)
            {
                try
                {
                    using var recoveryContainer = string.Equals(
                            pending.ProfileName, _profileName, StringComparison.Ordinal)
                        ? null
                        : OpenExistingProfile(pending.ProfileName);
                    using var recoveryOperations = recoveryContainer?
                        .CreateAuthorityOperationsForTesting();
                    AppContainerAuthorityTransaction.Recover(
                        pending.Snapshots,
                        recoveryOperations ?? authorityOperations);
                    journalLease.ClearPending();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    throw new WidgetProcessAdmissionException(
                        "Worker content authority is quarantined pending host recovery.",
                        exception);
                }
            }

            var normalizedTargets =
                WidgetProcessContentTargets.NormalizeAndValidate(expectedTargets);
            var targets = normalizedTargets.Select(target => target.Target).ToArray();
            var snapshots = AppContainerAuthorityTransaction.Capture(
                targets, authorityOperations);
            if (snapshots.Count != normalizedTargets.Count ||
                snapshots.Where((snapshot, index) =>
                    snapshot.Target != normalizedTargets[index].Target ||
                    snapshot.ObjectIdentity != normalizedTargets[index].ObjectIdentity).Any())
                throw new IOException(
                    "An AppContainer content-authority target changed after catalog verification.");
            journalLease.WritePending(snapshots);
            try
            {
                AppContainerAuthorityTransaction.Apply(
                    snapshots, authorityOperations);
                journalLease.ClearPending();
            }
            catch (AppContainerAuthorityRollbackException exception)
            {
                throw new WidgetProcessAdmissionException(
                    "Worker content authority is quarantined pending host recovery.",
                    exception);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                try
                {
                    journalLease.ClearPending();
                }
                catch (Exception clearFailure) when (clearFailure is not OutOfMemoryException)
                {
                    throw new WidgetProcessAdmissionException(
                        "Worker content authority is quarantined pending host recovery.",
                        new AggregateException(exception, clearFailure));
                }
                throw new WidgetProcessAdmissionException(
                    "Worker content authority could not be established.", exception);
            }
        }
        catch (WidgetProcessAdmissionException)
        {
            throw;
        }
        catch (AppContainerAuthorityJournalException exception)
        {
            throw new WidgetProcessAdmissionException(
                "Worker content authority journal is unavailable.", exception);
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
        var bundleExtraction = Path.Combine(temporary, "WidgetRail", "WidgetRuntime", ".net");
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

    internal static string ProfileNameFor(string isolationKey) =>
        ProfilePrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(isolationKey)).AsSpan(0, 16));

    private static bool IsProfileName(string value) =>
        value.Length == ProfilePrefix.Length + 32 &&
        value.StartsWith(ProfilePrefix, StringComparison.Ordinal) &&
        value.AsSpan(ProfilePrefix.Length).ToString().All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    internal IAppContainerAuthorityOperations CreateAuthorityOperationsForTesting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new WindowsAuthorityOperations(_identity);
    }

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
        private readonly Dictionary<AppContainerAuthorityTarget, SafeFileHandle> _handles = [];
        private bool _disposed;

        public AppContainerAuthoritySnapshot Capture(AppContainerAuthorityTarget target)
        {
            var handle = GetOrOpen(target);
            var descriptor = GetAccessDescriptor(handle);
            RejectAlternateAppContainerAuthority(target, descriptor);
            return new AppContainerAuthoritySnapshot(
                target, descriptor, GetObjectIdentity(handle));
        }

        public void Apply(AppContainerAuthoritySnapshot snapshot)
        {
            var handle = GetBoundHandle(snapshot);
            var descriptor = snapshot.Target.Kind == AppContainerAuthorityTargetKind.VerifiedFile
                ? CreateAppliedFileSecurity(snapshot)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                : CreateAppliedDirectorySecurity(snapshot)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            SetAccessDescriptor(handle, descriptor);
        }

        public void VerifyApplied(AppContainerAuthoritySnapshot snapshot)
        {
            var actual = GetAccessDescriptor(GetBoundHandle(snapshot));
            RejectAlternateAppContainerAuthority(snapshot.Target, actual);
            var security = snapshot.Target.Kind == AppContainerAuthorityTargetKind.VerifiedFile
                ? (FileSystemSecurity)CreateFileSecurity(actual)
                : CreateDirectorySecurity(actual);
            var rules = security.GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    targetType: typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(rule => identity.Equals(rule.IdentityReference))
                .ToArray();
            var valid = rules.Length == 1 &&
                  !rules[0].IsInherited &&
                  rules[0].AccessControlType == AccessControlType.Allow &&
                  rules[0].InheritanceFlags == InheritanceFlags.None &&
                  (rules[0].FileSystemRights &
                      (FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize)) ==
                  (FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
            if (!valid)
                throw new IOException(
                    "An AppContainer content-authority DACL did not verify after apply.");
        }

        public void Restore(AppContainerAuthoritySnapshot snapshot) =>
            SetAccessDescriptor(GetBoundHandle(snapshot), snapshot.AccessDescriptor);

        public void VerifyRestored(AppContainerAuthoritySnapshot snapshot)
        {
            var actual = GetAccessDescriptor(GetBoundHandle(snapshot));
            RejectAlternateAppContainerAuthority(snapshot.Target, actual);
            if (!string.Equals(
                    snapshot.AccessDescriptor, actual, StringComparison.Ordinal))
            {
                throw new IOException(
                    "An AppContainer content-authority DACL did not verify after restore.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var handle in _handles.Values) handle.Dispose();
            _handles.Clear();
        }

        private SafeFileHandle GetBoundHandle(AppContainerAuthoritySnapshot snapshot)
        {
            var handle = GetOrOpen(snapshot.Target);
            if (GetObjectIdentity(handle) != snapshot.ObjectIdentity)
                throw new IOException(
                    "An AppContainer content-authority target changed identity.");
            return handle;
        }

        private SafeFileHandle GetOrOpen(AppContainerAuthorityTarget target)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handles.TryGetValue(target, out var existing)) return existing;

            var handle = NativeMethods.CreateFile(
                target.Path,
                NativeMethods.ReadControl | NativeMethods.WriteDac,
                NativeMethods.FileShareRead |
                    NativeMethods.FileShareWrite |
                    NativeMethods.FileShareDelete,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileFlagBackupSemantics |
                    NativeMethods.FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(
                    error, "An AppContainer content-authority target could not be opened.");
            }
            try
            {
                if (!NativeMethods.GetFileAttributeTagInfo(
                        handle,
                        NativeMethods.FileAttributeTagInfoClass,
                        out var tagInfo,
                        Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "An AppContainer content-authority target shape could not be verified.");
                }
                var isDirectory =
                    (tagInfo.FileAttributes & FileAttributes.Directory) != 0;
                if ((tagInfo.FileAttributes & FileAttributes.ReparsePoint) != 0 ||
                    (target.Kind == AppContainerAuthorityTargetKind.VerifiedFile
                        ? isDirectory
                        : !isDirectory))
                {
                    throw new IOException(
                        "An AppContainer content-authority target has an invalid shape.");
                }
                _handles.Add(target, handle);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static AppContainerAuthorityObjectIdentity GetObjectIdentity(
            SafeFileHandle handle)
        {
            if (!NativeMethods.GetFileIdInfo(
                    handle,
                    NativeMethods.FileIdInfoClass,
                    out var info,
                    Marshal.SizeOf<NativeMethods.FileIdInfo>()))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "An AppContainer content-authority target identity could not be read.");
            }
            return new AppContainerAuthorityObjectIdentity(
                info.VolumeSerialNumber,
                $"{info.FileId.Low:X16}{info.FileId.High:X16}");
        }

        private static string GetAccessDescriptor(SafeFileHandle handle)
        {
            var result = NativeMethods.GetSecurityInfo(
                handle,
                NativeMethods.SeFileObject,
                NativeMethods.DaclSecurityInformation,
                out _,
                out _,
                out var dacl,
                out _,
                out var securityDescriptor);
            if (result != 0)
                throw new Win32Exception(
                    checked((int)result),
                    "An AppContainer content-authority DACL could not be read.");
            try
            {
                if (dacl == IntPtr.Zero)
                    throw new IOException(
                        "An AppContainer content-authority target has no bounded DACL.");
                if (!NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptor(
                        securityDescriptor,
                        NativeMethods.SddlRevision1,
                        NativeMethods.DaclSecurityInformation,
                        out var text,
                        out _))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "An AppContainer content-authority DACL could not be serialized.");
                }
                try
                {
                    return Marshal.PtrToStringUni(text)
                        ?? throw new IOException(
                            "An AppContainer content-authority DACL is unavailable.");
                }
                finally
                {
                    _ = NativeMethods.LocalFree(text);
                }
            }
            finally
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }

        private static void SetAccessDescriptor(
            SafeFileHandle handle,
            string descriptor)
        {
            if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                    descriptor,
                    NativeMethods.SddlRevision1,
                    out var securityDescriptor,
                    out _))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "An AppContainer content-authority DACL could not be parsed.");
            }
            try
            {
                if (!NativeMethods.GetSecurityDescriptorDacl(
                        securityDescriptor,
                        out var present,
                        out var dacl,
                        out _))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "An AppContainer content-authority DACL is unavailable.");
                }
                if (!present || dacl == IntPtr.Zero)
                    throw new IOException(
                        "An AppContainer content-authority target has no bounded DACL.");
                var raw = new RawSecurityDescriptor(descriptor);
                var protection =
                    (raw.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0
                        ? NativeMethods.ProtectedDaclSecurityInformation
                        : NativeMethods.UnprotectedDaclSecurityInformation;
                var result = NativeMethods.SetSecurityInfo(
                    handle,
                    NativeMethods.SeFileObject,
                    NativeMethods.DaclSecurityInformation | protection,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    dacl,
                    IntPtr.Zero);
                if (result != 0)
                    throw new Win32Exception(
                        checked((int)result),
                        "An AppContainer content-authority DACL could not be written.");
            }
            finally
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }

        private void RejectAlternateAppContainerAuthority(
            AppContainerAuthorityTarget target,
            string descriptor)
        {
            var security = target.Kind == AppContainerAuthorityTargetKind.VerifiedFile
                ? (FileSystemSecurity)CreateFileSecurity(descriptor)
                : CreateDirectorySecurity(descriptor);
            var grants = security.GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    targetType: typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>();
            if (grants.Any(rule =>
                    rule.AccessControlType == AccessControlType.Allow &&
                    rule.IdentityReference is SecurityIdentifier sid &&
                    !identity.Equals(sid) &&
                    sid.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal) &&
                    (rule.FileSystemRights & FileSystemRights.ReadAndExecute) != 0))
            {
                throw new IOException(
                    "An AppContainer content-authority target grants alternate application-package access.");
            }
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

        private FileSecurity CreateAppliedFileSecurity(
            AppContainerAuthoritySnapshot snapshot)
        {
            var security = CreateFileSecurity(snapshot.AccessDescriptor);
            security.PurgeAccessRules(identity);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                AccessControlType.Allow));
            return security;
        }

        private DirectorySecurity CreateAppliedDirectorySecurity(
            AppContainerAuthoritySnapshot snapshot)
        {
            var security = CreateDirectorySecurity(snapshot.AccessDescriptor);
            security.PurgeAccessRules(identity);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            return security;
        }
    }

    private static class NativeMethods
    {
        internal const uint ReadControl = 0x00020000;
        internal const uint WriteDac = 0x00040000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint FileShareDelete = 0x00000004;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagBackupSemantics = 0x02000000;
        internal const uint FileFlagOpenReparsePoint = 0x00200000;
        internal const int FileAttributeTagInfoClass = 9;
        internal const int FileIdInfoClass = 18;
        internal const int SeFileObject = 1;
        internal const uint DaclSecurityInformation = 0x00000004;
        internal const uint ProtectedDaclSecurityInformation = 0x80000000;
        internal const uint UnprotectedDaclSecurityInformation = 0x20000000;
        internal const uint SddlRevision1 = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileAttributeTagInfo
        {
            internal FileAttributes FileAttributes;
            internal uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileId128
        {
            internal ulong Low;
            internal ulong High;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdInfo
        {
            internal ulong VolumeSerialNumber;
            internal FileId128 FileId;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
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
        internal static extern bool GetFileAttributeTagInfo(
            SafeFileHandle file,
            int informationClass,
            out FileAttributeTagInfo information,
            int bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileIdInfo(
            SafeFileHandle file,
            int informationClass,
            out FileIdInfo information,
            int bufferSize);

        [DllImport("advapi32.dll")]
        internal static extern uint GetSecurityInfo(
            SafeFileHandle handle,
            int objectType,
            uint securityInformation,
            out IntPtr owner,
            out IntPtr group,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr securityDescriptor);

        [DllImport("advapi32.dll")]
        internal static extern uint SetSecurityInfo(
            SafeFileHandle handle,
            int objectType,
            uint securityInformation,
            IntPtr owner,
            IntPtr group,
            IntPtr dacl,
            IntPtr sacl);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertSecurityDescriptorToStringSecurityDescriptor(
            IntPtr securityDescriptor,
            uint requestedStringSdRevision,
            uint securityInformation,
            out IntPtr stringSecurityDescriptor,
            out uint stringSecurityDescriptorLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            out uint securityDescriptorSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSecurityDescriptorDacl(
            IntPtr securityDescriptor,
            [MarshalAs(UnmanagedType.Bool)] out bool daclPresent,
            out IntPtr dacl,
            [MarshalAs(UnmanagedType.Bool)] out bool daclDefaulted);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);

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
