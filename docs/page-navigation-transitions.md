# Page navigation and live media transitions

Status: candidate ready for physical validation (2026-10-12).

## Contract

- Page navigation declares a stable region, destination key, and Forward/Back/Replace operation.
- Section changes continue using sibling ordering. Loading and content updates retain their destination key.
- Page effects: Slide (default), Vertical slide, Fade, Reveal, Cover slide, None.
  Fade and Replace fade through the page background: outgoing content finishes
  before incoming content fades in. Speed and reduced motion remain shared host preferences.
- The host retains outgoing visuals only for the bounded transition, with input and
  accessibility revoked. Native media uses its resident surface; no screenshot or decoder copy.
- Retained media is retired on completion, interruption, hide, owner change or disposal.
- Capture Library home/preview/settings and Settings hierarchy use page navigation.
- Returning restores destination focus and scrolling without retaining entire page controls.

## Progress

- [x] Protocol/SDK declarations, validation, and documentation
- [x] Separate page motion policy and Settings preference
- [x] Native media presentation lifetime and synchronized page placement
- [x] Capture Library and Settings migration
- [x] Focus/scroll restoration, cancellation, and reduced-motion checks
- [x] Native visual reproduction, final candidate and physical handoff
- [ ] User physical acceptance and integration

Existing browser/window-preview ownership must remain intact. Renderers without a
safe transition lifetime must use an explicit safe fallback; never remove the live-surface
guard indiscriminately. SDK authors declare navigation, not renderer workarounds.

## Evidence

- SDK console suite: 157 checks; motion policy: 18 tests; Settings: 87 checks;
  Capture Library: 22 checks; platform settings: 27 checks.
- Native page fixture: 57 checks across all six page effects, including video
  placement during movement, no native reattachment, outgoing input revocation,
  focus/scroll restoration, source disposal, interrupted navigation and reduced motion.
- Existing native media/image fullscreen fixture: 79 checks.
- Native recording `artifacts/page-transitions/native-pages-final.mp4` confirms
  movement and clipping while video plays. The first position-following experiment
  lagged one frame because transformed bounds were read back from WinUI. The final
  implementation maps each renderer from the same page pose and untransformed slot.
- The page renderer shares policy/recipes with existing motion and uses one XAML
  frame clock so XAML and native media receive matching position, opacity and clips.
  Existing section-only compositor motion remains unchanged.
- Browser, embedded-media and window-preview owners retain the safe snap fallback
  for changing content regions; their attachment/authority contracts are unchanged.

## Candidate review

- The complete candidate was checked with existing Dawnwalker video and screenshot
  entries. Forward/Back animate and the original capture tile regains focus.
  Settings exposes all six page options independently of section options.
- Additional checks cover concurrent Back navigation in independent page regions.
  Video without a resident shell stage uses the conservative exit fallback.
- Broad native-style validation hits the same preexisting poster aspect-ratio timeout
  on both this branch and unchanged main (before transition checks). Evidence:
  `styles-status.json` and `baseline-styles-status.json` in the artifact directory.
  The focused transition/media/widget/settings checks listed above pass.

The final normal-profile developer candidate is built from `155fa2da` under
`artifacts/page-transitions/candidate-final/0.1.0-preview.30` and was launched in
Capture Library. Both production/developer folder catalogs passed integrity and
icon admission (13/17 widgets). No installer, release, push or merge was performed.

## Visual polish follow-up (2026-10-12)

The candidate above was not accepted: actual saved clips exposed defects that the
geometry fixtures did not catch. Recordings in `artifacts/page-transition-polish`
reproduce the Fade desktop hole and uneven entry motion on the 4K and top-middle
1080p clips. Geometry checks alone are not evidence of correct composed pixels.

- Native video never becomes visible before its placement and clip are valid.
  The plane can measure with its video hidden. Outgoing video is hidden before
  restoring transforms, clearing its player or returning it to the inline tree.
- Fade uses a themed cover over opaque native video instead of animating its
  swap-chain opacity. Both pages use the same two-phase fade-through timing.
  Playback stays attached; there is no snapshot, frame-server or second decoder.
- Source authority resolution can proceed during navigation. Decoder creation and
  initial image loading wait for the bounded transition to settle, with cancellation
  and authority rechecks preserved. This avoids codec startup in the animation's
  UI-thread budget. The page's loading presentation remains visible meanwhile.
- The clock begins on the first presentation tick. Clip geometry is reused, and
  the media participant list is rebuilt only when the binding publication changes.
- Opt-in `WIDGETRAIL_TRACE_PAGE_MOTION=1` records at most 256 transition summaries
  per process in the existing bounded frontend diagnostic log: frame count,
  elapsed time, maximum frame gap and maximum update work. No media paths/content.

Focused validation: 19 motion policy tests, 62 native page checks (including
deferred decoder startup), and 79 existing media/fullscreen checks passed.
Fade's saved-clip frame inspection shows the desktop hole is gone.

The complete candidate built from `c9d1ad14` is under
`artifacts/page-transition-polish/candidate/0.1.0-preview.30`; both production and
developer catalog checks passed (13/17 widgets). `Reproduce.ps1` exercised both
saved clips paused and playing for all six page preferences. `Stress.ps1` repeated
the top-middle clip 20 times, including rapid Back during loading/navigation.

The old candidate's `baseline-stress.mp4` reproduces full-monitor first-frame
flashes. The same run on the corrected candidate (`final-stress.mp4`) has none.
The old video plane could become visible before its first positive native layout;
the new visibility ordering closes that interval. Peripheral frame comparison and
contact sheets are saved beside the recordings (`*-inspection.json`, `*-outside.jpg`).
These are display recordings, not just XAML geometry assertions; they requested
120 fps but encoded roughly 65–70 fps on this system, so they do not prove every
display refresh was sampled.

In the 24-transition diagnostic comparison for Slide/Fade/Vertical slide, the
median per-transition maximum UI frame gap dropped from 30.63 to 16.63 ms; worst
gap from 56.52 to 39.37 ms, and worst animation-update work from 22.02 to 0.26 ms.
The comparison includes recording overhead and different Debug/Release builds;
it is local diagnostic evidence, not a general performance guarantee. Timing
samples are in `frame-timings.json`. Film inspection also covers Reveal, Cover
slide, and None. Original Vertical slide preference was restored.

The user accepted the page animations. Integration remains pending the subsequent
shared presentation-layer correction; no merge, push, installer or release.

## Shared layering follow-up

The user found a widget modal beneath resident video and requested a general host
solution. The root layer policy, dialog portals, logical ownership bridge and
renderer-wide native-dialog motion fallback are documented in
`docs/maintainers/presentation-layers.md`. Validation covers 24 layering checks,
38 existing modal checks, 79 media/fullscreen checks and 62 page-navigation checks.
Live desktop capture confirms the delete dialog and its scrim paint above playing
video. A real pointer click on Cancel closes only the dialog, keyboard Right moves
between its actions, fullscreen transport remains clickable, and Escape from the
focused native video returns to the inline player. No capture was deleted.

The broad production-shell fixture stops at its radial Back assertion after 92
checks on both this change and unchanged `bc535391`; both results are preserved.
It is excluded from passing totals.

The complete candidate from `c088e53f` is in
`artifacts/presentation-layering/candidate/0.1.0-preview.30`. Production/developer
catalog admission passed (13/17 widgets). The trimmed candidate's modal was
visually checked over native video; its Cancel control appeared once in automation
and a real pointer click returned to the visible preview without hiding the
overlay. Evidence: `release-dialog.png` and `release-smoke.json`.
It is running in Capture Library for physical acceptance. No merge or installer.
