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
var ticket = await HostServices.Capture.RequestAsync(
    WindowCaptureKind.Video, cancellationToken);
var status = await HostServices.Capture.GetStatusAsync(ticket, cancellationToken);
```

The host asks for confirmation, completes its release-aware hide transition,
shows a five-second preparation countdown, then resolves the foreground application
once and captures that window. It does not activate or restore a selected target.
Game Help uses this flow and has no application picker or window-list permission.
The desktop, shell and WidgetRail's own windows are not foreground capture targets.
Video lasts five seconds, at 15 fps, with no audio or cursor. Target
loss, replacement, resize, protected content, cancellation, or reopening the
overlay terminates capture; another window is never silently substituted.
The overlay does not reopen automatically.

This is the only capture mode. Requests accept a capture kind, never a window ID
or application name. Window selection and application activation are not part of
the capture service. The native layer binds the foreground window identity once
when capture starts and retains it until that screenshot or recording finishes.

Initiation requires interactive lifecycle. Status, cancellation, discard and
authorized reads can continue while the overlay is hidden. Use a widget-lifetime
operation for status polling. Preview permission alone does not grant capture.

Attachments contain opaque IDs and bounded metadata. PNG is limited to
1920x1080; video to 1280x720; each attachment to 8 MiB. The host permits four
attachments per widget, sixteen total, and 64 MiB of retained media. Pending
requests expire after two minutes; completed captures expire after thirty.
Revocation, worker retirement and discard remove the owning captures. Failed
deletes are retried; expired unlocked capture directories from a crashed host
are cleaned on a later host run.

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
