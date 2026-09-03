#pragma once

#include <Windows.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <mutex>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace widgetrail {

// Describes the exception currently being handled. Implemented in the
// translation unit that already owns winrt so callers can identify a
// winrt::hresult_error without pulling winrt into their own headers.
[[nodiscard]] std::wstring DescribeCurrentException() noexcept;

struct WidgetBridgeReadinessContract final {
    static constexpr DWORD AcceptTimeoutMilliseconds = 10'000;
    static constexpr DWORD PollIntervalMilliseconds = 20;
};

enum class WidgetBridgePipeReadinessStatus {
    Connected,
    ChildExited,
    TimedOut,
};

struct WidgetBridgePipeConnectAttempt final {
    HANDLE pipe{INVALID_HANDLE_VALUE};
    DWORD error{ERROR_SUCCESS};
};

struct WidgetBridgePipeReadinessResult final {
    WidgetBridgePipeReadinessStatus status{WidgetBridgePipeReadinessStatus::TimedOut};
    HANDLE pipe{INVALID_HANDLE_VALUE};
    DWORD error{ERROR_SUCCESS};
};

[[nodiscard]] std::wstring ProjectStartupSettingsFailure(
    std::wstring_view bridgeStartupFailure);

[[nodiscard]] WidgetBridgePipeReadinessResult WaitForWidgetBridgePipeReadiness(
    const std::function<WidgetBridgePipeConnectAttempt()>& tryConnect,
    const std::function<bool()>& childExited,
    const std::function<ULONGLONG()>& currentTick,
    const std::function<void(DWORD)>& wait);

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
    WorkerExited,
};

enum class WidgetBridgeRequestFailureCategory {
    None,
    StalePresentationBase,
};

struct WidgetBridgeRuntimeFailure final {
    std::wstring widgetId;
    WidgetBridgeRuntimeFailureCategory category{
        WidgetBridgeRuntimeFailureCategory::Other};
    std::wstring safeMessage;
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
    std::wstring contentType;
    std::wstring contentBase64;
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
    std::wstring repeatPolicy;
};

struct WidgetShortcut final {
    std::wstring button;
    std::wstring actionId;
    std::wstring phase;
    std::wstring repeatPolicy;
};

struct WidgetContextAction final {
    std::wstring actionId;
    std::wstring label;
    std::wstring style;
    bool isDisabled{};
    bool isBusy{};
};

struct WidgetSelectOption final {
    std::wstring id;
    std::wstring label;
    std::wstring actionId;
    std::wstring glyph;
    std::wstring accessibilityLabel;
    bool isSelected{};
    bool isDisabled{};
    bool isBusy{};
};

struct WidgetStyleValue final {
    std::wstring kind;
    std::wstring text;
    std::optional<double> number;
    std::wstring unit;
};

using WidgetComputedStyle =
    std::unordered_map<std::wstring, WidgetStyleValue>;

enum class VirtualCollectionWindowChange {
    Replace,
    Append,
    Prepend,
};

struct VirtualCollectionWindow final {
    std::uint64_t requestGeneration{};
    VirtualCollectionWindowChange change{VirtualCollectionWindowChange::Replace};
    std::optional<std::uint64_t> firstItemIndex;
    std::optional<std::uint64_t> totalItemCount;
    bool hasBefore{};
    bool hasAfter{};
    double estimatedItemExtent{};
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
    std::wstring textEntryInputKind;
    std::size_t textEntryMaximumLength{};
    std::wstring valueChangedActionId;
    // Optional protocol-v13 identity for mutually exclusive presentations of
    // one logical focus destination. It is never an action-routing key.
    std::wstring focusPersistenceId;
    std::wstring sliderInteractionMode;
    std::wstring imageSource;
    std::wstring artworkHandle;
    // Protocol-v39 trusted artwork selected only while this exact actionable
    // node owns focus inside its nearest opted-in BackgroundSurface.
    std::wstring focusBackgroundArtworkHandle;
    // Protocol-v23 declarative binding from one native layout viewport to the
    // one current embedded-media declaration. It grants no browser authority.
    std::wstring mediaSessionId;
    std::wstring imageFit;
    std::wstring glyph;
    std::wstring indicatorSize;
    // Empty and "always" are equivalent. Protocol-v9 conditional values are
    // interpreted only by the native host against its compact breakpoint.
    std::wstring visibleWhen;
    std::wstring inputScopeId;
    // Protocol-v33 opt-in remembered-child focus group fallback.
    std::wstring initialChildFocusId;
    bool usesFocusedDescendantArtwork{};
    // Protocol-v40 presentation-only subtrees. They are admitted with the
    // snapshot but remain outside ordinary input/action/focus traversal.
    std::vector<WidgetNode> focusPresentation;
    std::vector<WidgetNode> defaultFocusPresentation;
    std::wstring scrollAxis;
    std::wstring scrollNearStartActionId;
    std::wstring scrollNearEndActionId;
    std::size_t scrollPaginationThreshold{};
    std::optional<VirtualCollectionWindow> virtualCollectionWindow;
    std::wstring collectionAnchorKey;
    std::wstring collectionItemKey;
    std::wstring actionSurfaceOrientation;
    // Protocol-v37 closed visual composition for an ActionSurface. Empty and
    // "standard" preserve ordinary child flow; "poster" paints one optional
    // Cover artwork child behind one bounded bottom content subtree.
    std::wstring actionSurfacePresentation;
    std::optional<double> gridMinimumColumnWidth;
    std::optional<std::size_t> gridMaximumColumns;
    std::vector<std::wstring> styleClasses;
    std::vector<WidgetShortcut> shortcuts;
    std::vector<WidgetContextAction> contextActions;
    std::vector<WidgetSelectOption> selectOptions;
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
    bool isSelect{};
    std::vector<WidgetNode> children;
};

struct WidgetSurfaceHints final {
    std::wstring mode{L"adaptive"};
    std::wstring appearance{L"theme"};
    std::optional<std::wstring> widthMode;
    std::optional<std::wstring> heightMode;
    std::optional<double> preferredWidth;
    std::optional<double> preferredHeight;
    std::optional<double> minimumWidth;
    std::optional<double> minimumHeight;
};

struct WidgetPinnedLayout final {
    std::wstring id;
    std::wstring name;
    WidgetSurfaceHints surface;
    std::optional<WidgetNode> root;
    std::wstring activeInputScopeId;
    std::wstring initialFocusId;
};

struct EmbeddedMediaResourceDeclaration final {
    std::wstring path;
    std::wstring contentType;
};

struct EmbeddedMediaPlaybackCommand final {
    long long sequence{};
    std::wstring kind;
    std::wstring mediaKey;
    std::optional<double> positionSeconds;
    std::optional<double> volume;
    std::optional<double> playbackRate;
    std::optional<bool> muted;
    std::optional<bool> loop;
};

struct EmbeddedMediaPlaybackEvent final {
    std::wstring sessionId;
    long long sequence{};
    long long commandSequence{};
    std::wstring mediaKey;
    std::wstring state;
    double positionSeconds{};
    double durationSeconds{};
    double volume{};
    std::wstring errorCode;
    double playbackRate{1.0};
    bool muted{};
    bool loop{};
};

enum class MediaPresentationKind {
    OverlayFullscreen,
    CompactPinned,
};

struct EmbeddedMediaSessionDeclaration final {
    std::wstring id;
    std::wstring accessibleName;
    std::wstring entryAsset;
    WidgetSurfaceHints surface;
    double aspectRatio{};
    std::vector<EmbeddedMediaResourceDeclaration> resources;
    std::vector<std::wstring> commands;
    std::vector<std::wstring> allowedFrameOrigins;
    std::vector<std::wstring> allowedFrameDomainFamilies;
    std::optional<EmbeddedMediaPlaybackCommand> pendingCommand;
    std::vector<MediaPresentationKind> supportedPresentations;
    std::optional<double> mediaSeekStepSeconds;
};

[[nodiscard]] inline bool SupportsMediaPresentation(
    const EmbeddedMediaSessionDeclaration& declaration,
    const MediaPresentationKind presentation) noexcept {
    return std::find(
        declaration.supportedPresentations.begin(),
        declaration.supportedPresentations.end(),
        presentation) != declaration.supportedPresentations.end();
}

struct EmbeddedMediaResource final {
    std::wstring path;
    std::wstring contentType;
    std::wstring sha256;
    std::vector<std::uint8_t> content;
};

struct EmbeddedMediaBundle final {
    std::wstring widgetId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    long long sequence{};
    EmbeddedMediaSessionDeclaration surface;
    std::vector<EmbeddedMediaResource> resources;
};

struct WidgetSnapshot final {
    int protocolVersion{1};
    long long sequence{};
    std::wstring instanceId;
    std::wstring activeInputScopeId;
    std::wstring initialFocusId;
    std::vector<WidgetQuickAction> quickActions;
    std::optional<WidgetSurfaceHints> surface;
    std::vector<WidgetPinnedLayout> pinnedLayouts;
    std::optional<EmbeddedMediaSessionDeclaration> embeddedMediaSession;
    WidgetNode root;
    // Canonical unstyled semantic document retained by the sole native
    // session owner. It is the immutable base for an atomic update candidate;
    // renderer-computed styles remain derived response data.
    std::wstring documentJson;
};

enum class WidgetPresentationUpdateOperationKind {
    SetProperties,
    InsertChild,
    RemoveChild,
    MoveChild,
    ReplaceSubtree,
};

struct WidgetPresentationPropertyChange final {
    std::wstring property;
    std::wstring valueJson;
};

struct WidgetPresentationUpdateOperation final {
    WidgetPresentationUpdateOperationKind kind{
        WidgetPresentationUpdateOperationKind::SetProperties};
    std::wstring targetId;
    std::wstring parentId;
    std::wstring childId;
    std::optional<std::size_t> index;
    std::vector<WidgetPresentationPropertyChange> properties;
    std::wstring subtreeJson;
};

struct WidgetPresentationUpdate final {
    int protocolVersion{};
    std::wstring widgetInstanceId;
    std::wstring presentationGeneration;
    long long baseSequence{};
    long long sequence{};
    std::vector<WidgetPresentationUpdateOperation> operations;
    std::wstring renderStylesJson;
};

enum class WidgetPresentationEffect : std::uint32_t {
    None = 0,
    Authority = 1U << 0,
    Paint = 1U << 1,
    MeasureLayout = 1U << 2,
    Accessibility = 1U << 3,
    Interaction = 1U << 4,
    Resource = 1U << 5,
    SurfacePlacement = 1U << 6,
    Structure = 1U << 7,
    Unknown = 1U << 8,
};

[[nodiscard]] constexpr WidgetPresentationEffect operator|(
    const WidgetPresentationEffect left,
    const WidgetPresentationEffect right) noexcept {
    return static_cast<WidgetPresentationEffect>(
        static_cast<std::uint32_t>(left) |
        static_cast<std::uint32_t>(right));
}

constexpr WidgetPresentationEffect& operator|=(
    WidgetPresentationEffect& left,
    const WidgetPresentationEffect right) noexcept {
    left = left | right;
    return left;
}

[[nodiscard]] constexpr bool HasWidgetPresentationEffect(
    const WidgetPresentationEffect value,
    const WidgetPresentationEffect effect) noexcept {
    return (static_cast<std::uint32_t>(value) &
            static_cast<std::uint32_t>(effect)) != 0;
}

struct WidgetPresentationImpact final {
    long long baseSequence{};
    long long sequence{};
    WidgetPresentationEffect effects{WidgetPresentationEffect::None};
    std::vector<std::wstring> affectedNodeIds;
    /// Exact nodes whose intrinsic text measurement changed. This remains
    /// internal admission metadata; the renderer may downgrade MeasureLayout
    /// only after comparing the new DirectWrite/Taffy result with its complete
    /// committed checkpoint.
    std::vector<std::wstring> textMeasurementNodeIds;
    /// A non-text property also requested layout work, so text equivalence
    /// alone can never justify reusing committed geometry.
    bool hasNonTextMeasureLayout{};
    /// One Select option collection changed. When its host popup is or was
    /// visible, main.cpp promotes the update to a full content raster so the
    /// old host-owned popup pixels cannot survive outside opener damage.
    bool selectOptionsChanged{};
};

[[nodiscard]] constexpr bool RequiresCompleteSelectPopupRaster(
    const WidgetPresentationImpact& impact,
    const bool popupWasOpen,
    const bool popupIsOpen) noexcept {
    return impact.selectOptionsChanged && (popupWasOpen || popupIsOpen);
}

struct WidgetPresentationMaterialization final {
    WidgetSnapshot snapshot;
    WidgetPresentationImpact impact;
};

enum class WidgetPresentationTransactionKind {
    IncrementalUpdate,
    OrdinaryCheckpoint,
    RecoveryCheckpoint,
};

struct WidgetPresentationPublication final {
    WidgetPresentationTransactionKind transactionKind{
        WidgetPresentationTransactionKind::OrdinaryCheckpoint};
    long long requestBaseSequence{};
    long long recoveryOriginSequence{};
    std::optional<WidgetSnapshot> checkpoint;
    std::optional<WidgetPresentationUpdate> update;
};

/// Builds and validates a complete candidate without mutating the admitted
/// checkpoint. The session owner publishes the returned value atomically.
[[nodiscard]] std::optional<WidgetPresentationMaterialization>
MaterializeWidgetPresentationUpdate(
    const WidgetSnapshot& checkpoint,
    const WidgetPresentationUpdate& update,
    std::wstring_view expectedPresentationGeneration,
    std::wstring& error);

enum class PlatformMotionPreference { System, Full, Reduced };
enum class PlatformContrastPreference { System, Standard, High };
enum class PlatformTransparencyPreference { Full, Reduced };
enum class PlatformSurfaceAppearanceOverride { Widget, Theme, Transparent, Solid };

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
    bool animateWidgetSwitching{};
    PlatformSurfaceAppearanceOverride widgetSurfaceAppearance{
        PlatformSurfaceAppearanceOverride::Widget};
    std::unordered_map<std::wstring, PlatformSurfaceAppearanceOverride>
        widgetSurfaceAppearanceOverrides;
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
    /// Atomically establishes a non-background lifecycle and requests either a
    /// cold checkpoint or a publication against the host's exact retained base.
    [[nodiscard]] std::optional<WidgetPresentationPublication>
        EstablishWidgetPresentation(
        std::wstring_view widgetId,
        std::wstring_view state,
        long long baseSequence,
        WidgetPresentationTransactionKind transactionKind,
        long long recoveryOriginSequence);
    /// Retires the exact current worker registration, clears its cached
    /// snapshot/input authority, and restores its prior host lifecycle.
    [[nodiscard]] std::optional<bool> RestartWidget(std::wstring_view widgetId);
    [[nodiscard]] std::optional<WidgetPresentationPublication> GetSnapshot(
        std::wstring_view widgetId,
        long long baseSequence,
        WidgetPresentationTransactionKind transactionKind,
        long long recoveryOriginSequence);
    [[nodiscard]] std::optional<bool> RequestArtwork(
        std::wstring_view widgetId,
        std::wstring_view artworkHandle);
    [[nodiscard]] std::optional<EmbeddedMediaBundle> ResolveEmbeddedMedia(
        std::wstring_view widgetId,
        std::wstring_view instanceId,
        std::wstring_view runtimeGeneration,
        std::wstring_view presentationGeneration,
        long long sequence,
        std::wstring_view sessionId);
    [[nodiscard]] std::optional<bool> PublishEmbeddedMediaPlaybackEvent(
        std::wstring_view widgetId,
        std::wstring_view instanceId,
        std::wstring_view runtimeGeneration,
        std::wstring_view presentationGeneration,
        long long sequence,
        const EmbeddedMediaPlaybackEvent& playbackEvent);
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
        ControllerInputOrigin origin = ControllerInputOrigin::PhysicalController,
        std::wstring_view runtimeGeneration = {},
        std::wstring_view pinnedLayoutId = {},
        std::optional<bool> pinnedLayoutSelected = std::nullopt,
        std::wstring_view expectedActionId = {},
        std::wstring_view expectedSelectOptionActionId = {});
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
    [[nodiscard]] DWORD lastStartupProcessId() const noexcept;
    [[nodiscard]] long long bridgeSessionGeneration() const noexcept;
    /// Bounded classification for the most recent controller-input reply.
    [[nodiscard]] std::wstring lastControllerInputResultCode() const;
    [[nodiscard]] std::optional<WidgetBridgeRuntimeFailureCategory>
        lastRuntimeFailureCategory(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] WidgetBridgeRequestFailureCategory
        lastRequestFailureCategory() const noexcept;
    /// Non-blocking UI-thread pump for complete asynchronous bridge events.
    [[nodiscard]] bool PumpEvents();
    [[nodiscard]] std::vector<WidgetBridgeRuntimeFailure>
        TakeRuntimeFailures() noexcept;
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
    DWORD lastStartupProcessId_{};
    bool transportTainted_{};
    std::wstring pipeName_;
    std::wstring lastError_;
    std::wstring lastControllerInputResultCode_;
    std::optional<WidgetBridgeRuntimeFailure> lastRuntimeFailure_;
    WidgetBridgeRequestFailureCategory lastRequestFailureCategory_{
        WidgetBridgeRequestFailureCategory::None};
    long long nextRequestId_{};
    long long bridgeSessionGeneration_{};
    WidgetInvalidationQueue invalidations_;
    WidgetActionFailureQueue actionFailures_;
    std::vector<WidgetBridgeRuntimeFailure> runtimeFailures_;
    WidgetHostEffectQueue hostEffects_;
    WidgetArtworkResultQueue artworkResults_;
    LocalWidgetPackageInstallResultQueue localPackageInstallResults_;
    PlatformAppearanceRevisionTracker appearanceChanges_;
    WidgetCatalogRevisionTracker catalogChanges_;
    mutable std::recursive_mutex requestMutex_;
};

} // namespace widgetrail

#ifdef WRAIL_WIDGET_BRIDGE_CLIENT_TESTING
namespace widgetrail::testing {
struct BridgeFrameReadResult final {
    std::optional<std::string> frame;
    bool transportTainted{};
    DWORD error{};
};

[[nodiscard]] BridgeFrameReadResult ReadBridgeFrame(HANDLE pipe);
[[nodiscard]] std::string SerializeControllerInputRequest(
    std::wstring_view expectedSelectOptionActionId);
[[nodiscard]] std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<PlatformAppearance> ParsePlatformAppearance(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetSnapshot> ParseWidgetSnapshotResponse(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<EmbeddedMediaBundle> ParseEmbeddedMediaBundleResponse(
    std::string_view payloadUtf8,
    std::wstring_view widgetId,
    std::wstring_view instanceId,
    std::wstring_view runtimeGeneration,
    std::wstring_view presentationGeneration,
    long long sequence,
    std::wstring_view sessionId,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetPresentationUpdate>
ParseWidgetPresentationUpdateResponse(
    std::string_view payloadUtf8,
    std::wstring& error);
[[nodiscard]] std::optional<WidgetPresentationUpdate>
ParseTypedWidgetPresentationUpdateResponse(
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
