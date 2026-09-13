# Investigate performance

Measure the operation that feels slow before choosing an optimization. Smooth
right-stick scrolling, D-pad response, and a resize transition can exercise
different paths even when they show the same content.

## Reproduce one workload

Record the build, widget, display scale, controller input, and whether content
was already cached. Compare first-use and warm behavior. Include background
activity such as music playback if it changes the reproduction.

Useful workloads include scrolling an unvisited poster grid, navigating during
playback updates, resizing between routes, and reopening a retained widget.
Do not combine all of them into one unexplained average.

## Separate the costs

| Stage | Investigate |
|---|---|
| Worker | Data fetching, action handling, and view creation |
| Bridge | Snapshot validation, style resolution, and transport |
| Preparation | Text measurement, layout, comparisons, and retained state |
| Drawing | Damaged regions, composition, images, and animation |
| Input | Queueing, admission, focus targeting, and scroll demand |

A complete snapshot is not a requirement to prepare or redraw everything.
Use the existing presentation-impact and damage paths. Retain unchanged styles
and text layouts with correct invalidation. See [Renderer preparation](renderer-preparation-retention.md).

## Common causes

Repeated image decoding can look like a scrolling problem. Check cache misses,
evictions, in-flight requests, and which visible images are protected before
increasing a memory limit.

Page replacement can shift content if scroll-offset compensation is missing.
Background changes can flash if a retained transition is discarded incorrectly.
Focus changes can trigger extra work even when the viewport did not move.

Playback progress should not force unrelated catalog discovery. Coalesce visual
updates where safe, while preserving action results and snapshot consistency.

## Hidden does not always mean idle

A hidden main overlay may still have a pinned view, playback session, controller
shortcut listener, or retained worker. Attribute work to the feature that needs it.
Do not terminate a valid session just to make a single process's memory graph smaller.

Conversely, retained workers should not poll providers without valid lifecycle
and consent. [Residency](../reference/widget-residency.md) explains that distinction.

## Validate an optimization

Compare the same before/after workload and keep the exact build identity with
the results. Include frame/response latency, preparation time, CPU, memory, and
cache behavior as relevant. Separate cold-start costs from steady state.

Check correctness: focus, offscreen artwork, paging, resizing, themes, and
background transitions are common regression points. An older local benchmark
is not a performance promise for current releases or different hardware.

Find diagnostics through [Diagnostics and recovery](diagnostics-and-recovery.md)
and scoped test commands in [Contributing](../../CONTRIBUTING.md).
