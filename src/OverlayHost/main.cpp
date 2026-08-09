#include "OverlayState.h"
#include "DeclarativeRenderer.h"
#include "ControllerNavigation.h"
#include "ControllerInputOwnership.h"
#include "GuideInputCompatibility.h"
#include "FocusNavigation.h"
#include "NativeIcons.h"
#include "NativeStyle.h"
#include "OverlayPlacement.h"
#include "OverlayTargeting.h"
#include "OverlayTransition.h"
#include "PressedInteraction.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"
#include "WidgetLifecycle.h"
#include "WidgetSurfaceFocus.h"
#include "SliderInteraction.h"

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <dwmapi.h>
#include <GameInput.h>
#include <Roapi.h>
#include <ShellScalingApi.h>
#include <Xinput.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <climits>
#include <filesystem>
#include <fstream>
#include <limits>
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
using namespace GameInput::v3;

namespace {

constexpr wchar_t kWindowClass[] = L"GameBarAlternative.OverlayHost";
constexpr wchar_t kBackdropWindowClass[] = L"GameBarAlternative.Backdrop";
constexpr int kPanelWidth = 1180;
constexpr int kDashboardHeight = 180;
constexpr int kWidgetPanelHeight = 700;
constexpr UINT_PTR kControllerTimer = 1;
constexpr UINT_PTR kGuideCompatibilityTimer = 2;
constexpr UINT_PTR kZOrderSettleTimer = 3;
constexpr UINT_PTR kCatalogRetryTimer = 4;
constexpr UINT_PTR kForegroundLossTimer = 5;
constexpr UINT kGuideMessage = WM_APP + 1;
constexpr UINT kImageReadyMessage = WM_APP + 2;
constexpr UINT kCatalogRefreshMessage = WM_APP + 3;
constexpr UINT kSnapshotRefreshMessage = WM_APP + 4;
constexpr UINT kForegroundChangedMessage = WM_APP + 5;
constexpr UINT kPlacementRefreshMessage = WM_APP + 6;
constexpr UINT kDisplayRefreshMessage = WM_APP + 7;
constexpr UINT kPerformanceResetMessage = WM_APP + 8;
constexpr UINT kGuideCompatibilityDeviceMessage = WM_APP + 9;
constexpr BYTE kBackdropOpacity = 164;
constexpr int kDeveloperHotkey = 1;
constexpr gba::NativeColor kSafeCanvasFallback{
    1.0F / 255.0F, 2.0F / 255.0F, 3.0F / 255.0F, 1.0F};
constexpr gba::NativeColor kDefaultCanvas{
    0x10 / 255.0F, 0x13 / 255.0F, 0x1A / 255.0F, 1.0F};
constexpr gba::NativeColor kDefaultPanel{
    0x1B / 255.0F, 0x1F / 255.0F, 0x29 / 255.0F, 1.0F};

struct GuideCompatibilityDeviceChange final {
    gba::input::GuideCompatibilityActivation::DeviceId id{};
    bool connected{};
};
constexpr gba::NativeColor kDefaultAccent{
    0xFC / 255.0F, 0x3F / 255.0F, 0x6C / 255.0F, 1.0F};

enum class OverlayShowResult {
    Shown,
    Deferred,
    Failed,
};

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
    OverlayApp() : state_(LoadPersistentState(), {}) {}
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
        windowClass.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
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

        window_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST,
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
        SetLayeredWindowAttributes(window_, RGB(1, 2, 3), 248,
                                   LWA_ALPHA | LWA_COLORKEY);

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
        foregroundTarget_.SetOwnedWindows(
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
        const HRESULT dwriteResult = DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(writeFactory_.ReleaseAndGetAddressOf()));
        if (FAILED(dwriteResult)) {
            return FailHresult(L"DWriteCreateFactory", dwriteResult);
        }

        imageCache_ = std::make_unique<gba::RemoteImageCache>(
            gba::RemoteImageLimits{},
            [this](std::wstring_view, gba::RemoteImageState) {
                if (window_) PostMessageW(window_, kImageReadyMessage, 0, 0);
            });
        declarativeRenderer_ = std::make_unique<gba::DeclarativeRenderer>(
            d2dFactory_.Get(), writeFactory_.Get(), imageCache_.get());
        if (!developmentProbeOnly_) {
            RegisterHotKey(window_, kDeveloperHotkey, MOD_NOREPEAT, VK_F1);
            InitializeGameInput();
            if (guideCompatibility_.Initialize()) {
                if (compatibilityDeviceTrackingAvailable_) {
                    AppendDiagnostic(
                        L"XInput Guide compatibility adapter standing by for legacy devices");
                } else {
                    SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
                    AppendDiagnostic(
                        L"XInput Guide compatibility adapter active because GameInput device tracking is unavailable");
                }
            } else {
                AppendDiagnostic(L"XInput Guide compatibility adapter unavailable");
            }
        }

        // Platform appearance is bridge-owned but does not cross the lazy
        // widget-worker boundary. Fetch it once at host startup, then only in
        // response to a revision event.
        bool developmentCatalogReady = false;
        if (bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            RefreshPlatformAppearance();
            developmentCatalogReady = RefreshWidgetCatalog();
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
                *performanceState_ != L"interactive") {
                initializationError_ =
                    L"--performance-state must be hidden, visible, or interactive.";
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
        if (*performanceState_ == L"interactive") {
            Dispatch(gba::Command::Activate);
        }

        const auto expected = *performanceState_ == L"interactive"
            ? gba::WidgetLifecycleState::Interactive
            : gba::WidgetLifecycleState::Visible;
        if (!lifecycleBridgeState_ || *lifecycleBridgeState_ != expected ||
            lifecycleBridgeWidget_ != *performanceWidgetId_) {
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
        return std::any_of(widgetDescriptors_.begin(), widgetDescriptors_.end(),
                           [&](const gba::WidgetDescriptor& descriptor) {
                               return descriptor.id == *developmentWidgetId_ &&
                                      descriptor.instanceId == *developmentWidgetInstance_;
                           });
    }

    bool ProbeExpectedDevelopmentWidget() {
        if (!developmentWidgetId_ || !developmentWidgetInstance_) return false;
        if (!bridge_.SetWidgetLifecycle(*developmentWidgetId_, L"visible")) {
            initializationError_ = L"Development widget worker could not become visible: " +
                                   bridge_.lastError();
            return false;
        }

        auto snapshot = bridge_.GetSnapshot(*developmentWidgetId_);
        const std::wstring snapshotError = bridge_.lastError();
        const bool returnedToBackground = bridge_.SetWidgetLifecycle(
                                                *developmentWidgetId_, L"background")
                                                .value_or(false);
        const std::wstring backgroundError = bridge_.lastError();

        if (!snapshot) {
            initializationError_ = L"Development widget worker did not produce a valid snapshot: " +
                                   snapshotError;
            return false;
        }
        if (snapshot->instanceId != *developmentWidgetInstance_) {
            initializationError_ =
                L"Development widget worker returned a snapshot for the wrong package generation.";
            return false;
        }
        if (!returnedToBackground) {
            initializationError_ = L"Development widget worker could not return to background: " +
                                   backgroundError;
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
        const auto descriptors = bridge_.ListWidgets();
        if (!descriptors) {
            initializationError_ =
                L"Development bridge did not remain responsive after showing the overlay: " +
                bridge_.lastError();
            return false;
        }
        if (!std::any_of(descriptors->begin(), descriptors->end(),
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

    static void CALLBACK OnSystemButton(GameInputCallbackToken,
                                        void* context,
                                        IGameInputDevice*,
                                        uint64_t,
                                        GameInputSystemButtons current,
                                        GameInputSystemButtons previous) {
        const auto isPressed = [](GameInputSystemButtons value) {
            return (static_cast<unsigned>(value) &
                    static_cast<unsigned>(GameInputSystemButtonGuide)) != 0;
        };
        if (isPressed(current) && !isPressed(previous)) {
            AppendDiagnostic(L"GameInput Guide press callback received");
            const auto app = static_cast<OverlayApp*>(context);
            if (app && app->window_) {
                PostMessageW(app->window_, kGuideMessage, 0, 0);
            }
        }
    }

    static void CALLBACK OnGameInputDevice(
        GameInputCallbackToken,
        void* context,
        IGameInputDevice* device,
        uint64_t,
        GameInputDeviceStatus current,
        GameInputDeviceStatus previous) {
        const bool connected =
            (current & GameInputDeviceConnected) != GameInputDeviceNoStatus;
        const bool wasConnected =
            (previous & GameInputDeviceConnected) != GameInputDeviceNoStatus;
        if (connected == wasConnected || !device) return;

        const GameInputDeviceInfo* info{};
        if (FAILED(device->GetDeviceInfo(&info)) || !info ||
            info->deviceFamily != GameInputFamilyXbox360) return;

        GuideCompatibilityDeviceChange change;
        std::copy(std::begin(info->deviceId.value), std::end(info->deviceId.value),
                  change.id.begin());
        change.connected = connected;

        auto* app = static_cast<OverlayApp*>(context);
        if (!app || !app->window_) return;
        {
            std::scoped_lock lock(app->guideCompatibilityDeviceChangesMutex_);
            app->guideCompatibilityDeviceChanges_.push_back(change);
        }
        (void)PostMessageW(
            app->window_, kGuideCompatibilityDeviceMessage, 0, 0);
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
        case kGuideMessage:
            if (GetTickCount64() - lastGuideDispatchAt_ < 150) return 0;
            lastGuideDispatchAt_ = GetTickCount64();
            AppendDiagnostic(L"Guide toggle dispatched on window thread");
            Dispatch(gba::Command::ToggleOverlay);
            return 0;
        case kGuideCompatibilityDeviceMessage: {
            std::vector<GuideCompatibilityDeviceChange> changes;
            {
                std::scoped_lock lock(guideCompatibilityDeviceChangesMutex_);
                changes.swap(guideCompatibilityDeviceChanges_);
            }
            if (!compatibilityDeviceTrackingAvailable_ ||
                !guideCompatibility_.available()) return 0;

            bool activationChanged = false;
            for (const auto& change : changes) {
                activationChanged = guideCompatibilityActivation_.Update(
                    change.id, change.connected) || activationChanged;
            }
            if (activationChanged) {
                if (guideCompatibilityActivation_.active()) {
                    SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
                    AppendDiagnostic(
                        L"XInput Guide compatibility polling activated for a connected Xbox 360-family device");
                } else {
                    KillTimer(window_, kGuideCompatibilityTimer);
                    AppendDiagnostic(
                        L"XInput Guide compatibility polling stopped after the last Xbox 360-family device disconnected");
                }
            }
            return 0;
        }
        case kImageReadyMessage:
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kCatalogRefreshMessage: {
            if (state_.surface() == gba::Surface::Hidden) {
                KillTimer(window_, kCatalogRetryTimer);
                bridge_.AbandonWidgetCatalogChangedRevision();
                catalogRetryAttempts_ = 0;
                return 0;
            }
            bool refreshed = false;
            RefreshAndApplyPresentation([&] {
                refreshed = RefreshWidgetCatalog();
                if (refreshed) {
                    KillTimer(window_, kCatalogRetryTimer);
                    catalogRetryAttempts_ = 0;
                    RefreshCurrentBridgeSnapshot();
                }
            });
            if (!refreshed && bridge_.HasWidgetCatalogChangedRevisionInFlight() &&
                       state_.surface() != gba::Surface::Hidden &&
                       catalogRetryAttempts_ < 3) {
                const UINT delay = 250U << catalogRetryAttempts_++;
                SetTimer(window_, kCatalogRetryTimer, delay, nullptr);
            } else if (!refreshed) {
                bridge_.AbandonWidgetCatalogChangedRevision();
                catalogRetryAttempts_ = 0;
            }
            return 0;
        }
        case kSnapshotRefreshMessage:
            if (state_.surface() == gba::Surface::Hidden) return 0;
            RefreshAndApplyPresentation([&] { RefreshCurrentBridgeSnapshot(); });
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
                    (void)foregroundTarget_.Observe(
                        reinterpret_cast<std::uintptr_t>(foreground), true);
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
            if (wParam == kControllerTimer) {
                const auto now = GetTickCount64();
                if (overlayTransition_.active()) {
                    AdvanceOverlayTransition(now);
                }
                if (awaitingSuccessfulOpenPaint_ &&
                    now >= nextOpenPaintRetryAt_) {
                    nextOpenPaintRetryAt_ = now + 100;
                    InvalidateRect(window_, nullptr, FALSE);
                }
                // Closing changes semantic state immediately. The existing
                // controller timer is retained only long enough to finish the
                // bounded physical fade; no input or worker work runs behind
                // the hidden surface.
                if (state_.surface() == gba::Surface::Hidden) return 0;
                // A newly shown or device-lost HWND is deliberately alpha-zero
                // until D2D commits a complete frame. Do not let an invisible
                // surface consume navigation or advance its worker meanwhile;
                // Guide/F1 continue to arrive through their dedicated paths.
                if (awaitingSuccessfulOpenPaint_) return 0;
                PollController();
                (void)bridge_.PumpEvents();
                if (const auto revision = bridge_.TakePlatformAppearanceChangedRevision()) {
                    const auto& current = appearanceState_.current();
                    if (!current || *revision > current->revision) {
                        RefreshPlatformAppearance();
                    }
                }
                if (const auto revision = bridge_.TakeWidgetCatalogChangedRevision()) {
                    catalogRetryAttempts_ = 0;
                    AppendDiagnostic(L"Reconciling widget catalog revision " +
                                     std::to_wstring(*revision));
                    PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
                }
                for (auto& invalidatedWidget : bridge_.TakeInvalidatedWidgetIds()) {
                    const auto currentWidget = state_.surface() == gba::Surface::Widget
                        ? state_.activeWidget()
                        : state_.selectedWidget();
                    if (state_.surface() != gba::Surface::Hidden &&
                        currentWidget == invalidatedWidget) {
                        RefreshAndApplyPresentation([&] {
                            RefreshWidgetSnapshot(invalidatedWidget);
                        });
                    } else {
                        // Preserve the event's widget identity. An offscreen
                        // cache is invalidated and will be fetched on selection.
                        widgetSnapshots_.erase(invalidatedWidget);
                        renderedSnapshotSequences_.erase(invalidatedWidget);
                    }
                }
                for (const auto& effect : bridge_.TakeHostEffects()) {
                    const auto descriptor = std::find_if(
                        widgetDescriptors_.begin(), widgetDescriptors_.end(),
                        [&](const gba::WidgetDescriptor& candidate) {
                            return candidate.id == effect.widgetId;
                        });
                    const bool currentInteractiveWidget =
                        state_.surface() == gba::Surface::Widget &&
                        state_.focusRegion() == gba::FocusRegion::Widget &&
                        state_.activeWidget() == effect.widgetId &&
                        descriptor != widgetDescriptors_.end() &&
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
                const auto slots = guideCompatibility_.PollRisingEdges();
                if (slots != 0) {
                    AppendDiagnostic(L"Guide press received from quarantined XInput compatibility adapter; slots=" +
                                     std::to_wstring(slots));
                    PostMessageW(window_, kGuideMessage, 1, slots);
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
                        (void)foregroundTarget_.Observe(
                            reinterpret_cast<std::uintptr_t>(foreground), true);
                        AppendDiagnostic(
                            L"Application deactivation closed the overlay");
                        Dispatch(gba::Command::CloseOverlay);
                    }
                }
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
            if (renderTarget_ && wParam != SIZE_MINIMIZED &&
                LOWORD(lParam) != 0 && HIWORD(lParam) != 0) {
                // The viewport is an input to shell style resolution and
                // declarative responsive layout, not merely a bitmap extent.
                // Recreate on the next paint so vw/vh and pixel snapping use
                // the new client geometry atomically.
                DiscardGraphicsResources();
            }
            return 0;
        case WM_DPICHANGED:
            QueueDisplayEnvironmentRefresh(gba::DisplayEnvironmentChange::Dpi);
            return 0;
        case WM_DISPLAYCHANGE:
            // Display topology may change without a DPI transition. Recreate
            // the target so viewport-relative shell styles use fresh metrics.
            QueueDisplayEnvironmentRefresh(gba::DisplayEnvironmentChange::Topology);
            return 0;
        case WM_SETTINGCHANGE:
            // SPI_SETWORKAREA/taskbar changes and accessibility/theme changes
            // share this notification. Always re-read monitor work-area data,
            // even when the bridge has not supplied an appearance revision.
            QueueDisplayEnvironmentRefresh(gba::DisplayEnvironmentChange::SystemSettings);
            return 0;
        case WM_WINDOWPOSCHANGED:
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
            DestroyWindow(window_);
            return 0;
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcW(window_, message, wParam, lParam);
        }
    }

    void InitializeGameInput() {
        const HRESULT createResult = GameInputCreate(gameInput_.ReleaseAndGetAddressOf());
        if (FAILED(createResult)) {
            AppendDiagnostic(L"GameInputCreate failed HRESULT=" +
                             std::to_wstring(static_cast<unsigned long>(createResult)));
            return;
        }
        gameInput_->SetFocusPolicy(static_cast<GameInputFocusPolicy>(
            GameInputExclusiveForegroundInput |
            GameInputEnableBackgroundInput |
            GameInputEnableBackgroundGuideButton |
            GameInputExclusiveForegroundGuideButton));
        const HRESULT result = gameInput_->RegisterSystemButtonCallback(
            nullptr,
            GameInputSystemButtonGuide,
            this,
            OnSystemButton,
            &guideCallback_);
        if (FAILED(result)) {
            AppendDiagnostic(L"RegisterSystemButtonCallback failed HRESULT=" +
                             std::to_wstring(static_cast<unsigned long>(result)));
            guideCallback_ = 0;
            AppendDiagnostic(
                L"GameInput remains available for the visible ordinary-input lease; "
                L"Guide requires the compatibility path");
        } else {
            AppendDiagnostic(
                L"GameInput configured for a visible background-read lease plus "
                L"foreground-exclusive Guide and ordinary controls");

            const HRESULT deviceResult = gameInput_->RegisterDeviceCallback(
                nullptr,
                GameInputKindGamepad,
                GameInputDeviceConnected,
                GameInputAsyncEnumeration,
                this,
                OnGameInputDevice,
                &guideCompatibilityDeviceCallback_);
            if (FAILED(deviceResult)) {
                guideCompatibilityDeviceCallback_ = 0;
                AppendDiagnostic(
                    L"GameInput legacy-device tracking failed HRESULT=" +
                    std::to_wstring(static_cast<unsigned long>(deviceResult)));
            } else {
                compatibilityDeviceTrackingAvailable_ = true;
                AppendDiagnostic(
                    L"GameInput legacy-device tracking registered; hidden Guide polling remains dormant unless required");
            }
        }
        AppendDiagnostic(
            L"Controller exclusivity covers other GameInput clients only; XInput, Raw Input, "
            L"HID, and remapping drivers may still receive the same physical input");
    }

    void Shutdown() {
        declarativeRenderer_.reset();
        if (imageCache_) {
            imageCache_->Shutdown();
            imageCache_.reset();
        }
        if (!lifecycleBridgeWidget_.empty()) {
            (void)bridge_.SetWidgetLifecycle(
                lifecycleBridgeWidget_,
                gba::WidgetLifecycleProtocolValue(gba::WidgetLifecycleState::Background));
            lifecycleBridgeWidget_.clear();
            lifecycleBridgeState_.reset();
        }
        bridge_.Stop();
        if (window_) {
            KillTimer(window_, kControllerTimer);
            KillTimer(window_, kGuideCompatibilityTimer);
            KillTimer(window_, kZOrderSettleTimer);
            KillTimer(window_, kCatalogRetryTimer);
            KillTimer(window_, kForegroundLossTimer);
            UnregisterHotKey(window_, kDeveloperHotkey);
        }
        guideCompatibility_.Shutdown();
        if (foregroundHook_) {
            UnhookWinEvent(foregroundHook_);
            foregroundHook_ = nullptr;
        }
        if (foregroundEventApp_ == this) foregroundEventApp_ = nullptr;
        if (backdropWindow_ && IsWindow(backdropWindow_)) {
            DestroyWindow(backdropWindow_);
            backdropWindow_ = nullptr;
        }
        if (backdropBrush_) {
            DeleteObject(backdropBrush_);
            backdropBrush_ = nullptr;
        }
        if (gameInput_ && guideCallback_ != 0) {
            gameInput_->StopCallback(guideCallback_);
            gameInput_->UnregisterCallback(guideCallback_);
            guideCallback_ = 0;
        }
        if (gameInput_ && guideCompatibilityDeviceCallback_ != 0) {
            gameInput_->StopCallback(guideCompatibilityDeviceCallback_);
            gameInput_->UnregisterCallback(guideCompatibilityDeviceCallback_);
            guideCompatibilityDeviceCallback_ = 0;
        }
        compatibilityDeviceTrackingAvailable_ = false;
        guideCompatibilityActivation_.Reset();
        {
            std::scoped_lock lock(guideCompatibilityDeviceChangesMutex_);
            guideCompatibilityDeviceChanges_.clear();
        }
        gameInput_.Reset();
        if (runtimeInitialized_) {
            RoUninitialize();
            runtimeInitialized_ = false;
        }
    }

    void Dispatch(const gba::Command command) {
        const auto priorSurface = state_.surface();
        const auto priorFocusRegion = state_.focusRegion();
        const auto priorExtent = DesiredPresentationExtentDip();
        const std::wstring priorSelected(state_.selectedWidget());
        const std::wstring priorActive(state_.activeWidget());
        if (priorSurface == gba::Surface::Widget && IsBridgeWidget(priorActive)) {
            RememberCurrentFocus(priorActive);
        }
        const auto before = state_.persistent();
        if (!state_.Dispatch(command)) {
            return;
        }
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
        SyncWidgetActivity();
        if (priorSurface != state_.surface() || priorActive != state_.activeWidget()) {
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
        }
        if (state_.surface() == gba::Surface::Widget &&
            priorFocusRegion != state_.focusRegion() &&
            state_.focusRegion() == gba::FocusRegion::Widget) {
            RestoreFocusForActiveSurface(state_.activeWidget());
        }

        const auto now = GetTickCount64();
        if (priorSurface == gba::Surface::Hidden &&
            state_.surface() != gba::Surface::Hidden) {
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
        if (gba::ShouldRevealWidgetContent(
                priorSurface == gba::Surface::Widget, priorActive,
                state_.surface() == gba::Surface::Widget,
                state_.activeWidget())) {
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

        if (state_.surface() != gba::Surface::Hidden) {
            const bool enteredBridgeWidget =
                state_.surface() == gba::Surface::Widget && IsBridgeWidget(state_.activeWidget()) &&
                (priorSurface != gba::Surface::Widget || priorActive != state_.activeWidget());
            const bool hoveredBridgeWidget =
                state_.surface() == gba::Surface::Dashboard && IsBridgeWidget(state_.selectedWidget()) &&
                (priorSurface != gba::Surface::Dashboard || priorSelected != state_.selectedWidget());
            if (priorSurface == gba::Surface::Hidden) {
                PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
            }
            if (priorSurface != gba::Surface::Hidden &&
                (enteredBridgeWidget || hoveredBridgeWidget)) {
                PostMessageW(window_, kSnapshotRefreshMessage, 0, 0);
            }
        }
        ApplyPresentation(gba::DecideOverlayPresentation(
            priorSurface != gba::Surface::Hidden,
            state_.surface() != gba::Surface::Hidden,
            priorExtent,
            DesiredPresentationExtentDip()));
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
            const bool hadVisibleSnapshot = widgetSnapshots_.contains(visibleWidget);
            widgetSnapshots_.clear();
            renderedSnapshotSequences_.clear();
            if (state_.surface() != gba::Surface::Hidden && hadVisibleSnapshot &&
                IsBridgeWidget(visibleWidget)) {
                RefreshWidgetSnapshot(visibleWidget);
            }
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
        if (state_.surface() == gba::Surface::Hidden) return;
        if (!plan.repositionWindows) return;

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

    bool RefreshWidgetCatalog() {
        if (!bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            AppendDiagnostic(L"Widget catalog unavailable: " + bridge_.lastError());
            return false;
        }
        auto descriptors = bridge_.ListWidgets();
        if (!descriptors) {
            AppendDiagnostic(L"Widget catalog failed: " + bridge_.lastError());
            return false;
        }
        auto previousDescriptors = std::exchange(widgetDescriptors_, std::move(*descriptors));
        const auto runtimeChanged = [&](const std::wstring_view id) {
            const auto before = std::find_if(
                previousDescriptors.begin(), previousDescriptors.end(),
                [id](const gba::WidgetDescriptor& candidate) { return candidate.id == id; });
            const auto after = std::find_if(
                widgetDescriptors_.begin(), widgetDescriptors_.end(),
                [id](const gba::WidgetDescriptor& candidate) { return candidate.id == id; });
            return before == previousDescriptors.end() || after == widgetDescriptors_.end() ||
                   before->instanceId != after->instanceId ||
                   before->runtimeGeneration != after->runtimeGeneration;
        };
        const auto presentationChanged = [&](const std::wstring_view id) {
            const auto before = std::find_if(
                previousDescriptors.begin(), previousDescriptors.end(),
                [id](const gba::WidgetDescriptor& candidate) { return candidate.id == id; });
            const auto after = std::find_if(
                widgetDescriptors_.begin(), widgetDescriptors_.end(),
                [id](const gba::WidgetDescriptor& candidate) { return candidate.id == id; });
            return before == previousDescriptors.end() || after == widgetDescriptors_.end() ||
                   before->presentationGeneration != after->presentationGeneration;
        };
        std::unordered_set<std::wstring> bridgeIds;
        bridgeIds.reserve(widgetDescriptors_.size());
        for (const auto& descriptor : widgetDescriptors_) bridgeIds.emplace(descriptor.id);
        // Runtime identity owns focus memory independently of snapshot cache
        // residency. Clear every replaced/removed runtime even when its
        // offscreen snapshot was evicted earlier.
        for (const auto& id : gba::ChangedWidgetRuntimeIds(
                 previousDescriptors, widgetDescriptors_)) {
            focusMemory_.Forget(id);
            const auto previous = std::find_if(
                previousDescriptors.begin(), previousDescriptors.end(),
                [&](const gba::WidgetDescriptor& candidate) {
                    return candidate.id == id;
                });
            if (declarativeRenderer_ && previous != previousDescriptors.end() &&
                !previous->instanceId.empty()) {
                // Scroll offsets belong to the exact worker runtime, just like
                // focus memory. Never let a replacement package inherit native
                // renderer state merely because it reused public node IDs.
                declarativeRenderer_->ForgetWidgetState(previous->instanceId);
                sliderInteraction_.ForgetWidget(previous->instanceId);
            }
        }
        std::erase_if(widgetSnapshots_, [&](const auto& entry) {
            const auto descriptor = std::find_if(
                widgetDescriptors_.begin(), widgetDescriptors_.end(),
                [&](const gba::WidgetDescriptor& candidate) {
                    return candidate.id == entry.first;
                });
            const bool changed = descriptor == widgetDescriptors_.end() ||
                                 descriptor->instanceId != entry.second.instanceId ||
                                 presentationChanged(entry.first);
            return changed;
        });
        std::erase_if(renderedSnapshotSequences_, [&](const auto& entry) {
            return !bridgeIds.contains(entry.first);
        });
        std::vector<std::wstring> ids;
        ids.reserve(widgetDescriptors_.size());
        for (const auto& descriptor : widgetDescriptors_) {
            if (std::find(ids.begin(), ids.end(), descriptor.id) == ids.end()) {
                ids.push_back(descriptor.id);
            }
        }
        const std::wstring runtimeRevealWidget =
            state_.surface() == gba::Surface::Widget &&
                    runtimeChanged(state_.activeWidget())
                ? std::wstring(state_.activeWidget())
                : std::wstring{};
        if (!lifecycleBridgeWidget_.empty() && runtimeChanged(lifecycleBridgeWidget_)) {
            lifecycleBridgeWidget_.clear();
            lifecycleBridgeState_.reset();
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
        }
        const auto before = state_.persistent();
        if (state_.SetAvailableWidgets(std::move(ids)) &&
            before != state_.persistent() && !performanceState_) {
            SavePersistentState(state_.persistent());
        }
        SyncWidgetActivity();
        if (!runtimeRevealWidget.empty() &&
            state_.surface() == gba::Surface::Widget &&
            state_.activeWidget() == runtimeRevealWidget) {
            RequestWidgetContentReveal(runtimeRevealWidget);
        }
        return true;
    }

    [[nodiscard]] bool IsBridgeWidget(const std::wstring_view id) const noexcept {
        return std::any_of(
            widgetDescriptors_.begin(), widgetDescriptors_.end(),
            [id](const gba::WidgetDescriptor& descriptor) { return descriptor.id == id; });
    }

    void SyncWidgetActivity() {
        const auto desired = gba::DesiredWidgetLifecycle(
            state_.surface(), state_.focusRegion(),
            state_.selectedWidget(), state_.activeWidget(),
            IsBridgeWidget(state_.selectedWidget()),
            IsBridgeWidget(state_.activeWidget()));
        if (desired && desired->widgetId == lifecycleBridgeWidget_ &&
            lifecycleBridgeState_ == desired->state) {
            return;
        }

        if (!lifecycleBridgeWidget_.empty() &&
            (!desired || desired->widgetId != lifecycleBridgeWidget_)) {
            // Never restart the bridge or an unselected worker merely to send
            // Background. A tracked widget has already crossed the lazy-start
            // boundary, so this is best-effort cleanup on the existing pipe.
            if (!bridge_.SetWidgetLifecycle(
                    lifecycleBridgeWidget_,
                    gba::WidgetLifecycleProtocolValue(gba::WidgetLifecycleState::Background))) {
                AppendDiagnostic(L"Widget background transition failed for " + lifecycleBridgeWidget_ +
                                 L": " + bridge_.lastError());
            }
            lifecycleBridgeWidget_.clear();
            lifecycleBridgeState_.reset();
        }
        if (!desired) return;
        if (!bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            AppendDiagnostic(L"Widget lifecycle bridge unavailable: " + bridge_.lastError());
            return;
        }
        if (!bridge_.SetWidgetLifecycle(
                desired->widgetId, gba::WidgetLifecycleProtocolValue(desired->state))) {
            AppendDiagnostic(L"Widget lifecycle transition failed for " + desired->widgetId +
                             L": " + bridge_.lastError());
            return;
        }
        lifecycleBridgeWidget_ = desired->widgetId;
        lifecycleBridgeState_ = desired->state;
    }

    std::wstring_view DisplayWidgetName(const std::wstring_view id) const noexcept {
        const auto descriptor = std::find_if(
            widgetDescriptors_.begin(), widgetDescriptors_.end(),
            [id](const gba::WidgetDescriptor& candidate) { return candidate.id == id; });
        return descriptor == widgetDescriptors_.end() ? WidgetName(id) : descriptor->name;
    }

    gba::icons::NativeIcon DisplayWidgetIcon(const std::wstring_view id) const noexcept {
        const auto descriptor = std::find_if(
            widgetDescriptors_.begin(), widgetDescriptors_.end(),
            [id](const gba::WidgetDescriptor& candidate) { return candidate.id == id; });
        if (descriptor != widgetDescriptors_.end()) {
            gba::icons::NativeIcon icon{};
            if (gba::icons::TryParseNativeIcon(descriptor->icon, icon)) return icon;
        }
        return WidgetIcon(id);
    }

    void ApplyTransitionWindowOpacity(const float opacityFactor) {
        const auto factor = std::clamp(opacityFactor, 0.0F, 1.0F);
        const auto overlayOpacity = static_cast<BYTE>(std::lround(
            static_cast<float>(targetOverlayOpacity_) * factor));
        const auto backdropOpacity = static_cast<BYTE>(std::lround(
            static_cast<float>(targetBackdropOpacity_) * factor));
        if (!appliedOverlayOpacity_ || *appliedOverlayOpacity_ != overlayOpacity) {
            // The overlay relies on this key for transparent pixels. Alpha
            // updates must never silently drop LWA_COLORKEY.
            if (SetLayeredWindowAttributes(
                    window_, RGB(1, 2, 3), overlayOpacity,
                    LWA_ALPHA | LWA_COLORKEY)) {
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
        overlayTransitionSample_ = overlayTransition_.Sample(
            timestamp, CurrentAccessibilityPolicy().reducedMotion);
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
            // A loading placeholder is host feedback, not the incoming
            // immutable widget snapshot. Keep it stable and reveal only once
            // the requested runtime publishes content.
            overlayTransition_.SnapContentVisible();
        }
        AdvanceOverlayTransition(GetTickCount64());
    }

    void SnapWidgetContentVisible() {
        pendingContentRevealWidget_.clear();
        overlayTransition_.SnapContentVisible();
        AdvanceOverlayTransition(GetTickCount64());
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
            (void)foregroundTarget_.Observe(
                reinterpret_cast<std::uintptr_t>(foreground),
                foreground && IsWindow(foreground));
        }

        const HWND remembered = reinterpret_cast<HWND>(foregroundTarget_.remembered());
        const HWND targetWindow = reinterpret_cast<HWND>(foregroundTarget_.Resolve(
            reinterpret_cast<std::uintptr_t>(window_),
            remembered && IsWindow(remembered)));
        const HMONITOR monitor = MonitorFromWindow(targetWindow, MONITOR_DEFAULTTONEAREST);
        if (!monitor) {
            AppendDiagnostic(L"Unable to resolve target monitor for overlay");
            return OverlayShowResult::Failed;
        }
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        if (!GetMonitorInfoW(monitor, &monitorInfo)) {
            AppendDiagnostic(L"GetMonitorInfoW failed error=" +
                             std::to_wstring(GetLastError()));
            return OverlayShowResult::Failed;
        }
        const RECT& work = monitorInfo.rcWork;
        // The foreground game may be DPI-unaware, in which case
        // GetDpiForWindow(targetWindow) is virtualized to 96. The host is PMv2,
        // so resolve the effective DPI from the destination monitor itself.
        UINT dpi = 96;
        UINT monitorDpiY = 96;
        if (FAILED(GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &dpi, &monitorDpiY)) ||
            dpi == 0 || monitorDpiY == 0) {
            dpi = 96;
        }
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        float desiredWidthDip = static_cast<float>(kPanelWidth);
        float desiredHeightDip = static_cast<float>(kDashboardHeight);
        if (state_.surface() == gba::Surface::Widget) {
            const auto resolved = gba::ResolveWidgetSurface(
                CurrentWidgetSurfaceRequest(),
                gba::WidgetSurfaceConstraints{
                    {work.left, work.top, work.right, work.bottom},
                    dpi,
                    interfaceScale,
                    CurrentTextScale(),
                });
            if (!resolved) {
                AppendDiagnostic(L"Unable to resolve a safe widget surface");
                return OverlayShowResult::Failed;
            }
            desiredWidthDip = resolved->windowWidthDip;
            desiredHeightDip = resolved->windowHeightDip;
        }
        const auto placement = gba::ComputeOverlayPlacement(
            {work.left, work.top, work.right, work.bottom}, dpi,
            desiredWidthDip * interfaceScale,
            desiredHeightDip * interfaceScale);
        if (!placement) {
            AppendDiagnostic(L"Unable to compute a safe overlay placement");
            return OverlayShowResult::Failed;
        }

        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        const BOOL backdropPlaced = SetWindowPos(
            backdropWindow_, HWND_TOPMOST,
            monitorInfo.rcMonitor.left, monitorInfo.rcMonitor.top,
            monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left,
            monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top,
            SWP_SHOWWINDOW | SWP_NOACTIVATE);
        const UINT overlayPlacementFlags = SWP_SHOWWINDOW | SWP_NOACTIVATE |
            (atomicVisibleTransition && wasVisible ? SWP_NOREDRAW : 0U);
        const BOOL overlayPlaced = SetWindowPos(
            window_, HWND_TOPMOST, placement->x, placement->y,
            placement->width, placement->height,
            overlayPlacementFlags);
        if (!backdropPlaced || !overlayPlaced) {
            AppendDiagnostic(L"Overlay placement failed error=" +
                             std::to_wstring(GetLastError()));
            return OverlayShowResult::Failed;
        }
        if (!wasVisible) {
            ShowWindow(backdropWindow_, SW_SHOWNOACTIVATE);
            ShowWindow(window_, SW_SHOWNORMAL);
        }
        visibleControllerReadLease_ = true;
        if (!wasVisible) {
            // Activation is one best-effort show-time request. Controller
            // reliability comes from the visible GameInput lease, not a
            // repeated foreground-steal loop.
            (void)AcquireOverlayForegroundInput();
            SetTimer(window_, kControllerTimer, 16, nullptr);
            PrimeControllerState();
        }
        return OverlayShowResult::Shown;
    }

    void HideOverlay() {
        KillTimer(window_, kControllerTimer);
        visibleControllerReadLease_ = false;
        lastControllerReadPath_ = gba::input::ControllerReadPath::None;
        lastControllerForegroundExclusive_.reset();
        lastForegroundOwnership_.reset();
        declarativeMotionActive_ = false;
        pendingContentRevealWidget_.clear();
        awaitingSuccessfulOpenPaint_ = false;
        nextOpenPaintRetryAt_ = 0;
        KillTimer(window_, kCatalogRetryTimer);
        KillTimer(window_, kForegroundLossTimer);
        bridge_.AbandonWidgetCatalogChangedRevision();
        catalogRetryAttempts_ = 0;
        ShowWindow(window_, SW_HIDE);
        ShowWindow(backdropWindow_, SW_HIDE);
        SetWindowPos(window_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(backdropWindow_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        DiscardGraphicsResources();
        const HWND restoreTarget = reinterpret_cast<HWND>(foregroundTarget_.remembered());
        if (restoreTarget && IsWindow(restoreTarget)) {
            SetForegroundWindow(restoreTarget);
        }
    }

    void ReassertOverlayZOrder() {
        if (!window_ || !backdropWindow_ ||
            state_.surface() == gba::Surface::Hidden) return;
        const auto flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW;
        const BOOL backdropResult = SetWindowPos(
            backdropWindow_, HWND_TOPMOST, 0, 0, 0, 0, flags);
        const BOOL overlayResult = SetWindowPos(
            window_, HWND_TOPMOST, 0, 0, 0, 0, flags);
        if (!backdropResult || !overlayResult) {
            AppendDiagnostic(L"Topmost reassertion failed error=" +
                             std::to_wstring(GetLastError()));
        }
    }

    [[nodiscard]] float CurrentTextScale() const noexcept {
        return appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->textScale)
            : 1.0F;
    }

    [[nodiscard]] std::optional<gba::WidgetSurfaceRequest>
    CurrentWidgetSurfaceRequest() const {
        if (!IsBridgeWidget(state_.activeWidget())) return std::nullopt;
        const auto* snapshot = SnapshotFor(state_.activeWidget());
        if (!snapshot) {
            // Startup is host UI rather than a protocol-v1 view. Keep it
            // compact until the worker publishes its authoritative surface.
            return gba::WidgetSurfaceRequest{gba::WidgetSurfaceMode::Compact};
        }
        if (!snapshot->surface) return std::nullopt;

        gba::WidgetSurfaceRequest request;
        if (snapshot->surface->mode == L"compact") {
            request.mode = gba::WidgetSurfaceMode::Compact;
        } else if (snapshot->surface->mode == L"standard") {
            request.mode = gba::WidgetSurfaceMode::Standard;
        } else if (snapshot->surface->mode == L"wide") {
            request.mode = gba::WidgetSurfaceMode::Wide;
        } else {
            // Unknown and empty values fail safely to Adaptive. The managed
            // validator rejects them earlier, but native parsing is still an
            // untrusted transport boundary.
            request.mode = gba::WidgetSurfaceMode::Adaptive;
        }
        const auto toFloat = [](const std::optional<double> value) -> std::optional<float> {
            if (!value || !std::isfinite(*value) ||
                *value > std::numeric_limits<float>::max() ||
                *value < -std::numeric_limits<float>::max()) return std::nullopt;
            return static_cast<float>(*value);
        };
        request.preferredWidthDip = toFloat(snapshot->surface->preferredWidth);
        request.preferredHeightDip = toFloat(snapshot->surface->preferredHeight);
        request.minimumWidthDip = toFloat(snapshot->surface->minimumWidth);
        request.minimumHeightDip = toFloat(snapshot->surface->minimumHeight);
        return request;
    }

    [[nodiscard]] gba::ResolvedWidgetSurface DesiredWidgetSurfaceTarget() const {
        return gba::ResolveWidgetSurfaceTarget(
            CurrentWidgetSurfaceRequest(), CurrentTextScale());
    }

    [[nodiscard]] gba::OverlayPresentationExtent DesiredPresentationExtentDip() const {
        if (state_.surface() != gba::Surface::Widget) {
            return {kPanelWidth, kDashboardHeight};
        }
        const auto target = DesiredWidgetSurfaceTarget();
        return {
            static_cast<int>(std::lround(target.windowWidthDip)),
            static_cast<int>(std::lround(target.windowHeightDip)),
        };
    }

    void ApplyPresentation(const gba::OverlayPresentationDirective directive) {
        switch (directive) {
        case gba::OverlayPresentationDirective::None:
            return;
        case gba::OverlayPresentationDirective::Hide:
            // Lifecycle/background state was committed before presentation.
            // Shut down semantic input immediately, then defer only HWND
            // hiding, resource discard, and foreground restoration.
            visibleControllerReadLease_ = false;
            lastControllerReadPath_ = gba::input::ControllerReadPath::None;
            lastControllerForegroundExclusive_.reset();
            lastForegroundOwnership_.reset();
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
        const auto priorExtent = DesiredPresentationExtentDip();
        std::forward<Refresh>(refresh)();
        const bool isVisible = state_.surface() != gba::Surface::Hidden;
        ApplyPresentation(gba::DecideOverlayPresentation(
            wasVisible, isVisible, priorExtent, DesiredPresentationExtentDip()));
    }

    [[nodiscard]] std::optional<std::size_t> HitTraySlot(
        const float x,
        const float y,
        const float width,
        const float height,
        const gba::OverlaySurfaceGeometry* surfaceGeometry) const {
        if (state_.order().empty() || width <= 0.0F || height <= 0.0F) {
            return std::nullopt;
        }
        constexpr float preferredTileSize = 64.0F;
        constexpr float gap = 14.0F;
        const float stripTop = surfaceGeometry
            ? surfaceGeometry->trayY
            : std::max(0.0F, height - 112.0F);
        const float stripBottom = surfaceGeometry
            ? surfaceGeometry->trayY + surfaceGeometry->trayHeight
            : std::max(stripTop, height - 14.0F);
        const float stripHeight = stripBottom - stripTop;
        if (stripHeight <= 0.0F) return std::nullopt;
        const float verticalPadding = std::min(
            16.0F, std::max(0.0F, (stripHeight - preferredTileSize) * 0.5F));
        const float horizontalPadding = std::min(14.0F, width * 0.15F);
        const float tileSize = std::max(
            1.0F, std::min({preferredTileSize,
                            width - horizontalPadding * 2.0F,
                            stripHeight - verticalPadding * 2.0F}));
        const float stripPadding = std::min(
            14.0F, std::max(0.0F, (width - tileSize) * 0.5F));
        const auto maximumVisible = static_cast<std::size_t>(std::max(
            1.0F, std::floor((width - horizontalPadding * 2.0F + gap) /
                             (preferredTileSize + gap))));
        const std::size_t visibleCount = std::min(state_.order().size(), maximumVisible);
        const std::size_t half = visibleCount / 2;
        const std::size_t maximumFirst = state_.order().size() - visibleCount;
        const std::size_t firstSlot = std::min(
            state_.selectedSlot() > half ? state_.selectedSlot() - half : 0U,
            maximumFirst);
        const float stripWidth = tileSize * static_cast<float>(visibleCount) +
                                 gap * static_cast<float>(visibleCount - 1) +
                                 stripPadding * 2.0F;
        const float stripLeft = (width - stripWidth) * 0.5F;
        const float tileTop = stripTop + verticalPadding;
        for (std::size_t visibleIndex = 0; visibleIndex < visibleCount; ++visibleIndex) {
            const float tileLeft = stripLeft + stripPadding +
                static_cast<float>(visibleIndex) * (tileSize + gap);
            if (x >= tileLeft && x < tileLeft + tileSize &&
                y >= tileTop && y < tileTop + tileSize) {
                return firstSlot + visibleIndex;
            }
        }
        return std::nullopt;
    }

    void HandlePointerActivation(const float clientX, const float clientY) {
        if (state_.surface() == gba::Surface::Hidden || !window_) return;
        RECT client{};
        if (!GetClientRect(window_, &client)) return;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            client.right - client.left, client.bottom - client.top,
            dpi, interfaceScale);
        if (!metrics) return;
        const float x = clientX / metrics->physicalPixelsPerDip;
        const float y = clientY / metrics->physicalPixelsPerDip;

        if (state_.surface() == gba::Surface::Widget) {
            const std::wstring widget{state_.activeWidget()};
            const auto* snapshot = SnapshotFor(widget);
            if (snapshot) {
                const auto hit = gba::input::FindPointerHitTarget(
                    x, y, snapshot->activeInputScopeId, lastWidgetRenderResult_);
                if (hit) {
                    if (state_.focusRegion() == gba::FocusRegion::Tray) {
                        Dispatch(gba::Command::Activate);
                    }
                    (void)pressedInteraction_.Clear();
                    sliderInteraction_.DeactivateAll();
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
            surfaceGeometry = gba::ComputeOverlaySurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip,
                DesiredWidgetSurfaceTarget().panelWidthDip);
        }
        const auto traySlot = HitTraySlot(
            x, y, metrics->viewportWidthDip, metrics->viewportHeightDip,
            surfaceGeometry ? &*surfaceGeometry : nullptr);
        if (!traySlot) return;
        if (state_.surface() == gba::Surface::Widget &&
            state_.focusRegion() == gba::FocusRegion::Widget) {
            Dispatch(gba::Command::SampleWidgetBack);
        }
        if (state_.reorderMode()) Dispatch(gba::Command::Cancel);
        for (std::size_t remaining = state_.order().size();
             remaining > 0 && state_.selectedSlot() != *traySlot; --remaining) {
            Dispatch(gba::Command::NavigateRight);
        }
        if (state_.selectedSlot() == *traySlot) {
            Dispatch(gba::Command::Activate);
        }
    }

    void HandleKey(const UINT key, const bool repeated) {
        if (state_.surface() == gba::Surface::Hidden) return;
        const auto phase = repeated
            ? gba::input::NavigationEventPhase::Repeated
            : gba::input::NavigationEventPhase::Pressed;
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
                    gameInput_
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

    static WORD MapGameInputButtons(const GameInputGamepadButtons buttons) noexcept {
        const auto has = [buttons](const GameInputGamepadButtons button) {
            return (static_cast<unsigned>(buttons) & static_cast<unsigned>(button)) != 0;
        };
        WORD mapped = 0;
        if (has(GameInputGamepadDPadUp)) mapped |= XINPUT_GAMEPAD_DPAD_UP;
        if (has(GameInputGamepadDPadDown)) mapped |= XINPUT_GAMEPAD_DPAD_DOWN;
        if (has(GameInputGamepadDPadLeft)) mapped |= XINPUT_GAMEPAD_DPAD_LEFT;
        if (has(GameInputGamepadDPadRight)) mapped |= XINPUT_GAMEPAD_DPAD_RIGHT;
        if (has(GameInputGamepadMenu)) mapped |= XINPUT_GAMEPAD_START;
        if (has(GameInputGamepadView)) mapped |= XINPUT_GAMEPAD_BACK;
        if (has(GameInputGamepadLeftThumbstick)) mapped |= XINPUT_GAMEPAD_LEFT_THUMB;
        if (has(GameInputGamepadRightThumbstick)) mapped |= XINPUT_GAMEPAD_RIGHT_THUMB;
        if (has(GameInputGamepadLeftShoulder)) mapped |= XINPUT_GAMEPAD_LEFT_SHOULDER;
        if (has(GameInputGamepadRightShoulder)) mapped |= XINPUT_GAMEPAD_RIGHT_SHOULDER;
        if (has(GameInputGamepadA)) mapped |= XINPUT_GAMEPAD_A;
        if (has(GameInputGamepadB)) mapped |= XINPUT_GAMEPAD_B;
        if (has(GameInputGamepadX)) mapped |= XINPUT_GAMEPAD_X;
        if (has(GameInputGamepadY)) mapped |= XINPUT_GAMEPAD_Y;
        return mapped;
    }

    static SHORT MapGameInputThumbstick(const float value) noexcept {
        const float clamped = std::clamp(value, -1.0F, 1.0F);
        const float scale = clamped < 0.0F ? 32768.0F : 32767.0F;
        return static_cast<SHORT>(std::lround(clamped * scale));
    }

    static BYTE MapGameInputTrigger(const float value) noexcept {
        return static_cast<BYTE>(std::lround(
            std::clamp(value, 0.0F, 1.0F) * 255.0F));
    }

    [[nodiscard]] bool TryReadControllerState(XINPUT_STATE& controller) {
        const bool overlayVisible = state_.surface() != gba::Surface::Hidden &&
                                    window_ && IsWindowVisible(window_);
        const bool foregroundConfirmed = IsOverlayProcessForeground();
        const auto decision = gba::input::DecideControllerInputOwnership(
            overlayVisible, visibleControllerReadLease_, foregroundConfirmed,
            gameInput_.Get() != nullptr);

        if (!lastControllerReadPath_ || *lastControllerReadPath_ != decision.readPath ||
            !lastControllerForegroundExclusive_ ||
            *lastControllerForegroundExclusive_ != decision.foregroundExclusive) {
            lastControllerReadPath_ = decision.readPath;
            lastControllerForegroundExclusive_ = decision.foregroundExclusive;
            switch (decision.readPath) {
            case gba::input::ControllerReadPath::GameInputVisibleLease:
                AppendDiagnostic(
                    decision.foregroundExclusive
                        ? L"Controller read path: visible GameInput lease, foreground-exclusive"
                        : L"Controller read path: visible GameInput lease, background-shared");
                break;
            case gba::input::ControllerReadPath::XInputCompatibility:
                AppendDiagnostic(
                    L"Controller read path: XInput compatibility (not exclusive)");
                break;
            case gba::input::ControllerReadPath::None:
                AppendDiagnostic(L"Controller read path dormant: visible lease is inactive");
                break;
            }
        }

        controller = {};
        if (decision.readPath ==
            gba::input::ControllerReadPath::GameInputVisibleLease) {
            ComPtr<IGameInputReading> reading;
            const HRESULT result = gameInput_->GetCurrentReading(
                GameInputKindGamepad, nullptr, reading.ReleaseAndGetAddressOf());
            if (FAILED(result) || !reading) return false;
            GameInputGamepadState state{};
            if (!reading->GetGamepadState(&state)) return false;
            controller.Gamepad.wButtons = MapGameInputButtons(state.buttons);
            controller.Gamepad.bLeftTrigger = MapGameInputTrigger(state.leftTrigger);
            controller.Gamepad.bRightTrigger = MapGameInputTrigger(state.rightTrigger);
            controller.Gamepad.sThumbLX = MapGameInputThumbstick(state.leftThumbstickX);
            controller.Gamepad.sThumbLY = MapGameInputThumbstick(state.leftThumbstickY);
            controller.Gamepad.sThumbRX = MapGameInputThumbstick(state.rightThumbstickX);
            controller.Gamepad.sThumbRY = MapGameInputThumbstick(state.rightThumbstickY);
            return true;
        }

        if (decision.readPath == gba::input::ControllerReadPath::XInputCompatibility) {
            for (DWORD index = 0; index < XUSER_MAX_COUNT; ++index) {
                if (XInputGetState(index, &controller) == ERROR_SUCCESS) return true;
            }
        }
        return false;
    }

    void PrimeControllerState() {
        XINPUT_STATE controller{};
        const bool connected = TryReadControllerState(controller);
        previousButtons_ = connected ? controller.Gamepad.wButtons : 0;
        leftTriggerPressed_ = connected && controller.Gamepad.bLeftTrigger >= 30;
        rightTriggerPressed_ = connected && controller.Gamepad.bRightTrigger >= 30;
        stickNavigator_.Prime(
            connected ? controller.Gamepad.sThumbLX : 0,
            connected ? controller.Gamepad.sThumbLY : 0,
            GetTickCount64());
        const WORD buttons = connected ? controller.Gamepad.wButtons : 0;
        dpadNavigator_.Prime(
            gba::input::DigitalNavigationAxis(
                buttons, XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_RIGHT),
            gba::input::DigitalNavigationAxis(
                buttons, XINPUT_GAMEPAD_DPAD_DOWN, XINPUT_GAMEPAD_DPAD_UP),
            GetTickCount64());
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
        XINPUT_STATE controller{};
        const bool connected = TryReadControllerState(controller);
        const WORD buttons = connected ? controller.Gamepad.wButtons : 0;
        const WORD pressed = static_cast<WORD>(buttons & ~previousButtons_);
        const WORD released = static_cast<WORD>(previousButtons_ & ~buttons);
        previousButtons_ = buttons;
        constexpr WORD recoveryChord = XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
        const bool recoveryChordDown = (buttons & recoveryChord) == recoveryChord;
        if (recoveryChordDown && !reloadChordHeld_) RestartCurrentWidget();
        reloadChordHeld_ = recoveryChordDown;

        const auto releaseButton = [&](const WORD mask, const std::wstring_view protocolButton) {
            if ((released & mask) != 0 && pressedInteraction_.Release(protocolButton))
                InvalidateRect(window_, nullptr, FALSE);
        };
        releaseButton(XINPUT_GAMEPAD_A, L"a");
        releaseButton(XINPUT_GAMEPAD_B, L"b");
        releaseButton(XINPUT_GAMEPAD_X, L"x");
        releaseButton(XINPUT_GAMEPAD_Y, L"y");
        releaseButton(XINPUT_GAMEPAD_LEFT_SHOULDER, L"leftBumper");
        releaseButton(XINPUT_GAMEPAD_RIGHT_SHOULDER, L"rightBumper");
        releaseButton(XINPUT_GAMEPAD_LEFT_THUMB, L"leftStick");
        releaseButton(XINPUT_GAMEPAD_RIGHT_THUMB, L"rightStick");
        releaseButton(XINPUT_GAMEPAD_BACK, L"view");
        releaseButton(XINPUT_GAMEPAD_START, L"menu");

        const ULONGLONG now = GetTickCount64();
        if (sliderReconcileAt_ != 0 && now >= sliderReconcileAt_) {
            sliderReconcileAt_ = 0;
            InvalidateRect(window_, nullptr, FALSE);
        }
        if (const auto direction = stickNavigator_.UpdateEvent(
                connected ? controller.Gamepad.sThumbLX : 0,
                connected ? controller.Gamepad.sThumbLY : 0,
                now)) {
            DispatchStickNavigation(*direction);
        }
        if (const auto direction = dpadNavigator_.UpdateEvent(
                gba::input::DigitalNavigationAxis(
                    buttons, XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_RIGHT),
                gba::input::DigitalNavigationAxis(
                    buttons, XINPUT_GAMEPAD_DPAD_DOWN, XINPUT_GAMEPAD_DPAD_UP),
                now)) {
            DispatchStickNavigation(*direction);
        }

        if (pressed & XINPUT_GAMEPAD_A) {
            DispatchControllerAction(L"A", true);
        }
        if (pressed & XINPUT_GAMEPAD_B) {
            DispatchControllerAction(L"B", true);
        }
        if (pressed & XINPUT_GAMEPAD_Y) {
            DispatchControllerAction(L"Y", true);
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
        const bool leftTriggerPressed = connected && controller.Gamepad.bLeftTrigger >= 30;
        const bool rightTriggerPressed = connected && controller.Gamepad.bRightTrigger >= 30;
        if (leftTriggerPressed && !leftTriggerPressed_) {
            DispatchControllerAction(L"LT", true);
        }
        if (rightTriggerPressed && !rightTriggerPressed_) {
            DispatchControllerAction(L"RT", true);
        }
        if (!leftTriggerPressed && leftTriggerPressed_ &&
            pressedInteraction_.Release(L"leftTrigger"))
            InvalidateRect(window_, nullptr, FALSE);
        if (!rightTriggerPressed && rightTriggerPressed_ &&
            pressedInteraction_.Release(L"rightTrigger"))
            InvalidateRect(window_, nullptr, FALSE);
        leftTriggerPressed_ = leftTriggerPressed;
        rightTriggerPressed_ = rightTriggerPressed;
        // The visible overlay already polls controller state at 60 Hz. Reuse
        // that bounded wakeup rather than owning an animation timer; settled
        // declarative content performs no paint invalidations, and this timer
        // is stopped altogether while the overlay is hidden.
        if (declarativeMotionActive_)
            InvalidateRect(window_, nullptr, FALSE);
    }

    const gba::WidgetSnapshot* SnapshotFor(const std::wstring_view widgetId) const noexcept {
        const auto snapshot = widgetSnapshots_.find(std::wstring(widgetId));
        return snapshot == widgetSnapshots_.end() ? nullptr : &snapshot->second;
    }

    void RememberCurrentFocus(const std::wstring_view widgetId) {
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot || focusedElementId_.empty()) return;
        focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
    }

    void RestoreFocusForActiveSurface(const std::wstring_view widgetId) {
        const auto* snapshot = SnapshotFor(widgetId);
        focusedElementId_ = snapshot
            ? focusMemory_.Restore(widgetId, *snapshot)
            : std::wstring{};
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
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget) return;
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot) return;
        const auto visible = gba::input::ResolveVisibleFocusTarget(
            focusedElementId_, snapshot->activeInputScopeId, lastWidgetRenderResult_);
        if (!visible) return;
        if (*visible != focusedElementId_) {
            focusedElementId_ = *visible;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
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

    void RefreshCurrentBridgeSnapshot() {
        if (state_.surface() == gba::Surface::Hidden) return;
        const std::wstring_view widgetId = state_.surface() == gba::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (IsBridgeWidget(widgetId)) RefreshWidgetSnapshot(widgetId);
    }

    void RestartCurrentWidget() {
        if (state_.surface() != gba::Surface::Widget) return;
        const std::wstring widgetId{state_.activeWidget()};
        if (!IsBridgeWidget(widgetId)) return;
        if (!bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) +
                                 L" reload failed: " + bridge_.lastError();
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        const auto descriptor = std::find_if(
            widgetDescriptors_.begin(), widgetDescriptors_.end(),
            [&](const gba::WidgetDescriptor& candidate) { return candidate.id == widgetId; });
        if (descriptor != widgetDescriptors_.end()) {
            if (declarativeRenderer_ && !descriptor->instanceId.empty())
                declarativeRenderer_->ForgetWidgetState(descriptor->instanceId);
            sliderInteraction_.ForgetWidget(descriptor->instanceId);
        }
        (void)pressedInteraction_.Clear();
        focusMemory_.Forget(widgetId);
        focusedElementId_.clear();
        widgetSnapshots_.erase(widgetId);
        renderedSnapshotSequences_.erase(widgetId);
        pendingContentRevealWidget_ = widgetId;
        overlayTransition_.SnapContentVisible();

        const auto restarted = bridge_.RestartWidget(widgetId);
        if (!restarted || !*restarted) {
            pendingContentRevealWidget_.clear();
            lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) +
                                 L" reload failed: " + bridge_.lastError();
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) + L" reloaded";
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(lastActionMessage_);
        RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widgetId); });
        InvalidateRect(window_, nullptr, FALSE);
    }

    void RefreshWidgetSnapshot(const std::wstring_view widgetId) {
        if (!IsBridgeWidget(widgetId)) return;
        if (!bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) +
                                 L" unavailable: " + bridge_.lastError();
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            return;
        }
        auto snapshot = bridge_.GetSnapshot(widgetId);
        if (!snapshot) {
            lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) +
                                 L" failed: " + bridge_.lastError();
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            return;
        }
        const auto descriptor = std::find_if(
            widgetDescriptors_.begin(), widgetDescriptors_.end(),
            [widgetId](const gba::WidgetDescriptor& candidate) {
                return candidate.id == widgetId;
            });
        if (descriptor == widgetDescriptors_.end() || snapshot->instanceId != descriptor->instanceId) {
            lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) +
                                 L" returned a mismatched widget instance";
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            return;
        }
        const auto currentWidget = state_.surface() == gba::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (currentWidget == widgetId) RememberCurrentFocus(widgetId);
        widgetSnapshots_.insert_or_assign(std::wstring(widgetId), std::move(*snapshot));
        if (currentWidget == widgetId &&
            pendingContentRevealWidget_ == widgetId) {
            pendingContentRevealWidget_.clear();
            overlayTransition_.BeginContentReveal(
                GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
            AdvanceOverlayTransition(GetTickCount64());
        }
        if (currentWidget == widgetId) RestoreFocusForActiveSurface(widgetId);
        if (const auto* current = SnapshotFor(widgetId);
            currentWidget == widgetId && current &&
            pressedInteraction_.Reconcile(*current, focusedElementId_)) {
            InvalidateRect(window_, nullptr, FALSE);
        }
    }

    void MoveWidgetFocus(const std::wstring_view direction) {
        if (state_.surface() != gba::Surface::Widget ||
            state_.focusRegion() != gba::FocusRegion::Widget ||
            focusedElementId_.empty()) {
            return;
        }
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot) return;
        const auto activeScope = std::wstring_view(snapshot->activeInputScopeId);
        const auto visibleFocus = gba::input::ResolveVisibleFocusTarget(
            focusedElementId_, activeScope, lastWidgetRenderResult_);
        if (!visibleFocus) return;
        if (*visibleFocus != focusedElementId_) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            focusedElementId_ = *visibleFocus;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        const auto* focused = gba::input::FindNodeInInputScope(
            *snapshot, focusedElementId_, activeScope);
        if (!focused) return;
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
        gba::input::NavigationDirection navigationDirection =
            gba::input::NavigationDirection::None;
        if (direction == L"left") navigationDirection = gba::input::NavigationDirection::Left;
        else if (direction == L"right") navigationDirection = gba::input::NavigationDirection::Right;
        else if (direction == L"up") navigationDirection = gba::input::NavigationDirection::Up;
        else if (direction == L"down") navigationDirection = gba::input::NavigationDirection::Down;
        if (explicitMoves) {
            sliderInteraction_.DeactivateAll();
            (void)pressedInteraction_.Clear();
            focusedElementId_ = explicitTarget->id;
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
            focusedElementId_ = *fallback;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            DispatchScrollPagination(widgetId, *snapshot, navigationDirection);
            return;
        }
        if (DispatchScrollPagination(widgetId, *snapshot, navigationDirection))
            return;
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
        const auto* snapshot = SnapshotFor(widget);
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
            if (!SnapshotFor(widget)) {
                RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widget); });
            }
            const auto protocolButton = ProtocolButton(button);
            const auto* snapshot = SnapshotFor(widget);
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
                requestedValue);
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
                snapshot = SnapshotFor(widget);
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

        lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                             L": forwarded " + std::wstring(button);
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(L"Widget action " + lastActionMessage_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    bool EnsureGraphicsResources() {
        if (renderTarget_) {
            return true;
        }
        RECT client{};
        GetClientRect(window_, &client);
        const auto size = D2D1::SizeU(
            static_cast<UINT32>(client.right - client.left),
            static_cast<UINT32>(client.bottom - client.top));
        if (FAILED(d2dFactory_->CreateHwndRenderTarget(
                D2D1::RenderTargetProperties(),
                D2D1::HwndRenderTargetProperties(window_, size),
                renderTarget_.ReleaseAndGetAddressOf()))) {
            return false;
        }
        const float windowDpi = static_cast<float>(GetDpiForWindow(window_));
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
        return backgroundBrush_ && cardBrush_ && textBrush_ && secondaryBrush_ &&
               dashboardTextBrush_ && dashboardSecondaryBrush_ && trayItemTextBrush_ &&
               accentBrush_ && successBrush_ && titleFormat_ && bodyFormat_ &&
               hintFormat_ && iconFormat_ && trayItemBrush_ && selectedTextBrush_ &&
               focusBrush_;
    }

    void DiscardGraphicsResources() {
        if (declarativeRenderer_) declarativeRenderer_->DiscardTargetResources();
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
        renderTarget_.Reset();
    }

    void DrawTextLine(std::wstring_view text,
                      IDWriteTextFormat* format,
                      const D2D1_RECT_F& rectangle,
                      ID2D1Brush* brush) const {
        renderTarget_->DrawTextW(text.data(), static_cast<UINT32>(text.size()), format,
                                 rectangle, brush, D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }

    void Paint() {
        PAINTSTRUCT paint{};
        BeginPaint(window_, &paint);
        if (state_.surface() == gba::Surface::Hidden || !EnsureGraphicsResources()) {
            declarativeMotionActive_ = false;
            EndPaint(window_, &paint);
            return;
        }

        RECT client{};
        GetClientRect(window_, &client);
        float dpiX = 96.0F;
        float dpiY = 96.0F;
        renderTarget_->GetDpi(&dpiX, &dpiY);
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = gba::ComputeOverlayRenderMetrics(
            client.right - client.left,
            client.bottom - client.top,
            dpiX > 0 ? static_cast<UINT>(std::lround(dpiX)) : 96U,
            interfaceScale);
        if (!metrics) {
            EndPaint(window_, &paint);
            return;
        }
        renderTarget_->BeginDraw();
        // The HWND is a color-keyed layered window above a separately dimmed
        // full-screen backdrop. Only the authored panel/tray surfaces belong
        // to this window; painting the theme canvas across the client creates
        // an opaque rectangular box around those content-shaped surfaces.
        // Keep the canvas color for style inheritance/contrast, but clear the
        // unused client area to the exact transparency key.
        renderTarget_->Clear(D2DColor(kSafeCanvasFallback));
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Scale(
            metrics->interfaceScale, metrics->interfaceScale));

        if (state_.surface() == gba::Surface::Widget) {
            DrawWidget(metrics->viewportWidthDip, metrics->viewportHeightDip,
                       metrics->physicalPixelsPerDip);
        } else {
            declarativeMotionActive_ = false;
            DrawDashboard(metrics->viewportWidthDip, metrics->viewportHeightDip);
        }
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());

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
        const gba::OverlaySurfaceGeometry* surfaceGeometry = nullptr) {
        if (state_.order().empty() || width <= 0.0F || height <= 0.0F) return;
        constexpr float preferredTileSize = 64.0F;
        constexpr float gap = 14.0F;
        const float stripTop = surfaceGeometry
            ? surfaceGeometry->trayY
            : std::max(0.0F, height - 112.0F);
        const float stripBottom = surfaceGeometry
            ? surfaceGeometry->trayY + surfaceGeometry->trayHeight
            : std::max(stripTop, height - 14.0F);
        const float stripHeight = stripBottom - stripTop;
        if (stripHeight <= 0.0F) return;
        const float verticalPadding = std::min(
            16.0F, std::max(0.0F, (stripHeight - preferredTileSize) * 0.5F));
        const float horizontalPadding = std::min(14.0F, width * 0.15F);
        const float tileSize = std::max(
            1.0F, std::min({preferredTileSize,
                            width - horizontalPadding * 2.0F,
                            stripHeight - verticalPadding * 2.0F}));
        const float stripPadding = std::min(
            14.0F, std::max(0.0F, (width - tileSize) * 0.5F));
        const auto maximumVisible = static_cast<std::size_t>(std::max(
            1.0F, std::floor((width - horizontalPadding * 2.0F + gap) /
                             (preferredTileSize + gap))));
        const std::size_t visibleCount = std::min(state_.order().size(), maximumVisible);
        const std::size_t half = visibleCount / 2;
        const std::size_t maximumFirst = state_.order().size() - visibleCount;
        const std::size_t firstSlot = std::min(
            state_.selectedSlot() > half ? state_.selectedSlot() - half : 0U,
            maximumFirst);
        const float stripWidth = tileSize * static_cast<float>(visibleCount) +
                                 gap * static_cast<float>(visibleCount - 1) +
                                 stripPadding * 2.0F;
        const float stripLeft = (width - stripWidth) / 2.0F;
        const D2D1_ROUNDED_RECT strip{
            D2D1::RectF(stripLeft, stripTop, stripLeft + stripWidth, stripBottom),
            trayCornerRadius_, trayCornerRadius_};
        renderTarget_->FillRoundedRectangle(strip, backgroundBrush_.Get());

        for (std::size_t visibleIndex = 0; visibleIndex < visibleCount; ++visibleIndex) {
            const std::size_t slot = firstSlot + visibleIndex;
            const float x = stripLeft + stripPadding +
                            static_cast<float>(visibleIndex) * (tileSize + gap);
            const float top = stripTop + verticalPadding;
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
                if (state_.focusRegion() == gba::FocusRegion::Tray) {
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

    std::wstring DashboardHint(const float availableWidth) const {
        if (lastActionExpiresAt_ > GetTickCount64() && !lastActionMessage_.empty()) {
            return lastActionMessage_;
        }
        std::vector<gba::ControllerGuideAction> quickActions;
        const auto* snapshot = SnapshotFor(state_.selectedWidget());
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
            state_.reorderMode(), quickActions);
    }

    void DrawDashboard(const float width, const float height) {
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
        DrawTextLine(title, titleFormat_.Get(),
                     D2D1::RectF(contentLeft, titleTop, contentRight, titleBottom),
                     dashboardTextBrush_.Get());
        DrawIconStrip(width, height);

        const std::wstring hint = DashboardHint(contentRight - contentLeft);
        DrawTextLine(hint, hintFormat_.Get(),
                     D2D1::RectF(contentLeft, hintTop, contentRight, hintBottom),
                     dashboardSecondaryBrush_.Get());
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

    std::wstring OpenWidgetPrompt() const {
        const auto* snapshot = SnapshotFor(state_.activeWidget());
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

    void DrawWidgetFooter(const gba::OverlaySurfaceGeometry& geometry) {
        if (geometry.footerHeight <= 0.0F || geometry.panelWidth <= 0.0F) return;
        const float panelLeft = geometry.panelX;
        const float panelRight = geometry.panelX + geometry.panelWidth;
        const float panelBottom = geometry.panelY + geometry.panelHeight;
        const float horizontalInset = std::min(30.0F, geometry.panelWidth * 0.1F);
        const float contentLeft = panelLeft + horizontalInset;
        const float contentRight = std::max(contentLeft, panelRight - horizontalInset);
        const float textTop = geometry.footerY +
            std::min(15.0F, geometry.footerHeight * 0.35F);
        const float textBottom = panelBottom -
            std::min(12.0F, geometry.footerHeight * 0.25F);
        if (textBottom <= textTop + 1.0F) return;
        if (state_.focusRegion() == gba::FocusRegion::Tray) {
            DrawTextLine(DashboardHint(contentRight - contentLeft), hintFormat_.Get(),
                         D2D1::RectF(contentLeft, textTop, contentRight, textBottom),
                         dashboardSecondaryBrush_.Get());
            return;
        }
        const std::wstring prompt = OpenWidgetPrompt();
        const auto* snapshot = SnapshotFor(state_.activeWidget());
        const bool rootScope = snapshot &&
            std::wstring_view(snapshot->activeInputScopeId) ==
                gba::input::RootInputScope(*snapshot);
        const std::wstring hostPrompt = rootScope
            ? L"B  Back     Guide  Close"
            : L"Guide  Close";
        if (contentRight - contentLeft >= 300.0F) {
            const float hostPromptWidth = rootScope ? 180.0F : 106.0F;
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
            DrawTextLine(hostPrompt, hintFormat_.Get(),
                         D2D1::RectF(contentLeft, textTop, contentRight, textBottom),
                         secondaryBrush_.Get());
        }
    }

    void DrawWidget(
        const float width,
        const float height,
        const float physicalPixelsPerDip) {
        declarativeMotionActive_ = false;
        const std::wstring_view widget = state_.activeWidget();
        const bool bridgeWidget = IsBridgeWidget(widget);
        const auto widgetSurface = DesiredWidgetSurfaceTarget();
        const auto geometry = gba::ComputeOverlaySurfaceGeometry(
            width, height, bridgeWidget ? widgetSurface.panelWidthDip : 720.0F);
        if (!geometry) return;
        const float panelLeft = geometry->panelX;
        const float panelTop = geometry->panelY;
        const float panelWidth = geometry->panelWidth;
        // The footer is host chrome, not widget content. End the card at the
        // content boundary so both widget and tray guides read as a detached
        // shell layer and never masquerade as part of a third-party widget.
        const float visualPanelBottom = geometry->footerY;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(panelLeft, panelTop, panelLeft + panelWidth, visualPanelBottom),
            panelCornerRadius_, panelCornerRadius_};
        renderTarget_->FillRoundedRectangle(panel, cardBrush_.Get());

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
            const auto* snapshot = SnapshotFor(widget);
            if (snapshot && declarativeRenderer_) {
                const gba::declarative::Rect viewport{
                    geometry->widgetViewportX,
                    geometry->widgetViewportY,
                    geometry->widgetViewportWidth,
                    geometry->widgetViewportHeight,
                };
                gba::DeclarativeRenderOptions options;
                options.pixelScale = physicalPixelsPerDip;
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
                    options.accessibility = CurrentAccessibilityPolicy();
                options.animationTimestampMilliseconds = GetTickCount64();
                sliderInteraction_.RetainAdjustmentMode(
                    snapshot->instanceId,
                    snapshot->activeInputScopeId,
                    state_.focusRegion() == gba::FocusRegion::Widget
                        ? std::wstring_view(focusedElementId_)
                        : std::wstring_view{});
                options.pressedElementId = pressedInteraction_.ActiveElementId(
                    *snapshot,
                    state_.focusRegion() == gba::FocusRegion::Widget
                        ? std::wstring_view(focusedElementId_)
                        : std::wstring_view{});
                if (options.pressedElementId.empty() &&
                    state_.focusRegion() == gba::FocusRegion::Widget) {
                    if (const auto* focused = gba::input::FindNodeInInputScope(
                            *snapshot, focusedElementId_, snapshot->activeInputScopeId);
                        focused && focused->sliderInteractionMode == L"activateToAdjust" &&
                        sliderInteraction_.AdjustmentModeActive(
                            SliderDescriptor(*snapshot, *focused), GetTickCount64())) {
                        options.pressedElementId = focused->id;
                    }
                }
                const auto collectSliderOverrides = [&](const auto& self,
                                                        const gba::WidgetNode& node) -> void {
                    if (node.kind == L"slider") {
                        if (const auto value = sliderInteraction_.PresentationValue(
                                SliderDescriptor(*snapshot, node), GetTickCount64())) {
                            options.sliderValueOverrides.emplace(node.id, *value);
                        }
                    }
                    for (const auto& child : node.children) self(self, child);
                };
                collectSliderOverrides(collectSliderOverrides, snapshot->root);
                auto result = declarativeRenderer_->Render(
                    renderTarget_.Get(), *snapshot,
                    state_.focusRegion() == gba::FocusRegion::Widget
                        ? std::wstring_view(focusedElementId_)
                        : std::wstring_view{},
                    viewport, options);
                declarativeMotionActive_ = result.animationActive;
                if (const auto visibleFocus = gba::input::ResolveVisibleFocusTarget(
                        focusedElementId_, snapshot->activeInputScopeId, result);
                    visibleFocus && *visibleFocus != focusedElementId_) {
                    (void)pressedInteraction_.Clear();
                    focusedElementId_ = *visibleFocus;
                    focusMemory_.Remember(widget, *snapshot, focusedElementId_);
                    // The completed pass used the old focus state. Schedule one
                    // more paint so the recovered target receives its ring.
                    InvalidateRect(window_, nullptr, FALSE);
                }
                lastWidgetRenderResult_ = result;
                const auto lastSequence = renderedSnapshotSequences_.find(std::wstring(widget));
                if (lastSequence == renderedSnapshotSequences_.end() ||
                    lastSequence->second != snapshot->sequence) {
                    for (const auto& diagnostic : result.diagnostics) {
                        AppendDiagnostic(
                            L"Renderer " + std::wstring(widget) + L" " + diagnostic.code + L" [" + diagnostic.nodeId +
                            L"] " + diagnostic.message);
                    }
                    renderedSnapshotSequences_.insert_or_assign(
                        std::wstring(widget), snapshot->sequence);
                }
            } else {
                DrawTextLine(L"Starting isolated " + std::wstring(DisplayWidgetName(widget)) +
                                 L" widget…",
                             bodyFormat_.Get(),
                             D2D1::RectF(panelLeft + 30, 52,
                                         panelLeft + panelWidth - 30, 110),
                             secondaryBrush_.Get());
            }
            if (contentLayerPushed) renderTarget_->PopLayer();
            DrawWidgetFooter(*geometry);
            DrawIconStrip(width, height, &*geometry);
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
        DrawWidgetFooter(*geometry);
        DrawIconStrip(width, height, &*geometry);
    }

    HINSTANCE instance_{};
    HWND window_{};
    gba::ForegroundTargetTracker foregroundTarget_;
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
    bool performanceCountersActive_{};
    unsigned long long performanceCounterStarted_{};
    unsigned long long performanceTimerMessages_{};
    unsigned long long performanceControllerTimerMessages_{};
    unsigned long long performanceGuideCompatibilityTimerMessages_{};
    unsigned long long performancePaintMessages_{};
    unsigned long long performanceSuccessfulFrames_{};
    gba::OverlayState state_;
    WORD previousButtons_{};
    gba::input::StickNavigator stickNavigator_;
    gba::input::StickNavigator dpadNavigator_{
        gba::input::StickNavigationOptions{1, 0, 360, 125}};
    bool leftTriggerPressed_{};
    bool rightTriggerPressed_{};
    bool reloadChordHeld_{};
    bool visibleControllerReadLease_{};
    std::optional<gba::input::ControllerReadPath> lastControllerReadPath_;
    std::optional<bool> lastControllerForegroundExclusive_;
    std::optional<bool> lastForegroundOwnership_;
    long long controllerSequence_{};
    std::wstring lastActionMessage_;
    ULONGLONG lastActionExpiresAt_{};
    ULONGLONG lastGuideDispatchAt_{};
    ULONGLONG sliderReconcileAt_{};
    std::wstring focusedElementId_;
    gba::input::WidgetSurfaceFocusMemory focusMemory_;
    gba::input::SliderInteractionState sliderInteraction_;
    gba::input::PressedInteractionState pressedInteraction_;
    std::unordered_map<std::wstring, gba::WidgetSnapshot> widgetSnapshots_;
    gba::WidgetBridgeClient bridge_;
    unsigned int catalogRetryAttempts_{};
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
    std::vector<gba::WidgetDescriptor> widgetDescriptors_;
    std::wstring lifecycleBridgeWidget_;
    std::optional<gba::WidgetLifecycleState> lifecycleBridgeState_;
    std::unique_ptr<gba::RemoteImageCache> imageCache_;
    std::unique_ptr<gba::DeclarativeRenderer> declarativeRenderer_;
    bool runtimeInitialized_{};
    std::unordered_map<std::wstring, long long> renderedSnapshotSequences_;
    gba::RenderResult lastWidgetRenderResult_;
    bool declarativeMotionActive_{};
    gba::OverlayTransitionTimeline overlayTransition_;
    gba::OverlayTransitionSample overlayTransitionSample_{};
    bool awaitingSuccessfulOpenPaint_{};
    ULONGLONG nextOpenPaintRetryAt_{};
    std::wstring pendingContentRevealWidget_;
    BYTE targetOverlayOpacity_{248};
    BYTE targetBackdropOpacity_{kBackdropOpacity};
    std::optional<BYTE> appliedOverlayOpacity_;
    std::optional<BYTE> appliedBackdropOpacity_;

    ComPtr<IGameInput> gameInput_;
    gba::input::XInputGuideCompatibility guideCompatibility_;
    gba::input::GuideCompatibilityActivation guideCompatibilityActivation_;
    std::mutex guideCompatibilityDeviceChangesMutex_;
    std::vector<GuideCompatibilityDeviceChange> guideCompatibilityDeviceChanges_;
    bool compatibilityDeviceTrackingAvailable_{};
    GameInputCallbackToken guideCallback_{};
    GameInputCallbackToken guideCompatibilityDeviceCallback_{};
    ComPtr<ID2D1Factory> d2dFactory_;
    ComPtr<IDWriteFactory> writeFactory_;
    ComPtr<ID2D1HwndRenderTarget> renderTarget_;
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
    OverlayApp app;
    if (!app.Initialize(instance, showCommand)) {
        const std::wstring message = L"OverlayHost failed to initialize.\n\n" +
                                     app.initializationError();
        SaveStartupError(message);
        MessageBoxW(nullptr, message.c_str(),
                    L"Game Bar Alternative", MB_OK | MB_ICONERROR);
        return EXIT_FAILURE;
    }
    return app.Run();
}
