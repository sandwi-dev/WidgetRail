# Retained presentation lifetime

`WidgetViewPresenter.SetPresentationActiveAsync(bool)` is a host lifetime seam,
separate from visibility, focus ownership and worker lifecycle. The shell serializes
calls on the WinUI dispatcher. Suspend immediately revokes input, automatic entry,
scroll and new artwork demand, then awaits outstanding range/continuation/artwork
retirement. Await it before sending the worker to Background.

The shell can keep the presenter in a stable Grid with `Visibility.Collapsed`.
Establish the foreground worker presentation, apply its current frame while the
presenter is inactive, show it, then await `SetPresentationActiveAsync(true)`.
Enable automatic focus and call `Enter` only for the current shell selection.
Resuming before a suspension drain finishes is a contract error. Disposal remains
terminal and is required on cache eviction, authority replacement and shell close.

An unchanged indexed query retains its native ItemsSource, logical slots, item
keys, count, declared revision and remembered focus. Suspension cancels fetches,
releases admitted page leases and ignores range callbacks produced by collapsed
layout. Last pixels and geometry remain as presentation only. Resume reacquires
the saved visible/tracked demand even if the content revision did not change;
it does not manufacture a revision, reset the collection or reuse retired input
authority. Late provider completion is drained and released without admission.
New runtime/query identity still follows normal source replacement rules.

Rows and focus fragments pause unfinished artwork; retained decoded pixels remain.
Old row leases are never resumed as current authority. Fresh row admission updates
the same slot and restarts its presentation demand. Media viewports are unbound,
not terminally retired; preview surfaces release their capture resource and are
configured again after resume. The shell owns durable media documents/controllers.
Global/static package-icon caches retain their existing process-wide policy.

Native validation uses `F15` in the real indexed worker fixture. It collapses the
retained presenter at item 70, crosses actual Background/Interactive lifecycle,
reacquires leases, and checks source/slot identity, unchanged revision/count,
zero structural resets, remembered focus and the deep native scroll offset.
The controlled source scenarios additionally exercise cancellation-ignoring
providers, stale successful replies, exactly-once release and repeated suspension.

Validation: 14 controlled ownership checks passed; real list and grid preserved
item 70 across collapse/show with offsets 3296→3296 and 1084→1084 respectively.
The existing 20-check indexed regression passed. Its preceding run reached the
correct grouped-list destination with one timing stall (`last:60;stalled:1`);
the bounded rerun passed without an input/navigation change. These functional
checks do not establish a frame-time or controller-performance claim.
