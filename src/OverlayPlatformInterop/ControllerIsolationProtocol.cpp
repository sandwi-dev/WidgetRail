#include "ControllerIsolationProtocol.h"

#include <algorithm>
#include <limits>

namespace widgetrail::isolation {
namespace {

[[nodiscard]] bool ValidInputBatch(
    const ControllerInputBatch& batch) noexcept {
    if (batch.count > ControllerIsolationInputBatchCapacity ||
        batch.reserved != 0)
        return false;
    if (batch.count == 0)
        return batch.firstIngressOrdinal == 0 &&
            batch.lastIngressOrdinal == 0;
    return batch.interactionGeneration != 0 &&
        batch.firstIngressOrdinal != 0 &&
        batch.lastIngressOrdinal >= batch.firstIngressOrdinal;
}

#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
[[nodiscard]] bool TestingMessageKind(const ControlMessageKind kind) noexcept {
    switch (kind) {
    case ControlMessageKind::TestExit:
    case ControlMessageKind::TestHang:
    case ControlMessageKind::TestWrongResponseKind:
    case ControlMessageKind::TestUnknownResponseKind:
    case ControlMessageKind::TestNonzeroResponseStatus:
    case ControlMessageKind::TestMalformedResponse:
        return true;
    default:
        return false;
    }
}
#endif

[[nodiscard]] bool ResponseMessageKind(
    const ControlMessageKind kind) noexcept {
    return kind == ControlMessageKind::HelloAccepted ||
        kind == ControlMessageKind::Heartbeat ||
        kind == ControlMessageKind::PrepareSession ||
        kind == ControlMessageKind::CommitPlaying ||
        kind == ControlMessageKind::EnterOverlay ||
        kind == ControlMessageKind::CloseOverlay ||
        kind == ControlMessageKind::QueryStatus ||
        kind == ControlMessageKind::Recover ||
        kind == ControlMessageKind::HoldContained ||
        kind == ControlMessageKind::Terminal;
}

[[nodiscard]] ControlMessageKind ExpectedResponseKind(
    const ControlMessageKind requestKind) noexcept {
    switch (requestKind) {
    case ControlMessageKind::Hello:
        return ControlMessageKind::HelloAccepted;
    case ControlMessageKind::Heartbeat:
        return ControlMessageKind::Heartbeat;
    case ControlMessageKind::PrepareSession:
    case ControlMessageKind::CommitPlaying:
    case ControlMessageKind::EnterOverlay:
    case ControlMessageKind::CloseOverlay:
    case ControlMessageKind::QueryStatus:
    case ControlMessageKind::Recover:
    case ControlMessageKind::HoldContained:
        return requestKind;
    case ControlMessageKind::Stop:
        return ControlMessageKind::Terminal;
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    case ControlMessageKind::TestExit:
    case ControlMessageKind::TestHang:
    case ControlMessageKind::TestWrongResponseKind:
    case ControlMessageKind::TestUnknownResponseKind:
    case ControlMessageKind::TestNonzeroResponseStatus:
    case ControlMessageKind::TestMalformedResponse:
        return ControlMessageKind::Terminal;
#endif
    default:
        return ControlMessageKind{};
    }
}

} // namespace

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
    case ControlMessageKind::PrepareSession:
    case ControlMessageKind::CommitPlaying:
    case ControlMessageKind::QueryStatus:
    case ControlMessageKind::Recover:
    case ControlMessageKind::HoldContained:
        return true;
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    case ControlMessageKind::TestExit:
    case ControlMessageKind::TestHang:
    case ControlMessageKind::TestWrongResponseKind:
    case ControlMessageKind::TestUnknownResponseKind:
    case ControlMessageKind::TestNonzeroResponseStatus:
    case ControlMessageKind::TestMalformedResponse:
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
        if (!TestingMessageKind(frame.kind))
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

ControlResponseValidation ValidateControlResponse(
    const ControlMessageKind requestKind,
    const ControlFrame& response,
    const ControllerIsolationNonce& nonce,
    const RoutingAuthority& authority,
    const std::uint64_t sequence) noexcept {
    if (response.magic != ControllerIsolationProtocolMagic ||
        response.version != ControllerIsolationProtocolVersion ||
        response.size != sizeof(ControlFrame) || response.reserved != 0) {
        return ControlResponseValidation::InvalidShape;
    }
    if (response.kind == ControlMessageKind::Heartbeat &&
        !ValidInputBatch(response.inputBatch))
        return ControlResponseValidation::InvalidShape;
    if (!ResponseMessageKind(response.kind))
        return ControlResponseValidation::InvalidKind;
    if (!SameNonce(response.nonce, nonce))
        return ControlResponseValidation::WrongNonce;
    if (response.authority != authority)
        return ControlResponseValidation::WrongAuthority;
    if (response.sequence != sequence)
        return ControlResponseValidation::WrongSequence;
    if (response.status != 0)
        return ControlResponseValidation::RemoteFailure;
    const auto expectedKind = ExpectedResponseKind(requestKind);
    if (expectedKind == ControlMessageKind{} ||
        response.kind != expectedKind) {
        return ControlResponseValidation::WrongResponseKind;
    }
    return ControlResponseValidation::Accepted;
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
