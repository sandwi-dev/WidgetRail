#include "WidgetInteractionSession.h"

#include <algorithm>
#include <cmath>
#include <iterator>
#include <utility>

namespace widgetrail::input {
namespace {

std::wstring_view ScrollPaginationEdgeName(
    const ScrollPaginationEdge edge) noexcept {
    return edge == ScrollPaginationEdge::Before ? L"before" : L"after";
}

std::wstring_view ScrollPaginationDemandName(
    const ScrollPaginationDemandReason reason) noexcept {
    switch (reason) {
    case ScrollPaginationDemandReason::Initial: return L"initial";
    case ScrollPaginationDemandReason::Intent: return L"intent";
    case ScrollPaginationDemandReason::ThresholdReentry:
        return L"threshold-reentry";
    }
    return L"unknown";
}

std::wstring_view ScrollPaginationIntentName(
    const ScrollPaginationIntentSource source) noexcept {
    switch (source) {
    case ScrollPaginationIntentSource::None: return L"none";
    case ScrollPaginationIntentSource::RightStick: return L"right-stick";
    case ScrollPaginationIntentSource::DirectionalNavigation:
        return L"directional-navigation";
    case ScrollPaginationIntentSource::Pointer: return L"pointer";
    case ScrollPaginationIntentSource::Accessibility: return L"accessibility";
    }
    return L"unknown";
}

bool SameScrollPaginationRequest(
    const ScrollPaginationPrefetchRequest& left,
    const ScrollPaginationPrefetchRequest& right) noexcept {
    return left.widgetId == right.widgetId &&
        left.widgetInstanceId == right.widgetInstanceId &&
        left.runtimeGeneration == right.runtimeGeneration &&
        left.presentationGeneration == right.presentationGeneration &&
        left.inputScopeId == right.inputScopeId &&
        left.action.scrollId == right.action.scrollId &&
        left.action.actionId == right.action.actionId &&
        left.action.edge == right.action.edge &&
        left.action.edgeKey == right.action.edgeKey &&
        left.demandGeneration == right.demandGeneration;
}

} // namespace

std::wstring FormatScrollPaginationDiagnostic(
    const ScrollPaginationDiagnostic& diagnostic) {
    const auto& request = diagnostic.request;
    const auto& action = request.action;
    std::wstring message = L"Scroll pagination ";
    switch (diagnostic.kind) {
    case ScrollPaginationDiagnosticKind::Queued: message += L"queued"; break;
    case ScrollPaginationDiagnosticKind::Suppressed: message += L"suppressed"; break;
    case ScrollPaginationDiagnosticKind::Admitted: message += L"admitted"; break;
    case ScrollPaginationDiagnosticKind::Completed: message += L"completed"; break;
    case ScrollPaginationDiagnosticKind::Retired: message += L"authority retired"; break;
    case ScrollPaginationDiagnosticKind::TerminalFailure:
        message += L"terminal failure";
        break;
    }
    message += L" widget=" + request.widgetId + L" scroll=" + action.scrollId +
        L" visible=" + std::to_wstring(action.firstVisibleIndex) + L"-" +
        std::to_wstring(action.lastVisibleIndex) + L"/" +
        std::to_wstring(action.itemCount) + L" direction=" +
        std::wstring{ScrollPaginationEdgeName(action.edge)} + L" edge=" +
        action.edgeKey + L" anchor=" +
        (action.anchorKey.empty() ? L"none" : action.anchorKey) +
        L" demand=" +
        std::wstring{ScrollPaginationDemandName(request.demandReason)} +
        L" intent-source=" +
        std::wstring{ScrollPaginationIntentName(request.intentSource)} +
        L" demand-generation=" +
        std::to_wstring(request.demandGeneration);
    if (!diagnostic.replacementEdgeKey.empty())
        message += L" replacement-edge=" + diagnostic.replacementEdgeKey;
    if (!diagnostic.reason.empty()) message += L" reason=" + diagnostic.reason;
    if (diagnostic.kind == ScrollPaginationDiagnosticKind::Admitted) {
        message += L" action=" + action.actionId + L" count=" +
            std::to_wstring(diagnostic.adjacentActionCount) +
            L" queue-latency-ms=" +
            std::to_wstring(diagnostic.queueLatencyMilliseconds);
    }
    if (diagnostic.kind == ScrollPaginationDiagnosticKind::Completed) {
        message += L" threshold-to-visible-page-ms=" +
            std::to_wstring(diagnostic.thresholdToVisibleMilliseconds) +
            L" actions=" + std::to_wstring(diagnostic.adjacentActionCount) +
            L" visible-completions=" +
            std::to_wstring(diagnostic.visibleCompletionCount) +
            L" average-visible-latency-ms=" +
            std::to_wstring(diagnostic.averageVisibleLatencyMilliseconds);
    }
    return message;
}

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

std::wstring WidgetInteractionSession::FocusRestoreCandidate(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) const {
    return focusMemory_.Restore(widgetId, snapshot);
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

const WidgetNode* WidgetInteractionSession::ExactSliderNode(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node) noexcept {
    if (!authority.semantics || node.kind != L"slider") return nullptr;
    const auto* admitted = FindNodeInInputScope(
        *authority.semantics, node.id, authority.semantics->activeInputScopeId);
    return admitted == &node ? admitted : nullptr;
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
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const NavigationDirection direction,
    const std::uint64_t now) {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return {};
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
    const auto priorRevision = sliders_.presentationRevision();
    const auto adjustment = sliders_.Adjust(slider, direction, now);
    RefreshSliderDeadline();
    const auto request = adjustment.requestedValue
        ? std::optional<WidgetInteractionActionRequest>{
            WidgetInteractionActionRequest{
                std::wstring{authority.widgetId},
                std::wstring{slider.widgetInstanceId},
                std::wstring{authority.runtimeGeneration},
                std::wstring{authority.presentationGeneration},
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
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const double value,
    const std::uint64_t now) {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return {};
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
    const auto priorRevision = sliders_.presentationRevision();
    const bool consumed = sliders_.SetRequestedValue(slider, value, now);
    RefreshSliderDeadline();
    const auto request = consumed
        ? std::optional<WidgetInteractionActionRequest>{
            WidgetInteractionActionRequest{
                std::wstring{authority.widgetId},
                std::wstring{slider.widgetInstanceId},
                std::wstring{authority.runtimeGeneration},
                std::wstring{authority.presentationGeneration},
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
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const std::uint64_t now) {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return {};
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
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

SliderInputOutcome WidgetInteractionSession::CancelSliderAction(
    const WidgetInteractionActionRequest& request,
    const std::uint64_t now) {
    const auto priorRevision = sliders_.presentationRevision();
    const bool consumed = sliders_.CancelPending(SliderPresentationIdentity{
        request.widgetInstanceId,
        request.inputScopeId,
        request.sourceElementId,
        request.actionId,
        request.snapshotSequence,
    }, now);
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
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const std::uint64_t now) {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return false;
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
    return sliders_.AdjustmentModeActive(slider, now);
}

bool WidgetInteractionSession::TransitionSliderAdjustmentMode(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const SliderAdjustmentModeTransition transition,
    const std::uint64_t now) {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return false;
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
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

ScrollPaginationPrefetchRequest
WidgetInteractionSession::MakeScrollPaginationRequest(
    const WidgetInteractionAuthority& authority,
    const ScrollPaginationAction& action,
    const ScrollPaginationDemandReason reason,
    const ScrollPaginationIntentSource source,
    const std::uint64_t demandGeneration) {
    return {
        std::wstring{authority.widgetId},
        authority.semantics ? authority.semantics->instanceId : std::wstring{},
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        authority.semantics
            ? authority.semantics->activeInputScopeId : std::wstring{},
        action,
        reason,
        source,
        demandGeneration,
    };
}

bool WidgetInteractionSession::SameScrollPaginationRoute(
    const ScrollPaginationDemandLatch& latch,
    const WidgetInteractionAuthority& authority) noexcept {
    return authority.semantics &&
        latch.widgetId == authority.widgetId &&
        latch.widgetInstanceId == authority.semantics->instanceId &&
        latch.runtimeGeneration == authority.runtimeGeneration &&
        latch.presentationGeneration == authority.presentationGeneration &&
        latch.inputScopeId == authority.semantics->activeInputScopeId;
}

bool WidgetInteractionSession::SameScrollPaginationAuthority(
    const ScrollPaginationDemandLatch& latch,
    const WidgetInteractionAuthority& current,
    const ScrollPaginationAction& action) noexcept {
    if (!latch.prefetch || !SameScrollPaginationRoute(latch, current))
        return false;
    const auto& request = latch.prefetch->request;
    return request.action.scrollId == action.scrollId &&
        request.action.actionId == action.actionId &&
        request.action.edge == action.edge &&
        request.action.edgeKey == action.edgeKey;
}

bool WidgetInteractionSession::SameScrollPaginationRouteEdge(
    const ScrollPaginationDemandLatch& latch,
    const WidgetInteractionAuthority& current,
    const ScrollPaginationAction& action) noexcept {
    if (!latch.prefetch || !SameScrollPaginationRoute(latch, current))
        return false;
    const auto& request = latch.prefetch->request;
    return request.action.scrollId == action.scrollId &&
        request.action.actionId == action.actionId &&
        request.action.edge == action.edge;
}

ScrollPaginationSessionOutcome
WidgetInteractionSession::ObserveScrollPaginationIntent(
    const WidgetInteractionAuthority& authority,
    const std::wstring_view scrollId,
    const declarative::ScrollAxis axis,
    const ScrollPaginationEdge edge,
    const ScrollPaginationIntentSource source,
    const std::uint64_t) {
    constexpr std::size_t maximumLatches = 16;
    ScrollPaginationSessionOutcome outcome;
    if (!authority.semantics || axis == declarative::ScrollAxis::None)
        return outcome;

    if (scrollId.empty()) return outcome;
    auto latch = std::ranges::find_if(
        scrollPaginationLatches_, [&](const auto& candidate) {
            return SameScrollPaginationRoute(candidate, authority) &&
                candidate.scrollId == scrollId && candidate.axis == axis;
        });
    if (latch == scrollPaginationLatches_.end() &&
        scrollPaginationLatches_.size() < maximumLatches) {
        scrollPaginationLatches_.push_back({
            std::wstring{authority.widgetId},
            authority.semantics->instanceId,
            std::wstring{authority.runtimeGeneration},
            std::wstring{authority.presentationGeneration},
            authority.semantics->activeInputScopeId,
            std::wstring{scrollId},
            axis,
        });
        latch = std::prev(scrollPaginationLatches_.end());
    }
    if (latch == scrollPaginationLatches_.end()) return outcome;

    latch->pendingIntentEdge = edge;
    latch->pendingIntentSource = source;
    latch->pendingIntentGeneration = ++scrollPaginationDemandGeneration_;
    if (latch->prefetch &&
        latch->prefetch->status == ScrollPaginationPrefetchStatus::TerminalFailure &&
        latch->prefetch->request.action.edge == edge) {
        outcome.diagnostics.push_back({
            ScrollPaginationDiagnosticKind::Retired,
            latch->prefetch->request,
            L"new-user-intent",
        });
        latch->prefetch.reset();
    }
    return outcome;
}

ScrollPaginationSessionOutcome
WidgetInteractionSession::ObserveScrollPaginationFocusIntent(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult,
    const std::wstring_view priorFocus,
    const std::wstring_view nextFocus,
    const ScrollPaginationIntentSource source,
    const std::uint64_t now) {
    if (priorFocus.empty() || nextFocus.empty() || priorFocus == nextFocus)
        return {};
    const auto prior = renderResult.navigationRects.find(
        std::wstring{priorFocus});
    const auto next = renderResult.navigationRects.find(
        std::wstring{nextFocus});
    if (prior == renderResult.navigationRects.end() ||
        next == renderResult.navigationRects.end()) return {};
    const float deltaX =
        (next->second.x + next->second.width * 0.5F) -
        (prior->second.x + prior->second.width * 0.5F);
    const float deltaY =
        (next->second.y + next->second.height * 0.5F) -
        (prior->second.y + prior->second.height * 0.5F);
    constexpr float intentEpsilon = 0.5F;
    if (std::abs(deltaX) <= intentEpsilon &&
        std::abs(deltaY) <= intentEpsilon) return {};
    const bool horizontal = std::abs(deltaX) > std::abs(deltaY);
    const auto axis = horizontal
        ? declarative::ScrollAxis::Horizontal
        : declarative::ScrollAxis::Vertical;
    const auto owner = ResolveFocusedScrollOwner(
        authority.semantics->root, nextFocus, axis,
        authority.semantics->activeInputScopeId, renderResult);
    if (owner.disposition != FocusedScrollResolutionDisposition::Resolved)
        return {};
    return ObserveScrollPaginationIntent(
        authority, owner.scrollId, axis,
        (horizontal ? deltaX : deltaY) < 0.0F
            ? ScrollPaginationEdge::Before
            : ScrollPaginationEdge::After,
        source, now);
}

ScrollPaginationBoundaryOutcome
WidgetInteractionSession::ObserveScrollPaginationBoundaryIntent(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult,
    const std::wstring_view focusedElementId,
    const NavigationDirection direction,
    const ScrollPaginationIntentSource source,
    const std::uint64_t now) {
    ScrollPaginationBoundaryOutcome result;
    if (!authority.semantics || focusedElementId.empty()) return result;
    const bool horizontal = direction == NavigationDirection::Left ||
        direction == NavigationDirection::Right;
    const bool vertical = direction == NavigationDirection::Up ||
        direction == NavigationDirection::Down;
    if (!horizontal && !vertical) return result;
    const auto axis = horizontal
        ? declarative::ScrollAxis::Horizontal
        : declarative::ScrollAxis::Vertical;
    const auto edge = direction == NavigationDirection::Left ||
            direction == NavigationDirection::Up
        ? ScrollPaginationEdge::Before
        : ScrollPaginationEdge::After;
    const auto owner = ResolveFocusedScrollOwner(
        authority.semantics->root, focusedElementId, axis,
        authority.semantics->activeInputScopeId, renderResult);
    if (owner.disposition != FocusedScrollResolutionDisposition::Resolved)
        return result;

    const auto actions = FindScrollPaginationActions(
        authority.semantics->root,
        authority.semantics->activeInputScopeId,
        renderResult);
    const auto action = std::ranges::find_if(actions, [&](const auto& item) {
        return item.scrollId == owner.scrollId && item.axis == axis &&
            item.edge == edge;
    });
    const bool matchingFlight = std::ranges::any_of(
        scrollPaginationLatches_, [&](const auto& latch) {
            return SameScrollPaginationRoute(latch, authority) &&
                latch.scrollId == owner.scrollId && latch.axis == axis &&
                latch.prefetch && latch.prefetch->request.action.edge == edge &&
                latch.prefetch->status !=
                    ScrollPaginationPrefetchStatus::TerminalFailure;
        });
    if (action == actions.end()) {
        result.retainFocus = matchingFlight;
        return result;
    }

    result.retainFocus = true;
    result.pagination = ObserveScrollPaginationIntent(
        authority, owner.scrollId, axis, edge, source, now);
    auto reconciled = ReconcileScrollPagination(authority, renderResult, now);
    result.pagination.dispatchReady =
        result.pagination.dispatchReady || reconciled.dispatchReady;
    result.pagination.diagnostics.insert(
        result.pagination.diagnostics.end(),
        std::make_move_iterator(reconciled.diagnostics.begin()),
        std::make_move_iterator(reconciled.diagnostics.end()));
    return result;
}

ScrollPaginationSessionOutcome WidgetInteractionSession::ReconcileScrollPagination(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult,
    const std::uint64_t now) {
    constexpr std::size_t maximumLatches = 16;
    ScrollPaginationSessionOutcome outcome;
    if (!authority.semantics) return outcome;
    const auto actions = FindScrollPaginationActions(
        authority.semantics->root,
        authority.semantics->activeInputScopeId,
        renderResult);
    bool routeChanged{};
    std::erase_if(scrollPaginationLatches_, [&](const auto& latch) {
        const bool currentRoute = SameScrollPaginationRoute(latch, authority);
        const bool currentScroll = currentRoute &&
            renderResult.scrollViewports.contains(latch.scrollId);
        if (currentScroll) return false;
        if (latch.prefetch) {
            outcome.diagnostics.push_back({
                ScrollPaginationDiagnosticKind::Retired,
                latch.prefetch->request,
                currentRoute ? L"scroll-removed" : L"route-changed",
            });
        }
        routeChanged = routeChanged || !currentRoute;
        return true;
    });
    if (routeChanged && scrollPaginationLatches_.empty())
        scrollPaginationRouteObserved_ = false;

    // Establish one latch per rendered Scroll, never one per changing cursor.
    for (const auto& action : actions) {
        const auto existing = std::ranges::find_if(
            scrollPaginationLatches_, [&](const auto& latch) {
                return SameScrollPaginationRoute(latch, authority) &&
                    latch.scrollId == action.scrollId;
            });
        if (existing != scrollPaginationLatches_.end()) continue;
        if (scrollPaginationLatches_.size() >= maximumLatches) {
            outcome.diagnostics.push_back({
                ScrollPaginationDiagnosticKind::Suppressed,
                MakeScrollPaginationRequest(
                    authority, action, ScrollPaginationDemandReason::Initial,
                    ScrollPaginationIntentSource::None, 0),
                L"authority-bound",
            });
            continue;
        }
        scrollPaginationLatches_.push_back({
            std::wstring{authority.widgetId},
            authority.semantics->instanceId,
            std::wstring{authority.runtimeGeneration},
            std::wstring{authority.presentationGeneration},
            authority.semantics->activeInputScopeId,
            action.scrollId,
            action.axis,
        });
    }
    for (const auto& [scrollId, viewport] : renderResult.scrollViewports) {
        if (viewport.axis == declarative::ScrollAxis::None ||
            scrollPaginationLatches_.size() >= maximumLatches) continue;
        const auto existing = std::ranges::find_if(
            scrollPaginationLatches_, [&](const auto& latch) {
                return SameScrollPaginationRoute(latch, authority) &&
                    latch.scrollId == scrollId;
            });
        if (existing != scrollPaginationLatches_.end()) continue;
        scrollPaginationLatches_.push_back({
            std::wstring{authority.widgetId},
            authority.semantics->instanceId,
            std::wstring{authority.runtimeGeneration},
            std::wstring{authority.presentationGeneration},
            authority.semantics->activeInputScopeId,
            scrollId,
            viewport.axis,
        });
    }

    const bool initialRouteObservation = !scrollPaginationRouteObserved_;
    bool initialQueued{};
    for (auto& latch : scrollPaginationLatches_) {
        if (!SameScrollPaginationRoute(latch, authority)) continue;
        const auto before = std::ranges::find_if(actions, [&](const auto& action) {
            return action.scrollId == latch.scrollId &&
                action.edge == ScrollPaginationEdge::Before;
        });
        const auto after = std::ranges::find_if(actions, [&](const auto& action) {
            return action.scrollId == latch.scrollId &&
                action.edge == ScrollPaginationEdge::After;
        });
        const bool beforeResident = before != actions.end();
        const bool afterResident = after != actions.end();
        const bool firstObservation = !latch.initialized;
        const bool residenceChanged = latch.initialized &&
            (latch.beforeResident != beforeResident ||
             latch.afterResident != afterResident);
        const auto actionFor = [&](const ScrollPaginationEdge edge)
            -> const ScrollPaginationAction* {
            const auto found = edge == ScrollPaginationEdge::Before
                ? before : after;
            return found == actions.end() ? nullptr : &*found;
        };
        const auto wasResident = [&](const ScrollPaginationEdge edge) {
            return edge == ScrollPaginationEdge::Before
                ? latch.beforeResident : latch.afterResident;
        };
        const auto isResident = [&](const ScrollPaginationEdge edge) {
            return edge == ScrollPaginationEdge::Before
                ? beforeResident : afterResident;
        };

        if (latch.prefetch) {
            const auto exact = std::ranges::find_if(actions, [&](const auto& action) {
                return SameScrollPaginationAuthority(latch, authority, action);
            });
            if (exact == actions.end()) {
                const auto replacement = std::ranges::find_if(
                    actions, [&](const auto& action) {
                        return SameScrollPaginationRouteEdge(
                            latch, authority, action);
                    });
                ScrollPaginationDiagnostic diagnostic;
                diagnostic.request = latch.prefetch->request;
                if (latch.prefetch->status ==
                    ScrollPaginationPrefetchStatus::InFlight) {
                    const auto latency = now - latch.prefetch->thresholdAt;
                    ++scrollPaginationVisibleCompletionCount_;
                    scrollPaginationVisibleLatencyTotalMs_ += latency;
                    diagnostic.kind = ScrollPaginationDiagnosticKind::Completed;
                    if (replacement != actions.end())
                        diagnostic.replacementEdgeKey = replacement->edgeKey;
                    diagnostic.reason = replacement != actions.end()
                        ? L"requested-edge-changed"
                        : L"requested-edge-unavailable";
                    diagnostic.adjacentActionCount =
                        scrollPaginationAdjacentActionCount_;
                    diagnostic.visibleCompletionCount =
                        scrollPaginationVisibleCompletionCount_;
                    diagnostic.thresholdToVisibleMilliseconds = latency;
                    diagnostic.averageVisibleLatencyMilliseconds =
                        scrollPaginationVisibleLatencyTotalMs_ /
                        scrollPaginationVisibleCompletionCount_;
                } else {
                    diagnostic.kind = ScrollPaginationDiagnosticKind::Suppressed;
                    diagnostic.reason = L"viewport-changed-before-dispatch";
                }
                outcome.diagnostics.push_back(std::move(diagnostic));
                latch.prefetch.reset();
            } else if (latch.prefetch->status !=
                           ScrollPaginationPrefetchStatus::Queued &&
                       !latch.prefetch->suppressionReported) {
                outcome.diagnostics.push_back({
                    ScrollPaginationDiagnosticKind::Suppressed,
                    latch.prefetch->request,
                    latch.prefetch->status ==
                            ScrollPaginationPrefetchStatus::InFlight
                        ? L"in-flight" : L"terminal-failure",
                });
                latch.prefetch->suppressionReported = true;
            }
        }

        const auto queue = [&](const ScrollPaginationAction& action,
                               const ScrollPaginationDemandReason reason,
                               const ScrollPaginationIntentSource source,
                               const std::uint64_t generation) {
            auto request = MakeScrollPaginationRequest(
                authority, action, reason, source, generation);
            latch.prefetch = ScrollPaginationPrefetchAuthority{
                request, ScrollPaginationPrefetchStatus::Queued,
                now, 0, false};
            latch.latchedEdge = action.edge;
            latch.lastDemandGeneration = generation;
            if (latch.pendingIntentGeneration <= generation) {
                latch.pendingIntentEdge.reset();
                latch.pendingIntentGeneration = 0;
            }
            latch.reconciliationSuppressionReported = false;
            outcome.diagnostics.push_back({
                ScrollPaginationDiagnosticKind::Queued, std::move(request)});
            outcome.dispatchReady = true;
        };

        if (!latch.prefetch && latch.pendingIntentEdge &&
            latch.pendingIntentGeneration > latch.lastDemandGeneration) {
            const auto intentEdge = *latch.pendingIntentEdge;
            const auto* candidate = actionFor(intentEdge);
            const bool sameDirection = !latch.latchedEdge ||
                *latch.latchedEdge == intentEdge ||
                !isResident(*latch.latchedEdge);
            if (candidate && sameDirection) {
                queue(
                    *candidate,
                    wasResident(intentEdge)
                        ? ScrollPaginationDemandReason::Intent
                        : ScrollPaginationDemandReason::ThresholdReentry,
                    latch.pendingIntentSource,
                    latch.pendingIntentGeneration);
            }
        }

        if (!latch.prefetch && firstObservation && initialRouteObservation &&
            !initialQueued &&
            !latch.pendingIntentEdge) {
            if (beforeResident != afterResident) {
                const auto* candidate = beforeResident ? &*before : &*after;
                queue(
                    *candidate,
                    ScrollPaginationDemandReason::Initial,
                    ScrollPaginationIntentSource::None,
                    ++scrollPaginationDemandGeneration_);
                initialQueued = true;
            } else if (beforeResident && afterResident) {
                outcome.diagnostics.push_back({
                    ScrollPaginationDiagnosticKind::Suppressed,
                    MakeScrollPaginationRequest(
                        authority, *before,
                        ScrollPaginationDemandReason::Initial,
                        ScrollPaginationIntentSource::None, 0),
                    L"ambiguous-initial",
                });
            }
        } else if (!latch.prefetch && residenceChanged &&
                   !latch.pendingIntentEdge &&
                   !latch.reconciliationSuppressionReported) {
            const auto* diagnosticAction = beforeResident
                ? &*before : afterResident ? &*after : nullptr;
            if (diagnosticAction) {
                outcome.diagnostics.push_back({
                    ScrollPaginationDiagnosticKind::Suppressed,
                    MakeScrollPaginationRequest(
                        authority, *diagnosticAction,
                        ScrollPaginationDemandReason::Initial,
                        ScrollPaginationIntentSource::None, 0),
                    L"reconciliation-no-rearm",
                });
                latch.reconciliationSuppressionReported = true;
            }
        }
        if (!residenceChanged) latch.reconciliationSuppressionReported = false;
        latch.initialized = true;
        latch.beforeResident = beforeResident;
        latch.afterResident = afterResident;
    }
    scrollPaginationRouteObserved_ = true;
    return outcome;
}

std::pair<std::optional<ScrollPaginationPrefetchRequest>,
          ScrollPaginationSessionOutcome>
WidgetInteractionSession::AcquireScrollPaginationDispatch(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult,
    const std::uint64_t now) {
    ScrollPaginationSessionOutcome outcome;
    if (!authority.semantics) return {std::nullopt, std::move(outcome)};
    const auto actions = FindScrollPaginationActions(
        authority.semantics->root,
        authority.semantics->activeInputScopeId,
        renderResult);
    for (auto& latch : scrollPaginationLatches_) {
        if (!latch.prefetch || latch.prefetch->status !=
            ScrollPaginationPrefetchStatus::Queued) continue;
        auto& entry = *latch.prefetch;
        const bool current = std::ranges::any_of(actions, [&](const auto& action) {
            return SameScrollPaginationAuthority(latch, authority, action);
        });
        if (!current) {
            entry.status = ScrollPaginationPrefetchStatus::TerminalFailure;
            outcome.diagnostics.push_back({
                ScrollPaginationDiagnosticKind::Suppressed,
                entry.request,
                L"viewport-changed-before-dispatch",
            });
            continue;
        }
        entry.status = ScrollPaginationPrefetchStatus::InFlight;
        entry.admittedAt = now;
        ++scrollPaginationAdjacentActionCount_;
        return {entry.request, std::move(outcome)};
    }
    return {std::nullopt, std::move(outcome)};
}

ScrollPaginationSessionOutcome
WidgetInteractionSession::CompleteScrollPaginationDispatch(
    const ScrollPaginationDispatchOutcome& dispatch) {
    ScrollPaginationSessionOutcome outcome;
    const auto latch = std::ranges::find_if(
        scrollPaginationLatches_, [&](const auto& candidate) {
            return candidate.prefetch &&
                candidate.prefetch->status ==
                    ScrollPaginationPrefetchStatus::InFlight &&
                SameScrollPaginationRequest(
                    candidate.prefetch->request, dispatch.request);
        });
    if (latch == scrollPaginationLatches_.end()) return outcome;
    auto& entry = *latch->prefetch;
    ScrollPaginationDiagnostic diagnostic;
    diagnostic.request = entry.request;
    diagnostic.adjacentActionCount = scrollPaginationAdjacentActionCount_;
    diagnostic.queueLatencyMilliseconds = entry.admittedAt - entry.thresholdAt;
    if (dispatch.disposition == ScrollPaginationDispatchDisposition::Admitted) {
        diagnostic.kind = ScrollPaginationDiagnosticKind::Admitted;
    } else {
        entry.status = ScrollPaginationPrefetchStatus::TerminalFailure;
        diagnostic.kind = dispatch.disposition ==
                ScrollPaginationDispatchDisposition::StaleAuthority
            ? ScrollPaginationDiagnosticKind::Suppressed
            : ScrollPaginationDiagnosticKind::TerminalFailure;
        diagnostic.reason = dispatch.safeDiagnostic;
    }
    outcome.diagnostics.push_back(std::move(diagnostic));
    return outcome;
}

ScrollPaginationSessionOutcome
WidgetInteractionSession::ObserveScrollPaginationFailure(
    const WidgetActionFailure& failure,
    const std::uint64_t) {
    ScrollPaginationSessionOutcome outcome;
    const auto latch = std::ranges::find_if(
        scrollPaginationLatches_, [&](const auto& candidate) {
            return candidate.prefetch &&
                candidate.prefetch->status ==
                    ScrollPaginationPrefetchStatus::InFlight &&
                candidate.prefetch->request.widgetId == failure.widgetId &&
                candidate.prefetch->request.runtimeGeneration ==
                    failure.runtimeGeneration &&
                candidate.prefetch->request.action.actionId == failure.actionId &&
                candidate.prefetch->request.action.sourceElementId ==
                    failure.sourceElementId;
        });
    if (latch == scrollPaginationLatches_.end()) return outcome;
    auto& entry = *latch->prefetch;
    entry.status = ScrollPaginationPrefetchStatus::TerminalFailure;
    outcome.diagnostics.push_back({
        ScrollPaginationDiagnosticKind::TerminalFailure,
        entry.request,
        L"worker-action-failure",
    });
    return outcome;
}

ScrollPaginationSessionOutcome WidgetInteractionSession::RetireScrollPagination(
    const std::wstring_view widgetId,
    const std::wstring_view reason) {
    ScrollPaginationSessionOutcome outcome;
    std::erase_if(scrollPaginationLatches_, [&](const auto& latch) {
        if (!widgetId.empty() && latch.widgetId != widgetId) return false;
        if (latch.prefetch) {
            outcome.diagnostics.push_back({
                ScrollPaginationDiagnosticKind::Retired,
                latch.prefetch->request,
                std::wstring{reason},
            });
        }
        return true;
    });
    if (scrollPaginationLatches_.empty())
        scrollPaginationRouteObserved_ = false;
    return outcome;
}

} // namespace widgetrail::input
