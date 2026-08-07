#pragma once

#include "DeclarativeLayout.h"
#include "NativeStyle.h"
#include "WidgetBridgeClient.h"

#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace gba {

class RemoteImageCache;

enum class RenderDiagnosticSeverity {
    Warning,
    Error,
};

struct RenderDiagnostic final {
    RenderDiagnosticSeverity severity{RenderDiagnosticSeverity::Warning};
    std::wstring nodeId;
    std::wstring code;
    std::wstring message;
};

struct RenderHitRegion final {
    std::wstring nodeId;
    declarative::Rect rect;
    bool enabled{};
};

struct RenderResult final {
    bool succeeded{};
    std::vector<RenderDiagnostic> diagnostics;
    std::vector<RenderHitRegion> hitRegions;
    std::map<std::wstring, declarative::Rect, std::less<>> focusRects;
    std::map<std::wstring, std::wstring, std::less<>> focusScopes;
    std::optional<declarative::Rect> currentFocusRect;
};

struct ImagePlacement final {
    declarative::Rect destination;
    declarative::Rect source;
};

struct DeclarativeRenderOptions final {
    float pixelScale{1.0F};
    float rootFontSizePx{16.0F};
    NativeAccessibilityPolicy accessibility;
};

class DeclarativeRenderer final {
public:
    DeclarativeRenderer(
        ID2D1Factory* d2dFactory,
        IDWriteFactory* writeFactory,
        RemoteImageCache* imageCache) noexcept;

    DeclarativeRenderer(const DeclarativeRenderer&) = delete;
    DeclarativeRenderer& operator=(const DeclarativeRenderer&) = delete;

    [[nodiscard]] RenderResult Render(
        ID2D1RenderTarget* renderTarget,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options = {});

    void DiscardTargetResources() noexcept;

    // Pure deterministic seam used by the renderer and native tests.
    [[nodiscard]] static ImagePlacement ComputeImagePlacement(
        declarative::Size imageSize,
        declarative::Rect destination,
        NativeImageFit fit,
        NativeObjectPosition position) noexcept;

private:
    struct PreparedNode;
    struct RenderPass;

    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap> GetImageBitmap(
        ID2D1RenderTarget* renderTarget,
        const WidgetNode& node,
        RenderPass& pass);

    ID2D1Factory* d2dFactory_{};
    IDWriteFactory* writeFactory_{};
    RemoteImageCache* imageCache_{};
    ID2D1RenderTarget* bitmapTarget_{};
    std::unordered_map<std::wstring, Microsoft::WRL::ComPtr<ID2D1Bitmap>> bitmaps_;
};

} // namespace gba
