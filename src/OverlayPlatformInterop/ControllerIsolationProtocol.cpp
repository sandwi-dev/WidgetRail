#include "ControllerIsolationProtocol.h"

#include <limits>

namespace widgetrail::isolation {

bool ValidNonce(const ControllerIsolationNonce& nonce) noexcept {
    std::uint8_t combined{};
    for (const auto value : nonce) combined |= value;
    return combined != 0;
}

bool SameNonce(
    const ControllerIsolationNonce& left,
    const ControllerIsolationNonce& right) noexcept {
    std::uint8_t difference{};
    for (std::size_t index = 0; index < left.size(); ++index)
        difference |= static_cast<std::uint8_t>(left[index] ^ right[index]);
    return difference == 0;
}

bool ProductionMessageKind(const ControlMessageKind kind) noexcept {
    switch (kind) {
    case ControlMessageKind::Hello:
    case ControlMessageKind::HelloAccepted:
    case ControlMessageKind::Heartbeat:
    case ControlMessageKind::Reading:
    case ControlMessageKind::EnterOverlay:
    case ControlMessageKind::CloseOverlay:
    case ControlMessageKind::Stop:
    case ControlMessageKind::Terminal:
        return true;
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    case ControlMessageKind::TestExit:
    case ControlMessageKind::TestHang:
        return false;
#endif
    }
    return false;
}

ControlSessionGate::ControlSessionGate(
    ControllerIsolationNonce nonce,
    RoutingAuthority authority,
    const std::uint64_t firstSequence) noexcept
    : nonce_(nonce), authority_(authority), nextSequence_(firstSequence) {}

ControlFrameValidation ControlSessionGate::Admit(
    const ControlFrame& frame) noexcept {
    if (frame.magic != ControllerIsolationProtocolMagic ||
        frame.version != ControllerIsolationProtocolVersion ||
        frame.size != sizeof(ControlFrame) || frame.reserved != 0) {
        return ControlFrameValidation::InvalidShape;
    }
    if (!ProductionMessageKind(frame.kind)) {
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
        if (frame.kind != ControlMessageKind::TestExit &&
            frame.kind != ControlMessageKind::TestHang)
#endif
            return ControlFrameValidation::InvalidKind;
    }
    if (!SameNonce(frame.nonce, nonce_))
        return ControlFrameValidation::WrongNonce;
    if (frame.authority != authority_)
        return ControlFrameValidation::WrongAuthority;
    if (nextSequence_ == 0 || frame.sequence != nextSequence_)
        return ControlFrameValidation::WrongSequence;
    if (nextSequence_ == std::numeric_limits<std::uint64_t>::max())
        nextSequence_ = 0;
    else
        ++nextSequence_;
    return ControlFrameValidation::Accepted;
}

ControlFrame MakeControlFrame(
    const ControlMessageKind kind,
    const ControllerIsolationNonce& nonce,
    const RoutingAuthority& authority,
    const std::uint64_t sequence) noexcept {
    ControlFrame result;
    result.size = static_cast<std::uint16_t>(sizeof(ControlFrame));
    result.kind = kind;
    result.nonce = nonce;
    result.authority = authority;
    result.sequence = sequence;
    return result;
}

} // namespace widgetrail::isolation
