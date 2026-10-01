# Modal visual ordering regression

Opening Playnite details replaced the first root raster while retaining later
siblings. `WidgetCompositionPresenter::ReconcileChildren` used `AddVisual(child,
FALSE, nullptr)` for index zero, placing the replacement **above** all existing
siblings. Its local order cache nevertheless recorded it first (backmost), so
subsequent unchanged scenes never corrected the actual compositor order. The
background obscured the open modal. Dismissal captured the modal separately,
which explains its brief appearance on B.

DirectComposition's null-reference rule differs from inserting relative to an
existing visual: TRUE with nullptr means below all siblings. TRUE with a
reference means immediately above that sibling. The reconciler now uses TRUE
for both cases. This preserves incremental reuse without clearing the tree.

Reference: https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositionvisual-addvisual

Validation (2026-09-27):

- Actual Playnite achievements/details export rendered through a real
  DirectComposition HWND at 125% reproduced the invisible-open/visible-dismissal
  symptom. The same probe displays the open modal after the correction.
- `OverlayChromeTests.exe --widget-ordering-pixels` replaces the leading
  background raster while retaining siblings, checks open/update/dismiss/reopen,
  and requires unchanged scenes to issue no visual additions/removals.
  Old presenter: FAIL, modal pixels are above their backdrop. Fixed: PASS.
- Default OverlayChromeTests: 50,411 checks passed.
- The broader `--widget-motion-pixels` run stopped at its existing timed DWM
  progress assertion before reaching modal coverage. It is not counted as passed;
  the independent ordering entry point avoids relying on that timing assertion.
- Local probe sources, screenshots, build logs, and old/new results are under
  ignored `artifacts/modal-presentation/`. Physical acceptance is pending.
