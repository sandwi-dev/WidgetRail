using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeAppUpdates
{
    private string? recoveryRoot;
    private void RefreshRecovery()
    {
        recoveryRoot = null;
        AppUpdateRecovery? recovery = null;
        if (_installation.CanInstall && Directory.Exists(_cache))
        {
            try
            {
                PlainPath(_cache);
                foreach (var root in Directory.EnumerateDirectories(_cache).Take(16).OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    try
                    {
                        if (!Guid.TryParseExact(Path.GetFileName(root), "N", out _)) continue;
                        PlainPath(root);
                        var planPath = Path.Combine(root, "plan.json"); var statePath = Path.Combine(root, "state.json");
                        if (!File.Exists(planPath) || !File.Exists(statePath)) continue;
                        PlainPath(planPath); PlainPath(statePath);
                        if (new FileInfo(planPath).Length > 8192 || new FileInfo(statePath).Length > 8192) continue;
                        var planText = File.ReadAllText(planPath); var stateText = File.ReadAllText(statePath);
                        using var plan = JsonDocument.Parse(planText); using var state = JsonDocument.Parse(stateText);
                        var phase = state.RootElement.GetProperty("phase").GetString();
                        var previous = plan.RootElement.GetProperty("previousVersion").GetString();
                        var updated = plan.RootElement.GetProperty("updateVersion").GetString();
                        if (plan.RootElement.GetProperty("schema").GetInt32() != 1 || updated != _installation.Version ||
                            plan.RootElement.GetProperty("edition").GetString() != _installation.Edition || string.IsNullOrWhiteSpace(previous) ||
                            previous.Length > 80 || updated.Length > 80 || previous.Any(char.IsControl) ||
                            phase is not ("backup-ready" or "installing" or "installed" or "install-failed" or "restoring-installer" or "restoring-profile" or "profile-preserved")) continue;
                        if (!File.Exists(Path.Combine(root, "previous.exe")) || !File.Exists(Path.Combine(root, "Recover.ps1"))) continue;
                        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root + "\n" + planText + "\n" + stateText)));
                        recovery = new(id, previous, updated, phase); recoveryRoot = root; break;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                    { /* A partial or damaged recovery set must not hide another valid backup. */ }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        }
        _snapshot = _snapshot with { Recovery = recovery };
    }
    private AppUpdateSnapshot RequestRecovery(string token)
    {
        if (_job?.IsCompleted == false || _snapshot.State == "handoff" || !_installation.CanInstall) return _snapshot;
        RefreshRecovery();
        if (_snapshot.Recovery?.Id != token || recoveryRoot is null)
        { Set(_snapshot with { Message = "Recovery changed or is unavailable. Review App updates again." }); return _snapshot; }
        try
        {
            _recover(recoveryRoot);
            Set(_snapshot with { State = "handoff", ReviewToken = null, Message = "Quit WidgetRail to continue recovery. The previous app and profile will be restored after confirmation." });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Set(_snapshot with { Message = "Could not start recovery. Try again from App updates." }); }
        return _snapshot;
    }
}
