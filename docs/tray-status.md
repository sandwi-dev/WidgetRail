# Tray status

The passive tray status area displays the local time without seconds and a short
date using Windows regional settings. Wide trays also show an internet globe
and Bluetooth radio indicator. The clock never becomes a controller action.
The selected widget remains centered; narrow layouts use a compact clock and
exceptionally narrow diagnostic surfaces omit the status area.

Internet status comes from Windows' current internet connection profile, for
both Ethernet and Wi-Fi. Online, offline, limited/sign-in-required and unknown
states remain distinct. Bluetooth indicates radio power, not connected devices.
Active indicators use the theme's selected text color; inactive indicators use
secondary text, with a slash for offline/off and a dot for unknown status.
The clock uses the theme's tray background, foreground, rounding and hint font.
Screen readers can read the full status without minute-by-minute announcements.

Windows status discovery runs in the background at most once every five seconds
while visible, with at most one pending request. Stale results become unknown
after 30 seconds. Clock checks use the existing one-second host timer and change
pixels only when the displayed time or status changes. DirectComposition updates
only the tray layer, preserving widget content, guide, and background frames.
