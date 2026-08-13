# AVP-002 controller dependency decision

Compared on 2026-08-13 for this isolated .NET 10 feasibility process.

| Candidate | Maintenance/deployment fit | Prototype cost and limits | Decision |
|---|---|---|---|
| `Vortice.XInput` 3.8.3 | Current stable MIT managed wrapper; targets .NET 10; about 46 KiB; no package dependencies | Simple polling and reconnect handling; limited to four XInput-compatible slots, weak identity, legacy controller coverage | Selected behind one replaceable adapter for AVP-002 |
| Microsoft GameInput 3.5.262 | Microsoft-owned modern input API with richer device identity and broad device semantics | NuGet exposes native COM rather than a managed API; PC deployment must own the GameInput redistributable installation; a correct narrow COM adapter plus deployment proof expands this prototype lane | Preferred future production evaluation, not selected for this bounded prototype |

Sources: [Vortice.XInput package](https://www.nuget.org/packages/Vortice.XInput),
[Vortice.Windows changelog](https://github.com/amerkoleci/Vortice.Windows/blob/main/CHANGELOG.md),
[Microsoft.GameInput package](https://www.nuget.org/packages/Microsoft.GameInput),
[GameInput versioning and redistributable](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/input/overviews/input-versioning),
and [GameInput NuGet deployment](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/input/overviews/input-nuget).

This choice validates one physical-controller path; it is not an endorsement of
legacy XInput as the production architecture. The adapter exposes only semantic
input and can be replaced without changing router, focus, tray, or page state.
