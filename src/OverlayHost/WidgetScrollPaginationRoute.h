#pragma once

#include "OverlayState.h"
#include "WidgetInteractionSession.h"
#include "WidgetSessionCoordinator.h"

#include <optional>
#include <string_view>

namespace widgetrail::input {

// Rendering a visible preview does not grant action authority. Queueing and
// dispatch must resolve the same route, otherwise each paint can recreate work
// that dispatch immediately rejects and retires.
[[nodiscard]] inline std::optional<WidgetInteractionAuthority>
ResolveScrollPaginationAuthority(
    Surface surface, std::wstring_view activeWidget, std::wstring_view widgetId,
    const WidgetSessionPresentation& presentation,
    const WidgetDescriptor* descriptor, bool failed,
    const WidgetSnapshot* renderedSnapshot = nullptr) noexcept {
    if (surface != Surface::Widget || widgetId != activeWidget || !descriptor || failed ||
        !presentation.HasCommittedViewAuthority(WidgetCommittedViewUse::Interaction) ||
        (renderedSnapshot && renderedSnapshot != presentation.snapshot)) {
        return std::nullopt;
    }
    return WidgetInteractionAuthority{
        widgetId, presentation.snapshot, descriptor->runtimeGeneration,
        descriptor->presentationGeneration, false};
}

} // namespace widgetrail::input
