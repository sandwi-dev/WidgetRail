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
- [Controller input model](controller-input.md) — Guide/Home ownership,
  dashboard quick actions, open-widget routing, and B behavior.

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
