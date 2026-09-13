using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using WidgetRail.WidgetRuntime;

internal static class DaclRestorationScenarios
{
    public static Task ComparisonPreservesAuthority()
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        const string original = "D:P(A;;FR;;;SY)(A;;FA;;;BA)";
        const string normalized = "D:PAI(A;;FR;;;SY)(A;;FA;;;BA)";
        if (!AppContainerDaclComparison.Matches(original, normalized) ||
            !AppContainerDaclComparison.Matches(normalized, original))
            throw new Exception("AI-only normalization must preserve DACL authority.");
        foreach (var changed in new[]
        {
            "D:AI(A;;FR;;;SY)(A;;FA;;;BA)", // Inheritance protection removed.
            "D:PAR(A;;FR;;;SY)(A;;FA;;;BA)", // Auto-inherit request is not the AI result flag.
            "D:PAI(A;;FW;;;SY)(A;;FA;;;BA)", // Changed access mask.
            "D:PAI(D;;FR;;;SY)(A;;FA;;;BA)", // Changed allow/deny semantics.
            "D:PAI(A;;FR;;;BU)(A;;FA;;;BA)", // Changed identity.
            "D:PAI(A;ID;FR;;;SY)(A;;FA;;;BA)", // Changed ACE inheritance.
            "D:PAI(A;;FA;;;BA)(A;;FR;;;SY)", // Changed ACE order.
            "D:PAI(A;;FR;;;SY)", // Removed ACE.
            "D:PAI(A;;FR;;;SY)(A;;FA;;;BA)(A;;FR;;;WD)", // Added authority.
            "D:P", // Empty ACL.
            "D:NO_ACCESS_CONTROL", // Null ACL is not a bounded ACL.
        })
            if (AppContainerDaclComparison.Matches(original, changed))
                throw new Exception("A changed DACL was incorrectly treated as restored: " + changed);
        return Task.CompletedTask;
    }

    public static Task LegacyDescriptorRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-dacl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var key = "dacl-normalization-" + Guid.NewGuid().ToString("N");
        try
        {
            var file = Path.Combine(root, "fixture.txt");
            File.WriteAllText(file, "synthetic ACL fixture");
            using var identity = WindowsIdentity.GetCurrent();
            var descriptor = new RawSecurityDescriptor(
                $"D:P(A;;FA;;;{identity.User!.Value})(A;;FA;;;SY)");
            var bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            // The legacy setter produces a protected descriptor without AI,
            // matching the shape that modern SetSecurityInfo normalizes on CI.
            foreach (var path in new[] { root, file })
                if (SetFileSecurity(path, 4, bytes) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using var container = WindowsAppContainer.OpenOrCreate(key);
            using var operations = container.CreateAuthorityOperationsForTesting();
            foreach (var target in new[]
            {
                new AppContainerAuthorityTarget(root, AppContainerAuthorityTargetKind.AuthorityRootDirectory),
                new AppContainerAuthorityTarget(file, AppContainerAuthorityTargetKind.VerifiedFile),
            })
            {
                var original = operations.Capture(target);
                operations.Apply(original);
                operations.VerifyApplied(original);
                operations.Restore(original);
                operations.VerifyRestored(original);
                if (!AppContainerDaclComparison.Matches(original.AccessDescriptor,
                        operations.Capture(target).AccessDescriptor))
                    throw new Exception("Real DACL permissions changed after restoration.");
            }
        }
        finally
        {
            _ = DeleteAppContainerProfile(WindowsAppContainer.ProfileNameFor(key));
            var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(temporary, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Fixture cleanup escaped the temporary directory.");
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    [DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetFileSecurity(string path, uint securityInformation, byte[] descriptor);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeleteAppContainerProfile(string name);
}
