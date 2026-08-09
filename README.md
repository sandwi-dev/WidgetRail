# Game Bar Alternative

Working title for a lightweight, controller-first Windows overlay with a modular widget platform.

The product direction is closer to a console control center than a collection of floating desktop windows. The goals are:

- The Guide/Home button is the only globally reserved controller button.
- Once a widget is active, it owns every other controller input.
- Widgets can be reordered and the last widget is restored; deeper focus/state restoration remains incremental.
- Users can replace the visual language through a safe CSS-like theme format.
- Widget logic runs outside the resident native shell. Installed/community
  workers run in mandatory capability-free Low-integrity AppContainers; typed
  OS access remains available only through authenticated brokered capabilities.
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
- Controller-first Settings, Audio Mixer, Network Controls, Games & Apps, and
  Now Playing workers in the runnable catalog; an installable YT Music
  Community addon; an installable Spotify Community-addon core; and a separate
  Clock SDK sample plus a capability-free, installable SDK Gallery reference
- A strict `.gbarwidget` package/catalog library and `gbar` developer CLI with
  bounded HTTPS/GitHub Release installation and required remote SHA-256 pinning;
  immutable version pin/rollback commands; accepted catalog changes reconcile
  live and workers still start lazily
- A generic installed-widget worker host with mandatory capability-free
  AppContainer isolation, pre-launch Job Object memory/process/UI containment,
  controller permission review, and typed authenticated audio/network/
  Bluetooth/recent-activity/app-library/media/exact-loopback/private-secret
  capability transport backed by deterministic simulators and narrow
  event-driven Windows Core Audio, WLAN, Bluetooth, and foreground providers
- A bounded GameInput/XInput/Raw Input containment probe with overlay and background-observer modes
- A bounded hidden/visible Windows process-tree performance observation harness
- A bounded Windows app-library provider that discovers Start Menu applications
  and launches only a current exact revalidated opaque registration while
  keeping paths, arguments, package identities, and process/window IDs private
- Managed contract suites plus native state, image-cache, layout, and icon tests

Start with the [documentation index](docs/README.md), the platform-grade
[widget authoring guide](docs/widget-authoring-guide.md), or the shorter
[widget quickstart](docs/widget-quickstart.md). See [implementation
status](docs/implementation-status.md) for verified components and honest
limitations.

Managed verification is immediately available:

```powershell
.\scripts\Verify.ps1 -Configuration Release -Lane managed
```

`-SkipNative` remains a compatibility alias for `-Lane managed`. Every step is
defined in `scripts/verification-steps.json`, has a process-tree timeout, and
writes stdout, stderr, JUnit-compatible results, revision/dirty-state/toolchain
provenance, and package hashes under `artifacts/verification/<run-id>`.

The complete verification command requires Visual Studio's Desktop development with C++ workload:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

Use `-StepId <stable-id>` for a focused local run. The checked-in Windows
workflow runs managed and native lanes independently and retains the same
evidence bundle even when a lane fails. Hardware, live-auth, real-controller,
and physical-display gates remain explicit manual release evidence.

## Documentation

- [Documentation index](docs/README.md)
- [Widget authoring guide and API map](docs/widget-authoring-guide.md)
- [SDK Gallery Community addon reference](samples/SdkGalleryWidget/README.md)
- [Games & Apps reference](docs/games-and-apps.md)
- [YT Music Community addon reference](samples/YtMusicWidget/README.md)
- [Retired Recent Apps reference](docs/recent-apps.md)
- [Platform architecture](docs/platform-architecture.md)
- [Widget quickstart](docs/widget-quickstart.md)
- [Declarative UI](docs/declarative-ui.md) and [GBSS](docs/gbss.md)
- [Settings and global themes](docs/settings-and-themes.md)
- [Theme packaging and distribution](docs/theme-packaging.md)
- [Controller input](docs/controller-input.md)
- [Local companion HTTP and private secrets](docs/community-companion-services.md)
- [Publishing, installation](docs/publishing-and-installation.md), and [security](docs/security-and-trust.md)
- [Troubleshooting](docs/troubleshooting.md)

## Architecture direction

Continue with a small native Windows shell using C++20, Win32,
Direct2D/DirectWrite, and GameInput. Do not inject into games and do not embed
Chromium in the resident host. Widget logic launches lazily out of process,
remains resident in Background by default, and sends a declarative UI tree over
versioned local IPC. Typed capability brokering is connected to simulators and
narrow Windows providers. Installed/community workers fail closed unless their
host-owned AppContainer, executable/package grants, and authenticated IPC can
be established. Settings is the only temporary trusted Job-only worker; YT
Music has no trusted fallback and uses the public package AppContainer plus
exact-port loopback/private-secret broker services. Publisher
signing/revocation, CPU and disk/profile quotas/cleanup, audit UI, broader
hardware/privacy evidence, and lifecycle-policy enforcement remain required
before public community distribution is safe.

The remaining evidence gates include:

1. Guide-button behavior is reliable across representative controllers and conflicts.
2. The underlying game does not act on controller input in the supported cases while the overlay has focus.
3. A non-injected topmost window works across the declared presentation modes.
4. The native shell meets explicit latency and resource budgets.
