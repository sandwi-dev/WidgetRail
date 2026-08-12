# Private widget state

Status: implemented public SDK, authenticated broker, Windows persistence, and
production Bridge wiring, 2026-08-07.

`HostServices.PrivateState` is the supported way for a widget to retain small,
readable preferences and UI state across worker restarts. Typical values are a
selected tab, stable library IDs, sort mode, filter choices, or the last
controller focus key. Authentication material belongs in
`HostServices.PrivateSecrets`, never in readable state.

## Full-application local data

`HostServices.PrivateState` remains intentionally small: it is the right home
for bounded preferences, selection, and controller presentation state. A full
application that needs databases, indexes, or caches may use ordinary files in
its package-specific AppContainer local-data profile. That storage does not
grant a host path, desktop-user filesystem authority, or a way to pass paths
over widget IPC. Package, capability, snapshot, frame, string, and private-state
bounds remain unchanged.

For an unsigned Community package, the AppContainer profile identity includes
the verified content digest. Changed package bytes therefore receive a new
local-data namespace; exact reviewed bytes recover only their own namespace.
Uninstall/profile cleanup, disk quotas, backup, migration between content
generations, and a user-facing clear-local-data workflow are not implemented
contracts. Applications must tolerate absent local data and keep their own file
formats bounded and recoverable.

## No manifest permission

Private state is a host-provided service, not a permission. Do **not** put
`storage.private-state.v1` in `permissions` or `optionalPermissions`; either a
trusted catalog or installed manifest that tries to declare it is rejected.
There is no Settings consent toggle.

The Bridge creates an authenticated broker companion and supplies this one
closed authority through a separate host-only grant set. Worker IPC has no
message that can add a grant. A worker may call the typed service—or reproduce
its known operation ID—but the broker accepts it only on a channel to which the
Bridge already attached the private-state service. Hand-crafted requests cannot
obtain audio, network, launch, secret, or another undeclared authority.

## SDK

```csharp
file sealed record Preferences(string SelectedTab, string? LastItemId);

private Preferences _preferences = new("library", null);
private long _stateRevision;
private bool _restored;

protected override async ValueTask OnActivatedAsync(
    CancellationToken activeLifetime)
{
    if (!_restored)
    {
        var saved = await HostServices.PrivateState.ReadAsync<Preferences>(
            cancellationToken: activeLifetime);
        if (saved.Exists && saved.Value is not null)
            _preferences = saved.Value;
        _stateRevision = saved.Revision;
        _restored = true;
        Invalidate();
    }
}

private async ValueTask SaveAsync(CancellationToken cancellationToken)
{
    var result = await HostServices.PrivateState.WriteAsync(
        _preferences,
        expectedRevision: _stateRevision,
        cancellationToken: cancellationToken);
    _stateRevision = result.Revision;
}
```

Restore lazily in the first `OnActivatedAsync` callback, not
`OnCreatedAsync`. Creation should remain lightweight and must not make an idle,
never-presented worker perform disk work. Keep the restored value in memory for
the lifetime of that worker. Persist meaningful changes when they occur; do not
save on every render, animation tick, slider sample, or `OnDestroyingAsync`.

Available methods are:

- `ReadJsonAsync(CancellationToken)` returns `Exists`, canonical `Json`, and
  `Revision`;
- `ReadAsync<T>(JsonSerializerOptions?, CancellationToken)` deserializes the
  same bounded document;
- `WriteJsonAsync(json, expectedRevision?, CancellationToken)`;
- `WriteAsync<T>(value, expectedRevision?, JsonSerializerOptions?,
  CancellationToken)`; and
- `ClearAsync(expectedRevision?, CancellationToken)`.

An absent new authority reads as `Exists = false`, `Json = null`, revision `0`.
Every successful write or clear increments the durable revision, including a
clear of an already absent document. Clear retains a tombstone so revisions do
not move backward after restart.

Pass the last observed revision as `expectedRevision` for compare-and-exchange.
If another worker or host process won first, the call fails with
`state_conflict`; read again, reconcile intentionally, and retry only after a
real user/state change. Omitting the expected revision requests an unconditional
last-writer-wins update.

## Lifecycle and residency

Read, write, and clear are allowed in the three active stable lifecycle states:
`Background`, `Visible`, and `Interactive`. They are denied in `Destroying`.
Background availability lets an author persist the result of already-approved
process-lifetime work, but it is not permission to poll, animate, or keep a
suspended widget busy. Restore presentation state only on first activation.

`unload-after-idle` reconstructs a new widget object and broker channel. State
survives that reconstruction. `suspend-when-hidden` still suppresses hidden
rendering, input, invalidation, and ordinary declared capabilities; the bounded
private-state service is the explicit persistence exception. Treat cancellation
while waiting for storage or a cross-process lock as normal teardown.

## Identity, updates, and retention

The host hashes authenticated `PublisherId + NUL + PackageId` with SHA-256 to
derive a path-safe opaque filename. Instance/version is excluded, so a trusted
first-party update with the same stable publisher/package retains state. A
different publisher or package cannot read it.

For unsigned community packages, the authenticated publisher authority includes
the host-verified immutable content digest. Modified bytes therefore receive a
different state namespace even if the manifest repeats publisher, package, and
version text. Exact verified bytes recover their previous namespace. A future
signed publisher authority can provide intentional update continuity.

Uninstall currently retains private state and tombstones. Reinstalling the same
authenticated authority can recover them. A user-facing per-widget clear-local-
data action is not implemented yet; uninstall must not be documented as data
deletion.

## Bounds and storage guarantees

| Contract | Bound |
| --- | --- |
| Canonical document | 64 KiB UTF-8 |
| Raw SDK JSON input | 256 KiB UTF-8 before canonicalization |
| JSON | strict UTF-8; no comments, trailing commas, duplicate object keys, or depth over 16 |
| Mutation burst | 8 writes/clears per authority |
| Sustained mutation rate | 1 token per second, up to the burst capacity |
| Documents | exactly one current document/tombstone per publisher/package authority |

Canonical output is whitespace-free and object properties are ordered with
ordinal comparison. JSON number spelling is preserved. The transport uses
base64 only inside the private broker DTO so arbitrary JSON string content
cannot escape the outer envelope; decoded canonical bytes are revalidated at
the SDK, broker, and provider. Filesystem paths never cross IPC.

The Windows store serializes each authority with an in-process gate plus an
adjacent exclusive lock file, so CAS and rate limits also hold across Bridge,
Settings, tests, or future host processes. Revision, tombstone, document, and
token-bucket state are one strict canonical envelope. A mutation writes a unique
temporary file in the same directory, flushes it, then atomically replaces the
old envelope. Cancellation before that commit leaves the previous revision
authoritative; after the atomic commit, success is the linearized result.

Existing reparse points in the root, authority file, or lock path are rejected.
Unknown fields, duplicate keys, noncanonical envelopes, invalid base64, invalid
documents, inconsistent tombstones, out-of-range rate metadata, and truncated
or oversized files fail closed as `state_corrupt` or `unsafe_state_store`; the
provider does not silently reset or overwrite them.

## Errors

| `WidgetCapabilityException.ErrorCode` | Author response |
| --- | --- |
| `state_conflict` | Read, reconcile, and retry from the new revision. |
| `state_rate_limited` | Coalesce changes; do not timer-retry each rejected mutation. |
| `state_corrupt` | Keep safe defaults and offer diagnostics/recovery; do not overwrite automatically. |
| `unsafe_state_store`, `state_store_unavailable`, `platform_unavailable` | Keep in-memory defaults and show a bounded storage-unavailable state if persistence is essential. |
| `lifecycle_denied` | Do not save during terminal cleanup; move work to an active state. |
| `invalid_payload`, `malformed_response` | Fix the widget/provider contract; do not expose raw JSON in UI or logs. |

Secrets remain a separate permission-backed, write-only facility. Never put a
bearer token, password, refresh token, cookie, private key, or recovery code in
`PrivateState` merely because the document is package-scoped.

## Tests

Use the public deterministic fixture in SDK unit tests:

```csharp
var state = new WidgetTestPrivateState(
    initialJson: "{\"selectedTab\":\"library\"}", initialRevision: 1);
var services = new WidgetTestHostServicesBuilder()
    .WithPrivateState(state)
    .Build();
```

The fixture supports external write/clear simulation and expected-revision
conflicts without touching the real profile. Product/provider coverage must
also prove authority isolation, instance-independent restart persistence, CAS
conflict, clear tombstones, canonical 64 KiB quota, corruption/reparse failure,
burst/refill behavior, cancellation, and two real host processes racing
expected revision `0`.
