#pragma once

#include "WidgetBridgeClient.h"

#include <algorithm>

namespace widgetrail {

struct EmbeddedMediaDocumentIdentity final {
    std::wstring id;
    std::wstring entryAsset;
    std::vector<EmbeddedMediaResourceDeclaration> resources;
    std::vector<std::wstring> allowedFrameOrigins;
    std::vector<std::wstring> allowedFrameDomainFamilies;
};

[[nodiscard]] inline EmbeddedMediaDocumentIdentity
MakeEmbeddedMediaDocumentIdentity(
    const EmbeddedMediaSessionDeclaration& declaration) {
    return {
        declaration.id,
        declaration.entryAsset,
        declaration.resources,
        declaration.allowedFrameOrigins,
        declaration.allowedFrameDomainFamilies,
    };
}

[[nodiscard]] inline bool SameEmbeddedMediaDocumentIdentity(
    const EmbeddedMediaDocumentIdentity& admitted,
    const EmbeddedMediaDocumentIdentity& candidate) noexcept {
    return admitted.id == candidate.id &&
        admitted.entryAsset == candidate.entryAsset &&
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

[[nodiscard]] inline bool SameEmbeddedMediaDocumentIdentity(
    const EmbeddedMediaDocumentIdentity& admitted,
    const EmbeddedMediaSessionDeclaration& candidate) noexcept {
    return SameEmbeddedMediaDocumentIdentity(
        admitted, MakeEmbeddedMediaDocumentIdentity(candidate));
}

struct EmbeddedMediaPresentationRetention final {
    bool identityCurrent{};
    bool documentIdentityCurrent{};
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
    return state.identityCurrent && state.documentIdentityCurrent &&
        state.projectionCurrent && state.geometryCurrent &&
        state.successorSequence > state.committedSequence;
}

// Pending playback commands are presentation state. They must not force the
// sealed adapter bundle or its controller session to be resolved again.
[[nodiscard]] inline bool SameEmbeddedMediaDocumentIdentity(
    const EmbeddedMediaSessionDeclaration& admitted,
    const EmbeddedMediaSessionDeclaration& candidate) noexcept {
    return SameEmbeddedMediaDocumentIdentity(
        MakeEmbeddedMediaDocumentIdentity(admitted),
        MakeEmbeddedMediaDocumentIdentity(candidate));
}

} // namespace widgetrail
