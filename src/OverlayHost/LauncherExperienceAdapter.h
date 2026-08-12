#pragma once

#include "DeclarativeRenderer.h"
#include "LauncherExperienceLayout.h"
#include "LauncherExperiencePresentation.h"

#include <string>
#include <vector>

namespace gba::launcher {

struct SlotContent final {
    Slot slot{Slot::GameRail};
    WidgetSnapshot snapshot;
};

struct RenderedExperience final {
    LayoutResult layout;
    WidgetSnapshot semanticSnapshot;
    RenderResult render;
};

/// Renders host-created semantic slot snapshots into a validated launcher
/// recipe. Recipes never create WidgetNode content or action IDs. One existing
/// DeclarativeRenderer remains the only painter and geometry producer.
[[nodiscard]] RenderedExperience RenderExperience(
    DeclarativeRenderer& renderer,
    ID2D1RenderTarget* target,
    const Recipe* recipe,
    Preset preset,
    declarative::Rect workArea,
    const std::vector<SlotContent>& contents,
    std::wstring_view focusedElementId,
    DeclarativeRenderOptions options = {},
    const LauncherPresentationFrame* presentation = nullptr,
    const WidgetSnapshot* semanticEnvelope = nullptr);

} // namespace gba::launcher
