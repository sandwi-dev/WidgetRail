# WinUI host-owned fullscreen media

> Implementation and validation record. Pending work, candidate state and test
> counts describe the checkpoint where they appear; later integration may
> supersede them. Use [current platform status](current-platform-status.md) and
> [WinUI authoring](../reference/winui-authoring.md) for the current release contract.

The existing SDK action `host.embeddedMediaSession.enterFullscreen` remains the
entry declaration. The widget declares `OverlayFullscreen` capability; it does
not hold fullscreen state. A trusted host presentation moves the existing admitted
browser between its ordinary viewport and an aspect-fit fullscreen viewport.

## Session admission

`EnterEmbeddedMediaFullscreen` accepts an exact session-published displayed frame,
its captured action and the resident admitted document. It applies the same
origin/current binding checks as ordinary actions, including scope, availability
and action identity. The host action never becomes a worker action. Normalized
controller shortcuts resolve through `ResolveEmbeddedMediaHostInput`, which
uses the same shortcut resolver and input-origin validation as ordinary input.

The returned `WidgetMediaPresentation` is an opaque receipt. Its capability/scope
epoch is captured at frame publication. Compatible metadata, aspect-ratio and
playback updates retain it. Removing the capability, changing scope, removing or
replacing the document, runtime replacement and session retirement invalidate it
permanently. Removing then reintroducing a capability cannot resurrect an old
receipt or admit input captured before removal, even when frontend dispatch skips
the intervening publication. Document lifetime remains independent: leaving a
capable route may close fullscreen without restarting the browser or audio.

These checks do not prove native ownership. Before entry the frontend must prove
the exact document is resident in its visible ordinary viewport, that interaction
is admitted and no pinned presentation owns it. Hide, widget switch, deactivation
and pinned takeover retire the frontend request. B returns to the ordinary widget;
host playback controls use the existing adapter with no invented widget command
sequence.

Fullscreen title, command labels and semantic controller glyphs register with the
shared shell style owner. Theme typography, Bold Text and text scale therefore
use the same native style adapter as other shell chrome. Interface zoom remains
applied once by the containing shell scale root; media aspect-fit geometry stays
independent of text size.

## Native ownership and shell

2026-09-29: fullscreen chrome overlays rather than reducing the video viewport.
The fullscreen root is transparent, matching the original renderer; the desktop
backdrop remains independent. Fullscreen and compact controls share a three-second
idle timeout. A or directional input first reveals hidden controls and restores the
remembered command without firing it. Pointer hover keeps the controls available;
direct transport shortcuts retain their existing authority. Passive compact pins
hide all chrome. Their XAML island attaches its transparent backdrop only after
Content exists. Native media119 and the actual local sample cover these paths.

Cold startup remains lazy by explicit user choice. Stage timings are recorded as
`media-startup` in the existing diagnostics, off the UI thread. The initial native
WebView2 controller measured about 5.2 seconds on this machine; media admission was
about 38 ms. A loading indicator makes initialization visible. Moving an existing
browser between presentations avoids paying that controller startup cost again.

The shell now provides one `MediaFullscreenView` with a native aspect-fit viewport,
responsive native buttons and semantic controller glyphs. The existing media owner
moves the same browser into that destination and returns it on exit; it neither
resolves the package again nor constructs another controller. The main widget stays
mounted but disabled behind the fullscreen surface. Existing display placement and
zoom policies supply the available work area; ordinary shell chrome returns on exit.

B returns to the widget, View returns to the tray, X toggles playback and triggers
seek using the declaration's bounded step. A activates the focused native guide
button. Releases from host-owned presses remain consumed after exit, so they cannot
activate a newly revealed widget shortcut. Pointer/keyboard controls call the same
host operations. Pausing and seeking use command-sequence zero; resuming uses the
existing trusted arm/activate handshake. The package's monotonic command lineage
is not advanced by host controls, and busy commands are consumed without a queue.

Validation: 189 session tests pass, including 13 fullscreen admission cases for
compatible updates, genuine frames, unavailable bindings, shortcut resolution,
scope/capability removal and reappearance, document retirement and disposal. This
does not establish physical fullscreen acceptance. The integrated native media
fixture passes 99 checks, including the actual fullscreen view, aspect fit, narrow
guide layout, controls inside the real window, same-browser transfer, trusted
pause/resume, capability loss, input deactivation and hide/reopen. Inspected
fullscreen/popup screenshots and results are in `artifacts/winui-media/fullscreen-visible/`.
The shell policy suite passes 24 tests and the analyzer build is clean. Real provider
fullscreen, production shell routing/placement and physical controller acceptance
remain to be verified together. Pinned ownership transfer remains separate work.

## Native browser failure while fullscreen

A native browser failure closes its controller but does not invalidate the
session's document/capability epoch. The owner therefore explicitly exits that
widget's fullscreen presentation before publishing the persistent media failure.
This lets the existing shell callback restore ordinary chrome before its recovery
message and Retry focus are requested. It does not resolve resources again,
recreate the failed browser, replay playback commands, or clear the failure.
Explicit document retirement/replacement remains the recovery boundary.

The owned native fixture first reproduced the blank-fullscreen defect at baseline
`539ce9b2`: prohibited navigation faulted the real WebView2, but fullscreen did not
exit. After the owner correction, all 105 checks passed. The regression verifies
one exit, ordinary placement restored before failure notification, an unchanged
widget document declaration, persistent failure, and no resource readmission or
playback-command replay on refresh. Existing checks then remove and replace the
document and confirm fresh-browser recovery. Both analyzer builds had zero warnings
and errors, and both fixture processes completed normal shutdown. Fullscreen and
popup captures were inspected.

Evidence: `artifacts/winui-media/fullscreen-fault/{red-native,green-native}` and
the adjacent unique build binlogs and `evidence.json`. This uses the fixture's
sealed assets and prohibited-navigation fault; it does not establish native
process-crash/GPU-loss recovery, real-provider behavior, or physical acceptance.
Production Retry placement/focus follows the source-reviewed shell callbacks;
the native regression asserts the owner's notification and placement order.

## WebView process failure and automatic recovery

The native ProcessFailed event does not always mean that the WebView/document has
died. WebView2 automatically recovers GPU and utility processes; its sandbox-helper
and PPAPI subprocess failures are documented as nonfatal. The frontend logs these
notifications without retiring the controller or revoking its media document.
Browser/main-renderer/frame-renderer, unresponsive and unknown failures retain the
existing explicit error/recovery path. Adapter command deadlines remain active.

Diagnostics now include enum process kind, enum reason, exit code and whether
WebView2 owns recovery. They omit process descriptions, URLs and media titles.
The same classification issue existed in the original renderer; the WinUI host
uses the documented WebView2 behavior rather than retaining that limitation.
Reference: Microsoft.Web.WebView2.Core.xml shipped with package1.0.4078.44,
CoreWebView2ProcessFailedKind GPU/utility/sandbox-helper/PPAPI entries.

Opt-in validation: `Test-WinUiNativeControls.ps1 -Fixture embedded-media
-ProcessFailures -UsePlatformActivation -OutputDirectory <fresh-directory>`.
This fixture requires exclusive frontend ownership, obtains process identities from
its own CoreWebView2Environment inventory and verifies msedgewebview2 before ending
only its owned GPU/browser subprocess. It does not target a system GPU driver,
user browser, real provider or physical controller workflow. Normal media checks
never inject process failures.

Evidence: `artifacts/winui-shell/media-process-20260930/`. The baseline reproduces
unnecessary media retirement on a genuine GPU exit; corrected native139 passes.
GPU recovery preserves the same browser/document/admission and responsive rendered
pixels. Fatal browser exit while fullscreen restores the ordinary recovery surface,
does not replay commands, and explicit replacement creates one fresh ready browser.
The existing resident-capacity and eight early-retirement checks also pass. This
verifies native subprocess recovery, not arbitrary graphics-device removal.
