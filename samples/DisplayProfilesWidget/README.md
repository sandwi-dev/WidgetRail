# Display Profiles

Install from **Catalogue**; this community widget is not bundled in either installer. It uses the public SDK display-profile service and contains no native display implementation.

Save and restore monitor configurations with a timed Keep/Revert confirmation. Missing read/control permissions are requested when the user selects the corresponding feature. Denial leaves the display configuration unchanged. The timed confirmation never waits for another permission dialog.

Run `./Build-CommunityPackage.ps1` to create a `.wrwidget` archive and SHA-256 sidecar without installing or publishing it. See [Display Profiles](../../docs/reference/display-profiles.md).
