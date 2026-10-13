# Discord webapp

Generated using the ordinary public CLI template:

```powershell
wrail new widget Discord --id widgetrail.samples.discord --publisher widgetrail.samples --template webapp --url https://discord.com/app
```

The source and manifest use the same contracts as external authors. The repository project references the SDK project instead of the generated offline NuGet feed. The host contains no Discord-specific selectors, token handling, or browser engine.

This is an in-progress consumer of the webapp framework. See [the implementation plan](../../docs/webapp-implementation-plan.md) for verified behaviour and remaining acceptance. Do not infer voice, screen-sharing, or login compatibility from successful compilation.
