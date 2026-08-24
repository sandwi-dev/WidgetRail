#include "PinnedSurfacePolicy.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <utility>

namespace widgetrail::pinned {
namespace {

constexpr float kDefaultWidthDip = 480.0F;
constexpr float kDefaultHeightDip = 270.0F;
constexpr float kMinimumWidthDip = 240.0F;
constexpr float kMinimumHeightDip = 135.0F;
constexpr float kMaximumWidthDip = 960.0F;
constexpr float kMaximumHeightDip = 540.0F;
constexpr float kMarginDip = 16.0F;

[[nodiscard]] bool ValidMonitor(const MonitorWorkArea& monitor) noexcept {
    const auto& area = monitor.workArea;
    const auto width = static_cast<long long>(area.right) - area.left;
    const auto height = static_cast<long long>(area.bottom) - area.top;
    return !monitor.stableId.empty() && monitor.dpi >= 48 && monitor.dpi <= 960 &&
        width > 0 && height > 0 && width <= std::numeric_limits<int>::max() &&
        height <= std::numeric_limits<int>::max();
}

[[nodiscard]] bool Finite(const float value) noexcept {
    return std::isfinite(value);
}

[[nodiscard]] int ScaleDip(const float value, const unsigned int dpi) noexcept {
    const double scaled = static_cast<double>(value) * dpi / 96.0;
    if (scaled <= std::numeric_limits<int>::min()) return std::numeric_limits<int>::min();
    if (scaled >= std::numeric_limits<int>::max()) return std::numeric_limits<int>::max();
    return static_cast<int>(std::lround(scaled));
}

} // namespace

ControllerCommand ResolveControllerCommand(
    const ControllerInputContext& context) noexcept {
    if (!context.pinned) return ControllerCommand::None;
    if (context.xPressed && context.leftShoulderDown && context.rightShoulderDown)
        return ControllerCommand::EmergencyHide;
    if (context.placementActive) return ControllerCommand::None;
    if (context.controllerFocused) {
        if (context.bPressed) return ControllerCommand::Exit;
        if (context.aPressed) return ControllerCommand::Activate;
        return ControllerCommand::None;
    }
    return ControllerCommand::None;
}

bool SurfacePolicy::Pin(SurfaceDescriptor descriptor) {
    if (descriptor.id.empty() || descriptor.accessibleName.empty()) return false;
    descriptor_ = std::move(descriptor);
    interactionMode_ = InteractionMode::ClickThrough;
    state_ = LifecycleState::Pinned;
    return true;
}

void SurfacePolicy::SetInteractionMode(const InteractionMode mode) noexcept {
    if (state_ == LifecycleState::Pinned) interactionMode_ = mode;
}

void SurfacePolicy::OnMainOverlayHidden() noexcept {
    // A pinned surface is a peer top-level host window, not an owned child of
    // the transient overlay window. Hiding the overlay is intentionally a no-op.
}

void SurfacePolicy::Stop(const StopReason reason) noexcept {
    descriptor_.reset();
    interactionMode_ = InteractionMode::ClickThrough;
    lastStopReason_ = reason;
    state_ = reason == StopReason::Unpin
        ? LifecycleState::Unpinned
        : LifecycleState::Stopped;
}

std::vector<SemanticNode> SurfacePolicy::ProjectSemantics() const {
    if (state_ != LifecycleState::Pinned || !descriptor_) return {};
    const bool focusable = interactionMode_ == InteractionMode::Focusable;
    return {
        {L"host.pinned.heading", descriptor_->accessibleName, L"", false},
        {L"host.pinned.status", L"Pinned surface mode",
         focusable ? L"Focusable" : L"Click-through", focusable},
    };
}

std::optional<ResolvedPlacement> ResolvePlacement(
    const std::vector<MonitorWorkArea>& monitors,
    const std::optional<PersistedPlacement>& persisted) noexcept {
    const MonitorWorkArea* selected{};
    bool fallback = false;
    if (persisted && !persisted->monitorId.empty()) {
        const auto match = std::ranges::find_if(monitors, [&](const auto& monitor) {
            return ValidMonitor(monitor) && monitor.stableId == persisted->monitorId;
        });
        if (match != monitors.end()) selected = &*match;
    }
    if (!selected) {
        fallback = persisted.has_value();
        const auto primary = std::ranges::find_if(monitors, [](const auto& monitor) {
            return ValidMonitor(monitor) && monitor.primary;
        });
        if (primary != monitors.end()) selected = &*primary;
    }
    if (!selected) {
        const auto first = std::ranges::find_if(monitors, ValidMonitor);
        if (first == monitors.end()) return std::nullopt;
        selected = &*first;
        fallback = persisted.has_value();
    }

    const auto& work = selected->workArea;
    const int workWidth = static_cast<int>(
        static_cast<long long>(work.right) - work.left);
    const int workHeight = static_cast<int>(
        static_cast<long long>(work.bottom) - work.top);
    bool persistedValid = persisted && !fallback &&
        Finite(persisted->leftDip) && Finite(persisted->topDip) &&
        Finite(persisted->widthDip) && Finite(persisted->heightDip) &&
        std::abs(persisted->leftDip) <= 1'000'000.0F &&
        std::abs(persisted->topDip) <= 1'000'000.0F &&
        persisted->widthDip > 0.0F && persisted->heightDip > 0.0F;
    fallback = fallback || (persisted.has_value() && !persistedValid);

    float widthDip = persistedValid ? persisted->widthDip : kDefaultWidthDip;
    float heightDip = persistedValid ? persisted->heightDip : kDefaultHeightDip;
    const float boundedWidthDip = std::clamp(widthDip, kMinimumWidthDip, kMaximumWidthDip);
    const float boundedHeightDip = std::clamp(heightDip, kMinimumHeightDip, kMaximumHeightDip);
    int width = std::min(workWidth, std::max(1, ScaleDip(boundedWidthDip, selected->dpi)));
    int height = std::min(workHeight, std::max(1, ScaleDip(boundedHeightDip, selected->dpi)));

    long long x{};
    long long y{};
    if (persistedValid) {
        x = static_cast<long long>(work.left) +
            ScaleDip(persisted->leftDip, selected->dpi);
        y = static_cast<long long>(work.top) +
            ScaleDip(persisted->topDip, selected->dpi);
    } else {
        const int margin = ScaleDip(kMarginDip, selected->dpi);
        x = static_cast<long long>(work.right) - width - margin;
        y = static_cast<long long>(work.top) + margin;
    }
    const int boundedX = static_cast<int>(std::clamp(
        x, static_cast<long long>(work.left),
        static_cast<long long>(work.right) - width));
    const int boundedY = static_cast<int>(std::clamp(
        y, static_cast<long long>(work.top),
        static_cast<long long>(work.bottom) - height));
    const bool clamped = boundedX != x || boundedY != y ||
        widthDip != boundedWidthDip || heightDip != boundedHeightDip ||
        width == workWidth || height == workHeight;
    return ResolvedPlacement{
        selected->stableId,
        {boundedX, boundedY, boundedX + width, boundedY + height},
        selected->dpi,
        fallback,
        clamped,
    };
}

} // namespace widgetrail::pinned
