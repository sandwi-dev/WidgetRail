# Playnite setup decision matrix

Installation means the executable folder plus its data profile. These decisions
apply equally to installed and portable copies. Discovery and inspection are
read-only; installation, enabling, repair, launch, restart, and credential changes
require the user to choose the displayed action.

## Choose a profile

| State | Decision | User experience |
| --- | --- | --- |
| One identifiable running profile; other stopped copies exist | Prefer the running profile | Show its folder before any setup action |
| More than one running profile, or unreadable running arguments | Do not guess or launch another copy | Explain the conflict; open connection settings |
| No running profile; one discovered profile | Inspect that profile | Offer its appropriate connection action |
| No running profile; multiple discovered profiles | Require selection | Explicit selected “Choose a profile” placeholder; primary action unavailable |
| Nothing discoverable | Do not scan all drives | Explain how to run Playnite or enter its folder |
| Remembered folder is missing | Do not recreate it or silently use another library | Explain the missing folder and offer connection settings |
| Manual folder differs from the running profile | Refuse the mismatch | Ask the user to close the other profile or select its real folder |

## Inspect the chosen profile

| Bridge / connection state | Automatic prompt | Explicit action |
| --- | --- | --- |
| Running, API matches profile, saved token matches | None; show library | Manual setup remains available |
| Running and healthy, but saved token missing/different | Connect dialog | Save that profile's token after verification |
| Running, Bridge absent | Installation dialog | Install bundled Bridge and restart the selected profile |
| Stopped, Bridge absent | Installation dialog | Install and start the selected profile |
| Stopped, Bridge present | Launch/repair dialog | Start and verify, or repair a recognized old build |
| Bridge disabled | Enable dialog | Preserve files/preferences, enable, restart if running |
| Running Bridge offline | Restart/reconnect dialog | Preserve installed files |
| Wrong profile or authentication rejected | Connection guidance | No file or credential overwrite |
| Recognized original build requires repair | Repair dialog | Verified bundled replacement with rollback |
| Unknown/newer incompatible build | Manual guidance | Never overwrite it automatically |
| Interrupted setup | Recovery dialog | Recover the recorded transaction before retry |
| Inspection fails | Visible error and connection-settings action | Never silently treat failure as a healthy connection |

Automatic inspection checks local API identity/availability but does not repeatedly
compile eval scripts. Explicit setup performs the complete compatibility check before
accepting the connection. An existing connection's credentials are retained until a
new connection has been verified.

## Interaction and lifecycle

| Event | Required behavior |
| --- | --- |
| Widget becomes visible, or enters Interactive from tray preview | Recheck; one latest discovery result owns the UI |
| Setup is already running | Do not launch a second operation or replace its plan |
| B dismisses dialog | Keep a reachable recovery action; do not immediately reopen on rendering |
| Start/install is busy | Keep the primary control focused and enabled with busy feedback |
| Overlay hides or loses foreground during setup | Continue the widget-lifetime operation; preserve its result |
| Setup fails after dismissal | Do not navigate to another page unexpectedly |
| Setup succeeds | Close dialog, reset connection/artwork cache, refresh the active library page |
| Saved-only rows are shown while loading | Do not focus a collection containing only disabled items |
| No current library is available | Show recovery controls, not an apparently usable cached library |

## Launch limitations

Use `--startclosedtotray --hidesplashscreen --userdatadir <profile>`.
Do not use `--nolibupdate`. Do not use `--startdesktop`: Playnite 10.60
processes it as a mode-switch command and restores the main window.

The actual 10.60 launch probe showed a roughly 530 ms visible main window and
brief foreground takeover before settling in the tray. Do not promise invisible
startup or modify Playnite's startup preferences. Plugin/first-run prompts can
require user interaction. Never force-kill Playnite to complete setup.

## Validation

Backend fixtures cover profile discovery, duplicate installations, disabled and
missing Bridge, token/health decisions, unknown builds, stale plans, shutdown refusal,
and rollback/recovery. Presentation/lifecycle fixtures must cover every automatic
dialog action and the zero/one/multiple-profile selector states, including busy and
error updates. A read-only live portable check verifies discovery and the offered
plan without installing or restarting that copy.
