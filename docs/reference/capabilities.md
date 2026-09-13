# Host capabilities

A capability grants a sandboxed widget access to one kind of host service.
Reading audio state does not automatically grant permission to change it.

## Declare and review access

Put required capabilities in manifest `permissions` and optional features in
`optionalPermissions`. Required does not mean automatically granted: the user
still reviews access in Settings.

After approval, use typed `HostServices` methods. Do not construct private broker
messages, choose pipe identities, or call native APIs as a workaround for denial.

| Service | Reference |
|---|---|
| Audio devices, volume, microphone, spatial sound | [Audio capabilities](audio-capabilities.md) |
| Network and Bluetooth | [Network capabilities](network-capabilities.md) |
| Apps, windows, media sessions, and power | [Application capabilities](application-capabilities.md) |
| Exact-port localhost JSON and write-only secrets | [Companion services](community-companion-services.md) |
| Host-granted saved widget state | [Private state](private-widget-state.md) |

## Lifecycle and actions

Reads commonly require Visible or Interactive state. Writes generally require
Interactive state. Some contracts permit one exact declared dashboard gesture
while Visible; this does not permit arbitrary background writes.

Check the service's table for exceptions. A worker remaining resident does not
extend permission beyond the current lifecycle.

## Denial and changing devices

Show a useful unavailable state if a permission is refused or revoked. A provider
can also fail after consent: an audio device can disconnect, a window can close,
or Windows can reject an operation.

Use current opaque IDs from the service. Do not store a short-lived window or
device ID as permanent authority to operate on a later object.

## Tests and boundaries

Use `WidgetTestHostServicesBuilder` and fake providers for deterministic tests.
Cover permission denial, lifecycle changes, and stale IDs as well as success.

Full-access application widgets are a separate model. Their ordinary Windows
user access is not made safe by a sandbox capability declaration.
See [Security](../maintainers/security-and-trust.md).

The current typed service definitions live in
[`Capabilities.cs`](../../src/WidgetSdk/Capabilities.cs),
[`Power.cs`](../../src/WidgetSdk/Power.cs), and
[`TaskSwitcher.cs`](../../src/WidgetSdk/TaskSwitcher.cs).
