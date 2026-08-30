#pragma once

#include "WidgetBridgeClient.h"

#include <algorithm>

namespace widgetrail {

struct EmbeddedMediaPresentationRetention final {
    bool identityCurrent{};
    bool resourceContractCurrent{};
    bool projectionCurrent{};
    bool geometryCurrent{};
    long long committedSequence{};
    long long successorSequence{};
};

// A compatible successor may keep showing the last committed media plane while
// its native shell renders. Geometry-changing notifications revoke the stored
// bounds before reaching this decision, and genuine resource/owner changes are
// excluded explicitly.
[[nodiscard]] constexpr bool RetainEmbeddedMediaPresentation(
    const EmbeddedMediaPresentationRetention& state) noexcept {
    return state.identityCurrent && state.resourceContractCurrent &&
        state.projectionCurrent && state.geometryCurrent &&
        state.successorSequence > state.committedSequence;
}

// Pending playback commands are presentation state. They must not force the
// sealed adapter bundle or its controller session to be resolved again.
[[nodiscard]] inline bool SameEmbeddedMediaResourceContract(
    const EmbeddedMediaSurfaceDeclaration& admitted,
    const EmbeddedMediaSurfaceDeclaration& candidate) noexcept {
    return admitted.id == candidate.id &&
        admitted.entryAsset == candidate.entryAsset &&
        admitted.surface.mode == candidate.surface.mode &&
        admitted.surface.widthMode == candidate.surface.widthMode &&
        admitted.surface.heightMode == candidate.surface.heightMode &&
        admitted.surface.preferredWidth == candidate.surface.preferredWidth &&
        admitted.surface.preferredHeight == candidate.surface.preferredHeight &&
        admitted.surface.minimumWidth == candidate.surface.minimumWidth &&
        admitted.surface.minimumHeight == candidate.surface.minimumHeight &&
        admitted.aspectRatio == candidate.aspectRatio &&
        admitted.compactPinnedPresentation == candidate.compactPinnedPresentation &&
        admitted.mediaSeekStepSeconds == candidate.mediaSeekStepSeconds &&
        admitted.commands == candidate.commands &&
        admitted.allowedFrameOrigins == candidate.allowedFrameOrigins &&
        admitted.allowedFrameDomainFamilies == candidate.allowedFrameDomainFamilies &&
        admitted.resources.size() == candidate.resources.size() &&
        std::equal(
            admitted.resources.begin(), admitted.resources.end(),
            candidate.resources.begin(), candidate.resources.end(),
            [](const auto& left, const auto& right) {
                return left.path == right.path &&
                    left.contentType == right.contentType;
            });
}

[[nodiscard]] inline EmbeddedMediaSurfaceDeclaration
EmbeddedMediaResourceContract(EmbeddedMediaSurfaceDeclaration declaration) {
    declaration.pendingCommand.reset();
    return declaration;
}

} // namespace widgetrail
