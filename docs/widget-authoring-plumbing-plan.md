# Widget SDK authoring improvements plan

Date: 2026-10-22  
Audit baseline: `adb696ac` (`codex/playnite-achievement-focus`)  
Status: Implementation authorized on 2026-10-22. Work proceeds on `codex/sdk-authoring-improvements`; each finding remains subject to the decision gate below.

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
- F13: narrow to adopting the existing fixed ticker in Settings. Spotify's two
  wake meanings (reschedule versus immediate read) and provider backoff remain local.
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
