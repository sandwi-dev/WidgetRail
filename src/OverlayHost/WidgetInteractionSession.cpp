#include "WidgetInteractionSession.h"

#include <algorithm>
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
    if (retireSliderPresentations) {
        result.sliderDamageNodeIds = CurrentSliderNodeIds(
            snapshot, sliders_.DeactivateAll());
    }
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
    RefreshSliderDeadline();
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

SliderInputDescriptor WidgetInteractionSession::SliderDescriptor(
    const WidgetSnapshot& snapshot,
    const WidgetNode& node) noexcept {
    return {
        snapshot.instanceId,
        snapshot.activeInputScopeId,
        node.id,
        node.valueChangedActionId,
        snapshot.sequence,
        node.minimum,
        node.maximum,
        node.value,
        node.step,
        node.isDisabled,
        node.isBusy,
        node.sliderInteractionMode == L"activateToAdjust",
    };
}

std::vector<std::wstring> WidgetInteractionSession::CurrentSliderNodeIds(
    const WidgetSnapshot& snapshot,
    const std::vector<SliderPresentationIdentity>& identities) {
    std::vector<std::wstring> nodeIds;
    for (const auto& identity : identities) {
        if (identity.widgetInstanceId != snapshot.instanceId ||
            identity.inputScopeId != snapshot.activeInputScopeId ||
            identity.snapshotSequence != snapshot.sequence) {
            continue;
        }
        const auto* node = FindNodeInInputScope(
            snapshot, identity.nodeId, snapshot.activeInputScopeId);
        if (!node || node->kind != L"slider" ||
            node->valueChangedActionId != identity.valueChangedActionId ||
            std::find(nodeIds.begin(), nodeIds.end(), node->id) != nodeIds.end()) {
            continue;
        }
        nodeIds.push_back(node->id);
    }
    return nodeIds;
}

InteractionVisualRetirement WidgetInteractionSession::RetirePresentations() {
    InteractionVisualRetirement result{
        sliders_.DeactivateAll(),
        pressed_.Clear(),
    };
    RefreshSliderDeadline();
    return result;
}

void WidgetInteractionSession::ForgetRuntime(
    const std::wstring_view widgetInstanceId) noexcept {
    sliders_.ForgetWidget(widgetInstanceId);
    RefreshSliderDeadline();
}

InteractionReconciliation WidgetInteractionSession::ReconcileAdmission(
    const WidgetSnapshot& snapshot,
    const std::uint64_t now) {
    InteractionReconciliation result;
    const auto visit = [&](const auto& self, const WidgetNode& node) -> void {
        if (node.kind == L"slider" &&
            sliders_.Reconcile(SliderDescriptor(snapshot, node), now).visualChanged) {
            result.sliderDamageNodeIds.push_back(node.id);
        }
        for (const auto& child : node.children) self(self, child);
    };
    visit(visit, snapshot.root);
    RefreshSliderDeadline();
    result.nextDeadline = sliderReconcileAt_;
    return result;
}

bool WidgetInteractionSession::ReconcilePressedPresentation(
    const WidgetSnapshot& snapshot) noexcept {
    return pressed_.Reconcile(snapshot, focusedElementId_);
}

InteractionReconciliation WidgetInteractionSession::Tick(
    const WidgetSnapshot* snapshot,
    const std::uint64_t now) {
    InteractionReconciliation result;
    if (snapshot) result = ReconcileAdmission(*snapshot, now);
    const auto expired = sliders_.ExpireTimedOut(now);
    if (snapshot) {
        for (auto& nodeId : CurrentSliderNodeIds(*snapshot, expired)) {
            if (std::find(
                    result.sliderDamageNodeIds.begin(),
                    result.sliderDamageNodeIds.end(), nodeId) ==
                result.sliderDamageNodeIds.end()) {
                result.sliderDamageNodeIds.push_back(std::move(nodeId));
            }
        }
    }
    RefreshSliderDeadline();
    result.nextDeadline = sliderReconcileAt_;
    return result;
}

SliderInputOutcome WidgetInteractionSession::AdjustSlider(
    const SliderInputDescriptor& slider,
    const NavigationDirection direction,
    const std::uint64_t now) {
    const auto priorRevision = sliders_.presentationRevision();
    const auto adjustment = sliders_.Adjust(slider, direction, now);
    RefreshSliderDeadline();
    const auto request = adjustment.requestedValue
        ? std::optional<WidgetInteractionActionRequest>{
            WidgetInteractionActionRequest{
                std::wstring{slider.widgetInstanceId},
                std::wstring{slider.inputScopeId},
                std::wstring{slider.nodeId},
                std::wstring{slider.valueChangedActionId},
                slider.snapshotSequence,
                adjustment.requestedValue,
            }}
        : std::nullopt;
    return {
        adjustment.consumed,
        sliders_.presentationRevision() != priorRevision,
        request,
        sliders_.presentationRevision(),
        sliderReconcileAt_,
    };
}

SliderInputOutcome WidgetInteractionSession::RequestSliderValue(
    const SliderInputDescriptor& slider,
    const double value,
    const std::uint64_t now) {
    const auto priorRevision = sliders_.presentationRevision();
    const bool consumed = sliders_.SetRequestedValue(slider, value, now);
    RefreshSliderDeadline();
    const auto request = consumed
        ? std::optional<WidgetInteractionActionRequest>{
            WidgetInteractionActionRequest{
                std::wstring{slider.widgetInstanceId},
                std::wstring{slider.inputScopeId},
                std::wstring{slider.nodeId},
                std::wstring{slider.valueChangedActionId},
                slider.snapshotSequence,
                value,
            }}
        : std::nullopt;
    return {
        consumed,
        sliders_.presentationRevision() != priorRevision,
        request,
        sliders_.presentationRevision(),
        sliderReconcileAt_,
    };
}

SliderInputOutcome WidgetInteractionSession::CancelSliderAction(
    const SliderInputDescriptor& slider,
    const std::uint64_t now) {
    const auto priorRevision = sliders_.presentationRevision();
    const bool consumed = sliders_.CancelPending(slider, now);
    RefreshSliderDeadline();
    return {
        consumed,
        sliders_.presentationRevision() != priorRevision,
        std::nullopt,
        sliders_.presentationRevision(),
        sliderReconcileAt_,
    };
}

bool WidgetInteractionSession::SliderAdjustmentModeActive(
    const SliderInputDescriptor& slider,
    const std::uint64_t now) {
    return sliders_.AdjustmentModeActive(slider, now);
}

bool WidgetInteractionSession::TransitionSliderAdjustmentMode(
    const SliderInputDescriptor& slider,
    const SliderAdjustmentModeTransition transition,
    const std::uint64_t now) {
    const bool changed = transition == SliderAdjustmentModeTransition::Enter
        ? sliders_.EnterAdjustmentMode(slider, now)
        : sliders_.ExitAdjustmentMode(slider, now);
    (void)pressed_.Clear();
    RefreshSliderDeadline();
    return changed;
}

bool WidgetInteractionSession::TransitionPressedPresentation(
    const PressedInputTransition transition,
    const WidgetSnapshot* snapshot,
    const std::wstring_view focusedElementId,
    const std::wstring_view protocolButton) {
    switch (transition) {
    case PressedInputTransition::Begin:
        return snapshot &&
            pressed_.Begin(*snapshot, focusedElementId, protocolButton);
    case PressedInputTransition::End:
        return pressed_.Release(protocolButton);
    case PressedInputTransition::Cancel:
        return pressed_.Cancel(protocolButton);
    case PressedInputTransition::Clear:
        return pressed_.Clear();
    }
    return false;
}

InteractionRenderPresentation WidgetInteractionSession::PrepareRenderPresentation(
    const WidgetSnapshot& snapshot,
    const std::wstring_view renderedFocusId,
    const std::uint64_t now,
    const bool retainAdjustmentMode,
    const bool allowPressedPresentation,
    const bool allowAdjustmentModePresentation) {
    if (retainAdjustmentMode) {
        sliders_.RetainAdjustmentMode(
            snapshot.instanceId, snapshot.activeInputScopeId, renderedFocusId);
    }
    InteractionRenderPresentation result;
    const auto collect = [&](const auto& self, const WidgetNode& node) -> void {
        if (node.kind == L"slider") {
            if (const auto value = sliders_.PresentationValue(
                    SliderDescriptor(snapshot, node), now)) {
                result.sliderValueOverrides.emplace(node.id, *value);
            }
        }
        for (const auto& child : node.children) self(self, child);
    };
    collect(collect, snapshot.root);
    if (allowPressedPresentation) {
        result.pressedElementId = pressed_.ActiveElementId(
            snapshot, renderedFocusId);
        if (allowAdjustmentModePresentation &&
            result.pressedElementId.empty()) {
            const auto* focused = FindNodeInInputScope(
                snapshot, renderedFocusId, snapshot.activeInputScopeId);
            if (focused && focused->kind == L"slider" &&
                focused->sliderInteractionMode == L"activateToAdjust" &&
                sliders_.AdjustmentModeActive(
                    SliderDescriptor(snapshot, *focused), now)) {
                result.pressedElementId = focused->id;
            }
        }
    }
    result.sliderPresentationRevision = sliders_.presentationRevision();
    RefreshSliderDeadline();
    return result;
}

void WidgetInteractionSession::RefreshSliderDeadline() noexcept {
    sliderReconcileAt_ = sliders_.NextReconcileDeadline().value_or(0);
}

bool WidgetInteractionSession::SliderReconcileDue(
    const std::uint64_t now) const noexcept {
    return sliderReconcileAt_ != 0 && now >= sliderReconcileAt_;
}

} // namespace widgetrail::input
