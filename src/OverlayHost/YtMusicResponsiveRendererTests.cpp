#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <map>
#include <string>
#include <string_view>
#include <vector>

namespace {

using gba::DeclarativeRenderer;
using gba::RenderResult;
using gba::WidgetNode;
using gba::WidgetSnapshot;
using gba::declarative::Rect;

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
    Check(static_cast<bool>(stream), "renderer fixture can be opened");
    return {std::istreambuf_iterator<char>(stream), {}};
}

const WidgetNode& Find(const WidgetNode& node, const std::wstring_view id) {
    if (node.id == id) return node;
    for (const auto& child : node.children) {
        if (const auto* found = [&]() -> const WidgetNode* {
                try {
                    return &Find(child, id);
                } catch (const std::out_of_range&) {
                    return nullptr;
                }
            }()) return *found;
    }
    throw std::out_of_range("node not found");
}

void CheckEdge(
    const WidgetSnapshot& snapshot,
    const wchar_t* source,
    const wchar_t* direction,
    const wchar_t* expected) {
    const auto& node = Find(snapshot.root, source);
    const std::wstring* actual = nullptr;
    if (std::wstring_view(direction) == L"up") actual = &node.focusUp;
    else if (std::wstring_view(direction) == L"down") actual = &node.focusDown;
    else if (std::wstring_view(direction) == L"left") actual = &node.focusLeft;
    else actual = &node.focusRight;
    Check(*actual == expected, "explicit YT Music focus adjacency is unchanged");
}

bool Contains(const Rect& outer, const Rect& inner) {
    return inner.x >= outer.x - 0.01F && inner.y >= outer.y - 0.01F &&
        inner.x + inner.width <= outer.x + outer.width + 0.01F &&
        inner.y + inner.height <= outer.y + outer.height + 0.01F;
}

RenderResult RenderAt(
    DeclarativeRenderer& renderer,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focused,
    const float width,
    const float height) {
    gba::DeclarativeRenderOptions options;
    options.responsiveViewport = gba::declarative::Size{width, height};
    return renderer.Render(
        nullptr, snapshot, focused, {0.0F, 0.0F, width, height}, options);
}

void CheckControlsReachable(
    const WidgetSnapshot& snapshot,
    const float width,
    const float height) {
    constexpr std::wstring_view controls[] = {
        L"previous", L"play-pause", L"next", L"refresh",
        L"shuffle", L"like", L"dislike", L"repeat",
    };
    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const Rect viewport{0.0F, 0.0F, width, height};
    for (const auto control : controls) {
        const auto id = std::wstring(control);
        const auto result = RenderAt(renderer, snapshot, control, width, height);
        Check(result.navigationRects.contains(id),
              "every authored YT Music control remains in native navigation");
        Check(result.focusRects.contains(id),
              "focus-follow reveals every authored YT Music control");
        Check(Contains(viewport, result.focusRects.at(id)),
              "revealed YT Music control stays inside the authored viewport");
    }
}

void Run(const std::filesystem::path& fixturePath) {
    std::wstring error;
    const auto parsed = gba::testing::ParseWidgetSnapshotResponse(
        ReadUtf8(fixturePath), error);
    Check(parsed.has_value(), "production native bridge parses the managed YT Music fixture");
    const auto& snapshot = *parsed;
    Check(snapshot.instanceId == L"ytmusic.renderer",
          "renderer scenario consumed the exact managed fixture identity");
    Check(snapshot.initialFocusId == L"play-pause",
          "YT Music initial focus identity is unchanged");
    Check(snapshot.surface && snapshot.surface->preferredWidth == 760.0 &&
              snapshot.surface->preferredHeight == 440.0 &&
              snapshot.surface->minimumWidth == 480.0 &&
              snapshot.surface->minimumHeight == 340.0,
          "renderer scenario uses the exact authored preferred and compact budgets");
    Check(Find(snapshot.root, L"media-details").baseStyle.contains(L"min-width") &&
              Find(snapshot.root, L"artwork-frame").baseStyle.contains(L"width"),
          "production bridge parsing retains compiled YT Music GBSS values");

    CheckEdge(snapshot, L"previous", L"right", L"play-pause");
    CheckEdge(snapshot, L"previous", L"down", L"shuffle");
    CheckEdge(snapshot, L"play-pause", L"left", L"previous");
    CheckEdge(snapshot, L"play-pause", L"right", L"next");
    CheckEdge(snapshot, L"play-pause", L"down", L"like");
    CheckEdge(snapshot, L"next", L"left", L"play-pause");
    CheckEdge(snapshot, L"next", L"right", L"refresh");
    CheckEdge(snapshot, L"next", L"down", L"dislike");
    CheckEdge(snapshot, L"refresh", L"left", L"next");
    CheckEdge(snapshot, L"refresh", L"down", L"repeat");
    CheckEdge(snapshot, L"shuffle", L"up", L"previous");
    CheckEdge(snapshot, L"shuffle", L"right", L"like");
    CheckEdge(snapshot, L"like", L"up", L"play-pause");
    CheckEdge(snapshot, L"like", L"left", L"shuffle");
    CheckEdge(snapshot, L"like", L"right", L"dislike");
    CheckEdge(snapshot, L"dislike", L"up", L"next");
    CheckEdge(snapshot, L"dislike", L"left", L"like");
    CheckEdge(snapshot, L"dislike", L"right", L"repeat");
    CheckEdge(snapshot, L"repeat", L"up", L"refresh");
    CheckEdge(snapshot, L"repeat", L"left", L"dislike");

    DeclarativeRenderer preferredRenderer{nullptr, nullptr, nullptr};
    const auto preferred = RenderAt(
        preferredRenderer, snapshot, L"play-pause", 760.0F, 440.0F);
    const auto& preferredRoot = preferred.elementRects.at(L"ytmusic-root");
    const auto& preferredLayout = preferred.elementRects.at(L"media-layout");
    const auto& preferredArtwork = preferred.elementRects.at(L"artwork-frame");
    const auto& preferredDetails = preferred.elementRects.at(L"media-details");
    Near(preferredRoot.width, 760.0F,
         "preferred YT Music root consumes the admitted envelope");
    Near(preferredLayout.width, preferredRoot.width - 32.0F,
         "preferred cohesive media panel consumes the root inner width");
    Check(Find(snapshot.root, L"media-layout").baseStyle.contains(L"background") &&
              Find(snapshot.root, L"media-layout").baseStyle.contains(L"border-width"),
          "production GBSS gives the unified media panel one bounded surface");
    Check(preferredArtwork.x + preferredArtwork.width <= preferredDetails.x + 0.01F,
          "preferred YT Music surface keeps artwork beside details");
    Check(preferredArtwork.y < preferredDetails.y + preferredDetails.height &&
              preferredDetails.y < preferredArtwork.y + preferredArtwork.height,
          "preferred artwork and details share one horizontal band");

    DeclarativeRenderer compactRenderer{nullptr, nullptr, nullptr};
    const auto compact = RenderAt(
        compactRenderer, snapshot, L"play-pause", 480.0F, 340.0F);
    const auto& compactLayout = compact.elementRects.at(L"media-layout");
    const auto& compactArtwork = compact.elementRects.at(L"artwork-frame");
    const auto& compactDetails = compact.elementRects.at(L"media-details");
    Check(compactArtwork.y + compactArtwork.height <= compactDetails.y + 0.01F,
          "compact YT Music surface reflows artwork above details");
    Near(compactDetails.x, compactLayout.x + 12.0F,
         "compact details begin at the unified panel inner edge");
    Near(compactDetails.width, compactLayout.width - 24.0F,
         "compact details consume the full unified-panel inner width");

    CheckControlsReachable(snapshot, 760.0F, 440.0F);
    CheckControlsReachable(snapshot, 480.0F, 340.0F);
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (argc != 3 || std::wstring_view(argv[1]) != L"--fixture") {
        std::cerr << "Usage: YtMusicResponsiveRendererTests --fixture <snapshot.json>\n";
        return EXIT_FAILURE;
    }
    Run(argv[2]);
    std::cout << "YtMusicResponsiveRendererTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
