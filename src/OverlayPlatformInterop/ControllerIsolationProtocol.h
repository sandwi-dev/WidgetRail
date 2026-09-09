#pragma once

#include "ControllerIsolationCore.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <type_traits>

namespace widgetrail::isolation {

inline constexpr std::uint32_t ControllerIsolationProtocolMagic = 0x57494349;
inline constexpr std::uint16_t ControllerIsolationProtocolVersion = 1;
inline constexpr std::size_t ControllerIsolationNonceBytes = 32;
inline constexpr std::size_t ControllerIsolationMaximumFrameBytes = 256;

using ControllerIsolationNonce =
    std::array<std::uint8_t, ControllerIsolationNonceBytes>;

enum class ControlMessageKind : std::uint16_t {
    Hello = 1,
    HelloAccepted = 2,
    Heartbeat = 3,
    Reading = 4,
    EnterOverlay = 5,
    CloseOverlay = 6,
    Stop = 7,
    Terminal = 8,
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    TestExit = 100,
    TestHang = 101,
#endif
};

struct ControlFrame final {
    std::uint32_t magic{ControllerIsolationProtocolMagic};
    std::uint16_t version{ControllerIsolationProtocolVersion};
    std::uint16_t size{};
    ControlMessageKind kind{ControlMessageKind::Hello};
    std::uint16_t reserved{};
    ControllerIsolationNonce nonce{};
    RoutingAuthority authority{};
    std::uint64_t sequence{};
    std::uint64_t deviceEnrollmentToken{};
    std::uint64_t observedAtMilliseconds{};
    GamepadState state{};
    std::uint32_t status{};
    std::uint32_t processId{};
};

static_assert(sizeof(ControlFrame) <= ControllerIsolationMaximumFrameBytes);
static_assert(std::is_trivially_copyable_v<ControlFrame>);

struct ProcessAdmission final {
    std::uint32_t parentProcessId{};
    std::uint64_t parentCreationTime{};
    RoutingAuthority authority{};
    ControllerIsolationNonce nonce{};
};

static_assert(std::is_trivially_copyable_v<ProcessAdmission>);

enum class ControlFrameValidation {
    Accepted,
    InvalidShape,
    InvalidKind,
    WrongNonce,
    WrongAuthority,
    WrongSequence,
};

[[nodiscard]] bool ValidNonce(
    const ControllerIsolationNonce& nonce) noexcept;
[[nodiscard]] bool SameNonce(
    const ControllerIsolationNonce& left,
    const ControllerIsolationNonce& right) noexcept;
[[nodiscard]] bool ProductionMessageKind(ControlMessageKind kind) noexcept;

class ControlSessionGate final {
public:
    ControlSessionGate(
        ControllerIsolationNonce nonce,
        RoutingAuthority authority,
        std::uint64_t firstSequence = 1) noexcept;

    [[nodiscard]] ControlFrameValidation Admit(
        const ControlFrame& frame) noexcept;
    [[nodiscard]] std::uint64_t nextSequence() const noexcept {
        return nextSequence_;
    }

private:
    ControllerIsolationNonce nonce_{};
    RoutingAuthority authority_{};
    std::uint64_t nextSequence_{};
};

[[nodiscard]] ControlFrame MakeControlFrame(
    ControlMessageKind kind,
    const ControllerIsolationNonce& nonce,
    const RoutingAuthority& authority,
    std::uint64_t sequence) noexcept;

} // namespace widgetrail::isolation
