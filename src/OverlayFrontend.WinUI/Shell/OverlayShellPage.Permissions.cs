using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private Task? permissionPump, permissionPrompt;
    private async Task PumpPermissionsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (permissionPrompt is { IsCompleted: false } || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) continue;
                try
                {
                    var pending = await owner!.Session.TakePermissionAsync(lifetime.Token);
                    if (pending.Prompt is { } prompt) permissionPrompt = RunPermissionPromptAsync(prompt);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("permission-pump", error); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private async Task RunPermissionPromptAsync(BridgePermissionPrompt prompt)
    {
        var session = owner!.Session;
        var outcome = WidgetPermissionOutcome.Unavailable;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(2));
        var token = cancellation.Token; Task? renewal = null;
        var sourcePin = PinnedInputActive && pinned?.WidgetId == prompt.WidgetId ? pinned : null;
        bool Eligible() => !retired && visible && !LocalInstallActive && !SystemFilePickerOpen && (sourcePin is not null
            ? ReferenceEquals(pinned, sourcePin) && sourcePin.IsCurrent && PinnedInputActive
            : foreground && interactive && activeWidget == prompt.WidgetId);
        try
        {
            if (!Eligible() || !await session.IsPermissionCurrentAsync(prompt.RequestId, token)) return;
            // A tiny pin cannot show a readable native permission dialog. Report
            // unavailable so the widget can offer Settings or its main view.
            if (sourcePin?.Window.AutomationRoot.XamlRoot is { } pinRoot && (pinRoot.Size.Width < 320 || pinRoot.Size.Height < 280)) return;
            renewal = WatchAsync();
            var selected = await ShowHostChoiceAsync("Allow widget access?",
                PermissionPromptMessage(prompt),
                (HostDialogChoice[])[new("allow", "Allow"), new("deny", "Deny")], token, directDecisionButtons: true, pinnedOwner: sourcePin);
            if (!Eligible() || !await session.IsPermissionCurrentAsync(prompt.RequestId, token)) return;
            outcome = selected switch { "allow" => WidgetPermissionOutcome.Granted, "deny" => WidgetPermissionOutcome.Denied, _ => WidgetPermissionOutcome.Cancelled };
        }
        catch (OperationCanceledException) { outcome = WidgetPermissionOutcome.Cancelled; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("permission-dialog", error); outcome = WidgetPermissionOutcome.Unavailable; }
        finally
        {
            cancellation.Cancel();
            if (renewal is not null) await renewal;
            if (!retired)
                try { await session.CompletePermissionAsync(prompt.RequestId, outcome, lifetime.Token); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Diagnostics.FrontendFailureLog.Current.Write("permission-completion", error); }
        }
        async Task WatchAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(300, token);
                    if (!Eligible() || !await session.IsPermissionCurrentAsync(prompt.RequestId, token)) { cancellation.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException) { cancellation.Cancel(); }
        }
    }
    internal static string PermissionPromptMessage(BridgePermissionPrompt prompt) =>
        prompt.WidgetName + " is requesting permission to:\n\n" + string.Join("\n", prompt.PermissionNames.Select(name => "• " + name)) +
        "\n\nApplies to this widget. You can change access in Settings → Widgets. Windows may ask for separate approval.";
}
