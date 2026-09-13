# Third-party dependencies

## Native layout engine

The production native overlay uses Taffy 0.12.2 for geometry calculation. It is
built as an x64 MSVC Rust static library and linked into the existing native
executables. Rust is a build-time dependency only; the installed product does
not require a Rust runtime or a separate layout process.

The integration deliberately exposes a narrow, product-owned C ABI in
`src/OverlayHost/TaffyLayoutBridge.h`. Only fixed-size plain data, one
synchronous intrinsic-measurement callback, integer result codes, and a bulk
layout output cross the boundary. Rust types, ownership, panics, allocators,
rendering, input, focus, scrolling, accessibility, and HWND authority do not.

### Reproducibility

- Rust channel: 1.97.1, pinned by `rust-toolchain.toml`.
- Target: `x86_64-pc-windows-msvc`.
- Taffy: exactly 0.12.2.
- Resolution: `Cargo.lock`, built with `cargo build --locked`.
- Enabled Taffy features: `std`, `taffy_tree`, `flexbox`, `grid`, and
  `content_size`.
- Release builds use thin LTO and one code-generation unit.

The active linked dependency tree is Taffy 0.12.2, arrayvec 0.7.8, grid 1.0.1,
and slotmap 1.1.1. Cargo may retain metadata for optional packages in the lock
file, but they are not part of the enabled build graph. Redistribution notices
are in `THIRD_PARTY_NOTICES.md` and are copied beside the native binaries.

### Ownership boundary

Taffy replaces the former custom flex/grid geometry solver. The product still
owns semantic-tree validation and style translation, DirectWrite intrinsic text
measurement, scroll offsets and extents, clipping, DPI snapping, rendering,
animation, controller navigation, focus restoration, UI Automation, process
lifecycle, and the single overlay window. The Widget SDK and wire protocol are
unchanged.

The bridge maps generic semantic layout properties to Taffy Flexbox and CSS
Grid. Responsive grids derive an explicit capped track count from the admitted
content width, then let Taffy perform final track sizing and placement. There
are no widget-identity branches in the bridge.
