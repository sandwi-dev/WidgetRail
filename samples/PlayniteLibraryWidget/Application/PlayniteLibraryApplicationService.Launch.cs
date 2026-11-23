using System.ComponentModel;
using System.Diagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteLibraryApplicationService
{
    public ValueTask<PlayniteWidgetLaunchResult> LaunchFromWidgetAsync(string appId, WidgetHostServices host,
        Func<bool> current, CancellationToken token, string? approval, Action admitted) =>
        HandoffAsync(appId, host, current, token, approval, admitted, false);
    public ValueTask<PlayniteWidgetLaunchResult> ReturnToGameAsync(string appId, WidgetHostServices host, Func<bool> current, CancellationToken token) =>
        HandoffAsync(appId, host, current, token, null, null, true);

    private async ValueTask<PlayniteWidgetLaunchResult> HandoffAsync(string appId, WidgetHostServices host,
        Func<bool> current, CancellationToken token, string? approval, Action? admitted, bool returnOnly)
    {
        using var connection = ConnectionScope(token);
        token = connection.Token;
        var game = await _client.ResolveGameAsync(appId, token).ConfigureAwait(false);
        if (game?.IsInstalled != true) throw new WidgetCapabilityException("app_not_found", "Game is no longer installed.");
        var permission = WidgetTaskSwitcherCapabilities.Switch.CapabilityId;
        WidgetLaunchHandoffTicket? ticket = null;
        var permissionGranted = false;
        try
        {
            var state = (await host.Permissions.CheckAsync([permission], token).ConfigureAwait(false)).Single().State;
            permissionGranted = state == WidgetPermissionState.Granted || (state == WidgetPermissionState.NotDecided &&
                await host.Permissions.RequestAsync([permission], token).ConfigureAwait(false) == WidgetPermissionOutcome.Granted);
            if (permissionGranted)
                ticket = await host.TaskSwitcher.BeginLaunchAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException) { }
        token.ThrowIfCancellationRequested();
        if (!current()) return Result(WidgetAppLaunchObservationState.Ended);
        if (returnOnly && ticket is null)
            return Result(WidgetAppLaunchObservationState.Running, permissionGranted ? "Open the game from the task switcher."
                : "Allow window switching in Playnite Library permissions to resume the game.");
        var receipt = await _client.BeginObservedGameAsync(game.Id, token, approval, returnOnly).ConfigureAwait(false);
        if (receipt is null && returnOnly) return Result(WidgetAppLaunchObservationState.Ended, "Cannot return to this game. Check that it is running and the Playnite add-on is up to date.");
        if (receipt is null) throw new WidgetCapabilityException("app_not_found", "Playnite could not launch the game.");
        var accepted = WidgetAppLaunchObservationState.RequestAccepted;
        if (receipt.Conflict is { } conflict) return new(new(WidgetAppLaunchObservationState.Ended, false, false), Conflict: conflict);
        admitted?.Invoke();
        var running = WidgetAppLaunchObservationState.Running;
        if (receipt.Observation is null)
            return Result(accepted, "Launch sent. Update the WidgetRail add-on in Playnite for automatic switching.");
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(token);
        observation.CancelAfter(TimeSpan.FromSeconds(25));
        var wait = observation.Token;
        try
        {
            while (current())
            {
                var status = await _client.GetLaunchStatusAsync(game.Id, receipt, wait).ConfigureAwait(false);
                if (status.State == "ended") return Result(WidgetAppLaunchObservationState.Ended, "Game launch ended.");
                if (status.State == "unconfirmed") break;
                if (status.State == "running")
                {
                    accepted = running;
                    if (ticket is null)
                        return Result(running, permissionGranted ? "Game started. Open it from the task switcher."
                            : "Game started. Enable window switching in widget permissions to bring it forward.");
                    if (status.ProcessId <= 0 || status.CreatedFileTime <= 0) break;
                    var window = MainWindow(status);
                    if (window != 0 && current() && await host.TaskSwitcher.TryCompleteLaunchAsync(
                        new(ticket.Ticket, (uint)status.ProcessId, status.CreatedFileTime, window), wait).ConfigureAwait(false))
                        return Result(running);
                }
                await Task.Delay(500, wait).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested && !token.IsCancellationRequested) { }
        catch (Exception error) when (error is PlayniteBridgeDataException or PlayniteBridgeTransportException or
            WidgetCapabilityException or WidgetCapabilityUnavailableException or System.Text.Json.JsonException)
        { /* Launch was already admitted. Do not retry the mutation on loss of observation or authority. */ }
        if (returnOnly) return Result(accepted, "Couldn’t confirm the game window. Open it from the task switcher.");
        return Result(accepted, accepted == running ? "Game started. Use the task switcher to bring it forward."
            : "Launch sent. Check Playnite or the launcher for a prompt.");
    }

    private static long MainWindow(PlayniteLaunchProgress status)
    {
        try
        {
            using var process = Process.GetProcessById(status.ProcessId);
            return process.StartTime.ToUniversalTime().ToFileTimeUtc() == status.CreatedFileTime ? process.MainWindowHandle.ToInt64() : 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return 0; }
    }

    private static PlayniteWidgetLaunchResult Result(WidgetAppLaunchObservationState state, string? message = null) =>
        new(new(state, SupportsRunning: state == WidgetAppLaunchObservationState.Running, SupportsEnded: false), message);
}
