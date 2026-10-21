# Display Controls

Adjust resolution, refresh rate and supported HDR from a controller, or save monitor setups as profiles. Install from **Catalogue**.

Choose **Adjust displays**, select the monitor and its resolution/refresh rate, then **Apply**. HDR has its own action. Keep the change within 15 seconds or WidgetRail restores the previous setup. Buttons stay focusable during the guarded operation.

Profiles retain their existing topology, orientation, primary-display and mode settings. HDR is an immediate adjustment and is not added to saved profiles. Windows scaling stays unchanged. Display access is requested when needed.

The widget uses the public SDK and contains no native display implementation. See [Display Controls](../../docs/reference/display-profiles.md) for capabilities, limitations and recovery.

## Verify and reset

No account, API key or companion is required. Open **Adjust displays**, confirm
that the intended monitor is listed, and try a supported change. Use the timed
Keep/revert prompt to verify the result. If access is denied, review this widget's
permissions in **Settings → Widgets**. Unsupported hardware controls are not an
authentication failure.

Disabling preserves saved profiles. Uninstalling or Clear local data removes
host-owned saved settings; it does not restore Windows display modes that you
already applied. Choose the previous mode/profile or use Windows Display settings
to undo an accepted change.

Run `./Build-CommunityPackage.ps1` to create a `.wrwidget` archive and SHA-256 sidecar without installing or publishing it.
