# AVP-002 retained evidence

`scripts/Measure-Avp002.ps1` writes exact-commit machine output to the ignored
`artifacts/avp002/measurement.json` path after the milestone commit exists. A
copied framework-dependent `win-x64` runtime is retained beside it.

The artifact separates visible and hidden CPU/private memory, cold first-frame
time, every destination switch, the 250 MiB comparison target, and the roughly
500 MiB unacceptable-region check. It records exact commit, executable SHA-256,
runtime/Avalonia versions, OS, architecture, controller dependency, all complete
frames, every required authored Button/TextBlock bound, ScrollViewer clipping,
and transition start/midpoint/completion surface samples.

The transition verdict is deliberately scoped to Avalonia visual/composition
state and brush coverage. Physical controller behavior and the Windows
compositor/transparency verdict remain planner/user evaluation; GPU cost is
unavailable without an authorized ETW/PresentMon capture.
