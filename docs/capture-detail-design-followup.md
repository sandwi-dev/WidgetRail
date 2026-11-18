# Capture detail and preview design

## Recording deck, feedback and capture actions — candidate 0.2.0.30

Replay uses one compact themed status deck with an indicator, application, buffered seconds, compact last-save receipt and Stop/Save commands. No invented Stop/X binding: X still means one-off recording. A new session clears the old receipt; existing host events, stale-session rejection and single-flight save remain authoritative. Preview title/metadata share a raised strip with subtle vertical and media dividers.

Action results now publish through the existing SDK notification publisher as BannerOnly/Informational/Always notices, at completion rather than rendering. The host owns expiry/placement/quiet preferences. Permission failures retain visible recovery controls using explicit widget state instead of parsing displayed message text. Replay failures remain in the deck. Host notification preferences can suppress banners; this is the existing notification policy, not a new toast renderer. The old 128 MiB settings copy and legacy settings test were corrected to the library-recording2GiB path and current Auto controls.

Validation:38 targeted widget checks passed before the final settings correction;8 focused settings/recording/permission/ownership checks then passed. Native filled-focus31/31 and native real-widget detail L3/reopen12/12 passed. The isolated detail fixture had no render-validation failure; separate thumbnail errors and concurrent controller-log contention were not claimed fixed. Owned fixtures closed. Linked recycle evidence/consumer matrix below includes the real headless Windows recycle result. User explicitly authorized stopping live streaming/replay for deployment; old frontend11564/replay38396/stream28976 exited normally. Candidate artifacts and package reviews are under artifacts/capture-deck-followup. No installer/release.

Design follow-up (proposed, not implemented): RemoveLinkAsync writes a durable per-ID .unlinked marker, deletes cached metadata/thumbnail and removes the SQLite row. ImportSourceFile derives the ID from source ID plus normalized relative path, then checks that marker. This preserves user exclusion across index rebuilds, but also excludes a replacement file at that path. Evaluate a transactional durable metadata/exclusion store separate from the rebuildable index, explicit replacement-identity semantics, and restoration of hidden items. Per-file marker scalability is a source-inferred maintenance concern, not a measured performance defect. Do not replace these safeguards with an index-only flag.

## Remove detail bumper hints, preserve bindings

Latest clarification: remove only the visible bumper hints from Previous/Next; preserve LB/RB page bindings and existing media input precedence. Previous/Next are ordinary labeled buttons. Back B, Favorite Y and L3 actions remain unchanged. Removed the now-unused leading-hint option and bumper-specific sizing style. Superseded package 0.2.0.28 removed bindings, was reviewed but never installed; corrected package is 0.2.0.29.

Evidence: artifacts/detail-bumper-removal. Deployment/relaunch deferred while the user's live Discord stream and replay recording are active; no processes or running catalog changed. Host unchanged; no routine native interaction tests, installer or release.

## Centered detail follow-up (supersedes top-edge alignment)

User clarified that the media should be vertically centered relative to the right card, with player controls over the video. Candidate 0.2.0.27 centers both native grid cells, keeps the constrained 16:9 media stage, and removes the sidebar's forced full-height styling and inherited scroll padding. Its scroll viewport can shrink to content or remain bounded for overflow; the card retains its internal padding. Previous layout used different content and viewport centers, and top alignment was the wrong interpretation. The existing shared player keeps its overlay toolbar; no separate toolbar row, fixed pixel offset, host or SDK change. Other source ratios remain contained in the 16:9 stage; this is not a source-ratio-specific viewport feature.

Two focused detail behavior checks, build and package validation pass. Routine native interaction/visual review remains with the user per their widget-only testing preference. Evidence: artifacts/detail-centering. No installer/release.

## Detail alignment and bumper spacing follow-up

Candidate 0.2.0.26 adopts the existing aspect-ratio explicit Grid and AlignInGrid(Start) for a top-aligned 16:9 detail stage. The sidebar retains its independently scrolling full-height viewport. Other media ratios remain contained, without cropping; short viewports can still constrain the stage height. No new host/SDK behavior or input routing. Source finding: DetailPresentation previously stretched the player through the full row, leaving centered video below the top-aligned sidebar. This widget layout now supplies the intended viewport.

Previous uses LB before its label; Next uses RB after its label. Bumper hints receive a 36×24 box instead of the shared 24×20 round-button box; all other hints are preserved. Build and two focused detail behavior checks pass; package validation passes. Routine native input/visual review is left to the user as requested for widget-only changes. Evidence: artifacts/bumper-hints. Package 25 was installed but not launched before the additional alignment request; 26 includes both changes. No installer or release.

## Latest candidate — corrected loading geometry and hint interpretation

Reproduced the attached video with delayed1341×807 image decoding: explicit Auto row/media grew from558.4×314.4 to558.4×807.2DIP while the preview stayed560×402.4. The resulting236DIP centering offset explains the user's~295physicalpx movement. Evidence `artifacts/preview-motion-report/native-before26`; prior assertions were insufficient because they used only a video with stable desired size.

Rejected the Spacer-in-Auto-row sizing approach. New opt-in `WidgetExplicitGrid` maps the existing `aspect-ratio` style to finite width-derived measurement. Capture's stage is Star×Star with one media child and no Spacer. Unset ratios use base native Grid behavior; explicit heights retain precedence. No player, cache, decode, input or fullscreen special case. Existing Poster/Spacer consumers remain unchanged.

Native validation91assertions passed: delayed reported-size image10, portrait10, wide image10, video/theme/reset15, ordinary explicit-grid22, and existing ultrawide media-preview playback24. The ultrawide fixture uses an existing fixed viewport; it complements the changed aspect-grid image/16:9 cases rather than claiming direct non16:9 coverage in that new grid. All owned fixtures exited cleanly. Before/after geometry and shutdown evidence: `artifacts/preview-motion-report/native-after27-*`.

Corrected user interpretation: LB/RB replace Previous/Next chevrons, rather than accompany them; Back/Favorite/Screenshot/Record hints are restored inside controls. No detached hint rows restored. Three focused widget checks pass. Candidate package0.2.0.24 reviewed SHA256 `1706ab391834a3a9294226e818dcf6a57a83d86c7039d591501861a6c98d0a4e`, frontend27/protocol124. Package23 was never deployed. Launch/payload evidence under `artifacts/preview-motion-report`; no installer/release.

Latest steering: the user meant LB/RB should replace the Previous/Next arrow icons, not that all other badges should be removed. Restore Back/Favorite and home capture badges inside their buttons; retain the normal-cascade/full-bleed work. Widget-only correction package0.2.0.23 was built/reviewed but not deployed because a new preview sizing regression was reported before restart.

Regression investigation: attached recording `20261118-0209-57.9434517.mp4` shows the frame/header remaining stationary while decoded media shifts downward and pushes actions below the clip. Extracted evidence is under `artifacts/preview-motion-report`. Source image1341×807 and observed~297px displacement at125%DPI are consistent with intrinsic image height winning an unbounded Auto grid row. The Spacer reserved a minimum but did not constrain its sibling. Native delayed-decode reproduction is required before fixing. Proposed reusable correction: honor existing aspect-ratio on an explicit Grid, constrain its native Star row from width, and remove the Spacer. Preserve default Grid behavior when ratio is unset; test images/non16:9video/poster transitions, not only the earlier16:9video case.

Approved visual proposal, November 17, 2026. User subsequently removed the proposed bottom guide row and requested preview polish using existing controls only. No host changes for this work; no installer or release.

## Implementation

- Existing Grid tracks give media/sidebar a 7:3 split, with a bounded 280–400 DIP sidebar. The media takes the remaining height; sidebar actions scroll independently. Existing native player retains its translucent, idle-hiding controls. No second player, timeline or footer.
- Back/discard at top left; previous/next at top right. Sidebar has explicit Title label, compact type/duration/dimensions/size/audio/date metadata, direct Share/Favorite/Edit/Folder/Save-copy/Recycle actions. Linked and unavailable entries retain their accurate removal wording and confirmation.
- SDK ControllerGlyph/ControllerHint displays actual B/LB/RB/Y bindings beside controls. L3 uses existing CreateFocusRequest/FocusElement to reach action controls, with A Select beside the action heading. Home uses matching Y/X capture badges and L3 at search. Selection mode removes screenshot/record shortcut badges when those bindings are absent. No manually rendered controller symbols or duplicate focus router.
- Existing media input precedence is unchanged: a focused media player uses LB/RB for playback/image controls; page capture browsing uses LB/RB outside that player. Fullscreen/options retain their own input scope. This limitation already exists and is not changed by this widget-only styling work.
- Preview header consolidates title plus compact metadata. Borderless player remains aspect-fit; action buttons use equal-width responsive columns and a quieter primary emphasis. Preserve existing centered placement, hover delay, saved-source leases, autoplay/loop/mute policy and R3 Interact.
- Preview limitations: the host owns outer padding and entry footer. A custom central Play overlay and Y/X Favorite/Share shortcuts are not added. Entered-preview routing currently consumes unhandled controller input before ordinary shortcut dispatch, and X belongs to the active player. Existing buttons preserve correct indexed action and prepared share-intent routing. A future shared shortcut contract is broader work, not a widget workaround.

## Ownership / consumers

Capture Library adopts existing SDK layout, hints, focus requests, buttons, intent and player APIs. No shared contract or host changes, so other widgets retain their existing behavior. The controller-hint/focused-player precedence finding is source-confirmed, not a new native runtime test. User requested routine mouse/keyboard/controller verification be left to them for widget-only changes.

## Checks and handoff

13 focused managed tests pass, including saved, unsaved, linked and unavailable detail actions, busy admission, actual SDK Y favorite dispatch, L3 focus intent, home selection hint consistency, navigation/lease preservation, prepared sharing and stale saved identity in a new capture. Fixed the new unsaved metadata date to exclude previous saved selection. WRSS and package validation pass; publish clean. Evidence: `artifacts/capture-detail-layout-tests/results-summary.json`, `artifacts/capture-detail-layout-final.{log,binlog}`, `artifacts/capture-detail-design/capture20-publish.{log,binlog}`.

Capture Library candidate package 0.2.0.20, reviewed SHA256 `dc8ff816a5ca243864636e838e777e0b1780ece4569883834326702d7b1255e4`, applied only to the existing candidate with settings/consent preserved. Existing frontend22/protocol124 retained; no host/runtime rebuild or installer/release. Candidate launch evidence: `artifacts/capture-detail-design/candidate-launch.json`. Routine native input flows were not run, per user preference.

## Inline-hint and preview-spacing correction

User rejected detached controller glyphs. Existing SDK ActionSurface now contains icon/label/trailing right-aligned badge in one semantic focus/click target for Back, Previous, Next and Favorite. Toolbar capture controls similarly contain their smaller badges inside the same outline. No button-tree translation, extra focus target or host change. Removed detached Actions/Select and search hint rows; existing L3 shortcuts remain. Four affected declaration/behavior checks pass; package/WRSS validation passes.

The equal-width preview action row now uses explicit Grid star columns rather than a responsive grid. Source diagnosis: responsive grid initially computes one column from zero ActualWidth, then reflows after arrangement; ContentSizedPresentationState freezes the initial preview height. This explains the observed spare height after the row becomes three columns. Explicit columns are the correct authored layout for these three commands and avoid that transient measure. Native visual confirmation remains with user; no generic measurement policy was changed.

Candidate 0.2.0.21 reviewed SHA256 `7c97f8884b51ce39aef896130827b78ec74b5055b2fe950b0c1590f2a7eef64d`; evidence `artifacts/capture-toolbar-preview`.

### Proposed shared follow-up (not implemented)

User correctly identified hardcoded preview padding as styling policy in the host. `WidgetPreviewFrame.Apply` sets Surface.Padding=12 while PreviewFrameStyle.Resolve already reads the shared `preview` style role for other chrome. Move 12 DIP to a default preview style, resolve/apply bounded padding through the existing style pipeline, and support appropriately scoped overrides so media previews can use zero without changing form/filter previews. Preserve high contrast, theme/reset behavior, viewport clamping, input ownership and content-sizing tests. Also investigate initial measurement readiness for responsive content before freezing fit-to-content height. These are host-owned gaps; no widget label parsing, negative margins or manual height compensation. Respect current no-host-change scope until authorized.

## 16:9 edge-to-edge follow-up

Latest request: 16:9 videos fill the media preview edge-to-edge, with controller badges retained only inside Previous/Next. Root announced the narrow shared styling change required for the requested inset removal. Existing shortcuts remain available; remove authored B/Y/X and Actions/Select glyphs rather than change input semantics.

Contract: global `preview` role `padding` supplies the theme default (12 DIP). Widget `preview-padding` spacing on a FocusPresentationSurface overrides only that preview's content inset. The host footer keeps theme-derived insets. Capture opts in on its gallery preview only; Filter/View and other widgets keep their prior defaults. Header/actions author their own internal12DIP spacing. No widget-ID check, negative margins or arbitrary footer style alias.

**Corrected assessment:** user explicitly requested the proper stylesheet override path instead of piecemeal fixes. The `preview-padding` experiment is rejected before shipping. Reuse the normal composed widget/theme cascade for scoped preview component styles, including frame/footer padding; preserve unchanged defaults for existing consumers. Remove only the experimental property/lookup, retain any proven frame layout infrastructure that fits normal styling semantics. Do not claim generic widget override support while only exposing a special padding property.

Sizing: adopt existing aspect-ratio Spacer in an explicit single-cell Grid with the existing player. This should preserve16:9 at full and clamped widths; native qualification required before shipping. Other ratios remain contained, never cropped or stretched. Native player/video lifetime, hover/input routing and fullscreen are retained.

Consumer matrix: Capture gallery opts in; Capture Filter/View, Discord links/media/voice, PoB item previews, Playnite rich previews and SDK examples retain default content inset. The native theme/reset/high-contrast checks cover the shared frame. Pending validation/build/candidate handoff will be appended here.

### Replay approval management — requested follow-up

Current candidate handoff: frontend25/protocol124, Capture0.2.0.22, PID42760. Proper scoped preview cascade implemented; the padding-only experiment was removed. `preview.capture-gallery-preview` and `preview-footer.capture-gallery-preview` resolve through the existing layered winner engine, with exact owner identity/classes and explicit part selectors. Generic content classes cannot restyle chrome. Optional bounded immutable PreviewFrame/PreviewFooter maps reuse existing bridge style transport/freeze/refresh; no SDK snapshot option or synthetic node introduced.

Capture gallery alone removes outer content inset, retaining internally padded header/actions/footer. Existing Spacer+Grid sizes16:9 at full and clamped widths; other ratios remain contained. Only Previous/Next retain authored controller badges. Other widgets/filters keep theme12px/default footer8px spacing. High contrast uses existing system palette policy, and style reset restores defaults without input/decoder changes.

Validation: component cascade/transport2, indexed styles/leases3, real shell palette1, native geometry/style11, native hover/controller/outside-click7, affected widget checks4. Native maximum edge error wide0.379physicalpx and narrow0.667physicalpx. Initial total-width1DIP assertion was corrected to per-edge physical-pixel tolerance; failed evidence retained. Shell inventory regression fixed by bounding against the exact33-role allowlist, with updated strict end-to-end role/padding assertions. All final checks pass; source audit does not substitute for native proofs.

Candidate package reviewed SHA256 `a7fdd471f2d8f160fe5df00a54dad810cea1cb57f5f5dc211bcf1ff11b37d8c0`. Build/native/launch evidence: `artifacts/capture-fullbleed` (frontend25, native-parts25, native-centered25). No installer/release. Approval management below remains planned.

User noticed no way to review/remove approved applications in Capture settings. Current host stores one last-approved application identity/name (`CaptureReplayPreferences`), not a list. No separate management screen exists. Add explicit approved-app display and Forget action in Capture settings, backed by the shared replay service. Clear approval and auto-start intent; handle active replay explicitly without deleting saved captures. Fence pending/late automatic-start completions against the cleared revision so a late result cannot restore approval. Avoid widget-side edits to host settings. Multiple approved applications would be a separate expansion, not implied by current behavior.

Do not conflate this with one-off capture confirmation suppression: `CaptureConfirmationSettings` stores per-widget screenshot/recording skip choices, and Settings → General → Captures has Restore capture confirmations. That action does not clear replay's app approval. This missing replay management was recorded while the preview work continues; no app approval was changed.

## Filled-action focus visibility (source ready; native qualification pending)

Source-confirmed cause: the shared primary icon-button recipe and widget primary/play recipes explicitly used canvas/surface-colored inset outlines. These dark edges were difficult to distinguish on colored controls. Replace them with the existing theme focus color and width, separated from the fill by `--focus-filled-offset: 2px`. At the default2DIP width the ring extends4DIP outside the control. Existing native `WidgetFocusDecoration` owns geometry/animation; no new focus host, input route, layout target or state counter. Neutral buttons and existing inset collection/slider outlines are unchanged.

| Consumer | Treatment |
| --- | --- |
| Shared primary IconButton and SDK Gallery override | Migrated to separated theme focus ring. |
| Capture Library primary actions; Playnite capture-save; Performance Monitor enable | Migrated, preserving fill, foreground and disabled declarations. |
| Spotify primary/play; YouTube primary; YouTube Music play; Media Sessions play; Embedded Media play | Migrated; ordinary transport/secondary controls remain unchanged. |
| Settings, Discord, PoB, Audio Mixer, remaining templates/examples | No dark filled-focus override found; existing focus recipes retained. Shared primary component consumers inherit the fix. |

Clipping audit: the ring needs4DIP surrounding space at default theme settings. Affected control rows have6–12DIP gaps and their clipped page/card owners reserve8–16DIP padding; Spotify setup's smaller scroll inset still contains a separately padded14/16DIP card. Capture preview actions retain12DIP horizontal and10DIP top padding, with footer spacing below. No negative margins or blanket overflow changes added. This is source evidence, not proof of every runtime layout. High contrast remains governed by the existing native adapter: normal buttons restore system focus visuals; targeted decorations use system focus ink.

Managed build clean and PlatformSettings suite36/36 passes, including all built-in palettes and the expanded actual widget primary-style matrix. The contrast audit now measures an outside ring against its surrounding theme surfaces while retaining the3:1 requirement, and preserves the old own-fill measurement for inset rings. New Capture/Performance fixtures are scoped to primary actions; Capture's preexisting translucent focused gallery-card fill is not changed by this task. Evidence: `artifacts/filled-focus/settings-final-build.binlog`, `settings-final-tests.log`. Initial failed assumptions are retained in `settings-tests.log`. No live candidate/Discord stream/replay interaction, host build, package deployment or native foreground test has occurred for this slice yet.

Native follow-up prepared in the existing `--focus-decoration-result` fixture (no new runner): actual shared primary styles under Mono Ink and Mineral Light, exact ring offset/extent, clipped-parent padding, and high-contrast fallback. It extends the existing decoration lifetime/motion checks. Native execution is pending the coordinated desktop lane; current user stream/replay and candidate remain untouched. All nine widget packages listed above need refreshed WRSS payloads, and the coherent host/Bridge runtime needs the updated embedded PlatformSettings theme; source changes alone do not update installed packages.

## Linked-original recycling (implemented; candidate deployment pending)

Capture Library now separates **Remove from library** (existing metadata-only suppression) from **Move to Recycle Bin** for available linked media. The latter requests the distinct optional `media.capture-library.recycle-linked.v1` permission at the explicit confirmed action. Existing Manage/source grants are not upgraded. `RecycleLinkedAsync` reuses bounded item/revision references and partial results; old `RecycleAsync` remains owned-library-only and mixed-source batch removal keeps its previous behavior.

Broker scope: exact indexed originals from enabled/authorized connected folders across source owners, with no provider-specific branch. Existing source resolution, reparse rejection, filesystem identity and directory pins are reused. The existing STA `ITransferSource.RecycleItem` primitive receives a final identity/cancellation guard. No permanent-delete fallback, arbitrary path API, or automatic permission changes. Same-original restoration recovers metadata; different-incarnation files do not inherit the old recycle marker.

| Consumer | Applicability |
| --- | --- |
| Capture Library linked detail | Migrated: distinct confirmed recycle action and optional permission; old removal remains. |
| Capture Library batch removal | Preserved: linked entries are removed from the index, provider originals retained. |
| Playnite related captures/providers | Existing read-only source contracts unchanged; original recycling is available through the separately authorized library action. |
| Other SDK/host recycle callers, templates/examples | Existing owned-only operation unchanged; new method requires explicit adoption and permission. |

Focused checks: 9/9 passed in `artifacts/linked-recycle-qualified-tests.log`: permissions/revocation, cross-owner indexed scope, revisions, queued identity replacement, disconnect, traversal/symlinks, partial results, cancellation after commit, SDK validation and distinct widget flows/preview recovery. The actual existing Windows recycle primitive also succeeded with retained directory pins and a single final guard for one uniquely-owned 12-byte temporary fixture (`artifacts/linked-recycle-native-result.json`). No user original, overlay, replay, Discord stream, or actual permission setting was changed by these checks. Earlier fixture assumptions/failures remain in the first/final test logs.

Linked-original recycling final verification: 5 additional legacy/detail checks passed (old read-only source behavior, mixed batch removal, failed-preview recovery, direct detail actions); broker vocabulary/domain contracts passed 10/10; SDK public-baseline checks passed 2/2 after additive API regeneration; documentation contract passed 257 Markdown files. Evidence: artifacts/linked-recycle-contract-tests-final.log, linked-recycle-compat.log, linked-recycle-docs-final.log. Production source stayed unchanged after the qualified managed/runtime build; candidate staging is coordinated by root.
