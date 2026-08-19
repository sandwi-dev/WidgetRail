#include "WidgetInteractionSession.h"

#include <algorithm>
#include <utility>

namespace widgetrail::input {
namespace {

std::wstring_view ScrollPaginationEdgeName(
    const ScrollPaginationEdge edge) noexcept {
    return edge == ScrollPaginationEdge::Before ? L"before" : L"after";
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
        left.action.edgeKey == right.action.edgeKey;
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
        (action.anchorKey.empty() ? L"none" : action.anchorKey);
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
    const ScrollPaginationAction& action) {
    return {
        std::wstring{authority.widgetId},
        authority.semantics ? authority.semantics->instanceId : std::wstring{},
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        authority.semantics
            ? authority.semantics->activeInputScopeId : std::wstring{},
        action,
    };
}

bool WidgetInteractionSession::SameScrollPaginationAuthority(
    const ScrollPaginationPrefetchAuthority& authority,
    const WidgetInteractionAuthority& current,
    const ScrollPaginationAction& action) noexcept {
    return current.semantics &&
        authority.request.widgetId == current.widgetId &&
        authority.request.widgetInstanceId == current.semantics->instanceId &&
        authority.request.runtimeGeneration == current.runtimeGeneration &&
        authority.request.presentationGeneration == current.presentationGeneration &&
        authority.request.inputScopeId == current.semantics->activeInputScopeId &&
        authority.request.action.scrollId == action.scrollId &&
        authority.request.action.actionId == action.actionId &&
        authority.request.action.edge == action.edge &&
        authority.request.action.edgeKey == action.edgeKey;
}

bool WidgetInteractionSession::SameScrollPaginationRouteEdge(
    const ScrollPaginationPrefetchAuthority& authority,
    const WidgetInteractionAuthority& current,
    const ScrollPaginationAction& action) noexcept {
    return current.semantics &&
        authority.request.widgetId == current.widgetId &&
        authority.request.widgetInstanceId == current.semantics->instanceId &&
        authority.request.runtimeGeneration == current.runtimeGeneration &&
        authority.request.presentationGeneration == current.presentationGeneration &&
        authority.request.inputScopeId == current.semantics->activeInputScopeId &&
        authority.request.action.scrollId == action.scrollId &&
        authority.request.action.actionId == action.actionId &&
        authority.request.action.edge == action.edge;
}

ScrollPaginationSessionOutcome WidgetInteractionSession::ReconcileScrollPagination(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult,
    const std::uint64_t now) {
    constexpr std::size_t maximumAuthorities = 16;
    ScrollPaginationSessionOutcome outcome;
    if (!authority.semantics) return outcome;
    const auto actions = FindScrollPaginationActions(
        authority.semantics->root,
        authority.semantics->activeInputScopeId,
        renderResult);
    std::erase_if(scrollPaginationPrefetch_, [&](const auto& entry) {
        const auto exact = std::ranges::find_if(actions, [&](const auto& action) {
            return SameScrollPaginationAuthority(entry, authority, action);
        });
        if (exact != actions.end()) return false;
        const auto replacement = std::ranges::find_if(actions, [&](const auto& action) {
            return SameScrollPaginationRouteEdge(entry, authority, action);
        });
        ScrollPaginationDiagnostic diagnostic;
        diagnostic.request = entry.request;
        if (replacement != actions.end() &&
            entry.status == ScrollPaginationPrefetchStatus::InFlight) {
            const auto latency = now - entry.thresholdAt;
            ++scrollPaginationVisibleCompletionCount_;
            scrollPaginationVisibleLatencyTotalMs_ += latency;
            diagnostic.kind = ScrollPaginationDiagnosticKind::Completed;
            diagnostic.replacementEdgeKey = replacement->edgeKey;
            diagnostic.adjacentActionCount = scrollPaginationAdjacentActionCount_;
            diagnostic.visibleCompletionCount =
                scrollPaginationVisibleCompletionCount_;
            diagnostic.thresholdToVisibleMilliseconds = latency;
            diagnostic.averageVisibleLatencyMilliseconds =
                scrollPaginationVisibleLatencyTotalMs_ /
                scrollPaginationVisibleCompletionCount_;
        } else {
            diagnostic.kind = ScrollPaginationDiagnosticKind::Retired;
            diagnostic.reason = replacement != actions.end()
                ? L"cursor-changed"
                : entry.request.widgetId == authority.widgetId
                    ? L"threshold-exit" : L"route-changed";
        }
        outcome.diagnostics.push_back(std::move(diagnostic));
        return true;
    });

    for (const auto& action : actions) {
        const auto existing = std::ranges::find_if(
            scrollPaginationPrefetch_, [&](const auto& entry) {
                return SameScrollPaginationAuthority(entry, authority, action);
            });
        if (existing != scrollPaginationPrefetch_.end()) {
            existing->request.action = action;
            if (existing->status != ScrollPaginationPrefetchStatus::Queued &&
                !existing->suppressionReported) {
                outcome.diagnostics.push_back({
                    ScrollPaginationDiagnosticKind::Suppressed,
                    existing->request,
                    existing->status == ScrollPaginationPrefetchStatus::InFlight
                        ? L"in-flight" : L"terminal-failure",
                });
                existing->suppressionReported = true;
            }
            continue;
        }
        if (scrollPaginationPrefetch_.size() >= maximumAuthorities) {
            outcome.diagnostics.push_back({
                ScrollPaginationDiagnosticKind::Suppressed,
                MakeScrollPaginationRequest(authority, action),
                L"authority-bound",
            });
            continue;
        }
        auto request = MakeScrollPaginationRequest(authority, action);
        scrollPaginationPrefetch_.push_back({
            request, ScrollPaginationPrefetchStatus::Queued, now, 0, false});
        outcome.diagnostics.push_back({
            ScrollPaginationDiagnosticKind::Queued, std::move(request)});
        outcome.dispatchReady = true;
    }
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
    for (auto& entry : scrollPaginationPrefetch_) {
        if (entry.status != ScrollPaginationPrefetchStatus::Queued) continue;
        const bool current = std::ranges::any_of(actions, [&](const auto& action) {
            return SameScrollPaginationAuthority(entry, authority, action);
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
    const auto entry = std::ranges::find_if(
        scrollPaginationPrefetch_, [&](const auto& candidate) {
            return candidate.status == ScrollPaginationPrefetchStatus::InFlight &&
                SameScrollPaginationRequest(candidate.request, dispatch.request);
        });
    if (entry == scrollPaginationPrefetch_.end()) return outcome;
    ScrollPaginationDiagnostic diagnostic;
    diagnostic.request = entry->request;
    diagnostic.adjacentActionCount = scrollPaginationAdjacentActionCount_;
    diagnostic.queueLatencyMilliseconds = entry->admittedAt - entry->thresholdAt;
    if (dispatch.disposition == ScrollPaginationDispatchDisposition::Admitted) {
        diagnostic.kind = ScrollPaginationDiagnosticKind::Admitted;
    } else {
        entry->status = ScrollPaginationPrefetchStatus::TerminalFailure;
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
    const auto entry = std::ranges::find_if(
        scrollPaginationPrefetch_, [&](const auto& candidate) {
            return candidate.status == ScrollPaginationPrefetchStatus::InFlight &&
                candidate.request.widgetId == failure.widgetId &&
                candidate.request.runtimeGeneration == failure.runtimeGeneration &&
                candidate.request.action.actionId == failure.actionId &&
                candidate.request.action.sourceElementId == failure.sourceElementId;
        });
    if (entry == scrollPaginationPrefetch_.end()) return outcome;
    entry->status = ScrollPaginationPrefetchStatus::TerminalFailure;
    outcome.diagnostics.push_back({
        ScrollPaginationDiagnosticKind::TerminalFailure,
        entry->request,
        L"worker-action-failure",
    });
    return outcome;
}

ScrollPaginationSessionOutcome WidgetInteractionSession::RetireScrollPagination(
    const std::wstring_view widgetId,
    const std::wstring_view reason) {
    ScrollPaginationSessionOutcome outcome;
    std::erase_if(scrollPaginationPrefetch_, [&](const auto& entry) {
        if (!widgetId.empty() && entry.request.widgetId != widgetId) return false;
        outcome.diagnostics.push_back({
            ScrollPaginationDiagnosticKind::Retired,
            entry.request,
            std::wstring{reason},
        });
        return true;
    });
    return outcome;
}

} // namespace widgetrail::input
