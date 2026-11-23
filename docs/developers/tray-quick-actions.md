# Offering tray quick actions

Declare stable actions in the widget package's manifest. The host can list them while the worker is stopped.

```json
"trayActions": [
  { "id": "mute", "label": "Mute or unmute" },
  { "id": "sync", "label": "Sync now", "wakeOnInvoke": true }
]
```

When the command is available, add a `TrayOffer` to a published `WidgetQuickAction`. Its ID matches the manifest. Its target key identifies the account, resource and intended change. For example, Discord's key changes when the account, call, or current mute state changes. Use an opaque identifier without secrets. A display name or list index is insufficient.

Override `OnTrayActionAsync(TrayActionOffer offer, CancellationToken token)`. Recheck the current target. Return `TrayActionResult.Success` after completing the command, or return `TrayActionResult.Unavailable("The selected player disconnected.")` with a short explanation. The host delivers the reason for that press even if the worker finishes later. Returning the default result rejects the command with the host’s stale-target message. Send the exact operation to that target. The host can call this method on an already-resident background worker. Opening the chooser never starts it. Offscreen handlers must respect the cancellation token and existing capability grants. The host grants a one-use media transport operation only for the specified session and command. A physical dashboard gesture keeps its existing authority.

Omit the live offer when the command is unavailable. The assignment stays visible and becomes usable again when an appropriate offer is published. Set `TrayOnly` for an offer whose underlying control is unavailable on the widget's dashboard. Set `EmbeddedSessionId` if the command requires an existing, ready native embedded player.

Use `WidgetView.WithUnavailableTrayActions` to explain why specific manifest actions are unavailable in the current snapshot. Give the user a short next step, up to 160 characters on one line. The helper skips IDs already offered by the view. Clearing the reason or publishing a valid offer takes effect on the next snapshot or incremental update.

```csharp
return inVoiceCall ? view : view.WithUnavailableTrayActions(
    "Join a voice call to use Discord voice controls.", "mute", "deafen");
```

The desktop menu exposes the reason on the disabled row. Pressing the assigned direction on the radial tray shows the reason in a banner. If the configured fallback is currently executable, the tray displays its label and pressing the direction invokes it.

An optional manifest `fallbackId` names one other action in the same package. The user can enable that fallback in the chooser. To make the alternate eligible, its current live offer must set `AvailableAsFallbackFor` to the primary action's ID. A fallback displays its own label before activation. A wake-enabled slot can declare a `statusOperation` to let the tray choose one hint from host state while the worker sleeps. Capture Library uses `capture.replay.start`, `capture.replay.save` and `capture.replay.stop`. The hint grants no permissions. A press must produce a matching live offer for the displayed operation and recorder session or revision. Background bindings without status metadata can display both configured labels. Each explicit press prepares a fresh offer, including when the worker is still in its reuse interval. Visible widget and pin offers retain their exact displayed target. A failed or stale command never invokes a second command automatically.

## Background work

Each action defaults to resident-only dispatch. Set `wakeOnInvoke` on an action that can run after a cold start. The host checks the installed generation, prepares the action in Background, and obtains a fresh offer. Override `PrepareTrayActionAsync` to refresh the state needed for an explicit press. Publish named offers through `WidgetQuickAction.ForTray`. The user assigns their directions.

Return or await the actual work from `OnTrayActionAsync`. The SDK signals completion to the host. For a cold-woken worker, the five-second reuse interval starts once tracked work finishes. Opening the widget promotes that run to its ordinary lifecycle. When the user leaves, its normal idle delay starts. Outstanding work can keep the process alive beyond that deadline. A widget handing off a host-backed job can return after acceptance. For example, `CaptureToLibraryAsync` hands confirmation, file writing and final feedback to the host. An explicitly stopped provider stays stopped.

Capture commands require the exact capture gesture and the package's existing permissions. Saving also requires capture-library save permission. Replay controls bind to the observed recorder revision or session. If a prepared offer sets `NeedsPermission`, the tray offers to open the widget for its ordinary permission flow.
