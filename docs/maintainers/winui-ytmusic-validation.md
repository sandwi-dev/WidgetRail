# YouTube Music WinUI validation

The standalone service returns a bounded complete page (generally up to 500
entries). The migration publishes that page as an immutable indexed query, with
contiguous Home section metadata over one grouped grid. Library, search, details
and queue use native indexed lists. This does not add remote continuation.

Rows capture their query and occurrence; duplicate song IDs remain distinct.
Actions no longer recover items from current array indexes supplied by a parent
action. Queue selection additionally checks the service's captured queue identity
under its state lock before changing the selected index. Progress ticks do not
change the query or row content revision. Back returns through an exact indexed
target. Independent tabs and Library filters retain their query identities.

## Evidence, 2026-09-28

- 28 standalone tests pass, including deep/random access, duplicate occurrence
  actions, replacement rejection, filters, section retention, controller shortcuts,
  account changes and background queue replacement. Compiled parent and 32-row
  range styles remain under normal protocol budgets for a 500-item query.
- The sealed package builds and validates through the normal package CLI. The
  tested artifact is `artifacts/winui-ytmusic/indexed-verified/`; it was installed
  only in the isolated migration catalog, not the user's installed product.
- First native launch crashed during XAML measurement. Native style projection
  had overwritten ProgressRing's intrinsic finite size with Auto. The corrected
  size fallback allows real startup; Compact/Standard/Large native checks pass.
- Actual Home revealed square poster declarations were using the portrait ratio.
  `aspect-ratio` now reaches the native poster panel. All 56 native style checks
  pass, including square ratio and loading geometry.
- `scripts/Test-WinUiMusic.ps1` passes five checks against the real package:
  startup without Retry, grouped Home square geometry, Library/list navigation,
  retained Home return and shutdown. Screenshots in
  `artifacts/winui-ytmusic/integrated/` show actual service artwork. The script
  never activates songs/playlists or changes account state.

Native artwork/structure/navigation are demonstrated, not full product acceptance.
Focused poster clipping and duplicate root/container size constraints remain under
review. Complete animations, pinned surfaces, sustained scrolling performance,
playback and physical controller behavior still need integrated verification.
