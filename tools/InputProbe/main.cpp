#include <windows.h>
#include <shellapi.h>
#include <xinput.h>

#if __has_include(<GameInput.h>)
#include <GameInput.h>
#define INPUT_PROBE_HAS_GAMEINPUT 1
#else
#define INPUT_PROBE_HAS_GAMEINPUT 0
#endif

#include <array>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cwctype>
#include <fstream>
#include <iomanip>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

namespace {

constexpr UINT kPollTimer = 1;
constexpr UINT kFinishTimer = 2;
constexpr UINT kGuideToggleMessage = WM_APP + 1;
constexpr WORD kUndocumentedXInputGuideBit = 0x0400;
constexpr size_t kRawHidDiffMaximumBytes = 128;
constexpr unsigned kRawHidDiffMaximumEventsPerDevice = 128;

enum class Mode { Overlay, Observer };
enum class FocusPolicy { Default, NoBackground, Exclusive };

#if INPUT_PROBE_HAS_GAMEINPUT
bool IsGuideFallbackLabel(const GameInputLabel label) {
    return label == GameInputLabelXboxGuide ||
           label == GameInputLabelIconHome ||
           label == GameInputLabelHome ||
           label == GameInputLabelGuide;
}

const char* GuideFallbackLabelName(const GameInputLabel label) {
    switch (label) {
    case GameInputLabelXboxGuide: return "XboxGuide";
    case GameInputLabelIconHome: return "IconHome";
    case GameInputLabelHome: return "Home";
    case GameInputLabelGuide: return "Guide";
    default: return nullptr;
    }
}

std::string GameInputStringText(const GameInputString* value) {
    if (!value || !value->data || value->sizeInBytes == 0) return "unknown";
    size_t length = value->sizeInBytes;
    while (length && value->data[length - 1] == '\0') --length;
    return std::string(value->data, length);
}
#endif

struct Options {
    Mode mode = Mode::Overlay;
    FocusPolicy focusPolicy = FocusPolicy::Exclusive;
    unsigned durationSeconds = 120;
    std::wstring logPath;
    bool rawHidDiff2dc83106 = false;
};

std::wstring Utf8ToWide(const std::string& text) {
    if (text.empty()) return {};
    const int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring result(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size);
    return result;
}

std::string WideToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size, nullptr, nullptr);
    return result;
}

std::string NowText() {
    SYSTEMTIME now{};
    GetLocalTime(&now);
    char buffer[64]{};
    std::snprintf(buffer, sizeof(buffer), "%04u-%02u-%02uT%02u:%02u:%02u.%03u",
        now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
    return buffer;
}

std::wstring DefaultLogPath(Mode mode) {
    SYSTEMTIME now{};
    GetLocalTime(&now);
    wchar_t name[128]{};
    swprintf_s(name, L"input-probe-%s-%04u%02u%02u-%02u%02u%02u-pid%lu.log",
        mode == Mode::Overlay ? L"overlay" : L"observer",
        now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, GetCurrentProcessId());
    wchar_t cwd[MAX_PATH]{};
    GetCurrentDirectoryW(MAX_PATH, cwd);
    return std::wstring(cwd) + L"\\" + name;
}

class Logger {
public:
    bool Open(const std::wstring& path) {
        path_ = path;
        file_.open(path, std::ios::out | std::ios::trunc);
        return file_.is_open();
    }

    void Write(const std::string& source, const std::string& event) {
        std::lock_guard<std::mutex> lock(mutex_);
        std::ostringstream line;
        line << NowText() << " pid=" << GetCurrentProcessId() << " [" << source << "] " << event;
        std::printf("%s\n", line.str().c_str());
        std::fflush(stdout);
        if (file_) {
            file_ << line.str() << '\n';
            file_.flush();
        }
    }

    const std::wstring& Path() const { return path_; }

private:
    std::mutex mutex_;
    std::ofstream file_;
    std::wstring path_;
};

using XInputGetStateFn = DWORD(WINAPI*)(DWORD, XINPUT_STATE*);

class ProbeApp {
public:
    explicit ProbeApp(Options options) : options_(std::move(options)) {}

    int Run(HINSTANCE instance, int showCommand) {
        if (options_.logPath.empty()) options_.logPath = DefaultLogPath(options_.mode);
        if (!logger_.Open(options_.logPath)) {
            std::fwprintf(stderr, L"Unable to open log: %s\n", options_.logPath.c_str());
            return 2;
        }

        logger_.Write("SESSION", "start mode=" + ModeName() + " focus_policy=" + PolicyName() +
            " duration_seconds=" + std::to_string(options_.durationSeconds) +
            " build_gameinput=" + std::string(INPUT_PROBE_HAS_GAMEINPUT ? "yes" : "no"));
        std::wprintf(L"Log: %s\n", logger_.Path().c_str());

        WNDCLASSEXW windowClass{sizeof(windowClass)};
        windowClass.lpfnWndProc = WindowProcThunk;
        windowClass.hInstance = instance;
        windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        windowClass.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        windowClass.lpszClassName = L"GameBarAlternative.InputProbe";
        if (!RegisterClassExW(&windowClass)) return 3;

        const DWORD exStyle = options_.mode == Mode::Overlay ? WS_EX_TOPMOST : 0;
        hwnd_ = CreateWindowExW(exStyle, windowClass.lpszClassName,
            options_.mode == Mode::Overlay ? L"Input Probe - OVERLAY" : L"Input Probe - BACKGROUND OBSERVER",
            WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 760, 430,
            nullptr, nullptr, instance, this);
        if (!hwnd_) return 4;

        RegisterRawInput();
        InitializeXInput();
        InitializeGameInput();

        ShowWindow(hwnd_, showCommand);
        UpdateWindow(hwnd_);
        if (options_.mode == Mode::Overlay) ActivateOverlay();

        started_ = std::chrono::steady_clock::now();
        SetTimer(hwnd_, kPollTimer, 8, nullptr);
        SetTimer(hwnd_, kFinishTimer, 250, nullptr);
        PollForegroundWindow(true);

        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }

        ShutdownGameInput();
        if (xinputModule_) FreeLibrary(xinputModule_);
        logger_.Write("SESSION", "finish exit_code=" + std::to_string(static_cast<int>(message.wParam)));
        return static_cast<int>(message.wParam);
    }

private:
    static LRESULT CALLBACK WindowProcThunk(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
        ProbeApp* self = reinterpret_cast<ProbeApp*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            self = static_cast<ProbeApp*>(create->lpCreateParams);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        return self ? self->WindowProc(hwnd, message, wParam, lParam) : DefWindowProcW(hwnd, message, wParam, lParam);
    }

    LRESULT WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
        switch (message) {
        case WM_ACTIVATEAPP:
            logger_.Write("WINDOW", std::string("activate_app active=") + (wParam ? "yes" : "no"));
            InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        case WM_SETFOCUS:
            logger_.Write("WINDOW", "keyboard_focus=gained");
            InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        case WM_KILLFOCUS:
            logger_.Write("WINDOW", "keyboard_focus=lost");
            InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        case WM_INPUT:
            HandleRawInput(reinterpret_cast<HRAWINPUT>(lParam));
            return DefWindowProcW(hwnd, message, wParam, lParam);
        case WM_INPUT_DEVICE_CHANGE:
            logger_.Write("RAWINPUT", std::string("device_") + (wParam == GIDC_ARRIVAL ? "arrived" : "removed"));
            return 0;
        case WM_TIMER:
            if (wParam == kPollTimer) {
                PollGameInput();
                PollXInput();
            } else if (wParam == kFinishTimer) {
                PollForegroundWindow(false);
                CheckDuration();
                InvalidateRect(hwnd, nullptr, TRUE);
            }
            return 0;
        case WM_KEYDOWN:
            if (wParam == VK_ESCAPE) DestroyWindow(hwnd);
            if (wParam == VK_F8 && options_.mode == Mode::Overlay) ToggleOverlay();
            return 0;
        case kGuideToggleMessage:
            if (options_.mode == Mode::Overlay) ToggleOverlay();
            return 0;
        case WM_PAINT:
            Paint(hwnd);
            return 0;
        case WM_DESTROY:
            KillTimer(hwnd, kPollTimer);
            KillTimer(hwnd, kFinishTimer);
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    void Paint(HWND hwnd) {
        PAINTSTRUCT paint{};
        HDC dc = BeginPaint(hwnd, &paint);
        RECT client{};
        GetClientRect(hwnd, &client);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(24, 24, 28));
        HFONT font = CreateFontW(20, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
            OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        HGDIOBJ oldFont = SelectObject(dc, font);

        const bool foreground = GetForegroundWindow() == hwnd_;
        const auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(std::chrono::steady_clock::now() - started_).count();
        std::wostringstream text;
        text << L"Game Bar Alternative - controller/input feasibility probe\n\n"
             << L"Mode: " << (options_.mode == Mode::Overlay ? L"OVERLAY (topmost focus target)" : L"BACKGROUND OBSERVER") << L"\n"
             << L"Probe foreground: " << (foreground ? L"YES" : L"NO") << L"\n"
             << L"GameInput: " << (gameInputReady_ ? L"active" : L"unavailable") << L"; policy: " << Utf8ToWide(PolicyName()) << L"\n"
             << L"XInput: " << (xinputGetState_ ? L"active" : L"unavailable") << L"; Raw Input: " << (rawInputReady_ ? L"registered" : L"registration failed") << L"\n"
             << L"Elapsed: " << elapsed << L" s / " << (options_.durationSeconds ? std::to_wstring(options_.durationSeconds) : L"unlimited") << L"\n\n"
             << L"Guide toggles the overlay probe when GameInput delivers it.\n"
             << L"F8 toggles it as a deterministic fallback. Escape exits.\n"
             << L"All state changes are timestamped in the console and log.\n\n"
             << L"Foreground window: " << foregroundDescription_ << L"\n"
             << L"Log: " << logger_.Path();
        RECT textRect = client;
        InflateRect(&textRect, -24, -24);
        DrawTextW(dc, text.str().c_str(), -1, &textRect, DT_LEFT | DT_TOP | DT_WORDBREAK);

        SelectObject(dc, oldFont);
        DeleteObject(font);
        EndPaint(hwnd, &paint);
    }

    void RegisterRawInput() {
        RAWINPUTDEVICE devices[3]{};
        const USHORT usages[] = {0x04, 0x05, 0x08}; // joystick, gamepad, multi-axis controller
        for (size_t i = 0; i < std::size(devices); ++i) {
            devices[i].usUsagePage = 0x01;
            devices[i].usUsage = usages[i];
            devices[i].dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY;
            devices[i].hwndTarget = hwnd_;
        }
        rawInputReady_ = RegisterRawInputDevices(devices, static_cast<UINT>(std::size(devices)), sizeof(RAWINPUTDEVICE)) == TRUE;
        logger_.Write("RAWINPUT", std::string("register_background_sink result=") + (rawInputReady_ ? "ok" : "failed") +
            " win32_error=" + std::to_string(rawInputReady_ ? 0 : GetLastError()));
    }

    std::wstring RawDeviceName(HANDLE device) {
        auto found = rawDeviceNames_.find(device);
        if (found != rawDeviceNames_.end()) return found->second;
        UINT size = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, nullptr, &size);
        std::wstring name(size ? size : 1, L'\0');
        if (size && GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, name.data(), &size) != static_cast<UINT>(-1)) {
            while (!name.empty() && name.back() == L'\0') name.pop_back();
        } else {
            name = L"unknown";
        }
        rawDeviceNames_[device] = name;
        return name;
    }

    void HandleRawInput(HRAWINPUT handle) {
        UINT size = 0;
        if (GetRawInputData(handle, RID_INPUT, nullptr, &size, sizeof(RAWINPUTHEADER)) != 0 || !size) return;
        std::vector<std::byte> buffer(size);
        if (GetRawInputData(handle, RID_INPUT, buffer.data(), &size, sizeof(RAWINPUTHEADER)) != size) return;
        const auto* input = reinterpret_cast<const RAWINPUT*>(buffer.data());
        if (input->header.dwType != RIM_TYPEHID) return;

        const size_t byteCount = static_cast<size_t>(input->data.hid.dwSizeHid) * input->data.hid.dwCount;
        const auto* bytes = input->data.hid.bRawData;
        uint64_t hash = 1469598103934665603ULL;
        for (size_t i = 0; i < byteCount; ++i) {
            hash ^= bytes[i];
            hash *= 1099511628211ULL;
        }
        if (lastRawHash_[input->header.hDevice] == hash) return;
        lastRawHash_[input->header.hDevice] = hash;

        std::ostringstream event;
        event << "hid_report foreground=" << (GetForegroundWindow() == hwnd_ ? "yes" : "no")
              << " bytes=" << byteCount << " reports=" << input->data.hid.dwCount
              << " hash=0x" << std::hex << hash << std::dec
              << " device=\"" << WideToUtf8(RawDeviceName(input->header.hDevice)) << "\"";
        logger_.Write("RAWINPUT", event.str());

        const std::wstring deviceName = RawDeviceName(input->header.hDevice);
        if (options_.rawHidDiff2dc83106 && Is2dc83106(deviceName)) {
            LogRawHidDiff(input->header.hDevice, bytes, byteCount, deviceName);
        }
    }

    static bool Is2dc83106(const std::wstring& deviceName) {
        std::wstring upper(deviceName);
        std::transform(upper.begin(), upper.end(), upper.begin(),
            [](const wchar_t value) { return static_cast<wchar_t>(std::towupper(value)); });
        return upper.find(L"VID_2DC8") != std::wstring::npos &&
               upper.find(L"PID_3106") != std::wstring::npos;
    }

    void LogRawHidDiff(HANDLE device, const BYTE* bytes, const size_t byteCount,
        const std::wstring& deviceName) {
        auto& eventCount = rawDiffEventCounts_[device];
        if (eventCount >= kRawHidDiffMaximumEventsPerDevice) {
            if (eventCount == kRawHidDiffMaximumEventsPerDevice) {
                logger_.Write("RAW_HID_DIFF_2DC8_3106",
                    "event_limit_reached limit=" + std::to_string(kRawHidDiffMaximumEventsPerDevice));
                ++eventCount;
            }
            return;
        }
        ++eventCount;

        const size_t captured = std::min(byteCount, kRawHidDiffMaximumBytes);
        std::vector<uint8_t> current(bytes, bytes + captured);
        auto& previous = rawDiffPreviousReports_[device];
        std::ostringstream event;
        event << "diagnostic_only=yes undocumented_mapping=yes device=\""
              << WideToUtf8(deviceName) << "\" report_bytes=" << byteCount
              << " captured_bytes=" << captured;
        if (previous.empty()) {
            event << " baseline=";
            for (const uint8_t value : current) {
                event << std::hex << std::setw(2) << std::setfill('0')
                      << static_cast<unsigned>(value);
            }
        } else {
            event << " changes=[";
            bool wroteChange = false;
            const size_t comparable = std::min(previous.size(), current.size());
            for (size_t index = 0; index < comparable; ++index) {
                if (previous[index] == current[index]) continue;
                if (wroteChange) event << ',';
                wroteChange = true;
                event << std::dec << index << ':' << std::hex << std::setw(2)
                      << std::setfill('0') << static_cast<unsigned>(previous[index])
                      << "->" << std::setw(2) << static_cast<unsigned>(current[index]);
            }
            if (previous.size() != current.size()) {
                if (wroteChange) event << ',';
                event << "size:" << std::dec << previous.size() << "->" << current.size();
            }
            event << ']';
        }
        logger_.Write("RAW_HID_DIFF_2DC8_3106", event.str());
        previous = std::move(current);
    }

    void InitializeXInput() {
        constexpr const wchar_t* dlls[] = {L"xinput1_4.dll", L"xinput9_1_0.dll", L"xinput1_3.dll"};
        for (const auto* dll : dlls) {
            xinputModule_ = LoadLibraryW(dll);
            if (!xinputModule_) continue;
            xinputGetState_ = reinterpret_cast<XInputGetStateFn>(GetProcAddress(xinputModule_, "XInputGetState"));
            if (xinputGetState_) {
                xinputGetStateEx_ = reinterpret_cast<XInputGetStateFn>(
                    GetProcAddress(xinputModule_, MAKEINTRESOURCEA(100)));
                logger_.Write("XINPUT", "runtime_loaded dll=" + WideToUtf8(dll));
                logger_.Write("XINPUT_UNDOCUMENTED",
                    std::string("diagnostic_get_state_ex_ordinal_100=") +
                    (xinputGetStateEx_ ? "available" : "unavailable") +
                    " production_contract=no");
                return;
            }
            FreeLibrary(xinputModule_);
            xinputModule_ = nullptr;
        }
        logger_.Write("XINPUT", "runtime_unavailable");
    }

    void PollXInput() {
        if (!xinputGetState_) return;
        for (DWORD user = 0; user < XUSER_MAX_COUNT; ++user) {
            XINPUT_STATE state{};
            const DWORD result = xinputGetState_(user, &state);
            const bool connected = result == ERROR_SUCCESS;
            if (connected != xinputConnected_[user]) {
                xinputConnected_[user] = connected;
                logger_.Write("XINPUT", "user=" + std::to_string(user) + " connected=" + (connected ? "yes" : "no"));
            }
            if (!connected || state.dwPacketNumber == xinputPackets_[user]) continue;
            xinputPackets_[user] = state.dwPacketNumber;
            std::ostringstream event;
            event << "state user=" << user << " foreground=" << (GetForegroundWindow() == hwnd_ ? "yes" : "no")
                  << " packet=" << state.dwPacketNumber << " buttons=0x" << std::hex << state.Gamepad.wButtons << std::dec
                  << " lt=" << static_cast<unsigned>(state.Gamepad.bLeftTrigger)
                  << " rt=" << static_cast<unsigned>(state.Gamepad.bRightTrigger)
                  << " lx=" << state.Gamepad.sThumbLX << " ly=" << state.Gamepad.sThumbLY
                  << " rx=" << state.Gamepad.sThumbRX << " ry=" << state.Gamepad.sThumbRY;
            logger_.Write("XINPUT", event.str());
        }

        if (!xinputGetStateEx_) return;
        for (DWORD user = 0; user < XUSER_MAX_COUNT; ++user) {
            XINPUT_STATE state{};
            const DWORD result = xinputGetStateEx_(user, &state);
            const bool connected = result == ERROR_SUCCESS;
            if (connected != xinputExConnected_[user]) {
                xinputExConnected_[user] = connected;
                if (!connected) xinputExHasState_[user] = false;
                logger_.Write("XINPUT_UNDOCUMENTED", "get_state_ex user=" +
                    std::to_string(user) + " connected=" + (connected ? "yes" : "no") +
                    " production_contract=no");
            }
            if (!connected || state.dwPacketNumber == xinputExPackets_[user]) continue;
            xinputExPackets_[user] = state.dwPacketNumber;
            const bool buttonsChanged = !xinputExHasState_[user] ||
                state.Gamepad.wButtons != xinputExButtons_[user];
            xinputExHasState_[user] = true;
            xinputExButtons_[user] = state.Gamepad.wButtons;
            if (!buttonsChanged) continue;
            std::ostringstream event;
            event << "get_state_ex ordinal=100 production_contract=no user=" << user
                  << " foreground=" << (GetForegroundWindow() == hwnd_ ? "yes" : "no")
                  << " packet=" << state.dwPacketNumber
                  << " buttons=0x" << std::hex << state.Gamepad.wButtons << std::dec
                  << " guide_bit_0x0400="
                  << ((state.Gamepad.wButtons & kUndocumentedXInputGuideBit) ? "pressed" : "released");
            logger_.Write("XINPUT_UNDOCUMENTED", event.str());
        }
    }

    void InitializeGameInput() {
#if INPUT_PROBE_HAS_GAMEINPUT
        using CreateFn = HRESULT(WINAPI*)(IGameInput**);
        CreateFn create = nullptr;
        // Match the production package loader's preference for the newer
        // redistributable on machines that also have an older inbox runtime.
        constexpr const wchar_t* runtimes[] = {L"GameInputRedist.dll", L"GameInput.dll"};
        for (const auto* runtime : runtimes) {
            gameInputModule_ = LoadLibraryW(runtime);
            if (!gameInputModule_) continue;
            create = reinterpret_cast<CreateFn>(GetProcAddress(gameInputModule_, "GameInputCreate"));
            if (create) {
                logger_.Write("GAMEINPUT", "runtime_loaded dll=" + WideToUtf8(runtime));
                break;
            }
            FreeLibrary(gameInputModule_);
            gameInputModule_ = nullptr;
        }
        if (!gameInputModule_ || !create) {
            logger_.Write("GAMEINPUT", "runtime_unavailable win32_error=" + std::to_string(GetLastError()));
            return;
        }
        const HRESULT result = create(&gameInput_);
        if (FAILED(result) || !gameInput_) {
            std::ostringstream event;
            event << "create_failed hresult=0x" << std::hex << static_cast<unsigned long>(result);
            logger_.Write("GAMEINPUT", event.str());
            return;
        }

        GameInputFocusPolicy policy = GameInputDefaultFocusPolicy;
        if (options_.focusPolicy == FocusPolicy::NoBackground) {
            policy = static_cast<GameInputFocusPolicy>(GameInputDisableBackgroundInput |
                GameInputDisableBackgroundGuideButton | GameInputDisableBackgroundShareButton);
        } else if (options_.focusPolicy == FocusPolicy::Exclusive) {
            policy = static_cast<GameInputFocusPolicy>(GameInputExclusiveForegroundInput |
                GameInputExclusiveForegroundGuideButton | GameInputExclusiveForegroundShareButton);
        }
        gameInput_->SetFocusPolicy(policy);
        const HRESULT callbackResult = gameInput_->RegisterSystemButtonCallback(nullptr,
            static_cast<GameInputSystemButtons>(GameInputSystemButtonGuide | GameInputSystemButtonShare),
            this, GameInputSystemButtonThunk, &gameInputCallbackToken_);
        gameInputReady_ = SUCCEEDED(callbackResult);
        std::ostringstream event;
        event << "initialized policy=" << PolicyName() << " callback_hresult=0x" << std::hex
              << static_cast<unsigned long>(callbackResult);
        logger_.Write("GAMEINPUT", event.str());

        const HRESULT deviceCallbackResult = gameInput_->RegisterDeviceCallback(
            nullptr,
            GameInputKindControllerButton,
            GameInputDeviceConnected,
            GameInputAsyncEnumeration,
            this,
            GameInputDeviceThunk,
            &gameInputDeviceCallbackToken_);
        std::ostringstream deviceEvent;
        deviceEvent << "controller_device_callback_hresult=0x" << std::hex
                    << static_cast<unsigned long>(deviceCallbackResult);
        logger_.Write("GAMEINPUT", deviceEvent.str());
#else
        logger_.Write("GAMEINPUT", "not_compiled header_missing");
#endif
    }

#if INPUT_PROBE_HAS_GAMEINPUT
    static void CALLBACK GameInputSystemButtonThunk(GameInputCallbackToken, void* context, IGameInputDevice*,
        uint64_t timestamp, GameInputSystemButtons current, GameInputSystemButtons previous) {
        static_cast<ProbeApp*>(context)->HandleSystemButtons(timestamp, current, previous);
    }

    static void CALLBACK GameInputDeviceThunk(GameInputCallbackToken, void* context, IGameInputDevice* device,
        uint64_t timestamp, GameInputDeviceStatus current, GameInputDeviceStatus previous) {
        static_cast<ProbeApp*>(context)->HandleGameInputDevice(device, timestamp, current, previous);
    }

    void HandleSystemButtons(uint64_t timestamp, GameInputSystemButtons current, GameInputSystemButtons previous) {
        const auto describe = [](GameInputSystemButtons buttons) {
            std::string result;
            if (buttons & GameInputSystemButtonGuide) result += "Guide|";
            if (buttons & GameInputSystemButtonShare) result += "Share|";
            if (result.empty()) return std::string("None");
            result.pop_back();
            return result;
        };
        logger_.Write("GAMEINPUT", "system_button timestamp=" + std::to_string(timestamp) +
            " foreground=" + (GetForegroundWindow() == hwnd_ ? std::string("yes") : std::string("no")) +
            " current=" + describe(current) + " previous=" + describe(previous));
        if ((current & GameInputSystemButtonGuide) && !(previous & GameInputSystemButtonGuide)) {
            DispatchGuideSignal("system-button", timestamp);
        }
    }

    struct ControllerButtonDevice {
        IGameInputDevice* device = nullptr;
        uint16_t vendorId = 0;
        uint16_t productId = 0;
        std::string name;
        std::vector<GameInputLabel> labels;
        std::vector<uint8_t> previousState;
        uint64_t lastTimestamp = 0;
        bool hasBaseline = false;
    };

    void HandleGameInputDevice(IGameInputDevice* device, uint64_t timestamp,
        GameInputDeviceStatus current, GameInputDeviceStatus previous) {
        if (!device) return;
        const bool connected = (current & GameInputDeviceConnected) != 0;
        std::lock_guard<std::mutex> lock(controllerDevicesMutex_);
        const auto existing = std::find_if(controllerDevices_.begin(), controllerDevices_.end(),
            [device](const ControllerButtonDevice& entry) { return entry.device == device; });

        if (!connected) {
            if (existing != controllerDevices_.end()) {
                logger_.Write("GAMEINPUT", "controller_device disconnected timestamp=" +
                    std::to_string(timestamp) + " name=\"" + existing->name + "\"");
                existing->device->Release();
                controllerDevices_.erase(existing);
            }
            return;
        }
        if (existing != controllerDevices_.end()) return;

        const GameInputDeviceInfo* info = device->GetDeviceInfo();
        if (!info) {
            logger_.Write("GAMEINPUT", "controller_device missing_device_info timestamp=" +
                std::to_string(timestamp));
            return;
        }

        ControllerButtonDevice entry;
        entry.device = device;
        entry.device->AddRef();
        entry.vendorId = info->vendorId;
        entry.productId = info->productId;
        entry.name = GameInputStringText(info->displayName);
        entry.labels.reserve(info->controllerButtonCount);
        entry.previousState.resize(info->controllerButtonCount);

        std::ostringstream labels;
        labels << '[';
        for (uint32_t index = 0; index < info->controllerButtonCount; ++index) {
            const GameInputLabel label = info->controllerButtonInfo
                ? info->controllerButtonInfo[index].label
                : GameInputLabelUnknown;
            entry.labels.push_back(label);
            if (index) labels << ',';
            labels << index << ':' << static_cast<int>(label);
            if (const char* name = GuideFallbackLabelName(label)) labels << '(' << name << ",guide-fallback)";
        }
        labels << ']';

        std::ostringstream event;
        event << "controller_device connected timestamp=" << timestamp
              << " previous_status=0x" << std::hex << static_cast<unsigned>(previous)
              << " current_status=0x" << static_cast<unsigned>(current)
              << " vid=0x" << std::setw(4) << std::setfill('0') << entry.vendorId
              << " pid=0x" << std::setw(4) << entry.productId
              << " supported_input=0x" << static_cast<unsigned>(info->supportedInput)
              << " supported_system_buttons=0x" << static_cast<unsigned>(info->supportedSystemButtons)
              << std::dec << std::setfill(' ')
              << " name=\"" << entry.name << "\" labels=" << labels.str();
        logger_.Write("GAMEINPUT", event.str());
        controllerDevices_.push_back(std::move(entry));
    }

    void DispatchGuideSignal(const char* source, uint64_t timestamp) {
        {
            std::lock_guard<std::mutex> lock(guideSignalMutex_);
            const uint64_t delta = timestamp >= lastGuideSignalTimestamp_
                ? timestamp - lastGuideSignalTimestamp_
                : lastGuideSignalTimestamp_ - timestamp;
            if (lastGuideSignalTimestamp_ != 0 && lastGuideSignalSource_ != source && delta <= 100000) {
                logger_.Write("GAMEINPUT", "guide_signal deduplicated source=" + std::string(source) +
                    " timestamp=" + std::to_string(timestamp) + " prior_source=" + lastGuideSignalSource_);
                return;
            }
            lastGuideSignalTimestamp_ = timestamp;
            lastGuideSignalSource_ = source;
        }
        logger_.Write("GAMEINPUT", "guide_signal dispatched source=" + std::string(source) +
            " timestamp=" + std::to_string(timestamp));
        PostMessageW(hwnd_, kGuideToggleMessage, 0, 0);
    }

    void PollControllerButtons() {
        std::lock_guard<std::mutex> lock(controllerDevicesMutex_);
        for (auto& entry : controllerDevices_) {
            IGameInputReading* reading = nullptr;
            const HRESULT result = gameInput_->GetCurrentReading(
                GameInputKindControllerButton, entry.device, &reading);
            if (FAILED(result) || !reading) continue;

            const uint64_t timestamp = reading->GetTimestamp();
            if (timestamp == entry.lastTimestamp) {
                reading->Release();
                continue;
            }
            entry.lastTimestamp = timestamp;
            const uint32_t count = reading->GetControllerButtonCount();
            std::unique_ptr<bool[]> state(count ? new bool[count]{} : nullptr);
            const uint32_t valid = count ? reading->GetControllerButtonState(count, state.get()) : 0;
            reading->Release();

            const size_t comparable = std::min<size_t>({
                valid, entry.labels.size(), entry.previousState.size()});
            if (!entry.hasBaseline) {
                for (size_t index = 0; index < comparable; ++index)
                    entry.previousState[index] = state[index] ? 1 : 0;
                entry.hasBaseline = true;
                logger_.Write("GAMEINPUT", "controller_button baseline timestamp=" +
                    std::to_string(timestamp) + " name=\"" + entry.name + "\" count=" +
                    std::to_string(valid));
                continue;
            }

            for (size_t index = 0; index < comparable; ++index) {
                const bool pressed = state[index];
                const bool wasPressed = entry.previousState[index] != 0;
                entry.previousState[index] = pressed ? 1 : 0;
                if (!pressed || wasPressed) continue;

                const GameInputLabel label = entry.labels[index];
                std::ostringstream event;
                event << "controller_button rising timestamp=" << timestamp
                      << " name=\"" << entry.name << "\" index=" << index
                      << " label=" << static_cast<int>(label)
                      << " guide_fallback=" << (IsGuideFallbackLabel(label) ? "yes" : "no");
                if (const char* name = GuideFallbackLabelName(label)) event << " label_name=" << name;
                logger_.Write("GAMEINPUT", event.str());
                if (IsGuideFallbackLabel(label)) DispatchGuideSignal("labeled-controller-button", timestamp);
            }
        }
    }
#endif

    void PollGameInput() {
#if INPUT_PROBE_HAS_GAMEINPUT
        if (!gameInput_) return;
        IGameInputReading* reading = nullptr;
        const HRESULT result = gameInput_->GetCurrentReading(GameInputKindGamepad, nullptr, &reading);
        if (SUCCEEDED(result) && reading) {
            const uint64_t timestamp = reading->GetTimestamp();
            if (timestamp != lastGameInputTimestamp_) {
                lastGameInputTimestamp_ = timestamp;
                GameInputGamepadState state{};
                if (reading->GetGamepadState(&state)) {
                    std::ostringstream event;
                    event << "gamepad_state timestamp=" << timestamp
                          << " foreground=" << (GetForegroundWindow() == hwnd_ ? "yes" : "no")
                          << " buttons=0x" << std::hex << static_cast<unsigned>(state.buttons) << std::dec
                          << std::fixed << std::setprecision(3)
                          << " lt=" << state.leftTrigger << " rt=" << state.rightTrigger
                          << " lx=" << state.leftThumbstickX << " ly=" << state.leftThumbstickY
                          << " rx=" << state.rightThumbstickX << " ry=" << state.rightThumbstickY;
                    logger_.Write("GAMEINPUT", event.str());
                }
            }
            reading->Release();
        }
        PollControllerButtons();
#endif
    }

    void ShutdownGameInput() {
#if INPUT_PROBE_HAS_GAMEINPUT
        if (gameInput_ && gameInputDeviceCallbackToken_ != GAMEINPUT_INVALID_CALLBACK_TOKEN_VALUE) {
            gameInput_->StopCallback(gameInputDeviceCallbackToken_);
            gameInput_->UnregisterCallback(gameInputDeviceCallbackToken_, 5000000);
            gameInputDeviceCallbackToken_ = GAMEINPUT_INVALID_CALLBACK_TOKEN_VALUE;
        }
        if (gameInput_ && gameInputCallbackToken_ != GAMEINPUT_INVALID_CALLBACK_TOKEN_VALUE) {
            gameInput_->StopCallback(gameInputCallbackToken_);
            gameInput_->UnregisterCallback(gameInputCallbackToken_, 5000000);
            gameInputCallbackToken_ = GAMEINPUT_INVALID_CALLBACK_TOKEN_VALUE;
        }
        {
            std::lock_guard<std::mutex> lock(controllerDevicesMutex_);
            for (auto& entry : controllerDevices_) entry.device->Release();
            controllerDevices_.clear();
        }
        if (gameInput_) gameInput_->Release();
        gameInput_ = nullptr;
        if (gameInputModule_) FreeLibrary(gameInputModule_);
        gameInputModule_ = nullptr;
#endif
    }

    void PollForegroundWindow(bool force) {
        const HWND foreground = GetForegroundWindow();
        if (!force && foreground == lastForeground_) return;
        lastForeground_ = foreground;
        wchar_t title[256]{};
        GetWindowTextW(foreground, title, static_cast<int>(std::size(title)));
        DWORD pid = 0;
        GetWindowThreadProcessId(foreground, &pid);
        std::wostringstream description;
        description << L"pid=" << pid << L" hwnd=0x" << std::hex << reinterpret_cast<uintptr_t>(foreground)
                    << std::dec << L" title=\"" << title << L"\"";
        foregroundDescription_ = description.str();
        logger_.Write("WINDOW", "foreground_changed " + WideToUtf8(foregroundDescription_) +
            " probe_foreground=" + (foreground == hwnd_ ? "yes" : "no"));
    }

    void ActivateOverlay() {
        ShowWindow(hwnd_, SW_RESTORE);
        SetWindowPos(hwnd_, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd_);
        logger_.Write("WINDOW", "overlay_requested=show");
    }

    void ToggleOverlay() {
        if (IsIconic(hwnd_) || !IsWindowVisible(hwnd_) || GetForegroundWindow() != hwnd_) {
            ActivateOverlay();
        } else {
            ShowWindow(hwnd_, SW_MINIMIZE);
            logger_.Write("WINDOW", "overlay_requested=minimize");
        }
    }

    void CheckDuration() {
        if (!options_.durationSeconds) return;
        const auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
            std::chrono::steady_clock::now() - started_).count();
        if (elapsed >= options_.durationSeconds) {
            logger_.Write("SESSION", "duration_elapsed");
            DestroyWindow(hwnd_);
        }
    }

    std::string ModeName() const { return options_.mode == Mode::Overlay ? "overlay" : "observer"; }
    std::string PolicyName() const {
        if (options_.focusPolicy == FocusPolicy::NoBackground) return "no-background";
        if (options_.focusPolicy == FocusPolicy::Exclusive) return "exclusive";
        return "default";
    }

    Options options_;
    HWND hwnd_ = nullptr;
    Logger logger_;
    std::chrono::steady_clock::time_point started_{};
    HWND lastForeground_ = nullptr;
    std::wstring foregroundDescription_ = L"not sampled";

    bool rawInputReady_ = false;
    std::map<HANDLE, std::wstring> rawDeviceNames_;
    std::map<HANDLE, uint64_t> lastRawHash_;
    std::map<HANDLE, std::vector<uint8_t>> rawDiffPreviousReports_;
    std::map<HANDLE, unsigned> rawDiffEventCounts_;

    HMODULE xinputModule_ = nullptr;
    XInputGetStateFn xinputGetState_ = nullptr;
    XInputGetStateFn xinputGetStateEx_ = nullptr;
    std::array<bool, XUSER_MAX_COUNT> xinputConnected_{};
    std::array<DWORD, XUSER_MAX_COUNT> xinputPackets_{};
    std::array<bool, XUSER_MAX_COUNT> xinputExConnected_{};
    std::array<DWORD, XUSER_MAX_COUNT> xinputExPackets_{};
    std::array<bool, XUSER_MAX_COUNT> xinputExHasState_{};
    std::array<WORD, XUSER_MAX_COUNT> xinputExButtons_{};

    bool gameInputReady_ = false;
#if INPUT_PROBE_HAS_GAMEINPUT
    HMODULE gameInputModule_ = nullptr;
    IGameInput* gameInput_ = nullptr;
    GameInputCallbackToken gameInputCallbackToken_ = GAMEINPUT_INVALID_CALLBACK_TOKEN_VALUE;
    GameInputCallbackToken gameInputDeviceCallbackToken_ = GAMEINPUT_INVALID_CALLBACK_TOKEN_VALUE;
    uint64_t lastGameInputTimestamp_ = 0;
    std::mutex controllerDevicesMutex_;
    std::vector<ControllerButtonDevice> controllerDevices_;
    std::mutex guideSignalMutex_;
    uint64_t lastGuideSignalTimestamp_ = 0;
    std::string lastGuideSignalSource_;
#endif
};

void PrintUsage() {
    std::wprintf(
        L"InputProbe [--mode overlay|observer] [--focus-policy default|no-background|exclusive]\n"
        L"           [--duration SECONDS] [--log PATH] [--raw-hid-diff-2dc8-3106]\n\n"
        L"Defaults: overlay mode, exclusive GameInput foreground policy, 120 seconds.\n"
        L"Use --duration 0 only for an intentionally unlimited manual session.\n");
}

bool ParseUnsigned(const wchar_t* text, unsigned& value) {
    wchar_t* end = nullptr;
    const unsigned long parsed = wcstoul(text, &end, 10);
    if (!text[0] || !end || *end != L'\0' || parsed > 86400) return false;
    value = static_cast<unsigned>(parsed);
    return true;
}

bool ParseOptions(int argc, wchar_t** argv, Options& options) {
    for (int i = 1; i < argc; ++i) {
        const std::wstring argument = argv[i];
        if (argument == L"--help" || argument == L"-h") {
            PrintUsage();
            return false;
        }
        if (argument == L"--raw-hid-diff-2dc8-3106") {
            options.rawHidDiff2dc83106 = true;
            continue;
        }
        if (i + 1 >= argc) {
            std::fwprintf(stderr, L"Missing value after %s\n", argument.c_str());
            return false;
        }
        const std::wstring value = argv[++i];
        if (argument == L"--mode") {
            if (value == L"overlay") options.mode = Mode::Overlay;
            else if (value == L"observer") options.mode = Mode::Observer;
            else return false;
        } else if (argument == L"--focus-policy") {
            if (value == L"default") options.focusPolicy = FocusPolicy::Default;
            else if (value == L"no-background") options.focusPolicy = FocusPolicy::NoBackground;
            else if (value == L"exclusive") options.focusPolicy = FocusPolicy::Exclusive;
            else return false;
        } else if (argument == L"--duration") {
            if (!ParseUnsigned(value.c_str(), options.durationSeconds)) return false;
        } else if (argument == L"--log") {
            options.logPath = value;
        } else {
            std::fwprintf(stderr, L"Unknown option: %s\n", argument.c_str());
            return false;
        }
    }
    if (options.mode == Mode::Observer && options.focusPolicy == FocusPolicy::Exclusive) {
        options.focusPolicy = FocusPolicy::Default;
    }
    return true;
}

} // namespace

int wmain(int argc, wchar_t** argv) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    Options options;
    if (!ParseOptions(argc, argv, options)) {
        if (argc <= 1 || (argc > 1 && std::wstring(argv[1]) != L"--help" && std::wstring(argv[1]) != L"-h")) PrintUsage();
        return argc > 1 && (std::wstring(argv[1]) == L"--help" || std::wstring(argv[1]) == L"-h") ? 0 : 1;
    }
    ProbeApp app(std::move(options));
    return app.Run(GetModuleHandleW(nullptr), SW_SHOWNORMAL);
}
