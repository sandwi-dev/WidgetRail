#pragma once

#include "DeclarativeLayout.h"

#include <map>
#include <optional>
#include <string>
#include <vector>

namespace gba::launcher {

enum class Preset { HeroRail, CoverWall, Carousel, CompactGrid };
enum class Branch { Compact, Standard, Wide };
enum class Primitive { Region, Grid, Stack, Overlay, Inset };
enum class Slot {
    HeroBackground,
    GameRail,
    DetailsPanel,
    CollectionTabs,
    SourceStatus,
    OperationStatus,
    SystemStatus,
    ControllerHints,
};
enum class Orientation { Horizontal, Vertical };
enum class Surface { Solid, Glass };

struct NormalizedRect final {
    float x{};
    float y{};
    float width{1.0F};
    float height{1.0F};
};

struct Insets final {
    float left{};
    float top{};
    float right{};
    float bottom{};
};

struct RecipeNode final {
    Primitive type{Primitive::Region};
    NormalizedRect region;
    Insets insets;
    std::optional<Slot> slot;
    std::optional<Orientation> orientation;
    std::optional<Surface> surface;
    std::optional<int> rows;
    std::optional<int> columns;
    std::vector<RecipeNode> children;
};

struct Recipe final {
    int schemaVersion{1};
    std::map<Branch, RecipeNode> branches;
};

struct SlotPlacement final {
    Slot slot{Slot::GameRail};
    declarative::Rect bounds;
    std::optional<Orientation> orientation;
    std::optional<Surface> surface;
    std::size_t paintOrder{};
};

struct LayoutDiagnostic final {
    std::string path;
    std::string code;
};

struct LayoutResult final {
    Branch branch{Branch::Standard};
    Preset preset{Preset::HeroRail};
    bool usedFallback{};
    std::vector<SlotPlacement> paintPlacements;
    std::vector<SlotPlacement> semanticPlacements;
    std::vector<LayoutDiagnostic> diagnostics;
    std::vector<LayoutDiagnostic> fallbackDiagnostics;

    [[nodiscard]] bool valid() const noexcept;
    [[nodiscard]] const SlotPlacement* Find(Slot slot) const noexcept;
};

[[nodiscard]] Recipe BuiltInRecipe(Preset preset);

/// Code-owned Hero Rail recovery composition used only after the ordinary
/// trusted-artwork pipeline reports that every tracked rail image is
/// terminally unavailable. It keeps every semantic slot while reclaiming the
/// otherwise empty artwork region.
[[nodiscard]] Recipe BuiltInNoArtworkHeroRailRecipe();

/// Resolves a previously validated recipe against the live logical work area.
/// Native compatibility is checked again so an incompatible revision is
/// replaced atomically by the matching code-owned preset.
[[nodiscard]] LayoutResult ResolveLayout(
    const Recipe* recipe,
    Preset preset,
    declarative::Rect workArea,
    float textScale = 1.0F);

[[nodiscard]] std::string_view SlotName(Slot slot) noexcept;

} // namespace gba::launcher
