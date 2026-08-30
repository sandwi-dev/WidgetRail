#pragma once

#include "OverlayPlacement.h"
#include "WidgetBridgeClient.h"

#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail::guide {

struct OpenWidgetAction final {
    std::wstring button;
    std::wstring label;
};

struct OpenWidgetAuthority final {
    std::vector<OpenWidgetAction> actions;
    bool focusedActivation{};
};

/// Resolves presentation metadata only for the exact action path admitted by
/// the current active input scope. Input remains owned by the SDK/Bridge route.
[[nodiscard]] OpenWidgetAuthority ResolveOpenWidgetAuthority(
    const WidgetSnapshot& snapshot,
    std::wstring_view focusedElementId);

struct OpenWidgetLine final {
    std::wstring contextual;
    std::wstring host;
    std::wstring accessible;
};

using MeasureOpenWidgetText =
    std::function<std::optional<float>(std::wstring_view)>;

/// Builds one bounded, whitespace-sanitized line while always retaining the
/// host escape actions. Contextual actions are removed by deterministic
/// priority when complete labels do not fit; labels are never truncated.
/// Accessibility text is the same complete resolved line.
[[nodiscard]] OpenWidgetLine BuildOpenWidgetLine(
    ControllerGuideDensity density,
    const OpenWidgetAuthority& authority,
    bool hasBack,
    float availableWidth,
    const MeasureOpenWidgetText& measureText);

} // namespace widgetrail::guide
