# OverlayHost code findings

Status: review findings; no implementation assigned
Baseline: local `main` at `4edb5f40` on 2026-08-28 (`src/` unchanged since `6afde5dc`)
Scope: concrete code-level defects and hazards in `src/OverlayHost`

These are specific, located findings with proposed remediations. They are not
user-visible bugs (those belong in [known issues](known-issues.md)) and not
architecture findings (those belong in [engineering quality
review](engineering-quality-review.md)). Decomposition of `OverlayApp` is
declined; see [that decision record](overlay-host-main-refactoring-review.md).

## Basis

Findings come from reading `HandleMessage` (642L), `PollController` (600L),
`DrawWidget`'s authority-resolution head, `EnsureGraphicsResources` (188L),
`DiscardGraphicsResources`, and the field block — roughly 1,900 lines. Each
finding below was verified against the source at the cited line. Regions **not**
read: `Initialize`, `ApplyStateTransition`, `DrawWidget`'s paint body,
`HandleAccessibilityActions`, `ReconcileEmbeddedMediaSurface`. Absence of
findings there is not evidence of their absence.

## Summary

| ID | Severity | Location | Finding |
| --- | --- | --- | --- |
| OH-001 | High | [main.cpp:13216](../src/OverlayHost/main.cpp) | Four-level nested ternary derives `renderedFocusId` per snapshot authority, on the DLV-273 boundary |
| OH-002 | Medium | [main.cpp:1910–2061](../src/OverlayHost/main.cpp) | ~150-line inline bridge event pump inside a `switch` case |
| OH-003 | Low | [main.cpp:8553, 8580, 8616](../src/OverlayHost/main.cpp) | `NavigationDirection`→`PlacementDirection` mapping written three times in two styles |
| OH-004 | Latent | [main.cpp:2059](../src/OverlayHost/main.cpp) | `break` silently discards remaining drained host effects |
| OH-005 | Medium | `PinnedSurfaceHostTests.cpp`, `WidgetBridgeCatalogTests.cpp` | 25 assertions match production source text, including whitespace |

## OH-001 — `renderedFocusId` authority ternary

**Severity:** High. Not a known defect; the highest-risk readable surface in the
class.

`DrawWidget` selects which focus identity corresponds to the snapshot it is
about to paint, through a four-level nested conditional spanning 11 lines:

```
const std::wstring_view renderedFocusId = transitionRetainedSnapshot
    ? retainedPresentation->focusId
    : sessionRetainedSnapshot
        ? retainedRefreshFreeScroll && interactionSession_.freeScrollBinding()
            ? interactionSession_.freeScrollBinding()->focusedElementId
            : {}
        : state_.focusRegion() == FocusRegion::Widget
            ? interactionSession_.focusedElementId()
            : {};
```

**Why it matters.** This is the exact computation DLV-273 broke: pairing a focus
identity with a frame's snapshot authority. `renderedFocusId` then feeds the
accessibility projection key, the paint key, and the `visual-focus` diagnostic —
the three places the DLV-273 failure surfaced. The two booleans it branches on
(`transitionRetainedSnapshot`, `sessionRetainedSnapshot`) are themselves derived
from the typed `WidgetContentAuthority` enum a few lines earlier, so the code
converts a typed enum into booleans and then branches on the booleans.

**Proposed fix.** Replace with a `switch` over `WidgetContentAuthority` returning
the focus identity. This makes the mapping exhaustive and compiler-checked, so a
future authority value cannot silently fall into the wrong branch. Values are
unchanged; this is a pure expression rewrite with no state, ordering, or
lifetime change.

**Risk of fix:** Low, and it hardens the one computation that has already failed
in production. Prove with the existing renderer suites plus a Back/focus case.

## OH-002 — inline bridge event pump

**Severity:** Medium (readability and reviewability, not correctness).

The `kControllerTimer` branch of `WM_TIMER` drains nine bridge queues inline:
`PumpEvents`, `TakeRuntimeFailures`, `TakeArtworkResults`,
`TakeLocalWidgetPackageInstallResults`, `TakePlatformAppearanceChangedRevision`,
`TakeWidgetCatalogChangedRevision`, `TakeInvalidatedWidgetIds`,
`TakeActionFailures`, and `TakeHostEffects` — about 150 lines inside one `case`.

**Why it matters.** This is the host's entire asynchronous intake path, and its
ordering is load-bearing (worker failures are processed before `PollController`;
artwork and package results after). Ordering that deliberate deserves a name and
a doc comment. Buried in a `switch` case it reads as incidental.

**Proposed fix.** Lift verbatim to `PumpBridgeEvents()` and call it from the same
place. Pure code motion inside the same object — no fields move, no lifetimes
change, no reordering. Document the intake order at the top of the new method.

**Risk of fix:** Very low. This is the largest readability gain available in the
class at effectively no risk, and it does not conflict with the decision to
decline decomposition — nothing leaves `OverlayApp`.

## OH-003 — duplicated placement-direction mapping

**Severity:** Low.

The same `NavigationDirection` → `PlacementDirection` mapping is written three
times inside `PollController`, in two different styles:

| Line | Context | Style |
| --- | --- | --- |
| 8553 | `stepPinnedPlacement` lambda | `switch` |
| 8580 | `step` lambda, `adjusting` branch | `if`/`else if` chain |
| 8616 | right-stick resize branch | `if`/`else if` chain |

**Why it matters.** Three copies of one total mapping is three places to update
and two chances to diverge. The `if`/`else if` forms are also non-exhaustive: an
added `NavigationDirection` value falls through silently, where the `switch` form
would at least be flagged under `/W4`.

**Not in scope:** line 8524 maps only Left/Right for opacity stepping, which is a
genuine partial mapping. Lines 7574 and 7603 map from Win32 `VK_` codes, a
different source vocabulary.

**Proposed fix.** One `constexpr` helper returning
`std::optional<PlacementDirection>` from a `NavigationDirection`, used at all
three sites.

**Risk of fix:** Low. Behavior-preserving; covered by pinned placement tests.

## OH-004 — host effects discarded after close

**Severity:** Latent. **Not a live defect.**

`TakeHostEffects()` drains the queue and returns by value. The loop `break`s
after dispatching `CloseOverlayAfterAppLaunch`, so any remaining elements in the
returned vector are discarded without a diagnostic — inconsistent with every
other drain in the pump, which logs each drop.

**Why it is not a bug today.** `WidgetHostEffectKind` has exactly one value
([WidgetBridgeClient.h:142](../src/OverlayHost/WidgetBridgeClient.h)), so the
only effects that can be discarded are additional close requests, and the
overlay is closing. The hazard activates the moment a second kind is added.

**Proposed fix.** Either log the discarded remainder, or process the rest of the
batch and dispatch the close once after the loop. Worth doing whenever a second
effect kind is introduced; not worth a standalone change now.

## OH-005 — assertions that match source text

**Severity:** Medium (false confidence in a test suite that otherwise earns
trust).

23 `has(` assertions in `PinnedSurfaceHostTests.cpp` and 2 constructs in
`WidgetBridgeCatalogTests.cpp` read `main.cpp` as a string and assert exact
substrings, including embedded newlines and indentation — for example a match on
`"CompactMediaSeekTarget(\n                                widgetrail::input::NavigationDirection::Left)"`.

**Why it matters.** These assert formatting, not behavior. They fail on any
reflow of correct code, and they pass for any behavior change that preserves the
text. Both failure directions are wrong, and they make the suite's green state
less meaningful than it looks.

**Proposed fix.** Delete them. Where one names behavior worth keeping — compact
media X/LB/RB/LT/RT routing, embedded-media observation sequencing, compact B
exit to click-through — re-express it as a behavioral assertion in the owning
focused test.

**Risk of fix:** Low, but do it deliberately: read each assertion first and
decide whether the behavior it gestures at is covered elsewhere.

## Checked and cleared

Recorded so these are not re-investigated:

- **`richMediaSurface_` null dereference** — several call sites guard on
  `richMediaProof_` rather than the pointer. Not a defect: the member is eagerly
  constructed with `make_shared` in its initializer
  ([main.cpp:13727](../src/OverlayHost/main.cpp)) and never reset.
- **Staleness discipline in the event pump** — every drain loop validates before
  acting (`sessions_.Contains`, `descriptor->runtimeGeneration == effect.runtimeGeneration`,
  `localWidgetPackageImport_.Complete`) and emits a diagnostic on drop. No
  unguarded drain found.
- **Input scope exclusivity in `PollController`** — the eight modal scopes form a
  guard-and-return cascade; each terminates with `return` and none falls through
  into the next. The length is essential, not tangled.

## Suggested order

OH-002 first: it is the largest readability gain at the lowest risk, and it
makes the intake ordering explicit before anyone edits OH-001. Then OH-001,
which is the finding that actually reduces the chance of a DLV-273 recurrence.
OH-005 whenever there is appetite. OH-003 opportunistically. OH-004 only when a
second host effect kind is added.
