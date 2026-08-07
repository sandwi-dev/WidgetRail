#pragma once

#include "ControllerNavigation.h"
#include "DeclarativeRenderer.h"

#include <optional>
#include <string>
#include <string_view>

namespace gba::input {

/// Finds the closest enabled focus target in a direction. Candidates whose
/// perpendicular span overlaps the current control are preferred, producing
/// stable row/column behavior before falling back across asymmetric layouts.
[[nodiscard]] std::optional<std::wstring> FindGeometricFocusTarget(
    std::wstring_view currentId,
    NavigationDirection direction,
    const RenderResult& renderResult);

[[nodiscard]] bool IsEnabledFocusTarget(
    std::wstring_view id,
    const RenderResult& renderResult) noexcept;

} // namespace gba::input
