#include "CompositorBackgroundSurfaceCoordinator.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace widgetrail {
namespace {
constexpr std::uint64_t kSettleMilliseconds = 150;
constexpr std::uint64_t kFadeMilliseconds = 400;
constexpr std::uint64_t kMaximumRebaseBytes = 32ULL * 1024ULL * 1024ULL;
}

bool CompositorBackgroundSurfaceCoordinator::SameDestination(
    const ComputedCompositorBackground& left,
    const ComputedCompositorBackground& right) noexcept {
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
        std::abs(left.bounds.height - right.bounds.height) < 0.01F;
}

std::wstring CompositorBackgroundSurfaceCoordinator::Key(
    const ComputedCompositorBackground& background) {
    return background.authorityId + L"\x1f" + background.widgetInstanceId +
        L"\x1f" + background.nodeId + L"\x1f" + background.imageSource +
        L"\x1f" + background.artworkHandle + L"\x1f" + background.imageFit;
}

bool CompositorBackgroundSurfaceCoordinator::Stage(
    State& next,
    DeclarativeRenderer& renderer,
    OverlayCompositionSurface& surface,
    const unsigned int width,
    const unsigned int height,
    const float pixelsPerDip,
    const std::uint64_t nowMilliseconds,
    const bool activate,
    std::wstring& diagnostic) {
    if (!next.proposal || width == 0 || height == 0 ||
        !std::isfinite(pixelsPerDip) || pixelsPerDip <= 0.0F) return false;
    auto& proposal = *next.proposal;
    std::array<OverlayCompositionSurface::Frame, 3> frames;
    const auto begin = [&](const std::size_t index,
                           const OverlayCompositionSurface::Layer layer) {
        const HRESULT result = surface.BeginFrame(
            layer, width, height, 0, 0, nullptr, frames[index]);
        if (FAILED(result)) return false;
        frames[index].target->SetDpi(96, 96);
        frames[index].target->SetTransform(
            D2D1::Matrix3x2F::Scale(pixelsPerDip, pixelsPerDip));
        frames[index].target->Clear(D2D1::ColorF(0, 0, 0, 0));
        return true;
    };
    auto outgoing = next.committed;
    const std::size_t candidateIndex = outgoing ? 2 : 1;
    const auto candidateLayer = outgoing
        ? OverlayCompositionSurface::Layer::BackgroundIncoming
        : OverlayCompositionSurface::Layer::BackgroundOutgoing;
    if (!begin(candidateIndex, candidateLayer)) return false;
    if (!proposal.image.bitmap)
        proposal.image.bitmap = renderer.ResolveCompositorBackgroundBitmap(
            frames[candidateIndex].target.Get(), proposal.image.descriptor);
    if (!proposal.image.bitmap) {
        surface.AbandonFrame(frames[candidateIndex]);
        diagnostic = L"readiness=pending";
        return false;
    }
    renderer.PaintCompositorBackground(
        frames[candidateIndex].target.Get(), proposal.image.descriptor,
        proposal.image.bitmap.Get(), false);
    if (FAILED(surface.EndFrame(frames[candidateIndex]))) return false;

    if (!begin(0, OverlayCompositionSurface::Layer::BackgroundBase)) return false;
    renderer.PaintCompositorBackground(
        frames[0].target.Get(), proposal.image.descriptor, nullptr, true);
    if (FAILED(surface.EndFrame(frames[0]))) return false;

    if (outgoing && !begin(
            1, OverlayCompositionSurface::Layer::BackgroundOutgoing)) return false;
    if (activate && next.incoming) {
        if (static_cast<std::uint64_t>(width) * height * 4ULL >
            kMaximumRebaseBytes) {
            for (auto& frame : frames) surface.AbandonFrame(frame);
            diagnostic = L"rebase=resource-bound";
            return false;
        }
        const auto elapsed = std::min<std::uint64_t>(
            kFadeMilliseconds, nowMilliseconds - next.transitionStartedAt);
        const float linear = static_cast<float>(elapsed) /
            static_cast<float>(kFadeMilliseconds);
        const float inverse = 1.0F - linear;
        const float progress = 1.0F - inverse * inverse * inverse;
        ComPtr<ID2D1BitmapRenderTarget> composite;
        const auto size = D2D1::SizeF(
            proposal.image.descriptor.bounds.width,
            proposal.image.descriptor.bounds.height);
        if (FAILED(frames[1].target->CreateCompatibleRenderTarget(
                &size, nullptr, nullptr,
                D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE,
                composite.ReleaseAndGetAddressOf())) || !composite) {
            surface.AbandonFrame(frames[1]);
            diagnostic = L"rebase=failed";
            return false;
        }
        composite->BeginDraw();
        composite->Clear(D2D1::ColorF(0, 0, 0, 0));
        renderer.PaintCompositorBackground(
            composite.Get(), next.committed->descriptor,
            next.committed->bitmap.Get(), false, 1.0F);
        renderer.PaintCompositorBackground(
            composite.Get(), next.incoming->descriptor,
            next.incoming->bitmap.Get(), false, progress);
        Image rebased{next.incoming->descriptor, {}};
        if (FAILED(composite->EndDraw()) ||
            FAILED(composite->GetBitmap(rebased.bitmap.ReleaseAndGetAddressOf()))) {
            surface.AbandonFrame(frames[1]);
            diagnostic = L"rebase=failed";
            return false;
        }
        outgoing = std::move(rebased);
    }
    if (outgoing) {
        renderer.PaintCompositorBackground(
            frames[1].target.Get(), outgoing->descriptor,
            outgoing->bitmap.Get(), false);
        if (FAILED(surface.EndFrame(frames[1]))) return false;
    }
    const auto generation = proposal.generation;
    OverlayCompositionSurface::BackgroundPresentation visual{
        true, outgoing.has_value(), !activate, Key(proposal.image.descriptor),
        generation, 0};
    std::vector<OverlayCompositionSurface::Frame*> pointers;
    for (auto& frame : frames) {
        if (frame.surface) pointers.push_back(&frame);
    }
    OverlayCompositionSurface::CommitTiming timing;
    if (FAILED(surface.CommitFrames(pointers, false, timing, nullptr, &visual)))
        return false;
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
    diagnostic = L"stage=background commit-us=" +
        std::to_wstring(timing.commitMicroseconds);
    return true;
}

bool CompositorBackgroundSurfaceCoordinator::Observe(
    const ComputedCompositorBackground& background,
    DeclarativeRenderer& renderer,
    OverlayCompositionSurface& surface,
    const unsigned int width,
    const unsigned int height,
    const float pixelsPerDip,
    const std::uint64_t nowMilliseconds,
    std::wstring& diagnostic) {
    auto next = state_;
    if (next.incoming && nowMilliseconds >= next.transitionStartedAt &&
        nowMilliseconds - next.transitionStartedAt >= kFadeMilliseconds) {
        next.committed = next.incoming;
        next.incoming.reset();
        next.transitionStartedAt = 0;
    }
    if (next.incoming && SameDestination(next.incoming->descriptor, background)) {
        next.incoming->descriptor = background;
        next.proposal.reset();
        state_ = std::move(next);
        diagnostic = L"proposal=deduped";
        return true;
    }
    if (!next.incoming && next.committed &&
        SameDestination(next.committed->descriptor, background)) {
        next.committed->descriptor = background;
        next.proposal.reset();
        state_ = std::move(next);
        diagnostic = L"proposal=deduped";
        return true;
    }
    if (!next.proposal ||
        !SameDestination(next.proposal->image.descriptor, background)) {
        if (next.generation == UINT64_MAX) return false;
        next.proposal = Proposal{{background, {}}, nowMilliseconds,
            ++next.generation, false};
        diagnostic = L"proposal=observed deadline=" +
            std::to_wstring(nowMilliseconds + kSettleMilliseconds);
    } else {
        next.proposal->image.descriptor = background;
    }
    if (!next.committed)
        (void)Stage(next, renderer, surface, width, height,
            pixelsPerDip, nowMilliseconds, true, diagnostic);
    else if (!next.incoming)
        (void)Stage(next, renderer, surface, width, height,
            pixelsPerDip, nowMilliseconds, false, diagnostic);
    state_ = std::move(next);
    return true;
}

bool CompositorBackgroundSurfaceCoordinator::Advance(
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
            current->snapshotSequence) return false;
    auto next = state_;
    const auto deadline = next.proposal->observedAt + kSettleMilliseconds;
    if (nowMilliseconds < deadline) {
        if (!next.incoming && !next.proposal->staged)
            (void)Stage(next, renderer, surface, width, height,
                pixelsPerDip, nowMilliseconds, false, diagnostic);
        state_ = std::move(next);
        return true;
    }
    if (next.proposal->staged && !next.incoming) {
        OverlayCompositionSurface::CommitTiming timing;
        const auto key = Key(next.proposal->image.descriptor);
        if (FAILED(surface.CommitPreparedBackground(
                key, next.proposal->generation, timing))) return false;
        next.incoming = next.proposal->image;
        next.transitionStartedAt = nowMilliseconds;
        next.proposal.reset();
        diagnostic = L"start=opacity-only commit-us=" +
            std::to_wstring(timing.commitMicroseconds);
        state_ = std::move(next);
        return true;
    }
    if (!Stage(next, renderer, surface, width, height,
            pixelsPerDip, nowMilliseconds, true, diagnostic)) return false;
    state_ = std::move(next);
    return true;
}

void CompositorBackgroundSurfaceCoordinator::Retire(
    OverlayCompositionSurface& surface) noexcept {
    if (!state_.committed && !state_.incoming && !state_.proposal) return;
    OverlayCompositionSurface::CommitTiming timing;
    if (SUCCEEDED(surface.RetireBackground(timing))) state_ = {};
}

std::optional<std::uint64_t>
CompositorBackgroundSurfaceCoordinator::deadline() const noexcept {
    if (!state_.proposal || state_.proposal->observedAt >
        UINT64_MAX - kSettleMilliseconds) return std::nullopt;
    return state_.proposal->observedAt + kSettleMilliseconds;
}

} // namespace widgetrail
