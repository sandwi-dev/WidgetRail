#pragma once

#include "ControllerIsolationCore.h"

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string_view>
#include <type_traits>

namespace widgetrail::isolation {

inline constexpr std::size_t ControllerReaderCapacity = 256;
inline constexpr std::size_t ControllerDeviceIdentityCharacterCapacity = 512;

struct ControllerDeviceNodeIdentity final {
    std::array<wchar_t, ControllerDeviceIdentityCharacterCapacity> value{};
    std::size_t length{};

    [[nodiscard]] bool valid() const noexcept;
    [[nodiscard]] std::wstring_view view() const noexcept {
        return {value.data(), length};
    }
    [[nodiscard]] friend bool operator==(
        const ControllerDeviceNodeIdentity&,
        const ControllerDeviceNodeIdentity&) noexcept = default;
};

using ControllerDeviceNodeToken = std::uintptr_t;

class ControllerDeviceAncestryBackend {
public:
    virtual ~ControllerDeviceAncestryBackend() = default;
    [[nodiscard]] virtual bool ResolveInterfaceInstanceId(
        std::wstring_view interfacePath,
        ControllerDeviceNodeIdentity& identity) noexcept = 0;
    [[nodiscard]] virtual bool LocateNode(
        const ControllerDeviceNodeIdentity& identity,
        ControllerDeviceNodeToken& node) noexcept = 0;
    [[nodiscard]] virtual bool LocateRoot(
        ControllerDeviceNodeToken& root) noexcept = 0;
    [[nodiscard]] virtual bool ReadNodeIdentity(
        ControllerDeviceNodeToken node,
        ControllerDeviceNodeIdentity& identity) noexcept = 0;
    [[nodiscard]] virtual bool Parent(
        ControllerDeviceNodeToken node,
        ControllerDeviceNodeToken& parent) noexcept = 0;
};

enum class ControllerDeviceAncestry : std::uint8_t {
    Physical,
    KnownVirtualOutput,
    Unknown,
};

[[nodiscard]] ControllerDeviceAncestry ClassifyControllerDeviceAncestry(
    std::wstring_view normalizedInterfacePath,
    ControllerDeviceAncestryBackend& backend) noexcept;

struct SelectedControllerEnrollment final {
    std::uint64_t enrollmentToken{};
    std::array<std::uint8_t, 32> deviceId{};
    std::array<std::uint8_t, 32> deviceRootId{};
    std::array<std::uint8_t, 16> containerId{};
    std::array<std::uint8_t, 32> normalizedPnpPathDigest{};
    std::uint16_t vendorId{};
    std::uint16_t productId{};
    std::int8_t deviceFamily{};
    bool connected{};
    bool gamepadSupported{};
    bool knownVirtualOutput{};

    [[nodiscard]] bool valid() const noexcept;
    [[nodiscard]] friend bool operator==(
        const SelectedControllerEnrollment&,
        const SelectedControllerEnrollment&) noexcept = default;
};

static_assert(sizeof(SelectedControllerEnrollment) == 128);
static_assert(std::is_trivially_copyable_v<SelectedControllerEnrollment>);

enum class ControllerReaderEventKind : std::uint8_t {
    Reading,
    GuidePressed,
    GuideReleased,
    Disconnected,
};

struct ControllerReaderEvent final {
    ControllerReaderEventKind kind{ControllerReaderEventKind::Reading};
    RoutingAuthority authority{};
    std::uint64_t deviceEnrollmentToken{};
    std::uint64_t ingressOrdinal{};
    std::uint64_t sourceTimestampMicroseconds{};
    std::uint64_t observedAtMilliseconds{};
    GamepadState state{};
    bool connected{};
};

static_assert(std::is_trivially_copyable_v<ControllerReaderEvent>);

enum class ControllerReaderFault : std::uint8_t {
    None,
    Closed,
    InvalidEvent,
    QueueFull,
    ProducerContention,
    OrdinalOverflow,
};

enum class ControllerReaderPopResult : std::uint8_t {
    Event,
    Empty,
    ProducerPending,
    Faulted,
};

// Fixed-capacity bounded MPSC ingress with one serialized consumer. Producers
// reserve an exact ticket using the per-cell sequence algorithm and never
// allocate, log, wait on UI work, or call an output backend.
class ControllerIsolationReaderIngress final {
public:
    ControllerIsolationReaderIngress() noexcept;

    [[nodiscard]] bool Open(
        const RoutingAuthority& authority,
        std::uint64_t deviceEnrollmentToken) noexcept;
    void Close() noexcept;
    [[nodiscard]] bool Publish(
        ControllerReaderEventKind kind,
        std::uint64_t sourceTimestampMicroseconds,
        std::uint64_t observedAtMilliseconds,
        const GamepadState& state = {},
        bool connected = true) noexcept;
    [[nodiscard]] ControllerReaderPopResult TryPop(
        ControllerReaderEvent& event) noexcept;

    [[nodiscard]] ControllerReaderFault fault() const noexcept {
        return fault_.load(std::memory_order_acquire);
    }
    [[nodiscard]] std::size_t approximateSize() const noexcept;
    [[nodiscard]] std::uint64_t reservedThroughOrdinal() const noexcept;

#if defined(WRAIL_CONTROLLER_ISOLATION_READER_TESTING)
    void PauseNextProducerForTest() noexcept;
    [[nodiscard]] bool ProducerPausedForTest() const noexcept;
    void ReleaseProducerForTest() noexcept;
#endif

private:
    struct Cell final {
        std::atomic<std::size_t> sequence{};
        ControllerReaderEvent event{};
    };

    void Fail(ControllerReaderFault fault) noexcept;

    std::array<Cell, ControllerReaderCapacity> cells_{};
    std::atomic<std::size_t> enqueuePosition_{};
    std::size_t dequeuePosition_{};
    std::atomic<ControllerReaderFault> fault_{ControllerReaderFault::Closed};
    RoutingAuthority authority_{};
    std::uint64_t deviceEnrollmentToken_{};
#if defined(WRAIL_CONTROLLER_ISOLATION_READER_TESTING)
    std::atomic_bool pauseNextProducerForTest_{};
    std::atomic_bool producerPausedForTest_{};
    std::atomic_bool releaseProducerForTest_{};
#endif
};

enum class SelectedControllerPrepareStatus : std::uint8_t {
    Ready,
    InvalidEnrollment,
    Unavailable,
    IdentityMismatch,
    VirtualOutputRejected,
    CurrentReadingUnavailable,
    CallbackRegistrationFailed,
};

struct SelectedControllerCurrent final {
    std::uint64_t sourceTimestampMicroseconds{};
    std::uint64_t observedAtMilliseconds{};
    GamepadState state{};
    bool connected{};
};

struct ControllerRumbleState final {
    float lowFrequency{};
    float highFrequency{};
    float leftTrigger{};
    float rightTrigger{};

    [[nodiscard]] friend bool operator==(
        const ControllerRumbleState&,
        const ControllerRumbleState&) noexcept = default;
};

struct SelectedControllerDescriptor final {
    SelectedControllerEnrollment enrollment{};
    ControllerDeviceNodeIdentity deviceInstanceId{};

    [[nodiscard]] bool valid() const noexcept {
        return enrollment.valid() && deviceInstanceId.valid();
    }
    [[nodiscard]] friend bool operator==(
        const SelectedControllerDescriptor&,
        const SelectedControllerDescriptor&) noexcept = default;
};

enum class SelectedControllerCandidateKind : std::uint8_t {
    Physical,
    KnownVirtualOutput,
    Unknown,
};

enum class SelectedControllerDiscoveryStatus : std::uint8_t {
    Ready,
    Unavailable,
    Ambiguous,
    UnknownIdentity,
};

enum class LocalControllerResolutionStatus : std::uint8_t {
    Ready,
    Unavailable,
    Ambiguous,
    UnknownIdentity,
    StableIdentityMismatch,
};

[[nodiscard]] bool SameStableControllerIdentity(
    const SelectedControllerEnrollment& enrolled,
    const SelectedControllerEnrollment& local) noexcept;
[[nodiscard]] LocalControllerResolutionStatus ResolveLocalController(
    const SelectedControllerEnrollment& enrolled,
    SelectedControllerDiscoveryStatus discoveryStatus,
    const SelectedControllerDescriptor& localDescriptor,
    SelectedControllerEnrollment& localEnrollment) noexcept;

// Bounded selection owner for one blocking GameInput enumeration. It retains
// at most one exact physical descriptor and saturates at ambiguity; known
// virtual outputs never compete with physical input.
class SelectedControllerDiscovery final {
public:
    void Observe(
        SelectedControllerCandidateKind kind,
        const SelectedControllerDescriptor& descriptor = {}) noexcept;
    [[nodiscard]] SelectedControllerDiscoveryStatus Resolve(
        SelectedControllerDescriptor& descriptor) const noexcept;

private:
    SelectedControllerDescriptor selected_{};
    std::uint8_t physicalCount_{};
    bool unknownIdentity_{};
};

class SelectedControllerSource {
public:
    virtual ~SelectedControllerSource() = default;
    [[nodiscard]] virtual SelectedControllerPrepareStatus Prepare(
        const SelectedControllerEnrollment& enrollment,
        ControllerIsolationReaderIngress& ingress,
        SelectedControllerEnrollment& preparedEnrollment) noexcept = 0;
    [[nodiscard]] virtual bool SampleCurrent(
        SelectedControllerCurrent& current) noexcept = 0;
    [[nodiscard]] virtual bool ApplyRumble(
        const ControllerRumbleState&) noexcept { return false; }
    virtual void Stop() noexcept = 0;
};

#if defined(WRAIL_GAMEINPUT_ISOLATION_READER)
[[nodiscard]] std::unique_ptr<SelectedControllerSource>
CreateGameInputSelectedControllerReader() noexcept;
[[nodiscard]] SelectedControllerDiscoveryStatus
DiscoverCurrentPhysicalController(
    std::uint64_t enrollmentToken,
    SelectedControllerDescriptor& descriptor) noexcept;
#endif

} // namespace widgetrail::isolation
