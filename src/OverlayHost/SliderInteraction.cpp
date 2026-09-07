#include "SliderInteraction.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <type_traits>
#include <utility>

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
    if (direction != NavigationDirection::Left &&
        direction != NavigationDirection::Right) return {};
    if (!Valid(slider)) return {true};
    const auto result = ApplyUpdate(
        slider, UpdateEvent{UpdateKind::StepInput, direction}, nowMilliseconds);
    return {result.consumed};
}

bool SliderInteractionState::SetRequestedValue(
    const SliderInputDescriptor& slider,
    const double requestedValue,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !std::isfinite(requestedValue) ||
        requestedValue < slider.minimum || requestedValue > slider.maximum)
        return false;
    return ApplyUpdate(
        slider,
        UpdateEvent{
            UpdateKind::AbsoluteInput,
            NavigationDirection::None,
            requestedValue},
        nowMilliseconds).consumed;
}

std::optional<SliderDispatch> SliderInteractionState::TakePendingDispatch(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds,
    const bool force) {
    if (!Valid(slider)) return std::nullopt;
    return ApplyUpdate(
        slider,
        UpdateEvent{
            UpdateKind::PumpDue,
            NavigationDirection::None,
            std::nullopt,
            0,
            force},
        nowMilliseconds).dispatch;
}

bool SliderInteractionState::EnterAdjustmentMode(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !slider.activationRequired) return false;
    RetainAdjustmentMode(slider.widgetInstanceId, slider.inputScopeId, slider.nodeId);
    return ApplyUpdate(
        slider, UpdateEvent{UpdateKind::EnterAdjustment}, nowMilliseconds).consumed;
}

bool SliderInteractionState::ExitAdjustmentMode(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider) || !slider.activationRequired) return false;
    return ApplyUpdate(
        slider, UpdateEvent{UpdateKind::ExitAdjustment}, nowMilliseconds).consumed;
}

bool SliderInteractionState::AdjustmentModeActive(
    const SliderInputDescriptor& slider) const {
    const auto* entry = FindCurrentEntry(slider);
    return entry && entry->activationRequired &&
        entry->adjustmentMode == AdjustmentMode::Active;
}

std::optional<double> SliderInteractionState::PresentationValue(
    const SliderInputDescriptor& slider) const {
    const auto* entry = FindCurrentEntry(slider);
    return entry ? PresentedValue(*entry) : std::nullopt;
}

std::optional<std::uint64_t>
SliderInteractionState::NextReconcileDeadline() const noexcept {
    std::optional<std::uint64_t> deadline;
    const auto consider = [&](const std::uint64_t candidate) {
        if (!deadline || candidate < *deadline) deadline = candidate;
    };
    for (const auto& [_, entry] : entries_) {
        std::visit([&](const auto& state) {
            using T = std::decay_t<decltype(state)>;
            if constexpr (std::is_same_v<T, SettlingValue>) {
                consider(DeadlineAfter(
                    state.lastActualChange, SettlementDelayMilliseconds));
            } else if constexpr (std::is_same_v<T, DispatchedValue>) {
                consider(state.expiresAt);
            } else if constexpr (std::is_same_v<T, GuardedEchoValue>) {
                consider(state.guardUntil);
            }
        }, entry.valueState);
    }
    return deadline;
}

SliderReconciliation SliderInteractionState::Reconcile(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider)) return {};
    const auto result = ApplyUpdate(
        slider, UpdateEvent{UpdateKind::SnapshotAdmission}, nowMilliseconds);
    return {result.stateChanged, result.visualChanged};
}

bool SliderInteractionState::CancelPending(
    const SliderInputDescriptor& slider,
    const std::uint64_t nowMilliseconds) {
    if (!Valid(slider)) return false;
    return ApplyUpdate(
        slider, UpdateEvent{UpdateKind::SynchronousReject}, nowMilliseconds).consumed;
}

bool SliderInteractionState::CancelPending(
    const SliderPresentationIdentity& identity,
    const std::uint64_t intentGeneration,
    const std::uint64_t nowMilliseconds) {
    if (identity.widgetInstanceId.empty() || identity.inputScopeId.empty() ||
        identity.nodeId.empty() || identity.valueChangedActionId.empty() ||
        identity.snapshotSequence <= 0) return false;
    std::wstring key{identity.widgetInstanceId};
    key.push_back(L'\x1f');
    key.append(identity.inputScopeId);
    key.push_back(L'\x1f');
    key.append(identity.nodeId);
    const auto position = entries_.find(key);
    if (position == entries_.end()) return false;
    auto& entry = position->second;
    if (entry.actionId != identity.valueChangedActionId ||
        (intentGeneration == 0 &&
         entry.snapshotSequence != identity.snapshotSequence)) return false;
    return ApplyUpdate(
        entry, nullptr,
        UpdateEvent{
            UpdateKind::SynchronousReject,
            NavigationDirection::None,
            std::nullopt,
            intentGeneration},
        nowMilliseconds).consumed;
}

SliderInteractionState::Entry* SliderInteractionState::AcquireForUpdate(
    const SliderInputDescriptor& slider) {
    const auto key = Key(slider);
    auto position = entries_.find(key);
    if (position == entries_.end()) {
        Entry entry;
        entry.minimum = slider.minimum;
        entry.maximum = slider.maximum;
        entry.step = slider.step;
        entry.latestObservedValue = slider.value;
        entry.snapshotSequence = slider.snapshotSequence;
        entry.actionId = slider.valueChangedActionId;
        entry.widgetInstanceId = slider.widgetInstanceId;
        entry.inputScopeId = slider.inputScopeId;
        entry.nodeId = slider.nodeId;
        entry.lastAccess = ++accessClock_;
        entry.activationRequired = slider.activationRequired;
        position = entries_.try_emplace(key, std::move(entry)).first;
        Trim(key);
        position = entries_.find(key);
        return position == entries_.end() ? nullptr : &position->second;
    }

    auto& entry = position->second;
    const bool sameContract =
        slider.snapshotSequence >= entry.snapshotSequence &&
        Near(entry.minimum, slider.minimum, slider.maximum - slider.minimum) &&
        Near(entry.maximum, slider.maximum, slider.maximum - slider.minimum) &&
        Near(entry.step, slider.step, slider.maximum - slider.minimum) &&
        entry.actionId == slider.valueChangedActionId &&
        entry.activationRequired == slider.activationRequired;
    if (!sameContract) {
        const bool presentationChanged =
            PresentedValue(entry).has_value() ||
            entry.adjustmentMode == AdjustmentMode::Active;
        entry.minimum = slider.minimum;
        entry.maximum = slider.maximum;
        entry.step = slider.step;
        entry.latestObservedValue = slider.value;
        entry.snapshotSequence = slider.snapshotSequence;
        entry.actionId = slider.valueChangedActionId;
        entry.widgetInstanceId = slider.widgetInstanceId;
        entry.inputScopeId = slider.inputScopeId;
        entry.nodeId = slider.nodeId;
        entry.activationRequired = slider.activationRequired;
        entry.adjustmentMode = AdjustmentMode::Inactive;
        entry.valueState = AuthoritativeValue{};
        entry.nextIntentGeneration = 0;
        entry.recentSentValues.clear();
        if (presentationChanged) ++presentationRevision_;
    }
    entry.lastAccess = ++accessClock_;
    return &entry;
}

const SliderInteractionState::Entry* SliderInteractionState::FindCurrentEntry(
    const SliderInputDescriptor& slider) const {
    if (!Valid(slider)) return nullptr;
    const auto position = entries_.find(Key(slider));
    if (position == entries_.end()) return nullptr;
    const auto& entry = position->second;
    const bool exact = entry.snapshotSequence == slider.snapshotSequence &&
        Near(entry.minimum, slider.minimum, slider.maximum - slider.minimum) &&
        Near(entry.maximum, slider.maximum, slider.maximum - slider.minimum) &&
        Near(entry.step, slider.step, slider.maximum - slider.minimum) &&
        entry.actionId == slider.valueChangedActionId &&
        entry.activationRequired == slider.activationRequired;
    return exact ? &entry : nullptr;
}

SliderInteractionState::UpdateResult SliderInteractionState::ApplyUpdate(
    const SliderInputDescriptor& slider,
    const UpdateEvent event,
    const std::uint64_t nowMilliseconds) {
    auto* entry = AcquireForUpdate(slider);
    return entry
        ? ApplyUpdate(*entry, &slider, event, nowMilliseconds)
        : UpdateResult{};
}

SliderInteractionState::UpdateResult SliderInteractionState::ApplyUpdate(
    Entry& entry,
    const SliderInputDescriptor* slider,
    const UpdateEvent event,
    const std::uint64_t nowMilliseconds) {
    UpdateResult result;
    const auto beforeProjected = PresentedValue(entry);
    const auto beforeObserved = entry.latestObservedValue;
    const auto beforeMode = entry.adjustmentMode;
    bool valueStateChanged{};
    const auto transition = [&](ValueState state) {
        entry.valueState = std::move(state);
        valueStateChanged = true;
    };

    double priorObserved = entry.latestObservedValue;
    bool newerSnapshot{};
    if (slider && slider->snapshotSequence > entry.snapshotSequence) {
        newerSnapshot = true;
        priorObserved = entry.latestObservedValue;
        entry.latestObservedValue = slider->value;
        entry.snapshotSequence = slider->snapshotSequence;
    }

    PruneRecentSentValues(entry, nowMilliseconds);
    if (const auto* dispatched = std::get_if<DispatchedValue>(&entry.valueState);
        dispatched && nowMilliseconds >= dispatched->expiresAt) {
        transition(AuthoritativeValue{});
    } else if (const auto* guarded = std::get_if<GuardedEchoValue>(&entry.valueState);
               guarded && nowMilliseconds >= guarded->guardUntil) {
        transition(AuthoritativeValue{});
    }

    if (newerSnapshot) {
        if (std::holds_alternative<AuthoritativeValue>(entry.valueState)) {
            if (!Near(entry.latestObservedValue, priorObserved,
                      entry.maximum - entry.minimum)) {
                if (const auto guard = GuardExpiryForValue(
                        entry, entry.latestObservedValue)) {
                    transition(GuardedEchoValue{priorObserved, *guard});
                } else {
                    entry.recentSentValues.clear();
                }
            }
        } else if (std::holds_alternative<SettlingValue>(entry.valueState)) {
            if (slider->disabled || slider->busy)
                transition(AuthoritativeValue{});
        } else if (const auto* dispatched =
                       std::get_if<DispatchedValue>(&entry.valueState)) {
            if (Near(entry.latestObservedValue, dispatched->target,
                     entry.maximum - entry.minimum)) {
                transition(AuthoritativeValue{});
            } else if (!Near(entry.latestObservedValue, priorObserved,
                             entry.maximum - entry.minimum) &&
                       !MatchesRecentSentValue(entry, entry.latestObservedValue)) {
                entry.recentSentValues.clear();
                transition(AuthoritativeValue{});
            }
        } else if (const auto* guarded =
                       std::get_if<GuardedEchoValue>(&entry.valueState)) {
            if (Near(entry.latestObservedValue, guarded->presentedValue,
                     entry.maximum - entry.minimum)) {
                transition(AuthoritativeValue{});
            } else if (!Near(entry.latestObservedValue, priorObserved,
                             entry.maximum - entry.minimum)) {
                if (const auto guard = GuardExpiryForValue(
                        entry, entry.latestObservedValue)) {
                    transition(GuardedEchoValue{
                        guarded->presentedValue, *guard});
                } else {
                    entry.recentSentValues.clear();
                    transition(AuthoritativeValue{});
                }
            }
        }
    }

    switch (event.kind) {
    case UpdateKind::StepInput: {
        result.consumed = true;
        if (!slider || slider->disabled || slider->busy) break;
        if (entry.activationRequired &&
            entry.adjustmentMode != AdjustmentMode::Active) {
            result.consumed = false;
            break;
        }
        const auto current = PresentedValue(entry).value_or(
            entry.latestObservedValue);
        const auto target = StepTarget(
            current, entry.minimum, entry.maximum, entry.step, event.direction);
        if (!target || Near(*target, current, entry.maximum - entry.minimum)) break;
        if (std::holds_alternative<SettlingValue>(entry.valueState) &&
            Near(*target, entry.latestObservedValue,
                 entry.maximum - entry.minimum) &&
            !HasSentDifferentValue(entry, *target)) {
            transition(AuthoritativeValue{});
        } else {
            transition(SettlingValue{*target, nowMilliseconds, event.direction});
        }
        break;
    }
    case UpdateKind::AbsoluteInput: {
        if (!slider || !event.absoluteValue || slider->disabled || slider->busy)
            break;
        result.consumed = true;
        const auto current = PresentedValue(entry).value_or(
            entry.latestObservedValue);
        if (Near(*event.absoluteValue, current, entry.maximum - entry.minimum))
            break;
        if (std::holds_alternative<SettlingValue>(entry.valueState) &&
            Near(*event.absoluteValue, entry.latestObservedValue,
                 entry.maximum - entry.minimum) &&
            !HasSentDifferentValue(entry, *event.absoluteValue)) {
            transition(AuthoritativeValue{});
        } else {
            transition(SettlingValue{
                *event.absoluteValue, nowMilliseconds,
                NavigationDirection::None});
        }
        break;
    }
    case UpdateKind::PumpDue: {
        auto* settling = std::get_if<SettlingValue>(&entry.valueState);
        if (!settling) break;
        if (!slider || slider->disabled || slider->busy) {
            transition(AuthoritativeValue{});
            break;
        }
        if (!event.force && nowMilliseconds < DeadlineAfter(
                settling->lastActualChange, SettlementDelayMilliseconds)) break;
        const auto generation = ++entry.nextIntentGeneration;
        const auto expiresAt = DeadlineAfter(
            nowMilliseconds, PendingTimeoutMilliseconds);
        const auto dispatch = SliderDispatch{
            settling->target, generation, settling->direction};
        entry.recentSentValues.push_back({
            settling->target, generation, expiresAt});
        if (entry.recentSentValues.size() > MaximumRecentDispatchedValues)
            entry.recentSentValues.pop_front();
        transition(DispatchedValue{
            settling->target, generation, expiresAt});
        result.consumed = true;
        result.dispatch = dispatch;
        break;
    }
    case UpdateKind::SynchronousReject: {
        if (event.intentGeneration == 0) {
            if (!std::holds_alternative<AuthoritativeValue>(entry.valueState)) {
                entry.recentSentValues.clear();
                transition(AuthoritativeValue{});
                result.consumed = true;
            }
            break;
        }
        const auto rejected = std::find_if(
            entry.recentSentValues.begin(), entry.recentSentValues.end(),
            [&](const RecentSentValue& sent) {
                return sent.intentGeneration == event.intentGeneration;
            });
        if (rejected == entry.recentSentValues.end()) break;
        entry.recentSentValues.erase(rejected);
        result.consumed = true;
        if (const auto* dispatched =
                std::get_if<DispatchedValue>(&entry.valueState);
            dispatched && dispatched->intentGeneration == event.intentGeneration) {
            transition(AuthoritativeValue{});
        } else if (const auto* guarded =
                       std::get_if<GuardedEchoValue>(&entry.valueState)) {
            if (const auto guard = GuardExpiryForValue(
                    entry, entry.latestObservedValue)) {
                transition(GuardedEchoValue{
                    guarded->presentedValue, *guard});
            } else {
                transition(AuthoritativeValue{});
            }
        }
        break;
    }
    case UpdateKind::RetireValue:
        result.consumed = !std::holds_alternative<AuthoritativeValue>(
            entry.valueState) || !entry.recentSentValues.empty() ||
            entry.adjustmentMode == AdjustmentMode::Active;
        entry.recentSentValues.clear();
        transition(AuthoritativeValue{});
        entry.adjustmentMode = AdjustmentMode::Inactive;
        break;
    case UpdateKind::EnterAdjustment:
        if (slider && entry.activationRequired && !slider->disabled &&
            !slider->busy && entry.adjustmentMode == AdjustmentMode::Inactive) {
            entry.adjustmentMode = AdjustmentMode::Active;
            result.consumed = true;
        }
        break;
    case UpdateKind::ExitAdjustment:
        if (entry.adjustmentMode == AdjustmentMode::Active) {
            entry.adjustmentMode = AdjustmentMode::Inactive;
            result.consumed = true;
        }
        break;
    case UpdateKind::SnapshotAdmission:
        break;
    }

    const auto afterProjected = PresentedValue(entry);
    const auto afterObserved = entry.latestObservedValue;
    const bool valueVisualChanged =
        (beforeProjected.has_value() || afterProjected.has_value()) &&
        !Near(
            beforeProjected.value_or(beforeObserved),
            afterProjected.value_or(afterObserved),
            entry.maximum - entry.minimum);
    const bool modeChanged = beforeMode != entry.adjustmentMode;
    result.stateChanged = valueStateChanged || modeChanged;
    result.visualChanged = valueVisualChanged || modeChanged;
    if (result.visualChanged) ++presentationRevision_;
    entry.lastAccess = ++accessClock_;
    return result;
}

std::optional<double> SliderInteractionState::PresentedValue(
    const Entry& entry) noexcept {
    return std::visit([](const auto& state) -> std::optional<double> {
        using T = std::decay_t<decltype(state)>;
        if constexpr (std::is_same_v<T, SettlingValue> ||
                      std::is_same_v<T, DispatchedValue>) {
            return state.target;
        } else if constexpr (std::is_same_v<T, GuardedEchoValue>) {
            return state.presentedValue;
        } else {
            return std::nullopt;
        }
    }, entry.valueState);
}

bool SliderInteractionState::MatchesRecentSentValue(
    const Entry& entry,
    const double value) noexcept {
    return GuardExpiryForValue(entry, value).has_value();
}

std::optional<std::uint64_t> SliderInteractionState::GuardExpiryForValue(
    const Entry& entry,
    const double value) noexcept {
    std::optional<std::uint64_t> expiry;
    for (const auto& sent : entry.recentSentValues) {
        if (!Near(sent.value, value, entry.maximum - entry.minimum)) continue;
        if (!expiry || sent.expiresAt > *expiry) expiry = sent.expiresAt;
    }
    return expiry;
}

void SliderInteractionState::PruneRecentSentValues(
    Entry& entry,
    const std::uint64_t nowMilliseconds) {
    std::erase_if(entry.recentSentValues, [&](const RecentSentValue& sent) {
        return nowMilliseconds >= sent.expiresAt;
    });
}

std::uint64_t SliderInteractionState::DeadlineAfter(
    const std::uint64_t start,
    const std::uint64_t delay) noexcept {
    return start > std::numeric_limits<std::uint64_t>::max() - delay
        ? std::numeric_limits<std::uint64_t>::max()
        : start + delay;
}

bool SliderInteractionState::HasSentDifferentValue(
    const Entry& entry,
    const double value) noexcept {
    return std::any_of(
        entry.recentSentValues.begin(), entry.recentSentValues.end(),
        [&](const RecentSentValue& sent) {
            return !Near(sent.value, value, entry.maximum - entry.minimum);
        });
}

bool SliderInteractionState::Valid(
    const SliderInputDescriptor& slider) noexcept {
    const auto range = slider.maximum - slider.minimum;
    return !slider.widgetInstanceId.empty() && !slider.inputScopeId.empty() &&
        !slider.nodeId.empty() && !slider.valueChangedActionId.empty() &&
        slider.snapshotSequence > 0 && std::isfinite(slider.minimum) &&
        std::isfinite(slider.maximum) && std::isfinite(slider.value) &&
        std::isfinite(slider.step) && slider.minimum < slider.maximum &&
        slider.value >= slider.minimum && slider.value <= slider.maximum &&
        std::isfinite(range) && range > 0.0 && slider.step > 0.0 &&
        slider.step <= range;
}

std::wstring SliderInteractionState::Key(
    const SliderInputDescriptor& slider) {
    std::wstring key{slider.widgetInstanceId};
    key.push_back(L'\x1f');
    key.append(slider.inputScopeId);
    key.push_back(L'\x1f');
    key.append(slider.nodeId);
    return key;
}

SliderPresentationIdentity SliderInteractionState::Identity(
    const Entry& entry) {
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
        const bool transient =
            !std::holds_alternative<AuthoritativeValue>(item->second.valueState) ||
            item->second.adjustmentMode == AdjustmentMode::Active;
        if (item->first == protectedKey || transient) continue;
        if (oldest == entries_.end() ||
            item->second.lastAccess < oldest->second.lastAccess) oldest = item;
    }
    if (oldest == entries_.end()) {
        for (auto item = entries_.begin(); item != entries_.end(); ++item) {
            if (item->first == protectedKey) continue;
            if (oldest == entries_.end() ||
                item->second.lastAccess < oldest->second.lastAccess) oldest = item;
        }
    }
    if (oldest != entries_.end()) {
        if (PresentedValue(oldest->second).has_value() ||
            oldest->second.adjustmentMode == AdjustmentMode::Active)
            ++presentationRevision_;
        entries_.erase(oldest);
    }
}

void SliderInteractionState::ForgetWidget(
    const std::wstring_view widgetInstanceId) noexcept {
    if (widgetInstanceId.empty()) return;
    std::wstring prefix{widgetInstanceId};
    prefix.push_back(L'\x1f');
    const bool presentationChanged = std::any_of(
        entries_.begin(), entries_.end(), [&](const auto& item) {
            return item.first.starts_with(prefix) &&
                (PresentedValue(item.second).has_value() ||
                 item.second.adjustmentMode == AdjustmentMode::Active);
        });
    std::erase_if(entries_, [&](const auto& item) {
        return item.first.starts_with(prefix);
    });
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
        if (key == retained || entry.adjustmentMode == AdjustmentMode::Inactive)
            continue;
        (void)ApplyUpdate(
            entry, nullptr, UpdateEvent{UpdateKind::ExitAdjustment}, 0);
    }
}

std::vector<SliderPresentationIdentity> SliderInteractionState::DeactivateAll() {
    std::vector<SliderPresentationIdentity> changed;
    for (auto& [_, entry] : entries_) {
        const bool hadPresentedOverride = PresentedValue(entry).has_value();
        (void)ApplyUpdate(
            entry, nullptr, UpdateEvent{UpdateKind::RetireValue}, 0);
        if (hadPresentedOverride) changed.push_back(Identity(entry));
    }
    return changed;
}

} // namespace widgetrail::input
