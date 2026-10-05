# Widget intents (in development)

Button/card actions, pinned controls and virtualized row actions are implemented
through contracts, manifests, Bridge dispatch, worker delivery, native frontend
activation and passive delivery into existing full-widget or authored-layout pins.
Compact embedded-media destinations also support passive delivery.
Do not advertise intent handling in published packages until the full integration
and release compatibility gate are complete. Track progress in
[the Game Help plan](../maintainers/game-help-implementation-plan.md).

## Routing an action through Windows

Normal web intents prefer enabled widget handlers and use the default browser only
when none is available. Sign-in/account pages can explicitly bypass widget handlers:

```csharp
UI.Button("Sign in", "signin", "signin")
    .OpenIntent(WidgetIntentContracts.Web,
        JsonSerializer.SerializeToElement(new { url = "https://example.com/signin" }),
        routing: WidgetIntentRouting.Windows);
```

This protocol-71 per-request override is separate from the receiver's placement
policy and does not alter the payload schema or manifest declaration. Currently,
only the standard web contract maps to Windows HTTP(S) URL handling. Unsupported
Windows mappings return unavailable; they do not fall back to a widget. There is
no arbitrary protocol/command execution. Routing is part of action identity, so a
stale action cannot change routes while it is being dispatched. WidgetRail's intent
registry is independent of Windows protocol associations.

Handlers may opt into `hasDynamicAvailability: true` on an individual mapping.
The Bridge calls `Widget.IsIntentAvailableAsync(request, token)` before listing the
handler and rechecks its exact worker before delivery. The callback can run after
initialization in Background; it must be read-only and bounded. It may inspect its
own key/account configuration but must not activate a window or perform the action.
No availability cache is used. Existing handlers without the opt-in remain static.
YouTube opts in for search only; known-video playback needs no search API key.

## Package declarations

An optional `intents` manifest object contains `requests` and `handles` arrays.
Each entry carries `id`, positive integer `version`, and `payloadSchema`.
Handler entries additionally accept `supportsPassiveDelivery` and
`hasDynamicAvailability` (both default `false`).
This belongs to that exact contract ID/version mapping; it is invalid on request
entries or on the enclosing `intents` object. In C#, author a handler using
`new WidgetIntentHandler(contract) { SupportsPassiveDelivery = true }`.
The presentation policy is independent of the payload schema digest: senders
and receivers share the contract without needing identical presentation settings.
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

## Search and sender feedback (protocol 72)

`widgetrail.video.search`, version 1, accepts `provider` (1–64 characters) and
`query` (1–240). This is an action contract, not a destination package ID. YouTube
Video accepts provider `youtube` when its search API key is configured, opens
Discover, and runs the normal search collection. Accepted means the query was
accepted into state; network results may still fail later in the receiver.

```csharp
UI.Button("Search YouTube", "search", "search")
    .OpenIntent(WidgetIntentContracts.VideoSearch,
        JsonSerializer.SerializeToElement(new { provider = "youtube", query }))
    .WithIntentFeedback(WidgetIntentRequest.Create(WidgetIntentContracts.Web,
        JsonSerializer.SerializeToElement(new {
            url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query)
        })));
```

`WithIntentFeedback()` is also usable without a fallback. Override
`OnIntentCompletedAsync(WidgetIntentFeedback feedback, CancellationToken token)`
to receive the correlated request ID, original request and terminal routing status:
`Accepted`, `Unavailable`, `Rejected`, `Cancelled` or `Failed`. Return true only
when the sender wants the originally declared fallback. The host honors this only
for Unavailable/Rejected. A callback cannot supply a new request. Both requests
must be declared by the source manifest. No nested fallback is allowed. Returning
false leaves the normal host feedback in place when the action cannot open.

The callback may run while the source is Background. Feedback is best-effort to
the same living worker; it never restarts a retired source or delivers an old
result to a replacement. Cancellation, an uncertain timeout, a worker failure or
an invalidated displayed action cannot authorize automatic fallback. Bounded
runtime operations retain correlation while draining cancellation. Each ticket is
consumed once; fallback revalidates the original action and current source.

Game Help chooses its existing results-page web intent only after search is
unavailable or rejected. The fallback uses normal widget-preferred web routing,
including eligible visible browser pins, then Windows if no web handler exists.
For external-browser routing, Accepted means the validated URL was handed to the
frontend; Windows launch failures are reported by the frontend's normal dialog.

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

The dispatcher binds every ordinary request to authenticated caller identity,
manifest declaration, current user action and lifetime, then revalidate the target
generation before delivery. The frontend opens the destination as an ordinary
widget; there is no return-to-sender stack or B remapping. Users return through
the tray or pin the destination using existing behavior.
These requirements are not satisfied merely by calling the pure resolver.
Preparation creates a bounded, one-shot 60-second ticket; the host establishes
the selected destination before committing it to that exact worker incarnation.
Cancelling a delivery does not tear down the Bridge connection. A failed web
handler can return a browser-fallback offer, which requires an explicit user
choice and never automatically invokes a second handler.

The presentation policy separates routing from placement: prefer an
existing pinned surface when that specific intent-handler mapping opts into
passive delivery, preserve
the sender's focus, and allow explicit sender/receiver requests for interaction.
The opt-in is per contract ID/version mapping, never widget-wide; one widget can
handle different intents with different interaction requirements. Sender hints
and receiver escalation are per invocation. A sender may pass
`WidgetIntentPresentation.OpenWidget` as the third argument to `.OpenIntent(...)`
to request ordinary activation. The default is `PreferExistingSurface`.
Preference changes participate in displayed-action authority, like payload changes.
The live host selection, projection lifetime, visibility, worker and mapping must
all remain valid for passive delivery; persisted pin settings grant no authority.
Unpinning cancels an in-flight delivery. Compact embedded-media destinations
currently use ordinary widget activation rather than this projection path.

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
input-authority revalidation. Routing admits only primary activation
(pointer/keyboard or controller A press); repeat/release and unrelated shortcuts
do not gain intent authority. Pinned controls use their live selected projection;
virtualized rows use the exact retained item lease, query and active input scope,
including within a pin. Retired selections/leases are rejected. Intent activation
does not also invoke the widget's ordinary action callback. A pinned source that
opens another widget or a chooser returns host input to the main window first;
this adds no return-to-sender stack or B remapping.

Receivers override `Widget.OnIntentAsync(WidgetIntentRequest, CancellationToken)`
and return `WidgetIntentResult.Accepted` after accepting data into widget state,
or `InteractionRequired` to ask the host to open that accepted state. The request
is never delivered again when escalating to interaction. Return `Rejected` when
unsupported. Use normal state/operation helpers for subsequent work. Receiver
transport admits visible/interactive lifetime only, has one pending delivery per
worker, rejects repeated/older IDs, and does not recover or start workers.
Cancellation drains the original reply before releasing response correlation;
an uncooperative receiver is terminated after the bounded drain deadline. A
cancelled acknowledgement cannot roll back effects already performed by author
code, so handlers should promptly accept navigation data and honor cancellation.

## Initial consumers

Playnite Library's game-details links declare `widgetrail.web.open`. The host
opens an eligible browser widget or uses the default-browser fallback; the widget
no longer launches a browser from its companion process. Link identity and URL
are validated against the displayed/current control.
Playnite game-details links use this shared route. Install and enable the
compatible Playnite package to exercise it with the bundled Browser receiver.

YouTube Video handles `widgetrail.video.open` with provider `youtube`, an exact
11-character YouTube video ID, and an optional timestamp. The embedded player
currently supports timestamps from 0 through 86,400 seconds; larger standard-
contract values are rejected without replacing existing playback. Loading keeps
the established cue behavior and saved volume. It does not require a search API
key. Install and enable a compatible YouTube Video package to use the handler;
provider playback verification is separate from synthetic routing checks. Compact pinned players can
receive passive intents using their live media-document authority, without
transferring source focus.

## Focused checks

This repository uses executable test harnesses for this area:

```powershell
dotnet build tests/WidgetIntents.Tests/WidgetIntents.Tests.csproj -c Release -bl:artifacts/game-help/intents-check.binlog -m:1 -nr:false
& tests/WidgetIntents.Tests/bin/Release/net8.0/WidgetIntents.Tests.exe
```

Use a fresh binlog path for subsequent builds. Checks are synthetic and do not
open websites, launch processes, change system settings or invoke real widgets.
