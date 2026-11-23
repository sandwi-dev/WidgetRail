# Application capabilities

Declare the capability in the manifest, obtain user approval, then use its typed
service. Both sandboxed and full-trust application widgets use these APIs. See [Capabilities](capabilities.md) for consent, lifecycle, and error behavior.

| Capability | SDK operation | Availability |
|---|---|---|
| `system.activity.recent.read.v1` | `HostServices.RecentActivity.GetRecentAsync`, `OpenSubscriptionAsync`, and `WatchAsync` | Visible or Interactive |
| `system.apps.library.read.v1` | `HostServices.AppLibrary.QueryAsync(query, cursor, direction, limit, refresh)` and `ResolveSavedAsync(savedIds)` for bounded cursor pages, sanitized names/source labels, conservative kinds, short-lived launch IDs, authority-scoped durable SavedIds, and up to 16 observation-only source-health rows bound to the page revision | Visible or Interactive |
| `system.apps.running.read.v1` | `HostServices.AppLibrary.ObserveRunningAsync()` and `ConfirmRunningAsync(savedId, revision)` for bounded privacy-safe current-window observations; no path, process, window, command line, or launch authority | Visible or Interactive |
| `system.apps.running.register.v1` | `RegisterRunningAsync(savedId, revision)` and `ForgetRunningAsync(savedId)` for explicit package-owned portable-app registration and removal | Interactive only; never dashboard gesture authority |
| `system.apps.library.launch.v1` | `HostServices.AppLibrary.LaunchAsync(appId)` for one current broker-issued app ID | Interactive only; never dashboard gesture authority |
| `system.media.sessions.read.v1` | `HostServices.Media.GetSessionsAsync`, `OpenSubscriptionAsync`, and `WatchAsync` | Visible or Interactive |
| `system.media.sessions.control.v1` | `HostServices.Media.ControlAsync` for one broker-issued session ID | Interactive, or one exact declared dashboard gesture while Visible |
| `system.apps.windows.read.v1` | `HostServices.TaskSwitcher.GetWindowsAsync` | Visible or Interactive |
| `system.apps.windows.capture.v1` | `HostServices.Capture` | Interactive confirmation; owned status/read/discard can continue in background |
| `media.capture-library.read.v1` | `HostServices.CaptureLibrary.ListAsync` / `OpenAsync` and owned media reads | Shared explicitly saved captures; background reads allowed |
| `media.capture-library.save.v1` | `HostServices.CaptureLibrary.SaveAsync` / `EditCopyAsync` | Interactive; save owned temporary media or create an edited copy with separate source-read consent |
| `media.capture-library.manage.v1` | `DeleteAsync` / `ExportAsync` | Interactive; fixed Pictures export destination |
| `presentation.documents.v1` | `HostServices.Documents.CreateAsync` / `DiscardAsync`, `UI.ProviderContent` | Owned restricted HTML; scripts/forms/network blocked; background publication allowed |
| `system.apps.windows.preview.v1` | Host-rendered `UI.WindowPreview` | A current window and preview permission |
| `system.apps.windows.switch.v1` | `HostServices.TaskSwitcher.SwitchAsync`, `BeginLaunchAsync`, `TryCompleteLaunchAsync` | Interactive |
| `system.apps.windows.close.v1` | `HostServices.TaskSwitcher.CloseAsync` | Interactive |
| `system.power.read.v1` | `HostServices.Power.GetAvailabilityAsync` | Visible or Interactive |
| `system.power.control.v1` | `ShutDownAsync`, `RestartAsync`, `SleepAsync` | Interactive |
| `system.displays.read.v1` | `HostServices.DisplayProfiles.GetAsync` and the `Changed` event | Visible or Interactive |
| `system.displays.control.v1` | Save, manage, apply, keep and revert display profiles | Interactive; restores have an independent confirmation timeout |

## Important behavior

Window and launch IDs have a current observation lifetime. The provider checks
them again before acting. Window preview is view-only, and switching/closing
require separate permission. Confirm destructive power actions in the UI.
Windows may still refuse a switch or a preview after capability approval.

A widget that launches through an external provider can call `BeginLaunchAsync`
inside its interactive Play action before dispatching the launch. When that
provider confirms the process and its window appears, call `TryCompleteLaunchAsync`
with the ticket, PID, UTC process creation time in Windows file-time units, and
window handle. A false result means the window is not eligible yet. Stop on
cancellation, loss of the originating action, or expiry. The broker admits one
handoff per ticket for up to 30 seconds and checks the exact window independently.
The ordinary task-switch activation deadline applies after the target is ready.
Successful admission queues activation and Windows still decides foreground ownership.
