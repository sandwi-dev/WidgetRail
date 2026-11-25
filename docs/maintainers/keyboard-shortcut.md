# Overlay keyboard shortcut ownership

Settings → Controllers includes the global overlay shortcut. Each of the four
tray directions also has an optional keyboard shortcut in its assignment menu.
The dedicated replay shortcut can be configured independently. The host-owned
dialog records keys and exposes enable, reset,
Save and Cancel. Settings declares the entry action and reloads the committed
document afterward; it never registers Windows keys or saves an untested chord.

`WidgetPresentationSession.ValidateKeyboardShortcutCommand` checks the displayed
Settings identity, source, scope and action before showing the recorder. The shell
uses the existing host-dialog lane for native controller navigation, focus return,
foreground/hide cancellation and theme handling. Unrelated widget shortcuts do
not acquire keyboard authority.

`MainWindow.KeyboardShortcut.cs` reserves a candidate `RegisterHotKey` registration
before updating the atomic platform-settings document. Conflicts and storage
failures retain the previous registration. The save compares the previously read
keyboard preference while preserving concurrent changes to other settings. A
successful disk commit is adopted even if dialog dismissal races completion; the
host must not leave the old binding active after saving the new one. The editor
does not automatically replay failed saves.

Each registration receives a distinct ID. Disabled or retired registrations cannot
handle queued messages, and message timestamps reject messages from a prior input
admission. `MOD_NOREPEAT` prevents holding a key from toggling repeatedly. While
the editor is open, the current registration remains reserved but cannot toggle
the overlay; its Windows hotkey message can instead supply that chord to the
active recorder. No low-level keyboard hook is used.

`controllers.overlayShortcut` stores Enabled, Key (Windows virtual-key integer)
and Modifiers. Its absence preserves the legacy `f1ShortcutEnabled` setting;
an explicit disabled record wins over legacy true. Invalid key/modifier values
use the existing strict settings failure path. Fresh profiles default to disabled
F1. The optional `controllers.trayShortcuts` records use the same format.
Each defaults to disabled. Windows-reserved, conflicting or externally
registered combinations are rejected at Save.

Directional shortcuts resolve the current tray assignment on each press. An
eligible background action follows the tray's normal permission and availability
checks. Widgets supply the current command, target and provider-specific reason
when unavailable. The host handles permission review, remembered confirmations,
source selection and foreground ownership. A hidden invocation that reaches
one of those UI decisions displays a notification explaining the next step.

| Tray consumer | Behavior when the overlay is closed |
| --- | --- |
| Capture Library screenshot | With permission and remembered screenshot confirmation, save directly. Zero delay pins the foreground window at the press. A configured delay captures the foreground window at the end of the countdown. |
| Capture Library one-off clip | Uses the independent recording confirmation, preparation settings, encoding and stop behavior of the normal recorder. |
| Capture Library replay | Save and Stop apply to the current session. Start (including the optional Save fallback) can use the approved foreground target. A new source requires the application chooser. Automatic replay remains independent. |
| Media Sessions, Spotify, YouTube and YouTube Music | Existing transport offers target the current player, session or device. Their availability and stale-target checks also apply to global shortcuts. |
| Discord | Mute and deafen use the current account and call. A disconnected or changed call yields its own reason. |
| Other widgets and templates | No tray action declaration means there is no assignable action. |

## Consumer matrix and checks

| Consumer | Treatment |
| --- | --- |
| Settings Controllers | Replaced the F1 toggle with the validated host editor action; committed state reload only. |
| WinUI MainWindow and native host dialog | Own registration, recording, transactional save, conflict feedback and teardown. |
| Controller preference exchange | Accepts the optional validated keyboard record while retaining old replies. |
| Introduction | Describes the configured chord instead of assuming F1. |
| Other widgets, templates and controller shortcuts | No keyboard registration or migration; their scoped controller actions are unchanged. |

Managed checks cover legacy/default/explicit-disabled settings, strict corrupt
input handling, persistence, Settings ownership and host action authority. The
existing `--shell-no-controller --validation-platform-activation --validate-f1 <result-file>` native fixture now
also checks a modified chord, current-binding recording suppression, conflicts,
storage failure, cancellation, same-binding Save, hidden reopen and disable.
Its synthetic profile is isolated. All 22 native checks pass in
`artifacts/input-discord/hotkey-native-2/result.json`. The activation switch is
required because the fixture checks real platform foreground ownership; omitting
it timed out before any shortcut assertion. A native UI smoke check also opened
the editor through Settings, recorded Ctrl+Alt+F11 using Windows input, enabled
and saved it, observed the updated settings row and exercised the new toggle.
All writes were confined to the isolated test profile.
