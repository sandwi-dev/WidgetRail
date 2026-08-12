#include "LauncherExperienceAdapter.h"

#include <algorithm>
#include <set>

namespace gba::launcher {
namespace {

const SlotContent* FindContent(const std::vector<SlotContent>& contents, const Slot slot) {
    const auto found = std::find_if(contents.begin(), contents.end(),
        [slot](const SlotContent& item) { return item.slot == slot; });
    return found == contents.end() ? nullptr : &*found;
}

WidgetSnapshot AdaptSnapshot(
    const SlotContent& content,
    const SlotPlacement& placement) {
    auto snapshot = content.snapshot;
    if (placement.slot == Slot::GameRail && placement.orientation)
        snapshot.root.kind = *placement.orientation == Orientation::Vertical ? L"column" : L"row";
    return snapshot;
}

void Append(RenderResult& destination, RenderResult source) {
    destination.succeeded = destination.succeeded && source.succeeded;
    destination.animationActive = destination.animationActive || source.animationActive;
    destination.diagnostics.insert(destination.diagnostics.end(),
        std::make_move_iterator(source.diagnostics.begin()),
        std::make_move_iterator(source.diagnostics.end()));
    destination.hitRegions.insert(destination.hitRegions.end(),
        std::make_move_iterator(source.hitRegions.begin()),
        std::make_move_iterator(source.hitRegions.end()));
    destination.accessibilityRegions.insert(destination.accessibilityRegions.end(),
        std::make_move_iterator(source.accessibilityRegions.begin()),
        std::make_move_iterator(source.accessibilityRegions.end()));
    destination.focusRects.insert(source.focusRects.begin(), source.focusRects.end());
    destination.navigationRects.insert(source.navigationRects.begin(), source.navigationRects.end());
    destination.navigationEnabled.insert(
        source.navigationEnabled.begin(), source.navigationEnabled.end());
    destination.revealableFocusIds.insert(
        source.revealableFocusIds.begin(), source.revealableFocusIds.end());
    destination.focusScopes.insert(source.focusScopes.begin(), source.focusScopes.end());
    destination.scrollOffsets.insert(source.scrollOffsets.begin(), source.scrollOffsets.end());
    if (source.currentFocusRect) destination.currentFocusRect = source.currentFocusRect;
    if (source.currentFocusOutlineClip)
        destination.currentFocusOutlineClip = source.currentFocusOutlineClip;
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
    destination.elementRects.insert(source.elementRects.begin(), source.elementRects.end());
    destination.elementVisibleRects.insert(
        source.elementVisibleRects.begin(), source.elementVisibleRects.end());
    destination.sliderThumbXs.insert(source.sliderThumbXs.begin(), source.sliderThumbXs.end());
    destination.buttonContentPlacements.insert(
        source.buttonContentPlacements.begin(), source.buttonContentPlacements.end());
    destination.textLineCounts.insert(source.textLineCounts.begin(), source.textLineCounts.end());
#endif
}

WidgetSnapshot AggregateSnapshot(
    const std::vector<SlotContent>& contents,
    const LayoutResult& layout,
    const std::wstring_view focusedElementId) {
    WidgetSnapshot aggregate;
    aggregate.sequence = 1;
    aggregate.instanceId = L"launcher-experience";
    aggregate.activeInputScopeId = L"launcher-root";
    aggregate.initialFocusId = focusedElementId;
    aggregate.root.id = L"launcher-experience-root";
    aggregate.root.kind = L"column";
    aggregate.root.inputScopeId = aggregate.activeInputScopeId;
    for (const auto& placement : layout.semanticPlacements) {
        if (const auto* content = FindContent(contents, placement.slot)) {
            auto root = AdaptSnapshot(*content, placement).root;
            if (root.inputScopeId.empty()) root.inputScopeId = aggregate.activeInputScopeId;
            aggregate.root.children.push_back(std::move(root));
        }
    }
    return aggregate;
}

} // namespace

RenderedExperience RenderExperience(
    DeclarativeRenderer& renderer,
    ID2D1RenderTarget* target,
    const Recipe* recipe,
    const Preset preset,
    const declarative::Rect workArea,
    const std::vector<SlotContent>& contents,
    const std::wstring_view focusedElementId,
    DeclarativeRenderOptions options) {
    RenderedExperience result;
    result.layout = ResolveLayout(recipe, preset, workArea, options.accessibility.textScale);
    result.semanticSnapshot = AggregateSnapshot(contents, result.layout, focusedElementId);
    result.render.succeeded = result.layout.valid() && target != nullptr;
    std::set<std::wstring, std::less<>> renderedIds;
    std::set<Slot> suppliedSlots;
    for (const auto& content : contents) {
        if (!suppliedSlots.insert(content.slot).second) {
            result.render.succeeded = false;
            result.render.diagnostics.push_back({
                RenderDiagnosticSeverity::Error, {}, L"duplicate_launcher_slot_content",
                L"The host may inject each launcher semantic slot at most once."});
        }
    }
    for (const auto required :
         {Slot::GameRail, Slot::DetailsPanel, Slot::SourceStatus, Slot::ControllerHints}) {
        if (!suppliedSlots.contains(required)) {
            result.render.succeeded = false;
            result.render.diagnostics.push_back({
                RenderDiagnosticSeverity::Error, {}, L"missing_launcher_slot_content",
                L"Required host-owned launcher slot content is absent."});
        }
    }
    for (const auto& placement : result.layout.paintPlacements) {
        const auto* content = FindContent(contents, placement.slot);
        if (!content) continue;
        auto snapshot = AdaptSnapshot(*content, placement);
        options.responsiveViewport = declarative::Size{
            placement.bounds.width, placement.bounds.height};
        bool duplicateIdentity{};
        const auto collect = [&](const auto& self, const WidgetNode& node) -> void {
            if (!node.id.empty() && !renderedIds.insert(node.id).second)
                duplicateIdentity = true;
            for (const auto& child : node.children) self(self, child);
        };
        collect(collect, snapshot.root);
        if (duplicateIdentity) {
            result.render.succeeded = false;
            result.render.diagnostics.push_back({
                RenderDiagnosticSeverity::Error, {}, L"duplicate_launcher_slot_identity",
                L"Host slot contents must use unique stable node IDs."});
            continue;
        }
        auto slotResult = renderer.Render(
            target, snapshot, focusedElementId, placement.bounds, options);
        Append(result.render, std::move(slotResult));
    }
    return result;
}

} // namespace gba::launcher
