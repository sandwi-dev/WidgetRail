# WidgetBridge.Contracts

Shared host/bridge wire contracts and the existing length-prefixed JSON frame codec.
This library keeps the `WidgetRail.WidgetBridge` namespaces and JSON shape stable
while allowing presentation clients to reference the protocol without referencing
the WidgetBridge executable or its provider/runtime dependency graph.

Dependencies are limited to WidgetProtocol, WidgetSdk, and WidgetStyling. The last
reference supplies the existing `WrssValueKind` used by computed-style DTOs; style
resolution remains in WidgetBridge. Catalog loading, worker execution, platform
providers, native window-preview authority, and server-only authority exceptions
also remain in WidgetBridge.

The internal protocol surface is shared only with the bridge, presentation session,
and their existing verification assemblies. This is an assembly boundary change,
not a new public widget SDK or a new transport version. Rebuild managed consumers
when adopting it, since moved types now live in WidgetBridge.Contracts.dll.
