# WinUI accessibility preferences

The native style adapter projects saved settings after WRSS resolution:

- Bold Text raises text/control weights to at least 600 while preserving heavier
  headings. Turning it off restores the authored or original native value. Glyph
  font families and glyph presentation remain separate.
- Contrast follows System, Standard or High explicitly. Standard preserves the
  authored palette even when Windows high contrast is enabled; High uses native
  system colors. The focus-decoration owner restores native high-contrast focus.
- Reduced Transparency makes painted translucent backgrounds and explicit subtree
  opacity opaque. Intentionally absent fills remain absent instead of becoming
  new opaque boxes. Borders and text alpha retain their separate meaning.

Policy updates notify existing native style owners, including realized indexed
containers and presentation fragments. They do not publish widget snapshots,
change focus identity or accumulate font scaling. Text Scale continues through
the native typography projection.

The integrated autonomous native style page passes 79 checks, including the six
preference checks and existing geometry/focus/shading regressions. Evidence is
`artifacts/winui-ytmusic/accessibility-result.json`. No Windows preference was
changed: the fixture supplies the existing test-only system contrast override.
Actual Windows theme switching and full accessibility acceptance remain separate.
