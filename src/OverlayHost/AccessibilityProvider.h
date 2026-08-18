#pragma once

#include "AccessibilityTree.h"

#include <UIAutomation.h>
#include <Windows.h>

#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail::accessibility {

enum class ActionKind {
    Invoke,
    SetValue,
    Focus,
};

struct ActionRequest final {
    ActionKind kind{ActionKind::Invoke};
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    long long snapshotSequence{};
    std::wstring activeInputScopeId;
    ElementDomain domain{ElementDomain::Widget};
    std::wstring nodeId;
    std::wstring actionId;
    HostAction hostAction{HostAction::None};
    std::wstring hostTargetId;
    std::optional<double> requestedValue;
};

struct ScreenTransform final {
    double originX{};
    double originY{};
    double pixelsPerDip{1.0};
    double width{};
    double height{};
    double scaleX{};
    double scaleY{};
    double visualOffsetX{};
    double visualOffsetY{};
    double chromeOffsetX{};
    double chromeOffsetY{};
    bool independentChrome{};
};

struct ResolvedAction final {
    ActionKind kind{ActionKind::Invoke};
    std::wstring nodeId;
    std::wstring protocolButton;
    std::optional<double> requestedValue;
};

/// Revalidates a queued request against the exact current worker generation
/// and snapshot before the UI thread mutates focus or sends controller input.
[[nodiscard]] std::optional<ResolvedAction> ResolveActionRequest(
    const ActionRequest& request,
    std::wstring_view currentWidgetId,
    std::wstring_view currentRuntimeGeneration,
    const WidgetSnapshot& currentSnapshot) noexcept;

struct ProviderState;

/// Owns the free-threaded UI Automation provider state for one HWND. Provider
/// reads copy one immutable published tree. Mutations are admitted only into a
/// bounded queue and notified asynchronously; the window thread remains the
/// sole owner of focus and worker IPC.
class ProviderHost final {
public:
    ProviderHost();
    ~ProviderHost();
    ProviderHost(const ProviderHost&) = delete;
    ProviderHost& operator=(const ProviderHost&) = delete;

    void Bind(HWND window, UINT actionMessage);
    void Detach() noexcept;
    void SetWindowFocused(bool focused) noexcept;
    void SetWindowVisible(bool visible) noexcept;
    void Publish(Tree tree, ScreenTransform transform);
    void Clear() noexcept;

    [[nodiscard]] LRESULT HandleWmGetObject(WPARAM wParam, LPARAM lParam);
    [[nodiscard]] HRESULT GetRootProvider(IRawElementProviderSimple** provider) const;
    [[nodiscard]] std::vector<ActionRequest> TakeActions() noexcept;
    void RaisePendingEvents() noexcept;

private:
    std::shared_ptr<ProviderState> state_;
};

} // namespace widgetrail::accessibility
