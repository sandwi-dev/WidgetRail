#pragma once

#include "DeclarativeLayout.h"
#include "WidgetAnimationPolicy.h"
#include "WidgetInteractionMotion.h"
#include <d2d1.h>
#include <wrl/client.h>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail {
// Capture in the widget's physical pixel grid, independent of layer grouping.
// Logical layout/hit targets keep their original fractional DIP bounds.
inline declarative::Rect CompositionRasterBounds(declarative::Rect bounds, float scale) noexcept {
    const float left = std::floor(bounds.x * scale + .0001F);
    const float top = std::floor(bounds.y * scale + .0001F);
    const float right = std::ceil((bounds.x + bounds.width) * scale - .0001F);
    const float bottom = std::ceil((bounds.y + bounds.height) * scale - .0001F);
    return {left / scale, top / scale, (right - left) / scale, (bottom - top) / scale};
}

enum class WidgetCompositionKind { Raster, Content, Layout, Selection, Modal, Scrim, Focus, FocusSurface, Control, Popup };

// A paint-ordered scene, in widget DIPs. Only raster nodes own pixels; group
// nodes own compositor transforms. Input remains in the current widget tree.
struct WidgetCompositionNode final {
    std::wstring id, parent, clock, key;
    WidgetCompositionKind kind{WidgetCompositionKind::Raster};
    int order{};
    declarative::Rect bounds, clip;
    Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
    std::optional<D2D1_COLOR_F> solid;
    // A live lease pins immutable pixels. Repainting must use a new token;
    // presenters can then reuse GPU content by identity, independent of position.
    std::shared_ptr<void> rasterLease;
    float controlScale{1};
    unsigned controlDuration{};
    animation::Curve controlCurve{animation::Smooth};
    declarative::Rect popupAnchor;
};

struct WidgetCompositionScene final {
    static constexpr std::size_t MaximumNodes = 256;
    static constexpr std::size_t MaximumBytes = 64U * 1024U * 1024U;
    std::wstring authority;
    declarative::Rect viewport;
    float scale{1}, cornerRadius{};
    bool reducedMotion{};
    bool directContent{};
    animation::Options animations;
    std::vector<WidgetCompositionNode> nodes;
    // Bounded to MaximumNodes. Used to retire outgoing decoration when its control changes or disappears.
    std::vector<animation::FocusTarget> focusTargets;
    std::size_t rasterBytes{};
    std::uint64_t paintCacheHits{}, paintCacheMisses{}, paintedBytes{};
};
} // namespace widgetrail
