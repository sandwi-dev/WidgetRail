#pragma once

#include "DeclarativeRenderer.h"
#include "OverlayCompositionSurface.h"

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail {

class CompositorBackgroundSurfaceCoordinator final {
public:
    enum class AdvanceDisposition {
        Advanced,
        HandledPending,
        NotApplicableOrStale,
        Failed,
    };
    struct Observation final {
        std::vector<OverlayCompositionSurface::Frame> frames;
        std::optional<OverlayCompositionSurface::BackgroundPresentation>
            presentation;
        std::uint64_t transactionId{};
        std::wstring diagnostic;
    };
    [[nodiscard]] std::optional<Observation> Observe(
        const ComputedCompositorBackground& background,
        DeclarativeRenderer& renderer,
        OverlayCompositionSurface& surface,
        unsigned int width,
        unsigned int height,
        float pixelsPerDip,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] bool CommitObservation(std::uint64_t transactionId) noexcept;
    void CancelObservation(std::uint64_t transactionId) noexcept;
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
    void Abandon() noexcept;
    [[nodiscard]] std::optional<std::uint64_t> deadline() const noexcept;

private:
#ifdef WRAIL_COMPOSITOR_BACKGROUND_TESTING
    friend struct CompositorBackgroundSurfaceCoordinatorTestAccess;
#endif
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
        std::wstring presentationKey;
        std::uint64_t presentationGeneration{};
    } state_;
    struct PendingObservation final {
        std::uint64_t id{};
        State state;
    };
    struct Staged final {
        std::vector<OverlayCompositionSurface::Frame> frames;
        OverlayCompositionSurface::BackgroundPresentation presentation;
    };

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
        Staged& staged,
        std::wstring& diagnostic);
    std::uint64_t observationClock_{};
    std::optional<PendingObservation> pendingObservation_;
};

} // namespace widgetrail
