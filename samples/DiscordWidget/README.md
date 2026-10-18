# Discord webapp

Generated using the ordinary public CLI template:

```powershell
wrail new widget Discord --id widgetrail.samples.discord --publisher widgetrail.samples --template webapp --url https://discord.com/app
```

The source and manifest use the same contracts as external authors. The repository project references the SDK project instead of the generated offline NuGet feed. The host contains no Discord-specific selectors, token handling, or browser engine.

Install from **Catalogue**; Discord is not bundled in either installer. The widget uses persistent, isolated website sessions. Microphone, camera and screen-sharing consent are handled by the host's website controls, separately from widget capability permissions.

Build its `.wrwidget` package with `./Build-CommunityPackage.ps1`. This creates an archive and SHA-256 sidecar without installing or publishing. See [the implementation plan](../../docs/webapp-implementation-plan.md) for validation and platform limitations.

The package icon uses the Discord mark from [Simple Icons](https://github.com/simple-icons/simple-icons/blob/develop/icons/discord.svg) on a blurple background. It uses the shared SVG package-icon contract, with the connection glyph as fallback.
