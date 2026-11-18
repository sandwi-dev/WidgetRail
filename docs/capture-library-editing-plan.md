# Capture Library editing

Status: implemented and qualified. Development candidate Capture Library 0.2.0.3 is running on the final host build 31 for user verification. No installer, release or merge was created.

## Scope and ownership

First pass: PNG crop and 90-degree rotation; MP4 start/end trimming; **Save copy** always creates a new owned capture. Existing saved and linked originals are never overwritten. Capture Library supplies the entry point and subsequent navigation. A reusable host editor owns crop/timeline interaction, pointer capture, controller adjustment, keyboard navigation, focus, preview, progress, and cancellation. The broker owns source authority, transformation admission, staged output, publication, and provenance.

`WidgetSdk/MediaPlayerElement.cs` and `WidgetProtocol/MediaPlayerDefinition.cs` currently provide playback and initial preferences, not crop handles, trim ranges, or playhead bindings. A widget-only implementation would duplicate native presentation mechanics. Prefer a bounded host edit session, following the existing interactive capture/copy request pattern, rather than extending every ordinary media-player consumer or giving widgets file paths.

Internal store sessions retain one verified source through preview and encode. A recipe contains exactly one image transform or one video range. Image crop coordinates are integer pixels after quarter-turn rotation. Video ranges use bounded start-inclusive/end-exclusive ticks. The public `EditCopyAsync` overloads accept either a revisioned saved reference or this widget's owned temporary `CaptureAttachment`, and return `Saved` plus the new item or `Cancelled`. Recipes and paths remain host-only. Saved sources require read + save consent; temporary sources require capture + save consent. The broker retains both leases through the operation.

## Storage and authority

- Reuse `CaptureLibraryStore.Playback.cs` / `CapturePinnedFile.cs`: retain source read handle and ancestor directories, deny mutation/replacement, and retain source-grant validity.
- Pin expected content and metadata revisions before transformation. Recheck revisions, source grant, caller permission/lifecycle, and cancellation before committing. Cancellation alone does not validate a late result.
- Permit one encoder per profile. Keep the library gate only for admission and publication; browsing must remain responsive during encoding.
- Encode directly into a uniquely named owned staging file. Never import/copy a large original through the 128-MiB temporary capture registry. Existing read support remains 64 GiB/eight hours.
- Bound pixels, output bytes, encode lifetime, and disk use separately from recording budgets. Unsupported codec/format cases remain readable but cannot be edited through this first-pass encoder.
- Publish a new ID by atomic same-volume rename and metadata-last publication; do not deduplicate the edit into its original. Current `SaveOwnedAsync` hashes/copies under the library gate and content-deduplicates, so it cannot be reused unchanged for large edited output.
- Maintain a small owned staging journal/lock for cancellation and crash recovery. Delete only files named by the host's closed staging format. Recovery must not touch provider originals or active jobs.
- New media has `SourceId = null` and a new saved timestamp. Preserve known actual capture time, application context, and provider associations without restamping their origin/event time. Record source ID/revision and recipe as private derivation metadata; retain legacy public query shapes.

## Native codecs

PNG uses Windows Graphics Imaging. Do not reuse the thumbnail helper's downscaling or `BitmapAlphaMode.Ignore`: editing preserves full resolution and alpha. Define orientation consistently with the preview and test non-square dimensions and crop coordinates after rotation. [BitmapTransform.Bounds](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.bitmaptransform.bounds) is defined after scale, rotation, and flip.

MP4 first qualifies Windows `MediaTranscoder.PrepareStreamTranscodeAsync`, retaining the original file rather than copying it. Trim may re-encode; lossless export is not promised. Preserve supported source dimensions/frame rate and audio, and reject unsupported HDR, multi-track, or codec cases rather than silently losing content. Native `MediaComposition` is already used by repository fixtures and is a fallback if required. [MediaTranscoder](https://learn.microsoft.com/en-us/uwp/api/windows.media.transcoding.mediatranscoder), [Windows media editing](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/media-compositions-and-editing).

### Initial native evidence

2026-11-16: isolated `artifacts/capture-edit-probe` compiled and executed without production edits. A 5×3 PNG with varying alpha was rotated clockwise and cropped to 2×3; all output pixel bytes matched the expected source pixels. A generated six-second H.264/AAC clip was trimmed from one to five seconds using retained managed random-access streams. Output was four seconds within one-frame tolerance, retained 320×240 dimensions and its audio track, and source bytes were unchanged. Encoding took 427 ms; observed process peak was 207 MB including fixture generation, not a production memory benchmark.

The basic `MediaEncodingProfile` probe did not expose HDR/profile metadata. This API alone is insufficient to certify SDR; format admission requires further evidence. No production codec guarantee is inferred from this small fixture.

### Implemented foundation evidence

`CaptureLibraryEditing.cs` defines host-only recipes and budgets. `CaptureLibraryStore.Editing.cs` retains the source, checks captured content/metadata revisions, holds a profile encoder lock, and encodes outside the library gate. `CaptureEditOutput` rejects oversized writes, seeks, and resizes. Revocation/disposal cancels the encoder; disposal keeps store resources alive until its encoder cleans up. Output is staged in a hidden owned subfolder on the destination volume, then renamed before metadata publication. A small host-only journal permits cleanup of abandoned output; corrupt journals cannot name arbitrary files. Startup invokes best-effort recovery before linked-source discovery; an active job or unavailable recovery directory cannot prevent ordinary host startup.

Derivation creates a new ID and owned file, preserves known capture time/application metadata, and records the source reference plus recipe privately. New media origin identifies the editing actor; copied context associations retain their original provider origin, event time, and capture time. New association IDs/receipt hashes do not steal the original provider's idempotency receipts. This avoids adding fields to legacy public query responses.

`WindowsCaptureEditCodec` now uses a disposable `MediaSource`/track inspection before stream transcoding. Its bounded AVC sample-description/SPS inspection permits progressive 8-bit AVC1 baseline/main/high with default/BT.709 limited-range color and zero or one mono/stereo AAC track. HDR/PQ/HLG, BT.2020, HEVC, extra video/audio/metadata tracks, rotation, non-square pixel aspect, and ambiguous sample-entry semantics are rejected. Unsupported videos remain readable. Variable-frame-rate and broad codec coverage are not claimed.

PNG editing currently admits at-most-8-bit SDR PNG without ICC profiles, EXIF orientation, animation, or non-sRGB color metadata. It preserves full resolution and straight alpha. A separate editor-preview helper retains alpha while bounding the preview to 1920×1080 and 8 MiB; ordinary opaque thumbnails/context renditions are unchanged.

Initial encode budgets: one profile encoder; selected video span at most ten minutes, output at most 2 GiB, encode deadline ten minutes; image output retains the existing 32M-pixel/128-MiB limits. No silent resizing occurs. Reading existing 64-GiB/eight-hour videos is unchanged.

The six editing scenarios pass, including all four image quarter-turn/crop mappings and exact alpha/preview pixels. A valid 134-MB linked MP4 (six seconds of actual media plus a sparse ISO-BMFF free box) was trimmed in place to a four-second owned copy in 513–641 ms on this machine. Its final frame selected the expected source segment, its audio track survived, original SHA-256 was unchanged, and original write access was available after cleanup. This is a bounded-I/O regression fixture, not an eight-hour-video performance claim. A real duplicated-audio-track MP4 is rejected; HDR/profile/rotation metadata rejection is also covered.

Full Capture Library regression suite after session/wire integration: **78/78 passed**, including legacy strict wire shapes, source authority/revocation/recovery, copy/recycle, context receipts, large linked-video playback, and indexed-query behavior. Evidence: `artifacts/capture-edit-sessions/all-tests.log`, the earlier foundation run, focused test binlogs and the isolated probe logs.

### Retained sessions and completion reconciliation

`OpenEditSessionAsync` admits either an exact saved reference or a registry-authenticated temporary lease. `AcquireInput()` gives preparation exclusive use of that retained stream; `SaveAsync` uses the same verified handle, avoiding a second original-file open/hash. Disposing the session cancels work and defers releasing the pin while an encoder or preview operation still uses it. Store/session cancellation callbacks execute outside their respective locks.

Each session mints a stable result ID. The first save freezes its recipe and memoizes the completion task; duplicate completion does not run another encoder. Durable completion receipts are bound to actor, source fingerprint, and recipe. A failure after media/metadata publication is reconciled by that result ID, and a later removal cannot cause the copy to be resurrected. Receipts are retained for seven days, with a 4096-receipt admission bound; expired receipts are pruned only by the trusted store. Startup also finishes interrupted receipt publication.

The temporary adapter checks the exact owner and complete attachment record, rejects borrowed saved-media tokens, and pins the existing temporary original without copying or saving it. Cancelling the editor leaves that original under its existing widget lifecycle. If the widget discards/retires the source during encoding, the edit is cancelled and the final pin release allows registry cleanup.

Session tests additionally cover exact stream reuse, duplicate saves, changed-recipe rejection, foreign receipt access, removed-result non-resurrection, postcommit receipt-publication failure, unsaved-capture editing, and temporary-source retirement. Manager tests cover completion/cancel ordering, cancellation while encoding reports progress, both consent leases, and 80 overlapping cancellation/late-preparation runs. Review corrected Ready-publication ordering to prevent a cancelled preparation from exposing an editor or stranding its caller. Expired terminal status is an explicit failure, not an invented cancellation.

Remaining: native editor interaction/appearance and final widget entry-point qualification. The editing API is not yet a qualified user-facing feature until those checks pass.

## Qualification

- Pixel-exact crops/rotations, alpha, odd dimensions, orientation, malformed/out-of-range recipes, no-op handling, and maximum image bounds.
- Real MP4 segment duration, first/last frame selection, audio presence/sync, source metadata, encoder failure, HDR/multi-track rejection, and large-source bounded I/O.
- Original hash/attributes unchanged; linked originals never overwritten/deleted; foreign or stale references rejected; source disconnection/permission revocation before commit aborts safely.
- Encode outside library gate; concurrent edit admission bounded; cancellation/disposal releases handles and cleans staging; disk-full and interruption during publication recover without orphaned visible entries.
- Native controller, keyboard, mouse dragging, lost pointer capture, large text, narrow layout, and edit-to-view focus return once the shared editor is wired.

## Consumer matrix

| Consumer | Applicability |
| --- | --- |
| Capture Library | Adopt editor entry point and returned-item navigation. |
| Game Help | Existing temporary capture review remains unchanged. The shared temporary-source overload is available without an initial save; adopting a new UI entry point remains outside this Capture Library pass. |
| Playnite | Existing capture links continue to open Capture Library; no provider-specific editing logic. |
| Discord, YouTube, other media-player users | Ordinary playback controls unchanged; editor is opt-in. |

Shared host input changes, if implementation proves necessary, require representative native regression checks. This plan does not authorize replacing working media lifecycle logic or moving provider-specific semantics into the host.

## Native editor integration in progress

The native editor uses the existing `ShowHostChoiceAsync` owner/neutral/focus lifecycle.
A typed `IHostDialogContent` opt-in supplies movement, activation and guide hints; existing
permission, folder, browser and preference dialogs retain their current behavior. This
avoids reconstructing modal focus/history in Capture Library. Source authority and encoder
completion remain broker-owned, with a single transaction identity and terminal receipt.

Screenshot controls share integer crop geometry across mouse handles, controller D-pad/
bumpers and keyboard arrows (Shift for ten pixels). Clip start/end sliders have explicit
controller adjustment, fine arrows and coarse bumper jumps, plus a native range preview.
Save copy is disabled for unchanged/no-op selections. Save progress retains a separate
Cancel action; cancellation after an actual commit must report the saved copy truthfully.
Native controller, pointer, keyboard, theme and teardown qualification is still pending.

`PointerCaptureLifetime.ReleaseWithin` reuses the existing preview cleanup algorithm for
editor controls. It enumerates owned native capture holders before invoking capture-lost
callbacks. Existing preview retirement uses the same helper without a semantic change.
Consumer matrix: rich previews migrate to the shared helper; editor content adopts it;
ordinary pages, browser surfaces, pins and banners retain their own existing lifetime.
Native held-slider/retirement checks cover the shared path.

The geometry suite passed as part of all377 WinUiShell tests: odd-size rotation/crop,
10,000 successive bounded edits, extreme deltas, all handles/aspects and ordered trim
endpoints. This establishes geometry behavior, not native input or codec correctness.

Game Help can use the temporary-owned-capture overload without saving an original first,
but this pass adds the user-facing editor entry only to Capture Library. Its existing
review workflow is unchanged. Other media widgets remain ordinary playback consumers.

### Rejected decoder-suspension experiment

Source review found that `MediaPlayerView.SuspendPlayer` clears the resolved source and
stops its timer, while saved-video access renews through `RefreshAuthorityAsync` every
two seconds against a two-minute idle lease. A proposed editor-only SetActive(false)
shortcut could therefore break the original preview after a long Cancel. It also did
not qualify pending-load cancellation. The experiment was removed before qualification.

The implementation is narrowed to pausing playback/input and suppressing automatic
play while the editor is open; the source authority timer and decoder stay alive.
On return, the original position remains paused. A scoped host-presenter lease applies
only to the invoking main presentation, not other pins or browsers. Reclaiming decoder
slots is deferred until authority renewal and pending-load lifetime can be preserved and
verified. Existing four-player budget still applies; no new unbounded decoder allowance.

First native screenshot editor run:13/13 passed, including controller neutral admission,
A/B adjustment, one-pixel keyboard changes, Escape, actual mouse dragging, unpressed
pointer movement after release, rotation and cancellation during the saving state.
Evidence `artifacts/capture-edit/native1/result.json` and `image-editor.png`. Clip/native
editor-to-broker saving and final candidate qualification remain pending.

Native editor qualification now25/25 passes (native3): image and clip controls,
controller neutral and B/release handling, keyboard precision, real mouse drags,
close-during-slider-capture, narrow window reflow/reachability, decoder teardown,
and paused-source renewal beyond a deliberately shortened initial lease. A real
screen capture confirms the expected blue video frame; RenderTargetBitmap omits
MediaPlayerElement compositor pixels and is not used as playback evidence. Save copy
uses the theme's primary accent, while Cancel remains initially focused. Evidence:
`artifacts/capture-edit/native3/{result.json,image-editor.png,image-editor-narrow.png,video-screen.png}`.

Shared native regressions with the integrated editor host passed: all42 interactive
preview checks (including interrupted slider capture, mouse transit, A/native/UIA entry,
source retirement and scoped Back) and all36 permission-dialog checks (including narrow
pins, neutral admission, scroll/focus and safe Cancel). Evidence:
`artifacts/capture-edit/previews-regression/result.json` and `permission-dialog.json`.

## Final qualification and handoff

- Capture Library 83/83, SDK 184/184, presentation session 380/380, compatibility 17/17,
  WinUiShell 377/377, broker timeout policy 4/4 and documentation 251 files passed.
- Native editor 25/25; shared permission dialogs 36/36 (including pins); real inline
  preview 13/13; real deferred/provider preview 40/40; interactive previews 42/42;
  ordinary Select/CommandMenu 47/47. Final host build 31: zero warnings/errors.
- Real candidate cancellation returned to the same original. Saving a square crop
  produced a new 2160 x 2160 PNG; trimming a real clip from 1-4 seconds produced and
  played a new 3-second 1920 x 1080 MP4. All original media SHA-256 hashes remained
  unchanged. Both generated test copies were recycled through Capture Library;
  the user's original three captures remain. Evidence: artifacts/capture-edit/
  real-ui-verification.json, real-video-verification.json, real-saved-square.png,
  and real-trimmed-playback.png.
- Final candidate PID 81588 uses the normal settings profile and candidate-only cloned
  catalog. Capture 0.2.0.3, Performance 0.1.5.1 and Playnite 0.4.6.1 remain selected there;
  every other enabled/version choice was preserved. The original installed catalog
  SHA-256 remains 5E46B388D54992FF691863FA6EC83FC1DD212965FD8B7ACD1CF87EBC7F80E91E.
  New runtime/bridge files match checked builds; Settings discovery remains one bundled
  Settings package, with no trusted widget overrides. Installed release/startup unchanged.
- Windows' automatic 6:00 AM restart prompt interrupted native validation. Restart was
  rescheduled through Windows Update to 7:30 AM today; updates were not disabled or paused.
  Automatic approval review blocked optional deletion of temporary Windows Settings
  inspection artifacts with only "blocked by policy". Those local artifacts remain;
  this is not an implementation or test blocker.

Remaining limits: PNG/MP4 formats admitted by the documented first-pass codec policy;
video trimming re-encodes and is bounded to ten minutes/two GiB output. Live PoB equip
mutation was not exercised; the shared provider/deferred selector path passed natively.
The four-player budget remains unchanged; decoder-slot reclamation during editing is
explicitly deferred because stopping authority renewal would regress long editor use.
