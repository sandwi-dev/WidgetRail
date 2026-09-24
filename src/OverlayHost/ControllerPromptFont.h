#pragma once

#include "ControllerPrompt.h"
#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <filesystem>
#pragma comment(lib, "dwrite.lib")

namespace widgetrail::controller {

// Private font face, loaded once without registering a system font. Direct glyph
// runs use explicit font faces and never substitute ordinary text glyphs.
class ControllerPromptFont final {
public:
    explicit ControllerPromptFont(const std::filesystem::path& relativePath) noexcept {
        try {
            wchar_t executable[32768]{};
            const DWORD length = GetModuleFileNameW(nullptr, executable, 32768);
            if (!length || length >= 32768) return;
            const auto path = std::filesystem::path(executable).parent_path() /
                L"assets" / L"fonts" / relativePath;
            Microsoft::WRL::ComPtr<IDWriteFactory> factory;
            if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                reinterpret_cast<IUnknown**>(factory.GetAddressOf())))) return;
            Microsoft::WRL::ComPtr<IDWriteFontFile> file;
            if (FAILED(factory->CreateFontFileReference(path.c_str(), nullptr, file.GetAddressOf()))) return;
            BOOL supported{};
            DWRITE_FONT_FILE_TYPE fileType{};
            DWRITE_FONT_FACE_TYPE faceType{};
            UINT32 count{};
            if (FAILED(file->Analyze(&supported, &fileType, &faceType, &count)) || !supported || !count) return;
            IDWriteFontFile* files[]{file.Get()};
            if (FAILED(factory->CreateFontFace(faceType, 1, files, 0,
                DWRITE_FONT_SIMULATIONS_NONE, face_.GetAddressOf()))) return;
            face_->GetMetrics(&metrics_);
        } catch (...) {
            face_.Reset();
        }
    }

    [[nodiscard]] bool Draw(ID2D1RenderTarget* target, UINT32 character,
        D2D1_RECT_F bounds, ID2D1Brush* brush) const noexcept {
        if (!face_ || !character || !metrics_.designUnitsPerEm) return false;
        UINT16 glyph{};
        DWRITE_GLYPH_METRICS ink{};
        if (FAILED(face_->GetGlyphIndices(&character, 1, &glyph)) || !glyph ||
            FAILED(face_->GetDesignGlyphMetrics(&glyph, 1, &ink))) return false;
        const float width = static_cast<float>(ink.advanceWidth) - ink.leftSideBearing - ink.rightSideBearing;
        const float height = static_cast<float>(ink.advanceHeight) - ink.topSideBearing - ink.bottomSideBearing;
        if (width <= 0 || height <= 0 || bounds.right <= bounds.left || bounds.bottom <= bounds.top) return false;
        const float scale = .88F * std::min((bounds.right - bounds.left) / width, (bounds.bottom - bounds.top) / height);
        const float advance = ink.advanceWidth * scale;
        const D2D1_POINT_2F baseline{
            (bounds.left + bounds.right) / 2 - (ink.leftSideBearing + width / 2) * scale,
            (bounds.top + bounds.bottom) / 2 - (ink.topSideBearing - ink.verticalOriginY + height / 2) * scale};
        const DWRITE_GLYPH_RUN run{face_.Get(), metrics_.designUnitsPerEm * scale, 1, &glyph, &advance, nullptr, FALSE, 0};
        target->DrawGlyphRun(baseline, &run, brush);
        return true;
    }

private:
    Microsoft::WRL::ComPtr<IDWriteFontFace> face_;
    DWRITE_FONT_METRICS metrics_{};
};

// One private face per family, loaded lazily. No system registration, image
// decoding, or per-widget asset lifetime is involved.
inline bool DrawPrompt(ID2D1RenderTarget* target, Control control,
    D2D1_RECT_F bounds, ID2D1Brush* brush,
    bool playStation = UsePlayStationControls()) noexcept {
    const auto character = PromptCharacter(control, playStation);
    if (!character) return false;
    if (playStation && control == Control::Guide) {
        static const ControllerPromptFont fallback(L"promptfont/promptfont.ttf");
        return fallback.Draw(target, character, bounds, brush);
    }
    if (playStation) {
        static const ControllerPromptFont font(L"kenney/kenney_input_playstation_series.ttf");
        return font.Draw(target, character, bounds, brush);
    }
    static const ControllerPromptFont font(L"kenney/kenney_input_xbox_series.ttf");
    return font.Draw(target, character, bounds, brush);
}

} // namespace widgetrail::controller
