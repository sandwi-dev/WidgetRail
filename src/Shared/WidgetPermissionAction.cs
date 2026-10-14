using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Widgets;

/// <summary>Sample/first-party authoring helper. Consent stays in the existing host service.</summary>
internal static class WidgetPermissionAction
{
    internal static async ValueTask RunAsync(WidgetPermissionService permissions, WidgetOperations operations,
        string[] capabilities, Func<CancellationToken, ValueTask> action, Action<string> feedback,
        CancellationToken cancellationToken, Action? granted = null)
    {
        if (capabilities.Length == 0) { await action(cancellationToken); return; }
        if (operations.IsBusy("permissions.action")) return;
        IReadOnlyList<WidgetPermissionStatus> states;
        try { states = await permissions.CheckAsync(capabilities, cancellationToken); }
        catch (WidgetCapabilityException error) when (error.ErrorCode is "unsupported_capability" or "capability_not_declared")
        {
            // Older hosts still enforce capability access on the original operation.
            await action(cancellationToken); return;
        }
        catch (WidgetCapabilityException) { feedback("Access could not be checked. Try again or review this widget's permissions in Settings."); return; }
        if (states.All(state => state.State == WidgetPermissionState.Granted)) { await action(cancellationToken); return; }
        // Return control to the widget runtime while the host owns the consent dialog.
        var operation = operations.RunSingleFlight("permissions.action", async context =>
        {
            try
            {
                var outcome = await permissions.RequestAsync(capabilities, context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (outcome == WidgetPermissionOutcome.Granted) { granted?.Invoke(); await action(context.CancellationToken); }
                else feedback(outcome switch
                {
                    WidgetPermissionOutcome.Cancelled => "Permission request cancelled. Nothing was changed.",
                    WidgetPermissionOutcome.Denied => "Access was not allowed. Select the action again to request access.",
                    _ => "Permission approval is unavailable. Try again from the open widget or review its permissions in Settings.",
                });
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (WidgetCapabilityException) { feedback("Access could not be checked. Try again or review this widget's permissions in Settings."); }
        });
        if (!operation.IsAccepted) feedback("Open the widget and try the action again.");
    }
}
