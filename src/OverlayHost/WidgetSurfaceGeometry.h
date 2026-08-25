#pragma once

#include "WidgetProtocolPresentationContract.generated.h"

namespace widgetrail::surface_geometry {

inline constexpr float kMinimumAuthoredContentWidthDip =
    static_cast<float>(protocol_contract::MinimumSurfaceWidth);
inline constexpr float kMinimumAuthoredContentHeightDip =
    static_cast<float>(protocol_contract::MinimumSurfaceHeight);
inline constexpr float kMaximumAuthoredContentWidthDip =
    static_cast<float>(protocol_contract::MaximumSurfaceWidth);
inline constexpr float kMaximumAuthoredContentHeightDip =
    static_cast<float>(protocol_contract::MaximumSurfaceHeight);

inline constexpr float kMinimumPinnedWidthDip = 240.0F;
inline constexpr float kMinimumPinnedHeightDip = 135.0F;
inline constexpr float kPinnedChromeHeightDip = 36.0F;
inline constexpr float kPinnedSideInsetDip = 8.0F;
inline constexpr float kPinnedBottomInsetDip = 8.0F;
inline constexpr float kPlacementAdjustmentStepDip = 32.0F;
inline constexpr float kMaximumPinnedWidthDip =
    kMaximumAuthoredContentWidthDip + kPinnedSideInsetDip * 2.0F;
inline constexpr float kMaximumPinnedHeightDip =
    kMaximumAuthoredContentHeightDip + kPinnedChromeHeightDip + kPinnedBottomInsetDip;

} // namespace widgetrail::surface_geometry
