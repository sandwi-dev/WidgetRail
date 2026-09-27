#pragma once

#include "NativeStyle.h"
#include "RemoteImageCache.h"
#include "WidgetCompositionScene.h"
#include <string>
#include <type_traits>

namespace widgetrail {
// Exact bounded paint dependencies, not a hash: a collision must never authorize
// stale pixels. Resolved immutable styles share their storage with preparation.
struct CompositionPaintIdentity final {
    static constexpr std::size_t MaximumBytes = 64 * 1024;
    static constexpr std::size_t MaximumRetainedBytes = 4 * 1024 * 1024;
    std::string values;
    std::vector<NativeRenderStyle> styles;
    std::vector<std::weak_ptr<const RemoteDecodedImage>> images;
    bool cacheable{true};

    template<class T> requires(std::is_arithmetic_v<T> || std::is_enum_v<T>)
    void Scalar(T value) {
        if (values.size() + sizeof(value) > MaximumBytes) { cacheable = false; return; }
        values.append(reinterpret_cast<const char*>(&value), sizeof(value));
    }
    void Text(std::wstring_view value) {
        Scalar(value.size());
        const auto bytes = value.size() * sizeof(wchar_t);
        if (values.size() + bytes > MaximumBytes) { cacheable = false; return; }
        values.append(reinterpret_cast<const char*>(value.data()), bytes);
    }
    void Box(declarative::Rect value) { Scalar(value.x); Scalar(value.y); Scalar(value.width); Scalar(value.height); }
    void Style(const NativeRenderStyle& style) {
        if (styles.size() >= 1024) { cacheable = false; return; }
        styles.push_back(style);
    }
    [[nodiscard]] std::size_t Bytes() const noexcept {
        return values.size() + styles.size() * sizeof(NativeRenderStyle) + images.size() * sizeof(images.front());
    }
    bool operator==(const CompositionPaintIdentity& other) const {
        if (!cacheable || !other.cacheable || values != other.values || styles != other.styles || images.size() != other.images.size()) return false;
        for (std::size_t i = 0; i < images.size(); ++i)
            if (images[i].owner_before(other.images[i]) || other.images[i].owner_before(images[i])) return false;
        return true;
    }
};

struct CompositionPaintEntry final {
    CompositionPaintIdentity identity;
    resources::UiResource<ID2D1Bitmap> bitmap;
    std::shared_ptr<void> lease;
};
} // namespace widgetrail
