#pragma once

#include "DeclarativeLayout.h"
#include <d2d1.h>
#include <cmath>

namespace widgetrail {
// Remove float cancellation noise around the layout's physical-pixel grid,
// preserving genuine subpixel geometry. Painting and identity share this rule.
inline declarative::Rect CompositionLocalRect(declarative::Rect box,
    float originX, float originY, float scale) noexcept {
    scale = std::isfinite(scale) && scale > 0 ? scale : 1.0F;
    const auto stable = [scale](float value) {
        const auto pixels = value * scale;
        const auto snapped = std::round(pixels);
        constexpr float pixelGridTolerance = .0001F;
        return std::abs(pixels - snapped) < pixelGridTolerance ? (snapped == 0 ? 0.0F : snapped / scale) : value;
    };
    return {stable(box.x - originX), stable(box.y - originY), stable(box.width), stable(box.height)};
}

// Fold a capture's translation into primitive coordinates before rasterizing
// text/curves. Existing device-space clip stacks stay valid, and the rasterizer
// cannot observe the old screen position. Other transforms keep their path.
class CompositionPaintSpace final {
public:
    CompositionPaintSpace(ID2D1RenderTarget* target, bool capture, float scale) noexcept
        : target_(capture ? target : nullptr), scale_(scale) {
        if (!target_) return;
        target_->GetTransform(&saved_);
        if (saved_._11 != 1 || saved_._12 != 0 || saved_._21 != 0 || saved_._22 != 1) {
            target_ = nullptr;
            return;
        }
        target_->SetTransform(D2D1::Matrix3x2F::Identity());
    }
    ~CompositionPaintSpace() { if (target_) target_->SetTransform(saved_); }
    CompositionPaintSpace(const CompositionPaintSpace&) = delete;
    CompositionPaintSpace& operator=(const CompositionPaintSpace&) = delete;
    [[nodiscard]] declarative::Rect Local(declarative::Rect box) const noexcept {
        return target_ ? CompositionLocalRect(box, -saved_._31, -saved_._32, scale_) : box;
    }
private:
    ID2D1RenderTarget* target_{};
    float scale_{};
    D2D1_MATRIX_3X2_F saved_{};
};

} // namespace widgetrail
