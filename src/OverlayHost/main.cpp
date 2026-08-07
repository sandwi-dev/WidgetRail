#include "OverlayState.h"
#include "DeclarativeRenderer.h"
#include "ControllerNavigation.h"
#include "GuideInputCompatibility.h"
#include "FocusNavigation.h"
#include "NativeIcons.h"
#include "OverlayPlacement.h"
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
constexpr UINT kGuideMessage = WM_APP + 1;
constexpr UINT kImageReadyMessage = WM_APP + 2;
constexpr UINT kCatalogRefreshMessage = WM_APP + 3;
constexpr UINT kSnapshotRefreshMessage = WM_APP + 4;
constexpr UINT kForegroundChangedMessage = WM_APP + 5;
constexpr BYTE kBackdropOpacity = 164;
constexpr int kDeveloperHotkey = 1;

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
            FillRect(dc, &paint.rcPaint, static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
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
        HWND,
        LONG,
        LONG,
        DWORD,
        DWORD) {
        auto* app = foregroundEventApp_;
        if (event == EVENT_SYSTEM_FOREGROUND && app && app->window_) {
            PostMessageW(app->window_, kForegroundChangedMessage, 0, 0);
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
            artworkBitmap_.Reset();
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kCatalogRefreshMessage:
            RefreshWidgetCatalog();
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kSnapshotRefreshMessage:
            RefreshYtMusicSnapshot();
            ShowOverlay();
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kForegroundChangedMessage:
            if (state_.surface() != gba::Surface::Hidden) {
                SetTimer(window_, kZOrderSettleTimer, 80, nullptr);
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
                if (bridge_.takeInvalidated() &&
                    ((state_.surface() == gba::Surface::Widget && state_.activeWidget() == L"yt-music") ||
                     (state_.surface() == gba::Surface::Dashboard && state_.selectedWidget() == L"yt-music"))) {
                    const int priorHeight = DesiredHeightDip();
                    RefreshYtMusicSnapshot();
                    // Repaint-only invalidations (progress, metadata, button
                    // state) must not churn HWND placement. Resize only when
                    // the widget changes between compact/expanded surfaces.
                    if (DesiredHeightDip() != priorHeight) ShowOverlay();
                    InvalidateRect(window_, nullptr, FALSE);
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
            if (renderTarget_) {
                renderTarget_->Resize(D2D1::SizeU(LOWORD(lParam), HIWORD(lParam)));
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
        case WM_SETTINGCHANGE:
            if (state_.surface() != gba::Surface::Hidden) {
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

        if (state_.surface() == gba::Surface::Hidden) {
            HideOverlay();
        } else {
            const bool enteredYtMusic =
                state_.surface() == gba::Surface::Widget && state_.activeWidget() == L"yt-music" &&
                (priorSurface != gba::Surface::Widget || priorActive != L"yt-music");
            const bool hoveredYtMusic =
                state_.surface() == gba::Surface::Dashboard && state_.selectedWidget() == L"yt-music" &&
                (priorSurface != gba::Surface::Dashboard || priorSelected != L"yt-music");
            ShowOverlay();
            InvalidateRect(window_, nullptr, FALSE);
            if (priorSurface == gba::Surface::Hidden) {
                PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
            }
            if (enteredYtMusic || hoveredYtMusic) {
                PostMessageW(window_, kSnapshotRefreshMessage, 0, 0);
            }
        }
    }

    void RefreshWidgetCatalog() {
        if (!bridge_.EnsureStarted(installationDirectory_)) {
            AppendDiagnostic(L"Widget catalog unavailable: " + bridge_.lastError());
            return;
        }
        auto descriptors = bridge_.ListWidgets();
        if (!descriptors) {
            AppendDiagnostic(L"Widget catalog failed: " + bridge_.lastError());
            return;
        }
        widgetDescriptors_ = std::move(*descriptors);
        std::vector<std::wstring> ids;
        ids.reserve(kBuiltInWidgets.size() + widgetDescriptors_.size());
        for (const auto& widget : kBuiltInWidgets) ids.emplace_back(widget.id);
        for (const auto& descriptor : widgetDescriptors_) {
            if (std::find(ids.begin(), ids.end(), descriptor.id) == ids.end()) {
                ids.push_back(descriptor.id);
            }
        }
        const auto before = state_.persistent();
        if (state_.SetAvailableWidgets(std::move(ids)) && before != state_.persistent()) {
            SavePersistentState(state_.persistent());
        }
        SyncWidgetActivity();
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

    void ShowOverlay() {
        const bool wasVisible = IsWindowVisible(window_) != FALSE;
        if (!wasVisible) {
            const HWND foreground = GetForegroundWindow();
            if (foreground != window_) {
                previousForeground_ = foreground;
            }
        }

        const HMONITOR monitor = MonitorFromWindow(
            previousForeground_ ? previousForeground_ : window_, MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        GetMonitorInfoW(monitor, &monitorInfo);
        const RECT& work = monitorInfo.rcWork;
        UINT dpi = previousForeground_ && IsWindow(previousForeground_)
            ? GetDpiForWindow(previousForeground_)
            : GetDpiForWindow(window_);
        if (dpi == 0) {
            UINT monitorDpiY = 96;
            if (FAILED(GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &dpi, &monitorDpiY))) {
                dpi = 96;
            }
        }
        const int desiredHeightDip = DesiredHeightDip();
        const auto placement = gba::ComputeOverlayPlacement(
            {work.left, work.top, work.right, work.bottom}, dpi,
            static_cast<float>(kPanelWidth), static_cast<float>(desiredHeightDip));
        if (!placement) {
            AppendDiagnostic(L"Unable to compute a safe overlay placement");
            return;
        }

        SetWindowPos(backdropWindow_, HWND_TOPMOST,
                     monitorInfo.rcMonitor.left, monitorInfo.rcMonitor.top,
                     monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left,
                     monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top,
                     SWP_SHOWWINDOW | SWP_NOACTIVATE);
        SetWindowPos(window_, HWND_TOPMOST, placement->x, placement->y,
                     placement->width, placement->height,
                     SWP_SHOWWINDOW | SWP_NOACTIVATE);
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
        ShowWindow(window_, SW_HIDE);
        ShowWindow(backdropWindow_, SW_HIDE);
        SetWindowPos(window_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(backdropWindow_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        DiscardGraphicsResources();
        if (previousForeground_ && IsWindow(previousForeground_)) {
            SetForegroundWindow(previousForeground_);
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
        if (state_.activeWidget() == L"yt-music" &&
            (!ytMusicSnapshot_ ||
             !FindWidgetNode(ytMusicSnapshot_->root, L"track-title"))) {
            return 540;
        }
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

    static const gba::WidgetNode* FindWidgetNode(const gba::WidgetNode& node,
                                                 const std::wstring_view id) {
        if (node.id == id) return &node;
        for (const auto& child : node.children) {
            if (const auto* match = FindWidgetNode(child, id)) return match;
        }
        return nullptr;
    }

    void RememberCurrentFocus(const std::wstring_view widgetId) {
        if (!ytMusicSnapshot_ || focusedElementId_.empty()) return;
        focusMemory_.Remember(widgetId, *ytMusicSnapshot_, focusedElementId_);
    }

    void RestoreFocusForActiveSurface(const std::wstring_view widgetId) {
        focusedElementId_ = ytMusicSnapshot_
            ? focusMemory_.Restore(widgetId, *ytMusicSnapshot_)
            : std::wstring{};
    }

    void RefreshYtMusicSnapshot() {
        if (!bridge_.EnsureStarted(installationDirectory_)) {
            lastActionMessage_ = L"YT Music unavailable: " + bridge_.lastError();
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            return;
        }
        auto snapshot = bridge_.GetSnapshot(L"yt-music");
        if (!snapshot) {
            lastActionMessage_ = L"YT Music failed: " + bridge_.lastError();
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            return;
        }
        RememberCurrentFocus(L"yt-music");
        ytMusicSnapshot_ = std::move(snapshot);
        if (const auto* artwork = FindWidgetNode(ytMusicSnapshot_->root, L"album-artwork");
            artwork && !artwork->imageSource.empty()) {
            if (artworkUrl_ != artwork->imageSource) {
                artworkUrl_ = artwork->imageSource;
                artworkBitmap_.Reset();
            }
            if (imageCache_) (void)imageCache_->Request(artworkUrl_);
        } else {
            artworkUrl_.clear();
            artworkBitmap_.Reset();
        }
        RestoreFocusForActiveSurface(L"yt-music");
    }

    void MoveWidgetFocus(const std::wstring_view direction) {
        if (state_.surface() != gba::Surface::Widget ||
            !ytMusicSnapshot_ || focusedElementId_.empty()) {
            return;
        }
        const auto activeScope = std::wstring_view(ytMusicSnapshot_->activeInputScopeId);
        const auto* focused = gba::input::FindNodeInInputScope(
            *ytMusicSnapshot_, focusedElementId_, activeScope);
        if (!focused) return;
        const std::wstring* target = nullptr;
        if (direction == L"up") target = &focused->focusUp;
        else if (direction == L"down") target = &focused->focusDown;
        else if (direction == L"left") target = &focused->focusLeft;
        else if (direction == L"right") target = &focused->focusRight;
        const auto* explicitTarget = target && !target->empty()
            ? gba::input::FindNodeInInputScope(*ytMusicSnapshot_, *target, activeScope)
            : nullptr;
        if (explicitTarget && !explicitTarget->isDisabled && !explicitTarget->isBusy) {
            focusedElementId_ = explicitTarget->id;
            focusMemory_.Remember(L"yt-music", *ytMusicSnapshot_, focusedElementId_);
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
            focusMemory_.Remember(L"yt-music", *ytMusicSnapshot_, focusedElementId_);
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

        if (widget == L"yt-music") {
            if (!ytMusicSnapshot_) RefreshYtMusicSnapshot();
            const auto protocolButton = ProtocolButton(button);
            if (protocolButton.empty() || !ytMusicSnapshot_) return;
            const bool isOpen = state_.surface() == gba::Surface::Widget;
            const auto handled = bridge_.SendControllerInput(
                L"yt-music", protocolButton,
                isOpen ? L"openWidget" : L"dashboardQuickAction",
                isOpen ? std::wstring_view(focusedElementId_) : std::wstring_view{},
                ytMusicSnapshot_->activeInputScopeId,
                ytMusicSnapshot_->sequence,
                ++controllerSequence_, static_cast<long long>(GetTickCount64() * 1000));
            if (!handled) {
                lastActionMessage_ = L"YT Music input failed: " + bridge_.lastError();
            } else if (*handled) {
                lastActionMessage_ = L"YT Music handled " + std::wstring(button);
                RefreshYtMusicSnapshot();
            } else {
                lastActionMessage_ = L"YT Music has no " + std::wstring(button) + L" action here";
                if (isOpen && button == L"B" &&
                    std::wstring_view(ytMusicSnapshot_->activeInputScopeId) ==
                        gba::input::RootInputScope(*ytMusicSnapshot_)) {
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

        renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0x10131A), backgroundBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0x1B1F29), cardBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0xF7F7FA), textBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0x9BA3B3), secondaryBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0xFC3F6C), accentBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2D1::ColorF(0x45D483), successBrush_.ReleaseAndGetAddressOf());

        writeFactory_->CreateTextFormat(L"Segoe UI Variable Display", nullptr,
                                        DWRITE_FONT_WEIGHT_SEMI_BOLD,
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        25.0F, L"en-us", titleFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(L"Segoe UI Variable Text", nullptr,
                                        DWRITE_FONT_WEIGHT_NORMAL,
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        16.0F, L"en-us", bodyFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(L"Segoe UI Variable Text", nullptr,
                                        DWRITE_FONT_WEIGHT_SEMI_BOLD,
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        14.0F, L"en-us", hintFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(L"Segoe UI Variable Display", nullptr,
                                        DWRITE_FONT_WEIGHT_SEMI_BOLD,
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        23.0F, L"en-us", iconFormat_.ReleaseAndGetAddressOf());
        if (iconFormat_) {
            iconFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            iconFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        }
        return backgroundBrush_ && cardBrush_ && textBrush_ && secondaryBrush_ &&
               accentBrush_ && successBrush_ && titleFormat_ && bodyFormat_ &&
               hintFormat_ && iconFormat_;
    }

    void DiscardGraphicsResources() {
        if (declarativeRenderer_) declarativeRenderer_->DiscardTargetResources();
        artworkBitmap_.Reset();
        iconFormat_.Reset();
        hintFormat_.Reset();
        bodyFormat_.Reset();
        titleFormat_.Reset();
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
        const float width = static_cast<float>(client.right - client.left) * 96.0F / dpiX;
        const float height = static_cast<float>(client.bottom - client.top) * 96.0F / dpiY;
        const float designWidth = static_cast<float>(kPanelWidth);
        const float designHeight = static_cast<float>(DesiredHeightDip());
        const float scale = std::min({1.0F, width / designWidth, height / designHeight});
        const float offsetX = (width - designWidth * scale) / 2.0F;
        const float offsetY = height - designHeight * scale;
        renderTarget_->BeginDraw();
        renderTarget_->Clear(D2D1::ColorF(1.0F / 255.0F, 2.0F / 255.0F,
                                          3.0F / 255.0F, 1.0F));
        renderTarget_->SetTransform(D2D1::Matrix3x2F(scale, 0.0F, 0.0F, scale,
                                                     offsetX, offsetY));

        if (state_.surface() == gba::Surface::Widget) {
            DrawWidget(designWidth, designHeight);
        } else {
            DrawDashboard(designWidth, designHeight);
        }
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());

        const HRESULT result = renderTarget_->EndDraw();
        if (result == D2DERR_RECREATE_TARGET) {
            DiscardGraphicsResources();
        }
        EndPaint(window_, &paint);
    }

    void DrawIconStrip(const float width, const float height) {
        if (state_.order().empty()) return;
        constexpr float tileWidth = 64.0F;
        constexpr float tileHeight = 64.0F;
        constexpr float gap = 14.0F;
        const auto maximumVisible = static_cast<std::size_t>(std::max(
            1.0F, std::floor((width - 80.0F + gap) / (tileWidth + gap))));
        const std::size_t visibleCount = std::min(state_.order().size(), maximumVisible);
        const std::size_t half = visibleCount / 2;
        const std::size_t maximumFirst = state_.order().size() - visibleCount;
        const std::size_t firstSlot = std::min(
            state_.selectedSlot() > half ? state_.selectedSlot() - half : 0U,
            maximumFirst);
        const float stripWidth = tileWidth * static_cast<float>(visibleCount) +
                                 gap * static_cast<float>(visibleCount - 1) + 28.0F;
        const float stripLeft = (width - stripWidth) / 2.0F;
        const float stripTop = height - 112.0F;
        const D2D1_ROUNDED_RECT strip{
            D2D1::RectF(stripLeft, stripTop, stripLeft + stripWidth, height - 14.0F),
            22.0F, 22.0F};
        renderTarget_->FillRoundedRectangle(strip, backgroundBrush_.Get());

        for (std::size_t visibleIndex = 0; visibleIndex < visibleCount; ++visibleIndex) {
            const std::size_t slot = firstSlot + visibleIndex;
            const float x = stripLeft + 14.0F + static_cast<float>(visibleIndex) * (tileWidth + gap);
            const float top = stripTop + 16.0F;
            const D2D1_ROUNDED_RECT tile{
                D2D1::RectF(x, top, x + tileWidth, top + tileHeight), 16.0F, 16.0F};
            renderTarget_->FillRoundedRectangle(
                tile, slot == state_.selectedSlot() ? cardBrush_.Get()
                                                    : backgroundBrush_.Get());
            if (slot == state_.selectedSlot()) {
                const D2D1_ROUNDED_RECT indicator{
                    D2D1::RectF(x + 18.0F, top + tileHeight - 4.0F,
                                x + tileWidth - 18.0F, top + tileHeight),
                    2.0F, 2.0F};
                renderTarget_->FillRoundedRectangle(indicator, accentBrush_.Get());
                if (state_.reorderMode()) {
                    renderTarget_->DrawRoundedRectangle(tile, accentBrush_.Get(), 2.0F);
                }
            }

            const std::wstring_view widget = state_.order()[slot];
            (void)gba::icons::DrawNativeIcon(
                renderTarget_.Get(), WidgetIcon(widget),
                D2D1::RectF(x + 15, top + 15, x + tileWidth - 15, top + tileHeight - 15),
                textBrush_.Get(), 2.35F);
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
        if (state_.selectedWidget() == L"yt-music" && ytMusicSnapshot_ &&
            !ytMusicSnapshot_->quickActions.empty()) {
            std::wstring prompt;
            for (const auto& action : ytMusicSnapshot_->quickActions) {
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

    const gba::WidgetNode* YtNode(const std::wstring_view id) const {
        return ytMusicSnapshot_ ? FindWidgetNode(ytMusicSnapshot_->root, id) : nullptr;
    }

    void DrawWidgetButtonRow(const gba::WidgetNode* row,
                             const float left,
                             const float top,
                             const float width,
                             const float height) {
        if (!row || row->children.empty()) return;
        constexpr float gap = 10.0F;
        const float buttonWidth =
            (width - gap * static_cast<float>(row->children.size() - 1)) /
            static_cast<float>(row->children.size());
        for (std::size_t index = 0; index < row->children.size(); ++index) {
            const auto& button = row->children[index];
            const float x = left + static_cast<float>(index) * (buttonWidth + gap);
            const D2D1_ROUNDED_RECT rectangle{
                D2D1::RectF(x, top, x + buttonWidth, top + height), 12.0F, 12.0F};
            renderTarget_->FillRoundedRectangle(
                rectangle, button.id == focusedElementId_ ? accentBrush_.Get()
                                                          : backgroundBrush_.Get());
            if (button.id == focusedElementId_) {
                renderTarget_->DrawRoundedRectangle(rectangle, textBrush_.Get(), 2.0F);
            }
            DrawTextLine(button.text, hintFormat_.Get(),
                         D2D1::RectF(x + 12, top + 14, x + buttonWidth - 10,
                                     top + height - 8),
                         button.id == focusedElementId_ ? textBrush_.Get()
                                                        : secondaryBrush_.Get());
        }
    }

    static gba::icons::NativeIcon MediaIcon(const gba::WidgetNode& button) {
        if (button.id == L"play-pause") {
            return button.text == L"Pause" ? gba::icons::NativeIcon::Pause
                                             : gba::icons::NativeIcon::Play;
        }
        gba::icons::NativeIcon icon{};
        if (gba::icons::TryParseNativeIcon(button.glyph, icon) ||
            gba::icons::TryParseNativeIcon(button.actionId, icon) ||
            gba::icons::TryParseNativeIcon(button.id, icon)) {
            return icon;
        }
        return gba::icons::NativeIcon::Connection;
    }

    void DrawMediaCircle(const gba::WidgetNode& button,
                         const float centerX,
                         const float centerY,
                         const float radius,
                         const bool primary = false) {
        const D2D1_ELLIPSE ellipse{D2D1::Point2F(centerX, centerY), radius, radius};
        renderTarget_->FillEllipse(ellipse, primary ? accentBrush_.Get()
                                                    : backgroundBrush_.Get());
        renderTarget_->DrawEllipse(
            ellipse,
            button.id == focusedElementId_ ? textBrush_.Get() : secondaryBrush_.Get(),
            button.id == focusedElementId_ ? 2.5F : 0.75F);
        (void)gba::icons::DrawNativeIcon(
            renderTarget_.Get(), MediaIcon(button),
            D2D1::RectF(centerX - radius * 0.46F, centerY - radius * 0.46F,
                        centerX + radius * 0.46F, centerY + radius * 0.46F),
            textBrush_.Get(), primary ? 2.4F : 1.8F);
    }

    void DrawAlbumArtwork(const D2D1_ROUNDED_RECT& destination) {
        renderTarget_->FillRoundedRectangle(destination, backgroundBrush_.Get());
        if (!artworkBitmap_ && imageCache_ && !artworkUrl_.empty()) {
            (void)imageCache_->CreateBitmap(renderTarget_.Get(), artworkUrl_,
                                            artworkBitmap_.ReleaseAndGetAddressOf());
        }
        if (artworkBitmap_) {
            const D2D1_SIZE_F imageSize = artworkBitmap_->GetSize();
            const float destinationWidth = destination.rect.right - destination.rect.left;
            const float destinationHeight = destination.rect.bottom - destination.rect.top;
            const float destinationAspect = destinationWidth / destinationHeight;
            const float imageAspect = imageSize.width / imageSize.height;
            D2D1_RECT_F source = D2D1::RectF(0, 0, imageSize.width, imageSize.height);
            if (imageAspect > destinationAspect) {
                const float croppedWidth = imageSize.height * destinationAspect;
                const float offset = (imageSize.width - croppedWidth) / 2.0F;
                source.left = offset;
                source.right = offset + croppedWidth;
            } else if (imageAspect < destinationAspect) {
                const float croppedHeight = imageSize.width / destinationAspect;
                const float offset = (imageSize.height - croppedHeight) / 2.0F;
                source.top = offset;
                source.bottom = offset + croppedHeight;
            }

            ComPtr<ID2D1RoundedRectangleGeometry> geometry;
            ComPtr<ID2D1Layer> layer;
            if (SUCCEEDED(d2dFactory_->CreateRoundedRectangleGeometry(
                    destination, geometry.ReleaseAndGetAddressOf())) &&
                SUCCEEDED(renderTarget_->CreateLayer(nullptr, layer.ReleaseAndGetAddressOf()))) {
                D2D1_LAYER_PARAMETERS parameters{};
                parameters.contentBounds = destination.rect;
                parameters.geometricMask = geometry.Get();
                parameters.maskAntialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE;
                parameters.maskTransform = D2D1::Matrix3x2F::Identity();
                parameters.opacity = 1.0F;
                parameters.layerOptions = D2D1_LAYER_OPTIONS_NONE;
                renderTarget_->PushLayer(parameters, layer.Get());
                renderTarget_->DrawBitmap(artworkBitmap_.Get(), destination.rect, 1.0F,
                                          D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, source);
                renderTarget_->PopLayer();
            } else {
                renderTarget_->DrawBitmap(artworkBitmap_.Get(), destination.rect, 1.0F,
                                          D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, source);
            }
        } else {
            (void)gba::icons::DrawNativeIcon(
                renderTarget_.Get(), gba::icons::NativeIcon::Music,
                destination.rect, secondaryBrush_.Get(), 2.0F);
        }
        renderTarget_->DrawRoundedRectangle(destination, secondaryBrush_.Get(), 0.75F);
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
        if (!ytMusicSnapshot_) return L"A  Select";
        std::vector<std::pair<std::wstring, std::wstring>> prompts;
        CollectShortcutPrompts(ytMusicSnapshot_->root, prompts);
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

    void DrawYtMusicPanel(const float panelLeft,
                          const float panelWidth,
                          const float panelBottom) {
        if (!ytMusicSnapshot_) {
            DrawTextLine(L"YT MUSIC", hintFormat_.Get(),
                         D2D1::RectF(panelLeft + 42, 48, panelLeft + panelWidth - 30, 76),
                         textBrush_.Get());
            renderTarget_->FillRoundedRectangle(
                D2D1::RoundedRect(D2D1::RectF(panelLeft + 30, 47, panelLeft + 34, 72),
                                  2.0F, 2.0F),
                accentBrush_.Get());
            DrawTextLine(lastActionMessage_.empty() ? L"Starting isolated widget…"
                                                     : std::wstring_view(lastActionMessage_),
                         bodyFormat_.Get(),
                         D2D1::RectF(panelLeft + 30, 94,
                                     panelLeft + panelWidth - 30, 150),
                         secondaryBrush_.Get());
            return;
        }

        const auto* status = YtNode(L"connection-status");
        renderTarget_->FillRoundedRectangle(
            D2D1::RoundedRect(D2D1::RectF(panelLeft + 30, 47, panelLeft + 34, 72),
                              2.0F, 2.0F),
            accentBrush_.Get());
        DrawTextLine(L"YT MUSIC", hintFormat_.Get(),
                     D2D1::RectF(panelLeft + 44, 48, panelLeft + panelWidth - 100, 76),
                     textBrush_.Get());
        if (status) {
            const bool connected = YtNode(L"track-title") != nullptr;
            renderTarget_->FillEllipse(
                D2D1::Ellipse(D2D1::Point2F(panelLeft + 36, 91), 4.0F, 4.0F),
                connected ? successBrush_.Get() : secondaryBrush_.Get());
            DrawTextLine(status->text, hintFormat_.Get(),
                         D2D1::RectF(panelLeft + 48, 78,
                                     panelLeft + panelWidth - 100, 104),
                         secondaryBrush_.Get());
        }
        const auto* trackTitle = YtNode(L"track-title");
        if (trackTitle) {
            const auto* artist = YtNode(L"track-artist");
            const auto* album = YtNode(L"track-album");
            const float artworkLeft = panelLeft + 34;
            const float artworkTop = 128.0F;
            const float artworkSize = 184.0F;
            const D2D1_ROUNDED_RECT artwork{
                D2D1::RectF(artworkLeft, artworkTop,
                            artworkLeft + artworkSize, artworkTop + artworkSize),
                14.0F, 14.0F};
            DrawAlbumArtwork(artwork);

            const float contentLeft = panelLeft + 238;
            const float contentRight = panelLeft + panelWidth - 34;
            DrawTextLine(trackTitle->text, titleFormat_.Get(),
                         D2D1::RectF(contentLeft, 128, contentRight, 164),
                         textBrush_.Get());
            if (artist) {
                DrawTextLine(artist->text, bodyFormat_.Get(),
                             D2D1::RectF(contentLeft, 166, contentRight, 192),
                             secondaryBrush_.Get());
            }
            if (album) {
                DrawTextLine(album->text, hintFormat_.Get(),
                             D2D1::RectF(contentLeft, 194, contentRight, 218),
                             secondaryBrush_.Get());
            }
            if (const auto* progress = YtNode(L"track-progress");
                progress && progress->hasProgress && progress->maximum > 0) {
                const float progressLeft = contentLeft;
                const float progressRight = contentRight;
                const float ratio = static_cast<float>(
                    std::clamp(progress->value / progress->maximum, 0.0, 1.0));
                const D2D1_ROUNDED_RECT track{
                    D2D1::RectF(progressLeft, 226, progressRight, 231), 2.5F, 2.5F};
                const D2D1_ROUNDED_RECT fill{
                    D2D1::RectF(progressLeft, 226,
                                progressLeft + (progressRight - progressLeft) * ratio, 231),
                    2.5F, 2.5F};
                renderTarget_->FillRoundedRectangle(track, backgroundBrush_.Get());
                renderTarget_->FillRoundedRectangle(fill, accentBrush_.Get());
            }
            if (const auto* position = YtNode(L"position-text")) {
                DrawTextLine(position->text, hintFormat_.Get(),
                             D2D1::RectF(contentLeft, 235, contentLeft + 80, 258),
                             secondaryBrush_.Get());
            }
            if (const auto* duration = YtNode(L"duration-text")) {
                DrawTextLine(duration->text, hintFormat_.Get(),
                             D2D1::RectF(contentRight - 64, 235, contentRight, 258),
                             secondaryBrush_.Get());
            }

            if (const auto* primary = YtNode(L"primary-actions")) {
                float centerX = contentLeft + 30;
                for (const auto& button : primary->children) {
                    if (button.id == L"refresh") continue;
                    const bool play = button.id == L"play-pause";
                    DrawMediaCircle(button, centerX, 294, play ? 32.0F : 25.0F, play);
                    centerX += play ? 76.0F : 66.0F;
                }
            }
            if (const auto* secondary = YtNode(L"secondary-actions")) {
                float centerX = contentLeft + 20;
                for (const auto& button : secondary->children) {
                    DrawMediaCircle(button, centerX, 362, 19.0F);
                    centerX += 50.0F;
                }
            }
            if (const auto* refresh = YtNode(L"refresh")) {
                DrawMediaCircle(*refresh, contentLeft + 220, 362, 19.0F);
            }
        } else {
            const auto* help = YtNode(L"connection-help");
            const auto* loading = YtNode(L"loading-detail");
            const auto* pairingCode = YtNode(L"pairing-code");
            const auto* detail = help ? help : loading;
            if (detail) {
                DrawTextLine(detail->text, bodyFormat_.Get(),
                             D2D1::RectF(panelLeft + 34, 130,
                                         panelLeft + panelWidth - 34, 184),
                             secondaryBrush_.Get());
            }
            if (pairingCode) {
                DrawTextLine(pairingCode->text, titleFormat_.Get(),
                             D2D1::RectF(panelLeft + 34, 188,
                                         panelLeft + panelWidth - 34, 230),
                             accentBrush_.Get());
            }
            DrawWidgetButtonRow(YtNode(L"connection-actions"), panelLeft + 34, 238,
                                panelWidth - 68, 58);
        }

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

    void DrawWidget(const float width, const float height) {
        const std::wstring_view widget = state_.activeWidget();
        const float panelWidth = std::min(widget == L"yt-music" ? 880.0F : 720.0F, width - 72.0F);
        const float panelLeft = (width - panelWidth) / 2.0F;
        const float panelBottom = height - 158.0F;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(panelLeft, 20.0F, panelLeft + panelWidth, panelBottom),
            18.0F, 18.0F};
        renderTarget_->FillRoundedRectangle(panel, cardBrush_.Get());

        if (widget == L"yt-music") {
            if (ytMusicSnapshot_ && declarativeRenderer_) {
                const gba::declarative::Rect viewport{
                    panelLeft + 1.0F,
                    21.0F,
                    panelWidth - 2.0F,
                    panelBottom - 76.0F,
                };
                auto result = declarativeRenderer_->Render(
                    renderTarget_.Get(), *ytMusicSnapshot_, focusedElementId_, viewport);
                lastWidgetRenderResult_ = result;
                if (ytMusicSnapshot_->sequence != lastRenderedSnapshotSequence_) {
                    for (const auto& diagnostic : result.diagnostics) {
                        AppendDiagnostic(
                            L"Renderer " + diagnostic.code + L" [" + diagnostic.nodeId +
                            L"] " + diagnostic.message);
                    }
                    lastRenderedSnapshotSequence_ = ytMusicSnapshot_->sequence;
                }
            } else {
                DrawTextLine(L"Starting isolated YT Music widget…", bodyFormat_.Get(),
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
    HWND previousForeground_{};
    HWND backdropWindow_{};
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
    std::optional<gba::WidgetSnapshot> ytMusicSnapshot_;
    gba::WidgetBridgeClient bridge_;
    std::vector<gba::WidgetDescriptor> widgetDescriptors_;
    std::wstring lifecycleBridgeWidget_;
    std::optional<gba::WidgetLifecycleState> lifecycleBridgeState_;
    std::unique_ptr<gba::RemoteImageCache> imageCache_;
    std::unique_ptr<gba::DeclarativeRenderer> declarativeRenderer_;
    std::wstring artworkUrl_;
    bool runtimeInitialized_{};
    long long lastRenderedSnapshotSequence_{-1};
    gba::RenderResult lastWidgetRenderResult_;

    ComPtr<IGameInput> gameInput_;
    gba::input::XInputGuideCompatibility guideCompatibility_;
    GameInputCallbackToken guideCallback_{};
    ComPtr<ID2D1Factory> d2dFactory_;
    ComPtr<IDWriteFactory> writeFactory_;
    ComPtr<ID2D1HwndRenderTarget> renderTarget_;
    ComPtr<ID2D1Bitmap> artworkBitmap_;
    ComPtr<ID2D1SolidColorBrush> backgroundBrush_;
    ComPtr<ID2D1SolidColorBrush> cardBrush_;
    ComPtr<ID2D1SolidColorBrush> textBrush_;
    ComPtr<ID2D1SolidColorBrush> secondaryBrush_;
    ComPtr<ID2D1SolidColorBrush> accentBrush_;
    ComPtr<ID2D1SolidColorBrush> successBrush_;
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
