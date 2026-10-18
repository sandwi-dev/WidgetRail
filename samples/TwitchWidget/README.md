# Twitch webapp

A community widget for Twitch, built with the ordinary public webapp template:

```powershell
wrail new widget Twitch --id widgetrail.samples.twitch --publisher widgetrail.samples --template webapp --url https://www.twitch.tv/
```

The repository copy uses a project reference to the SDK and a custom streaming icon. Its browser declaration is unchanged from the template. There is no Twitch-specific DOM code, player replacement, credential handling or API key.

## Use

The page starts at Twitch's home page. Left stick moves the pointer, D-pad jumps between controls, A clicks or opens the controller keyboard on an editable field, and right stick scrolls. LS opens Website controls. B closes transient controls or a hosted pop-out before following the normal framework navigation.

Pin the whole widget through the tray. If Twitch opens a separate player or chat window, the shared host offers to open it in the widget or pin it, with the existing remember-choice option. This depends on Twitch actually requesting a new window; a page navigation does not create a pop-out. No automatic extraction of a player from the page is performed.

The host keeps a persistent website profile isolated from Discord, Browser and desktop browsers. Website controls manage permissions, clearing website data and stopping the session. Background residency keeps the page alive while hidden; it does not guarantee that Twitch itself will keep every stream playing. Twitch owns playback, advertisements and account requirements. Do not assume that successful home-page rendering proves sign-in or protected playback works.

## Build and install

```powershell
./samples/TwitchWidget/Build-CommunityPackage.ps1
wrail install <path-to-widgetrail.samples.twitch-0.1.0.wrwidget>
wrail enable widgetrail.samples.twitch
```

The builder validates the package and produces SHA-256 sidecars. It does not install or publish. Twitch is not bundled in either installer. Its curated catalogue listing will become downloadable after a matching package is published; local installation works before publication.

## Physical test checklist

- Browse a category and channel using D-pad and pointer movement; search with the controller keyboard.
- Play a public stream; check audio, volume and fullscreen entry/exit.
- Pin, resize and change opacity, then hide/reopen the overlay and switch widgets during playback.
- Try Twitch's player/chat pop-out; test both placement choices and return from it with B.
- If desired, sign in yourself, restart WidgetRail, and check session retention. Chat entry can be checked without sending a message.

See [the hosted-browser reference](../../docs/reference/widget-web-browser.md) for shared behavior. The checklist above still requires verification against the current Twitch website; local fixtures do not establish live-site compatibility.
