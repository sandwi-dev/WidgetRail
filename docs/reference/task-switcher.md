# Task Switcher

Task Switcher is a host-owned utility beside Notifications. It is available in
both Bare and Production without installing a widget or approving widget
permissions. Recent Apps and the separate Task Switcher widget are retired.

## Controls

Focus the Task Switcher icon to show its compact window list. **Up** enters the
list; **A** switches to the selected window, and **X** requests a normal close.
The first entry, **Windows Task View**, instead opens Windows' own app switcher
using Win+Tab after releasing the selecting gesture and hiding the overlay. It
has no close action. The existing window list remains available underneath.
**B** returns from the list to its icon, keeping the panel visible. B on the icon
uses the same radial-chooser Back behavior as the notification bell. Left/Right
moves between utility icons and the rail.

In the rail or radial chooser, **hold Y for 300 ms** to enter Notifications when
there are pending notifications, or Task Switcher when there are none. Focus goes
directly to the first list item. The guide names the current destination and
updates as notifications arrive or are dismissed, without switching an open panel.
Tapping Y still
enters reorder mode. This is a shell shortcut, not a global override of widget
or keyboard actions. The tray menu also offers **Task Switcher** beside
**Notifications**. Notifications follows the same focus/Up/Back flow.

The list shows up to 64 windows with application name, window title and minimized
state. Separate windows of one app stay separate. There are no live captures or
thumbnail permissions. Two-second refreshes run only while its panel is open;
existing rows retain their order and identity so refresh does not move focus.
Closing a window does not kill its process or suppress save prompts.

## Host-owned list and broker activation

`WindowsTaskWindowCatalog` reuses the existing running-window eligibility,
packaged-app resolution and close policy without initializing the game/app
library. Listing, icons and close requests stay in the host. Only the final
foreground activation uses the existing trusted WidgetRail broker.

Switching uses the existing `TaskWindowActivation` admission and `MainWindow`
handoff sequence. It verifies foreground ownership and the exact window,
process lifetime and class; waits for controller release; completes closing
motion; attempts to refresh foreground permission for its exact owned broker process; rechecks
authority and identity; releases controller capture; and hides the overlay.
The broker revalidates the target and asks Windows to restore and activate it.
A failed permission refresh does not cancel the handoff: the broker can retain
activation permission after the host loses the ability to grant it again, such
as after opening a folder. The broker's activation result and the observed
foreground window determine success, matching the original widget path.
The host observes foreground for up
to 250 ms, accepting that target or its owned dialog, rather than treating the
API return or an unrelated window in the same process as proof. A canceled
handoff cannot later hide a reopened overlay.

A denial before commitment restores the still-owned overlay and its input. After commitment,
the host does not steal focus back if Windows denies activation; the failure is
reported when Task Switcher is next opened. Windows can still reject activation
or cross-integrity close requests. No simulated-input workaround, retry loop or
force-close path is used.

## Upgrade and SDK compatibility

Startup retires the old `widgetrail.firstparty.task-switcher` and
`widgetrail.firstparty.recent-apps` packages through the normal disable/uninstall
transaction. Live launch leases remain protected; deferred cleanup retries at
startup. The old packages are no longer listed or seeded. Other widgets and their
data/permissions are untouched.

The SDK's `WidgetHostServices.TaskSwitcher` and its read/switch/close capabilities
remain supported for third-party widgets. Those calls still require the normal
widget permissions and lifecycle authority. Raw HWNDs and process-lifetime
identity are host-private. The separate [window preview API](window-previews.md)
also remains available for other features.

## Verification

Focused coverage includes shell hold gestures, native utility focus and Back
navigation, stable window identities, package retirement, and the existing
foreground/held-input handoff policies. Native fixture results and exact tested
scope belong with the candidate evidence; passing unit tests alone do not prove
controller ownership or every Windows application will accept activation.

The persistent-host fixture's `--task-cycle-folder` option opens an empty fixture
folder through the real external-launch handoff before alternating two exact
window targets. It uses no injected pointer/keyboard input between transfers;
the trace records permission refresh failures separately from broker activation.

`scripts/Test-WinUiFolderTaskSwitching.ps1` adds input and explicitly replaces the
broker's previous grant after each switch, requiring six new successful grants
and transfers. This catches failures hidden by startup or retained permission.
Capture-folder opening dispatches a literal path to the system Explorer executable;
it does not call `LaunchFolderAsync` in the frontend, which left the host unable
to refresh foreground permission in this regression.
