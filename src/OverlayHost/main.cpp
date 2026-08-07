#include "OverlayState.h"
#include "DeclarativeRenderer.h"
#include "ControllerNavigation.h"
#include "GuideInputCompatibility.h"
#include "FocusNavigation.h"
#include "NativeIcons.h"
#include "NativeStyle.h"
#include "OverlayPlacement.h"
#include "OverlayTargeting.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"
#include "WidgetLifecycle.h"
#include "WidgetSurfaceFocus.h"

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
#include <filesystem>
#include <fstream>
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
constexpr UINT kGuideMessage = WM_APP + 1;
constexpr UINT kImageReadyMessage = WM_APP + 2;
constexpr UINT kCatalogRefreshMessage = WM_APP + 3;
constexpr UINT kSnapshotRefreshMessage = WM_APP + 4;
constexpr UINT kForegroundChangedMessage = WM_APP + 5;
constexpr UINT kPlacementRefreshMessage = WM_APP + 6;
constexpr BYTE kBackdropOpacity = 164;
constexpr int kDeveloperHotkey = 1;

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
    std::wstring_view detail;
    gba::icons::NativeIcon icon;
};

constexpr std::array<BuiltInWidget, 3> kBuiltInWidgets{{
    {L"audio-mixer", L"Audio Mixer", L"Sessions, output, and microphone",
     gba::icons::NativeIcon::Connection},
    {L"yt-music", L"YT Music", L"Media controls and progress",
     gba::icons::NativeIcon::Music},
    {L"performance", L"Performance", L"Frame rate and system load",
     gba::icons::NativeIcon::Warning},
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

std::wstring_view WidgetDetail(const std::wstring_view id) noexcept {
    const auto* widget = FindBuiltInWidget(id);
    return widget ? widget->detail : L"Community widget";
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
    OverlayApp() : state_(LoadPersistentState()) {}
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
        const HRESULT runtimeResult = RoInitialize(RO_INIT_SINGLETHREADED);
        if (FAILED(runtimeResult) && runtimeResult != RPC_E_CHANGED_MODE) {
            return FailHresult(L"RoInitialize", runtimeResult);
        }
        runtimeInitialized_ = SUCCEEDED(runtimeResult);
        SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);

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

        RegisterHotKey(window_, kDeveloperHotkey, MOD_NOREPEAT, VK_F1);
        imageCache_ = std::make_unique<gba::RemoteImageCache>(
            gba::RemoteImageLimits{},
            [this](std::wstring_view, gba::RemoteImageState) {
                if (window_) PostMessageW(window_, kImageReadyMessage, 0, 0);
            });
        declarativeRenderer_ = std::make_unique<gba::DeclarativeRenderer>(
            d2dFactory_.Get(), writeFactory_.Get(), imageCache_.get());
        InitializeGameInput();
        if (guideCompatibility_.Initialize()) {
            SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
            AppendDiagnostic(L"XInput Guide compatibility adapter available");
        } else {
            AppendDiagnostic(L"XInput Guide compatibility adapter unavailable");
        }

        // Platform appearance is bridge-owned but does not cross the lazy
        // widget-worker boundary. Fetch it once at host startup, then only in
        // response to a revision event.
        if (bridge_.EnsureStarted(installationDirectory_)) {
            RefreshPlatformAppearance();
        } else {
            AppendDiagnostic(L"Platform appearance unavailable at startup: " +
                             bridge_.lastError());
        }

        (void)showCommand;
        bool startShown = false;
        for (int i = 1; i < __argc; ++i) {
            if (_wcsicmp(__wargv[i], L"--show") == 0) {
                startShown = true;
            } else if (_wcsicmp(__wargv[i], L"--hidden") == 0) {
                startShown = false;
            }
        }
        if (startShown) {
            Dispatch(gba::Command::ToggleOverlay);
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
        case kImageReadyMessage:
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kCatalogRefreshMessage:
            if (RefreshWidgetCatalog()) {
                KillTimer(window_, kCatalogRetryTimer);
                catalogRetryAttempts_ = 0;
                RefreshCurrentBridgeSnapshot();
                InvalidateRect(window_, nullptr, FALSE);
            } else if (bridge_.HasWidgetCatalogChangedRevisionInFlight() &&
                       state_.surface() != gba::Surface::Hidden &&
                       catalogRetryAttempts_ < 3) {
                const UINT delay = 250U << catalogRetryAttempts_++;
                SetTimer(window_, kCatalogRetryTimer, delay, nullptr);
            } else {
                bridge_.AbandonWidgetCatalogChangedRevision();
                catalogRetryAttempts_ = 0;
            }
            return 0;
        case kSnapshotRefreshMessage:
            RefreshCurrentBridgeSnapshot();
            if (state_.surface() != gba::Surface::Hidden) {
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case kForegroundChangedMessage:
            if (state_.surface() != gba::Surface::Hidden) {
                const HWND foreground = reinterpret_cast<HWND>(lParam);
                if (foregroundTarget_.Observe(
                        reinterpret_cast<std::uintptr_t>(foreground),
                        foreground && IsWindow(foreground))) {
                    // A visible overlay follows the newly foregrounded app to
                    // its monitor. This also recomputes work-area and DPI data
                    // rather than merely restoring topmost z-order in place.
                    ShowOverlay();
                    InvalidateRect(window_, nullptr, FALSE);
                }
                SetTimer(window_, kZOrderSettleTimer, 80, nullptr);
            }
            return 0;
        case kPlacementRefreshMessage:
            if (state_.surface() != gba::Surface::Hidden) {
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case WM_KEYDOWN:
            if ((lParam & (1LL << 30)) == 0) {
                HandleKey(static_cast<UINT>(wParam));
            }
            return 0;
        case WM_TIMER:
            if (wParam == kControllerTimer) {
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
                        const int priorHeight = DesiredHeightDip();
                        RefreshWidgetSnapshot(invalidatedWidget);
                        // Repaint-only invalidations must not churn HWND
                        // placement. Resize only when the generic surface size
                        // class changes.
                        if (DesiredHeightDip() != priorHeight) ShowOverlay();
                        InvalidateRect(window_, nullptr, FALSE);
                    } else {
                        // Preserve the event's widget identity. An offscreen
                        // cache is invalidated and will be fetched on selection.
                        widgetSnapshots_.erase(invalidatedWidget);
                        renderedSnapshotSequences_.erase(invalidatedWidget);
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
            }
            return 0;
        case WM_ACTIVATEAPP:
            if (state_.surface() != gba::Surface::Hidden) {
                SetTimer(window_, kZOrderSettleTimer, 80, nullptr);
            }
            return 0;
        case WM_PAINT:
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
            DiscardGraphicsResources();
            if (state_.surface() != gba::Surface::Hidden) {
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case WM_DISPLAYCHANGE:
            if (state_.surface() != gba::Surface::Hidden) {
                // Display topology, taskbar work area, and accessibility
                // settings may change without a DPI transition. Recreate the
                // target so viewport-relative shell styles use fresh metrics.
                DiscardGraphicsResources();
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case WM_SETTINGCHANGE:
            if (state_.surface() != gba::Surface::Hidden) {
                // A work-area change will produce WM_SIZE and recreate target
                // resources only when geometry actually changed.
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
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
            gameInput_.Reset();
            guideCallback_ = 0;
        } else {
            AppendDiagnostic(L"GameInput Guide callback registered with background+foreground-exclusive policy");
        }
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
        gameInput_.Reset();
        if (runtimeInitialized_) {
            RoUninitialize();
            runtimeInitialized_ = false;
        }
    }

    void Dispatch(const gba::Command command) {
        const auto priorSurface = state_.surface();
        const std::wstring priorSelected(state_.selectedWidget());
        const std::wstring priorActive(state_.activeWidget());
        if (priorSurface == gba::Surface::Widget && IsBridgeWidget(priorActive)) {
            RememberCurrentFocus(priorActive);
        }
        const auto before = state_.persistent();
        if (!state_.Dispatch(command)) {
            return;
        }
        if (before.order != state_.persistent().order ||
            before.lastWidget != state_.persistent().lastWidget ||
            before.reopenWidget != state_.persistent().reopenWidget) {
            SavePersistentState(state_.persistent());
        }
        SyncWidgetActivity();
        if (priorSurface != state_.surface() || priorActive != state_.activeWidget()) {
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
        }

        if (state_.surface() == gba::Surface::Hidden) {
            HideOverlay();
        } else {
            const bool enteredBridgeWidget =
                state_.surface() == gba::Surface::Widget && IsBridgeWidget(state_.activeWidget()) &&
                (priorSurface != gba::Surface::Widget || priorActive != state_.activeWidget());
            const bool hoveredBridgeWidget =
                state_.surface() == gba::Surface::Dashboard && IsBridgeWidget(state_.selectedWidget()) &&
                (priorSurface != gba::Surface::Dashboard || priorSelected != state_.selectedWidget());
            ShowOverlay();
            InvalidateRect(window_, nullptr, FALSE);
            if (priorSurface == gba::Surface::Hidden) {
                PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
            }
            if (priorSurface != gba::Surface::Hidden &&
                (enteredBridgeWidget || hoveredBridgeWidget)) {
                PostMessageW(window_, kSnapshotRefreshMessage, 0, 0);
            }
        }
    }

    const gba::WidgetComputedStyle& ShellComputedStyle(
        const std::wstring_view key) const noexcept {
        static const gba::WidgetComputedStyle empty;
        const auto& current = appearanceState_.current();
        if (!current) return empty;
        const auto found = current->shellStyles.find(std::wstring(key));
        return found == current->shellStyles.end() ? empty : found->second;
    }

    gba::NativeRenderStyle AdaptShellStyle(
        const std::wstring_view key,
        const bool focused = false,
        const float viewportWidth = static_cast<float>(kPanelWidth),
        const float viewportHeight = static_cast<float>(kWidgetPanelHeight)) const {
        gba::NativeStyleContext context;
        context.viewportWidthPx = viewportWidth;
        context.viewportHeightPx = viewportHeight;
        context.parentWidthPx = context.viewportWidthPx;
        context.parentHeightPx = context.viewportHeightPx;
        context.parentFontSizePx = 16.0F;
        context.rootFontSizePx = 16.0F;
        context.focused = focused;
        gba::NativeAccessibilityPolicy accessibility;
        const auto& current = appearanceState_.current();
        accessibility.reducedMotion = current &&
            current->motion == gba::PlatformMotionPreference::Reduced;
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
        canvasStyle_ = AdaptShellStyle(L"canvas", false, viewportWidth, viewportHeight);
        backdropStyle_ = AdaptShellStyle(L"backdrop", false, viewportWidth, viewportHeight);
        panelStyle_ = AdaptShellStyle(L"panel", false, viewportWidth, viewportHeight);
        trayStyle_ = AdaptShellStyle(L"tray", false, viewportWidth, viewportHeight);
        trayItemStyle_ = AdaptShellStyle(L"tray-item", false, viewportWidth, viewportHeight);
        trayItemSelectedStyle_ = AdaptShellStyle(
            L"tray-item:selected", false, viewportWidth, viewportHeight);
        trayItemFocusedStyle_ = AdaptShellStyle(
            L"tray-item:focused", true, viewportWidth, viewportHeight);
        trayItemSelectedFocusedStyle_ =
            AdaptShellStyle(L"tray-item:selected:focused", true,
                            viewportWidth, viewportHeight);
        titleStyle_ = AdaptShellStyle(L"title", false, viewportWidth, viewportHeight);
        bodyStyle_ = AdaptShellStyle(L"body", false, viewportWidth, viewportHeight);
        hintStyle_ = AdaptShellStyle(L"hint", false, viewportWidth, viewportHeight);
        statusStyle_ = AdaptShellStyle(L"status", false, viewportWidth, viewportHeight);
    }

    void ApplyPlatformAppearance() {
        const auto& current = appearanceState_.current();
        if (!current) return;
        RebuildShellStyles();

        const auto backdropColor = backdropStyle_.background().value_or(
            gba::NativeColor{0, 0, 0, 1});
        if (HBRUSH replacement = CreateSolidBrush(GdiColor(backdropColor))) {
            if (backdropBrush_) DeleteObject(backdropBrush_);
            backdropBrush_ = replacement;
        }
        const BYTE opacity = static_cast<BYTE>(std::lround(
            std::clamp(current->backdropOpacity, 0.35, 0.8) * 255.0));
        SetLayeredWindowAttributes(backdropWindow_, 0, opacity, LWA_ALPHA);
        InvalidateRect(backdropWindow_, nullptr, TRUE);

        BOOL disableTransitions = FALSE;
        if (current->motion == gba::PlatformMotionPreference::Reduced) {
            disableTransitions = TRUE;
        } else if (current->motion == gba::PlatformMotionPreference::System) {
            BOOL animationsEnabled = TRUE;
            if (!SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0,
                                       &animationsEnabled, 0) || !animationsEnabled) {
                disableTransitions = TRUE;
            }
        }
        (void)DwmSetWindowAttribute(window_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));

        // Worker snapshots contain bridge-computed widget styles derived from
        // the same platform revision. Drop every cached snapshot, then refresh
        // only the already active/visible worker; background workers remain
        // untouched and lazily rebuild when selected later.
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

        DiscardGraphicsResources();
        if (state_.surface() != gba::Surface::Hidden) {
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

    bool RefreshWidgetCatalog() {
        if (!bridge_.EnsureStarted(installationDirectory_)) {
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
                 previousDescriptors, widgetDescriptors_)) focusMemory_.Forget(id);
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
        ids.reserve(kBuiltInWidgets.size() + widgetDescriptors_.size());
        for (const auto& widget : kBuiltInWidgets) ids.emplace_back(widget.id);
        for (const auto& descriptor : widgetDescriptors_) {
            if (std::find(ids.begin(), ids.end(), descriptor.id) == ids.end()) {
                ids.push_back(descriptor.id);
            }
        }
        if (!lifecycleBridgeWidget_.empty() && runtimeChanged(lifecycleBridgeWidget_)) {
            lifecycleBridgeWidget_.clear();
            lifecycleBridgeState_.reset();
            focusedElementId_.clear();
            lastWidgetRenderResult_ = {};
        }
        const auto before = state_.persistent();
        if (state_.SetAvailableWidgets(std::move(ids)) && before != state_.persistent()) {
            SavePersistentState(state_.persistent());
        }
        SyncWidgetActivity();
        return true;
    }

    [[nodiscard]] bool IsBridgeWidget(const std::wstring_view id) const noexcept {
        return std::any_of(
            widgetDescriptors_.begin(), widgetDescriptors_.end(),
            [id](const gba::WidgetDescriptor& descriptor) { return descriptor.id == id; });
    }

    void SyncWidgetActivity() {
        const auto desired = gba::DesiredWidgetLifecycle(
            state_.surface(), state_.selectedWidget(), state_.activeWidget(),
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
        if (!bridge_.EnsureStarted(installationDirectory_)) {
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

    void ShowOverlay() {
        if (!placementRefreshGate_.TryEnter()) return;
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
            return;
        }
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        if (!GetMonitorInfoW(monitor, &monitorInfo)) {
            AppendDiagnostic(L"GetMonitorInfoW failed error=" +
                             std::to_wstring(GetLastError()));
            return;
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
        const int desiredHeightDip = DesiredHeightDip();
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto placement = gba::ComputeOverlayPlacement(
            {work.left, work.top, work.right, work.bottom}, dpi,
            static_cast<float>(kPanelWidth) * interfaceScale,
            static_cast<float>(desiredHeightDip) * interfaceScale);
        if (!placement) {
            AppendDiagnostic(L"Unable to compute a safe overlay placement");
            return;
        }

        const BOOL backdropPlaced = SetWindowPos(
            backdropWindow_, HWND_TOPMOST,
            monitorInfo.rcMonitor.left, monitorInfo.rcMonitor.top,
            monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left,
            monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top,
            SWP_SHOWWINDOW | SWP_NOACTIVATE);
        const BOOL overlayPlaced = SetWindowPos(
            window_, HWND_TOPMOST, placement->x, placement->y,
            placement->width, placement->height,
            SWP_SHOWWINDOW | SWP_NOACTIVATE);
        if (!backdropPlaced || !overlayPlaced) {
            AppendDiagnostic(L"Overlay placement failed error=" +
                             std::to_wstring(GetLastError()));
            return;
        }
        if (!wasVisible) {
            ShowWindow(backdropWindow_, SW_SHOWNOACTIVATE);
            ShowWindow(window_, SW_SHOWNORMAL);
            SetForegroundWindow(window_);
            SetFocus(window_);
            SetTimer(window_, kControllerTimer, 16, nullptr);
            PrimeControllerState();
        }
    }

    void HideOverlay() {
        KillTimer(window_, kControllerTimer);
        KillTimer(window_, kCatalogRetryTimer);
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

    int DesiredHeightDip() const {
        if (state_.surface() != gba::Surface::Widget) return kDashboardHeight;
        if (IsBridgeWidget(state_.activeWidget()) && !SnapshotFor(state_.activeWidget())) return 540;
        return kWidgetPanelHeight;
    }

    void HandleKey(const UINT key) {
        switch (key) {
        case VK_LEFT:
            state_.surface() == gba::Surface::Widget
                ? MoveWidgetFocus(L"left")
                : Dispatch(gba::Command::NavigateLeft);
            break;
        case VK_RIGHT:
            state_.surface() == gba::Surface::Widget
                ? MoveWidgetFocus(L"right")
                : Dispatch(gba::Command::NavigateRight);
            break;
        case VK_UP:
            if (state_.surface() == gba::Surface::Widget) MoveWidgetFocus(L"up");
            break;
        case VK_DOWN:
            if (state_.surface() == gba::Surface::Widget) MoveWidgetFocus(L"down");
            break;
        case VK_RETURN:
            state_.surface() == gba::Surface::Widget
                ? DispatchWidgetAction(L"A")
                : Dispatch(gba::Command::Activate);
            break;
        case VK_ESCAPE:
        case 'B':
            state_.surface() == gba::Surface::Widget
                ? DispatchWidgetAction(L"B")
                : Dispatch(gba::Command::Cancel);
            break;
        case 'X':
            DispatchWidgetAction(L"X");
            break;
        case 'Y':
            state_.surface() == gba::Surface::Dashboard
                ? Dispatch(gba::Command::ToggleReorder)
                : DispatchWidgetAction(L"Y");
            break;
        case 'E':
            Dispatch(gba::Command::ToggleReorder);
            break;
        default:
            break;
        }
    }

    void PrimeControllerState() {
        XINPUT_STATE controller{};
        bool connected = false;
        for (DWORD index = 0; index < XUSER_MAX_COUNT; ++index) {
            if (XInputGetState(index, &controller) == ERROR_SUCCESS) {
                connected = true;
                break;
            }
        }
        previousButtons_ = connected ? controller.Gamepad.wButtons : 0;
        leftTriggerPressed_ = connected && controller.Gamepad.bLeftTrigger >= 30;
        rightTriggerPressed_ = connected && controller.Gamepad.bRightTrigger >= 30;
        stickNavigator_.Prime(
            connected ? controller.Gamepad.sThumbLX : 0,
            connected ? controller.Gamepad.sThumbLY : 0,
            GetTickCount64());
    }

    void DispatchStickNavigation(const gba::input::NavigationDirection direction) {
        using gba::input::NavigationDirection;
        if (state_.surface() == gba::Surface::Dashboard) {
            if (direction == NavigationDirection::Left) {
                Dispatch(gba::Command::NavigateLeft);
            } else if (direction == NavigationDirection::Right) {
                Dispatch(gba::Command::NavigateRight);
            }
            return;
        }
        if (state_.surface() != gba::Surface::Widget) return;
        switch (direction) {
        case NavigationDirection::Left: MoveWidgetFocus(L"left"); break;
        case NavigationDirection::Right: MoveWidgetFocus(L"right"); break;
        case NavigationDirection::Up: MoveWidgetFocus(L"up"); break;
        case NavigationDirection::Down: MoveWidgetFocus(L"down"); break;
        default: break;
        }
    }

    void PollController() {
        XINPUT_STATE controller{};
        bool connected = false;
        for (DWORD index = 0; index < XUSER_MAX_COUNT; ++index) {
            if (XInputGetState(index, &controller) == ERROR_SUCCESS) {
                connected = true;
                break;
            }
        }
        const WORD buttons = connected ? controller.Gamepad.wButtons : 0;
        const WORD pressed = static_cast<WORD>(buttons & ~previousButtons_);
        previousButtons_ = buttons;

        if (pressed & XINPUT_GAMEPAD_DPAD_LEFT) {
            state_.surface() == gba::Surface::Dashboard
                ? Dispatch(gba::Command::NavigateLeft)
                : MoveWidgetFocus(L"left");
        }
        if (pressed & XINPUT_GAMEPAD_DPAD_RIGHT) {
            state_.surface() == gba::Surface::Dashboard
                ? Dispatch(gba::Command::NavigateRight)
                : MoveWidgetFocus(L"right");
        }
        if (pressed & XINPUT_GAMEPAD_DPAD_UP) {
            if (state_.surface() == gba::Surface::Widget) MoveWidgetFocus(L"up");
        }
        if (pressed & XINPUT_GAMEPAD_DPAD_DOWN) {
            if (state_.surface() == gba::Surface::Widget) MoveWidgetFocus(L"down");
        }

        const ULONGLONG now = GetTickCount64();
        if (const auto direction = stickNavigator_.Update(
                connected ? controller.Gamepad.sThumbLX : 0,
                connected ? controller.Gamepad.sThumbLY : 0,
                now)) {
            DispatchStickNavigation(*direction);
        }

        if (pressed & XINPUT_GAMEPAD_A) {
            if (state_.surface() == gba::Surface::Dashboard) {
                Dispatch(gba::Command::Activate);
            } else {
                DispatchWidgetAction(L"A");
            }
        }
        if (pressed & XINPUT_GAMEPAD_B) {
            if (state_.surface() == gba::Surface::Dashboard && state_.reorderMode()) {
                Dispatch(gba::Command::Cancel);
            } else {
                DispatchWidgetAction(L"B");
            }
        }
        if (pressed & XINPUT_GAMEPAD_Y) {
            if (state_.surface() == gba::Surface::Dashboard) {
                Dispatch(gba::Command::ToggleReorder);
            } else {
                DispatchWidgetAction(L"Y");
            }
        }
        if (pressed & XINPUT_GAMEPAD_X) {
            DispatchWidgetAction(L"X");
        }
        if (pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) {
            DispatchWidgetAction(L"LB");
        }
        if (pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) {
            DispatchWidgetAction(L"RB");
        }
        if (pressed & XINPUT_GAMEPAD_LEFT_THUMB) {
            DispatchWidgetAction(L"LS");
        }
        if (pressed & XINPUT_GAMEPAD_RIGHT_THUMB) {
            DispatchWidgetAction(L"RS");
        }
        if (pressed & XINPUT_GAMEPAD_BACK) {
            DispatchWidgetAction(L"View");
        }
        if (pressed & XINPUT_GAMEPAD_START) {
            DispatchWidgetAction(L"Menu");
        }
        const bool leftTriggerPressed = connected && controller.Gamepad.bLeftTrigger >= 30;
        const bool rightTriggerPressed = connected && controller.Gamepad.bRightTrigger >= 30;
        if (leftTriggerPressed && !leftTriggerPressed_) {
            DispatchWidgetAction(L"LT");
        }
        if (rightTriggerPressed && !rightTriggerPressed_) {
            DispatchWidgetAction(L"RT");
        }
        leftTriggerPressed_ = leftTriggerPressed;
        rightTriggerPressed_ = rightTriggerPressed;
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

    void RefreshCurrentBridgeSnapshot() {
        if (state_.surface() == gba::Surface::Hidden) return;
        const std::wstring_view widgetId = state_.surface() == gba::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (IsBridgeWidget(widgetId)) RefreshWidgetSnapshot(widgetId);
    }

    void RefreshWidgetSnapshot(const std::wstring_view widgetId) {
        if (!IsBridgeWidget(widgetId)) return;
        if (!bridge_.EnsureStarted(installationDirectory_)) {
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
        if (currentWidget == widgetId) RestoreFocusForActiveSurface(widgetId);
    }

    void MoveWidgetFocus(const std::wstring_view direction) {
        if (state_.surface() != gba::Surface::Widget || focusedElementId_.empty()) {
            return;
        }
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot) return;
        const auto activeScope = std::wstring_view(snapshot->activeInputScopeId);
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
        if (explicitTarget && !explicitTarget->isDisabled && !explicitTarget->isBusy) {
            focusedElementId_ = explicitTarget->id;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        gba::input::NavigationDirection navigationDirection =
            gba::input::NavigationDirection::None;
        if (direction == L"left") navigationDirection = gba::input::NavigationDirection::Left;
        else if (direction == L"right") navigationDirection = gba::input::NavigationDirection::Right;
        else if (direction == L"up") navigationDirection = gba::input::NavigationDirection::Up;
        else if (direction == L"down") navigationDirection = gba::input::NavigationDirection::Down;
        if (const auto fallback = gba::input::FindGeometricFocusTarget(
                focusedElementId_, navigationDirection, lastWidgetRenderResult_)) {
            focusedElementId_ = *fallback;
            focusMemory_.Remember(widgetId, *snapshot, focusedElementId_);
            InvalidateRect(window_, nullptr, FALSE);
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
        return L"";
    }

    void DispatchWidgetAction(const std::wstring_view button) {
        if (state_.surface() == gba::Surface::Hidden) {
            return;
        }

        const std::wstring_view widget = state_.surface() == gba::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();

        if (IsBridgeWidget(widget)) {
            if (!SnapshotFor(widget)) RefreshWidgetSnapshot(widget);
            const auto protocolButton = ProtocolButton(button);
            const auto* snapshot = SnapshotFor(widget);
            if (protocolButton.empty() || !snapshot) return;
            const bool isOpen = state_.surface() == gba::Surface::Widget;
            const auto handled = bridge_.SendControllerInput(
                widget, protocolButton,
                isOpen ? L"openWidget" : L"dashboardQuickAction",
                isOpen ? std::wstring_view(focusedElementId_) : std::wstring_view{},
                snapshot->activeInputScopeId,
                snapshot->sequence,
                ++controllerSequence_, static_cast<long long>(GetTickCount64() * 1000));
            if (!handled) {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" input failed: " + bridge_.lastError();
            } else if (*handled) {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" handled " + std::wstring(button);
                RefreshWidgetSnapshot(widget);
            } else {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" has no " + std::wstring(button) + L" action here";
                snapshot = SnapshotFor(widget);
                if (isOpen && button == L"B" &&
                    snapshot && std::wstring_view(snapshot->activeInputScopeId) ==
                        gba::input::RootInputScope(*snapshot)) {
                    Dispatch(gba::Command::SampleWidgetBack);
                    return;
                }
            }
            lastActionExpiresAt_ = GetTickCount64() + 1800;
            AppendDiagnostic(lastActionMessage_);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        if (state_.surface() == gba::Surface::Widget && button == L"B") {
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
        const gba::NativeColor defaultCanvas{0x10 / 255.0F, 0x13 / 255.0F,
                                              0x1A / 255.0F, 1};
        const gba::NativeColor defaultPanel{0x1B / 255.0F, 0x1F / 255.0F,
                                             0x29 / 255.0F, 1};
        const gba::NativeColor defaultText{0xF7 / 255.0F, 0xF7 / 255.0F,
                                            0xFA / 255.0F, 1};
        const gba::NativeColor defaultSecondary{0x9B / 255.0F, 0xA3 / 255.0F,
                                                 0xB3 / 255.0F, 1};
        const gba::NativeColor defaultAccent{0xFC / 255.0F, 0x3F / 255.0F,
                                              0x6C / 255.0F, 1};
        const gba::NativeColor defaultSuccess{0x45 / 255.0F, 0xD4 / 255.0F,
                                               0x83 / 255.0F, 1};
        const auto canvasBackground = colorOr(canvasStyle_.background(), defaultCanvas);
        const auto trayBackground = colorOr(trayStyle_.background(), canvasBackground);
        const auto panelBackground = colorOr(panelStyle_.background(), defaultPanel);
        const auto foreground = colorOr(titleStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultText));
        const auto secondary = colorOr(hintStyle_.foreground(),
            colorOr(bodyStyle_.foreground(), defaultSecondary));
        const auto selectedBackground = colorOr(
            trayItemSelectedFocusedStyle_.background(),
            colorOr(trayItemSelectedStyle_.background(), defaultAccent));
        const auto itemBackground = colorOr(trayItemStyle_.background(), trayBackground);
        const auto selectedForeground = colorOr(
            trayItemSelectedFocusedStyle_.foreground(),
            colorOr(trayItemSelectedStyle_.foreground(), foreground));
        const auto focusColor = colorOr(
            trayItemSelectedFocusedStyle_.outlineColor(),
            colorOr(trayItemFocusedStyle_.outlineColor(), defaultAccent));

        renderTarget_->CreateSolidColorBrush(
            D2DColor(trayBackground), backgroundBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(panelBackground), cardBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(foreground), textBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(secondary), secondaryBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(selectedBackground), accentBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(colorOr(statusStyle_.foreground(), defaultSuccess)),
            successBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(itemBackground), trayItemBrush_.ReleaseAndGetAddressOf());
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
               accentBrush_ && successBrush_ && titleFormat_ && bodyFormat_ &&
               hintFormat_ && iconFormat_ && trayItemBrush_ && selectedTextBrush_ &&
               focusBrush_;
    }

    void DiscardGraphicsResources() {
        if (declarativeRenderer_) declarativeRenderer_->DiscardTargetResources();
        iconFormat_.Reset();
        hintFormat_.Reset();
        bodyFormat_.Reset();
        titleFormat_.Reset();
        focusBrush_.Reset();
        selectedTextBrush_.Reset();
        trayItemBrush_.Reset();
        accentBrush_.Reset();
        successBrush_.Reset();
        secondaryBrush_.Reset();
        textBrush_.Reset();
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
        renderTarget_->Clear(D2D1::ColorF(1.0F / 255.0F, 2.0F / 255.0F,
                                          3.0F / 255.0F, 1.0F));
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Scale(
            metrics->interfaceScale, metrics->interfaceScale));

        if (state_.surface() == gba::Surface::Widget) {
            DrawWidget(metrics->viewportWidthDip, metrics->viewportHeightDip,
                       metrics->physicalPixelsPerDip);
        } else {
            DrawDashboard(metrics->viewportWidthDip, metrics->viewportHeightDip);
        }
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());

        const HRESULT result = renderTarget_->EndDraw();
        if (result == D2DERR_RECREATE_TARGET) {
            DiscardGraphicsResources();
        }
        EndPaint(window_, &paint);
    }

    void DrawIconStrip(const float width, const float height) {
        if (state_.order().empty() || width <= 0.0F || height <= 0.0F) return;
        constexpr float preferredTileSize = 64.0F;
        constexpr float gap = 14.0F;
        const float stripTop = std::max(0.0F, height - 112.0F);
        const float stripBottom = std::max(stripTop, height - 14.0F);
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
                if (state_.reorderMode()) {
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
                slot == state_.selectedSlot() ? selectedTextBrush_.Get() : textBrush_.Get(),
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

    std::wstring DashboardHint() const {
        if (state_.reorderMode()) {
            return L"D-pad / Left stick  Move     Y  Done     B  Cancel";
        }
        if (lastActionExpiresAt_ > GetTickCount64() && !lastActionMessage_.empty()) {
            return lastActionMessage_;
        }
        const auto* snapshot = SnapshotFor(state_.selectedWidget());
        if (IsBridgeWidget(state_.selectedWidget()) && snapshot &&
            !snapshot->quickActions.empty()) {
            std::wstring prompt;
            for (const auto& action : snapshot->quickActions) {
                if (!prompt.empty()) prompt += L"     ";
                prompt += DisplayButton(action.button);
                prompt += L"  ";
                prompt += action.label;
            }
            prompt += L"     A  Open     Y  Reorder";
            return prompt;
        }
        return L"D-pad / Left stick  Select     A  Open     Y  Reorder     Guide  Close";
    }

    void DrawDashboard(const float width, const float height) {
        const std::wstring_view title = state_.reorderMode()
            ? L"Reorder widgets"
            : DisplayWidgetName(state_.selectedWidget());
        DrawTextLine(title, titleFormat_.Get(),
                     D2D1::RectF(34, 10, width - 34, 44),
                     textBrush_.Get());
        DrawIconStrip(width, height);

        const std::wstring hint = DashboardHint();
        DrawTextLine(hint, hintFormat_.Get(),
                     D2D1::RectF(34, 44, width - 34, 66),
                     secondaryBrush_.Get());
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
            if (button == L"x") return 0;
            if (button == L"leftBumper") return 1;
            if (button == L"rightBumper") return 2;
            if (button == L"y") return 3;
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

    void DrawWidgetFooter(const float panelLeft,
                          const float panelWidth,
                          const float panelBottom) {
        renderTarget_->DrawLine(
            D2D1::Point2F(panelLeft + 30, panelBottom - 54),
            D2D1::Point2F(panelLeft + panelWidth - 30, panelBottom - 54),
            secondaryBrush_.Get(), 0.5F);
        const std::wstring prompt = OpenWidgetPrompt();
        DrawTextLine(prompt, hintFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, panelBottom - 40,
                                 panelLeft + panelWidth - 150, panelBottom - 12),
                     secondaryBrush_.Get());
        DrawTextLine(L"Guide  Close", hintFormat_.Get(),
                     D2D1::RectF(panelLeft + panelWidth - 130, panelBottom - 40,
                                 panelLeft + panelWidth - 24, panelBottom - 12),
                     secondaryBrush_.Get());
    }

    void DrawWidget(
        const float width,
        const float height,
        const float physicalPixelsPerDip) {
        const std::wstring_view widget = state_.activeWidget();
        const bool bridgeWidget = IsBridgeWidget(widget);
        const auto geometry = gba::ComputeOverlaySurfaceGeometry(
            width, height, bridgeWidget ? 880.0F : 720.0F);
        if (!geometry) return;
        const float panelLeft = geometry->panelX;
        const float panelTop = geometry->panelY;
        const float panelWidth = geometry->panelWidth;
        const float panelBottom = geometry->panelY + geometry->panelHeight;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(panelLeft, panelTop, panelLeft + panelWidth, panelBottom),
            panelCornerRadius_, panelCornerRadius_};
        renderTarget_->FillRoundedRectangle(panel, cardBrush_.Get());

        if (bridgeWidget) {
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
                if (const auto& appearance = appearanceState_.current()) {
                    options.accessibility.textScale =
                        static_cast<float>(appearance->textScale);
                    options.accessibility.reducedMotion =
                        appearance->motion == gba::PlatformMotionPreference::Reduced;
                }
                auto result = declarativeRenderer_->Render(
                    renderTarget_.Get(), *snapshot, focusedElementId_, viewport, options);
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
            DrawWidgetFooter(panelLeft, panelWidth, panelBottom);
            DrawIconStrip(width, height);
            return;
        }

        DrawTextLine(DisplayWidgetName(widget), titleFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 48, panelLeft + panelWidth - 30, 88),
                     textBrush_.Get());
        DrawTextLine(WidgetDetail(widget), bodyFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 94, panelLeft + panelWidth - 30, 122),
                     secondaryBrush_.Get());
        DrawTextLine(L"This is the native host placeholder. The active widget owns LB, RB, "
                     L"and all non-Guide controller input.",
                     bodyFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 146, panelLeft + panelWidth - 30, 202),
                     secondaryBrush_.Get());

        const D2D1_ROUNDED_RECT action{
            D2D1::RectF(panelLeft + 30, 224, panelLeft + 300, 298), 14.0F, 14.0F};
        renderTarget_->FillRoundedRectangle(action, accentBrush_.Get());
        DrawTextLine(L"A  Sample action", titleFormat_.Get(),
                     D2D1::RectF(panelLeft + 54, 243, panelLeft + 278, 280),
                     textBrush_.Get());
        DrawTextLine(L"B  Back to icons                       Guide  Close overlay",
                     hintFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, panelBottom - 40,
                                 panelLeft + panelWidth - 30, panelBottom - 14),
                     secondaryBrush_.Get());
        DrawIconStrip(width, height);
    }

    HINSTANCE instance_{};
    HWND window_{};
    gba::ForegroundTargetTracker foregroundTarget_;
    gba::PlacementRefreshGate placementRefreshGate_;
    HWND backdropWindow_{};
    HBRUSH backdropBrush_{};
    HWINEVENTHOOK foregroundHook_{};
    inline static OverlayApp* foregroundEventApp_{};
    std::wstring initializationError_;
    std::wstring installationDirectory_;
    gba::OverlayState state_;
    WORD previousButtons_{};
    gba::input::StickNavigator stickNavigator_;
    bool leftTriggerPressed_{};
    bool rightTriggerPressed_{};
    long long controllerSequence_{};
    std::wstring lastActionMessage_;
    ULONGLONG lastActionExpiresAt_{};
    ULONGLONG lastGuideDispatchAt_{};
    std::wstring focusedElementId_;
    gba::input::WidgetSurfaceFocusMemory focusMemory_;
    std::unordered_map<std::wstring, gba::WidgetSnapshot> widgetSnapshots_;
    gba::WidgetBridgeClient bridge_;
    unsigned int catalogRetryAttempts_{};
    gba::PlatformAppearanceState appearanceState_;
    gba::NativeRenderStyle canvasStyle_;
    gba::NativeRenderStyle backdropStyle_;
    gba::NativeRenderStyle panelStyle_;
    gba::NativeRenderStyle trayStyle_;
    gba::NativeRenderStyle trayItemStyle_;
    gba::NativeRenderStyle trayItemSelectedStyle_;
    gba::NativeRenderStyle trayItemFocusedStyle_;
    gba::NativeRenderStyle trayItemSelectedFocusedStyle_;
    gba::NativeRenderStyle titleStyle_;
    gba::NativeRenderStyle bodyStyle_;
    gba::NativeRenderStyle hintStyle_;
    gba::NativeRenderStyle statusStyle_;
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

    ComPtr<IGameInput> gameInput_;
    gba::input::XInputGuideCompatibility guideCompatibility_;
    GameInputCallbackToken guideCallback_{};
    ComPtr<ID2D1Factory> d2dFactory_;
    ComPtr<IDWriteFactory> writeFactory_;
    ComPtr<ID2D1HwndRenderTarget> renderTarget_;
    ComPtr<ID2D1SolidColorBrush> backgroundBrush_;
    ComPtr<ID2D1SolidColorBrush> cardBrush_;
    ComPtr<ID2D1SolidColorBrush> textBrush_;
    ComPtr<ID2D1SolidColorBrush> secondaryBrush_;
    ComPtr<ID2D1SolidColorBrush> accentBrush_;
    ComPtr<ID2D1SolidColorBrush> successBrush_;
    ComPtr<ID2D1SolidColorBrush> trayItemBrush_;
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
