#include "WidgetActionFeedback.h"

#include <algorithm>
#include <limits>

namespace gba {

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

} // namespace gba
