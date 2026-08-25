#include "OverlayCompositionSurface.h"
#include "RichMediaSurfaceCoordinator.h"

#include <Windows.h>
#include <d2d1_1.h>
#include <psapi.h>
#include <TlHelp32.h>
#include <wrl/client.h>

#include <algorithm>
#include <chrono>
#include <filesystem>
#include <iostream>
#include <numeric>
#include <string>
#include <thread>
#include <vector>

using namespace std::chrono_literals;
using Microsoft::WRL::ComPtr;

namespace {

void Require(const bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

template <typename Predicate>
bool PumpUntil(Predicate predicate, const std::chrono::milliseconds timeout) {
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < deadline) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        if (predicate()) return true;
        MsgWaitForMultipleObjectsEx(0, nullptr, 10, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }
    return predicate();
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    return DefWindowProcW(window, message, wParam, lParam);
}

class Fixture final {
public:
    Fixture(const unsigned int ordinal, const bool visible) {
        WNDCLASSW windowClass{};
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpfnWndProc = WindowProc;
        windowClass.lpszClassName = L"WidgetRail.RichMediaProofTests";
        RegisterClassW(&windowClass);
        window_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP,
            windowClass.lpszClassName, L"", WS_POPUP,
            0, 0, 800, 520, nullptr, nullptr, windowClass.hInstance, nullptr);
        Require(window_ != nullptr, "proof owner HWND creation failed");
        HRESULT result = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED,
            factory_.ReleaseAndGetAddressOf());
        Require(SUCCEEDED(result), "D2D factory creation failed");
        std::wstring error;
        Require(composition_.Initialize(window_, factory_.Get(), error),
                "composition owner initialization failed");
        ComPtr<IUnknown> target;
        Require(SUCCEEDED(composition_.CreateExternalContentTarget(&target)),
                "external composition slot creation failed");
        profile_ = std::filesystem::temp_directory_path() /
            (L"wrail-rich-media-proof-test-" + std::to_wstring(GetCurrentProcessId()) +
             L"-" + std::to_wstring(ordinal));
        widgetrail::richmedia::Configuration configuration;
        configuration.ownerWindow = window_;
        configuration.compositionTarget = target;
        configuration.bounds = {0, 0, 800, 520};
        configuration.rasterScale = 1.0;
        configuration.initiallyVisible = visible;
        configuration.ephemeralProfileDirectory = profile_.wstring();
        configuration.diagnostic = [](const std::wstring_view message) {
            std::wcout << L"diagnostic " << message << L'\n' << std::flush;
        };
        Require(SUCCEEDED(coordinator_.Initialize(std::move(configuration))),
                "coordinator initialization submission failed");
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        Require(SUCCEEDED(composition_.CommitExternalContentPresentation(
                    {0, 0, 800, 520}, visible, timing)),
                "external composition presentation failed");
    }

    ~Fixture() { Close(); }

    void Close() {
        if (!window_) return;
        coordinator_.Shutdown();
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        (void)composition_.DetachExternalContentTarget(timing);
        composition_.Reset();
        factory_.Reset();
        DestroyWindow(window_);
        window_ = nullptr;
    }

    widgetrail::richmedia::RichMediaSurfaceCoordinator& coordinator() {
        return coordinator_;
    }
    const std::filesystem::path& profile() const { return profile_; }

private:
    HWND window_{};
    ComPtr<ID2D1Factory1> factory_;
    widgetrail::OverlayCompositionSurface composition_;
    widgetrail::richmedia::RichMediaSurfaceCoordinator coordinator_;
    std::filesystem::path profile_;
};

void RunContractCases() {
    using namespace widgetrail::richmedia;
    State state;
    state.authority = {7, 3};
    State next = state;
    Require(RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","generation":7,"sequence":4,"focus":"seek","playing":false})",
        7, 3, next), "exact current event was rejected");
    Require(next.focusedElement == L"seek" && next.authority.commandSequence == 4,
            "exact event authority was not retained");
    next = state;
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","generation":6,"sequence":4,"focus":"seek","playing":false})",
        7, 3, next), "stale generation was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","generation":7,"sequence":3,"focus":"seek","playing":false})",
        7, 3, next), "reused sequence was admitted");
    Require(!RichMediaSurfaceCoordinator::ValidatePageEvent(
        LR"({"type":"focus","generation":7,"sequence":4,"focus":"seek","playing":false,"script":"bad"})",
        7, 3, next), "unknown page field was admitted");
    const auto command = RichMediaSurfaceCoordinator::CommandJson(
        Command::Activate, {9, 12});
    Require(command == LR"({"command":"activate","generation":9,"sequence":12})",
            "private command encoding drifted");
    std::cout << "RichMediaSurfaceCoordinator contract cases passed=5\n";
}

struct ProcessSample final {
    std::size_t privateBytes{};
    std::uint64_t cpu100ns{};
    std::size_t processCount{};
};

ProcessSample OwnedProcessSample() {
    const DWORD root = GetCurrentProcessId();
    std::vector<DWORD> owned{root};
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    Require(snapshot != INVALID_HANDLE_VALUE, "process snapshot failed");
    PROCESSENTRY32W entry{sizeof(entry)};
    bool changed = true;
    while (changed) {
        changed = false;
        if (Process32FirstW(snapshot, &entry)) {
            do {
                if (std::find(owned.begin(), owned.end(), entry.th32ProcessID) == owned.end() &&
                    std::find(owned.begin(), owned.end(), entry.th32ParentProcessID) != owned.end()) {
                    owned.push_back(entry.th32ProcessID);
                    changed = true;
                }
            } while (Process32NextW(snapshot, &entry));
        }
    }
    CloseHandle(snapshot);
    ProcessSample sample;
    for (const DWORD processId : owned) {
        const HANDLE process = OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, FALSE, processId);
        if (!process) continue;
        PROCESS_MEMORY_COUNTERS_EX counters{sizeof(counters)};
        FILETIME created{}, exited{}, kernel{}, user{};
        if (GetProcessMemoryInfo(
                process, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
                sizeof(counters))) {
            sample.privateBytes += counters.PrivateUsage;
            ++sample.processCount;
        }
        if (GetProcessTimes(process, &created, &exited, &kernel, &user)) {
            ULARGE_INTEGER kernelValue{kernel.dwLowDateTime, kernel.dwHighDateTime};
            ULARGE_INTEGER userValue{user.dwLowDateTime, user.dwHighDateTime};
            sample.cpu100ns += kernelValue.QuadPart + userValue.QuadPart;
        }
        CloseHandle(process);
    }
    return sample;
}

void RunLifecycleAndPerformance() {
    std::vector<long long> coldMilliseconds;
    for (unsigned int run = 0; run < 5; ++run) {
        const auto started = std::chrono::steady_clock::now();
        Fixture fixture(run, true);
        Require(PumpUntil([&] {
            const auto state = fixture.coordinator().state();
            return state.lifecycle == widgetrail::richmedia::Lifecycle::Visible &&
                state.focusedElement == L"play";
        }, 15s), "cold session did not become ready");
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started).count();
        coldMilliseconds.push_back(elapsed);
        ComPtr<IRawElementProviderSimple> provider;
        Require(SUCCEEDED(fixture.coordinator().GetAutomationProvider(&provider)) && provider,
                "composition UIA provider was unavailable");
        if (run == 0) {
            Require(SUCCEEDED(fixture.coordinator().UpdateGeometry(
                        {12, 18, 712, 438}, 1.5)),
                    "DPI/bounds update was rejected");
            const auto prior = fixture.coordinator().state().authority.commandSequence;
            Require(fixture.coordinator().SendCommand(
                        widgetrail::richmedia::Command::NavigateNext),
                    "focus command was rejected");
            Require(PumpUntil([&] {
                const auto state = fixture.coordinator().state();
                return state.authority.commandSequence > prior + 1 &&
                    state.focusedElement == L"seek";
            }, 50ms), "focus command did not acknowledge within 50 ms");
            Require(SUCCEEDED(fixture.coordinator().SetVisible(false)),
                    "hide transition failed");
            Require(!fixture.coordinator().SendCommand(
                        widgetrail::richmedia::Command::Activate),
                    "hidden surface retained input admission");
            Require(SUCCEEDED(fixture.coordinator().SetVisible(true)),
                    "show transition failed");
        }
        fixture.Close();
        Require(!std::filesystem::exists(fixture.profile()),
                "ephemeral profile survived teardown");
        std::cout << "cold-session run=" << run + 1 << " ready-ms=" << elapsed << '\n'
                  << std::flush;
    }

    Fixture fixture(20, false);
    Require(PumpUntil([&] {
        return fixture.coordinator().state().focusedElement == L"play";
    }, 15s), "hidden session did not initialize");
    Require(!fixture.coordinator().SendCommand(
                widgetrail::richmedia::Command::NavigateNext),
            "hidden session admitted input");

    const auto sampleState = [&](const char* label, const bool visible,
                                 const bool interactive) {
        Require(SUCCEEDED(fixture.coordinator().SetVisible(visible)),
                "visibility transition failed");
        std::this_thread::sleep_for(3s);
        std::size_t maximum{};
        std::size_t maximumProcesses{};
        std::vector<long long> acknowledgements;
        const auto cpuStarted = OwnedProcessSample();
        const auto stateStarted = std::chrono::steady_clock::now();
        for (int sample = 0; sample < 30; ++sample) {
            if (interactive) {
                const auto before = fixture.coordinator().state().authority.commandSequence;
                const auto started = std::chrono::steady_clock::now();
                Require(fixture.coordinator().SendCommand(
                            widgetrail::richmedia::Command::NavigateNext),
                        "interactive command was rejected");
                Require(PumpUntil([&] {
                    return fixture.coordinator().state().authority.commandSequence > before + 1;
                }, 50ms), "input acknowledgement exceeded 50 ms");
                acknowledgements.push_back(
                    std::chrono::duration_cast<std::chrono::milliseconds>(
                        std::chrono::steady_clock::now() - started).count());
            }
            const auto processSample = OwnedProcessSample();
            maximum = std::max(maximum, processSample.privateBytes);
            maximumProcesses = std::max(maximumProcesses, processSample.processCount);
            std::cout << "sample state=" << label << " index=" << sample + 1
                      << " process-count=" << processSample.processCount
                      << " private-bytes=" << processSample.privateBytes << '\n'
                      << std::flush;
            std::this_thread::sleep_for(1s);
        }
        Require(maximum < 500ULL * 1024ULL * 1024ULL,
                "visible proof exceeded 500 MiB private-memory stop threshold");
        const auto cpuFinished = OwnedProcessSample();
        const auto wall = std::chrono::duration<double>(
            std::chrono::steady_clock::now() - stateStarted).count();
        const auto cpuDelta = cpuFinished.cpu100ns >= cpuStarted.cpu100ns
            ? cpuFinished.cpu100ns - cpuStarted.cpu100ns : 0;
        std::cout << "state-summary state=" << label
                  << " max-private-bytes=" << maximum
                  << " max-process-count=" << maximumProcesses
                  << " cpu-core-percent=" << (static_cast<double>(cpuDelta) / 1.0e7 / wall * 100.0)
                  << '\n';
        if (!acknowledgements.empty()) {
            std::sort(acknowledgements.begin(), acknowledgements.end());
            std::cout << "input-ack p95-ms="
                      << acknowledgements[(acknowledgements.size() * 95 - 1) / 100]
                      << " max-ms=" << acknowledgements.back() << '\n';
        }
    };

    sampleState("ready-hidden", false, false);
    sampleState("visible-idle", true, false);
    sampleState("visible-interactive", true, true);
    fixture.Close();
    Require(!std::filesystem::exists(fixture.profile()),
            "performance profile survived teardown");
    std::sort(coldMilliseconds.begin(), coldMilliseconds.end());
    std::cout << "cold-session min-ms=" << coldMilliseconds.front()
              << " median-ms=" << coldMilliseconds[coldMilliseconds.size() / 2]
              << " max-ms=" << coldMilliseconds.back() << '\n';
    std::cout << "RichMediaSurfaceCoordinator lifecycle/performance passed=1\n";
}

} // namespace

int wmain(int argc, wchar_t**) {
    const HRESULT initialize = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialize)) {
        std::cerr << "COM initialization failed hr=" << initialize << '\n';
        return 1;
    }
    try {
        RunContractCases();
        if (argc > 1) RunLifecycleAndPerformance();
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "RichMediaSurfaceCoordinatorTests failed: " << error.what() << '\n';
        CoUninitialize();
        return 1;
    }
}
