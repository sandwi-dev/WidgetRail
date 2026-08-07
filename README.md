# Game Bar Alternative

Working title for a lightweight, controller-only Windows overlay with a modular widget platform.

The product direction is closer to a console control center than a collection of floating desktop windows. The goals are:

- The Guide/Home button is the only globally reserved controller button.
- Once a widget is active, it owns every other controller input.
- Widgets can be reordered and the last widget is restored; deeper focus/state restoration remains incremental.
- Users can replace the visual language through a safe CSS-like theme format.
- Widget logic runs outside the resident native shell; typed brokered
  capabilities are implemented, while hostile-code sandboxing remains planned.
- A cold hidden start launches no third-party workers. Once used, Background
  workers remain resident by default while Visible/Interactive work is canceled.

This repository contains an integrated Phase 0 platform prototype. It is not a production installer, public marketplace, or complete Game Bar replacement.

## Implemented foundation

- A native Win32/D2D overlay shell with Guide toggle, controller navigation, reorder mode, last-widget restoration, and hidden-state resource teardown
- A versioned declarative widget protocol, typed C# SDK, lazy worker runtime, and managed native bridge
- Safe GBSS compilation, typed computed styles, deterministic data-only theme
  packages/tooling, HTTPS images, semantic icons, and controller-aware state
- A verified controller Settings worker plus strict appearance store,
  version-pinned themes, platform/widget/user cascade, no-poll reload, and live
  native shell appearance plus post-cascade text/contrast/motion/transparency
  accessibility policy
- Controller-first Settings, Clock, YT Music, Audio Mixer, and Network Controls
  reference widgets through the same declarative worker path
- A strict `.gbarwidget` package/catalog library and `gbar` developer CLI with
  bounded HTTPS/GitHub Release installation and required remote SHA-256 pinning;
  immutable version pin/rollback commands; accepted catalog changes reconcile
  live and workers still start lazily
- A generic installed-widget worker host, pre-launch Windows Job Object memory/
  process containment, controller permission review, and typed authenticated
  audio/network capability transport backed by deterministic simulators and
  narrow event-driven Windows Core Audio/WLAN providers
- A bounded GameInput/XInput/Raw Input containment probe with overlay and background-observer modes
- A bounded hidden/visible Windows process-tree performance observation harness
- Managed contract suites plus native state, image-cache, layout, and icon tests

Start with the [documentation index](docs/README.md) or [widget quickstart](docs/widget-quickstart.md). See [implementation status](docs/implementation-status.md) for verified components and honest limitations.

Managed verification is immediately available:

```powershell
.\scripts\Verify.ps1 -Configuration Release -SkipNative
```

The complete verification command requires Visual Studio's Desktop development with C++ workload:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

## Documentation

- [Documentation index](docs/README.md)
- [Platform architecture](docs/platform-architecture.md)
- [Widget quickstart](docs/widget-quickstart.md)
- [Declarative UI](docs/declarative-ui.md) and [GBSS](docs/gbss.md)
- [Settings and global themes](docs/settings-and-themes.md)
- [Theme packaging and distribution](docs/theme-packaging.md)
- [Controller input](docs/controller-input.md)
- [Publishing, installation](docs/publishing-and-installation.md), and [security](docs/security-and-trust.md)
- [Troubleshooting](docs/troubleshooting.md)

## Architecture direction

Continue with a small native Windows shell using C++20, Win32,
Direct2D/DirectWrite, and GameInput. Do not inject into games and do not embed
Chromium in the resident host. Widget logic launches lazily out of process,
remains resident in Background by default, and sends a declarative UI tree over
versioned local IPC. Typed capability brokering is connected to simulators and
narrow Windows providers; broader hardware/privacy evidence, production
AppContainer-equivalent sandboxing, lifecycle-policy enforcement, and signing
are still required before accepting untrusted widgets.

The remaining evidence gates include:

1. Guide-button behavior is reliable across representative controllers and conflicts.
2. The underlying game does not act on controller input in the supported cases while the overlay has focus.
3. A non-injected topmost window works across the declared presentation modes.
4. The native shell meets explicit latency and resource budgets.
