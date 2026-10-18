using System.Text.Json;
using System.Text.Json.Nodes;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeAppUpdates
{
    private static async Task SetInteractivePlanAsync(string root, bool interactive, CancellationToken token)
    {
        var path = Path.Combine(root, "plan.json");
        PlainPath(path);
        if (new FileInfo(path).Length > 8192) throw new IOException("Invalid update plan.");
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(path, token).ConfigureAwait(false))?.AsObject()
            ?? throw new IOException("Invalid update plan.");
        plan["interactive"] = interactive;
        await File.WriteAllTextAsync(path, plan.ToJsonString(), token).ConfigureAwait(false);
    }

    private void ReadInstallerOutcome()
    {
        var path = Path.Combine(_cache, "result.json");
        try
        {
            PlainPath(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024) return;
            using var result = JsonDocument.Parse(File.ReadAllText(path));
            var outcome = result.RootElement.GetProperty("outcome").GetString();
            var message = outcome switch
            {
                "cancelled" => "Setup was cancelled. Your verified installation reopened. Check for updates to retry when ready.",
                "failed" => "Setup did not finish. Your verified installation reopened. Check for updates to retry, or run the downloaded installer interactively.",
                "restart-required" => "Setup requires a Windows restart. Save your work and restart Windows before retrying the update.",
                "startup-warning" => "The update installed, but startup at sign-in could not be restored. Run setup again and select Start WidgetRail when I sign in.",
                _ => null,
            };
            File.Delete(path);
            if (message is null) return;
            // Do not immediately replace a recovery result with an automatic check.
            // Manual Check for updates remains the normal recovery/retry action.
            _startupChecked = true;
            _snapshot = _snapshot with { State = "failed", Message = message, InstallerOutcomePending = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException) { }
    }
}
