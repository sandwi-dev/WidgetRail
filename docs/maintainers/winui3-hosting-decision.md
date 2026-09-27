# WinUI overlay hosting decision

Status: implementation direction; runtime capability proof outstanding. Researched
2026-09-27 against Microsoft documentation, NuGet metadata, official samples, and
the current WidgetRail checkout. No window was launched for this research.

Subsequent runtime checks are recorded in [implementation status](winui3-implementation-status.md).
The transparent backdrop alone left opaque margins. The selected implementation
now uses pinned **WinUIEx 2.9.3 TransparentTintBackdrop**, which supplies the
documented windowing/DWM integration around WinUI. Three margin pixels followed
both red and green controlled windows beneath the shell, and a physical pointer
click reached the Close control. This passes basic desktop alpha, not all hosting
gates. Media, input-region policy, device recovery and multi-monitor/DPI remain.

## Current direction

Keep WinUI's top-level Window, with `WinUIEx.TransparentTintBackdrop` instead of
maintaining our own backdrop/DWM/message-hook implementation. NuGet source commit
`72f2975d2a237c0d7ad1113fe617d5894e66feb6`, MIT license in `third_party/WinUIEx`.
The former `TransparentWindowBackdrop.cs` has been removed. Reproduce basic alpha
with `scripts/Test-WinUiTransparency.ps1 -AppPid <running-shell-pid>`; evidence is
under `artifacts/winui-shell/alpha-*`. No unstable composition engine is enabled.

## Original public-API investigation (superseded implementation)

Start with one C# WinUI `Window` and its `AppWindow`, with a custom transparent
`SystemBackdrop`. Keep WinUI in charge of its HWND and composition tree. Use the
existing `OverlayPlatformInterop` ABI for controller and foreground policy.

This is a concrete public-API approach to test, not a declaration that every
overlay requirement is already supported. Do not use an opaque window, Mica,
acrylic, or whole-window opacity as a substitute for per-pixel transparency.

The initial implementation is
`src/OverlayFrontend.WinUI/TransparentWindowBackdrop.cs`. It owns a brush per
connected target, creates an OS compositor solely for that backdrop, and releases
both owned resources on disconnect without touching XAML's compositor. Compilation and
runtime capability results must be recorded separately by the shell owner.

Use stable `Microsoft.WindowsAppSDK` **2.5.1** for the initial capability build.
The official stable-channel page lists its release as 2026-09-16 and NuGet lists
the same package. The 1.8 release line is no longer the current stable choice.
Pin the dependency and record the actually loaded runtime in test evidence.
The 2.5.1 release notes describe fixes involving the System Composition Engine;
that does not make its selector a supported default for us. `CompositionEngine`
was introduced as an **unstable Limited Access Feature**. Do not depend on it.

## Transparent top-level window

The documented `Microsoft.UI.Xaml.Media.SystemBackdrop` extension point allows
overriding `OnTargetConnected(ICompositionSupportsSystemBackdrop, XamlRoot)` and
`OnTargetDisconnected`. The target's documented `SystemBackdrop` property accepts
a `Windows.UI.Composition.CompositionBrush`. This is deliberately an external
OS-composition boundary even though the interface is in `Microsoft.UI.Composition`.
The 2.5.1 build rejects a `Microsoft.UI.Composition.CompositionColorBrush` here.
`ElementCompositionPreview.GetElementVisual` and XAML's current-thread compositor
both return the Microsoft variant, so neither supplies this backdrop's brush.

Implement a private, host-owned transparent backdrop class:

1. In `OnTargetConnected`, call the base method and create an owned
   `Windows.UI.Composition.Compositor` for the external backdrop. This is not a
   replacement compositor for XAML or a new content-rendering pipeline.
2. Create a fully transparent OS `CompositionColorBrush` and assign it to
   `connectedTarget.SystemBackdrop`. Retain resources per target, or disallow
   instance sharing explicitly.
3. In disconnect, clear only this target's brush, close that brush and its owned
   OS compositor, and call the base method. Do not close XAML's compositor.
4. Assign the non-null custom backdrop to `Window.SystemBackdrop`. Leave the
   relevant root/page backgrounds transparent; paint actual shell/widget
   backgrounds explicitly with their intended alpha.

Why this differs from simply setting `Grid.Background` to transparent:
Microsoft's `DesktopWindowImpl.cpp` explicitly disables transparent island
backgrounds by default. Its `SetXamlIslandRootBackground` enables transparency
when either a system-backdrop object or brush exists, and restores the opaque
background when neither exists. This source evidence supports the proposed
public extension point; it is not a promise about a specific installed binary.
No private interface or implementation function above should be called.

Configure borderless/topmost behavior with `OverlappedPresenter` and documented
`AppWindow` operations. Obtain the actual HWND for the narrow Win32 operations
that remain necessary. Do not copy the old host's DirectComposition setup or add
`WS_EX_LAYERED`/color-key rendering by habit: ownership and alpha behavior differ.
Do not replace WinUI's composition root or reparent internal visuals.

Transparency and input are separate. Clear pixels do not by themselves promise
click-through to another process. Explicitly validate pointer delivery on clear
regions, interactive widgets, pinned windows, and during modal display. A
`WS_EX_TRANSPARENT` flag or successful hit-test handler is not delivery evidence.
The normal interactive overlay and inactive/click-through pinned surfaces can
have distinct window policies without having distinct rendering engines.

## Bounded alternative: native HWND with a WinUI island

`Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource` is a supported Windows App SDK
API, distinct from the older `Windows.UI.Xaml` UWP hosting APIs. The official C#
island sample uses:

- `new DesktopWindowXamlSource()` followed by `Initialize(WindowId)`;
- a `DesktopChildSiteBridge` under the existing parent HWND;
- `SiteBridge.MoveAndResize(RectInt32)` as the native client area changes;
- `Content` for the XAML root and `SystemBackdrop` for backdrop configuration;
- `NavigateFocus` and `TakeFocusRequested` to cross the native/XAML boundary.

The API documentation requires disposing/closing the source. Initialize the XAML
hosting environment on the intended UI thread before constructing content when
needed, maintain the dispatcher/message pump, and disconnect content and events
before destroying its native parent.

Evaluate this alternative only if the top-level Window has a demonstrated
activation, placement, or transparency blocker. An island gives control over
the parent HWND, but adds child-window focus, pointer, accessibility, lifetime,
and composition integration obligations. Its existence is not proof that the
parent will have transparent pixels or that child media will layer correctly.
Do not ship two hosting implementations indefinitely.

The official SceneGraph repository contains a `TransparentWindow.PNG` thumbnail,
but that image alone is not a current runnable transparent-window sample. The
official Islands sample demonstrates hosting and acrylic, not the full
WidgetRail transparent-overlay requirement.

## Existing native boundary: reuse, do not re-extract

The checkout already has `src/OverlayPlatformInterop/OverlayPlatformInterop.h`
and its README, defining version **4** of a narrow C ABI. This boundary owns no
window or renderer. It already supplies:

| Concern | Existing boundary |
|---|---|
| Owner lifetime and callback admission | `Create`, `Initialize`, `Shutdown`, `Destroy` |
| Guide/events | `DrainEvent`, `RequiresLegacyGuidePolling`, `PollLegacyGuide` |
| Opening and visible input lease | `PrepareVisible`, `SetWindowState`, `PrimeController` |
| Controller state, edges and repeats | `ReadController` and versioned controller frames |
| Foreground target policy | `SetOwnedWindows`, `ObserveForegroundTarget`, `RememberedForegroundTarget`, `ResolveForegroundTarget` |
| Work-area/DPI placement | `ComputePlacement` |
| Optional controller isolation | Existing isolation configuration/recovery functions |

The frontend should marshal native callbacks onto `DispatcherQueue`; callbacks
must not synchronously mutate XAML. Use one native controller owner and preserve
ABI structure sizes, calling convention, callback rooting, shutdown ordering and
device-family information. The adapter's neutral priming and release behavior
must not be replaced by a second managed controller reader.

Window mechanics still belong to the frontend: show/hide, actual foreground
observation, best-effort activation, HWND validity and foreground restoration.
Current `OverlayHost/main.cpp` shows the sequence: prepare visible, show/place,
attempt foreground once, publish actual window/input state, then prime input.
Closing releases visible state and restores a valid remembered target. Preserve
the semantics, not the old rendering code. Successful `SetForegroundWindow` or
visibility is not proof of controller ownership. Register all relevant frontend
windows with the ownership policy; an island child must not be mistaken for an
external application during activation changes.

## Media and compositor limits

Microsoft documents that the Windows App SDK compositor cannot sample pixels
from external content such as `MediaPlayerElement`, `WebView2`, `SwapChainPanel`,
and system backdrops. In particular, transparent WebView2 is not a general way
to reveal arbitrary XAML behind it; `SwapChainPanel` does not support transparency.
An acrylic brush cannot blur video pixels that the compositor cannot sample.

These documented constraints must inform the adapter. A native media swap chain
or capture visual cannot simply be attached to a different compositor instance.
Prove supported interop and ownership before porting the media subsystem. Do not
silently replace GPU presentation with routine CPU readback, or reduce the
product's clipping, pinning, menus or modal behavior to make a demo pass.

## Capability proof to run before bulk migration

Use the actual prospective frontend, not a separate drawing-only toy. Record
Windows build, GPU, DPI, package/runtime version and selected hosting model.

1. Place an animated, independently owned checkerboard/reference window behind
   the overlay. Show opaque, half-alpha and zero-alpha XAML regions. Capture
   displayed pixels over both reference colors and prove the expected blend,
   including no opaque startup/hide/show frame. A screenshot of a static desktop
   or XAML RenderTargetBitmap alone cannot establish desktop transparency.
2. Test pointer delivery to controls and intended clear/click-through regions;
   confirm the actual receiving HWND/process. Test activation loss, Guide and
   View+Menu reopen, text input, controller navigation, and restoration.
3. Test two monitors and fractional DPI, resize, negative monitor coordinates,
   topmost/Alt+Tab/taskbar behavior, minimize and display reconfiguration.
4. In that shell play WebView2 video and live capture, with ordinary XAML controls,
   menus and a widget-local modal above them. Exercise rounded clips, transforms,
   animation, pin/unpin, hide/resume and device loss/recreation. Record unsupported
   effect combinations rather than claiming parity from video playback alone.
5. Verify keyboard/UIA focus through the actual controls, including popup and
   modal boundaries. Test pinned inactive windows separately from active overlay.
6. Measure resident/committed memory, present/frame latency and GPU work while
   animating and scrolling real virtualized content. Framework selection is not
   performance proof.

The current research proves API availability and a plausible supported hosting
route. It does **not** pass any of these runtime gates.

## Sources

Checked 2026-09-27:

- [Current stable releases](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/stable-channel)
- [2.x release notes, including 2.5.1 and the LAF classification](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0?pivots=stable)
- [NuGet package versions](https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/index.json)
- [SystemBackdrop extension contract](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop)
- [System-backdrop brush property](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.icompositionsupportssystembackdrop.systembackdrop)
- [DesktopWindowXamlSource](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.desktopwindowxamlsource)
- [Official C# island sample](https://github.com/microsoft/WindowsAppSDK-Samples/blob/5cecb25bd9325de162cba2d59345b6c2a9f1f139/Samples/Islands/cs-winforms-unpackaged/DesktopWindowXamlSourceControl.cs)
- [Top-level transparency implementation evidence](https://github.com/microsoft/microsoft-ui-xaml/blob/1fdf51480ab1e5fe92b63d2e1c0b8d56c367049e/dxaml/xcp/dxaml/lib/DesktopWindowImpl.cpp)
- [Composition and external-content limitations](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/composition)

The source snapshots explain implementation behavior; documented public APIs and
the selected runtime's actual behavior remain the shipping authority. Main-branch
source is not assumed identical to the 2.5.1 binary.
