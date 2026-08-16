#pragma once

#include "OverlayPlacement.h"
#include "OverlayTargeting.h"
#include "OverlayTransition.h"
#include "WidgetBridgeClient.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

namespace gba {

struct RetainedWidgetPresentation final {
    std::wstring widgetId;
    WidgetSnapshot snapshot;
    std::wstring focusId;
};

struct CompositionAdmissionDirective final {
    OverlayPlacement destinationPlacement;
    OverlayPlacement containerPlacement;
    OverlayPresentationExtent destinationExtentDip;
    CompositionMotionPlan initialPresentation;
    bool animateMotion{};
};

struct CompositionMotionStep final {
    OverlayPlacement destinationPlacement;
    CompositionMotionPlan presentation;
    OverlayPresentationExtent presentedExtentDip;
    std::uint64_t index{};
    bool finalFrame{};
};

struct CommittedPresentationDestination final {
    std::wstring widgetId;
    OverlayPresentationExtent extentDip;
};

/// Owns the bounded state of one visible presentation handoff. The Win32 host
/// remains the sole HWND, compositor, render, focus, input, and UIA owner; it
/// executes the typed directives produced here after destination layout has
/// resolved. This object deliberately does not own a timer or OS resource.
class OverlayPresentationTransaction final {
public:
    void RetainAdmittedWidget(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusId,
        std::optional<WidgetSurfaceRequest> surfaceRequest) {
        retainedSurfaceRequest_ = std::move(surfaceRequest);
        retainedSurfaceAvailable_ = true;
        retainedPresentation_ = RetainedWidgetPresentation{
            std::wstring{widgetId}, snapshot, std::wstring{focusId}};
    }

    [[nodiscard]] bool retainedSurfaceAvailable() const noexcept {
        return retainedSurfaceAvailable_;
    }

    [[nodiscard]] const std::optional<WidgetSurfaceRequest>&
    retainedSurfaceRequest() const noexcept {
        return retainedSurfaceRequest_;
    }

    [[nodiscard]] const std::optional<RetainedWidgetPresentation>&
    retainedPresentation() const noexcept {
        return retainedPresentation_;
    }

    [[nodiscard]] OverlayPresentationExtent PresentedExtent(
        const OverlayPresentationExtent desired,
        const bool compositionAvailable) const noexcept {
        if (!compositionAvailable) return animatedExtentDip_.value_or(desired);
        if (compositionPresentedExtentDip_)
            return *compositionPresentedExtentDip_;
        return committedDestination_
            ? committedDestination_->extentDip
            : desired;
    }

    void HoldPresentedExtent(const OverlayPresentationExtent extent) noexcept {
        compositionPresentedExtentDip_ = extent;
    }

    void ReleasePresentedExtent() noexcept {
        compositionPresentedExtentDip_.reset();
    }

    void BeginExtentTransition(
        const OverlayPresentationExtent from,
        const OverlayPresentationExtent target,
        const std::uint64_t timestamp,
        const bool reducedMotion,
        const bool compositionAvailable) noexcept {
        extentTransition_.Begin(
            timestamp,
            static_cast<float>(from.widthDip),
            static_cast<float>(from.heightDip),
            static_cast<float>(target.widthDip),
            static_cast<float>(target.heightDip),
            reducedMotion);
        const auto initial = extentTransition_.Sample(timestamp, reducedMotion);
        if (compositionAvailable) {
            animatedExtentDip_.reset();
            compositionPresentedExtentDip_ = from;
        } else {
            animatedExtentDip_ = OverlayPresentationExtent{
                static_cast<int>(std::lround(initial.widthDip)),
                static_cast<int>(std::lround(initial.heightDip)),
            };
        }
    }

    void SettleExtent(
        const OverlayPresentationExtent extent,
        const std::uint64_t timestamp,
        const bool reducedMotion) noexcept {
        extentTransition_.Begin(
            timestamp,
            static_cast<float>(extent.widthDip),
            static_cast<float>(extent.heightDip),
            static_cast<float>(extent.widthDip),
            static_cast<float>(extent.heightDip),
            reducedMotion);
        animatedExtentDip_.reset();
        compositionPresentedExtentDip_.reset();
    }

    [[nodiscard]] bool hasActiveExtent() const noexcept {
        return extentTransition_.active() || animatedExtentDip_ ||
            finalCompositionPlacement_;
    }

    [[nodiscard]] bool extentTransitionActive() const noexcept {
        return extentTransition_.active();
    }

    [[nodiscard]] OverlayPresentationExtent AdvanceFallbackExtent(
        const std::uint64_t timestamp,
        const bool reducedMotion) noexcept {
        const auto sample = extentTransition_.Sample(timestamp, reducedMotion);
        animatedExtentDip_ = OverlayPresentationExtent{
            static_cast<int>(std::lround(sample.widthDip)),
            static_cast<int>(std::lround(sample.heightDip)),
        };
        const auto result = *animatedExtentDip_;
        if (!sample.active) animatedExtentDip_.reset();
        return result;
    }

    [[nodiscard]] CompositionAdmissionDirective PrepareCompositionAdmission(
        const unsigned int priorWidth,
        const unsigned int priorHeight,
        const OverlayPlacement& destinationPlacement,
        const OverlayPlacement& containerPlacement,
        const OverlayPresentationExtent destinationExtentDip,
        const std::uint64_t timestamp,
        const bool reducedMotion,
        const bool wasVisible) noexcept {
        CompositionAdmissionDirective directive{
            destinationPlacement,
            containerPlacement,
            destinationExtentDip,
            CompositionMotionPlan{
                static_cast<unsigned int>(containerPlacement.width),
                static_cast<unsigned int>(containerPlacement.height),
                1.0F,
                1.0F,
                static_cast<float>(
                    destinationPlacement.x - containerPlacement.x),
                static_cast<float>(
                    destinationPlacement.y - containerPlacement.y),
                containerPlacement.width != destinationPlacement.width ||
                    containerPlacement.height != destinationPlacement.height,
            },
            wasVisible && priorWidth != 0 && priorHeight != 0 &&
                extentTransition_.active(),
        };
        if (!directive.animateMotion) return directive;

        const auto initial = extentTransition_.Sample(timestamp, reducedMotion);
        const float pixelsPerDipX =
            static_cast<float>(destinationPlacement.width) /
            static_cast<float>(std::max(1, destinationExtentDip.widthDip));
        const float pixelsPerDipY =
            static_cast<float>(destinationPlacement.height) /
            static_cast<float>(std::max(1, destinationExtentDip.heightDip));
        directive.initialPresentation = PlanCompositionMotion(
            static_cast<unsigned int>(containerPlacement.width),
            static_cast<unsigned int>(containerPlacement.height),
            static_cast<unsigned int>(destinationPlacement.width),
            static_cast<unsigned int>(destinationPlacement.height),
            initial.widthDip * pixelsPerDipX,
            initial.heightDip * pixelsPerDipY,
            CompositionVerticalAnchor::Bottom);
        return directive;
    }

    void AcceptCompositionAdmission(
        const CompositionAdmissionDirective& directive,
        const std::wstring_view widgetId = {}) noexcept {
        contentPlacement_ = directive.destinationPlacement;
        settledContainerPlacement_ = directive.containerPlacement;
        committedDestination_ = CommittedPresentationDestination{
            std::wstring{widgetId}, directive.destinationExtentDip};
        if (!directive.animateMotion) {
            finalCompositionPlacement_.reset();
            compositionPresentedExtentDip_.reset();
            motionCommitCount_ = 0;
            return;
        }
        finalCompositionPlacement_ = directive.destinationPlacement;
        motionContainerPlacement_ = directive.containerPlacement;
        motionPixelsPerDipX_ =
            static_cast<float>(directive.destinationPlacement.width) /
            static_cast<float>(std::max(1, directive.destinationExtentDip.widthDip));
        motionPixelsPerDipY_ =
            static_cast<float>(directive.destinationPlacement.height) /
            static_cast<float>(std::max(1, directive.destinationExtentDip.heightDip));
        motionCommitCount_ = 0;
        compositionPresentedExtentDip_ = OverlayPresentationExtent{
            static_cast<int>(std::lround(
                static_cast<float>(directive.destinationPlacement.width) *
                directive.initialPresentation.scaleX / motionPixelsPerDipX_)),
            static_cast<int>(std::lround(
                static_cast<float>(directive.destinationPlacement.height) *
                directive.initialPresentation.scaleY / motionPixelsPerDipY_)),
        };
    }

    /// A same-geometry repaint may transfer the active identity without moving
    /// the HWND. Keep the already committed extent and update only that typed
    /// destination identity after the frame commit succeeds.
    void AcceptCompositionRepaint(
        const std::wstring_view widgetId) noexcept {
        if (committedDestination_)
            committedDestination_->widgetId = widgetId;
    }

    [[nodiscard]] const std::optional<CommittedPresentationDestination>&
    committedDestination() const noexcept {
        return committedDestination_;
    }

    [[nodiscard]] OverlayPresentationExtent CommittedDestinationExtent(
        const OverlayPresentationExtent fallback) const noexcept {
        return committedDestination_
            ? committedDestination_->extentDip
            : fallback;
    }

    void RejectCompositionAdmission() noexcept {
        finalCompositionPlacement_.reset();
        motionContainerPlacement_.reset();
        settledContainerPlacement_.reset();
        compositionPresentedExtentDip_.reset();
        contentPlacement_.reset();
        motionCommitCount_ = 0;
    }

    [[nodiscard]] std::optional<CompositionMotionStep> PrepareCompositionStep(
        const std::uint64_t timestamp,
        const bool reducedMotion) noexcept {
        if (!finalCompositionPlacement_ || !motionContainerPlacement_)
            return std::nullopt;
        const auto extent = extentTransition_.Sample(timestamp, reducedMotion);
        const auto& destination = *finalCompositionPlacement_;
        const auto& container = *motionContainerPlacement_;
        return CompositionMotionStep{
            destination,
            PlanCompositionMotion(
                static_cast<unsigned int>(container.width),
                static_cast<unsigned int>(container.height),
                static_cast<unsigned int>(destination.width),
                static_cast<unsigned int>(destination.height),
                extent.widthDip * motionPixelsPerDipX_,
                extent.heightDip * motionPixelsPerDipY_,
                CompositionVerticalAnchor::Bottom),
            OverlayPresentationExtent{
                static_cast<int>(std::lround(extent.widthDip)),
                static_cast<int>(std::lround(extent.heightDip)),
            },
            motionCommitCount_ + 1,
            !extent.active,
        };
    }

    void AcceptCompositionStep(const CompositionMotionStep& step) noexcept {
        motionCommitCount_ = step.index;
        compositionPresentedExtentDip_ = step.presentedExtentDip;
        if (!step.finalFrame) return;
        finalCompositionPlacement_.reset();
        motionContainerPlacement_.reset();
        compositionPresentedExtentDip_.reset();
        motionCommitCount_ = 0;
    }

    void SettleCompositionContainer(const OverlayPlacement& placement) noexcept {
        settledContainerPlacement_ = placement;
    }

    [[nodiscard]] const std::optional<OverlayPlacement>& contentPlacement() const noexcept {
        return contentPlacement_;
    }

    [[nodiscard]] CompositionPoint ChromeOffsetWithinContainer() const noexcept {
        const auto& container = motionContainerPlacement_
            ? motionContainerPlacement_ : settledContainerPlacement_;
        if (!contentPlacement_ || !container) return {};
        return {
            static_cast<float>(
                contentPlacement_->x - container->x),
            static_cast<float>(
                contentPlacement_->y - container->y),
        };
    }

    [[nodiscard]] std::optional<CompositionMotionPlan> CurrentMotionPlan(
        const unsigned int clientWidth,
        const unsigned int clientHeight,
        const OverlayPresentationExtent desired) const noexcept {
        if (!contentPlacement_) return std::nullopt;
        const auto& target = *contentPlacement_;
        if (!finalCompositionPlacement_ && settledContainerPlacement_) {
            const auto& container = *settledContainerPlacement_;
            return CompositionMotionPlan{
                clientWidth, clientHeight,
                1.0F, 1.0F,
                static_cast<float>(target.x - container.x),
                static_cast<float>(target.y - container.y),
                clientWidth != static_cast<unsigned int>(target.width) ||
                    clientHeight != static_cast<unsigned int>(target.height),
            };
        }
        const auto presented = compositionPresentedExtentDip_.value_or(desired);
        const float pixelsPerDipX = finalCompositionPlacement_
            ? motionPixelsPerDipX_
            : static_cast<float>(target.width) /
                static_cast<float>(std::max(1, desired.widthDip));
        const float pixelsPerDipY = finalCompositionPlacement_
            ? motionPixelsPerDipY_
            : static_cast<float>(target.height) /
                static_cast<float>(std::max(1, desired.heightDip));
        return PlanCompositionMotion(
            clientWidth,
            clientHeight,
            static_cast<unsigned int>(target.width),
            static_cast<unsigned int>(target.height),
            static_cast<float>(presented.widthDip) * pixelsPerDipX,
            static_cast<float>(presented.heightDip) * pixelsPerDipY,
            CompositionVerticalAnchor::Bottom);
    }

    [[nodiscard]] bool RetireHidden() noexcept {
        const bool inFlight = extentTransition_.active() ||
            finalCompositionPlacement_ || compositionPresentedExtentDip_;
        extentTransition_.Cancel();
        animatedExtentDip_.reset();
        finalCompositionPlacement_.reset();
        motionContainerPlacement_.reset();
        settledContainerPlacement_.reset();
        contentPlacement_.reset();
        compositionPresentedExtentDip_.reset();
        committedDestination_.reset();
        motionPixelsPerDipX_ = 1.0F;
        motionPixelsPerDipY_ = 1.0F;
        motionCommitCount_ = 0;
        return inFlight;
    }

private:
    bool retainedSurfaceAvailable_{};
    std::optional<WidgetSurfaceRequest> retainedSurfaceRequest_;
    std::optional<RetainedWidgetPresentation> retainedPresentation_;
    OverlayExtentTransitionTimeline extentTransition_;
    std::optional<OverlayPresentationExtent> animatedExtentDip_;
    std::optional<OverlayPlacement> finalCompositionPlacement_;
    std::optional<OverlayPlacement> motionContainerPlacement_;
    std::optional<OverlayPlacement> settledContainerPlacement_;
    std::optional<OverlayPlacement> contentPlacement_;
    std::optional<OverlayPresentationExtent> compositionPresentedExtentDip_;
    std::optional<CommittedPresentationDestination> committedDestination_;
    float motionPixelsPerDipX_{1.0F};
    float motionPixelsPerDipY_{1.0F};
    std::uint64_t motionCommitCount_{};
};

} // namespace gba
