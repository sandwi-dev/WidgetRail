#include "DeveloperInspector.h"
#include <commctrl.h>
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <map>
#include <sstream>

namespace widgetrail {
namespace {
std::wstring Safe(std::wstring_view value) {
    std::wstring result(value.substr(0, 180));
    for (auto& character : result) if (character < L' ') character = L' ';
    return result;
}
std::wstring RectText(declarative::Rect value) {
    std::wostringstream out;
    out << std::fixed << std::setprecision(1) << value.x << L", " << value.y
        << L"   " << value.width << L" x " << value.height;
    return out.str();
}
std::wstring Edges(NativeEdges value) {
    std::wostringstream out;
    out << value.top << L" " << value.right << L" " << value.bottom << L" " << value.left;
    return out.str();
}
std::wstring Color(const std::optional<NativeColor>& color) {
    if (!color) return L"none";
    std::wostringstream out;
    out << L"rgba(" << std::lround(color->red * 255) << L", " << std::lround(color->green * 255)
        << L", " << std::lround(color->blue * 255) << L", " << color->alpha << L")";
    return out.str();
}
std::wstring Optional(const std::optional<float>& value) {
    return value ? std::to_wstring(*value) : L"auto";
}
std::wstring Styles(const RenderInspectionNode& node) {
    const auto& a = node.layoutStyle;
    const auto& b = node.paintStyle;
    std::wostringstream out;
    out << L"\r\nLAYOUT STYLE (resolved DIPs)\r\nwidth: " << Optional(a.widthPx()) << L"   height: " << Optional(a.heightPx())
        << L"\r\nmin: " << Optional(a.minWidthPx()) << L" x " << Optional(a.minHeightPx())
        << L"   max: " << Optional(a.maxWidthPx()) << L" x " << Optional(a.maxHeightPx())
        << L"\r\npadding (T R B L): " << Edges(a.paddingPx()) << L"\r\nmargin: " << Edges(a.marginPx())
        << L"\r\ngap: " << Edges(a.gapPx()) << L"\r\nflex grow/shrink: " << a.flexGrow() << L" / " << a.flexShrink()
        << L"\r\ndirection: " << (a.direction() == NativeDirection::Row ? L"row" : a.direction() == NativeDirection::Column ? L"column" : L"default")
        << L"   overflow: " << (a.overflow() == NativeOverflow::Clip ? L"clip" : L"visible")
        << L"\r\n\r\nPAINT STYLE (includes focus/pressed rules)\r\nbackground: " << Color(b.background())
        << L"\r\nforeground: " << Color(b.foreground()) << L"\r\nfont: " << Safe(b.fontFamily())
        << L" " << b.fontSizePx() << L" DIP, weight " << b.fontWeight()
        << L"\r\nopacity: " << b.opacity() << L"   scale: " << b.scale()
        << L"\r\ntranslation: " << b.translateXPx() << L", " << b.translateYPx()
        << L"\r\nradius: " << b.cornerRadiusPx() << L"   border: " << b.borderWidthPx()
        << L"\r\noutline: " << b.outlineWidthPx() << L"   offset: " << b.outlineOffsetPx();
    return out.str();
}
}

DeveloperInspectorFrame BuildDeveloperInspectorFrame(std::wstring_view widgetId,
    const WidgetSnapshot& snapshot, const RenderResult& result, std::wstring_view focusedId) {
    DeveloperInspectorFrame frame;
    frame.widgetId = Safe(widgetId);
    frame.focusedId = focusedId;
    frame.sequence = snapshot.sequence;
    if (!result.succeeded || !result.inspection) {
        frame.status = L"No committed inspector frame available.";
        return frame;
    }
    frame.viewport = result.inspection->viewport;
    const auto& timing = result.timing;
    std::wostringstream status;
    status << Safe(widgetId) << L"  |  snapshot " << snapshot.sequence << L"  |  focus: " << Safe(focusedId)
        << L"\r\nRenderer: " << timing.totalMicroseconds << L" us  |  prepare: " << timing.preparationMicroseconds
        << L" us  |  layout: " << timing.layoutMicroseconds << L" us  |  draw: " << timing.nodeDrawMicroseconds
        << L" us\r\nStyle cache hit/miss: " << timing.styleCacheHits << L"/" << timing.styleCacheMisses
        << L"  |  text layout cache hit/miss: " << timing.textLayoutCacheHits << L"/" << timing.textLayoutCacheMisses
        << L"  |  " << (result.responsiveSurface && result.responsiveSurface->mode == ResponsiveSurfaceMode::Compact ? L"compact" : L"expanded")
        << (result.inspection->truncated ? L"  |  node limit reached" : L"");
    frame.status = status.str();
    std::map<std::wstring, const WidgetNode*, std::less<>> nodes;
    const auto visit = [&](const auto& self, const WidgetNode& node) -> void {
        if (nodes.size() >= RenderInspection::maximumNodes) return;
        nodes.emplace(node.id, &node);
        for (const auto& child : node.children) self(self, child);
        for (const auto& child : node.focusPresentation) self(self, child);
        for (const auto& child : node.defaultFocusPresentation) self(self, child);
    };
    visit(visit, snapshot.root);
    for (const auto& visual : result.inspection->nodes) {
        if (frame.nodes.size() >= RenderInspection::maximumNodes) break;
        std::wostringstream details;
        details << Safe(visual.id) << L"  (" << Safe(visual.kind) << L")\r\n";
        if (visual.laidOut) details << L"Bounds: " << RectText(visual.bounds) << L"\r\nVisible: " << RectText(visual.visibleBounds);
        else details << L"Not laid out in this responsive presentation.";
        details << L"\r\n\r\nFOCUS / ACTIONS\r\n";
        const auto found = nodes.find(visual.id);
        if (found != nodes.end()) {
            const auto& node = *found->second;
            details << L"action: " << Safe(node.actionId) << L"\r\ndisabled: " << node.isDisabled << L"   busy: " << node.isBusy
                << L"\r\nexplicit up: " << Safe(node.focusUp) << L"\r\ndown: " << Safe(node.focusDown)
                << L"\r\nleft: " << Safe(node.focusLeft) << L"\r\nright: " << Safe(node.focusRight) << L"\r\nclasses:";
            for (std::size_t i = 0; i < std::min<std::size_t>(16, node.styleClasses.size()); ++i)
                details << L" " << Safe(node.styleClasses[i]);
            if (node.styleClasses.size() > 16) details << L" ...";
            if (node.kind == L"scroll") {
                details << L"\r\n\r\nSCROLL / CURSOR\r\naxis: " << Safe(node.scrollAxis)
                    << L"\r\nloading: " << Safe(node.collectionLoading) << L"   anchor present: " << !node.collectionAnchorKey.empty();
                if (node.collectionGeneration) details << L"\r\ngeneration: " << *node.collectionGeneration;
                if (node.collectionResetGeneration) details << L"   reset: " << *node.collectionResetGeneration;
                if (const auto scroll = result.scrollViewports.find(node.id); scroll != result.scrollViewports.end())
                    details << L"\r\noffset: " << scroll->second.offset << L" / " << scroll->second.maximumOffset;
                if (node.virtualCollectionWindow) {
                    const auto& window = *node.virtualCollectionWindow;
                    details << L"\r\nmore before/after: " << window.hasBefore << L" / " << window.hasAfter;
                    if (window.firstItemIndex) details << L"\r\nfirst item: " << *window.firstItemIndex;
                    if (window.totalItemCount) details << L"   total: " << *window.totalItemCount;
                }
            }
        }
        if (const auto scope = result.focusScopes.find(visual.id); scope != result.focusScopes.end()) details << L"\r\nresolved input scope: " << Safe(scope->second);
        details << L"\r\nvisible focus target: " << result.focusRects.contains(visual.id)
            << L"   navigation target: " << result.navigationRects.contains(visual.id);
        if (visual.laidOut) details << Styles(visual);
        frame.nodes.push_back({visual, details.str()});
    }
    return frame;
}

} // namespace widgetrail
