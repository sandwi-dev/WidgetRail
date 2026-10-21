# Discord webapp

Use Discord's website from the rail with WidgetRail's controller browsing and a
persistent sign-in. Install from **Catalogue**; it is not bundled in either edition.
Sign in through Discord's page, then open a server or conversation to check that
your account is connected. There is no WidgetRail API key or companion setup.

Microphone, camera and screen-sharing permissions are managed by the host's
website controls. Grant only what you need. Website compatibility can vary;
loading Discord is not proof that calls or every authentication flow work.
See [controller browsing](../../docs/users/browsing.md).

Use Discord's own sign-out action to disconnect the account. Disabling the widget
keeps its local sign-in; uninstalling or **Clear local data** removes its host-owned
browser profile. This does not delete your Discord account or conversations.
[Saved-data behavior](../../docs/users/lifecycle.md).

## Build or customize

Generated using the ordinary public CLI template:

```powershell
wrail new widget Discord --id widgetrail.samples.discord --publisher widgetrail.samples --template webapp --url https://discord.com/app
```

The source and manifest use the same contracts as external authors. The repository project references the SDK project instead of the generated offline NuGet feed. The host contains no Discord-specific selectors, token handling, or browser engine.

Build its `.wrwidget` package with `./Build-CommunityPackage.ps1`. This creates an archive and SHA-256 sidecar without installing or publishing. See [the hosted-browser reference](../../docs/reference/widget-web-browser.md) for input, permissions, profile isolation and platform limitations.

The package icon uses the Discord mark from [Simple Icons](https://github.com/simple-icons/simple-icons/blob/develop/icons/discord.svg) on a blurple background. It uses the shared SVG package-icon contract, with the connection glyph as fallback.
