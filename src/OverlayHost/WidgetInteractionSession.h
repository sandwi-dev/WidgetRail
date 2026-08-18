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
    std::wstring widgetInstanceId;
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
        const SliderInputDescriptor& slider,
        NavigationDirection direction,
        std::uint64_t now);
    [[nodiscard]] SliderInputOutcome RequestSliderValue(
        const SliderInputDescriptor& slider,
        double value,
        std::uint64_t now);
    [[nodiscard]] SliderInputOutcome CancelSliderAction(
        const SliderInputDescriptor& slider,
        std::uint64_t now);
    [[nodiscard]] bool SliderAdjustmentModeActive(
        const SliderInputDescriptor& slider,
        std::uint64_t now);
    [[nodiscard]] bool TransitionSliderAdjustmentMode(
        const SliderInputDescriptor& slider,
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

private:
    [[nodiscard]] bool BindingMatches(
        const FreeScrollBinding& binding,
        const WidgetInteractionAuthority& authority) const noexcept;
    [[nodiscard]] static SliderInputDescriptor SliderDescriptor(
        const WidgetSnapshot& snapshot,
        const WidgetNode& node) noexcept;
    [[nodiscard]] static std::vector<std::wstring> CurrentSliderNodeIds(
        const WidgetSnapshot& snapshot,
        const std::vector<SliderPresentationIdentity>& identities);
    void RefreshSliderDeadline() noexcept;

    RightStickScrollKinetics rightStickKinetics_;
    std::optional<FreeScrollBinding> freeScrollBinding_;
    bool freeScrollRefreshDeferred_{};
    std::wstring focusedElementId_;
    WidgetSurfaceFocusMemory focusMemory_;
    SliderInteractionState sliders_;
    PressedInteractionState pressed_;
    std::uint64_t sliderReconcileAt_{};
};

} // namespace widgetrail::input
