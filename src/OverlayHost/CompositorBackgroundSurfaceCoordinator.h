#pragma once

#include "DeclarativeRenderer.h"
#include "OverlayCompositionSurface.h"

#include <cstdint>
#include <optional>
#include <string>

namespace widgetrail {

class CompositorBackgroundSurfaceCoordinator final {
public:
    enum class AdvanceDisposition {
        Advanced,
        HandledPending,
        NotApplicableOrStale,
        Failed,
    };
    [[nodiscard]] bool Observe(
        const ComputedCompositorBackground& background,
        DeclarativeRenderer& renderer,
        OverlayCompositionSurface& surface,
        unsigned int width,
        unsigned int height,
        float pixelsPerDip,
        std::uint64_t nowMilliseconds,
        std::wstring& diagnostic);
    [[nodiscard]] AdvanceDisposition Advance(
        const std::optional<ComputedCompositorBackground>& current,
        DeclarativeRenderer& renderer,
        OverlayCompositionSurface& surface,
        unsigned int width,
        unsigned int height,
        float pixelsPerDip,
        std::uint64_t nowMilliseconds,
        std::wstring& diagnostic);
    void Retire(OverlayCompositionSurface& surface) noexcept;
    [[nodiscard]] std::optional<std::uint64_t> deadline() const noexcept;

private:
    enum class StageDisposition { Committed, Pending, Failed };
    struct Image final {
        ComputedCompositorBackground descriptor;
        Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
    };
    struct Proposal final {
        Image image;
        std::uint64_t observedAt{};
        std::uint64_t generation{};
        bool staged{};
    };
    struct State final {
        std::optional<Image> committed;
        std::optional<Image> incoming;
        std::optional<Proposal> proposal;
        std::uint64_t transitionStartedAt{};
        std::uint64_t generation{};
    } state_;

    [[nodiscard]] static bool SameDestination(
        const ComputedCompositorBackground& left,
        const ComputedCompositorBackground& right) noexcept;
    [[nodiscard]] static std::wstring Key(
        const ComputedCompositorBackground& background);
    [[nodiscard]] bool RebaseOutgoing(
        const State& state,
        DeclarativeRenderer& renderer,
        ID2D1RenderTarget* target,
        unsigned int width,
        unsigned int height,
        std::uint64_t nowMilliseconds,
        Image& result,
        std::wstring& diagnostic) const;
    [[nodiscard]] StageDisposition Stage(
        State& next,
        DeclarativeRenderer& renderer,
        OverlayCompositionSurface& surface,
        unsigned int width,
        unsigned int height,
        float pixelsPerDip,
        std::uint64_t nowMilliseconds,
        bool activate,
        std::wstring& diagnostic);
};

} // namespace widgetrail
