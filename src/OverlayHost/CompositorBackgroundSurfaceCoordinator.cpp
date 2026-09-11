#include "CompositorBackgroundSurfaceCoordinator.h"

#include "BackgroundSurfaceTransitionPolicy.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <limits>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace widgetrail {
namespace {
constexpr auto kSettleMilliseconds = background_surface_policy::SettleMilliseconds;
constexpr auto kFadeMilliseconds = background_surface_policy::FadeMilliseconds;
constexpr auto kMaximumRebaseBytes = background_surface_policy::MaximumRebaseBytes;
}

bool CompositorBackgroundSurfaceCoordinator::SameDestination(
    const ComputedCompositorBackground& left,
    const ComputedCompositorBackground& right) noexcept {
    const auto leftClip = left.clipBounds.value_or(left.bounds);
    const auto rightClip = right.clipBounds.value_or(right.bounds);
    return left.authorityId == right.authorityId &&
        left.widgetInstanceId == right.widgetInstanceId &&
        left.nodeId == right.nodeId &&
        left.imageSource == right.imageSource &&
        left.artworkHandle == right.artworkHandle &&
        left.imageFit == right.imageFit &&
        left.style.background() == right.style.background() &&
        left.style.imageTint() == right.style.imageTint() &&
        left.style.scrimColor() == right.style.scrimColor() &&
        left.style.objectPosition() == right.style.objectPosition() &&
        std::abs(left.style.cornerRadiusPx() -
            right.style.cornerRadiusPx()) < 0.01F &&
        std::abs(left.opacity - right.opacity) < 0.001F &&
        left.resourceGeneration == right.resourceGeneration &&
        std::abs(left.bounds.x - right.bounds.x) < 0.01F &&
        std::abs(left.bounds.y - right.bounds.y) < 0.01F &&
        std::abs(left.bounds.width - right.bounds.width) < 0.01F &&
        std::abs(left.bounds.height - right.bounds.height) < 0.01F &&
        std::abs(leftClip.x - rightClip.x) < 0.01F &&
        std::abs(leftClip.y - rightClip.y) < 0.01F &&
        std::abs(leftClip.width - rightClip.width) < 0.01F &&
        std::abs(leftClip.height - rightClip.height) < 0.01F;
}

std::wstring CompositorBackgroundSurfaceCoordinator::Key(
    const ComputedCompositorBackground& background) {
    return background.authorityId + L"\x1f" + background.widgetInstanceId +
        L"\x1f" + background.nodeId + L"\x1f" + background.imageSource +
        L"\x1f" + background.artworkHandle + L"\x1f" + background.imageFit;
}

void CompositorBackgroundSurfaceCoordinator::ConfigureTarget(
    ID2D1RenderTarget* const target, const RECT& updateArea,
    const POINT updateOffset, const float pixelsPerDip) {
    const auto mapping = PlanCompositionUpdateRasterMapping(
        updateArea, updateOffset, pixelsPerDip);
    target->SetDpi(96, 96);
    target->SetTransform(
        D2D1::Matrix3x2F::Scale(pixelsPerDip, pixelsPerDip) *
        D2D1::Matrix3x2F::Translation(
            mapping.sceneTranslationPixels.x, mapping.sceneTranslationPixels.y));
}

bool CompositorBackgroundSurfaceCoordinator::RebaseOutgoing(
    const State& state,
    DeclarativeRenderer& renderer,
    ID2D1RenderTarget* const target,
    const float pixelsPerDip,
    const std::uint64_t nowMilliseconds,
    Image& result,
    std::wstring& diagnostic) const {
    if (!state.committed || !state.incoming || !target) {
        diagnostic = L"rebase=invalid-state";
        return false;
    }
    const auto outgoingBounds = state.committed->descriptor.bounds;
    const auto incomingBounds = state.incoming->descriptor.bounds;
    const auto validBounds = [](const auto& bounds) {
        return std::isfinite(bounds.x) && std::isfinite(bounds.y) &&
            std::isfinite(bounds.width) && std::isfinite(bounds.height) &&
            bounds.width > 0.0F && bounds.height > 0.0F;
    };
    if (!validBounds(outgoingBounds) || !validBounds(incomingBounds)) {
        diagnostic = L"rebase=invalid-bounds";
        return false;
    }
    // Keep the same physical pixel grid as the presented layers, including
    // fractional logical origins and any extent retained by an earlier rebase.
    // A retained pixel edge round-trips through float DIPs. Remove only that
    // sub-millipixel noise before rounding outward, otherwise scales such as
    // 1.05 can grow the capture by a pixel on every retarget.
    const auto snapRoundoff = [](const double edge) {
        const double rounded = std::round(edge);
        return std::abs(edge - rounded) < 0.001 ? rounded : edge;
    };
    const double left = std::floor(snapRoundoff(
        static_cast<double>(std::min(outgoingBounds.x, incomingBounds.x)) * pixelsPerDip));
    const double top = std::floor(snapRoundoff(
        static_cast<double>(std::min(outgoingBounds.y, incomingBounds.y)) * pixelsPerDip));
    const double right = std::ceil(snapRoundoff(std::max(
        static_cast<double>(outgoingBounds.x) + outgoingBounds.width,
        static_cast<double>(incomingBounds.x) + incomingBounds.width) * pixelsPerDip));
    const double bottom = std::ceil(snapRoundoff(std::max(
        static_cast<double>(outgoingBounds.y) + outgoingBounds.height,
        static_cast<double>(incomingBounds.y) + incomingBounds.height) * pixelsPerDip));
    const double pixelWidth = right - left;
    const double pixelHeight = bottom - top;
    if (!std::isfinite(pixelsPerDip) || pixelsPerDip <= 0.0F ||
        !std::isfinite(left) || !std::isfinite(top) ||
        !std::isfinite(pixelWidth) || !std::isfinite(pixelHeight) ||
        pixelWidth <= 0 || pixelHeight <= 0 ||
        pixelWidth > std::numeric_limits<UINT32>::max() ||
        pixelHeight > std::numeric_limits<UINT32>::max() ||
        pixelWidth * pixelHeight * 4.0 > kMaximumRebaseBytes) {
        diagnostic = L"rebase=resource-bound";
        return false;
    }
    const auto elapsed = std::min<std::uint64_t>(
        kFadeMilliseconds, nowMilliseconds - state.transitionStartedAt);
    const float linear = static_cast<float>(elapsed) /
        static_cast<float>(kFadeMilliseconds);
    const float inverse = 1.0F - linear;
    const float progress = 1.0F - inverse * inverse * inverse;
    ComPtr<ID2D1BitmapRenderTarget> composite;
    const declarative::Rect bounds{
        static_cast<float>(left / pixelsPerDip),
        static_cast<float>(top / pixelsPerDip),
        static_cast<float>(pixelWidth / pixelsPerDip),
        static_cast<float>(pixelHeight / pixelsPerDip)};
    const auto size = D2D1::SizeF(bounds.width, bounds.height);
    const auto pixels = D2D1::SizeU(
        static_cast<UINT32>(pixelWidth), static_cast<UINT32>(pixelHeight));
    if (FAILED(target->CreateCompatibleRenderTarget(
            &size, &pixels, nullptr,
            D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE,
            composite.ReleaseAndGetAddressOf())) || !composite) {
        diagnostic = L"rebase=failed";
        return false;
    }
    // Capture in surface-local coordinates at the physical display resolution.
    // The parent target's backing-atlas translation must not enter this bitmap.
    composite->SetTransform(D2D1::Matrix3x2F::Translation(-bounds.x, -bounds.y));
    composite->BeginDraw();
    composite->Clear(D2D1::ColorF(0, 0, 0, 0));
    (void)renderer.PaintCompositorBackground(
        composite.Get(), state.committed->descriptor,
        state.committed->bitmap.Get(), false, 1.0F,
        state.committed->surfaceComposite);
    (void)renderer.PaintCompositorBackground(
        composite.Get(), state.incoming->descriptor,
        state.incoming->bitmap.Get(), false, progress,
        state.incoming->surfaceComposite);
    result.descriptor = state.incoming->descriptor;
    result.descriptor.bounds = bounds;
    result.surfaceComposite = true;
    if (FAILED(composite->EndDraw()) ||
        FAILED(composite->GetBitmap(result.bitmap.ReleaseAndGetAddressOf()))) {
        diagnostic = L"rebase=failed";
        return false;
    }
    return true;
}

CompositorBackgroundSurfaceCoordinator::StageDisposition
CompositorBackgroundSurfaceCoordinator::Stage(
    State& next,
    DeclarativeRenderer& renderer,
    OverlayCompositionSurface& surface,
    const unsigned int width,
    const unsigned int height,
    const float pixelsPerDip,
    const std::uint64_t nowMilliseconds,
    const bool activate,
    Staged& staged,
    std::wstring& diagnostic) {
    staged = {};
    if (!next.proposal || width == 0 || height == 0 ||
        !std::isfinite(pixelsPerDip) || pixelsPerDip <= 0.0F)
        return StageDisposition::Failed;
    auto& proposal = *next.proposal;
    // A completed fade can still display the committed image in Incoming.
    // Resolve readiness without opening any displayed surface for drawing.
    if (!proposal.image.bitmap) {
        ComPtr<ID2D1DeviceContext> resources;
        if (FAILED(surface.CreateBitmapResourceContext(
                resources.ReleaseAndGetAddressOf())))
            return StageDisposition::Failed;
        proposal.image.bitmap = renderer.ResolveCompositorBackgroundBitmap(
            resources.Get(), proposal.image.descriptor);
    }
    if (!proposal.image.bitmap && next.committed) {
        diagnostic = L"readiness=pending retained=displayed";
        return StageDisposition::Pending;
    }
    std::array<OverlayCompositionSurface::Frame, 3> frames;
    const auto begin = [&](const std::size_t index,
                           const OverlayCompositionSurface::Layer layer) {
        const HRESULT result = surface.BeginFrame(
            layer, width, height, 0, 0, nullptr, frames[index]);
        if (FAILED(result)) return false;
        ConfigureTarget(frames[index].target.Get(), frames[index].updateArea,
            frames[index].updateOffset, pixelsPerDip);
        return true;
    };
    if (!begin(0, OverlayCompositionSurface::Layer::BackgroundBase))
        return StageDisposition::Failed;
    frames[0].target->Clear(D2D1::ColorF(0, 0, 0, 0));
    (void)renderer.PaintCompositorBackground(
        frames[0].target.Get(), proposal.image.descriptor, nullptr, true);
    if (FAILED(surface.EndFrame(frames[0]))) return StageDisposition::Failed;
    auto outgoing = next.committed;
    if (!proposal.image.bitmap) {
        diagnostic = L"readiness=pending";
        staged.frames.push_back(std::move(frames[0]));
        staged.presentation = {
            true, next.incoming.has_value(), false,
            next.presentationKey.empty()
                ? Key(proposal.image.descriptor) : next.presentationKey,
            next.presentationGeneration == 0
                ? proposal.generation : next.presentationGeneration,
            next.incoming && nowMilliseconds >= next.transitionStartedAt
                ? static_cast<float>(nowMilliseconds - next.transitionStartedAt)
                : 0.0F};
        if (next.presentationKey.empty()) {
            next.presentationKey = staged.presentation.key;
            next.presentationGeneration = staged.presentation.generation;
        }
        return StageDisposition::Pending;
    }
    const std::size_t candidateIndex = outgoing ? 2 : 1;
    const auto candidateLayer = outgoing
        ? OverlayCompositionSurface::Layer::BackgroundIncoming
        : OverlayCompositionSurface::Layer::BackgroundOutgoing;
    if (!begin(candidateIndex, candidateLayer)) return StageDisposition::Failed;
    frames[candidateIndex].target->Clear(D2D1::ColorF(0, 0, 0, 0));
    (void)renderer.PaintCompositorBackground(
        frames[candidateIndex].target.Get(), proposal.image.descriptor,
        proposal.image.bitmap.Get(), false);
    if (FAILED(surface.EndFrame(frames[candidateIndex])))
        return StageDisposition::Failed;

    if (outgoing && !begin(
            1, OverlayCompositionSurface::Layer::BackgroundOutgoing))
        return StageDisposition::Failed;
    if (outgoing)
        frames[1].target->Clear(D2D1::ColorF(0, 0, 0, 0));
    if (activate && next.incoming) {
        Image rebased;
        if (!RebaseOutgoing(
                next, renderer, frames[1].target.Get(), pixelsPerDip,
                nowMilliseconds, rebased, diagnostic)) {
            surface.AbandonFrame(frames[1]);
            return StageDisposition::Failed;
        }
        outgoing = std::move(rebased);
    }
    if (outgoing) {
        (void)renderer.PaintCompositorBackground(
            frames[1].target.Get(), outgoing->descriptor,
            outgoing->bitmap.Get(), false, 1.0F, outgoing->surfaceComposite);
        if (FAILED(surface.EndFrame(frames[1])))
            return StageDisposition::Failed;
    }
    const auto generation = proposal.generation;
    OverlayCompositionSurface::BackgroundPresentation visual{
        true, outgoing.has_value(), !activate, Key(proposal.image.descriptor),
        generation, 0};
    for (auto& frame : frames) {
        if (frame.surface) staged.frames.push_back(std::move(frame));
    }
    staged.presentation = visual;
    if (!outgoing) {
        next.committed = proposal.image;
        next.proposal.reset();
    } else if (activate) {
        next.committed = std::move(outgoing);
        next.incoming = proposal.image;
        next.transitionStartedAt = nowMilliseconds;
        next.proposal.reset();
    } else {
        proposal.staged = true;
    }
    next.presentationKey = visual.key;
    next.presentationGeneration = visual.generation;
    diagnostic = L"stage=background";
    return StageDisposition::Committed;
}

std::optional<CompositorBackgroundSurfaceCoordinator::Observation>
CompositorBackgroundSurfaceCoordinator::Observe(
    const ComputedCompositorBackground& background,
    DeclarativeRenderer& renderer,
    OverlayCompositionSurface& surface,
    const unsigned int width,
    const unsigned int height,
    const float pixelsPerDip,
    const std::uint64_t nowMilliseconds) {
    auto next = state_;
    std::wstring diagnostic;
    Staged staged;
    if (next.incoming && nowMilliseconds >= next.transitionStartedAt &&
        nowMilliseconds - next.transitionStartedAt >= kFadeMilliseconds) {
        next.committed = next.incoming;
        next.incoming.reset();
        next.transitionStartedAt = 0;
    }
    if (next.incoming && SameDestination(next.incoming->descriptor, background)) {
        next.incoming->descriptor = background;
        next.proposal.reset();
        diagnostic = L"proposal=deduped";
        goto publish;
    }
    if (!next.incoming && next.committed &&
        SameDestination(next.committed->descriptor, background)) {
        next.committed->descriptor = background;
        next.proposal.reset();
        diagnostic = L"proposal=deduped";
        goto publish;
    }
    if (!next.proposal ||
        !SameDestination(next.proposal->image.descriptor, background)) {
        if (next.generation == UINT64_MAX) return std::nullopt;
        next.proposal = Proposal{{background, {}}, nowMilliseconds,
            ++next.generation, false};
        diagnostic = L"proposal=observed deadline=" +
            std::to_wstring(nowMilliseconds + kSettleMilliseconds);
    } else {
        next.proposal->image.descriptor = background;
    }
    if (!next.committed) {
        if (Stage(next, renderer, surface, width, height,
                pixelsPerDip, nowMilliseconds, true, staged, diagnostic) ==
            StageDisposition::Failed) return std::nullopt;
    } else if (!next.incoming) {
        if (Stage(next, renderer, surface, width, height,
                pixelsPerDip, nowMilliseconds, false, staged, diagnostic) ==
            StageDisposition::Failed) return std::nullopt;
    }
publish:
    if (observationClock_ == UINT64_MAX) return std::nullopt;
    const auto transactionId = ++observationClock_;
    pendingObservation_ = PendingObservation{transactionId, std::move(next)};
    auto presentation = staged.frames.empty()
        ? std::nullopt
        : std::optional{staged.presentation};
    return Observation{
        std::move(staged.frames),
        std::move(presentation),
        transactionId,
        std::move(diagnostic)};
}

bool CompositorBackgroundSurfaceCoordinator::CommitObservation(
    const std::uint64_t transactionId) noexcept {
    if (!pendingObservation_ || transactionId == 0 ||
        pendingObservation_->id != transactionId) return false;
    state_ = std::move(pendingObservation_->state);
    pendingObservation_.reset();
    return true;
}

void CompositorBackgroundSurfaceCoordinator::CancelObservation(
    const std::uint64_t transactionId) noexcept {
    if (pendingObservation_ && pendingObservation_->id == transactionId)
        pendingObservation_.reset();
}

CompositorBackgroundSurfaceCoordinator::AdvanceDisposition
CompositorBackgroundSurfaceCoordinator::Advance(
    const std::optional<ComputedCompositorBackground>& current,
    DeclarativeRenderer& renderer,
    OverlayCompositionSurface& surface,
    const unsigned int width,
    const unsigned int height,
    const float pixelsPerDip,
    const std::uint64_t nowMilliseconds,
    std::wstring& diagnostic) {
    if (!current || !state_.proposal ||
        !SameDestination(state_.proposal->image.descriptor, *current) ||
        state_.proposal->image.descriptor.focusedElementId !=
            current->focusedElementId ||
        state_.proposal->image.descriptor.snapshotSequence !=
            current->snapshotSequence)
        return AdvanceDisposition::NotApplicableOrStale;
    auto next = state_;
    Staged stagedFrames;
    const auto commitStaged = [&]() {
        if (stagedFrames.frames.empty()) return true;
        std::vector<OverlayCompositionSurface::Frame*> pointers;
        for (auto& frame : stagedFrames.frames) pointers.push_back(&frame);
        OverlayCompositionSurface::CommitTiming timing;
        const HRESULT result = surface.CommitFrames(
            pointers, false, timing, nullptr, &stagedFrames.presentation);
        if (SUCCEEDED(result)) diagnostic += L" commit-us=" +
            std::to_wstring(timing.commitMicroseconds);
        return SUCCEEDED(result);
    };
    const auto deadline = next.proposal->observedAt + kSettleMilliseconds;
    if (nowMilliseconds < deadline) {
        StageDisposition staged = StageDisposition::Committed;
        if (!next.incoming && !next.proposal->staged)
            staged = Stage(next, renderer, surface, width, height,
                pixelsPerDip, nowMilliseconds, false, stagedFrames, diagnostic);
        if (!commitStaged()) return AdvanceDisposition::Failed;
        state_ = std::move(next);
        return staged == StageDisposition::Pending
            ? AdvanceDisposition::HandledPending
            : staged == StageDisposition::Failed
                ? AdvanceDisposition::Failed
                : AdvanceDisposition::Advanced;
    }
    if (next.proposal->staged && !next.incoming) {
        OverlayCompositionSurface::CommitTiming timing;
        const auto key = Key(next.proposal->image.descriptor);
        if (FAILED(surface.CommitPreparedBackground(
                key, next.proposal->generation, timing)))
            return AdvanceDisposition::Failed;
        next.incoming = next.proposal->image;
        next.transitionStartedAt = nowMilliseconds;
        next.proposal.reset();
        diagnostic = L"start=opacity-only commit-us=" +
            std::to_wstring(timing.commitMicroseconds);
        state_ = std::move(next);
        return AdvanceDisposition::Advanced;
    }
    const auto staged = Stage(next, renderer, surface, width, height,
        pixelsPerDip, nowMilliseconds, true, stagedFrames, diagnostic);
    if (staged == StageDisposition::Failed)
        return AdvanceDisposition::Failed;
    if (!commitStaged()) return AdvanceDisposition::Failed;
    if (staged == StageDisposition::Pending) {
        state_ = std::move(next);
        return AdvanceDisposition::HandledPending;
    }
    state_ = std::move(next);
    return AdvanceDisposition::Advanced;
}

void CompositorBackgroundSurfaceCoordinator::Retire(
    OverlayCompositionSurface& surface) noexcept {
    if (!state_.committed && !state_.incoming && !state_.proposal) return;
    OverlayCompositionSurface::CommitTiming timing;
    if (SUCCEEDED(surface.RetireBackground(timing))) state_ = {};
}

void CompositorBackgroundSurfaceCoordinator::Abandon() noexcept {
    state_ = {};
    pendingObservation_.reset();
}

std::optional<std::uint64_t>
CompositorBackgroundSurfaceCoordinator::deadline() const noexcept {
    if (!state_.proposal || state_.proposal->observedAt >
        UINT64_MAX - kSettleMilliseconds) return std::nullopt;
    return state_.proposal->observedAt + kSettleMilliseconds;
}

} // namespace widgetrail
