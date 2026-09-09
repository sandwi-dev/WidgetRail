#include "ControllerIsolationReader.h"

#include <algorithm>
#include <limits>
#include <type_traits>

namespace widgetrail::isolation {
namespace {

template <std::size_t Size>
[[nodiscard]] bool HasBytes(
    const std::array<std::uint8_t, Size>& value) noexcept {
    return std::ranges::any_of(value, [](const std::uint8_t part) {
        return part != 0;
    });
}

} // namespace

bool SelectedControllerEnrollment::valid() const noexcept {
    return enrollmentToken != 0 && HasBytes(deviceId) &&
        HasBytes(deviceRootId) && HasBytes(containerId) &&
        HasBytes(normalizedPnpPathDigest) && vendorId != 0 && productId != 0 &&
        deviceFamily > 0 && connected && gamepadSupported &&
        !knownVirtualOutput;
}

ControllerIsolationReaderIngress::ControllerIsolationReaderIngress() noexcept {
    for (std::size_t index = 0; index < cells_.size(); ++index)
        cells_[index].sequence.store(index, std::memory_order_relaxed);
}

bool ControllerIsolationReaderIngress::Open(
    const RoutingAuthority& authority,
    const std::uint64_t deviceEnrollmentToken) noexcept {
    if (!authority.valid() || deviceEnrollmentToken == 0 ||
        fault_.load(std::memory_order_acquire) != ControllerReaderFault::Closed) {
        return false;
    }
    authority_ = authority;
    deviceEnrollmentToken_ = deviceEnrollmentToken;
    enqueuePosition_.store(0, std::memory_order_relaxed);
    dequeuePosition_ = 0;
    for (std::size_t index = 0; index < cells_.size(); ++index)
        cells_[index].sequence.store(index, std::memory_order_relaxed);
    fault_.store(ControllerReaderFault::None, std::memory_order_release);
    return true;
}

void ControllerIsolationReaderIngress::Close() noexcept {
    fault_.store(ControllerReaderFault::Closed, std::memory_order_release);
    authority_ = {};
    deviceEnrollmentToken_ = 0;
}

bool ControllerIsolationReaderIngress::Publish(
    const ControllerReaderEventKind kind,
    const std::uint64_t sourceTimestampMicroseconds,
    const std::uint64_t observedAtMilliseconds,
    const GamepadState& state,
    const bool connected) noexcept {
    if (fault_.load(std::memory_order_acquire) != ControllerReaderFault::None)
        return false;
    if (sourceTimestampMicroseconds == 0 ||
        (kind == ControllerReaderEventKind::Reading && !connected) ||
        (kind == ControllerReaderEventKind::Disconnected && connected)) {
        Fail(ControllerReaderFault::InvalidEvent);
        return false;
    }

    std::size_t position = enqueuePosition_.load(std::memory_order_relaxed);
    for (std::size_t attempt = 0;
         attempt < ControllerReaderCapacity; ++attempt) {
        if (position == std::numeric_limits<std::size_t>::max()) {
            Fail(ControllerReaderFault::OrdinalOverflow);
            return false;
        }
        Cell& cell = cells_[position % cells_.size()];
        const auto sequence = cell.sequence.load(std::memory_order_acquire);
        const auto difference = static_cast<std::intptr_t>(sequence) -
            static_cast<std::intptr_t>(position);
        if (difference == 0) {
            if (!enqueuePosition_.compare_exchange_weak(
                    position, position + 1, std::memory_order_relaxed)) {
                continue;
            }
            cell.event = {
                kind,
                authority_,
                deviceEnrollmentToken_,
                static_cast<std::uint64_t>(position + 1),
                sourceTimestampMicroseconds,
                observedAtMilliseconds,
                state,
                connected,
            };
            cell.sequence.store(position + 1, std::memory_order_release);
            return true;
        }
        if (difference < 0) {
            Fail(ControllerReaderFault::QueueFull);
            return false;
        }
        position = enqueuePosition_.load(std::memory_order_relaxed);
    }
    Fail(ControllerReaderFault::ProducerContention);
    return false;
}

ControllerReaderPopResult ControllerIsolationReaderIngress::TryPop(
    ControllerReaderEvent& event) noexcept {
    const auto fault = fault_.load(std::memory_order_acquire);
    if (fault != ControllerReaderFault::None)
        return ControllerReaderPopResult::Faulted;
    Cell& cell = cells_[dequeuePosition_ % cells_.size()];
    const auto sequence = cell.sequence.load(std::memory_order_acquire);
    const auto expected = dequeuePosition_ + 1;
    const auto difference = static_cast<std::intptr_t>(sequence) -
        static_cast<std::intptr_t>(expected);
    if (difference < 0) {
        return enqueuePosition_.load(std::memory_order_acquire) ==
                dequeuePosition_
            ? ControllerReaderPopResult::Empty
            : ControllerReaderPopResult::ProducerPending;
    }
    if (difference != 0) return ControllerReaderPopResult::ProducerPending;
    event = cell.event;
    cell.sequence.store(
        dequeuePosition_ + cells_.size(), std::memory_order_release);
    ++dequeuePosition_;
    return ControllerReaderPopResult::Event;
}

std::size_t ControllerIsolationReaderIngress::approximateSize() const noexcept {
    const auto enqueued = enqueuePosition_.load(std::memory_order_acquire);
    return enqueued >= dequeuePosition_ ? enqueued - dequeuePosition_ : 0;
}

void ControllerIsolationReaderIngress::Fail(
    const ControllerReaderFault fault) noexcept {
    auto expected = ControllerReaderFault::None;
    (void)fault_.compare_exchange_strong(
        expected, fault, std::memory_order_release, std::memory_order_relaxed);
}

} // namespace widgetrail::isolation
