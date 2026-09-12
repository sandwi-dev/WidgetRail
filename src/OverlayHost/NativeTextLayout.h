#pragma once

#include "NativeStyle.h"

#include <dwrite.h>
#include <wrl/client.h>

#include <string_view>
#include <list>
#include <map>
#include <tuple>

namespace widgetrail {

enum class NativeTextVerticalAlignment {
    Start,
    Center,
};

/// One immutable DirectWrite layout used by both intrinsic measurement and
/// paint. Its measured height includes vertical font ink that DirectWrite
/// reports outside the authored line box, while the layout origin keeps that
/// ink inside the measured component box.
struct NativeTextLayoutPlan final {
    Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
    float measuredWidth{};
    float measuredHeight{};
    float layoutWidth{};
    float layoutHeight{};
    float inkInsetTop{};
    float inkInsetBottom{};
    float baseline{};

    [[nodiscard]] bool IsValid() const noexcept { return layout != nullptr; }
    [[nodiscard]] float LayoutOriginY(
        float availableY,
        float availableHeight,
        NativeTextVerticalAlignment alignment) const noexcept;
};

/// Creates the single bounded DirectWrite plan consumed by measurement and
/// paint. Text transformation, font selection, line spacing, character
/// spacing, wrapping, trimming, and ink-overhang accounting cannot diverge
/// between those two phases.
[[nodiscard]] NativeTextLayoutPlan CreateNativeTextLayoutPlan(
    IDWriteFactory* factory,
    std::wstring_view text,
    const NativeRenderStyle& style,
    float maximumWidth,
    float maximumHeight);

// Renderer-owned cache; returned layouts must remain immutable. DirectWrite
// plans contain typography in DIPs and are independent of D2D device resources.
class NativeTextLayoutCache final {
public:
    NativeTextLayoutCache() = default;
    NativeTextLayoutCache(const NativeTextLayoutCache&) = delete;
    NativeTextLayoutCache& operator=(const NativeTextLayoutCache&) = delete;
    [[nodiscard]] NativeTextLayoutPlan Get(
        IDWriteFactory* factory, std::wstring_view text,
        const NativeRenderStyle& style, float maximumWidth, float maximumHeight);
    void Clear();
    [[nodiscard]] std::size_t size() const noexcept { return entries_.size(); }
    std::uint64_t hits{};
    std::uint64_t misses{};
private:
    using Key = std::tuple<std::wstring, std::wstring, int, float, float, float,
        int, NativeTextOverflow, NativeOverflowWrap, NativeTextTransform,
        NativeTextAlign, float, float>;
    struct Entry { NativeTextLayoutPlan plan; std::list<Key>::iterator use; };
    Microsoft::WRL::ComPtr<IDWriteFactory> factory_;
    std::list<Key> uses_;
    std::map<Key, Entry> entries_;
    std::size_t characters_{};
};

} // namespace widgetrail
