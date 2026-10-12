# Page navigation and live media transitions

Status: implemented; candidate and physical validation in progress (2026-10-12).

## Contract

- Page navigation declares a stable region, destination key, and Forward/Back/Replace operation.
- Section changes continue using sibling ordering. Loading and content updates retain their destination key.
- Page effects: Slide (default), Vertical slide, Fade, Reveal, Cover slide, None.
  Replace uses a neutral fade. Speed and reduced motion remain shared host preferences.
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
- [ ] Native visual reproduction, final candidate and physical handoff

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
