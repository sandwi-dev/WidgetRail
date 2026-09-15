#pragma once

#include "ControllerGuideVisual.h"
#include "TrayLayout.h"
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <string>

namespace widgetrail::shell {

struct RadialTrayColors final {
    D2D1_COLOR_F surface, selected, text, selectedText, muted, focus;
};

// Presentation only. TrayLayout remains the authority for pointer, controller,
// and accessibility geometry; the small visual gutters do not create dead zones.
inline void DrawRadialTray(
    ID2D1RenderTarget* target, IDWriteFactory* textFactory, IDWriteTextFormat* font,
    const TrayLayout& layout, std::size_t selectedSlot, std::wstring_view title,
    const RadialTrayColors& colors, float textScale, float focusWidth) {
    if (!layout.radialBounds) return;
    const auto& bounds = *layout.radialBounds;
    const float size = std::min(bounds.width, bounds.height);
    const float cx = bounds.x + bounds.width / 2, cy = bounds.y + bounds.height / 2;
    const float outer = size * .475F, inner = size * .275F, hub = size * .245F;
    const float ui = std::min(1.0F, size / 400.0F);
    constexpr float pi = 3.14159265358979323846F;
    const auto blend = [](D2D1_COLOR_F a, D2D1_COLOR_F b, float amount) {
        return D2D1::ColorF(a.r + (b.r - a.r) * amount,
            a.g + (b.g - a.g) * amount, a.b + (b.b - a.b) * amount, 1.0F);
    };
    const auto base = blend(colors.surface, colors.surface, 0);
    const auto selected = blend(base, colors.selected, colors.selected.a);
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> brush;
    if (FAILED(target->CreateSolidColorBrush(base, brush.GetAddressOf()))) return;
    const auto ellipse = [&](float radius) {
        return D2D1::Ellipse(D2D1::Point2F(cx, cy), radius, radius);
    };
    // An opaque foundation keeps artwork and text behind the wheel from showing
    // through themes whose ordinary tray items are transparent.
    target->FillEllipse(ellipse(size * .49F), brush.Get());
    brush->SetColor(blend(base, colors.text, .12F));
    target->DrawEllipse(ellipse(size * .49F), brush.Get(), 1.0F);

    Microsoft::WRL::ComPtr<ID2D1Factory> factory;
    target->GetFactory(factory.GetAddressOf());
    const auto point = [&](float angle, float radius) {
        return D2D1::Point2F(cx + std::sin(angle) * radius, cy - std::cos(angle) * radius);
    };
    const auto arc = [&](float end, float radius, D2D1_SWEEP_DIRECTION direction) {
        return D2D1::ArcSegment(point(end, radius), D2D1::SizeF(radius, radius),
            0, direction, D2D1_ARC_SIZE_SMALL);
    };
    for (const auto& tile : layout.tiles) {
        const float angle = static_cast<float>(tile.slot % kRadialPageSize) * pi / 4;
        const float start = angle - pi / 8 + .025F, end = angle + pi / 8 - .025F;
        const bool selectedTile = tile.slot == selectedSlot;
        Microsoft::WRL::ComPtr<ID2D1PathGeometry> segment;
        Microsoft::WRL::ComPtr<ID2D1GeometrySink> sink;
        if (FAILED(factory->CreatePathGeometry(segment.GetAddressOf())) ||
            FAILED(segment->Open(sink.GetAddressOf()))) continue;
        sink->BeginFigure(point(start, inner), D2D1_FIGURE_BEGIN_FILLED);
        sink->AddLine(point(start, outer));
        sink->AddArc(arc(end, outer, D2D1_SWEEP_DIRECTION_CLOCKWISE));
        sink->AddLine(point(end, inner));
        sink->AddArc(arc(start, inner, D2D1_SWEEP_DIRECTION_COUNTER_CLOCKWISE));
        sink->EndFigure(D2D1_FIGURE_END_CLOSED);
        if (FAILED(sink->Close())) continue;
        brush->SetColor(selectedTile ? selected : blend(base, colors.text, .055F));
        target->FillGeometry(segment.Get(), brush.Get());
        // Only the outside arc is accented, avoiding a bright wedge through the
        // center. High-contrast focus widths still apply to the selected segment.
        if (selectedTile) {
            Microsoft::WRL::ComPtr<ID2D1PathGeometry> accent;
            sink.Reset();
            if (SUCCEEDED(factory->CreatePathGeometry(accent.GetAddressOf())) &&
                SUCCEEDED(accent->Open(sink.GetAddressOf()))) {
                sink->BeginFigure(point(start + .025F, outer - 2), D2D1_FIGURE_BEGIN_HOLLOW);
                sink->AddArc(arc(end - .025F, outer - 2, D2D1_SWEEP_DIRECTION_CLOCKWISE));
                sink->EndFigure(D2D1_FIGURE_END_OPEN);
                if (SUCCEEDED(sink->Close())) {
                    brush->SetColor(colors.focus);
                    target->DrawGeometry(accent.Get(), brush.Get(), std::max(3.0F * ui, focusWidth));
                }
            }
        }
    }
    brush->SetColor(blend(base, colors.text, .025F));
    target->FillEllipse(ellipse(hub), brush.Get());
    brush->SetColor(blend(base, colors.text, .1F));
    target->DrawEllipse(ellipse(hub), brush.Get(), 1.0F);

    wchar_t family[128]{};
    if (FAILED(font->GetFontFamilyName(family, 128))) return;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> nameFormat, detailFormat;
    if (FAILED(textFactory->CreateTextFormat(family, nullptr, font->GetFontWeight(),
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 15 * ui * textScale,
            L"", nameFormat.GetAddressOf())) ||
        FAILED(textFactory->CreateTextFormat(family, nullptr, font->GetFontWeight(),
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 11 * ui * textScale,
            L"", detailFormat.GetAddressOf()))) return;
    for (auto* format : {nameFormat.Get(), detailFormat.Get()}) {
        format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
    }
    const auto text = [&](std::wstring_view value, IDWriteTextFormat* format,
                          D2D1_RECT_F rectangle, D2D1_COLOR_F color) {
        brush->SetColor(color);
        target->DrawTextW(value.data(), static_cast<UINT32>(value.size()), format,
            rectangle, brush.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    };
    text(title, nameFormat.Get(), D2D1::RectF(cx - hub + 10 * ui,
        cy - (layout.pageCount > 1 ? 52 : 28) * ui, cx + hub - 10 * ui,
        cy + (layout.pageCount > 1 ? -4 : 28) * ui), colors.text);
    if (layout.pageCount > 1) {
        const auto pill = D2D1::RoundedRect(
            D2D1::RectF(cx - 52 * ui, cy + 4 * ui, cx + 52 * ui, cy + 34 * ui), 15 * ui, 15 * ui);
        brush->SetColor(blend(base, colors.text, .07F));
        target->FillRoundedRectangle(pill, brush.Get());
        brush->SetColor(colors.muted);
        guide::DrawControl(target, detailFormat.Get(), guide::Control::RightStick,
            D2D1::RectF(cx - 12 * ui, cy + 7 * ui, cx + 12 * ui, cy + 31 * ui), brush.Get());
        for (const auto* overflow : {&layout.previousOverflow, &layout.nextOverflow}) {
            if (!*overflow) continue;
            const auto& rect = (*overflow)->bounds;
            const float x = rect.x + rect.width / 2, y = rect.y + rect.height / 2;
            const float direction = (*overflow)->direction == TrayOverflowDirection::Previous ? -1.0F : 1.0F;
            const auto tip = D2D1::Point2F(x + direction * 2 * ui, y);
            target->DrawLine(D2D1::Point2F(x - direction * 2 * ui, y - 4 * ui), tip, brush.Get(), 1.5F * ui);
            target->DrawLine(tip, D2D1::Point2F(x - direction * 2 * ui, y + 4 * ui), brush.Get(), 1.5F * ui);
        }
        text(std::to_wstring(layout.page + 1) + L" / " + std::to_wstring(layout.pageCount),
            detailFormat.Get(), D2D1::RectF(cx - 40 * ui, cy + 41 * ui, cx + 40 * ui, cy + 61 * ui), colors.muted);
    }
}

} // namespace widgetrail::shell
