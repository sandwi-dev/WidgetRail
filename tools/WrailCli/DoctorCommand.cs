using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WidgetRail.WrailCli;

internal sealed record DoctorCheck(string Id, string Status, string Message, string? Remedy = null);
internal sealed record SdkProbeResult(bool Success, string Output, string? Problem = null);

internal static class DoctorCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<SdkProbeResult>>? probe = null,
        Func<string?, string>? resolveHost = null)
    {
        var parsed = new CommandArguments(args, ["--host"], ["--json"]);
        if (parsed.Positionals.Count > 1)
            throw new CliUsageException("Usage: wrail doctor [project-directory|widget.csproj] [--host <OverlayHost.exe>] [--json]");
        var target = Path.GetFullPath(parsed.Positionals.SingleOrDefault() ?? Environment.CurrentDirectory);
        var directory = File.Exists(target) && Path.GetExtension(target).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(target)! : target;
        if (!Directory.Exists(directory)) throw new CliUsageException("Supply an existing project directory or .csproj.");
        var checks = new List<DoctorCheck>
        {
            new("platform", OperatingSystem.IsWindows() ? "pass" : "warning",
                OperatingSystem.IsWindows() ? "Windows development host is supported." : "Packaging tools work here; the overlay requires Windows."),
        };
        try
        {
            var release = WidgetSdkReleaseContract.Current;
            checks.Add(new("cli-sdk", "pass", $"CLI and SDK release {release.Version}; template contract {release.TemplateVersion}."));
        }
        catch (Exception exception) when (exception is CliUsageException or TypeInitializationException)
        {
            checks.Add(new("cli-sdk", "fail", "CLI/SDK release metadata is missing or inconsistent.", "Reinstall the complete WidgetRail CLI and SDK together."));
        }
        var sdk = await (probe ?? ((path, token) => ProbeSdkAsync(path, token)))(directory, cancellationToken);
        if (sdk.Success && Version.TryParse(sdk.Output.Trim().Split('-')[0], out var version) && version.Major >= 8)
            checks.Add(new("dotnet-sdk", "pass", $"Selected .NET SDK: {sdk.Output.Trim()} (from {directory})."));
        else
            checks.Add(new("dotnet-sdk", "fail", sdk.Problem ?? "No usable .NET SDK 8 or newer was selected.",
                "Install a .NET SDK supporting net8.0 and check this project's global.json. Run dotnet --version from the project directory."));
        try
        {
            var host = (resolveHost ?? (requested => DevHostLocator.Resolve(requested, "Release")))(parsed.Option("--host"));
            checks.Add(new("overlay-host", "pass", $"Complete packaged host found: {host}. File presence checked; the host was not launched."));
        }
        catch (Exception exception) when (exception is CliUsageException or IOException or UnauthorizedAccessException)
        {
            checks.Add(new("overlay-host", "fail", DevSession.SafeMessage(exception),
                "Install WidgetRail or build the packaged overlay; use --host to select another complete installation."));
        }
        var passed = checks.All(check => check.Status != "fail");
        if (parsed.HasFlag("--json"))
            await output.WriteLineAsync(JsonSerializer.Serialize(new { schemaVersion = 1, passed, checks }, DiagnosticJson.Options));
        else
        {
            await output.WriteLineAsync("WidgetRail development check");
            foreach (var check in checks)
            {
                await output.WriteLineAsync($"[{check.Status.ToUpperInvariant()}] {check.Id}: {check.Message}");
                if (check.Remedy is not null) await output.WriteLineAsync($"  Fix: {check.Remedy}");
            }
            await output.WriteLineAsync(passed ? "Development prerequisites found." : "Resolve the failed checks, then run wrail doctor again.");
        }
        return passed ? 0 : 1;
    }

    internal static async Task<SdkProbeResult> ProbeSdkAsync(string directory, CancellationToken cancellationToken,
        ProcessStartInfo? testStart = null)
    {
        var start = testStart ?? new ProcessStartInfo("dotnet");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.WorkingDirectory = directory;
        if (testStart is null) start.ArgumentList.Add("--version");
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            process.Start();
            var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            return new(process.ExitCode == 0, await stdout,
                process.ExitCode == 0 ? null : "dotnet could not select an SDK for this directory.");
        }
        catch (Win32Exception) { return new(false, "", "dotnet was not found or could not start."); }
        catch (OperationCanceledException)
        {
            var reclaimed = false;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                reclaimed = true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or TimeoutException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, "", reclaimed ? "dotnet did not respond within 10 seconds."
                : "dotnet timed out and process cleanup could not be confirmed.");
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
            if (result.Length < 8192) result.Append(buffer, 0, Math.Min(count, 8192 - result.Length));
        return result.ToString();
    }
}
