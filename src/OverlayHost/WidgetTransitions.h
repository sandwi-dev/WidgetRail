#pragma once

#include "DeclarativeLayout.h"
#include "WidgetProtocolPresentationContract.generated.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <map>
#include <string>
#include <string_view>
#include <utility>

namespace widgetrail {

// One clock for content, navigation layout and modal presentation. No worker
// timers or retained input trees. Copies are cheap and permit transactional
// render planning: only a successfully painted pass publishes this state.
class WidgetTransitionCoordinator final {
public:
    static constexpr std::uint64_t Duration = 180;
    static constexpr std::size_t MaximumGroups = protocol_contract::MaximumWidgetTransitionGroups + 1; // includes modal
    static constexpr std::size_t MaximumLayoutNodes = protocol_contract::MaximumWidgetTransitionLayoutNodes;
    static constexpr std::size_t MaximumBitmapBytes = 64U * 1024U * 1024U;
    static constexpr float ContentTravel = 24.0F;
    static constexpr float ModalTravel = 14.0F;
    struct Offset { float x{}, y{}; };
    struct Sample final {
        float progress{1.0F};
        int direction{1};
        std::uint64_t revision{};
        bool active{};
    };

    bool Begin(std::wstring authority, declarative::Rect viewport, float scale,
        std::uint64_t timestamp, bool reducedMotion) {
        const bool reset = authority_ != authority || viewport_.x != viewport.x || viewport_.y != viewport.y ||
            viewport_.width != viewport.width || viewport_.height != viewport.height || scale_ != scale;
        if (reset)
            Clear();
        authority_ = std::move(authority);
        viewport_ = viewport;
        scale_ = scale;
        now_ = std::max(now_, timestamp);
        reduced_ = reducedMotion;
        ++frame_;
        return reset;
    }

    Sample Observe(const std::wstring& id, const std::wstring& key, int order) {
        auto found = groups_.find(id);
        if (found == groups_.end()) {
            if (groups_.size() >= MaximumGroups) return {};
            found = groups_.emplace(id, Group{key, order, now_, 1, frame_, 1,
                false}).first;
        }
        auto& group = found->second;
        // Inconsistent declarations in one snapshot cannot repeatedly restart a
        // timeline. Managed/native admission also validates the group contract.
        if (group.seen != frame_ && group.key != key) {
            group.direction = order >= group.order ? 1 : -1;
            group.key = key;
            group.order = order;
            group.started = now_;
            ++group.revision;
            group.active = !reduced_;
        }
        group.seen = frame_;
        if (reduced_ || now_ - group.started >= Duration) group.active = false;
        const auto linear = group.active ? static_cast<float>(now_ - group.started) / Duration : 1.0F;
        const auto inverse = 1.0F - linear;
        return {1.0F - inverse * inverse * inverse, group.direction, group.revision, group.active};
    }

    // FLIP-style position motion: layout is resolved once at its final size.
    // A retarget starts at the currently visible position, not its old endpoint.
    Offset LayoutOffset(const std::wstring& id, declarative::Rect rect,
        const Sample sample) {
        const auto visible = ResolveRect(id, rect, sample, false);
        return {visible.x - rect.x, visible.y - rect.y};
    }

    declarative::Rect SelectionRect(const std::wstring& id, declarative::Rect rect, const Sample sample) {
        return ResolveRect(id, rect, sample, true);
    }

    declarative::Rect ResolveRect(const std::wstring& id, declarative::Rect rect,
        const Sample sample, const bool resize) {
        auto found = positions_.find(id);
        if (found == positions_.end()) {
            if (positions_.size() >= MaximumLayoutNodes + MaximumGroups) return rect;
            found = positions_.emplace(id, Position{rect, {}, sample.revision, frame_}).first;
        }
        auto& position = found->second;
        if (position.revision != sample.revision) {
            position.from = {position.visible.x - rect.x, position.visible.y - rect.y,
                resize ? position.visible.width - rect.width : 0, resize ? position.visible.height - rect.height : 0};
            position.revision = sample.revision;
        }
        position.seen = frame_;
        const auto remaining = 1 - sample.progress;
        position.visible = {rect.x + position.from.x * remaining, rect.y + position.from.y * remaining,
            rect.width + position.from.width * remaining, rect.height + position.from.height * remaining};
        return position.visible;
    }

    void End() {
        std::erase_if(groups_, [&](const auto& entry) { return entry.second.seen != frame_; });
        std::erase_if(positions_, [&](const auto& entry) { return entry.second.seen != frame_; });
    }
    void Clear() noexcept { groups_.clear(); positions_.clear(); authority_.clear(); now_ = 0; }
    [[nodiscard]] bool OwnsInstance(std::wstring_view instance) const noexcept {
        return authority_.size() > instance.size() && authority_.starts_with(instance) &&
            authority_[instance.size()] == L'\x1f';
    }

private:
    struct Group {
        std::wstring key;
        int order{};
        std::uint64_t started{}, revision{}, seen{};
        int direction{1};
        bool active{};
    };
    struct Position {
        declarative::Rect visible;
        declarative::Rect from;
        std::uint64_t revision{}, seen{};
    };
    std::map<std::wstring, Group> groups_;
    std::map<std::wstring, Position> positions_;
    std::wstring authority_;
    declarative::Rect viewport_;
    float scale_{};
    std::uint64_t now_{}, frame_{};
    bool reduced_{};
};
} // namespace widgetrail
