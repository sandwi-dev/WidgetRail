#pragma once

#include <filesystem>
#include <string>
#include <string_view>

namespace gba {

struct RenderResult;
struct WidgetSnapshot;

enum class ScrollEvidencePublishResult {
    Disabled,
    Published,
    InvalidFrame,
    UnavailablePath,
};

// Authenticated development-only evidence owner. OverlayApp admits the CLI
// path; this boundary owns all probe state and publication behavior.
class ScrollEvidenceProbe final {
public:
    [[nodiscard]] bool Enable(const std::filesystem::path& destination);
    [[nodiscard]] bool enabled() const noexcept { return !destination_.empty(); }

    [[nodiscard]] bool RecordTarget(
        std::wstring_view target,
        std::wstring_view direction);

    [[nodiscard]] ScrollEvidencePublishResult Publish(
        std::wstring_view widgetId,
        const WidgetSnapshot& snapshot,
        const RenderResult& result,
        std::wstring_view renderedFocusId,
        float pixelScale,
        float textScale) const;

private:
    std::filesystem::path destination_;
    std::wstring explicitTarget_;
    std::wstring direction_;
};

} // namespace gba
