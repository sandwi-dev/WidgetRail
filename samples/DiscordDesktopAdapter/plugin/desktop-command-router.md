# Initial desktop command router

`desktop-command-router.mjs` is a pure renderer-domain foundation. It is not
imported by the installed probe, connected to the pipe, or a complete Discord
command implementation. Its only methods are `state.read`, `voice.setMuted` and
`voice.setDeafened`; native sharing and all other features remain separate work.
It reuses the source-qualified synchronous `applyVoiceIntent` translator rather
than inventing another provider mute/deafen path. `share-preparation.mjs` is not
used: it prepares a different, explicitly user-selected media workflow and does
not authorize sharing through this router.

The authenticated main/renderer bridge will construct one router per connection,
using the authenticated connection ID and a trusted provider owner. No wire
value can register a handler, evaluate code or supply native objects. The bridge
must validate its sender/renderer before calling this module; HMAC and renderer
IPC admission are not replaced by these domain checks.

```js
// Created once by the provider-lifetime owner, NOT on each pipe connection.
const providerGate = new DesktopProviderGate();
const router = createDesktopCommandRouter({
    connectionId, providerGate, getOwner, readState, getVoiceBindings
});
const response = await router.dispatch(request);
// On disconnection/renderer retirement:
router.retire();
```

Request schema (additional fields and accessors are rejected):

```text
{ type: "request", version: 1, requestId: positive safe integer,
  connectionId: 32 lowercase hex,
  scope: { rendererEpoch: 32 lowercase hex, accountEpoch: 32 lowercase hex,
           callEpoch: null or 32 lowercase hex },
  method: "state.read" | "voice.setMuted" | "voice.setDeafened",
  args: {} | { value: boolean } }
```

`args` must be empty for reads and contain exactly `value` for either mutation.
Request IDs increase across **all** methods within one authenticated connection;
the connection owner generates them centrally. This is a constant-memory high
watermark, not an evicting replay cache or a widget-author counter. A valid request
consumes its ID even when busy/stale/rejected. Reusing it never retries the action;
a deliberate new request needs a new ID. New connections have different IDs and
independent sequences. Domain scope epochs do not reset the connection watermark.

`getOwner()` returns the same plain immutable scope object for one provider /
renderer / account / call incarnation. Replace the object on every transition,
including same-public-ID restarts. Renderer/account epochs also identify signed-out
states; callEpoch is null when outside a call. Reference identity plus epoch fields
are rechecked before mutation and after every asynchronous read. `scope()` copies
only these three public epoch fields for the future authenticated scope event.

Read projection is intentionally minimal and strict:

```text
{ revision: nonnegative safe integer, signedIn: boolean,
  voiceConnection: "disconnected" | "connecting" | "connected" |
                   "reconnecting" | "unavailable",
  voice: null | { preference: {muted: boolean, deafened: boolean},
                  effective: {muted: boolean|null, deafened: boolean|null} } }
```

The provider supplies an atomic copied observation/revision, not a live store
dump. This projection contains no account/channel/participant names or IDs and
is not full widget state parity. Older revisions are not republished for the same
owner. A mutation admitted while a read is pending invalidates that read result,
preventing it from overwriting the command acknowledgment. External state/event
reconciliation remains the provider and future publication layer's responsibility.

Responses have type `response`, version1, connection/request IDs, captured scope,
status, reason, mutationAttempted, state and voice. `read` carries validated state;
`applied` carries confirmed raw/effective voice preferences. Already-satisfied
intent is `applied` with mutationAttempted=false. `rejected` performs no mutation.
`uncertain` means an action may have applied: mutationAttempted is true when known,
or null when retirement/exception prevents knowing. Raw provider exceptions and
unvalidated state never cross the boundary.

At most one read and one synchronous mutation are admitted across routers sharing
the provider gate; there is no queue. Provider discovery may reenter the router,
so admission is reserved before discovery. An uncertain mutation quarantines the
**provider-lifetime gate**, not just the connection. A replacement router using
that same gate still rejects mutations, including after account/call changes.
Reads do not clear quarantine: a read alone cannot prove an unexpected async
setter will not finish later. The gate has no reset method. The lifetime owner
must preserve it across reconnects and same-renderer adapter replacement. A new
gate requires proof that the previous provider can no longer complete work
(for example, its renderer process ended), or separately qualified settlement
and provider requalification. Merely creating a new router/connection is not
reconciliation and must not create a new gate in production wiring.

`retire()` immediately settles pending callers, aborts read dependencies and
prevents any later admission. A dependency ignoring cancellation cannot publish
its late result. Its provider read slot remains held until actual settlement, so
new connections cannot accumulate more abort-ignoring reads either. No automatic
retry, permission request, join, capture or sharing action is implemented here.
