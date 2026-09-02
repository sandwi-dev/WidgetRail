# Adding a declarative UI element end to end

Status: active implementation checklist

This guide is for WidgetRail contributors who are adding a public declarative
UI element or a new closed behavior to an existing element. It complements the
author-facing [declarative UI reference](declarative-ui.md), the
[Widget SDK compatibility policy](widget-sdk-compatibility.md), the
[SDK evolution policy](widget-sdk-evolution.md), the
[WRSS reference](wrss.md), and the
[build execution contract](build-execution.md).

The short version is that a new element is not complete when it serializes or
draws. A real interactive element crosses a bounded chain of authorities:

```text
author API
  -> SDK materialization
  -> protocol version and managed validation
  -> Bridge serialization and admission
  -> native parsing and validation
  -> layout, rendering, and theme resolution
  -> focus, input, and exact-current action authority
  -> accessibility projection and events
  -> refresh/update reconciliation
  -> full-widget and pinned-surface parity
```

Every arrow is a compatibility or security boundary. Missing one often leaves
an element that looks correct but cannot be focused, works only in the full
widget, exposes stale actions, rejects otherwise valid snapshots, or is
unusable through UI Automation.

## First decide whether a new wire element is necessary

Do this before editing `ViewNodeKind`. The smallest correct public contract is
usually one of these three shapes:

| Shape | Use it when | Typical changes |
|---|---|---|
| SDK composite | Existing nodes already express the semantics, focus, actions, and accessibility tree. | `src/WidgetSdk`, public API baseline, sample, docs, and focused SDK tests. No protocol kind or native parser change. |
| Closed visual or behavioral variant | An existing semantic element owns the same interaction and accessibility role, but needs one bounded presentation mode. | A versioned property or closed enum, validator, style/layout/render branch, public API, and focused parity tests. |
| New protocol element | The element has distinct data, validation, accessibility semantics, interaction state, or action authority that cannot be represented safely by composition. | The complete cross-boundary checklist in this guide. |

Do not add a new protocol kind just to avoid writing an SDK helper. Do not hide
a distinct interactive role inside an ordinary `Button` composite if doing so
would make validation, action authority, or UI Automation ambiguous.

Before choosing the shape, answer these questions in the assignment or design
note:

1. Is the element presentational, focusable, actionable, value-bearing, a
   container, or a host-owned transient surface?
2. Which controller, keyboard, pointer, and accessibility actions does it
   consume, and which inputs must fall through unchanged?
3. Does the widget publish an action directly, or does the host translate an
   interaction into a more specific current action?
4. Which authored state is mutable, and can it update atomically without
   replacing the entire snapshot?
5. Does it participate in ordinary, responsive, modal, pinned, or
   focus-associated presentation trees?
6. What are the finite count, text, depth, byte, geometry, and resource bounds?
7. Which identity changes retire in-progress interaction: widget, instance,
   runtime generation, presentation generation, sequence, layout, input scope,
   node, or action binding?
8. What is the exact UI Automation role, pattern set, state, event set, and
   accessible name/value?
9. Can an older host safely ignore the new data? If not, which new protocol
   version admits it?

If these answers are not explicit, implementation will usually discover the
contract piecemeal and duplicate ownership.

## Write the contract before the code

Record a compact contract table before implementation. The following fields
are the minimum for an interactive element:

| Contract field | Required decision |
|---|---|
| Identity | Stable node ID, any child/item IDs, and whether IDs are also action sources. |
| Authored payload | Required and optional fields, closed enum values, null/default behavior, and serialization names. |
| Bounds | Maximum list size, string length, tree/depth/resource contribution, finite numeric ranges, and aggregate accounting. |
| Children | None, exactly one, bounded presentational children, or ordinary container semantics. |
| Focus | Whether focusable while enabled, disabled, or busy; explicit-neighbor behavior; initial/restored focus; modal scope behavior. |
| Activation | Inputs consumed, one-shot versus repeat, disabled/busy behavior, and the result that distinguishes “not this element” from “this element consumed but could not act.” |
| Action authority | Exact source node, action ID, optional child/item action, current scope/layout, and stale-result error. |
| Transient state | Host-owned open/adjust/edit state, exact identity captured by that state, refresh compatibility, and every teardown condition. |
| Presentation | Intrinsic measurement, paint, damage, animation, high contrast, DPI/text scale, and fallback composition parity. |
| Accessibility | Role, patterns, name/value, enabled/focused/expanded/selected/offscreen state, child structure, events, and stable automation IDs. |
| Surfaces | Full widget, pinned full-widget projection, compact authored pinned layouts, and excluded presentation-only trees. |
| Updates | Properties that may change atomically and their authority/layout/paint/interaction/accessibility impact. |
| Compatibility | Protocol feature/version, SDK public API classification, generated native parity, and immutable package implications. |

Use a typed result when an activation has more than two meaningful outcomes.
For example, WIDGE-167 uses `NotSelect`, `ConsumedClosed`, and `Opened`. A
Boolean could not safely distinguish an ordinary non-Select node, which should
fall through to generic A handling, from a disabled or all-unavailable Select,
which must consume A without dispatching an unrelated widget action.

## Managed protocol and SDK changes

The managed protocol is the first admission authority. Keep the public SDK and
wire model separate even when they are edited together.

### Protocol constants and closed vocabulary

- Add a feature version to
  `src/WidgetProtocol/ProtocolConstants.cs` when an older host cannot safely
  admit the element or property.
- Advance `CurrentVersion` only as part of that reviewed feature.
- Add every finite bound here when native code also needs the value. Examples
  include maximum option counts, dimensions, and aggregate limits.
- Regenerate
  `src/OverlayHost/WidgetProtocolPresentationContract.generated.h` from the
  managed constants. Never hand-maintain its values:

  ```powershell
  dotnet run --project tests/WidgetProtocol.ContractParity.Tests/WidgetProtocol.ContractParity.Tests.csproj `
    --configuration Release -- --write
  ```

- Verify the checked-in artifact with the canonical `--verify` invocation in
  `scripts/verification-steps.json`.

### Wire model

Update `src/WidgetProtocol/ViewModels.cs`:

- add the closed `ViewNodeKind` member;
- add a bounded record for repeated child/item payload when needed;
- add fields to `ViewNode` with deterministic defaults;
- update `ViewNode.IsFocusable` when the new semantic node participates in
  ordinary focus;
- preserve camel-case JSON vocabulary and avoid polymorphic or arbitrary
  payloads.

Defaults matter. The serializer may publish an empty collection on every node.
The native parser must accept the exact managed output for unrelated kinds, or
the first snapshot of every widget can fail. Conversely, a property that is
forbidden when non-empty must still fail closed when a caller attempts to use
it on the wrong kind. Add explicit tests for absent, empty, non-empty-valid,
and non-empty-wrong-kind forms.

This also applies to any payload that omits an optional property initialized to
a declared non-null default. Host-side managed deserialization in
WidgetRuntime/WidgetProcessClient followed by Bridge serialization through
`SnapshotJson` materializes every such default collection; current workers may
also emit it directly. Update every strict native per-kind property allowlist
before its generic semantic validator can run. For example, `MediaViewport`
admits `selectOptions` as a recognized property only so the shared rule can
accept `[]` and reject a non-empty collection. Exercise the exact affected
`ViewNode` default-array shape through ordinary/recovery checkpoint admission
and incremental presentation-update materialization (or the action-result path
when applicable), while retaining unknown-property and non-empty-wrong-kind
negatives. Do not describe a hand-authored affected-node fixture as complete
byte-for-byte `SnapshotJson` parity unless the test generates or compares the
complete current document.

### Managed validation

Update `src/WidgetProtocol/ViewSnapshotValidator.cs` in all relevant owners:

- required fields and exact kind-specific allowed fields;
- forbidden fields on every other kind;
- identifier, duplicate-ID, action-ID, visible-text, control-character,
  numeric, enum, count, and child constraints;
- exact selected/default item cardinality where applicable;
- accessible name/value consistency;
- focusability and explicit-neighbor target eligibility;
- restrictions inside pinned, action-surface, responsive,
  focus-associated-presentation, and other specialized trees;
- aggregate node, string, byte, depth, and resource accounting.

Do not validate only the happy-path constructor. Raw protocol callers,
presentation updates, restored snapshots, and malicious packages can bypass
SDK constructor checks.

### Feature-version calculation

Update `src/WidgetProtocol/ProtocolVersionRequirements.cs`:

- map the new kind to its feature name and minimum version;
- map any new property independently when it can appear outside that kind;
- preserve diagnostics for an invalid raw node that still uses the new field;
- add a too-old rejection and exact-version admission case.

The SDK should calculate the minimum version required by the actual tree. Do
not use the SDK package version as protocol admission.

### Presentation updates

If any field can change without replacing the whole snapshot, update all four
owners:

1. `src/WidgetProtocol/PresentationUpdates.cs` — add the closed
   `PresentationProperty` and its impact flags.
2. `src/WidgetProtocol/PresentationUpdateValidator.cs` — deserialize and
   materialize the property.
3. `src/WidgetSdk/WidgetPresentationDiff.cs` — emit the change atomically.
4. `src/OverlayHost/WidgetBridgeClient.cpp` — parse the property, materialize
   it, map the native impact, and record any specialized raster or transient
   state consequence.

Classify impact precisely. The available dimensions are authority,
interaction, measurement/layout, resource, paint, surface placement, and
accessibility. Under-classification leaves stale pixels or stale action
authority; over-classification causes unnecessary layout, repaint, or
composition churn.

For collection-valued properties, prove that the complete collection changes
atomically. If a host-owned popup or modal shows data from that collection,
define whether a compatible refresh updates it, closes it, or forces a
complete raster before old transient pixels can survive.

### Public SDK

Update the smallest applicable files:

- `src/WidgetSdk/Elements.cs` for a semantic element record and materialization;
- `src/WidgetSdk/ModernComponents.cs` for public option/item records or closed
  higher-level component contracts;
- `src/WidgetSdk/UI.cs` for the discoverable `UI.*` factory;
- specialized component files only when the element is genuinely a composite;
- `src/WidgetSdk/PublicApi.txt` for the reviewed public surface.

The SDK constructor should reject invalid state early, but it must materialize
exactly the same invariants enforced by `ViewSnapshotValidator`. Include
stable IDs, action IDs, accessibility labels, state flags, focus-neighbor
fluent methods, deterministic style classes, and a complete protocol node.

Classify the API change under
[Widget SDK evolution](widget-sdk-evolution.md). Review the generated missing
and added symbols before updating the baseline. The intentional updater is:

```powershell
dotnet run --project tests/WidgetSdk.Compatibility.Tests/WidgetSdk.Compatibility.Tests.csproj `
  --configuration Release -- update
```

Never replace an existing CLR overload with a new overload that only adds an
optional parameter. Optional parameters are source-compatible but do not
preserve the old compiled CLR signature. Keep an exact forwarding overload
when supported already-compiled consumers require it.

## Bridge and native admission changes

Managed validation is not a substitute for native validation. The host treats
Bridge JSON as untrusted and must independently recognize the same bounded
contract.

### Managed Bridge

Update these owners only when the element needs them:

- `src/WidgetBridge/BridgeRenderStyles.cs` maps the managed kind to the exact
  WRSS/native selector name. Missing this mapping rejects an otherwise valid
  snapshot before native rendering.
- `src/WidgetBridge/BridgeProtocol.cs` carries any host-to-Bridge expected
  action authority. Keep this typed and optional; do not add arbitrary JSON.
- `src/WidgetBridge/WidgetBridgeServer.cs` validates the shape and context of
  the new request before calling the registry.
- `src/WidgetBridge/BridgeClientRegistry.cs` re-resolves the exact current
  widget, runtime, presentation, layout, scope, node, state, and action binding
  immediately before dispatch.

If the host chooses a child/item action, send that exact expected action ID to
the Bridge. Do not trust only the opener node ID. The registry must reject a
changed or unavailable current binding rather than dispatch a stale action.
Use the existing bounded stale-authority result for the owning surface.

### Native JSON model, parser, and validator

Update both `src/OverlayHost/WidgetBridgeClient.h` and
`src/OverlayHost/WidgetBridgeClient.cpp`:

- add a bounded native record for new payload;
- add the field and semantic marker to `WidgetNode`;
- extend the exact allowed-property lists for ordinary snapshots and
  presentation-update materialization;
- parse absent/default and present payloads with exact type checks;
- reject unknown option/item properties;
- duplicate every managed count, identifier, text, enum, state, child, and
  version invariant;
- add the kind to every relevant focus, container, presentation-fragment, and
  forbidden-property classifier;
- include the new payload in aggregate accounting;
- parse and classify atomic presentation updates.

Search for exhaustive or closed classifiers rather than assuming there is one
central switch:

```powershell
rg -n "ViewNodeKind\.|node\.kind|IsFocusableNode|IsEnabledFocusNode|HasNoUnknownProperties|PresentationProperty" `
  src\WidgetProtocol src\WidgetBridge src\OverlayHost
```

The native model may lower a new semantic kind to an existing layout primitive
after validation. WIDGE-167 parses `select`, sets `isSelect`, and lowers the
layout kind to `button`. If using this pattern, preserve the original semantic
marker everywhere that focus, rendering, input, guide, and accessibility need
to distinguish it. If the element is not lowered, update both native focus
owners (`FocusNavigation.cpp` and `WidgetSurfaceFocus.cpp`) and their tests.

## Layout, rendering, and theme changes

### Measurement and geometry

Decide whether the element is a leaf, a container, or a host-owned overlay.

- Inline leaf/container measurement belongs in the declarative layout and
  renderer owners, not in input code.
- Reserve deterministic lanes for state cues, glyphs, values, or disclosure
  indicators during measurement. Do not paint into unreserved text space.
- Use DIP geometry, finite bounds, supported text scale, and the admitted
  viewport. Test compact, standard, wide, fractional-DPI, and high-text-scale
  cases as applicable.
- A transient popup, menu, or editor needs one geometry helper shared by draw,
  hit testing, accessibility bounds, and pinned/full-widget surfaces. Do not
  recompute four similar rectangles.
- Constrain overlays to the content viewport and choose deterministic
  above/below or start/end fallback when the preferred anchor does not fit.
- Define scrolling or virtualization for bounded but potentially long lists.

WIDGE-167 keeps Select popup geometry and hit testing in
`WidgetInteractionSession.*`, while `main.cpp` and
`WidgetSurfaceCoordinator.cpp` consume that shared plan for their independent
windows.

### Native drawing

Update `src/OverlayHost/DeclarativeRenderer.cpp` for inline appearance:

- intrinsic measurement and state-cue reservation;
- background, border, focus, pressed, disabled, busy, selected, value, and
  semantic indicator drawing;
- high-contrast and reduced-motion behavior;
- damage classification and resource lifetime.

Host-owned transient surfaces are normally composed by the relevant host
window owner (`main.cpp` for the ordinary overlay and
`WidgetSurfaceCoordinator.cpp` for pinned surfaces), using the same geometry
and state machine. Preserve paint order: the admitted widget first, then the
host-owned transient surface, then any required chrome owned above it.

### WRSS

Add the semantic selector to the built-in theme in
`src/PlatformSettings/Themes/builtin-default.wrss`, and map the Bridge style
kind to the same selector. Reuse typed WRSS properties; do not bake theme color,
font, radius, or spacing into protocol behavior.

At minimum, verify base, focused, pressed when applicable, disabled, busy,
selected/value state, and high contrast. The default theme must remain usable
without a package style sheet.

## Interaction and exact-current authority

### One reusable interaction owner

Put state transitions and bounded geometry in one provider-neutral native
owner, usually `WidgetInteractionSession.h/.cpp` when the behavior is shared by
ordinary and pinned surfaces. The state object should capture only the exact
authority it needs, such as:

- widget ID and widget instance ID;
- runtime and presentation generation;
- active input scope;
- opener node ID;
- admitted sequence when required;
- exact option/item IDs and action bindings;
- highlighted, selected, or edited state.

Define all retirement paths. A transient interaction must not survive widget
switch, package/runtime replacement, presentation-generation replacement,
scope change, node removal, disabled/busy transition, incompatible payload
change, pinned-layout replacement, unpin, modal replacement, overlay close,
Bridge replacement, or host teardown unless the contract explicitly proves
compatibility.

Snapshot refresh must reconcile the transient state before rendering or action
dispatch. Compatible state may be retained only when the exact semantic
authority is still current.

### Full-widget routing

`src/OverlayHost/main.cpp` owns real overlay input. Wire the element into every
authorized path:

- controller press/repeat phases;
- keyboard parity;
- pointer down/up, hit testing, capture, wheel, and outside-click behavior;
- UI Automation requests;
- focus and B/back precedence;
- overlay close and lifecycle teardown;
- paint and accessibility publication.

Order matters. A specific element consumes its input before the generic widget
action path. Tests must assert the exact consumed predicate and return, not just
the presence and order of nearby tokens. A source test that looks only for
`NotSelect` would still pass if `!= NotSelect` were accidentally inverted to
`== NotSelect`.

### Pinned routing

`src/OverlayHost/WidgetSurfaceCoordinator.h/.cpp` is an independent surface
owner. Do not assume full-widget wiring automatically applies there. Cover:

- focusable versus click-through pinned policy;
- full-widget pinned projection and authored compact layouts;
- controller, keyboard, pointer, wheel, and accessibility routes;
- exact selected layout and input scope;
- geometry, monitor/DPI, placement, unpin, and teardown;
- request queue bounds, exactly-once drain, and optimistic rollback if used.

Keep interaction logic shared while allowing a separate short-lived session
instance when the pinned window has independent focus, lifecycle, geometry,
and teardown authority.

### Controller guide

Update `src/OverlayHost/ControllerGuide.cpp` only when the focused element
changes an actually actionable host hint. The guide must use the same admitted
scope, node state, and available action binding as dispatch. Do not advertise A
when every option is disabled or busy, and do not introduce a second dispatch
owner merely to build text.

## Accessibility is a complete feature, not a label

Update the complete native accessibility stack when the semantic role is new:

- `src/OverlayHost/AccessibilityTree.h/.cpp` — role, domain, tree structure,
  stable automation ID, name/value, enabled/focused/expanded/selected/offscreen
  state, parent/child relation, set position, and bounds.
- `src/OverlayHost/AccessibilityProvider.cpp` — UIA control type, supported
  patterns, property values, pattern methods, hit testing, fragment roots,
  focus, action enqueueing, and stale-generation checks.
- `src/OverlayHost/AccessibilityEvents.h/.cpp` — property-change, structure,
  focus, selection, expansion, live-region, and other required events.
- `main.cpp` and `WidgetSurfaceCoordinator.cpp` — publish the transient
  geometry/state and consume host actions under exact current authority.

Choose the semantic UIA role first. Then implement all required patterns. A
ComboBox, for example, needs more than `UIA_ComboBoxControlTypeId`: it needs
ExpandCollapse, Selection, selected ListItem children, enabled and offscreen
state, value/expanded/selection events, correct fragment parenting, and stable
focus while the popup is open.

Accessible text must remain complete even when visible text is clamped or
ellipsized. Transient children need collision-safe stable automation IDs that
cannot alias authored node IDs. Disabled or busy actions must return the
appropriate UIA unavailable result and never dispatch.

## Samples, author documentation, and packages

Add a provider-neutral example to `samples/SdkGalleryWidget` when the element
is public. The example should exercise the real state transition, not just
render the default appearance. Update
`tests/SdkGalleryWidget.Tests/Program.cs` with exact declaration, protocol
version, state, and action assertions.

Update the public references that authors use:

- `docs/declarative-ui.md` for the element contract and limits;
- `docs/controller-ui-components.md` when it is a reusable controller pattern;
- `docs/widget-authoring-guide.md` for API use and lifecycle/action caveats;
- `docs/wrss.md` when a new selector or themeable state is exposed;
- sample README or package guidance when the sample is distributed.

If a distributed immutable package consumes the new API, advance that package
version and rebuild/validate/package it as a separate explicit assignment
gate. Do not silently overwrite an installed version.

## Test matrix

Use the smallest directly owned gates first. Add a test at every boundary that
can independently regress.

### Managed contract tests

| Owner | Required coverage |
|---|---|
| `tests/WidgetSdk.Tests/Program.cs` | SDK constructor/materialization, deterministic defaults, exact node kind/data, public fluent methods, limits, malformed state, accessible name/value, and computed protocol version. |
| `tests/WidgetSdk.Tests/ProtocolVersionRequirementsTests.cs` | Feature calculation, nested/pinned use, too-old rejection, exact-version admission, and invalid raw property use. |
| `tests/WidgetSdk.Tests/WidgetPresentationUpdateTests.cs` | Atomic diff/materialization and exact impact flags for mutable properties. |
| `tests/WidgetSdk.Tests/PinnedPresentationLayoutTests.cs` | Aggregate pinned bounds and validation when the element is legal in pinned projections. |
| `tests/WidgetSdk.Compatibility.Tests` | Exact public API additions/removals and release-unit classification. |
| `tests/SdkGalleryWidget.Tests` | Provider-neutral public sample declaration and behavior. |
| `tests/WidgetBridge.Tests` | Managed JSON/registry dispatch, exact-current action authority, stale runtime/scope/node/action rejection, and no unintended provider identity branch. |

### Native contract tests

| Owner | Required coverage |
|---|---|
| `WidgetBridgeCatalogTests.cpp` | Valid JSON, absent/empty defaults, wrong-kind payload, unknown fields, malformed types, limits, version boundary, native materialization, and update impact. |
| `DeclarativeRendererTests.cpp` and layout tests | Intrinsic geometry, reserved lanes, style states, DPI/text scale, clipping, high contrast, and deterministic pixels/plans. |
| `WidgetInteractionSessionTests.cpp` | State machine, typed activation outcomes, navigation, unavailable state, compatible refresh, every retirement identity, exact action result, and bounded geometry. |
| `WidgetSurfaceCoordinatorTests.cpp` | Real pinned controller/pointer/keyboard/UIA routes, geometry, paint, focus, action queueing, reconciliation, and teardown. |
| `PinnedSurfaceHostTests.cpp` | Host routing order and exact consumed branch before generic dispatch. |
| `GuideInputCompatibilityTests.cpp` | Hint visibility only when the exact focused action is available. |
| `AccessibilityTreeTests.cpp` | Role/tree/domain, stable IDs, child structure, focus, selected/expanded/offscreen state, bounds, and collision resistance. |
| `AccessibilityProviderTests.cpp` | UIA control types, patterns, methods, properties, focus, hit test, current-generation authority, and unavailable behavior. |
| `AccessibilityEventsTests.cpp` | Exact property and automation events without duplication or stale delivery. |

For a host-owned transient surface, tests must exercise both ordinary and
pinned surfaces through their real routing owners. Separate session and Bridge
seams are useful but are not a substitute for an integrated host route.

## Build and test wiring

New test code often compiles only after the target owns the exact production
objects it calls. Keep target-local wiring synchronized in both:

- `src/OverlayHost/build.ps1` for the repository's focused Windows selectors;
- `src/OverlayHost/CMakeLists.txt` for the equivalent CMake test target.

Do not add stubs, suppress unresolved symbols, or link an unrelated aggregate
library. Include the established implementation object and its exact dependency
bundle. A fresh worktree may also lack packaged `out\Release` fixtures; run the
assignment-ordered coherent build rather than changing an oracle for missing
artifacts.

Canonical managed and parity commands are registered in
`scripts/verification-steps.json`. Typical directly affected commands are:

```powershell
# Restore-bearing: use supported network access and keep NuGet auditing enabled.
dotnet build tests/WidgetSdk.Tests/WidgetSdk.Tests.csproj --configuration Release --nologo
dotnet run --project tests/WidgetProtocol.ContractParity.Tests/WidgetProtocol.ContractParity.Tests.csproj `
  --configuration Release -- --verify
dotnet run --project tests/WidgetSdk.Tests/WidgetSdk.Tests.csproj `
  --configuration Release --no-build
dotnet test tests/WidgetSdk.Compatibility.Tests/WidgetSdk.Compatibility.Tests.csproj `
  --configuration Release --no-ansi --no-progress --output Detailed `
  --minimum-expected-tests 12
dotnet run --project tests/SdkGalleryWidget.Tests/SdkGalleryWidget.Tests.csproj `
  --configuration Release

# Native focused selectors: use the smallest owners touched by the element.
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -WidgetBridgeCatalogTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -DeclarativeRendererTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -WidgetInteractionTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -WidgetSurfaceTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -PinnedSurfaceTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -AccessibilityTreeTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -AccessibilityEventsTestsOnly
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -ControllerGuideTestsOnly

# Final production compilation after focused owners are green.
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -SkipTests
```

Not every element requires every selector. Derive the gate list from the files
and owners changed, state the order before execution, and stop at the first
genuine red. A setup failure, zero-test run, missing packaged fixture, or test
binary that never launched is infrastructure evidence, not a green behavior
gate.

Follow the assignment's physical-first or test-first order. A green build does
not prove controller, focus, accessibility, DPI, or lifecycle behavior.

## Recommended implementation order

Use this sequence to avoid discovering late boundaries after the renderer is
already written:

1. Write the contract table and decide composite, variant, or new kind.
2. Add protocol constants, wire model, managed validation, and feature-version
   calculation.
3. Add the SDK element/factory and generate the reviewed public API diff.
4. Add presentation-update semantics for every mutable property.
5. Regenerate and verify the native constants artifact.
6. Add managed Bridge style/request authority and native parse/validation.
7. Add layout, renderer, default theme, and damage classification.
8. Add one shared interaction owner with exact identity and teardown.
9. Wire full-widget and pinned input paths in the correct precedence order.
10. Add UI Automation tree, provider patterns, actions, and events.
11. Add controller-guide projection from the real action authority.
12. Add the provider-neutral SDK Gallery example and public documentation.
13. Add test-target wiring and run the smallest focused gates in the assigned
    order.
14. Run one coherent Release build, inspect the exact diff, and obtain the
    required physical verdict.

Do not start by editing `main.cpp`. Starting at the host input path encourages
an ad hoc feature with no public, protocol, validation, or accessibility
contract.

## Copyable completion checklist

### Contract and compatibility

- [ ] The element cannot be represented safely by an existing composite or
      closed variant.
- [ ] Required, optional, default, forbidden, and serialized fields are
      explicit.
- [ ] Counts, strings, numbers, geometry, bytes, depth, and aggregate resource
      use are finite.
- [ ] A feature version and too-old diagnostic exist when required.
- [ ] `CurrentVersion` and generated native constants are in parity.
- [ ] The public SDK diff is classified and reviewed; existing CLR signatures
      are preserved when required.

### Managed SDK and protocol

- [ ] `ViewNodeKind`, payload records, `ViewNode`, and `IsFocusable` are
      complete.
- [ ] SDK constructor/factory materialization matches raw protocol validation.
- [ ] Wrong-kind, absent, empty, duplicate, malformed, oversized, and
      inaccessible forms fail or admit exactly as specified.
- [ ] Specialized subtree and pinned aggregate rules are updated.
- [ ] Mutable properties diff and materialize atomically with exact impacts.

### Bridge and native admission

- [ ] Bridge style mapping uses the intended WRSS selector.
- [ ] Native unknown-property allowlists match managed serializer output.
- [ ] Ordinary/recovery checkpoints and materialized incremental/action results
      admit the exact affected `ViewNode` default-collection shape while
      wrong-kind non-empty collections and unknown properties still fail
      closed; complete-document parity claims use generated/current bytes.
- [ ] Native parsing duplicates all managed invariants and bounds.
- [ ] Native focus/container/presentation classifiers are exhaustive.
- [ ] Exact-current widget/runtime/presentation/layout/scope/node/action
      authority is re-proven before dispatch.
- [ ] Stale action binding fails closed with the owning bounded error.

### Presentation and interaction

- [ ] Layout reserves every painted lane and is stable across supported DPI and
      text scale.
- [ ] Base, focused, pressed, disabled, busy, selected/value, and high-contrast
      visuals are defined as applicable.
- [ ] Transient geometry is shared by drawing, hit testing, UIA, and pinned/full
      surfaces.
- [ ] The interaction state captures exact identity and retires on every
      incompatible lifecycle transition.
- [ ] Specific activation consumes input before generic widget dispatch;
      non-owning inputs fall through unchanged.
- [ ] Controller, keyboard, pointer/wheel, B/back, UIA, full-widget, and pinned
      parity are explicit.
- [ ] Atomic refresh cannot leave stale transient pixels or action authority.

### Accessibility and guide

- [ ] UIA role, patterns, properties, methods, parent/child structure, stable
      IDs, bounds, and hit testing are complete.
- [ ] Enabled, focused, expanded, selected, offscreen, value, and set-position
      semantics are correct.
- [ ] Required focus, selection, structure, property, and live events fire once
      and only for current authority.
- [ ] Visible truncation does not truncate accessible text.
- [ ] The controller guide advertises only the exact available action and adds
      no dispatch owner.

### Evidence

- [ ] Managed SDK, protocol-version, presentation-update, parity, compatibility,
      Bridge, and sample cases cover the new contract.
- [ ] Native parse, renderer, interaction, pinned host, guide, UIA tree/provider,
      and event cases cover every changed owner.
- [ ] Focused test targets link the exact production objects in `build.ps1` and
      `CMakeLists.txt`.
- [ ] No test passes only because it searches for unordered tokens or ignores
      an inverted condition.
- [ ] One coherent Release build and the assignment-required physical cases are
      green or explicitly pending.
- [ ] The exact source, generated artifact, docs, sample, tests, and package
      scope is reviewed before commit.

## WIDGE-167 Select case study

WIDGE-167 is the reference implementation for an A-activated host-owned
anchored element. It demonstrates why the complete checklist is necessary:

| Boundary | Select implementation |
|---|---|
| Protocol | `ViewNodeKind.Select`, `WidgetSelectOption`, `SelectOptions`, protocol feature `AnchoredSelectVersion`, and `MaximumSelectOptionCount`. |
| Managed validation | Exactly one selected option; unique option/action IDs; bounded visible and accessible labels; disabled/busy option state; opener value matches selected label; wrong-kind use fails. |
| SDK | `SelectOption`, `SelectElement`, `UI.Select`, focus-neighbor methods, deterministic `wrail-select` class, and complete accessible value materialization. |
| Updates | `SelectOptions` changes atomically with authority, interaction, paint, and accessibility impact; an open popup triggers complete-raster protection. |
| Native admission | Exact `selectOptions` JSON parsing and property allowlists, `isSelect` semantic marker, lowering to the existing button layout primitive, version and selected-option checks. |
| Rendering | The button layout reserves a disclosure lane; the renderer draws the disclosure cue; the default WRSS theme supplies `select` and `select:focused`. |
| Interaction | One `WidgetInteractionSession` popup state owns layout, hit testing, highlight, commit, compatibility, and retirement. `NotSelect`, `ConsumedClosed`, and `Opened` make A routing unambiguous. |
| Action authority | The host sends the exact selected option action; Bridge re-resolves the current full or pinned layout, scope, opener, and option binding before dispatch. |
| Full widget | `main.cpp` owns controller, keyboard, pointer, wheel, paint, UIA, lifecycle, and generic-action precedence. |
| Pinned surface | `WidgetSurfaceCoordinator` owns the same routes for its independent HWND, focus, geometry, lifecycle, and request queue. |
| Accessibility | The opener is a ComboBox; options are stable ListItem children with ExpandCollapse, Selection, selected/expanded/offscreen state, bounds, actions, focus, and events. |
| Controller guide | A is advertised only when the focused current Select has at least one non-disabled, non-busy option. |
| Sample and docs | SDK Gallery demonstrates density selection; author references describe options, bounds, state, action callback, and anchored behavior. |
| Tests | Managed materialization/version/update/pinned tests plus native parser, renderer, interaction, full/pinned routes, guide, UIA tree/provider/events, and source-order assertions. |

The most important lessons from this case are:

1. Reusing button layout did not remove the need for a distinct semantic
   marker, validator, guide rule, UIA role, or action authority.
2. A host-owned popup required one exact identity model and two independent
   window owners, not two state machines.
3. Option changes were not “just paint”; they changed action and accessibility
   authority and could leave popup pixels outside the opener's damage region.
4. UIA required role, patterns, children, events, bounds, hit testing, focus,
   and host-action routing together.
5. The native parser had to match exact managed default serialization, not an
   assumed minimal JSON shape.
6. Tests had to prove the exact consumed inequality and return before generic
   dispatch; token order alone was insufficient.
7. Focused test executables needed explicit production-object wiring in both
   supported build descriptions.

When a future element differs from Select, remove inapplicable rows deliberately
rather than silently skipping them. The review should be able to say why each
boundary is unchanged.
