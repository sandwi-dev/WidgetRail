# Requesting widget permissions

`HostServices.Permissions` is an intrinsic host service available to sandboxed and
full-trust workers. Do not declare `widget.permissions.v1` in a manifest. Declare
the ordinary capabilities your widget needs in `permissions` or
`optionalPermissions`; this service can request only those declarations for its
own authenticated worker. It cannot approve full trust, install drivers, or replace
Windows permission/UAC dialogs.

## Check without prompting

```csharp
var access = await HostServices.Permissions.CheckAsync(
    [WidgetCaptureLibraryCapabilities.ReadPermission], token);
// NotDecided, Granted, Denied, or Unavailable for each requested ID.
```

Checks work in active lifecycle states, including Background. They never open a
dialog or change consent. Unknown, undeclared and intrinsic capability IDs report
Unavailable. Do not automatically request permission from activation or polling.

## Request from an explicit action

Use a widget operation so the action callback returns while the user reads the
host dialog. Request between one and eight distinct capability IDs:

```csharp
Operations.RunLatest("open-library", async context =>
{
    var permission = await HostServices.Permissions.RequestAsync(
        [WidgetCaptureLibraryCapabilities.ReadPermission], context.CancellationToken);
    if (permission != WidgetPermissionOutcome.Granted)
    {
        // Present Denied, Cancelled, or Unavailable; preserve the user's work.
        return;
    }
    var page = await HostServices.CaptureLibrary.ListAsync(
        cancellationToken: context.CancellationToken);
    // Publish the page through the widget's model.
});
```

Already-granted access returns without a prompt. Missing access opens a host-owned
controller-accessible dialog showing the host's widget name and permission labels.
The initial focus is Cancel; Allow or Deny changes only the displayed missing
permissions. A cancelled dialog changes nothing. Settings remains available through
[navigation intents](widget-intents.md#settings-destinations).

Requests require Interactive lifecycle and a foreground host presentation. Losing
interaction, retirement, cancellation or expiry closes/invalidates pending consent.
The current frontend presents permission dialogs from the main widget surface;
pinned-dialog placement is still under validation. Requests that cannot be shown
do not create a background queue. An SDK cancellation token throws cancellation;
user dismissal returns Cancelled.

The broker returns a bounded ticket immediately; the SDK polls its status and
cancels abandoned tickets. There is at most one pending dialog globally, up to
sixteen retained results, a two-minute pending deadline and one-minute result
retention. The normal RPC timeout does not become a user-interaction timeout.
Every ticket belongs to its exact broker object, even across workers with identical
package identity. The frontend rechecks catalog identity/declarations before
completion. Grants are persisted and reconciled with the live broker before the
SDK reports Granted; the actual subsequent operation still checks current consent.

Capture Library uses this flow for capture/save/manage actions and explicit access
recovery. Game Help requests capture or saved-library access from its context
actions. Neither widget requests access from ordinary background reads. SDK Gallery
offers separate Check access and Request access buttons in its Capture demo.
