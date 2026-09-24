#pragma once

#include "PopupMenuMetrics.h"
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstddef>
#include <string>
#include <string_view>

namespace widgetrail::shell {

struct PopupMenuColors final {
    D2D1_COLOR_F surface, selection, text, selectedText, muted, focus;
    bool highContrast{};
};

inline D2D1_COLOR_F MenuShade(D2D1_COLOR_F color, float amount) noexcept {
    const auto shade = [amount](float value) {
        return amount >= 0 ? value + (1 - value) * amount : value * (1 + amount);
    };
    return {shade(color.r), shade(color.g), shade(color.b), color.a};
}

inline float PopupMenuCornerRadius(float themedRadius) noexcept {
    return std::clamp(themedRadius * .75F, 0.0F, 12.0F);
}

inline float MeasurePopupMenuText(IDWriteFactory* factory, IDWriteTextFormat* font,
    std::wstring_view text) {
    if (!factory || !font || text.empty()) return 0;
    Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
    if (FAILED(factory->CreateTextLayout(text.data(), static_cast<UINT32>(text.size()), font,
        16384, 16384, layout.GetAddressOf()))) return 0;
    layout->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
    DWRITE_TEXT_METRICS metrics{};
    return SUCCEEDED(layout->GetMetrics(&metrics)) ? metrics.widthIncludingTrailingWhitespace : 0;
}

inline Microsoft::WRL::ComPtr<IDWriteTextLayout> CreatePopupMenuTextLayout(
    IDWriteFactory* factory, IDWriteTextFormat* font, std::wstring_view text, float width, float height) {
    Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
    if (!factory || !font || width <= 0 || height <= 0 ||
        FAILED(factory->CreateTextLayout(text.data(), static_cast<UINT32>(text.size()), font,
            width, height, layout.GetAddressOf()))) return {};
    layout->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
    layout->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
    layout->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
    Microsoft::WRL::ComPtr<IDWriteInlineObject> ellipsis;
    if (SUCCEEDED(factory->CreateEllipsisTrimmingSign(font, ellipsis.GetAddressOf()))) {
        const DWRITE_TRIMMING trimming{DWRITE_TRIMMING_GRANULARITY_CHARACTER, 0, 0};
        layout->SetTrimming(&trimming, ellipsis.Get());
    }
    return layout;
}

inline void DrawPopupMenuPanel(ID2D1RenderTarget* target, const declarative::Rect& bounds,
    const PopupMenuColors& colors, float themedRadius, float focusWidth) {
    if (!target || bounds.width <= kPopupMenuShadowMargin * 2 ||
        bounds.height <= kPopupMenuShadowMargin * 2) return;
    const auto face = D2D1::RectF(bounds.x + kPopupMenuShadowMargin, bounds.y + kPopupMenuShadowMargin,
        bounds.x + bounds.width - kPopupMenuShadowMargin, bounds.y + bounds.height - kPopupMenuShadowMargin);
    const float radius = PopupMenuCornerRadius(themedRadius);
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> brush;
    auto surface = colors.surface;
    surface.a = 1;
    if (FAILED(target->CreateSolidColorBrush(surface, brush.GetAddressOf()))) return;
    if (!colors.highContrast) {
        // Every shadow layer stays inside the reserved bounds used by the host.
        auto shadow = MenuShade(surface, -.65F);
        shadow.a = .09F;
        brush->SetColor(shadow);
        for (int layer = 5; layer > 0; --layer) {
            const float spread = static_cast<float>(layer) * .8F;
            target->FillRoundedRectangle(D2D1::RoundedRect(
                D2D1::RectF(face.left - spread, face.top - spread + 1.5F,
                    face.right + spread, face.bottom + spread + 1.5F),
                radius + spread, radius + spread), brush.Get());
        }
        brush->SetColor(surface);
    }
    const auto panel = D2D1::RoundedRect(face, radius, radius);
    target->FillRoundedRectangle(panel, brush.Get());
    if (!colors.highContrast) {
        const D2D1_GRADIENT_STOP stops[]{
            {0, MenuShade(surface, .08F)}, {1, MenuShade(surface, -.08F)}};
        Microsoft::WRL::ComPtr<ID2D1GradientStopCollection> collection;
        Microsoft::WRL::ComPtr<ID2D1LinearGradientBrush> lighting;
        if (SUCCEEDED(target->CreateGradientStopCollection(stops, 2, collection.GetAddressOf())) &&
            SUCCEEDED(target->CreateLinearGradientBrush(
                D2D1::LinearGradientBrushProperties({face.left, face.top}, {face.left, face.bottom}),
                collection.Get(), lighting.GetAddressOf()))) {
            target->FillRoundedRectangle(panel, lighting.Get());
        }
    }
    auto border = colors.highContrast ? colors.focus : colors.muted;
    if (!colors.highContrast) border.a *= .55F;
    brush->SetColor(border);
    target->DrawRoundedRectangle(panel, brush.Get(), colors.highContrast
        ? std::clamp(focusWidth, 1.0F, kPopupMenuShadowMargin) : 1.0F);
}

inline void DrawPopupMenuSelection(ID2D1RenderTarget* target, const declarative::Rect& bounds,
    const PopupMenuColors& colors, float themedRadius) {
    if (!target || bounds.width <= 4 || bounds.height <= 4) return;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> fill;
    if (SUCCEEDED(target->CreateSolidColorBrush(colors.selection, fill.GetAddressOf()))) {
        const float radius = PopupMenuCornerRadius(themedRadius) * .65F;
        const auto selectedBounds = D2D1::RoundedRect(D2D1::RectF(
            bounds.x + 2, bounds.y + 2, bounds.x + bounds.width - 2, bounds.y + bounds.height - 2), radius, radius);
        target->FillRoundedRectangle(selectedBounds, fill.Get());
        if (!colors.highContrast) {
            auto highlight = MenuShade(colors.selection, .22F);
            highlight.a *= .35F;
            fill->SetColor(highlight);
            target->DrawRoundedRectangle(selectedBounds, fill.Get(), 1.0F);
        }
        const float markerHeight = std::min(20.0F, bounds.height * .5F);
        const float markerTop = bounds.y + (bounds.height - markerHeight) * .5F;
        const float markerRadius = std::min(radius, 1.5F);
        if (bounds.width >= 12) {
            fill->SetColor(colors.highContrast ? colors.selectedText : colors.focus);
            target->FillRoundedRectangle(D2D1::RoundedRect(
                D2D1::RectF(bounds.x + 4.5F, markerTop, bounds.x + 7.5F, markerTop + markerHeight),
                markerRadius, markerRadius), fill.Get());
        }
    }
}

inline void DrawPopupMenuText(ID2D1RenderTarget* target, IDWriteFactory* factory,
    IDWriteTextFormat* font, const declarative::Rect& bounds, std::wstring_view label,
    std::wstring_view detail, ID2D1Brush* ink, ID2D1Brush* muted) {
    if (!target || !factory || !font || !ink || bounds.width <= 0 || bounds.height <= 0) return;
    std::wstring text{label};
    if (!detail.empty()) { text += L'\n'; text += detail; }
    // Per-layout alignment cannot leak into the shared hint format or guide.
    const auto layout = CreatePopupMenuTextLayout(factory, font, text, bounds.width, bounds.height);
    if (!layout) return;
    if (!detail.empty()) layout->SetDrawingEffect(muted,
        {static_cast<UINT32>(label.size() + 1), static_cast<UINT32>(detail.size())});
    target->DrawTextLayout({bounds.x, bounds.y}, layout.Get(), ink, D2D1_DRAW_TEXT_OPTIONS_CLIP);
}

inline void DrawPopupMenuRow(ID2D1RenderTarget* target, IDWriteFactory* factory,
    IDWriteTextFormat* font, const declarative::Rect& bounds, std::wstring_view label,
    std::wstring_view detail, bool selected, bool enabled, const PopupMenuColors& colors,
    float themedRadius) {
    if (!target || !factory || !font || bounds.width <= kPopupMenuContentInset + 12 || bounds.height <= 6) return;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> ink, muted;
    const auto foreground = !enabled ? colors.muted : selected ? colors.selectedText : colors.text;
    if (FAILED(target->CreateSolidColorBrush(foreground, ink.GetAddressOf())) ||
        FAILED(target->CreateSolidColorBrush(colors.muted, muted.GetAddressOf()))) return;
    if (selected) DrawPopupMenuSelection(target, bounds, colors, themedRadius);
    DrawPopupMenuText(target, factory, font,
        {bounds.x + kPopupMenuContentInset, bounds.y + 3,
            bounds.width - kPopupMenuContentInset - 12, bounds.height - 6},
        label, detail, ink.Get(), muted.Get());
}

} // namespace widgetrail::shell
