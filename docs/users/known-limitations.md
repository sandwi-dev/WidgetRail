# Known limitations

These are the user-facing limits to consider in the source preview.
Historical bug ledgers do not describe current release status.

## Controller and game behavior

- An elevated game can prevent WidgetRail from taking foreground focus.
  Controller input may reach the game when the overlay first opens. This
  remains unresolved; running over every game is not a compatibility promise.
- Optional **Exclusive control** can produce duplicate input in Windows
  Settings and the Windows app switcher. It is disabled by default.
- If a game stops recognizing your controller, or input behaves unexpectedly,
  turn Exclusive control off. Enable it only when input is also reaching your
  game while you use the overlay.
- Changes to Exclusive control may require closing and reopening an
  application. An already-running application may retain its old controller
  connection.
- Guide behavior varies with controllers and Windows gaming features. The
  default View + Menu shortcut provides another way to open the overlay.

## Windows and services

- Some windows, including elevated or protected applications, cannot be switched
  to or previewed. Minimized windows can show an old or unavailable preview.
- Spatial sound choices depend on the output device, installed providers and
  provider licensing. Windows may reject a selection.
- Spotify, YouTube and other service integrations depend on provider accounts,
  access policies, quotas and availability. Consult each widget's setup guide.
- The source preview has no published installer, automatic application updater
  or verified clean-machine support matrix yet.

## Packages and trust

Package validation and hashes establish structure and exact bytes, not a
publisher's identity. Publisher signing and revocation are not implemented.
Review full-trust widgets as you would another Windows application.

For a new issue, include the build, controller, settings involved and
reproduction steps. Review logs before sharing them.
