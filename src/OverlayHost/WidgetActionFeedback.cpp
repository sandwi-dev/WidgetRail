#include "WidgetActionFeedback.h"

#include "WidgetBridgeClient.h"

#include <algorithm>
#include <limits>
#include <utility>

namespace widgetrail {

bool WidgetActionFeedbackStore::Publish(
    const std::wstring_view widgetId,
    const std::wstring_view runtimeGeneration,
    std::wstring message,
    const std::uint64_t now,
    const std::uint64_t duration) {
    if (widgetId.empty() || runtimeGeneration.empty() || message.empty() || duration == 0) {
        return false;
    }
    const auto existing = entries_.find(widgetId);
    if (existing == entries_.end() && entries_.size() == MaximumWidgets) return false;

    const auto maximum = std::numeric_limits<std::uint64_t>::max();
    const auto expiresAt = duration > maximum - now ? maximum : now + duration;
    entries_.insert_or_assign(
        std::wstring{widgetId},
        Entry{std::wstring{runtimeGeneration}, std::move(message), expiresAt});
    return true;
}

std::optional<std::wstring_view> WidgetActionFeedbackStore::MessageFor(
    const std::wstring_view widgetId,
    const std::wstring_view runtimeGeneration,
    const std::uint64_t now) const noexcept {
    const auto entry = entries_.find(widgetId);
    if (entry == entries_.end() || entry->second.runtimeGeneration != runtimeGeneration ||
        entry->second.expiresAt <= now) {
        return std::nullopt;
    }
    return std::wstring_view{entry->second.message};
}

bool WidgetActionFeedbackStore::Expire(const std::uint64_t now) noexcept {
    const auto before = entries_.size();
    std::erase_if(entries_, [now](const auto& entry) {
        return entry.second.expiresAt <= now;
    });
    return entries_.size() != before;
}

std::optional<std::uint64_t> WidgetActionFeedbackStore::NextExpiry() const noexcept {
    std::optional<std::uint64_t> next;
    for (const auto& pair : entries_) {
        const auto& entry = pair.second;
        if (!next || entry.expiresAt < *next) next = entry.expiresAt;
    }
    return next;
}

bool WidgetActionFeedbackStore::Forget(const std::wstring_view widgetId) noexcept {
    const auto entry = entries_.find(widgetId);
    if (entry == entries_.end()) return false;
    entries_.erase(entry);
    return true;
}

bool WidgetActionFeedbackStore::Clear() noexcept {
    const bool changed = !entries_.empty();
    entries_.clear();
    return changed;
}

WidgetActionFeedbackTransition WidgetActionFeedbackController::Hide() noexcept {
    visible_ = false;
    (void)store_.Clear();
    return {};
}

WidgetActionFeedbackTransition WidgetActionFeedbackController::Publish(
    const std::wstring_view widgetId,
    const std::wstring_view runtimeGeneration,
    std::wstring message,
    const std::uint64_t now,
    const std::uint64_t duration) {
    if (!visible_) return {};
    return Transition(store_.Publish(
        widgetId, runtimeGeneration, std::move(message), now, duration));
}

WidgetActionFeedbackTransition WidgetActionFeedbackController::Expire(
    const std::uint64_t now) noexcept {
    return Transition(store_.Expire(now));
}

WidgetActionFeedbackTransition WidgetActionFeedbackController::Forget(
    const std::wstring_view widgetId) noexcept {
    return Transition(store_.Forget(widgetId));
}

std::optional<std::wstring_view> WidgetActionFeedbackController::MessageFor(
    const std::wstring_view widgetId,
    const std::wstring_view runtimeGeneration,
    const std::uint64_t now) const noexcept {
    return visible_ ? store_.MessageFor(widgetId, runtimeGeneration, now) : std::nullopt;
}

std::optional<std::uint64_t> WidgetActionFeedbackController::NextExpiry() const noexcept {
    return visible_ ? store_.NextExpiry() : std::nullopt;
}

WidgetActionFeedbackTransition WidgetActionFeedbackController::Transition(
    const bool changed) const noexcept {
    return {visible_ && changed, NextExpiry()};
}

WidgetActionFeedbackHost::WidgetActionFeedbackHost(
    WidgetActionFeedbackHostCallbacks callbacks)
    : callbacks_(std::move(callbacks)) {}

void WidgetActionFeedbackHost::Show() {
    controller_.Show();
    Apply(false);
}

void WidgetActionFeedbackHost::Hide() {
    (void)controller_.Hide();
    Apply(false);
}

void WidgetActionFeedbackHost::Stop() {
    (void)controller_.Hide();
    catalog_.clear();
    Apply(false);
}

bool WidgetActionFeedbackHost::ReconcileCatalog(
    const std::vector<WidgetDescriptor>& descriptors) {
    if (descriptors.size() > WidgetActionFeedbackStore::MaximumWidgets) return false;

    std::map<std::wstring, CatalogEntry, std::less<>> next;
    for (const auto& descriptor : descriptors) {
        if (descriptor.id.empty() || descriptor.name.empty() ||
            descriptor.runtimeGeneration.empty()) {
            return false;
        }
        if (!next.emplace(
                descriptor.id,
                CatalogEntry{descriptor.name, descriptor.runtimeGeneration}).second) {
            return false;
        }
    }

    bool changed = false;
    for (const auto& [widgetId, entry] : catalog_) {
        const auto replacement = next.find(widgetId);
        if (replacement == next.end() ||
            replacement->second.runtimeGeneration != entry.runtimeGeneration) {
            changed = controller_.Forget(widgetId).shouldInvalidate || changed;
        }
    }
    catalog_ = std::move(next);
    Apply(changed);
    return true;
}

WidgetActionFeedbackBatchResult WidgetActionFeedbackHost::PublishBridgeFailures(
    const std::vector<WidgetActionFailure>& failures) {
    static_assert(
        WidgetActionFeedbackBatchResult::MaximumFailures ==
        WidgetActionFailureQueue::MaximumFailures);

    WidgetActionFeedbackBatchResult result;
    result.count = std::min(
        failures.size(), WidgetActionFeedbackBatchResult::MaximumFailures);
    const auto now = callbacks_.now ? callbacks_.now() : 0;
    bool changed = false;
    for (std::size_t index = 0; index < result.count; ++index) {
        const auto& failure = failures[index];
        const auto descriptor = catalog_.find(failure.widgetId);
        if (descriptor == catalog_.end() ||
            descriptor->second.runtimeGeneration != failure.runtimeGeneration) {
            result.outcomes[index] = WidgetActionFeedbackOutcome::Stale;
            continue;
        }
        const auto transition = controller_.Publish(
            failure.widgetId,
            failure.runtimeGeneration,
            descriptor->second.name + L" action failed; try again",
            now,
            DisplayDurationMilliseconds);
        if (!transition.shouldInvalidate) {
            result.outcomes[index] = WidgetActionFeedbackOutcome::Refused;
            continue;
        }
        result.outcomes[index] = WidgetActionFeedbackOutcome::Published;
        changed = true;
    }
    Apply(changed);
    if (changed && callbacks_.commitAccessibility &&
        callbacks_.commitAccessibility() &&
        callbacks_.raiseAccessibilityEvents) {
        callbacks_.raiseAccessibilityEvents();
    }
    return result;
}

void WidgetActionFeedbackHost::OnDeadlineTimer() {
    ExpireNow();
}

void WidgetActionFeedbackHost::OnControllerTimer() {
    ExpireNow();
}

std::optional<std::wstring_view> WidgetActionFeedbackHost::MessageForSurface(
    const WidgetActionFeedbackSurface surface,
    const std::wstring_view dashboardWidgetId,
    const std::wstring_view openWidgetId) const {
    const auto widgetId = surface == WidgetActionFeedbackSurface::Dashboard
        ? dashboardWidgetId
        : openWidgetId;
    const auto descriptor = catalog_.find(widgetId);
    if (descriptor == catalog_.end()) return std::nullopt;
    const auto now = callbacks_.now ? callbacks_.now() : 0;
    return controller_.MessageFor(
        widgetId, descriptor->second.runtimeGeneration, now);
}

void WidgetActionFeedbackHost::ExpireNow() {
    const auto now = callbacks_.now ? callbacks_.now() : 0;
    Apply(controller_.Expire(now).shouldInvalidate);
}

void WidgetActionFeedbackHost::Apply(const bool shouldInvalidate) {
    if (callbacks_.scheduleExpiry) {
        callbacks_.scheduleExpiry(controller_.NextExpiry());
    }
    if (shouldInvalidate && callbacks_.invalidate) callbacks_.invalidate();
}

} // namespace widgetrail
