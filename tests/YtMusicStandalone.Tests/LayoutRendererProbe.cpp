#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"
#include <dwrite.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <cmath>

void Require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
int Run(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    Microsoft::WRL::ComPtr<IDWriteFactory> factory;
    Require(SUCCEEDED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(factory.GetAddressOf()))), "DirectWrite unavailable");
    widgetrail::DeclarativeRenderer renderer{nullptr, factory.Get(), nullptr};
    for (int file = 1; file < argc; ++file) {
        std::ifstream stream(std::filesystem::path(argv[file]), std::ios::binary);
        const std::string text{std::istreambuf_iterator<char>(stream), {}};
        std::wstring error;
        const auto snapshot = widgetrail::testing::ParseWidgetSnapshotResponse(text, error);
        Require(snapshot.has_value(), "Cannot parse production styled snapshot");
        for (const auto size : {widgetrail::declarative::Size{980, 700}, widgetrail::declarative::Size{620, 400}}) {
            widgetrail::DeclarativeRenderOptions options;
            options.responsiveViewport = size;
            const auto result = renderer.Render(nullptr, *snapshot, L"item.0", {0, 0, size.width, size.height}, options);
            const auto& panes = result.elementRects.at(L"music.panes");
            const bool playing = result.elementRects.contains(L"player.main.scroll");
            const auto& player = result.elementRects.at(playing ? L"player.main.scroll" : L"player.main.empty");
            const auto& browse = result.elementRects.at(L"music.scroll");
            std::cout << (playing ? "playing" : "empty") << " " << size.width << "x" << size.height
                << " player=" << player.x << "," << player.y << "," << player.width << "," << player.height
                << " browse=" << browse.x << "," << browse.y << "," << browse.width << "," << browse.height << '\n';
            Require(player.width >= 259 && player.width <= 341, "Player escaped its 260-340 DIP width bounds");
            Require(browse.x >= player.x + player.width + 11, "Browsing pane overlaps the player or loses its gap");
            Require(browse.x + browse.width <= size.width + 1, "Browsing pane exceeds widget viewport");
            Require(std::abs(player.height - panes.height) < 1, "Player does not fill pane height");
            Require(browse.height > panes.height - 30, "Browsing panel collapsed");
            if (!playing) {
                const auto& title = result.elementRects.at(L"player.main.empty.title");
                Require(title.y > player.y + player.height * .3f && title.y < player.y + player.height * .7f, "Empty player message is not centered");
            } else {
                const auto& controls = result.elementRects.at(L"player.main.controls");
                Require(controls.x >= player.x && controls.x + controls.width <= player.x + player.width + 1, "Transport escapes player pane");
            }
        }
    }
    std::cout << "PASS native layout: empty and playing at compact and preferred sizes\n";
    return 0;
}

int wmain(int argc, wchar_t** argv) {
    try { return Run(argc, argv); }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
