#pragma once

#include <optional>
#include <string>
#include <utility>

namespace gba::accessibility {

struct ProjectionKey final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    std::wstring activeInputScopeId;
    std::wstring focusedElementId;
    long long snapshotSequence{};
    long long appearanceRevision{};
    float viewportX{};
    float viewportY{};
    float viewportWidth{};
    float viewportHeight{};
    float panelWidth{};
    float panelHeight{};
    float pixelsPerDip{};
    float textScale{};
    int minimumFontWeight{};
    bool reducedMotion{};
    bool reducedTransparency{};

    friend bool operator==(const ProjectionKey&, const ProjectionKey&) = default;
};

/// Owns semantic projection cadence independently from paint cadence. Snapshot,
/// focus, layout, DPI, or accessibility-policy changes collect immediately;
/// paint-only motion retains the immutable tree and collects once more when the
/// transition reaches its final geometry.
class ProjectionTracker final {
public:
    [[nodiscard]] bool ShouldCollect(const ProjectionKey& key) const noexcept {
        return dirty_ || !published_ || *published_ != key;
    }

    void Published(ProjectionKey key) {
        published_ = std::move(key);
        dirty_ = false;
    }

    /// Returns true exactly when the caller should request one final-geometry
    /// frame after a paint-only animation settles.
    [[nodiscard]] bool ObserveFrame(const bool animationActive) noexcept {
        if (animationActive) {
            animationPending_ = true;
            return false;
        }
        if (!animationPending_) return false;
        animationPending_ = false;
        dirty_ = true;
        return true;
    }

    void Clear() noexcept {
        published_.reset();
        dirty_ = true;
        animationPending_ = false;
    }

private:
    std::optional<ProjectionKey> published_;
    bool dirty_{true};
    bool animationPending_{};
};

} // namespace gba::accessibility
