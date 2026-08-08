# Product brief

Status: initial product baseline, 2026-08-06

## Problem

Xbox Game Bar behaves like a host that retains control over widgets instead of a console-style shell that hands control to the selected experience. It also gives users too little control over ordering, resumption, and appearance.

The product should be a general-purpose Windows control center that happens to work well over games. It is not limited to game telemetry or game-specific integrations.

## North star

Press Guide, perform a useful action with a controller, and return to the previous application without thinking about window management.

The interaction should feel immediate, predictable, personal, and substantially lighter than a browser-based overlay platform.

## Product principles

1. **Controller-only runtime.** Every host surface and every accepted widget must be usable without mouse, keyboard, or touch.
2. **Guide is the boundary.** Guide/Home is the only controller input globally reserved by the shell.
3. **The active widget owns actions; B is hierarchical Back.** The active scope receives `B` first, an unhandled root `B` returns to the dashboard, and dashboard `B` closes the overlay. Bumpers, triggers, stick clicks, Menu, and View are never repurposed as host widget switching while a widget is active.
4. **Resume, do not reset.** Reopening restores the last widget, focused element, selected tab, scroll position, and relevant widget state.
5. **The layout belongs to the user.** Widget order, visibility, favorites, and presentation are persistent and controller-editable.
6. **Deep customization is a platform feature.** A safe CSS-like language styles stable semantic roles and controller states across the shell and participating widgets.
7. **Modularity must not sacrifice trust.** One faulty or malicious community widget must not crash the shell or inherit unrestricted access to the machine.
8. **Performance is a feature.** Hidden UI means no presentation or visible-state
   polling. Resident Background workers and permitted background work remain
   measured, bounded, and visible to the user.
9. **No game injection in the initial product.** Prefer predictable compatibility with windowed, borderless, and Fullscreen Optimizations over hooks that increase anti-cheat and stability risk.
10. **First-party widgets teach the public SDK.** Built-in widgets use the same public contracts wherever practical and ship with readable reference implementations.

## Interaction model

```text
Game or application
  -> Guide press
Dashboard shell focus
  -> choose/reorder widget with controller
  -> A activates
Widget focus
  -> widget actions and nested Back
  -> B returns to dashboard; B again closes
  -> Guide press
Game or application
```

Within a widget, the host may provide standard spatial focus movement for declarative controls, but it does so on behalf of that widget. It never converts `LB` or `RB` into shell navigation. B follows the explicit nested/root/dashboard Back hierarchy above.

If a widget crashes, the host returns focus to that widget's dashboard card and offers controller-accessible restart or disable actions.

## Initial first-party widget candidates

- Audio Mixer: event-driven per-application session volume/mute on the current
  default multimedia render endpoint, master volume/mute, sanitized default-
  device visibility, and current default-microphone volume/mute; no output-
  device switching or microphone sample capture
- Network Controls: event-driven aggregate/Ethernet/Wi-Fi state, explicit
  nearby scans, saved/open connection, software Wi-Fi/Bluetooth radio control,
  and sanitized Bluetooth discovery; no password entry, pairing, generic
  Bluetooth connection, automatic
  location-sensitive current-SSID query, or signal disclosure in the initial
  scope
- Performance: CPU, GPU, VRAM, RAM, frame-rate sources, and bounded history
- Media: system media session controls
- Recent Activity: event-driven bounded read-only running-application
  observations; no activation, history/relaunch, or authoritative game detection
- Screenshot and capture controls
- Notifications and quick settings
- Discord: a generally distributable launch/invite surface; conditional
  identity and Social SDK friends/presence only after Discord eligibility review

Discord is the only planned social provider initially. Normal OAuth and bots
cannot mirror the signed-in user's friends, DMs, current call, or mute state.
Social SDK communications are game-oriented and review-gated; control of the
existing Discord desktop call requires partner-only local RPC scopes. The
ungated baseline is therefore opening Discord home, official channel/message
links, and invite links. Anything broader remains conditional on written
Discord eligibility and production access.

Audio Mixer and Network Controls are the first two implemented system-control
reference widgets, backed by narrow event-driven Core Audio and WLAN/IP Helper
providers. Their automated packaged Release gates pass; hardware/privacy and
performance gates remain. Both widgets use the same public SDK, controller
input scope, lifecycle, permission, and packaging model.
Their Windows integrations belong behind narrow brokers;
neither feature is a reason to hard-code an alternate UI or expose raw OS
handles in the native host.

## Customization

Normal customization is controller-accessible: presets, colors, density, animation, layout, and previews.

Advanced users can edit a text-based stylesheet, provisionally called GBSS. It supports variables, a constrained cascade, semantic selectors, package-contained assets, and states such as `:focused`, `:pressed`, `:selected`, and `:disabled`. It does not support scripts, remote resources, commands, arbitrary shaders, or unrestricted local file paths.

Accessibility rules override themes where necessary, including visible focus, minimum contrast, text scaling, and reduced motion.

## Initial non-goals

- Mouse-first or keyboard-first interaction
- Reproducing proprietary code, assets, branding, or trade dress
- Universal support for true Fullscreen Exclusive games
- DLL injection, graphics API hooks, or a controller filter driver
- Loading arbitrary third-party DLLs into the shell process
- A Chromium/WebView runtime for every widget
- Multiple social networks in the first release
- Browsing or replacing every surface of the Discord desktop client
- A public widget marketplace before installation, sandboxing, rollback, and resource controls are trustworthy

## Performance budgets to validate

These are engineering gates, not marketing claims. Phase 0 measurements may refine them while preserving the intent.

| Scenario | Initial target |
| --- | --- |
| Hidden steady-state CPU | p95 at or below 0.1% on the reference machine |
| Hidden GPU activity | No continuous presentation or animation |
| Hidden host private working set | At or below 50 MB |
| Warm Guide-to-first-frame | p95 at or below 150 ms |
| Cold Guide-to-interactive | p95 at or below 500 ms |
| Controller-to-visual response | p95 at or below 50 ms |
| Overlay frame pacing | 60 Hz without adding repeated game-frame spikes |
| Background third-party workers | Resident by default; Visible/Interactive work stopped, per-widget cost measured and user-visible |
| Visible/Interactive default worker | Target at or below 64 MB; measured and user-visible |

The overlay must expose per-widget CPU, working set, wakeups, crash count, and network activity so users can identify bloat rather than merely trusting a store description.

Lifecycle is a user/developer contract rather than an eviction heuristic.
`keep-alive` is the default after first launch. `suspend-when-hidden` and
`unload-after-idle` are opt-in manifest/user policies; the host must not unload
a Background widget merely because a generic idle timer elapsed.

## Success criteria for the first vertical slice

- Guide toggles a native overlay in the supported presentation modes.
- Three fake widget cards can be reordered using only a controller.
- Activating a widget transfers every widget action to its context; B is
  offered to the active scope before the root-level Back fallback.
- Closing and reopening restores the active widget and stable focus ID.
- A minimal GBSS file can change tokens and focused-state treatment with live reload.
- A deliberately crashing sample widget does not terminate the overlay.
- Resource and latency measurements are captured automatically.
