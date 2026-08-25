#pragma once

namespace widgetrail::surface_geometry {

inline constexpr float kMinimumAuthoredContentWidthDip = 240.0F;
inline constexpr float kMinimumAuthoredContentHeightDip = 180.0F;
inline constexpr float kMaximumAuthoredContentWidthDip = 1'600.0F;
inline constexpr float kMaximumAuthoredContentHeightDip = 1'200.0F;

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
