#include "OverlayState.h"
#include "../OverlayPlatformInterop/OverlayPlatformInterop.h"
#include "AccessibilityProvider.h"
#include "AccessibilityProjection.h"
#include "AccessibilityTree.h"
#include "DeclarativeRenderer.h"
#include "ControllerNavigation.h"
#include "ControllerInputOwnership.h"
#include "FocusNavigation.h"
#include "HostAccessibility.h"
#include "NativeIcons.h"
#include "NativeStyle.h"
#include "OverlayChrome.h"
#include "OverlayCompositionSurface.h"
#include "OverlayPlacement.h"
#include "OverlayPresentationTransaction.h"
#include "OverlayProcessOwner.h"
#include "OverlayTargeting.h"
#include "OverlayTransition.h"
#include "PressedInteraction.h"
#include "RemoteImageCache.h"
#include "ScrollEvidenceProbe.h"
#include "LocalWidgetPackageImport.h"
#include "LauncherExperienceProjection.h"
#include "LauncherExperienceHostProof.h"
#include "WidgetBridgeClient.h"
#include "WidgetActionFeedback.h"
#include "WidgetAdmissionTrace.h"
#include "WidgetLifecycle.h"
#include "WidgetSessionCoordinator.h"
#include "WidgetSurfaceCoordinator.h"
#include "WidgetSurfaceFocus.h"
#include "SliderInteraction.h"
#include "TextEntryModal.h"
#include "TextEntryActionAdmission.h"
#include "TrayLayout.h"

#include <Windows.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <dwmapi.h>
#include <Roapi.h>
#include <ShellScalingApi.h>
#include <Xinput.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <climits>
#include <filesystem>
#include <fstream>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace {

constexpr wchar_t kWindowClass[] = L"GameBarAlternative.OverlayHost";
constexpr wchar_t kBackdropWindowClass[] = L"GameBarAlternative.Backdrop";
constexpr wchar_t kChromeWindowClass[] = L"GameBarAlternative.Chrome";
constexpr int kPanelWidth = 1180;
constexpr int kDashboardHeight = 180;
constexpr int kWidgetPanelHeight = 700;
constexpr UINT_PTR kControllerTimer = 1;
constexpr UINT_PTR kGuideCompatibilityTimer = 2;
constexpr UINT_PTR kZOrderSettleTimer = 3;
constexpr UINT_PTR kCatalogRetryTimer = 4;
constexpr UINT_PTR kForegroundLossTimer = 5;
constexpr UINT_PTR kActionFeedbackTimer = 6;
constexpr UINT_PTR kPinnedSurfaceTimer = 7;
constexpr UINT kPlatformEventMessage = WM_APP + 1;
constexpr UINT kImageReadyMessage = WM_APP + 2;
constexpr UINT kCatalogRefreshMessage = WM_APP + 3;
constexpr UINT kSnapshotRefreshMessage = WM_APP + 4;
constexpr UINT kWidgetSessionCompletionMessage = WM_APP + 13;
constexpr UINT kForegroundChangedMessage = WM_APP + 5;
constexpr UINT kPlacementRefreshMessage = WM_APP + 6;
constexpr UINT kDisplayRefreshMessage = WM_APP + 7;
constexpr UINT kPerformanceResetMessage = WM_APP + 8;
constexpr UINT kAccessibilityActionMessage = WM_APP + 10;
constexpr UINT kPinnedSurfaceChangedMessage = WM_APP + 11;
constexpr UINT kProcessActivationMessage = WM_APP + 12;
constexpr UINT kDevelopmentTrayYHoldMessage = WM_APP + 14;
constexpr ULONG_PTR kLauncherExperienceSelectionProof = 0x4742414c;

constexpr BYTE kBackdropOpacity = 164;
constexpr int kDeveloperHotkey = 1;
constexpr gba::NativeColor kSafeCanvasFallback{
    1.0F / 255.0F, 2.0F / 255.0F, 3.0F / 255.0F, 1.0F};
constexpr gba::NativeColor kDefaultCanvas{
    0x10 / 255.0F, 0x13 / 255.0F, 0x1A / 255.0F, 1.0F};
constexpr gba::NativeColor kDefaultPanel{
    0x1B / 255.0F, 0x1F / 255.0F, 0x29 / 255.0F, 1.0F};

constexpr gba::NativeColor kDefaultAccent{
    0xFC / 255.0F, 0x3F / 255.0F, 0x6C / 255.0F, 1.0F};

enum class OverlayShowResult {
    Shown,
    Deferred,
    Failed,
};

enum class FixedChromePlacementReason {
    None,
    NewVisibleSession,
    DisplayEnvironment,
    Appearance,
    CatalogOrder,
};

constexpr std::wstring_view FixedChromePlacementReasonName(
    const FixedChromePlacementReason reason) noexcept {
    switch (reason) {
    case FixedChromePlacementReason::NewVisibleSession:
        return L"new-visible-session";
    case FixedChromePlacementReason::DisplayEnvironment:
        return L"display-environment";
    case FixedChromePlacementReason::Appearance:
        return L"appearance";
    case FixedChromePlacementReason::CatalogOrder:
        return L"catalog-order";
    case FixedChromePlacementReason::None:
    default:
        return L"none";
    }
}

constexpr std::uint32_t PlatformBoolean(const bool value) noexcept {
    return value
        ? GBA_OVERLAY_PLATFORM_TRUE
        : GBA_OVERLAY_PLATFORM_FALSE;
}

[[nodiscard]] std::optional<gba::OverlayPlacement> ComputePlatformPlacement(
    const RECT& workArea,
    const UINT dpi,
    const float desiredWidthDip,
    const float desiredHeightDip) noexcept {
    GbaOverlayPlatformPlacementInput input;
    input.workLeft = workArea.left;
    input.workTop = workArea.top;
    input.workRight = workArea.right;
    input.workBottom = workArea.bottom;
    input.dpi = dpi;
    input.desiredWidthDip = desiredWidthDip;
    input.desiredHeightDip = desiredHeightDip;
    GbaOverlayPlatformPlacement placement;
    std::uint32_t hasPlacement = GBA_OVERLAY_PLATFORM_FALSE;
    if (GbaOverlayPlatformComputePlacement(
            &input, &placement, &hasPlacement) !=
            GbaOverlayPlatformStatus::Ok ||
        hasPlacement == GBA_OVERLAY_PLATFORM_FALSE) {
        return std::nullopt;
    }
    return gba::OverlayPlacement{
        placement.x, placement.y, placement.width, placement.height};
}

D2D1_COLOR_F D2DColor(const gba::NativeColor& color) noexcept {
    return D2D1::ColorF(color.red, color.green, color.blue, color.alpha);
}

COLORREF GdiColor(const gba::NativeColor& color) noexcept {
    const auto channel = [](const float value) {
        return static_cast<BYTE>(std::lround(std::clamp(value, 0.0F, 1.0F) * 255.0F));
    };
    return RGB(channel(color.red), channel(color.green), channel(color.blue));
}

struct BuiltInWidget final {
    std::wstring_view id;
    std::wstring_view name;
    gba::icons::NativeIcon icon;
};

constexpr std::array<BuiltInWidget, 3> kBuiltInWidgets{{
    {L"audio-mixer", L"Audio Mixer", gba::icons::NativeIcon::Connection},
    {L"yt-music", L"YT Music", gba::icons::NativeIcon::Music},
    {L"performance", L"Performance", gba::icons::NativeIcon::Warning},
}};

const BuiltInWidget* FindBuiltInWidget(const std::wstring_view id) noexcept {
    const auto found = std::find_if(kBuiltInWidgets.begin(), kBuiltInWidgets.end(),
        [id](const BuiltInWidget& widget) { return widget.id == id; });
    return found == kBuiltInWidgets.end() ? nullptr : &*found;
}

std::wstring_view WidgetName(const std::wstring_view id) noexcept {
    const auto* widget = FindBuiltInWidget(id);
    return widget ? widget->name : id;
}

gba::icons::NativeIcon WidgetIcon(const std::wstring_view id) noexcept {
    const auto* widget = FindBuiltInWidget(id);
    return widget ? widget->icon : gba::icons::NativeIcon::Connection;
}

std::wstring InstallationDirectory() {
    std::array<wchar_t, 32768> path{};
    const DWORD length = GetModuleFileNameW(nullptr, path.data(),
                                            static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) {
        return {};
    }
    return std::filesystem::path(std::wstring_view(path.data(), length))
        .parent_path().wstring();
}

gba::PersistentState LoadPersistentState() {
    gba::PersistentState state;
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return state;
    }

    const std::filesystem::path path =
        std::filesystem::path(localAppData) / L"GameBarAlternative" / L"overlay-state.ini";
    std::wifstream input(path);
    std::wstring first;
    if (!(input >> first)) return {};
    if (first == L"v2") {
        std::size_t count{};
        if (!(input >> count) || count > 256) return {};
        state.order.reserve(count);
        for (std::size_t index = 0; index < count; ++index) {
            std::wstring id;
            if (!(input >> id) || id.empty() || id.size() > 128) return {};
            state.order.push_back(std::move(id));
        }
        std::wstring last;
        int reopen{};
        if (!(input >> last >> reopen)) return {};
        if (last != L"-") state.lastWidget = std::move(last);
        state.reopenWidget = reopen != 0;
        return state;
    }

    // One-time migration from the fixed numeric prototype format.
    try {
        std::array<int, 3> legacy{std::stoi(first), 0, 0};
        int last = -1;
        int reopen = 0;
        if (!(input >> legacy[1] >> legacy[2] >> last)) return {};
        (void)(input >> reopen);
        for (const int index : legacy) {
            if (index < 0 || index >= static_cast<int>(kBuiltInWidgets.size())) return {};
            state.order.emplace_back(kBuiltInWidgets[static_cast<std::size_t>(index)].id);
        }
        if (last >= 0 && last < static_cast<int>(kBuiltInWidgets.size())) {
            state.lastWidget = std::wstring(kBuiltInWidgets[static_cast<std::size_t>(last)].id);
        }
        state.reopenWidget = reopen != 0;
    } catch (...) {
        return {};
    }
    return state;
}

void SavePersistentState(const gba::PersistentState& state) {
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }

    const auto directory = std::filesystem::path(localAppData) / L"GameBarAlternative";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) {
        return;
    }

    const auto path = directory / L"overlay-state.ini";
    const auto temporary = directory / L"overlay-state.tmp";
    {
        std::wofstream output(temporary, std::ios::trunc);
        if (!output) {
            return;
        }
        output << L"v2 " << state.order.size();
        for (const auto& id : state.order) output << L' ' << id;
        output << L' ' << (state.lastWidget ? *state.lastWidget : L"-")
               << L' ' << (state.reopenWidget ? 1 : 0) << L'\n';
    }
    MoveFileExW(temporary.c_str(), path.c_str(),
                MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}

void SaveStartupError(const std::wstring& message) {
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }

    const auto directory = std::filesystem::path(localAppData) / L"GameBarAlternative";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) {
        return;
    }

    std::wofstream output(directory / L"startup-error.log", std::ios::trunc);
    if (output) {
        output << message << L'\n';
    }
}

void ClearStartupError() {
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }
    std::error_code error;
    std::filesystem::remove(
        std::filesystem::path(localAppData) / L"GameBarAlternative" / L"startup-error.log",
        error);
}

void AppendDiagnostic(const std::wstring_view message) {
    static std::mutex logMutex;
    std::lock_guard lock(logMutex);

    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }
    const auto directory = std::filesystem::path(localAppData) / L"GameBarAlternative";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) {
        return;
    }

    SYSTEMTIME now{};
    GetLocalTime(&now);
    std::wofstream output(directory / L"overlay.log", std::ios::app);
    if (output) {
        output << now.wYear << L'-' << now.wMonth << L'-' << now.wDay << L' '
               << now.wHour << L':' << now.wMinute << L':' << now.wSecond << L'.'
               << now.wMilliseconds << L' ' << message << L'\n';
    }
}

class OverlayApp final {
public:
    // The dashboard is catalog-owned. Persisted IDs are reconciled only after
    // the bridge supplies runnable worker descriptors, so an unavailable
    // bridge cannot expose inert native placeholder tiles.
    OverlayApp()
        : state_(LoadPersistentState(), {}),
          actionFailureFeedback_({
              [] { return GetTickCount64(); },
              [this](const std::optional<std::uint64_t> deadline) {
                  ScheduleActionFeedbackExpiry(deadline);
              },
              [this] {
                  if (window_) InvalidateRect(window_, nullptr, FALSE);
              }}),
          admissionTrace_([](std::wstring message) {
              AppendDiagnostic(message);
          }),
          sessions_(
              gba::WidgetSessionOperations{
                  [this](std::stop_token) {
                      return bridge_.EnsureStarted(
                                 installationDirectory_,
                                 developmentCatalogRoot_.value_or(L""))
                          ? gba::WidgetSessionOperationResult<bool>::Success(true)
                          : gba::WidgetSessionOperationResult<bool>::Failure(
                                gba::WidgetSessionFailureStage::Start,
                                bridge_.lastError());
                  },
                  [this](std::stop_token) {
                      auto value = bridge_.ListWidgets();
                      return value
                          ? gba::WidgetSessionOperationResult<
                                std::vector<gba::WidgetDescriptor>>::Success(
                                    std::move(*value))
                          : gba::WidgetSessionOperationResult<
                                std::vector<gba::WidgetDescriptor>>::Failure(
                                    gba::WidgetSessionFailureStage::Catalog,
                                    bridge_.lastError());
                  },
                  [this](std::stop_token, const std::wstring_view widgetId,
                         const gba::WidgetLifecycleState state) {
                      auto value = bridge_.EstablishWidgetPresentation(
                          widgetId, gba::WidgetLifecycleProtocolValue(state));
                      return value
                          ? gba::WidgetSessionOperationResult<gba::WidgetSnapshot>::Success(
                                std::move(*value))
                          : gba::WidgetSessionOperationResult<gba::WidgetSnapshot>::Failure(
                                bridge_.lastRuntimeFailureCategory(widgetId) ==
                                        gba::WidgetBridgeRuntimeFailureCategory::WorkerStart
                                    ? gba::WidgetSessionFailureStage::Start
                                    : gba::WidgetSessionFailureStage::Snapshot,
                                bridge_.lastError());
                  },
                  [this](std::stop_token, const std::wstring_view widgetId,
                         const gba::WidgetLifecycleState state) {
                      auto value = bridge_.SetWidgetLifecycle(
                          widgetId, gba::WidgetLifecycleProtocolValue(state));
                      return value
                          ? gba::WidgetSessionOperationResult<bool>::Success(*value)
                          : gba::WidgetSessionOperationResult<bool>::Failure(
                                gba::WidgetSessionFailureStage::Lifecycle,
                                bridge_.lastError());
                  },
                  [this](std::stop_token, const std::wstring_view widgetId) {
                      auto value = bridge_.GetSnapshot(widgetId);
                      return value
                          ? gba::WidgetSessionOperationResult<gba::WidgetSnapshot>::Success(
                                std::move(*value))
                          : gba::WidgetSessionOperationResult<gba::WidgetSnapshot>::Failure(
                                gba::WidgetSessionFailureStage::Snapshot,
                                bridge_.lastError());
                  },
                  [this](std::stop_token, const std::wstring_view widgetId) {
                      auto value = bridge_.RestartWidget(widgetId);
                      return value
                          ? gba::WidgetSessionOperationResult<bool>::Success(*value)
                          : gba::WidgetSessionOperationResult<bool>::Failure(
                                gba::WidgetSessionFailureStage::Restart,
                                bridge_.lastError());
                  },
              },
              [this] {
                  if (window_)
                      PostMessageW(window_, kWidgetSessionCompletionMessage, 0, 0);
              },
              [this](const gba::WidgetSessionTraceEvent& event) {
                  admissionTrace_.RecordSession(event);
              },
              [] { return static_cast<std::uint64_t>(GetTickCount64()); }),
          localWidgetPackageImport_(
              localWidgetPackagePicker_,
              [this] { return CurrentLocalWidgetPackageImportOrigin(); },
              [this](const std::wstring_view path,
                     const gba::packages::LocalWidgetPackageOrigin& origin,
                     const std::wstring_view operationId) {
                  gba::LocalWidgetPackageInstallOrigin requestOrigin{
                      origin.widgetId,
                      origin.packageId,
                      origin.publisherId,
                      origin.instanceId,
                      origin.runtimeGeneration,
                      origin.presentationGeneration};
                  const auto submitted = bridge_.BeginLocalWidgetPackageInstall(
                      path, requestOrigin, operationId);
                  return submitted.value_or(false);
              }) {}
    ~OverlayApp() { Shutdown(); }

    [[nodiscard]] const std::wstring& initializationError() const noexcept {
        return initializationError_;
    }

    bool Initialize(HINSTANCE instance, int showCommand) {
        ClearStartupError();
        instance_ = instance;
        installationDirectory_ = InstallationDirectory();
        if (installationDirectory_.empty()) {
            return FailWin32(L"GetModuleFileNameW", GetLastError());
        }
        if (!ParseDevelopmentArguments()) return false;
        if (performanceState_) {
            // Performance observations must not inherit or mutate the user's
            // last-open widget, tray order, or reopen preference. The catalog
            // is still the real installed catalog; only presentation state is
            // ephemeral for this process.
            state_ = gba::OverlayState({}, {});
        }
        const HRESULT runtimeResult = RoInitialize(RO_INIT_SINGLETHREADED);
        if (FAILED(runtimeResult) && runtimeResult != RPC_E_CHANGED_MODE) {
            return FailHresult(L"RoInitialize", runtimeResult);
        }
        runtimeInitialized_ = SUCCEEDED(runtimeResult);
        // The packaged build embeds PerMonitorV2 in app.manifest. Keep the
        // runtime declaration as a defense for developer/CMake builds that do
        // not embed that manifest; the older shcore API can only request PMv1.
        // ERROR_ACCESS_DENIED is expected when the manifest already fixed the
        // process awareness before entry-point execution.
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) {
            const DWORD awarenessError = GetLastError();
            if (awarenessError != ERROR_ACCESS_DENIED) {
                AppendDiagnostic(L"Unable to request Per-Monitor-V2 DPI awareness error=" +
                                 std::to_wstring(awarenessError));
            }
        }

        WNDCLASSEXW windowClass{sizeof(windowClass)};
        windowClass.style = CS_HREDRAW | CS_VREDRAW;
        windowClass.lpfnWndProc = WindowProc;
        windowClass.hInstance = instance_;
        windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        // This HWND is a premultiplied DirectComposition target. A class brush
        // is an independent opaque presentation owner and can become visible
        // in uncovered client pixels while a hidden host is reopened. The
        // separate backdrop window remains the only full-monitor black owner.
        windowClass.hbrBackground = nullptr;
        windowClass.lpszClassName = kWindowClass;
        if (!RegisterClassExW(&windowClass)) {
            return FailWin32(L"RegisterClassExW", GetLastError());
        }

        WNDCLASSEXW backdropClass{sizeof(backdropClass)};
        backdropClass.style = CS_HREDRAW | CS_VREDRAW;
        backdropClass.lpfnWndProc = BackdropWindowProc;
        backdropClass.hInstance = instance_;
        backdropClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        backdropClass.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
        backdropClass.lpszClassName = kBackdropWindowClass;
        if (!RegisterClassExW(&backdropClass)) {
            return FailWin32(L"RegisterClassExW(backdrop)", GetLastError());
        }

        WNDCLASSEXW chromeClass{sizeof(chromeClass)};
        chromeClass.style = CS_HREDRAW | CS_VREDRAW;
        chromeClass.lpfnWndProc = ChromeWindowProc;
        chromeClass.hInstance = instance_;
        chromeClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        chromeClass.hbrBackground = nullptr;
        chromeClass.lpszClassName = kChromeWindowClass;
        if (!RegisterClassExW(&chromeClass)) {
            return FailWin32(L"RegisterClassExW(chrome)", GetLastError());
        }

        window_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST,
            kWindowClass,
            L"Game Bar Alternative",
            WS_POPUP,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            kPanelWidth,
            kWidgetPanelHeight,
            nullptr,
            nullptr,
            instance_,
            this);
        if (!window_) {
            return FailWin32(L"CreateWindowExW", GetLastError());
        }
        accessibilityProvider_.Bind(window_, kAccessibilityActionMessage);
        chromeWindow_ = CreateWindowExW(
            gba::shell::FixedChromeWindowExStyle(),
            kChromeWindowClass,
            L"Game Bar Alternative chrome",
            gba::shell::FixedChromeWindowStyle(),
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            1,
            1,
            window_,
            nullptr,
            instance_,
            this);
        if (!chromeWindow_) {
            return FailWin32(L"CreateWindowExW(chrome)", GetLastError());
        }
        chromeAccessibilityProvider_.Bind(chromeWindow_, kAccessibilityActionMessage);
        backdropWindow_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
            kBackdropWindowClass,
            L"Game Bar Alternative backdrop",
            WS_POPUP,
            0,
            0,
            1,
            1,
            nullptr,
            nullptr,
            instance_,
            this);
        if (!backdropWindow_) {
            return FailWin32(L"CreateWindowExW(backdrop)", GetLastError());
        }
        GbaOverlayPlatformCreateOptions platformOptions;
        platformOptions.callbackContext = this;
        platformOptions.eventAvailable = OnPlatformEventAvailable;
        platformOptions.diagnostic = OnPlatformDiagnostic;
        if (GbaOverlayPlatformCreate(&platformOptions, &platform_) !=
            GbaOverlayPlatformStatus::Ok) {
            initializationError_ =
                L"Unable to create the versioned native platform boundary.";
            return false;
        }
        (void)GbaOverlayPlatformSetOwnedWindows(
            platform_,
            reinterpret_cast<std::uintptr_t>(window_),
            reinterpret_cast<std::uintptr_t>(backdropWindow_));
        SetLayeredWindowAttributes(backdropWindow_, 0, kBackdropOpacity, LWA_ALPHA);
        const BOOL disableTransitions = TRUE;
        const BOOL excludeFromPeek = TRUE;
        (void)DwmSetWindowAttribute(window_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(window_, DWMWA_EXCLUDED_FROM_PEEK,
                                    &excludeFromPeek, sizeof(excludeFromPeek));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_EXCLUDED_FROM_PEEK,
                                    &excludeFromPeek, sizeof(excludeFromPeek));
        foregroundEventApp_ = this;
        foregroundHook_ = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            nullptr,
            OnForegroundChanged,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (!foregroundHook_) {
            AppendDiagnostic(L"SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed error=" +
                             std::to_wstring(GetLastError()));
        }

        const HRESULT d2dResult = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED,
            IID_PPV_ARGS(d2dFactory_.ReleaseAndGetAddressOf()));
        if (FAILED(d2dResult)) {
            return FailHresult(L"D2D1CreateFactory", d2dResult);
        }
        std::wstring compositionError;
        if (gba::shell::InitializeFixedChromeComposition(
                compositionSurface_, window_, chromeWindow_,
                d2dFactory_.Get(), compositionError)) {
            // A premultiplied composition target carries transparency in its
            // alpha channel. Drop the legacy HWND color key and use full
            // authored opacity; the separate backdrop remains the only dimmer.
            appliedOverlayOpacity_.reset();
            AppendDiagnostic(
                L"DirectComposition complete-content presentation owner active "
                L"alpha=premultiplied-clear hwnd=no-redirection "
                L"opacity=composition-effect hwnd-background=none");
        } else {
            EnableLegacyLayeredFallback();
            AppendDiagnostic(
                L"DirectComposition unavailable; retaining HWND render-target fallback: " +
                compositionError);
        }
        const HRESULT dwriteResult = DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(writeFactory_.ReleaseAndGetAddressOf()));
        if (FAILED(dwriteResult)) {
            return FailHresult(L"DWriteCreateFactory", dwriteResult);
        }

        imageCache_ = std::make_unique<gba::RemoteImageCache>(
            gba::RemoteImageLimits{},
            [this](const std::wstring_view source, const gba::RemoteImageState state) {
                constexpr std::wstring_view prefix = L"gbar-artwork\x1f";
                if (state == gba::RemoteImageState::Failed && source.starts_with(prefix)) {
                    const auto widgetEnd = source.find(L'\x1f', prefix.size());
                    const auto handleStart = source.rfind(L'\x1f');
                    if (widgetEnd != std::wstring_view::npos &&
                        handleStart != std::wstring_view::npos &&
                        handleStart > widgetEnd) {
                        AppendDiagnostic(
                            L"Trusted artwork unavailable widget=" +
                            std::wstring(source.substr(
                                prefix.size(), widgetEnd - prefix.size())) +
                            L" handle=" + std::wstring(source.substr(handleStart + 1)) +
                            L" state=terminal");
                    }
                }
                if (window_) PostMessageW(window_, kImageReadyMessage, 0, 0);
            },
            gba::RemoteImageCache::FetchFunction{},
            [this](std::wstring_view source) {
                constexpr std::wstring_view prefix = L"gbar-artwork\x1f";
                const auto widgetSeparator = source.find(L'\x1f', prefix.size());
                const auto handleSeparator = source.rfind(L'\x1f');
                if (!source.starts_with(prefix) || widgetSeparator == std::wstring_view::npos ||
                    handleSeparator == widgetSeparator)
                    return false;
                const auto widgetId = source.substr(
                    prefix.size(), widgetSeparator - prefix.size());
                const auto handle = source.substr(handleSeparator + 1);
                return bridge_.RequestArtwork(widgetId, handle).value_or(false);
            });
        declarativeRenderer_ = std::make_unique<gba::DeclarativeRenderer>(
            d2dFactory_.Get(), writeFactory_.Get(), imageCache_.get());
        std::wstring pinnedSurfaceError;
        if (!pinnedSurfaceCoordinator_.Initialize(
                instance_, window_, kPinnedSurfaceChangedMessage,
                d2dFactory_.Get(), writeFactory_.Get(), imageCache_.get(),
                pinnedSurfaceError)) {
            initializationError_ = std::move(pinnedSurfaceError);
            return false;
        }
        if (!developmentProbeOnly_) {
            RegisterHotKey(window_, kDeveloperHotkey, MOD_NOREPEAT, VK_F1);
            if (GbaOverlayPlatformInitialize(platform_) !=
                GbaOverlayPlatformStatus::Ok) {
                initializationError_ =
                    L"Unable to initialize the native platform boundary.";
                return false;
            }
            if (GbaOverlayPlatformRequiresLegacyGuidePolling(platform_) !=
                GBA_OVERLAY_PLATFORM_FALSE) {
                SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
            }
        }

        // Platform appearance is bridge-owned but does not cross the lazy
        // widget-worker boundary. Fetch it once at host startup, then only in
        // response to a revision event.
        bool developmentCatalogReady = false;
        if (bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            RefreshPlatformAppearance();
            RefreshLauncherExperience();
            if (auto change = sessions_.EstablishCatalog()) {
                ApplyWidgetCatalogChange(*change);
                developmentCatalogReady = true;
            }
        } else {
            AppendDiagnostic(L"Platform appearance unavailable at startup: " +
                             bridge_.lastError());
        }
        if (developmentReadyPath_) {
            if (!developmentCatalogReady || !ExpectedDevelopmentWidgetPresent()) {
                initializationError_ =
                    L"Development bridge catalog did not contain the expected package generation.";
                return false;
            }
            if (!ProbeExpectedDevelopmentWidget()) return false;
            if (developmentProbeOnly_ && !PublishDevelopmentReady()) return false;
        }

        (void)showCommand;
        if (performanceState_) {
            if (!ApplyPerformanceStartupState()) return false;
            return true;
        }
        bool startShown = false;
        for (int i = 1; i < __argc; ++i) {
            if (_wcsicmp(__wargv[i], L"--show") == 0) {
                startShown = true;
            } else if (_wcsicmp(__wargv[i], L"--hidden") == 0) {
                startShown = false;
            }
        }
        if (startShown && !developmentProbeOnly_) {
            Dispatch(gba::Command::ToggleOverlay);
        }
        if (developmentReadyPath_ && !developmentProbeOnly_) {
            if (!startShown) {
                initializationError_ =
                    L"An interactive development readiness handshake requires --show.";
                return false;
            }
            if (!InteractiveDevelopmentHostReady()) return false;
            if (!PublishDevelopmentReady()) return false;
        }
        return true;
    }

    int Run() {
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        return static_cast<int>(message.wParam);
    }

    void BindProcessActivation(gba::process::OverlayProcessOwner& owner) noexcept {
        owner.BindNotificationWindow(window_, kProcessActivationMessage);
    }

private:
    bool ParseDevelopmentArguments() {
        auto takeValue = [&](const int& index, std::optional<std::wstring>& target,
                             const wchar_t* label) -> bool {
            if (target || index + 1 >= __argc || !__wargv[index + 1] ||
                __wargv[index + 1][0] == L'\0' ||
                wcsncmp(__wargv[index + 1], L"--", 2) == 0) {
                initializationError_ = std::wstring(label) + L" requires one value.";
                return false;
            }
            target = __wargv[index + 1];
            return true;
        };
        for (int i = 1; i < __argc; ++i) {
            if (_wcsicmp(__wargv[i], L"--development-catalog-root") == 0) {
                if (!takeValue(i, developmentCatalogRoot_, L"--development-catalog-root")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-ready-path") == 0) {
                if (!takeValue(i, developmentReadyPath_, L"--development-ready-path")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-ready-nonce") == 0) {
                if (!takeValue(i, developmentReadyNonce_, L"--development-ready-nonce")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-widget-id") == 0) {
                if (!takeValue(i, developmentWidgetId_, L"--development-widget-id")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-widget-instance") == 0) {
                if (!takeValue(i, developmentWidgetInstance_, L"--development-widget-instance")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-probe-only") == 0) {
                if (developmentProbeOnly_) {
                    initializationError_ = L"--development-probe-only was supplied more than once.";
                    return false;
                }
                developmentProbeOnly_ = true;
            } else if (_wcsicmp(__wargv[i], L"--performance-state") == 0) {
                if (!takeValue(i, performanceState_, L"--performance-state")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--performance-widget-id") == 0) {
                if (!takeValue(i, performanceWidgetId_, L"--performance-widget-id")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--performance-diagnostics-path") == 0) {
                if (!takeValue(i, performanceDiagnosticsPath_, L"--performance-diagnostics-path")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--performance-diagnostics-nonce") == 0) {
                if (!takeValue(i, performanceDiagnosticsNonce_, L"--performance-diagnostics-nonce")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--scroll-evidence-path") == 0) {
                if (!takeValue(i, scrollEvidencePath_, L"--scroll-evidence-path")) return false;
                ++i;
            }
        }
        try {
            if (developmentCatalogRoot_)
                developmentCatalogRoot_ = std::filesystem::absolute(*developmentCatalogRoot_).wstring();
            if (developmentReadyPath_)
                developmentReadyPath_ = std::filesystem::absolute(*developmentReadyPath_).wstring();
            if (performanceDiagnosticsPath_)
                performanceDiagnosticsPath_ =
                    std::filesystem::absolute(*performanceDiagnosticsPath_).wstring();
            if (scrollEvidencePath_)
                scrollEvidencePath_ =
                    std::filesystem::absolute(*scrollEvidencePath_).wstring();
        } catch (const std::filesystem::filesystem_error&) {
            initializationError_ = L"A development path is invalid.";
            return false;
        }
        const bool hasHandshake = developmentReadyPath_ || developmentReadyNonce_ ||
                                  developmentWidgetId_ || developmentWidgetInstance_;
        if (hasHandshake && (!developmentCatalogRoot_ || !developmentReadyPath_ ||
                             !developmentReadyNonce_ || !developmentWidgetId_ ||
                             !developmentWidgetInstance_)) {
            initializationError_ = L"Development readiness arguments must be supplied together.";
            return false;
        }
        if (developmentProbeOnly_ && !hasHandshake) {
            initializationError_ = L"A development probe requires an authenticated readiness handshake.";
            return false;
        }
        if (scrollEvidencePath_ && !hasHandshake) {
            initializationError_ =
                L"Scroll evidence requires an authenticated development readiness handshake.";
            return false;
        }
        if (developmentReadyNonce_ &&
            (developmentReadyNonce_->size() != 64 ||
             !std::all_of(developmentReadyNonce_->begin(), developmentReadyNonce_->end(),
                          [](const wchar_t value) {
                              return (value >= L'0' && value <= L'9') ||
                                     (value >= L'a' && value <= L'f') ||
                                     (value >= L'A' && value <= L'F');
                          }))) {
            initializationError_ = L"The development readiness nonce is invalid.";
            return false;
        }
        const auto validIdentity = [](const std::optional<std::wstring>& value) {
            return value && !value->empty() && value->size() <= 128 &&
                   std::all_of(value->begin(), value->end(), [](const wchar_t character) {
                       return (character >= L'a' && character <= L'z') ||
                              (character >= L'A' && character <= L'Z') ||
                              (character >= L'0' && character <= L'9') ||
                              character == L'-' || character == L'_' || character == L'.';
                   });
        };
        if (hasHandshake &&
            (!validIdentity(developmentWidgetId_) || !validIdentity(developmentWidgetInstance_))) {
            initializationError_ = L"The expected development widget identity is invalid.";
            return false;
        }
        const bool hasPerformanceArguments = performanceState_ || performanceWidgetId_ ||
                                             performanceDiagnosticsPath_ ||
                                             performanceDiagnosticsNonce_;
        if (hasPerformanceArguments &&
            (!performanceState_ || !performanceWidgetId_ ||
             !performanceDiagnosticsPath_ || !performanceDiagnosticsNonce_)) {
            initializationError_ =
                L"Performance diagnostics arguments must be supplied together.";
            return false;
        }
        if (hasPerformanceArguments && (hasHandshake || developmentProbeOnly_)) {
            initializationError_ =
                L"Performance diagnostics cannot be combined with development readiness.";
            return false;
        }
        if (performanceState_) {
            std::transform(performanceState_->begin(), performanceState_->end(),
                           performanceState_->begin(), towlower);
            if (*performanceState_ != L"hidden" && *performanceState_ != L"visible" &&
                *performanceState_ != L"interactive" &&
                *performanceState_ != L"safe-start") {
                initializationError_ =
                    L"--performance-state must be hidden, visible, interactive, or safe-start.";
                return false;
            }
            if (!validIdentity(performanceWidgetId_)) {
                initializationError_ = L"The performance widget identity is invalid.";
                return false;
            }
            if (performanceDiagnosticsNonce_->size() != 64 ||
                !std::all_of(performanceDiagnosticsNonce_->begin(),
                             performanceDiagnosticsNonce_->end(),
                             [](const wchar_t value) {
                                 return (value >= L'0' && value <= L'9') ||
                                        (value >= L'a' && value <= L'f') ||
                                        (value >= L'A' && value <= L'F');
                             })) {
                initializationError_ = L"The performance diagnostics nonce is invalid.";
                return false;
            }
        }
        if (scrollEvidencePath_) {
            if (!scrollEvidenceProbe_.Enable(*scrollEvidencePath_)) {
                initializationError_ = L"The scroll evidence destination is invalid.";
                return false;
            }
            scrollEvidencePath_.reset();
        }
        return true;
    }

    bool ApplyPerformanceStartupState() {
        if (!performanceState_ || !performanceWidgetId_) return false;
        if (*performanceState_ == L"hidden") return true;
        if (state_.order().empty()) {
            initializationError_ = L"Performance diagnostics found no runnable widgets.";
            return false;
        }

        Dispatch(gba::Command::ToggleOverlay);
        if (state_.surface() == gba::Surface::Widget &&
            state_.focusRegion() == gba::FocusRegion::Widget) {
            Dispatch(gba::Command::SampleWidgetBack);
        }
        for (std::size_t remaining = state_.order().size();
             remaining > 0 && state_.selectedWidget() != *performanceWidgetId_;
             --remaining) {
            Dispatch(gba::Command::NavigateRight);
        }
        if (state_.selectedWidget() != *performanceWidgetId_) {
            initializationError_ = L"The requested performance widget is not installed and enabled.";
            return false;
        }
        if (*performanceState_ == L"interactive" ||
            *performanceState_ == L"safe-start") {
            // The bounded performance fixture enters through the same
            // one-shot activation state as the physical LT+RT+A gesture. It
            // does not change selection or add an action/protocol route.
            launcherSafeStartPending_ = *performanceState_ == L"safe-start";
            Dispatch(gba::Command::Activate);
        }

        const auto expected = *performanceState_ == L"interactive" ||
                              *performanceState_ == L"safe-start"
            ? gba::WidgetLifecycleState::Interactive
            : gba::WidgetLifecycleState::Visible;
        const auto deadline = std::chrono::steady_clock::now() +
            std::chrono::seconds(5);
        do {
            ProcessWidgetSessionEvents();
            const auto lifecycle = sessions_.Lifecycle(*performanceWidgetId_);
            if (lifecycle && *lifecycle == expected) return true;
            Sleep(5);
        } while (std::chrono::steady_clock::now() < deadline);
        ProcessWidgetSessionEvents();
        const auto lifecycle = sessions_.Lifecycle(*performanceWidgetId_);
        if (!lifecycle || *lifecycle != expected) {
            initializationError_ =
                L"The requested performance lifecycle could not be established.";
            return false;
        }
        return true;
    }

    void ResetPerformanceCounters() noexcept {
        if (!performanceState_) return;
        performanceTimerMessages_ = 0;
        performanceControllerTimerMessages_ = 0;
        performanceGuideCompatibilityTimerMessages_ = 0;
        performancePaintMessages_ = 0;
        performanceSuccessfulFrames_ = 0;
        LARGE_INTEGER now{};
        performanceCountersActive_ = QueryPerformanceCounter(&now) != FALSE;
        performanceCounterStarted_ = performanceCountersActive_
            ? static_cast<unsigned long long>(now.QuadPart)
            : 0;
    }

    bool PublishPerformanceCounters() {
        if (!performanceCountersActive_ || !performanceState_ ||
            !performanceWidgetId_ || !performanceDiagnosticsPath_ ||
            !performanceDiagnosticsNonce_) {
            return false;
        }
        LARGE_INTEGER ended{};
        LARGE_INTEGER frequency{};
        if (!QueryPerformanceCounter(&ended) || !QueryPerformanceFrequency(&frequency) ||
            ended.QuadPart < 0 || frequency.QuadPart <= 0) {
            return false;
        }
        const auto ascii = [](const std::wstring& value) {
            std::string result;
            result.reserve(value.size());
            for (const wchar_t character : value) {
                result.push_back(static_cast<char>(character));
            }
            return result;
        };
        const std::string payload =
            "gbar-performance-runtime-v2\n" +
            ascii(*performanceDiagnosticsNonce_) + "\n" +
            ascii(*performanceState_) + "\n" +
            ascii(*performanceWidgetId_) + "\n" +
            std::to_string(performanceCounterStarted_) + "\n" +
            std::to_string(static_cast<unsigned long long>(ended.QuadPart)) + "\n" +
            std::to_string(static_cast<unsigned long long>(frequency.QuadPart)) + "\n" +
            std::to_string(performanceTimerMessages_) + "\n" +
            std::to_string(performanceControllerTimerMessages_) + "\n" +
            std::to_string(performanceGuideCompatibilityTimerMessages_) + "\n" +
            std::to_string(performancePaintMessages_) + "\n" +
            std::to_string(performanceSuccessfulFrames_) + "\n";

        const std::filesystem::path destination(*performanceDiagnosticsPath_);
        const auto temporary = destination.wstring() + L".tmp-" +
                               std::to_wstring(GetCurrentProcessId());
        HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr,
                                  CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        DWORD written = 0;
        const bool wrote = payload.size() <= MAXDWORD &&
                           WriteFile(file, payload.data(),
                                     static_cast<DWORD>(payload.size()),
                                     &written, nullptr) &&
                           written == static_cast<DWORD>(payload.size()) &&
                           FlushFileBuffers(file);
        CloseHandle(file);
        if (!wrote || !MoveFileExW(temporary.c_str(), destination.c_str(),
                                   MOVEFILE_WRITE_THROUGH)) {
            DeleteFileW(temporary.c_str());
            return false;
        }
        performanceCountersActive_ = false;
        return true;
    }

    bool ExpectedDevelopmentWidgetPresent() const noexcept {
        if (!developmentWidgetId_ || !developmentWidgetInstance_) return false;
        return std::any_of(sessions_.descriptors().begin(), sessions_.descriptors().end(),
                           [&](const gba::WidgetDescriptor& descriptor) {
                               return descriptor.id == *developmentWidgetId_ &&
                                      descriptor.instanceId == *developmentWidgetInstance_;
                           });
    }

    bool ProbeExpectedDevelopmentWidget() {
        if (!developmentWidgetId_ || !developmentWidgetInstance_) return false;
        auto snapshot = sessions_.EstablishPresentationForProbe(
            *developmentWidgetId_, gba::WidgetLifecycleState::Visible);
        if (!snapshot) {
            initializationError_ =
                L"Development widget worker did not complete the visible snapshot probe.";
            return false;
        }
        if (snapshot->instanceId != *developmentWidgetInstance_) {
            initializationError_ =
                L"Development widget worker returned a snapshot for the wrong package generation.";
            return false;
        }
        return true;
    }

    bool InteractiveDevelopmentHostReady() {
        if (state_.surface() == gba::Surface::Hidden || !IsWindow(window_) ||
            !IsWindow(backdropWindow_) || !IsWindowVisible(window_) ||
            !IsWindowVisible(backdropWindow_)) {
            initializationError_ =
                L"Development overlay did not complete its visible window transition.";
            return false;
        }
        if (!sessions_.EstablishCatalog()) {
            initializationError_ =
                L"Development bridge did not remain responsive after showing the overlay.";
            return false;
        }
        if (!std::any_of(sessions_.descriptors().begin(), sessions_.descriptors().end(),
                         [&](const gba::WidgetDescriptor& descriptor) {
                             return descriptor.id == *developmentWidgetId_ &&
                                    descriptor.instanceId == *developmentWidgetInstance_;
                         })) {
            initializationError_ =
                L"Development bridge changed package generation while showing the overlay.";
            return false;
        }
        return true;
    }

    bool PublishDevelopmentReady() {
        const std::wstring widePayload =
            L"gbar-dev-ready-v1\n" + *developmentReadyNonce_ + L"\n" +
            *developmentCatalogRoot_ + L"\n" + *developmentWidgetId_ + L"\n" +
            *developmentWidgetInstance_ + L"\n";
        if (widePayload.size() > static_cast<std::size_t>(INT_MAX)) {
            initializationError_ = L"The development readiness record is too large.";
            return false;
        }
        const int utf8Length = WideCharToMultiByte(
            CP_UTF8, WC_ERR_INVALID_CHARS, widePayload.data(),
            static_cast<int>(widePayload.size()), nullptr, 0, nullptr, nullptr);
        if (utf8Length <= 0) {
            initializationError_ = L"Could not encode the development readiness record.";
            return false;
        }
        std::string payload(static_cast<std::size_t>(utf8Length), '\0');
        if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, widePayload.data(),
                                static_cast<int>(widePayload.size()), payload.data(), utf8Length,
                                nullptr, nullptr) != utf8Length) {
            initializationError_ = L"Could not encode the development readiness record.";
            return false;
        }
        const std::filesystem::path destination(*developmentReadyPath_);
        const auto temporary = destination.wstring() + L".tmp-" +
                               std::to_wstring(GetCurrentProcessId());
        HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                                  FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            initializationError_ = L"Could not create the development readiness record.";
            return false;
        }
        DWORD written = 0;
        const bool wrote = payload.size() <= MAXDWORD &&
                           WriteFile(file, payload.data(), static_cast<DWORD>(payload.size()),
                                     &written, nullptr) &&
                           written == static_cast<DWORD>(payload.size()) &&
                           FlushFileBuffers(file);
        CloseHandle(file);
        if (!wrote || !MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_WRITE_THROUGH)) {
            DeleteFileW(temporary.c_str());
            initializationError_ = L"Could not publish the development readiness record.";
            return false;
        }
        return true;
    }

    bool FailWin32(const std::wstring_view operation, const DWORD error) {
        initializationError_ = std::wstring(operation) + L" failed with Win32 error " +
                               std::to_wstring(error) + L".";
        return false;
    }

    bool FailHresult(const std::wstring_view operation, const HRESULT result) {
        initializationError_ = std::wstring(operation) + L" failed with HRESULT " +
                               std::to_wstring(static_cast<unsigned long>(result)) + L".";
        return false;
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        OverlayApp* app = nullptr;
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            app = static_cast<OverlayApp*>(create->lpCreateParams);
            app->window_ = window;
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
        } else {
            app = reinterpret_cast<OverlayApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        }
        return app ? app->HandleMessage(message, wParam, lParam)
                   : DefWindowProcW(window, message, wParam, lParam);
    }

    static LRESULT CALLBACK BackdropWindowProc(
        HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        OverlayApp* app = nullptr;
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            app = static_cast<OverlayApp*>(create->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
        } else {
            app = reinterpret_cast<OverlayApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        }
        if (!app) return DefWindowProcW(window, message, wParam, lParam);
        switch (message) {
        case WM_LBUTTONDOWN:
        case WM_LBUTTONUP:
            if (message == WM_LBUTTONUP && app->state_.surface() != gba::Surface::Hidden) {
                app->Dispatch(gba::Command::ToggleOverlay);
            }
            return 0;
        case WM_ERASEBKGND:
            return 1;
        case WM_PAINT: {
            PAINTSTRUCT paint{};
            const HDC dc = BeginPaint(window, &paint);
            FillRect(dc, &paint.rcPaint,
                     app->backdropBrush_ ? app->backdropBrush_
                                         : static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
            EndPaint(window, &paint);
            return 0;
        }
        default:
            return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    static LRESULT CALLBACK ChromeWindowProc(
        HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        OverlayApp* app = nullptr;
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            app = static_cast<OverlayApp*>(create->lpCreateParams);
            app->chromeWindow_ = window;
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
        } else {
            app = reinterpret_cast<OverlayApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        }
        if (!app) return DefWindowProcW(window, message, wParam, lParam);
        switch (message) {
        case WM_NCHITTEST: {
            POINT point{static_cast<LONG>(static_cast<short>(LOWORD(lParam))),
                        static_cast<LONG>(static_cast<short>(HIWORD(lParam)))};
            if (!app->compositionChromeSession_) return HTTRANSPARENT;
            const auto guide = app->ProjectChromeClientBoundsToScreen(
                app->compositionChromeSession_->guideClientBounds);
            const auto tray = app->ProjectChromeClientBoundsToScreen(
                app->compositionChromeSession_->trayClientBounds);
            if (!guide || !tray ||
                !gba::shell::IsFixedChromeHit(point, *guide, *tray))
                return HTTRANSPARENT;
            return HTCLIENT;
        }
        case WM_ERASEBKGND:
            return 1;
        case WM_PAINT: {
            PAINTSTRUCT paint{};
            BeginPaint(window, &paint);
            EndPaint(window, &paint);
            return 0;
        }
        case WM_LBUTTONUP: {
            (void)gba::shell::RouteFixedChromePointerRelease(
                window, app->window_, lParam, app,
                [](void* context, const float x, const float y) noexcept {
                    static_cast<OverlayApp*>(context)->HandlePointerActivation(x, y);
                });
            return 0;
        }
        case WM_SHOWWINDOW:
            app->chromeAccessibilityProvider_.SetWindowVisible(wParam != FALSE);
            return DefWindowProcW(window, message, wParam, lParam);
        case WM_GETOBJECT:
            if (static_cast<LONG>(lParam) == UiaRootObjectId) {
                if (!app->accessibilityActive_) {
                    app->accessibilityActive_ = true;
                    InvalidateRect(app->window_, nullptr, FALSE);
                    UpdateWindow(app->window_);
                }
                return app->chromeAccessibilityProvider_.HandleWmGetObject(wParam, lParam);
            }
            return DefWindowProcW(window, message, wParam, lParam);
        case kAccessibilityActionMessage:
            app->HandleAccessibilityActions();
            return 0;
        default:
            return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    static void GBA_OVERLAY_PLATFORM_CALL OnPlatformEventAvailable(
        void* context) noexcept {
        const auto app = static_cast<OverlayApp*>(context);
        if (app && app->window_) {
            (void)PostMessageW(app->window_, kPlatformEventMessage, 0, 0);
        }
    }

    static void GBA_OVERLAY_PLATFORM_CALL OnPlatformDiagnostic(
        void*,
        const wchar_t* message) noexcept {
        if (message) AppendDiagnostic(message);
    }

    void HandlePlatformEvent(const GbaOverlayPlatformEvent& event) {
        switch (event.kind) {
        case GbaOverlayPlatformEventKind::GuideToggleRequested:
            if (event.guideSource ==
                GbaOverlayPlatformGuideSource::LegacyCompatibility) {
                AppendDiagnostic(
                    L"Guide press received from quarantined XInput compatibility adapter; slots=" +
                    std::to_wstring(event.value));
            }
            AppendDiagnostic(L"Guide toggle dispatched on window thread");
            Dispatch(gba::Command::ToggleOverlay);
            break;
        case GbaOverlayPlatformEventKind::LegacyGuidePollingChanged:
            if (event.value != 0) {
                SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
                AppendDiagnostic(
                    L"XInput Guide compatibility polling activated for a connected Xbox 360-family device");
            } else {
                KillTimer(window_, kGuideCompatibilityTimer);
                AppendDiagnostic(
                    L"XInput Guide compatibility polling stopped after the last Xbox 360-family device disconnected");
            }
            break;
        case GbaOverlayPlatformEventKind::VisibilityChanged:
        case GbaOverlayPlatformEventKind::FocusChanged:
        case GbaOverlayPlatformEventKind::None:
            break;
        }
    }

    void HandlePlatformEvents() {
        if (!platform_) return;
        for (;;) {
            GbaOverlayPlatformEvent event;
            std::uint32_t hasEvent = GBA_OVERLAY_PLATFORM_FALSE;
            if (GbaOverlayPlatformDrainEvent(
                    platform_, GetTickCount64(), &event, &hasEvent) !=
                    GbaOverlayPlatformStatus::Ok ||
                hasEvent == GBA_OVERLAY_PLATFORM_FALSE) {
                return;
            }
            HandlePlatformEvent(event);
        }
    }

    static void CALLBACK OnForegroundChanged(
        HWINEVENTHOOK,
        DWORD event,
        HWND foregroundWindow,
        LONG,
        LONG,
        DWORD,
        DWORD) {
        auto* app = foregroundEventApp_;
        if (event == EVENT_SYSTEM_FOREGROUND && app && app->window_) {
            PostMessageW(app->window_, kForegroundChangedMessage, 0,
                         reinterpret_cast<LPARAM>(foregroundWindow));
        }
    }

    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam) {
        switch (message) {
        case WM_HOTKEY:
            if (wParam == kDeveloperHotkey) {
                AppendDiagnostic(L"F1 fallback toggle received");
                Dispatch(gba::Command::ToggleOverlay);
            }
            return 0;
        case WM_COPYDATA:
            return HandleLauncherExperienceSelectionProof(
                reinterpret_cast<const COPYDATASTRUCT*>(lParam)) ? TRUE : FALSE;
        case kPlatformEventMessage:
            HandlePlatformEvents();
            return 0;
        case kImageReadyMessage:
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kCatalogRefreshMessage: {
            if (state_.surface() == gba::Surface::Hidden &&
                !pinnedSurfaceCoordinator_.pinned()) {
                KillTimer(window_, kCatalogRetryTimer);
                bridge_.AbandonWidgetCatalogChangedRevision();
                sessions_.ResetCatalogRetry();
                return 0;
            }
            if (!sessions_.RequestCatalog()) {
                bridge_.AbandonWidgetCatalogChangedRevision();
                sessions_.ResetCatalogRetry();
            }
            return 0;
        }
        case kWidgetSessionCompletionMessage:
            ProcessWidgetSessionEvents();
            return 0;
        case kSnapshotRefreshMessage:
            if (state_.surface() == gba::Surface::Hidden &&
                !pinnedSurfaceCoordinator_.pinned()) return 0;
            {
                const auto correlationId = static_cast<std::uint64_t>(wParam);
                const std::wstring widgetId = state_.surface() == gba::Surface::Hidden
                    ? std::wstring(pinnedSurfaceCoordinator_.widgetId())
                    : state_.surface() == gba::Surface::Widget
                    ? std::wstring(state_.activeWidget())
                    : std::wstring(state_.selectedWidget());
                admissionTrace_.RecordRefreshDequeued(
                    correlationId, widgetId, GetTickCount64());
                const bool coldPresentationPending =
                    IsBridgeWidget(widgetId) && SnapshotFor(widgetId) == nullptr;
                RefreshAndApplyPresentation([&] {
                    if (!coldPresentationPending) {
                        RefreshCurrentBridgeSnapshot(correlationId);
                        return;
                    }
                    SyncWidgetActivity(
                        false, correlationId,
                        correlationId != 0 ? widgetId : std::wstring_view{});
                    if (SnapshotFor(widgetId) && pendingContentRevealWidget_ == widgetId) {
                        pendingContentRevealWidget_.clear();
                        // The last-good content was already fully visible.
                        // Admission is one atomic identity swap, not a fade to
                        // an empty content layer and back.
                        overlayTransition_.SnapContentVisible();
                        AdvanceOverlayTransition(GetTickCount64());
                        RestoreFocusForActiveSurface(widgetId);
                    }
                });
            }
            return 0;
        case kForegroundChangedMessage:
            if (state_.surface() != gba::Surface::Hidden) {
                const HWND foreground = reinterpret_cast<HWND>(lParam);
                const bool valid = foreground && IsWindow(foreground);
                DWORD processId = 0;
                if (valid) (void)GetWindowThreadProcessId(foreground, &processId);
                if (gba::input::DecideVisibleForegroundTransition(
                        true, valid, processId == GetCurrentProcessId()) ==
                    gba::input::VisibleForegroundTransition::CloseOverlay) {
                    (void)GbaOverlayPlatformObserveForegroundTarget(
                        platform_,
                        reinterpret_cast<std::uintptr_t>(foreground),
                        GBA_OVERLAY_PLATFORM_TRUE);
                    AppendDiagnostic(
                        L"External foreground activation closed the overlay");
                    Dispatch(gba::Command::CloseOverlay);
                }
            }
            return 0;
        case kPlacementRefreshMessage:
            if (state_.surface() != gba::Surface::Hidden) {
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case kDisplayRefreshMessage:
            ApplyPendingDisplayEnvironmentRefresh();
            return 0;
        case kPerformanceResetMessage:
            ResetPerformanceCounters();
            return 0;
        case kAccessibilityActionMessage:
            HandleAccessibilityActions();
            return 0;
        case kPinnedSurfaceChangedMessage:
            DrainPinnedSurfaceInputs();
            if (state_.surface() == gba::Surface::Hidden) {
                if (pinnedSurfaceCoordinator_.pinned())
                    SetTimer(window_, kPinnedSurfaceTimer, 100, nullptr);
                else
                    KillTimer(window_, kPinnedSurfaceTimer);
            }
            SyncWidgetActivity();
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kProcessActivationMessage:
            AppendDiagnostic(L"Resident process received an authenticated Show activation");
            if (state_.surface() == gba::Surface::Hidden) {
                Dispatch(gba::Command::ToggleOverlay);
            } else {
                (void)ShowOverlay();
                (void)AcquireOverlayForegroundInput();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case kDevelopmentTrayYHoldMessage:
            // Authenticated development fixtures exercise the same host-owned
            // recognizer/action route as GameInput without adding a production
            // controller remapping or public bridge message.
            if (!developmentReadyNonce_ || !developmentCatalogRoot_) return 0;
            AppendDiagnostic(
                L"Development tray Y phase=" + std::to_wstring(wParam) +
                L" eligible=" + (TrayYRestartEligible() ? L"true" : L"false"));
            if (wParam == 1) {
                trayYGesture_.Press(
                    state_.selectedWidget(), TrayYRestartEligible(), GetTickCount64());
            } else if (wParam == 2) {
                ApplyTrayYGestureAction(trayYGesture_.Update(
                    state_.selectedWidget(), TrayYRestartEligible(), GetTickCount64()));
            } else if (wParam == 3) {
                ApplyTrayYGestureAction(trayYGesture_.Release(
                    state_.selectedWidget(), TrayYRestartEligible(), GetTickCount64()));
            } else if (wParam == 4) {
                const auto now = GetTickCount64();
                trayYGesture_.Press(
                    state_.selectedWidget(), TrayYRestartEligible(),
                    now - gba::input::kTrayWidgetRestartHoldMilliseconds);
                ApplyTrayYGestureAction(trayYGesture_.Update(
                    state_.selectedWidget(), TrayYRestartEligible(), now));
            }
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case WM_SETFOCUS:
            accessibilityProvider_.SetWindowFocused(true);
            chromeAccessibilityProvider_.SetWindowFocused(true);
            if (platform_) {
                (void)GbaOverlayPlatformSetWindowState(
                    platform_,
                    PlatformBoolean(
                        state_.surface() != gba::Surface::Hidden),
                    GBA_OVERLAY_PLATFORM_TRUE);
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_KILLFOCUS:
            accessibilityProvider_.SetWindowFocused(false);
            chromeAccessibilityProvider_.SetWindowFocused(false);
            trayYGesture_.Cancel();
            if (platform_) {
                (void)GbaOverlayPlatformSetWindowState(
                    platform_,
                    PlatformBoolean(
                        state_.surface() != gba::Surface::Hidden),
                    GBA_OVERLAY_PLATFORM_FALSE);
            }
            InvalidateRect(window_, nullptr, FALSE);
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_SHOWWINDOW:
            accessibilityProvider_.SetWindowVisible(wParam != FALSE);
            if (wParam == FALSE) trayYGesture_.Reset();
            if (platform_) {
                (void)GbaOverlayPlatformSetWindowState(
                    platform_,
                    PlatformBoolean(wParam != FALSE),
                    PlatformBoolean(GetFocus() == window_));
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_GETOBJECT:
            if (static_cast<LONG>(lParam) == UiaRootObjectId) {
                if (!accessibilityActive_) {
                    accessibilityActive_ = true;
                    InvalidateRect(window_, nullptr, FALSE);
                    UpdateWindow(window_);
                }
                return accessibilityProvider_.HandleWmGetObject(wParam, lParam);
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_NCHITTEST:
            if (compositionSurface_.available() &&
                state_.surface() != gba::Surface::Hidden) {
                POINT point{
                    static_cast<LONG>(static_cast<short>(LOWORD(lParam))),
                    static_cast<LONG>(static_cast<short>(HIWORD(lParam))),
                };
                if (ScreenToClient(window_, &point) &&
                    !HitTestAuthoredCompositionSurface(
                        static_cast<float>(point.x),
                        static_cast<float>(point.y))) {
                    return HTTRANSPARENT;
                }
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_KEYDOWN:
            HandleKey(
                static_cast<UINT>(wParam),
                (lParam & (1LL << 30)) != 0);
            return 0;
        case WM_LBUTTONUP:
            HandlePointerActivation(
                static_cast<float>(static_cast<short>(LOWORD(lParam))),
                static_cast<float>(static_cast<short>(HIWORD(lParam))));
            return 0;
        case WM_TIMER:
            if (performanceCountersActive_) {
                ++performanceTimerMessages_;
                if (wParam == kControllerTimer) {
                    ++performanceControllerTimerMessages_;
                } else if (wParam == kGuideCompatibilityTimer) {
                    ++performanceGuideCompatibilityTimerMessages_;
                }
            }
            if (wParam == kControllerTimer || wParam == kPinnedSurfaceTimer) {
                const bool controllerTick = wParam == kControllerTimer;
                const auto now = GetTickCount64();
                if (controllerTick) admissionTrace_.ObserveSlow(now);
                // The dedicated deadline timer owns normal expiry. This cheap
                // state check also closes the race if Win32 timer creation is
                // temporarily unavailable; it never repaints unless state changed.
                if (controllerTick) actionFailureFeedback_.OnControllerTimer();
                if (controllerTick &&
                    (overlayTransition_.active() ||
                     presentationTransaction_.extentTransitionActive())) {
                    AdvanceOverlayTransition(now);
                }
                if (controllerTick && awaitingSuccessfulOpenPaint_ &&
                    now >= nextOpenPaintRetryAt_) {
                    nextOpenPaintRetryAt_ = now + 100;
                    InvalidateRect(window_, nullptr, FALSE);
                }
                // Closing changes semantic state immediately. The existing
                // controller timer is retained only long enough to finish the
                // bounded physical fade; no input or worker work runs behind
                // the hidden surface.
                if (state_.surface() == gba::Surface::Hidden &&
                    !pinnedSurfaceCoordinator_.pinned()) return 0;
                // A newly shown or device-lost HWND is deliberately alpha-zero
                // until D2D commits a complete frame. Do not let an invisible
                // surface consume navigation or advance its worker meanwhile;
                // Guide/F1 continue to arrive through their dedicated paths.
                if (awaitingSuccessfulOpenPaint_) return 0;
                (void)bridge_.PumpEvents();
                for (auto& failure : bridge_.TakeRuntimeFailures()) {
                    if (!sessions_.Contains(failure.widgetId)) {
                        AppendDiagnostic(
                            L"Dropped stale widget runtime failure for " +
                            failure.widgetId);
                        continue;
                    }
                    RecordWidgetStartupFailure(
                        failure.widgetId,
                        failure.safeMessage,
                        failure.category ==
                                gba::WidgetBridgeRuntimeFailureCategory::WorkerStart
                            ? gba::WidgetSessionFailureStage::Start
                            : gba::WidgetSessionFailureStage::Lifecycle,
                        true);
                    if (pinnedSurfaceCoordinator_.pinned() &&
                        pinnedSurfaceCoordinator_.widgetId() == failure.widgetId) {
                        (void)pinnedSurfaceCoordinator_.Unpin(
                            gba::pinned::WidgetSurfaceStopReason::WorkerUnavailable);
                    }
                }
                if (controllerTick && state_.surface() != gba::Surface::Hidden)
                    PollController();
                for (auto& artwork : bridge_.TakeArtworkResults()) {
                    if (artwork.pngBase64.empty())
                        (void)imageCache_->FailTrustedArtwork(
                            artwork.widgetId, artwork.artworkHandle);
                    else
                        (void)imageCache_->SupplyTrustedArtwork(
                            artwork.widgetId, artwork.artworkHandle,
                            std::move(artwork.pngBase64));
                }
                for (auto& result : bridge_.TakeLocalWidgetPackageInstallResults()) {
                    if (!localWidgetPackageImport_.Complete(result.operationId)) {
                        AppendDiagnostic(
                            L"Dropped stale local widget package result operation=" +
                            result.operationId);
                        continue;
                    }
                    lastLocalWidgetPackageInstallResult_ = result;
                    if (result.status != gba::LocalWidgetPackageInstallStatus::Cancelled) {
                        lastActionWidgetId_ = L"settings";
                        lastActionMessage_ = result.safeMessage;
                        lastActionExpiresAt_ = GetTickCount64() + 5000;
                        AppendDiagnostic(L"Local widget package import: " +
                                         result.safeMessage);
                    }
                }
                if (const auto revision = bridge_.TakePlatformAppearanceChangedRevision()) {
                    const auto& current = appearanceState_.current();
                    if (!current || *revision > current->revision) {
                        RefreshPlatformAppearance();
                    }
                }
                if (const auto revision =
                        bridge_.TakeLauncherExperienceChangedRevision()) {
                    if (*revision > launcherExperienceProjection_.selectionRevision())
                        RefreshLauncherExperience();
                }
                if (const auto revision = bridge_.TakeWidgetCatalogChangedRevision()) {
                    sessions_.ResetCatalogRetry();
                    AppendDiagnostic(L"Reconciling widget catalog revision " +
                                     std::to_wstring(*revision));
                    PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
                }
                for (auto& invalidatedWidget : bridge_.TakeInvalidatedWidgetIds()) {
                    const auto currentWidget = state_.surface() == gba::Surface::Widget
                        ? state_.activeWidget()
                        : state_.selectedWidget();
                    const bool pinnedInvalidation =
                        pinnedSurfaceCoordinator_.pinned() &&
                        pinnedSurfaceCoordinator_.widgetId() == invalidatedWidget;
                    if ((state_.surface() != gba::Surface::Hidden &&
                         currentWidget == invalidatedWidget) || pinnedInvalidation) {
                        RefreshAndApplyPresentation([&] {
                            RefreshWidgetSnapshot(invalidatedWidget);
                        });
                    } else {
                        // Preserve the event's widget identity. An offscreen
                        // cache is invalidated and will be fetched on selection.
                        sessions_.RemoveSnapshot(invalidatedWidget);
                        renderedSnapshotSequences_.erase(invalidatedWidget);
                    }
                }
                const auto actionFailures = bridge_.TakeActionFailures();
                const auto actionFeedback =
                    actionFailureFeedback_.PublishBridgeFailures(actionFailures);
                for (std::size_t index = 0; index < actionFeedback.count; ++index) {
                    const auto& failure = actionFailures[index];
                    const auto outcome = actionFeedback.OutcomeAt(index);
                    if (outcome == gba::WidgetActionFeedbackOutcome::Stale) {
                        AppendDiagnostic(
                            L"Dropped stale widget action failure for " + failure.widgetId);
                        continue;
                    }
                    if (outcome == gba::WidgetActionFeedbackOutcome::Refused) {
                        AppendDiagnostic(
                            L"Dropped widget action feedback at the bounded presentation seam for " +
                            failure.widgetId);
                        continue;
                    }
                    AppendDiagnostic(
                        L"Widget action failed: widget=" + failure.widgetId +
                        L" generation=" + failure.runtimeGeneration +
                        L" code=" +
                        std::wstring(gba::WidgetActionFailureCodeValue(failure.code)) +
                        L" action=" + failure.actionId +
                        L" source=" + failure.sourceElementId);
                }
                for (const auto& effect : bridge_.TakeHostEffects()) {
                    const auto* descriptor = sessions_.FindDescriptor(effect.widgetId);
                    const bool currentInteractiveWidget =
                        state_.surface() == gba::Surface::Widget &&
                        state_.focusRegion() == gba::FocusRegion::Widget &&
                        state_.activeWidget() == effect.widgetId &&
                        descriptor &&
                        descriptor->runtimeGeneration == effect.runtimeGeneration;
                    if (!currentInteractiveWidget) {
                        AppendDiagnostic(
                            L"Dropped stale or non-interactive widget host effect for " +
                            effect.widgetId);
                        continue;
                    }
                    if (effect.kind ==
                        gba::WidgetHostEffectKind::CloseOverlayAfterAppLaunch) {
                        AppendDiagnostic(
                            L"Closing overlay after confirmed app launch from " +
                            effect.widgetId);
                        Dispatch(gba::Command::CloseOverlay);
                        break;
                    }
                }
            } else if (wParam == kGuideCompatibilityTimer) {
                GbaOverlayPlatformEvent event;
                std::uint32_t hasEvent = GBA_OVERLAY_PLATFORM_FALSE;
                if (GbaOverlayPlatformPollLegacyGuide(
                        platform_, GetTickCount64(), &event, &hasEvent) ==
                        GbaOverlayPlatformStatus::Ok &&
                    hasEvent != GBA_OVERLAY_PLATFORM_FALSE) {
                    HandlePlatformEvent(event);
                }
            } else if (wParam == kZOrderSettleTimer) {
                KillTimer(window_, kZOrderSettleTimer);
                ReassertOverlayZOrder();
            } else if (wParam == kCatalogRetryTimer) {
                KillTimer(window_, kCatalogRetryTimer);
                bridge_.RetryWidgetCatalogChangedRevision();
                if (bridge_.TakeWidgetCatalogChangedRevision()) {
                    PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
                }
            } else if (wParam == kForegroundLossTimer) {
                KillTimer(window_, kForegroundLossTimer);
                if (state_.surface() != gba::Surface::Hidden) {
                    const HWND foreground = GetForegroundWindow();
                    const bool valid = foreground && IsWindow(foreground);
                    DWORD processId = 0;
                    if (valid) (void)GetWindowThreadProcessId(foreground, &processId);
                    if (gba::input::DecideVisibleForegroundTransition(
                            true, valid, processId == GetCurrentProcessId()) ==
                        gba::input::VisibleForegroundTransition::CloseOverlay) {
                        (void)GbaOverlayPlatformObserveForegroundTarget(
                            platform_,
                            reinterpret_cast<std::uintptr_t>(foreground),
                            GBA_OVERLAY_PLATFORM_TRUE);
                        AppendDiagnostic(
                            L"Application deactivation closed the overlay");
                        Dispatch(gba::Command::CloseOverlay);
                    }
                }
            } else if (wParam == kActionFeedbackTimer) {
                KillTimer(window_, kActionFeedbackTimer);
                actionFailureFeedback_.OnDeadlineTimer();
            }
            return 0;
        case WM_ACTIVATEAPP:
            if (state_.surface() != gba::Surface::Hidden) {
                if (wParam == FALSE) {
                    // Foreground assignment can lag WM_ACTIVATEAPP. Defer one
                    // bounded check and close only with a valid external HWND.
                    SetTimer(window_, kForegroundLossTimer, 40, nullptr);
                } else {
                    KillTimer(window_, kForegroundLossTimer);
                }
            }
            return 0;
        case WM_PAINT:
            if (performanceCountersActive_) ++performancePaintMessages_;
            Paint();
            return 0;
        case WM_SIZE:
        {
            const auto width = static_cast<unsigned int>(LOWORD(lParam));
            const auto height = static_cast<unsigned int>(HIWORD(lParam));
            const auto resize = gba::PlanRenderTargetResize(
                static_cast<bool>(hwndRenderTarget_), wParam == SIZE_MINIMIZED,
                width, height);
            if (!compositionSurface_.available() && resize.resizeInPlace) {
                // Resize keeps the HWND target allocation/lifecycle stable;
                // viewport-derived brushes, text formats, renderer resources,
                // focus geometry, and semantics are rebuilt by the committed
                // paint at the new extent.
                DiscardGraphicsResources(false);
                const HRESULT result = hwndRenderTarget_->Resize(D2D1::SizeU(width, height));
                if (FAILED(result)) {
                    AppendDiagnostic(
                        L"Overlay render target resize failed; recreating target hresult=" +
                        std::to_wstring(static_cast<unsigned long>(result)));
                    DiscardGraphicsResources();
                } else {
                    AppendDiagnostic(
                        L"Overlay render target resized in place width=" +
                        std::to_wstring(width) + L" height=" +
                        std::to_wstring(height));
                }
            }
            if (accessibilityActive_ && !compositionPlacementInProgress_)
                ClearAccessibilityTree();
            if (!compositionPlacementInProgress_)
                (void)ReconcileResponsiveFocusPersistence();
            if (resize.invalidate && !compositionPlacementInProgress_)
                InvalidateRect(window_, nullptr, FALSE);
            return 0;
        }
        case WM_DPICHANGED:
            if (accessibilityActive_) ClearAccessibilityTree();
            QueueDisplayEnvironmentRefresh(gba::DisplayEnvironmentChange::Dpi);
            return 0;
        case WM_DISPLAYCHANGE:
            // Display topology may change without a DPI transition. Recreate
            // the target so viewport-relative shell styles use fresh metrics.
            if (accessibilityActive_) ClearAccessibilityTree();
            QueueDisplayEnvironmentRefresh(gba::DisplayEnvironmentChange::Topology);
            return 0;
        case WM_SETTINGCHANGE:
            // SPI_SETWORKAREA/taskbar changes and accessibility/theme changes
            // share this notification. Always re-read monitor work-area data,
            // even when the bridge has not supplied an appearance revision.
            if (accessibilityActive_) ClearAccessibilityTree();
            QueueDisplayEnvironmentRefresh(gba::DisplayEnvironmentChange::SystemSettings);
            return 0;
        case WM_WINDOWPOSCHANGED:
            if (accessibilityActive_ &&
                (reinterpret_cast<const WINDOWPOS*>(lParam)->flags & SWP_NOMOVE) == 0 &&
                !accessibilityTree_.widgetId.empty())
                (void)PublishAccessibilityTree(
                    static_cast<double>(std::max(1U, GetDpiForWindow(window_))) / 96.0);
            if (state_.surface() != gba::Surface::Hidden &&
                (GetWindowLongPtrW(window_, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) {
                SetTimer(window_, kZOrderSettleTimer, 80, nullptr);
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_ERASEBKGND:
            return 1;
        case WM_CLOSE:
            if (performanceState_ && performanceCountersActive_ &&
                !PublishPerformanceCounters()) {
                AppendDiagnostic(L"Performance runtime diagnostics could not be published");
            }
            accessibilityProvider_.Detach();
            chromeAccessibilityProvider_.Detach();
            DestroyWindow(window_);
            return 0;
        case WM_DESTROY:
            accessibilityProvider_.Detach();
            chromeAccessibilityProvider_.Detach();
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcW(window_, message, wParam, lParam);
        }
    }

    void Shutdown() {
        localWidgetPackageImport_.CancelPicker();
        if (const auto operation = localWidgetPackageImport_.CancelActiveOperation()) {
            (void)bridge_.CancelLocalWidgetPackageInstall(
                *operation);
        }
        pinnedSurfaceCoordinator_.Dispose();
        DiscardGraphicsResources();
        gba::shell::ResetFixedChromeComposition(
            compositionSurface_, chromeWindow_);
        retainedGuidePaintKey_.clear();
        retainedTrayPaintState_.reset();
        fixedChromeAnchor_.reset();
        compositionChromeSession_.reset();
        declarativeRenderer_.reset();
        if (imageCache_) {
            imageCache_->Shutdown();
            imageCache_.reset();
        }
        (void)sessions_.DrainLifecycle(std::chrono::milliseconds(1000));
        sessions_.Shutdown();
        actionFailureFeedback_.Stop();
        bridge_.Stop();
        if (window_) {
            accessibilityProvider_.Detach();
            chromeAccessibilityProvider_.Detach();
            KillTimer(window_, kControllerTimer);
            KillTimer(window_, kGuideCompatibilityTimer);
            KillTimer(window_, kZOrderSettleTimer);
            KillTimer(window_, kCatalogRetryTimer);
            KillTimer(window_, kForegroundLossTimer);
            KillTimer(window_, kActionFeedbackTimer);
            KillTimer(window_, kPinnedSurfaceTimer);
            UnregisterHotKey(window_, kDeveloperHotkey);
        }
        if (platform_) {
            GbaOverlayPlatformShutdown(platform_);
        }
        if (foregroundHook_) {
            UnhookWinEvent(foregroundHook_);
            foregroundHook_ = nullptr;
        }
        if (foregroundEventApp_ == this) foregroundEventApp_ = nullptr;
        if (backdropWindow_ && IsWindow(backdropWindow_)) {
            DestroyWindow(backdropWindow_);
            backdropWindow_ = nullptr;
        }
        if (chromeWindow_ && IsWindow(chromeWindow_)) {
            DestroyWindow(chromeWindow_);
            chromeWindow_ = nullptr;
        }
        if (backdropBrush_) {
            DeleteObject(backdropBrush_);
            backdropBrush_ = nullptr;
        }
        if (platform_) {
            GbaOverlayPlatformDestroy(platform_);
            platform_ = nullptr;
        }
        if (runtimeInitialized_) {
            RoUninitialize();
            runtimeInitialized_ = false;
        }
    }

    template <typename Mutation>
    void ApplyStateTransition(
        Mutation mutation,
        const std::optional<gba::Command> command = std::nullopt) {
        const auto priorSurface = state_.surface();
        const auto priorFocusRegion = state_.focusRegion();
        const auto priorDesiredExtent = DesiredPresentationExtentDip();
        const auto priorExtent = compositionSurface_.available()
            ? presentationTransaction_.CommittedDestinationExtent(
                priorDesiredExtent)
            : priorDesiredExtent;
        const auto priorPresentedExtent = PresentedPresentationExtentDip();
        const std::wstring priorSelected(state_.selectedWidget());
        const std::wstring priorActive(state_.activeWidget());
        const auto priorDesiredLifecycle = gba::DesiredWidgetLifecycle(
            priorSurface, priorFocusRegion, priorSelected, priorActive,
            IsBridgeWidget(priorSelected), IsBridgeWidget(priorActive));
        if (priorSurface == gba::Surface::Widget)
            CommitAdmittedWidgetPresentation(priorActive);
        if (priorSurface == gba::Surface::Widget && IsBridgeWidget(priorActive)) {
            RememberCurrentFocus(priorActive);
        }
        const auto before = state_.persistent();
        if (!mutation()) {
            return;
        }
        // Any accepted shell transition changes focus, selection, presentation,
        // reorder, or lifecycle authority. A pending Y must never survive it;
        // the gesture's own tap action has already retired its capture here.
        if (trayYGesture_.capturing()) trayYGesture_.Cancel();
        if (priorSurface != state_.surface() || priorActive != state_.activeWidget() ||
            priorFocusRegion != state_.focusRegion()) {
            sliderInteraction_.DeactivateAll();
            if (pressedInteraction_.Clear())
                InvalidateRect(window_, nullptr, FALSE);
        }
        if (!performanceState_ &&
            (before.order != state_.persistent().order ||
            before.lastWidget != state_.persistent().lastWidget ||
             before.reopenWidget != state_.persistent().reopenWidget)) {
            SavePersistentState(state_.persistent());
        }
        if (priorSurface != state_.surface() || priorActive != state_.activeWidget()) {
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
            ClearAccessibilityTree();
        }
        const bool deferColdWidgetStart =
            priorSurface == gba::Surface::Widget &&
            state_.surface() == gba::Surface::Widget &&
            priorActive != state_.activeWidget() &&
            IsBridgeWidget(state_.activeWidget()) &&
            SnapshotFor(state_.activeWidget()) == nullptr;
        const bool selectionAuthorityChanged =
            priorSelected != state_.selectedWidget() ||
            priorActive != state_.activeWidget();
        const std::wstring traceWidget = state_.surface() == gba::Surface::Widget
            ? std::wstring(state_.activeWidget())
            : std::wstring(state_.selectedWidget());
        const auto now = GetTickCount64();
        std::uint64_t correlationId{};
        if (selectionAuthorityChanged &&
            state_.surface() != gba::Surface::Hidden &&
            IsBridgeWidget(traceWidget)) {
            correlationId = admissionTrace_.BeginSelection(
                state_.selectedWidget(), state_.activeWidget(),
                deferColdWidgetStart, SnapshotFor(traceWidget) != nullptr, now);
            admissionTraceWidget_ = traceWidget;
            admissionTraceCorrelationId_ = correlationId;
        } else if (command == gba::Command::Activate &&
                   admissionTraceWidget_ == traceWidget) {
            correlationId = admissionTraceCorrelationId_;
        }
        SyncWidgetActivity(
            deferColdWidgetStart, correlationId,
            correlationId != 0 ? traceWidget : std::wstring_view{});
        const auto nextDesiredLifecycle = gba::DesiredWidgetLifecycle(
            state_.surface(), state_.focusRegion(),
            state_.selectedWidget(), state_.activeWidget(),
            IsBridgeWidget(state_.selectedWidget()),
            IsBridgeWidget(state_.activeWidget()));
        if (command == gba::Command::Activate && correlationId != 0 &&
            priorDesiredLifecycle && nextDesiredLifecycle &&
            priorDesiredLifecycle->widgetId == nextDesiredLifecycle->widgetId) {
            admissionTrace_.RecordMeaningfulInteractive(
                correlationId, nextDesiredLifecycle->widgetId,
                priorDesiredLifecycle->state, nextDesiredLifecycle->state, now);
        }
        if (state_.surface() == gba::Surface::Widget &&
            priorFocusRegion != state_.focusRegion() &&
            state_.focusRegion() == gba::FocusRegion::Widget) {
            RestoreFocusForActiveSurface(state_.activeWidget());
        }

        if (priorSurface == gba::Surface::Hidden &&
            state_.surface() != gba::Surface::Hidden) {
            RequestFixedChromeAnchorRefresh(
                FixedChromePlacementReason::NewVisibleSession);
            if (awaitingSuccessfulOpenPaint_ || !IsWindowVisible(window_)) {
                awaitingSuccessfulOpenPaint_ = true;
                nextOpenPaintRetryAt_ = now + 100;
                overlayTransition_.PrepareInitialOpen();
            } else {
                overlayTransition_.BeginOpen(
                    now, CurrentAccessibilityPolicy().reducedMotion);
            }
            AdvanceOverlayTransition(now);
        }
        const bool revealWidgetContent = gba::ShouldRevealWidgetContent(
                priorSurface == gba::Surface::Widget, priorActive,
                state_.surface() == gba::Surface::Widget,
                state_.activeWidget());
        if (revealWidgetContent) {
            RequestWidgetContentReveal(state_.activeWidget());
        }
        if (gba::ShouldSnapWidgetContentVisible(
                state_.surface() == gba::Surface::Widget,
                priorFocusRegion == gba::FocusRegion::Widget,
                state_.focusRegion() == gba::FocusRegion::Widget)) {
            SnapWidgetContentVisible();
        }
        if (state_.surface() == gba::Surface::Hidden) {
            pendingContentRevealWidget_.clear();
        }

        const bool awaitingIncomingSnapshot =
            state_.surface() == gba::Surface::Widget &&
            IsBridgeWidget(state_.activeWidget()) &&
            SnapshotFor(state_.activeWidget()) == nullptr;

        if (state_.surface() != gba::Surface::Hidden) {
            const bool enteredBridgeWidget =
                state_.surface() == gba::Surface::Widget && IsBridgeWidget(state_.activeWidget()) &&
                (priorSurface != gba::Surface::Widget || priorActive != state_.activeWidget());
            const bool hoveredBridgeWidget =
                state_.surface() == gba::Surface::Dashboard && IsBridgeWidget(state_.selectedWidget()) &&
                (priorSurface != gba::Surface::Dashboard || priorSelected != state_.selectedWidget());
            const bool startupFailureBlocksSnapshot =
                (enteredBridgeWidget && sessions_.Failure(state_.activeWidget())) ||
                (hoveredBridgeWidget && sessions_.Failure(state_.selectedWidget()));
            if (priorSurface == gba::Surface::Hidden) {
                PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
            }
            if (priorSurface != gba::Surface::Hidden &&
                (enteredBridgeWidget || hoveredBridgeWidget) &&
                !startupFailureBlocksSnapshot) {
                const bool posted = PostMessageW(
                    window_, kSnapshotRefreshMessage,
                    static_cast<WPARAM>(correlationId), 0) != FALSE;
                admissionTrace_.RecordRefreshPosted(
                    correlationId, traceWidget, posted, GetTickCount64());
            }
        }
        const auto nextExtent = DesiredPresentationExtentDip();
        if (compositionSurface_.available() &&
            priorSurface == gba::Surface::Widget &&
            state_.surface() == gba::Surface::Widget &&
            priorActive != state_.activeWidget() &&
            awaitingIncomingSnapshot) {
            presentationTransaction_.HoldPresentedExtent(priorPresentedExtent);
        }
        const bool destinationSnapshotAdmitted =
            state_.surface() == gba::Surface::Widget &&
            (!IsBridgeWidget(state_.activeWidget()) ||
             SnapshotFor(state_.activeWidget()) != nullptr);
        const bool animateWidgetExtent =
            priorSurface == gba::Surface::Widget &&
            state_.surface() == gba::Surface::Widget &&
            priorExtent != nextExtent && destinationSnapshotAdmitted;
        if (animateWidgetExtent) {
            BeginWidgetExtentTransition(priorPresentedExtent, nextExtent);
        } else if (compositionSurface_.available() &&
                   destinationSnapshotAdmitted &&
                   priorPresentedExtent == nextExtent) {
            presentationTransaction_.ReleasePresentedExtent();
        } else if (state_.surface() != gba::Surface::Widget) {
            presentationTransaction_.SettleExtent(nextExtent, now, true);
        }
        const auto presentation = gba::DecideOverlayPresentation(
            priorSurface != gba::Surface::Hidden,
            state_.surface() != gba::Surface::Hidden,
            priorExtent,
            nextExtent);
        if (priorSurface == gba::Surface::Widget &&
            state_.surface() == gba::Surface::Widget &&
            (priorActive != state_.activeWidget() || priorExtent != nextExtent)) {
            const bool identityChanged = priorActive != state_.activeWidget();
            const bool extentChanged = priorExtent != nextExtent;
            AppendDiagnostic(
                L"Widget presentation transition from=" + priorActive +
                L" extent=" + std::to_wstring(priorPresentedExtent.widthDip) + L"x" +
                std::to_wstring(priorPresentedExtent.heightDip) + L" to=" +
                std::wstring(state_.activeWidget()) + L" extent=" +
                std::to_wstring(nextExtent.widthDip) + L"x" +
                std::to_wstring(nextExtent.heightDip) + L" cause=" +
                (identityChanged && extentChanged
                    ? L"identity-and-extent"
                    : identityChanged ? L"identity" : L"extent") +
                L" target=" +
                (animateWidgetExtent
                    ? (compositionSurface_.available()
                        ? (CurrentAccessibilityPolicy().reducedMotion
                            ? L"composition-immediate"
                            : L"composition-motion")
                        : CurrentAccessibilityPolicy().reducedMotion
                            ? L"resize-in-place"
                            : L"animated-resize-in-place")
                    : L"retained") +
                L" content=" +
                (awaitingIncomingSnapshot &&
                 presentationTransaction_.retainedPresentation()
                    ? L"retained-until-snapshot"
                    : revealWidgetContent ? L"reveal" : L"stable") +
                L" sizing=" + (awaitingIncomingSnapshot
                    ? L"retained-until-snapshot"
                    : L"snapshot-admitted") +
                (awaitingIncomingSnapshot &&
                 presentationTransaction_.retainedPresentation()
                    ? L" retained-from=" +
                        presentationTransaction_.retainedPresentation()->widgetId
                    : L""));
        }
        ApplyPresentation(presentation);
        if (deferColdWidgetStart && IsWindowVisible(window_)) {
            // Revoke the old widget's lifecycle/input semantics above, but
            // commit its last admitted pixels before the posted cold-start
            // request can block this window thread. The retained snapshot is
            // visual-only and therefore publishes no stale focus or UIA.
            RedrawWindow(window_, nullptr, nullptr,
                RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
        }
        (void)ReconcileResponsiveFocusPersistence();
    }

    void Dispatch(const gba::Command command) {
        if (command == gba::Command::Activate &&
            state_.focusRegion() == gba::FocusRegion::Tray &&
            UsesLauncherExperiencePresentation(state_.selectedWidget())) {
            launcherExperienceProjection_.BeginActivation(
                std::exchange(launcherSafeStartPending_, false));
        } else if (command == gba::Command::Activate) {
            launcherSafeStartPending_ = false;
        }
        ApplyStateTransition(
            [&] { return state_.Dispatch(command); }, command);
    }

    bool SelectTrayWidget(const std::wstring_view widgetId) {
        bool accepted = false;
        ApplyStateTransition([&] {
            const auto priorSelected = state_.selectedWidget();
            accepted = state_.TrySelectTrayWidget(widgetId);
            return accepted && priorSelected != state_.selectedWidget();
        });
        return accepted;
    }

    const gba::WidgetComputedStyle& ShellComputedStyle(
        const std::wstring_view key) const noexcept {
        static const gba::WidgetComputedStyle empty;
        const auto& current = appearanceState_.current();
        if (!current) return empty;
        const auto found = current->shellStyles.find(std::wstring(key));
        return found == current->shellStyles.end() ? empty : found->second;
    }

    gba::NativeAccessibilityPolicy CurrentAccessibilityPolicy() const {
        const auto& current = appearanceState_.current();
        if (!current) return {};

        HIGHCONTRASTW highContrast{sizeof(highContrast)};
        const bool systemHighContrast =
            SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(highContrast),
                                  &highContrast, 0) &&
            (highContrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
        BOOL animationsEnabled = TRUE;
        if (!SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0,
                                   &animationsEnabled, 0)) {
            animationsEnabled = TRUE;
        }
        return gba::CreateNativeAccessibilityPolicy(
            *current, systemHighContrast, animationsEnabled != FALSE);
    }

    gba::NativeRenderStyle AdaptShellStyle(
        const std::wstring_view key,
        const bool focused = false,
        const float viewportWidth = static_cast<float>(kPanelWidth),
        const float viewportHeight = static_cast<float>(kWidgetPanelHeight),
        const std::optional<gba::NativeColor>& inheritedBackground = std::nullopt,
        const std::optional<gba::NativeColor>& fallbackBackground = std::nullopt) const {
        gba::NativeStyleContext context;
        context.viewportWidthPx = viewportWidth;
        context.viewportHeightPx = viewportHeight;
        context.parentWidthPx = context.viewportWidthPx;
        context.parentHeightPx = context.viewportHeightPx;
        context.parentFontSizePx = 16.0F;
        context.rootFontSizePx = 16.0F;
        context.focused = focused;
        context.effectiveBackground = inheritedBackground;
        context.fallbackBackground = fallbackBackground;
        const auto accessibility = CurrentAccessibilityPolicy();
        auto result = gba::NativeStyleAdapter::Adapt(
            ShellComputedStyle(key), context, accessibility);
        for (const auto& diagnostic : result.diagnostics) {
            AppendDiagnostic(L"Platform shell style " + std::wstring(key) + L" " +
                             diagnostic.property + L": " + diagnostic.message);
        }
        return std::move(result.style);
    }

    void RebuildShellStyles(
        const float viewportWidth = static_cast<float>(kPanelWidth),
        const float viewportHeight = static_cast<float>(kWidgetPanelHeight)) {
        canvasStyle_ = AdaptShellStyle(
            L"canvas", false, viewportWidth, viewportHeight,
            kSafeCanvasFallback, kDefaultCanvas);
        effectiveCanvasBackground_ = gba::ResolveNativeSurfaceColor(
            canvasStyle_.background().has_value()
                ? canvasStyle_.background()
                : std::optional<gba::NativeColor>{kDefaultCanvas},
            kSafeCanvasFallback,
            canvasStyle_.opacity());
        backdropStyle_ = AdaptShellStyle(L"backdrop", false, viewportWidth, viewportHeight);
        panelStyle_ = AdaptShellStyle(
            L"panel", false, viewportWidth, viewportHeight,
            effectiveCanvasBackground_, kDefaultPanel);
        effectivePanelBackground_ = gba::ResolveNativeSurfaceColor(
            panelStyle_.background().has_value()
                ? panelStyle_.background()
                : std::optional<gba::NativeColor>{kDefaultPanel},
            effectiveCanvasBackground_,
            panelStyle_.opacity());
        trayStyle_ = AdaptShellStyle(
            L"tray", false, viewportWidth, viewportHeight,
            effectiveCanvasBackground_, effectiveCanvasBackground_);
        const auto trayLayer = trayStyle_.background().has_value()
            ? trayStyle_.background()
            : std::optional<gba::NativeColor>{effectiveCanvasBackground_};
        effectiveTrayBackground_ = gba::ResolveNativeSurfaceColor(
            trayLayer, effectiveCanvasBackground_, trayStyle_.opacity());
        trayItemStyle_ = AdaptShellStyle(
            L"tray-item", false, viewportWidth, viewportHeight,
            effectiveTrayBackground_, trayLayer);
        trayItemSelectedStyle_ = AdaptShellStyle(
            L"tray-item:selected", false, viewportWidth, viewportHeight,
            effectiveTrayBackground_, kDefaultAccent);
        trayItemFocusedStyle_ = AdaptShellStyle(
            L"tray-item:focused", true, viewportWidth, viewportHeight,
            effectiveTrayBackground_, kDefaultAccent);
        trayItemSelectedFocusedStyle_ =
            AdaptShellStyle(L"tray-item:selected:focused", true,
                            viewportWidth, viewportHeight,
                            effectiveTrayBackground_, kDefaultAccent);
        titleStyle_ = AdaptShellStyle(
            L"title", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        bodyStyle_ = AdaptShellStyle(
            L"body", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        hintStyle_ = AdaptShellStyle(
            L"hint", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        statusStyle_ = AdaptShellStyle(
            L"status", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        dashboardTitleStyle_ = AdaptShellStyle(
            L"title", false, viewportWidth, viewportHeight, effectiveCanvasBackground_);
        dashboardHintStyle_ = AdaptShellStyle(
            L"hint", false, viewportWidth, viewportHeight, effectiveCanvasBackground_);
    }

    void ApplyPlatformAppearance(
        const bool applyPresentation = true,
        const bool invalidateWidgetSnapshots = true) {
        const auto& current = appearanceState_.current();
        if (!current) return;
        RebuildShellStyles();

        const auto backdropColor = backdropStyle_.background().value_or(
            gba::NativeColor{0, 0, 0, 1});
        if (HBRUSH replacement = CreateSolidBrush(GdiColor(backdropColor))) {
            if (backdropBrush_) DeleteObject(backdropBrush_);
            backdropBrush_ = replacement;
        }
        targetBackdropOpacity_ = static_cast<BYTE>(std::lround(
            std::clamp(current->backdropOpacity, 0.35, 0.8) * 255.0));
        // Sampling here makes an accessibility change to reduced motion snap
        // an in-flight transition immediately rather than waiting for a timer.
        AdvanceOverlayTransition(GetTickCount64());
        InvalidateRect(backdropWindow_, nullptr, TRUE);

        // The host owns every physical animation. Leaving DWM transitions on
        // can add an unbounded second animation around show/hide and resize.
        const BOOL disableTransitions = TRUE;
        (void)DwmSetWindowAttribute(window_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));

        if (invalidateWidgetSnapshots) {
            // Worker snapshots contain bridge-computed widget styles derived
            // from the same platform revision. Drop every cached snapshot,
            // then refresh only the visible worker. Live Win32 accessibility
            // broadcasts reuse the current revision and skip this block.
            const std::wstring visibleWidget = state_.surface() == gba::Surface::Widget
                ? std::wstring(state_.activeWidget())
                : std::wstring(state_.selectedWidget());
            const bool hadVisibleSnapshot = SnapshotFor(visibleWidget) != nullptr;
            sessions_.ClearSnapshots();
            renderedSnapshotSequences_.clear();
            if (state_.surface() != gba::Surface::Hidden && hadVisibleSnapshot &&
                IsBridgeWidget(visibleWidget)) {
                RefreshWidgetSnapshot(visibleWidget);
            }
        }

        if (applyPresentation && state_.surface() != gba::Surface::Hidden) {
            RequestFixedChromeAnchorRefresh(
                FixedChromePlacementReason::Appearance);
        }
        DiscardGraphicsResources();
        if (applyPresentation && state_.surface() != gba::Surface::Hidden) {
            ShowOverlay();
            InvalidateRect(window_, nullptr, FALSE);
        }
        AppendDiagnostic(L"Applied platform appearance revision " +
                         std::to_wstring(current->revision) + L" theme=" +
                         current->themeId + L"@" + current->themeVersion);
    }

    void RefreshPlatformAppearance() {
        auto appearance = bridge_.GetPlatformAppearance();
        if (!appearance) {
            AppendDiagnostic(L"Platform appearance refresh failed; retaining last good state: " +
                             bridge_.lastError());
            return;
        }
        const long long revision = appearance->revision;
        if (!appearanceState_.Publish(std::move(*appearance))) {
            AppendDiagnostic(L"Ignored stale platform appearance revision " +
                             std::to_wstring(revision));
            return;
        }
        ApplyPlatformAppearance();
    }

    void RefreshLauncherExperience() {
        auto selection = bridge_.GetLauncherExperience();
        if (!selection) {
            AppendDiagnostic(
                L"Launcher Experience refresh failed; retaining last good state: " +
                bridge_.lastError());
            return;
        }
        (void)PublishLauncherExperience(std::move(*selection));
    }

    bool SelectLauncherExperience(
        const gba::LauncherExperienceSelectionRequest& request) {
        auto selection = bridge_.SelectLauncherExperience(request);
        if (!selection) {
            AppendDiagnostic(
                L"Launcher Experience selection denied; retaining last good state: " +
                bridge_.lastError());
            return false;
        }
        return PublishLauncherExperience(std::move(*selection));
    }

    bool PublishLauncherExperience(gba::LauncherExperienceSelection selection) {
        const long long revision = selection.revision;
        std::wstring diagnostic;
        if (!launcherExperienceProjection_.PublishSelection(
                std::move(selection), diagnostic)) {
            AppendDiagnostic(
                L"Launcher Experience revision " + std::to_wstring(revision) +
                L" rejected; retaining last good state: " + diagnostic);
            return false;
        }
        AppendDiagnostic(
            L"Applied Launcher Experience revision " + std::to_wstring(revision) +
            L" validated=compact@100%,compact@150%,standard@100%,standard@150%,wide@100%,wide@150%" +
            (diagnostic.empty() ? L"" : L" retained-diagnostic=" + diagnostic));
        if (state_.surface() != gba::Surface::Hidden)
            InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    bool HandleLauncherExperienceSelectionProof(
        const COPYDATASTRUCT* message) {
        // The production-host fixture drives the existing host-owned Settings
        // selection service. Keep the proof seam unavailable
        // unless the existing bounded performance-evidence nonce is present.
        if (!performanceDiagnosticsNonce_ || !message ||
            message->dwData != kLauncherExperienceSelectionProof ||
            !message->lpData || message->cbData < sizeof(wchar_t) ||
            message->cbData > 2048 ||
            message->cbData % sizeof(wchar_t) != 0) return false;
        const auto characters = message->cbData / sizeof(wchar_t);
        const auto* data = static_cast<const wchar_t*>(message->lpData);
        if (data[characters - 1] != L'\0') return false;
        const std::wstring_view payload(data, characters - 1);
        std::array<std::wstring_view, 5> fields{};
        std::size_t cursor{};
        for (std::size_t index = 0; index < fields.size(); ++index) {
            const auto end = payload.find(L'\n', cursor);
            if (end == std::wstring_view::npos) {
                if (index != fields.size() - 1) return false;
                fields[index] = payload.substr(cursor);
                cursor = payload.size();
            } else {
                fields[index] = payload.substr(cursor, end - cursor);
                cursor = end + 1;
            }
        }
        if (cursor != payload.size() || fields[0] != L"gba-launcher-selection-v1" ||
            fields[1] != *performanceDiagnosticsNonce_) return false;

        gba::LauncherExperienceSelectionRequest request;
        if (fields[2] == L"select-exact" && !fields[3].empty() &&
            !fields[4].empty()) {
            request.operation =
                gba::LauncherExperienceSelectionOperation::SelectExact;
            request.id = fields[3];
            request.version = fields[4];
        } else if (fields[2] == L"recover-built-in" && fields[3].empty() &&
                   fields[4].empty()) {
            request.operation =
                gba::LauncherExperienceSelectionOperation::RecoverBuiltIn;
        } else {
            return false;
        }
        AppendDiagnostic(L"Authenticated Launcher Experience selection proof operation=" +
            std::wstring(fields[2]));
        return SelectLauncherExperience(request);
    }

    void QueueDisplayEnvironmentRefresh(const gba::DisplayEnvironmentChange change) {
        if (displayRefresh_.Enqueue(
                state_.surface() != gba::Surface::Hidden, change)) {
            if (!PostMessageW(window_, kDisplayRefreshMessage, 0, 0)) {
                // A live HWND should accept its private message, but never
                // strand the accumulator if the queue is temporarily full.
                // The synchronous fallback retains correctness; coalescing is
                // an optimization, not a precondition for display recovery.
                ApplyPendingDisplayEnvironmentRefresh();
            }
        }
    }

    void ApplyPendingDisplayEnvironmentRefresh() {
        const auto plan = displayRefresh_.Take();
        pinnedSurfaceCoordinator_.ReconcileDisplayEnvironment();
        if (state_.surface() == gba::Surface::Hidden) return;
        if (!plan.repositionWindows) return;

        RequestFixedChromeAnchorRefresh(
            FixedChromePlacementReason::DisplayEnvironment);

        // Appearance application also invalidates widget snapshots and target
        // resources. Suppress its placement step so one notification produces
        // one authoritative monitor/work-area/DPI resolve. If appearance is
        // unavailable, the placement refresh still proceeds independently.
        if (plan.reapplyAppearance) {
            // Applying the current appearance already recreates the target
            // resources; do not discard them a second time for the same
            // display-environment notification.
            ApplyPlatformAppearance(false, false);
        } else if (plan.recreateGraphics) {
            DiscardGraphicsResources();
        }
        ShowOverlay();
        InvalidateRect(window_, nullptr, FALSE);
    }

    void ApplyWidgetCatalogChange(const gba::WidgetSessionCatalogChange& change) {
        const auto& descriptors = sessions_.descriptors();
        pinnedSurfaceCoordinator_.ReconcileCatalog(descriptors);
        if (!actionFailureFeedback_.ReconcileCatalog(descriptors)) {
            AppendDiagnostic(L"Widget action feedback rejected an invalid catalog projection");
        }
        // Runtime identity owns focus memory independently of snapshot cache
        // residency. Clear every replaced/removed runtime even when its
        // offscreen snapshot was evicted earlier.
        for (const auto& runtime : change.runtimeChanges) {
            focusMemory_.Forget(runtime.widgetId);
            if (declarativeRenderer_ && !runtime.previousInstanceId.empty()) {
                // Scroll offsets belong to the exact worker runtime, just like
                // focus memory. Never let a replacement package inherit native
                // renderer state merely because it reused public node IDs.
                declarativeRenderer_->ForgetWidgetState(runtime.previousInstanceId);
                sliderInteraction_.ForgetWidget(runtime.previousInstanceId);
            }
        }
        std::erase_if(renderedSnapshotSequences_, [&](const auto& entry) {
            return !sessions_.Contains(entry.first);
        });
        const std::wstring runtimeRevealWidget =
            state_.surface() == gba::Surface::Widget &&
                    std::any_of(
                        change.runtimeChanges.begin(), change.runtimeChanges.end(),
                        [&](const gba::WidgetSessionRuntimeChange& runtime) {
                            return runtime.widgetId == state_.activeWidget();
                        })
                ? std::wstring(state_.activeWidget())
                : std::wstring{};
        if (!runtimeRevealWidget.empty()) {
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
        }
        const bool catalogOrderChanged =
            change.availableWidgetIds != state_.order();
        if (catalogOrderChanged &&
            state_.surface() != gba::Surface::Hidden) {
            RequestFixedChromeAnchorRefresh(
                FixedChromePlacementReason::CatalogOrder);
        }
        bool trayStateChanged = false;
        ApplyStateTransition([&] {
            trayStateChanged = state_.SetAvailableWidgets(change.availableWidgetIds);
            return trayStateChanged;
        });
        if (!trayStateChanged) {
            // Descriptor-only replacement can still change the rendered tray
            // name/icon projection. Keep lifecycle ownership synchronized and
            // publish the new catalog even when stable IDs and selection did
            // not move.
            SyncWidgetActivity();
            if (state_.surface() != gba::Surface::Hidden && window_)
                InvalidateRect(window_, nullptr, FALSE);
        }
        if (state_.surface() != gba::Surface::Hidden && window_) {
            // Catalog events and pointer messages share this UI thread. Commit
            // the replacement tray before returning to the message queue so
            // old pixels can never dispatch through new slot geometry.
            UpdateWindow(window_);
        }
        if (!runtimeRevealWidget.empty() &&
            state_.surface() == gba::Surface::Widget &&
            state_.activeWidget() == runtimeRevealWidget) {
            AppendDiagnostic(
                L"Widget presentation transition from=" + runtimeRevealWidget +
                L" to=" + runtimeRevealWidget +
                L" cause=runtime-replaced target=retained content=reveal");
            RequestWidgetContentReveal(runtimeRevealWidget);
        }
    }

    [[nodiscard]] bool IsBridgeWidget(const std::wstring_view id) const noexcept {
        return sessions_.Contains(id);
    }

    [[nodiscard]] bool UsesLauncherExperiencePresentation(
        const std::wstring_view id) const noexcept {
        const auto* descriptor = sessions_.FindDescriptor(id);
        return descriptor && descriptor->advancedPresentation &&
            descriptor->advancedPresentation->schemaVersion == 1 &&
            descriptor->advancedPresentation->kind == L"launcherExperience";
    }

    void RecordWidgetStartupFailure(
        const std::wstring_view widgetId,
        const std::wstring_view safeFailure,
        const gba::WidgetSessionFailureStage stage =
            gba::WidgetSessionFailureStage::Snapshot,
        const bool revokeCurrentGeneration = false) {
        std::wstring message = std::wstring(DisplayWidgetName(widgetId)) +
            L" failed: " + std::wstring(safeFailure) + L" Press A to retry.";
        if (message.size() > 640) message.resize(640);
        if (revokeCurrentGeneration)
            sessions_.RecordRuntimeFailure(widgetId, stage, message);
        else
            sessions_.RecordFailure(widgetId, stage, message);
        if (state_.surface() == gba::Surface::Widget &&
            state_.activeWidget() == widgetId) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
            ClearAccessibilityTree();
        }
        AppendDiagnostic(message);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void SyncWidgetActivity(
        const bool deferColdWidgetStart = false,
        const std::uint64_t correlationId = 0,
        const std::wstring_view correlationWidgetId = {}) {
        std::map<std::wstring, gba::WidgetLifecycleState, std::less<>> desiredStates;
        const auto overlayDesired = gba::DesiredWidgetLifecycle(
            state_.surface(), state_.focusRegion(),
            state_.selectedWidget(), state_.activeWidget(),
            IsBridgeWidget(state_.selectedWidget()),
            IsBridgeWidget(state_.activeWidget()));
        if (overlayDesired) {
            desiredStates.insert_or_assign(overlayDesired->widgetId, overlayDesired->state);
        }
        if (pinnedSurfaceCoordinator_.pinned()) {
            const std::wstring pinnedId(pinnedSurfaceCoordinator_.widgetId());
            const auto pinnedState =
                pinnedSurfaceCoordinator_.interactionMode() ==
                        gba::pinned::InteractionMode::Focusable
                    ? gba::WidgetLifecycleState::Interactive
                    : gba::WidgetLifecycleState::Visible;
            const auto existing = desiredStates.find(pinnedId);
            if (existing == desiredStates.end() ||
                (existing->second == gba::WidgetLifecycleState::Visible &&
                 pinnedState == gba::WidgetLifecycleState::Interactive)) {
                desiredStates.insert_or_assign(pinnedId, pinnedState);
            }
        }

        sessions_.SetLifecycleTargets(
            desiredStates, deferColdWidgetStart, correlationId,
            correlationWidgetId);
    }

    void ProcessWidgetSessionEvents() {
        for (auto& event : sessions_.TakeEvents()) {
            if (event.kind == gba::WidgetSessionEventKind::CatalogChanged && event.catalog) {
                KillTimer(window_, kCatalogRetryTimer);
                sessions_.ResetCatalogRetry();
                ApplyWidgetCatalogChange(*event.catalog);
                RefreshCurrentBridgeSnapshot();
                continue;
            }
            if (event.kind == gba::WidgetSessionEventKind::Failed) {
                if (event.widgetId.empty()) {
                    AppendDiagnostic(L"Widget catalog failed: " + event.failure.safeMessage);
                    const bool active = state_.surface() != gba::Surface::Hidden ||
                                        pinnedSurfaceCoordinator_.pinned();
                    const auto delay = sessions_.NextCatalogRetryDelay(
                        false, bridge_.HasWidgetCatalogChangedRevisionInFlight(), active);
                    if (delay) {
                        SetTimer(window_, kCatalogRetryTimer, *delay, nullptr);
                    } else {
                        bridge_.AbandonWidgetCatalogChangedRevision();
                    }
                    continue;
                }
                RecordWidgetStartupFailure(
                    event.widgetId, event.failure.safeMessage, event.failure.stage);
                if (pinnedSurfaceCoordinator_.pinned() &&
                    pinnedSurfaceCoordinator_.widgetId() == event.widgetId) {
                    (void)pinnedSurfaceCoordinator_.Unpin(
                        gba::pinned::WidgetSurfaceStopReason::WorkerUnavailable);
                }
                continue;
            }
            if (event.kind == gba::WidgetSessionEventKind::StaleCompletionRejected) {
                AppendDiagnostic(
                    L"Dropped stale widget session completion for " + event.widgetId);
                continue;
            }
            if (event.kind == gba::WidgetSessionEventKind::Restarted) {
                RefreshAndApplyPresentation([&] { SyncWidgetActivity(); });
                continue;
            }
            if (event.kind != gba::WidgetSessionEventKind::SnapshotAdmitted) continue;
            const auto* current = SnapshotFor(event.widgetId);
            if (!current) continue;
            if (event.completedRestart) {
                lastActionWidgetId_ = event.widgetId;
                lastActionMessage_ =
                    std::wstring(DisplayWidgetName(event.widgetId)) + L" reloaded";
                lastActionExpiresAt_ = GetTickCount64() + 1800;
                AppendDiagnostic(lastActionMessage_);
            }
            if (pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == event.widgetId) {
                (void)pinnedSurfaceCoordinator_.UpdateSnapshot(
                    event.widgetId,
                    pinnedSurfaceCoordinator_.runtimeGeneration(),
                    *current);
            }
            const auto currentWidget = state_.surface() == gba::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget();
            if (currentWidget != event.widgetId) {
                admissionTrace_.RecordAdmissionPresentation(
                    event.correlationId, event.widgetId, false, GetTickCount64());
                continue;
            }
            CommitAdmittedWidgetPresentation(event.widgetId);
            if (pendingContentRevealWidget_ == event.widgetId) {
                pendingContentRevealWidget_.clear();
                overlayTransition_.BeginContentReveal(
                    GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
                AdvanceOverlayTransition(GetTickCount64());
            }
            RestoreFocusForActiveSurface(event.widgetId);
            if (pressedInteraction_.Reconcile(*current, focusedElementId_))
                InvalidateRect(window_, nullptr, FALSE);
            RefreshAndApplyPresentation([] {});
            admissionTrace_.RecordAdmissionPresentation(
                event.correlationId, event.widgetId, true, GetTickCount64());
        }
    }

    std::wstring_view DisplayWidgetName(const std::wstring_view id) const noexcept {
        const auto* descriptor = sessions_.FindDescriptor(id);
        return descriptor ? descriptor->name : WidgetName(id);
    }

    [[nodiscard]] std::optional<gba::packages::LocalWidgetPackageOrigin>
    CurrentLocalWidgetPackageImportOrigin() const {
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget ||
            state_.activeWidget() != L"settings") return std::nullopt;
        const auto* descriptor = sessions_.FindDescriptor(L"settings");
        const auto lifecycle = sessions_.Lifecycle(L"settings");
        if (!descriptor || !lifecycle ||
            *lifecycle != gba::WidgetLifecycleState::Interactive)
            return std::nullopt;
        return gba::packages::LocalWidgetPackageOrigin{
            descriptor->id,
            L"org.gbar.firstparty.settings",
            L"org.gbar.firstparty",
            descriptor->instanceId,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            *lifecycle};
    }

    [[nodiscard]] bool TryInvokeLocalWidgetPackageImport(
        const gba::WidgetSnapshot& snapshot,
        const gba::WidgetNode& node,
        const std::wstring_view protocolButton,
        const gba::input::NavigationEventPhase phase) {
        const auto action = localWidgetPackageImport_.Invoke(
            window_,
            {
                CurrentLocalWidgetPackageImportOrigin(),
                snapshot.instanceId,
                snapshot.activeInputScopeId,
                node.actionId,
                node.id,
                std::wstring(protocolButton),
                phase == gba::input::NavigationEventPhase::Pressed,
                !node.isDisabled,
                node.isBusy,
            });
        if (!action.claimed) return false;
        lastActionWidgetId_ = L"settings";
        if (action.import.status !=
            gba::packages::LocalWidgetPackageImportStatus::Cancelled) {
            lastActionMessage_ = action.import.safeMessage;
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            if (!lastActionMessage_.empty())
                AppendDiagnostic(L"Local widget package action: " +
                                 lastActionMessage_);
        }
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    gba::icons::NativeIcon DisplayWidgetIcon(const std::wstring_view id) const noexcept {
        const auto* descriptor = sessions_.FindDescriptor(id);
        if (descriptor) {
            gba::icons::NativeIcon icon{};
            if (gba::icons::TryParseNativeIcon(descriptor->icon, icon)) return icon;
        }
        return WidgetIcon(id);
    }

    void ApplyTransitionWindowOpacity(const float opacityFactor) {
        const auto factor = std::clamp(opacityFactor, 0.0F, 1.0F);
        const BYTE baseOverlayOpacity =
            compositionSurface_.available() ? 255 : targetOverlayOpacity_;
        const auto overlayOpacity = static_cast<BYTE>(std::lround(
            static_cast<float>(baseOverlayOpacity) * factor));
        const auto backdropOpacity = static_cast<BYTE>(std::lround(
            static_cast<float>(targetBackdropOpacity_) * factor));
        if (!appliedOverlayOpacity_ || *appliedOverlayOpacity_ != overlayOpacity) {
            const bool composed = compositionSurface_.available();
            bool applied = false;
            if (composed) {
                gba::OverlayCompositionSurface::CommitTiming timing;
                applied = SUCCEEDED(compositionSurface_.CommitOpacity(
                    static_cast<float>(overlayOpacity) / 255.0F, timing));
            } else {
                applied = SetLayeredWindowAttributes(
                    window_, RGB(1, 2, 3), overlayOpacity,
                    LWA_ALPHA | LWA_COLORKEY) != FALSE;
            }
            if (applied) {
                appliedOverlayOpacity_ = overlayOpacity;
            } else {
                AppendDiagnostic(L"Overlay alpha update failed error=" +
                                 std::to_wstring(GetLastError()));
            }
        }
        if (!appliedBackdropOpacity_ ||
            *appliedBackdropOpacity_ != backdropOpacity) {
            if (SetLayeredWindowAttributes(
                    backdropWindow_, 0, backdropOpacity, LWA_ALPHA)) {
                appliedBackdropOpacity_ = backdropOpacity;
            } else {
                AppendDiagnostic(L"Backdrop alpha update failed error=" +
                                 std::to_wstring(GetLastError()));
            }
        }
    }

    void ReprimeOpenAfterRenderTargetLoss() {
        if (state_.surface() == gba::Surface::Hidden) return;
        awaitingSuccessfulOpenPaint_ = true;
        nextOpenPaintRetryAt_ = GetTickCount64() + 100;
        overlayTransition_.PrepareInitialOpen();
        overlayTransitionSample_ = overlayTransition_.Sample(
            GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
        ApplyTransitionWindowOpacity(0.0F);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void BeginOpenAfterSuccessfulPaint() {
        if (!awaitingSuccessfulOpenPaint_ ||
            state_.surface() == gba::Surface::Hidden) return;
        awaitingSuccessfulOpenPaint_ = false;
        nextOpenPaintRetryAt_ = 0;
        const auto now = GetTickCount64();
        overlayTransition_.BeginOpen(
            now, CurrentAccessibilityPolicy().reducedMotion);
        AdvanceOverlayTransition(now);
    }

    void AdvanceOverlayTransition(const ULONGLONG timestamp) {
        const auto previousContentOpacity =
            overlayTransitionSample_.contentOpacity;
        const auto previousExtent = PresentedPresentationExtentDip();
        overlayTransitionSample_ = overlayTransition_.Sample(
            timestamp, CurrentAccessibilityPolicy().reducedMotion);
        if (presentationTransaction_.hasActiveExtent()) {
            if (compositionSurface_.available()) {
                const auto step = presentationTransaction_.PrepareCompositionStep(
                    timestamp, CurrentAccessibilityPolicy().reducedMotion);
                if (step) {
                    const gba::OverlayCompositionSurface::VisualPresentation
                        presentation{
                            step->presentation.scaleX,
                            step->presentation.scaleY,
                            step->presentation.offsetX,
                            step->presentation.offsetY,
                            static_cast<float>(step->destinationPlacement.width),
                            static_cast<float>(step->destinationPlacement.height),
                        };
                    gba::OverlayCompositionSurface::CommitTiming timing;
                    const HRESULT result = compositionSurface_.CommitPresentation(
                        presentation, timing);
                    if (FAILED(result)) {
                        DisableCompositionFallback(
                            L"motion commit failed hresult=" +
                            std::to_wstring(static_cast<unsigned long>(result)));
                        presentationTransaction_.RejectCompositionAdmission();
                    } else {
                        AppendDiagnostic(
                        L"Composition motion step index=" +
                        std::to_wstring(step->index) +
                        L" presented=" +
                        std::to_wstring(static_cast<unsigned int>(
                            std::lround(
                                step->destinationPlacement.width *
                                step->presentation.scaleX))) + L"x" +
                        std::to_wstring(static_cast<unsigned int>(
                            std::lround(
                                step->destinationPlacement.height *
                                step->presentation.scaleY))) +
                        L" commit-us=" + std::to_wstring(timing.commitMicroseconds) +
                        L" waited=false redraw=false");
                        bool accepted = true;
                        bool transactionAccepted = false;
                        if (step->finalFrame) {
                        const auto settledContainer = step->destinationPlacement;
                        const gba::OverlayCompositionSurface::VisualPresentation
                            settledPresentation{
                                1.0F, 1.0F,
                                static_cast<float>(step->destinationPlacement.x -
                                    settledContainer.x),
                                static_cast<float>(step->destinationPlacement.y -
                                    settledContainer.y),
                                static_cast<float>(step->destinationPlacement.width),
                                static_cast<float>(step->destinationPlacement.height),
                            };
                        gba::OverlayCompositionSurface::CommitTiming settleTiming;
                        const HRESULT settleResult =
                            compositionSurface_.CommitPresentation(
                                settledPresentation, settleTiming);
                        BOOL placed = FALSE;
                        if (SUCCEEDED(settleResult)) {
                            // Publish settled transaction state before the
                            // synchronous WM_WINDOWPOSCHANGED/UIA projection.
                            presentationTransaction_.AcceptCompositionStep(*step);
                            transactionAccepted = true;
                            struct PlacementGuard final {
                                bool& active;
                                explicit PlacementGuard(bool& value)
                                    : active(value) { active = true; }
                                ~PlacementGuard() { active = false; }
                            } guard(compositionPlacementInProgress_);
                            placed = SetWindowPos(
                                window_, nullptr,
                                settledContainer.x,
                                settledContainer.y,
                                settledContainer.width,
                                settledContainer.height,
                                SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOREDRAW |
                                    SWP_NOZORDER);
                            if (placed)
                                presentationTransaction_.SettleCompositionContainer(
                                    settledContainer);
                        }
                        accepted = SUCCEEDED(settleResult) && placed;
                        if (!accepted) {
                            DisableCompositionFallback(
                                L"motion settlement failed hresult=" +
                                std::to_wstring(static_cast<unsigned long>(
                                    settleResult)) +
                                L" error=" + std::to_wstring(GetLastError()));
                            presentationTransaction_.RejectCompositionAdmission();
                        }
                        if (accepted) {
                        AppendDiagnostic(
                            L"Composition motion final steps=" +
                            std::to_wstring(step->index) +
                            L" geometry=destination-settled bounds=" +
                            std::to_wstring(step->destinationPlacement.x) + L"," +
                            std::to_wstring(step->destinationPlacement.y) + L"," +
                            std::to_wstring(step->destinationPlacement.width) + L"," +
                            std::to_wstring(step->destinationPlacement.height) +
                            L" commit-us=" +
                            std::to_wstring(settleTiming.commitMicroseconds) +
                            L" waited=false redraw=false alpha=premultiplied-clear");
                        }
                        }
                        if (accepted) {
                            if (!transactionAccepted)
                                presentationTransaction_.AcceptCompositionStep(*step);
                            if (accessibilityActive_ &&
                                !accessibilityTree_.widgetId.empty()) {
                                const float pixelScale =
                                    static_cast<float>(
                                        step->destinationPlacement.width) /
                                    static_cast<float>(std::max(
                                        1, DesiredPresentationExtentDip().widthDip));
                                (void)PublishAccessibilityTree(pixelScale);
                            }
                            AppendCompositionCoordinateSample(step->index);
                        }
                    }
                }
            } else {
                (void)presentationTransaction_.AdvanceFallbackExtent(
                    timestamp, CurrentAccessibilityPolicy().reducedMotion);
                const auto nextExtent = PresentedPresentationExtentDip();
                if (state_.surface() != gba::Surface::Hidden &&
                    previousExtent != nextExtent) {
                    const auto result = ShowOverlay(true);
                    if (result == OverlayShowResult::Shown) {
                        RedrawWindow(window_, nullptr, nullptr,
                            RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
                    }
                }
            }
        }
        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        if (state_.surface() != gba::Surface::Hidden &&
            std::abs(previousContentOpacity -
                     overlayTransitionSample_.contentOpacity) > 0.0001F) {
            InvalidateRect(window_, nullptr, FALSE);
        }
        if (overlayTransition_.TakeHideCompletion() &&
            state_.surface() == gba::Surface::Hidden) {
            HideOverlay();
        }
    }

    void RequestWidgetContentReveal(const std::wstring_view widgetId) {
        if (widgetId.empty()) return;
        if (SnapshotFor(widgetId)) {
            pendingContentRevealWidget_.clear();
            overlayTransition_.BeginContentReveal(
                GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
        } else {
            pendingContentRevealWidget_ = widgetId;
            // Retained content (or first-open startup status) is host
            // presentation, not the incoming immutable widget snapshot. Keep
            // it stable and reveal only once that runtime publishes content.
            overlayTransition_.SnapContentVisible();
        }
        AdvanceOverlayTransition(GetTickCount64());
    }

    void SnapWidgetContentVisible() {
        pendingContentRevealWidget_.clear();
        overlayTransition_.SnapContentVisible();
        AdvanceOverlayTransition(GetTickCount64());
    }

    bool PresentCompositionPlacement(
        const gba::OverlayPlacement& placement,
        const gba::OverlayPlacement& containerPlacement,
        const UINT dpi,
        const bool wasVisible) {
        const unsigned int priorWidth = compositionSurface_.width();
        const unsigned int priorHeight = compositionSurface_.height();
        struct PlacementGuard final {
            bool& active;
            explicit PlacementGuard(bool& value) : active(value) { active = true; }
            ~PlacementGuard() { active = false; }
        } guard(compositionPlacementInProgress_);
        if (!EnsureCompositionChromeSession()) {
            DisableCompositionFallback(L"session chrome layout unavailable");
            return false;
        }
        // The content endpoint owns only the active envelope. Fixed guide/tray
        // chrome is committed in its companion target and never expands this HWND.
        const auto composedContainer = containerPlacement;
        CompositionFrameSet frames;
        std::uint64_t drawMicroseconds{};
        const float destinationOffsetX = static_cast<float>(
            placement.x - composedContainer.x);
        const float destinationOffsetY = static_cast<float>(
            placement.y - composedContainer.y);
        if (!RenderCompositionFrames(
                static_cast<unsigned int>(placement.width),
                static_cast<unsigned int>(placement.height), dpi,
                destinationOffsetX, destinationOffsetY,
                composedContainer,
                frames, drawMicroseconds)) {
            DisableCompositionFallback(L"destination draw failed before geometry");
            return false;
        }

        const auto geometryStarted = std::chrono::steady_clock::now();
        const auto directive =
            presentationTransaction_.PrepareCompositionAdmission(
                priorWidth, priorHeight, placement, composedContainer,
                DesiredPresentationExtentDip(), GetTickCount64(),
                CurrentAccessibilityPolicy().reducedMotion, wasVisible);
        const auto& motion = directive.initialPresentation;
        gba::OverlayCompositionSurface::VisualPresentation presentation{
            motion.scaleX, motion.scaleY, motion.offsetX, motion.offsetY,
            static_cast<float>(placement.width),
            static_cast<float>(placement.height),
        };
        gba::OverlayCompositionSurface::CommitTiming commitTiming;
        std::vector<gba::OverlayCompositionSurface::Frame*> framePointers;
        framePointers.reserve(frames.frames.size());
        for (auto& frame : frames.frames) framePointers.push_back(&frame);
        const HRESULT commitResult = compositionSurface_.CommitFrames(
            framePointers, !wasVisible, commitTiming, &presentation);
        if (FAILED(commitResult)) {
            presentationTransaction_.RejectCompositionAdmission();
            DisableCompositionFallback(
                L"destination commit failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(commitResult)));
            return false;
        }

        const int containerWidth = composedContainer.width;
        const int containerHeight = composedContainer.height;
        const int containerX = composedContainer.x;
        const int containerY = composedContainer.y;
        const BOOL placed = SetWindowPos(
            window_, nullptr,
            containerX, containerY, containerWidth, containerHeight,
            SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOREDRAW | SWP_NOZORDER);
        const auto geometryMicroseconds = static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now() - geometryStarted).count());
        if (!placed) {
            presentationTransaction_.RejectCompositionAdmission();
            AppendDiagnostic(
                L"Committed composition surface could not be placed error=" +
                std::to_wstring(GetLastError()));
            return false;
        }
        retainedGuidePaintKey_ = std::move(frames.guideKey);
        retainedTrayPaintState_ = std::move(frames.trayState);
        presentationTransaction_.AcceptCompositionAdmission(
            directive,
            state_.surface() == gba::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget());
        if (accessibilityActive_ && !accessibilityTree_.widgetId.empty()) {
            const float pixelScale = static_cast<float>(placement.width) /
                static_cast<float>(std::max(
                    1, directive.destinationExtentDip.widthDip));
            (void)PublishAccessibilityTree(pixelScale, true);
        }

        const int visibleContentX = containerX + static_cast<int>(
            std::lround(presentation.offsetX));
        const int visibleContentY = containerY + static_cast<int>(
            std::lround(presentation.offsetY));
        const int visibleContentWidth = static_cast<int>(std::lround(
            static_cast<float>(placement.width) * presentation.scaleX));
        const int visibleContentHeight = static_cast<int>(std::lround(
            static_cast<float>(placement.height) * presentation.scaleY));

        AppendDiagnostic(
            L"Composition placement committed content=complete from=" +
            std::to_wstring(priorWidth) + L"x" + std::to_wstring(priorHeight) +
            L" to=" + std::to_wstring(placement.width) + L"x" +
            std::to_wstring(placement.height) +
            L" order=" + (directive.animateMotion
                ? L"commit-motion-container"
                : L"commit-place") +
            L" draw-us=" + std::to_wstring(drawMicroseconds) +
            L" commit-us=" + std::to_wstring(commitTiming.commitMicroseconds) +
            L" geometry-us=" + std::to_wstring(geometryMicroseconds) +
            L" waited=" + (commitTiming.waitedForCompletion ? L"true" : L"false") +
            L" alpha=premultiplied-clear" +
            L" host-bounds=" + std::to_wstring(containerX) + L"," +
            std::to_wstring(containerY) + L"," +
            std::to_wstring(containerWidth) + L"," +
            std::to_wstring(containerHeight) +
            L" visible-content-bounds=" + std::to_wstring(visibleContentX) + L"," +
            std::to_wstring(visibleContentY) + L"," +
            std::to_wstring(visibleContentWidth) + L"," +
            std::to_wstring(visibleContentHeight) +
            L" anchor=bottom first-visible=" +
            std::wstring(wasVisible ? L"false" : L"true"));
        if (directive.animateMotion) {
            AppendDiagnostic(
                L"Composition motion start from=" +
                std::to_wstring(priorWidth) + L"x" + std::to_wstring(priorHeight) +
                L" to=" + std::to_wstring(placement.width) + L"x" +
                std::to_wstring(placement.height) +
                L" container=" + std::to_wstring(containerWidth) + L"x" +
                std::to_wstring(containerHeight) +
                L" destination=complete waited=false redraw=false "
                L"alpha=premultiplied-clear");
        }
        // Keep the transaction's initial coordinate sample inside its own
        // motion interval. The prior order placed the next admission sample
        // before its motion-start boundary and could attribute that widget's
        // different fixed chrome width to the preceding transaction.
        AppendCompositionCoordinateSample(0);
        if (performanceCountersActive_) ++performanceSuccessfulFrames_;
        BeginOpenAfterSuccessfulPaint();
        return true;
    }

    OverlayShowResult ShowOverlay(const bool atomicVisibleTransition = false) {
        if (!placementRefreshGate_.TryEnter()) return OverlayShowResult::Deferred;
        struct PlacementScope final {
            gba::PlacementRefreshGate& gate;
            HWND notifyWindow;
            ~PlacementScope() {
                if (gate.Complete() && notifyWindow) {
                    PostMessageW(notifyWindow, kPlacementRefreshMessage, 0, 0);
                }
            }
        } placementScope{placementRefreshGate_, window_};

        const bool wasVisible = IsWindowVisible(window_) != FALSE;
        if (!wasVisible) {
            const HWND foreground = GetForegroundWindow();
            (void)GbaOverlayPlatformObserveForegroundTarget(
                platform_,
                reinterpret_cast<std::uintptr_t>(foreground),
                PlatformBoolean(foreground && IsWindow(foreground)));
        }

        if (!EnsureFixedChromeAnchor()) {
            AppendDiagnostic(L"Unable to resolve visible-session chrome anchor");
            return OverlayShowResult::Failed;
        }
        // Keep placement inputs valid if a composition setup failure below
        // retires the latched anchor while falling back to the legacy HWND.
        const auto fixedChrome = *fixedChromeAnchor_;
        const RECT& work = fixedChrome.workArea;
        const UINT dpi = fixedChrome.dpi;
        const float interfaceScale = fixedChrome.interfaceScale;
        const auto bodyTarget = DesiredWidgetSurfaceTarget();
        float desiredWidthDip = static_cast<float>(kPanelWidth);
        float desiredHeightDip = static_cast<float>(kDashboardHeight);
        if (state_.surface() == gba::Surface::Widget) {
            // A composition admission renders the complete destination once
            // at its authored viewport. Only its visual envelope interpolates
            // from the retained source; the destination layout never does.
            const auto layoutExtent = compositionSurface_.available()
                ? DesiredPresentationExtentDip()
                : PresentedPresentationExtentDip();
            desiredWidthDip = static_cast<float>(layoutExtent.widthDip);
            desiredHeightDip = static_cast<float>(layoutExtent.heightDip);
        }
        auto placement = ComputePlatformPlacement(
            work, dpi,
            desiredWidthDip * interfaceScale,
            desiredHeightDip * interfaceScale);
        if (!placement) {
            AppendDiagnostic(L"Unable to compute a safe overlay placement");
            return OverlayShowResult::Failed;
        }
        auto compositionContainer = ComputePlatformPlacement(
            work, dpi,
            std::max(
                desiredWidthDip * interfaceScale,
                static_cast<float>(compositionSurface_.width()) * 96.0F /
                    static_cast<float>(dpi)),
            std::max(
                desiredHeightDip * interfaceScale,
                static_cast<float>(compositionSurface_.height()) * 96.0F /
                    static_cast<float>(dpi)));
        if (compositionSurface_.available() && !compositionContainer) {
            AppendDiagnostic(L"Unable to compute the composition host container");
            return OverlayShowResult::Failed;
        }
        if (compositionSurface_.available() &&
            !EnsureCompositionChromeSession()) {
            DisableCompositionFallback(
                L"local fixed chrome session unavailable");
        }
        if (compositionSurface_.available()) {
            const auto anchoredPlacement = AnchorContentPlacementToChrome(
                *placement);
            const auto anchoredContainer = compositionContainer
                ? AnchorContentPlacementToChrome(
                    *compositionContainer)
                : std::nullopt;
            if (!anchoredPlacement || !anchoredContainer) {
                DisableCompositionFallback(
                    L"content could not be anchored above local fixed chrome");
            } else {
                placement = anchoredPlacement;
                compositionContainer = anchoredContainer;
            }
        }

        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        const BOOL backdropPlaced = SetWindowPos(
            backdropWindow_, nullptr,
            fixedChrome.monitorArea.left, fixedChrome.monitorArea.top,
            fixedChrome.monitorArea.right - fixedChrome.monitorArea.left,
            fixedChrome.monitorArea.bottom - fixedChrome.monitorArea.top,
            SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOZORDER);
        BOOL overlayPlaced = FALSE;
        if (compositionSurface_.available()) {
            overlayPlaced = PresentCompositionPlacement(
                *placement, *compositionContainer, dpi, wasVisible) ? TRUE : FALSE;
        }
        if (!compositionSurface_.available()) {
            const UINT overlayPlacementFlags = SWP_SHOWWINDOW | SWP_NOACTIVATE |
                (atomicVisibleTransition && wasVisible ? SWP_NOREDRAW : 0U);
            overlayPlaced = SetWindowPos(
                window_, HWND_TOPMOST, placement->x, placement->y,
                placement->width, placement->height,
                overlayPlacementFlags);
        }
        if (!backdropPlaced || !overlayPlaced) {
            AppendDiagnostic(L"Overlay placement failed error=" +
                             std::to_wstring(GetLastError()));
            return OverlayShowResult::Failed;
        }
        const auto surfaceRequest = CurrentWidgetSurfaceRequest();
        const auto axisName = [](const gba::WidgetSurfaceAxisMode mode) {
            switch (mode) {
            case gba::WidgetSurfaceAxisMode::Content: return L"content";
            case gba::WidgetSurfaceAxisMode::FillAvailable: return L"fillAvailable";
            case gba::WidgetSurfaceAxisMode::Preferred:
            default: return L"preferred";
            }
        };
        AppendDiagnostic(
            L"Overlay work-area placement work=" +
            std::to_wstring(work.left) + L"," + std::to_wstring(work.top) + L"," +
            std::to_wstring(work.right) + L"," + std::to_wstring(work.bottom) +
            L" host=" + std::to_wstring(compositionSurface_.available()
                ? compositionContainer->x : placement->x) + L"," +
            std::to_wstring(compositionSurface_.available()
                ? compositionContainer->y : placement->y) + L"," +
            std::to_wstring(compositionSurface_.available()
                ? compositionContainer->width : placement->width) + L"," +
            std::to_wstring(compositionSurface_.available()
                ? compositionContainer->height : placement->height) +
            L" dpi=" + std::to_wstring(dpi) +
            L" interface-scale=" + std::to_wstring(interfaceScale) +
            L" surface=" +
            (state_.surface() == gba::Surface::Widget ? L"widget" : L"dashboard") +
            L" shell=" +
            (state_.surface() == gba::Surface::Widget ? L"shared" : L"dashboard") +
            L" body-preferred=" + std::to_wstring(bodyTarget.panelWidthDip) + L"x" +
            std::to_wstring(bodyTarget.panelHeightDip) +
            L" intrinsic-passes=" +
            std::to_wstring(bodyTarget.intrinsicMeasurementPasses) +
            L" surface-axis=" + axisName(surfaceRequest
                ? surfaceRequest->widthMode
                : gba::WidgetSurfaceAxisMode::Preferred) + L"/" +
            axisName(surfaceRequest
                ? surfaceRequest->heightMode
                : gba::WidgetSurfaceAxisMode::Preferred));
        if (!wasVisible) {
            ShowWindow(backdropWindow_, SW_SHOWNOACTIVATE);
            ShowWindow(window_, SW_SHOWNORMAL);
            ShowWindow(chromeWindow_, compositionSurface_.available()
                ? SW_SHOWNOACTIVATE : SW_HIDE);
        }
        if (compositionSurface_.available() && !ApplyOverlayZOrder()) {
            AppendDiagnostic(
                L"Coordinated overlay Z-order placement failed error=" +
                std::to_wstring(GetLastError()));
            return OverlayShowResult::Failed;
        }
        if (!wasVisible) {
            // Activation is one best-effort show-time request. Controller
            // reliability comes from the visible GameInput lease, not a
            // repeated foreground-steal loop.
            (void)AcquireOverlayForegroundInput();
        }
        // A rapid Guide reopen can arrive while the closing HWND remains
        // visible. Successful placement always reacquires the platform's
        // visible input lease; only first-show presentation work is gated by
        // the HWND's prior visibility.
        (void)GbaOverlayPlatformSetWindowState(
            platform_,
            GBA_OVERLAY_PLATFORM_TRUE,
            PlatformBoolean(IsOverlayProcessForeground()));
        if (!wasVisible) {
            actionFailureFeedback_.Show();
            KillTimer(window_, kPinnedSurfaceTimer);
            SetTimer(window_, kControllerTimer, 16, nullptr);
            PrimeControllerState();
        }
        pinnedSurfaceCoordinator_.OnOverlayShown();
        return OverlayShowResult::Shown;
    }

    void HideOverlay() {
        localWidgetPackageImport_.CancelPicker();
        if (const auto operation = localWidgetPackageImport_.CancelActiveOperation()) {
            (void)bridge_.CancelLocalWidgetPackageInstall(
                *operation);
        }
        (void)pinnedSurfaceCoordinator_.CancelPlacement();
        pinnedSurfaceCoordinator_.OnOverlayHidden();
        KillTimer(window_, kControllerTimer);
        KillTimer(window_, kPinnedSurfaceTimer);
        if (pinnedSurfaceCoordinator_.pinned())
            SetTimer(window_, kPinnedSurfaceTimer, 100, nullptr);
        trayYGesture_.Reset();
        if (platform_) {
            (void)GbaOverlayPlatformSetWindowState(
                platform_,
                GBA_OVERLAY_PLATFORM_FALSE,
                GBA_OVERLAY_PLATFORM_FALSE);
        }
        actionFailureFeedback_.Hide();
        ClearAccessibilityTree();
        lastForegroundOwnership_.reset();
        declarativeMotionActive_ = false;
        pendingContentRevealWidget_.clear();
        awaitingSuccessfulOpenPaint_ = false;
        nextOpenPaintRetryAt_ = 0;
        KillTimer(window_, kCatalogRetryTimer);
        KillTimer(window_, kForegroundLossTimer);
        bridge_.AbandonWidgetCatalogChangedRevision();
        sessions_.ResetCatalogRetry();
        ShowWindow(window_, SW_HIDE);
        ShowWindow(chromeWindow_, SW_HIDE);
        ShowWindow(backdropWindow_, SW_HIDE);
        SetWindowPos(window_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(backdropWindow_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(chromeWindow_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        DiscardGraphicsResources();
        const HWND restoreTarget = reinterpret_cast<HWND>(
            GbaOverlayPlatformRememberedForegroundTarget(platform_));
        if (restoreTarget && IsWindow(restoreTarget)) {
            SetForegroundWindow(restoreTarget);
        }
    }

    void RetireCompositionMotionForHiddenState() {
        const bool retiredInFlightCompositionMotion =
            presentationTransaction_.RetireHidden();
        AppendDiagnostic(
            L"Composition motion retired state=hidden in-flight=" +
            std::wstring(retiredInFlightCompositionMotion ? L"true" : L"false") +
            L" future-work=false geometry=discarded");
    }

    void ScheduleActionFeedbackExpiry(
        const std::optional<std::uint64_t> next) {
        KillTimer(window_, kActionFeedbackTimer);
        if (!next) return;
        const auto now = GetTickCount64();
        const auto remaining = *next <= now ? 1ULL : *next - now;
        constexpr ULONGLONG maximumTimerDelay = 0x7FFFFFFFULL;
        SetTimer(
            window_,
            kActionFeedbackTimer,
            static_cast<UINT>(std::min(remaining, maximumTimerDelay)),
            nullptr);
    }

    [[nodiscard]] bool ApplyOverlayZOrder() {
        if (!window_ || !chromeWindow_ || !backdropWindow_ ||
            state_.surface() == gba::Surface::Hidden) return false;
        const auto flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW;
        HDWP positions = BeginDeferWindowPos(3);
        if (!positions) return false;
        positions = DeferWindowPos(
            positions, chromeWindow_, HWND_TOPMOST, 0, 0, 0, 0, flags);
        if (positions) {
            positions = DeferWindowPos(
                positions, window_, chromeWindow_, 0, 0, 0, 0, flags);
        }
        if (positions) {
            positions = DeferWindowPos(
                positions, backdropWindow_, window_, 0, 0, 0, 0, flags);
        }
        return positions && EndDeferWindowPos(positions) != FALSE;
    }

    void ReassertOverlayZOrder() {
        if (state_.surface() == gba::Surface::Hidden) return;
        if (!ApplyOverlayZOrder()) {
            AppendDiagnostic(L"Topmost reassertion failed error=" +
                             std::to_wstring(GetLastError()));
        }
    }

    [[nodiscard]] float CurrentTextScale() const noexcept {
        return appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->textScale)
            : 1.0F;
    }

    struct WidgetSurfaceResolutionCache final {
        std::wstring instanceId;
        long long sequence{};
        gba::WidgetSurfaceRequest request;
        gba::WidgetSurfaceConstraints constraints;
        gba::ResolvedWidgetSurface resolved;
    };

    [[nodiscard]] std::optional<gba::WidgetSurfaceRequest>
    WidgetSurfaceRequestForSnapshot(const gba::WidgetSnapshot& snapshot) const {
        if (!snapshot.surface) return std::nullopt;

        gba::WidgetSurfaceRequest request;
        if (snapshot.surface->mode == L"compact") {
            request.mode = gba::WidgetSurfaceMode::Compact;
        } else if (snapshot.surface->mode == L"standard") {
            request.mode = gba::WidgetSurfaceMode::Standard;
        } else if (snapshot.surface->mode == L"wide") {
            request.mode = gba::WidgetSurfaceMode::Wide;
        } else {
            // Unknown and empty values fail safely to Adaptive. The managed
            // validator rejects them earlier, but native parsing is still an
            // untrusted transport boundary.
            request.mode = gba::WidgetSurfaceMode::Adaptive;
        }
        const auto widthMode = snapshot.surface->widthMode
            ? gba::ParseWidgetSurfaceAxisMode(
                *snapshot.surface->widthMode, snapshot.protocolVersion)
            : std::optional{gba::WidgetSurfaceAxisMode::Preferred};
        const auto heightMode = snapshot.surface->heightMode
            ? gba::ParseWidgetSurfaceAxisMode(
                *snapshot.surface->heightMode, snapshot.protocolVersion)
            : std::optional{gba::WidgetSurfaceAxisMode::Preferred};
        if (!widthMode || !heightMode) return std::nullopt;
        request.widthMode = *widthMode;
        request.heightMode = *heightMode;
        const auto toFloat = [](const std::optional<double> value) -> std::optional<float> {
            if (!value || !std::isfinite(*value) ||
                *value > std::numeric_limits<float>::max() ||
                *value < -std::numeric_limits<float>::max()) return std::nullopt;
            return static_cast<float>(*value);
        };
        request.preferredWidthDip = toFloat(snapshot.surface->preferredWidth);
        request.preferredHeightDip = toFloat(snapshot.surface->preferredHeight);
        request.minimumWidthDip = toFloat(snapshot.surface->minimumWidth);
        request.minimumHeightDip = toFloat(snapshot.surface->minimumHeight);
        return request;
    }

    void CommitAdmittedWidgetPresentation(const std::wstring_view widgetId) {
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot) return;
        presentationTransaction_.RetainAdmittedWidget(
            widgetId,
            *snapshot,
            state_.focusRegion() == gba::FocusRegion::Widget
                ? std::wstring_view{focusedElementId_}
                : std::wstring_view{},
            WidgetSurfaceRequestForSnapshot(*snapshot));
    }

    [[nodiscard]] std::optional<gba::WidgetSurfaceRequest>
    CurrentWidgetSurfaceRequest() const {
        if (!IsBridgeWidget(state_.activeWidget())) return std::nullopt;
        const auto* snapshot = SnapshotFor(state_.activeWidget());
        switch (gba::ResolveWidgetExtentAuthority(
            snapshot != nullptr,
            presentationTransaction_.retainedSurfaceAvailable())) {
        case gba::WidgetExtentAuthority::AdmittedSnapshot:
            return WidgetSurfaceRequestForSnapshot(*snapshot);
        case gba::WidgetExtentAuthority::RetainedCommittedSurface:
            return presentationTransaction_.retainedSurfaceRequest();
        case gba::WidgetExtentAuthority::CompactStartupFallback:
            return gba::WidgetSurfaceRequest{gba::WidgetSurfaceMode::Compact};
        }
        return gba::WidgetSurfaceRequest{gba::WidgetSurfaceMode::Compact};
    }

    [[nodiscard]] gba::ResolvedWidgetSurface DesiredWidgetSurfaceTarget() const {
        const auto request = CurrentWidgetSurfaceRequest();
        if (!request ||
            (request->widthMode == gba::WidgetSurfaceAxisMode::Preferred &&
             request->heightMode == gba::WidgetSurfaceAxisMode::Preferred)) {
            return gba::ResolveWidgetSurfaceTarget(request, CurrentTextScale());
        }

        RECT workArea{};
        UINT dpi = 96;
        float interfaceScale = 1.0F;
        if (fixedChromeAnchor_) {
            workArea = fixedChromeAnchor_->workArea;
            dpi = fixedChromeAnchor_->dpi;
            interfaceScale = fixedChromeAnchor_->interfaceScale;
        } else {
            HWND monitorTarget = window_;
            if (platform_) {
                const HWND remembered = reinterpret_cast<HWND>(
                    GbaOverlayPlatformRememberedForegroundTarget(platform_));
                monitorTarget = reinterpret_cast<HWND>(
                    GbaOverlayPlatformResolveForegroundTarget(
                        platform_, reinterpret_cast<std::uintptr_t>(window_),
                        PlatformBoolean(remembered && IsWindow(remembered))));
            }
            const HMONITOR monitor = MonitorFromWindow(
                monitorTarget, MONITOR_DEFAULTTONEAREST);
            MONITORINFO monitorInfo{sizeof(monitorInfo)};
            if (!monitor || !GetMonitorInfoW(monitor, &monitorInfo))
                return gba::ResolveWidgetSurfaceTarget(request, CurrentTextScale());
            workArea = monitorInfo.rcWork;
            UINT dpiY = 96;
            if (FAILED(GetDpiForMonitor(
                    monitor, MDT_EFFECTIVE_DPI, &dpi, &dpiY)) ||
                dpi == 0 || dpiY == 0) {
                dpi = 96;
            }
            interfaceScale = appearanceState_.current()
                ? static_cast<float>(appearanceState_.current()->interfaceScale)
                : 1.0F;
        }
        const gba::WidgetSurfaceConstraints constraints{
            {workArea.left, workArea.top, workArea.right, workArea.bottom},
            dpi,
            interfaceScale,
            fixedChromeAnchor_
                ? fixedChromeAnchor_->textScale
                : CurrentTextScale(),
        };

        const gba::WidgetSnapshot* measurementSnapshot{};
        if (state_.surface() == gba::Surface::Widget) {
            const auto* admitted = SnapshotFor(state_.activeWidget());
            measurementSnapshot = admitted
                ? InteractionSnapshotFor(state_.activeWidget())
                : presentationTransaction_.retainedPresentation()
                    ? &presentationTransaction_.retainedPresentation()->snapshot
                    : nullptr;
        }
        const auto sameRequest = [](const gba::WidgetSurfaceRequest& left,
                                    const gba::WidgetSurfaceRequest& right) {
            return left.mode == right.mode &&
                left.widthMode == right.widthMode &&
                left.heightMode == right.heightMode &&
                left.preferredWidthDip == right.preferredWidthDip &&
                left.preferredHeightDip == right.preferredHeightDip &&
                left.minimumWidthDip == right.minimumWidthDip &&
                left.minimumHeightDip == right.minimumHeightDip;
        };
        const auto sameConstraints = [](const gba::WidgetSurfaceConstraints& left,
                                        const gba::WidgetSurfaceConstraints& right) {
            return left.workArea.left == right.workArea.left &&
                left.workArea.top == right.workArea.top &&
                left.workArea.right == right.workArea.right &&
                left.workArea.bottom == right.workArea.bottom &&
                left.dpi == right.dpi &&
                left.interfaceScale == right.interfaceScale &&
                left.textScale == right.textScale &&
                left.margins.side == right.margins.side &&
                left.margins.top == right.margins.top &&
                left.margins.bottom == right.margins.bottom;
        };
        const std::wstring_view instanceId = measurementSnapshot
            ? std::wstring_view{measurementSnapshot->instanceId}
            : std::wstring_view{};
        const long long sequence = measurementSnapshot
            ? measurementSnapshot->sequence
            : -1;
        if (widgetSurfaceResolutionCache_ &&
            widgetSurfaceResolutionCache_->instanceId == instanceId &&
            widgetSurfaceResolutionCache_->sequence == sequence &&
            sameRequest(widgetSurfaceResolutionCache_->request, *request) &&
            sameConstraints(widgetSurfaceResolutionCache_->constraints, constraints)) {
            return widgetSurfaceResolutionCache_->resolved;
        }
        gba::WidgetSurfaceIntrinsicMeasure measure;
        if (measurementSnapshot && declarativeRenderer_ &&
            (request->widthMode == gba::WidgetSurfaceAxisMode::Content ||
             request->heightMode == gba::WidgetSurfaceAxisMode::Content)) {
            const bool intrinsicWidth =
                request->widthMode == gba::WidgetSurfaceAxisMode::Content;
            measure = [this, measurementSnapshot, dpi, interfaceScale, intrinsicWidth](
                const float maximumWidthDip,
                const float maximumHeightDip)
                -> std::optional<gba::WidgetSurfaceIntrinsicExtent> {
                gba::DeclarativeRenderOptions options;
                options.pixelScale = static_cast<float>(dpi) / 96.0F * interfaceScale;
                options.accessibility = CurrentAccessibilityPolicy();
                options.surfaceBackground = effectivePanelBackground_;
                const auto measured = declarativeRenderer_->MeasureContent(
                    *measurementSnapshot,
                    {maximumWidthDip, maximumHeightDip},
                    intrinsicWidth,
                    options);
                if (!measured.succeeded) return std::nullopt;
                return gba::WidgetSurfaceIntrinsicExtent{
                    measured.extent.width, measured.extent.height};
            };
        }
        const auto resolved = gba::ResolveWidgetSurface(
            request, constraints, measure).value_or(
            gba::ResolveWidgetSurfaceTarget(request, CurrentTextScale()));
        widgetSurfaceResolutionCache_ = WidgetSurfaceResolutionCache{
            std::wstring{instanceId}, sequence, *request, constraints, resolved};
        return resolved;
    }

    [[nodiscard]] gba::OverlayPresentationExtent
    DesiredContentPanelExtentDip() const {
        const auto target = DesiredWidgetSurfaceTarget();
        const auto shellGeometry = gba::ComputeOverlaySurfaceGeometry(
            target.windowWidthDip, target.windowHeightDip,
            target.panelWidthDip, target.panelHeightDip);
        if (!shellGeometry) {
            return {
                std::max(1, static_cast<int>(std::lround(target.panelWidthDip))),
                std::max(1, static_cast<int>(std::lround(target.panelHeightDip))),
            };
        }
        return {
            std::max(1, static_cast<int>(std::lround(shellGeometry->panelWidth))),
            std::max(1, static_cast<int>(std::lround(
                shellGeometry->footerY - shellGeometry->panelY))),
        };
    }

    [[nodiscard]] static std::optional<gba::OverlaySurfaceGeometry>
    ComputePanelLocalSurfaceGeometry(
        const float viewportWidthDip,
        const float viewportHeightDip) noexcept {
        if (!std::isfinite(viewportWidthDip) ||
            !std::isfinite(viewportHeightDip) ||
            viewportWidthDip <= 0.0F || viewportHeightDip <= 0.0F) {
            return std::nullopt;
        }
        const float contentInset = std::min(
            1.0F, std::min(viewportWidthDip, viewportHeightDip) * 0.1F);
        return gba::OverlaySurfaceGeometry{
            0.0F,
            0.0F,
            viewportWidthDip,
            viewportHeightDip,
            0.0F,
            0.0F,
            contentInset,
            contentInset,
            std::max(0.0F, viewportWidthDip - contentInset * 2.0F),
            std::max(0.0F, viewportHeightDip - contentInset),
            viewportHeightDip,
            0.0F,
        };
    }

    [[nodiscard]] std::optional<gba::OverlaySurfaceGeometry>
    ComputeCurrentWidgetSurfaceGeometry(
        const float viewportWidthDip,
        const float viewportHeightDip) const {
        if (compositionSurface_.available()) {
            return ComputePanelLocalSurfaceGeometry(
                viewportWidthDip, viewportHeightDip);
        }
        const auto target = DesiredWidgetSurfaceTarget();
        return gba::ComputeOverlaySurfaceGeometry(
            viewportWidthDip, viewportHeightDip,
            target.panelWidthDip, target.panelHeightDip);
    }

    [[nodiscard]] gba::OverlayPresentationExtent DesiredPresentationExtentDip() const {
        if (state_.surface() != gba::Surface::Widget) {
            return {kPanelWidth, kDashboardHeight};
        }
        const auto target = DesiredWidgetSurfaceTarget();
        if (compositionSurface_.available()) {
            return DesiredContentPanelExtentDip();
        }
        return {
            static_cast<int>(std::lround(target.windowWidthDip)),
            static_cast<int>(std::lround(target.windowHeightDip)),
        };
    }

    [[nodiscard]] gba::OverlayPresentationExtent
    PresentedPresentationExtentDip() const {
        return presentationTransaction_.PresentedExtent(
            DesiredPresentationExtentDip(), compositionSurface_.available());
    }

    void BeginWidgetExtentTransition(
        const gba::OverlayPresentationExtent from,
        const gba::OverlayPresentationExtent target) {
        presentationTransaction_.BeginExtentTransition(
            from, target, GetTickCount64(),
            CurrentAccessibilityPolicy().reducedMotion,
            compositionSurface_.available());
    }

    void ApplyPresentation(const gba::OverlayPresentationDirective directive) {
        switch (directive) {
        case gba::OverlayPresentationDirective::None:
            return;
        case gba::OverlayPresentationDirective::Hide:
            // Lifecycle/background state was committed before presentation.
            // Shut down semantic input immediately, then defer only HWND
            // hiding, resource discard, and foreground restoration.
            if (platform_) {
                (void)GbaOverlayPlatformSetWindowState(
                    platform_,
                    GBA_OVERLAY_PLATFORM_FALSE,
                    GBA_OVERLAY_PLATFORM_FALSE);
            }
            lastForegroundOwnership_.reset();
            RetireCompositionMotionForHiddenState();
            overlayTransition_.BeginClose(
                GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
            SetTimer(window_, kControllerTimer, 16, nullptr);
            AdvanceOverlayTransition(GetTickCount64());
            return;
        case gba::OverlayPresentationDirective::Place:
        {
            const bool wasWindowVisible = IsWindowVisible(window_) != FALSE;
            const auto result = ShowOverlay(wasWindowVisible);
            if (result == OverlayShowResult::Failed && !wasWindowVisible &&
                state_.surface() != gba::Surface::Hidden) {
                AppendDiagnostic(
                    L"Initial overlay presentation failed; restoring hidden state");
                Dispatch(gba::Command::CloseOverlay);
                return;
            }
            if (result == OverlayShowResult::Shown &&
                compositionSurface_.available()) {
                // ShowOverlay already rendered and transactionally committed
                // the complete destination before exposing its final HWND.
                ValidateRect(window_, nullptr);
                return;
            }
            InvalidateRect(window_, nullptr, FALSE);
            const bool initialFrameCommitRequired =
                result == OverlayShowResult::Shown &&
                !wasWindowVisible && awaitingSuccessfulOpenPaint_;
            if (initialFrameCommitRequired ||
                gba::ShouldCommitVisiblePlacementSynchronously(
                    wasWindowVisible, directive)) {
                RedrawWindow(window_, nullptr, nullptr,
                    RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
            }
            return;
        }
        case gba::OverlayPresentationDirective::Repaint:
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
    }

    template <typename Refresh>
    void RefreshAndApplyPresentation(Refresh&& refresh) {
        const bool wasVisible = state_.surface() != gba::Surface::Hidden;
        const auto priorDesiredExtent = DesiredPresentationExtentDip();
        const auto priorExtent = compositionSurface_.available()
            ? presentationTransaction_.CommittedDestinationExtent(
                priorDesiredExtent)
            : priorDesiredExtent;
        const auto priorPresentedExtent = PresentedPresentationExtentDip();
        const auto& committedDestination =
            presentationTransaction_.committedDestination();
        const std::wstring priorWidget = committedDestination
            ? committedDestination->widgetId
            : state_.surface() == gba::Surface::Widget
                ? std::wstring(state_.activeWidget())
                : std::wstring(state_.selectedWidget());
        std::forward<Refresh>(refresh)();
        const bool isVisible = state_.surface() != gba::Surface::Hidden;
        const auto nextExtent = DesiredPresentationExtentDip();
        const bool animateWidgetExtent =
            wasVisible && isVisible &&
            state_.surface() == gba::Surface::Widget &&
            priorExtent != nextExtent;
        if (animateWidgetExtent)
            BeginWidgetExtentTransition(priorPresentedExtent, nextExtent);
        const auto presentation = gba::DecideOverlayPresentation(
            wasVisible, isVisible, priorExtent, nextExtent);
        if (wasVisible && isVisible && priorExtent != nextExtent) {
            const std::wstring currentWidget = state_.surface() == gba::Surface::Widget
                ? std::wstring(state_.activeWidget())
                : std::wstring(state_.selectedWidget());
            AppendDiagnostic(
                L"Widget presentation extent refresh widget=" + currentWidget +
                L" from=" + std::to_wstring(priorPresentedExtent.widthDip) + L"x" +
                std::to_wstring(priorPresentedExtent.heightDip) + L" to=" +
                std::to_wstring(nextExtent.widthDip) + L"x" +
                std::to_wstring(nextExtent.heightDip) + L" identity=" +
                (priorWidget == currentWidget ? L"retained" : L"changed") +
                L" target=" +
                (compositionSurface_.available()
                    ? (animateWidgetExtent && !CurrentAccessibilityPolicy().reducedMotion
                        ? L"composition-motion"
                        : L"composition-surface-commit")
                    : CurrentAccessibilityPolicy().reducedMotion
                    ? L"resize-in-place"
                    : L"animated-resize-in-place"));
        }
        ApplyPresentation(presentation);
        (void)ReconcileResponsiveFocusPersistence();
    }

    struct TrayPointerTarget final {
        std::size_t slot{};
        bool activate{};
    };

    [[nodiscard]] std::optional<gba::CompositionMotionPlan>
    CurrentCompositionMotionPlan() const {
        if (!compositionSurface_.available()) return std::nullopt;
        RECT client{};
        if (!window_ || !GetClientRect(window_, &client)) return std::nullopt;
        return presentationTransaction_.CurrentMotionPlan(
            static_cast<unsigned int>(client.right - client.left),
            static_cast<unsigned int>(client.bottom - client.top),
            DesiredPresentationExtentDip());
    }

    [[nodiscard]] std::optional<gba::CompositionChildCoordinateSpaces>
    CurrentCompositionChildCoordinates() const {
        const auto motion = CurrentCompositionMotionPlan();
        const auto& placement = presentationTransaction_.contentPlacement();
        if (!motion || !placement) return std::nullopt;
        const auto chromeOffset =
            presentationTransaction_.ChromeOffsetWithinContainer();
        const auto spaces = gba::PlanCompositionChildCoordinates(
            *motion,
            static_cast<unsigned int>(placement->width),
            static_cast<unsigned int>(placement->height),
            chromeOffset.x, chromeOffset.y);
        if (spaces.content.containerWidth == 0) return std::nullopt;
        auto result = spaces;
        RECT windowBounds{};
        if (compositionChromeSession_ && window_ && GetWindowRect(window_, &windowBounds)) {
            const auto guide = ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->guideClientBounds);
            const auto tray = ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->trayClientBounds);
            if (!guide || !tray) return std::nullopt;
            result.guideOffsetX = static_cast<float>(guide->left - windowBounds.left);
            result.guideOffsetY = static_cast<float>(guide->top - windowBounds.top);
            result.trayOffsetX = static_cast<float>(tray->left - windowBounds.left);
            result.trayOffsetY = static_cast<float>(tray->top - windowBounds.top);
        }
        return result;
    }

    void AppendCompositionCoordinateSample(const std::size_t stepIndex) {
        const auto spaces = CurrentCompositionChildCoordinates();
        RECT windowBounds{};
        if (!spaces || !window_ || !GetWindowRect(window_, &windowBounds)) return;
        const auto contentSize = presentationTransaction_.contentPlacement();
        if (!contentSize) return;
        const auto screenPoint = [&](const gba::CompositionPoint local) {
            return gba::CompositionPoint{
                static_cast<float>(windowBounds.left) + local.x,
                static_cast<float>(windowBounds.top) + local.y,
            };
        };
        const auto contentOrigin = screenPoint(
            gba::ProjectContentPoint(*spaces, {0.0F, 0.0F}));
        const auto actualGuide = compositionChromeSession_
            ? ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->guideClientBounds)
            : std::nullopt;
        const auto actualTray = compositionChromeSession_
            ? ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->trayClientBounds)
            : std::nullopt;
        const auto actualChrome = ActualChromeWindowBounds();
        if (!actualGuide || !actualTray || !actualChrome) return;
        bool chromeExact = false;
        if (fixedChromeAnchor_) {
            fixedChromeAnchor_->actualWindowBounds = *actualChrome;
            chromeExact = EqualRect(
                &fixedChromeAnchor_->intendedWindowBounds,
                &fixedChromeAnchor_->actualWindowBounds) != FALSE;
            if (!chromeExact) {
                AppendDiagnostic(
                    L"Fixed chrome applied rectangle mismatch intended=" +
                    FormatPhysicalBounds(
                        fixedChromeAnchor_->intendedWindowBounds) +
                    L" actual=" + FormatPhysicalBounds(
                        fixedChromeAnchor_->actualWindowBounds) +
                    L" placement-reason=" + std::wstring(
                        FixedChromePlacementReasonName(
                            fixedChromeAnchor_->reason)));
            }
        }
        const gba::CompositionPoint guideOrigin{
            static_cast<float>(actualGuide->left),
            static_cast<float>(actualGuide->top),
        };
        const gba::CompositionPoint trayOrigin{
            static_cast<float>(actualTray->left),
            static_cast<float>(actualTray->top),
        };
        std::wstring selectedBounds = L"missing";
        if (retainedTrayPaintState_) {
            const auto selected = std::find_if(
                retainedTrayPaintState_->items.begin(),
                retainedTrayPaintState_->items.end(),
                [](const gba::shell::RetainedTrayItem& item) {
                    return item.selected;
                });
            if (selected != retainedTrayPaintState_->items.end()) {
                selectedBounds =
                    std::to_wstring(static_cast<int>(trayOrigin.x) +
                                    selected->bounds.left) + L"," +
                    std::to_wstring(static_cast<int>(trayOrigin.y) +
                                    selected->bounds.top) + L"," +
                    std::to_wstring(selected->bounds.right - selected->bounds.left) +
                    L"," +
                    std::to_wstring(selected->bounds.bottom - selected->bounds.top);
            }
        }
        const auto localProbe = gba::CompositionPoint{
            static_cast<float>(contentSize->width) * 0.5F,
            static_cast<float>(contentSize->height) * 0.5F,
        };
        const auto presentedProbe = gba::ProjectContentPoint(*spaces, localProbe);
        const auto inverseProbe = gba::InverseContentPoint(*spaces, presentedProbe);
        const auto counters = compositionSurface_.paintCounters();
        AppendDiagnostic(
            L"Composition child sample step=" + std::to_wstring(stepIndex) +
            L" content=" + std::to_wstring(static_cast<int>(contentOrigin.x)) + L"," +
            std::to_wstring(static_cast<int>(contentOrigin.y)) + L"," +
            std::to_wstring(static_cast<int>(std::lround(
                contentSize->width * spaces->content.scaleX))) + L"," +
            std::to_wstring(static_cast<int>(std::lround(
                contentSize->height * spaces->content.scaleY))) +
            L" guide=" + std::to_wstring(static_cast<int>(guideOrigin.x)) + L"," +
            std::to_wstring(static_cast<int>(guideOrigin.y)) + L"," +
            std::to_wstring(actualGuide->right - actualGuide->left) + L"," +
            std::to_wstring(actualGuide->bottom - actualGuide->top) +
            L" tray=" + std::to_wstring(static_cast<int>(trayOrigin.x)) + L"," +
            std::to_wstring(static_cast<int>(trayOrigin.y)) + L"," +
            std::to_wstring(actualTray->right - actualTray->left) + L"," +
            std::to_wstring(actualTray->bottom - actualTray->top) +
            L" chrome-hwnd=" + FormatPhysicalBounds(*actualChrome) +
            L" chrome-placement-reason=" +
            std::wstring(fixedChromeAnchor_
                ? FixedChromePlacementReasonName(fixedChromeAnchor_->reason)
                : L"missing") +
            L" chrome-placement-count=" +
            std::to_wstring(fixedChromePlacementCount_) +
            L" chrome-applied-exact=" +
            (chromeExact ? std::wstring{L"true"} : L"false") +
            L" selected=" + selectedBounds +
            L" pointer-local=" +
            std::to_wstring(static_cast<int>(std::lround(inverseProbe.x))) + L"," +
            std::to_wstring(static_cast<int>(std::lround(inverseProbe.y))) +
            L" uia-content-transform=matched" +
            L" paints=content:" + std::to_wstring(counters.content) +
            L",guide:" + std::to_wstring(counters.guide) +
            L",tray:" + std::to_wstring(counters.tray));
    }

    [[nodiscard]] std::optional<TrayPointerTarget> HitTrayTarget(
        const float x,
        const float y,
        const float width,
        const float height,
        const gba::OverlaySurfaceGeometry* surfaceGeometry) const {
        const auto layout = gba::shell::ComputeTrayLayout(
            width, height, state_.order().size(), state_.selectedSlot(),
            surfaceGeometry
                ? std::optional<gba::shell::TrayBand>{gba::shell::TrayBand{
                    surfaceGeometry->trayY,
                    surfaceGeometry->trayY + surfaceGeometry->trayHeight,
                }}
                : std::nullopt);
        if (!layout) return std::nullopt;
        const auto* hit = gba::shell::HitTestTray(*layout, x, y);
        if (hit) return TrayPointerTarget{hit->slot, true};
        const auto* overflow = gba::shell::HitTestTrayOverflow(*layout, x, y);
        return overflow
            ? std::optional<TrayPointerTarget>{TrayPointerTarget{
                overflow->targetSlot, false}}
            : std::nullopt;
    }

    void HandlePointerActivation(const float clientX, const float clientY) {
        if (state_.surface() == gba::Surface::Hidden || !window_) return;
        RECT client{};
        if (!GetClientRect(window_, &client)) return;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto& contentPlacement = presentationTransaction_.contentPlacement();
        const int renderWidth = contentPlacement
            ? contentPlacement->width
            : client.right - client.left;
        const int renderHeight = contentPlacement
            ? contentPlacement->height
            : client.bottom - client.top;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            renderWidth, renderHeight,
            dpi, interfaceScale);
        if (!metrics) return;
        const auto childSpaces = CurrentCompositionChildCoordinates();
        float localX = clientX;
        float localY = clientY;
        if (childSpaces) {
            const auto local = gba::InverseContentPoint(
                *childSpaces, {localX, localY});
            localX = local.x;
            localY = local.y;
        }
        const float x = localX / metrics->physicalPixelsPerDip;
        const float y = localY / metrics->physicalPixelsPerDip;
        const auto trayPoint = childSpaces
            ? gba::InverseTrayPoint(*childSpaces, {clientX, clientY})
            : gba::CompositionPoint{clientX, clientY};
        const float trayPixelsPerDip = compositionChromeSession_
            ? compositionChromeSession_->pixelsPerDip
            : metrics->physicalPixelsPerDip;
        const float trayX = trayPoint.x / trayPixelsPerDip;
        const float trayY = trayPoint.y / trayPixelsPerDip;

        if (state_.surface() == gba::Surface::Widget) {
            const std::wstring widget{state_.activeWidget()};
            const auto* snapshot = InteractionSnapshotFor(widget);
            if (snapshot) {
                const auto hit = gba::input::FindPointerHitTarget(
                    x, y, snapshot->activeInputScopeId, lastWidgetRenderResult_);
                if (hit) {
                    if (state_.focusRegion() == gba::FocusRegion::Tray) {
                        Dispatch(gba::Command::Activate);
                    }
                    (void)pressedInteraction_.Clear();
                    sliderInteraction_.DeactivateAll();
                    launcherExperienceProjection_.ObserveFocusInput(
                        widget, GetTickCount64());
                    focusedElementId_ = hit->id;
                    focusMemory_.Remember(widget, *snapshot, focusedElementId_);
                    InvalidateRect(window_, nullptr, FALSE);
                    if (hit->enabled) DispatchControllerAction(L"A");
                    return;
                }
            }
        }

        std::optional<gba::OverlaySurfaceGeometry> surfaceGeometry;
        if (state_.surface() == gba::Surface::Widget) {
            surfaceGeometry = ComputeCurrentWidgetSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip);
        }
        std::optional<TrayPointerTarget> trayTarget;
        if (const auto sessionTray = CurrentCompositionTrayLayout()) {
            if (const auto* hit = gba::shell::HitTestTray(*sessionTray, trayX, trayY)) {
                trayTarget = TrayPointerTarget{hit->slot, true};
            } else if (const auto* overflow = gba::shell::HitTestTrayOverflow(
                           *sessionTray, trayX, trayY)) {
                trayTarget = TrayPointerTarget{overflow->targetSlot, false};
            }
        } else {
            trayTarget = HitTrayTarget(
                trayX, trayY,
                metrics->viewportWidthDip, metrics->viewportHeightDip,
                surfaceGeometry ? &*surfaceGeometry : nullptr);
        }
        if (!trayTarget || trayTarget->slot >= state_.order().size()) return;
        if (state_.surface() == gba::Surface::Widget &&
            state_.focusRegion() == gba::FocusRegion::Widget) {
            Dispatch(gba::Command::SampleWidgetBack);
        }
        if (state_.reorderMode()) Dispatch(gba::Command::Cancel);
        const std::wstring targetWidget = state_.order()[trayTarget->slot];
        if (!SelectTrayWidget(targetWidget)) return;
        if (trayTarget->activate && state_.selectedSlot() == trayTarget->slot) {
            Dispatch(gba::Command::Activate);
        }
    }

    [[nodiscard]] bool HitTestAuthoredCompositionSurface(
        float clientX,
        float clientY) const {
        RECT client{};
        if (!window_ || !GetClientRect(window_, &client)) return false;
        const auto& contentPlacement = presentationTransaction_.contentPlacement();
        const int renderWidth = contentPlacement
            ? contentPlacement->width
            : client.right - client.left;
        const int renderHeight = contentPlacement
            ? contentPlacement->height
            : client.bottom - client.top;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            renderWidth, renderHeight, dpi, interfaceScale);
        if (!metrics) return false;
        float contentX = clientX;
        float contentY = clientY;
        const auto spaces = CurrentCompositionChildCoordinates();
        if (spaces) {
            const auto local = gba::InverseContentPoint(
                *spaces, {contentX, contentY});
            contentX = local.x;
            contentY = local.y;
        }
        contentX /= metrics->physicalPixelsPerDip;
        contentY /= metrics->physicalPixelsPerDip;
        const auto guidePoint = spaces
            ? gba::InverseGuidePoint(*spaces, {clientX, clientY})
            : gba::CompositionPoint{clientX, clientY};
        const auto trayPoint = spaces
            ? gba::InverseTrayPoint(*spaces, {clientX, clientY})
            : gba::CompositionPoint{clientX, clientY};
        const float chromePixelsPerDip = compositionChromeSession_
            ? compositionChromeSession_->pixelsPerDip
            : metrics->physicalPixelsPerDip;
        const float guideX = guidePoint.x / chromePixelsPerDip;
        const float guideY = guidePoint.y / chromePixelsPerDip;
        const float trayX = trayPoint.x / chromePixelsPerDip;
        const float trayY = trayPoint.y / chromePixelsPerDip;
        const auto contains = [](const float x, const float y,
                                 const gba::declarative::Rect& bounds) {
            return x >= bounds.x && y >= bounds.y &&
                x <= bounds.x + bounds.width &&
                y <= bounds.y + bounds.height;
        };

        std::optional<gba::OverlaySurfaceGeometry> surface;
        if (state_.surface() == gba::Surface::Widget) {
            surface = ComputeCurrentWidgetSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip);
            if (surface && contains(contentX, contentY, {
                    surface->panelX, surface->panelY, surface->panelWidth,
                    surface->footerY - surface->panelY})) return true;
            if (compositionChromeSession_ && contains(guideX, guideY,
                    compositionChromeSession_->guideBounds)) return true;
            if (!compositionChromeSession_ && surface && contains(guideX, guideY, {
                    surface->panelX, surface->footerY, surface->panelWidth,
                    surface->footerHeight})) return true;
        }
        const auto tray = CurrentCompositionTrayLayout();
        if (tray) return contains(trayX, trayY, tray->stripBounds);
        const auto fallbackTray = gba::shell::ComputeTrayLayout(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            state_.order().size(), state_.selectedSlot(),
            surface
                ? std::optional<gba::shell::TrayBand>{gba::shell::TrayBand{
                    surface->trayY, surface->trayY + surface->trayHeight}}
                : std::nullopt);
        return fallbackTray && contains(trayX, trayY, fallbackTray->stripBounds);
    }

    void ToggleCurrentPinnedSurface() {
        if (pinnedSurfaceCoordinator_.pinned()) {
            const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
            if (state_.surface() != gba::Surface::Widget ||
                state_.activeWidget() != widgetId) {
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ = L"Only one pinned surface is currently supported. Press U to unpin it.";
                lastActionExpiresAt_ = GetTickCount64() + 4000;
                InvalidateRect(window_, nullptr, FALSE);
                return;
            }
            if (pinnedSurfaceCoordinator_.ToggleInteractionMode()) {
                if (pinnedSurfaceCoordinator_.interactionMode() ==
                    gba::pinned::InteractionMode::Focusable)
                    (void)pinnedSurfaceCoordinator_.EnterControllerFocus();
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ =
                    pinnedSurfaceCoordinator_.interactionMode() ==
                            gba::pinned::InteractionMode::Focusable
                        ? L"Pinned surface is interactive"
                        : L"Pinned surface is click-through";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
                SyncWidgetActivity();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        if (state_.surface() != gba::Surface::Widget) {
            lastActionWidgetId_.clear();
            lastActionMessage_ = L"Open a pinnable widget before pressing P";
            lastActionExpiresAt_ = GetTickCount64() + 3000;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        const std::wstring widgetId(state_.activeWidget());
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto* snapshot = SnapshotFor(widgetId);
        if (!descriptor ||
            !descriptor->pinningSupported || !snapshot) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = descriptor &&
                    descriptor->pinningSupported
                ? L"Pinned surface unavailable until the widget has loaded"
                : L"This widget does not support pinned surfaces";
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        std::wstring error;
        if (!pinnedSurfaceCoordinator_.Pin({
                descriptor->id,
                descriptor->instanceId,
                descriptor->runtimeGeneration,
                descriptor->presentationGeneration,
                descriptor->name,
                descriptor->pinningSupported,
                *snapshot,
            }, error)) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = error;
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(L"Pinned surface rejected for " + widgetId + L": " + error);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        lastActionWidgetId_ = widgetId;
        lastActionMessage_ = L"Pinned click-through surface created. Press P to interact or U to unpin.";
        lastActionExpiresAt_ = GetTickCount64() + 5000;
        SyncWidgetActivity();
        AppendDiagnostic(L"Pinned surface created for " + widgetId);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void DrainPinnedSurfaceInputs() {
        for (const auto& request : pinnedSurfaceCoordinator_.TakeInputRequests()) {
            const auto* snapshot = SnapshotFor(request.widgetId);
            const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
            const auto* node = snapshot
                ? gba::input::FindNodeInInputScope(
                      *snapshot, request.nodeId, request.activeInputScopeId)
                : nullptr;
            if (!pinnedSurfaceCoordinator_.pinned() ||
                state_.surface() == gba::Surface::Hidden ||
                pinnedSurfaceCoordinator_.interactionMode() !=
                    gba::pinned::InteractionMode::Focusable ||
                !descriptor || !snapshot || !node ||
                node->isDisabled || node->isBusy ||
                descriptor->runtimeGeneration != request.runtimeGeneration ||
                snapshot->sequence != request.snapshotSequence ||
                snapshot->activeInputScopeId != request.activeInputScopeId) {
                AppendDiagnostic(L"Dropped stale or unavailable pinned-surface input");
                continue;
            }
            const auto handled = bridge_.SendControllerInput(
                request.widgetId, request.protocolButton, L"pinnedSurface",
                request.nodeId, request.activeInputScopeId, request.snapshotSequence,
                ++controllerSequence_,
                static_cast<long long>(GetTickCount64() * 1000), L"pressed",
                request.requestedValue, request.origin);
            if (!handled) {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    L"Pinned action failed. Reopen the overlay and try again.", true);
                AppendDiagnostic(L"Pinned action transport failed for " +
                                 request.widgetId);
            } else if (*handled) {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    L"Pinned action completed.", false);
                RefreshAndApplyPresentation([&] {
                    RefreshWidgetSnapshot(request.widgetId);
                });
            } else {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    L"No pinned action is available here.", false);
            }
        }
    }

    void EmergencyHidePinnedSurfaces() {
        if (!pinnedSurfaceCoordinator_.pinned()) return;
        const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
        (void)pressedInteraction_.Clear();
        if (pinnedSurfaceCoordinator_.EmergencyHideAll()) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = L"Emergency hide removed all pinned surfaces";
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            SyncWidgetActivity();
            InvalidateRect(window_, nullptr, FALSE);
        }
    }

    void HandleKey(const UINT key, const bool repeated) {
        if (state_.surface() == gba::Surface::Hidden) return;
        if (!repeated && key == 'H' &&
            (GetKeyState(VK_CONTROL) & 0x8000) != 0 &&
            (GetKeyState(VK_SHIFT) & 0x8000) != 0) {
            EmergencyHidePinnedSurfaces();
            return;
        }
        if (pinnedSurfaceCoordinator_.placementMode() !=
            gba::pinned::PlacementMode::None) {
            bool changed = false;
            if (key == VK_LEFT)
                changed = pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Left);
            else if (key == VK_RIGHT)
                changed = pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Right);
            else if (key == VK_UP)
                changed = pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Up);
            else if (key == VK_DOWN)
                changed = pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Down);
            else if (!repeated && key == VK_RETURN) {
                std::wstring error;
                changed = pinnedSurfaceCoordinator_.CommitPlacement(error);
                lastActionMessage_ = changed ? L"Pinned placement saved" : error;
                lastActionExpiresAt_ = GetTickCount64() + 3000;
            } else if (!repeated && key == VK_ESCAPE) {
                changed = pinnedSurfaceCoordinator_.CancelPlacement();
                lastActionMessage_ = L"Pinned placement canceled";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
            }
            if (changed) InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (!repeated && (key == 'M' || key == 'R') &&
            pinnedSurfaceCoordinator_.pinned() &&
            state_.surface() == gba::Surface::Widget &&
            state_.activeWidget() == pinnedSurfaceCoordinator_.widgetId()) {
            const auto mode = key == 'M'
                ? gba::pinned::PlacementMode::Move
                : gba::pinned::PlacementMode::Resize;
            if (pinnedSurfaceCoordinator_.BeginPlacement(mode)) {
                (void)pressedInteraction_.Clear();
                lastActionWidgetId_ = std::wstring(pinnedSurfaceCoordinator_.widgetId());
                lastActionMessage_ = key == 'M'
                    ? L"Move pinned surface: arrows, Enter commit, Esc cancel"
                    : L"Resize pinned surface: arrows, Enter commit, Esc cancel";
                lastActionExpiresAt_ = GetTickCount64() + 5000;
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        const auto phase = repeated
            ? gba::input::NavigationEventPhase::Repeated
            : gba::input::NavigationEventPhase::Pressed;
        if (key == 'P') {
            if (!repeated) ToggleCurrentPinnedSurface();
            return;
        }
        if (key == 'U') {
            if (!repeated && pinnedSurfaceCoordinator_.pinned()) {
                const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
                (void)pinnedSurfaceCoordinator_.Unpin(
                    gba::pinned::WidgetSurfaceStopReason::Unpin);
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ = L"Pinned surface removed";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
                SyncWidgetActivity();
            }
            return;
        }
        if (key == VK_F5) {
            if (!repeated) RestartCurrentWidget();
            return;
        }
        switch (gba::input::ResolveBasicKeyboardAction(key)) {
        case gba::input::BasicKeyboardAction::NavigateLeft:
            if (state_.surface() == gba::Surface::Widget &&
                state_.focusRegion() == gba::FocusRegion::Widget) {
                HandleWidgetDirection(gba::input::NavigationDirection::Left, phase, false);
            } else if (!repeated) {
                Dispatch(gba::Command::NavigateLeft);
            }
            break;
        case gba::input::BasicKeyboardAction::NavigateRight:
            if (state_.surface() == gba::Surface::Widget &&
                state_.focusRegion() == gba::FocusRegion::Widget) {
                HandleWidgetDirection(gba::input::NavigationDirection::Right, phase, false);
            } else if (!repeated) {
                Dispatch(gba::Command::NavigateRight);
            }
            break;
        case gba::input::BasicKeyboardAction::NavigateUp:
            if (!repeated && state_.surface() == gba::Surface::Widget) {
                if (state_.focusRegion() == gba::FocusRegion::Tray) {
                    Dispatch(gba::Command::Activate);
                } else {
                    HandleWidgetDirection(
                        gba::input::NavigationDirection::Up, phase, false);
                }
            }
            break;
        case gba::input::BasicKeyboardAction::NavigateDown:
            if (!repeated && state_.surface() == gba::Surface::Widget &&
                state_.focusRegion() == gba::FocusRegion::Widget) {
                HandleWidgetDirection(
                    gba::input::NavigationDirection::Down, phase, false);
            }
            break;
        case gba::input::BasicKeyboardAction::Activate:
            if (!repeated) DispatchControllerAction(L"A");
            break;
        case gba::input::BasicKeyboardAction::Back:
            if (!repeated) DispatchControllerAction(L"B");
            break;
        case gba::input::BasicKeyboardAction::None:
        default:
            break;
        }
    }

    [[nodiscard]] bool IsOverlayProcessForeground() const noexcept {
        const HWND foreground = GetForegroundWindow();
        if (!foreground) return false;
        DWORD foregroundProcess = 0;
        (void)GetWindowThreadProcessId(foreground, &foregroundProcess);
        return foregroundProcess == GetCurrentProcessId();
    }

    [[nodiscard]] bool AcquireOverlayForegroundInput() {
        if (!window_ || !IsWindow(window_) || !IsWindowVisible(window_)) return false;

        HWND foreground = GetForegroundWindow();
        DWORD foregroundProcess = 0;
        const DWORD foregroundThread = foreground
            ? GetWindowThreadProcessId(foreground, &foregroundProcess)
            : 0;
        const DWORD overlayThread = GetCurrentThreadId();
        const auto plan = gba::input::PlanForegroundAcquisition(
            foregroundProcess == GetCurrentProcessId(), overlayThread, foregroundThread);

        if (plan.attemptDirect) {
            (void)SetForegroundWindow(window_);
            (void)SetActiveWindow(window_);
            (void)SetFocus(window_);
        }

        if (!IsOverlayProcessForeground() && plan.attachForegroundThread) {
            // A Guide callback is delivered asynchronously and does not itself
            // grant the UI thread foreground rights. Join the current
            // foreground queue for one bounded activation attempt, then detach
            // immediately. This does not bypass Windows' foreground lock when
            // the OS declines the request.
            const BOOL attached = AttachThreadInput(
                overlayThread, foregroundThread, TRUE);
            if (attached) {
                (void)BringWindowToTop(window_);
                (void)SetForegroundWindow(window_);
                (void)SetActiveWindow(window_);
                (void)SetFocus(window_);
                (void)AttachThreadInput(overlayThread, foregroundThread, FALSE);
            }
        }

        const bool confirmed = IsOverlayProcessForeground();
        if (!lastForegroundOwnership_ || *lastForegroundOwnership_ != confirmed) {
            lastForegroundOwnership_ = confirmed;
            if (confirmed) {
                AppendDiagnostic(
                    GbaOverlayPlatformHasGameInput(platform_) !=
                            GBA_OVERLAY_PLATFORM_FALSE
                        ? L"Overlay foreground confirmed; ordinary controller input is "
                          L"GameInput foreground-exclusive"
                        : L"Overlay foreground confirmed; GameInput unavailable, ordinary "
                          L"controller input is non-exclusive XInput compatibility");
            } else {
                AppendDiagnostic(
                    L"Overlay foreground acquisition was not confirmed; the visible "
                    L"GameInput read lease remains active without exclusivity");
            }
        }
        return confirmed;
    }

    void PrimeControllerState() {
        if (!platform_) return;
        (void)GbaOverlayPlatformPrimeController(
            platform_,
            PlatformBoolean(IsOverlayProcessForeground()),
            GetTickCount64());
    }

    [[nodiscard]] static std::optional<gba::input::StickNavigationEvent>
    DecodeNavigation(
        const GbaOverlayPlatformNavigationEvent event) noexcept {
        gba::input::NavigationDirection direction{};
        switch (event.direction) {
        case GbaOverlayPlatformNavigationDirection::Left:
            direction = gba::input::NavigationDirection::Left;
            break;
        case GbaOverlayPlatformNavigationDirection::Right:
            direction = gba::input::NavigationDirection::Right;
            break;
        case GbaOverlayPlatformNavigationDirection::Up:
            direction = gba::input::NavigationDirection::Up;
            break;
        case GbaOverlayPlatformNavigationDirection::Down:
            direction = gba::input::NavigationDirection::Down;
            break;
        case GbaOverlayPlatformNavigationDirection::None:
        default:
            return std::nullopt;
        }
        const auto phase = event.phase ==
                GbaOverlayPlatformNavigationPhase::Repeated
            ? gba::input::NavigationEventPhase::Repeated
            : gba::input::NavigationEventPhase::Pressed;
        return gba::input::StickNavigationEvent{direction, phase};
    }

    void DispatchStickNavigation(const gba::input::StickNavigationEvent event) {
        using gba::input::NavigationDirection;
        const auto direction = event.direction;
        if (state_.focusRegion() == gba::FocusRegion::Tray) {
            if (direction == NavigationDirection::Left) {
                Dispatch(gba::Command::NavigateLeft);
            } else if (direction == NavigationDirection::Right) {
                Dispatch(gba::Command::NavigateRight);
            } else if (gba::input::ShouldEnterWidgetFromTray(
                           direction, event.phase)) {
                Dispatch(gba::Command::Activate);
            }
            return;
        }
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget) return;
        switch (direction) {
        case NavigationDirection::Left:
        case NavigationDirection::Right:
            HandleWidgetDirection(direction, event.phase, true);
            break;
        case NavigationDirection::Up: MoveWidgetFocus(L"up"); break;
        case NavigationDirection::Down: MoveWidgetFocus(L"down"); break;
        default: break;
        }
    }

    void PollController() {
        const ULONGLONG now = GetTickCount64();
        GbaOverlayPlatformControllerFrame frame;
        if (GbaOverlayPlatformReadController(
                platform_,
                PlatformBoolean(IsOverlayProcessForeground()),
                now,
                &frame) !=
            GbaOverlayPlatformStatus::Ok) {
            return;
        }
        const bool connected =
            frame.connected != GBA_OVERLAY_PLATFORM_FALSE;
        const WORD buttons = frame.state.buttons;
        const WORD pressed = frame.pressedButtons;
        const WORD released = frame.releasedButtons;
        const auto pinnedControllerCommand = gba::pinned::ResolveControllerCommand({
            pinnedSurfaceCoordinator_.pinned(),
            pinnedSurfaceCoordinator_.placementMode() !=
                gba::pinned::PlacementMode::None,
            pinnedSurfaceCoordinator_.pinned() &&
                state_.surface() == gba::Surface::Widget &&
                state_.activeWidget() == pinnedSurfaceCoordinator_.widgetId(),
            pinnedSurfaceCoordinator_.controllerFocused(),
            (pressed & XINPUT_GAMEPAD_A) != 0,
            (pressed & XINPUT_GAMEPAD_B) != 0,
            (pressed & XINPUT_GAMEPAD_X) != 0,
            (pressed & XINPUT_GAMEPAD_RIGHT_THUMB) != 0,
            (buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0,
            (buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0,
        });
        if (pinnedControllerCommand ==
            gba::pinned::ControllerCommand::EmergencyHide) {
            EmergencyHidePinnedSurfaces();
            return;
        }
        constexpr WORD recoveryChord = XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
        const bool recoveryChordDown = (buttons & recoveryChord) == recoveryChord;
        if (frame.recoveryChordPressed != GBA_OVERLAY_PLATFORM_FALSE) {
            RestartCurrentWidget();
        }

        const auto stepPinnedPlacement = [&](const gba::input::StickNavigationEvent& event) {
            using gba::input::NavigationDirection;
            switch (event.direction) {
            case NavigationDirection::Left:
                (void)pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Left);
                break;
            case NavigationDirection::Right:
                (void)pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Right);
                break;
            case NavigationDirection::Up:
                (void)pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Up);
                break;
            case NavigationDirection::Down:
                (void)pinnedSurfaceCoordinator_.StepPlacement(
                    gba::pinned::PlacementDirection::Down);
                break;
            default: break;
            }
        };
        if (pinnedSurfaceCoordinator_.placementMode() !=
            gba::pinned::PlacementMode::None) {
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                stepPinnedPlacement(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                stepPinnedPlacement(*direction);
            if ((pressed & XINPUT_GAMEPAD_A) != 0) {
                std::wstring error;
                const bool committed = pinnedSurfaceCoordinator_.CommitPlacement(error);
                lastActionMessage_ = committed ? L"Pinned placement saved" : error;
                lastActionExpiresAt_ = now + 3000;
            } else if ((pressed & XINPUT_GAMEPAD_B) != 0) {
                (void)pinnedSurfaceCoordinator_.CancelPlacement();
                lastActionMessage_ = L"Pinned placement canceled";
                lastActionExpiresAt_ = now + 2400;
            }
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        const bool placementEligible = !recoveryChordDown &&
            pinnedSurfaceCoordinator_.pinned() &&
            state_.surface() == gba::Surface::Widget &&
            state_.activeWidget() == pinnedSurfaceCoordinator_.widgetId();
        gba::pinned::PlacementMode requestedPlacement = gba::pinned::PlacementMode::None;
        if (placementEligible && (pressed & XINPUT_GAMEPAD_START) != 0)
            requestedPlacement = gba::pinned::PlacementMode::Move;
        else if (placementEligible && (pressed & XINPUT_GAMEPAD_BACK) != 0)
            requestedPlacement = gba::pinned::PlacementMode::Resize;
        if (requestedPlacement != gba::pinned::PlacementMode::None &&
            pinnedSurfaceCoordinator_.BeginPlacement(requestedPlacement)) {
            (void)pressedInteraction_.Clear();
            lastActionWidgetId_ = std::wstring(pinnedSurfaceCoordinator_.widgetId());
            lastActionMessage_ = requestedPlacement == gba::pinned::PlacementMode::Move
                ? L"Move pinned surface: D-pad/stick, A commit, B cancel"
                : L"Resize pinned surface: D-pad/stick, A commit, B cancel";
            lastActionExpiresAt_ = now + 5000;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        if (pinnedControllerCommand == gba::pinned::ControllerCommand::Enter) {
            if (pinnedSurfaceCoordinator_.interactionMode() !=
                gba::pinned::InteractionMode::Focusable)
                (void)pinnedSurfaceCoordinator_.SetInteractionMode(
                    gba::pinned::InteractionMode::Focusable);
            if (pinnedSurfaceCoordinator_.EnterControllerFocus()) {
                (void)pressedInteraction_.Clear();
                lastActionWidgetId_ =
                    std::wstring(pinnedSurfaceCoordinator_.widgetId());
                lastActionMessage_ =
                    L"Pinned focus entered. B returns, X closes, Menu moves, View resizes.";
                lastActionExpiresAt_ = now + 5000;
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }

        if (pinnedSurfaceCoordinator_.controllerFocused()) {
            const auto movePinnedFocus = [&](const gba::input::StickNavigationEvent& event) {
                (void)pinnedSurfaceCoordinator_.MoveControllerFocus(event.direction);
            };
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                movePinnedFocus(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                movePinnedFocus(*direction);
            if (pinnedControllerCommand == gba::pinned::ControllerCommand::Activate) {
                if (!pinnedSurfaceCoordinator_.QueueFocusedInput(
                        L"a", gba::ControllerInputOrigin::PhysicalController))
                    pinnedSurfaceCoordinator_.SetActionFeedback(
                        L"The focused pinned item is unavailable.", false);
            } else if (pinnedControllerCommand ==
                       gba::pinned::ControllerCommand::Exit) {
                (void)pinnedSurfaceCoordinator_.ExitControllerFocus();
                (void)pinnedSurfaceCoordinator_.SetInteractionMode(
                    gba::pinned::InteractionMode::ClickThrough);
                lastActionMessage_ = L"Controller focus returned to the overlay";
                lastActionExpiresAt_ = now + 2400;
            } else if (pinnedControllerCommand ==
                       gba::pinned::ControllerCommand::Close) {
                const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
                (void)pinnedSurfaceCoordinator_.Unpin(
                    gba::pinned::WidgetSurfaceStopReason::Close);
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ = L"Pinned surface closed";
                lastActionExpiresAt_ = now + 2400;
                SyncWidgetActivity();
            }
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        const auto releaseButton = [&](const WORD mask, const std::wstring_view protocolButton) {
            if ((released & mask) != 0 && pressedInteraction_.Release(protocolButton))
                InvalidateRect(window_, nullptr, FALSE);
        };
        releaseButton(XINPUT_GAMEPAD_A, L"a");
        releaseButton(XINPUT_GAMEPAD_B, L"b");
        releaseButton(XINPUT_GAMEPAD_X, L"x");
        releaseButton(XINPUT_GAMEPAD_LEFT_SHOULDER, L"leftBumper");
        releaseButton(XINPUT_GAMEPAD_RIGHT_SHOULDER, L"rightBumper");
        releaseButton(XINPUT_GAMEPAD_LEFT_THUMB, L"leftStick");
        releaseButton(XINPUT_GAMEPAD_RIGHT_THUMB, L"rightStick");
        releaseButton(XINPUT_GAMEPAD_BACK, L"view");
        releaseButton(XINPUT_GAMEPAD_START, L"menu");

        if (sliderReconcileAt_ != 0 && now >= sliderReconcileAt_) {
            sliderReconcileAt_ = 0;
            InvalidateRect(window_, nullptr, FALSE);
        }
        if (const auto direction = DecodeNavigation(frame.stickNavigation)) {
            DispatchStickNavigation(*direction);
        }
        if (const auto direction = DecodeNavigation(frame.dpadNavigation)) {
            DispatchStickNavigation(*direction);
        }

        const bool launcherSafeStartGesture =
            (pressed & XINPUT_GAMEPAD_A) != 0 &&
            state_.focusRegion() == gba::FocusRegion::Tray &&
            UsesLauncherExperiencePresentation(state_.selectedWidget()) &&
            frame.state.leftTrigger >= 30 &&
            frame.state.rightTrigger >= 30;
        if (launcherSafeStartGesture) {
            launcherSafeStartPending_ = true;
            lastActionWidgetId_ = state_.selectedWidget();
            lastActionMessage_ =
                L"Advanced presentation safe start: built-in experience for this activation";
            lastActionExpiresAt_ = now + 5000;
            AppendDiagnostic(lastActionMessage_);
        }
        if (pressed & XINPUT_GAMEPAD_A) {
            DispatchControllerAction(L"A", true);
        }
        if (pressed & XINPUT_GAMEPAD_B) {
            DispatchControllerAction(L"B", true);
        }
        if (pressed & XINPUT_GAMEPAD_X) {
            DispatchControllerAction(L"X", true);
        }
        if (pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) {
            DispatchControllerAction(L"LB", true);
        }
        if (pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) {
            DispatchControllerAction(L"RB", true);
        }
        if (pressed & XINPUT_GAMEPAD_LEFT_THUMB) {
            DispatchControllerAction(L"LS", true);
        }
        if (pressed & XINPUT_GAMEPAD_RIGHT_THUMB) {
            DispatchControllerAction(L"RS", true);
        }
        if (!recoveryChordDown && (pressed & XINPUT_GAMEPAD_BACK)) {
            DispatchControllerAction(L"View", true);
        }
        if (!recoveryChordDown && (pressed & XINPUT_GAMEPAD_START)) {
            DispatchControllerAction(L"Menu", true);
        }
        if (!launcherSafeStartGesture &&
            frame.leftTriggerPressed != GBA_OVERLAY_PLATFORM_FALSE) {
            DispatchControllerAction(L"LT", true);
        }
        if (!launcherSafeStartGesture &&
            frame.rightTriggerPressed != GBA_OVERLAY_PLATFORM_FALSE) {
            DispatchControllerAction(L"RT", true);
        }
        if (frame.leftTriggerReleased != GBA_OVERLAY_PLATFORM_FALSE &&
            pressedInteraction_.Release(L"leftTrigger"))
            InvalidateRect(window_, nullptr, FALSE);
        if (frame.rightTriggerReleased != GBA_OVERLAY_PLATFORM_FALSE &&
            pressedInteraction_.Release(L"rightTrigger"))
            InvalidateRect(window_, nullptr, FALSE);

        // Resolve every other button and direction first. If one changes tray
        // focus, selection, overlay, reorder, or lifecycle on this same sample,
        // the central state transition cancels Y before it can win.
        if (!connected && trayYGesture_.capturing()) {
            trayYGesture_.Cancel();
            InvalidateRect(window_, nullptr, FALSE);
        }
        if (connected && (pressed & XINPUT_GAMEPAD_Y) != 0) {
            if (trayYGesture_.capturing()) {
                // Device reconnect while a canceled capture is waiting for its
                // physical release must not create a second gesture.
            } else if (state_.focusRegion() == gba::FocusRegion::Tray) {
                trayYGesture_.Press(
                    state_.selectedWidget(), TrayYRestartEligible(), now);
                InvalidateRect(window_, nullptr, FALSE);
            } else {
                DispatchControllerAction(L"Y", true);
            }
        }
        if (connected && trayYGesture_.capturing() &&
            (buttons & XINPUT_GAMEPAD_Y) != 0) {
            ApplyTrayYGestureAction(trayYGesture_.Update(
                state_.selectedWidget(), TrayYRestartEligible(), now));
            if (trayYGesture_.pendingRestart())
                InvalidateRect(window_, nullptr, FALSE);
        }
        if (connected && (released & XINPUT_GAMEPAD_Y) != 0) {
            if (trayYGesture_.capturing()) {
                ApplyTrayYGestureAction(trayYGesture_.Release(
                    state_.selectedWidget(), TrayYRestartEligible(), now));
                InvalidateRect(window_, nullptr, FALSE);
            } else {
                releaseButton(XINPUT_GAMEPAD_Y, L"y");
            }
        } else if (connected && trayYGesture_.capturing() &&
                   (buttons & XINPUT_GAMEPAD_Y) == 0) {
            // A device can reconnect after the synthetic loss edge. Retire the
            // canceled capture only after its physical Y is observed released.
            ApplyTrayYGestureAction(trayYGesture_.Release(
                state_.selectedWidget(), TrayYRestartEligible(), now));
            InvalidateRect(window_, nullptr, FALSE);
        }
        // The visible overlay already polls controller state at 60 Hz. Reuse
        // that bounded wakeup rather than owning an animation timer; settled
        // declarative content performs no paint invalidations, and this timer
        // is stopped altogether while the overlay is hidden.
        if (declarativeMotionActive_)
            InvalidateRect(window_, nullptr, FALSE);
    }

    const gba::WidgetSnapshot* SnapshotFor(const std::wstring_view widgetId) const noexcept {
        return sessions_.Snapshot(widgetId);
    }

    const gba::WidgetSnapshot* InteractionSnapshotFor(
        const std::wstring_view widgetId) const noexcept {
        const auto presentation = sessions_.Presentation(widgetId);
        if (presentation.authority != gba::WidgetPresentationAuthority::Current)
            return nullptr;
        const auto* source = presentation.snapshot;
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        return source && descriptor
            ? &launcherExperienceProjection_.InteractionSnapshot(
                widgetId, descriptor->presentationGeneration, *source)
            : nullptr;
    }

    void RememberCurrentFocus(const std::wstring_view widgetId) {
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!snapshot || focusedElementId_.empty()) return;
        focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
    }

    void RestoreFocusForActiveSurface(const std::wstring_view widgetId) {
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        focusedElementId_ = snapshot
            ? focusMemory_.Restore(widgetId, *snapshot)
            : std::wstring{};
        if (!focusedElementId_.empty())
            (void)scrollEvidenceProbe_.RecordTarget(focusedElementId_, L"restore");
        (void)ReconcileResponsiveFocusPersistence();
    }

    void HandleAccessibilityActions() {
        auto pendingActions = accessibilityProvider_.TakeActions();
        auto chromeActions = chromeAccessibilityProvider_.TakeActions();
        pendingActions.insert(pendingActions.end(),
                              std::make_move_iterator(chromeActions.begin()),
                              std::make_move_iterator(chromeActions.end()));
        for (const auto& request : pendingActions) {
            const auto publishedNode = std::find_if(
                accessibilityTree_.nodes.begin(), accessibilityTree_.nodes.end(),
                [&](const gba::accessibility::Node& candidate) {
                    return candidate.domain == request.domain &&
                        candidate.id == request.nodeId;
                });
            if (request.hostAction != gba::accessibility::HostAction::None) {
                if (request.widgetId != accessibilityTree_.widgetId ||
                    request.runtimeGeneration != accessibilityTree_.runtimeGeneration ||
                    request.snapshotSequence != accessibilityTree_.snapshotSequence ||
                    request.activeInputScopeId != accessibilityTree_.activeInputScopeId ||
                    publishedNode == accessibilityTree_.nodes.end() ||
                    !publishedNode->enabled ||
                    request.hostAction != publishedNode->hostAction ||
                    request.hostTargetId != publishedNode->hostTargetId) {
                    AppendDiagnostic(L"Dropped stale or invalid host accessibility action");
                    continue;
                }
                if (request.hostAction ==
                    gba::accessibility::HostAction::ActivateTrayItem) {
                    if (request.kind != gba::accessibility::ActionKind::Invoke &&
                        request.kind != gba::accessibility::ActionKind::Focus)
                        continue;
                    if (state_.surface() == gba::Surface::Widget &&
                        state_.focusRegion() == gba::FocusRegion::Widget)
                        Dispatch(gba::Command::SampleWidgetBack);
                    if (state_.focusRegion() != gba::FocusRegion::Tray) continue;
                    if (state_.reorderMode()) Dispatch(gba::Command::Cancel);
                    if (!SelectTrayWidget(request.hostTargetId)) continue;
                    if (request.kind == gba::accessibility::ActionKind::Invoke)
                        Dispatch(gba::Command::Activate);
                } else if (request.hostAction ==
                               gba::accessibility::HostAction::SelectTrayOverflow) {
                    if (request.kind != gba::accessibility::ActionKind::Invoke ||
                        state_.focusRegion() != gba::FocusRegion::Tray ||
                        state_.reorderMode())
                        continue;
                    if (!SelectTrayWidget(request.hostTargetId)) continue;
                } else if (request.hostAction ==
                               gba::accessibility::HostAction::BackToTray ||
                           request.hostAction ==
                               gba::accessibility::HostAction::BackWithinWidget) {
                    if (request.kind != gba::accessibility::ActionKind::Invoke ||
                        state_.surface() != gba::Surface::Widget ||
                        state_.focusRegion() != gba::FocusRegion::Widget ||
                        state_.activeWidget() != request.widgetId ||
                        !IsBridgeWidget(request.widgetId))
                        continue;
                    const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
                    const auto* snapshot = InteractionSnapshotFor(request.widgetId);
                    if (!descriptor || !snapshot ||
                        descriptor->runtimeGeneration != request.runtimeGeneration ||
                        snapshot->sequence != request.snapshotSequence ||
                        snapshot->activeInputScopeId != request.hostTargetId) {
                        AppendDiagnostic(L"Dropped stale Back accessibility action");
                        continue;
                    }
                    if (!gba::accessibility::IsCurrentBackAction(
                            request.hostAction, request.hostTargetId, *snapshot,
                            focusedElementId_))
                        continue;
                    if (request.hostAction == gba::accessibility::HostAction::BackToTray) {
                        Dispatch(gba::Command::SampleWidgetBack);
                    } else {
                        const auto handled = bridge_.SendControllerInput(
                            request.widgetId, L"b", L"openWidget", focusedElementId_,
                            snapshot->activeInputScopeId, snapshot->sequence,
                            ++controllerSequence_,
                            static_cast<long long>(GetTickCount64() * 1000),
                            L"pressed", std::nullopt,
                            gba::ControllerInputOrigin::AccessibilityAutomation);
                        if (!handled) {
                            AppendDiagnostic(
                                L"Accessibility Back transport failed for " +
                                request.widgetId);
                        } else if (*handled) {
                            RefreshAndApplyPresentation([&] {
                                RefreshWidgetSnapshot(request.widgetId);
                            });
                        }
                    }
                } else if (request.hostAction ==
                           gba::accessibility::HostAction::CloseOverlay) {
                    if (request.kind != gba::accessibility::ActionKind::Invoke ||
                        state_.surface() == gba::Surface::Hidden)
                        continue;
                    Dispatch(gba::Command::CloseOverlay);
                } else {
                    continue;
                }
                (void)SetFocus(window_);
                InvalidateRect(window_, nullptr, FALSE);
                continue;
            }
            if (state_.surface() != gba::Surface::Widget ||
                state_.activeWidget() != request.widgetId ||
                !IsBridgeWidget(request.widgetId)) {
                AppendDiagnostic(L"Dropped accessibility action outside the active widget");
                continue;
            }
            const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
            const auto* snapshot = InteractionSnapshotFor(request.widgetId);
            if (!descriptor || !snapshot) {
                AppendDiagnostic(L"Dropped stale accessibility action for " + request.widgetId);
                continue;
            }
            const auto resolved = gba::accessibility::ResolveActionRequest(
                request, state_.activeWidget(), descriptor->runtimeGeneration, *snapshot);
            if (!resolved) {
                AppendDiagnostic(L"Dropped stale or unavailable accessibility action for " +
                                 request.widgetId);
                continue;
            }

            if (resolved->kind == gba::accessibility::ActionKind::Focus) {
                if (state_.focusRegion() == gba::FocusRegion::Tray)
                    Dispatch(gba::Command::Activate);
                if (state_.surface() != gba::Surface::Widget ||
                    state_.focusRegion() != gba::FocusRegion::Widget ||
                    state_.activeWidget() != request.widgetId) continue;
                sliderInteraction_.DeactivateAll();
                (void)pressedInteraction_.Clear();
                launcherExperienceProjection_.ObserveFocusInput(
                    request.widgetId, GetTickCount64());
                focusedElementId_ = resolved->nodeId;
                focusMemory_.Remember(request.widgetId, *snapshot, focusedElementId_);
                (void)SetFocus(window_);
                InvalidateRect(window_, nullptr, FALSE);
                continue;
            }

            // UIA patterns are accessibility automation, not proof of a
            // physical controller gesture. Enter the widget's interactive
            // lifecycle for ordinary action routing while retaining the same
            // generation/snapshot checks and explicit origin below.
            if (state_.focusRegion() == gba::FocusRegion::Tray)
                Dispatch(gba::Command::Activate);
            if (state_.surface() != gba::Surface::Widget ||
                state_.focusRegion() != gba::FocusRegion::Widget ||
                state_.activeWidget() != request.widgetId) continue;

            if (resolved->protocolButton == L"A" &&
                OpenTextEntryModal(request.widgetId, *snapshot, resolved->nodeId))
                continue;
            if (const auto* node = gba::input::FindNodeInInputScope(
                    *snapshot, resolved->nodeId, snapshot->activeInputScopeId);
                node && TryInvokeLocalWidgetPackageImport(
                    *snapshot, *node, resolved->protocolButton,
                    gba::input::NavigationEventPhase::Pressed)) {
                continue;
            }

            const auto handled = bridge_.SendControllerInput(
                request.widgetId, resolved->protocolButton, L"openWidget", resolved->nodeId,
                snapshot->activeInputScopeId, snapshot->sequence,
                ++controllerSequence_, static_cast<long long>(GetTickCount64() * 1000),
                L"pressed", resolved->requestedValue,
                gba::ControllerInputOrigin::AccessibilityAutomation);
            if (!handled) {
                AppendDiagnostic(L"Accessibility action transport failed for " + request.widgetId);
            } else if (*handled) {
                RefreshAndApplyPresentation([&] {
                    RefreshWidgetSnapshot(request.widgetId);
                });
            }
        }
        accessibilityProvider_.RaisePendingEvents();
        chromeAccessibilityProvider_.RaisePendingEvents();
    }

    void ClearAccessibilityTree() noexcept {
        accessibilityTree_ = {};
        widgetAccessibilityTree_ = {};
        accessibilityProvider_.Clear();
        chromeAccessibilityProvider_.Clear();
        accessibilityProjection_.Clear();
        widgetAccessibilityProjection_.Clear();
    }

    [[nodiscard]] gba::accessibility::Tree PartitionAccessibilityTree(
        const bool chrome) const {
        if (!compositionChromeSession_) {
            if (!chrome) return accessibilityTree_;
            gba::accessibility::Tree empty;
            empty.widgetId = accessibilityTree_.widgetId;
            empty.runtimeGeneration = accessibilityTree_.runtimeGeneration;
            empty.snapshotSequence = accessibilityTree_.snapshotSequence;
            empty.activeInputScopeId = accessibilityTree_.activeInputScopeId;
            empty.name = accessibilityTree_.name;
            return empty;
        }
        const auto& session = *compositionChromeSession_;
        const float guideThreshold = state_.surface() == gba::Surface::Widget
            ? -std::numeric_limits<float>::infinity()
            : std::numeric_limits<float>::infinity();
        auto partition = gba::accessibility::PartitionForFixedChrome(
            accessibilityTree_, guideThreshold, 0.0F, 0.0F);
        for (auto& node : partition.chrome.nodes) {
            const RECT& childBounds =
                node.domain == gba::accessibility::ElementDomain::Tray
                ? session.trayClientBounds
                : session.guideClientBounds;
            node.bounds.x += static_cast<float>(childBounds.left) /
                session.pixelsPerDip;
            node.bounds.y += static_cast<float>(childBounds.top) /
                session.pixelsPerDip;
        }
        return chrome ? std::move(partition.chrome) : std::move(partition.content);
    }

    bool PublishAccessibilityTree(
        const double pixelsPerDip,
        const bool forceDuringPlacement = false) {
        if (compositionPlacementInProgress_ && !forceDuringPlacement)
            return false;
        POINT origin{};
        RECT client{};
        if (!ClientToScreen(window_, &origin) || !GetClientRect(window_, &client)) {
            ClearAccessibilityTree();
            return false;
        }
        double scaleX = pixelsPerDip;
        double scaleY = pixelsPerDip;
        double visualOffsetX = 0.0;
        double visualOffsetY = 0.0;
        double presentedWidth = static_cast<double>(client.right - client.left);
        double presentedHeight = static_cast<double>(client.bottom - client.top);
        const auto childSpaces = CurrentCompositionChildCoordinates();
        if (childSpaces) {
            const auto& motion = childSpaces->content;
            const auto& contentPlacement = presentationTransaction_.contentPlacement();
            if (!contentPlacement) {
                ClearAccessibilityTree();
                return false;
            }
            scaleX *= static_cast<double>(motion.scaleX);
            scaleY *= static_cast<double>(motion.scaleY);
            visualOffsetX = static_cast<double>(motion.offsetX);
            visualOffsetY = static_cast<double>(motion.offsetY);
            presentedWidth = static_cast<double>(
                contentPlacement->width) * motion.scaleX;
            presentedHeight = static_cast<double>(
                contentPlacement->height) * motion.scaleY;
        }
        accessibilityProvider_.Publish(
            PartitionAccessibilityTree(false),
            {
                static_cast<double>(origin.x),
                static_cast<double>(origin.y),
                pixelsPerDip,
                presentedWidth,
                presentedHeight,
                scaleX,
                scaleY,
                visualOffsetX,
                visualOffsetY,
                0.0,
                0.0,
                false,
            });
        if (compositionChromeSession_ && chromeWindow_) {
            POINT chromeOrigin{};
            RECT chromeClient{};
            if (!ClientToScreen(chromeWindow_, &chromeOrigin) ||
                !GetClientRect(chromeWindow_, &chromeClient)) return false;
            const auto& session = *compositionChromeSession_;
            chromeAccessibilityProvider_.Publish(
                PartitionAccessibilityTree(true),
                {
                    static_cast<double>(chromeOrigin.x),
                    static_cast<double>(chromeOrigin.y),
                    session.pixelsPerDip,
                    static_cast<double>(chromeClient.right - chromeClient.left),
                    static_cast<double>(chromeClient.bottom - chromeClient.top),
                    session.pixelsPerDip,
                    session.pixelsPerDip,
                });
        }
        return true;
    }

    void PublishTrayAccessibility(
        const gba::shell::TrayLayout& layout,
        const float width,
        const float height,
        const gba::accessibility::DashboardSemantics* dashboard = nullptr) {
        if (!accessibilityActive_) return;
        std::vector<gba::accessibility::TrayItem> items;
        items.reserve(state_.order().size());
        for (const auto& widgetId : state_.order()) {
            const std::wstring name{DisplayWidgetName(widgetId)};
            items.push_back({widgetId, name});
        }
        if (state_.surface() == gba::Surface::Widget) {
            const bool currentWidgetSemantics =
                widgetAccessibilityTree_.widgetId == state_.activeWidget();
            if ((!currentWidgetSemantics &&
                 state_.focusRegion() != gba::FocusRegion::Tray) ||
                openWidgetAccessibility_.title.empty())
                return;
            gba::accessibility::Tree semanticTree = currentWidgetSemantics
                ? widgetAccessibilityTree_
                : gba::accessibility::Tree{};
            if (!currentWidgetSemantics) {
                semanticTree.widgetId = state_.activeWidget();
                semanticTree.activeInputScopeId = L"host.tray";
                if (const auto* descriptor = sessions_.FindDescriptor(
                        state_.activeWidget())) {
                    semanticTree.runtimeGeneration = descriptor->runtimeGeneration;
                }
            }
            const auto semanticRevision =
                gba::accessibility::ComputeOpenWidgetSemanticRevision(
                    items, openWidgetAccessibility_);
            const auto policy = appearanceState_.current()
                ? CurrentAccessibilityPolicy()
                : gba::NativeAccessibilityPolicy{};
            gba::accessibility::ProjectionKey key{
                semanticTree.widgetId,
                semanticTree.runtimeGeneration,
                semanticTree.activeInputScopeId,
                state_.focusRegion() == gba::FocusRegion::Tray
                    ? std::wstring{state_.selectedWidget()}
                    : focusedElementId_,
                semanticTree.snapshotSequence,
                widgetAccessibilityRevision_,
                appearanceState_.current() ? appearanceState_.current()->revision : 0,
                layout.stripBounds.x,
                layout.stripBounds.y,
                layout.stripBounds.width,
                layout.stripBounds.height,
                width,
                height,
                static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F,
                policy.textScale,
                policy.minimumFontWeight,
                policy.reducedMotion,
                policy.reducedTransparency,
            };
            key.hostSemanticRevision = semanticRevision;
            if (!accessibilityProjection_.ShouldCollect(key)) return;
            accessibilityTree_ = gba::accessibility::BuildOpenWidgetTree(
                std::move(semanticTree), items, layout, state_.selectedSlot(),
                state_.focusRegion() == gba::FocusRegion::Tray,
                openWidgetAccessibility_);
            if (PublishAccessibilityTree(key.pixelsPerDip))
                accessibilityProjection_.Published(std::move(key));
            return;
        }
        if (state_.focusRegion() != gba::FocusRegion::Tray) return;
        const auto semanticRevision =
            gba::accessibility::ComputeTraySemanticRevision(items, dashboard);
        const auto policy = appearanceState_.current()
            ? CurrentAccessibilityPolicy()
            : gba::NativeAccessibilityPolicy{};
        const gba::accessibility::ProjectionKey key{
            L"host.tray",
            L"host",
            L"host.tray",
            std::wstring{state_.selectedWidget()},
            semanticRevision,
            0,
            appearanceState_.current() ? appearanceState_.current()->revision : 0,
            layout.stripBounds.x,
            layout.stripBounds.y,
            layout.stripBounds.width,
            layout.stripBounds.height,
            width,
            height,
            static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F,
            policy.textScale,
            policy.minimumFontWeight,
            policy.reducedMotion,
            policy.reducedTransparency,
        };
        if (!accessibilityProjection_.ShouldCollect(key)) return;
        accessibilityTree_ = gba::accessibility::BuildTrayTree(
            items, layout, state_.selectedSlot(), ++hostAccessibilitySequence_, dashboard);
        if (PublishAccessibilityTree(key.pixelsPerDip))
            accessibilityProjection_.Published(key);
    }

    bool ReconcileResponsiveFocusPersistence() {
        if (!window_ || state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget) {
            return false;
        }
        const std::wstring widget{state_.activeWidget()};
        const auto* snapshot = InteractionSnapshotFor(widget);
        if (!snapshot || focusedElementId_.empty()) return false;

        RECT client{};
        if (!GetClientRect(window_, &client)) return false;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            client.right - client.left,
            client.bottom - client.top,
            dpi,
            interfaceScale);
        if (!metrics) return false;
        const auto geometry = ComputeCurrentWidgetSurfaceGeometry(
            metrics->viewportWidthDip, metrics->viewportHeightDip);
        if (!geometry) return false;

        const auto target = gba::input::ResolveResponsiveFocusPersistenceTarget(
            *snapshot,
            focusedElementId_,
            snapshot->activeInputScopeId,
            gba::IsCompactResponsiveSurface({
                geometry->panelWidth,
                geometry->panelHeight,
            }));
        if (!target || *target == focusedElementId_) return false;

        sliderInteraction_.DeactivateAll();
        (void)pressedInteraction_.Clear();
        focusedElementId_ = *target;
        (void)scrollEvidenceProbe_.RecordTarget(*target, L"responsive");
        focusMemory_.Remember(widget, *snapshot, focusedElementId_);
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    static gba::input::SliderInputDescriptor SliderDescriptor(
        const gba::WidgetSnapshot& snapshot,
        const gba::WidgetNode& node) noexcept {
        return {
            snapshot.instanceId,
            snapshot.activeInputScopeId,
            node.id,
            node.valueChangedActionId,
            snapshot.sequence,
            node.minimum,
            node.maximum,
            node.value,
            node.step,
            node.isDisabled,
            node.isBusy,
            node.sliderInteractionMode == L"activateToAdjust",
        };
    }

    bool DispatchScrollPagination(
        const std::wstring_view widgetId,
        const gba::WidgetSnapshot& snapshot,
        const gba::input::NavigationDirection direction) {
        const auto action = gba::input::FindScrollPaginationAction(
            snapshot.root, focusedElementId_, direction);
        if (!action) return false;
        const auto handled = bridge_.SendAction(
            widgetId, action->actionId, action->sourceElementId,
            snapshot.activeInputScopeId);
        if (!handled) {
            AppendDiagnostic(std::wstring(DisplayWidgetName(widgetId)) +
                L" pagination failed: " + bridge_.lastError());
            return true;
        }
        if (*handled)
            RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widgetId); });
        return true;
    }

    void HandleWidgetDirection(
        const gba::input::NavigationDirection direction,
        const gba::input::NavigationEventPhase phase,
        const bool repeatedCanNavigate) {
        if (textEntryModal_.active()) {
            const auto button = direction == gba::input::NavigationDirection::Left ? L"DPadLeft" :
                direction == gba::input::NavigationDirection::Right ? L"DPadRight" :
                direction == gba::input::NavigationDirection::Up ? L"DPadUp" : L"DPadDown";
            textEntryModal_.HandleController(button);
            return;
        }
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget) return;
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!snapshot) return;
        const auto visible = gba::input::ResolveVisibleFocusTarget(
            focusedElementId_, snapshot->activeInputScopeId, lastWidgetRenderResult_);
        if (!visible) return;
        if (*visible != focusedElementId_) {
            focusedElementId_ = *visible;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
        }
        const auto projectedDirection =
            direction == gba::input::NavigationDirection::Left ? L"left" :
            direction == gba::input::NavigationDirection::Right ? L"right" :
            direction == gba::input::NavigationDirection::Up ? L"up" : L"down";
        const auto projectedFocusTarget =
            launcherExperienceProjection_.ProjectedFocusTarget(
                widgetId, focusedElementId_, projectedDirection);
        if ((phase != gba::input::NavigationEventPhase::Repeated ||
             repeatedCanNavigate) &&
            projectedFocusTarget) {
            MoveWidgetFocus(projectedDirection);
            return;
        }
        const auto* focused = gba::input::FindNodeInInputScope(
            *snapshot, focusedElementId_, snapshot->activeInputScopeId);
        if (!focused) return;
        const auto sliderDescriptor = SliderDescriptor(*snapshot, *focused);
        const bool activationRequired =
            focused->sliderInteractionMode == L"activateToAdjust";
        const bool adjustmentActive = activationRequired &&
            sliderInteraction_.AdjustmentModeActive(
                sliderDescriptor, GetTickCount64());
        const auto route = gba::input::RouteFocusedDirection(
            focused->kind, focused->isDisabled, focused->isBusy,
            activationRequired, adjustmentActive, direction);
        if (route == gba::input::FocusedDirectionRoute::Consume) return;
        if (route == gba::input::FocusedDirectionRoute::SliderAdjustment) {
            const auto adjustment = sliderInteraction_.Adjust(
                sliderDescriptor, direction, GetTickCount64());
            if (adjustment.requestedValue) {
                sliderReconcileAt_ = GetTickCount64() +
                    gba::input::SliderInteractionState::PendingTimeoutMilliseconds + 1;
                // Paint the host-owned target before the synchronous worker
                // acknowledgement so controller feedback never waits on IPC.
                InvalidateRect(window_, nullptr, FALSE);
                UpdateWindow(window_);
                const auto button = direction == gba::input::NavigationDirection::Left
                    ? std::wstring_view{L"DPadLeft"}
                    : std::wstring_view{L"DPadRight"};
                DispatchWidgetAction(button, phase, adjustment.requestedValue);
            }
            // Optimistic value paints on the next frame even when the worker
            // queue is busy; acknowledgement or timeout reconciles it.
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (phase == gba::input::NavigationEventPhase::Repeated && !repeatedCanNavigate)
            return;
        switch (direction) {
        case gba::input::NavigationDirection::Left: MoveWidgetFocus(L"left"); break;
        case gba::input::NavigationDirection::Right: MoveWidgetFocus(L"right"); break;
        case gba::input::NavigationDirection::Up: MoveWidgetFocus(L"up"); break;
        case gba::input::NavigationDirection::Down: MoveWidgetFocus(L"down"); break;
        default: break;
        }
    }

    void RefreshCurrentBridgeSnapshot(const std::uint64_t correlationId = 0) {
        if (state_.surface() == gba::Surface::Hidden &&
            !pinnedSurfaceCoordinator_.pinned()) return;
        const std::wstring_view widgetId = state_.surface() == gba::Surface::Hidden
            ? pinnedSurfaceCoordinator_.widgetId()
            : state_.surface() == gba::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (IsBridgeWidget(widgetId))
            RefreshWidgetSnapshot(widgetId, correlationId);
    }

    [[nodiscard]] bool TrayYRestartEligible() const noexcept {
        return state_.surface() != gba::Surface::Hidden &&
               state_.focusRegion() == gba::FocusRegion::Tray &&
               !state_.reorderMode() &&
               IsBridgeWidget(state_.selectedWidget());
    }

    void ApplyTrayYGestureAction(const gba::input::TrayYGestureAction action) {
        switch (action) {
        case gba::input::TrayYGestureAction::ToggleReorder:
            Dispatch(gba::Command::ToggleReorder);
            break;
        case gba::input::TrayYGestureAction::RestartSelectedWidget:
            // The recognizer revalidated the exact selected bridge widget and
            // tray context. Resolve it once more through the same restart
            // authority as F5; tray Y never depends on a widget-authored action.
            RestartCurrentWidget();
            break;
        case gba::input::TrayYGestureAction::None:
            break;
        }
    }

    void RestartCurrentWidget() {
        const std::wstring widgetId{gba::input::ResolveCurrentWidgetReloadTarget(
            state_.surface() != gba::Surface::Hidden,
            state_.focusRegion() == gba::FocusRegion::Tray,
            state_.selectedWidget(), state_.activeWidget())};
        if (!IsBridgeWidget(widgetId)) return;
        if (pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == widgetId) {
            (void)pinnedSurfaceCoordinator_.Unpin(
                gba::pinned::WidgetSurfaceStopReason::RuntimeReplaced);
        }
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        CommitAdmittedWidgetPresentation(widgetId);
        if (descriptor) {
            if (declarativeRenderer_ && !descriptor->instanceId.empty())
                declarativeRenderer_->ForgetWidgetState(descriptor->instanceId);
            sliderInteraction_.ForgetWidget(descriptor->instanceId);
        }
        (void)pressedInteraction_.Clear();
        focusMemory_.Forget(widgetId);
        focusedElementId_.clear();
        sessions_.RemoveSnapshot(widgetId);
        renderedSnapshotSequences_.erase(widgetId);
        pendingContentRevealWidget_ = widgetId;
        overlayTransition_.SnapContentVisible();
        ClearAccessibilityTree();
        if (state_.surface() == gba::Surface::Widget &&
            state_.activeWidget() == widgetId && IsWindowVisible(window_)) {
            RedrawWindow(window_, nullptr, nullptr,
                RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
        }

        if (!sessions_.RequestRestart(widgetId)) {
            pendingContentRevealWidget_.clear();
            RecordWidgetStartupFailure(widgetId, L"The restart request queue is full.");
            return;
        }

        lastActionWidgetId_ = widgetId;
        lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) + L" reloading";
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(lastActionMessage_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void RefreshWidgetSnapshot(
        const std::wstring_view widgetId,
        const std::uint64_t correlationId = 0) {
        if (!IsBridgeWidget(widgetId)) return;
        // A failed admission owns the presentation until an explicit retry.
        // Fetching a snapshot from the still-background registration would
        // replace the actionable startup diagnostic with a missing-cache error.
        if (sessions_.Failure(widgetId)) return;
        const auto currentWidget = state_.surface() == gba::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (currentWidget == widgetId) RememberCurrentFocus(widgetId);
        if (!sessions_.RequestSnapshot(widgetId, false, correlationId)) {
            RecordWidgetStartupFailure(widgetId, L"The snapshot request queue is full.");
        }
    }

    void MoveWidgetFocus(const std::wstring_view direction) {
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget) {
            return;
        }
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!snapshot) return;
        const auto activeScope = std::wstring_view(snapshot->activeInputScopeId);
        gba::input::NavigationDirection navigationDirection =
            gba::input::NavigationDirection::None;
        if (direction == L"left") navigationDirection = gba::input::NavigationDirection::Left;
        else if (direction == L"right") navigationDirection = gba::input::NavigationDirection::Right;
        else if (direction == L"up") navigationDirection = gba::input::NavigationDirection::Up;
        else if (direction == L"down") navigationDirection = gba::input::NavigationDirection::Down;
        const auto visibleFocus = gba::input::ResolveVisibleFocusTarget(
            focusedElementId_, activeScope, lastWidgetRenderResult_);
        if (!visibleFocus) {
            // A constrained viewport or responsive branch can leave a valid
            // root snapshot with no currently reachable control. Preserve the
            // same lower-boundary contract as an ordinary last row instead of
            // trapping controller focus in invisible widget geometry.
            if (gba::input::ShouldTransferFocusToTray(
                    navigationDirection,
                    activeScope == gba::input::RootInputScope(*snapshot),
                    false,
                    false)) {
                Dispatch(gba::Command::SampleWidgetBack);
            }
            return;
        }
        if (*visibleFocus != focusedElementId_) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            launcherExperienceProjection_.ObserveFocusInput(
                widgetId, GetTickCount64());
            focusedElementId_ = *visibleFocus;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        const auto projectedTarget =
            launcherExperienceProjection_.ProjectedFocusTarget(
                widgetId, focusedElementId_, direction);
        if (projectedTarget && gba::input::IsEnabledFocusTarget(
                *projectedTarget, lastWidgetRenderResult_)) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            launcherExperienceProjection_.ObserveFocusInput(
                widgetId, GetTickCount64());
            focusedElementId_ = *projectedTarget;
            (void)scrollEvidenceProbe_.RecordTarget(
                focusedElementId_, direction);
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            DispatchScrollPagination(widgetId, *snapshot, navigationDirection);
            return;
        }
        const auto* focused = gba::input::FindNodeInInputScope(
            *snapshot, focusedElementId_, activeScope);
        if (!focused) return;
        // Ordinary collection prefetch remains authoritative unless the exact
        // immutable Launcher Experience frame already projects an internal
        // game-to-game edge. In that case commit focus first, then let the
        // existing pagination owner observe the newly focused collection edge.
        if (DispatchScrollPagination(widgetId, *snapshot, navigationDirection))
            return;
        const std::wstring* target = nullptr;
        if (direction == L"up") target = &focused->focusUp;
        else if (direction == L"down") target = &focused->focusDown;
        else if (direction == L"left") target = &focused->focusLeft;
        else if (direction == L"right") target = &focused->focusRight;
        const auto* explicitTarget = target && !target->empty()
            ? gba::input::FindNodeInInputScope(*snapshot, *target, activeScope)
            : nullptr;
        const bool explicitNavigable = explicitTarget &&
            gba::input::IsEnabledFocusTarget(explicitTarget->id, lastWidgetRenderResult_);
        const bool explicitMoves = explicitTarget && gba::input::IsDistinctFocusMove(
            focusedElementId_, explicitTarget->id, explicitNavigable);
        if (explicitMoves) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            launcherExperienceProjection_.ObserveFocusInput(
                widgetId, GetTickCount64());
            focusedElementId_ = explicitTarget->id;
            (void)scrollEvidenceProbe_.RecordTarget(explicitTarget->id, direction);
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            DispatchScrollPagination(widgetId, *snapshot, navigationDirection);
            return;
        }

        const auto fallback = gba::input::FindGeometricFocusTarget(
            focusedElementId_, navigationDirection, lastWidgetRenderResult_);
        if (fallback) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            launcherExperienceProjection_.ObserveFocusInput(
                widgetId, GetTickCount64());
            focusedElementId_ = *fallback;
            (void)scrollEvidenceProbe_.RecordTarget(*fallback, direction);
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            DispatchScrollPagination(widgetId, *snapshot, navigationDirection);
            return;
        }
        if (gba::input::ShouldTransferFocusToTray(
                navigationDirection,
                activeScope == gba::input::RootInputScope(*snapshot),
                explicitMoves,
                fallback.has_value())) {
            Dispatch(gba::Command::SampleWidgetBack);
        }
    }

    static std::wstring_view ProtocolButton(const std::wstring_view button) {
        if (button == L"A") return L"a";
        if (button == L"B") return L"b";
        if (button == L"X") return L"x";
        if (button == L"Y") return L"y";
        if (button == L"LB") return L"leftBumper";
        if (button == L"RB") return L"rightBumper";
        if (button == L"LT") return L"leftTrigger";
        if (button == L"RT") return L"rightTrigger";
        if (button == L"LS") return L"leftStick";
        if (button == L"RS") return L"rightStick";
        if (button == L"Menu") return L"menu";
        if (button == L"View") return L"view";
        if (button == L"DPadLeft") return L"dPadLeft";
        if (button == L"DPadRight") return L"dPadRight";
        return L"";
    }

    bool HandleFocusedSliderModeButton(const std::wstring_view button) {
        if (button != L"A" && button != L"B") return false;
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget) return false;
        const std::wstring_view widget = state_.activeWidget();
        const auto* snapshot = InteractionSnapshotFor(widget);
        if (!snapshot) return false;
        const auto visible = gba::input::ResolveVisibleFocusTarget(
            focusedElementId_, snapshot->activeInputScopeId, lastWidgetRenderResult_);
        if (!visible) return false;
        if (*visible != focusedElementId_) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            focusedElementId_ = *visible;
            focusMemory_.Remember(widget, *snapshot, focusedElementId_);
        }
        const auto* focused = gba::input::FindNodeInInputScope(
            *snapshot, focusedElementId_, snapshot->activeInputScopeId);
        if (!focused) return false;
        const auto descriptor = SliderDescriptor(*snapshot, *focused);
        const bool activationRequired =
            focused->sliderInteractionMode == L"activateToAdjust";
        const bool adjustmentActive = activationRequired &&
            sliderInteraction_.AdjustmentModeActive(
                descriptor, GetTickCount64());
        using gba::input::FocusedSliderButtonRoute;
        switch (gba::input::RouteFocusedSliderButton(
            focused->kind, activationRequired, adjustmentActive, button)) {
        case FocusedSliderButtonRoute::EnterAdjustment:
            (void)sliderInteraction_.EnterAdjustmentMode(
                descriptor, GetTickCount64());
            (void)pressedInteraction_.Clear();
            InvalidateRect(window_, nullptr, FALSE);
            return true;
        case FocusedSliderButtonRoute::ExitAdjustment:
            (void)sliderInteraction_.ExitAdjustmentMode(
                descriptor, GetTickCount64());
            (void)pressedInteraction_.Clear();
            InvalidateRect(window_, nullptr, FALSE);
            return true;
        case FocusedSliderButtonRoute::Widget:
            return false;
        }
        return false;
    }

    void DispatchControllerAction(
        const std::wstring_view button,
        const bool physicalPress = false) {
        if (textEntryModal_.active()) {
            textEntryModal_.HandleController(button);
            return;
        }
        if (HandleFocusedSliderModeButton(button)) return;
        using gba::input::ControllerActionContext;
        using gba::input::ControllerActionRoute;
        const auto context = state_.focusRegion() == gba::FocusRegion::Tray
            ? ControllerActionContext::Tray
            : ControllerActionContext::RootWidgetScope;
        switch (gba::input::RouteControllerAction(context, button)) {
        case ControllerActionRoute::HostActivate:
            Dispatch(gba::Command::Activate);
            return;
        case ControllerActionRoute::HostToggleReorder:
            Dispatch(gba::Command::ToggleReorder);
            return;
        case ControllerActionRoute::HostCloseOverlay:
            Dispatch(gba::Command::ToggleOverlay);
            return;
        case ControllerActionRoute::Widget:
            DispatchWidgetAction(
                button,
                gba::input::NavigationEventPhase::Pressed,
                std::nullopt,
                physicalPress);
            return;
        case ControllerActionRoute::HostBackToDashboard:
        case ControllerActionRoute::None:
            return;
        }
    }

    void DispatchWidgetAction(
        const std::wstring_view button,
        const gba::input::NavigationEventPhase phase =
            gba::input::NavigationEventPhase::Pressed,
        const std::optional<double> requestedValue = std::nullopt,
        const bool physicalPress = false) {
        if (state_.surface() == gba::Surface::Hidden) {
            return;
        }

        const bool interactiveWidget = state_.surface() == gba::Surface::Widget &&
            state_.focusRegion() == gba::FocusRegion::Widget;
        const std::wstring_view widget = interactiveWidget
            ? state_.activeWidget()
            : state_.selectedWidget();

        if (IsBridgeWidget(widget)) {
            if (sessions_.Failure(widget)) {
                const auto route = gba::input::RouteFailedWidgetAction(
                    interactiveWidget, phase, button);
                if (route == gba::input::FailedWidgetActionRoute::Retry) {
                    RestartCurrentWidget();
                } else if (route ==
                           gba::input::FailedWidgetActionRoute::HostBackToDashboard) {
                    Dispatch(gba::Command::SampleWidgetBack);
                }
                return;
            }
            if (!SnapshotFor(widget)) {
                RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widget); });
            }
            const auto protocolButton = ProtocolButton(button);
            const auto* snapshot = InteractionSnapshotFor(widget);
            if (protocolButton.empty() || !snapshot) return;
            const bool isOpen = interactiveWidget;
            const auto visibleFocus = isOpen
                ? gba::input::ResolveVisibleFocusTarget(
                    focusedElementId_, snapshot->activeInputScopeId,
                    lastWidgetRenderResult_)
                : std::optional<std::wstring>{};
            if (isOpen && visibleFocus && *visibleFocus != focusedElementId_) {
                (void)pressedInteraction_.Clear();
                focusedElementId_ = *visibleFocus;
                focusMemory_.Remember(widget, *snapshot, focusedElementId_);
                InvalidateRect(window_, nullptr, FALSE);
            }
            if (physicalPress && isOpen && visibleFocus &&
                phase == gba::input::NavigationEventPhase::Pressed &&
                pressedInteraction_.Begin(
                    *snapshot, *visibleFocus, protocolButton)) {
                InvalidateRect(window_, nullptr, FALSE);
                UpdateWindow(window_);
            }
            if (isOpen && visibleFocus && protocolButton == L"a" &&
                phase == gba::input::NavigationEventPhase::Pressed &&
                OpenTextEntryModal(widget, *snapshot, *visibleFocus)) return;
            if (isOpen && visibleFocus) {
                const auto* focusedNode = gba::input::FindNodeInInputScope(
                    *snapshot, *visibleFocus, snapshot->activeInputScopeId);
                if (focusedNode && TryInvokeLocalWidgetPackageImport(
                        *snapshot, *focusedNode, protocolButton, phase)) return;
            }
            const auto handled = bridge_.SendControllerInput(
                widget, protocolButton,
                isOpen ? L"openWidget" : L"dashboardQuickAction",
                isOpen && visibleFocus
                    ? std::wstring_view(*visibleFocus)
                    : std::wstring_view{},
                snapshot->activeInputScopeId,
                snapshot->sequence,
                ++controllerSequence_, static_cast<long long>(GetTickCount64() * 1000),
                phase == gba::input::NavigationEventPhase::Repeated
                    ? std::wstring_view{L"repeated"}
                    : std::wstring_view{L"pressed"},
                requestedValue, gba::ControllerInputOrigin::PhysicalController);
            lastActionWidgetId_ = widget;
            if (!handled) {
                if (pressedInteraction_.Cancel(protocolButton))
                    InvalidateRect(window_, nullptr, FALSE);
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" input failed: " + bridge_.lastError();
            } else if (*handled) {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" handled " + std::wstring(button);
                RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widget); });
            } else {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" has no " + std::wstring(button) + L" action here";
                snapshot = InteractionSnapshotFor(widget);
                const auto unhandledContext = snapshot &&
                    std::wstring_view(snapshot->activeInputScopeId) ==
                        gba::input::RootInputScope(*snapshot)
                    ? gba::input::ControllerActionContext::RootWidgetScope
                    : gba::input::ControllerActionContext::NestedWidgetScope;
                if (isOpen && gba::input::RouteUnhandledControllerAction(
                        unhandledContext, button) ==
                        gba::input::ControllerActionRoute::HostBackToDashboard) {
                    Dispatch(gba::Command::SampleWidgetBack);
                    return;
                }
            }
            lastActionExpiresAt_ = GetTickCount64() + 1800;
            AppendDiagnostic(lastActionMessage_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        if (interactiveWidget &&
            gba::input::RouteUnhandledControllerAction(
                gba::input::ControllerActionContext::RootWidgetScope, button) ==
                gba::input::ControllerActionRoute::HostBackToDashboard) {
            Dispatch(gba::Command::SampleWidgetBack);
            return;
        }

        lastActionWidgetId_ = widget;
        lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                             L": forwarded " + std::wstring(button);
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(L"Widget action " + lastActionMessage_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    bool OpenTextEntryModal(
        const std::wstring_view widget,
        const gba::WidgetSnapshot& snapshot,
        const std::wstring_view nodeId) {
        const auto* node = gba::input::FindNodeInInputScope(
            snapshot, nodeId, snapshot.activeInputScopeId);
        if (!node || !node->isTextEntry) return false;
        const auto* descriptor = sessions_.FindDescriptor(widget);
        if (!descriptor) return true;
        const auto request = gba::input::CaptureTextEntryActionRequest(
            widget, descriptor->runtimeGeneration, snapshot, nodeId);
        if (!request) return true;

        sliderInteraction_.DeactivateAll();
        (void)pressedInteraction_.Clear();
        focusedElementId_ = request->nodeId;
        focusMemory_.Remember(widget, snapshot, focusedElementId_);
        const bool protectedWifi = descriptor->protectedWifiPromptSupported &&
            request->actionId == L"wifi.connect.protected";
        const auto modalTitle = protectedWifi
            ? std::wstring(L"Password for ") + request->placeholder
            : request->placeholder;
        auto modalResult = textEntryModal_.Show(
            instance_, window_, request->value,
            modalTitle, request->maximumLength, protectedWifi);
        bool actionDispatched{};
        if (modalResult.outcome == gba::input::TextEntryModalOutcome::Committed &&
            modalResult.committedText) {
            const auto* currentDescriptor = sessions_.FindDescriptor(request->widgetId);
            const auto* currentSnapshot = InteractionSnapshotFor(request->widgetId);
            const auto target = currentDescriptor && currentSnapshot
                ? gba::input::ResolveTextEntryActionTarget(
                    *request,
                    state_.surface() == gba::Surface::Widget &&
                        state_.focusRegion() == gba::FocusRegion::Widget,
                    state_.activeWidget(),
                    currentDescriptor->runtimeGeneration, *currentSnapshot)
                : std::nullopt;
            if (target) {
                std::optional<bool> handled;
                if (protectedWifi) {
                    const auto result = bridge_.ConnectProtectedWifi(
                        request->widgetId,
                        request->runtimeGeneration,
                        target->sourceElementId,
                        modalResult.committedText->view());
                    handled = result.has_value();
                    lastActionWidgetId_ = request->widgetId;
                    lastActionMessage_ = result && *result == L"connecting"
                        ? L"Connecting to protected Wi-Fi"
                        : L"Protected Wi-Fi connection was not started";
                    lastActionExpiresAt_ = GetTickCount64() + 3000;
                } else {
                    handled = bridge_.SendAction(
                        request->widgetId, target->actionId, target->sourceElementId,
                        target->activeInputScopeId,
                        std::wstring_view(
                            modalResult.committedText->view().data(),
                            modalResult.committedText->view().size()));
                }
                actionDispatched = handled && *handled;
                if (handled && *handled) {
                    RefreshAndApplyPresentation([&] {
                        RefreshWidgetSnapshot(request->widgetId);
                    });
                }
            }
            modalResult.committedText->clear();
        }
        if (state_.surface() == gba::Surface::Widget)
            RestoreFocusForActiveSurface(state_.activeWidget());
        const auto outcome = [&] {
            switch (modalResult.outcome) {
            case gba::input::TextEntryModalOutcome::Failed: return L"failed";
            case gba::input::TextEntryModalOutcome::Cancelled: return L"cancel";
            case gba::input::TextEntryModalOutcome::Closed: return L"close";
            case gba::input::TextEntryModalOutcome::Committed: return L"commit";
            }
            return L"unknown";
        }();
        std::wstring preservation{L"not-applicable"};
        if (modalResult.outcome != gba::input::TextEntryModalOutcome::Committed) {
            const auto* currentSnapshot = InteractionSnapshotFor(request->widgetId);
            const auto* currentNode = currentSnapshot
                ? gba::input::FindNodeInInputScope(
                    *currentSnapshot, request->nodeId,
                    currentSnapshot->activeInputScopeId)
                : nullptr;
            preservation = !currentNode || !currentNode->isTextEntry
                ? L"unavailable"
                : currentNode->textEntryValue == request->value
                    ? L"preserved" : L"changed";
        }
        AppendDiagnostic(
            L"Text entry modal outcome=" + std::wstring(outcome) +
            L" action-dispatched=" + (actionDispatched ? L"true" : L"false") +
            L" committed-value=" + preservation +
            L" focus=" + (focusedElementId_.empty() ? L"none" : focusedElementId_));
        (void)SetFocus(window_);
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    [[nodiscard]] bool GraphicsResourcesReady() const noexcept {
        return backgroundBrush_ && cardBrush_ && textBrush_ && secondaryBrush_ &&
               dashboardSecondaryBrush_ && dashboardTextBrush_ && accentBrush_ &&
               successBrush_ && trayItemBrush_ && trayItemTextBrush_ &&
               selectedTextBrush_ && focusBrush_ && titleFormat_ && bodyFormat_ &&
               hintFormat_ && iconFormat_;
    }

    bool EnsureGraphicsResources(
        const unsigned int requestedWidth = 0,
        const unsigned int requestedHeight = 0,
        const UINT requestedDpi = 0) {
        if (renderTarget_ && GraphicsResourcesReady()) return true;
        RECT client{};
        GetClientRect(window_, &client);
        const auto size = D2D1::SizeU(
            requestedWidth != 0
                ? requestedWidth
                : static_cast<UINT32>(client.right - client.left),
            requestedHeight != 0
                ? requestedHeight
                : static_cast<UINT32>(client.bottom - client.top));
        if (!renderTarget_) {
            if (FAILED(d2dFactory_->CreateHwndRenderTarget(
                    D2D1::RenderTargetProperties(),
                    D2D1::HwndRenderTargetProperties(window_, size),
                    hwndRenderTarget_.ReleaseAndGetAddressOf()))) {
                return false;
            }
            renderTarget_ = hwndRenderTarget_;
        }
        const float windowDpi = static_cast<float>(
            requestedDpi != 0 ? requestedDpi : GetDpiForWindow(window_));
        renderTarget_->SetDpi(windowDpi > 0 ? windowDpi : 96.0F,
                              windowDpi > 0 ? windowDpi : 96.0F);

        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        if (const auto metrics = gba::ComputeOverlayRenderMetrics(
                static_cast<int>(size.width), static_cast<int>(size.height),
                windowDpi > 0 ? static_cast<UINT>(windowDpi) : 96U,
                interfaceScale)) {
            RebuildShellStyles(metrics->viewportWidthDip, metrics->viewportHeightDip);
        }

        const auto colorOr = [](const std::optional<gba::NativeColor>& value,
                                const gba::NativeColor fallback) {
            return value.value_or(fallback);
        };
        const gba::NativeColor defaultText{0xF7 / 255.0F, 0xF7 / 255.0F,
                                            0xFA / 255.0F, 1};
        const gba::NativeColor defaultSecondary{0x9B / 255.0F, 0xA3 / 255.0F,
                                                 0xB3 / 255.0F, 1};
        const gba::NativeColor defaultSuccess{0x45 / 255.0F, 0xD4 / 255.0F,
                                               0x83 / 255.0F, 1};
        const auto PaintedLayer = [](const std::optional<gba::NativeColor>& configured,
                                     const gba::NativeColor fallback,
                                     const float opacity) {
            auto result = configured.value_or(fallback);
            result.alpha *= std::isfinite(opacity)
                ? std::clamp(opacity, 0.0F, 1.0F)
                : 1.0F;
            return result;
        };
        const auto trayBackground = PaintedLayer(
            trayStyle_.background(), effectiveCanvasBackground_, trayStyle_.opacity());
        const auto panelBackground = PaintedLayer(
            panelStyle_.background(), kDefaultPanel, panelStyle_.opacity());
        const auto foreground = colorOr(titleStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultText));
        const auto secondary = colorOr(hintStyle_.foreground(),
            colorOr(bodyStyle_.foreground(), defaultSecondary));
        const auto dashboardForeground = colorOr(
            dashboardTitleStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultText));
        const auto dashboardSecondary = colorOr(
            dashboardHintStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultSecondary));
        const auto selectedBackground = trayItemSelectedFocusedStyle_.background()
            ? PaintedLayer(trayItemSelectedFocusedStyle_.background(), kDefaultAccent,
                           trayItemSelectedFocusedStyle_.opacity())
            : PaintedLayer(trayItemSelectedStyle_.background(), kDefaultAccent,
                           trayItemSelectedStyle_.opacity());
        const auto itemBackground = PaintedLayer(
            trayItemStyle_.background(), trayBackground, trayItemStyle_.opacity());
        const auto trayItemForeground = colorOr(
            trayItemStyle_.foreground(), dashboardForeground);
        const auto selectedForeground = colorOr(
            trayItemSelectedFocusedStyle_.foreground(),
            colorOr(trayItemSelectedStyle_.foreground(), foreground));
        const auto focusColor = colorOr(
            trayItemSelectedFocusedStyle_.outlineColor(),
            colorOr(trayItemFocusedStyle_.outlineColor(), kDefaultAccent));

        renderTarget_->CreateSolidColorBrush(
            D2DColor(trayBackground), backgroundBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(panelBackground), cardBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(foreground), textBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(secondary), secondaryBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(dashboardForeground),
            dashboardTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(dashboardSecondary),
            dashboardSecondaryBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(selectedBackground), accentBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(colorOr(statusStyle_.foreground(), defaultSuccess)),
            successBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(itemBackground), trayItemBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(trayItemForeground),
            trayItemTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(selectedForeground), selectedTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(focusColor), focusBrush_.ReleaseAndGetAddressOf());

        const auto hasProperty = [&](const std::wstring_view role,
                                     const std::wstring_view property) {
            return ShellComputedStyle(role).contains(std::wstring(property));
        };
        const float textScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->textScale)
            : 1.0F;
        const auto fontSize = [&](const std::wstring_view role,
                                  const gba::NativeRenderStyle& style,
                                  const float fallback) {
            return (hasProperty(role, L"font-size") ? style.fontSizePx() : fallback) *
                   textScale;
        };
        const auto fontFamily = [&](const std::wstring_view role,
                                    const gba::NativeRenderStyle& style,
                                    const wchar_t* fallback) -> const wchar_t* {
            return hasProperty(role, L"font-family") ? style.fontFamily().c_str() : fallback;
        };
        const auto fontWeight = [&](const std::wstring_view role,
                                    const gba::NativeRenderStyle& style,
                                    const DWRITE_FONT_WEIGHT fallback) {
            return hasProperty(role, L"font-weight")
                ? static_cast<DWRITE_FONT_WEIGHT>(std::clamp(style.fontWeight(), 100, 950))
                : fallback;
        };

        writeFactory_->CreateTextFormat(fontFamily(L"title", titleStyle_, L"Segoe UI Variable Display"), nullptr,
                                        fontWeight(L"title", titleStyle_, DWRITE_FONT_WEIGHT_SEMI_BOLD),
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        fontSize(L"title", titleStyle_, 25.0F), L"en-us",
                                        titleFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(fontFamily(L"body", bodyStyle_, L"Segoe UI Variable Text"), nullptr,
                                        fontWeight(L"body", bodyStyle_, DWRITE_FONT_WEIGHT_NORMAL),
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        fontSize(L"body", bodyStyle_, 16.0F), L"en-us",
                                        bodyFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(fontFamily(L"hint", hintStyle_, L"Segoe UI Variable Text"), nullptr,
                                        fontWeight(L"hint", hintStyle_, DWRITE_FONT_WEIGHT_SEMI_BOLD),
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        fontSize(L"hint", hintStyle_, 14.0F), L"en-us",
                                        hintFormat_.ReleaseAndGetAddressOf());
        if (hintFormat_) {
            // Host chrome has one reserved row. Wrapping controller mappings
            // into a clipped second line is never a valid responsive state;
            // DrawWidgetFooter selects a shorter semantic guide instead.
            hintFormat_->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        }
        writeFactory_->CreateTextFormat(L"Segoe UI Variable Display", nullptr,
                                        DWRITE_FONT_WEIGHT_SEMI_BOLD,
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        23.0F, L"en-us", iconFormat_.ReleaseAndGetAddressOf());
        if (iconFormat_) {
            iconFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            iconFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        }
        panelCornerRadius_ = hasProperty(L"panel", L"corner-radius")
            ? panelStyle_.cornerRadiusPx() : 18.0F;
        trayCornerRadius_ = hasProperty(L"tray", L"corner-radius")
            ? trayStyle_.cornerRadiusPx() : 22.0F;
        trayItemCornerRadius_ = hasProperty(L"tray-item", L"corner-radius")
            ? trayItemStyle_.cornerRadiusPx() : 16.0F;
        focusOutlineWidth_ = hasProperty(L"tray-item:selected:focused", L"outline-width")
            ? trayItemSelectedFocusedStyle_.outlineWidthPx()
            : hasProperty(L"tray-item:focused", L"outline-width")
                ? trayItemFocusedStyle_.outlineWidthPx() : 2.0F;
        return GraphicsResourcesReady();
    }

    void DiscardGraphicsResources(const bool discardRenderTarget = true) {
        if (declarativeRenderer_) declarativeRenderer_->DiscardTargetResources();
        launcherExperienceProjection_.DiscardTargetResources();
        // Hit and focus rectangles are valid only for the render target's
        // logical viewport. Never dispatch controller focus through geometry
        // retained across a resize, DPI migration, or appearance rebuild.
        lastWidgetRenderResult_ = {};
        iconFormat_.Reset();
        hintFormat_.Reset();
        bodyFormat_.Reset();
        titleFormat_.Reset();
        focusBrush_.Reset();
        selectedTextBrush_.Reset();
        trayItemTextBrush_.Reset();
        trayItemBrush_.Reset();
        accentBrush_.Reset();
        successBrush_.Reset();
        secondaryBrush_.Reset();
        textBrush_.Reset();
        dashboardSecondaryBrush_.Reset();
        dashboardTextBrush_.Reset();
        cardBrush_.Reset();
        backgroundBrush_.Reset();
        if (discardRenderTarget) {
            renderTarget_.Reset();
            hwndRenderTarget_.Reset();
        }
    }

    void DrawTextLine(std::wstring_view text,
                      IDWriteTextFormat* format,
                      const D2D1_RECT_F& rectangle,
                      ID2D1Brush* brush) const {
        renderTarget_->DrawTextW(text.data(), static_cast<UINT32>(text.size()), format,
                                 rectangle, brush, D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }

    enum class CompositionPaintLayer {
        Combined,
        Content,
        Guide,
        Tray,
    };

    void DrawCurrentFrame(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi,
        const POINT updateOffset,
        const CompositionPaintLayer layer = CompositionPaintLayer::Combined,
        const gba::shell::TrayLayout* trayLayout = nullptr,
        const gba::declarative::Rect* guideBounds = nullptr) {
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            static_cast<int>(width), static_cast<int>(height),
            dpi != 0 ? dpi : 96U, interfaceScale);
        if (!metrics) return;

        // IDCompositionSurface::BeginDraw reports its backing-surface offset
        // in physical pixels. This device context draws in DIPs after SetDpi,
        // so convert the atlas offset before composing it after interface zoom.
        // Treating the raw pixel value as DIPs over-translates every child at
        // non-96 DPI and can crop an otherwise correctly placed tray surface.
        const float deviceDipPerPixel = 96.0F /
            static_cast<float>(dpi != 0 ? dpi : 96U);
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
        renderTarget_->Clear(compositionSurface_.available()
            ? D2D1::ColorF(0.0F, 0.0F, 0.0F, 0.0F)
            : D2DColor(kSafeCanvasFallback));
        renderTarget_->SetTransform(
            D2D1::Matrix3x2F::Scale(
                metrics->interfaceScale, metrics->interfaceScale) *
            D2D1::Matrix3x2F::Translation(
                static_cast<float>(updateOffset.x) * deviceDipPerPixel,
                static_cast<float>(updateOffset.y) * deviceDipPerPixel));

        if (layer == CompositionPaintLayer::Tray && trayLayout) {
            DrawIconStrip(
                width, height, nullptr, nullptr, trayLayout, false);
            renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
            return;
        }
        if (layer == CompositionPaintLayer::Guide && guideBounds) {
            if (state_.surface() == gba::Surface::Widget) {
                gba::OverlaySurfaceGeometry localGuideGeometry;
                localGuideGeometry.panelWidth = guideBounds->width;
                localGuideGeometry.footerHeight = guideBounds->height;
                DrawWidgetFooter(localGuideGeometry, guideBounds);
                if (accessibilityActive_ && trayLayout) {
                    PublishTrayAccessibility(*trayLayout, width, height);
                }
            }
            renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
            return;
        }

        if (state_.surface() == gba::Surface::Widget) {
            DrawWidget(metrics->viewportWidthDip, metrics->viewportHeightDip,
                       metrics->physicalPixelsPerDip, layer,
                       trayLayout, guideBounds);
        } else {
            declarativeMotionActive_ = false;
            DrawDashboard(metrics->viewportWidthDip, metrics->viewportHeightDip,
                          layer, trayLayout);
        }
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
    }

    struct CompositionLayerGeometry final {
        unsigned int width{};
        unsigned int height{};
    };

    struct CompositionFrameSet final {
        std::vector<gba::OverlayCompositionSurface::Frame> frames;
        std::optional<gba::shell::RetainedTrayState> trayState;
        std::wstring guideKey;
    };

    // One visible overlay session owns this small chrome raster and its
    // absolute screen anchor. It is intentionally independent of whichever
    // destination widget currently owns the content viewport.
    struct FixedChromeAnchor final {
        HMONITOR monitor{};
        RECT monitorArea{};
        RECT workArea{};
        UINT dpi{96};
        float interfaceScale{1.0F};
        float textScale{1.0F};
        std::uint64_t appearanceRevision{};
        std::vector<std::wstring> catalogOrder;
        RECT intendedWindowBounds{};
        RECT actualWindowBounds{};
        FixedChromePlacementReason reason{FixedChromePlacementReason::None};
    };

    struct CompositionChromeSession final {
        unsigned int canvasWidth{};
        unsigned int canvasHeight{};
        unsigned int guideWidth{};
        unsigned int guideHeight{};
        unsigned int trayWidth{};
        unsigned int trayHeight{};
        unsigned int trayFocusPadding{};
        UINT dpi{};
        std::uint64_t appearanceRevision{};
        float pixelsPerDip{};
        gba::declarative::Rect guideBounds;
        RECT trayClientBounds{};
        RECT guideClientBounds{};
        RECT windowBounds{};
        gba::shell::FixedChromeSessionKey key;
    };

    void RequestFixedChromeAnchorRefresh(
        const FixedChromePlacementReason reason) {
        fixedChromeAnchor_.reset();
        compositionChromeSession_.reset();
        pendingFixedChromePlacementReason_ = reason;
    }

    [[nodiscard]] static std::wstring FormatPhysicalBounds(
        const RECT& bounds) {
        return std::to_wstring(bounds.left) + L"," +
            std::to_wstring(bounds.top) + L"," +
            std::to_wstring(bounds.right - bounds.left) + L"," +
            std::to_wstring(bounds.bottom - bounds.top);
    }

    [[nodiscard]] std::optional<RECT> ActualChromeWindowBounds() const {
        RECT actual{};
        return chromeWindow_ && GetWindowRect(chromeWindow_, &actual)
            ? std::optional<RECT>{actual}
            : std::nullopt;
    }

    [[nodiscard]] std::optional<RECT> ProjectChromeClientBoundsToScreen(
        const RECT& clientBounds) const {
        RECT client{};
        if (!chromeWindow_ || !GetClientRect(chromeWindow_, &client) ||
            clientBounds.left < client.left ||
            clientBounds.top < client.top ||
            clientBounds.right > client.right ||
            clientBounds.bottom > client.bottom ||
            clientBounds.right <= clientBounds.left ||
            clientBounds.bottom <= clientBounds.top) {
            return std::nullopt;
        }
        POINT topLeft{clientBounds.left, clientBounds.top};
        POINT bottomRight{clientBounds.right, clientBounds.bottom};
        if (!ClientToScreen(chromeWindow_, &topLeft) ||
            !ClientToScreen(chromeWindow_, &bottomRight)) {
            return std::nullopt;
        }
        return RECT{
            topLeft.x, topLeft.y, bottomRight.x, bottomRight.y,
        };
    }

    [[nodiscard]] bool EnsureFixedChromeAnchor() {
        if (fixedChromeAnchor_) return true;
        if (pendingFixedChromePlacementReason_ ==
            FixedChromePlacementReason::None) {
            AppendDiagnostic(
                L"Fixed chrome anchor selection rejected reason=content-route");
            return false;
        }

        const HWND remembered = reinterpret_cast<HWND>(
            GbaOverlayPlatformRememberedForegroundTarget(platform_));
        const HWND targetWindow = reinterpret_cast<HWND>(
            GbaOverlayPlatformResolveForegroundTarget(
                platform_, reinterpret_cast<std::uintptr_t>(window_),
                PlatformBoolean(remembered && IsWindow(remembered))));
        const HMONITOR monitor = MonitorFromWindow(
            targetWindow, MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        if (!monitor || !GetMonitorInfoW(monitor, &monitorInfo)) {
            AppendDiagnostic(
                L"Unable to resolve fixed chrome monitor for placement reason=" +
                std::wstring(FixedChromePlacementReasonName(
                    pendingFixedChromePlacementReason_)));
            return false;
        }
        UINT dpi = 96;
        UINT dpiY = 96;
        if (FAILED(GetDpiForMonitor(
                monitor, MDT_EFFECTIVE_DPI, &dpi, &dpiY)) ||
            dpi == 0 || dpiY == 0) {
            dpi = 96;
        }
        const auto& appearance = appearanceState_.current();
        FixedChromeAnchor anchor;
        anchor.monitor = monitor;
        anchor.monitorArea = monitorInfo.rcMonitor;
        anchor.workArea = monitorInfo.rcWork;
        anchor.dpi = dpi;
        anchor.interfaceScale = appearance
            ? static_cast<float>(appearance->interfaceScale) : 1.0F;
        anchor.textScale = appearance
            ? static_cast<float>(appearance->textScale) : 1.0F;
        anchor.appearanceRevision = static_cast<std::uint64_t>(
            std::max<long long>(0, appearance ? appearance->revision : 0));
        anchor.catalogOrder = state_.order();
        anchor.reason = pendingFixedChromePlacementReason_;
        fixedChromeAnchor_ = std::move(anchor);
        return true;
    }

    [[nodiscard]] static RECT PhysicalCrop(
        const gba::declarative::Rect& logical,
        const float pixelsPerDip,
        const unsigned int fullWidth,
        const unsigned int fullHeight) noexcept {
        RECT result{
            static_cast<LONG>(std::floor(logical.x * pixelsPerDip)),
            static_cast<LONG>(std::floor(logical.y * pixelsPerDip)),
            static_cast<LONG>(std::ceil((logical.x + logical.width) * pixelsPerDip)),
            static_cast<LONG>(std::ceil((logical.y + logical.height) * pixelsPerDip)),
        };
        result.left = std::clamp(result.left, 0L, static_cast<LONG>(fullWidth));
        result.top = std::clamp(result.top, 0L, static_cast<LONG>(fullHeight));
        result.right = std::clamp(
            result.right, result.left + 1, static_cast<LONG>(fullWidth));
        result.bottom = std::clamp(
            result.bottom, result.top + 1, static_cast<LONG>(fullHeight));
        return result;
    }

    [[nodiscard]] bool EnsureCompositionChromeSession() {
        if (compositionChromeSession_) return true;
        if (!fixedChromeAnchor_ ||
            pendingFixedChromePlacementReason_ ==
                FixedChromePlacementReason::None) {
            AppendDiagnostic(
                L"Fixed chrome session creation rejected reason=content-route");
            return false;
        }
        auto& anchor = *fixedChromeAnchor_;
        const UINT effectiveDpi = anchor.dpi != 0 ? anchor.dpi : 96U;
        const float interfaceScale = anchor.interfaceScale;
        const RECT& workArea = anchor.workArea;
        const unsigned int workWidth =
            static_cast<unsigned int>(workArea.right - workArea.left);
        const unsigned int workHeight =
            static_cast<unsigned int>(workArea.bottom - workArea.top);
        gba::shell::FixedChromeSessionKey key{
            workArea,
            effectiveDpi,
            interfaceScale,
            anchor.appearanceRevision,
            anchor.catalogOrder,
        };
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            static_cast<int>(workWidth), static_cast<int>(workHeight),
            effectiveDpi, interfaceScale);
        if (!metrics) return false;
        const auto policyLayout = gba::shell::ComputeTrayLayout(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            state_.order().size(), state_.selectedSlot());
        if (!policyLayout) return false;

        constexpr float kGuideHeightDip = 58.0F;
        constexpr float kGuideToTrayGapDip = 46.0F;
        const LONG focusPadding = std::max(2L, static_cast<LONG>(std::ceil(
            (focusOutlineWidth_ + 2.0F) * metrics->physicalPixelsPerDip)));
        const LONG trayWidth = std::max(
            1L, static_cast<LONG>(std::ceil(
                policyLayout->stripBounds.width *
                metrics->physicalPixelsPerDip))) + focusPadding * 2;
        const LONG trayHeight = std::max(
            1L, static_cast<LONG>(std::ceil(
                policyLayout->stripBounds.height *
                metrics->physicalPixelsPerDip))) + focusPadding * 2;
        const LONG guideWidth = std::max(
            1L, static_cast<LONG>(std::ceil(
                policyLayout->stripBounds.width *
                metrics->physicalPixelsPerDip)));
        const LONG guideHeight = std::max(
            1L, static_cast<LONG>(std::ceil(
                kGuideHeightDip * metrics->physicalPixelsPerDip)));
        const LONG guideToTrayGap = std::max(
            0L, static_cast<LONG>(std::lround(
                kGuideToTrayGapDip * metrics->physicalPixelsPerDip)));
        const LONG chromeWidth = std::max(trayWidth, guideWidth);
        const LONG chromeHeight =
            guideHeight + guideToTrayGap + trayHeight;
        if (chromeWidth <= 0 || chromeHeight <= 0) return false;

        CompositionChromeSession session;
        session.canvasWidth = static_cast<unsigned int>(chromeWidth);
        session.canvasHeight = static_cast<unsigned int>(chromeHeight);
        session.guideWidth = static_cast<unsigned int>(guideWidth);
        session.guideHeight = static_cast<unsigned int>(guideHeight);
        session.trayWidth = static_cast<unsigned int>(trayWidth);
        session.trayHeight = static_cast<unsigned int>(trayHeight);
        session.trayFocusPadding = static_cast<unsigned int>(focusPadding);
        session.dpi = effectiveDpi;
        session.key = std::move(key);
        session.pixelsPerDip = metrics->physicalPixelsPerDip;
        const LONG trayTop = guideHeight + guideToTrayGap;
        const auto localTrayLayout = ComputeCompositionTrayLayout(session);
        if (!localTrayLayout) return false;
        const LONG guideLeft = (chromeWidth - guideWidth) / 2;
        const LONG trayLeft = (chromeWidth - trayWidth) / 2;
        session.guideClientBounds = {
            guideLeft, 0, guideLeft + guideWidth, guideHeight,
        };
        session.trayClientBounds = {
            trayLeft, trayTop, trayLeft + trayWidth, trayTop + trayHeight,
        };
        session.guideBounds = {
            0.0F,
            0.0F,
            static_cast<float>(guideWidth) / session.pixelsPerDip,
            static_cast<float>(guideHeight) / session.pixelsPerDip,
        };
        session.windowBounds = gba::shell::ComputeFixedChromeWindowBounds(
            workArea, chromeWidth, chromeHeight);
        if (!gba::shell::ApplyFixedChromeWindow(
                window_, chromeWindow_, session.windowBounds, true)) {
            AppendDiagnostic(L"Fixed chrome placement failed error=" +
                             std::to_wstring(GetLastError()));
            return false;
        }
        RECT actualWindowBounds{};
        if (!GetWindowRect(chromeWindow_, &actualWindowBounds)) {
            AppendDiagnostic(
                L"Fixed chrome applied rectangle unavailable error=" +
                std::to_wstring(GetLastError()));
            return false;
        }
        RECT actualClient{};
        const auto guideScreen = ProjectChromeClientBoundsToScreen(
            session.guideClientBounds);
        const auto trayScreen = ProjectChromeClientBoundsToScreen(
            session.trayClientBounds);
        if (!GetClientRect(chromeWindow_, &actualClient) ||
            actualClient.right - actualClient.left != chromeWidth ||
            actualClient.bottom - actualClient.top != chromeHeight ||
            !guideScreen || !trayScreen) {
            AppendDiagnostic(
                L"Fixed chrome client layout did not fit its applied HWND");
            return false;
        }
        const gba::OverlayCompositionSurface::ChromePresentation chrome{
            static_cast<float>(session.guideClientBounds.left),
            static_cast<float>(session.guideClientBounds.top),
            static_cast<float>(session.trayClientBounds.left),
            static_cast<float>(session.trayClientBounds.top),
        };
        gba::OverlayCompositionSurface::CommitTiming chromeTiming;
        const HRESULT chromeResult =
            compositionSurface_.CommitChromePresentation(chrome, chromeTiming);
        if (FAILED(chromeResult)) {
            AppendDiagnostic(
                L"Fixed chrome child presentation failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(chromeResult)));
            return false;
        }
        ++fixedChromePlacementCount_;
        anchor.intendedWindowBounds = session.windowBounds;
        anchor.actualWindowBounds = actualWindowBounds;
        const bool exact = EqualRect(
            &anchor.intendedWindowBounds, &anchor.actualWindowBounds) != FALSE;
        AppendDiagnostic(
            L"Fixed chrome placement reason=" +
            std::wstring(FixedChromePlacementReasonName(anchor.reason)) +
            L" monitor=" + std::to_wstring(
                reinterpret_cast<std::uintptr_t>(anchor.monitor)) +
            L" intended=" + FormatPhysicalBounds(anchor.intendedWindowBounds) +
            L" actual=" + FormatPhysicalBounds(anchor.actualWindowBounds) +
            L" client=" + FormatPhysicalBounds(actualClient) +
            L" guide-client=" +
            FormatPhysicalBounds(session.guideClientBounds) +
            L" tray-client=" +
            FormatPhysicalBounds(session.trayClientBounds) +
            L" guide-screen=" + FormatPhysicalBounds(*guideScreen) +
            L" tray-screen=" + FormatPhysicalBounds(*trayScreen) +
            L" exact=" + (exact ? std::wstring{L"true"} : L"false") +
            L" chrome-commit-us=" +
            std::to_wstring(chromeTiming.commitMicroseconds) +
            L" placement-count=" +
            std::to_wstring(fixedChromePlacementCount_));
        if (!exact) return false;
        compositionChromeSession_ = std::move(session);
        pendingFixedChromePlacementReason_ = FixedChromePlacementReason::None;
        return true;
    }

    [[nodiscard]] std::optional<gba::OverlayPlacement>
    AnchorContentPlacementToChrome(
        gba::OverlayPlacement placement) const {
        if (!fixedChromeAnchor_ || !compositionChromeSession_ ||
            placement.width <= 0 || placement.height <= 0) {
            return std::nullopt;
        }
        const auto guide = ProjectChromeClientBoundsToScreen(
            compositionChromeSession_->guideClientBounds);
        if (!guide) return std::nullopt;

        constexpr float kPanelToGuideGapDip = 3.0F;
        const int panelToGuideGap = static_cast<int>(std::lround(
            kPanelToGuideGapDip * fixedChromeAnchor_->dpi / 96.0F *
            fixedChromeAnchor_->interfaceScale));
        const RECT& work = fixedChromeAnchor_->workArea;
        const int maximumX = work.right - placement.width;
        const int maximumY = work.bottom - placement.height;
        if (maximumX < work.left || maximumY < work.top) return std::nullopt;
        placement.x = work.left +
            ((work.right - work.left) - placement.width) / 2;
        placement.y = std::clamp(
            static_cast<int>(guide->top) - panelToGuideGap - placement.height,
            static_cast<int>(work.top), maximumY);
        return placement;
    }

    [[nodiscard]] std::optional<gba::shell::TrayLayout>
    ComputeCompositionTrayLayout(
        const CompositionChromeSession& session) const {
        const float interfaceScale =
            static_cast<float>(session.key.interfaceScale);
        const unsigned int contentWidth = session.trayWidth -
            std::min(session.trayWidth, session.trayFocusPadding * 2U);
        const unsigned int contentHeight = session.trayHeight -
            std::min(session.trayHeight, session.trayFocusPadding * 2U);
        if (contentWidth == 0 || contentHeight == 0) return std::nullopt;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            static_cast<int>(contentWidth), static_cast<int>(contentHeight),
            session.dpi, interfaceScale);
        if (!metrics) return std::nullopt;
        auto layout = gba::shell::ComputeTrayLayout(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            state_.order().size(), state_.selectedSlot(),
            gba::shell::TrayBand{0.0F, metrics->viewportHeightDip});
        if (!layout) return std::nullopt;
        const float inset = static_cast<float>(session.trayFocusPadding) /
            session.pixelsPerDip;
        const auto offset = [inset](gba::declarative::Rect& bounds) {
            bounds.x += inset;
            bounds.y += inset;
        };
        offset(layout->stripBounds);
        for (auto& tile : layout->tiles) offset(tile.bounds);
        if (layout->previousOverflow) offset(layout->previousOverflow->bounds);
        if (layout->nextOverflow) offset(layout->nextOverflow->bounds);
        return layout;
    }

    [[nodiscard]] std::optional<gba::shell::TrayLayout>
    CurrentCompositionTrayLayout() const {
        return compositionChromeSession_
            ? ComputeCompositionTrayLayout(*compositionChromeSession_)
            : std::nullopt;
    }

    [[nodiscard]] std::wstring CurrentGuidePaintKey(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi) const {
        if (state_.surface() != gba::Surface::Widget) return L"dashboard:none";
        std::wstring key = std::wstring{state_.activeWidget()} + L"\n" +
            std::to_wstring(width) + L"x" + std::to_wstring(height) + L"\n" +
            std::to_wstring(dpi) + L"\n" +
            std::to_wstring(appearanceState_.current()
                ? appearanceState_.current()->revision : 0) + L"\n" +
            std::to_wstring(static_cast<int>(state_.focusRegion()));
        if (state_.focusRegion() == gba::FocusRegion::Tray) {
            if (const auto status = DashboardStatus()) key += L"\n" + *status;
            key += L"\n" + DashboardHint(static_cast<float>(width));
        } else {
            if (const auto status = OpenWidgetStatus()) key += L"\n" + *status;
            key += L"\n" + OpenWidgetPrompt();
            if (const auto* snapshot = InteractionSnapshotFor(state_.activeWidget()))
                key += L"\n" + snapshot->activeInputScopeId;
        }
        return key;
    }

    [[nodiscard]] std::optional<gba::shell::RetainedTrayState>
    CurrentTrayPaintState(
        const gba::shell::TrayLayout& layout,
        const unsigned int width,
        const unsigned int height,
        const float pixelsPerDip) const {
        gba::shell::RetainedTrayState state;
        state.width = width;
        state.height = height;
        state.appearanceRevision = static_cast<std::uint64_t>(std::max<long long>(
            0, appearanceState_.current() ? appearanceState_.current()->revision : 0));
        const auto relative = [&](const gba::declarative::Rect& bounds) {
            RECT result = PhysicalCrop(
                bounds, pixelsPerDip,
                state.width, state.height);
            result.left = std::clamp(result.left, 0L, static_cast<LONG>(state.width));
            result.top = std::clamp(result.top, 0L, static_cast<LONG>(state.height));
            result.right = std::clamp(
                result.right, result.left + 1, static_cast<LONG>(state.width));
            result.bottom = std::clamp(
                result.bottom, result.top + 1, static_cast<LONG>(state.height));
            return result;
        };
        if (layout.previousOverflow) {
            state.items.push_back({
                relative(layout.previousOverflow->bounds),
                L"overflow:previous:" +
                    std::to_wstring(layout.previousOverflow->targetSlot),
                false, false});
        }
        for (const auto& tile : layout.tiles) {
            if (tile.slot >= state_.order().size()) return std::nullopt;
            const bool selected = tile.slot == state_.selectedSlot();
            state.items.push_back({
                relative(tile.bounds),
                std::wstring{state_.order()[tile.slot]} + L":" +
                    std::to_wstring(static_cast<int>(
                        DisplayWidgetIcon(state_.order()[tile.slot]))),
                selected,
                // The retained DirectComposition tray is selection chrome.
                // Entering, moving within, or leaving widget content changes
                // UIA/input focus but must never clear or redraw this child.
                // A later tray-owned selection/order/style update refreshes
                // the complete raster and its in-bounds selection indicator.
                false,
            });
        }
        if (layout.nextOverflow) {
            state.items.push_back({
                relative(layout.nextOverflow->bounds),
                L"overflow:next:" +
                    std::to_wstring(layout.nextOverflow->targetSlot),
                false, false});
        }
        return state;
    }

    bool RenderCompositionLayer(
        const unsigned int fullWidth,
        const unsigned int fullHeight,
        const UINT dpi,
        const gba::OverlayCompositionSurface::Layer layer,
        const CompositionPaintLayer paintLayer,
        const CompositionLayerGeometry& geometry,
        const RECT* update,
        CompositionFrameSet& set,
        const gba::shell::TrayLayout* trayLayout = nullptr,
        const gba::declarative::Rect* guideBounds = nullptr) {
        gba::OverlayCompositionSurface::Frame frame;
        HRESULT result = compositionSurface_.BeginFrame(
            layer, geometry.width, geometry.height,
            0.0F, 0.0F, update, frame);
        if (FAILED(result)) {
            AppendDiagnostic(
                L"DirectComposition child BeginDraw failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(result)));
            return false;
        }

        renderTarget_ = frame.target;
        renderTarget_->SetDpi(
            static_cast<float>(dpi != 0 ? dpi : 96U),
            static_cast<float>(dpi != 0 ? dpi : 96U));
        if (!EnsureGraphicsResources(fullWidth, fullHeight, dpi)) {
            renderTarget_.Reset();
            compositionSurface_.AbandonFrame(frame);
            AppendDiagnostic(
                L"DirectComposition child resources could not be created");
            return false;
        }
        DrawCurrentFrame(
            fullWidth, fullHeight, dpi, frame.updateOffset,
            paintLayer, trayLayout, guideBounds);
        renderTarget_.Reset();
        result = compositionSurface_.EndFrame(frame);
        if (FAILED(result)) {
            AppendDiagnostic(
                L"DirectComposition child EndDraw failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(result)));
            return false;
        }
        set.frames.push_back(std::move(frame));
        return true;
    }

    bool RenderCompositionFrames(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi,
        const float destinationOffsetX,
        const float destinationOffsetY,
        const gba::OverlayPlacement& containerPlacement,
        CompositionFrameSet& set,
        std::uint64_t& drawMicroseconds) {
        static_cast<void>(destinationOffsetX);
        static_cast<void>(destinationOffsetY);
        static_cast<void>(containerPlacement);
        const auto started = std::chrono::steady_clock::now();
        set = {};
        if (!compositionChromeSession_) return false;
        const auto& chromeSession = *compositionChromeSession_;
        const auto trayLayout = CurrentCompositionTrayLayout();
        if (!trayLayout) return false;

        const bool replaceContent = !compositionSurface_.hasContent(
            gba::OverlayCompositionSurface::Layer::Content) ||
            compositionSurface_.width() != width ||
            compositionSurface_.height() != height;
        if (replaceContent) DiscardGraphicsResources();
        const CompositionLayerGeometry contentGeometry{
            width, height,
        };
        if (!RenderCompositionLayer(
                width, height, dpi,
                gba::OverlayCompositionSurface::Layer::Content,
                CompositionPaintLayer::Content, contentGeometry, nullptr, set,
                &*trayLayout)) {
            return false;
        }

        const auto guideKey = CurrentGuidePaintKey(
            chromeSession.guideWidth, chromeSession.guideHeight, chromeSession.dpi);
        const bool guideDirty =
            !compositionSurface_.hasContent(
                gba::OverlayCompositionSurface::Layer::Guide) ||
            guideKey != retainedGuidePaintKey_;
        const CompositionLayerGeometry guideGeometry{
            chromeSession.guideWidth, chromeSession.guideHeight,
        };
        if (guideDirty && !RenderCompositionLayer(
                chromeSession.guideWidth, chromeSession.guideHeight,
                chromeSession.dpi,
                gba::OverlayCompositionSurface::Layer::Guide,
                CompositionPaintLayer::Guide, guideGeometry, nullptr, set,
                &*trayLayout, &chromeSession.guideBounds)) {
            return false;
        }
        set.guideKey = guideKey;

        auto nextTrayState = CurrentTrayPaintState(
            *trayLayout, chromeSession.trayWidth, chromeSession.trayHeight,
            chromeSession.pixelsPerDip);
        if (!nextTrayState) return false;
        const bool trayDirty = gba::shell::RequiresTrayRepaint(
            retainedTrayPaintState_ ? &*retainedTrayPaintState_ : nullptr,
            *nextTrayState);
        const bool repaintTray = trayDirty || !compositionSurface_.hasContent(
            gba::OverlayCompositionSurface::Layer::Tray);
        const CompositionLayerGeometry trayGeometry{
            chromeSession.trayWidth, chromeSession.trayHeight,
        };
        if (repaintTray) {
            if (!RenderCompositionLayer(
                    chromeSession.trayWidth, chromeSession.trayHeight,
                    chromeSession.dpi,
                    gba::OverlayCompositionSurface::Layer::Tray,
                    CompositionPaintLayer::Tray, trayGeometry, nullptr, set,
                    &*trayLayout)) {
                return false;
            }
        }
        set.trayState = std::move(nextTrayState);
        drawMicroseconds = static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now() - started).count());
        return true;
    }

    void DisableCompositionFallback(const std::wstring_view reason) {
        AppendDiagnostic(
            L"DirectComposition presentation disabled; using HWND fallback: " +
            std::wstring(reason));
        DiscardGraphicsResources();
        gba::shell::ResetFixedChromeComposition(
            compositionSurface_, chromeWindow_);
        chromeAccessibilityProvider_.Clear();
        retainedGuidePaintKey_.clear();
        retainedTrayPaintState_.reset();
        fixedChromeAnchor_.reset();
        compositionChromeSession_.reset();
        pendingFixedChromePlacementReason_ =
            FixedChromePlacementReason::NewVisibleSession;
        presentationTransaction_.RejectCompositionAdmission();
        EnableLegacyLayeredFallback();
        appliedOverlayOpacity_.reset();
        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        if (window_ && state_.surface() != gba::Surface::Hidden)
            InvalidateRect(window_, nullptr, FALSE);
    }

    void EnableLegacyLayeredFallback() {
        if (!window_) return;
        const auto current = static_cast<DWORD>(GetWindowLongPtrW(window_, GWL_EXSTYLE));
        const auto fallback =
            (current & ~WS_EX_NOREDIRECTIONBITMAP) | WS_EX_LAYERED;
        if (fallback != current) {
            SetWindowLongPtrW(window_, GWL_EXSTYLE, static_cast<LONG_PTR>(fallback));
            (void)SetWindowPos(
                window_, nullptr, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE |
                    SWP_FRAMECHANGED | SWP_NOREDRAW);
        }
        (void)SetLayeredWindowAttributes(
            window_, RGB(1, 2, 3), targetOverlayOpacity_,
            LWA_ALPHA | LWA_COLORKEY);
    }

    bool CommitCompositionRepaint(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi) {
        const std::wstring priorPresentationPaintKey =
            lastWidgetPresentationPaintKey_;
        const bool replacement =
            compositionSurface_.width() != width ||
            compositionSurface_.height() != height;
        RECT client{};
        GetClientRect(window_, &client);
        const auto settledMotion = gba::PlanCompositionMotion(
            static_cast<unsigned int>(client.right - client.left),
            static_cast<unsigned int>(client.bottom - client.top),
            width, height, static_cast<float>(width), static_cast<float>(height),
            gba::CompositionVerticalAnchor::Bottom);
        const auto settledSpaces = gba::PlanCompositionChildCoordinates(
            settledMotion, width, height);
        const float destinationOffsetX = settledSpaces.chromeOffsetX;
        const float destinationOffsetY = settledSpaces.chromeOffsetY;
        CompositionFrameSet frames;
        std::uint64_t drawMicroseconds{};
        RECT windowBounds{};
        if (!GetWindowRect(window_, &windowBounds)) return false;
        const gba::OverlayPlacement containerPlacement{
            windowBounds.left, windowBounds.top,
            client.right - client.left, client.bottom - client.top};
        if (!RenderCompositionFrames(
                width, height, dpi, destinationOffsetX, destinationOffsetY,
                containerPlacement,
                frames, drawMicroseconds)) {
            DisableCompositionFallback(L"destination draw failed");
            return false;
        }
        std::vector<gba::OverlayCompositionSurface::Frame*> framePointers;
        framePointers.reserve(frames.frames.size());
        for (auto& frame : frames.frames) framePointers.push_back(&frame);
        gba::OverlayCompositionSurface::CommitTiming timing;
        const HRESULT result = compositionSurface_.CommitFrames(
            framePointers, replacement, timing, nullptr);
        if (FAILED(result)) {
            DisableCompositionFallback(
                L"surface commit failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(result)));
            return false;
        }
        retainedGuidePaintKey_ = std::move(frames.guideKey);
        retainedTrayPaintState_ = std::move(frames.trayState);
        presentationTransaction_.AcceptCompositionRepaint(
            state_.surface() == gba::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget());
        AppendCompositionCoordinateSample(0);
        const bool presentationChanged =
            priorPresentationPaintKey != lastWidgetPresentationPaintKey_;
        if (replacement || presentationChanged || performanceCountersActive_) {
            AppendDiagnostic(
                L"Composition frame committed content=complete size=" +
                std::to_wstring(width) + L"x" + std::to_wstring(height) +
                L" order=commit-no-geometry" +
                L" draw-us=" + std::to_wstring(drawMicroseconds) +
                L" commit-us=" + std::to_wstring(timing.commitMicroseconds) +
                L" geometry-us=0" +
                L" waited=" + (timing.waitedForCompletion ? L"true" : L"false") +
                L" geometry=unchanged");
        }
        if (performanceCountersActive_) ++performanceSuccessfulFrames_;
        BeginOpenAfterSuccessfulPaint();
        return true;
    }

    void Paint() {
        PAINTSTRUCT paint{};
        BeginPaint(window_, &paint);
        if (state_.surface() == gba::Surface::Hidden) {
            declarativeMotionActive_ = false;
            EndPaint(window_, &paint);
            return;
        }

        RECT client{};
        GetClientRect(window_, &client);
        const auto& contentPlacement = presentationTransaction_.contentPlacement();
        const auto width = contentPlacement
            ? static_cast<unsigned int>(contentPlacement->width)
            : static_cast<unsigned int>(client.right - client.left);
        const auto height = contentPlacement
            ? static_cast<unsigned int>(contentPlacement->height)
            : static_cast<unsigned int>(client.bottom - client.top);
        const UINT windowDpi = GetDpiForWindow(window_);
        if (compositionSurface_.available()) {
            (void)CommitCompositionRepaint(width, height, windowDpi);
            EndPaint(window_, &paint);
            return;
        }
        if (!EnsureGraphicsResources(width, height, windowDpi)) {
            declarativeMotionActive_ = false;
            EndPaint(window_, &paint);
            return;
        }

        float dpiX = 96.0F;
        float dpiY = 96.0F;
        renderTarget_->GetDpi(&dpiX, &dpiY);
        renderTarget_->BeginDraw();
        // The HWND is a color-keyed layered window above a separately dimmed
        // full-screen backdrop. Only the authored panel/tray surfaces belong
        // to this window; painting the theme canvas across the client creates
        // an opaque rectangular box around those content-shaped surfaces.
        // Keep the canvas color for style inheritance/contrast, but clear the
        // unused client area to the exact transparency key.
        DrawCurrentFrame(
            width, height,
            dpiX > 0 ? static_cast<UINT>(std::lround(dpiX)) : 96U,
            POINT{});

        const HRESULT result = renderTarget_->EndDraw();
        if (result == D2DERR_RECREATE_TARGET) {
            DiscardGraphicsResources();
            ReprimeOpenAfterRenderTargetLoss();
        } else if (SUCCEEDED(result)) {
            if (performanceCountersActive_) ++performanceSuccessfulFrames_;
            BeginOpenAfterSuccessfulPaint();
        }
        EndPaint(window_, &paint);
    }

    void DrawIconStrip(
        const float width,
        const float height,
        const gba::OverlaySurfaceGeometry* surfaceGeometry = nullptr,
        const gba::accessibility::DashboardSemantics* dashboard = nullptr,
        const gba::shell::TrayLayout* frameLayout = nullptr,
        const bool publishAccessibility = true) {
        const auto computedLayout = frameLayout
            ? std::optional<gba::shell::TrayLayout>{}
            : gba::shell::ComputeTrayLayout(
                width, height, state_.order().size(), state_.selectedSlot(),
                surfaceGeometry
                    ? std::optional<gba::shell::TrayBand>{gba::shell::TrayBand{
                        surfaceGeometry->trayY,
                        surfaceGeometry->trayY + surfaceGeometry->trayHeight,
                    }}
                    : std::nullopt);
        const auto* layout = frameLayout
            ? frameLayout
            : computedLayout ? &*computedLayout : nullptr;
        if (!layout) return;
        const auto& stripBounds = layout->stripBounds;
        const D2D1_ROUNDED_RECT strip{
            D2D1::RectF(
                stripBounds.x, stripBounds.y,
                stripBounds.x + stripBounds.width,
                stripBounds.y + stripBounds.height),
            trayCornerRadius_, trayCornerRadius_};
        gba::shell::FillColorKeyRoundedRectangle(
            renderTarget_.Get(), strip, backgroundBrush_.Get(),
            compositionSurface_.available()
                ? gba::shell::OuterChromeBoundary::PremultipliedAlpha
                : gba::shell::OuterChromeBoundary::ColorKeyAliased);

        const auto drawOverflow = [&](const gba::shell::TrayOverflowLayout& overflow) {
            const auto& bounds = overflow.bounds;
            const D2D1_ROUNDED_RECT control{
                D2D1::RectF(
                    bounds.x, bounds.y,
                    bounds.x + bounds.width, bounds.y + bounds.height),
                std::min(trayItemCornerRadius_, bounds.width * 0.35F),
                std::min(trayItemCornerRadius_, bounds.height * 0.35F)};
            renderTarget_->FillRoundedRectangle(control, trayItemBrush_.Get());
            const float inset = std::max(5.0F, bounds.width * 0.22F);
            (void)gba::icons::DrawNativeIcon(
                renderTarget_.Get(),
                overflow.direction == gba::shell::TrayOverflowDirection::Previous
                    ? gba::icons::NativeIcon::Previous
                    : gba::icons::NativeIcon::Next,
                D2D1::RectF(
                    bounds.x + inset, bounds.y + inset,
                    bounds.x + bounds.width - inset,
                    bounds.y + bounds.height - inset),
                trayItemTextBrush_.Get(), 1.8F);
        };
        if (layout->previousOverflow) drawOverflow(*layout->previousOverflow);

        for (const auto& tileLayout : layout->tiles) {
            const std::size_t slot = tileLayout.slot;
            const float x = tileLayout.bounds.x;
            const float top = tileLayout.bounds.y;
            const float tileSize = tileLayout.bounds.width;
            const D2D1_ROUNDED_RECT tile{
                D2D1::RectF(x, top, x + tileSize, top + tileSize),
                trayItemCornerRadius_, trayItemCornerRadius_};
            renderTarget_->FillRoundedRectangle(
                tile, slot == state_.selectedSlot() ? accentBrush_.Get()
                                                    : trayItemBrush_.Get());
            if (slot == state_.selectedSlot()) {
                const float indicatorInset = std::min(18.0F, tileSize * 0.28F);
                const float indicatorHeight = std::min(4.0F, tileSize * 0.12F);
                const D2D1_ROUNDED_RECT indicator{
                    D2D1::RectF(x + indicatorInset, top + tileSize - indicatorHeight,
                                x + tileSize - indicatorInset, top + tileSize),
                    2.0F, 2.0F};
                renderTarget_->FillRoundedRectangle(indicator, selectedTextBrush_.Get());
                if (!compositionSurface_.available() &&
                    state_.focusRegion() == gba::FocusRegion::Tray) {
                    renderTarget_->DrawRoundedRectangle(
                        tile, focusBrush_.Get(), focusOutlineWidth_);
                }
            }

            const std::wstring_view widget = state_.order()[slot];
            const float iconInset = std::min(15.0F, tileSize * 0.24F);
            (void)gba::icons::DrawNativeIcon(
                renderTarget_.Get(), DisplayWidgetIcon(widget),
                D2D1::RectF(x + iconInset, top + iconInset,
                            x + tileSize - iconInset, top + tileSize - iconInset),
                slot == state_.selectedSlot()
                    ? selectedTextBrush_.Get()
                    : trayItemTextBrush_.Get(),
                2.35F);
        }
        if (layout->nextOverflow) drawOverflow(*layout->nextOverflow);
        if (publishAccessibility)
            PublishTrayAccessibility(*layout, width, height, dashboard);
    }

    static std::wstring_view DisplayButton(const std::wstring_view button) {
        if (button == L"leftBumper") return L"LB";
        if (button == L"rightBumper") return L"RB";
        if (button == L"leftTrigger") return L"LT";
        if (button == L"rightTrigger") return L"RT";
        if (button == L"leftStick") return L"LS";
        if (button == L"rightStick") return L"RS";
        if (button == L"a") return L"A";
        if (button == L"b") return L"B";
        if (button == L"x") return L"X";
        if (button == L"y") return L"Y";
        if (button == L"menu") return L"Menu";
        if (button == L"view") return L"View";
        return button;
    }

    std::optional<std::wstring> DashboardStatus() const {
        const auto now = GetTickCount64();
        if (const auto* startup = sessions_.Failure(state_.selectedWidget()))
            return startup->safeMessage;
        if (const auto failure = actionFailureFeedback_.MessageForSurface(
                gba::WidgetActionFeedbackSurface::Dashboard,
                state_.selectedWidget(),
                state_.activeWidget())) return std::wstring{*failure};
        if (lastActionExpiresAt_ > now && !lastActionMessage_.empty() &&
            lastActionWidgetId_ == state_.selectedWidget()) {
            return lastActionMessage_;
        }
        return std::nullopt;
    }

    std::wstring DashboardHint(const float availableWidth) const {
        if (trayYGesture_.pendingRestart()) {
            return L"Hold Y to restart " +
                std::wstring(DisplayWidgetName(trayYGesture_.selectedWidget())) +
                L" — " +
                std::to_wstring(trayYGesture_.progressPercent(GetTickCount64())) +
                L"%";
        }
        std::vector<gba::ControllerGuideAction> quickActions;
        const auto* snapshot = InteractionSnapshotFor(state_.selectedWidget());
        if (IsBridgeWidget(state_.selectedWidget()) && snapshot) {
            quickActions.reserve(snapshot->quickActions.size());
            for (const auto& action : snapshot->quickActions) {
                // A/Y/B remain shell navigation while focus is on the tray.
                if (action.button == L"a" || action.button == L"y" ||
                    action.button == L"b") continue;
                quickActions.push_back({DisplayButton(action.button), action.label});
            }
        }
        return gba::BuildTrayControllerGuide(
            gba::ResolveControllerGuideDensity(
                availableWidth, CurrentTextScale()),
            state_.reorderMode(), TrayYRestartEligible(), quickActions);
    }

    std::wstring DashboardAccessibilityHint(const std::wstring_view visualHint) const {
        if (trayYGesture_.pendingRestart()) return std::wstring{visualHint};
        if (state_.reorderMode() || !TrayYRestartEligible())
            return std::wstring{visualHint};
        return std::wstring{visualHint} +
            L". Tap Y to reorder. Hold Y to restart the selected widget.";
    }

    void DrawDashboard(
        const float width, const float height,
        const CompositionPaintLayer layer = CompositionPaintLayer::Combined,
        const gba::shell::TrayLayout* frameTrayLayout = nullptr) {
        if (!accessibilityActive_) {
            if (!accessibilityTree_.widgetId.empty()) ClearAccessibilityTree();
        }
        if (width <= 0.0F || height <= 0.0F) return;
        const float horizontalInset = std::min(34.0F, width * 0.1F);
        const float contentLeft = horizontalInset;
        const float contentRight = std::max(contentLeft, width - horizontalInset);
        const float titleTop = std::min(10.0F, height);
        const float titleBottom = std::max(titleTop, std::min(44.0F, height));
        const float hintTop = titleBottom;
        const float hintBottom = std::max(hintTop, std::min(66.0F, height));
        const std::wstring_view title = state_.reorderMode()
            ? L"Reorder widgets"
            : DisplayWidgetName(state_.selectedWidget());
        const auto status = DashboardStatus();
        const std::wstring help = DashboardHint(contentRight - contentLeft);
        const std::wstring accessibleHelp = DashboardAccessibilityHint(help);
        const std::wstring displayedHint = status ? *status : help;
        const gba::declarative::Rect hintBounds{
            contentLeft, hintTop, contentRight - contentLeft, hintBottom - hintTop,
        };
        const gba::accessibility::DashboardSemantics dashboard{
            std::wstring{title},
            {contentLeft, titleTop, contentRight - contentLeft, titleBottom - titleTop},
            status ? std::wstring{} : accessibleHelp,
            hintBounds,
            status ? *status : std::wstring{},
            hintBounds,
        };
        if (layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Content) {
            DrawTextLine(title, titleFormat_.Get(),
                         D2D1::RectF(contentLeft, titleTop, contentRight, titleBottom),
                         dashboardTextBrush_.Get());
            DrawTextLine(displayedHint, hintFormat_.Get(),
                         D2D1::RectF(contentLeft, hintTop, contentRight, hintBottom),
                         dashboardSecondaryBrush_.Get());
        }
        if (layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Tray) {
            DrawIconStrip(width, height, nullptr, &dashboard, frameTrayLayout);
        } else if (accessibilityActive_) {
            const auto layout = frameTrayLayout
                ? std::optional<gba::shell::TrayLayout>{*frameTrayLayout}
                : gba::shell::ComputeTrayLayout(
                    width, height, state_.order().size(), state_.selectedSlot());
            if (layout) PublishTrayAccessibility(*layout, width, height, &dashboard);
        }
    }

    static void CollectShortcutPrompts(
        const gba::WidgetNode& node,
        std::vector<std::pair<std::wstring, std::wstring>>& prompts) {
        for (const auto& shortcut : node.shortcuts) {
            const std::wstring_view label = !node.text.empty()
                ? std::wstring_view(node.text)
                : std::wstring_view(node.accessibilityLabel);
            if (shortcut.phase != L"pressed" || label.empty()) continue;
            const bool exists = std::any_of(
                prompts.begin(), prompts.end(), [&](const auto& prompt) {
                    return prompt.first == shortcut.button;
                });
            if (!exists) prompts.emplace_back(shortcut.button, label);
        }
        for (const auto& child : node.children) CollectShortcutPrompts(child, prompts);
    }

    std::optional<std::wstring> OpenWidgetStatus() const {
        const auto now = GetTickCount64();
        if (const auto* startup = sessions_.Failure(state_.activeWidget()))
            return startup->safeMessage;
        if (const auto failure = actionFailureFeedback_.MessageForSurface(
                gba::WidgetActionFeedbackSurface::OpenWidget,
                state_.selectedWidget(),
                state_.activeWidget())) return std::wstring{*failure};
        if (lastActionExpiresAt_ > now && !lastActionMessage_.empty() &&
            lastActionWidgetId_ == state_.activeWidget()) {
            return lastActionMessage_;
        }
        return std::nullopt;
    }

    std::wstring OpenWidgetPrompt() const {
        if (sessions_.Failure(state_.activeWidget()))
            return L"A  Retry    B  Back";
        const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
        if (!snapshot) return L"A  Select";
        std::vector<std::pair<std::wstring, std::wstring>> prompts;
        CollectShortcutPrompts(snapshot->root, prompts);
        const auto order = [](const std::wstring_view button) {
            if (button == L"b") return 0;
            if (button == L"x") return 1;
            if (button == L"leftBumper") return 2;
            if (button == L"rightBumper") return 3;
            if (button == L"y") return 4;
            return 10;
        };
        std::stable_sort(prompts.begin(), prompts.end(), [&](const auto& left, const auto& right) {
            return order(left.first) < order(right.first);
        });
        std::wstring result;
        for (std::size_t index = 0; index < prompts.size() && index < 4; ++index) {
            if (!result.empty()) result += L"     ";
            result += DisplayButton(prompts[index].first);
            result += L"  ";
            result += prompts[index].second;
        }
        return result.empty() ? L"A  Select" : result;
    }

    void DrawWidgetFooter(
        const gba::OverlaySurfaceGeometry& geometry,
        const gba::declarative::Rect* fixedGuideBounds = nullptr) {
        openWidgetAccessibility_ = {};
        if (geometry.footerHeight <= 0.0F || geometry.panelWidth <= 0.0F) return;
        const float panelLeft = fixedGuideBounds
            ? fixedGuideBounds->x : geometry.panelX;
        const float panelRight = fixedGuideBounds
            ? fixedGuideBounds->x + fixedGuideBounds->width
            : geometry.panelX + geometry.panelWidth;
        const float panelBottom = fixedGuideBounds
            ? fixedGuideBounds->y + fixedGuideBounds->height
            : geometry.panelY + geometry.panelHeight;
        const float guideWidth = panelRight - panelLeft;
        const float horizontalInset = std::min(30.0F, guideWidth * 0.1F);
        const float contentLeft = panelLeft + horizontalInset;
        const float contentRight = std::max(contentLeft, panelRight - horizontalInset);
        const float guideTop = fixedGuideBounds
            ? fixedGuideBounds->y : geometry.footerY;
        const float guideHeight = fixedGuideBounds
            ? fixedGuideBounds->height : geometry.footerHeight;
        const float textTop = guideTop +
            std::min(15.0F, guideHeight * 0.35F);
        const float textBottom = panelBottom -
            std::min(12.0F, guideHeight * 0.25F);
        if (textBottom <= textTop + 1.0F) return;
        const gba::declarative::Rect footerBounds{
            contentLeft, textTop, contentRight - contentLeft, textBottom - textTop,
        };
        openWidgetAccessibility_.title =
            std::wstring{DisplayWidgetName(state_.activeWidget())};
        if (state_.focusRegion() == gba::FocusRegion::Tray) {
            const auto status = DashboardStatus();
            const std::wstring help = DashboardHint(contentRight - contentLeft);
            const std::wstring accessibleHelp = DashboardAccessibilityHint(help);
            const std::wstring footer = status ? *status : help;
            openWidgetAccessibility_.closeBounds = footerBounds;
            if (status) {
                openWidgetAccessibility_.status = *status;
                openWidgetAccessibility_.statusBounds = footerBounds;
            } else {
                openWidgetAccessibility_.help = accessibleHelp;
                openWidgetAccessibility_.helpBounds = footerBounds;
            }
            DrawTextLine(footer, hintFormat_.Get(),
                         D2D1::RectF(contentLeft, textTop, contentRight, textBottom),
                         dashboardSecondaryBrush_.Get());
            return;
        }
        const auto status = OpenWidgetStatus();
        const std::wstring help = OpenWidgetPrompt();
        const std::wstring prompt = status ? *status : help;
        const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
        const bool rootScope = snapshot &&
            std::wstring_view(snapshot->activeInputScopeId) ==
                gba::input::RootInputScope(*snapshot);
        const bool nestedBack = snapshot && !rootScope &&
            gba::accessibility::HasActiveScopeBackShortcut(
                *snapshot, focusedElementId_);
        const bool hasBack = rootScope || nestedBack;
        const std::wstring hostPrompt = hasBack
            ? L"B  Back     Guide  Close"
            : L"Guide  Close";
        if (hasBack) {
            openWidgetAccessibility_.backAction = rootScope
                ? gba::accessibility::HostAction::BackToTray
                : gba::accessibility::HostAction::BackWithinWidget;
            openWidgetAccessibility_.backTargetId = snapshot->activeInputScopeId;
        }
        if (contentRight - contentLeft >= 300.0F) {
            const float hostPromptWidth = hasBack ? 180.0F : 106.0F;
            const float hostPromptLeft = contentRight - hostPromptWidth;
            const gba::declarative::Rect promptBounds{
                contentLeft, textTop,
                std::max(0.0F, hostPromptLeft - 14.0F - contentLeft),
                textBottom - textTop,
            };
            if (status) {
                openWidgetAccessibility_.status = *status;
                openWidgetAccessibility_.statusBounds = promptBounds;
            } else {
                openWidgetAccessibility_.help = help;
                openWidgetAccessibility_.helpBounds = promptBounds;
            }
            if (hasBack) {
                openWidgetAccessibility_.backBounds = {
                    hostPromptLeft, textTop, 74.0F, textBottom - textTop,
                };
                openWidgetAccessibility_.closeBounds = {
                    hostPromptLeft + 74.0F, textTop,
                    contentRight - hostPromptLeft - 74.0F, textBottom - textTop,
                };
            } else {
                openWidgetAccessibility_.closeBounds = {
                    hostPromptLeft, textTop, hostPromptWidth, textBottom - textTop,
                };
            }
            DrawTextLine(prompt, hintFormat_.Get(),
                         D2D1::RectF(contentLeft, textTop,
                                     contentRight - hostPromptWidth - 14.0F, textBottom),
                         secondaryBrush_.Get());
            DrawTextLine(hostPrompt, hintFormat_.Get(),
                         D2D1::RectF(contentRight - hostPromptWidth, textTop,
                                     contentRight, textBottom),
                         secondaryBrush_.Get());
        } else {
            // At narrow logical widths retain the hierarchy/escape affordance;
            // widget action labels remain discoverable on larger surfaces.
            const float halfWidth = footerBounds.width * 0.5F;
            if (hasBack) {
                openWidgetAccessibility_.backBounds = {
                    footerBounds.x, footerBounds.y, halfWidth, footerBounds.height,
                };
                openWidgetAccessibility_.closeBounds = {
                    footerBounds.x + halfWidth, footerBounds.y,
                    footerBounds.width - halfWidth, footerBounds.height,
                };
            } else {
                openWidgetAccessibility_.closeBounds = footerBounds;
            }
            DrawTextLine(hostPrompt, hintFormat_.Get(),
                         D2D1::RectF(contentLeft, textTop, contentRight, textBottom),
                         secondaryBrush_.Get());
        }
    }

    void DrawWidget(
        const float width,
        const float height,
        const float physicalPixelsPerDip,
        const CompositionPaintLayer layer = CompositionPaintLayer::Combined,
        const gba::shell::TrayLayout* frameTrayLayout = nullptr,
        const gba::declarative::Rect* frameGuideBounds = nullptr) {
        declarativeMotionActive_ = false;
        const std::wstring_view widget = state_.activeWidget();
        const bool bridgeWidget = IsBridgeWidget(widget);
        const auto geometry = layer == CompositionPaintLayer::Content &&
                compositionSurface_.available()
            ? ComputePanelLocalSurfaceGeometry(width, height)
            : [&]() {
                const auto widgetSurface = DesiredWidgetSurfaceTarget();
                return gba::ComputeOverlaySurfaceGeometry(
                    width, height,
                    bridgeWidget ? widgetSurface.panelWidthDip : 720.0F,
                    bridgeWidget
                        ? std::optional<float>{widgetSurface.panelHeightDip}
                        : std::nullopt);
            }();
        if (!geometry) return;
        const auto sessionTrayLayout = !frameTrayLayout && compositionSurface_.available()
            ? CurrentCompositionTrayLayout()
            : std::optional<gba::shell::TrayLayout>{};
        const auto computedTrayLayout = frameTrayLayout || sessionTrayLayout
            ? std::optional<gba::shell::TrayLayout>{}
            : gba::shell::ComputeTrayLayout(
                width, height, state_.order().size(), state_.selectedSlot(),
                gba::shell::TrayBand{
                    geometry->trayY, geometry->trayY + geometry->trayHeight});
        const auto* trayLayout = frameTrayLayout
            ? frameTrayLayout
            : sessionTrayLayout ? &*sessionTrayLayout
            : computedTrayLayout ? &*computedTrayLayout : nullptr;
        const float panelLeft = geometry->panelX;
        const float panelTop = geometry->panelY;
        const float panelWidth = geometry->panelWidth;
        const bool drawContent = layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Content;
        const bool drawGuide = layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Guide;
        const bool drawTray = layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Tray;
        const std::optional<gba::declarative::Rect> fixedGuideBounds = frameGuideBounds
            ? std::optional<gba::declarative::Rect>{*frameGuideBounds}
            : trayLayout
            ? std::optional<gba::declarative::Rect>{gba::declarative::Rect{
                trayLayout->stripBounds.x,
                geometry->footerY,
                trayLayout->stripBounds.width,
                geometry->footerHeight,
            }}
            : std::nullopt;

        if (!drawContent) {
            if (drawGuide) DrawWidgetFooter(
                *geometry, fixedGuideBounds ? &*fixedGuideBounds : nullptr);
            if (drawTray) {
                DrawIconStrip(width, height, &*geometry, nullptr,
                              trayLayout);
            } else if (accessibilityActive_ && trayLayout) {
                PublishTrayAccessibility(*trayLayout, width, height);
            }
            return;
        }
        // The footer is host chrome, not widget content. End the authored card
        // at the content boundary; the immediately following fixed-height
        // guide then connects that visible panel edge to the stationary tray
        // without masquerading as third-party widget content.
        const float visualPanelBottom = geometry->footerY;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(panelLeft, panelTop, panelLeft + panelWidth, visualPanelBottom),
            panelCornerRadius_, panelCornerRadius_};
        gba::shell::FillColorKeyRoundedRectangle(
            renderTarget_.Get(), panel, cardBrush_.Get(),
            compositionSurface_.available()
                ? gba::shell::OuterChromeBoundary::PremultipliedAlpha
                : gba::shell::OuterChromeBoundary::ColorKeyAliased);

        if (bridgeWidget) {
            ComPtr<ID2D1Layer> contentLayer;
            bool contentLayerPushed = false;
            if (overlayTransitionSample_.contentOpacity < 0.999F &&
                SUCCEEDED(renderTarget_->CreateLayer(
                    nullptr, contentLayer.ReleaseAndGetAddressOf()))) {
                auto layerParameters = D2D1::LayerParameters();
                layerParameters.contentBounds = D2D1::RectF(
                    geometry->widgetViewportX,
                    geometry->widgetViewportY,
                    geometry->widgetViewportX + geometry->widgetViewportWidth,
                    geometry->widgetViewportY + geometry->widgetViewportHeight);
                layerParameters.opacity = std::clamp(
                    overlayTransitionSample_.contentOpacity, 0.0F, 1.0F);
                renderTarget_->PushLayer(layerParameters, contentLayer.Get());
                contentLayerPushed = true;
            }
            const auto sessionPresentation = sessions_.Presentation(widget);
            const auto& retainedPresentation =
                presentationTransaction_.retainedPresentation();
            const auto contentAuthority = gba::ResolveWidgetContentAuthority(
                sessionPresentation.snapshot != nullptr,
                sessionPresentation.authority ==
                    gba::WidgetPresentationAuthority::FailureRetained,
                retainedPresentation.has_value());
            const bool failureRetainedSnapshot =
                contentAuthority == gba::WidgetContentAuthority::FailureRetainedSnapshot;
            const bool transitionRetainedSnapshot =
                contentAuthority == gba::WidgetContentAuthority::RetainedCommittedSnapshot;
            const bool inertRetainedSnapshot =
                failureRetainedSnapshot || transitionRetainedSnapshot;
            const auto* snapshot = failureRetainedSnapshot
                ? sessionPresentation.snapshot
                : transitionRetainedSnapshot
                    ? &retainedPresentation->snapshot
                    : sessionPresentation.snapshot;
            const std::wstring_view renderedWidget = transitionRetainedSnapshot
                ? std::wstring_view{retainedPresentation->widgetId}
                : widget;
            const std::wstring_view renderedFocusId = transitionRetainedSnapshot
                ? std::wstring_view{retainedPresentation->focusId}
                : failureRetainedSnapshot
                    ? std::wstring_view{}
                    : state_.focusRegion() == gba::FocusRegion::Widget
                        ? std::wstring_view{focusedElementId_}
                        : std::wstring_view{};
            if (snapshot && declarativeRenderer_) {
                const gba::declarative::Rect viewport{
                    geometry->widgetViewportX,
                    geometry->widgetViewportY,
                    geometry->widgetViewportWidth,
                    geometry->widgetViewportHeight,
                };
                const auto* descriptor = sessions_.FindDescriptor(renderedWidget);
                const auto accessibilityPolicy = appearanceState_.current()
                    ? CurrentAccessibilityPolicy()
                    : gba::NativeAccessibilityPolicy{};
                const auto presentationTime = GetTickCount64();
                std::map<std::wstring, double, std::less<>> presentedSliderValues;
                const auto collectSliderOverrides = [&](const auto& self,
                                                        const gba::WidgetNode& node) -> void {
                    if (node.kind == L"slider") {
                        if (const auto value = sliderInteraction_.PresentationValue(
                                SliderDescriptor(*snapshot, node), presentationTime)) {
                            presentedSliderValues.emplace(node.id, *value);
                        }
                    }
                    for (const auto& child : node.children) self(self, child);
                };
                collectSliderOverrides(collectSliderOverrides, snapshot->root);
                const gba::accessibility::ProjectionKey projectionKey{
                    std::wstring{renderedWidget},
                    descriptor
                        ? descriptor->runtimeGeneration
                        : std::wstring{},
                    snapshot->activeInputScopeId,
                    std::wstring{renderedFocusId},
                    snapshot->sequence,
                    sliderInteraction_.presentationRevision(),
                    appearanceState_.current() ? appearanceState_.current()->revision : 0,
                    viewport.x,
                    viewport.y,
                    viewport.width,
                    viewport.height,
                    geometry->panelWidth,
                    geometry->panelHeight,
                    physicalPixelsPerDip,
                    accessibilityPolicy.textScale,
                    accessibilityPolicy.minimumFontWeight,
                    accessibilityPolicy.reducedMotion,
                    accessibilityPolicy.reducedTransparency,
                };
                const bool collectAccessibility = !inertRetainedSnapshot &&
                    accessibilityActive_ &&
                    descriptor &&
                    widgetAccessibilityProjection_.ShouldCollect(projectionKey);
                gba::DeclarativeRenderOptions options;
                options.pixelScale = physicalPixelsPerDip;
                options.collectAccessibility = collectAccessibility;
                options.responsiveViewport = gba::declarative::Size{
                    geometry->panelWidth,
                    geometry->panelHeight,
                };
                options.surfaceBackground = effectivePanelBackground_;
                const float panelContentInset = std::max(
                    0.0F, geometry->widgetViewportX - panelLeft);
                options.surfaceCornerRadiusPx = std::max(
                    0.0F, panelCornerRadius_ - panelContentInset);
                if (appearanceState_.current())
                    options.accessibility = accessibilityPolicy;
                options.animationTimestampMilliseconds = presentationTime;
                options.sliderValueOverrides = presentedSliderValues;
                options.artworkWidgetId = std::wstring{renderedWidget};
                if (!inertRetainedSnapshot) {
                    sliderInteraction_.RetainAdjustmentMode(
                        snapshot->instanceId,
                        snapshot->activeInputScopeId,
                        renderedFocusId);
                    options.pressedElementId = pressedInteraction_.ActiveElementId(
                        *snapshot, renderedFocusId);
                }
                if (!inertRetainedSnapshot && options.pressedElementId.empty() &&
                    state_.focusRegion() == gba::FocusRegion::Widget) {
                    if (const auto* focused = gba::input::FindNodeInInputScope(
                            *snapshot, focusedElementId_, snapshot->activeInputScopeId);
                        focused && focused->sliderInteractionMode == L"activateToAdjust" &&
                        sliderInteraction_.AdjustmentModeActive(
                            SliderDescriptor(*snapshot, *focused), GetTickCount64())) {
                        options.pressedElementId = focused->id;
                    }
                }
                auto launcherProjection = launcherExperienceProjection_.Render(
                    *declarativeRenderer_, renderTarget_.Get(), imageCache_.get(),
                    descriptor ? descriptor->advancedPresentation
                               : std::optional<gba::WidgetAdvancedPresentationDeclaration>{},
                    descriptor ? std::wstring_view{descriptor->presentationGeneration}
                               : std::wstring_view{},
                    renderedWidget,
                    *snapshot, renderedFocusId, viewport, options);
                auto result = std::move(launcherProjection.render);
                const auto& semanticSnapshot =
                    launcherExperienceProjection_.InteractionSnapshot(
                        renderedWidget,
                        descriptor ? std::wstring_view{descriptor->presentationGeneration}
                                   : std::wstring_view{},
                        *snapshot);
                if (result.succeeded) {
                    const auto projectedNavigationCount =
                        launcherProjection.presentationActive
                            ? result.navigationRects.size() : 0U;
                    std::wstring launcherRailPrevious;
                    std::wstring launcherRailNext;
                    if (launcherProjection.preset) {
                        if (const auto* focusedLauncherNode =
                                gba::input::FindNodeInInputScope(
                                    semanticSnapshot, renderedFocusId,
                                    semanticSnapshot.activeInputScopeId)) {
                            const bool verticalRail =
                                *launcherProjection.preset ==
                                    gba::launcher::Preset::CoverWall ||
                                *launcherProjection.preset ==
                                    gba::launcher::Preset::CompactGrid;
                            launcherRailPrevious = verticalRail
                                ? focusedLauncherNode->focusUp
                                : focusedLauncherNode->focusLeft;
                            launcherRailNext = verticalRail
                                ? focusedLauncherNode->focusDown
                                : focusedLauncherNode->focusRight;
                        }
                    }
                    const std::wstring inputOwner =
                        state_.focusRegion() == gba::FocusRegion::Tray
                            ? L"tray"
                            : L"widget";
                    const std::wstring semanticFocus =
                        state_.focusRegion() == gba::FocusRegion::Tray
                            ? L"tray:" + std::wstring(state_.selectedWidget())
                            : inertRetainedSnapshot || focusedElementId_.empty()
                                ? L"none"
                                : L"widget:" + focusedElementId_;
                    const bool selectedTrayItemVisible = trayLayout &&
                        std::any_of(
                            trayLayout->tiles.begin(), trayLayout->tiles.end(),
                            [&](const gba::shell::TrayTileLayout& tile) {
                                return tile.slot == state_.selectedSlot();
                            });
                    const gba::shell::TrayTileLayout* selectedTrayItem = nullptr;
                    if (trayLayout) {
                        const auto selected = std::find_if(
                            trayLayout->tiles.begin(), trayLayout->tiles.end(),
                            [&](const gba::shell::TrayTileLayout& tile) {
                                return tile.slot == state_.selectedSlot();
                            });
                        if (selected != trayLayout->tiles.end()) {
                            selectedTrayItem = &*selected;
                        }
                    }
                    const std::wstring trayState = trayLayout
                        ? L"\n" + std::to_wstring(trayLayout->totalCount) + L"\n" +
                            std::to_wstring(trayLayout->tiles.size()) + L"\n" +
                            (trayLayout->previousOverflow ? L"1" : L"0") + L"\n" +
                            (trayLayout->nextOverflow ? L"1" : L"0") + L"\n" +
                            (selectedTrayItemVisible ? L"1" : L"0") + L"\n" +
                            std::to_wstring(width) + L"x" +
                            std::to_wstring(height) + L"\n" +
                            std::to_wstring(trayLayout->stripBounds.x) + L"," +
                            std::to_wstring(trayLayout->stripBounds.y) + L"," +
                            std::to_wstring(trayLayout->stripBounds.width) + L"," +
                            std::to_wstring(trayLayout->stripBounds.height)
                        : L"\nmissing";
                    const std::wstring launcherPresentationKey =
                        launcherProjection.presentationActive
                            ? L"\nlauncher-presentation\n" +
                                std::wstring(gba::launcher::EffectQualityName(
                                    launcherProjection.effectQuality)) + L"\n" +
                                (launcherProjection.backgroundIsFallback
                                    ? L"fallback"
                                    : launcherProjection.backgroundFocusId) + L"\n" +
                                (launcherProjection.backgroundTransitionActive
                                    ? L"transitioning"
                                    : L"settled") + L"\n" +
                                launcherProjection.selectionIdentity + L"\n" +
                                (launcherProjection.selectionUsesGlobalAppearance
                                    ? L"global" : L"launcher") + L"\n" +
                                (launcherProjection.safeStart ? L"safe" : L"selected") + L"\n" +
                                launcherProjection.layoutBranch + L"\n" +
                                launcherProjection.railOrientation + L"\n" +
                                launcherProjection.detailsSurface + L"\n" +
                                launcherProjection.artworkAvailability + L"\n" +
                                std::to_wstring(launcherProjection.bodyBounds.width) + L"x" +
                                std::to_wstring(launcherProjection.bodyBounds.height) + L"\n" +
                                std::to_wstring(launcherProjection.railBounds.x) + L"," +
                                std::to_wstring(launcherProjection.railBounds.y) + L"," +
                                std::to_wstring(launcherProjection.railBounds.width) + L"," +
                                std::to_wstring(launcherProjection.railBounds.height) + L"\n" +
                                std::to_wstring(launcherProjection.textScale) + L"\n" +
                                (launcherProjection.reducedMotion ? L"motion-reduced" : L"motion-full") + L"\n" +
                                (launcherProjection.reducedTransparency
                                    ? L"transparency-reduced" : L"transparency-full") + L"\n" +
                                (launcherProjection.highContrast
                                    ? L"contrast-high" : L"contrast-standard")
                            : L"\nlauncher-presentation\ninactive";
                    const std::wstring paintKey =
                        std::wstring(widget) + L"\n" + std::wstring(renderedWidget) +
                        L"\n" + std::to_wstring(snapshot->sequence) + L"\n" +
                        (failureRetainedSnapshot
                            ? L"failure-retained"
                            : transitionRetainedSnapshot ? L"retained" : L"admitted") +
                        L"\n" + inputOwner + L"\n" + std::wstring(renderedFocusId) +
                        L"\n" + semanticFocus + trayState + launcherPresentationKey;
                    if (paintKey != lastWidgetPresentationPaintKey_) {
                        lastWidgetPresentationPaintKey_ = paintKey;
                        if (launcherProjection.disposition ==
                            gba::launcher::ProductionProjectionDisposition::Adopted) {
                            AppendDiagnostic(
                                L"Launcher Experience projection adopted widget=" +
                                std::wstring(renderedWidget) + L" preset=" +
                                std::wstring(gba::launcher::PresetName(
                                    *launcherProjection.preset)) +
                                L" sequence=" + std::to_wstring(snapshot->sequence) +
                                L" instance=" + snapshot->instanceId +
                                L" scope=" + snapshot->activeInputScopeId +
                                L" focus=" +
                                (renderedFocusId.empty()
                                    ? std::wstring{L"none"}
                                    : std::wstring{renderedFocusId}) +
                                L" projected-navigation=" + std::to_wstring(
                                    projectedNavigationCount) +
                                L" rail-previous=" +
                                (launcherRailPrevious.empty()
                                    ? std::wstring{L"none"}
                                    : launcherRailPrevious) +
                                L" rail-next=" +
                                (launcherRailNext.empty()
                                    ? std::wstring{L"none"}
                                    : launcherRailNext) +
                                L" selection=" +
                                launcherProjection.selectionIdentity +
                                L" appearance=" +
                                (launcherProjection.selectionUsesGlobalAppearance
                                    ? L"global" : L"launcher") +
                                L" safe-start=" +
                                (launcherProjection.safeStart ? L"true" : L"false") +
                                L" branch=" + launcherProjection.layoutBranch +
                                L" rail=" + launcherProjection.railOrientation +
                                L" details-surface=" + launcherProjection.detailsSurface +
                                L" artwork=" + launcherProjection.artworkAvailability +
                                L" body=" +
                                std::to_wstring(launcherProjection.bodyBounds.width) + L"x" +
                                std::to_wstring(launcherProjection.bodyBounds.height) +
                                L" rail-bounds=" +
                                std::to_wstring(launcherProjection.railBounds.x) + L"," +
                                std::to_wstring(launcherProjection.railBounds.y) + L"," +
                                std::to_wstring(launcherProjection.railBounds.width) + L"," +
                                std::to_wstring(launcherProjection.railBounds.height) +
                                L" text-scale=" +
                                std::to_wstring(launcherProjection.textScale) +
                                L" reduced-motion=" +
                                (launcherProjection.reducedMotion ? L"true" : L"false") +
                                L" reduced-transparency=" +
                                (launcherProjection.reducedTransparency ? L"true" : L"false") +
                                L" high-contrast=" +
                                (launcherProjection.highContrast ? L"true" : L"false") +
                                L" effect=" + std::wstring(
                                    gba::launcher::EffectQualityName(
                                        launcherProjection.effectQuality)) +
                                L" background=" +
                                (launcherProjection.backgroundIsFallback
                                    ? std::wstring{L"fallback"}
                                    : std::wstring{L"ready"}) +
                                L" background-focus=" +
                                (launcherProjection.backgroundFocusId.empty()
                                    ? std::wstring{L"none"}
                                    : launcherProjection.backgroundFocusId) +
                                L" crossfade=" +
                                (launcherProjection.backgroundTransitionActive
                                    ? L"active"
                                    : L"settled") +
                                L" input-to-focus-last-ms=" + std::to_wstring(
                                    launcherProjection.presentationMetrics
                                        .lastInputToFocusMilliseconds) +
                                L" input-to-focus-p95-ms=" + std::to_wstring(
                                    launcherProjection.presentationMetrics
                                        .p95InputToFocusMilliseconds) +
                                L" input-samples=" + std::to_wstring(
                                    launcherProjection.presentationMetrics
                                        .inputToFocusSampleCount) +
                                L" degraded=" + std::to_wstring(
                                    launcherProjection.presentationMetrics
                                        .degradedFrameCount));
                        }
                        const auto actualTrayBounds = compositionChromeSession_
                            ? ProjectChromeClientBoundsToScreen(
                                compositionChromeSession_->trayClientBounds)
                            : std::nullopt;
                        const auto actualChromeBounds = ActualChromeWindowBounds();
                        AppendDiagnostic(
                            L"Widget presentation paint target=" + std::wstring(widget) +
                            L" content=" +
                            (failureRetainedSnapshot
                                ? L"failure-retained"
                                : transitionRetainedSnapshot ? L"retained" : L"admitted") +
                            L" rendered=" + std::wstring(renderedWidget) +
                            L" sequence=" + std::to_wstring(snapshot->sequence) +
                            L" semantics=" +
                            (inertRetainedSnapshot ? L"inert" : L"current") +
                            L" input-owner=" + inputOwner +
                            L" selected=" + std::wstring(state_.selectedWidget()) +
                            L" visual-focus=" +
                            (renderedFocusId.empty()
                                ? std::wstring{L"none"}
                                : std::wstring{renderedFocusId}) +
                            L" semantic-focus=" + semanticFocus +
                            L" tray-total=" +
                            std::to_wstring(trayLayout ? trayLayout->totalCount : 0) +
                            L" tray-visible=" +
                            std::to_wstring(trayLayout ? trayLayout->tiles.size() : 0) +
                            L" tray-previous=" +
                            (trayLayout && trayLayout->previousOverflow ? L"true" : L"false") +
                            L" tray-next=" +
                            (trayLayout && trayLayout->nextOverflow ? L"true" : L"false") +
                            L" tray-selected-visible=" +
                            (selectedTrayItemVisible ? L"true" : L"false") +
                            L" shell-bounds=0,0," + std::to_wstring(width) + L"," +
                            std::to_wstring(height) +
                            L" desired-extent=" +
                            std::to_wstring(DesiredPresentationExtentDip().widthDip) +
                            L"x" +
                            std::to_wstring(DesiredPresentationExtentDip().heightDip) +
                            L" presented-extent=" +
                            std::to_wstring(PresentedPresentationExtentDip().widthDip) +
                            L"x" +
                            std::to_wstring(PresentedPresentationExtentDip().heightDip) +
                            L" body-bounds=" + std::to_wstring(geometry->panelX) + L"," +
                            std::to_wstring(geometry->panelY) + L"," +
                            std::to_wstring(geometry->panelWidth) + L"," +
                            std::to_wstring(geometry->panelHeight) +
                            L" viewport-bounds=" +
                            std::to_wstring(geometry->widgetViewportX) + L"," +
                            std::to_wstring(geometry->widgetViewportY) + L"," +
                            std::to_wstring(geometry->widgetViewportWidth) + L"," +
                            std::to_wstring(geometry->widgetViewportHeight) +
                            L" tray-bounds=" +
                            (actualTrayBounds
                                ? FormatPhysicalBounds(*actualTrayBounds)
                                : trayLayout
                                ? std::to_wstring(trayLayout->stripBounds.x) + L"," +
                                    std::to_wstring(trayLayout->stripBounds.y) + L"," +
                                    std::to_wstring(trayLayout->stripBounds.width) + L"," +
                                    std::to_wstring(trayLayout->stripBounds.height)
                                : std::wstring{L"missing"}) +
                            L" chrome-hwnd=" +
                            (actualChromeBounds
                                ? FormatPhysicalBounds(*actualChromeBounds)
                                : std::wstring{L"missing"}) +
                            L" chrome-placement-count=" +
                            std::to_wstring(fixedChromePlacementCount_) +
                            L" tray-selected-bounds=" +
                            (selectedTrayItem
                                ? std::to_wstring(selectedTrayItem->bounds.x) + L"," +
                                    std::to_wstring(selectedTrayItem->bounds.y) + L"," +
                                    std::to_wstring(selectedTrayItem->bounds.width) + L"," +
                                    std::to_wstring(selectedTrayItem->bounds.height)
                                : std::wstring{L"missing"}));
                    }
                }
                declarativeMotionActive_ = !inertRetainedSnapshot && result.animationActive;
                if (!inertRetainedSnapshot) {
                    if (const auto visibleFocus = gba::input::ResolveVisibleFocusTarget(
                        focusedElementId_, semanticSnapshot.activeInputScopeId, result);
                        visibleFocus && *visibleFocus != focusedElementId_) {
                        (void)pressedInteraction_.Clear();
                        focusedElementId_ = *visibleFocus;
                        (void)scrollEvidenceProbe_.RecordTarget(
                            *visibleFocus, L"reconcile");
                        focusMemory_.Remember(widget, semanticSnapshot, focusedElementId_);
                        // The completed pass used the old focus state. Schedule one
                        // more paint so the recovered target receives its ring.
                        InvalidateRect(window_, nullptr, FALSE);
                    }
                }
                if (inertRetainedSnapshot) {
                    ClearAccessibilityTree();
                } else if (accessibilityActive_ && !result.succeeded) {
                    lastWidgetRenderResult_ = result;
                    ClearAccessibilityTree();
                } else {
                    lastWidgetRenderResult_ = result;
                }
                if (!inertRetainedSnapshot && renderedFocusId == focusedElementId_) {
                    (void)scrollEvidenceProbe_.Publish(
                        widget, semanticSnapshot, result, renderedFocusId,
                        options.pixelScale, options.accessibility.textScale);
                }
                if (!inertRetainedSnapshot && collectAccessibility) {
                    widgetAccessibilityTree_ = gba::accessibility::BuildWidgetTree(
                        std::wstring{widget}, descriptor->runtimeGeneration,
                        semanticSnapshot, result,
                        state_.focusRegion() == gba::FocusRegion::Widget
                            ? std::wstring_view{focusedElementId_}
                            : std::wstring_view{},
                        options.sliderValueOverrides);
                    ++widgetAccessibilityRevision_;
                    widgetAccessibilityProjection_.Published(projectionKey);
                }
                if (!inertRetainedSnapshot && accessibilityActive_ &&
                    widgetAccessibilityProjection_.ObserveFrame(result.animationActive))
                    InvalidateRect(window_, nullptr, FALSE);
                const auto lastSequence = renderedSnapshotSequences_.find(std::wstring(widget));
                if (!inertRetainedSnapshot &&
                    (lastSequence == renderedSnapshotSequences_.end() ||
                     lastSequence->second != snapshot->sequence)) {
                    for (const auto& diagnostic : result.diagnostics) {
                        AppendDiagnostic(
                            L"Renderer " + std::wstring(widget) + L" " + diagnostic.code + L" [" + diagnostic.nodeId +
                            L"] " + diagnostic.message);
                    }
                    renderedSnapshotSequences_.insert_or_assign(
                        std::wstring(widget), snapshot->sequence);
                }
            } else {
                ClearAccessibilityTree();
                DrawTextLine(sessions_.Failure(widget)
                                 ? std::wstring{L"Widget unavailable. Press A to retry."}
                                 : L"Starting isolated " +
                                       std::wstring(DisplayWidgetName(widget)) + L" widget…",
                             bodyFormat_.Get(),
                             D2D1::RectF(panelLeft + 30, 52,
                                         panelLeft + panelWidth - 30, 110),
                             secondaryBrush_.Get());
            }
            if (contentLayerPushed) renderTarget_->PopLayer();
            if (drawGuide) DrawWidgetFooter(
                *geometry, fixedGuideBounds ? &*fixedGuideBounds : nullptr);
            if (drawTray) {
                DrawIconStrip(width, height, &*geometry, nullptr,
                              trayLayout ? &*trayLayout : nullptr);
            } else if (accessibilityActive_ && trayLayout) {
                PublishTrayAccessibility(*trayLayout, width, height);
            }
            return;
        }

        DrawTextLine(L"Widget unavailable", titleFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 48, panelLeft + panelWidth - 30, 88),
                     textBrush_.Get());
        DrawTextLine(L"This widget is no longer present in the active catalog.", bodyFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 94, panelLeft + panelWidth - 30, 122),
                     secondaryBrush_.Get());
        DrawTextLine(L"Return to the dashboard while the catalog is refreshed.",
                     bodyFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 146, panelLeft + panelWidth - 30, 202),
                     secondaryBrush_.Get());
        if (drawGuide) DrawWidgetFooter(
            *geometry, fixedGuideBounds ? &*fixedGuideBounds : nullptr);
        if (drawTray) {
            DrawIconStrip(width, height, &*geometry, nullptr,
                          trayLayout ? &*trayLayout : nullptr);
        } else if (accessibilityActive_ && trayLayout) {
            PublishTrayAccessibility(*trayLayout, width, height);
        }
    }

    HINSTANCE instance_{};
    HWND window_{};
    HWND chromeWindow_{};
    GbaOverlayPlatformHandle* platform_{};
    gba::PlacementRefreshGate placementRefreshGate_;
    gba::DisplayRefreshAccumulator displayRefresh_;
    HWND backdropWindow_{};
    HBRUSH backdropBrush_{};
    HWINEVENTHOOK foregroundHook_{};
    inline static OverlayApp* foregroundEventApp_{};
    std::wstring initializationError_;
    std::wstring installationDirectory_;
    std::optional<std::wstring> developmentCatalogRoot_;
    std::optional<std::wstring> developmentReadyPath_;
    std::optional<std::wstring> developmentReadyNonce_;
    std::optional<std::wstring> developmentWidgetId_;
    std::optional<std::wstring> developmentWidgetInstance_;
    bool developmentProbeOnly_{};
    std::optional<std::wstring> performanceState_;
    std::optional<std::wstring> performanceWidgetId_;
    std::optional<std::wstring> performanceDiagnosticsPath_;
    std::optional<std::wstring> performanceDiagnosticsNonce_;
    std::optional<std::wstring> scrollEvidencePath_;
    gba::ScrollEvidenceProbe scrollEvidenceProbe_;
    bool performanceCountersActive_{};
    unsigned long long performanceCounterStarted_{};
    unsigned long long performanceTimerMessages_{};
    unsigned long long performanceControllerTimerMessages_{};
    unsigned long long performanceGuideCompatibilityTimerMessages_{};
    unsigned long long performancePaintMessages_{};
    unsigned long long performanceSuccessfulFrames_{};
    gba::OverlayState state_;
    gba::input::TrayYGesture trayYGesture_;
    std::optional<bool> lastForegroundOwnership_;
    long long controllerSequence_{};
    std::wstring lastActionMessage_;
    std::wstring lastActionWidgetId_;
    ULONGLONG lastActionExpiresAt_{};
    std::wstring admissionTraceWidget_;
    std::uint64_t admissionTraceCorrelationId_{};
    gba::WidgetActionFeedbackHost actionFailureFeedback_;
    gba::WidgetAdmissionTrace admissionTrace_;
    gba::accessibility::Tree accessibilityTree_;
    gba::accessibility::Tree widgetAccessibilityTree_;
    gba::accessibility::OpenWidgetSemantics openWidgetAccessibility_;
    gba::accessibility::ProviderHost accessibilityProvider_;
    gba::accessibility::ProviderHost chromeAccessibilityProvider_;
    bool accessibilityActive_{};
    gba::accessibility::ProjectionTracker accessibilityProjection_;
    gba::accessibility::ProjectionTracker widgetAccessibilityProjection_;
    std::uint64_t widgetAccessibilityRevision_{};
    long long hostAccessibilitySequence_{};
    ULONGLONG sliderReconcileAt_{};
    std::wstring focusedElementId_;
    gba::input::WidgetSurfaceFocusMemory focusMemory_;
    gba::input::SliderInteractionState sliderInteraction_;
    gba::input::PressedInteractionState pressedInteraction_;
    gba::input::TextEntryModal textEntryModal_;
    gba::WidgetBridgeClient bridge_;
    gba::WidgetSessionCoordinator sessions_;
    gba::packages::FileOpenDialogWidgetPackagePicker localWidgetPackagePicker_;
    gba::packages::LocalWidgetPackageImport localWidgetPackageImport_;
    std::optional<gba::LocalWidgetPackageInstallResult>
        lastLocalWidgetPackageInstallResult_;
    gba::PlatformAppearanceState appearanceState_;
    gba::NativeRenderStyle canvasStyle_;
    gba::NativeRenderStyle backdropStyle_;
    gba::NativeRenderStyle panelStyle_;
    gba::NativeRenderStyle trayStyle_;
    gba::NativeColor effectiveCanvasBackground_{kDefaultCanvas};
    gba::NativeColor effectivePanelBackground_{kDefaultPanel};
    gba::NativeColor effectiveTrayBackground_{kDefaultCanvas};
    gba::NativeRenderStyle trayItemStyle_;
    gba::NativeRenderStyle trayItemSelectedStyle_;
    gba::NativeRenderStyle trayItemFocusedStyle_;
    gba::NativeRenderStyle trayItemSelectedFocusedStyle_;
    gba::NativeRenderStyle titleStyle_;
    gba::NativeRenderStyle bodyStyle_;
    gba::NativeRenderStyle hintStyle_;
    gba::NativeRenderStyle statusStyle_;
    gba::NativeRenderStyle dashboardTitleStyle_;
    gba::NativeRenderStyle dashboardHintStyle_;
    float panelCornerRadius_{18.0F};
    float trayCornerRadius_{22.0F};
    float trayItemCornerRadius_{16.0F};
    float focusOutlineWidth_{2.0F};
    std::unique_ptr<gba::RemoteImageCache> imageCache_;
    std::unique_ptr<gba::DeclarativeRenderer> declarativeRenderer_;
    gba::launcher::LauncherExperienceProjection launcherExperienceProjection_;
    bool launcherSafeStartPending_{};
    gba::pinned::WidgetSurfaceCoordinator pinnedSurfaceCoordinator_;
    bool runtimeInitialized_{};
    std::unordered_map<std::wstring, long long> renderedSnapshotSequences_;
    gba::RenderResult lastWidgetRenderResult_;
    bool declarativeMotionActive_{};
    gba::OverlayTransitionTimeline overlayTransition_;
    gba::OverlayTransitionSample overlayTransitionSample_{};
    gba::OverlayPresentationTransaction presentationTransaction_;
    std::wstring retainedGuidePaintKey_;
    std::optional<gba::shell::RetainedTrayState> retainedTrayPaintState_;
    std::optional<FixedChromeAnchor> fixedChromeAnchor_;
    std::optional<CompositionChromeSession> compositionChromeSession_;
    FixedChromePlacementReason pendingFixedChromePlacementReason_{
        FixedChromePlacementReason::NewVisibleSession};
    std::uint64_t fixedChromePlacementCount_{};
    bool awaitingSuccessfulOpenPaint_{};
    ULONGLONG nextOpenPaintRetryAt_{};
    std::wstring pendingContentRevealWidget_;
    mutable std::optional<WidgetSurfaceResolutionCache>
        widgetSurfaceResolutionCache_;
    std::wstring lastWidgetPresentationPaintKey_;
    BYTE targetOverlayOpacity_{248};
    BYTE targetBackdropOpacity_{kBackdropOpacity};
    std::optional<BYTE> appliedOverlayOpacity_;
    std::optional<BYTE> appliedBackdropOpacity_;

    ComPtr<ID2D1Factory1> d2dFactory_;
    ComPtr<IDWriteFactory> writeFactory_;
    gba::OverlayCompositionSurface compositionSurface_;
    bool compositionPlacementInProgress_{};
    ComPtr<ID2D1HwndRenderTarget> hwndRenderTarget_;
    ComPtr<ID2D1RenderTarget> renderTarget_;
    ComPtr<ID2D1SolidColorBrush> backgroundBrush_;
    ComPtr<ID2D1SolidColorBrush> cardBrush_;
    ComPtr<ID2D1SolidColorBrush> textBrush_;
    ComPtr<ID2D1SolidColorBrush> secondaryBrush_;
    ComPtr<ID2D1SolidColorBrush> dashboardTextBrush_;
    ComPtr<ID2D1SolidColorBrush> dashboardSecondaryBrush_;
    ComPtr<ID2D1SolidColorBrush> accentBrush_;
    ComPtr<ID2D1SolidColorBrush> successBrush_;
    ComPtr<ID2D1SolidColorBrush> trayItemBrush_;
    ComPtr<ID2D1SolidColorBrush> trayItemTextBrush_;
    ComPtr<ID2D1SolidColorBrush> selectedTextBrush_;
    ComPtr<ID2D1SolidColorBrush> focusBrush_;
    ComPtr<IDWriteTextFormat> titleFormat_;
    ComPtr<IDWriteTextFormat> bodyFormat_;
    ComPtr<IDWriteTextFormat> hintFormat_;
    ComPtr<IDWriteTextFormat> iconFormat_;
};

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int showCommand) {
    std::wstring processProfile = L"production";
    bool processOwnerProbe = false;
    bool launcherExperienceSemanticProof = false;
    for (int index = 1; index < __argc; ++index) {
        if (_wcsicmp(__wargv[index], L"--process-profile") == 0 &&
            index + 1 < __argc) {
            processProfile = __wargv[++index];
        } else if (_wcsicmp(__wargv[index], L"--process-owner-probe") == 0) {
            processOwnerProbe = true;
        } else if (_wcsicmp(__wargv[index], L"--launcher-experience-semantic-proof") == 0) {
            launcherExperienceSemanticProof = true;
        }
    }
    if (launcherExperienceSemanticProof) {
        std::wstring diagnostic;
        const bool passed = gba::launcher::RunProductionHostSemanticProof(diagnostic);
        AppendDiagnostic(L"Launcher Experience production-host semantic proof " + diagnostic);
        return passed ? EXIT_SUCCESS : EXIT_FAILURE;
    }
    gba::process::OverlayProcessOwner processOwner;
    std::wstring ownershipError;
    const auto ownership = processOwner.Begin(
        processProfile, std::chrono::milliseconds(3000), ownershipError);
    if (ownership == gba::process::OwnershipResult::ClientAcknowledged) {
        AppendDiagnostic(
            L"Activation client forwarded Show and exited profile=" + processProfile +
            L" pid=" + std::to_wstring(GetCurrentProcessId()));
        return EXIT_SUCCESS;
    }
    if (ownership != gba::process::OwnershipResult::Owner) {
        AppendDiagnostic(L"Activation client failed: " + ownershipError);
        return EXIT_FAILURE;
    }
    AppendDiagnostic(
        L"OverlayHost process owner elected profile=" + processProfile +
        L" pid=" + std::to_wstring(GetCurrentProcessId()));
    if (processOwnerProbe) {
        const bool received = processOwner.WaitForShow(std::chrono::seconds(10));
        AppendDiagnostic(received
            ? L"Process-owner probe received Show without initializing OverlayApp"
            : L"Process-owner probe timed out");
        return received ? EXIT_SUCCESS : EXIT_FAILURE;
    }
    OverlayApp app;
    if (!app.Initialize(instance, showCommand)) {
        const std::wstring message = L"OverlayHost failed to initialize.\n\n" +
                                     app.initializationError();
        SaveStartupError(message);
        MessageBoxW(nullptr, message.c_str(),
                    L"Game Bar Alternative", MB_OK | MB_ICONERROR);
        return EXIT_FAILURE;
    }
    app.BindProcessActivation(processOwner);
    return app.Run();
}
