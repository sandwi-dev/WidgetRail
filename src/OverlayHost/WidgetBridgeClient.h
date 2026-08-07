#pragma once

#include <Windows.h>

#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace gba {

struct WidgetDescriptorQuickAction final {
    std::wstring id;
    std::wstring label;
    std::wstring actionId;
    std::wstring sourceElementId;
    std::optional<std::wstring> controllerButton;
};

/// Public catalog data returned by WidgetBridge. Worker paths and arguments are
/// deliberately absent from this native model.
struct WidgetDescriptor final {
    std::wstring id;
    std::wstring name;
    std::wstring instanceId;
    std::vector<WidgetDescriptorQuickAction> quickActions;
};

struct WidgetQuickAction final {
    std::wstring button;
    std::wstring actionId;
    std::wstring label;
};

struct WidgetShortcut final {
    std::wstring button;
    std::wstring actionId;
    std::wstring phase;
};

struct WidgetStyleValue final {
    std::wstring kind;
    std::wstring text;
    std::optional<double> number;
    std::wstring unit;
};

using WidgetComputedStyle =
    std::unordered_map<std::wstring, WidgetStyleValue>;

struct WidgetNode final {
    std::wstring id;
    std::wstring kind;
    std::wstring text;
    std::wstring accessibilityLabel;
    std::wstring actionId;
    std::wstring imageSource;
    std::wstring imageFit;
    std::wstring glyph;
    std::wstring inputScopeId;
    std::vector<std::wstring> styleClasses;
    std::vector<WidgetShortcut> shortcuts;
    std::wstring focusUp;
    std::wstring focusDown;
    std::wstring focusLeft;
    std::wstring focusRight;
    WidgetComputedStyle baseStyle;
    WidgetComputedStyle focusedStyle;
    double value{};
    double maximum{};
    bool hasProgress{};
    bool isDisabled{};
    bool isSelected{};
    bool isBusy{};
    std::vector<WidgetNode> children;
};

struct WidgetSnapshot final {
    long long sequence{};
    std::wstring instanceId;
    std::wstring activeInputScopeId;
    std::wstring initialFocusId;
    std::vector<WidgetQuickAction> quickActions;
    WidgetNode root;
};

class WidgetBridgeClient final {
public:
    WidgetBridgeClient() = default;
    ~WidgetBridgeClient();
    WidgetBridgeClient(const WidgetBridgeClient&) = delete;
    WidgetBridgeClient& operator=(const WidgetBridgeClient&) = delete;

    [[nodiscard]] bool EnsureStarted(const std::wstring& installationDirectory);
    void Stop() noexcept;
    /// Enumerates public widget descriptors without starting widget workers.
    [[nodiscard]] std::optional<std::vector<WidgetDescriptor>> ListWidgets();
    /// Sends the worker's explicit background, visible, or interactive state.
    [[nodiscard]] std::optional<bool> SetWidgetLifecycle(
        std::wstring_view widgetId,
        std::wstring_view state);
    [[nodiscard]] std::optional<WidgetSnapshot> GetSnapshot(std::wstring_view widgetId);
    [[nodiscard]] std::optional<bool> SendControllerInput(
        std::wstring_view widgetId,
        std::wstring_view button,
        std::wstring_view context,
        std::wstring_view focusedElementId,
        std::wstring_view activeInputScopeId,
        long long snapshotSequence,
        long long sequence,
        long long monotonicTimestampMicroseconds);
    [[nodiscard]] const std::wstring& lastError() const noexcept { return lastError_; }
    /// Non-blocking UI-thread pump for complete asynchronous bridge events.
    [[nodiscard]] bool PumpEvents();
    [[nodiscard]] bool takeInvalidated() noexcept;

private:
    [[nodiscard]] bool Launch(const std::wstring& installationDirectory);
    [[nodiscard]] bool Connect();
    [[nodiscard]] bool WriteFrame(std::string_view utf8);
    [[nodiscard]] std::optional<std::string> ReadFrame();
    void Fail(std::wstring message);

    HANDLE pipe_{INVALID_HANDLE_VALUE};
    HANDLE process_{};
    DWORD processId_{};
    std::wstring pipeName_;
    std::wstring lastError_;
    long long nextRequestId_{};
    bool invalidated_{};
};

} // namespace gba

#ifdef GBA_WIDGET_BRIDGE_CLIENT_TESTING
namespace gba::testing {
[[nodiscard]] std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    std::string_view payloadUtf8,
    std::wstring& error);
}
#endif
