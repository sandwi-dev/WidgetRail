#include "WidgetInteractionSession.h"

#include <algorithm>
#include <cmath>
#include <iterator>
#include <utility>

namespace widgetrail::input {

SelectPopupLayout ComputeSelectPopupLayout(
    const declarative::Rect anchor,
    const declarative::Rect viewport,
    const SelectPopupBinding& popup) {
    constexpr float kRowHeight = 38.0F;
    constexpr float kMinimumWidth = 220.0F;
    constexpr float kGap = 4.0F;
    constexpr std::size_t kMaximumVisibleRows = 8;
    SelectPopupLayout result;
    if (popup.options.empty() || viewport.width <= 1.0F || viewport.height <= 1.0F)
        return result;
    const auto rowsThatFit = std::max<std::size_t>(
        1U, static_cast<std::size_t>(std::floor(viewport.height / kRowHeight)));
    const auto visibleCount = std::min(
        {kMaximumVisibleRows, popup.options.size(), rowsThatFit});
    const float rowHeight = std::min(
        kRowHeight, viewport.height / static_cast<float>(visibleCount));
    const float width = std::min(viewport.width, std::max(anchor.width, kMinimumWidth));
    const float height = rowHeight * static_cast<float>(visibleCount);
    float x = std::clamp(anchor.x, viewport.x, viewport.x + viewport.width - width);
    float y = anchor.y + anchor.height + kGap;
    if (y + height > viewport.y + viewport.height)
        y = anchor.y - kGap - height;
    y = std::clamp(y, viewport.y, viewport.y + viewport.height - height);
    result.bounds = {x, y, width, height};
    std::size_t first{};
    if (popup.highlightedOption >= visibleCount)
        first = std::min(
            popup.highlightedOption - visibleCount + 1,
            popup.options.size() - visibleCount);
    result.items.reserve(visibleCount);
    for (std::size_t row = 0; row < visibleCount; ++row) {
        result.items.push_back({
            first + row,
            {x, y + static_cast<float>(row) * rowHeight, width, rowHeight},
        });
    }
    return result;
}

SelectPopupContentLayout ComputeSelectPopupContentLayout(
    const declarative::Rect rowBounds,
    const bool showCheckmark,
    const bool showGlyph,
    const float leftInset,
    const float rightInset) noexcept {
    constexpr float kCheckmarkWidth = 20.0F;
    constexpr float kCheckmarkAdvance = 22.0F;
    constexpr float kGlyphSize = 22.0F;
    constexpr float kGlyphAdvance = 28.0F;
    SelectPopupContentLayout result;
    float contentLeft = rowBounds.x + std::max(0.0F, leftInset);
    const float contentRight = std::max(
        contentLeft,
        rowBounds.x + rowBounds.width - std::max(0.0F, rightInset));
    if (showCheckmark) {
        result.checkmarkBounds = {
            contentLeft, rowBounds.y, kCheckmarkWidth, rowBounds.height};
        contentLeft += kCheckmarkAdvance;
    }
    if (showGlyph) {
        const float glyphSize = std::min(kGlyphSize, std::max(0.0F, rowBounds.height));
        result.glyphBounds = {
            contentLeft,
            rowBounds.y + (rowBounds.height - glyphSize) * 0.5F,
            glyphSize,
            glyphSize,
        };
        contentLeft += kGlyphAdvance;
    }
    result.labelBounds = {
        contentLeft,
        rowBounds.y,
        std::max(0.0F, contentRight - contentLeft),
        rowBounds.height,
    };
    return result;
}

std::optional<std::size_t> HitTestSelectPopup(
    const SelectPopupLayout& layout,
    const float x,
    const float y) noexcept {
    for (const auto& item : layout.items) {
        if (x >= item.bounds.x && y >= item.bounds.y &&
            x < item.bounds.x + item.bounds.width &&
            y < item.bounds.y + item.bounds.height)
            return item.optionIndex;
    }
    return std::nullopt;
}
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

bool IsVisibleFreeScrollFocus(
    const WidgetInteractionAuthority& authority,
    const FreeScrollBinding& binding,
    const std::wstring_view focusedElementId,
    const RenderResult& renderResult) noexcept {
    if (!authority.semantics || focusedElementId.empty()) return false;
    const auto* focused = FindNodeInInputScope(
        *authority.semantics, focusedElementId,
        authority.semantics->activeInputScopeId);
    if (!focused || !IsEnabledFocusTarget(focusedElementId, renderResult)) {
        return false;
    }
    const auto owner = ResolveFocusedScrollOwner(
        authority.semantics->root, focusedElementId, binding.axis,
        authority.semantics->activeInputScopeId, renderResult);
    if (owner.disposition != FocusedScrollResolutionDisposition::Resolved ||
        owner.scrollId != binding.scrollId) {
        return false;
    }
    const auto visible = renderResult.focusRects.find(focusedElementId);
    return visible != renderResult.focusRects.end() &&
        visible->second.width > 0.0F && visible->second.height > 0.0F;
}

} // namespace

DirectionalFocusResolution SurfaceInteractionTransactions::ResolveDirectionalFocus(
    const WidgetSnapshot& snapshot, const std::wstring_view focusedElementId,
    const NavigationDirection direction, const RenderResult& renderResult,
    const WidgetFocusGroupMemory* focusGroups,
    const std::wstring_view widgetId) {
    const auto visible = ResolveVisibleFocusTarget(
        focusedElementId, snapshot.activeInputScopeId, renderResult);
    if (!visible) return {DirectionalFocusDisposition::MissingVisibleFocus, {}};
    if (*visible != focusedElementId)
        return {DirectionalFocusDisposition::VisibleRecovery, *visible};
    const auto* focused = FindNodeInInputScope(
        snapshot, focusedElementId, snapshot.activeInputScopeId);
    if (!focused) return {DirectionalFocusDisposition::Boundary, {}};
    const std::wstring* authored{};
    switch (direction) {
    case NavigationDirection::Left: authored = &focused->focusLeft; break;
    case NavigationDirection::Right: authored = &focused->focusRight; break;
    case NavigationDirection::Up: authored = &focused->focusUp; break;
    case NavigationDirection::Down: authored = &focused->focusDown; break;
    case NavigationDirection::None: break;
    }
    const auto* explicitTarget = authored && !authored->empty()
        ? FindNodeInInputScope(snapshot, *authored, snapshot.activeInputScopeId)
        : nullptr;
    if (explicitTarget && IsDistinctFocusMove(
            focusedElementId, explicitTarget->id,
            IsEnabledFocusTarget(explicitTarget->id, renderResult))) {
        return {DirectionalFocusDisposition::Explicit, explicitTarget->id};
    }
    if (explicitTarget && focusGroups &&
        !explicitTarget->initialChildFocusId.empty()) {
        if (auto target = focusGroups->Resolve(
                widgetId, snapshot, explicitTarget->id, renderResult)) {
            return {DirectionalFocusDisposition::Explicit, std::move(target)};
        }
    }
    const auto focusGroupCandidates = focusGroups
        ? FindExternalFocusGroupCandidates(
              snapshot, focusedElementId, renderResult)
        : std::vector<GeometricFocusGroupCandidate>{};
    const auto resolveGeometric = [&](const std::wstring_view geometric)
        -> std::optional<std::wstring> {
        const auto group = std::ranges::find_if(
            focusGroupCandidates, [&](const auto& candidate) {
                return candidate.groupId == geometric;
            });
        if (group != focusGroupCandidates.end()) {
            return focusGroups->Resolve(
                widgetId, snapshot, group->groupId, renderResult);
        }
        return std::wstring{geometric};
    };
    const auto internal = FindDirectionalFocusTargetInOwningSubtrees(
        snapshot.root, focusedElementId, direction, renderResult,
        focusGroupCandidates);
    if (internal.target) {
        if (auto target = resolveGeometric(*internal.target)) {
            const auto scrollExit = ClassifyDirectionalScrollExit(
                snapshot.root, focusedElementId, *target, direction,
                snapshot.activeInputScopeId, renderResult);
            if (scrollExit ==
                DirectionalScrollExitDisposition::StaleAuthority) {
                return {
                    DirectionalFocusDisposition::BlockedAuthority, {}};
            }
            return {
                DirectionalFocusDisposition::Geometric,
                std::move(target),
                scrollExit == DirectionalScrollExitDisposition::OutsideOwner,
            };
        }
        return {DirectionalFocusDisposition::Boundary, {}};
    }
    if (internal.staleAuthority) {
        return {DirectionalFocusDisposition::BlockedAuthority, {}};
    }
    if (const auto geometric = FindGeometricFocusTarget(
            focusedElementId, direction, renderResult,
            focusGroupCandidates)) {
        auto target = resolveGeometric(*geometric);
        if (!target) return {DirectionalFocusDisposition::Boundary, {}};
        const auto scrollExit = ClassifyDirectionalScrollExit(
            snapshot.root, focusedElementId, *target, direction,
            snapshot.activeInputScopeId, renderResult);
        if (scrollExit == DirectionalScrollExitDisposition::StaleAuthority) {
            return {DirectionalFocusDisposition::BlockedAuthority, {}};
        }
        return {
            DirectionalFocusDisposition::Geometric,
            std::move(target),
            scrollExit == DirectionalScrollExitDisposition::OutsideOwner,
        };
    }
    return {DirectionalFocusDisposition::Boundary, {}};
}

FocusMutation SurfaceInteractionTransactions::MoveFocus(
    std::wstring& focusedElementId, const std::wstring_view target) {
    FocusMutation result;
    result.priorFocus = focusedElementId;
    focusedElementId = target;
    result.focusedElementId = focusedElementId;
    result.changed = result.priorFocus != result.focusedElementId;
    return result;
}

FreeScrollAuthorityDecision SurfaceInteractionTransactions::EvaluateFreeScroll(
    FreeScrollInteractionState& state,
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId,
    const RenderResult& renderResult) {
    auto decision = state.Evaluate(authority, focusedElementId);
    if (decision.disposition == FreeScrollAuthorityDisposition::Replaced) {
        (void)state.Clear();
        return decision;
    }
    if (const auto& binding = state.binding(); binding && authority.semantics &&
        !IsExactScrollAuthorityCurrent(
            authority.semantics->root, binding->scrollId, binding->axis,
            renderResult)) {
        (void)state.Clear();
        return {FreeScrollAuthorityDisposition::Replaced, false};
    }
    return decision;
}

FreeScrollReentryRequest SurfaceInteractionTransactions::ResolveFreeScrollReentry(
    FreeScrollInteractionState& state,
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId,
    const RenderResult& renderResult) {
    (void)EvaluateFreeScroll(state, authority, focusedElementId, renderResult);
    return state.ResolveReentry(authority, focusedElementId, renderResult);
}

std::optional<FocusedFreeScrollPlan> SurfaceInteractionTransactions::PlanFreeScroll(
    FreeScrollInteractionState& state, DeclarativeRenderer& renderer,
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId,
    const RenderResult& renderResult, const declarative::ScrollAxis axis,
    const float deltaDip, const declarative::Rect viewport,
    FocusedFreeScrollPlanDiagnostic* diagnostic) {
    const auto decision = EvaluateFreeScroll(
        state, authority, focusedElementId, renderResult);
    if (!authority.semantics ||
        decision.disposition == FreeScrollAuthorityDisposition::Missing ||
        decision.disposition == FreeScrollAuthorityDisposition::Replaced) {
        return std::nullopt;
    }
    std::wstring exactScrollId;
    if (state.binding() && state.binding()->axis == axis) {
        exactScrollId = state.binding()->scrollId;
    } else {
        const auto owner = ResolveFocusedScrollOwner(
            authority.semantics->root, focusedElementId, axis,
            authority.semantics->activeInputScopeId, renderResult);
        if (owner.disposition != FocusedScrollResolutionDisposition::Resolved)
            return std::nullopt;
        exactScrollId = owner.scrollId;
    }
    return renderer.PlanFocusedFreeScroll(
        *authority.semantics, focusedElementId, axis, deltaDip, viewport,
        exactScrollId, diagnostic);
}

bool SurfaceInteractionTransactions::CommitFreeScroll(
    FreeScrollInteractionState& state,
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId,
    const FocusedFreeScrollPlan& plan) {
    return state.Bind(authority, focusedElementId, plan.scrollId, plan.axis);
}

std::optional<IncrementalPresentationPlan>
SurfaceInteractionTransactions::PlanFocusUpdate(
    DeclarativeRenderer& renderer, const WidgetSnapshot& snapshot,
    const std::wstring_view priorFocusedElementId,
    const std::wstring_view nextFocusedElementId,
    const declarative::Rect viewport,
    const std::vector<std::wstring>& additionalPaintNodeIds) {
    return renderer.PlanFocusUpdate(
        snapshot, priorFocusedElementId, nextFocusedElementId, viewport,
        additionalPaintNodeIds);
}

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
    if (focusedElementId_ != target) pendingCollectionFocus_.reset();
    focusedElementId_ = target;
    sliders_.RetainAdjustmentMode(
        snapshot.instanceId, snapshot.activeInputScopeId, focusedElementId_);
    focusMemory_.Remember(widgetId, snapshot, focusedElementId_);
    focusGroupMemory_.Remember(widgetId, snapshot, focusedElementId_);
}

DirectionalFocusResolution WidgetInteractionSession::ResolveDirectionalFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const NavigationDirection direction,
    const RenderResult& renderResult) const {
    return SurfaceInteractionTransactions::ResolveDirectionalFocus(
        snapshot, focusedElementId_, direction, renderResult,
        &focusGroupMemory_, widgetId);
}

FocusMutation WidgetInteractionSession::MoveFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view target,
    const bool retireSliderPresentations,
    const bool retirePressedPresentation) {
    if (focusedElementId_ != target) pendingCollectionFocus_.reset();
    auto result = SurfaceInteractionTransactions::MoveFocus(
        focusedElementId_, target);
    sliders_.RetainAdjustmentMode(
        snapshot.instanceId, snapshot.activeInputScopeId, focusedElementId_);
    if (retireSliderPresentations) {
        result.sliderDamageNodeIds = CurrentSliderNodeIds(
            snapshot, sliders_.DeactivateAll());
    }
    result.pressedPresentationChanged =
        retirePressedPresentation && pressed_.Clear();
    if (!ProvisionalFocusGroupEntry(widgetId, snapshot)) {
        focusMemory_.Remember(widgetId, snapshot, focusedElementId_);
        focusGroupMemory_.Remember(widgetId, snapshot, focusedElementId_);
    }
    if (retireSliderPresentations) RefreshSliderDeadline();
    return result;
}

void WidgetInteractionSession::ClearFocus() noexcept {
    focusedElementId_.clear();
    sliders_.RetainAdjustmentMode({}, {}, {});
    pendingFocusGroupEntry_.reset();
}

void WidgetInteractionSession::ClearLiveFocus() noexcept {
    focusedElementId_.clear();
    sliders_.RetainAdjustmentMode({}, {}, {});
}

void WidgetInteractionSession::RememberFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) {
    if (ProvisionalFocusGroupEntry(widgetId, snapshot)) return;
    focusMemory_.Remember(widgetId, snapshot, focusedElementId_);
}

std::wstring WidgetInteractionSession::RestoreFocus(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) {
    if (ProvisionalFocusGroupEntry(widgetId, snapshot)) {
        focusedElementId_.clear();
        sliders_.RetainAdjustmentMode({}, {}, {});
        return {};
    }
    focusedElementId_ = focusMemory_.Restore(widgetId, snapshot);
    focusMemory_.Remember(widgetId, snapshot, focusedElementId_);
    sliders_.RetainAdjustmentMode(
        snapshot.instanceId, snapshot.activeInputScopeId, focusedElementId_);
    focusGroupMemory_.Remember(widgetId, snapshot, focusedElementId_);
    return focusedElementId_;
}

std::wstring WidgetInteractionSession::FocusRestoreCandidate(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) const {
    if (ProvisionalFocusGroupEntry(widgetId, snapshot)) return {};
    return focusMemory_.Restore(widgetId, snapshot);
}

void WidgetInteractionSession::ForgetWidget(const std::wstring_view widgetId) {
    focusMemory_.Forget(widgetId);
    focusGroupMemory_.Forget(widgetId);
    sliders_.ForgetWidget(widgetId);
    std::erase_if(focusGroupEntryHighWater_, [&](const auto& entry) {
        return entry.widgetId == widgetId;
    });
    if (pendingFocusGroupEntry_ && pendingFocusGroupEntry_->widgetId == widgetId)
        pendingFocusGroupEntry_.reset();
    RefreshSliderDeadline();
}

bool WidgetInteractionSession::SameFocusGroupEntryRuntime(
    const FocusGroupEntryHighWater& entry,
    const WidgetInteractionAuthority& authority) noexcept {
    return authority.semantics && entry.widgetId == authority.widgetId &&
        entry.widgetInstanceId == authority.semantics->instanceId &&
        entry.runtimeGeneration == authority.runtimeGeneration;
}

bool WidgetInteractionSession::SameFocusGroupEntryRuntime(
    const PendingFocusGroupEntry& entry,
    const WidgetInteractionAuthority& authority) noexcept {
    return authority.semantics && entry.widgetId == authority.widgetId &&
        entry.widgetInstanceId == authority.semantics->instanceId &&
        entry.runtimeGeneration == authority.runtimeGeneration;
}

FocusGroupEntryObservation WidgetInteractionSession::ObserveFocusGroupEntryRequest(
    const WidgetInteractionAuthority& authority,
    const FocusGroupEntryAdmission admission) {
    if (!authority.semantics || authority.widgetId.empty() ||
        authority.runtimeGeneration.empty()) {
        pendingFocusGroupEntry_.reset();
        return FocusGroupEntryObservation::Retired;
    }
    const auto& snapshot = *authority.semantics;
    const auto* request = snapshot.focusGroupEntryRequest
        ? &*snapshot.focusGroupEntryRequest : nullptr;
    const bool pendingSameRuntime = pendingFocusGroupEntry_ &&
        SameFocusGroupEntryRuntime(*pendingFocusGroupEntry_, authority);
    if (!request) {
        if (pendingSameRuntime) {
            pendingFocusGroupEntry_.reset();
            return FocusGroupEntryObservation::Retired;
        }
        return FocusGroupEntryObservation::None;
    }

    auto highWater = std::find_if(
        focusGroupEntryHighWater_.begin(), focusGroupEntryHighWater_.end(),
        [&](const auto& entry) {
            return SameFocusGroupEntryRuntime(entry, authority);
        });
    if (pendingSameRuntime &&
        pendingFocusGroupEntry_->requestId == request->requestId) {
        const bool compatible = admission != FocusGroupEntryAdmission::Retire &&
            pendingFocusGroupEntry_->presentationGeneration ==
                authority.presentationGeneration &&
            pendingFocusGroupEntry_->inputScopeId == snapshot.activeInputScopeId &&
            pendingFocusGroupEntry_->groupId == request->groupId &&
            snapshot.sequence >= pendingFocusGroupEntry_->snapshotSequence;
        if (!compatible) {
            pendingFocusGroupEntry_.reset();
            return FocusGroupEntryObservation::Retired;
        }
        pendingFocusGroupEntry_->snapshotSequence = snapshot.sequence;
        return admission == FocusGroupEntryAdmission::Dormant
            ? FocusGroupEntryObservation::Dormant
            : FocusGroupEntryObservation::Pending;
    }
    if (highWater != focusGroupEntryHighWater_.end() &&
        request->requestId <= highWater->requestId) {
        if (pendingSameRuntime) pendingFocusGroupEntry_.reset();
        return FocusGroupEntryObservation::Retired;
    }
    if (highWater == focusGroupEntryHighWater_.end()) {
        focusGroupEntryHighWater_.push_back(FocusGroupEntryHighWater{
            std::wstring{authority.widgetId}, snapshot.instanceId,
            std::wstring{authority.runtimeGeneration}, request->requestId});
    } else {
        highWater->requestId = request->requestId;
    }
    if (pendingSameRuntime) pendingFocusGroupEntry_.reset();
    if (admission != FocusGroupEntryAdmission::Active)
        return FocusGroupEntryObservation::Retired;
    pendingFocusGroupEntry_.reset();
    pendingFocusGroupEntry_ = PendingFocusGroupEntry{
        std::wstring{authority.widgetId}, snapshot.instanceId,
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        snapshot.activeInputScopeId, request->groupId,
        request->requestId, snapshot.sequence};
    return FocusGroupEntryObservation::Pending;
}

FocusGroupEntryApplication WidgetInteractionSession::ConsumeFocusGroupEntryRequest(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult) {
    if (!pendingFocusGroupEntry_) return {};
    const auto preview = PreviewFocusGroupEntryRequest(authority, renderResult);
    if (!preview.current || !preview.target) return {};
    pendingFocusGroupEntry_.reset();
    return {true, preview.target};
}

FocusGroupEntryPreview WidgetInteractionSession::PreviewFocusGroupEntryRequest(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult) const {
    if (!pendingFocusGroupEntry_ ||
        !ExactFocusGroupEntryRequest(*pendingFocusGroupEntry_, authority)) {
        return {};
    }
    return {
        true,
        focusGroupMemory_.Resolve(
            authority.widgetId,
            *authority.semantics,
            pendingFocusGroupEntry_->groupId,
            renderResult),
    };
}

FocusGroupEntryApplication
WidgetInteractionSession::CommitPreparedFocusGroupEntryRequest(
    const WidgetInteractionAuthority& authority,
    const std::optional<std::wstring>& target) {
    if (!pendingFocusGroupEntry_ ||
        !ExactFocusGroupEntryRequest(*pendingFocusGroupEntry_, authority)) {
        return {};
    }
    if (!target) return {};
    pendingFocusGroupEntry_.reset();
    return {true, target};
}

bool WidgetInteractionSession::FocusGroupEntryRequestPending(
    const WidgetInteractionAuthority& authority) const noexcept {
    return pendingFocusGroupEntry_ &&
        ExactFocusGroupEntryRequest(*pendingFocusGroupEntry_, authority);
}

bool WidgetInteractionSession::RetireFocusGroupEntryRequest(
    const WidgetInteractionAuthority& authority) noexcept {
    if (!pendingFocusGroupEntry_ ||
        !ExactFocusGroupEntryRequest(*pendingFocusGroupEntry_, authority)) {
        return false;
    }
    pendingFocusGroupEntry_.reset();
    return true;
}

bool WidgetInteractionSession::ExactFocusGroupEntryRequest(
    const PendingFocusGroupEntry& pending,
    const WidgetInteractionAuthority& authority) noexcept {
    if (!SameFocusGroupEntryRuntime(pending, authority) ||
        !authority.semantics) {
        return false;
    }
    const auto& snapshot = *authority.semantics;
    return pending.presentationGeneration == authority.presentationGeneration &&
        pending.inputScopeId == snapshot.activeInputScopeId &&
        snapshot.focusGroupEntryRequest &&
        snapshot.focusGroupEntryRequest->requestId == pending.requestId &&
        snapshot.focusGroupEntryRequest->groupId == pending.groupId &&
        snapshot.sequence >= pending.snapshotSequence;
}

void WidgetInteractionSession::ResetFocusGroupEntryRequests() noexcept {
    pendingFocusGroupEntry_.reset();
    focusGroupEntryHighWater_.clear();
}

bool WidgetInteractionSession::ProvisionalFocusGroupEntry(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot) const noexcept {
    return pendingFocusGroupEntry_ &&
        pendingFocusGroupEntry_->widgetId == widgetId &&
        pendingFocusGroupEntry_->widgetInstanceId == snapshot.instanceId &&
        pendingFocusGroupEntry_->inputScopeId == snapshot.activeInputScopeId &&
        snapshot.focusGroupEntryRequest &&
        pendingFocusGroupEntry_->requestId ==
            snapshot.focusGroupEntryRequest->requestId &&
        pendingFocusGroupEntry_->groupId ==
            snapshot.focusGroupEntryRequest->groupId &&
        snapshot.sequence >= pendingFocusGroupEntry_->snapshotSequence;
}

RightStickScrollUpdate FreeScrollInteractionState::SampleRightStick(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
    return kinetics_.Update(x, y, now);
}

bool FreeScrollInteractionState::BindingMatches(
    const FreeScrollBinding& binding,
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId) noexcept {
    return authority.semantics &&
        binding.widgetId == authority.widgetId &&
        binding.widgetInstanceId == authority.semantics->instanceId &&
        binding.runtimeGeneration == authority.runtimeGeneration &&
        binding.presentationGeneration == authority.presentationGeneration &&
        binding.inputScopeId == authority.semantics->activeInputScopeId &&
        binding.focusedElementId == focusedElementId &&
        binding.axis != declarative::ScrollAxis::None;
}

FreeScrollAuthorityDecision FreeScrollInteractionState::Evaluate(
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId) const noexcept {
    if (!authority.semantics) {
        return {FreeScrollAuthorityDisposition::Missing, false};
    }
    if (!binding_)
        return {FreeScrollAuthorityDisposition::Current, false};
    if (!BindingMatches(*binding_, authority, focusedElementId))
        return {FreeScrollAuthorityDisposition::Replaced, false};
    return {
        authority.retainedRefresh
            ? FreeScrollAuthorityDisposition::Retained
            : FreeScrollAuthorityDisposition::Current,
        true,
    };
}

bool FreeScrollInteractionState::Bind(
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId,
    const std::wstring_view scrollId,
    const declarative::ScrollAxis axis) {
    if (!authority.semantics || axis == declarative::ScrollAxis::None ||
        scrollId.empty()) {
        return false;
    }
    const bool changed = !binding_ ||
        binding_->scrollId != scrollId ||
        binding_->axis != axis;
    binding_ = FreeScrollBinding{
        std::wstring{authority.widgetId},
        authority.semantics->instanceId,
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        authority.semantics->activeInputScopeId,
        std::wstring{focusedElementId},
        std::wstring{scrollId},
        axis,
    };
    refreshDeferred_ = false;
    return changed;
}

std::optional<FreeScrollBinding> FreeScrollInteractionState::Clear() noexcept {
    auto prior = std::move(binding_);
    binding_.reset();
    refreshDeferred_ = false;
    kinetics_.Reset();
    return prior;
}

bool FreeScrollInteractionState::SetRefreshDeferred(const bool deferred) noexcept {
    if (!binding_ || refreshDeferred_ == deferred)
        return false;
    refreshDeferred_ = deferred;
    return true;
}

FreeScrollReentryRequest FreeScrollInteractionState::ResolveReentry(
    const WidgetInteractionAuthority& authority,
    const std::wstring_view focusedElementId,
    const RenderResult& renderResult) {
    FreeScrollReentryRequest request;
    if (!binding_ || !authority.semantics ||
        !BindingMatches(*binding_, authority, focusedElementId)) {
        return request;
    }
    request.retiredBinding = std::move(binding_);
    binding_.reset();
    refreshDeferred_ = false;
    kinetics_.Reset();
    if (IsVisibleFreeScrollFocus(
            authority, *request.retiredBinding, focusedElementId,
            renderResult)) {
        request.disposition =
            FreeScrollReentryDisposition::ResumeDirectionalInput;
        return request;
    }
    request.disposition = FreeScrollReentryDisposition::RecoveryConsumed;
    request.target = FindFreeScrollReentryTarget(
        authority.semantics->root,
        request.retiredBinding->scrollId,
        request.retiredBinding->axis,
        authority.semantics->activeInputScopeId,
        renderResult);
    return request;
}

RightStickScrollUpdate WidgetInteractionSession::SampleRightStick(
    const short x,
    const short y,
    const std::uint64_t now) noexcept {
    return freeScroll_.SampleRightStick(x, y, now);
}

FreeScrollAuthorityDecision WidgetInteractionSession::EvaluateFreeScrollAuthority(
    const WidgetInteractionAuthority& authority) const noexcept {
    return freeScroll_.Evaluate(authority, focusedElementId_);
}

bool WidgetInteractionSession::BindFreeScroll(
    const WidgetInteractionAuthority& authority,
    const std::wstring_view scrollId,
    const declarative::ScrollAxis axis) {
    return freeScroll_.Bind(authority, focusedElementId_, scrollId, axis);
}

std::optional<FreeScrollBinding> WidgetInteractionSession::ClearFreeScroll() noexcept {
    return freeScroll_.Clear();
}

bool WidgetInteractionSession::SetRefreshDeferred(const bool deferred) noexcept {
    return freeScroll_.SetRefreshDeferred(deferred);
}

FreeScrollReentryRequest WidgetInteractionSession::ResolveFreeScrollReentry(
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult) {
    return freeScroll_.ResolveReentry(authority, focusedElementId_, renderResult);
}

WidgetInteractionPresentation WidgetInteractionSession::Presentation(
    const WidgetInteractionAuthority& authority) const noexcept {
    const bool exact = authority.semantics &&
        (!freeScroll_.binding() ||
         freeScroll_.Evaluate(authority, focusedElementId_).followSuppressed);
    const auto pressedElement = authority.semantics
        ? pressed_.ActiveElementId(*authority.semantics, focusedElementId_)
        : std::wstring_view{};
    return {
        focusedElementId_,
        pressedElement,
        sliders_.presentationRevision(),
        exact && freeScroll_.binding().has_value(),
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
    selectPopup_.reset();
    return result;
}

void WidgetInteractionSession::ForgetRuntime(
    const std::wstring_view widgetInstanceId) noexcept {
    sliders_.ForgetWidget(widgetInstanceId);
    if (selectPopup_ && selectPopup_->widgetInstanceId == widgetInstanceId)
        selectPopup_.reset();
    std::erase_if(focusGroupEntryHighWater_, [&](const auto& entry) {
        return entry.widgetInstanceId == widgetInstanceId;
    });
    if (pendingFocusGroupEntry_ &&
        pendingFocusGroupEntry_->widgetInstanceId == widgetInstanceId)
        pendingFocusGroupEntry_.reset();
    RefreshSliderDeadline();
}

InteractionReconciliation WidgetInteractionSession::ReconcileAdmission(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const std::uint64_t now) {
    InteractionReconciliation result;
    std::vector<SliderInputDescriptor> currentSliders;
    const auto visit = [&](const auto& self, const WidgetNode& node) -> void {
        if (node.kind == L"slider") {
            const auto* exact = FindNodeInInputScope(
                snapshot, node.id, snapshot.activeInputScopeId);
            if (exact == &node) {
                const auto slider = SliderDescriptor(snapshot, node);
                currentSliders.push_back(slider);
                if (sliders_.Reconcile(slider, now).visualChanged)
                    result.sliderDamageNodeIds.push_back(node.id);
            }
        }
        for (const auto& child : node.children) self(self, child);
    };
    visit(visit, snapshot.root);
    sliders_.RetireAbsent(snapshot.instanceId, currentSliders);
    if (!focusedElementId.empty()) {
        sliders_.RetainAdjustmentMode(
            snapshot.instanceId, snapshot.activeInputScopeId, focusedElementId);
    }
    if (selectPopup_) {
        const auto* node = FindNodeInInputScope(
            snapshot, selectPopup_->openerElementId, snapshot.activeInputScopeId);
        const auto sameOptions = node && node->isSelect &&
            node->selectOptions.size() == selectPopup_->options.size() &&
            std::equal(
                node->selectOptions.begin(), node->selectOptions.end(),
                selectPopup_->options.begin(),
                [](const WidgetSelectOption& left, const WidgetSelectOption& right) {
                    return left.id == right.id && left.actionId == right.actionId &&
                        left.label == right.label && left.glyph == right.glyph &&
                        left.packageIcon == right.packageIcon &&
                        left.accessibilityLabel == right.accessibilityLabel &&
                        left.isDisabled == right.isDisabled && left.isBusy == right.isBusy;
                });
        if (snapshot.instanceId != selectPopup_->widgetInstanceId ||
            snapshot.activeInputScopeId != selectPopup_->inputScopeId ||
            !sameOptions || !node || node->isDisabled || node->isBusy) {
            selectPopup_.reset();
        } else {
            selectPopup_->snapshotSequence = snapshot.sequence;
            selectPopup_->options = node->selectOptions;
            const auto available = [](const WidgetSelectOption& option) {
                return !option.isDisabled && !option.isBusy;
            };
            if (selectPopup_->highlightedOption >= selectPopup_->options.size() ||
                !available(selectPopup_->options[selectPopup_->highlightedOption])) {
                auto replacement = std::find_if(
                    selectPopup_->options.begin(), selectPopup_->options.end(),
                    [&](const WidgetSelectOption& option) {
                        return option.isSelected && available(option);
                    });
                if (replacement == selectPopup_->options.end())
                    replacement = std::find_if(
                        selectPopup_->options.begin(), selectPopup_->options.end(),
                        available);
                if (replacement == selectPopup_->options.end()) {
                    selectPopup_.reset();
                } else {
                    selectPopup_->highlightedOption = static_cast<std::size_t>(
                        replacement - selectPopup_->options.begin());
                }
            }
        }
    }
    RefreshSliderDeadline();
    result.nextDeadline = sliderReconcileAt_;
    return result;
}

bool WidgetInteractionSession::ReconcilePressedPresentation(
    const WidgetSnapshot& snapshot) noexcept {
    return pressed_.Reconcile(snapshot, focusedElementId_);
}

InteractionReconciliation WidgetInteractionSession::Tick(
    const WidgetInteractionAuthority* authority,
    const std::wstring_view focusedElementId,
    const std::uint64_t now) {
    InteractionReconciliation result;
    const auto* snapshot = authority ? authority->semantics : nullptr;
    if (snapshot) result = ReconcileAdmission(*snapshot, focusedElementId, now);
    else (void)sliders_.DeactivateAll();
    if (authority && snapshot) {
        const auto* focused = FindNodeInInputScope(
            *snapshot, focusedElementId, snapshot->activeInputScopeId);
        if (focused && focused->kind == L"slider" &&
            !focused->isDisabled && !focused->isBusy) {
            const auto slider = SliderDescriptor(*snapshot, *focused);
            const auto priorRevision = sliders_.presentationRevision();
            if (const auto dispatch = sliders_.TakePendingDispatch(slider, now)) {
                result.sliderActionRequests.push_back(
                    MakeSliderActionRequest(*authority, slider, *dispatch));
            }
            if (sliders_.presentationRevision() != priorRevision &&
                std::find(
                    result.sliderDamageNodeIds.begin(),
                    result.sliderDamageNodeIds.end(), focused->id) ==
                    result.sliderDamageNodeIds.end()) {
                result.sliderDamageNodeIds.push_back(focused->id);
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
    return {
        adjustment.consumed,
        sliders_.presentationRevision() != priorRevision,
        std::nullopt,
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
    const auto dispatch = consumed
        ? sliders_.TakePendingDispatch(slider, now, true)
        : std::nullopt;
    RefreshSliderDeadline();
    const auto request = dispatch
        ? std::optional{MakeSliderActionRequest(authority, slider, *dispatch)}
        : std::nullopt;
    return {
        consumed,
        sliders_.presentationRevision() != priorRevision,
        request,
        sliders_.presentationRevision(),
        sliderReconcileAt_,
    };
}

SliderInputOutcome WidgetInteractionSession::TakePendingSliderAction(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const std::uint64_t now,
    const bool force) {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return {};
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
    const auto priorRevision = sliders_.presentationRevision();
    const auto dispatch = sliders_.TakePendingDispatch(slider, now, force);
    RefreshSliderDeadline();
    return {
        dispatch.has_value(),
        sliders_.presentationRevision() != priorRevision,
        dispatch
            ? std::optional{MakeSliderActionRequest(authority, slider, *dispatch)}
            : std::nullopt,
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
    }, request.sliderIntentGeneration, now);
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
    const WidgetNode& node) const {
    const auto* exactNode = ExactSliderNode(authority, node);
    if (!exactNode) return false;
    const auto slider = SliderDescriptor(*authority.semantics, *exactNode);
    return sliders_.AdjustmentModeActive(slider);
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

SelectActivationResult WidgetInteractionSession::OpenSelectPopup(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node) {
    if (!node.isSelect) return SelectActivationResult::NotSelect;
    if (!authority.semantics || authority.retainedRefresh || node.isDisabled ||
        node.isBusy || node.selectOptions.empty())
        return SelectActivationResult::ConsumedClosed;
    const auto* exact = FindNodeInInputScope(
        *authority.semantics, node.id, authority.semantics->activeInputScopeId);
    if (exact != &node || !exact->isSelect)
        return SelectActivationResult::ConsumedClosed;
    const auto available = [](const WidgetSelectOption& option) {
        return !option.isDisabled && !option.isBusy;
    };
    auto selected = std::find_if(
        node.selectOptions.begin(), node.selectOptions.end(),
        [&](const WidgetSelectOption& option) {
            return option.isSelected && available(option);
        });
    if (selected == node.selectOptions.end())
        selected = std::find_if(
            node.selectOptions.begin(), node.selectOptions.end(), available);
    if (selected == node.selectOptions.end())
        return SelectActivationResult::ConsumedClosed;
    selectPopup_ = SelectPopupBinding{
        std::wstring{authority.widgetId},
        authority.semantics->instanceId,
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        authority.semantics->activeInputScopeId,
        node.id,
        authority.semantics->sequence,
        node.selectOptions,
        static_cast<std::size_t>(selected - node.selectOptions.begin()),
    };
    (void)pressed_.Clear();
    return SelectActivationResult::Opened;
}

bool WidgetInteractionSession::SelectPopupCurrent(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node) const noexcept {
    if (!selectPopup_ || !authority.semantics || authority.retainedRefresh ||
        !node.isSelect || node.isDisabled || node.isBusy) return false;
    const auto& popup = *selectPopup_;
    if (popup.widgetId != authority.widgetId ||
        popup.widgetInstanceId != authority.semantics->instanceId ||
        popup.runtimeGeneration != authority.runtimeGeneration ||
        popup.presentationGeneration != authority.presentationGeneration ||
        popup.inputScopeId != authority.semantics->activeInputScopeId ||
        popup.openerElementId != node.id ||
        popup.options.size() != node.selectOptions.size()) return false;
    return std::equal(
        popup.options.begin(), popup.options.end(), node.selectOptions.begin(),
        [](const WidgetSelectOption& left, const WidgetSelectOption& right) {
            return left.id == right.id && left.actionId == right.actionId &&
                left.label == right.label && left.glyph == right.glyph &&
                left.packageIcon == right.packageIcon &&
                left.accessibilityLabel == right.accessibilityLabel &&
                left.isDisabled == right.isDisabled && left.isBusy == right.isBusy;
        });
}

bool WidgetInteractionSession::MoveSelectPopup(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const NavigationDirection direction) {
    if (!SelectPopupCurrent(authority, node) ||
        (direction != NavigationDirection::Up &&
         direction != NavigationDirection::Down)) return false;
    auto& popup = *selectPopup_;
    const auto count = popup.options.size();
    for (std::size_t offset = 1; offset <= count; ++offset) {
        const auto candidate = direction == NavigationDirection::Up
            ? (popup.highlightedOption + count - (offset % count)) % count
            : (popup.highlightedOption + offset) % count;
        if (!popup.options[candidate].isDisabled &&
            !popup.options[candidate].isBusy) {
            popup.highlightedOption = candidate;
            return true;
        }
    }
    return true;
}

bool WidgetInteractionSession::HighlightSelectPopupOption(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node,
    const std::size_t optionIndex) {
    if (!SelectPopupCurrent(authority, node) ||
        optionIndex >= selectPopup_->options.size() ||
        selectPopup_->options[optionIndex].isDisabled ||
        selectPopup_->options[optionIndex].isBusy) return false;
    selectPopup_->highlightedOption = optionIndex;
    return true;
}

std::optional<SelectPopupAction> WidgetInteractionSession::CommitSelectPopup(
    const WidgetInteractionAuthority& authority,
    const WidgetNode& node) {
    if (!SelectPopupCurrent(authority, node)) {
        selectPopup_.reset();
        return std::nullopt;
    }
    const auto popup = *selectPopup_;
    if (popup.highlightedOption >= popup.options.size()) {
        selectPopup_.reset();
        return std::nullopt;
    }
    const auto option = popup.options[popup.highlightedOption];
    selectPopup_.reset();
    if (option.isDisabled || option.isBusy) return std::nullopt;
    return SelectPopupAction{
        WidgetInteractionActionRequest{
            std::wstring{authority.widgetId},
            authority.semantics->instanceId,
            std::wstring{authority.runtimeGeneration},
            std::wstring{authority.presentationGeneration},
            authority.semantics->activeInputScopeId,
            node.id,
            option.actionId,
            authority.semantics->sequence,
            std::nullopt,
        },
        option.id,
    };
}

bool WidgetInteractionSession::CloseSelectPopup() noexcept {
    if (!selectPopup_) return false;
    selectPopup_.reset();
    return true;
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
    const bool allowPressedPresentation,
    const bool allowAdjustmentModePresentation) const {
    InteractionRenderPresentation result;
    const auto collect = [&](const auto& self, const WidgetNode& node) -> void {
        if (node.kind == L"slider") {
            if (const auto value = sliders_.PresentationValue(
                    SliderDescriptor(snapshot, node))) {
                result.sliderValueOverrides.emplace(node.id, *value);
            }
        }
        for (const auto& child : node.children) self(self, child);
    };
    collect(collect, snapshot.root);
    if (allowPressedPresentation) {
        result.pressedElementId = pressed_.ActiveElementId(
            snapshot, renderedFocusId);
    }
    if (allowAdjustmentModePresentation) {
        const auto* focused = FindNodeInInputScope(
            snapshot, renderedFocusId, snapshot.activeInputScopeId);
        if (focused && focused->kind == L"slider" &&
            !focused->isDisabled && !focused->isBusy) {
            const bool activationRequired =
                focused->sliderInteractionMode == L"activateToAdjust";
            if (!activationRequired || sliders_.AdjustmentModeActive(
                    SliderDescriptor(snapshot, *focused))) {
                result.activeSliderElementId = focused->id;
            }
        }
    }
    result.sliderPresentationRevision = sliders_.presentationRevision();
    return result;
}

WidgetInteractionActionRequest WidgetInteractionSession::MakeSliderActionRequest(
    const WidgetInteractionAuthority& authority,
    const SliderInputDescriptor& slider,
    const SliderDispatch& dispatch) {
    return {
        std::wstring{authority.widgetId},
        std::wstring{slider.widgetInstanceId},
        std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration},
        std::wstring{slider.inputScopeId},
        std::wstring{slider.nodeId},
        std::wstring{slider.valueChangedActionId},
        slider.snapshotSequence,
        dispatch.requestedValue,
        dispatch.intentGeneration,
        dispatch.direction,
    };
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
    if (source != ScrollPaginationIntentSource::DirectionalNavigation) pendingCollectionFocus_.reset();
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
    if (priorFocus != nextFocus) pendingCollectionFocus_.reset();
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
    if (pendingCollectionFocus_ && pendingCollectionFocus_->direction != direction) pendingCollectionFocus_.reset();
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
    pendingCollectionFocus_ = PendingCollectionFocus{std::wstring{authority.widgetId},
        authority.semantics->instanceId, std::wstring{authority.runtimeGeneration},
        std::wstring{authority.presentationGeneration}, authority.semantics->activeInputScopeId,
        owner.scrollId, std::wstring{focusedElementId}, direction, authority.semantics->sequence};
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

DirectionalFocusAdmission AdmitDirectionalFocusResolution(
    WidgetInteractionSession& paginationOwner,
    const WidgetInteractionAuthority& authority,
    const RenderResult& renderResult,
    const std::wstring_view focusedElementId,
    const NavigationDirection direction,
    DirectionalFocusResolution resolution,
    const ScrollPaginationIntentSource source,
    const std::uint64_t now) {
    DirectionalFocusAdmission result;
    result.resolution = std::move(resolution);
    if (result.resolution.disposition ==
        DirectionalFocusDisposition::BlockedAuthority) {
        result.retainFocus = true;
        return result;
    }
    const bool proposedScrollExit = result.resolution.target &&
        result.resolution.requiresScrollBoundaryAdmission;
    const bool unresolvedBoundary = !result.resolution.target &&
        result.resolution.disposition == DirectionalFocusDisposition::Boundary;
    if (!proposedScrollExit && !unresolvedBoundary) return result;

    auto boundary = paginationOwner.ObserveScrollPaginationBoundaryIntent(
        authority, renderResult, focusedElementId, direction, source, now);
    result.pagination = std::move(boundary.pagination);
    result.retainFocus = boundary.retainFocus;
    return result;
}

std::optional<std::wstring> WidgetInteractionSession::ResolvePendingCollectionFocus(
    const WidgetInteractionAuthority& authority, const std::wstring_view focusedElementId,
    const RenderResult& renderResult) {
    if (!pendingCollectionFocus_) return std::nullopt;
    const auto pending = *pendingCollectionFocus_;
    if (!authority.semantics || authority.widgetId != pending.widgetId ||
        authority.semantics->instanceId != pending.instanceId ||
        authority.runtimeGeneration != pending.runtime || authority.presentationGeneration != pending.presentation ||
        authority.semantics->activeInputScopeId != pending.scope || focusedElementId != pending.focusId) {
        pendingCollectionFocus_.reset(); return std::nullopt;
    }
    if (authority.semantics->sequence <= pending.sequence) return std::nullopt;
    const auto next = FindDirectionalFocusTargetInOwningSubtrees(
        authority.semantics->root, focusedElementId, pending.direction, renderResult, {});
    if (!next.target || ClassifyDirectionalScrollExit(authority.semantics->root,
            focusedElementId, *next.target, pending.direction, pending.scope, renderResult) !=
            DirectionalScrollExitDisposition::InsideOwner) return std::nullopt;
    pendingCollectionFocus_.reset();
    return next.target;
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
        const auto* scrollNode = FindNodeInInputScope(*authority.semantics, latch.scrollId, authority.semantics->activeInputScopeId);
        const auto containsItems = [&](const auto& self, const WidgetNode& node) -> bool {
            if (&node != scrollNode && node.kind == L"scroll") return false;
            if (!node.collectionItemKey.empty()) return true;
            return std::ranges::any_of(node.children, [&](const WidgetNode& child) { return self(self, child); });
        };
        const bool hasCollection = scrollNode && scrollNode->collectionStartIndex && containsItems(containsItems, *scrollNode);
        const bool firstCollection = hasCollection && !latch.collectionObserved;
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
            // A real newer direction may reverse inside overlapping prefetch
            // zones. Passive snapshot reconciliation still cannot rearm either edge.
            if (candidate) {
                queue(
                    *candidate,
                    wasResident(intentEdge)
                        ? ScrollPaginationDemandReason::Intent
                        : ScrollPaginationDemandReason::ThresholdReentry,
                    latch.pendingIntentSource,
                    latch.pendingIntentGeneration);
            }
        }

        // A small transport page is not necessarily a full viewport. This is
        // bounded by forward progress and the provider's end cursor; visible
        // keys accompany the request so filling cannot evict those same rows.
        if (!latch.prefetch && afterResident && after->viewportUnderfilled && !latch.pendingIntentEdge) {
            queue(*after, ScrollPaginationDemandReason::Initial,
                ScrollPaginationIntentSource::None, ++scrollPaginationDemandGeneration_);
        }

        if (!latch.prefetch && ((firstObservation && initialRouteObservation) || firstCollection) &&
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
        latch.collectionObserved = latch.collectionObserved || hasCollection;
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
    pendingCollectionFocus_.reset();
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
