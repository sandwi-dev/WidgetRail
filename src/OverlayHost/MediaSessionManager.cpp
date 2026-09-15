#include "MediaSessionManager.h"

#include <utility>

namespace widgetrail::media {

bool SameEndpointGeometry(
    const EndpointGeometry& left,
    const EndpointGeometry& right) noexcept {
    const auto sameRect = [](const RECT& a, const RECT& b) noexcept {
        return a.left == b.left && a.top == b.top &&
            a.right == b.right && a.bottom == b.bottom;
    };
    return left.ownerWindow == right.ownerWindow &&
        sameRect(left.bounds, right.bounds) &&
        sameRect(left.clip, right.clip) &&
        sameRect(left.controllerBounds, right.controllerBounds) &&
        left.rasterScale == right.rasterScale &&
        left.visible == right.visible;
}

std::wstring SessionKey::value() const {
    return widgetId + L"\n" + instanceId + L"\n" + runtimeGeneration +
        L"\n" + sessionId;
}

MediaSessionManager::MediaSessionManager(
    richmedia::RichMediaEnvironmentHandle environment)
    : environment_(std::move(environment)) {}

void MediaSessionManager::InvalidateCompositionTargets() noexcept {
    overlayOwner_.reset();
    pinnedOwner_.reset();
    for (auto& [key, session] : sessions_) {
        session.parkingTarget.Reset();
        session.committedGeometry.reset();
        session.clientBounds.reset();
        session.clientClip.reset();
        session.deferralStreak = 0;
        if (session.authority) {
            session.authority->presentation = PresentationState::Parked;
            session.authority->parkingReason = ParkingReason::EndpointUnavailable;
            session.authority->pinnedFrameGeneration = 0;
        }
    }
}

TransitionPlan MediaSessionManager::PlanTransition(
    const SessionRecord& current,
    const TransitionInput& input) noexcept {
    if (!input.declarationCurrent || !input.documentIdentityCurrent)
        return {TransitionEffect::Retire, PresentationState::Parked, std::nullopt};
    if (input.geometry == GeometryState::Invalid)
        return {TransitionEffect::Fault, PresentationState::Parked,
            input.parkingReason};
    if (!input.endpointRequested || !input.endpointAvailable ||
        input.geometry == GeometryState::Pending) {
        if (current.authority &&
            current.authority->presentation == PresentationState::Parked &&
            !current.committedGeometry) {
            return {
                current.authority->parkingReason == input.parkingReason
                    ? TransitionEffect::None
                    : TransitionEffect::UpdateParkingReason,
                PresentationState::Parked,
                input.parkingReason,
            };
        }
        return {TransitionEffect::Park, PresentationState::Parked,
            input.parkingReason};
    }
    if (!input.desiredGeometry)
        return {TransitionEffect::Fault, PresentationState::Parked,
            input.parkingReason};
    if (current.authority &&
        current.authority->presentation == input.requestedPresentation &&
        !current.authority->parkingReason) {
        if (current.committedGeometry && SameEndpointGeometry(
                *current.committedGeometry, *input.desiredGeometry))
            return {TransitionEffect::None, input.requestedPresentation, std::nullopt};
        return {TransitionEffect::Update, input.requestedPresentation, std::nullopt};
    }
    if (current.authority && !current.authority->parkingReason &&
        EndpointFor(current.authority->presentation) ==
            EndpointFor(input.requestedPresentation)) {
        return {TransitionEffect::Update, input.requestedPresentation, std::nullopt};
    }
    return {TransitionEffect::Present, input.requestedPresentation, std::nullopt};
}

SessionRecord* MediaSessionManager::Ensure(const SessionKey& key) {
    const auto value = key.value();
    if (const auto found = sessions_.find(value); found != sessions_.end())
        return found->second.retirementInProgress ? nullptr : &found->second;
    if (sessions_.size() >= MaximumResidentSessions) return nullptr;
    SessionRecord record;
    record.key = key;
    record.coordinator =
        std::make_shared<richmedia::RichMediaSurfaceCoordinator>(environment_);
    const auto [position, inserted] = sessions_.emplace(value, std::move(record));
    return inserted ? &position->second : nullptr;
}

SessionRecord* MediaSessionManager::Find(const SessionKey& key) noexcept {
    const auto found = sessions_.find(key.value());
    return found == sessions_.end() ? nullptr : &found->second;
}

const SessionRecord* MediaSessionManager::Find(
    const SessionKey& key) const noexcept {
    const auto found = sessions_.find(key.value());
    return found == sessions_.end() ? nullptr : &found->second;
}

std::vector<SessionKey> MediaSessionManager::Keys() const {
    std::vector<SessionKey> keys;
    keys.reserve(sessions_.size());
    for (const auto& [encoded, session] : sessions_) {
        (void)encoded;
        keys.push_back(session.key);
    }
    return keys;
}

std::vector<SessionKey> MediaSessionManager::KeysForWidget(
    const std::wstring_view widgetId) const {
    std::vector<SessionKey> keys;
    for (const auto& [encoded, session] : sessions_) {
        (void)encoded;
        if (session.key.widgetId == widgetId) keys.push_back(session.key);
    }
    return keys;
}

std::optional<Endpoint> MediaSessionManager::EndpointFor(
    const PresentationState presentation) noexcept {
    switch (presentation) {
    case PresentationState::OverlayViewport:
    case PresentationState::OverlayFullscreen:
        return Endpoint::Overlay;
    case PresentationState::CompactPinned:
        return Endpoint::Pinned;
    case PresentationState::Parked:
        return std::nullopt;
    }
    return std::nullopt;
}

bool MediaSessionManager::RequestPresentation(
    const SessionKey& key,
    const PresentationState target,
    const std::wstring_view presentationGeneration) {
    auto* session = Find(key);
    if (!session || session->retirementInProgress || !session->authority ||
        presentationGeneration.empty() ||
        session->authority->presentationGeneration != presentationGeneration)
        return false;
    session->presentationRequest = PresentationRequest{
        target, std::wstring{presentationGeneration}};
    return true;
}

void MediaSessionManager::ClearPresentationRequest(const SessionKey& key) {
    if (auto* session = Find(key)) session->presentationRequest.reset();
}

std::optional<SessionKey> MediaSessionManager::EndpointOwner(
    const Endpoint endpoint) const {
    return endpoint == Endpoint::Overlay ? overlayOwner_ : pinnedOwner_;
}

void MediaSessionManager::ClearEndpointOwnership(
    const SessionKey& key) noexcept {
    if (overlayOwner_ && *overlayOwner_ == key) overlayOwner_.reset();
    if (pinnedOwner_ && *pinnedOwner_ == key) pinnedOwner_.reset();
}

// A session presents on exactly one endpoint. The endpoint it is leaving must
// be released as it acquires the next one, otherwise the stale owner makes an
// unrelated session on that endpoint displace this live session.
void MediaSessionManager::ReleaseOtherEndpointOwnership(
    const SessionKey& key, const Endpoint retained) noexcept {
    if (retained != Endpoint::Overlay && overlayOwner_ && *overlayOwner_ == key)
        overlayOwner_.reset();
    if (retained != Endpoint::Pinned && pinnedOwner_ && *pinnedOwner_ == key)
        pinnedOwner_.reset();
}

bool MediaSessionManager::DeferralExhausted(SessionRecord& record) noexcept {
    const auto now = GetTickCount64();
    if (record.deferralStreak == 0) record.deferralStreakStartTick = now;
    ++record.deferralStreak;
    return record.deferralStreak >= MinimumDeferralStreakAttempts &&
        now - record.deferralStreakStartTick >=
            MaximumDeferralStreakMilliseconds;
}

HRESULT MediaSessionManager::Reconcile(
    const SessionKey& key,
    const TransitionInput& input,
    const TransitionOperations& operations) {
    auto* current = Find(key);
    if (!current) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    if (current->retirementInProgress) return E_PENDING;
    auto plan = PlanTransition(*current, input);
    if (plan.effect == TransitionEffect::None) {
        // A parked plan carries no endpoint. Adopting the caller resolved
        // geometry here would leave committed geometry on a parked session,
        // which defeats the parked no-op guard in PlanTransition and replans a
        // redundant Park on every following reconcile.
        if (input.desiredGeometry && plan.target != PresentationState::Parked) {
            current->committedGeometry = input.desiredGeometry;
            if (current->authority)
                current->authority->pinnedFrameGeneration =
                    input.requestedPresentation ==
                            PresentationState::CompactPinned
                        ? input.desiredGeometry->committedFrameGeneration
                        : 0;
        }
        current->deferralStreak = 0;
        return S_FALSE;
    }
    if (plan.effect == TransitionEffect::UpdateParkingReason) {
        if (!current->authority || !plan.parkingReason) return E_UNEXPECTED;
        current->authority->parkingReason = plan.parkingReason;
        return S_OK;
    }

    // Retiring this session releases the registry reference to its
    // coordinator. When the retirement is reached from inside the coordinator
    // fault callback below, that would destroy the coordinator while its own
    // member function is still on the stack, so hold the controller alive for
    // the whole side effect.
    const auto coordinatorLifetime = current->coordinator;

    // Every effect below runs a host side effect that can drive the media
    // controller into a synchronous fault. That fault reaches the host through
    // the coordinator invalidate callback, which retires this exact session and
    // erases its registry entry before the effect returns. Re-resolve the
    // record after each side effect and treat a vanished entry as the completed
    // terminal transition instead of dereferencing a freed record.
    const auto faultAndErase = [&](const SessionKey& target,
                                   const HRESULT failure) {
        if (auto* present = Find(target)) {
            if (present->retirementInProgress) return failure;
            present->retirementInProgress = true;
            const auto lifetime = present->coordinator;
            if (operations.fault) (void)operations.fault(target, *present);
        }
        ClearEndpointOwnership(target);
        sessions_.erase(target.value());
        return failure;
    };

    if (plan.effect == TransitionEffect::Retire ||
        plan.effect == TransitionEffect::Fault) {
        const auto& terminal = plan.effect == TransitionEffect::Retire
            ? operations.retire : operations.fault;
        if (!terminal) return E_UNEXPECTED;
        current->retirementInProgress = true;
        const HRESULT result = terminal(key, *current);
        if (FAILED(result)) {
            current->retirementInProgress = false;
            return result;
        }
        ClearEndpointOwnership(key);
        sessions_.erase(key.value());
        return plan.effect == TransitionEffect::Retire ? S_OK : E_INVALIDARG;
    }

    if (plan.effect == TransitionEffect::Park) {
        if (!operations.park || !plan.parkingReason) return E_UNEXPECTED;
        const HRESULT result = operations.park(key, *current, *plan.parkingReason);
        current = Find(key);
        if (!current) {
            ClearEndpointOwnership(key);
            return result;
        }
        // A park that cannot run yet leaves the session exactly as it was,
        // unless it has been failing to run for too long.
        if (result == E_PENDING) {
            return DeferralExhausted(*current)
                ? faultAndErase(key, HRESULT_FROM_WIN32(ERROR_TIMEOUT))
                : result;
        }
        if (FAILED(result)) return faultAndErase(key, result);
        current->deferralStreak = 0;
        ClearEndpointOwnership(key);
        if (current->authority) {
            current->authority->presentation = PresentationState::Parked;
            current->authority->parkingReason = plan.parkingReason;
        }
        current->committedGeometry.reset();
        return S_OK;
    }

    const auto endpoint = EndpointFor(plan.target);
    if (!endpoint || !input.desiredGeometry) return E_UNEXPECTED;
    auto& owner = *endpoint == Endpoint::Overlay ? overlayOwner_ : pinnedOwner_;
    const auto priorAuthority = current->authority;
    const auto priorGeometry = current->committedGeometry;
    const auto priorOwner = owner;
    if (plan.effect == TransitionEffect::Update && (!owner || *owner != key)) {
        plan.effect = TransitionEffect::Present;
    }
    if (plan.effect == TransitionEffect::Update) {
        if (!operations.update) return E_UNEXPECTED;
        // Visibility callbacks are synchronous. Publish the desired endpoint
        // state before invoking the update owner so a callback can only
        // observe the geometry and presentation being committed, never the
        // preceding bounds.
        current->committedGeometry = input.desiredGeometry;
        if (current->authority) {
            current->authority->presentation = plan.target;
            current->authority->parkingReason.reset();
            current->authority->pinnedFrameGeneration =
                plan.target == PresentationState::CompactPinned
                    ? input.desiredGeometry->committedFrameGeneration
                    : 0;
        }
        const HRESULT result = operations.update(
            key, *current, plan.target, *input.desiredGeometry);
        current = Find(key);
        if (!current) {
            ClearEndpointOwnership(key);
            return result;
        }
        // E_PENDING means the endpoint could not accept this presentation yet,
        // not that the session is invalid. Restore the pre-effect state and
        // leave the session resident so a later reconcile can retry.
        if (result == E_PENDING) {
            current->authority = priorAuthority;
            current->committedGeometry = priorGeometry;
            owner = priorOwner;
            return DeferralExhausted(*current)
                ? faultAndErase(key, HRESULT_FROM_WIN32(ERROR_TIMEOUT))
                : result;
        }
        if (FAILED(result)) return faultAndErase(key, result);
        current->deferralStreak = 0;
        return S_OK;
    }
    if (!operations.present) return E_UNEXPECTED;
    if (owner && *owner != key) {
        auto* displaced = Find(*owner);
        if (!displaced || !operations.park) return E_UNEXPECTED;
        if (displaced->retirementInProgress) return E_PENDING;
        const SessionKey displacedKey = displaced->key;
        const auto displacedLifetime = displaced->coordinator;
        const auto displacementReason = *endpoint == Endpoint::Pinned
            ? ParkingReason::PinnedEndpointTaken
            : ParkingReason::EndpointUnavailable;
        const HRESULT parkResult = operations.park(
            displacedKey, *displaced, displacementReason);
        displaced = Find(displacedKey);
        if (FAILED(parkResult) && displaced)
            return faultAndErase(displacedKey, parkResult);
        if (displaced) {
            if (displaced->authority) {
                displaced->authority->presentation = PresentationState::Parked;
                displaced->authority->parkingReason = displacementReason;
            }
            displaced->committedGeometry.reset();
        }
        ClearEndpointOwnership(displacedKey);
        if (FAILED(parkResult)) return parkResult;
        // Parking the displaced owner can retire this session through the same
        // synchronous fault path.
        current = Find(key);
        if (!current) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    }
    // Presentation attachment invokes the endpoint-visibility callback
    // synchronously. Publish the exact desired owner and geometry first so
    // that callback can validate only the presentation being attached. Any
    // failed side effect faults and removes this exact session below.
    ReleaseOtherEndpointOwnership(key, *endpoint);
    owner = key;
    current->committedGeometry = input.desiredGeometry;
    if (current->authority) {
        current->authority->presentation = plan.target;
        current->authority->parkingReason.reset();
        current->authority->pinnedFrameGeneration =
            plan.target == PresentationState::CompactPinned
                ? input.desiredGeometry->committedFrameGeneration
                : 0;
    }
    const HRESULT result = operations.present(
        key, *current, plan.target, *input.desiredGeometry);
    current = Find(key);
    if (!current) {
        ClearEndpointOwnership(key);
        return result;
    }
    // A deferred attachment keeps the session resident and unchanged so the
    // next reconcile can present it once the endpoint is ready.
    if (result == E_PENDING) {
        current->authority = priorAuthority;
        current->committedGeometry = priorGeometry;
        owner = priorOwner;
        return DeferralExhausted(*current)
            ? faultAndErase(key, HRESULT_FROM_WIN32(ERROR_TIMEOUT))
            : result;
    }
    if (FAILED(result)) return faultAndErase(key, result);
    current->deferralStreak = 0;
    return S_OK;
}

bool MediaSessionManager::EraseAfterTerminal(const SessionKey& key) {
    const auto found = sessions_.find(key.value());
    if (found == sessions_.end()) return false;
    if (found->second.retirementInProgress) return false;
    if ((overlayOwner_ && *overlayOwner_ == key) ||
        (pinnedOwner_ && *pinnedOwner_ == key)) return false;
    const auto& session = found->second;
    if (session.authority &&
        session.authority->presentation != PresentationState::Parked)
        return false;
    if (session.coordinator) {
        const auto lifecycle = session.coordinator->state().lifecycle;
        if (lifecycle != richmedia::Lifecycle::Absent &&
            lifecycle != richmedia::Lifecycle::Faulted &&
            lifecycle != richmedia::Lifecycle::Closing)
            return false;
    }
    sessions_.erase(found);
    return true;
}

} // namespace widgetrail::media
