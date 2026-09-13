# Compatibility and limitations

WidgetRail is preview software. These notes help you choose settings and understand
cases where Windows, a game, or a service can limit a feature.

## Games and controller input

Some games can keep foreground focus when the overlay first opens, allowing
input to reach the game too. Windows foreground restrictions and application
privileges can affect this. Compatibility with every game is not established.

Optional **Exclusive control** is off by default. It needs separate HidHide and
ViGEmBus components and can cause duplicate input in Windows Settings or the
Windows app switcher. Changes may only affect another application after you
close and reopen that application.

If controller behavior becomes confusing, turn Exclusive control off and reopen
the affected app. It is not required for ordinary WidgetRail navigation.

The Guide button can interact with Windows gaming features. View + Menu is the
default alternative. Windowed or borderless games are a better starting point
for desktop overlays than exclusive fullscreen.

## Media and Windows features

Protected or elevated windows may not be available for switching or preview.
Minimized windows can have stale or unavailable previews. A preview remains
view-only; its surrounding widget handles actions.

Spatial sound choices depend on the output device and installed providers.
Windows can reject an option that needs a license. Provider-specific errors
should leave the widget usable.

Spotify, YouTube, and other services have their own account requirements, quotas,
and playback policies. Pinning a view does not bypass them.

## Distribution and trust

Updates are manual. There is no automatic widget marketplace or publisher-signing
service. A package checksum verifies bytes, not the publisher's identity.
Treat full-access widgets as you would any other Windows application.

The project has locally tested installers. That does not imply certification on
every Windows configuration. See the [release page](https://github.com/sandwi-dev/WidgetRail/releases)
for the status and limits of a particular published build.
