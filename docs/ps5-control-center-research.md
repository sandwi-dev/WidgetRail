# PS5 Control Center interaction research

Status: design input from primary Sony/PlayStation sources, 2026-08-07

## What Sony's design establishes

The PS5 Control Center is a transient, in-game layer: one press of the PS button opens it without leaving the game, while holding PS goes to the full home screen. Sony separates context-sensitive **cards** from stable system **controls**. Cards expose information and actions relevant to the current game or app; controls provide recurring functions such as music, sound, accessories, profile, and power. [PS5 button functions](https://www.playstation.com/en-us/support/hardware/ps5-button-functions/) · [Control Center customization and contents](https://www.playstation.com/en-us/support/games/customize-ps5-control-center/)

Cards are useful before the player enters a deeper menu. Sony's media card shows the current item, displays controller assignments on the card, and supports a direct play/pause shortcut. Selecting the card reveals additional playback controls. Sony also supports returning directly to the most recent card, reinforcing continuity rather than resetting navigation on every open. [Music and podcasts on PS5](https://www.playstation.com/en-us/support/subscriptions/how-to-stream-music-and-podcasts-on-ps5-consoles/)

Customization is performed entirely with the controller: highlight a control, enter edit mode, move it, confirm placement, or move it to a hidden area. Some essential controls remain non-hideable. Cards themselves are contextual—the official quick-start guide says their availability depends on what the player is doing at that moment. [Customize the Control Center](https://www.playstation.com/en-us/support/games/customize-ps5-control-center/) · [PS5 Quick Start Guide, Control Center section](https://www.playstation.com/content/dam/global_pdc/en-gb/corporate/support/manuals/ps5-docs/1100a/CFI-11XXA_PS5_Quick_Start_Guide%24en-gb.pdf)

Accessibility is a system concern above individual content. PS5 offers text sizing, bold text, high contrast, check marks that supplement state color, auto-scroll speed, reduced motion, zoom/magnification, and a screen reader. [PS5 accessibility settings](https://www.playstation.com/en-ca/support/hardware/ps5-accessibility-settings/)

## Concrete rules for this overlay

1. **One global toggle plus hierarchical Back.** Guide/Home toggles from any
   depth and no widget may bind it. B returns one level—nested widget view,
   widget root to dashboard, then dashboard to closed.
2. **Separate input surfaces.** On the dashboard, the host owns D-pad/analog
   navigation, `A` to open, `B` to close, `Y` to enter reorder mode, and Guide
   to close. Inside an opened widget,
   A and directional input retain host activation/focus semantics; the
   explicitly active widget scope receives B first and owns X, Y, bumpers,
   triggers, stick clicks, Menu, and View without bubbling to another scope.
3. **Cards answer “what now?”** A dashboard card shows a small live snapshot—state, one primary datum, and no more than three safe quick actions. It must not be a miniature full widget.
4. **Prompts are explicit.** Every quick action is supplied as `{ button, actionId, label }`; the card renders the button prompt beside its label. Never hide undocumented shortcuts behind a selected card.
5. **Quick actions cannot steal shell navigation.** Dashboard quick actions are limited to `X`, bumpers, triggers, and stick clicks. `A`, `B`, `Y`, D-pad, Menu, and View remain host-owned on the dashboard.
6. **Quick actions are low-risk.** Prefer immediate, familiar, reversible actions such as play/pause, previous/next, mute, or accept/decline. Destructive, purchasing, account, permission, and text-entry flows require opening the widget and explicit confirmation.
7. **Context controls availability.** A widget publishes only actions that work in its current state. A disconnected music card, for example, exposes no transport shortcuts.
8. **Opening deepens; it does not reset.** `A` opens the selected card's widget at its last stable focus and state. Closing and reopening resumes the last-used card/widget unless that state is no longer valid.
9. **Reorder is a visible mode.** On dashboard `Y` enters reorder mode; D-pad moves the card, `A` confirms, `B` cancels. Persist order and hidden status. Do not overload ordinary navigation with accidental rearrangement.
10. **Stable system controls stay available.** Reserve a small host-owned set for essentials such as settings, notifications, sound, controller battery, and exit. Community widgets cannot replace or hide safety-critical host controls.
11. **Accessibility overrides themes.** Focus must remain visible without relying only on color. Text scale, bold/high-contrast state, reduced motion, screen-reader labels, and “show an enabled mark” semantics override GBSS styling.
12. **Design for interruption.** Opening is fast, card content is glanceable, animations are brief or disabled, and returning to the game is one Guide press from any widget depth.

## YT Music dashboard contract

The connected YT Music snapshot publishes exactly these quick actions for the native dashboard host:

| Button | Action ID | Label |
| --- | --- | --- |
| `LB` | `previous` | Previous track |
| `X` | `toggle-playback` | Play or pause |
| `RB` | `next` | Next track |

The host routes them through the dashboard controller-input context. The SDK
resolves the declared quick action with `SourceElementId = "dashboard-card"`,
the matching `ControllerButton`, and `Phase = Pressed`. `Y` never appears in
widget quick-action metadata because it is the host's dashboard reorder
command. The same `Y` button remains available to an opened widget; YT Music
uses it for `refresh` there.
