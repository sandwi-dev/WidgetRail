#pragma once

#include <Windows.h>

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
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
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring icon{L"connection"};
    std::vector<WidgetDescriptorQuickAction> quickActions;
};

/// Returns prior widget IDs whose runtime identity was removed or replaced,
/// independent of any snapshot/focus cache residency.
[[nodiscard]] std::vector<std::wstring> ChangedWidgetRuntimeIds(
    const std::vector<WidgetDescriptor>& before,
    const std::vector<WidgetDescriptor>& after);

/// Preserves the identity and arrival order of asynchronous widget
/// invalidations while coalescing repeated IDs. The bound matches the maximum
/// public bridge catalog size, preventing an untrusted peer from growing host
/// memory without limit.
class WidgetInvalidationQueue final {
public:
    static constexpr std::size_t MaximumWidgetIds = 256;

    [[nodiscard]] bool Push(std::wstring widgetId);
    [[nodiscard]] std::vector<std::wstring> Take() noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return queued_.size(); }

private:
    std::vector<std::wstring> queued_;
    std::unordered_set<std::wstring> known_;
};

enum class WidgetHostEffectKind {
    CloseOverlayAfterAppLaunch,
};

/// A trusted, one-shot host effect emitted only after the platform broker has
/// completed the corresponding privileged operation. Widget snapshots and
/// worker action acknowledgements cannot create this value.
struct WidgetHostEffect final {
    long long sequence{};
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    WidgetHostEffectKind kind{WidgetHostEffectKind::CloseOverlayAfterAppLaunch};
};

/// Bounded replay-resistant queue for asynchronous broker-owned effects.
class WidgetHostEffectQueue final {
public:
    static constexpr std::size_t MaximumEffects = 16;

    /// Invalid values fail. Duplicate/stale sequences are accepted but ignored.
    [[nodiscard]] bool Push(WidgetHostEffect effect);
    [[nodiscard]] std::vector<WidgetHostEffect> Take() noexcept;
    void Reset() noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return queued_.size(); }
    [[nodiscard]] long long lastSequence() const noexcept { return lastSequence_; }

private:
    long long lastSequence_{};
    std::vector<WidgetHostEffect> queued_;
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
    std::wstring accessibilityValue;
    std::wstring actionId;
    std::wstring valueChangedActionId;
    std::wstring sliderInteractionMode;
    std::wstring imageSource;
    std::wstring imageFit;
    std::wstring glyph;
    std::wstring indicatorSize;
    // Empty and "always" are equivalent. Protocol-v9 conditional values are
    // interpreted only by the native host against its compact breakpoint.
    std::wstring visibleWhen;
    std::wstring inputScopeId;
    std::wstring scrollAxis;
    std::wstring actionSurfaceOrientation;
    std::optional<double> gridMinimumColumnWidth;
    std::optional<std::size_t> gridMaximumColumns;
    std::vector<std::wstring> styleClasses;
    std::vector<WidgetShortcut> shortcuts;
    std::wstring focusUp;
    std::wstring focusDown;
    std::wstring focusLeft;
    std::wstring focusRight;
    WidgetComputedStyle baseStyle;
    WidgetComputedStyle focusedStyle;
    WidgetComputedStyle pressedStyle;
    double value{};
    double minimum{};
    double maximum{};
    double step{};
    bool hasProgress{};
    bool hasSliderRange{};
    bool isDisabled{};
    bool isSelected{};
    bool isBusy{};
    std::vector<WidgetNode> children;
};

struct WidgetSurfaceHints final {
    std::wstring mode{L"adaptive"};
    std::optional<double> preferredWidth;
    std::optional<double> preferredHeight;
    std::optional<double> minimumWidth;
    std::optional<double> minimumHeight;
};

struct WidgetSnapshot final {
    long long sequence{};
    std::wstring instanceId;
    std::wstring activeInputScopeId;
    std::wstring initialFocusId;
    std::vector<WidgetQuickAction> quickActions;
    std::optional<WidgetSurfaceHints> surface;
    WidgetNode root;
};

enum class PlatformMotionPreference { System, Full, Reduced };
enum class PlatformContrastPreference { System, Standard, High };
enum class PlatformTransparencyPreference { Full, Reduced };

struct PlatformAppearance final {
    long long revision{};
    std::wstring themeId;
    std::wstring themeVersion;
    double interfaceScale{1.0};
    double textScale{1.0};
    double backdropOpacity{0.64};
    PlatformMotionPreference motion{PlatformMotionPreference::System};
    PlatformContrastPreference contrast{PlatformContrastPreference::System};
    bool boldText{};
    PlatformTransparencyPreference transparency{PlatformTransparencyPreference::Full};
    std::unordered_map<std::wstring, WidgetComputedStyle> shellStyles;
};

class PlatformAppearanceRevisionTracker final {
public:
    [[nodiscard]] bool Notify(long long revision) noexcept;
    [[nodiscard]] std::optional<long long> Take() noexcept;
    [[nodiscard]] const std::optional<long long>& pending() const noexcept { return pending_; }

private:
    std::optional<long long> pending_;
};

/// Tracks the highest catalog revision observed from either an asynchronous
/// change notification or an atomic list response. Replayed/stale events do not
/// cause repeated catalog reconciliation.
class WidgetCatalogRevisionTracker final {
public:
    [[nodiscard]] bool Notify(long long revision) noexcept;
    [[nodiscard]] bool ObserveSnapshot(long long revision) noexcept;
    [[nodiscard]] std::optional<long long> Take() noexcept;
    void Retry() noexcept;
    void Abandon() noexcept;
    void Reset() noexcept;
    [[nodiscard]] bool hasInFlight() const noexcept { return inFlight_.has_value(); }
    [[nodiscard]] const std::optional<long long>& pending() const noexcept { return pending_; }
    [[nodiscard]] long long observed() const noexcept { return observed_; }

private:
    long long observed_{};
    std::optional<long long> pending_;
    std::optional<long long> inFlight_;
};

/// Atomically retains the last accepted appearance. Callers parse into a
/// temporary value first, then publish; rejected or malformed updates cannot
/// partially replace the currently rendered platform state.
class PlatformAppearanceState final {
public:
    [[nodiscard]] bool Publish(PlatformAppearance appearance);
    [[nodiscard]] const std::optional<PlatformAppearance>& current() const noexcept {
        return current_;
    }

private:
    std::optional<PlatformAppearance> current_;
};

class WidgetBridgeClient final {
public:
    WidgetBridgeClient() = default;
    ~WidgetBridgeClient();
    WidgetBridgeClient(const WidgetBridgeClient&) = delete;
    WidgetBridgeClient& operator=(const WidgetBridgeClient&) = delete;

    [[nodiscard]] bool EnsureStarted(
        const std::wstring& installationDirectory,
        const std::wstring& installedCatalogRoot = L"");
    void Stop() noexcept;
    /// Enumerates public widget descriptors without starting widget workers.
    [[nodiscard]] std::optional<std::vector<WidgetDescriptor>> ListWidgets();
    /// Retrieves immutable platform appearance without launching a widget worker.
    [[nodiscard]] std::optional<PlatformAppearance> GetPlatformAppearance();
    /// Coalesced latest revision announced by platform-appearance-changed events.
    [[nodiscard]] std::optional<long long> TakePlatformAppearanceChangedRevision() noexcept;
    /// Coalesced latest catalog revision announced by widget-catalog-changed events.
    [[nodiscard]] std::optional<long long> TakeWidgetCatalogChangedRevision() noexcept;
    /// Requeues an announced revision after a transient list/parse failure.
    void RetryWidgetCatalogChangedRevision() noexcept;
    void AbandonWidgetCatalogChangedRevision() noexcept;
    [[nodiscard]] bool HasWidgetCatalogChangedRevisionInFlight() const noexcept;
    /// Sends the worker's explicit background, visible, or interactive state.
    [[nodiscard]] std::optional<bool> SetWidgetLifecycle(
        std::wstring_view widgetId,
        std::wstring_view state);
    /// Retires the exact current worker registration, clears its cached
    /// snapshot/input authority, and restores its prior host lifecycle.
    [[nodiscard]] std::optional<bool> RestartWidget(std::wstring_view widgetId);
    [[nodiscard]] std::optional<WidgetSnapshot> GetSnapshot(std::wstring_view widgetId);
    [[nodiscard]] std::optional<bool> SendControllerInput(
        std::wstring_view widgetId,
        std::wstring_view button,
        std::wstring_view context,
        std::wstring_view focusedElementId,
        std::wstring_view activeInputScopeId,
        long long snapshotSequence,
        long long sequence,
        long long monotonicTimestampMicroseconds,
        std::wstring_view phase = L"pressed",
        std::optional<double> requestedValue = std::nullopt);
    [[nodiscard]] const std::wstring& lastError() const noexcept { return lastError_; }
    /// Non-blocking UI-thread pump for complete asynchronous bridge events.
    [[nodiscard]] bool PumpEvents();
    [[nodiscard]] std::vector<std::wstring> TakeInvalidatedWidgetIds() noexcept;
    [[nodiscard]] std::vector<WidgetHostEffect> TakeHostEffects() noexcept;

private:
    [[nodiscard]] bool Launch(
        const std::wstring& installationDirectory,
        const std::wstring& installedCatalogRoot);
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
    WidgetInvalidationQueue invalidations_;
    WidgetHostEffectQueue hostEffects_;
    PlatformAppearanceRevisionTracker appearanceChanges_;
    WidgetCatalogRevisionTracker catalogChanges_;
};

} // namespace gba

#ifdef GBA_WIDGET_BRIDGE_CLIENT_TESTING
namespace gba::testing {
[[nodiscard]] std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<PlatformAppearance> ParsePlatformAppearance(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetSnapshot> ParseWidgetSnapshotResponse(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetHostEffect> ParseWidgetHostEffectEvent(
    std::string_view eventUtf8,
    std::wstring& error);
}
#endif
