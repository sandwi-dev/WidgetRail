#pragma once

#include "AccessibilityProvider.h"
#include "DeclarativeRenderer.h"
#include "PinnedSurfacePolicy.h"
#include "WidgetBridgeClient.h"

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cstddef>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace gba::pinned {

enum class WidgetSurfacePresentationState {
    Hidden,
    Overlay,
    PinnedInteractive,
    PinnedClickThrough,
};

enum class WidgetSurfaceStopReason {
    Unpin,
    Close,
    WidgetRemoved,
    RuntimeReplaced,
    WorkerUnavailable,
    HostExit,
    CoordinatorDisposed,
};

struct WidgetSurfaceAdmission final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring name;
    bool pinningSupported{};
    WidgetSnapshot snapshot;
};

/// Sole native owner for the first generic pinned HWND. Widget input is data
/// only: catalog identity, immutable generations, and a validated declarative
/// snapshot. No public or worker-facing type can supply a window or z-order.
class WidgetSurfaceCoordinator final {
public:
    static constexpr std::size_t MaximumPinnedSurfaces = 1;

    WidgetSurfaceCoordinator();
    ~WidgetSurfaceCoordinator();
    WidgetSurfaceCoordinator(const WidgetSurfaceCoordinator&) = delete;
    WidgetSurfaceCoordinator& operator=(const WidgetSurfaceCoordinator&) = delete;

    [[nodiscard]] bool Initialize(
        HINSTANCE instance,
        HWND notificationWindow,
        UINT notificationMessage,
        ID2D1Factory* d2dFactory,
        IDWriteFactory* writeFactory,
        RemoteImageCache* imageCache,
        std::wstring& error);
    [[nodiscard]] bool Pin(WidgetSurfaceAdmission admission, std::wstring& error);
    [[nodiscard]] bool UpdateSnapshot(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        const WidgetSnapshot& snapshot);
    [[nodiscard]] bool SetInteractionMode(InteractionMode mode);
    [[nodiscard]] bool ToggleInteractionMode();
    [[nodiscard]] bool Unpin(WidgetSurfaceStopReason reason) noexcept;
    void OnOverlayHidden() noexcept;
    void OnOverlayShown() noexcept;
    void ReconcileCatalog(const std::vector<WidgetDescriptor>& descriptors) noexcept;
    void Dispose() noexcept;

    [[nodiscard]] bool pinned() const noexcept;
    [[nodiscard]] HWND window() const noexcept { return window_; }
    [[nodiscard]] std::wstring_view widgetId() const noexcept;
    [[nodiscard]] std::wstring_view runtimeGeneration() const noexcept;
    [[nodiscard]] InteractionMode interactionMode() const noexcept;
    [[nodiscard]] WidgetSurfacePresentationState presentationState() const noexcept;
    [[nodiscard]] std::size_t teardownCount() const noexcept { return teardownCount_; }
    [[nodiscard]] WidgetSurfaceStopReason lastStopReason() const noexcept {
        return lastStopReason_;
    }

private:
    static LRESULT CALLBACK WindowProc(HWND, UINT, WPARAM, LPARAM);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    [[nodiscard]] bool CreateWindowForAdmission(std::wstring& error);
    [[nodiscard]] bool EnsureGraphicsResources();
    void Paint();
    void PublishAccessibility();
    void ApplyWindowPolicy();
    void NotifyOwner() const noexcept;
    void ReleaseGraphicsResources() noexcept;
    void OnWindowDestroyed() noexcept;

    HINSTANCE instance_{};
    HWND notificationWindow_{};
    UINT notificationMessage_{};
    HWND window_{};
    bool initialized_{};
    bool disposed_{};
    bool tearingDown_{};
    std::optional<WidgetSurfaceAdmission> admission_;
    SurfacePolicy policy_;
    std::size_t teardownCount_{};
    WidgetSurfaceStopReason lastStopReason_{WidgetSurfaceStopReason::Unpin};
    accessibility::ProviderHost accessibilityProvider_;
    Microsoft::WRL::ComPtr<ID2D1Factory> d2dFactory_;
    Microsoft::WRL::ComPtr<IDWriteFactory> writeFactory_;
    std::unique_ptr<DeclarativeRenderer> renderer_;
    RemoteImageCache* imageCache_{};
    Microsoft::WRL::ComPtr<ID2D1HwndRenderTarget> renderTarget_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> backgroundBrush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> chromeBrush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> textBrush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> secondaryBrush_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> titleFormat_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> chromeFormat_;
};

[[nodiscard]] constexpr std::wstring_view WidgetSurfacePresentationStateValue(
    const WidgetSurfacePresentationState state) noexcept {
    switch (state) {
    case WidgetSurfacePresentationState::Overlay: return L"overlay";
    case WidgetSurfacePresentationState::PinnedInteractive: return L"pinnedInteractive";
    case WidgetSurfacePresentationState::PinnedClickThrough: return L"pinnedClickThrough";
    case WidgetSurfacePresentationState::Hidden: return L"hidden";
    }
    return L"hidden";
}

} // namespace gba::pinned
