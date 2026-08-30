# WidgetRail documentation

WidgetRail is a controller-first Windows overlay and declarative
widget platform. This documentation describes the repository as it exists
today. Pages marked **planned** describe direction, not commands or security
guarantees that are available now.

## Directory organization

The documentation tree distinguishes current guidance from historical planning
evidence:

- The root contains stable public entry points and the small active control
  plane. These paths are intentionally stable because tooling, agent goals, and
  external author instructions link to them.
- [`history/`](history/README.md) contains immutable timestamped snapshots
  removed from active planning/review documents. Historical files are not
  implementation authority and are opened only for a named prior decision.
- New nested areas must be coherent and link-aware. Public architecture,
  authoring, reference, operations, and research pages will move only in
  complete validated groups rather than through piecemeal path churn.

The active [delivery plan](delivery-plan.md), [engineering review](engineering-quality-review.md),
[widget-authoring review](widget-authoring-experience-review.md), and
[roadmap](roadmap.md) are intentionally operational. Timestamped predecessors
live in their matching [`history/`](history/README.md) directories so routine
planner and implementation turns do not spend context on closed evidence.

## Start here

- [Widget authoring guide and API map](widget-authoring-guide.md) — complete
  minimal-to-advanced tutorial, manifest/API reference, controller scopes,
  Scroll/surface contracts, responsive `UI.NavigationShell`, lifecycle,
  capabilities, bounded static scenario declaration/listing, local/GitHub
  workflows, security, performance, responsive layout, and diagnostics.
  Scenario factory execution fails closed until an AppContainer preview worker
  exists.
- [Platform architecture](platform-architecture.md) — process boundaries,
  data flow, and current implementation limits.
- [Widget quickstart](widget-quickstart.md) — scaffold, build, validate, list
  bounded scenario declarations, render trusted code, replay, and run a
  controller widget through the isolated `wrail dev` watch loop with
  authenticated readiness and last-good recovery.
- [Embedded-media widget template](embedded-media-widget.md) — generate and
  verify a provider-neutral two-source player, explicitly stage the shared
  adapter runtime, and follow route, command, terminal, replacement, and
  shutdown lifecycle ownership.
- [Widget SDK compatibility and release unit](widget-sdk-compatibility.md) —
  checked-in public API baseline, exact CLI/template/package version contract,
  and the intentional pre-release addition or breaking-reset workflow.
- [Widget SDK stability, migration, and deprecation](widget-sdk-evolution.md) —
  active pre-release API classification, release-unit movement, migration
  record, external-distribution trigger, and separate protocol policy.
- [Widget lifecycle and process residency](widget-residency.md) — lifecycle
  callbacks, versioned manifest policy, safe suspension, bounded idle unload,
  cached views, lazy resume, and legacy migration.
- [Declarative UI reference](declarative-ui.md) — elements, focus, actions,
  images, icons, state, invalidation, and protocol limits.
- [Controller UI component patterns](controller-ui-components.md) — Slider v3,
  the Audio icon-Slider-percentage reference, focusable Disabled/Busy states,
  and the public controller-safe Card, IconButton, StatusBadge, Alert,
  EmptyState, SegmentedTabs, Switch, ScopedDialog, SettingsRow, ActionSheet,
  single-select Picker, Scrubber, non-focus-stealing Toast, and protocol-v7
  ActionSurface/Tile compositions, plus protocol-v8 ResponsiveGrid
  and semantic CodeText. Settings uses Picker, Grid, and CodeText; Spotify uses
  Scrubber; Games & Apps uses Tile and lifecycle-safe Toast feedback.
  Responsive Row wrapping and per-edge WRSS borders are implemented. The full
  Release gate is green; hands-on packaged visual/controller/accessibility
  evidence remains open.
- [WRSS styling reference](wrss.md) — safe selectors, variables, typed
  properties, independent per-edge borders, semantic CodeText typography,
  imports, diagnostics, and bounded native opacity/scale transitions.
- [Settings and global themes](settings-and-themes.md) — the controller Settings
  widget, persisted appearance, live bridge/native cascade, host-owned
  accessibility overrides, and authoring requirements.
- [Theme packaging and distribution](theme-packaging.md) — scaffold,
  validate, computed preview, deterministic `.wrtheme` packaging, GitHub/
  HTTPS installation, format limits, and trust semantics.
- [Display and resolution](display-and-resolution.md) — active-monitor
  targeting, Per-Monitor-V2/DIP behavior, responsive widget rules,
  deterministic resolution evidence, and physical mixed-monitor limitations.
- [Performance](performance.md) — engineering budgets, implemented
  low-overhead rules, widget lifecycle guidance, bounded Windows process
  observations, current measurements, and remaining ETW/PresentMon evidence.
- [Host-owned pinned-surface feasibility](pinned-surfaces.md) — bounded Win32
  tool-window selection, focus/input/lifecycle and monitor/DPI policy, measured
  DLV-016 comparison, and explicit physical-evidence limits. This is an
  architecture gate, not a public pinning feature.
- [Controller input model](controller-input.md) — Guide/Home ownership,
  dashboard quick actions, open-widget routing, and B behavior.
- [Widget capabilities](capabilities.md) — typed audio/network/Bluetooth/recent-
  activity/app-library/media-session services, exact-operation dashboard gesture authority,
  manifest declarations, lifecycle/consent behavior, errors, testing, and the
  current security boundary.
- [Local companion HTTP and private secrets](community-companion-services.md) —
  exact-port JSON GET/POST, write-only package secret slots, host-side Bearer
  injection, lifecycle/consent rules, limits, errors, and security boundaries.
- [Private widget state](private-widget-state.md) — package-scoped readable
  JSON persistence, revision/CAS semantics, lifecycle, identity/update scope,
  quotas, atomic storage, retention, errors, and tests.
- [Network Controls reference](network-controls.md) — implemented first-party
  integration, explicit available-Wi-Fi scan/current saved-open connection,
  controller UX, privacy/location
  gates, event-driven authoring, tests, and remaining release evidence.
- [Games & Apps reference](games-and-apps.md) — implemented trusted-Game
  auto-curation with durable exclusions, opt-in Applications/Unknown entries,
  stable opaque identity reconciliation, paged SDK, separately consented exact
  launch, controller UX, and current source/icon/classification limits.
- [Game Launcher reference](game-launcher.md) — autonomous full-trust Community
  game library over bounded opaque cursors, package-owned Windows/store discovery,
  responsive virtualized semantics, favorites and groups, display-only warm state,
  and fresh exact SavedId launch admission through the generic application runtime.
- [Game Launcher product and engineering requirements](game-launcher-requirements.md)
  — proposed console-home expansion informed by Heroic and One Game Launcher,
  including trusted multi-source adapters and optional content operations.
- [YT Music Community addon reference](../samples/YtMusicWidget/README.md) — the
  first real public-package/AppContainer local-companion integration, including
  pairing, optimistic media UX, dashboard actions, and local pack/install.
- [SDK Gallery Community addon](../samples/SdkGalleryWidget/README.md) — a
  capability-free, separately installable reference for modern public SDK
  composites, responsive layout, nested B scopes, themes, and local packaging.
- [Full Application reference](../samples/FullApplicationWidget/README.md) — an
  optional capability-free sample with a 10,000-record private model, a bounded
  cursor projection, nested navigation, lifecycle drain, retry, and generic
  AppContainer packaging without adding another built-in tray widget.
- [Recent Apps reference](recent-apps.md) — retained reference for the read-only
  foreground-activity API; it is no longer in the bundled dashboard catalog.
- [Discord integration research](discord-integration.md) — official API/SDK
  capability matrix, authentication and production-access gates, and why the
  social/voice widget is deferred rather than built on unsupported client APIs.
- [Spotify integration](spotify-integration.md) — autonomous full-trust
  Community application, package-owned fixed-loopback PKCE/Web API/token and Web
  Playback behavior, current CLI setup, Development Mode quota, manual live-
  account/EME evidence, and attribution gates.

## Distribution and operations

- [Publishing and installation](publishing-and-installation.md) — sharing
  source and `.wrwidget` releases through GitHub, deterministic packing,
  SHA-256 pinning, bounded remote acquisition, disabled-only version
  selection/rollback, and catalog management.
- [Security and trust](security-and-trust.md) — package validation, mandatory
  installed/community AppContainer isolation, remaining publisher/resource
  boundaries, and the current trust decision.
- [Troubleshooting](troubleshooting.md) — CLI, worker, bridge, controller,
  rendering, image, and WRSS diagnostics.
- [Diagnostics and recovery](diagnostics-and-recovery.md) — sanitized runtime
  health, the private Settings channel, controller behavior, and safe recovery
  boundaries.
- [Widget packaging contract](widget-packaging.md) — archive layout,
  containment rules, immutable installation, schema migration, version pins,
  and catalog APIs.
- [Implementation status](implementation-status.md) — verified components and
  honest limitations.
- [Known issues](known-issues.md) — active user-visible bugs, reproduction
  evidence, acceptance criteria, status, and closing commits.
- [Windows provider architecture](windows-provider-architecture.md) — the
  event-driven Core Audio, WLAN/IP Helper, and bounded Start Menu providers,
  privacy boundaries, and
  simulator/hardware evidence gates for Audio Mixer and Network Controls. The
  current GSMTC Now Playing provider and its public-package path are summarized
  in [implementation status](implementation-status.md) and [widget
  capabilities](capabilities.md).

## Product and research

- [Product brief](product-brief.md)
- [Architecture and technology research](architecture-plan.md) — historical
  native-first research retained for context; current decisions are documented
  in the platform architecture and implementation status.
- [Plugin-platform research](plugin-platform.md)
- [PS5 control-center interaction research](ps5-control-center-research.md)
- [Visual design system](visual-design-system.md)
- [Product roadmap](roadmap.md)

The source of truth wins if a document and code disagree. Protocol types live
under `src/WidgetProtocol`, the author-facing SDK under `src/WidgetSdk`, WRSS
under `src/WidgetStyling`, and developer commands under `tools/WrailCli`.
