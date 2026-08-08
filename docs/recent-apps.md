# Recent Apps reference

Status: read-only compatibility slice; scheduled to be replaced by the
catalog-backed Games & Apps launcher

Recent Apps is a first-party public-SDK widget backed by a trusted Windows
foreground-activity provider. It demonstrates how a widget can consume a
privacy-bounded event stream without gaining process or window authority. It
does not switch, restore, close, or launch an application.

## Implemented behavior

- Observation starts lazily after the first authorized recent-activity read.
- WinEvent foreground and destroyed-window notifications drive updates; there
  is no timer polling, UserAssist/registry read, or Xbox service dependency.
- The provider retains at most 16 eligible, still-running top-level
  applications and publishes full coalescible snapshots.
- The worker receives a bounded display name, a random opaque ID scoped to one
  process lifetime, conservative `Application` kind, and running/most-recent
  markers. It does not receive a PID, executable path, command line, HWND, or
  raw process key.
- The controller surface is one bounded vertical Scroll. Stable opaque IDs
  preserve selection across reorder/removal events.
- Rows are focusable local selections so controller navigation can reveal the
  complete list. A row action changes only the widget's selected styling; no
  host or Windows control operation runs.

The first slice does not launch or switch an application. It does not classify
an entry as a game without an authoritative signal, so current Windows
observations are intentionally labelled as applications. The planned Games &
Apps replacement will use authoritative installed-library sources, icons,
grouping, and explicit launch contracts rather than foreground-window history.

## Capabilities and enforcement

| Manifest capability | Surface | Lifecycle |
| --- | --- | --- |
| `system.activity.recent.read.v1` | `HostServices.RecentActivity.GetRecentAsync`, acknowledged subscription, and full-snapshot events | Visible or Interactive |

The package declares only the read capability. Declaration does not grant
access. The user must allow it in Settings, then the
authenticated broker rechecks package/publisher/instance identity, declaration,
consent, and lifecycle for every operation/event. The public SDK and broker
vocabulary contain no recent-activity control operation.

## Lifecycle and current hardening limit

The widget opens an acknowledged subscription before fetching its snapshot in
each Visible/Interactive active lifetime, then cancels that work on lifecycle
exit. Background capability use is denied by the broker. Consent loss closes
delivery and cancels in-flight reads.

Provider observation itself starts on the first authorized read and currently
remains event-driven until bridge/backend disposal. Immediately stopping the
native observer and clearing its bounded in-memory history when read consent is
revoked is future hardening. This limit does not make the retained data or
native window/process identifiers available to the revoked widget.

## Evidence and remaining gates

The focused Recent Apps widget, Windows activity provider, and PlatformBroker
Release suites cover this contract.

The tests cover lazy no-poll startup, ordering/deduplication, bounds, sanitized
identity, process-lifetime token rotation, destroyed/stale removal, exact live
window pruning, permission/channel failures, lifecycle cancellation, absence of the retired
activation capability, and the real WinEvent adapter smoke. Packaged
controller/visual testing, broader Windows app types, observer shutdown on
revocation, and measured hidden-state resource evidence remain open.

See [widget capabilities](capabilities.md), [roadmap](roadmap.md), and
[security and trust](security-and-trust.md).
