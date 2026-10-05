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

## WinUI implementation

Windows Graphics Capture uses the documented Win32
`IGraphicsCaptureItemInterop::CreateForWindow` entry point. The shell-owned
`WindowPreviewCaptureService` shares a native GPU owner across widget renderers.
`WindowPreviewSurface` binds a published DXGI composition surface to a WinUI
`SpriteVisual`; XAML owns layout, clipping, transforms and z order.

Capture, native drawing, resizing and teardown run off the UI thread. Frame
callbacks signal readiness rather than dispatching into WinUI or closing capture
objects themselves. No CPU screenshot encoding, artwork cache or widget snapshot
is involved in live frame delivery. Hiding, unloading, scrolling offscreen or
losing a grant revokes demand. A blocked dispatcher does not extend native
permission: expired content is safety-blanked by the native owner.

The current native budget is eight capture slots and 192 MiB of accounted image
storage, with an allowance for retiring output surfaces. Output is bounded to
960 by 540 pixels; sources exceeding 4096 × 2160 pixels of content are rejected.
This bounds owned allocations, not all Windows/driver overhead. Capture failure,
minimized/protected sources or exhausted capacity leave a fallback. Windows may
display capture borders; the host does not bypass capture restrictions.

Production preview admission covers ordinary widget roots. Indexed-range and
pinned/focus-fragment preview slots require their own admitted inventory and
lifecycle and currently remain placeholders. See the
[GPU preview implementation](../maintainers/winui-window-preview-renderer.md)
for ownership, limits, shutdown and device recovery.

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
While previews have demand, the renderer renews permission through the session
on its 400 ms control-plane cadence. Grants expire within two seconds; renewal
failure, denial or changed ownership invalidates the prior authority. Renewal
does not invoke widget code or read pixels. See
[session admission](../maintainers/winui-window-previews.md).

## Verification

SDK, broker and session tests cover declaration validation, hidden native
metadata, identity scoping, consent and retirement. Native policy tests cover
budgets and lifecycle rules. WinUI preview validation exercises actual XAML
surface binding, permission expiry, resize, demand and shutdown using synthetic
windows. See [build and validation](../maintainers/winui-window-preview-renderer.md#build-and-validation).
These checks do not establish compatibility with every protected or exclusive-
fullscreen application.
