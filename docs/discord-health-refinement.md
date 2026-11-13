# Discord connection and incoming-stream measurements

Implemented 2026-11-12; synthetic native review passed, live provider acceptance remains.

## User flow

The call's connection-status control and Menu → Connection details open one native
modal in the main widget. It shows the current connection phase, available last and
provider-average ping, provider quality, and up to 24 local ping samples. If a screen
is selected, it separately reports incoming resolution/FPS and pin video dimensions,
playback progress and dropped-frame counters. Refresh is explicit; a bounded SDK
ticker also reads every two seconds only while this modal is actively displayed.
Closing the modal or hiding the overlay stops sampling. Reactivation starts a fresh
interval history. No observations are persisted.

Reconnect pin is an explicit action that returns to Home and reuses the existing
Watch flow. It is bound to the current account, call, visit, selected stream and
receiver lease. Reading diagnostics never joins, shares, watches, grants microphone
permission or restarts media. The incoming video pin contains no new controls.

## Provider evidence and ownership

The adapter's current-call phase path already uses `RTCConnectionStore`. Optional
`getLastPing`, `getAveragePing`, `getQuality` and `getRTCConnection` signatures were
checked against [Vencord's RTCConnectionStore definitions](https://github.com/Vendicated/Vencord/blob/d07030c0968e31e41caa0c35fda0ef8ae63a079e/packages/discord-types/src/stores/RTCConnectionStore.d.ts).
They remain feature-probed private APIs, not guaranteed support. Getter reads are
checked against the same store, call, account and connection; connecting/stale
calls do not reuse the previous connection's ping. Missing measurements are null,
not fabricated zero values. No hostname, account ID, session ID or token is returned
in the voice measurement payload.

Incoming statistics use `getRawStats` on the exact current leased stream connection,
the same provider API already used by outgoing diagnostics. They do not scan other
connections. Membership checks, relay owner/lease, selected stream, current viewer
subscription, RTC identity and account/call are revalidated after asynchronous reads.
At most one incoming provider read remains outstanding, even after its 1.5-second
response deadline, so a hung provider cannot accumulate promises with each refresh.
Reports are bounded to 256 entries and at most four incoming video rows; only
numeric measurements from the largest observed layer leave the adapter. Encoded
content, report IDs and remote addresses are excluded. Adapter build is now 21.

The receiver's separate diagnostics request requires its exact lease. Browser
`getVideoPlaybackQuality` supplies total/dropped frame counts. Their interval
difference indicates video-element playback; it is not a physical display scanout
measurement. A paused element is explicit. Advancing upstream decoded frames with
stationary local playback are distinguished from idle/waiting content. A zero-delta
picture alone never ends a receiver. Existing terminal health events remain the
authority for actual failed/closed/ended playback.

The widget uses existing SDK `WidgetResource`, `WidgetTicker`, `WidgetNavigator`
and `UI.LineGraph`. The resource owns loading/error/refresh state and publication
epochs; the navigator supplies visit cancellation and return focus. The widget
owns account/call/stream identity, optional metric interpretation and bounded
history. No new host/protocol API is needed for this addition.

## Consumer matrix

| Consumer | Result |
| --- | --- |
| Discord main call view | Connection status is actionable; menu entry opens scoped measurements. |
| Discord incoming receiver | Adds explicit lease-bound read-only measurements; existing health event behavior retained. |
| Discord outgoing share details | Existing source/encoded/advertised measurements and ownership unchanged. |
| Discord pins | No diagnostic controls or graphs added. |
| Other widgets / SDK templates | Not applicable; no shared contract changes. |

## Validation

Managed widget suite passed 440 checks in `artifacts/discord-health-managed.log`.
New cases cover bounded history, missing/invalid measurements, idle vs stalled or
paused playback, no background polling, account/visit retirement, late receiver
results, unobstructed pins and explicit single Watch retry. Provider/receiver/backend
Node tests passed 164/164 in `artifacts/discord-health-node.log`; the minified provider
and composed sealed backend also pass. Logs and binlogs remain in artifacts.

The synthetic UI fixture now returns bounded connection values so the main-window
panel can be reviewed without joining Discord. Native review should check controller
entry via connection status/Menu, graph/scroll visibility, Refresh, B/Escape/backdrop
return to the caller, and overlay hide/reopen. A live accepted test call is needed to
confirm current Discord ping availability and incoming/pin measurements. Static
content must remain idle/waiting; moving content should advance both counters.
Native review on the integrated validation frontend and trimmed production frontend
opened the details panel from the call status, displayed synthetic ping/quality/history,
invoked Refresh, and used real Escape to restore focus to the call status. The panel
was visually inspected; evidence is `artifacts/input-discord/health-native`.
These source and synthetic checks do not establish live latency or streaming quality.
