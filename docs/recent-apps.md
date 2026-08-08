# Recent Apps reference

Status: first locally testable vertical slice implemented; not a system-wide
history service, game detector, or arbitrary launcher

Recent Apps is a first-party public-SDK widget backed by a trusted Windows
foreground-activity provider. It demonstrates how a widget can consume a
privacy-bounded event stream and request one narrow host action without gaining
process or window authority.

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
- Optional activation restores a minimized window when needed and calls the
  Windows foreground switch only after revalidating that the opaque observation
  still maps to the same live process/window.

The first slice does not launch an application that has exited. It does not
classify an entry as a game without an authoritative signal, so current Windows
observations are intentionally labelled as applications. Icons, grouping,
authoritative game classification, durable history, and a reviewed relaunch
contract remain roadmap work.

## Capabilities and enforcement

| Manifest capability | Surface | Lifecycle |
| --- | --- | --- |
| `system.activity.recent.read.v1` | `HostServices.RecentActivity.GetRecentAsync`, acknowledged subscription, and full-snapshot events | Visible or Interactive |
| `system.activity.recent.activate.v1` | `ActivateAsync` for one current opaque observation | Interactive only |

The package declares read as required and activation as optional. Declaration
does not grant access. The user must allow each capability in Settings, then the
authenticated broker rechecks package/publisher/instance identity, declaration,
consent, and lifecycle for every operation/event. The trusted provider performs
the final live-window/process validation; Windows can still deny a foreground
switch.

Activation authority cannot start observation by itself. A usable opaque ID
can exist only after an authorized read. Unknown, expired, destroyed, or
process-replaced IDs fail as `resource_not_found`; a refused foreground switch
fails as `activation_denied`. The widget rolls back busy feedback and refreshes
the authoritative snapshot.

## Lifecycle and current hardening limit

The widget opens an acknowledged subscription before fetching its snapshot in
each Visible/Interactive active lifetime, then cancels that work on lifecycle
exit. Background capability use is denied by the broker. Consent loss closes
delivery, blocks activation, and cancels in-flight requests.

Provider observation itself starts on the first authorized read and currently
remains event-driven until bridge/backend disposal. Immediately stopping the
native observer and clearing its bounded in-memory history when read consent is
revoked is future hardening. This limit does not make the retained data or
activation available to the revoked widget.

## Evidence and remaining gates

The focused Release suites currently pass:

- Recent Apps widget: 8/8;
- Windows activity provider: 10/10; and
- PlatformBroker: 32/32, including lifecycle/consent cancellation of in-flight
  requests.

The tests cover lazy no-poll startup, ordering/deduplication, bounds, sanitized
identity, process-lifetime token rotation, destroyed/stale removal, exact live
activation, permission/channel failures, lifecycle cancellation, and the real
WinEvent adapter smoke. Packaged controller/visual testing, broader Windows app
types, foreground-denial cases, observer shutdown on revocation, and measured
hidden-state resource evidence remain open.

See [widget capabilities](capabilities.md), [roadmap](roadmap.md), and
[security and trust](security-and-trust.md).
