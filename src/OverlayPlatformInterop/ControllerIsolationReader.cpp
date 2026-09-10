#include "ControllerIsolationReader.h"

#include <algorithm>
#include <cwctype>
#include <limits>
#include <type_traits>
#if defined(WRAIL_CONTROLLER_ISOLATION_READER_TESTING)
#include <thread>
#endif

namespace widgetrail::isolation {
namespace {

template <std::size_t Size>
[[nodiscard]] bool HasBytes(
    const std::array<std::uint8_t, Size>& value) noexcept {
    return std::ranges::any_of(value, [](const std::uint8_t part) {
        return part != 0;
    });
}

[[nodiscard]] bool ContainsVirtualMarker(
    const std::wstring_view value) noexcept {
    constexpr std::array markers{
        std::wstring_view{L"VIGEMBUS"},
        std::wstring_view{L"NEFARIUS\\VIGEM"}};
    for (const auto marker : markers) {
        if (value.size() < marker.size()) continue;
        for (std::size_t start = 0;
             start + marker.size() <= value.size(); ++start) {
            bool matches = true;
            for (std::size_t index = 0; index < marker.size(); ++index) {
                if (std::towupper(value[start + index]) != marker[index]) {
                    matches = false;
                    break;
                }
            }
            if (matches) return true;
        }
    }
    return false;
}

} // namespace

bool ControllerDeviceNodeIdentity::valid() const noexcept {
    if (length == 0 || length >= value.size() || value[length] != L'\0')
        return false;
    return std::find(value.begin(), value.begin() + length, L'\0') ==
        value.begin() + length;
}

ControllerDeviceAncestry ClassifyControllerDeviceAncestry(
    const std::wstring_view normalizedInterfacePath,
    ControllerDeviceAncestryBackend& backend) noexcept {
    if (normalizedInterfacePath.empty())
        return ControllerDeviceAncestry::Unknown;
    if (ContainsVirtualMarker(normalizedInterfacePath))
        return ControllerDeviceAncestry::KnownVirtualOutput;

    ControllerDeviceNodeIdentity instanceId;
    ControllerDeviceNodeToken node{};
    ControllerDeviceNodeToken root{};
    if (!backend.ResolveInterfaceInstanceId(normalizedInterfacePath, instanceId) ||
        !instanceId.valid() || !backend.LocateNode(instanceId, node) ||
        !backend.LocateRoot(root)) {
        return ControllerDeviceAncestry::Unknown;
    }

    std::array<ControllerDeviceNodeToken, 32> visited{};
    std::size_t visitedCount{};
    for (; visitedCount < visited.size(); ++visitedCount) {
        if (std::find(
                visited.begin(), visited.begin() + visitedCount, node) !=
            visited.begin() + visitedCount) {
            return ControllerDeviceAncestry::Unknown;
        }
        visited[visitedCount] = node;
        ControllerDeviceNodeIdentity identity;
        if (!backend.ReadNodeIdentity(node, identity) || !identity.valid())
            return ControllerDeviceAncestry::Unknown;
        if (ContainsVirtualMarker(identity.view()))
            return ControllerDeviceAncestry::KnownVirtualOutput;
        if (node == root) return ControllerDeviceAncestry::Physical;
        ControllerDeviceNodeToken parent{};
        if (!backend.Parent(node, parent))
            return ControllerDeviceAncestry::Unknown;
        node = parent;
    }
    return ControllerDeviceAncestry::Unknown;
}

bool SelectedControllerEnrollment::valid() const noexcept {
    return enrollmentToken != 0 && HasBytes(deviceId) &&
        HasBytes(deviceRootId) && HasBytes(containerId) &&
        HasBytes(normalizedPnpPathDigest) && vendorId != 0 && productId != 0 &&
        deviceFamily > 0 && connected && gamepadSupported &&
        !knownVirtualOutput;
}

void SelectedControllerDiscovery::Observe(
    const SelectedControllerCandidateKind kind,
    const SelectedControllerDescriptor& descriptor) noexcept {
    if (kind == SelectedControllerCandidateKind::KnownVirtualOutput) return;
    if (kind == SelectedControllerCandidateKind::Unknown ||
        !descriptor.valid()) {
        unknownIdentity_ = !chooseStableFirst_;
        return;
    }
    if (physicalCount_ != 0 && selected_ == descriptor) return;
    if (physicalCount_ == 0 || (chooseStableFirst_ &&
        descriptor.deviceInstanceId.view() < selected_.deviceInstanceId.view())) selected_ = descriptor;
    if (physicalCount_ < 2) ++physicalCount_;
}

SelectedControllerDiscoveryStatus SelectedControllerDiscovery::Resolve(
    SelectedControllerDescriptor& descriptor) const noexcept {
    descriptor = {};
    if (unknownIdentity_)
        return SelectedControllerDiscoveryStatus::UnknownIdentity;
    if (physicalCount_ == 0)
        return SelectedControllerDiscoveryStatus::Unavailable;
    if (physicalCount_ != 1 && !chooseStableFirst_)
        return SelectedControllerDiscoveryStatus::Ambiguous;
    descriptor = selected_;
    return SelectedControllerDiscoveryStatus::Ready;
}

bool SameStableControllerIdentity(
    const SelectedControllerEnrollment& enrolled,
    const SelectedControllerEnrollment& local) noexcept {
    return enrolled.valid() && local.valid() &&
        enrolled.enrollmentToken == local.enrollmentToken &&
        enrolled.containerId == local.containerId &&
        enrolled.normalizedPnpPathDigest == local.normalizedPnpPathDigest &&
        enrolled.vendorId == local.vendorId &&
        enrolled.productId == local.productId &&
        enrolled.deviceFamily == local.deviceFamily &&
        enrolled.connected == local.connected &&
        enrolled.gamepadSupported == local.gamepadSupported &&
        !enrolled.knownVirtualOutput && !local.knownVirtualOutput;
}

LocalControllerResolutionStatus ResolveLocalController(
    const SelectedControllerEnrollment& enrolled,
    const SelectedControllerDiscoveryStatus discoveryStatus,
    const SelectedControllerDescriptor& localDescriptor,
    SelectedControllerEnrollment& localEnrollment) noexcept {
    localEnrollment = {};
    switch (discoveryStatus) {
    case SelectedControllerDiscoveryStatus::Unavailable:
        return LocalControllerResolutionStatus::Unavailable;
    case SelectedControllerDiscoveryStatus::Ambiguous:
        return LocalControllerResolutionStatus::Ambiguous;
    case SelectedControllerDiscoveryStatus::UnknownIdentity:
        return LocalControllerResolutionStatus::UnknownIdentity;
    case SelectedControllerDiscoveryStatus::Ready:
        break;
    }
    if (!localDescriptor.valid() ||
        !SameStableControllerIdentity(enrolled, localDescriptor.enrollment))
        return LocalControllerResolutionStatus::StableIdentityMismatch;
    localEnrollment = localDescriptor.enrollment;
    return LocalControllerResolutionStatus::Ready;
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
    const auto authority = authority_;
    const auto deviceEnrollmentToken = deviceEnrollmentToken_;

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
#if defined(WRAIL_CONTROLLER_ISOLATION_READER_TESTING)
            if (pauseNextProducerForTest_.exchange(
                    false, std::memory_order_acq_rel)) {
                producerPausedForTest_.store(true, std::memory_order_release);
                while (!releaseProducerForTest_.load(
                    std::memory_order_acquire)) {
                    std::this_thread::yield();
                }
                producerPausedForTest_.store(false, std::memory_order_release);
            }
#endif
            cell.event = {
                kind,
                authority,
                deviceEnrollmentToken,
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

std::uint64_t
ControllerIsolationReaderIngress::reservedThroughOrdinal() const noexcept {
    static_assert(sizeof(std::size_t) <= sizeof(std::uint64_t));
    return static_cast<std::uint64_t>(
        enqueuePosition_.load(std::memory_order_acquire));
}

#if defined(WRAIL_CONTROLLER_ISOLATION_READER_TESTING)
void ControllerIsolationReaderIngress::PauseNextProducerForTest() noexcept {
    releaseProducerForTest_.store(false, std::memory_order_relaxed);
    pauseNextProducerForTest_.store(true, std::memory_order_release);
}

bool ControllerIsolationReaderIngress::ProducerPausedForTest() const noexcept {
    return producerPausedForTest_.load(std::memory_order_acquire);
}

void ControllerIsolationReaderIngress::ReleaseProducerForTest() noexcept {
    releaseProducerForTest_.store(true, std::memory_order_release);
}
#endif

void ControllerIsolationReaderIngress::Fail(
    const ControllerReaderFault fault) noexcept {
    auto expected = ControllerReaderFault::None;
    (void)fault_.compare_exchange_strong(
        expected, fault, std::memory_order_release, std::memory_order_relaxed);
}

} // namespace widgetrail::isolation
