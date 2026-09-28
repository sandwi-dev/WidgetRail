# Native embedded media adapter

`EmbeddedMediaSurface` hosts the existing sealed SDK adapter in **WinUI WebView2**.
It is a renderer for `EmbeddedMediaSession`, not a widget browser or script API.
The session service admits bytes and document identity; this directory owns browser
policy, the existing adapter message protocol and native input/element lifetime.

## Owner integration

All surface methods run on the WinUI dispatcher thread.

`OverlayShellPage` now owns one `EmbeddedMediaOwner` and a collapsed parking layer.
The owner subscribes directly to session publications, independently of the shell's
active-widget/visible-view filter. It admits at most four resident or retiring
controllers; reaching capacity preserves the parked sessions and their audio.
`WidgetViewPresenter` only registers/retire its `WidgetMediaViewport` slots. Slot
replacement moves the same browser through parking; ordinary command updates do
not reparent it. Hidden and inactive declaration removal retires the document.
Shutdown awaits resolution, observations and controller teardown before the bridge.

The SDK viewport remains nonfocusable and pointer-inert. Authored native Play,
Pause and seek controls use the existing normalized input/action path and publish
typed playback commands. No controller reader or new browser focus model exists.
Native WinUI layout gives MediaViewport a bounded growing track; the browser is
clipped inside it. Pinned/fullscreen endpoint transfer remains separate work.

1. Resolve `WidgetPresentationEmbeddedMediaDocument` with the exact current frame
   authority through `WidgetPresentationSession.ResolveEmbeddedMediaAsync`.
2. Confirm `GetEmbeddedMediaState(document)` still returns a declaration before
   constructing a surface. Attach `surface.Element` to a durable host-owned media
   layer, sized/clipped to the matching `MediaViewport`. The element must enter a
   live XAML root before WebView2 initialization can finish.
3. Keep this surface/document through compatible snapshot, viewport, scope,
   presentation and command changes. Call `Refresh` after session publication;
   update `UpdatePresentation(visible, acceptsInput)` as host visibility, foreground,
   viewport and modal input ownership change. Do not derive document lifetime from
   `Unloaded` or recreate it when the widget's ordinary view tree is refreshed.
4. Retaining a declaration without a viewport parks the browser (hidden and input
   disabled) while retaining audio/document/commands. Omitting or semantically
   replacing the declaration permanently retires it. `Refresh` detects retirement;
   the owner also calls `Dispose` at widget/session/host teardown.
5. Route only authored media commands through `Dispatch`. An authored command is
   consumed while the adapter is busy, without queuing or bubbling into the parent.
   `BackRequested` reports a validated adapter acknowledgement; the owner decides
   the associated host transition. `FailureCode`/`Diagnostic` expose bounded codes.

Failed surfaces stay retired. Retrying uses explicit widget/session recovery;
ordinary compatible snapshots must not recreate a failed adapter and replay its
already consumed command. A single admitted document must have one active surface.

## Security and lifecycle

- Exact synthetic HTTPS entry URI and exact package-resource routing, using only
  sealed admitted streams. No virtual-folder mapping, `NavigateToString`, widget
  filesystem access, `ExecuteScript`, host objects or widget-selected CDP calls.
- PSL validation remains in WidgetProtocol. Exact origin rules control frame
  navigation; validated domain families grant subresources only. External requests
  receive the installed application's canonical referer and fail closed without it.
- Media resources retain the existing single-range 200/206/416 contract.
- New windows, permissions, downloads, external schemes, authentication and invalid
  certificates are denied. Default browser chrome/input accelerators are disabled.
- Shared/service workers have no SDK capability and are denied by all media views.
  One host media environment shares browser resources, with an isolated InPrivate
  profile per widget. A failed environment creation can be retried by a new owner.
- Five generation fields, exact JSON schema, duplicate-field rejection, bounded
  numbers/action bounds, monotonic page events and one command in flight preserve
  the SDK adapter protocol. Bridge observations use a separate host sequence.
- WinUI owns the underlying WebView2 controller. The existing `arm-activate`
  handshake uses fixed `Input.dispatchMouseEvent` press/release calls, validated
  document-local bounds and current visible/input authority. The trusted fixture
  verifies both `event.isTrusted` and active browser user activation before
  `audio.play()`. No autoplay bypass flag is used.
- Programmatic Play waits while parked or covered; non-gesture commands can run
  while parked. A revoked in-flight activation releases input outside the action,
  then uses the existing SDK `initialize` authority reset to abort its operation
  without closing audio or navigating/recreating the browser. Consumed Play is not
  replayed after reopening. An exact canceled-command terminal is sent when still
  current; queued messages from the retired transport generation are discarded.
- Native popup ownership is checked at the actual activation boundary. The
  existing SDK rejection of widget-local ModalLayer plus embedded media remains
  unchanged; host dialogs and native flyouts can cover the media surface.
- Late initialization/messages cannot acquire a retired document. All event and
  nested-frame subscriptions are removed at teardown; browser closure retires audio.

## Validation and remaining scope

`--validate-embedded-media` runs a trusted named-pipe bundle fixture through real
session admission, exact SDK runtime bytes and native WebView2. It covers origin,
range and message rejection, trusted activation, playback commands, park/resume,
modal input gating and omission teardown. Its owner phase uses the real presenter
and shell media owner, normalized A input, compatible snapshots, tree replacement,
native popup interruption while audio continues, hidden updates, four real native
controllers, inactive retirement and awaited teardown. The WAV contains silence.
This is synthetic frontend/owner proof, not real YouTube/provider, compact-pinned/
fullscreen presentation or physical controller acceptance.
