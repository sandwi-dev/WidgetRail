#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <dwrite.h>
#include <wrl/client.h>

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
using gba::DeclarativeRenderer;
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
    Check(static_cast<bool>(stream), "Audio Mixer renderer fixture can be opened");
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
    if (!found) throw std::out_of_range("Audio Mixer fixture node not found");
    return *found;
}

const WidgetNode& Child(const WidgetNode& node, const std::size_t index) {
    Check(index < node.children.size(), "Audio Mixer fixture child exists");
    return node.children[index];
}

gba::RenderResult RenderAt(
    DeclarativeRenderer& renderer,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focused,
    const float width) {
    gba::DeclarativeRenderOptions options;
    options.responsiveViewport = gba::declarative::Size{width, 520.0F};
    return renderer.Render(
        nullptr, snapshot, focused, {0.0F, 0.0F, width, 520.0F}, options);
}

void CheckRow(
    const gba::RenderResult& result,
    const std::wstring& cardId,
    const std::wstring& headingId,
    const std::wstring& controlsId,
    const std::wstring& muteId,
    const std::wstring& sliderId,
    const std::wstring& valueId,
    const float horizontalPadding) {
    const auto& card = result.elementRects.at(cardId);
    const auto& heading = result.elementRects.at(headingId);
    const auto& controls = result.elementRects.at(controlsId);
    const auto& mute = result.elementRects.at(muteId);
    const auto& slider = result.elementRects.at(sliderId);
    const auto& value = result.elementRects.at(valueId);
    Near(heading.width, card.width - horizontalPadding,
         "Audio heading consumes its card's inner width");
    Near(controls.width, card.width - horizontalPadding,
         "Audio control row consumes its card's inner width");
    Near(slider.width,
         controls.width - mute.width - value.width - 16.0F,
         "Audio slider consumes the exact flexible remainder");
    Check(slider.width > 0.0F && slider.x > mute.x && value.x > slider.x,
          "Audio control order and flexible track remain valid");
}

void Run(const std::filesystem::path& fixturePath) {
    std::wstring error;
    const auto parsed = gba::testing::ParseWidgetSnapshotResponse(
        ReadUtf8(fixturePath), error);
    Check(parsed.has_value(), "production bridge parses the managed Audio Mixer fixture");
    const auto& snapshot = *parsed;
    Check(snapshot.activeInputScopeId == L"audio-mixer" &&
              snapshot.initialFocusId == L"audio.master.volume.slider",
          "Audio Mixer scope and initial focus remain unchanged");

    const auto& sessions = Find(snapshot.root, L"audio.sessions.list");
    const auto& sessionCard = Child(sessions, 0);
    const auto& sessionHeading = Child(sessionCard, 0);
    const auto& sessionControls = Child(sessionCard, 1);
    const auto& sessionMute = Child(sessionControls, 0);
    const auto& sessionSlider = Child(sessionControls, 1);
    const auto& sessionValue = Child(sessionControls, 2);

    ComPtr<IDWriteFactory> writeFactory;
    Check(SUCCEEDED(DWriteCreateFactory(
              DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
              reinterpret_cast<IUnknown**>(writeFactory.GetAddressOf()))),
          "DirectWrite factory is available for production Audio measurement");
    DeclarativeRenderer renderer{nullptr, writeFactory.Get(), nullptr};

    float compactMasterSlider{};
    float preferredMasterSlider{};
    for (const auto width : {320.0F, 520.0F}) {
        const auto master = RenderAt(
            renderer, snapshot, L"audio.master.volume.slider", width);
        Near(master.elementRects.at(L"audio.root").width, width,
             "Audio root consumes the admitted content width");
        CheckRow(master,
                 L"audio.master.card", L"audio.master.heading",
                 L"audio.master.controls", L"audio.master.mute.icon",
                 L"audio.master.volume.slider", L"audio.master.volume.value", 20.0F);
        const auto masterSlider = master.elementRects.at(
            L"audio.master.volume.slider").width;
        if (width == 320.0F) compactMasterSlider = masterSlider;
        else preferredMasterSlider = masterSlider;

        const auto input = RenderAt(
            renderer, snapshot, L"audio.input.volume.slider", width);
        CheckRow(input,
                 L"audio.input.card", L"audio.input.heading",
                 L"audio.input.controls", L"audio.input.mute.icon",
                 L"audio.input.volume.slider", L"audio.input.volume.value", 20.0F);

        const auto session = RenderAt(renderer, snapshot, sessionSlider.id, width);
        Near(session.elementRects.at(L"audio.sessions.list").width,
             session.elementRects.at(L"audio.root").width - 28.0F,
             "Audio session list consumes the root inner width");
        CheckRow(session, sessionCard.id, sessionHeading.id, sessionControls.id,
                 sessionMute.id, sessionSlider.id, sessionValue.id, 12.0F);
        Check(session.focusRects.contains(sessionSlider.id),
              "focused session slider remains scroll-revealed");
    }
    Check(preferredMasterSlider > compactMasterSlider,
          "Audio slider grows with the admitted preferred width");
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    if (argc != 3 || std::wstring_view(argv[1]) != L"--fixture") {
        std::cerr << "Usage: AudioMixerWidthRendererTests --fixture <snapshot.json>\n";
        return EXIT_FAILURE;
    }
    Run(argv[2]);
    std::cout << "AudioMixerWidthRendererTests: " << checks << " checks passed\n";
    return EXIT_SUCCESS;
}
