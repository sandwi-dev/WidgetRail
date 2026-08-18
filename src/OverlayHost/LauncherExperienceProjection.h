#pragma once

#include "LauncherExperienceAdapter.h"
#include "RemoteImageCache.h"

#include <d2d1.h>
#include <wrl/client.h>

#include <optional>
#include <string>
#include <string_view>

namespace widgetrail::launcher {

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
    bool presentationActive{};
    EffectQuality effectQuality{EffectQuality::Full};
    bool backgroundIsFallback{true};
    bool backgroundTransitionActive{};
    float previousBackgroundOpacity{};
    float currentBackgroundOpacity{1.0F};
    std::wstring backgroundFocusId;
    std::wstring selectionIdentity;
    bool selectionUsesGlobalAppearance{};
    bool safeStart{};
    std::wstring layoutBranch;
    std::wstring railOrientation;
    std::wstring detailsSurface;
    std::wstring artworkAvailability;
    declarative::Rect bodyBounds;
    declarative::Rect railBounds;
    float textScale{1.0F};
    bool reducedMotion{};
    bool reducedTransparency{};
    bool highContrast{};
    LauncherPresentationMetrics presentationMetrics;
};

[[nodiscard]] std::wstring_view PresetName(Preset preset) noexcept;
[[nodiscard]] std::wstring_view EffectQualityName(EffectQuality quality) noexcept;

/// Owns the public-declaration projection boundary between one admitted
/// Community snapshot and the existing native Launcher Experience adapter. It
/// never creates actions or domain state. Invalid or failed projection is
/// painted through the ordinary declarative path without exposing a partial
/// native frame.
class LauncherExperienceProjection final {
public:
    /// Atomically validates and publishes one complete private catalog value.
    /// A rejected candidate leaves the last rendered selection untouched.
    [[nodiscard]] bool PublishSelection(
        LauncherExperienceSelection selection,
        std::wstring& diagnostic);
    [[nodiscard]] long long selectionRevision() const noexcept {
        return selection_ ? selection_->revision : 0;
    }
    /// Sets the recovery route for the next admitted presentation activation. Ordinary
    /// activation passes false and therefore retires any prior one-shot bypass.
    void BeginActivation(bool safeStart) noexcept;
    [[nodiscard]] ProductionProjectionResult Render(
        DeclarativeRenderer& renderer,
        ID2D1RenderTarget* target,
        RemoteImageCache* imageCache,
        const std::optional<WidgetAdvancedPresentationDeclaration>& declaration,
        std::wstring_view presentationGeneration,
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options);

    /// Records the existing host input owner's exact focus-change edge. The
    /// projection consumes it only for the current admitted presentation and
    /// closes it when the corresponding immutable frame is committed.
    void ObserveFocusInput(
        std::wstring_view widgetId,
        std::uint64_t nowMilliseconds) noexcept;

    /// Returns the canonical snapshot only while it is the projection of this
    /// exact immutable source generation. All other callers retain the source.
    [[nodiscard]] const WidgetSnapshot& InteractionSnapshot(
        std::wstring_view widgetId,
        std::wstring_view presentationGeneration,
        const WidgetSnapshot& source) const noexcept;

    /// Returns an authored focus edge only from the last complete canonical
    /// frame committed by the Launcher Experience projection. This lets
    /// an internal game-to-game move complete before the ordinary collection
    /// prefetch action observes the newly focused edge.
    [[nodiscard]] std::optional<std::wstring> ProjectedFocusTarget(
        std::wstring_view widgetId,
        std::wstring_view focusedElementId,
        std::wstring_view direction) const;

    void DiscardTargetResources() noexcept;

#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    void FailNextAdapterFrameForTesting() noexcept {
        failNextAdapterFrameForTesting_ = true;
    }
    void RecordPresentationTimingForTesting(
        double inputDispatchMilliseconds,
        double renderCommitMilliseconds) noexcept {
        presentationOwner_.RecordFrameTiming(
            inputDispatchMilliseconds, renderCommitMilliseconds);
    }
    void RecordInputToFocusForTesting(double milliseconds) noexcept {
        presentationOwner_.RecordInputToFocus(milliseconds);
    }
#endif

private:
    struct Projection final {
        Preset preset{Preset::HeroRail};
        std::vector<SlotContent> contents;
    };

    enum class ArtworkAvailability {
        Pending,
        Available,
        Mixed,
        AllTerminal,
    };

    [[nodiscard]] static std::optional<Projection> Recognize(
        const std::optional<WidgetAdvancedPresentationDeclaration>& declaration,
        const WidgetSnapshot& snapshot,
        bool& presentationRequested);
    [[nodiscard]] bool EnsureStagingTarget(
        ID2D1RenderTarget* target,
        declarative::Rect viewport);
    [[nodiscard]] LauncherPresentationFrame PreparePresentation(
        RemoteImageCache* imageCache,
        std::wstring_view widgetId,
        const Projection& projection,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        const NativeAccessibilityPolicy& accessibility,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] static ArtworkAvailability InspectArtworkAvailability(
        RemoteImageCache* imageCache,
        std::wstring_view widgetId,
        const Projection& projection);
    void RetirePresentation() noexcept;
    void RetainCanonical(
        std::wstring_view widgetId,
        std::wstring_view presentationGeneration,
        WidgetSnapshot snapshot);
    void ClearCanonical() noexcept;

    ID2D1RenderTarget* parentTarget_{};
    declarative::Size stagingSize_{};
    Microsoft::WRL::ComPtr<ID2D1BitmapRenderTarget> stagingTarget_;
    std::wstring canonicalWidgetId_;
    std::wstring canonicalPresentationGeneration_;
    std::optional<WidgetSnapshot> canonicalSnapshot_;
    LauncherExperiencePresentationOwner presentationOwner_;
    std::optional<LauncherExperienceSelection> selection_;
    std::shared_ptr<const DecodedLauncherAsset> packBackground_;
    bool safeStartActivation_{};
    std::wstring activePresentationKey_;
    std::optional<std::uint64_t> pendingFocusInputMilliseconds_;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    bool failNextAdapterFrameForTesting_{};
#endif
};

} // namespace widgetrail::launcher
