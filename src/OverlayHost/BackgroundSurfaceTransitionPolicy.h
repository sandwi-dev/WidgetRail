#pragma once

#include <cstddef>
#include <cstdint>

namespace widgetrail::background_surface_policy {

inline constexpr std::uint64_t SettleMilliseconds = 150;
inline constexpr std::uint64_t FadeMilliseconds = 400;
inline constexpr std::size_t MaximumRebaseBytes = 32U * 1024U * 1024U;

} // namespace widgetrail::background_surface_policy
