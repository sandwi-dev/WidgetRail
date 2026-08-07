#include "NativeIcons.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <utility>

#include <d2d1helper.h>

namespace gba::icons {
namespace {

constexpr float kMaximumCoordinate = 1'000'000.0F;
constexpr float kMaximumExtent = 4096.0F;
constexpr float kContentInset = 0.12F;

struct Canvas {
    float left{};
    float top{};
    float size{};

    [[nodiscard]] D2D1_POINT_2F Point(const float x, const float y) const noexcept {
        return D2D1::Point2F(left + x * size, top + y * size);
    }

    [[nodiscard]] D2D1_RECT_F Rect(
        const float leftValue,
        const float topValue,
        const float rightValue,
        const float bottomValue) const noexcept {
        return D2D1::RectF(
            left + leftValue * size,
            top + topValue * size,
            left + rightValue * size,
            top + bottomValue * size);
    }
};

enum class PathPaint {
    Fill,
    Stroke,
};

[[nodiscard]] bool Finite(const float value) noexcept {
    return std::isfinite(value) && std::abs(value) <= kMaximumCoordinate;
}

[[nodiscard]] bool TryCanvas(const D2D1_RECT_F bounds, Canvas& canvas) noexcept {
    if (!Finite(bounds.left) || !Finite(bounds.top) ||
        !Finite(bounds.right) || !Finite(bounds.bottom)) {
        return false;
    }
    const float width = bounds.right - bounds.left;
    const float height = bounds.bottom - bounds.top;
    if (!std::isfinite(width) || !std::isfinite(height) ||
        width <= 0.0F || height <= 0.0F ||
        width > kMaximumExtent || height > kMaximumExtent) {
        return false;
    }
    const float outerSize = std::min(width, height);
    const float inset = outerSize * kContentInset;
    canvas.left = bounds.left + (width - outerSize) * 0.5F + inset;
    canvas.top = bounds.top + (height - outerSize) * 0.5F + inset;
    canvas.size = outerSize - inset * 2.0F;
    return canvas.size >= 0.5F && Finite(canvas.left) && Finite(canvas.top);
}

[[nodiscard]] float SafeStroke(const float requested, const float iconSize) noexcept {
    if (!std::isfinite(requested) || requested <= 0.0F) return 0.0F;
    const float minimum = std::max(0.35F, iconSize / 64.0F);
    const float maximum = std::max(minimum, iconSize * 0.14F);
    return std::clamp(requested, minimum, maximum);
}

void FillCircle(
    ID2D1RenderTarget* target,
    ID2D1Brush* brush,
    const D2D1_POINT_2F center,
    const float radius) noexcept {
    target->FillEllipse(D2D1::Ellipse(center, radius, radius), brush);
}

void DrawRoundLine(
    ID2D1RenderTarget* target,
    ID2D1Brush* brush,
    const D2D1_POINT_2F start,
    const D2D1_POINT_2F end,
    const float stroke) noexcept {
    target->DrawLine(start, end, brush, stroke);
    const float radius = stroke * 0.5F;
    FillCircle(target, brush, start, radius);
    FillCircle(target, brush, end, radius);
}

template <typename Build>
[[nodiscard]] bool PaintPath(
    ID2D1RenderTarget* target,
    ID2D1Brush* brush,
    const float stroke,
    const PathPaint paint,
    Build&& build) noexcept {
    ID2D1Factory* factory = nullptr;
    target->GetFactory(&factory);
    if (factory == nullptr) return false;

    ID2D1PathGeometry* geometry = nullptr;
    HRESULT result = factory->CreatePathGeometry(&geometry);
    factory->Release();
    if (FAILED(result) || geometry == nullptr) return false;

    ID2D1GeometrySink* sink = nullptr;
    result = geometry->Open(&sink);
    if (SUCCEEDED(result) && sink != nullptr) {
        sink->SetFillMode(D2D1_FILL_MODE_WINDING);
        build(sink);
        result = sink->Close();
        sink->Release();
    }

    if (SUCCEEDED(result)) {
        if (paint == PathPaint::Fill) target->FillGeometry(geometry, brush);
        else target->DrawGeometry(geometry, brush, stroke);
    }
    geometry->Release();
    return SUCCEEDED(result);
}

template <std::size_t Count>
[[nodiscard]] bool FillPolygon(
    ID2D1RenderTarget* target,
    ID2D1Brush* brush,
    const std::array<D2D1_POINT_2F, Count>& points) noexcept {
    static_assert(Count >= 3);
    return PaintPath(target, brush, 1.0F, PathPaint::Fill,
        [&points](ID2D1GeometrySink* sink) {
            sink->BeginFigure(points[0], D2D1_FIGURE_BEGIN_FILLED);
            sink->AddLines(points.data() + 1, static_cast<UINT32>(Count - 1));
            sink->EndFigure(D2D1_FIGURE_END_CLOSED);
        });
}

[[nodiscard]] bool DrawMusic(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    DrawRoundLine(target, brush, c.Point(0.40F, 0.20F), c.Point(0.40F, 0.70F), stroke);
    DrawRoundLine(target, brush, c.Point(0.40F, 0.20F), c.Point(0.76F, 0.13F), stroke);
    DrawRoundLine(target, brush, c.Point(0.76F, 0.13F), c.Point(0.76F, 0.61F), stroke);
    DrawRoundLine(target, brush, c.Point(0.40F, 0.34F), c.Point(0.76F, 0.27F), stroke);
    target->FillEllipse(D2D1::Ellipse(c.Point(0.30F, 0.72F), 0.14F * c.size, 0.10F * c.size), brush);
    target->FillEllipse(D2D1::Ellipse(c.Point(0.66F, 0.63F), 0.14F * c.size, 0.10F * c.size), brush);
    return true;
}

[[nodiscard]] bool DrawPlay(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c) noexcept {
    return FillPolygon(target, brush, std::array{
        c.Point(0.32F, 0.16F), c.Point(0.84F, 0.50F), c.Point(0.32F, 0.84F)});
}

[[nodiscard]] bool DrawPause(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c) noexcept {
    target->FillRoundedRectangle(D2D1::RoundedRect(c.Rect(0.25F, 0.16F, 0.43F, 0.84F),
        c.size * 0.035F, c.size * 0.035F), brush);
    target->FillRoundedRectangle(D2D1::RoundedRect(c.Rect(0.57F, 0.16F, 0.75F, 0.84F),
        c.size * 0.035F, c.size * 0.035F), brush);
    return true;
}

[[nodiscard]] bool DrawSkip(
    ID2D1RenderTarget* target,
    ID2D1Brush* brush,
    const Canvas& c,
    const bool next) noexcept {
    const auto x = [next](const float value) { return next ? 1.0F - value : value; };
    const std::array triangle{
        c.Point(x(0.30F), 0.16F), c.Point(x(0.76F), 0.50F), c.Point(x(0.30F), 0.84F)};
    const bool result = FillPolygon(target, brush, triangle);
    const auto bar = c.Rect(x(0.18F), 0.16F, x(0.28F), 0.84F);
    target->FillRectangle(D2D1::RectF(
        std::min(bar.left, bar.right), bar.top,
        std::max(bar.left, bar.right), bar.bottom), brush);
    return result;
}

[[nodiscard]] bool DrawRefresh(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    const bool result = PaintPath(target, brush, stroke, PathPaint::Stroke,
        [&c](ID2D1GeometrySink* sink) {
            sink->BeginFigure(c.Point(0.78F, 0.42F), D2D1_FIGURE_BEGIN_HOLLOW);
            sink->AddBezier(D2D1::BezierSegment(
                c.Point(0.70F, 0.18F), c.Point(0.35F, 0.13F), c.Point(0.22F, 0.40F)));
            sink->AddBezier(D2D1::BezierSegment(
                c.Point(0.08F, 0.69F), c.Point(0.36F, 0.91F), c.Point(0.62F, 0.80F)));
            sink->EndFigure(D2D1_FIGURE_END_OPEN);
        });
    const bool arrow = FillPolygon(target, brush, std::array{
        c.Point(0.62F, 0.68F), c.Point(0.84F, 0.75F), c.Point(0.67F, 0.91F)});
    return result && arrow;
}

[[nodiscard]] bool DrawRepeat(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    DrawRoundLine(target, brush, c.Point(0.19F, 0.34F), c.Point(0.75F, 0.34F), stroke);
    DrawRoundLine(target, brush, c.Point(0.81F, 0.66F), c.Point(0.25F, 0.66F), stroke);
    const bool upper = FillPolygon(target, brush, std::array{
        c.Point(0.69F, 0.20F), c.Point(0.87F, 0.34F), c.Point(0.69F, 0.48F)});
    const bool lower = FillPolygon(target, brush, std::array{
        c.Point(0.31F, 0.52F), c.Point(0.13F, 0.66F), c.Point(0.31F, 0.80F)});
    return upper && lower;
}

[[nodiscard]] bool DrawShuffle(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    bool result = PaintPath(target, brush, stroke, PathPaint::Stroke,
        [&c](ID2D1GeometrySink* sink) {
            sink->BeginFigure(c.Point(0.16F, 0.28F), D2D1_FIGURE_BEGIN_HOLLOW);
            sink->AddBezier(D2D1::BezierSegment(
                c.Point(0.44F, 0.28F), c.Point(0.51F, 0.72F), c.Point(0.76F, 0.72F)));
            sink->EndFigure(D2D1_FIGURE_END_OPEN);
        });
    result = PaintPath(target, brush, stroke, PathPaint::Stroke,
        [&c](ID2D1GeometrySink* sink) {
            sink->BeginFigure(c.Point(0.16F, 0.72F), D2D1_FIGURE_BEGIN_HOLLOW);
            sink->AddBezier(D2D1::BezierSegment(
                c.Point(0.44F, 0.72F), c.Point(0.51F, 0.28F), c.Point(0.76F, 0.28F)));
            sink->EndFigure(D2D1_FIGURE_END_OPEN);
        }) && result;
    const bool top = FillPolygon(target, brush, std::array{
        c.Point(0.70F, 0.14F), c.Point(0.88F, 0.28F), c.Point(0.70F, 0.42F)});
    const bool bottom = FillPolygon(target, brush, std::array{
        c.Point(0.70F, 0.58F), c.Point(0.88F, 0.72F), c.Point(0.70F, 0.86F)});
    return result && top && bottom;
}

[[nodiscard]] bool DrawLike(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const bool dislike) noexcept {
    const auto y = [dislike](const float value) { return dislike ? 1.0F - value : value; };
    return FillPolygon(target, brush, std::array{
        c.Point(0.16F, y(0.43F)), c.Point(0.31F, y(0.43F)),
        c.Point(0.43F, y(0.18F)), c.Point(0.51F, y(0.16F)),
        c.Point(0.57F, y(0.22F)), c.Point(0.56F, y(0.38F)),
        c.Point(0.78F, y(0.38F)), c.Point(0.85F, y(0.45F)),
        c.Point(0.77F, y(0.78F)), c.Point(0.70F, y(0.84F)),
        c.Point(0.31F, y(0.84F)), c.Point(0.31F, y(0.78F)),
        c.Point(0.16F, y(0.78F))});
}

[[nodiscard]] bool DrawSettings(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    constexpr float pi = 3.14159265358979323846F;
    const auto center = c.Point(0.5F, 0.5F);
    target->DrawEllipse(D2D1::Ellipse(center, c.size * 0.30F, c.size * 0.30F), brush, stroke);
    target->DrawEllipse(D2D1::Ellipse(center, c.size * 0.12F, c.size * 0.12F), brush, stroke);
    for (int index = 0; index < 8; ++index) {
        const float angle = static_cast<float>(index) * pi / 4.0F;
        const auto inner = c.Point(0.5F + std::cos(angle) * 0.29F, 0.5F + std::sin(angle) * 0.29F);
        const auto outer = c.Point(0.5F + std::cos(angle) * 0.42F, 0.5F + std::sin(angle) * 0.42F);
        DrawRoundLine(target, brush, inner, outer, stroke * 1.35F);
    }
    return true;
}

[[nodiscard]] bool DrawWarning(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    const bool result = PaintPath(target, brush, stroke, PathPaint::Stroke,
        [&c](ID2D1GeometrySink* sink) {
            sink->BeginFigure(c.Point(0.50F, 0.12F), D2D1_FIGURE_BEGIN_HOLLOW);
            sink->AddLine(c.Point(0.91F, 0.84F));
            sink->AddLine(c.Point(0.09F, 0.84F));
            sink->EndFigure(D2D1_FIGURE_END_CLOSED);
        });
    DrawRoundLine(target, brush, c.Point(0.50F, 0.36F), c.Point(0.50F, 0.60F), stroke);
    FillCircle(target, brush, c.Point(0.50F, 0.73F), stroke * 0.62F);
    return result;
}

[[nodiscard]] bool DrawCheck(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    DrawRoundLine(target, brush, c.Point(0.15F, 0.53F), c.Point(0.40F, 0.76F), stroke);
    DrawRoundLine(target, brush, c.Point(0.40F, 0.76F), c.Point(0.86F, 0.24F), stroke);
    return true;
}

[[nodiscard]] bool DrawConnection(
    ID2D1RenderTarget* target, ID2D1Brush* brush, const Canvas& c, const float stroke) noexcept {
    auto arc = [target, brush, stroke, &c](const float left, const float top, const float right, const float endY) {
        return PaintPath(target, brush, stroke, PathPaint::Stroke,
            [&c, left, top, right, endY](ID2D1GeometrySink* sink) {
                sink->BeginFigure(c.Point(left, endY), D2D1_FIGURE_BEGIN_HOLLOW);
                sink->AddBezier(D2D1::BezierSegment(
                    c.Point(left + 0.12F, top), c.Point(right - 0.12F, top), c.Point(right, endY)));
                sink->EndFigure(D2D1_FIGURE_END_OPEN);
            });
    };
    const bool outer = arc(0.08F, 0.13F, 0.92F, 0.47F);
    const bool inner = arc(0.25F, 0.37F, 0.75F, 0.58F);
    FillCircle(target, brush, c.Point(0.50F, 0.78F), stroke * 0.92F);
    return outer && inner;
}

} // namespace

bool TryParseNativeIcon(const std::wstring_view semanticId, NativeIcon& icon) noexcept {
    using Pair = std::pair<std::wstring_view, NativeIcon>;
    static constexpr std::array mappings{
        Pair{L"music", NativeIcon::Music}, Pair{L"play", NativeIcon::Play},
        Pair{L"pause", NativeIcon::Pause}, Pair{L"previous", NativeIcon::Previous},
        Pair{L"next", NativeIcon::Next}, Pair{L"refresh", NativeIcon::Refresh},
        Pair{L"shuffle", NativeIcon::Shuffle}, Pair{L"like", NativeIcon::Like},
        Pair{L"dislike", NativeIcon::Dislike}, Pair{L"repeat", NativeIcon::Repeat},
        Pair{L"settings", NativeIcon::Settings}, Pair{L"warning", NativeIcon::Warning},
        Pair{L"check", NativeIcon::Check}, Pair{L"connection", NativeIcon::Connection},
        Pair{L"toggle-playback", NativeIcon::Play}, Pair{L"play-pause", NativeIcon::Play},
        Pair{L"previous-track", NativeIcon::Previous}, Pair{L"next-track", NativeIcon::Next},
        Pair{L"retry", NativeIcon::Refresh}, Pair{L"repeat-mode", NativeIcon::Repeat},
        Pair{L"connect", NativeIcon::Connection}, Pair{L"pair", NativeIcon::Connection},
    };
    const auto match = std::find_if(mappings.begin(), mappings.end(),
        [semanticId](const Pair& candidate) { return candidate.first == semanticId; });
    if (match == mappings.end()) return false;
    icon = match->second;
    return true;
}

bool DrawNativeIcon(
    ID2D1RenderTarget* renderTarget,
    const NativeIcon icon,
    const D2D1_RECT_F bounds,
    ID2D1Brush* brush,
    const float strokeWidth) noexcept {
    if (renderTarget == nullptr || brush == nullptr) return false;
    Canvas canvas;
    if (!TryCanvas(bounds, canvas)) return false;
    const float stroke = SafeStroke(strokeWidth, canvas.size);
    if (stroke <= 0.0F) return false;

    switch (icon) {
        case NativeIcon::Music: return DrawMusic(renderTarget, brush, canvas, stroke);
        case NativeIcon::Play: return DrawPlay(renderTarget, brush, canvas);
        case NativeIcon::Pause: return DrawPause(renderTarget, brush, canvas);
        case NativeIcon::Previous: return DrawSkip(renderTarget, brush, canvas, false);
        case NativeIcon::Next: return DrawSkip(renderTarget, brush, canvas, true);
        case NativeIcon::Refresh: return DrawRefresh(renderTarget, brush, canvas, stroke);
        case NativeIcon::Shuffle: return DrawShuffle(renderTarget, brush, canvas, stroke);
        case NativeIcon::Like: return DrawLike(renderTarget, brush, canvas, false);
        case NativeIcon::Dislike: return DrawLike(renderTarget, brush, canvas, true);
        case NativeIcon::Repeat: return DrawRepeat(renderTarget, brush, canvas, stroke);
        case NativeIcon::Settings: return DrawSettings(renderTarget, brush, canvas, stroke);
        case NativeIcon::Warning: return DrawWarning(renderTarget, brush, canvas, stroke);
        case NativeIcon::Check: return DrawCheck(renderTarget, brush, canvas, stroke);
        case NativeIcon::Connection: return DrawConnection(renderTarget, brush, canvas, stroke);
        default: return false;
    }
}

bool DrawNativeIcon(
    ID2D1RenderTarget* renderTarget,
    const NativeIcon icon,
    const D2D1_POINT_2F center,
    const float size,
    ID2D1Brush* brush,
    const float strokeWidth) noexcept {
    if (!Finite(center.x) || !Finite(center.y) ||
        !std::isfinite(size) || size <= 0.0F || size > kMaximumExtent) {
        return false;
    }
    const float half = size * 0.5F;
    return DrawNativeIcon(renderTarget, icon,
        D2D1::RectF(center.x - half, center.y - half, center.x + half, center.y + half),
        brush, strokeWidth);
}

} // namespace gba::icons
