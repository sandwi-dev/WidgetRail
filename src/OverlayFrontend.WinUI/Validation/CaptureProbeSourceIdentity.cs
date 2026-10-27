using System.Globalization;
using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Self-tests select only the process incarnation launched by their runner.</summary>
internal sealed record CaptureProbeSourceIdentity(uint ProcessId, string Created)
{
    internal static CaptureProbeSourceIdentity FromArguments(IReadOnlyList<string> arguments)
    {
        string Required(string name)
        {
            var values = arguments.Where(argument => argument.StartsWith(name + "=", StringComparison.Ordinal)).ToArray();
            if (values.Length != 1) throw new InvalidOperationException("The self-test requires an exact runner-owned source identity.");
            return values[0][(name.Length + 1)..];
        }
        var process = Required("--capture-probe-source-pid");
        var created = Required("--capture-probe-source-created");
        if (!uint.TryParse(process, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == 0 ||
            created.Length != 16 || !created.All(char.IsAsciiHexDigit) || !ulong.TryParse(created, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var time) || time == 0)
            throw new InvalidOperationException("Invalid synthetic source process identity.");
        return new(pid, created);
    }
    internal bool Matches(NativeWindowPreviewTarget? target) => target is not null && target.ProcessId == ProcessId &&
        target.ProcessCreated.Equals(Created, StringComparison.OrdinalIgnoreCase);
}
