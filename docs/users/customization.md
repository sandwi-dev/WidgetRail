# Customization

Open Settings to adjust the overlay. Changes to Quit, Restart, or startup here
refer to **WidgetRail**, not shutting down or restarting Windows. PC power
actions live in the Power widget.

## Appearance

New installations start with the Neon Circuit theme.

Choose a theme and adjust the interface scale until text and controls are easy
to read from where you play. Shared widget controls and controller hints use
theme colors. Widgets may also supply their own artwork and style choices.

**Settings → Overlay → Interface size** ranges from 50% to 125%. Lower it if
Windows display scaling makes the overlay too large. The size is saved per display.

Accessibility settings can adjust text and contrast, motion, and transparency.
Try changes in a widget you use often so you can see how they affect real content.

**Settings → Overlay → Section animation** controls page changes inside widgets:
**Slide** (default) moves pages horizontally; **Paging** raises the next page
while the previous page recedes; **Vertical slide** moves pages up or down;
**Reveal** uncovers stationary content; **Cover slide** moves the new page over
the previous page; **None** changes pages immediately.
**Animate widget dialogs** independently controls opening and closing motion.
**Focus animation** offers **Fade** (default), **Settle**, and **None**.
Settle expands the outline from up to 6 DIPs inside the control over 220 ms, starting at 65% opacity;
the background fades on the same timeline. Fade changes opacity over 240 ms without
outline movement. These effects leave the control geometry unchanged. Existing
WRSS scale and press feedback still apply to the whole control, including its outline.
Controller hints appear immediately with Settle and Fade. Animation speed and reduced
motion apply to focus highlights; saved animation choices remain respected.

**Dialog animation** offers **Zoom** (default), which grows the dialog from 90%
to full size, and **Lift**, which slides it gently into place. Explicit saved
choices remain respected when defaults change.
**Widget animation speed** ranges from **0.5×** (slower) to **2×** (faster), with
**1×** as normal speed. It scales section, navigation-indicator, focus-outline and dialog timing
together. These preferences apply immediately and survive restarts. Reduced motion,
including the Windows preference when followed, overrides both without changing
your saved choices. Tray widget-switch animation remains in **Settings → Appearance**.

Focus normally fades in place using a thin theme-accent edge and surface emphasis.
Panels, controls and dialogs use subtle shading and shadows to show depth.
Old focus Slide preferences migrate to Fade; section Slide remains available.
High contrast retains
a stronger outline, and reduced motion disables animated focus changes.
Widget or theme authors can instead use WRSS scaling to enlarge a focused card
with its text and artwork. This uses the existing styling rules, rather than a
separate competing zoom setting.
Games and Apps cards, Settings category cards, Playnite posters, YouTube Music
Home posters, and the Spotify and YouTube Music play buttons use subtle focus
enlargement. Their layout positions stay fixed. Animation speed applies to the
effect; Reduced motion switches sizes immediately without animation.
Buttons and action surfaces compress while pressed, then return to their normal
or focused size. Context menus and dropdowns open with a short zoom toward their
trigger. Both effects follow Animation speed and Reduced motion; commands and
menu dismissal remain immediate.

## Arrange your tray

Choose **Settings → Overlay → Position** to place the overlay in the center,
bottom left, or bottom right. Center is the default.

Corner layouts keep the outside edge and bottom edge of the widget fixed as it
resizes. The guide and rail stay in place. The open widget is the first visible
rail icon at bottom left, or the last at bottom right; your saved widget order
does not change. Guide hints align with the chosen corner and remain centered
in Center mode. Position works with both Rail and Radial + rail.

Reorder widgets to keep your frequent actions close together. The tray's
controller guide shows the reorder and reload actions. You can disable widgets
you do not use, including built-in widgets other than Settings.

## Manage widgets

**Settings → Widgets** is the place to review permissions, enable or disable a
widget, install packages, and manage versions.

- **Install:** choose a `.wrwidget` file. A toast explains where to review permissions and enable it.
- **Update:** use **Update from file** in the widget's details. A successful update needs review and enablement; new permissions are not granted automatically.
- **Remove old versions:** use the version controls to remove an unused version. The current version is protected.
- **Uninstall:** remove a community widget from its details. If it is enabled, confirm **Disable and uninstall**. Its saved data is kept.

Built-in widgets belong to the application release. Disable them if you do not
want them; they are not separately uninstalled or updated from a package file.

If you previously installed a widget that now comes with WidgetRail, Settings
labels that installed package as an **Unused copy**. Choose **Manage built-in
version** to enable the version the overlay uses. You can disable and remove
the extra copy separately. Local Data controls apply only to the copy you select.

## Permissions

Enable only the Windows capabilities you want a widget to use. A permission
describes access such as reading audio devices or switching windows.

A **full-access** widget runs as an ordinary Windows application and can do more
than a sandboxed widget. Review its source and publisher before enabling it.
An integrity checksum detects changed bytes; it does not establish who wrote them.

## Controller options

Choose either **View + Menu** or **Guide** as the opening shortcut. Optional
Exclusive control has separate driver requirements and compatibility limits.
Changing it may require reopening other applications before the change takes effect.
See [Compatibility](known-limitations.md) before using it.
