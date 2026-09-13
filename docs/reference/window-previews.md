# Live window previews

Widgets can display live application windows inside their own layouts. A preview
is view-only: it never forwards clicks or controller input to the source window.
The surrounding poster or action surface owns navigation and actions.

## Authoring

Declare `system.apps.windows.read.v1` to discover windows and
`system.apps.windows.preview.v1` to display their contents. Preview access is a
separate user permission. It does not expose pixels, files, native handles or a
capture operation to widget code. Switching and closing still require their
respective existing permissions.

```csharp
var windows = await HostServices.TaskSwitcher.GetWindowsAsync(cancellationToken);
var window = windows[0]; // Handle an empty list in the widget's ordinary empty state.

var poster = UI.PosterTile(
    window.ApplicationName,
    window.IsMinimized ? "Minimized" : "Open",
    action: "switch." + window.WindowId,
    id: window.WindowId,
    content: UI.WindowPreview(
        window.WindowId, window.WindowId + ".preview",
        "Preview of " + window.ApplicationName),
    subtitle: window.Title);
```

`UI.WindowPreview` also works outside a poster, including inside a custom
`UI.ActionSurface`. It defaults to Contain fit and a 16:9 aspect ratio. The
`fit` and `aspectRatio` arguments allow other arrangements. Theme sizing can
override its aspect ratio.

The additional `PosterTile` overload accepts a presentational `ImageElement`
or `WindowPreviewElement` and places its content above the existing themed title,
subtitle and state. Existing `TileArtwork` posters retain their original API and
cover/scrim layout. The slot can accept additional supported sources in future;
animated WebP is not implemented by this change.

Window IDs belong to the authenticated broker session and current observed list.
Do not persist them as application identities. Refresh the window list when
appropriate for the widget. Closing, minimizing, permission denial, capture
failure or resource pressure leaves a themed fallback; the surrounding card and
its actions remain usable.

Shared classes are `wrail-window-preview` and `wrail-preview-poster`. The
default, Neon Circuit and Arcade Rush themes provide semantic colors. Poster
copy uses the existing `wrail-tile__*` classes.

## Native implementation

The backend is Windows Graphics Capture, using the documented Win32
`IGraphicsCaptureItemInterop::CreateForWindow` entry point. A synthetic offscreen
window probe verifies the capture texture can be consumed by Direct2D on the
overlay's existing D3D device. DWM thumbnail registration targets an HWND,
rather than an arbitrary visual in our DirectComposition scene; it cannot
directly participate in the renderer's masks and retained widget transforms.

The native host imports capture frames as GPU bitmaps. No CPU readback, image
encoding, artwork decoding, remote-image cache entry or widget snapshot is
involved in frame delivery. A 33 ms host timer polls for newer available frames
and requests retained paints for their visible clipped regions through the
existing damage planner. The renderer applies its normal rounding, fit, opacity,
focus ordering, background composition and popup ordering. Source applications
and Windows determine when new frames exist; there is no source frame-rate
guarantee.

Capture setup and teardown run from the preview timer after drawing completes.
Windows capture calls can dispatch window messages, so they must not run between
a composition surface's BeginDraw and EndDraw. Reentrant updates retain their own
source list and discard work if visibility or source authority changes.

Live previews update while the widget is visible, including while focus is on
the tray. They do not require entering the widget. Only visible preview demands
are retained. Scrolling offscreen, hiding the
overlay, switching widget instances or losing source authority retires resources.
Minimized sources pause capture and show a fallback; restoration can resume
capture. Resizing uses the frame's actual ContentSize and recreates its pool
under the same resource budget.

There are at most eight active source sessions, two frame-pool buffers per source,
and an aggregate 16-megapixel source budget. Individual sources are bounded to
4096 x 2160 pixels worth of content. A conservative bound allowing an additional
retained frame is 192 MiB of preview frame storage, excluding Windows compositor
and driver overhead. Duplicate posters for one window share its capture.
Oversized sources and unsupported rendering targets show the fallback.

Windows controls capture availability and may display a capture border.
Protected content may be unavailable or blank. WidgetRail does not remove these
OS restrictions. Preview capture currently belongs to the ordinary overlay;
pinned-only presentations show the fallback.

## Authority

The provider issues host-private HWND/PID/process-creation/class evidence for
windows admitted by the existing running-window policy. Native targets are
excluded from the broker's widget response. A broker-owned registry binds opaque
IDs to package, publisher and instance; list replacement and disposal retire
entries.

For each full or incremental publication, the trusted bridge checks manifest
declaration and current consent, then resolves only IDs referenced in that
snapshot. Native targets travel separately from the public widget document on
the trusted bridge-to-host channel, only for native clients that negotiate
preview support in their hello. Other presentation clients retain their previous
response shape. Changes to this metadata invalidate preview
pixels even if the public document is unchanged. Native capture revalidates the
window, owning process lifetime and class, and respects display-affinity policy.
While previews are visible, the host independently checks current preview grants and broker-owned source IDs
on its one-second control-plane timer. Denial, broker retirement or an unavailable permission result
stops capture, including for widgets that publish no further snapshots. Regranting
requests a fresh publication. This check does not invoke widget code or read pixels.

## Verification

- SDK tests cover protocol versioning, view-only behavior, malformed declarations
  and compatibility with existing poster callers.
- Broker/bridge tests cover hidden native metadata, identity scoping, current
  snapshot membership, manifest consent, revocation and retirement.
- Native renderer tests cover aspect ratio, visible demand, retained painting,
  focus-follow and offscreen retirement.
- `WindowPreviewCaptureTests` captures only its own synthetic offscreen window.
  It imports a frame into Direct2D, rejects a wrong process lifetime and checks
  closure/retirement. It saves no pixel files. Windows may freeze this offscreen
  source after its first frame, so live animation remains a physical candidate
  check.
- Physical acceptance: enable preview permission for Task Switcher, inspect a
  changing source window, navigate/scroll, switch/close windows, minimize/restore,
  resize the source and open/close the overlay.
