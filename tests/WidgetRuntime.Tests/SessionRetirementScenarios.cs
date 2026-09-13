using System.Diagnostics;
using WidgetRail.WidgetRuntime;

internal static class SessionRetirementScenarios
{
    public static async Task ExitDiagnosticSurvivesCleanup()
    {
        if (!OperatingSystem.IsWindows()) return;
        var session = new WidgetProcessSession(TimeProvider.System, TimeSpan.FromSeconds(5));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/d", "/c", "exit", "65" },
        };
        try
        {
            session.StartProcess(() => (Process.Start(start)!, null));
            await session.Process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            // Force retirement to finish before the startup waiter inspects the
            // diagnostic, rather than depending on thread scheduling in a test.
            await session.DisposeAsync();
            if (session.Process is not null || session.ExitCode != 65)
                throw new Exception("Session cleanup lost the worker exit diagnostic.");
        }
        finally
        {
            session.Terminate();
            await session.DisposeAsync();
        }
    }
}
