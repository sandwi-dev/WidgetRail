using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

if (args.Length == 2 && args[0] == "--fixture-child")
{
    await File.WriteAllTextAsync(args[1], $"child:{Environment.ProcessId}");
    return 0;
}

var instanceId = Required(args, "--widget-instance");
var probe = await FullTrustProbe.RunAsync(instanceId);
return await WidgetApplicationBootstrap.RunAsync(args, () => new ProbeWidget(probe));

static string Required(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length)
        throw new ArgumentException($"Missing {name}.");
    return values[index + 1];
}

file sealed record ProbeResult(int RunCount, bool Child, bool File, bool Database, bool Https);

file static class FullTrustProbe
{
    internal static async Task<ProbeResult> RunAsync(string instanceId)
    {
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(instanceId)))[..20].ToLowerInvariant();
        var root = Path.Combine(Path.GetTempPath(), "gba-full-trust-alpha", identity);
        Directory.CreateDirectory(root);

        var filePath = Path.Combine(root, "ordinary-file.txt");
        await File.WriteAllTextAsync(filePath, "ordinary-current-user-file");
        var fileWorked = await File.ReadAllTextAsync(filePath) ==
            "ordinary-current-user-file";

        var databasePath = Path.Combine(root, "records.json");
        var runCount = await FileDatabase.IncrementAsync(databasePath);
        var databaseWorked = runCount >= 1 && File.Exists(databasePath);

        var childPath = Path.Combine(root, $"child-{runCount}.txt");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--fixture-child");
        start.ArgumentList.Add(childPath);
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("The deterministic child did not start.");
        await child.WaitForExitAsync();
        var childWorked = child.ExitCode == 0 && File.Exists(childPath);

        using var client = new HttpClient(new FakeHttpsHandler());
        var httpsWorked = await client.GetStringAsync("https://fixture.invalid/status") ==
            "fake-https-ok";
        return new(runCount, childWorked, fileWorked, databaseWorked, httpsWorked);
    }
}

file static class FileDatabase
{
    internal static async Task<int> IncrementAsync(string path)
    {
        var current = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, int>>(
                await File.ReadAllTextAsync(path)) ?? []
            : [];
        current["runs"] = current.GetValueOrDefault("runs") + 1;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(current));
        File.Move(temporary, path, overwrite: true);
        return current["runs"];
    }
}

file sealed class FakeHttpsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RequestUri is not { Scheme: "https" })
            throw new InvalidOperationException("The fake client accepted a non-HTTPS URI.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("fake-https-ok", Encoding.UTF8, "text/plain"),
        });
    }
}

file sealed class ProbeWidget(ProbeResult probe) : Widget
{
    public override WidgetView Render() => new(UI.Stack("alpha-root",
        UI.Text("Full-trust alpha", "alpha-title", "Fixture title"),
        UI.Text(
            $"run={probe.RunCount.ToString(CultureInfo.InvariantCulture)} child={probe.Child} file={probe.File} database={probe.Database} https={probe.Https}",
            "alpha-result", "Full-trust capability result"),
        UI.Button("Crash for restart", "crash", "alpha-crash")),
        InitialFocusId: "alpha-crash");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ActionId == "crash")
            Environment.FailFast("deterministic full-trust restart fixture");
        return ValueTask.CompletedTask;
    }
}
