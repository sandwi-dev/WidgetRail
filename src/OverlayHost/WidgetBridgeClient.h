#pragma once

#include <Windows.h>

#include "LauncherExperienceLayout.h"

#include <cstddef>
#include <map>
#include <mutex>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace gba {

class ProtectedWifiSecretFrame final {
public:
    static std::optional<ProtectedWifiSecretFrame> Create(
        std::span<const wchar_t> secret);
    ~ProtectedWifiSecretFrame();
    ProtectedWifiSecretFrame(const ProtectedWifiSecretFrame&) = delete;
    ProtectedWifiSecretFrame& operator=(const ProtectedWifiSecretFrame&) = delete;
    ProtectedWifiSecretFrame(ProtectedWifiSecretFrame&& other) noexcept;
    ProtectedWifiSecretFrame& operator=(ProtectedWifiSecretFrame&& other) noexcept;

    [[nodiscard]] std::span<const unsigned char> bytes() const noexcept { return bytes_; }
    void clear() noexcept;

private:
    explicit ProtectedWifiSecretFrame(std::vector<unsigned char>&& bytes) noexcept;
    std::vector<unsigned char> bytes_;
};

struct WidgetDescriptorQuickAction final {
    std::wstring id;
    std::wstring label;
    std::wstring actionId;
    std::wstring sourceElementId;
    std::optional<std::wstring> controllerButton;
};

struct WidgetAdvancedPresentationDeclaration final {
    int schemaVersion{};
    std::wstring kind;
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
    bool pinningSupported{};
    std::optional<WidgetAdvancedPresentationDeclaration> advancedPresentation;
    bool protectedWifiPromptSupported{};
    std::vector<WidgetDescriptorQuickAction> quickActions;
};

/// Resolves one exact private catalog quick-action advertisement. Identifiers
/// remain opaque to the bridge; the native shell assigns meaning only to the
/// IDs it owns (for example, the tray contextual-refresh opt-in).
[[nodiscard]] const WidgetDescriptorQuickAction* FindDescriptorQuickAction(
    const WidgetDescriptor& descriptor,
    std::wstring_view quickActionId) noexcept;

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

enum class WidgetActionFailureCode {
    ControllerActionFailed,
};

[[nodiscard]] constexpr std::wstring_view WidgetActionFailureCodeValue(
    const WidgetActionFailureCode code) noexcept {
    switch (code) {
    case WidgetActionFailureCode::ControllerActionFailed:
        return L"controllerActionFailed";
    }
    return L"controllerActionFailed";
}

struct WidgetActionFailure final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    std::wstring actionId;
    std::wstring sourceElementId;
    WidgetActionFailureCode code{WidgetActionFailureCode::ControllerActionFailed};
};

/// Bounded FIFO for post-admission action failures. Runtime generation is
/// retained so the UI cannot apply a delayed event to a replacement worker.
class WidgetActionFailureQueue final {
public:
    static constexpr std::size_t MaximumFailures = 16;

    [[nodiscard]] bool Push(WidgetActionFailure failure);
    [[nodiscard]] std::vector<WidgetActionFailure> Take() noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return queued_.size(); }

private:
    std::vector<WidgetActionFailure> queued_;
};

enum class WidgetBridgeRuntimeFailureCategory {
    Other,
    WorkerStart,
};

struct WidgetBridgeRuntimeFailure final {
    std::wstring widgetId;
    WidgetBridgeRuntimeFailureCategory category{
        WidgetBridgeRuntimeFailureCategory::Other};
};

enum class WidgetHostEffectKind {
    CloseOverlayAfterAppLaunch,
};

enum class ControllerInputOrigin {
    PhysicalController,
    AccessibilityAutomation,
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

struct WidgetArtworkResult final {
    std::wstring widgetId;
    std::wstring artworkHandle;
    std::wstring pngBase64;
};

struct LocalWidgetPackageInstallOrigin final {
    std::wstring widgetId;
    std::wstring packageId;
    std::wstring publisherId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
};

enum class LocalWidgetPackageInstallStatus { InstalledDisabled, Cancelled, Failed };

struct LocalWidgetPackageInstallResult final {
    std::wstring operationId;
    LocalWidgetPackageInstallStatus status{LocalWidgetPackageInstallStatus::Failed};
    std::wstring widgetId;
    std::wstring version;
    std::wstring safeMessage;
};

class LocalWidgetPackageInstallResultQueue final {
public:
    static constexpr std::size_t MaximumPending = 8;
    [[nodiscard]] bool Push(LocalWidgetPackageInstallResult result);
    [[nodiscard]] std::vector<LocalWidgetPackageInstallResult> Take() noexcept;
    void Reset() noexcept;

private:
    std::vector<LocalWidgetPackageInstallResult> queued_;
};

class WidgetArtworkResultQueue final {
public:
    static constexpr std::size_t MaximumResults = 32;
    [[nodiscard]] bool Push(WidgetArtworkResult result);
    [[nodiscard]] std::vector<WidgetArtworkResult> Take() noexcept;
    void Reset() noexcept { queued_.clear(); }
    [[nodiscard]] std::size_t size() const noexcept { return queued_.size(); }
private:
    std::vector<WidgetArtworkResult> queued_;
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

struct LauncherExperienceSealedAsset final {
    std::wstring opaqueAssetId;
    std::wstring revision;
    std::wstring format;
    std::vector<unsigned char> bytes;
};

/// Complete private settings/catalog projection. It contains no path, URL,
/// action, provider identity, or public protocol value.
struct LauncherExperienceSelection final {
    long long revision{};
    std::wstring id;
    std::wstring version;
    std::wstring contentDigest;
    std::wstring presentationRevision;
    launcher::Preset preset{launcher::Preset::HeroRail};
    bool builtIn{};
    bool followWidgetPreset{};
    bool useGlobalAppearance{};
    std::wstring backgroundMode;
    std::wstring focusEffect;
    std::wstring motionIntensity;
    launcher::Recipe recipe;
    std::map<launcher::Slot, WidgetComputedStyle> packStyles;
    std::optional<LauncherExperienceSealedAsset> packBackground;
    std::wstring diagnostic;
};

enum class LauncherExperienceSelectionOperation {
    SelectExact,
    RecoverBuiltIn,
};

struct LauncherExperienceSelectionRequest final {
    LauncherExperienceSelectionOperation operation{
        LauncherExperienceSelectionOperation::RecoverBuiltIn};
    std::wstring id;
    std::wstring version;
};

struct WidgetNode final {
    std::wstring id;
    std::wstring kind;
    std::wstring text;
    std::wstring accessibilityLabel;
    std::wstring accessibilityValue;
    std::wstring actionId;
    std::wstring textEntryValue;
    std::wstring textEntryPlaceholder;
    std::size_t textEntryMaximumLength{};
    std::wstring valueChangedActionId;
    // Optional protocol-v13 identity for mutually exclusive presentations of
    // one logical focus destination. It is never an action-routing key.
    std::wstring focusPersistenceId;
    std::wstring sliderInteractionMode;
    std::wstring imageSource;
    std::wstring artworkHandle;
    std::wstring imageFit;
    std::wstring glyph;
    std::wstring indicatorSize;
    // Empty and "always" are equivalent. Protocol-v9 conditional values are
    // interpreted only by the native host against its compact breakpoint.
    std::wstring visibleWhen;
    std::wstring inputScopeId;
    std::wstring scrollAxis;
    std::wstring scrollNearStartActionId;
    std::wstring scrollNearEndActionId;
    std::size_t scrollPaginationThreshold{};
    std::wstring collectionAnchorKey;
    std::wstring collectionItemKey;
    std::wstring advancedPresentationSlot;
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
    bool isTextEntry{};
    std::vector<WidgetNode> children;
};

struct WidgetSurfaceHints final {
    std::wstring mode{L"adaptive"};
    std::optional<std::wstring> widthMode;
    std::optional<std::wstring> heightMode;
    std::optional<double> preferredWidth;
    std::optional<double> preferredHeight;
    std::optional<double> minimumWidth;
    std::optional<double> minimumHeight;
};

struct WidgetSnapshot final {
    int protocolVersion{1};
    long long sequence{};
    std::wstring instanceId;
    std::wstring activeInputScopeId;
    std::wstring initialFocusId;
    std::vector<WidgetQuickAction> quickActions;
    std::optional<WidgetSurfaceHints> surface;
    std::wstring advancedPresentationKind;
    std::wstring advancedPresentationPreset;
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
    /// Retrieves one immutable trusted Launcher Experience presentation value
    /// without starting a widget worker.
    [[nodiscard]] std::optional<LauncherExperienceSelection>
        GetLauncherExperience();
    /// Applies one exact installed-package selection or the code-owned built-in
    /// recovery through the private Settings/catalog authority.
    [[nodiscard]] std::optional<LauncherExperienceSelection>
        SelectLauncherExperience(const LauncherExperienceSelectionRequest& request);
    /// Coalesced latest revision announced by platform-appearance-changed events.
    [[nodiscard]] std::optional<long long> TakePlatformAppearanceChangedRevision() noexcept;
    [[nodiscard]] std::optional<long long>
        TakeLauncherExperienceChangedRevision() noexcept;
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
    /// Atomically establishes a non-background lifecycle and admits the exact
    /// generation's first immutable snapshot.
    [[nodiscard]] std::optional<WidgetSnapshot> EstablishWidgetPresentation(
        std::wstring_view widgetId,
        std::wstring_view state);
    /// Retires the exact current worker registration, clears its cached
    /// snapshot/input authority, and restores its prior host lifecycle.
    [[nodiscard]] std::optional<bool> RestartWidget(std::wstring_view widgetId);
    [[nodiscard]] std::optional<WidgetSnapshot> GetSnapshot(std::wstring_view widgetId);
    [[nodiscard]] std::optional<bool> RequestArtwork(
        std::wstring_view widgetId,
        std::wstring_view artworkHandle);
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
        std::optional<double> requestedValue = std::nullopt,
        ControllerInputOrigin origin = ControllerInputOrigin::PhysicalController);
    [[nodiscard]] std::optional<bool> SendAction(
        std::wstring_view widgetId,
        std::wstring_view actionId,
        std::wstring_view sourceElementId,
        std::wstring_view inputScopeId,
        std::optional<std::wstring_view> committedText = std::nullopt);
    [[nodiscard]] std::optional<std::wstring> ConnectProtectedWifi(
        std::wstring_view widgetId,
        std::wstring_view runtimeGeneration,
        std::wstring_view sourceElementId,
        std::span<const wchar_t> secret);
    [[nodiscard]] std::optional<bool> BeginLocalWidgetPackageInstall(
        std::wstring_view packagePath,
        const LocalWidgetPackageInstallOrigin& origin,
        std::wstring_view operationId);
    [[nodiscard]] std::optional<bool> CancelLocalWidgetPackageInstall(
        std::wstring_view operationId);
    [[nodiscard]] std::wstring lastError() const;
    [[nodiscard]] std::optional<WidgetBridgeRuntimeFailureCategory>
        lastRuntimeFailureCategory(std::wstring_view widgetId) const noexcept;
    /// Non-blocking UI-thread pump for complete asynchronous bridge events.
    [[nodiscard]] bool PumpEvents();
    [[nodiscard]] std::vector<std::wstring> TakeInvalidatedWidgetIds() noexcept;
    [[nodiscard]] std::vector<WidgetActionFailure> TakeActionFailures() noexcept;
    [[nodiscard]] std::vector<WidgetHostEffect> TakeHostEffects() noexcept;
    [[nodiscard]] std::vector<WidgetArtworkResult> TakeArtworkResults() noexcept;
    [[nodiscard]] std::vector<LocalWidgetPackageInstallResult>
        TakeLocalWidgetPackageInstallResults() noexcept;

private:
    [[nodiscard]] bool Launch(
        const std::wstring& installationDirectory,
        const std::wstring& installedCatalogRoot);
    [[nodiscard]] bool Connect();
    void CloseTransport() noexcept;
    [[nodiscard]] bool WriteFrame(std::string_view utf8);
    [[nodiscard]] bool WriteProtectedWifiSecret(std::span<const wchar_t> secret);
    [[nodiscard]] std::optional<std::string> ReadFrame();
    void Fail(std::wstring message);

    HANDLE pipe_{INVALID_HANDLE_VALUE};
    HANDLE process_{};
    DWORD processId_{};
    bool transportTainted_{};
    std::wstring pipeName_;
    std::wstring lastError_;
    std::optional<WidgetBridgeRuntimeFailure> lastRuntimeFailure_;
    long long nextRequestId_{};
    WidgetInvalidationQueue invalidations_;
    WidgetActionFailureQueue actionFailures_;
    WidgetHostEffectQueue hostEffects_;
    WidgetArtworkResultQueue artworkResults_;
    LocalWidgetPackageInstallResultQueue localPackageInstallResults_;
    PlatformAppearanceRevisionTracker appearanceChanges_;
    PlatformAppearanceRevisionTracker launcherExperienceChanges_;
    WidgetCatalogRevisionTracker catalogChanges_;
    mutable std::recursive_mutex requestMutex_;
};

} // namespace gba

#ifdef GBA_WIDGET_BRIDGE_CLIENT_TESTING
namespace gba::testing {
struct BridgeFrameReadResult final {
    std::optional<std::string> frame;
    bool transportTainted{};
    DWORD error{};
};

[[nodiscard]] BridgeFrameReadResult ReadBridgeFrame(HANDLE pipe);
[[nodiscard]] std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<PlatformAppearance> ParsePlatformAppearance(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<LauncherExperienceSelection> ParseLauncherExperience(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetSnapshot> ParseWidgetSnapshotResponse(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetHostEffect> ParseWidgetHostEffectEvent(
    std::string_view eventUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetActionFailure> ParseWidgetActionFailureEvent(
    std::string_view eventUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetBridgeRuntimeFailure> ParseWidgetRuntimeFailureEvent(
    std::string_view eventUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetArtworkResult> ParseWidgetArtworkResultEvent(
    std::string_view eventUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<LocalWidgetPackageInstallResult>
ParseLocalWidgetPackageInstallResultEvent(
    std::string_view eventUtf8,
    std::wstring& error);
}
#endif
