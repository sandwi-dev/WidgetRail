# Library recording output and uncropped video thumbnails

Scope authorized 2026-11-17: shared disk-backed recording output, preserve review/save/discard, fix cropped video thumbnails, build and launch candidate. One-off recordings retain the Windows yellow border. No installer or release.

## Decisions and evidence

- Reproduced configuration: 60 seconds at 21 Mbps plus application audio estimates 152 MiB, above the temporary attachment ceiling of 128 MiB. MP4 recording already streams to a file; the ceiling is an API/storage policy, not a whole-file RAM buffer.
- Add generic `CaptureOutputPurpose.LibraryRecording`, default omitted `TemporaryAttachment`. Drafts use explicit `LibraryRecordingDraft` origin, protocol 124, a maximum completed-file size of 2 GiB, and shared library staging/journal/move publication. The host requires both capture and save permissions. No widget ID exceptions.
- Temporary attachment limits, Game Help's 8 MiB request, and replay memory policy stay independent. Large reads remain bounded streaming; the SDK's whole-file convenience method continues to reject files above 128 MiB.
- Known bitrate requests reserve duration/bitrate plus headroom within the caller's maximum; unresolved Auto/quality settings reserve the stated maximum. Explicit settings are not silently reduced. The native writer checks size during recording and after finalization; this is a completed-file limit, not a guarantee against transient disk-write overshoot.
- Native library export reuses the existing recorder with a purpose-specific admission ceiling. Old exports retain their limits and ABI. One-off border policy is unchanged.
- Existing advanced recording completion compared actual Auto-resolved output against unresolved fallback fields. Pass the trusted resolved configuration through completion and validate it against the original request, including exact even canvas/source dimensions.
- Thumbnail reproduction: Windows `VideosView` returns a cropped 256×175 image for an owned 1280×480 marked-edge video. Both edges are absent. `SingleItem` returns 256×96 with both markers. Matching user's Silksong clip also has a cropped 210×144 cache; corrected output is 256×144. Originals unchanged. Version the video derivative cache; keep PNG cache unchanged and regenerate lazily.

## Consumer matrix

| Consumer | Status / required check |
|---|---|
| Capture Library Record clip | Migrate to library output; review, save by move, discard, source playback/edit/share authority. |
| Game Help | Existing temporary output retained; 8 MiB request and context bounds regression checks. |
| Replay capture | Existing output session reused; preserve default bounds and commit/cleanup behavior. |
| Discord streaming | Not applicable to one-off storage budgets; unchanged border policy and streaming API. |
| Capture Library gallery / Playnite related captures | Already use Contain; shared thumbnail extraction/cache fix applies to both. |
| SDK examples/templates | Audit actual capture entry points and origin dispatch; no new recorder or widget-managed file paths. |
| Contextual library save | No shipping draft consumer; initially reject draft contextual save explicitly rather than lose metadata. Existing temporary contextual saves retained. |

## Validation / handoff

- [x] Purpose/SDK bounds and legacy wire compatibility: targeted SDK test passes, protocol parity 213 constants. Unsupported draft saves never fall back to an older operation.
- [x] Broker permission, separate reservation, completion identity, save/discard/revoke/expiry and recovery: drafts 5/5, existing capture 8/8. Includes 129 MiB staged output, repeated failures/pending reservation bounds, duration tolerance and interrupted publication. Independent source review identified the latter two edge cases; corrected and covered before launch.
- [x] Native old/new cap admission, overflow, cancellation, and unchanged border policy: 23 assertions plus suite completion. Small real output tests prove admission/cleanup, not a 2 GiB encoding stress run.
- [x] Playback-active publication and editor lifecycle: actual Windows MediaPlayer continued playback across same-volume move; pinned editor rejects concurrent publication and allows retry after release. Draft review uses per-output locks; replay keeps its original 256 MiB reservation ceiling.
- [x] Real video edges and versioned derivative cleanup: marked video and actual Silksong comparison; 5 focused codec/cache/permission tests pass. Source files unchanged.
- [x] Coherent candidate/runtime/native build, package review/update, launch: frontend22/protocol124, Capture Library 0.2.0.19, PID52108. 130 deployed DLLs and 22 native exports verified; Settings discovery intact. Package reviewed SHA256 `83f72fb49aaae16985ffe3586e584b2a6346abdfb3dc67e75151d119e865a0da`. Candidate only, no installer/release.

Game Help's full 51-test suite passed. Capture Library's review/save test covers normal saved playback replacement and a failed preview reopen after successful publication; the UI preserves the save receipt rather than offering Save again for a retired draft. No routine widget input suite repeated.

## Trim playhead report

User reported frozen seek thumb when using the player's Play button without dragging. It is not intentional. Native editor tests passed: Play selection 4/4; actual mouse click on player's Play 5/5. The latter advanced native position 0.106→1.307 s, Slider.Value 0.090→1.119 s, visible thumb 9.6→116 DIP; endpoints unchanged. Evidence `artifacts/ux-followup/trim-player-play-before22`. No speculative timer/input change made. Actual-user-source/load-specific freeze remains unverified; new validation-only fixture retained.

Normal shutdown exited previous candidate45848 and its active replayworker28440; no fixture recording process was retained. Existing replay preferences were preserved. New launch/payload/shutdown evidence lives in `artifacts/library-recording`.

## Next design discussion — not implemented

Subsequently approved and implemented as a widget-only change; see [capture detail design follow-up](capture-detail-design-followup.md), including the user's removal of the bottom guide and preview limitations without host changes.

User requests a visual proposal for capture details with visible controller hints. Generated flat mockup: `C:/Users/dwive/.codex/generated_images/01a1d483-3da5-7a70-851e-a669560cda92/exec-d4b8cea8-5e5d-49d3-91a5-aaa07a00cd71.png`. Approximate 70/30 media/sidebar split, top-left Back, LB/RB previous/next, compact metadata/title field, coherent action stack, Y Favorite, selected-action A badge and contextual footer guide. Player controls float translucently and hide idle. Bindings are proposed; use existing shortcut arbitration/guide/badge mechanisms and actual active scope when implementing. No layout or shortcut changes authorized by this visualization request alone.

Evidence is recorded under `artifacts/library-recording*`, `artifacts/thumbnail-aspect*`, and `artifacts/thumbnail-user-proof`. Source audit alone does not establish runtime performance or correctness.
