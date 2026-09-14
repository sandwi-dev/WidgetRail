# Application capabilities

Declare the capability in the manifest, obtain user approval, then use its typed
service. See [Capabilities](capabilities.md) for consent, lifecycle, and error behavior.

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
| `system.apps.windows.preview.v1` | Host-rendered `UI.WindowPreview` | A current window and preview permission |
| `system.apps.windows.switch.v1` | `HostServices.TaskSwitcher.SwitchAsync` | Interactive |
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
