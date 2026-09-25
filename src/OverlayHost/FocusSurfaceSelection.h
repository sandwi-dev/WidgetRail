#pragma once

#include "WidgetBridgeClient.h"
#include <algorithm>
#include <map>
#include <string>
#include <vector>

namespace widgetrail::declarative {

// Semantic source selection only. No retained nodes, pixels, input authority,
// or cursor indexes: every source is resolved against the current snapshot.
class FocusSurfaceSelectionMemory final {
public:
    struct Selection final {
        const WidgetNode* surface{};
        const WidgetNode* source{};
        [[nodiscard]] const WidgetNode* Fragment() const noexcept {
            if (source && !source->focusPresentation.empty()) return &source->focusPresentation.front();
            return surface && !surface->defaultFocusPresentation.empty()
                ? &surface->defaultFocusPresentation.front() : nullptr;
        }
    };
    using Selections = std::map<std::wstring, Selection, std::less<>>;

    [[nodiscard]] Selections Resolve(const WidgetSnapshot& snapshot, std::wstring_view focusedId,
        std::wstring_view authority, bool compact) {
        struct Surface { const WidgetNode* node; std::wstring scope; };
        struct Candidate {
            const WidgetNode* node;
            const WidgetNode* background;
            const WidgetNode* presentation;
            std::wstring identity;
        };
        std::vector<Surface> surfaces;
        std::map<std::wstring, Candidate, std::less<>> candidates;
        const auto visit = [&](const auto& self, const WidgetNode& node,
            const WidgetNode* background, const WidgetNode* presentation,
            std::wstring scope, std::wstring itemPath) -> void {
            if (!(node.visibleWhen.empty() || node.visibleWhen == L"always" ||
                (compact && node.visibleWhen == L"compactOnly") ||
                (!compact && node.visibleWhen == L"expandedOnly"))) return;
            if (!node.inputScopeId.empty()) scope = node.inputScopeId;
            if (!node.collectionItemKey.empty()) itemPath += L"\x1f" + node.collectionItemKey;
            if (node.kind == L"backgroundSurface") { background = &node; surfaces.push_back({&node, scope}); }
            if (node.kind == L"focusPresentationSurface") { presentation = &node; surfaces.push_back({&node, scope}); }
            if (!node.focusBackgroundArtworkHandle.empty() || !node.focusPresentation.empty())
                candidates.emplace(node.id, Candidate{&node, background, presentation, scope + L"\x1f" + node.kind + itemPath});
            // Presentation fragments are deliberately outside focus/source traversal.
            for (const auto& child : node.children) self(self, child, background, presentation, scope, itemPath);
        };
        visit(visit, snapshot.root, nullptr, nullptr, snapshot.root.inputScopeId.empty()
            ? snapshot.root.id : snapshot.root.inputScopeId, {});
        const std::wstring prefix = std::wstring(authority) + L"\x1e" + snapshot.instanceId + L"\x1e";
        // A new authority for the same instance retires the old one permanently.
        std::erase_if(entries_, [&](const auto& pair) {
            return pair.second.instanceId == snapshot.instanceId && !pair.first.starts_with(prefix);
        });
        Selections result;
        std::vector<std::wstring> present;
        for (const auto& surface : surfaces) {
            const bool background = surface.node->kind == L"backgroundSurface";
            const bool consumes = !background || surface.node->usesFocusedDescendantArtwork;
            const bool retains = surface.node->retainLastPresentation.value_or(background);
            const auto key = prefix + surface.scope + L"\x1f" + surface.node->kind + L"\x1f" + surface.node->id;
            present.push_back(key);
            const auto belongs = [&](const Candidate& candidate) {
                return background ? candidate.background == surface.node && !candidate.node->focusBackgroundArtworkHandle.empty()
                    : candidate.presentation == surface.node && !candidate.node->focusPresentation.empty();
            };
            const Candidate* source{};
            const auto focused = candidates.find(focusedId);
            if (consumes && focused != candidates.end() && belongs(focused->second)) source = &focused->second;
            if (!source && consumes && retains) {
                const auto remembered = entries_.find(key);
                if (remembered != entries_.end()) {
                    const auto current = candidates.find(remembered->second.sourceId);
                    if (current != candidates.end() && belongs(current->second) &&
                        current->second.identity == remembered->second.sourceIdentity) source = &current->second;
                }
            }
            if (source && retains) entries_.insert_or_assign(key,
                Entry{snapshot.instanceId, source->node->id, source->identity, ++clock_});
            else entries_.erase(key);
            result.emplace(surface.node->id, Selection{surface.node, source ? source->node : nullptr});
        }
        // A removed surface cannot resurrect an old selection when reintroduced.
        std::erase_if(entries_, [&](const auto& pair) {
            return pair.first.starts_with(prefix) && std::find(present.begin(), present.end(), pair.first) == present.end();
        });
        while (entries_.size() > 256) {
            auto oldest = std::min_element(entries_.begin(), entries_.end(),
                [](const auto& a, const auto& b) { return a.second.lastUse < b.second.lastUse; });
            entries_.erase(oldest);
        }
        return result;
    }

    void Forget(std::wstring_view instanceId) {
        std::erase_if(entries_, [&](const auto& pair) { return pair.second.instanceId == instanceId; });
    }

private:
    struct Entry { std::wstring instanceId, sourceId, sourceIdentity; std::uint64_t lastUse{}; };
    std::map<std::wstring, Entry, std::less<>> entries_;
    std::uint64_t clock_{};
};

} // namespace widgetrail::declarative
