# Application captures and Game Help

Game Help is a `full-trust-application-v1` application. Its own package implements
Gemini networking, model selection, request/response parsing, Google attribution,
and encrypted credential storage. There is no Gemini capability, client or model
contract in the shared host, broker, protocol or SDK.

The shared framework supplies ordered windows, confirmed capture, native media,
intents and restricted document presentation. These services are available to both
sandboxed and full-trust widgets with the same permissions and ownership checks.

## Capture

For foreground capture, declare only `system.apps.windows.capture.v1`.
No application list or window selection is required:

```csharp
var supported = await HostServices.Capture.GetCapabilitiesAsync(cancellationToken);
var options = new CaptureSessionRequest {
    Recording = new RecordingOptions(1280, 720, 15, 3_000_000, 10),
    Preparation = new CapturePreparation(3),
    IncludeCursor = false,
};
var validation = await HostServices.Capture.ValidateAsync(options, cancellationToken);
if (!validation.Supported) { /* Explain validation.ErrorCode; do not silently substitute. */ return; }
var ticket = await HostServices.Capture.StartAsync(options, cancellationToken);
var status = await HostServices.Capture.GetStatusAsync(ticket, cancellationToken);
// Once Recording, StopAsync(ticket, token) retains it; CancelAsync discards it.
```

The host asks for confirmation, completes its release-aware hide transition,
shows the requested preparation countdown (zero to thirty seconds), then resolves the foreground application
once and captures that window. It does not activate or restore a selected target.
Game Help uses this flow and has no application picker or window-list permission.
The desktop, shell and WidgetRail's own windows are not foreground capture targets.
Screenshot dimensions/format and recording dimensions, frame rate, bitrate,
encoder and maximum duration are separate typed options. Query the host's actual
advertised recording envelopes and revalidate before starting. Unsupported
combinations fail explicitly. PNG and H.264 profiles are discovered on this PC.
Qualified video envelopes are software 720p/15fps and hardware 720p/15fps,
1080p/30fps, 1080p/60fps and 4K/30fps; only successful native probes are advertised.
Audio defaults to None. When advertised, Application audio includes the captured
process and its child processes, encoded as 48 kHz stereo AAC at 128 kbps.
System-wide and microphone audio are not supported. Cursor inclusion is configurable.

Encoder discovery runs once on demand, off the UI thread, without recording a
screen or writing a file. Startup and screenshot-only requests do not initialize
video encoders. Each envelope checks its minimum and maximum bitrate through the
actual Media Foundation pipeline. The hardware path verifies the selected MFT's
hardware identity and fails if Windows chose software. Capture startup initializes
the exact requested output again; a runtime failure never silently changes encoder
or quality. Discovery is cached for the frontend session.

Audio discovery initializes process loopback against the host's own process only;
it does not capture another application's audio or retain PCM. Application audio
is advertised only if process capture and AAC combined with every advertised
video envelope initialize successfully. Actual recording uses the fixed video
target's process identity. Consent and the recording indicator explicitly name
audio inclusion. `CaptureLimits.AudioBitrate` is included in byte-budget validation.
Game Help keeps its silent defaults.

Video samples use the newest available WGC frame; older queued frames are retired
to avoid adding latency at lower recording rates. Audio and WGC use a common QPC
epoch. Audio is committed only through encoded video, with bounded pending PCM
and silence for gaps. Early stopping cannot leave a long audio tail. A discontinuity
or buffer overflow fails the recording rather than silently switching to system
audio or returning corrupted sound.

Hardware frames remain on the GPU: BGRA capture/output is converted to NV12 with
the D3D11 video processor. Three tracked NV12 surfaces bound queued frames and are
reused only after the encoder releases each sample. Backpressure drops scheduled
output slots instead of growing memory; metadata reports encoded/missed slots.
These counts do not claim that every source-game frame was sampled.
Target loss, replacement, resize, protected content, or cancellation terminates
capture; another window is never silently substituted. Reopening the overlay
finishes and retains configured recordings. During preparation it cancels instead.
The overlay does not reopen automatically.

This is the only source-selection mode. Requests never accept a window ID
or application name. Window selection and application activation are not part of
the capture service. The native layer binds the foreground window identity once
when capture starts and retains it until that screenshot or recording finishes.

Initiation requires interactive lifecycle. Discovery/validation never starts a
recording or opens a dialog. Status, stop, cancellation, discard and
authorized reads can continue while the overlay is hidden. Use a widget-lifetime
operation for status polling. Preview permission alone does not grant capture.

Attachments contain opaque IDs and bounded metadata. New captures include actual
source dimensions and, for recordings, requested frame rate, encoded/missed frame
counts, encoder mode and audio mode. Configured media requires protocol 79.
Use `CopyContentToAsync(attachment, destination, token)` for bounded streaming;
`ReadContentAsync` remains a convenience that allocates the whole encoded file.

Configured captures have a requested byte budget up to 128 MiB, and recordings
have a maximum duration up to 120 seconds. Admission checks estimated encoded
size plus container overhead; the native writer and completion recheck actual
size. Screenshot bounds preserve aspect ratio without upscaling; null dimensions
request the exact original physical pixels, up to 33,554,432 pixels. The host
verifies returned dimensions rather than accepting silent downscaling.
See [Capture Library](capture-library.md). The host permits four
attachments per widget, sixteen total, and 256 MiB of temporary media. Pending
requests reserve their declared maximum output bytes before confirmation, so
concurrent library imports cannot consume recording capacity. Request leases include preparation and maximum recording duration plus a
two-minute confirmation/finalization allowance; completed captures expire after thirty minutes.
Revocation, worker retirement and discard remove the owning captures. Failed
deletes are retried; expired unlocked capture directories from a crashed host
are cleaned on a later host run.

Game Help explicitly requests a 1920x1080-bounded PNG or five-second 1280x720/15fps
silent clip with an 8 MiB budget and five-second preparation. It chooses an
advertised hardware encoder when available, retaining the same lightweight format.
These are its own
preferences, not framework constants for every widget. Send remains a separate
explicit action after preview/question review. Capture Library uses the same
configured service with original-resolution screenshots.

Legacy `RequestAsync(kind)` and original-resolution overloads remain available
with their original defaults and attachment bounds. They do not silently gain
new format/size behavior, and reopening the overlay cancels legacy requests.

## Native media element

```csharp
UI.CapturedMedia(status.Attachment!, "context.preview", "Captured game context")
```

This is an inline element: use it in ordinary layouts, a widget modal, or an
authored pinned layout. It accepts only host-issued attachments belonging to
the current worker. Images use native image decoding; videos use WinUI
`MediaPlayerElement` and Windows `MediaPlayer`, muted and without autoplay.
A plays/pauses, LT/RT seek with repeat, and LB replays. B retains the normal
widget/modal meaning. Inactive views pause; disposal releases the player.
The host rechecks displayed-frame, worker and attachment authority, including
permission revocation after pixels have loaded.

For a modal, compose the same element with Send, Retry and Discard actions.
Capture consent does not imply consent to upload.

## Restricted documents

Declare `presentation.documents.v1`. Both execution models can publish bounded
HTML for display in the existing native browser element:

```csharp
var document = await HostServices.Documents.CreateAsync([html], cancellationToken);
var element = UI.ProviderContent(document, BrowserInteractionMode.ActivateToInteract,
    "document", "Search suggestions");
// When no longer needed:
await HostServices.Documents.DiscardAsync(document, cancellationToken);
```

This is a general presentation service. The host does not recognize Google,
Gemini or a game-help response. Treat supplied HTML as untrusted: scripts, forms,
frames, downloads, permissions and background network requests are blocked.
User-selected HTTP(S) links retain their destination through the ordinary external
browser handoff. There is no address editor. Controller pointer/scroll/zoom and
the specified A/B interaction mode are retained.

Protocol 69 uses opaque, worker-owned references. Each document accepts at most
five snippets, 64 Ki characters and 64 KiB of UTF-8 content; the registry retains at most 24 per owner and
64 total. Revocation and worker retirement invalidate documents. Native instances
are created only near the visible viewport and share the four-controller browser
budget. Default inline height is 180 logical pixels, styleable by the author.

## Game Help application

The application owns `GeminiGameHelpClient` and sends requests directly to Google's
fixed Interactions endpoint using its own HttpClient. No redirects or automatic
retries are enabled. Gemini's model, structured schema, provider errors, optional
grounding, and citation parsing all live under `samples/GameHelpWidget`.

The current model is `gemini-3.5-flash-lite`. Its structured response separates
`hint` from `detailedSolution`; only the hint is initially rendered. Each message
can expand/collapse its solution locally without an API call or a new chat turn.
The prompt requires plain text and keeps spoilers out of the hint for Hints first.
Clarifying questions belong in the hint. `replyOptions` contains at most five
`{ label, message }` user replies: the button shows the short label and submits the
complete message. Missing free-text facts should produce a clarification, not a
placeholder reply. Expanded solutions are included in subsequent conversation
context; unopened solutions remain outside the user-visible transcript history.

Sending a question requests a host-owned scroll reveal of that new user turn.
This uses the generic protocol-70 `WidgetView.ScrollRevealRequest` contract:
`new ScrollRevealRequest(requestId, scrollId, targetId)`. Advance the positive
request ID for each explicit reveal. The target must be a declared descendant in
the same active scope of an ordinary scroll container. The host waits for layout,
animates using the app motion policy, preserves focus, consumes each request once
per presenter owner, and cancels pending work on manual navigation or retirement.
Modal/pinned projections do not inherit requests aimed at another surface.

The API key is encrypted with Windows DPAPI for the current user and saved at
`%LOCALAPPDATA%/WidgetRail/applications/game-help/gemini-key.dat`. Remove it using
Chat options > Remove saved key. It is never part of a presentation snapshot or
chat transcript. This application store is separate from the earlier sandbox
prototype's host secret slot; a prototype key must be entered again.

After explicit Send, the application reads its owned attachment through
`HostServices.Capture.ReadContentAsync`, performs its HTTP upload, and clears the
temporary byte buffer. Host revocation stops further capture reads; a full-trust
application's subsequent networking is application-owned, not a broker operation.
Operation cancellation/new-chat/teardown cancels this application's pending work.

Grounding is off by default and requires eligible paid Google API quota. The
application displays the provider's original suggestions using the generic
restricted-document service. Ordinary cited links use web/video intents, including
compatible pinned destinations. Free-key mode offers model answers and labeled
manual searches rather than claiming grounded sources.

## Controller layout

Setup is a bounded account card without chat/application controls. Chat has a
compact header, readable conversation viewport, and fixed composer. X opens Add
context, Y opens Chat options, LB opens New chat confirmation, R3 focuses the composer, and RT sends when focus is
within the composer and a question is ready. A and B retain ordinary activation
and tray/modal behavior. Preview retains A playback and LT/RT seeking; Y sends
and X retries. Starting a new chat and removing a key require confirmation.
Capture review also has an editable question; R3 focuses that field. Editing
replaces the pending user turn, and blank questions cannot be sent. The native
media toolbar has individually focusable Play/Replay controls with themed focus
visuals. Left/right moves between them, A invokes the focused action, and triggers
seek without moving focus. The timeline fills the available width.

The UI keeps the latest 20 messages. Each API request includes the newest 16
conversation entries within a 22,000-character budget; opened detailed solutions
count as additional assistant entries. These are character/message budgets, not
token limits or automatic summaries. Game Help's API-key link explicitly requests
Windows routing so it opens the default browser even when the Browser widget exists.

Automated provider checks use synthetic responses and disposable capture windows;
they do not make authenticated Gemini calls or upload user media. Real provider
answers and visual design are being physically reviewed by the user. Current
candidate versions and validation evidence are tracked in the implementation plan.

### Captured application context

Capture attachments may include `SourceApplication` with the Task Switcher
provider's display name and window title (120/240 characters). The frontend keeps
the exact native target used for capture; the Bridge matches its handle, PID,
process creation time and window class against the Task Switcher provider. It
never selects the first window from a later foreground list. Native identity stays
host-private; only bounded display metadata is attached to the media.

The lookup is best-effort with a three-second limit. Missing, ambiguous, replaced
or inaccessible windows leave metadata absent without discarding valid media.
Game Help sends the captured name/title to Gemini on Send, retry and follow-up
questions using that attachment, and shows the name in capture review. It does not
restore selected-window capture, activate windows or require window-list permission.
The model is instructed to treat the application name as a hint, not proof.
Snapshots containing this metadata require protocol 74.

Capture review now uses [the shared native media player](widget-media-player.md). The capture service owns only capture creation, attachment authority and metadata; playback controls are shared with other media sources.

Restricted documents without an authored fixed height fit their rendered content,
within the widget's min/max height bounds. A fixed height keeps an internally
scrollable viewport. Both documents and ordinary browser elements show a themed
outline when their container has focus before interaction; the outline hides while
interacting. Browser and document controls share the Browser environment (with
per-widget private profiles); embedded media has its separate shared environment.

Saved originals are retained separately in [Capture Library](capture-library.md). Game Help can select a saved item as a context-sized temporary copy, review it and edit the question before explicit Send. Optional library read access grants no new recording authority.
