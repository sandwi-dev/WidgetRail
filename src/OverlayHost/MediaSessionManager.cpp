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
        return &found->second;
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
    if (!session || !session->authority ||
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

HRESULT MediaSessionManager::Reconcile(
    const SessionKey& key,
    const TransitionInput& input,
    const TransitionOperations& operations) {
    auto* current = Find(key);
    if (!current) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    auto plan = PlanTransition(*current, input);
    if (plan.effect == TransitionEffect::None) {
        if (input.desiredGeometry) {
            current->committedGeometry = input.desiredGeometry;
            if (current->authority)
                current->authority->pinnedFrameGeneration =
                    input.requestedPresentation ==
                            PresentationState::CompactPinned
                        ? input.desiredGeometry->committedFrameGeneration
                        : 0;
        }
        return S_FALSE;
    }
    if (plan.effect == TransitionEffect::UpdateParkingReason) {
        if (!current->authority || !plan.parkingReason) return E_UNEXPECTED;
        current->authority->parkingReason = plan.parkingReason;
        return S_OK;
    }

    if (plan.effect == TransitionEffect::Retire) {
        if (!operations.retire) return E_UNEXPECTED;
        const HRESULT result = operations.retire(key, *current);
        if (FAILED(result)) return result;
        ClearEndpointOwnership(key);
        sessions_.erase(key.value());
        return S_OK;
    }

    if (plan.effect == TransitionEffect::Fault) {
        if (!operations.fault) return E_UNEXPECTED;
        const HRESULT result = operations.fault(key, *current);
        if (FAILED(result)) return result;
        ClearEndpointOwnership(key);
        sessions_.erase(key.value());
        return E_INVALIDARG;
    }

    if (plan.effect == TransitionEffect::Park) {
        if (!operations.park || !plan.parkingReason) return E_UNEXPECTED;
        const HRESULT result = operations.park(key, *current, *plan.parkingReason);
        if (FAILED(result)) {
            if (operations.fault) (void)operations.fault(key, *current);
            ClearEndpointOwnership(key);
            sessions_.erase(key.value());
            return result;
        }
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
    if (plan.effect == TransitionEffect::Update &&
        (!owner || *owner != key)) {
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
        if (FAILED(result)) {
            if (operations.fault) (void)operations.fault(key, *current);
            ClearEndpointOwnership(key);
            sessions_.erase(key.value());
            return result;
        }
        return S_OK;
    }
    if (!operations.present) return E_UNEXPECTED;
    if (owner && *owner != key) {
        auto* displaced = Find(*owner);
        if (!displaced || !operations.park) return E_UNEXPECTED;
        const SessionKey displacedKey = displaced->key;
        const auto displacementReason = *endpoint == Endpoint::Pinned
            ? ParkingReason::PinnedEndpointTaken
            : ParkingReason::EndpointUnavailable;
        const HRESULT parkResult = operations.park(
            displacedKey, *displaced, displacementReason);
        if (FAILED(parkResult)) {
            if (operations.fault)
                (void)operations.fault(displacedKey, *displaced);
            ClearEndpointOwnership(displacedKey);
            sessions_.erase(displacedKey.value());
            return parkResult;
        }
        if (displaced->authority) {
            displaced->authority->presentation = PresentationState::Parked;
            displaced->authority->parkingReason =
                displacementReason;
        }
        displaced->committedGeometry.reset();
        ClearEndpointOwnership(displacedKey);
    }
    // Presentation attachment invokes the endpoint-visibility callback
    // synchronously. Publish the exact desired owner and geometry first so
    // that callback can validate only the presentation being attached. Any
    // failed side effect faults and removes this exact session below.
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
    if (FAILED(result)) {
        if (operations.fault) (void)operations.fault(key, *current);
        ClearEndpointOwnership(key);
        sessions_.erase(key.value());
        return result;
    }
    return S_OK;
}

bool MediaSessionManager::EraseAfterTerminal(const SessionKey& key) {
    const auto found = sessions_.find(key.value());
    if (found == sessions_.end()) return false;
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
