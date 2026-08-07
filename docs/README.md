# Game Bar Alternative documentation

Game Bar Alternative is a controller-first Windows overlay and declarative
widget platform. This documentation describes the repository as it exists
today. Pages marked **planned** describe direction, not commands or security
guarantees that are available now.

## Start here

- [Platform architecture](platform-architecture.md) — process boundaries,
  data flow, and current implementation limits.
- [Widget quickstart](widget-quickstart.md) — scaffold, build, validate,
  render, and replay a controller widget.
- [Declarative UI reference](declarative-ui.md) — elements, focus, actions,
  images, icons, state, invalidation, and protocol limits.
- [GBSS styling reference](gbss.md) — safe selectors, variables, typed
  properties, imports, and diagnostics.
- [Settings and global themes](settings-and-themes.md) — the controller Settings
  widget, persisted appearance, versioned themes, live bridge/native cascade,
  remaining accessibility preferences/visual evidence, and authoring
  requirements.
- [Controller input model](controller-input.md) — Guide/Home ownership,
  dashboard quick actions, open-widget routing, and B behavior.
- [Widget capabilities](capabilities.md) — typed audio/network services,
  manifest declarations, lifecycle/consent behavior, errors, testing, and the
  current security boundary.
- [Network Controls reference](network-controls.md) — active first-party
  milestone, saved-profile-only scope, controller UX, privacy/location gates,
  event-driven authoring, tests, and release evidence.

## Distribution and operations

- [Publishing and installation](publishing-and-installation.md) — sharing
  source and `.gbarwidget` releases through GitHub, deterministic packing,
  SHA-256 pinning, bounded remote acquisition, and catalog management.
- [Security and trust](security-and-trust.md) — package validation, process
  boundaries, missing production isolation, and the current trust decision.
- [Troubleshooting](troubleshooting.md) — CLI, worker, bridge, controller,
  rendering, image, and GBSS diagnostics.
- [Widget packaging contract](widget-packaging.md) — archive layout,
  containment rules, immutable installation, and catalog APIs.
- [Implementation status](implementation-status.md) — verified components and
  honest limitations.
- [Windows provider architecture](windows-provider-architecture.md) — the
  event-driven Core Audio and WLAN/IP Helper providers, privacy boundaries, and
  simulator/hardware evidence gates for Audio Mixer and Network Controls.

## Product and research

- [Product brief](product-brief.md)
- [Architecture and technology research](architecture-plan.md)
- [Plugin-platform research](plugin-platform.md)
- [PS5 control-center interaction research](ps5-control-center-research.md)
- [Visual design system](visual-design-system.md)
- [Prototype roadmap](roadmap.md)

The source of truth wins if a document and code disagree. Protocol types live
under `src/WidgetProtocol`, the author-facing SDK under `src/WidgetSdk`, GBSS
under `src/WidgetStyling`, and developer commands under `tools/GbarCli`.
