# What keeps running, and what is saved?

Hiding WidgetRail, stopping a widget and deleting it have different effects.
The widget and website also have their own playback/session policies.

| Action | Running content | Saved data |
| --- | --- | --- |
| Hide the overlay | Pins stay visible. Widgets with background support may keep playback, calls or other work running. Other widgets can suspend or unload. | Settings and persistent sign-ins remain. An uncommitted widget-field draft is cancelled. |
| Leave a widget | Another widget receives input. The previous widget follows its background policy; a visible pin can retain its content. | Saved settings remain. Transient page/scroll state is retained only while supported by the widget/session. |
| Stop widget | Ends that widget's worker, playback, hosted pages, pop-outs and pin. Other widgets are unaffected. | Settings, permissions, persistent sign-ins and pin placement preferences remain. Stop is remembered across restarts until Start/Reload. |
| Disable widget | Removes it from the chooser and stops its running content. | Keeps saved data and the remembered stopped state. Re-enable in Settings. |
| Unpin | Removes the pinned surface. A live browser page returns to its widget; other widgets control their playback behavior. | Keeps saved data and placement preferences. Unpin is not a general Stop command. |
| Restart widget | Ends its current session and starts it again. Transient state may be lost. | Keeps saved data and persistent sign-ins. |
| Quit WidgetRail | Ends its widgets, live pages, playback and pending notification session. Playnite keeps running independently. | Saved settings/sign-ins remain. Pins and live calls are not restored automatically. |
| Clear local data | Resets and restarts the selected widget. | Deletes host-owned state, configuration and browser data; separately stored integration credentials and downloaded/external files remain. |
| Remove a webapp / uninstall a widget | Ends its sessions and removes it. | Deletes its host-owned saved state and browser profiles as part of removal. Does not delete remote accounts or arbitrary add-on-owned credentials/files. |

Use **Menu** on the tray icon for Stop, Start/Restart and pin controls.
Use **Settings → Widgets** for enablement, removal and Clear local data.
Settings remains available for managing the app. See [widget management](customization.md#manage-widgets).

Persistent browser profiles retain cookies and preferences, not a recording of a
running page. Signing in again after a service expires its session can still be
necessary. Page transfer to a pin preserves the existing live page; restarting
the app does not preserve an in-progress call or unsaved website form.

Updates preserve enabled/stopped choices and ordinary saved data. Package identity
and permissions are checked again; changed packages can need renewed approval or
credentials. An app update is different from a widget update: see
[app updates](../reference/app-updates.md) and [Catalogue](../reference/widget-marketplace.md).

Notifications pending in the panel last only for the current app session; delivery
preferences survive restart. Stopping a source prevents new events, while a stale
entry may still offer to open its widget normally.

Before uninstalling a service integration, use its **Disconnect**, **Remove saved
token** or **Remove saved key** action if you also want its separately stored
credentials removed. Follow the [integration guides](integrations.md) for the
specific storage boundary. Whole-app uninstall has a separate keep/delete-data
choice described in [Getting started](getting-started.md#uninstall).
