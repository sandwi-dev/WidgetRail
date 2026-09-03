#pragma once

#include "RichMediaSurfaceCoordinator.h"
#include "EmbeddedMediaResourceContract.h"
#include "WidgetBridgeClient.h"

#include <Windows.h>
#include <wrl/client.h>

#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail::media {

inline constexpr std::size_t MaximumResidentSessions = 4;

// A deferred effect means "not yet", so it must be retried. It must not be
// retried forever: a condition that never clears would leave a session
// resident but permanently unpresented, which reads to a user as a frozen or
// empty surface with no error. A deferral streak that outlives both bounds
// below is escalated to a real failure so it fails loudly instead of silently.
inline constexpr std::uint64_t MaximumDeferralStreakMilliseconds = 10000;
inline constexpr std::uint32_t MinimumDeferralStreakAttempts = 8;

enum class PresentationState {
    Parked,
    OverlayViewport,
    OverlayFullscreen,
    CompactPinned,
};

enum class Endpoint {
    Overlay,
    Pinned,
};

enum class GeometryState {
    Ready,
    Pending,
    Invalid,
};

enum class ParkingReason {
    DeclaredWithoutViewport,
    HostHidden,
    WidgetCycled,
    EndpointUnavailable,
    PinnedEndpointTaken,
};

struct SessionKey final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring sessionId;

    [[nodiscard]] std::wstring value() const;
    [[nodiscard]] bool operator==(const SessionKey&) const noexcept = default;
};

struct CommandOriginAuthority final {
    long long snapshotSequence{};
    long long commandSequence{};
    std::wstring mediaKey;
    std::wstring sessionId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    richmedia::PlaybackCommandStage stage{
        richmedia::PlaybackCommandStage::Accepted};
};

struct EndpointGeometry final {
    HWND ownerWindow{};
    RECT bounds{};
    RECT clip{};
    RECT controllerBounds{};
    double rasterScale{1.0};
    bool visible{};
    std::uint64_t committedFrameGeneration{};
};

[[nodiscard]] bool SameEndpointGeometry(
    const EndpointGeometry& left,
    const EndpointGeometry& right) noexcept;

struct SessionAuthority final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring sessionId;
    long long sequence{};
    EmbeddedMediaDocumentIdentity documentIdentity;
    EmbeddedMediaSessionDeclaration declaration;
    std::vector<std::wstring> commands;
    long long lastDispatchedPlaybackCommand{};
    PresentationState presentation{PresentationState::Parked};
    std::optional<ParkingReason> parkingReason;
    bool projectionDeferralRecorded{};
    std::uint64_t pinnedFrameGeneration{};
    std::optional<CommandOriginAuthority> commandOrigin;
};

struct PresentationRequest final {
    PresentationState target{PresentationState::Parked};
    std::wstring presentationGeneration;
};

struct SessionRecord final {
    SessionKey key;
    // Consecutive deferrals for this session. Reset by any effect that
    // actually commits, including a no-op reconcile.
    std::uint32_t deferralStreak{};
    std::uint64_t deferralStreakStartTick{};
    std::shared_ptr<richmedia::RichMediaSurfaceCoordinator> coordinator;
    std::optional<SessionAuthority> authority;
    std::optional<RECT> clientBounds;
    std::optional<RECT> clientClip;
    Microsoft::WRL::ComPtr<IUnknown> parkingTarget;
    std::optional<PresentationRequest> presentationRequest;
    std::optional<EndpointGeometry> committedGeometry;
};

struct TransitionInput final {
    bool declarationCurrent{};
    bool documentIdentityCurrent{};
    bool endpointRequested{};
    bool endpointAvailable{};
    GeometryState geometry{GeometryState::Pending};
    PresentationState requestedPresentation{PresentationState::Parked};
    ParkingReason parkingReason{ParkingReason::EndpointUnavailable};
    std::optional<EndpointGeometry> desiredGeometry;
};

enum class TransitionEffect {
    None,
    Park,
    UpdateParkingReason,
    Present,
    Update,
    Retire,
    Fault,
};

struct TransitionPlan final {
    TransitionEffect effect{TransitionEffect::None};
    PresentationState target{PresentationState::Parked};
    std::optional<ParkingReason> parkingReason;
};

struct TransitionOperations final {
    std::function<HRESULT(const SessionKey&, SessionRecord&, ParkingReason)> park;
    std::function<HRESULT(
        const SessionKey&,
        SessionRecord&,
        PresentationState,
        const EndpointGeometry&)> present;
    std::function<HRESULT(
        const SessionKey&,
        SessionRecord&,
        PresentationState,
        const EndpointGeometry&)> update;
    std::function<HRESULT(const SessionKey&, SessionRecord&)> retire;
    std::function<HRESULT(const SessionKey&, SessionRecord&)> fault;
};

class MediaSessionManager final {
public:
    explicit MediaSessionManager(richmedia::RichMediaEnvironmentHandle environment);

    [[nodiscard]] static TransitionPlan PlanTransition(
        const SessionRecord& current,
        const TransitionInput& input) noexcept;

    [[nodiscard]] SessionRecord* Ensure(const SessionKey& key);
    [[nodiscard]] SessionRecord* Find(const SessionKey& key) noexcept;
    [[nodiscard]] const SessionRecord* Find(const SessionKey& key) const noexcept;
    [[nodiscard]] std::vector<SessionKey> Keys() const;
    [[nodiscard]] std::vector<SessionKey> KeysForWidget(
        std::wstring_view widgetId) const;
    [[nodiscard]] std::optional<SessionKey> EndpointOwner(Endpoint endpoint) const;
    [[nodiscard]] bool RequestPresentation(
        const SessionKey& key,
        PresentationState target,
        std::wstring_view presentationGeneration);
    void ClearPresentationRequest(const SessionKey& key);
    [[nodiscard]] HRESULT Reconcile(
        const SessionKey& key,
        const TransitionInput& input,
        const TransitionOperations& operations);
    [[nodiscard]] std::size_t size() const noexcept { return sessions_.size(); }
    [[nodiscard]] bool empty() const noexcept { return sessions_.empty(); }
    [[nodiscard]] bool EraseAfterTerminal(const SessionKey& key);

private:
    [[nodiscard]] static std::optional<Endpoint> EndpointFor(
        PresentationState presentation) noexcept;
    void ClearEndpointOwnership(const SessionKey& key) noexcept;
    // Records one deferral and reports whether the streak has outlived
    // its bounds and must be escalated to a failure.
    [[nodiscard]] static bool DeferralExhausted(SessionRecord& record) noexcept;
    void ReleaseOtherEndpointOwnership(
        const SessionKey& key, Endpoint retained) noexcept;

    richmedia::RichMediaEnvironmentHandle environment_;
    std::map<std::wstring, SessionRecord, std::less<>> sessions_;
    std::optional<SessionKey> overlayOwner_;
    std::optional<SessionKey> pinnedOwner_;
};

} // namespace widgetrail::media
