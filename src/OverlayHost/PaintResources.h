#pragma once

#include <d2d1.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <map>
#include <utility>

namespace widgetrail::paint {

// Owned by one renderer resource domain. The owner binds its primary target and
// clears this cache on domain retirement; compatible capture targets may share
// it. Returned brushes are immutable: never change color, opacity or transform.
class Resources final {
public:
    static constexpr std::size_t MaximumEntriesPerKind = 64;
    struct Stats {
        std::size_t solids{}, gradients{}, stopCollections{};
        std::uint64_t solidCreates{}, gradientCreates{}, stopCreates{}, hits{}, evictions{};
        std::size_t geometries{};
        std::uint64_t geometryCreates{};
    };
    void Clear() noexcept { solids_.clear(); gradients_.clear(); stops_.clear(); geometries_.clear(); }
    void SetReuseEnabled(bool enabled) noexcept { enabled_ = enabled; }
    [[nodiscard]] Stats stats() const noexcept {
        return {solids_.size(), gradients_.size(), stops_.size(), solidCreates_, gradientCreates_, stopCreates_, hits_, evictions_,
            geometries_.size(), geometryCreates_};
    }
    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1RoundedRectangleGeometry> RoundedRectangle(
        ID2D1Factory* factory, float width, float height, float radius) {
        if (!factory) return {};
        const std::array<std::uint32_t, 3> key{Bits(width), Bits(height), Bits(radius)};
        return Get(geometries_, key, [&] {
            Microsoft::WRL::ComPtr<ID2D1RoundedRectangleGeometry> geometry;
            if (SUCCEEDED(factory->CreateRoundedRectangleGeometry(
                    {D2D1::RectF(0, 0, width, height), radius, radius}, geometry.GetAddressOf()))) ++geometryCreates_;
            return geometry;
        });
    }
    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> Solid(ID2D1RenderTarget* target, D2D1_COLOR_F color) {
        if (!target) return {};
        return Get(solids_, ColorKey(color), [&] {
            Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> brush;
            if (SUCCEEDED(target->CreateSolidColorBrush(color, brush.GetAddressOf()))) ++solidCreates_;
            return brush;
        });
    }
    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1LinearGradientBrush> Gradient(
        ID2D1RenderTarget* target, D2D1_POINT_2F start, D2D1_POINT_2F end, D2D1_COLOR_F top, D2D1_COLOR_F bottom) {
        if (!target) return {};
        const auto a = ColorKey(top), b = ColorKey(bottom);
        const std::array<std::uint32_t, 8> colors{a[0], a[1], a[2], a[3], b[0], b[1], b[2], b[3]};
        const std::array<std::uint32_t, 12> key{colors[0], colors[1], colors[2], colors[3], colors[4], colors[5], colors[6], colors[7],
            Bits(start.x), Bits(start.y), Bits(end.x), Bits(end.y)};
        return Get(gradients_, key, [&] {
            auto stops = Get(stops_, colors, [&] {
                const D2D1_GRADIENT_STOP entries[]{{0, top}, {1, bottom}};
                Microsoft::WRL::ComPtr<ID2D1GradientStopCollection> value;
                if (SUCCEEDED(target->CreateGradientStopCollection(entries, 2, value.GetAddressOf()))) ++stopCreates_;
                return value;
            });
            Microsoft::WRL::ComPtr<ID2D1LinearGradientBrush> brush;
            if (stops && SUCCEEDED(target->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(start, end),
                    stops.Get(), brush.GetAddressOf()))) ++gradientCreates_;
            return brush;
        });
    }

private:
    template<typename T> struct Entry { Microsoft::WRL::ComPtr<T> value; std::uint64_t used{}; };
    template<std::size_t N, typename T> using Cache = std::map<std::array<std::uint32_t, N>, Entry<T>>;
    Cache<4, ID2D1SolidColorBrush> solids_;
    Cache<8, ID2D1GradientStopCollection> stops_;
    Cache<12, ID2D1LinearGradientBrush> gradients_;
    Cache<3, ID2D1RoundedRectangleGeometry> geometries_;
    bool enabled_{true};
    std::uint64_t clock_{}, solidCreates_{}, gradientCreates_{}, stopCreates_{}, hits_{}, evictions_{};
    std::uint64_t geometryCreates_{};
    static std::uint32_t Bits(float value) noexcept { return std::bit_cast<std::uint32_t>(value); }
    static std::array<std::uint32_t, 4> ColorKey(D2D1_COLOR_F color) noexcept {
        return {Bits(color.r), Bits(color.g), Bits(color.b), Bits(color.a)};
    }
    template<std::size_t N, typename T, typename Factory>
    Microsoft::WRL::ComPtr<T> Get(Cache<N, T>& cache, const std::array<std::uint32_t, N>& key, Factory&& create) {
        if (!enabled_) return create();
        if (const auto found = cache.find(key); found != cache.end()) {
            found->second.used = ++clock_; ++hits_; return found->second.value;
        }
        auto value = create();
        if (!value) return {};
        if (cache.size() >= MaximumEntriesPerKind) {
            cache.erase(std::min_element(cache.begin(), cache.end(), [](const auto& a, const auto& b) { return a.second.used < b.second.used; }));
            ++evictions_;
        }
        cache.emplace(key, Entry<T>{value, ++clock_});
        return value;
    }
};
} // namespace widgetrail::paint
