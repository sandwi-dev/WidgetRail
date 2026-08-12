# Documentation history

This directory contains immutable, timestamped snapshots of reviewer-owned
documents after their detailed historical evidence stops being useful in the
active control plane.

Snapshots are:

- evidence, not implementation authority;
- named with a filesystem-safe local ISO timestamp;
- grouped by the active document name;
- linked back to the current document;
- excluded from routine planner and implementation-agent startup reads; and
- opened only for a named historical DLV, commit, decision, or evidence chain.

Current implementation selection always comes from
[`../delivery-plan.md`](../delivery-plan.md). Current public guidance remains
linked from [the documentation index](../README.md).

Do not append unrelated hours of work to one historical file. Create another
timestamped snapshot at the next compaction boundary so Git and the directory
name preserve chronology without inflating current context.
