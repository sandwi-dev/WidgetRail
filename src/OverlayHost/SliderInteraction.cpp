#include "SliderInteraction.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace widgetrail::input {
namespace {

bool Near(const double first, const double second, const double scale) noexcept {
    if (!std::isfinite(first) || !std::isfinite(second) || !std::isfinite(scale))
        return false;
    return std::abs(first - second) <= std::max(1.0, std::abs(scale)) * 1e-9;
}

std::optional<double> StepTarget(
    const double value,
    const double minimum,
    const double maximum,
    const double step,
    const NavigationDirection direction) noexcept {
    const auto range = maximum - minimum;
    const auto offset = value - minimum;
    if (!std::isfinite(range) || range <= 0.0 || !std::isfinite(offset))
        return std::nullopt;
    const auto position = offset / step;
    if (!std::isfinite(position)) return std::nullopt;
    constexpr auto floatGridUlps = 4.0;
    constexpr auto maximumNearGridFraction = 1e-4;
    // Absorb float-to-double residue without treating a meaningful fraction of
    // one step as on-grid.
    const auto gridTolerance = std::min(
        maximumNearGridFraction,
        floatGridUlps * static_cast<double>(std::numeric_limits<float>::epsilon()) *
            std::max(1.0, std::abs(position)));
    const auto nearestGridPosition = std::round(position);
    const auto steppedPosition =
        std::abs(position - nearestGridPosition) <= gridTolerance
        ? nearestGridPosition
        : position;
    const auto index = direction == NavigationDirection::Right
        ? std::floor(steppedPosition) + 1.0
        : std::ceil(steppedPosition) - 1.0;
    const auto candidate = minimum + index * step;
    if (!std::isfinite(index) || !std::isfinite(candidate)) return std::nullopt;
    const auto rangeTolerance = std::max(1.0, std::abs(range)) * 1e-12;
    if (direction == NavigationDirection::Right && candidate > maximum - rangeTolerance)
        return maximum;
    if (direction == NavigationDirection::Left && candidate < minimum + rangeTolerance)
        return minimum;
    return std::clamp(candidate, minimum, maximum);
}

} // namespace

SliderAdjustment SliderInteractionState::Adjust(
    const SliderInputDescriptor& slider,
    const NavigationDirection direction,
    const std::uint64_t nowMilliseconds) {
    if (direction != NavigationDirection::Left && direction != NavigationDirection::Right)
        return {};
    if (!Valid(slider)) return {true};
    auto* entry = CreateOrSynchronize(slider, nowMilliseconds);
    if (!entry) return {true};
    if (entry->activationRequired && !entry->adjustmentActive)
        return {false};
    if (slider.disabled || slider.busy) return {true};
    const auto target = StepTarget(
        entry->targetValue, entry->minimum, entry->maximum, entry->step, direction);
    if (!target || Near(*target, entry->targetValue, entry->maximum - entry->minimum))
        return {true};
    if (entry->unsent && entry->recentDispatchedValues.empty() &&
        entry->latestTargetDispatchedAt == 0 &&
        Near(*target, entry->authoritativeValue,
             entry->maximum - entry->minimum)) {
        (void)CancelUnsent(*entry);
        entry->lastAccess = ++accessClock_;
        return {true};
    }
    entry->targetValue = *target;
    entry->pending = true;
    entry->unsent = true;
    entry->suppressingGuardedEcho = false;
    entry->latestDirection = direction;
    entry->adjustmentSnapshotSequence = slider.snapshotSequence;
    entry->lastAdjustment = nowMilliseconds;
    entry->lastAccess = ++accessClock_;
    ++presentationRevision_;
    return {true};
}

bool SliderInteractionState::SetRequestedValue(
    const SliderInputDescriptor& slider,
    const double requestedValue,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || slider.disabled || slider.busy ||
        !std::isfinite(requestedValue) || requestedValue < slider.minimum ||
        requestedValue > slider.maximum) {
        return false;
    }
    auto* entry = CreateOrSynchronize(slider, nowMilliseconds);
    if (!entry) return false;
    if (entry->pending &&
        Near(requestedValue, entry->targetValue, entry->maximum - entry->minimum)) {
        entry->lastAccess = ++accessClock_;
        return true;
    }
    if (entry->unsent && entry->recentDispatchedValues.empty() &&
        entry->latestTargetDispatchedAt == 0 &&
        Near(requestedValue, entry->authoritativeValue,
             entry->maximum - entry->minimum)) {
        (void)CancelUnsent(*entry);
        entry->lastAccess = ++accessClock_;
        return true;
    }
    if (!entry->pending &&
        Near(requestedValue, entry->authoritativeValue,
             entry->maximum - entry->minimum)) {
        entry->lastAccess = ++accessClock_;
        return true;
    }
    entry->targetValue = requestedValue;
    entry->pending = true;
    entry->unsent = true;
    entry->suppressingGuardedEcho = false;
    entry->latestDirection = NavigationDirection::None;
    entry->adjustmentSnapshotSequence = slider.snapshotSequence;
    entry->lastAdjustment = nowMilliseconds;
    entry->lastAccess = ++accessClock_;
    ++presentationRevision_;
    return true;
}

std::optional<SliderDispatch> SliderInteractionState::TakePendingDispatch(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds,
    const bool force) {
    if (!Valid(slider)) return std::nullopt;
    auto* entry = FindAndSynchronize(slider, nowMilliseconds);
    if (!entry || !entry->pending || !entry->unsent) return std::nullopt;
    if (slider.disabled || slider.busy) {
        (void)CancelUnsent(*entry);
        return std::nullopt;
    }
    if (!force && (nowMilliseconds < entry->lastAdjustment ||
        nowMilliseconds - entry->lastAdjustment < SettlementDelayMilliseconds)) {
        return std::nullopt;
    }
    ExpireDispatchHistory(*entry, nowMilliseconds);
    const auto generation = ++entry->nextIntentGeneration;
    entry->unsent = false;
    entry->latestTargetDispatchedAt = nowMilliseconds;
    entry->recentDispatchedValues.push_back({
        entry->targetValue, generation, nowMilliseconds});
    if (entry->recentDispatchedValues.size() > MaximumRecentDispatchedValues)
        entry->recentDispatchedValues.pop_front();
    entry->lastAccess = ++accessClock_;
    return SliderDispatch{
        entry->targetValue, generation, entry->latestDirection};
}

bool SliderInteractionState::EnterAdjustmentMode(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !slider.activationRequired) return false;
    auto* entry = CreateOrSynchronize(slider, nowMilliseconds);
    if (!entry || slider.disabled || slider.busy) return false;
    RetainAdjustmentMode(slider.widgetInstanceId, slider.inputScopeId, slider.nodeId);
    if (!entry->adjustmentActive) ++presentationRevision_;
    entry->adjustmentActive = true;
    entry->lastAccess = ++accessClock_;
    return true;
}

bool SliderInteractionState::ExitAdjustmentMode(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !slider.activationRequired) return false;
    auto* entry = FindAndSynchronize(slider, nowMilliseconds);
    if (!entry || !entry->adjustmentActive) return false;
    entry->adjustmentActive = false;
    ++presentationRevision_;
    entry->lastAccess = ++accessClock_;
    return true;
}

bool SliderInteractionState::AdjustmentModeActive(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !slider.activationRequired) return false;
    const auto* entry = FindAndSynchronize(slider, nowMilliseconds);
    return entry && entry->adjustmentActive;
}

std::optional<double> SliderInteractionState::PresentationValue(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider)) return std::nullopt;
    const auto* entry = FindAndSynchronize(slider, nowMilliseconds);
    return entry && entry->pending
        ? std::optional{entry->targetValue}
        : std::nullopt;
}

std::optional<std::uint64_t>
SliderInteractionState::NextReconcileDeadline() const noexcept {
    std::optional<std::uint64_t> deadline;
    for (const auto& [_, entry] : entries_) {
        if (!entry.pending) continue;
        if (entry.unsent) {
            const auto candidate = entry.lastAdjustment + SettlementDelayMilliseconds;
            if (!deadline || candidate < *deadline) deadline = candidate;
        } else if (entry.suppressingGuardedEcho) {
            for (const auto& dispatched : entry.recentDispatchedValues) {
                if (!Near(dispatched.value, entry.authoritativeValue,
                          entry.maximum - entry.minimum)) continue;
                const auto candidate = dispatched.dispatchedAt +
                    PendingTimeoutMilliseconds + 1;
                if (!deadline || candidate < *deadline) deadline = candidate;
            }
        } else if (entry.latestTargetDispatchedAt != 0) {
            const auto candidate = entry.latestTargetDispatchedAt +
                PendingTimeoutMilliseconds + 1;
            if (!deadline || candidate < *deadline) deadline = candidate;
        }
    }
    return deadline;
}

std::vector<SliderPresentationIdentity> SliderInteractionState::ExpireTimedOut(
    const std::uint64_t nowMilliseconds) {
    std::vector<SliderPresentationIdentity> changed;
    for (auto& [_, entry] : entries_) {
        ExpireDispatchHistory(entry, nowMilliseconds);
        if (!entry.pending || entry.unsent) continue;
        const bool guardStillApplies = entry.suppressingGuardedEcho &&
            MatchesRecentDispatch(entry, entry.authoritativeValue);
        const bool targetStillAwaiting = !entry.suppressingGuardedEcho &&
            entry.latestTargetDispatchedAt != 0 &&
            nowMilliseconds >= entry.latestTargetDispatchedAt &&
            nowMilliseconds - entry.latestTargetDispatchedAt <=
                PendingTimeoutMilliseconds;
        if (guardStillApplies || targetStillAwaiting) continue;
        const bool visualChanged = !Near(
            entry.targetValue, entry.authoritativeValue,
            entry.maximum - entry.minimum);
        entry.pending = false;
        entry.suppressingGuardedEcho = false;
        entry.latestTargetDispatchedAt = 0;
        entry.targetValue = entry.authoritativeValue;
        if (visualChanged) changed.push_back(Identity(entry));
    }
    // The visible/current tree is reconciled first through Reconcile(). What
    // remains here is private off-tree state, so retiring it must not advance
    // the host-visible presentation revision or schedule unrelated raster work.
    return changed;
}

SliderReconciliation SliderInteractionState::Reconcile(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    SliderReconciliation reconciliation;
    if (Valid(slider))
        (void)FindAndSynchronize(slider, nowMilliseconds, &reconciliation);
    return reconciliation;
}

bool SliderInteractionState::CancelPending(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider)) return false;
    return CancelPending(SliderPresentationIdentity{
        std::wstring{slider.widgetInstanceId},
        std::wstring{slider.inputScopeId},
        std::wstring{slider.nodeId},
        std::wstring{slider.valueChangedActionId},
        slider.snapshotSequence,
    }, 0, nowMilliseconds);
}

bool SliderInteractionState::CancelPending(
    const SliderPresentationIdentity& identity,
    const std::uint64_t intentGeneration,
    const std::uint64_t nowMilliseconds) {
    if (identity.widgetInstanceId.empty() || identity.inputScopeId.empty() ||
        identity.nodeId.empty() || identity.valueChangedActionId.empty() ||
        identity.snapshotSequence <= 0) {
        return false;
    }
    std::wstring key{identity.widgetInstanceId};
    key.push_back(L'\x1f');
    key.append(identity.inputScopeId);
    key.push_back(L'\x1f');
    key.append(identity.nodeId);
    const auto position = entries_.find(key);
    if (position == entries_.end()) return false;
    auto& entry = position->second;
    if (!entry.pending || entry.actionId != identity.valueChangedActionId) {
        return false;
    }
    if (intentGeneration == 0 &&
        entry.snapshotSequence != identity.snapshotSequence) return false;
    if (intentGeneration != 0) {
        const auto dispatched = std::find_if(
            entry.recentDispatchedValues.begin(),
            entry.recentDispatchedValues.end(),
            [&](const DispatchedValue& value) {
                return value.intentGeneration == intentGeneration;
            });
        if (dispatched == entry.recentDispatchedValues.end()) return false;
        entry.recentDispatchedValues.erase(dispatched);
        if (entry.unsent || intentGeneration != entry.nextIntentGeneration)
            return false;
    }
    entry.pending = false;
    entry.unsent = false;
    entry.suppressingGuardedEcho = false;
    entry.latestTargetDispatchedAt = 0;
    entry.targetValue = entry.authoritativeValue;
    entry.lastAccess = ++accessClock_;
    entry.lastAdjustment = nowMilliseconds;
    ++presentationRevision_;
    return true;
}

SliderInteractionState::Entry* SliderInteractionState::FindAndSynchronize(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds,
    SliderReconciliation* const reconciliation) {
    const auto key = Key(slider);
    const auto position = entries_.find(key);
    if (position == entries_.end()) return nullptr;
    auto& entry = position->second;
    ExpireDispatchHistory(entry, nowMilliseconds);
    if (entry.pending && !entry.unsent) {
        const bool guardStillApplies = entry.suppressingGuardedEcho &&
            MatchesRecentDispatch(entry, entry.authoritativeValue);
        const bool targetStillAwaiting = !entry.suppressingGuardedEcho &&
            entry.latestTargetDispatchedAt != 0 &&
            nowMilliseconds >= entry.latestTargetDispatchedAt &&
            nowMilliseconds - entry.latestTargetDispatchedAt <=
                PendingTimeoutMilliseconds;
        if (!guardStillApplies && !targetStillAwaiting) {
            const bool visualChanged = !Near(
                entry.targetValue, entry.authoritativeValue,
                entry.maximum - entry.minimum);
            entry.pending = false;
            entry.suppressingGuardedEcho = false;
            entry.latestTargetDispatchedAt = 0;
            entry.targetValue = entry.authoritativeValue;
            if (visualChanged) ++presentationRevision_;
            if (reconciliation) *reconciliation = {true, visualChanged};
        }
    }
    if (slider.snapshotSequence < entry.snapshotSequence) {
        // The host processes snapshots and controller input on one UI thread,
        // so a sequence regression cannot be an in-flight stale read. Workers
        // legitimately restart with the same installed instance ID and reset
        // their sequence; treat that as a fresh authoritative session.
        const bool presentationChanged = entry.pending;
        entry = {
            slider.minimum, slider.maximum, slider.step, slider.value, slider.value,
            slider.snapshotSequence, 0, std::wstring{slider.valueChangedActionId},
            std::wstring{slider.widgetInstanceId},
            std::wstring{slider.inputScopeId}, std::wstring{slider.nodeId},
            0, ++accessClock_, false, slider.activationRequired, false,
        };
        if (presentationChanged) {
            ++presentationRevision_;
            if (reconciliation) *reconciliation = {true, true};
        }
        return &entry;
    }
    const bool sameContract =
        Near(entry.minimum, slider.minimum, slider.maximum - slider.minimum) &&
        Near(entry.maximum, slider.maximum, slider.maximum - slider.minimum) &&
        Near(entry.step, slider.step, slider.maximum - slider.minimum) &&
        entry.actionId == slider.valueChangedActionId &&
        entry.activationRequired == slider.activationRequired;
    if (!sameContract) {
        const bool presentationChanged = entry.pending;
        entry = {
            slider.minimum, slider.maximum, slider.step, slider.value, slider.value,
            slider.snapshotSequence, 0, std::wstring{slider.valueChangedActionId},
            std::wstring{slider.widgetInstanceId},
            std::wstring{slider.inputScopeId}, std::wstring{slider.nodeId},
            0, ++accessClock_, false, slider.activationRequired, false,
        };
        if (presentationChanged) {
            ++presentationRevision_;
            if (reconciliation) *reconciliation = {true, true};
        }
    } else {
        const bool newerSnapshot = slider.snapshotSequence > entry.snapshotSequence;
        const bool newerPendingSnapshot = entry.pending && newerSnapshot;
        const bool matchesOptimisticTarget = newerPendingSnapshot && Near(
            slider.value, entry.targetValue, slider.maximum - slider.minimum);
        const bool repeatsPriorAuthority = newerPendingSnapshot && Near(
            slider.value, entry.authoritativeValue,
            slider.maximum - slider.minimum);
        const bool matchesRecentDispatch = newerPendingSnapshot &&
            MatchesRecentDispatch(entry, slider.value);
        // Snapshot sequence is a render serial, not action acknowledgement.
        // A newer render that repeats the prior provider value keeps the latest
        // host-owned target visible. Value evidence settles only when it
        // confirms that target or provides a genuinely different correction.
        const bool authoritativeCorrection = newerPendingSnapshot &&
            !entry.unsent && !matchesOptimisticTarget &&
            !repeatsPriorAuthority && !matchesRecentDispatch;
        if (matchesOptimisticTarget && !entry.unsent) {
            entry.pending = false;
            entry.unsent = false;
            entry.suppressingGuardedEcho = false;
            entry.latestTargetDispatchedAt = 0;
            entry.targetValue = slider.value;
            if (reconciliation) *reconciliation = {true, false};
        } else if (authoritativeCorrection) {
            const bool visualChanged = !Near(
                slider.value, entry.targetValue, slider.maximum - slider.minimum);
            entry.pending = false;
            entry.unsent = false;
            entry.suppressingGuardedEcho = false;
            entry.latestTargetDispatchedAt = 0;
            entry.targetValue = slider.value;
            entry.recentDispatchedValues.clear();
            if (visualChanged) ++presentationRevision_;
            if (reconciliation) *reconciliation = {true, visualChanged};
        } else if (!entry.pending && newerSnapshot &&
                   !Near(slider.value, entry.targetValue,
                         slider.maximum - slider.minimum) &&
                   MatchesRecentDispatch(entry, slider.value)) {
            // Equality with a recently sent value is bounded stale-echo
            // evidence, not an acknowledgement. Keep the confirmed target
            // presented until this value's immutable guard expires.
            entry.pending = true;
            entry.suppressingGuardedEcho = true;
            if (reconciliation) *reconciliation = {true, false};
        } else if (!entry.pending) {
            entry.targetValue = slider.value;
        }
        entry.authoritativeValue = slider.value;
        entry.snapshotSequence = std::max(entry.snapshotSequence, slider.snapshotSequence);
        if ((slider.disabled || slider.busy) && entry.unsent) {
            const bool visualChanged = CancelUnsent(entry);
            if (reconciliation)
                *reconciliation = {true, visualChanged};
        }
        entry.lastAccess = ++accessClock_;
    }
    return &entry;
}

void SliderInteractionState::ExpireDispatchHistory(
    Entry& entry,
    const std::uint64_t nowMilliseconds) {
    std::erase_if(entry.recentDispatchedValues, [&](const DispatchedValue& value) {
        return nowMilliseconds >= value.dispatchedAt &&
            nowMilliseconds - value.dispatchedAt > PendingTimeoutMilliseconds;
    });
}

bool SliderInteractionState::MatchesRecentDispatch(
    const Entry& entry,
    const double value) noexcept {
    return std::any_of(
        entry.recentDispatchedValues.begin(),
        entry.recentDispatchedValues.end(),
        [&](const DispatchedValue& dispatched) {
            return Near(dispatched.value, value, entry.maximum - entry.minimum);
        });
}

bool SliderInteractionState::CancelUnsent(Entry& entry) noexcept {
    if (!entry.pending || !entry.unsent) return false;
    const bool visualChanged = !Near(
        entry.targetValue, entry.authoritativeValue,
        entry.maximum - entry.minimum);
    entry.pending = false;
    entry.unsent = false;
    entry.suppressingGuardedEcho = false;
    entry.latestTargetDispatchedAt = 0;
    entry.targetValue = entry.authoritativeValue;
    if (visualChanged) ++presentationRevision_;
    return visualChanged;
}

SliderInteractionState::Entry* SliderInteractionState::CreateOrSynchronize(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    const auto key = Key(slider);
    if (auto* existing = FindAndSynchronize(slider, nowMilliseconds)) return existing;
    auto [position, inserted] = entries_.try_emplace(key, Entry{
        slider.minimum, slider.maximum, slider.step, slider.value, slider.value,
        slider.snapshotSequence, 0, std::wstring{slider.valueChangedActionId},
        std::wstring{slider.widgetInstanceId},
        std::wstring{slider.inputScopeId}, std::wstring{slider.nodeId},
        0, ++accessClock_, false, slider.activationRequired, false,
    });
    (void)inserted;
    Trim(key);
    const auto retained = entries_.find(key);
    return retained == entries_.end() ? nullptr : &retained->second;
}

bool SliderInteractionState::Valid(const SliderInputDescriptor& slider) noexcept {
    const auto range = slider.maximum - slider.minimum;
    return !slider.widgetInstanceId.empty() && !slider.inputScopeId.empty() &&
        !slider.nodeId.empty() && !slider.valueChangedActionId.empty() &&
        slider.snapshotSequence > 0 && std::isfinite(slider.minimum) &&
        std::isfinite(slider.maximum) && std::isfinite(slider.value) &&
        std::isfinite(slider.step) && slider.minimum < slider.maximum &&
        slider.value >= slider.minimum && slider.value <= slider.maximum &&
        std::isfinite(range) && range > 0.0 &&
        slider.step > 0.0 && slider.step <= range;
}

std::wstring SliderInteractionState::Key(const SliderInputDescriptor& slider) {
    std::wstring key{slider.widgetInstanceId};
    key.push_back(L'\x1f');
    key.append(slider.inputScopeId);
    key.push_back(L'\x1f');
    key.append(slider.nodeId);
    return key;
}

SliderPresentationIdentity SliderInteractionState::Identity(const Entry& entry) {
    return {
        entry.widgetInstanceId,
        entry.inputScopeId,
        entry.nodeId,
        entry.actionId,
        entry.snapshotSequence,
    };
}

void SliderInteractionState::Trim(const std::wstring_view protectedKey) {
    if (entries_.size() <= MaximumEntries) return;
    auto oldest = entries_.end();
    for (auto item = entries_.begin(); item != entries_.end(); ++item) {
        if (item->first == protectedKey || item->second.pending) continue;
        if (oldest == entries_.end() || item->second.lastAccess < oldest->second.lastAccess)
            oldest = item;
    }
    // More than MaximumEntries simultaneous pending sliders is only possible
    // through adversarial synthetic input. Stay hard bounded while preserving
    // the just-adjusted entry and evicting the oldest pending entry as a last
    // resort.
    if (oldest == entries_.end()) {
        for (auto item = entries_.begin(); item != entries_.end(); ++item) {
            if (item->first == protectedKey) continue;
            if (oldest == entries_.end() || item->second.lastAccess < oldest->second.lastAccess)
                oldest = item;
        }
    }
    if (oldest != entries_.end()) {
        if (oldest->second.pending) ++presentationRevision_;
        entries_.erase(oldest);
    }
}

void SliderInteractionState::ForgetWidget(const std::wstring_view widgetInstanceId) noexcept {
    if (widgetInstanceId.empty()) return;
    std::wstring prefix{widgetInstanceId};
    prefix.push_back(L'\x1f');
    const bool presentationChanged = std::any_of(
        entries_.begin(), entries_.end(), [&](const auto& entry) {
            return entry.first.starts_with(prefix) && entry.second.pending;
        });
    std::erase_if(entries_, [&](const auto& entry) { return entry.first.starts_with(prefix); });
    if (presentationChanged) ++presentationRevision_;
}

void SliderInteractionState::RetainAdjustmentMode(
    const std::wstring_view widgetInstanceId,
    const std::wstring_view inputScopeId,
    const std::wstring_view nodeId) noexcept {
    std::wstring retained;
    if (!widgetInstanceId.empty() && !inputScopeId.empty() && !nodeId.empty()) {
        SliderInputDescriptor descriptor;
        descriptor.widgetInstanceId = widgetInstanceId;
        descriptor.inputScopeId = inputScopeId;
        descriptor.nodeId = nodeId;
        retained = Key(descriptor);
    }
    bool changed{};
    for (auto& [key, entry] : entries_) {
        if (key != retained && entry.adjustmentActive) {
            entry.adjustmentActive = false;
            changed = true;
        }
    }
    if (changed) ++presentationRevision_;
}

std::vector<SliderPresentationIdentity> SliderInteractionState::DeactivateAll() {
    std::vector<SliderPresentationIdentity> changed;
    bool presentationChanged{};
    for (auto& [_, entry] : entries_) {
        presentationChanged = presentationChanged || entry.adjustmentActive;
        entry.adjustmentActive = false;
        const bool pending = entry.pending;
        presentationChanged = presentationChanged || pending;
        entry.pending = false;
        entry.unsent = false;
        entry.suppressingGuardedEcho = false;
        entry.latestTargetDispatchedAt = 0;
        entry.recentDispatchedValues.clear();
        if (!pending) continue;
        entry.targetValue = entry.authoritativeValue;
        changed.push_back(Identity(entry));
    }
    if (presentationChanged) ++presentationRevision_;
    return changed;
}

} // namespace widgetrail::input
