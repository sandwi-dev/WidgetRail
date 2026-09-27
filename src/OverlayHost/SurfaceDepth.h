#pragma once

#include "DeclarativeLayout.h"
#include "NativeStyle.h"
#include "PaintResources.h"
#include <d2d1_1.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <compare>
#include <cstddef>
#include <cstdint>
#include <map>
#include <utility>
#include <vector>

namespace widgetrail::surface {
using Rect = declarative::Rect;

inline float MaskScale(float radius, float blur, float scale) noexcept {
    return std::min(std::clamp(scale, 1.0F, 8.0F), 1000.0F / std::max(1.0F, radius + blur * 2 + 4));
}

inline float ShadowExtent(float blur, float scale, float radius = 0) noexcept {
    scale = MaskScale(radius, blur, scale);
    return blur > 0 ? (3 * std::ceil(blur * scale / 3) + 1) / scale : 0;
}

inline Rect PaintBounds(Rect bounds, const NativeRenderStyle &style, float scale) noexcept {
    if (!style.shadowColor() || style.shadowColor()->alpha <= 0) return bounds;
    const float maximumRadius = std::min(bounds.width, bounds.height) * .5F;
    const float radius = style.shape() == NativeShape::Circle || style.shape() == NativeShape::Pill
        ? maximumRadius : std::min(maximumRadius, std::max(style.cornerRadiusPx(), style.shape() == NativeShape::Rounded ? 8.0F : 0.0F));
    const float spread = ShadowExtent(style.shadowBlurPx(), scale, radius);
    const float left = std::min(0.0F, style.shadowOffsetXPx() - spread);
    const float top = std::min(0.0F, style.shadowOffsetYPx() - spread);
    const float right = std::max(0.0F, style.shadowOffsetXPx() + spread);
    const float bottom = std::max(0.0F, style.shadowOffsetYPx() + spread);
    return {bounds.x + left, bounds.y + top, bounds.width + right - left, bounds.height + bottom - top};
}

inline D2D1_COLOR_F Shade(D2D1_COLOR_F color, float amount) noexcept {
    const auto channel = [amount](float value) { return amount >= 0 ? value + (1 - value) * amount : value * (1 + amount); };
    return {channel(color.r), channel(color.g), channel(color.b), color.a};
}

inline bool Fill(ID2D1RenderTarget *target, D2D1_ROUNDED_RECT bounds,
                 D2D1_COLOR_F top, D2D1_COLOR_F bottom, paint::Resources* resources = nullptr) {
    if (!target) return false;
    if (resources) {
        const auto brush = resources->Gradient(target, {bounds.rect.left, bounds.rect.top}, {bounds.rect.left, bounds.rect.bottom}, top, bottom);
        if (!brush) return false;
        target->FillRoundedRectangle(bounds, brush.Get());
        return true;
    }
    const D2D1_GRADIENT_STOP stops[]{{0, top}, {1, bottom}};
    Microsoft::WRL::ComPtr<ID2D1GradientStopCollection> colors;
    Microsoft::WRL::ComPtr<ID2D1LinearGradientBrush> brush;
    auto hr = target->CreateGradientStopCollection(stops, 2, colors.GetAddressOf());
    if (SUCCEEDED(hr)) hr = target->CreateLinearGradientBrush(
        D2D1::LinearGradientBrushProperties({bounds.rect.left, bounds.rect.top}, {bounds.rect.left, bounds.rect.bottom}),
        colors.Get(), brush.GetAddressOf());
    if (FAILED(hr)) return false;
    target->FillRoundedRectangle(bounds, brush.Get());
    return true;
}

// One resource domain per owner (shared by compatible render targets). Nine
// slices make shadow cost independent of panel size. Color/opacity are applied
// at paint time, so themes and focus states reuse the same bounded alpha masks.
class ShadowCache final {
public:
    struct Stats { std::size_t entries{}, bytes{}; std::uint64_t creates{}, hits{}; };
    static constexpr std::size_t MaximumBytes = 16 * 1024 * 1024, MaximumEntries = 32;
    void Clear() noexcept { entries_.clear(); bytes_ = 0; domain_.Reset(); }
    void Bind(ID2D1RenderTarget *target) {
        Microsoft::WRL::ComPtr<IUnknown> identity;
        Microsoft::WRL::ComPtr<ID2D1DeviceContext> context;
        if (SUCCEEDED(target->QueryInterface(IID_PPV_ARGS(context.GetAddressOf())))) {
            Microsoft::WRL::ComPtr<ID2D1Device> device;
            context->GetDevice(device.GetAddressOf());
            if (device) device.As(&identity);
        }
        if (!identity) target->QueryInterface(IID_PPV_ARGS(identity.GetAddressOf()));
        if (domain_.Get() != identity.Get()) { Clear(); domain_ = std::move(identity); }
    }
    [[nodiscard]] Stats stats() const noexcept { return {entries_.size(), bytes_, creates_, hits_}; }

    bool Draw(ID2D1RenderTarget *target, Rect bounds, float radius, float blur,
              float offsetX, float offsetY, D2D1_COLOR_F color, float scale, paint::Resources* resources = nullptr) {
        if (!target || bounds.width <= 0 || bounds.height <= 0 || color.a <= 0) return false;
        Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> brush;
        if (resources) brush = resources->Solid(target, color);
        else (void)target->CreateSolidColorBrush(color, brush.GetAddressOf());
        if (!brush) return false;
        radius = std::clamp(radius, 0.0F, std::min(bounds.width, bounds.height) * .5F);
        if (blur <= 0) {
            target->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(bounds.x + offsetX, bounds.y + offsetY,
                bounds.x + offsetX + bounds.width, bounds.y + offsetY + bounds.height), radius, radius), brush.Get());
            return true;
        }
        // Large DPI/shape combinations reduce mask resolution, never allocate
        // a viewport-sized blur or unbounded temporary working buffers.
        scale = MaskScale(radius, blur, scale);
        const int boxRadius = std::max(1, static_cast<int>(std::ceil(blur * scale / 3)));
        const int padPixels = boxRadius * 3 + 1;
        const int minimumPatch = 2 * (static_cast<int>(std::ceil(radius * scale)) + padPixels) + 1;
        const Key key{static_cast<int>(std::round(radius * scale * 8)), boxRadius,
            std::max(1, static_cast<int>(std::round(scale * 1000))),
            std::max(1, std::min(minimumPatch, static_cast<int>(std::ceil(bounds.width * scale)))),
            std::max(1, std::min(minimumPatch, static_cast<int>(std::ceil(bounds.height * scale))))};
        auto found = entries_.find(key);
        if (found == entries_.end()) {
            Entry entry;
            if (!Create(target, key, entry)) return false;
            while (!entries_.empty() && (entries_.size() >= MaximumEntries || bytes_ + entry.bytes > MaximumBytes)) {
                auto oldest = std::min_element(entries_.begin(), entries_.end(), [](const auto &a, const auto &b) {
                    return a.second.used < b.second.used;
                });
                bytes_ -= oldest->second.bytes; entries_.erase(oldest);
            }
            bytes_ += entry.bytes;
            found = entries_.emplace(key, std::move(entry)).first;
            ++creates_;
        } else ++hits_;
        auto &entry = found->second;
        entry.used = ++clock_;
        const float pad = entry.pad;
        const Rect outer{bounds.x + offsetX - pad, bounds.y + offsetY - pad,
            bounds.width + 2 * pad, bounds.height + 2 * pad};
        const float cx = std::min(entry.capX, outer.width * .5F), cy = std::min(entry.capY, outer.height * .5F);
        const float sx[]{0, entry.capX, entry.width - entry.capX, entry.width};
        const float sy[]{0, entry.capY, entry.height - entry.capY, entry.height};
        const float dx[]{outer.x, outer.x + cx, outer.x + outer.width - cx, outer.x + outer.width};
        const float dy[]{outer.y, outer.y + cy, outer.y + outer.height - cy, outer.y + outer.height};
        const auto antialias = target->GetAntialiasMode();
        target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
        for (int y = 0; y < 3; ++y) for (int x = 0; x < 3; ++x) {
            if (dx[x + 1] <= dx[x] || dy[y + 1] <= dy[y]) continue;
            const auto destination = D2D1::RectF(dx[x], dy[y], dx[x + 1], dy[y + 1]);
            const auto source = D2D1::RectF(sx[x], sy[y], sx[x + 1], sy[y + 1]);
            target->FillOpacityMask(entry.bitmap.Get(), brush.Get(), D2D1_OPACITY_MASK_CONTENT_GRAPHICS, &destination, &source);
        }
        target->SetAntialiasMode(antialias);
        return true;
    }

private:
    struct Key {
        int radiusEighths{}, boxRadius{}, scaleMilli{}, width{}, height{};
        auto operator<=>(const Key &) const = default;
    };
    struct Entry {
        Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
        float pad{}, capX{}, capY{}, width{}, height{};
        std::size_t bytes{};
        std::uint64_t used{};
    };
    Microsoft::WRL::ComPtr<IUnknown> domain_;
    std::map<Key, Entry> entries_;
    std::size_t bytes_{};
    std::uint64_t clock_{}, creates_{}, hits_{};

    static void Blur(std::vector<float> &pixels, std::vector<float> &scratch, int width, int height, int radius, bool vertical) {
        const int rows = vertical ? width : height, columns = vertical ? height : width;
        const auto index = [=](int row, int column) { return vertical ? column * width + row : row * width + column; };
        const float divisor = static_cast<float>(radius * 2 + 1);
        for (int row = 0; row < rows; ++row) {
            float sum{};
            for (int i = 0; i <= radius; ++i) sum += pixels[index(row, i)];
            for (int i = 0; i < columns; ++i) {
                scratch[index(row, i)] = sum / divisor;
                if (i - radius >= 0) sum -= pixels[index(row, i - radius)];
                if (i + radius + 1 < columns) sum += pixels[index(row, i + radius + 1)];
            }
        }
        pixels.swap(scratch);
    }

    static bool Create(ID2D1RenderTarget *target, Key key, Entry &entry) {
        const float radius = key.radiusEighths / 8.0F, scale = key.scaleMilli / 1000.0F;
        const int pad = key.boxRadius * 3 + 1, cap = pad + static_cast<int>(std::ceil(radius));
        const int width = key.width + pad * 2, height = key.height + pad * 2;
        const auto count = static_cast<std::size_t>(width) * height;
        if (width > 2048 || height > 2048 || count * 4 > MaximumBytes) return false;
        std::vector<float> alpha(count), scratch(count);
        const float centerX = width * .5F, centerY = height * .5F;
        for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
            const float dx = std::abs(x + .5F - centerX) - (key.width * .5F - radius);
            const float dy = std::abs(y + .5F - centerY) - (key.height * .5F - radius);
            const float distance = std::hypot(std::max(dx, 0.0F), std::max(dy, 0.0F)) +
                std::min(std::max(dx, dy), 0.0F) - radius;
            alpha[static_cast<std::size_t>(y) * width + x] = std::clamp(.5F - distance, 0.0F, 1.0F);
        }
        // Three separable box passes approximate a Gaussian in linear time.
        for (int pass = 0; pass < 3; ++pass) {
            Blur(alpha, scratch, width, height, key.boxRadius, false);
            Blur(alpha, scratch, width, height, key.boxRadius, true);
        }
        std::vector<std::uint32_t> pixels(count);
        for (std::size_t i = 0; i < count; ++i) {
            const auto a = static_cast<std::uint32_t>(std::clamp(std::round(alpha[i] * 255), 0.0F, 255.0F));
            pixels[i] = a * 0x01010101U;
        }
        const auto properties = D2D1::BitmapProperties(D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,
            D2D1_ALPHA_MODE_PREMULTIPLIED), 96 * scale, 96 * scale);
        if (FAILED(target->CreateBitmap(D2D1::SizeU(width, height), pixels.data(), width * 4, properties, entry.bitmap.GetAddressOf()))) return false;
        entry.pad = pad / scale;
        entry.capX = std::min(static_cast<float>(cap + pad), width * .5F) / scale;
        entry.capY = std::min(static_cast<float>(cap + pad), height * .5F) / scale;
        entry.width = width / scale; entry.height = height / scale; entry.bytes = count * 4;
        return true;
    }
};
} // namespace widgetrail::surface
