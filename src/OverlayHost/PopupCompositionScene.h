#pragma once

#include "WidgetCompositionScene.h"
#include <d2d1helper.h>
#include <cmath>
#include <utility>

namespace widgetrail::shell {

// Capture a complete host popup once per content update. DWM owns entrance
// frames; navigation, dismissal and accessibility stay with the existing host.
template <class Paint>
std::shared_ptr<const WidgetCompositionScene> CapturePopupComposition(
    ID2D1RenderTarget* target, declarative::Rect bounds, declarative::Rect anchor,
    declarative::Rect viewport, float scale, std::wstring key, bool reducedMotion,
    animation::Options animations, Paint&& paint) {
    if (!target || !std::isfinite(scale) || scale <= 0 ||
        !std::isfinite(bounds.x) || !std::isfinite(bounds.y) ||
        !std::isfinite(anchor.x) || !std::isfinite(anchor.y) ||
        !std::isfinite(anchor.width) || !std::isfinite(anchor.height) ||
        !std::isfinite(bounds.width) || !std::isfinite(bounds.height) ||
        bounds.width <= 0 || bounds.height <= 0) return {};
    const auto rasterBounds = CompositionRasterBounds(bounds, scale);
    const double width = std::round(rasterBounds.width * scale);
    const double height = std::round(rasterBounds.height * scale);
    constexpr std::size_t maximumPopupBytes = 8U * 1024U * 1024U;
    if (width > 8192 || height > 8192 || width * height * 4 > maximumPopupBytes)
        return {};
    const auto size = D2D1::SizeF(rasterBounds.width, rasterBounds.height);
    const auto pixels = D2D1::SizeU(static_cast<UINT32>(width), static_cast<UINT32>(height));
    Microsoft::WRL::ComPtr<ID2D1BitmapRenderTarget> surface;
    if (FAILED(target->CreateCompatibleRenderTarget(&size, &pixels, nullptr,
            D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE, surface.GetAddressOf()))) return {};
    surface->SetDpi(96 * scale, 96 * scale);
    surface->SetTransform(D2D1::Matrix3x2F::Translation(-rasterBounds.x, -rasterBounds.y));
    surface->BeginDraw();
    surface->Clear(D2D1::ColorF(0, 0, 0, 0));
    try {
        paint(surface.Get());
    } catch (...) {
        (void)surface->EndDraw();
        throw;
    }
    if (FAILED(surface->EndDraw())) return {};
    Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
    if (FAILED(surface->GetBitmap(bitmap.GetAddressOf()))) return {};
    auto scene = std::make_shared<WidgetCompositionScene>();
    scene->authority = L"host-popup";
    scene->viewport = viewport;
    scene->scale = scale;
    scene->reducedMotion = reducedMotion;
    scene->animations = animations;
    WidgetCompositionNode group{L"popup", {}, L"popup", std::move(key), WidgetCompositionKind::Popup,
        0, bounds, viewport};
    group.popupAnchor = anchor;
    scene->nodes.push_back(std::move(group));
    WidgetCompositionNode raster{L"popup.pixels", L"popup", {}, {}, WidgetCompositionKind::Raster,
        0, rasterBounds, viewport};
    raster.bitmap = std::move(bitmap);
    scene->nodes.push_back(std::move(raster));
    scene->rasterBytes = static_cast<std::size_t>(width * height * 4);
    return scene;
}
} // namespace widgetrail::shell
