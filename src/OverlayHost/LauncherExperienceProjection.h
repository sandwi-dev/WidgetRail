#pragma once

#include "LauncherExperienceAdapter.h"

#include <d2d1.h>
#include <wrl/client.h>

#include <optional>
#include <string>
#include <string_view>

namespace gba::launcher {

enum class ProductionProjectionDisposition {
    Ordinary,
    Adopted,
    Fallback,
};

struct ProductionProjectionResult final {
    RenderResult render;
    ProductionProjectionDisposition disposition{
        ProductionProjectionDisposition::Ordinary};
    std::optional<Preset> preset;
};

[[nodiscard]] std::wstring_view PresetName(Preset preset) noexcept;

/// Owns the private first-party projection boundary between one admitted Game
/// Launcher snapshot and the existing native Launcher Experience adapter. It
/// never creates actions or domain state. Invalid or failed projection is
/// painted through the ordinary declarative path without exposing a partial
/// native frame.
class LauncherExperienceProjection final {
public:
    [[nodiscard]] ProductionProjectionResult Render(
        DeclarativeRenderer& renderer,
        ID2D1RenderTarget* target,
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options);

    /// Returns the canonical snapshot only while it is the projection of this
    /// exact immutable source generation. All other callers retain the source.
    [[nodiscard]] const WidgetSnapshot& InteractionSnapshot(
        std::wstring_view widgetId,
        const WidgetSnapshot& source) const noexcept;

    void DiscardTargetResources() noexcept;

#ifdef GBA_DECLARATIVE_RENDERER_TESTING
    void FailNextAdapterFrameForTesting() noexcept {
        failNextAdapterFrameForTesting_ = true;
    }
#endif

private:
    struct Projection final {
        Preset preset{Preset::HeroRail};
        std::vector<SlotContent> contents;
    };

    [[nodiscard]] static std::optional<Projection> Recognize(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        bool& markerPresent);
    [[nodiscard]] bool EnsureStagingTarget(
        ID2D1RenderTarget* target,
        declarative::Rect viewport);
    void RetainCanonical(
        std::wstring_view widgetId,
        WidgetSnapshot snapshot);
    void ClearCanonical() noexcept;

    ID2D1RenderTarget* parentTarget_{};
    declarative::Size stagingSize_{};
    Microsoft::WRL::ComPtr<ID2D1BitmapRenderTarget> stagingTarget_;
    std::wstring canonicalWidgetId_;
    std::optional<WidgetSnapshot> canonicalSnapshot_;
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
    bool failNextAdapterFrameForTesting_{};
#endif
};

} // namespace gba::launcher
