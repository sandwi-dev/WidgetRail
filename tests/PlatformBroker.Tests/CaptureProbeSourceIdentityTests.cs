using WidgetRail.OverlayFrontend.WinUI.Validation;
using WidgetRail.PlatformBroker;

internal static class CaptureProbeSourceIdentityTests
{
    internal static Task Run()
    {
        var owner = CaptureProbeSourceIdentity.FromArguments(new[] { "--capture-probe-source-pid=123", "--capture-probe-source-created=0123456789ABCDEF" });
        var target = new NativeWindowPreviewTarget("A", 123, "0123456789abcdef", "SyntheticClass");
        Check(owner.Matches(target), "Exact runner-owned process is admitted.");
        Check(!owner.Matches(target with { ProcessId=124 }) && !owner.Matches(target with { ProcessCreated="0123456789ABCDE0" }) && !owner.Matches(null),
            "A matching title or recycled PID cannot select another source.");
        foreach(var arguments in new[]
        {
            Array.Empty<string>(),
            new[]{"--capture-probe-source-pid=123"},
            new[]{"--capture-probe-source-pid=0","--capture-probe-source-created=0123456789ABCDEF"},
            new[]{"--capture-probe-source-pid=123","--capture-probe-source-created=0000000000000000"},
            new[]{"--capture-probe-source-pid=123","--capture-probe-source-created=bad"},
            new[]{"--capture-probe-source-pid=123","--capture-probe-source-pid=124","--capture-probe-source-created=0123456789ABCDEF"}
        })
        {
            var rejected=false;
            try{CaptureProbeSourceIdentity.FromArguments(arguments);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Missing, malformed and ambiguous source identities fail closed.");
        }
        return Task.CompletedTask;
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
