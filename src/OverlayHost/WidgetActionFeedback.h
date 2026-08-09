#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>

namespace gba {

/// Bounded, generation-owned presentation state for asynchronous widget action
/// failures. The bridge queue preserves delivery order; this store preserves the
/// newest failure independently for every installed widget until its deadline.
class WidgetActionFeedbackStore final {
public:
    static constexpr std::size_t MaximumWidgets = 256;

    [[nodiscard]] bool Publish(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        std::wstring message,
        std::uint64_t now,
        std::uint64_t duration);

    [[nodiscard]] std::optional<std::wstring_view> MessageFor(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        std::uint64_t now) const noexcept;

    /// Removes all entries whose deadline has passed. Returns true exactly when
    /// visible state changed, allowing the host to request one repaint per batch.
    [[nodiscard]] bool Expire(std::uint64_t now) noexcept;
    [[nodiscard]] std::optional<std::uint64_t> NextExpiry() const noexcept;
    [[nodiscard]] bool Forget(std::wstring_view widgetId) noexcept;
    [[nodiscard]] bool Clear() noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return entries_.size(); }

private:
    struct TransparentStringHash final {
        using is_transparent = void;
        [[nodiscard]] std::size_t operator()(const std::wstring_view value) const noexcept {
            return std::hash<std::wstring_view>{}(value);
        }
    };

    struct TransparentStringEqual final {
        using is_transparent = void;
        [[nodiscard]] bool operator()(
            const std::wstring_view left,
            const std::wstring_view right) const noexcept {
            return left == right;
        }
    };

    struct Entry final {
        std::wstring runtimeGeneration;
        std::wstring message;
        std::uint64_t expiresAt{};
    };

    std::unordered_map<
        std::wstring,
        Entry,
        TransparentStringHash,
        TransparentStringEqual> entries_;
};

struct WidgetActionFeedbackTransition final {
    bool shouldInvalidate{};
    std::optional<std::uint64_t> nextExpiry;
};

/// Pure host-orchestration seam. Time is injected by the caller and timer work
/// is returned as a deadline, so bridge pumps, timer failure fallback, catalog
/// replacement, and hide/show behavior are deterministic without an HWND.
class WidgetActionFeedbackController final {
public:
    void Show() noexcept { visible_ = true; }
    [[nodiscard]] WidgetActionFeedbackTransition Hide() noexcept;
    [[nodiscard]] WidgetActionFeedbackTransition Publish(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        std::wstring message,
        std::uint64_t now,
        std::uint64_t duration);
    [[nodiscard]] WidgetActionFeedbackTransition Expire(std::uint64_t now) noexcept;
    [[nodiscard]] WidgetActionFeedbackTransition Forget(std::wstring_view widgetId) noexcept;
    [[nodiscard]] std::optional<std::wstring_view> MessageFor(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        std::uint64_t now) const noexcept;
    [[nodiscard]] std::optional<std::uint64_t> NextExpiry() const noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return store_.size(); }

private:
    [[nodiscard]] WidgetActionFeedbackTransition Transition(bool changed) const noexcept;

    bool visible_{};
    WidgetActionFeedbackStore store_;
};

} // namespace gba
