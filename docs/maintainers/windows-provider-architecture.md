# Windows providers

A provider adapts a Windows API into a small typed service. Widgets call the
public service; the broker decides whether a request is allowed; the provider
performs the native operation.

## Keep responsibilities separate

| Layer | Owns |
|---|---|
| Widget | UI state, actions, useful errors, and declared permissions |
| SDK service | Typed requests and responses |
| Broker | Identity, consent, lifecycle, and operation admission |
| Provider | Native API calls, observation, validation, and cleanup |

A provider should not choose the widget's theme or route. A widget should not
construct private broker envelopes or handle native pointers.

## Use display-ready records

Return the information the UI needs: names, state, supported actions, and opaque
identifiers. Keep process IDs, window handles, native device paths, credentials,
and provider-specific command lines on the trusted side unless a public contract
explicitly says otherwise.

An opaque ID identifies an observed object in its defined lifetime. Revalidate
it immediately before a side effect. A device can disconnect or a window can
close between enumeration and activation.

## Read and write permissions

Reading a list does not authorize changing it. Use separate capabilities for
operations such as switching windows, setting a device, or controlling power.
Most provider writes require the widget to be Interactive.

Some contracts allow one exact dashboard action while Visible. That is a narrow
user gesture, not general permission for background writes. See
[Capabilities](../reference/capabilities.md).

## Observe without busy polling

Prefer Windows events and coalesced subscriptions where the API supports them.
Start observation when there is demand and stop or release it according to the
provider lifecycle. A scan or expensive refresh should remain explicit when its
contract requires an interactive action.

Do not let a native callback synchronously block the overlay's paint thread.
Bound queues and cancel obsolete work. Deliver failure states without taking
down unrelated capabilities.

## Cleanup and cancellation

Release native handles and subscriptions on disposal. If an operation creates
temporary Windows state, record enough ownership to remove only what that
operation created. Never infer ownership solely from a friendly name.

Credential prompts belong on the trusted side. Keep secrets out of widget
snapshots, command lines, and diagnostic messages.

## Tests and source map

Test contracts with fake providers and transport-free host services. Then use
focused native tests for the Windows boundary. Include stale IDs, revocation,
disconnection, cancellation, partial failure, and repeated disposal.

Start with [`PlatformBroker`](../../src/PlatformBroker/) and the relevant
[`WindowsAudioProvider`](../../src/WindowsAudioProvider/),
[`WindowsNetworkProvider`](../../src/WindowsNetworkProvider/),
[`WindowsBluetoothProvider`](../../src/WindowsBluetoothProvider/),
[`WindowsAppLibraryProvider`](../../src/WindowsAppLibraryProvider/), or
[`WindowsPowerProvider`](../../src/WindowsPowerProvider/).

Native media and preview presentation also involves the host. Follow the
[architecture map](platform-architecture.md) rather than moving UI ownership into a provider.
