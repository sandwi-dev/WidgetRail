# Widget intents (in development)

The contract/manifest, handler-selection policy, SDK action declarations and
worker receiver transport are implemented. Bridge dispatch and frontend
activation are not connected yet.
Do not advertise intent handling in published packages until that integration
and its compatibility gate are complete. Track progress in
[the Game Help plan](../maintainers/game-help-implementation-plan.md).

## Package declarations

An optional `intents` manifest object contains `requests` and `handles` arrays.
Each entry carries `id`, positive integer `version`, and `payloadSchema`.
Omission preserves existing manifest serialization. Empty lists default to empty;
explicit null lists are invalid. There may be at most 16 declarations in total.
Full-trust and sandboxed packages can describe contracts: neither declaration
grants platform permissions or constitutes user approval for an action.

```json
"intents": {
  "requests": [{
    "id": "example.guide.explain",
    "version": 1,
    "payloadSchema": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "topic": { "type": "string", "minLength": 1, "maxLength": 120 }
      },
      "required": ["topic"]
    }
  }],
  "handles": []
}
```

IDs contain two or more dot-separated segments, each starting with an ASCII
lowercase letter and containing lowercase letters, digits or hyphens. Maximum
length is 128; versions range from 1 through 65535. `widgetrail.*` is reserved
for shipped standard contracts; other contracts need no host enum change.

Schemas are embedded in the package manifest and covered by package integrity.
Use the same contract definition for sender and receiver. Canonical SHA-256
identity ignores object-property order, `required`/`enum` order and numeric
spelling. It does not attempt general semantic equivalence: adding an explicitly
defaulted constraint can change identity. An incompatible schema needs a new
contract version. Duplicate declarations in one direction and conflicting
definitions across directions are rejected.

## Bounded JSON Schema subset

Only the following keywords are supported. Unknown or misplaced keywords fail
validation, rather than being silently ignored. No `$ref`, remote schemas,
regular expressions, union types, composition or executable validators.

| Type | Supported fields in addition to `type` |
| --- | --- |
| object | Required `properties`, required `additionalProperties: false`, optional `required` |
| array | Required `items` and `maxItems`, optional `minItems` |
| string | Required `maxLength`, optional `minLength` and unique string `enum` |
| integer / number | Required `minimum` and `maximum` |
| boolean | None |

Root must be an object. Limits: 16 KiB encoded schema, 128 schema nodes,
six nested levels below the root, 32 properties per object, 64 array items,
8192 Unicode scalar values per string, 64 enum alternatives, and 32 KiB encoded
payload. Numeric values must be exactly representable by .NET `decimal`; rounded
underflow/fractions are rejected. Payloads must satisfy required fields, type and
size bounds; duplicate and additional properties are rejected at every level.

Standard definitions live in `WidgetIntentContracts`:

- `widgetrail.web.open`, version 1: `url` (1–2048 characters). The routing policy
  additionally permits only absolute HTTP(S) URLs with a host, without credentials,
  control characters or surrounding whitespace. Supporting HTTP preserves existing
  Playnite links. It never accepts OS commands or arbitrary URI schemes.
- `widgetrail.video.open`, version 1: `provider` (1–64 characters), `videoId`
  (1–256), optional `startTimeSeconds` (0–604800). The receiving provider must
  additionally validate its own video identifier and supported playback range.

## Host resolution boundary

`IntentResolutionPolicy` is a pure policy, not a permission or dispatch API.
It receives already-admitted catalog entries and returns one of:

- Widget: one eligible handler, or an eligible explicit user preference.
- ChooseHandler: multiple eligible handlers; deterministic display order does
  not imply a default selection.
- ExternalBrowser: only the exact standard web contract, with no eligible handler.
- Unavailable, InvalidPayload, SchemaConflict, or InvalidCatalog.

Disabled handlers and different versions are not eligible. Enabled conflicting
schemas fail closed even if a preferred compatible handler exists. Catalogs are
bounded to 256 entries. Eligible entries retain the host's generation so live
delivery can reject replacements. A runtime handler failure is not a request to
resolve again and cannot automatically trigger another handler or fallback.

The remaining dispatcher must bind every request to authenticated caller identity,
manifest declaration, current user action and lifetime, then revalidate the target
generation before delivery. The frontend opens the destination as an ordinary
widget; there is no return-to-sender stack or B remapping. Users return through
the tray or pin the destination using existing behavior.
These requirements are not satisfied merely by calling the pure resolver.

## Declarative authoring and receiving

Interactive requests attach to the displayed control so the host can validate
the actual selected action and its payload. Protocol v65 is required; ordinary
controls without intents retain their existing minimum protocol. For example:

```csharp
UI.Button("Read guide", "read-guide", "guide")
    .OpenIntent(WidgetIntentContracts.Web,
        JsonSerializer.SerializeToElement(new { url = "https://example.com/guide" }));
```

`ActionSurfaceElement` provides the same `OpenIntent` method. The manifest must
declare the matching request contract. Payloads are validated and cloned when
authored. Intent payload changes participate in atomic presentation updates and
input-authority revalidation. Initial routing admits only primary activation
(pointer/keyboard or controller A press); repeat/release and unrelated shortcuts
do not gain intent authority. Indexed and pinned routing still require their
respective lease/projection admission before activation is enabled there.

Receivers override `Widget.OnIntentAsync(WidgetIntentRequest, CancellationToken)`
and return true after accepting data into widget state. Use normal state/operation
helpers for subsequent work. The default rejects unsupported requests. Receiver
transport admits visible/interactive lifetime only, has one pending delivery per
worker, rejects repeated/older IDs, and does not recover or start workers.
Cancellation drains the original reply before releasing response correlation;
an uncooperative receiver is terminated after the bounded drain deadline. A
cancelled acknowledgement cannot roll back effects already performed by author
code, so handlers should promptly accept navigation data and honor cancellation.

## Focused checks

This repository uses executable test harnesses for this area:

```powershell
dotnet build tests/WidgetIntents.Tests/WidgetIntents.Tests.csproj -c Release -bl:artifacts/game-help/intents-check.binlog -m:1 -nr:false
& tests/WidgetIntents.Tests/bin/Release/net8.0/WidgetIntents.Tests.exe
```

Use a fresh binlog path for subsequent builds. Checks are synthetic and do not
open websites, launch processes, change system settings or invoke real widgets.
