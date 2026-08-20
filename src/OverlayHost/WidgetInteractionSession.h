#pragma once

#include "ControllerNavigation.h"
#include "FocusNavigation.h"
#include "PressedInteraction.h"
#include "SliderInteraction.h"
#include "WidgetSurfaceFocus.h"

#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace widgetrail::input {

/// Immutable authority supplied by OverlayApp for one admitted interaction
/// generation. The session never reaches back into lifecycle, bridge, renderer,
/// or window owners to discover a newer authority.
struct WidgetInteractionAuthority final {
    std::wstring_view widgetId;
    const WidgetSnapshot* semantics{};
    std::wstring_view runtimeGeneration;
    std::wstring_view presentationGeneration;
    bool retainedRefresh{};
};

struct FreeScrollBinding final {
    std::wstring widgetId;
    std::wstring widgetInstanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring inputScopeId;
    std::wstring focusedElementId;
    std::wstring scrollId;
    declarative::ScrollAxis axis{declarative::ScrollAxis::None};
};

enum class FreeScrollAuthorityDisposition {
    Missing,
    Current,
    Retained,
    Replaced,
};

struct FreeScrollAuthorityDecision final {
    FreeScrollAuthorityDisposition disposition{
        FreeScrollAuthorityDisposition::Missing};
    bool followSuppressed{};
};

struct FreeScrollReentryRequest final {
    bool consumed{};
    std::optional<std::wstring> target;
    std::optional<FreeScrollBinding> retiredBinding;
};

/// Typed visual work returned when focus authority changes. OverlayApp remains
/// the final damage arbiter because it owns the HWND and renderer geometry.
struct FocusMutation final {
    std::wstring priorFocus;
    std::wstring focusedElementId;
    std::vector<std::wstring> sliderDamageNodeIds;
    bool pressedPresentationChanged{};
    bool changed{};
};

struct WidgetInteractionPresentation final {
    std::wstring_view focusedElementId;
    std::wstring_view pressedElementId;
    std::uint64_t sliderPresentationRevision{};
    bool suppressFocusedDescendantFollow{};
};

struct InteractionVisualRetirement final {
    std::vector<SliderPresentationIdentity> sliderRollbacks;
    bool pressedPresentationChanged{};
};

struct InteractionReconciliation final {
    std::vector<std::wstring> sliderDamageNodeIds;
    bool pressedPresentationChanged{};
    std::uint64_t nextDeadline{};
};

struct WidgetInteractionActionRequest final {
    std::wstring widgetId;
    std::wstring widgetInstanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring inputScopeId;
    std::wstring sourceElementId;
    std::wstring actionId;
    long long snapshotSequence{};
    std::optional<double> requestedValue;
};

struct SliderInputOutcome final {
    bool consumed{};
    bool visualChanged{};
    std::optional<WidgetInteractionActionRequest> actionRequest;
    std::uint64_t presentationRevision{};
    std::uint64_t nextDeadline{};
};

enum class PressedInputTransition {
    Begin,
    End,
    Cancel,
    Clear,
};

enum class SliderAdjustmentModeTransition {
    Enter,
    Exit,
};

struct InteractionRenderPresentation final {
    std::map<std::wstring, double, std::less<>> sliderValueOverrides;
    std::wstring pressedElementId;
    std::uint64_t sliderPresentationRevision{};
};

enum class ScrollPaginationDiagnosticKind {
    Queued,
    Suppressed,
    Admitted,
    Completed,
    Retired,
    TerminalFailure,
};

enum class ScrollPaginationDispatchDisposition {
    Admitted,
    NotHandled,
    TransportFailure,
    StaleAuthority,
};

enum class ScrollPaginationDemandReason {
    Initial,
    Intent,
    ThresholdReentry,
};

enum class ScrollPaginationIntentSource {
    None,
    RightStick,
    DirectionalNavigation,
    Pointer,
    Accessibility,
};

struct ScrollPaginationPrefetchRequest final {
    std::wstring widgetId;
    std::wstring widgetInstanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring inputScopeId;
    ScrollPaginationAction action;
    ScrollPaginationDemandReason demandReason{
        ScrollPaginationDemandReason::Initial};
    ScrollPaginationIntentSource intentSource{
        ScrollPaginationIntentSource::None};
    std::uint64_t demandGeneration{};
};

struct ScrollPaginationDiagnostic final {
    ScrollPaginationDiagnosticKind kind{ScrollPaginationDiagnosticKind::Retired};
    ScrollPaginationPrefetchRequest request;
    std::wstring reason;
    std::wstring replacementEdgeKey;
    std::uint64_t adjacentActionCount{};
    std::uint64_t visibleCompletionCount{};
    std::uint64_t queueLatencyMilliseconds{};
    std::uint64_t thresholdToVisibleMilliseconds{};
    std::uint64_t averageVisibleLatencyMilliseconds{};
};

struct ScrollPaginationSessionOutcome final {
    std::vector<ScrollPaginationDiagnostic> diagnostics;
    bool dispatchReady{};
};

struct ScrollPaginationBoundaryOutcome final {
    ScrollPaginationSessionOutcome pagination;
    bool retainFocus{};
};

struct ScrollPaginationDispatchOutcome final {
    ScrollPaginationPrefetchRequest request;
    ScrollPaginationDispatchDisposition disposition{
        ScrollPaginationDispatchDisposition::StaleAuthority};
    std::wstring safeDiagnostic;
    std::uint64_t now{};
};

[[nodiscard]] std::wstring FormatScrollPaginationDiagnostic(
    const ScrollPaginationDiagnostic& diagnostic);

/// Owns mutable interaction presentation for the admitted widget surface.
/// It does not poll controllers, mutate renderer scroll offsets, dispatch bridge
/// actions, authorize text entry, or arbitrate final host/widget commands.
class WidgetInteractionSession final {
public:
    [[nodiscard]] const std::wstring& focusedElementId() const noexcept {
        return focusedElementId_;
    }

    void SetFocus(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view target);
    [[nodiscard]] FocusMutation MoveFocus(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view target,
        bool retireSliderPresentations = true,
        bool retirePressedPresentation = true);
    void ClearFocus() noexcept;
    void RememberFocus(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot);
    [[nodiscard]] std::wstring RestoreFocus(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot);
    [[nodiscard]] std::wstring FocusRestoreCandidate(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot) const;
    void ForgetWidget(std::wstring_view widgetId);

    [[nodiscard]] RightStickScrollUpdate SampleRightStick(
        short x,
        short y,
        std::uint64_t now) noexcept;
    [[nodiscard]] FreeScrollAuthorityDecision EvaluateFreeScrollAuthority(
        const WidgetInteractionAuthority& authority) const noexcept;
    [[nodiscard]] bool BindFreeScroll(
        const WidgetInteractionAuthority& authority,
        std::wstring_view scrollId,
        declarative::ScrollAxis axis);
    [[nodiscard]] std::optional<FreeScrollBinding> ClearFreeScroll() noexcept;
    [[nodiscard]] bool SetRefreshDeferred(bool deferred) noexcept;
    [[nodiscard]] const std::optional<FreeScrollBinding>& freeScrollBinding()
        const noexcept {
        return freeScrollBinding_;
    }
    [[nodiscard]] FreeScrollReentryRequest ResolveFreeScrollReentry(
        const WidgetInteractionAuthority& authority,
        const RenderResult& renderResult);

    [[nodiscard]] WidgetInteractionPresentation Presentation(
        const WidgetInteractionAuthority& authority) const noexcept;

    [[nodiscard]] InteractionVisualRetirement RetirePresentations();
    void ForgetRuntime(std::wstring_view widgetInstanceId) noexcept;
    [[nodiscard]] InteractionReconciliation ReconcileAdmission(
        const WidgetSnapshot& snapshot,
        std::uint64_t now);
    [[nodiscard]] bool ReconcilePressedPresentation(
        const WidgetSnapshot& snapshot) noexcept;
    [[nodiscard]] InteractionReconciliation Tick(
        const WidgetSnapshot* snapshot,
        std::uint64_t now);

    [[nodiscard]] SliderInputOutcome AdjustSlider(
        const WidgetInteractionAuthority& authority,
        const WidgetNode& node,
        NavigationDirection direction,
        std::uint64_t now);
    [[nodiscard]] SliderInputOutcome RequestSliderValue(
        const WidgetInteractionAuthority& authority,
        const WidgetNode& node,
        double value,
        std::uint64_t now);
    [[nodiscard]] SliderInputOutcome CancelSliderAction(
        const WidgetInteractionAuthority& authority,
        const WidgetNode& node,
        std::uint64_t now);
    [[nodiscard]] SliderInputOutcome CancelSliderAction(
        const WidgetInteractionActionRequest& request,
        std::uint64_t now);
    [[nodiscard]] bool SliderAdjustmentModeActive(
        const WidgetInteractionAuthority& authority,
        const WidgetNode& node,
        std::uint64_t now);
    [[nodiscard]] bool TransitionSliderAdjustmentMode(
        const WidgetInteractionAuthority& authority,
        const WidgetNode& node,
        SliderAdjustmentModeTransition transition,
        std::uint64_t now);
    [[nodiscard]] bool TransitionPressedPresentation(
        PressedInputTransition transition,
        const WidgetSnapshot* snapshot = nullptr,
        std::wstring_view focusedElementId = {},
        std::wstring_view protocolButton = {});
    [[nodiscard]] InteractionRenderPresentation PrepareRenderPresentation(
        const WidgetSnapshot& snapshot,
        std::wstring_view renderedFocusId,
        std::uint64_t now,
        bool retainAdjustmentMode,
        bool allowPressedPresentation,
        bool allowAdjustmentModePresentation);

    [[nodiscard]] std::uint64_t sliderPresentationRevision() const noexcept {
        return sliders_.presentationRevision();
    }
    [[nodiscard]] bool SliderReconcileDue(std::uint64_t now) const noexcept;

    [[nodiscard]] ScrollPaginationSessionOutcome ReconcileScrollPagination(
        const WidgetInteractionAuthority& authority,
        const RenderResult& renderResult,
        std::uint64_t now);
    [[nodiscard]] ScrollPaginationSessionOutcome ObserveScrollPaginationIntent(
        const WidgetInteractionAuthority& authority,
        std::wstring_view scrollId,
        declarative::ScrollAxis axis,
        ScrollPaginationEdge edge,
        ScrollPaginationIntentSource source,
        std::uint64_t now);
    [[nodiscard]] ScrollPaginationSessionOutcome ObserveScrollPaginationFocusIntent(
        const WidgetInteractionAuthority& authority,
        const RenderResult& renderResult,
        std::wstring_view priorFocus,
        std::wstring_view nextFocus,
        ScrollPaginationIntentSource source,
        std::uint64_t now);
    [[nodiscard]] ScrollPaginationBoundaryOutcome
        ObserveScrollPaginationBoundaryIntent(
            const WidgetInteractionAuthority& authority,
            const RenderResult& renderResult,
            std::wstring_view focusedElementId,
            NavigationDirection direction,
            ScrollPaginationIntentSource source,
            std::uint64_t now);
    [[nodiscard]] std::pair<
        std::optional<ScrollPaginationPrefetchRequest>,
        ScrollPaginationSessionOutcome> AcquireScrollPaginationDispatch(
            const WidgetInteractionAuthority& authority,
            const RenderResult& renderResult,
            std::uint64_t now);
    [[nodiscard]] ScrollPaginationSessionOutcome CompleteScrollPaginationDispatch(
        const ScrollPaginationDispatchOutcome& outcome);
    [[nodiscard]] ScrollPaginationSessionOutcome ObserveScrollPaginationFailure(
        const WidgetActionFailure& failure,
        std::uint64_t now);
    [[nodiscard]] ScrollPaginationSessionOutcome RetireScrollPagination(
        std::wstring_view widgetId,
        std::wstring_view reason);

private:
    [[nodiscard]] bool BindingMatches(
        const FreeScrollBinding& binding,
        const WidgetInteractionAuthority& authority) const noexcept;
    [[nodiscard]] static SliderInputDescriptor SliderDescriptor(
        const WidgetSnapshot& snapshot,
        const WidgetNode& node) noexcept;
    [[nodiscard]] static const WidgetNode* ExactSliderNode(
        const WidgetInteractionAuthority& authority,
        const WidgetNode& node) noexcept;
    [[nodiscard]] static std::vector<std::wstring> CurrentSliderNodeIds(
        const WidgetSnapshot& snapshot,
        const std::vector<SliderPresentationIdentity>& identities);
    void RefreshSliderDeadline() noexcept;

    enum class ScrollPaginationPrefetchStatus {
        Queued,
        InFlight,
        TerminalFailure,
    };
    struct ScrollPaginationPrefetchAuthority final {
        ScrollPaginationPrefetchRequest request;
        ScrollPaginationPrefetchStatus status{
            ScrollPaginationPrefetchStatus::Queued};
        std::uint64_t thresholdAt{};
        std::uint64_t admittedAt{};
        bool suppressionReported{};
    };
    struct ScrollPaginationDemandLatch final {
        std::wstring widgetId;
        std::wstring widgetInstanceId;
        std::wstring runtimeGeneration;
        std::wstring presentationGeneration;
        std::wstring inputScopeId;
        std::wstring scrollId;
        declarative::ScrollAxis axis{declarative::ScrollAxis::None};
        bool initialized{};
        bool beforeResident{};
        bool afterResident{};
        std::optional<ScrollPaginationEdge> latchedEdge;
        std::optional<ScrollPaginationEdge> pendingIntentEdge;
        ScrollPaginationIntentSource pendingIntentSource{
            ScrollPaginationIntentSource::None};
        std::uint64_t pendingIntentGeneration{};
        std::uint64_t lastDemandGeneration{};
        std::optional<ScrollPaginationPrefetchAuthority> prefetch;
        bool reconciliationSuppressionReported{};
    };
    [[nodiscard]] static bool SameScrollPaginationAuthority(
        const ScrollPaginationDemandLatch& latch,
        const WidgetInteractionAuthority& current,
        const ScrollPaginationAction& action) noexcept;
    [[nodiscard]] static bool SameScrollPaginationRouteEdge(
        const ScrollPaginationDemandLatch& latch,
        const WidgetInteractionAuthority& current,
        const ScrollPaginationAction& action) noexcept;
    [[nodiscard]] static ScrollPaginationPrefetchRequest MakeScrollPaginationRequest(
        const WidgetInteractionAuthority& authority,
        const ScrollPaginationAction& action,
        ScrollPaginationDemandReason reason,
        ScrollPaginationIntentSource source,
        std::uint64_t demandGeneration);
    [[nodiscard]] static bool SameScrollPaginationRoute(
        const ScrollPaginationDemandLatch& latch,
        const WidgetInteractionAuthority& authority) noexcept;

    RightStickScrollKinetics rightStickKinetics_;
    std::optional<FreeScrollBinding> freeScrollBinding_;
    bool freeScrollRefreshDeferred_{};
    std::wstring focusedElementId_;
    WidgetSurfaceFocusMemory focusMemory_;
    SliderInteractionState sliders_;
    PressedInteractionState pressed_;
    std::uint64_t sliderReconcileAt_{};
    std::vector<ScrollPaginationDemandLatch> scrollPaginationLatches_;
    bool scrollPaginationRouteObserved_{};
    std::uint64_t scrollPaginationDemandGeneration_{};
    std::uint64_t scrollPaginationAdjacentActionCount_{};
    std::uint64_t scrollPaginationVisibleCompletionCount_{};
    std::uint64_t scrollPaginationVisibleLatencyTotalMs_{};
};

} // namespace widgetrail::input
