#pragma once
#include "DeclarativeRenderer.h"
#include <windows.h>
#include <memory>
#include <string>
#include <vector>

namespace widgetrail {
struct DeveloperInspectorNode final {
    RenderInspectionNode visual;
    std::wstring details;
};
struct DeveloperInspectorFrame final {
    std::wstring widgetId, focusedId, status;
    long long sequence{};
    declarative::Rect viewport;
    std::vector<DeveloperInspectorNode> nodes;
};
[[nodiscard]] DeveloperInspectorFrame BuildDeveloperInspectorFrame(
    std::wstring_view widgetId, const WidgetSnapshot& snapshot,
    const RenderResult& result, std::wstring_view focusedId);

// Read-only, same-process development UI. No widget input, IPC or disk capture.
class DeveloperInspectorWindow final {
public:
    DeveloperInspectorWindow();
    ~DeveloperInspectorWindow();
    DeveloperInspectorWindow(const DeveloperInspectorWindow&) = delete;
    DeveloperInspectorWindow& operator=(const DeveloperInspectorWindow&) = delete;
    [[nodiscard]] bool WantsCapture() const noexcept;
    void Publish(HWND owner, DeveloperInspectorFrame frame);
    void Unavailable(std::wstring_view reason);
    void RetainLastFrame(std::wstring_view reason);
    void Navigation(std::wstring_view from, std::wstring_view direction,
        std::wstring_view target, std::wstring_view resolution);
    void Reopen() noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
