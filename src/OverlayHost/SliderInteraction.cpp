#include "SliderInteraction.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace gba::input {
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
    const auto tolerance = std::max(1.0, std::abs(position)) * 1e-10;
    const auto index = direction == NavigationDirection::Right
        ? std::floor(position + tolerance) + 1.0
        : std::ceil(position - tolerance) - 1.0;
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
    if (!Valid(slider)) return {true, std::nullopt};
    auto* entry = CreateOrSynchronize(slider, nowMilliseconds);
    if (!entry) return {true, std::nullopt};
    if (entry->activationRequired && !entry->adjustmentActive)
        return {false, std::nullopt};
    if (slider.disabled || slider.busy) return {true, std::nullopt};
    const auto target = StepTarget(
        entry->targetValue, entry->minimum, entry->maximum, entry->step, direction);
    if (!target || Near(*target, entry->targetValue, entry->maximum - entry->minimum))
        return {true, std::nullopt};
    entry->targetValue = *target;
    entry->pending = true;
    entry->adjustmentSnapshotSequence = slider.snapshotSequence;
    entry->lastAdjustment = nowMilliseconds;
    entry->lastAccess = ++accessClock_;
    ++presentationRevision_;
    return {true, *target};
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
        entry->lastAdjustment = nowMilliseconds;
        entry->lastAccess = ++accessClock_;
        return true;
    }
    if (!entry->pending &&
        Near(requestedValue, entry->authoritativeValue,
             entry->maximum - entry->minimum)) {
        return true;
    }
    entry->targetValue = requestedValue;
    entry->pending = true;
    entry->adjustmentSnapshotSequence = slider.snapshotSequence;
    entry->lastAdjustment = nowMilliseconds;
    entry->lastAccess = ++accessClock_;
    ++presentationRevision_;
    return true;
}

bool SliderInteractionState::EnterAdjustmentMode(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !slider.activationRequired) return false;
    auto* entry = CreateOrSynchronize(slider, nowMilliseconds);
    if (!entry || slider.disabled || slider.busy) return false;
    RetainAdjustmentMode(slider.widgetInstanceId, slider.inputScopeId, slider.nodeId);
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
        const auto candidate = entry.lastAdjustment + PendingTimeoutMilliseconds + 1;
        if (!deadline || candidate < *deadline) deadline = candidate;
    }
    return deadline;
}

bool SliderInteractionState::ExpireTimedOut(
    const std::uint64_t nowMilliseconds) noexcept {
    bool presentationChanged{};
    for (auto& [_, entry] : entries_) {
        if (!entry.pending || nowMilliseconds < entry.lastAdjustment ||
            nowMilliseconds - entry.lastAdjustment <= PendingTimeoutMilliseconds) {
            continue;
        }
        entry.pending = false;
        entry.targetValue = entry.authoritativeValue;
        presentationChanged = true;
    }
    if (presentationChanged) ++presentationRevision_;
    return presentationChanged;
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
    const auto position = entries_.find(Key(slider));
    if (position == entries_.end()) return false;
    auto& entry = position->second;
    if (!entry.pending || entry.actionId != slider.valueChangedActionId ||
        entry.adjustmentSnapshotSequence != slider.snapshotSequence) {
        return false;
    }
    entry.pending = false;
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
    if (slider.snapshotSequence < entry.snapshotSequence) {
        // The host processes snapshots and controller input on one UI thread,
        // so a sequence regression cannot be an in-flight stale read. Workers
        // legitimately restart with the same installed instance ID and reset
        // their sequence; treat that as a fresh authoritative session.
        const bool presentationChanged = entry.pending;
        entry = {
            slider.minimum, slider.maximum, slider.step, slider.value, slider.value,
            slider.snapshotSequence, 0, std::wstring{slider.valueChangedActionId},
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
            0, ++accessClock_, false, slider.activationRequired, false,
        };
        if (presentationChanged) {
            ++presentationRevision_;
            if (reconciliation) *reconciliation = {true, true};
        }
    } else {
        const bool authoritativeCompletion = entry.pending &&
            slider.snapshotSequence > entry.adjustmentSnapshotSequence;
        const bool timedOut = entry.pending &&
            nowMilliseconds >= entry.lastAdjustment &&
            nowMilliseconds - entry.lastAdjustment > PendingTimeoutMilliseconds;
        if (authoritativeCompletion || timedOut) {
            const bool visualChanged = !Near(
                slider.value, entry.targetValue, slider.maximum - slider.minimum);
            entry.pending = false;
            entry.targetValue = slider.value;
            if (visualChanged) ++presentationRevision_;
            if (reconciliation) *reconciliation = {true, visualChanged};
        } else if (!entry.pending) {
            entry.targetValue = slider.value;
        }
        entry.authoritativeValue = slider.value;
        entry.snapshotSequence = std::max(entry.snapshotSequence, slider.snapshotSequence);
        entry.lastAccess = ++accessClock_;
    }
    return &entry;
}

SliderInteractionState::Entry* SliderInteractionState::CreateOrSynchronize(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    const auto key = Key(slider);
    if (auto* existing = FindAndSynchronize(slider, nowMilliseconds)) return existing;
    auto [position, inserted] = entries_.try_emplace(key, Entry{
        slider.minimum, slider.maximum, slider.step, slider.value, slider.value,
        slider.snapshotSequence, 0, std::wstring{slider.valueChangedActionId},
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
    for (auto& [key, entry] : entries_) {
        if (key != retained) entry.adjustmentActive = false;
    }
}

bool SliderInteractionState::DeactivateAll() noexcept {
    bool presentationChanged{};
    for (auto& [_, entry] : entries_) {
        entry.adjustmentActive = false;
        if (!entry.pending) continue;
        entry.pending = false;
        entry.targetValue = entry.authoritativeValue;
        presentationChanged = true;
    }
    if (presentationChanged) ++presentationRevision_;
    return presentationChanged;
}

} // namespace gba::input
