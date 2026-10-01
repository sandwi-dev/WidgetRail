# Retired external-content deployment

The external-content MSIX route and its deployment helper were retired on
2026-10-01. WidgetRail now installs through Inno Setup and launches its unpackaged
WinUI executable directly. There is no application identity package or required
application signing certificate.

Use the [installer contract](../../eng/installer/README.md) and
[release checklist](release-checklist.md) for the current build and deployment
workflow. Previous external-content observations remain historical evidence in
[implementation status](winui3-implementation-status.md); they do not qualify the
current unpackaged installer.
