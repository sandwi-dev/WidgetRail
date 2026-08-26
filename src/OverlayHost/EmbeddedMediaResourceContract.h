#pragma once

#include "WidgetBridgeClient.h"

#include <algorithm>

namespace widgetrail {

// Pending playback commands are presentation state. They must not force the
// sealed adapter bundle or its controller session to be resolved again.
[[nodiscard]] inline bool SameEmbeddedMediaResourceContract(
    const EmbeddedMediaSurfaceDeclaration& admitted,
    const EmbeddedMediaSurfaceDeclaration& candidate) noexcept {
    return admitted.id == candidate.id &&
        admitted.accessibleName == candidate.accessibleName &&
        admitted.entryAsset == candidate.entryAsset &&
        admitted.surface.mode == candidate.surface.mode &&
        admitted.surface.widthMode == candidate.surface.widthMode &&
        admitted.surface.heightMode == candidate.surface.heightMode &&
        admitted.surface.preferredWidth == candidate.surface.preferredWidth &&
        admitted.surface.preferredHeight == candidate.surface.preferredHeight &&
        admitted.surface.minimumWidth == candidate.surface.minimumWidth &&
        admitted.surface.minimumHeight == candidate.surface.minimumHeight &&
        admitted.aspectRatio == candidate.aspectRatio &&
        admitted.commands == candidate.commands &&
        admitted.allowedFrameOrigins == candidate.allowedFrameOrigins &&
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
