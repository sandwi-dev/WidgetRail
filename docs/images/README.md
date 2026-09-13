# Add the screenshots

The README has three visible placeholders. The authoring guide has a fourth.
Replace them with your own screenshots when you are happy with the layout.

| Slot | Capture | Save as | Replace the image link in |
|---|---|---|---|
| Overview | WidgetRail open over a game, with tray, controller guide, and a useful widget visible | `docs/images/overview.png` | Root `README.md` |
| Pinned video | A playing video beside a game, with the main overlay hidden; make the video large enough to recognize | `docs/images/pinned-video.png` | Root `README.md` |
| Themes | The same widget and focus state in two installed themes, placed side by side | `docs/images/themes.png` | Root `README.md` |
| SDK Gallery | A gallery page with several controls and a visible focus highlight | `docs/images/sdk-gallery.png` | `docs/developers/widget-authoring-guide.md` |

For example, change `docs/images/overview-placeholder.svg` to
`docs/images/overview.png` in the root README. The other slots work the same way.
The gallery link is relative to its guide: `../images/sdk-gallery.png`.

Aim for a 16:9 capture around 1440×810 or 1920×1080. The theme comparison can use
two matched crops on that canvas. Keep enough context to explain what the overlay
adds, but avoid shrinking its text until it is unreadable on GitHub.

Use ordinary product settings. Hide tokens, account emails, private window titles,
and other personal information. Use game/video material you have permission to
publish. A public-domain or self-recorded video works well for the pinned example.

The existing SVGs are labeled placeholders, not simulated product screenshots.
Remove unused placeholder files after replacing all their links. No screenshots
were captured as part of this documentation change.
