# Display Controls

Adjust resolution, refresh rate and supported HDR from a controller, or save monitor setups as profiles. Install from **Catalogue**.

Choose **Adjust displays**, select the monitor and its resolution/refresh rate, then **Apply**. HDR has its own action. Keep the change within 15 seconds or WidgetRail restores the previous setup. Buttons stay focusable during the guarded operation.

Profiles retain their existing topology, orientation, primary-display and mode settings. HDR is an immediate adjustment and is not added to saved profiles. Windows scaling stays unchanged. Display access is requested when needed.

The widget uses the public SDK and contains no native display implementation. See [Display Controls](../../docs/reference/display-profiles.md) for capabilities, limitations and recovery.

Run `./Build-CommunityPackage.ps1` to create a `.wrwidget` archive and SHA-256 sidecar without installing or publishing it.
