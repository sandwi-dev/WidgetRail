#include "WidgetInteractionSession.h"

#include <utility>

namespace widgetrail::input {

void WidgetInteractionSession::SetFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view target) {
    focusedElementId_ = target;
    focusMemory_.Remember(widgetId, snapshot, focusedElementId_);
}

FocusMutation WidgetInteractionSession::MoveFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view target,
    const bool retireSliderPresentations,
    const bool retirePressedPresentation) {
    FocusMutation result;
    result.priorFocus = focusedElementId_;
    if (retireSliderPresentations)
        result.sliderRollbacks = sliders_.DeactivateAll();
    result.pressedPresentationChanged =
        retirePressedPresentation && pressed_.Clear();
    SetFocus(widgetId, snapshot, target);
    result.focusedElementId = focusedElementId_;
    result.changed = result.priorFocus != result.focusedElementId;
    if (retireSliderPresentations) RefreshSliderDeadline();
    return result;
}

void WidgetInteractionSession::ClearFocus() noexcept {
    focusedElementId_.clear();
}

void WidgetInteractionSession::RememberFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) {
    focusMemory_.Remember(widgetId, snapshot, focusedElementId_);
}

std::wstring WidgetInteractionSession::RestoreFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) {
    focusedElementId_ = focusMemory_.Restore(widgetId, snapshot);
    return focusedElementId_;
}

void WidgetInteractionSession::ForgetWidget(const std::wstring_view widgetId) {
    focusMemory_.Forget(widgetId);
    sliders_.ForgetWidget(widgetId);
}

RightStickScrollUpdate WidgetInteractionSession::SampleRightStick(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
    return rightStickKinetics_.Update(x, y, now);
}

bool WidgetInteractionSession::BindingMatches(
    const FreeScrollBinding& binding,
    const WidgetInteractionAuthority& authority) const noexcept {
    return authority.semantics &&
        binding.widgetId == authority.widgetId &&
        binding.widgetInstanceId == authority.semantics->instanceId &&
        binding.runtimeGeneration == authority.runtimeGeneration &&
        binding.presentationGeneration == authority.presentationGeneration &&
        binding.inputScopeId == authority.semantics->activeInputScopeId &&
        binding.focusedElementId == focusedElementId_ &&
        binding.axis != declarative::ScrollAxis::None;
}

FreeScrollAuthorityDecision WidgetInteractionSession::EvaluateFreeScrollAuthority(
    const WidgetInteractionAuthority& authority) const noexcept {
    if (!authority.semantics) {
        return {FreeScrollAuthorityDisposition::Missing, false};
    }
    if (!freeScrollBinding_)
        return {FreeScrollAuthorityDisposition::Current, false};
    if (!BindingMatches(*freeScrollBinding_, authority))
        return {FreeScrollAuthorityDisposition::Replaced, false};
    return {
        authority.retainedRefresh
            ? FreeScrollAuthorityDisposition::Retained
            : FreeScrollAuthorityDisposition::Current,
        true,
    };
}

bool WidgetInteractionSession::BindFreeScroll(
    const WidgetInteractionAuthority& authority,
    const std::wstring_view scrollId,
    const declarative::ScrollAxis axis) {
    if (!authority.semantics || axis == declarative::ScrollAxis::None ||
        scrollId.empty()) {
        return false;
    }
    const bool changed = !freeScrollBinding_ ||
        freeScrollBinding_->scrollId != scrollId ||
        freeScrollBinding_->axis != axis;
    freeScrollBinding_ = FreeScrollBinding{
        std::wstring{authority.widgetId},
        authority.semantics->instanceId,
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        authority.semantics->activeInputScopeId,
        focusedElementId_,
        std::wstring{scrollId},
        axis,
    };
    freeScrollRefreshDeferred_ = false;
    return changed;
}

std::optional<FreeScrollBinding> WidgetInteractionSession::ClearFreeScroll() noexcept {
    auto prior = std::move(freeScrollBinding_);
    freeScrollBinding_.reset();
    freeScrollRefreshDeferred_ = false;
    rightStickKinetics_.Reset();
    return prior;
}

bool WidgetInteractionSession::SetRefreshDeferred(const bool deferred) noexcept {
    if (!freeScrollBinding_ || freeScrollRefreshDeferred_ == deferred)
        return false;
    freeScrollRefreshDeferred_ = deferred;
    return true;
}

FreeScrollReentryRequest WidgetInteractionSession::ResolveFreeScrollReentry(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult) {
    FreeScrollReentryRequest request;
    if (!freeScrollBinding_ || !authority.semantics ||
        !BindingMatches(*freeScrollBinding_, authority)) {
        return request;
    }
    request.consumed = true;
    request.retiredBinding = std::move(freeScrollBinding_);
    freeScrollBinding_.reset();
    freeScrollRefreshDeferred_ = false;
    rightStickKinetics_.Reset();
    request.target = FindFreeScrollReentryTarget(
        authority.semantics->root,
        request.retiredBinding->scrollId,
        request.retiredBinding->axis,
        authority.semantics->activeInputScopeId,
        renderResult);
    return request;
}

WidgetInteractionPresentation WidgetInteractionSession::Presentation(
    const WidgetInteractionAuthority& authority) const noexcept {
    const bool exact = authority.semantics &&
        (!freeScrollBinding_ || BindingMatches(*freeScrollBinding_, authority));
    const auto pressedElement = authority.semantics
        ? pressed_.ActiveElementId(*authority.semantics, focusedElementId_)
        : std::wstring_view{};
    return {
        focusedElementId_,
        pressedElement,
        sliders_.presentationRevision(),
        exact && freeScrollBinding_.has_value(),
    };
}

void WidgetInteractionSession::RefreshSliderDeadline() noexcept {
    sliderReconcileAt_ = sliders_.NextReconcileDeadline().value_or(0);
}

bool WidgetInteractionSession::SliderReconcileDue(
    const std::uint64_t now) const noexcept {
    return sliderReconcileAt_ != 0 && now >= sliderReconcileAt_;
}

} // namespace widgetrail::input
