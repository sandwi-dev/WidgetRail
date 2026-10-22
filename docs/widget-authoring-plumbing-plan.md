# Widget SDK authoring improvements plan

Date: 2026-10-22  
Audit baseline: `adb696ac` (`codex/playnite-achievement-focus`)  
Status: Active completion audit on `codex/sdk-authoring-improvements`. F5 scoped resources, F6 loaded lists, and F9 controlled disclosures are implemented and validated. Comparative YTM performance evidence is recorded below. F3 explicit viewport-return implementation passes its delayed-return fixture; broader native regression and recreation qualification remain in progress. Historical first-pass outcomes below are superseded where later sections say so. Nothing is merged, pushed, published, or installed. The current user authorization permits launching a separate candidate for validation.

Move generic focus, navigation, collection, and asynchronous presentation bookkeeping out of Playnite, shared PoB, YouTube Music, Spotify, and Settings into reusable SDK and host facilities. Widget authors should declare intent and domain data without maintaining competing request counters or reconstructing framework lifecycle rules.

The YouTube Music audit covers the shipping `StandaloneMusicWidget` and its service/player adapters, as constructed by `samples/YtMusicWidget/Application/Program.cs`. The older companion implementation under `tests/YtMusicCompanionFixture` is a fixture, not the shipping widget. Spotify coverage includes presentation, provider integration, pinned demand, and playback coordination. Settings coverage includes navigation, forms, installed widgets, permissions, diagnostics, and update flows.

This is an engineering design plan. Live assignments, delivery status, and review evidence remain governed by [the delivery workflow](delivery-plan.md). The separate Catalogue return-focus work is outside this plan.

## Findings and priorities

Complexity includes API design, migration of affected widgets, and regression coverage: **1** is a small cleanup; **5** is a substantial architectural change. These are relative estimates, not delivery dates. Except for the previously reproduced Playnite focus-counter defect, the findings are source-level architectural observations and risks, not newly reproduced runtime failures. The additional audit did not run application tests or change production code.

| ID | Improvement | Priority | Ownership | Complexity |
| --- | --- | --- | --- | --- |
| F1 | Allocate focus requests centrally | High | SDK | 2/5 |
| F2 | Support direct element-focus requests | High | SDK, protocol, host | 3/5 |
| F3 | Integrate navigation and modal return targets | High | SDK and host integration | 4/5 |
| F4 | Remove redundant Playnite Back-focus capture | High | Widget migration and SDK default | 1/5 |
| F5 | Add scoped resources keyed by page or item | High | SDK | 4/5 |
| F6 | Add an adapter for loaded keyed collections | Medium | SDK | 3/5 |
| F7 | Bind suggestion providers to fields and scopes | Medium | SDK | 3/5 |
| F8 | Add bounded rich-document construction | Medium | SDK; host if continuous flow needs changes | 3/5 initially |
| F9 | Add a standard disclosure component | Medium | SDK and potentially host semantics | 3/5 |
| F10 | Derive busy presentation from operation admission | High | Existing SDK adoption or small helper | 2/5 |
| F11 | Replace Settings label parsing with structured form declarations | High | Widget migration and SDK form composition | 3/5 |
| F12 | Combine resource demand from pages and pinned layouts | High | SDK | 3/5 |
| F13 | Share lifecycle-owned adaptive polling infrastructure | Medium | Existing ticker adoption and SDK extension | 3/5 |
| F14 | Centralize coalesced notification delivery | Medium | SDK | 3/5 |
| F15 | Remove duplicate activity-report coordination | Low | Existing SDK adoption | 2/5 |
| F16 | Reuse optimistic command coordination selectively | Medium | Existing SDK adoption; optional reconciliation extension | 3/5; 4/5 with observation integration |

## Design constraints

- Reuse `WidgetNavigator`, `WidgetResource`, `WidgetOperations`, indexed collections, and host presentation memory. Extend their composition rather than build parallel state systems.
- Preserve host validation, input-scope ownership, stable identities, cancellation, stale-result rejection, bounded memory, and lazy realization.
- Keep existing SDK declarations compatible where possible. Add protocol features with explicit version requirements and define behavior on unsupported hosts.
- Request focus in response to an event, not on every render. An unchanged UI snapshot must not steal focus after a refresh.
- Keep generic request identity separate from domain revisions. A framework generation cannot prove that a build, game, file, or external operation is still semantically current.
- Migrate in bounded steps and remove obsolete counters, flags, helpers, and tests that enforce obsolete implementation details once equivalent behavior is covered.
- Do not deploy, restart the user's instance, merge, or release as part of writing this plan.

## Decision gate for every proposed change

Preserving existing functionality takes priority over completing the findings in
this plan. Each proposed abstraction must earn adoption through implementation
evidence; an audit recommendation can be wrong.

Before broad migration, establish the existing behavior and relevant failure
cases, then compare a representative implementation for correctness, total
complexity, lifecycle ownership, performance, and debuggability. Count the SDK,
host, and consumer machinery together rather than only the lines removed from a
widget. Passing tests is necessary for affected behavior but does not by itself
make an abstraction simpler or safer.

If a proposal makes the design more complicated or brittle, relies on growing
special cases, weakens guarantees, or cannot preserve required behavior, stop
that part and retain the working approach. Remove only the abandoned experiment's
own changes, preserving unrelated work. Do not compensate with more manual
plumbing, weaken regression tests, or remove domain safeguards to force adoption.

Update the finding as rejected, narrowed, or deferred, explain the evidence and
the corrected assessment to the user, and continue independent authorized work.
A finding may also apply to some consumers but not others; record that explicitly
instead of forcing uniformity. No item must be implemented merely because it was
included in this plan, and unverified behavior must remain identified as such.

## Phase 1 Focus requests and Back actions

### F1 Central focus request allocation

**Evidence:** [Playnite request allocation](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.cs), [PoB focus routing](../samples/PobCompanion/PobWidget.Collections.cs), and [host request consumption](../src/OverlayFrontend.WinUI/Presentation/WidgetViewPresenter.Focus.cs).

The host consumes increasing request IDs across a presenter. Playnite previously allocated independent Library and Achievements sequences: request 1 from Achievements was ignored after Library request 9. Commit `adb696ac` shares the sequence. PoB has 72 matching lines that increment `focusRequestSequence` across features at the audit baseline.

Introduce a widget-owned focus request facility that allocates identity internally and retains a stable request across renders. Define ownership across main, modal, and pinned presentations, cancellation/supersession, widget reloads, retained presenter state, and request-limit behavior. Do not create independent allocators that compete within one consumer's sequence.

**Acceptance:** First L3 works after Library navigation and after returning from Achievements; repeated rendering does not issue a fresh request; separate features cannot collide; scope changes cannot redirect an old request into another page.

### F4 Consume the focus metadata already on actions

**Evidence:** [Playnite controller interception and pending Back dictionary](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.cs), [SDK action creation](../src/WidgetSdk/Widget.cs), and [navigator Back handling](../src/WidgetSdk/WidgetNavigator.cs).

Use `WidgetActionEvent.FocusedElementId` instead of correlating controller input and actions through `_pendingBackFocus`. Make `TryHandleBack` use actual focus metadata by default, preserving an explicit override. Define the fallback for actions with no focus metadata; do not confuse the shortcut declaration owner with the focused control.

**Acceptance:** Nested-route Back remembers the actual child, including ancestor-owned shortcuts; pointer and synthetic actions with missing metadata remain valid; stale actions remain rejected. Remove the dictionary, interception, and related cleanup after verification.

## Phase 2 Direct targeting and navigation return

### F2 Direct element focus

**Evidence:** [PoB unified navigation](../samples/PobCompanion/PobWidget.UnifiedNavigation.cs).

PoB recursively walks containers and presentation decorators and inserts a group around the target just to focus a search result. Add a typed direct-element target alongside existing group and indexed-item targets. The host should resolve it within the active scope and enforce visibility, enabled state, identity, and stale-request checks.

Define bounded behavior for targets that are loading, absent, removed, disabled, or superseded. Keep a genuine reading viewport for read-only content; do not make arbitrary text focusable as a shortcut.

**Acceptance:** Search can reach ordinary, decorated, responsive, and virtualized targets without widget-side tree rewriting. Missing targets have an explicit fallback and cannot leave a request that unexpectedly steals focus later.

### F3 Navigation and modal return bookmarks

**Evidence:** [PoB gem return handling](../samples/PobCompanion/PobWidget.GemOptions.cs), [PoB skill navigation state](../samples/PobCompanion/PobWidget.SkillManagement.cs), and Playnite's `ModalReturn` handling in [Home](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.Home.cs) and [Browse](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.Browse.cs).

Extend navigator/modal composition to capture an opening action's return target, including collection occurrence identity. Integrate existing host focus and viewport memory. Migrate PoB's `detailKind` routing incrementally rather than replacing every workflow at once.

Define restoration when an item is removed, filtered out, reordered, or belongs to a retired query. Preserve stable content across loading transitions. Distinguish restoring a viewport from revealing an off-screen target so these mechanisms do not fight each other.

**Acceptance:** Game details, gem options, passive inspection, and nested search return to the original valid control and preserve viewport position. Test B and visible Back controls, modal replacement, empty pages, deleted targets, and delayed loading. No extra list realization or retained native controls.

## Phase 3 Scoped loading and busy state

### F5 Resources keyed by page or item

**Evidence:** [Playnite details lifecycle](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.Details.cs), [PoB build comparisons](../samples/PobCompanion/PobWidget.BuildComparison.cs), [PoB skill options](../samples/PobCompanion/PobWidget.SkillOptions.cs), and [existing resources](../src/WidgetSdk/WidgetResource.cs).

Extend resources and operations with an explicit owner scope and data key. The scope should coordinate cancellation, cache identity, loading/error state, retry, and rejection of late publication. Avoid manually enumerating operation-name prefixes when a dialog closes.

Separate read publication from external side effects. In particular, PoB intentionally retires some stale reads without interrupting an already-admitted engine command. Preserve that policy instead of imposing blanket cancellation. Domain checks such as current build XML, document identity, and external file changes remain authored predicates.

**Acceptance:** Rapid game/build switching never publishes into the wrong page; providers that ignore cancellation cannot overwrite current state; hiding, reopening, retrying, and replacing a modal produce correct loading state; caches and operation registrations stay bounded.

### F10 Operation-owned busy presentation

**Evidence:** [PoB Work wrapper](../samples/PobCompanion/PobWidget.cs) and [operation admission](../src/WidgetSdk/WidgetOperations.cs).

`Work()` sets a separate busy flag before admission and clears it inside the callback. It ignores the returned handle. If the operation is rejected, that callback does not run. This is a source-level failure path requiring a targeted reproduction.

Prefer existing `IsBusy`, admission results, and operation events. Add a small presentation helper only if needed for status text and error mapping. Preserve single-flight behavior and distinguish joined work from newly started work.

**Acceptance:** Started, joined, rejected, canceled, and failed operations all settle presentation correctly. An inactive or capacity-rejected operation cannot leave the widget busy forever, and an older completion cannot clear a newer operation's status.

## Phase 4 Collections and suggestions

### F6 Loaded collection adapter

**Evidence:** [Playnite achievement rows](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.Achievements.cs), [PoB saved builds](../samples/PobCompanion/PobWidget.Collections.cs), [PoB passive groups](../samples/PobCompanion/PobWidget.PassiveGroups.cs), and [indexed collection APIs](../src/WidgetSdk/WidgetIndexedCollection.cs).

For already-loaded immutable keyed items, provide range reads, occurrence keys, and publication classification. Authors supply items, stable identity, rendering, and actions; the adapter decides whether content changed or membership/order changed. Keep explicit query replacement available for providers with different semantics.

**Acceptance:** Content-only changes preserve identity and focus; insertions, removals, reordering, duplicate occurrences, and explicit resets behave correctly. Retain virtualization and protocol continuity limits. Measure allocation and publication cost on large libraries; do not compute or render every row on every frame.

### F7 Scoped suggestion sources

**Evidence:** [PoB suggestion routing](../samples/PobCompanion/PobWidget.TextSuggestions.cs), [PoB alias matching](../samples/PobCompanion/PobSearchSuggestions.cs), [Playnite search](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.LibraryControls.cs), and [SDK suggestion catalog](../src/WidgetSdk/TextSuggestions.cs).

Bind a suggestion source to a field and owner scope. Let the SDK manage source identity, dispatch, retirement, and stale responses. Reuse the existing catalog; consider configurable aliases and matching rather than copying PoB's domain-specific text cleanup into the SDK.

**Acceptance:** Leaving a field or changing its source invalidates old replies; same-source rendering preserves identity; limits, cancellation, and fallback behavior remain intact. Keep secret-achievement policy, filter policy, and whether selection submits a query as explicit behavior rather than silently changing them during migration.

## Phase 5 Rich documents and disclosures

### F8 Bounded rich-document builder

**Evidence:** [PoB AddRuns](../samples/PobCompanion/PobWidget.RichText.cs) and [SDK rich-text element](../src/WidgetSdk/RichTextElement.cs).

Move Unicode-safe splitting, stable child IDs, run counts, and protocol budgeting into a reusable builder. PoB currently flushes at 2,400 characters or 128 spans, emitting a new paragraph each time. Keep PoB tone selection and upstream wording in the widget.

First define overflow semantics. If transport chunks must preserve continuous layout, budget a separate host/protocol change rather than disguising new paragraph breaks as equivalent output. Whole-frame and lazy-row budgets still apply even when individual paragraphs are valid.

**Acceptance:** Long text, surrogate pairs, links, many short runs, and multiple paragraphs preserve intended text and navigation. Large batches stay within protocol budgets without falling back to plain text or eagerly constructing all previews.

### F9 Disclosure component

**Evidence:** [PoB disclosure state](../samples/PobCompanion/PobDisclosureState.cs), [configuration disclosures](../samples/PobCompanion/PobWidget.Disclosures.cs), and [passive grouping](../samples/PobCompanion/PobWidget.PassiveGroups.cs).

Provide consistent expansion semantics, accessibility, indicator rendering, and focus recovery when collapsing focused descendants. Allow controlled state and optional bounded presentation memory. Keep passive grouping and context identity in PoB.

**Acceptance:** Pointer and controller toggles agree; collapse returns focus to the disclosure header; stale actions cannot toggle another context; recalculation preserves the intended expansion state; children remain lazy and absent from navigation when collapsed.

## Additional audit of YouTube Music Spotify and Settings

### Existing findings that apply to these widgets

| Area | Current implementation and risk | Reuse from the plan |
| --- | --- | --- |
| YouTube Music focus and return | `_focusSequence`, `_queueRevealSequence`, `_panelReturnFocus`, and a manual `(kind, value, focus)` history stack coordinate navigation. History stores `item.<index>` rather than a logical occurrence; a refreshed or reordered page can make the index refer to another item. Setup also captures a general focused-element ID while `EnterContent` only interprets the special indexed form. These are source-level restoration risks to reproduce before migration. | F1 and F3, with a distinct scroll-only request API; do not merge independent host request streams accidentally. |
| YouTube Music page loading | `LoadPage` maintains generation, cache slots, loading flags, current query, and late-response checks around `RunLatest`. | F5. Keep bounded cached section slots and the intentionally reusable detail slot. |
| YouTube Music queue identity | `ReplaceCollection` matches reference-identical songs through queues of occurrence keys, then chooses content update or query replacement. Duplicate songs must stay separate. | F6, with an explicit occurrence-identity policy. Never substitute song ID alone. |
| Both music search fields | Catalog/source-key rotation, field and route checks, account checks, and local fallback are manually assembled. | F7. Keep provider ordering and account isolation; the host already implements request debounce and stale-response handling. |
| Spotify focus requests | Navigation revisions are repurposed as focus request IDs in `Render` and `SetupCollectionEntry`. This is coupling to remove, not evidence of the same collision already reproduced in Playnite. | F1 and F3. Retain the existing `WidgetNavigator` rather than replacing it. |
| Spotify queue projection | `SpotifyIndexedQueue.Prepare` freezes items and saved state, compares key sequences, and chooses `PublishQuery` or `UpdateContent`. | F6. Avoid repeated copying/dictionary construction when inputs are unchanged. Measure before claiming a performance improvement. |
| Settings page navigation | `ObservePageNavigation` infers Forward/Back while rendering by walking a parent-page policy. The widget separately handles intent loading destinations, theme return focus, and an installed-list entry counter. | F1, F3, and F5. Record navigation at the action boundary and preserve the accepted resize-only Settings page behavior. |
| Settings installed list | It stores return keys and entry revisions, scans rows for the selected package, and classifies membership changes manually. | F3 and F6. Preserve built-in versus installed-copy identity and behavior when a package disappears. |
| Settings release notes | `ReleaseNoteElements` manually splits text at protocol limits, protecting surrogate pairs and Windows newlines. | F8, including a plain-text path through the shared document builder. |
| Settings expansion state | App-update notes and diagnostics use custom expanded-state handling. | F9 where a disclosure fits; do not impose it on unrelated navigation. |

**Source anchors:** [YouTube Music navigation and collections](../samples/YtMusicWidget/Standalone/StandaloneMusicWidget.cs), [YouTube Music suggestions](../samples/YtMusicWidget/Standalone/StandaloneMusicWidget.Suggestions.cs), [Spotify suggestions](../samples/SpotifyWidget/SpotifyWidget.Suggestions.cs), [Spotify queue projection](../samples/SpotifyWidget/SpotifyIndexedQueue.cs), [Settings page navigation](../src/FirstPartyWidgets/SettingsWidget/SettingsWidget.PageNavigation.cs), [Settings installed list](../src/FirstPartyWidgets/SettingsWidget/SettingsWidget.InstalledList.cs), and [Settings update notes](../src/FirstPartyWidgets/SettingsWidget/SettingsWidget.AppUpdates.cs).

### F11 Structured Settings forms

**Evidence:** [Settings layout rewriting](../src/FirstPartyWidgets/SettingsWidget/SettingsPresentation.Layout.cs) and [existing SettingsField](../src/WidgetSdk/SettingsField.cs).

`ArrangeSettingsRows` infers semantics from presentation: a switch is recognized by its CSS class, its label is split at a double space, another button is split at `: `, and a stepper's first child is assumed to be its label. A following `page-help` text node becomes the description. A second traversal builds Up/Down links using a manually enumerated set of focusable element types.

Declare label, description, control, and accessibility name explicitly with `UI.SettingsField` at each call site. Extend form composition only where needed to preserve row/column navigation. Do not make the host parse labels or style classes to recover field semantics. Separate values from labels before localization, and avoid exposing SDK child ordering as an authoring contract.

The direct field migration is approximately 2/5; reusable form navigation and coverage raise the complete finding to 3/5. If runtime layout-aware navigation requires new host behavior, scope that separately rather than expanding the migration silently.

**Acceptance:** Changing wording, punctuation, spacing, or localization cannot change layout semantics. Steppers preserve their intended column on vertical moves. Selects, switches, text entries, sliders, action rows, and disabled/hidden fields work at narrow and wide layouts, increased text size, and high contrast. Visible labels and accessibility names remain correct. Remove the parsing/tree-rewriting pass after migration.

**Precedent:** WinUI Community Toolkit [SettingsCard](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/settingscontrols/settingscard) exposes Header, Description, and Content separately. Its presentation pattern supports this direction; it does not supply WidgetRail's controller navigation policy.

### F12 Resource demand shared by pages and pins

**Evidence:** Spotify's `OnPinnedLayoutSelectionChangedAsync`, `ReleaseUpNextPinnedLayoutDemand`, `EnsureDemandedQueueLoaded`, and `InvalidateQueueCollection` in [SpotifyWidget](../samples/SpotifyWidget/SpotifyWidget.cs); [PinnedLayoutHandle](../src/WidgetSdk/PinnedLayoutHandle.cs).

Spotify already fixes queue loading by explicitly combining the main Queue route with the selected Up Next pin. It registers selection cancellation, loads when either consumer needs data, and resets only when the other consumer does not need it. The lifecycle signals exist; resource composition remains manual.

Extend F5 with explicit demand leases or bindings for routes and selected pinned layouts. The resource should coalesce loading, stay demanded while any authorized consumer remains, and apply a declared cache/retirement policy after the last consumer leaves. Linking consumer tokens directly would be wrong: cancellation of one consumer must not cancel data still needed by another. Keep widget activation/teardown authority separate from pin selection.

**Acceptance:** Up Next loads without opening the main Queue page; switching main tabs does not interrupt it; unpinning while Queue is open does not reset it; replacing pins retires old demand; repeated selections cannot accumulate registrations; account changes retire obsolete data. Local playback continues under its existing lifetime even when presentation demand ends.

**Precedent:** TanStack Query's shared query observers and [enabled/subscribed queries](https://tanstack.com/query/latest/docs/framework/react/reference/useQuery) separate consumers from cached data. WidgetRail needs explicit integration with its own pin-selection and lifecycle contracts.

### F13 Adaptive polling and readiness composition

**Evidence:** `RunAdaptivePollingAsync` and `_pollWake` in [SpotifyWidget](../samples/SpotifyWidget/SpotifyWidget.cs), `PollSettingsStatusAsync` in [SettingsWidget](../src/FirstPartyWidgets/SettingsWidget/SettingsWidget.cs), and [WidgetTicker](../src/WidgetSdk/WidgetTicker.cs).

Spotify correctly uses the existing ticker for progress display, but maintains its own wakeable polling loop for provider state. That loop handles changing intervals, immediate reads, activation generation, and shutdown. Settings separately tracks an initialization task and a one-second status loop, checking the current page to choose reads and sharing a gate with mutations.

Use the existing fixed ticker where it fits. Consider an adaptive extension with one non-overlapping callback, a configurable next delay, coalesced wake requests, a TimeProvider, and an explicit owner scope. Keep Spotify's rate-limit delays, playback settlement rules, and error classification in Spotify. Use F5 for Settings page-specific read ownership; preserve serialization of settings writes and package operations.

**Acceptance:** Wake storms never create concurrent polling loops; deactivation cancels and drains work; page changes retire obsolete reads; callback failures are observed; retry delays are honored. Optional status work must not queue pure navigation behind long provider reads. Test with a fake clock and stalled providers. Do not retry mutations through a polling abstraction.

**Precedent:** TanStack Query exposes configurable `refetchInterval` and lifecycle-related refetch policies. The SDK's existing ticker already supplies fixed-interval lifecycle ownership; the gap is adaptive scheduling and composition, not a missing timer.

### F14 Coalesced notification publisher

**Evidence:** [Spotify notifications](../samples/SpotifyWidget/SpotifyWidget.Notifications.cs), [YouTube Music notifications](../samples/YtMusicWidget/Standalone/StandaloneMusicWidget.Notifications.cs), and [notification service](../src/WidgetSdk/Notifications.cs).

Both music widgets independently maintain `pendingNotification`, `notificationNeedsShow`, and `notificationRevision`, delay for 250 ms, choose Show versus Update, and clear the show flag only after an accepted matching revision. This is nearly identical delivery bookkeeping. The SDK currently exposes the individual commands but not this coordination.

Provide an optional bounded publisher per notification key/category with a configurable debounce, latest-content replacement, and explicit new-event versus update-only intent. Widget policies still decide what counts as a track change, whether startup should notify, and what text/artwork to show. Host preferences and quiet periods remain authoritative.

**Acceptance:** Rapid skips coalesce; metadata changes update without a second banner; an older receipt cannot clear a newer pending event; update-only never revives a dismissed/expired notification. Rejection or unavailable capabilities do not block playback or cause unbounded retries. Stop publisher work on its owning lifecycle, with background music behavior retained.

**Precedent:** ReactiveX [Debounce](https://reactivex.io/documentation/operators/debounce.html) supplies the timing/coalescing pattern. WidgetRail's Show/Update distinction and receipt ordering require additional semantics; a delay alone is insufficient.

### F15 Reuse activity-report coalescing

**Evidence:** [YouTube Music activity](../samples/YtMusicWidget/Standalone/StandaloneMusicWidget.Activity.cs), Spotify's `ObservePlaybackEffects` in [notifications](../samples/SpotifyWidget/SpotifyWidget.Notifications.cs), and [WidgetActivityService](../src/WidgetSdk/Activity.cs).

Both widgets keep a desired-activity cache and run latest-operation wrappers around activity delivery. The SDK already aggregates sources, shares identical reports, coalesces pending changes, and records accepted state. This finding is primarily adoption/cleanup, not a new host feature.

Simplify the outer layer while still observing failures and avoiding unnecessary per-tick allocations. Preserve the existing distinction between cancellation of the caller's wait and delivery already submitted to the host. Evaluate an SDK-owned reporting binding only if existing calls cannot express the lifecycle cleanly.

**Acceptance:** Pause, mute, buffering, remote playback, local playback, and teardown produce correct activity; clearing one source preserves another. Failures remain observed and can recover on later reports. Do not remove the widget's policy for whether sound is actually audible or accidentally remove background reporting.

**Precedent:** Distinct-until-changed/coalesced state streams are common reactive patterns. WidgetRail already implements the relevant transport coordination in its SDK.

### F16 Optimistic command adoption with provider-aware reconciliation

**Evidence:** `ExecuteAsync`, `RestoreOptimistic`, and `AdmitPlaybackObservationLocked` in [SpotifyWidget](../samples/SpotifyWidget/SpotifyWidget.cs); [WidgetOptimisticCommand](../src/WidgetSdk/WidgetOptimisticCommand.cs).

Spotify manually coordinates optimistic projection, pending operation identity, failure rollback, and later authoritative observations. The SDK already offers Apply, Execute, Reconcile, Rollback, and Fail callbacks around lifecycle-owned operations. Evaluate adopting it for the generic command portion, beginning with a bounded control rather than rewriting the playback coordinator wholesale.

The full path is not a mechanical replacement. Spotify commands can return acknowledgment without playback state, and a read started before that acknowledgment can return old values. Preserve its observation boundary and field-specific merge rules. The SDK helper currently does not establish that external observations confirm the mutation; a reusable observation reconciliation extension would be a separate 4/5 design effort. Basic command migration is approximately 3/5 because Spotify's mutable state must be integrated carefully with the SDK model.

**Acceptance:** Slow or failed controls roll back only owned fields; concurrent track, device, and position updates survive; old reads cannot briefly undo an accepted control. Account changes, local/remote playback transitions, cancellation after an external side effect, and repeated inputs remain correct. No automatic replay of playback mutations.

**Precedent:** TanStack Query [optimistic updates](https://tanstack.com/query/latest/docs/framework/react/guides/optimistic-updates) separates optimistic state, rollback, and authoritative refresh. Provider confirmation rules still require application policy.

### Integration order for the additional findings

1. Include music/Settings focus and navigation cases in F1/F3; do F11 early because the current label-based layout is fragile and the basic field primitive already exists.
2. Extend F5 with F12 consumer demand, then evaluate F13 polling without changing playback or mutation semantics.
3. Include all three widgets in F6/F7/F8 migrations instead of creating widget-specific adapters.
4. Add F14 where shared notification behavior is covered by tests; treat F15 as a smaller cleanup.
5. Evaluate F16 last and migrate selectively. Do not remove provider-specific observation safeguards merely to reduce code size.

### Existing behavior to preserve

- Spotify already uses `WidgetNavigator`, discovered collections, a cursor resource for Queue, timed mutations, and the SDK progress ticker. Preserve discovered-prefix paging and provider occurrence identity; do not invent a complete playlist size or eagerly fetch all pages.
- YouTube Music already uses indexed collections, `RunLatest`/`RunSingleFlight`, `TextSuggestionCatalog`, host WebSessions, private state, notifications, and activity APIs. Its account epochs, selected-track generations, stream authorization, recovery rules, and duplicate queue occurrences are domain/lifecycle safeguards.
- The host [TextSuggestionController](../src/OverlayFrontend.WinUI/Presentation/TextSuggestionController.cs) already owns keyboard-query debounce, throttling, request cancellation, timeout presentation, and stale selection checks. F7 should not add a second competing debounce loop. Remote-provider timeouts and account validation remain useful boundaries.
- Settings already uses `WidgetTimedMutation` for toast expiry. Its extra generation check guards publication under the widget lock; do not delete it merely because the timer also has a generation. A callback can already be executing when a newer status is published. A toast abstraction is optional lower-priority work, not a missing timer feature.
- Keep Settings permission, uninstall, package-update review, startup registration, theme mutation, and diagnostics-export rules within their existing authority boundaries. The bounded readiness retry classifies service/catalog failures and explicitly excludes mutation replay; do not replace it with an indiscriminate retry helper.

### Additional verification matrix

| Widget | Required scenarios |
| --- | --- |
| YouTube Music | Nested album/artist/playlist Back after reorder; Setup opened from different controls; queue duplicates and removal of the playing occurrence; refresh while navigating; account switch during remote suggestions; provider timeout with local fallback; playback and notifications while overlay is hidden |
| Spotify | Up Next pinned while main widget is on another tab; pin replacement/unpin while Queue remains open; canceled or delayed queue reads; rapid search/playlist switching; optimistic controls with stale and failed observations; remote/local device transitions; rate-limit backoff and poll wakes; metadata-only notification updates |
| Settings | Label punctuation/localization changes; stepper/select/text/slider row navigation after responsive reflow; installed-package removal while details is open; Back from permission destinations entered from multiple parents; loading intent destination cancellation; display change during Appearance; long release notes with surrogate pairs and CRLF; status polling concurrent with writes; replaced toast while previous expiry callback is running |

Use the shipping YouTube Music tests under `tests/YtMusicStandalone.Tests`, Spotify tests under `tests/SpotifyWidget.Tests`, Settings tests under `tests/SettingsWidget.Tests`, and relevant SDK/host tests. Legacy companion tests do not substitute for shipping-widget coverage. Existing test cases are useful starting points, not evidence that the proposed changes pass.

## Lower priority follow-ups

- Consolidate Playnite's initial-focus fallback into an SDK resolver, avoiding repeated protocol-tree conversion. The host remains responsible for actual visibility and layout.
- Evaluate adopting existing navigation components for PoB tabs and existing private-state facilities for small UI preferences. Do not migrate transactional build storage into generic presentation preferences.

## Keep domain logic in the widgets

Retain PoB engine serialization, semantic build revisions, comparison validation, undo, transactional saves, and preview content selection. Retain Playnite Bridge compatibility, installation discovery, launch confirmation, provider-specific artwork lookup, and achievement privacy rules.

Lazy collections, deferred previews, host presentation memory, and file-backed artwork already exist. This plan improves their authoring interface and integration; it does not replace them or claim they are missing.

## Framework precedents

These are design precedents, not drop-in implementations of WidgetRail's cross-process protocol.

| Area | Reference | Relevant behavior |
| --- | --- | --- |
| Focus | [Flutter focus](https://docs.flutter.dev/ui/interactivity/focus) | Focus nodes, scopes, and requests without author-allocated transport counters |
| Navigation | [Flutter Navigator](https://api.flutter.dev/flutter/widgets/Navigator-class.html) | Route stack and restorable navigation; exact indexed-item return needs additional design |
| Resource identity | [TanStack Query keys](https://tanstack.com/query/latest/docs/framework/react/guides/query-keys) | Explicit data dependencies and separate cache identities |
| Item identity | [React list keys](https://react.dev/learn/rendering-lists) | Stable identity through insertion, removal, and reordering |
| Collection adapters | [WinUI IncrementalLoadingCollection](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/collections/incrementalloadingcollection) | Standard data-source adapter; not the same as random-access virtualization |
| Suggestions | [WinUI AutoSuggestBox](https://learn.microsoft.com/en-us/windows/apps/design/controls/auto-suggest-box) | Integrated suggestion and submission events; application supplies eligibility and matching |
| Rich documents | [WinUI RichTextBlock](https://learn.microsoft.com/en-us/windows/apps/design/controls/rich-text-block) | Paragraph/run model and overflow support |
| Disclosures | [WinUI Expander](https://learn.microsoft.com/en-us/windows/apps/design/controls/expander) | Standard expansion state, events, and presentation |

## Delivery and verification

### Cross-widget applicability

Each shared change includes an applicability audit of the other shipping widgets
alongside implementation; a separate comprehensive audit of every widget is not
a prerequisite. Inspect both direct API consumers and equivalent manual patterns
in `samples` and `src/FirstPartyWidgets`, then check templates, SDK examples, and
tests. Confirm entry points so legacy fixtures are not mistaken for shipping code.

Maintain a compact consumer matrix for each finding: affected and migrated,
already compliant, not applicable, or deferred with a specific reason. Independent
consumer investigation can proceed in parallel with shared API work; migrations
depend on the agreed contract and should not race changes to the same files.
Preserve provider-specific semantics when deciding applicability. Do not claim a
framework-wide cleanup while affected consumers remain unexamined or silently
deferred.

The root [AGENTS.md](../AGENTS.md) makes this ownership review and consumer check
part of future widget/framework work, including tasks that do not explicitly
request an audit. It is agent guidance, not an automated static-analysis gate.

### Implementation and acceptance

For each phase, establish the current baseline and existing contracts, add focused behavioral coverage, implement the reusable capability, then migrate representative flows from the affected widgets. Include Playnite, PoB, YouTube Music, Spotify, and Settings where the applicability tables call for them. Remove superseded plumbing only after behavior is covered. Recheck the source findings before deleting code because implementation may have moved since the audit.

Run focused SDK/protocol/widget tests and native integration checks where focus, rendering, accessibility, or input behavior changes. Use WinUI guidance for host changes. Include light, dark, and high-contrast review for new controls. Native checks must cover keyboard, mouse, controller, loading transitions, and empty/error states.

For collection/resource changes, compare retained memory, allocations, rapid-scroll behavior, and reopen latency against the baseline. Preserve existing protocol limits and test stale inputs and late results explicitly. Keep unrelated Catalogue work and other local edits separate from implementation commits.

Completion requires the migrated widget code to express domain intent without the replaced bookkeeping, passing behavior-focused tests, and native evidence for affected interaction paths. A successful compile alone does not establish focus or viewport correctness.

## Implementation record

### First implementation batch

- F4: SDK Back now uses dispatch focus by default; explicit overrides and missing-metadata behavior are covered. Removed Playnite's input-correlation dictionary and lifecycle cleanup.
- Reproduction: the new SDK navigation test failed before the change (`detail.second` was not remembered), then passed. Focused Playnite navigation/achievement/model tests: 5 passed, 0 failed. Native verification remains pending with F1.
- F11 assessment narrowed: explicit field declarations are justified, but a new shared form-navigation abstraction is not yet justified. Preserve Settings' existing navigation pass initially; it also serves non-form pages and responsive controls.
- Worktree: `C:/Users/dwive/.codex/worktrees/sdk-authoring-improvements/WidgetRail`. Unrelated Catalogue changes remain in the primary checkout.

### Focus allocation consumer coverage

F1 now uses one widget-instance allocator shared with every SDK-created navigator.
`CreateFocusRequest()` captures an immutable intent once per event; its helpers
produce group entry, indexed/discovered collection entry, and scroll-only reveal.
The explicit-ID APIs remain compatible. Pending-intent IDs remain where they
protect supersession; they no longer allocate independent sequences.

| Consumer | Disposition |
| --- | --- |
| WidgetNavigator and Playnite | Shared allocation; Playnite's model counter removed |
| PoB | Shared allocation throughout; retain last-intent comparisons for search and website supersession |
| Spotify | Navigator IDs no longer rewritten; collection return IDs allocated at Back events |
| YouTube Music | Shared focus/reveal allocation; no change to queue reveal policy |
| Settings and Capture Library | Entry events allocate centrally |
| Games and Apps | Navigator request identity preserved; separate indexed selection intents allocate centrally |
| Marketplace | Search entry allocates centrally; unrelated Catalogue work excluded |
| Game Help, YouTube video, Full Application, SDK Gallery | Central allocation; YouTube pure state transitions receive explicit IDs |
| Templates and other shipping widgets | No matching direct allocation; navigators inherit the shared allocator |
| Legacy fixtures and raw protocol tests | Keep explicit IDs as intentional protocol test inputs |
| WidgetCursorResource navigation | Separate protocol stream; not changed |

Native focus policy: 22 checks passed, including explicit request → navigator →
explicit request interleaving, repeated-snapshot non-interference, deferred entry,
owner replacement, passive presentation, and scope containment. Screenshot inspected;
this is a focus fixture, not a themed product-layout acceptance run.

Managed coverage so far: SDK 169/169, Spotify 87/87, YouTube Music 42/42,
Settings 93/93, YouTube video 64/64, Games and Apps 94/94, Capture Library 27/27,
Game Help 48/48, Full Application 5/5, Marketplace 18/18. SDK Gallery and WinUI
validation build passed. Engine-backed PoB checks are recorded after completion.
Evidence is under `artifacts/authoring/` in this worktree.

### Reassessments from implementation inspection

- F12: preserve the existing small Spotify page-or-pin demand logic. A new generic
  lease graph is not justified solely to replace this tested composition. Revisit
  only if scoped resources and another real consumer make it simpler.
- F13: initially considered adopting the SDK ticker in Settings; subsequent cadence
  analysis rejected that substitution (see the final assessment below).
- F15: reject blanket removal. The widget cache suppresses operation scheduling;
  SDK coalescing suppresses transport. Removing the former adds work on playback ticks.
- F16: reject mechanical optimistic-command migration. The current SDK model
  abstraction does not replace Spotify's acknowledgment/observation boundary.
  Investigate the separate inferred whole-snapshot rollback risk with a reproduction.

PoB engine-backed F1 checks passed for both games: navigation restore 17/17 each;
unified search 170/170 (PoE1) and 164/164 (PoE2); skill management 73/73 and 74/74.
An initial runner invocation lacked required arguments, and a subsequent probe
used a PoE2 fixture for PoE1; neither reached the relevant tests. Corrected inputs
produced the passing results above. The final rebuild includes the pending-intent
field rename; that rename changes no request behavior.

### Exact focus targets and operation admission

- F2: protocol 99 adds a validated exact descendant target on focus entry. The
  SDK exposes `WidgetFocusRequest.FocusElement`. The host consumes unavailable
  targets without replaying them when later enabled. PoB no longer rewrites its
  completed tree to insert focus groups; read-only configuration reading
  viewports are authored at the field declaration.
- SDK suite: 169/169. PoB unified search: 170/170 and 164/164. Native focus-policy
  fixture: 27/27, including exact-versus-remembered focus, refresh, and disabled
  target behavior. Protocol/native generated-contract parity verified.
- Broader PoB layout fixture has an existing constrained-layout visibility failure
  after 107 successful checks. Reconstructing the former focus wrapper against
  the same snapshots reproduces the same failure at the same check. Evidence:
  `pob-exact/native-result.json` and `pob-legacy-wrapper/native-result.json` under
  `artifacts/authoring`. This is not claimed as a fully passing layout sweep or
  fixed by the new focus API; the separate baseline layout limitation remains.
- F10: reproduced capacity rejection leaving PoB busy. Busy presentation now
  begins inside admitted execution, so rejected/joined work cannot orphan it.
  The reproduction fails before and passes after the change; it also verifies
  joined work does not execute twice and a later request succeeds.

### Structured Settings fields

F11 implemented in narrowed form: Appearance, Accessibility, General, Controllers,
and extension fields now declare labels/descriptions explicitly. Removed label
punctuation parsing, stepper child extraction, and next-sibling help inference.
The existing navigation pass remains, renamed to describe its actual purpose.
Additive Switch/Stepper overloads hide only the visual label while retaining
accessible names, values, states, and existing signatures.

Settings tests: 94/94; SDK compatibility: 17/17. Native form fixture: 290 checks
passed at wide/narrow sizes and normal/enlarged text, including stepper column
navigation. Screenshot inspected. The initial broader fixture encountered an
unrelated installed-list snapshot requiring a live presentation session; the
passing form run uses the seven intended Settings page fixtures, not that list.
Extension declarations are covered by managed validation; no claim of a full
native extension-manager integration run is made.

Further F13 assessment: the existing SDK ticker uses fixed-rate PeriodicTimer,
whereas Settings waits a full second after each completed poll. Substituting it
would change cadence after slow reads. Retain the current loop rather than add
a new timing abstraction solely to remove a few scheduler lines. Both Settings'
and Spotify's existing poll scheduling are retained under the decision gate.

### Coalesced notification delivery

F14 implemented as one SDK publisher per stable notification key/category, using
existing latest-operation scheduling. Spotify and shipping YouTube Music retain
their domain event policies and publish under their existing policy locks. The
shared publisher owns debounce, pending content, show-versus-update intent, and
receipt revision matching. Updates do not resurrect dismissed notifications;
failures retry only on a later explicit publication, with no background retry loop.

SDK: 170/170; Spotify: 87/87; YouTube Music: 42/42. Deterministic tests cover
coalescing, initial update-only delivery, noncooperative old receipts, failures,
invalid keys, and destruction. Logs: `artifacts/authoring/f14-*-full.log`.
Other notification consumers send discrete events and have no equivalent
coalescing state; no migration is needed. Activity caches remain (F15).

### Spotify failed-command observations

F16's mechanical SDK migration remains rejected: provider acknowledgment and
observation sequencing are domain policy. A separate regression was reproduced:
a held command, newer provider metadata, and a delayed rejection restored the old
track title. Spotify now retains the latest authoritative rollback value during
the pending command, including local transport observations, and clears it when
the command settles. Exact operation ownership remains enforced.

The reproduction failed before the fix. Spotify's full suite passes 88/88;
coverage includes all five optimistic controls, both unchanged and replaced track
identities, cancellation, stale terminals, and pre-response observations.
Evidence: `artifacts/authoring/f16-repro.log`, `f16-fixed.log`, `f16-full.log`.

### Exact focus admission race

A new native regression applied a disabled-target request and an enabled-target
refresh before dispatcher focus restoration. This failed against the initial F2
implementation: the unavailable request replayed into the enabled frame. Admission
now checks declared enabled state and current visibility before retaining the
exact identity; dispatch still rechecks current identity/scope/availability.

Native focus policy passes 28/28, including the new coalesced-publication case.
Build is warning-free; screenshot inspected. Evidence: `f2-coalesced-repro` and
`f2-coalesced-fixed` under `artifacts/authoring`. Only owned fixture processes
were closed. No shipping app or installed widget was replaced.

### Bounded text chunking

F8 is narrowed to a pure shared UTF-16 chunk utility, used by PoB formatted runs
and Settings release notes. It preserves exact concatenation, surrogate pairs,
and CRLF, with newline/optional word-boundary preference. PoB retains its 2,400
character/128-span paragraph budgets, semantic tones, IDs, and lazy row ownership.
Settings retains its 4,096-character text-node limit. Whole-frame budgeting and
continuous layout across separate paragraphs remain separate protocol concerns.

SDK chunk boundary checks pass across small budgets and Unicode/newline cases;
Settings 94/94 and PoB formatted reading 20 checks pass, including authored tone
preservation and a CRLF at a partially full styled paragraph boundary.
Evidence: `artifacts/authoring/f8-*.log`. Other widgets with truncation policies
are not migrated: truncation is different from lossless document splitting.

### Indexed navigation returns

F3 implemented in narrowed form: an opt-in captured-action push retains only
collection occurrence identity on each navigator frame. Back allocates its intent
from the shared widget sequence. Snapshot resolution checks current collection,
source, query, and index without retaining queries/items/native controls. No new
viewport owner or implicit cache policy is introduced.

Full Application's three return bookkeeping fields and Spotify's six playlist/
setup return fields were removed. Existing Spotify source-to-route selection
remains domain policy. Full Application 5/5, Spotify 88/88, SDK 171/171 pass.
Native logical-focus fixture passes 22 checks through the real bridge/worker;
its exact-entry request now comes from navigator Push/Back. The fixture tests
request delivery, one-shot behavior, stale/empty queries, focus ownership, and
content refresh, not a themed product-layout review. Screenshot was inspected
(the deliberately minimal fixture lacks product styling).

PoB's semantic destination and passive-expansion recovery, Playnite's modal
anchor/captured-action targets, Settings package-selection recovery, and YTM's
provider/cache history remain local. They are not mechanically equivalent to an
indexed route return, and the new API is not a replacement modal framework.
Evidence: `artifacts/authoring/f3-*.log`, `f3-native/result.json`.

### Operation admission and SDK lifecycle follow-up

F10 consumer checks found the same pre-admission loading hazard in Playnite details
and shipping YouTube Music page reads. Capacity-exhaustion regressions fail before
and pass after handling rejected admission, settling their loading/error state,
and permitting retry. Playnite covers overview, statuses, achievements, activity,
and installation operation busy state. Spotify starts its page loading state
inside admitted execution; Settings intent navigation already handles rejection;
PoB skill-option reads already retire rejected requests.

Playnite destruction also reproduced an SDK-ownership error: OnDestroying tried
to publish an empty indexed query after WidgetLifetime had been canceled, throwing
before application disposal. Removed the redundant query retirement; SDK already
retires leases and drains lifetime operations. Added active/background destruction
coverage proving service disposal. The initial capacity test's destruction threw
and masked its assertion; a second pre-fix run using Background exposed the exact
stuck-loading assertion. Final tests restore full destruction cleanup.

Playnite 216/216; YouTube Music 43/43. Logs: `playnite-final.log`,
`f10-playnite-repro.log`, `f10-playnite-fixed.log`, and `f10-ytm-*.log`.

### Resource reassessment and reset/reload correction (first pass; superseded below)

F5 is narrowed to correcting the existing resource facility, rather than adding
a second state owner to the audited widgets. A new noncooperative-provider test
reproduced `Reset(); EnsureLoaded()` joining canceled underlying SingleFlight
work: only the original read ran. WidgetResource already coalesces duplicates in
its own current-request slot; underlying admission now uses RunLatest so a new
resource owner queues behind the draining old provider. Multiple resets retain
only the latest pending read, with no overlapping provider calls or stale publish.
SDK 171/171 and Playnite 216/216 pass. Existing shipping consumers are Playnite
hidden rows and connection status; other audited resources use distinct cursor,
discovered-source, or domain operation contracts. Evidence: `f5-resource-repro.log`,
`f5-resource-fixed.log` (the later F5 pilot provides current Playnite evidence).

A general keyed/scope resource remains deferred under the decision gate:
- Playnite's details model atomically carries the selected game, full metadata,
  completion mutation confirmations, tabs and suspended presentation. Adding a
  separately observable resource snapshot while retaining those fields would
  create two owners and still need the existing opening/generation fence. A clean
  conversion would redesign that model, not remove a few cancellation lines.
- PoB skill reads retain one-build/32-group caches and intentionally let admitted
  engine commands finish when selection changes. A normal cancel-on-key-change
  resource would weaken those semantics.
- YTM retains typed section caches plus a reusable detail slot, with account
  invalidation and a live queue. Spotify's queue is a cursor resource; neither is
  interchangeable with one ordinary key/value cache.
No new keyed-resource API or automatic scope/caching policy is shipped. The
concrete admission, teardown, and resource-reset defects are fixed through
existing framework ownership. This is an intentional narrower outcome, not a
claim that the proposed general keyed abstraction was implemented.

### Remaining proposals: corrected ownership decisions

- **F6 — retain current loaded-list publications; generalized adapter deferred.**
  Settings' publication gate compares domain inputs before building rows, then
  uses a small ordered-key comparison. PoB set collections must retire identical
  keys when `managedSetKind` changes; passives additionally distinguish filter,
  build context and expansion. Spotify queue content includes saved-song state;
  YTM uses occurrence keys so duplicate songs remain distinct. An adapter that
  inferred query identity from keys would be wrong. An explicit-context adapter
  would still need all these input/equality/publication decisions and additional
  callback plumbing. A four-line range slicer is not enough benefit to introduce
  another collection owner. Existing lazy sources and continuity contracts remain.
- **F7 — reject a competing suggestion registry.** The original audit overstated
  the missing framework work. BridgeClientRegistry.TextSuggestions validates
  field/source/scope/runtime/lifecycle ownership both before and after a callback;
  WidgetTextSuggestionLane bounds noncooperative callbacks and cancellation;
  TextSuggestionController owns debounce, throttling and stale selection. Remaining
  widget predicates protect domain state that can change before the next published
  snapshot, such as account/build/achievement identity. They remain necessary.
  Alias matching is not added without two consumers requiring the same semantics.
- **F9 — defer a general disclosure control.** A button helper would leave the
  difficult ownership with authors. A real control needs protocol/UIA expansion
  semantics, lazy child inclusion and collapse focus recovery. PoB's contextual
  expansion cache and flattened virtualized passive groups differ from Settings'
  local release-note/diagnostic booleans. No new competing expansion store is added.
  This remains a separately scoped control feature, not a completed implementation.

These decisions supersede the initial proposed migrations. They follow the user's
instruction to retain working designs when the proposed abstraction adds coupling
or weakens behavior. No existing widget feature was intentionally removed.

### Final disposition matrix

| Finding | Outcome of this pass | Consumer scope |
| --- | --- | --- |
| F1 | Implemented shared focus allocation | All matching shipping widgets, SDK navigators and examples |
| F2 | Implemented exact target protocol/host/SDK and admission-race fix | PoB search and native fixture |
| F3 | Implemented narrow indexed navigation return intent | Spotify and Full Application; semantic/modal routing retained elsewhere |
| F4 | Implemented action-focused Back default | SDK and Playnite |
| F5 | Implemented single-current-query resource and Playnite read-section migration; validated below | Existing state machine, bounded scheduler, explicit caller scope; domain mutations retained |
| F6 | Implemented explicit-identity loaded-list adapter | Settings, Games & Apps, Playnite achievements, PoB loaded lists, Spotify queue, YTM snapshots; specialized sources retained |
| F7 | Corrected audit: shared ownership/lifetime facilities already exist | Preserve widget domain guards |
| F8 | Implemented shared lossless chunk boundaries; rejected automatic whole-frame budgeting | PoB and Settings retain domain run/layout policy |
| F9 | Implemented controlled disclosure with protocol/UIA and native collapse recovery | Settings, PoB configuration, Playnite setup, Game Help and SDK Gallery migrated |
| F10 | Fixed operation admission presentation | PoB, Playnite, YTM; Spotify/Settings existing admission paths retained |
| F11 | Implemented explicit Settings field declarations | Settings fields; existing navigation pass retained |
| F12 | Retained typed page-or-pin demand | Spotify; generic lease graph not justified |
| F13 | Retained provider polling | Settings fixed-delay and Spotify adaptive timing are not interchangeable |
| F14 | Implemented shared notification publisher | Spotify and YTM; other discrete-notification consumers unaffected |
| F15 | Retained activity caches | They suppress scheduling, while SDK coalescing suppresses transport |
| F16 | Retained provider reconciliation; fixed failed-command rollback | Spotify observations and exact command ownership |

This table is a progress record, not completion evidence. Later F5/F6/F9 sections
supersede the original deferrals. The final verification audit remains active.

### First-pass validation and limits (historical checkpoint)

The notification publisher's final review added another delayed-receipt test:
metadata arriving during an accepted Show must remain Update-only. It failed when
content revisions were treated as event revisions. The publisher now advances
its event revision only for a genuinely new event; an accepted receipt settles
that event even when its metadata changed, but never settles a newer event.
SDK 171/171, Spotify 88/88 and shipping YouTube Music 43/43 pass after this fix.

Final/relevant suite results for this pass:

| Scope | Result |
| --- | --- |
| SDK | 171/171 |
| Playnite | 216/216 |
| Spotify | 88/88 |
| Shipping YouTube Music | 43/43 |
| Settings | 94/94 |
| Full Application reference | 5/5 |
| SDK compatibility | 17/17 |
| PoB formatted reading | 20 checks |
| Native exact/group focus | 28 checks |
| Native indexed return delivery | 22 checks |
| Native Settings forms | 290 checks (earlier batch) |
| Generated protocol/native contract | 184 constants and ownership/repeat rules match |
| Documentation contract | 231 Markdown files checked |

Representative exact managed commands (run from this worktree):

```powershell
dotnet run --project tests/WidgetSdk.Tests/WidgetSdk.Tests.csproj -c Release -p:UseSharedCompilation=false '-bl:artifacts/authoring/final-sdk-{}.binlog'
dotnet run --project tests/SpotifyWidget.Tests/SpotifyWidget.Tests.csproj -c Release -p:UseSharedCompilation=false '-bl:artifacts/authoring/final-spotify-{}.binlog'
dotnet run --project tests/YtMusicStandalone.Tests/YtMusicStandalone.Tests.csproj -c Release -p:UseSharedCompilation=false '-bl:artifacts/authoring/final-ytm-{}.binlog'
dotnet test --project tests/PlayniteLibraryWidget.Tests/PlayniteLibraryWidget.Tests.csproj -c Release -p:UseSharedCompilation=false '-bl:artifacts/authoring/f5-playnite-{}.binlog'
dotnet test --project tests/WidgetSdk.Compatibility.Tests/WidgetSdk.Compatibility.Tests.csproj -c Release -p:UseSharedCompilation=false '-bl:artifacts/authoring/compat-final-{}.binlog'
```

Native fixture screenshots were inspected. Native evidence uses automated
controller-routing/keyboard/UI Automation fixtures, not a claim of a physical
controller acceptance pass. No new public UI control was introduced. No full
shipping installer or native C++ release rebuild was performed; the WinUI fixture
build reused the existing native DLL artifacts. No performance improvement is
claimed from these correctness/authoring changes.

The broader PoB constrained-layout failure remains reproduced with both the old
wrapper and exact-target implementation (107 checks precede it). It is not marked
green or silently fixed here. Earlier engine-backed PoB results are recorded above.

All owned validation processes were closed; the user's existing overlay instance
was not replaced. All changes are local commits on the isolated implementation
branch. The primary checkout's unrelated Catalogue and plan edits remain intact.


### Goal completion audit follow-up

The previous turn made implementation progress, but did not prove full goal
completion. F5/F6/F9 first-pass deferrals lack either a concrete external blocker
or enough representative evidence to reject the proposed bounded contracts. They
are reopened for focused evaluation. An explicit-key proposal must not be rejected
solely because implicit identity inference would be wrong. Existing implementation
and validation evidence above remains valid within its stated scope.


### Nested YouTube Music return identity

The completion audit identified a residual F3 risk that the retained-query test
had not covered. A new home → artist → album → Back fixture refetched the artist
page in a different order. It failed: the stored `item.<index>` selected another
album. History now retains the captured occurrence key plus provider kind/ID,
not an index or full item/page. A retained query resolves the exact occurrence;
a refetched detail resolves only a unique kind/ID match. Missing or ambiguous
matches leave ordinary collection entry in charge. Setup return uses captured
collection metadata instead of parsing a rendered row ID.

Shipping YTM suite: 44/44, including reordered, removed, and duplicate-identity
return cases and the existing retained deep-query/queue/pin cases. Evidence:
`f3-ytm-return-repro.log`, `f3-ytm-return-fixed.log`, `f3-ytm-return-final.log`.
No native targeting semantics changed; the result is the existing current-query
indexed focus request. End-to-end YTM nested-return native acceptance remains to
be evaluated with the remaining interaction evidence.

The completion audit also requires the following before closure:
- F5: a representative single-current-key resource implementation replacing its
  redundant data/status/error fields, or concrete evidence that this bounded
  design is worse. The reset/reload correction alone does not settle the proposal.
- F6: an explicit-query-identity loaded-list adapter pilot, with measured
  publication/allocation cost. Rejecting implicit key-based identity is not a
  rejection of this explicit-identity contract.
- F9: an ordinary controlled disclosure with explicit expand/collapse actions,
  lazy children, UIA state, and collapse focus recovery. Missing protocol support
  is work to implement, not an external blocker. Passive virtual groups can remain
  a different consumer.
- F2: hidden→visible same-request and coalesced-frame coverage for the public
  unavailable-target guarantee.
- Relevant comparative resource/collection allocation and restoration evidence;
  existing bounds/laziness assertions are useful but not a before/after measurement.

F7's redundant registry, F8's automatic whole-frame budgeting, F12's general lease
graph, F13's generalized polling machinery, F15's blanket cache removal, and F16's
mechanical model conversion can be rejected with the consumer-specific evidence
above. Their useful narrow improvements remain implemented; no external decision
is currently blocking the remaining work.


### F6 explicit-identity loaded-list implementation

The bounded prototype disproved the first-pass deferral. `WidgetLoadedCollection`
adapts complete immutable items to the existing indexed source, retaining its
range/lease/action/lifecycle authority. Authors provide semantic query identity,
captured immutable context/items, row rendering/actions and optional artwork.
The helper owns shallow list freezing, unique occurrence-key validation, range
slicing, unchanged/content/query classification and optional bounded continuity.
It does not infer account/build/filter identity or own a provider/cache/expansion
store. A force reset is explicit. Ordinary publication remains outside rendering
where the existing consumer already publishes there.

Identity replacement and force reset suppress old occurrence continuity, even
when new rows have identical keys. Continuity remains opt-in, with the existing
512-key wire bound and no cap on the actual list size. Key validation is shared;
the first probe exposed per-key string-enumerator allocation in StableIdentifier,
which was replaced with an equivalent allocation-free character loop. No validation
rule was removed.

| Consumer | Outcome |
| --- | --- |
| Settings installed widgets | Migrated; explicit filter identity, built-in/installed keys, package selection and permission logic retained |
| Playnite achievements | Migrated; explicit details-generation identity, secret/search/filter policy and artwork checks retained |
| PoB saved builds and managed sets | Migrated; set kind explicitly retires same-key queries; stale query/action guards retained |
| PoB calculation cells | Migrated; existing content-versus-membership behavior retained |
| PoB passive groups | Migrated loaded-list publication; grouping/expansion/build/filter policy and deferred previews remain domain-owned |
| PoB comparison/source/picker/ranking snapshots | Retain raw indexed sources where every engine/selection snapshot deliberately retires the query; a forced-reset adapter would add a frozen copy and key scan without removing classification logic |
| Playnite main library/Home | Retain provider-owned indexed range queries; not complete loaded lists |
| Spotify discovered pages | Not applicable: provider continuation ownership |
| Spotify queue and YTM complete section/queue snapshots | Migrated only range/publication plumbing; retain occurrence matching, saved/rating projection, account reset and typed demand |
| Games & Apps saved/running observations | Migrated complete observations; broker identity/icon authority, current-generation checks, row-state projection and provider-paged Catalog retained |
| Other shipping widgets/templates | No matching complete-list classification contract found in the applicability search; existing indexed/discovered APIs remain supported |

Managed tests cover 10,000 items with zero initial row rendering, one-row deep
reads, list freezing, unchanged publication, content identity, captured action
context, stale action retirement, explicit identity/reset, reordering, distinct
duplicate occurrences, invalid duplicate keys, empty lists, and collection of
retired publication contexts. SDK 172/172; Settings 94/94; Playnite 216/216;
SDK compatibility 17/17; documentation contract passes. Public API and reference
mapping updated.

A disposable publication probe compares the prior authored algorithm with the
adapter at 512 and 10,000 items. `f6-publication-probe-final.json` records timings
and per-thread allocated bytes. At 10,000 items, unchanged publication allocated
zero bytes in both; content updates were approximately 122 versus 117 microseconds
with about 80 KB allocated in both. Full reorders cost about 0.40 ms / 363 KB in the
adapter versus 0.002 ms / 80 KB in the old algorithm because the adapter validates
all occurrence keys. This is an explicit data-change cost, not per-scroll/per-frame
work; no rows were rendered by any publication. The adapter is not applied to the
main provider-backed game library or provider-discovered pages. Music queues retain
their domain-specific occurrence assignment and presentation-change gates. These
are microbench observations, not a universal performance or native-memory claim.
The weak-reference regression independently verifies retired contexts are released.

Native fixture `f6-native-final/results.json` passes six checks through the real
bridge/worker/presenter: deep exact entry, content update with stable focus/offset,
reordering with surviving-key focus, semantic replacement without old focus,
new exact entry, and removed-item fallback. It realizes 30-45 of 100 fixture rows,
not the full list. The first attempt used a toolbar Invoke that moved focus; the
second used high function keys that did not dispatch. Final inputs use the existing
low-function-key controller route; assertions were not weakened. Screenshot
inspected; the minimal fixture is unthemed and is not product visual acceptance.
Only fixture processes were closed; the user's PID13988 was left running.

PoB engine-backed checks: set operations 28/28 for both games; passive/row previews
59/59 (PoE1) and 62/62 (PoE2). PoE1 management has five checkpoint/history failures,
reproduced unchanged with the pre-adapter library/sets implementations. Detailed
calculation tests have the same formula-reading assertion failure (16 pass, 1 fail)
for both games with old and new calculation collection implementations. These
baseline failures remain explicit; no assertion was weakened or marked green.
Additional managed-set query/row checks pass for all four set kinds in both games:
final management results are 44 passed/5 baseline failures (PoE1) and 50/50 (PoE2).

Evidence: `artifacts/authoring/f6-*.log`, `f6-publication-probe*.json`, and
`f6-native-final/`. The probe can be rerun with the SDK test executable's
`--loaded-collection-probe <output.json>` option. Native fixture command:
`pwsh -NoProfile -File scripts/Test-WinUiLoadedCollection.ps1 -AppPid <fixture PID> -OutputDirectory <fresh evidence directory>`.


F6 consumer follow-up: the Spotify/YTM/Games & Apps complete observations also
benefit from the adapter without moving their domain logic. Spotify keeps its
saved-state/generation/observation-change gate, avoiding render-triggered publication;
YTM keeps reference-based duplicate matching and account reset; Games & Apps keeps
row-state/broker generation validation and its paged Catalog. The first Spotify
run caught an unnecessary force-reset on initial empty-source loading, which
suppressed the expected initial continuity keys. Removed that redundant caller
reset; the original regression now passes. No test was weakened.

Games & Apps 94/94 and YTM 44/44 pass after migration. The final SDK suite includes
context-retention and key-selector-cost checks: updating one of 10,000 immutable
items recomputes only that item's key. Keys for unchanged values are reused, which
also avoids rehashing opaque app identities. The additional key-reuse probe is
`f6-publication-probe-key-reuse.json`; its timings are diagnostic, not a timing gate.


F6 final consumer regression: Spotify 88/88 after removing the initial force reset;
YTM 44/44; Games & Apps 94/94; Playnite 216/216; Settings 94/94. SDK 172/172,
compatibility 17/17, and native loaded-list interaction 6/6. The remaining
keyed-resource/disclosure/hidden-target work stays open; this closes F6 only.


### F5 single-current-query resource and Playnite validation

The first-pass deferral above is superseded. The implementation demonstrates that
Playnite's readonly sections can use a resource without retaining duplicate
writable data/loading/error state in the model. `WidgetQueryResource<TQuery,TValue>`
wraps the existing `WidgetResource<TValue>` state machine; it owns only an immutable
selected query and its captured loader. `GetSnapshot(query)` guards mismatched
queries without taking a selection lock. Admission shares the existing resource
lock rather than adding another lock order. The same operation lane retains at
most one running and one latest pending read; an uncooperative provider must drain
before the successor begins. No provider, account, build, or permission identity
is inferred by the SDK.

The base resource now captures each loader at admission, registers caller-scope
cancellation before provider execution, supports cancel-with-last-good retention,
settles capacity rejection as a bounded safe error, and releases scope callbacks
without blocking on their execution. Error mapping runs outside the snapshot
lock. Snapshot presence is explicit so a successful zero is distinguishable from
an unloaded value type. Existing plain-resource entry points remain compatible.

Review found and corrected two prototype defects before commit: an independent
query lock could invert with cancellation and synchronous invalidation; registering
after execution could classify synchronous scope cancellation as provider failure.
Coverage now includes concurrent cancellation/render reads and synchronous
cancellation before the first await. A weak-reference check keeps the caller scope
alive while proving a completed/reset query is no longer retained by its callback.

Playnite achievements and activity now use `(SavedId, DetailsOpening)` keys and
navigation cancellation scopes. Rendering and action handlers compose their
resource snapshots with model-owned navigation and confirmed mutations. No second
writable copy of section values/loading/errors is maintained. Hide/resume keeps
completed data and retries interrupted reads; close resets read resources. Refresh
keeps last-good achievements, and preserves the previous policy of invalidating
activity even when another tab is selected. Overview, completion confirmation,
installation, and game authority remain in their existing domain owners.

Consumer applicability was checked across shipping samples, first-party widgets,
and examples, including equivalent manual patterns:

| Consumer | Disposition and concrete reason |
|---|---|
| Playnite achievements/activity | Migrated; ordinary lazy reads with one selected game/opening. |
| Playnite hidden rows/connection | Already use plain resources; no query changes to model. Existing API retained and regression-tested. |
| Playnite overview/status/install | Retain domain flow: confirmed completion mutates overview, status selection has bounded choice admission, installation is an external command. |
| PoB inline skill options | Reject mechanical replacement: `PobWidget.SkillOptions.cs` retains one-build/32-group cache and intentionally lets an admitted engine command finish after selection change. The new single-key cancel policy is different. |
| PoB build comparison | Retain explicit command transaction: `StartBuildComparison` is single-flight, re-reads committed XML to detect external changes, publishes under document/result authority, and reports command failure through feedback. A cached latest-query read would still require these owners and change error/admission behavior. |
| Spotify | Retain discovered/cursor resources, page-or-pin queue demand, provider observation and saved/rating projections. A single selected key cannot replace these policies. |
| Shipping YTM | Retain `LoadPage` typed section caches plus a shared nested detail slot, account reset, occurrence return identity, and live queue substitution. Wrapping the transport read would retain that state and add a second result owner. |
| Settings | Retain reads serialized with writes/package actions by `_operationGate`; initialization, polling and authority recovery publish related settings projections. Independent cancel-on-selection resources would not replace that transaction boundary. |
| Other samples/templates | Direct `CreateResource` consumers are only the existing Playnite reads. Remaining operations are commands, provider pagination, subscriptions or explicit lifecycle work; no equivalent single-current-key loading owner was identified for migration. |

Validation:
- SDK **173/173**, including ten scoped-resource scenarios (the executable reports
  the scenario suite as one test): latest A→B→C coalescing/no overlap, duplicate
  joining, scope replacement, cancellation retention, retry, inactive/capacity
  rejection, synchronous cancellation, concurrent snapshots, callback retirement,
  and value-type presence/non-retaining failure.
- Playnite **219/219**, including added same-game/different-game close/reopen with
  a noncooperative old read, failed-refresh retention/retry, and existing
  hide/resume, lazy loading, completion confirmation and secret-achievement tests.
- Public API baseline updated to **3866 symbols**; compatibility **17/17**.
  Documentation contract covers **231 Markdown files** after adding the new SDK
  file to the documentation ownership map.
- No host, protocol, installer, running instance, or physical input path changed
  in F5. Native focus/scroll work remains in F2/F9 validation.

The diagnostic comparison is reproducible through the SDK executable's
`--query-resource-probe <json>` option. `f5-query-resource-probe-final.json` measures
10,000 in-process calls after warmup, comparing a plain resource against the keyed
resource with a real caller scope. Snapshot reads and cache hits allocate **0 B**
in both. Cache hits measured approximately **0.031 µs plain / 0.069 µs keyed**;
refresh **0.92 / 1.17 µs**, **1128 / 1416 B**; replacement **0.99 / 1.16 µs**,
**1176 / 1592 B**. The extra per-read cost is query/scope ownership, not duplicated
provider data. These are diagnostic observations, not timing gates or claims about
native scrolling, process working set, provider IO, or user-visible latency.

Evidence under `artifacts/authoring`: `f5-sdk-verification.log`,
`f5-sdk-scopes.log`, `f5-playnite-final.log`, `f5-query-resource-probe-final.json`,
`f5-compat.log`, and `f5-docs-final.log`.
F9, hidden-target native coverage, and the final restoration/completion audit
remain active work; this closes the bounded F5 pilot and applicability decision.


### F2 hidden exact targets: native completion evidence

Extended the existing native `FocusPolicyValidationPage` and
`Test-WinUiFocusPolicy.ps1` with eight assertions for responsive hidden targets and
hidden ancestors. A request while hidden leaves the valid current focus alone;
revealing the target with that same request never steals focus; a fresh request
can enter it. Hidden→visible updates applied within one UI handler also pass,
proving that deferred layout/focus restoration cannot replay the consumed intent.
This is coalescing of admitted native publications, not a claim about an
unobserved frame discarded before host admission.

All **36/36 native checks passed**, including the original disabled/remembered,
exact/group, worker-replacement, passive-owner and request-withdrawal cases. No
production focus code needed alteration. The first run exposed the fixture's
pre-existing white-on-white labels in its screenshot. A standard Dark theme
background now makes the test surface readable; the complete rerun stayed green.
Inspected hidden, visible/focused, and final-state screenshots: labels and borders
are readable, the hidden control is absent, and no controls overlap or clip.
This minimal diagnostic page is not product-theme or physical-controller acceptance.

Evidence: `artifacts/authoring/f2-hidden-themed/results.json` and the three PNGs
alongside it, `f2-hidden-themed.log`, and `f2-hidden-themed-build.log` (zero warnings
or errors). Only verified fixture PIDs 29492 and 7952 were closed. User instance
13988 at the primary checkout's interactive-introduction installation was retained.
The hidden-target gap in the completion audit is resolved.

### F9 next implementation contract

Read-only source audit and the WinUI Gallery Expander sample confirm the bounded
approach: a controlled header with explicit authored expanded state and owned
content identity; lazy optional children; separate idempotent expand/collapse
actions; host UIA ExpandCollapse state and focus recovery when collapse removes
the focused descendant. Use ordinary admitted button actions, not a separate
privileged endpoint or host preference cache. A native Expander's independently
changing state must not outrun the asynchronous widget snapshot owner.

The host must capture focused ancestry before reconciliation removes controls,
recover only to the same retained header/scope, and preserve newer explicit focus
requests. Expansion, refresh, outside focus, inactive presentations and unrelated
replacement must not steal focus. Protocol validation must reject foreign,
self/ancestor, malformed or ambiguous content ownership, and UIA should raise a
state change only for an accepted new expanded value. The helper must not build
collapsed children.

Confirmed consumer scope: Settings release notes and diagnostics; PoB inactive
configuration (retain context/token safeguards, remove unconditional manual focus
request); SDK Gallery details; Game Help solution/sources; and Playnite setup
Advanced where composition preserves the action grid. PoB comparison context
reveals fields in two persistent cards from a toolbar header, so do not rearrange
that layout merely to fit a one-content-root component. Passive indexed groups
retain their different virtualized grouping contract.

Source seams: `WidgetViewPresenter.cs` reconciliation/control reuse,
`WidgetValueButton.cs` UIA peer precedent, `Elements.cs`, protocol node/update
contracts, and `PobWidget.Disclosures.cs`. Native acceptance must include UIA
idempotence and state changes, controller/pointer parity, disabled/busy admission,
child→header recovery, outside/refresh focus stability, nested collapse, stale
owners and explicit request priority. F9 remains implementation work, not an
external blocker. Final comparative restoration/completion review also remains.


### F9 controlled disclosure implementation and validation

Protocol **100** adds `WidgetDisclosure(ContentId, IsExpanded)` on ordinary
button actions. SDK `UI.Disclosure` composes a stable header and lazy optional body;
`UI.DisclosureHeader` supports existing layouts with a separate body root. The
wire validator requires unique content ownership in the same input scope and
presentation fragment; content exists exactly when expanded and cannot contain
its header. It rejects malformed/cross-fragment ownership and inline rich-text
headers. Diffing, atomic property updates, version inference and the generated
native constants include the new declaration. No host preference cache or
additional action endpoint was introduced.

`WidgetDisclosureButton` retains normal button authority, styles, leading icons,
and controller/pointer activation. Its UIA Expand/Collapse requests are idempotent
and use the current explicit action. Accepted snapshots drive chevrons and UIA
state-change events. The host captures old ancestry before removal and recovers to
the nearest surviving disclosure header only when the focused control was removed
or became unavailable. Surviving/reparented controls, outside focus, refreshes,
expansion and newer explicit focus requests keep their existing behavior. This
also works in an entered interactive preview and retains normal B-to-source.

Review identified and fixed prototype issues before commit: leading icons had
replaced the chevron; new content wrappers had inherited the wrong gaps; preview
fragments lacked recovery and allowed cross-fragment content references; and
collapse could override a control surviving outside the old body. The final tests
cover these cases. Wrappers retain their original gaps (Settings 8, PoB 4,
Playnite 14 logical pixels), without inheriting page padding/growth/surface styles.
The host's no-expanded-disclosure path uses a flag maintained during its existing
binding pass; it performs no extra ancestry scan. Disclosure validation allocates
its ownership set only when the feature is present. Optional children are never
constructed while collapsed. These are bounded source/behavior observations, not
a claim of measured process-memory or scrolling improvements.

| Consumer | Final disposition |
|---|---|
| Settings update notes / service diagnostics | Migrated, explicit set-true/set-false actions, original page state and operation guards retained. |
| PoB saved inactive configuration | Migrated; retained 24-context preference bound and stale-token/build guards; removed widget-generated collapse/expand focus request. Collapsed value summaries remain visible outside optional editing controls. |
| Playnite setup Advanced | Migrated without moving its action-grid header or changing setup/authentication authority. |
| Game Help solution / source lists | Migrated, preserving message identity, provider citations, attribution and local-only expansion. |
| SDK Gallery details | Migrated as a public example. |
| PoB comparison context | Narrowed exception: toolbar action reveals fields in two existing permanent cards. Combining those into one removable subtree would change the layout/summary contract. Retain the domain disclosure. |
| PoB passive virtual groups | Separate indexed-group contract retained; not ordinary optional content. |
| Spotify / YTM and other shipping widgets | No equivalent ordinary optional-content disclosure identified in the applicability audit. Navigation, dialogs and provider pagination are not migrated as disclosures. |

Validation:
- SDK **174/174**; focused disclosure contract also covers lazy creation, malformed
  references, version gating, nested trees, cross-fragment rejection, and atomic
  expand/collapse/metadata removal.
- Settings **94/94**, Playnite **219/219**, Game Help **48/48**, SDK Gallery
  **13/13**; explicit duplicate expand/collapse scenarios preserve state.
- PoB disclosure workflows **27/27 for each game**, including context retirement,
  cache bounds, repeated expansion, search revealing inactive controls, and
  unchanged build/XML. Real exported snapshots remain in `f9-pob-fixtures`.
- Native **33/33**, including ten built-in theme foreground checks plus high
  contrast. Real pointer activation and UIA property-state reads passed.
- A separate UIA client received **Collapsed, Expanded** events; repeated Expand
  raised no additional event. Its first probe expected enum strings, while WinUI
  delivers boxed numeric enum values; normalizing the standard UIA enum fixed the
  probe without changing product behavior or assertions.
- Compatibility **17/17**, Full Application **5/5**; SDK API baseline **3869**.
- Inspected final expanded/collapsed native screenshots: text, leading icon,
  chevrons and children remain visible without overlap or clipping. This is an
  isolated diagnostic surface, not acceptance of every product page/theme.

Evidence under `artifacts/authoring`: `f9-sdk-full.log`,
`f9-sdk-ownership.log`, `f9-settings-final.log`, `f9-playnite.log`,
`f9-gamehelp-final.log`, `f9-gallery.log`, `f9-pob1.log`, `f9-pob2.log`,
`f9-native-final-results.json`, `f9-uia-final-events.json`,
`f9-final-pointer-state.json`, `f9-final-expanded.png`,
`f9-final-collapsed.png`, `f9-compat.log`, and `f9-fullapp.log`.
All owned native fixtures were closed. No installed package, permissions, or
running production app was changed. The goal remains active for the final
requirement-by-requirement completion audit and remaining restoration evidence;
this section closes F9's implementation and representative validation.

### Completion audit: focus lookup and retained domain policies

The lower-priority Playnite focus lookup is now consolidated with
`WidgetFocusTargetLookup.Capture`. One decision serializes the authored tree once;
its requested/fallback/first queries share that protocol snapshot. Playnite's two
multi-query paths and Spotify's fallback chain use the helper. Their fallback
ordering and disabled-target policy are unchanged. PoB's single query keeps the
existing static API. No cross-render cache or native visibility inference is added.
SDK coverage checks one conversion, scope/disabled/missing equivalence, and a new
tree leaving the old snapshot unchanged. SDK **174/174**, Playnite **219/219**,
Spotify **88/88**, compatibility **17/17**; API baseline **3873 symbols**, four
additions and no removals. Logs: `audit-focus-snapshot-sdk.log`,
`audit-focus-playnite.log`, `audit-focus-spotify.log`, `audit-focus-compat.log`.

The remaining source-only cleanup proposals were reassessed:

- Settings `ObservePageNavigation` observes the displayed page and preserves its
  transition across repeated renders. The parent relation, including the permission
  page's dynamic return parent, is domain policy. Moving this to every assignment
  would track intermediate asynchronous destinations that may never be displayed
  and duplicate mutation-path hooks. Retain this narrow display-transition observer;
  it neither allocates focus requests nor owns host motion. Existing resize-only
  behavior for size-changing Settings pages remains intact.
- PoB's tabs use a proportional grid (Configuration gets a wider track), explicit
  stable target IDs, pending-edit disabling and a Notes focus wrapper. Replacing
  this with NavigationShell would change layout/IDs or require compensating wrappers.
  It already uses the shared TransitionContent motion declaration. Retain composition.
- PoB's website preference is shared across the two game widgets; the SDK private
  state boundary is per widget. Its navigation preference is a bounded 64-byte
  per-game file in the existing data directory, atomically written separately from
  build XML/history. Migrating only that value would require data migration and a
  second asynchronous state owner without removing the shared preference store.
  Retain these small stores, including malformed/oversized input handling and
  write-failure feedback. These are source-grounded ownership dispositions, not new
  physical acceptance claims.

### Completion audit: shipping YTM native restoration and performance

`Test-YtmAuthoring.ps1` runs the shipping StandaloneMusicWidget in a real worker,
bridge, and native presenter. Only IMusicService is synthetic. The fixture copies
the actual manifest/icons/styles and validates icon digests normally. No account,
network, playback, installed package, or existing process is changed. Controller
navigation reaches Home item 60, artist item 75, then enters the album. Native B
and the visible Settings Back button exercise the actual action paths.

Native reordered/removed/ambiguous scenarios confirm the correct surviving
identity (index 24 after reorder), safe initial entry when missing/ambiguous,
bounded native realization, unchanged Home query identity, and Home item 60 focus.
**Viewport assertions remain red**: Home's offset changes from about 2847 to
2920 logical pixels (one run settled near 2833). Settings Back also changes the
offset when it opens from that latter position. The failed assertions remain in
the fixture; no acceptance threshold was relaxed. Files:
`ytm-nav-{reordered,removed,ambiguous}-complete/result.json`.

The pre-loaded-adapter YTM source at `df4c6555`, rebuilt against the same SDK and
executed in the same native renderer, reproduces the 2847-to-2920 shift
(`audit-ytm-preloaded.json`). This isolates the F6 migration from the viewport
problem; it is not a comparison against a fully historical host binary. The host
currently remembers collection focus across removed pages but only restores
viewport mementos for lifecycle recreation/continuity. Exact entry requests do
not identify navigation-return intent separately from reveal. F3's original
viewport integration requirement therefore remains **open**, despite passing
focus-target tests. This is an internal implementation gap, not an external blocker.

The performance mode loads 10,000 synthetic artists, makes three six-second full
scroll passes, reopens the retained presentation three times, and samples memory.
It compares pre-F6 shipping source at `df4c6555` with current shipping source while
holding SDK/bridge/native binaries and provider data constant. Two quiet paired
runs (`ytm-performance-{before,after}-{2,3}`) passed; the earlier pair overlapped
compilation and is excluded from conclusions. Aggregate:
`ytm-performance-comparison.json`.

| Measurement | Before adapter | After adapter |
| --- | --- | --- |
| Maximum native rows realized | 34 | 34 |
| Per-pass composition interval p95 | 9.43–9.75 ms | 8.13–10.25 ms |
| Intervals over 50 ms across six passes | 1 | 1 |
| Retained-view reopen readiness | 12.48–24.15 ms | 13.55–33.83 ms |
| Frontend managed memory after diagnostic full GC | 33.32–36.68 MiB | 33.03–38.96 MiB |
| Frontend private memory after diagnostic full GC | 238.93–251.08 MiB | 220.59–253.09 MiB |
| Worker final private memory | 75.87–76.00 MiB | 75.71–75.95 MiB |
| Frontend allocated bytes during measured workload | 2751–2773 MiB | 2753–2896 MiB |

Ranges overlap; this does not establish a performance improvement. The timed
scroll loop can demand different numbers of rows as scheduling varies, so the
allocation totals are workload observations, not per-item costs. Native allocation
churn remains substantial in both versions. This fixture excludes artwork/network,
measures retained-view readiness rather than cold startup, and is not a claim
about game-library memory or physical controller polling. Full GC occurs only in
the isolated diagnostic fixture after timing, never in product code. Screenshots
show bounded, readable rows with ordinary clipped edge rows in the scroll viewport.

### F3 follow-up: native binding lifetime

The delayed-return fixture reproduced a retained hide/show stall: row 75's current
lease was available, but its disabled native container remained bound to a different,
evicted slot object at the same index. `IndexedItemsSource.Pump` had discarded the
slot while WinUI temporarily excluded its page from range demand, even though
`ContainerContentChanging` had not recycled the container. Refilling updated a
new slot rather than the native binding.

Keep slot identity while its internal `StateChanged` event has a native container
subscriber. Existing recycling/detachment removes those subscribers. Page leases
still retire when undemanded; keeping a slot does not request its page. Unobserved
slots are evicted on the next pump. The retained bound is demanded pages plus
currently bound containers, not total library size. A retired row payload may
remain in a bound slot, but its native container already retained that same value;
this does not extend provider action authority.

`IndexedSourceReentrancyScenarios` now checks page release while a native observer
remains, same-slot refill notifications, and eviction after observer removal.
The source lifetime/reentrancy suite passes **29 checks**, exercised inside the
new return fixture. `artifacts/authoring/return-complete/result.json` reports
**51/51** overall checks, including disabled/query-replaced/delayed targets,
explicit native departure, retained/recreated return, horizontal scrolling,
changed row height, smaller viewport, repeated suspension, and actual wheel input.
The earlier `return-container-trace` artifact preserves the mismatched-slot failure.

The larger F3 protocol/viewport changes are still uncommitted at this checkpoint.
The existing memento and broad indexed-widget fixtures have additional failures
under the current viewport, and a separate checkout of `223ffc16` is being used
to establish their baseline. They are not counted as green or assumed pre-existing.
