using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.Internal;

internal static class CaptureProcessKey
{
    // Shared by Windows capture and external integrations. No path, command line or reusable PID is published.
    internal static string Create(uint pid, ulong creationFileTime)
    {
        if (pid == 0 || creationFileTime == 0) throw new ArgumentOutOfRangeException();
        var value = string.Format(CultureInfo.InvariantCulture, "WidgetRail/process/v1/{0:X8}/{1:X16}", pid, creationFileTime);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
}
