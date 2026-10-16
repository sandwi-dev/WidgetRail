# MapGenie community webapp

## Scope

- Public `WebBrowserOptions.WebApp` declaration at `https://mapgenie.io/`.
- Persistent widget-isolated profile, on-demand chrome, controller interaction,
  hosted new windows and full-widget pinning through the shared framework.
- Custom map-pin SVG; no website-specific scripts or extra widget permissions.
- Marketplace-only package; no inclusion in either installer edition.
- Existing Task Switcher hint edit remains separate and untouched.

## Evidence

- `artifacts/mapgenie/package01/widgetrail.samples.mapgenie-0.1.0.wrwidget` built
  and passed manifest/SVG/archive validation, with generated SHA256 sidecars.
- `artifacts/mapgenie/ready.scenario.json`: isolated preview worker completed with
  zero diagnostics; URL, persistent profile, hosted windows and focus declaration
  were inspected without creating a browser surface.
- `artifacts/mapgenie/release-packaging.log`: all packaging self-tests passed,
  including exclusion of MapGenie from both editions.
- Installed and enabled 0.1.0 in the normal local catalog; verified using CLI list.
- The curated marketplace entry is source-only until the next host build. Remote
  download also requires publishing the matching archive. Nothing was published.

## Remaining acceptance

The browser tool's site-safety policy rejected automated access to MapGenie.
No alternate browser or network route was used. Live page rendering, map panning,
zooming, marker/filter controls, pinning and optional sign-in persistence require
manual acceptance. Offline validation does not prove site compatibility.
