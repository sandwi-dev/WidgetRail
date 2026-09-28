# Presentation memory after native view eviction

`CapturePresentationState()` produces a lightweight `WidgetPresentationMemento`.
Capture before suspending/collapsing a visible widget; the shell can then dispose
its native view while retaining this record in its separately bounded history.
Each of the five histories is capped at 64 entries. The record contains strings,
numeric values and semantic identities only: no frames, controls, row leases,
provider delegates, artwork or native resources.

For a recreated presenter, apply the current genuine frame while inactive, call
`RestorePresentationState(memory)`, then show/resume and perform normal `Enter`.
Restore rejects a different runtime, widget instance, presentation or session
generation. It imports only matching declaration identities and query generations.
Ordinary scope focus, declared group focus and indexed item focus keep their
existing resolution rules. Already consumed author focus requests are not replayed;
a newer author request remains authoritative.

Viewport memory identifies the first visible item/element and the fraction of its
measured leading extent at the viewport edge. An indexed anchor additionally
records collection/source/query/key/index, layout kind and axis. Restore acquires
the current anchor lease through ordinary bounded range demand and validates its
key before calling native `ScrollIntoView`. The final offset is derived from the
new native layout and item extent, never copied from an old pixel coordinate.
Window resizing, wrapping columns and DPI therefore use current measurements.
Query, layout-kind, axis or item-key changes reject that anchor. Ordinary declared
ScrollViewers similarly use a matching descendant anchor and current measurements.

An adaptive grid also retains item zero through the normal bounded source. WinUI's
`ItemsWrapGrid` uses that offscreen container to measure uniform cell geometry even
when its reported native range excludes index zero. Without current content there,
deep recreation can size every row from the loading estimate. Restoration waits
for this measurement dependency as well as its semantic anchor. Both arrivals and
failures wake restoration; source replacement and disposal release the retention,
and suspension still releases provider leases through the existing source lifecycle.
This adds at most one retained index and its ordinary provider page, not a second
reader or independent cache.

Only native layout/readiness and actual slot arrival advance deferred restoration.
There is no polling, forced layout or replay queue. Explicit navigation, scrolling,
pointer input, author focus replacement, suspension and disposal cancel pending
work and release temporary anchor retention. Native entry/focus remains governed
by the shell's automatic-focus policy; memory restoration never grants new input
authority or realizes an entire collection.

Native pending navigation retains one logical target through the existing bounded
range source. A payload-ready notification can reissue that current target after
placeholder measurements change; grid-width changes do the same. Completion or
cancellation releases the retention. A navigation-settled notification then lets
viewport memory finish without depending on focus causing another layout event.
`ChangeView` is asynchronous: restoration confirms the resulting anchor using
native ViewChanged/layout events and suppresses identical pending offset requests.

A new inactive source records its first native range without fetching, including
when an independent measurement retention already exists. An already
populated suspended source continues preserving its prior demand through collapsed
layout. This distinction prevents a recreated source from waiting for a duplicate
range callback that WinUI need not send on resume.

The real indexed-worker fixture exposes `F16` for actual dispose/recreate tests:
deep list/grid focus and viewport, same-query fresh leases, consumed focus-request
suppression, resized layout, runtime/query replacement, and explicit cancellation.
Its visible `Memory` button exposes the same check through UIA. Restoration is also
checked as a preview before focus entry, so native focus cannot mask missing demand.
Two 1.5-second
inspection phases allow before/after desktop screenshots without physical input.

Validation passed 20 checks for each real list/grid recreation, the existing
20-check indexed sequence, and 150 native style checks including ordinary group
focus and ScrollViewer recreation. Before/after pixels were inspected; identical
layout requires matching first-visible identity/fraction and native anchor Y/height
within 2 DIPs. Raw virtual scroll offsets may change after eviction because native
estimates for unmeasured rows differ; restoring those offsets would be incorrect.

The real Playnite eviction regression additionally compares the focused tile's
X/Y/width/height after cycling through three other widgets, then opens its details
and verifies modal retention. A reproduced 48–52-pixel vertical shift disappeared
after retaining the native measurement item; the tested tile returned to identical
physical bounds. This check keeps its original three-pixel tolerance.
The expanded recreation probes pass 22 list and 23 grid checks, including the
inactive source's first viewport alongside independent measurement retention.
