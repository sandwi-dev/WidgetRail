# AVP-001 retained evidence

The focused source tree retains the evidence procedure, schema, thresholds, and
honest unavailable-metric declarations. Exact-commit machine output is written
to the ignored `artifacts/avp001/measurement.json` path by
`scripts/Measure-Avp001.ps1` after the milestone commit exists. The matching
copied framework-dependent `win-x64` runtime is retained beside that JSON.

The artifact separates:

- hidden-after-use CPU/private-memory after a bounded two-second settling period;
- visible idle CPU/private-memory;
- visible representative-shell private memory, the 250 MiB initial target, and
  the user's roughly 500 MiB unacceptable-region check;
- a 0.5% normalized-CPU decision check for effectively idle hidden rendering;
- cold process start to the first complete Settings frame;
- page switch to complete-frame samples for every representative destination;
- each complete frame's transparent-root and containment diagnostic; and
- exact source commit, executable SHA-256, runtime, Avalonia version, OS, and
  process architecture.

GPU frame cost is explicitly unavailable in AVP-001 because it would require a
separately authorized ETW/PresentMon measurement lane. Physical appearance,
mixed-monitor behavior, and the final visual verdict remain live planner/user
checks; the prototype does not infer them from screenshots.
