# Computed-style refresh without widget data reload

An appearance notification used to reload only host settings and shell palette.
Ordinary widget frames kept their old WRSS maps until another widget publication;
indexed leases kept the maps from acquisition even after later parent frames.
Newly acquired rows could therefore use a different theme from retained rows.

The session now owns one coalescing refresh lane for a newer appearance revision.
It refreshes admitted ordinary frames and retained indexed leases, with one
bounded request in flight. Rapid changes skip superseded replies. New frames or
leases that were acquired against the current revision need no extra request.
This is independent of provider request capacity, ContentRevision and query
generation: it never requests a worker render or reacquires a provider range.

## Trusted transport and authority

`refresh-presentation-styles` identifies widget incarnation and exact snapshot
sequence. `refresh-indexed-styles` identifies widget incarnation and opaque lease
ID. Neither accepts declarations, selectors, provider arguments, data or actions
from the frontend. Bridge resolves its own retained immutable declarations with
the current compiled widget/global cascade and returns complete, bounded style
maps plus that immutable theme snapshot's revision. Ordinary/pinned roots and
focus/default-focus fragments use the same style resolver as initial publication.

Registry publication ownership protects resources while styles are resolved;
final incarnation/snapshot/lease checks reject retirement races. The session
validates identity, revision, exact node membership, value bounds and all style
states before freezing the maps. A bad, timed-out or retired reply cannot replace
valid pixels or trigger a provider reload. The session records a bounded diagnostic
and attempts again on a later revision or fresh presentation. It does not spin
retrying a failed revision.

Ordinary style replies create a genuine session frame via `CommitStateLocked`.
The exact Snapshot, Authority, Descriptor and preview inventory remain shared;
only immutable RenderStyles and AppearanceRevision change. Existing displayed
input frames remain valid according to the usual binding checks. An ordinary
snapshot that supersedes an in-flight style request is never rolled back.
The native presenter admits a newer AppearanceRevision at the same widget
snapshot sequence, without replaying a consumed focus-entry request.

Appearance-only frames take a dedicated visual update pass after proving the
same immutable declaration snapshot and semantic authority. They refresh
computed geometry, typography, decoration, native layout and surface fragments,
without structural reconciliation or collection demand updates. This preserves
a pending logical focus restoration when a structural reorder is immediately
followed by a theme publication before the low-priority dispatcher restore.
A subsequent native, keyboard or controller focus choice supersedes that restore.
Indexed sources update their displayed binding but do not retry failed provider
pages for appearance-only frames; ordinary semantic updates retain that behavior.

Indexed leases retain their semantic range, lease identity and action/artwork
authority. The session atomically replaces their immutable style snapshot and
notifies retained native row views. Views detach subscriptions on retirement and
marshal updates to their dispatcher. Collection owners also refresh selected or
retained focus fragments. There is no collection Reset, count change or logical
slot replacement. A palette-only update preserves focus and scroll; a theme that
deliberately changes geometry still receives normal native layout.

Widget authors do not need a new SDK API, ContentRevision bump or data reload when
the user switches global themes. The host/Bridge binaries must be deployed together
for these additive trusted transport messages.

## Verification

Scripted transport tests cover same-snapshot and retained-lease authority,
immutability, superseding theme replies, superseding ordinary snapshots, lease
retirement, foreign node/lease identities and older revisions. Bridge tests prove
style refresh reads its retained declarations and keeps the same provider lease.

`Test-WinUiThemeRefresh.ps1` runs the actual Bridge and worker with an isolated,
file-watched settings profile. Its native page enters item 75 through the authored
exact-focus request, changes Neon Circuit to Cool Slate, and checks ordinary
snapshot/action identity, retained slot/item/lease/container, deep focus/viewport,
unchanged completed provider loads, row/focus-fragment colors and new realization.
The 11 native checks passed at 125% Windows scaling. Text and fragment colors both
changed from `#FFE8F1FB` to `#FFEDF2F7`, with no session diagnostics; capture-screen
pixels were inspected. Evidence: `artifacts/winui-theme-refresh/native-05`.

Follow-up native regressions cover reorder followed by a same-sequence theme
before dispatcher yield, user focus supersession on either side of that theme,
geometry-changing styles, failed provider pages across a global theme switch,
and later semantic retries. On the combined integration build all 169 native style
checks (including five new focus/appearance checks) and all 15 retained-theme checks
pass. The associated declaration-admission tests also pass in the 242-test managed
session suite. Combined evidence: `artifacts/winui-shell/appearance-native-styles.json`
and `appearance-failed-pages-01/result.json`; analyzer and provider builds are clean.

The initial native attempt exposed the presenter's old same-sequence rejection;
the fixture also needed an explicit theme-dependent color and authored focus
entry instead of racing raw ScrollIntoView/Focus. These checks establish the
theme-refresh behavior, not overall browsing performance or full migration parity.
