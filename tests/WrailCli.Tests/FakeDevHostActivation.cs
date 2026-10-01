using System.Diagnostics;
using WidgetRail.WrailCli;

// Exercises real job self-enrollment/cleanup with this fixture's child process.
// Real direct-launch image verification is also covered by the named-job fixture.
internal sealed class FakeDevHostActivation(bool rejectIdentity = false, bool skipEnrollment = false) : IDevHostActivation
{
    internal static DevHostTarget Target => new(Environment.ProcessPath!, AppContext.BaseDirectory);
    internal int? LastProcessId { get; private set; }

    public Process Activate(DevHostTarget target, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (skipEnrollment) start.ArgumentList.Add("--dev-persistent-grandchild");
        else foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture child did not start.");
        LastProcessId = process.Id;
        return process;
    }

    public void VerifyIdentity(DevHostTarget target, Process process)
    {
        if (rejectIdentity) throw new CliOperationException("Fixture rejected application identity.");
        if (target != Target || process.Id != LastProcessId ||
            !string.Equals(process.StartInfo.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected fixture process identity.");
    }
}

internal sealed class ControlledNativeDevActivation : IDevHostActivation
{
    private readonly WindowsDevHostActivation native = new();
    private int failInteractive;
    internal int InjectedFailures { get; private set; }
    internal void RejectNextInteractive() => Interlocked.Exchange(ref failInteractive, 1);
    public Process Activate(DevHostTarget target, IReadOnlyList<string> arguments)
    {
        if (!arguments.Contains("--development-probe-only") && Interlocked.Exchange(ref failInteractive, 0) != 0)
        {
            InjectedFailures++;
            throw new CliOperationException("Controlled smoke failure before replacement activation.");
        }
        return native.Activate(target, arguments);
    }
    public void VerifyIdentity(DevHostTarget target, Process process) => native.VerifyIdentity(target, process);
}
