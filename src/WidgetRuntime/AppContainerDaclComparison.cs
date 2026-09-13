using System.Runtime.Versioning;
using System.Security.AccessControl;

namespace WidgetRail.WidgetRuntime;

/// <summary>Compares captured DACL authority, allowing Windows' AI bookkeeping normalization.</summary>
[SupportedOSPlatform("windows")]
internal static class AppContainerDaclComparison
{
    internal static bool Matches(string expected, string actual)
    {
        var before = new RawSecurityDescriptor(expected);
        var after = new RawSecurityDescriptor(actual);
        // SetSecurityInfo can add SE_DACL_AUTO_INHERITED even to a protected
        // DACL. It is bookkeeping, not a change to inheritance protection or an
        // ACE's inherited flag. Every other control bit must remain identical.
        const ControlFlags bookkeeping = ControlFlags.DiscretionaryAclAutoInherited;
        if ((before.ControlFlags & ~bookkeeping) != (after.ControlFlags & ~bookkeeping) ||
            before.DiscretionaryAcl is null || after.DiscretionaryAcl is null)
            return false;

        // Preserve ACE order, type, flags, SID and mask exactly. Do not sort,
        // merge entries, ignore inherited ACEs, or compare only effective rights.
        var beforeBytes = new byte[before.DiscretionaryAcl.BinaryLength];
        var afterBytes = new byte[after.DiscretionaryAcl.BinaryLength];
        before.DiscretionaryAcl.GetBinaryForm(beforeBytes, 0);
        after.DiscretionaryAcl.GetBinaryForm(afterBytes, 0);
        return beforeBytes.AsSpan().SequenceEqual(afterBytes);
    }
}
