#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <dwrite.h>
#include <wrl/client.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <string>
#include <string_view>

namespace {

using Microsoft::WRL::ComPtr;
using widgetrail::DeclarativeRenderer;
using widgetrail::WidgetNode;
using widgetrail::WidgetSnapshot;
using widgetrail::declarative::Rect;

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (condition) return;
    std::cerr << "FAIL: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

void Near(
    const float actual,
    const float expected,
    const std::string_view message,
    const float tolerance = 0.05F) {
    ++checks;
    if (std::abs(actual - expected) <= tolerance) return;
    std::cerr << "FAIL: " << message << " (actual=" << actual
              << ", expected=" << expected << ")\n";
    std::exit(EXIT_FAILURE);
}

std::string ReadUtf8(const std::filesystem::path& path) {
    std::ifstream stream(path, std::ios::binary);
    Check(static_cast<bool>(stream), "Settings renderer fixture can be opened");
    return {std::istreambuf_iterator<char>(stream), {}};
}

const WidgetNode* FindOrNull(const WidgetNode& node, const std::wstring_view id) {
    if (node.id == id) return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindOrNull(child, id)) return found;
    }
    return nullptr;
}

const WidgetNode& Find(const WidgetNode& node, const std::wstring_view id) {
    const auto* found = FindOrNull(node, id);
    if (!found) throw std::out_of_range("Settings fixture node not found");
    return *found;
}

bool Contains(const Rect& outer, const Rect& inner) {
    return inner.x >= outer.x - 0.01F && inner.y >= outer.y - 0.01F &&
        inner.x + inner.width <= outer.x + outer.width + 0.01F &&
        inner.y + inner.height <= outer.y + outer.height + 0.01F;
}

widgetrail::RenderResult RenderAt(
    DeclarativeRenderer& renderer,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focused,
    const float width,
    const float height) {
    widgetrail::DeclarativeRenderOptions options;
    options.responsiveViewport = widgetrail::declarative::Size{width, height};
    return renderer.Render(
        nullptr, snapshot, focused, {0.0F, 0.0F, width, height}, options);
}

void CheckReachable(
    DeclarativeRenderer& renderer,
    const WidgetSnapshot& snapshot,
    const float width,
    const float height) {
    constexpr std::wstring_view controls[] = {
        L"category.appearance", L"category.accessibility", L"category.overlay",
        L"category.installed-widgets", L"category.diagnostics",
        L"settings.refresh", L"category.reset",
    };
    const Rect viewport{0.0F, 0.0F, width, height};
    for (const auto control : controls) {
        const auto id = std::wstring(control);
        const auto result = RenderAt(renderer, snapshot, control, width, height);
        Check(result.navigationRects.contains(id),
              "every Settings root category remains in native navigation");
        Check(result.focusRects.contains(id),
              "focus-follow reveals every Settings root category");
        Check(Contains(viewport, result.focusRects.at(id)),
              "revealed Settings category stays inside the constrained viewport");
    }
}

void Run(const std::filesystem::path& fixturePath) {
    std::wstring error;
    const auto parsed = widgetrail::testing::ParseWidgetSnapshotResponse(
        ReadUtf8(fixturePath), error);
    Check(parsed.has_value(), "production native bridge parses the managed Settings fixture");
    const auto& snapshot = *parsed;
    Check(snapshot.protocolVersion == 17,
          "Settings root snapshot selects the surface-axis protocol");
    Check(snapshot.activeInputScopeId == L"settings-root" &&
              snapshot.initialFocusId == L"category.appearance",
          "Settings root scope and initial focus remain unchanged");
    Check(snapshot.surface &&
              (!snapshot.surface->widthMode ||
               snapshot.surface->widthMode == L"preferred") &&
              snapshot.surface->heightMode == L"content" &&
              snapshot.surface->preferredWidth == 880.0 &&
              snapshot.surface->preferredHeight == 520.0 &&
              snapshot.surface->minimumWidth == 520.0 &&
              snapshot.surface->minimumHeight == 360.0,
          "Settings root carries exact Preferred-by-Content authored bounds");
    const auto& categories = Find(snapshot.root, L"settings.categories");
    Check(!categories.baseStyle.contains(L"flex-grow") &&
              !categories.baseStyle.contains(L"flex-basis"),
          "Settings root category list does not request fill-growth dead height");

    ComPtr<IDWriteFactory> writeFactory;
    Check(SUCCEEDED(DWriteCreateFactory(
              DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
              reinterpret_cast<IUnknown**>(writeFactory.GetAddressOf()))),
          "DirectWrite factory is available for production intrinsic text measurement");
    DeclarativeRenderer renderer{nullptr, writeFactory.Get(), nullptr};
    const auto preferredMeasure = renderer.MeasureContent(
        snapshot, {880.0F, 520.0F}, false);
    const auto compactMeasure = renderer.MeasureContent(
        snapshot, {520.0F, 520.0F}, false);
    Check(preferredMeasure.succeeded && compactMeasure.succeeded,
          "production renderer measures the real Settings root at both widths");
    Near(preferredMeasure.extent.width, 880.0F,
         "Preferred width remains definite during Content-height measurement");
    Near(compactMeasure.extent.width, 520.0F,
         "compact admitted width remains definite during Content-height measurement");
    Check(preferredMeasure.extent.height > 0.0F &&
              preferredMeasure.extent.height <= 520.0F,
          "preferred intrinsic Settings height is finite and below its ceiling");
    Check(compactMeasure.extent.height > preferredMeasure.extent.height,
          "one-column compact reflow expands intrinsic content height");

    const auto preferredHeight = std::clamp(
        preferredMeasure.extent.height, 360.0F, 520.0F);
    const auto compactHeight = std::clamp(
        compactMeasure.extent.height, 360.0F, 520.0F);
    Near(compactHeight, 520.0F,
         "compact intrinsic height is bounded by the authored preferred ceiling");
    const auto preferred = RenderAt(
        renderer, snapshot, L"category.appearance", 880.0F, preferredHeight);
    const auto compact = RenderAt(
        renderer, snapshot, L"category.appearance", 520.0F, compactHeight);
    const auto& wideAppearance = preferred.elementRects.at(L"category.appearance");
    const auto& wideAccessibility = preferred.elementRects.at(L"category.accessibility");
    Check(wideAppearance.y == wideAccessibility.y &&
              wideAppearance.x < wideAccessibility.x,
          "preferred Settings root retains two category columns");
    const auto& compactAppearance = compact.elementRects.at(L"category.appearance");
    const auto& compactAccessibility = compact.elementRects.at(L"category.accessibility");
    Near(compactAppearance.x, compactAccessibility.x,
         "compact Settings root reflows categories to one column");
    Check(compactAccessibility.y >= compactAppearance.y + compactAppearance.height,
          "compact category order remains vertical and non-overlapping");

    const auto& preferredRoot = preferred.elementRects.at(L"settings-root");
    const auto& preferredReset = preferred.elementRects.at(L"category.reset");
    Check(preferredRoot.y + preferredRoot.height -
              (preferredReset.y + preferredReset.height) <= 28.01F,
          "content-sized Settings root leaves only authored trailing spacing below Reset");

    CheckReachable(renderer, snapshot, 520.0F, 360.0F);
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (argc != 3 || std::wstring_view(argv[1]) != L"--fixture") {
        std::cerr << "Usage: SettingsContentRendererTests --fixture <snapshot.json>\n";
        return EXIT_FAILURE;
    }
    Run(argv[2]);
    std::cout << "SettingsContentRendererTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
