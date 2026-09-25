#pragma once

#include "DeclarativeLayout.h"
#include <d2d1.h>
#include <wrl/client.h>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail {
enum class WidgetCompositionKind { Raster, Content, Layout, Selection, Modal, Scrim };

// A paint-ordered scene, in widget DIPs. Only raster nodes own pixels; group
// nodes own compositor transforms. Input remains in the current widget tree.
struct WidgetCompositionNode final {
    std::wstring id, parent, clock, key;
    WidgetCompositionKind kind{WidgetCompositionKind::Raster};
    int order{};
    declarative::Rect bounds, clip;
    Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
    std::optional<D2D1_COLOR_F> solid;
    std::shared_ptr<void> rasterLease;
};

struct WidgetCompositionScene final {
    static constexpr std::size_t MaximumNodes = 256;
    static constexpr std::size_t MaximumBytes = 64U * 1024U * 1024U;
    std::wstring authority;
    declarative::Rect viewport;
    float scale{1}, cornerRadius{};
    bool reducedMotion{};
    bool directContent{};
    std::vector<WidgetCompositionNode> nodes;
    std::size_t rasterBytes{};
};
} // namespace widgetrail
