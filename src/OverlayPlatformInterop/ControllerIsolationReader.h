#pragma once

#include "ControllerIsolationCore.h"

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <type_traits>

namespace widgetrail::isolation {

inline constexpr std::size_t ControllerReaderCapacity = 256;

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

class SelectedControllerSource {
public:
    virtual ~SelectedControllerSource() = default;
    [[nodiscard]] virtual SelectedControllerPrepareStatus Prepare(
        const SelectedControllerEnrollment& enrollment,
        ControllerIsolationReaderIngress& ingress) noexcept = 0;
    [[nodiscard]] virtual bool SampleCurrent(
        SelectedControllerCurrent& current) noexcept = 0;
    virtual void Stop() noexcept = 0;
};

#if defined(WRAIL_GAMEINPUT_ISOLATION_READER)
[[nodiscard]] std::unique_ptr<SelectedControllerSource>
CreateGameInputSelectedControllerReader() noexcept;
#endif

} // namespace widgetrail::isolation
