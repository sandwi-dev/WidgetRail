#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <dwrite.h>
#include <wrl/client.h>

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>
#include <string_view>

namespace {

using Microsoft::WRL::ComPtr;
using gba::DeclarativeRenderer;
using gba::WidgetSnapshot;
using gba::declarative::Rect;

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (condition) return;
    std::cerr << "FAIL: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

std::string ReadUtf8(const std::filesystem::path& path) {
    std::ifstream stream(path, std::ios::binary);
    Check(static_cast<bool>(stream), "Network renderer fixture can be opened");
    return {std::istreambuf_iterator<char>(stream), {}};
}

bool Contains(const Rect& outer, const Rect& inner) {
    return inner.x >= outer.x - 0.01F && inner.y >= outer.y - 0.01F &&
        inner.x + inner.width <= outer.x + outer.width + 0.01F &&
        inner.y + inner.height <= outer.y + outer.height + 0.01F;
}

gba::RenderResult RenderAt(
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

void Run(const std::filesystem::path& fixturePath) {
    std::wstring error;
    const auto parsed = gba::testing::ParseWidgetSnapshotResponse(
        ReadUtf8(fixturePath), error);
    Check(parsed.has_value(), "production bridge parses the managed Network fixture");
    const auto& snapshot = *parsed;
    Check(snapshot.activeInputScopeId == L"network-controls" &&
              snapshot.initialFocusId == L"network.wifi.scan",
          "Network root scope and initial scan focus remain unchanged");
    Check(snapshot.surface && snapshot.surface->preferredWidth == 560.0 &&
              snapshot.surface->preferredHeight == 700.0 &&
              snapshot.surface->minimumWidth == 320.0 &&
              snapshot.surface->minimumHeight == 420.0,
          "Network root carries its bounded authored surface");

    ComPtr<IDWriteFactory> writeFactory;
    Check(SUCCEEDED(DWriteCreateFactory(
              DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
              reinterpret_cast<IUnknown**>(writeFactory.GetAddressOf()))),
          "DirectWrite factory is available for production Network measurement");
    DeclarativeRenderer renderer{nullptr, writeFactory.Get(), nullptr};

    // The 700-DIP authored panel supplies 644 DIPs after fixed host footer
    // chrome. Initial scan focus must not need a body-scroll correction.
    const auto preferred = RenderAt(
        renderer, snapshot, L"network.wifi.scan", 560.0F, 644.0F);
    const Rect preferredViewport{0.0F, 0.0F, 560.0F, 644.0F};
    Check(preferred.focusRects.contains(L"network.wifi.scan") &&
              Contains(preferredViewport,
                       preferred.focusRects.at(L"network.wifi.scan")),
          "preferred first page exposes the primary Scan action");
    Check(preferred.elementRects.contains(L"network.wifi.state.title") &&
              preferred.elementRects.contains(L"network.wifi.state.help") &&
              Contains(preferredViewport,
                       preferred.elementRects.at(L"network.wifi.state.title")) &&
              Contains(preferredViewport,
                       preferred.elementRects.at(L"network.wifi.state.help")),
          "preferred first page exposes the complete Ready to scan state");
    Check(preferred.scrollOffsets.at(L"network.wifi.body.scroll") == 0.0F,
          "preferred initial Scan action requires no nested scroll correction");

    const Rect constrainedViewport{0.0F, 0.0F, 320.0F, 364.0F};
    for (const auto* focus : {L"network.wifi.radio", L"network.wifi.scan"}) {
        const auto constrained = RenderAt(
            renderer, snapshot, focus, 320.0F, 364.0F);
        Check(constrained.focusRects.contains(focus),
              "constrained Network root keeps the primary control focusable");
        Check(Contains(constrainedViewport, constrained.focusRects.at(focus)),
              "constrained Network root scroll-reveals the primary control");
    }
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (argc != 3 || std::wstring_view(argv[1]) != L"--fixture") {
        std::cerr << "Usage: NetworkFirstPageRendererTests --fixture <snapshot.json>\n";
        return EXIT_FAILURE;
    }
    Run(argv[2]);
    std::cout << "NetworkFirstPageRendererTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
