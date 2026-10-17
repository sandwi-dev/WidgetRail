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

`ComponentAction.Intent` and `ActionSheetItem.Intent` carry the same request to
the generated button in settings rows, alerts, empty states and action sheets.
Declare the corresponding contract in the package's `intents.requests`, including
for Windows routing. Use this path for external account/setup links so the host
can wait for controller release and its close transition before launching.

```csharp
var request = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
    JsonSerializer.SerializeToElement(new { url = "https://example.com/setup" }),
    WidgetIntentRouting.Windows);
var setup = new ComponentAction("Open setup", "open-setup") { Intent = request };
var row = UI.SettingsRow("Account", setup, "account");
var item = new ActionSheetItem("links.setup", "Open setup", "open-setup")
    { Intent = request };
```

The same property supports widget-preferred routing and presentation preferences;
it is not limited to external links. Keep a stable action ID and declare the
contract normally. Omitting `Intent` retains ordinary widget action handling.
Generated buttons use the existing intent authorization and routing path; these
components do not open a browser themselves.

Handlers may opt into `hasDynamicAvailability: true` on an individual mapping.
The Bridge calls `Widget.IsIntentAvailableAsync(request, token)` before listing the
handler and rechecks its exact worker before delivery. The callback can run after
initialization in Background; it must be read-only and bounded. It may inspect its
own key/account configuration but must not activate a window or perform the action.
No availability cache is used. Existing handlers without the opt-in remain static.
YouTube opts in for search only; known-video playback needs no search API key.

## Calculate an intent payload after a click

Protocol 85 adds optional preparation for destinations that require asynchronous
work, such as calculating a build-aware search URL. Existing intents are unchanged.

```csharp
UI.Button("Search", "search", "search")
    .OpenIntent(WidgetIntentContracts.WebV2,
        JsonSerializer.SerializeToElement(new { url = "https://example.com/search" }),
        routing: WidgetIntentRouting.Windows)
    .PrepareIntent("calculate-search");

public override async ValueTask<JsonElement?> OnPrepareIntentAsync(
    string key, WidgetIntentRequest request, CancellationToken token)
{
    if (key != "calculate-search") return null;
    var url = await CalculateSearchUrlAsync(token);
    return JsonSerializer.SerializeToElement(new { url });
}
```

The declared initial payload must already match the declared schema. It is an
action identity, not a destination the host opens before preparation. Include
relevant context in that payload or preparation key if changing the displayed
selection should invalidate an outstanding click. During progress, keep the
original action and input scope stable if the request should remain valid; do not
replace it with a different destination or disable its ancestors.

Preparation runs only after host admission of the user's action, on the bounded
intent lane. The widget may show cancellable progress and must honor its token.
The host enforces a 65-second bound and rechecks the original displayed action,
worker, declared schema, route, presentation and storage policy before routing.
Only the payload may change. A preparation cannot add another preparation or a
fallback. Returning null means preparation failed or was declined; show useful
feedback in the widget. Back, hiding the overlay, or leaving the source cancels
pending preparation. A cancelled or stale result must not open a destination.

## Temporary overlay browser pages

Protocol 84 adds `WidgetIntentRouting.OverlayBrowser`. A host-owned page opens
over the source widget; Back closes it and returns to the source, including a
source pinned surface. It does not navigate the Browser widget's existing page
and does not require that widget to be installed or enabled.

```csharp
// A fresh private session is the SDK default.
UI.Button("Open reference", "reference", "reference")
    .OpenOverlayBrowser("https://example.com/reference");

// Use persistent Browser storage when the user expects sign-in to survive.
UI.Button("Open account", "account", "account")
    .OpenOverlayBrowser("https://example.com/account", BrowserStorage.Persistent);
```

Persistent pages share the Browser profile's cookies, website storage and enabled
extensions. Private pages use a fresh native InPrivate profile, do not inherit
that storage or extensions, and do not retain their session for the next opening.
Choose storage explicitly when presenting a persistent sign-in workflow. The
host owns navigation and focus restoration; widgets receive no access to remote
page scripts, cookies or credentials through this intent.

Declare `WidgetIntentContracts.Web` in `intents.requests` for ordinary links.
The helper selects `WebV2` for URLs longer than 2,048 characters; declare that
version too if such links are possible. Version 2 allows up to 8,192 characters,
while version 1 retains its existing limit. Both accept only valid HTTP(S) URLs
through the host's web routing. Requests remain bound to the displayed action;
storage mode is part of that identity. For `ComponentAction` or action sheets,
create the equivalent request with `OverlayBrowser` routing and set
`OverlayBrowserStorage` to the chosen `BrowserStorage` value.

Only one temporary page can be open at a time, and it counts toward the host's
browser capacity. Failure or cancellation must leave the source usable. This
route is for website viewing; use a packaged web session when the widget needs
to exchange messages with its own page.

## Package declarations

### Settings destinations

Settings handles two navigation-only contracts, both with a payload such as
`{"packageId":"widgetrail.firstparty.capture-library"}`:

- `widgetrail.settings.widget.open` (`WidgetIntentContracts.WidgetSettings`) opens
  that widget's details, for an installed package or a bundled widget.
- `widgetrail.settings.permissions.open` (`WidgetIntentContracts.WidgetPermissions`)
  opens that widget's declared permissions.

Declare the corresponding request with this schema:

```json
{"type":"object","additionalProperties":false,"properties":{
  "packageId":{"type":"string","minLength":3,"maxLength":128}
},"required":["packageId"]}
```

Use `WidgetIntentPresentation.OpenWidget`. These handlers do not support passive
delivery. They refresh the relevant projection and replace the previous page and
selection; missing packages or packages without permissions reject the request.
Navigation does not grant permission, enable a widget, or approve an installation.
There is no special B return path: Settings retains its ordinary navigation.

```csharp
UI.Button("Open permissions", "unused", "permissions")
    .OpenIntent(WidgetIntentContracts.WidgetPermissions,
        JsonSerializer.SerializeToElement(new { packageId = "example.publisher.widget" }),
        WidgetIntentPresentation.OpenWidget);
```

### Manifest format

The standard `widgetrail.marketplace.open` contract (`WidgetIntentContracts.Marketplace`)
opens the dedicated Marketplace with an empty object payload. Its schema is
`{"type":"object","additionalProperties":false,"properties":{}}`. Request
`OpenWidget` presentation. This navigation contract does not grant installation
authority to its sender; users still review and approve each package in Marketplace.

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

### App update destination

`WidgetIntentContracts.AppUpdates` declares `widgetrail.settings.updates.open`,
version 1, with an empty object payload. Settings handles it by opening App updates
and reading status. It never starts a check, download, installation or recovery
as a side effect of navigation. Pending ordinary widget notifications may use
`OpenIntent` with any valid sender-declared intent contract; see
[widget notifications](widget-notifications.md).
