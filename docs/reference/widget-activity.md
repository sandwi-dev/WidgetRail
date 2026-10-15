# Widget activity and icon badges

WidgetRail shows status on widget icons in the rail and radial menu. These are
host adornments: they do not add focus targets, change widget selection, or wake
workers. Existing shell theme colors and high-contrast outlines apply.

## Display

A pending notification count appears at the upper right, capped visually at `99+`.
The full count remains in the accessible name and tooltip. Banner-only notices
never contribute. These are WidgetRail pending notifications, not a website's
complete unread count; opening a widget does not dismiss them.

One activity symbol appears at the lower right. Priority is **Stopped, Recording,
Needs attention, Playing audio, Remote playback**. Stopped state suppresses stale
activity while retirement completes. The normal radial shows the same two badges.
When the radial is narrower than 320 DIPs, it shows one priority badge: stopped,
recording, failure, notification count, audio, remote playback. All applicable
statuses remain in its accessible name and tooltip. Rail tiles below 50 DIPs also
use a single badge; exceptionally small tiles below 32 DIPs retain status in their
accessible name and tooltip without squeezing in unreadable adornments. There is no continuous badge
animation.

## Automatic observations

- Host browser and embedded-media WebViews use `IsDocumentPlayingAudioChanged`
  and `IsMutedChanged`. Main pages and live hosted pop-outs contribute to the same
  widget, including passive pins and hidden playback. No DOM inspection or timers
  are added for detection.
- Native media uses its existing player state, mute and volume observations. It
  reports playing and unmuted playback with nonzero volume; this is not a sound
  level meter or a guarantee that the source contains audible samples.
- The host owns pending counts, stopped state and failure state. Widget activity
  reports cannot replace these facts or hide host-observed audio.
- Screen sharing is **not inferred from permission or chooser opening**.
  WebView2 exposes `ScreenCaptureStarting`, describing a request, without a
  matching public capture-ended/state event in the supported SDK. A canceled
  chooser or a stream ended by the website would make a latched badge misleading.
  Automatic website-sharing badges are deferred; no page-specific probes are used.

## Ordinary widgets

`HostServices.Activity` is an intrinsic service for sandboxed and full-trust
widgets in Background, Visible or Interactive state. It needs no manifest
permission, does not retain a worker, and uses no Windows notification UI.

Each component owns a named contribution:

```csharp
await HostServices.Activity.SetAsync("player", new WidgetActivity(LocalAudio: true), token);
await HostServices.Activity.SetAsync("recorder", new WidgetActivity(Recording: true), token);
await HostServices.Activity.ClearAsync("player", token); // Recording remains.
await HostServices.Activity.ClearAsync("recorder", token);
```

Use `RemotePlayback: true` when controlling playback elsewhere rather than
producing local audio. Framework-managed browser/native media needs no manual
reporting. YouTube Music's standalone player reports its own playback. Capture
Library reports only after its capture reaches the actual Recording phase and
clears the contribution on completion, cancellation or failure.

Sources are internal component identifiers, not displayed text. A widget may
have at most 16 nonempty contributions. IDs contain 1–64 ASCII letters, digits,
dots, hyphens or underscores. Empty activity is equivalent to clearing that
source. Clearing one of several audio sources leaves audio active. Concurrent
instances of an operation should use distinct source IDs and clear their own ID
in `finally`; clearing one instance must not erase another.

The SDK atomically combines contributions into one immutable boolean snapshot.
Source IDs never leave the SDK. Identical state shares delivery; bursts coalesce
with at most one in-flight request and one replaceable pending aggregate, spaced
by at least 100 ms. A superseded pending aggregate completes with
`Superseded=true`, not an error; its source contribution still participates in
the newest aggregate unless explicitly replaced or cleared. Do not retry an old
aggregate because it was superseded. `Accepted` means host admission, not proof
that the badge is currently visible. Optional reporting failures must not stop
playback or recording.

Cancellation before submission changes nothing. After submission, cancellation
only cancels the caller's wait; it cannot undo an already delivered update.
Always clear a component's contribution during its normal cleanup, using a
bounded cleanup token if the operation token was canceled.

The broker validates and authenticates reports, suppresses duplicates, and bounds
changed nonempty publications to 20/s per worker; empty cleanup is always
admissible. No widget can choose another owner, badge artwork, count or priority.
Reports carry exact worker-run identity. Stop, crash, idle unload and replacement
clear activity; late publications or exit events from older runs cannot revive or
clear the replacement's state. Nothing is persisted as ongoing activity across
application restart.

## Extension contract

Activity is separate from notification content and rendering. Add future semantic
activities through an explicitly compatible, versioned capability contract;
current `widget.activity.v1` accepts only local audio, remote playback and
recording. Do not silently send new fields to an older host. The shared host
status model chooses density and priority, so adding a source does not require
separate rail and radial implementations. Custom colors, labels and priorities
are intentionally outside widget authority.
