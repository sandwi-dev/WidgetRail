#include "WidgetSurfaceCoordinator.h"

#include <Windows.h>
#include <UIAutomation.h>
#include <d2d1.h>
#include <dwrite.h>
#include <psapi.h>
#include <wrl/client.h>

#include <chrono>
#include <cstddef>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace {

using Microsoft::WRL::ComPtr;
int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

[[nodiscard]] std::size_t PrivateWorkingSetBytes() {
    std::vector<std::byte> buffer(256 * 1024);
    for (int attempt = 0; attempt < 8; ++attempt) {
        if (QueryWorkingSet(GetCurrentProcess(), buffer.data(),
                            static_cast<DWORD>(buffer.size())) != FALSE) {
            const auto* information =
                reinterpret_cast<const PSAPI_WORKING_SET_INFORMATION*>(buffer.data());
            std::size_t privatePages{};
            for (ULONG_PTR index = 0; index < information->NumberOfEntries; ++index)
                if (!information->WorkingSetInfo[index].Shared) ++privatePages;
            SYSTEM_INFO system{};
            GetNativeSystemInfo(&system);
            return privatePages * static_cast<std::size_t>(system.dwPageSize);
        }
        Check(GetLastError() == ERROR_BAD_LENGTH,
              "private working-set query fails only for a short buffer");
        buffer.resize(buffer.size() * 2);
    }
    throw std::runtime_error("private working-set query exceeded its bounded buffer");
}

gba::WidgetSnapshot Snapshot(const long long sequence = 1) {
    gba::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"gallery.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.initialFocusId = L"pin.fixture.action";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"root";
    gba::WidgetNode heading;
    heading.id = L"pin.fixture.heading";
    heading.kind = L"text";
    heading.text = L"Pinned declarative fixture";
    heading.accessibilityLabel = heading.text;
    heading.inputScopeId = L"root";
    gba::WidgetNode action;
    action.id = L"pin.fixture.action";
    action.kind = L"button";
    action.text = L"Deterministic action";
    action.accessibilityLabel = action.text;
    action.actionId = L"fixture-action";
    action.inputScopeId = L"root";
    snapshot.root.children = {std::move(heading), std::move(action)};
    return snapshot;
}

gba::pinned::WidgetSurfaceAdmission Admission(const bool supported = true) {
    return {
        L"org.gbar.samples.sdk-gallery",
        L"gallery.instance",
        L"runtime-1",
        L"presentation-1",
        L"SDK Gallery",
        supported,
        Snapshot(),
    };
}

gba::WidgetDescriptor Descriptor(
    const std::wstring_view runtime = L"runtime-1",
    const bool supported = true) {
    gba::WidgetDescriptor descriptor;
    descriptor.id = L"org.gbar.samples.sdk-gallery";
    descriptor.name = L"SDK Gallery";
    descriptor.instanceId = L"gallery.instance";
    descriptor.runtimeGeneration = runtime;
    descriptor.presentationGeneration = L"presentation-1";
    descriptor.pinningSupported = supported;
    return descriptor;
}

} // namespace

int main() {
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    try {
        Check(SUCCEEDED(apartment), "COM apartment initializes");
        (void)SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        ComPtr<ID2D1Factory> d2d;
        ComPtr<IDWriteFactory> write;
        Check(SUCCEEDED(D2D1CreateFactory(
                  D2D1_FACTORY_TYPE_SINGLE_THREADED,
                  IID_PPV_ARGS(d2d.ReleaseAndGetAddressOf()))),
              "D2D factory initializes");
        Check(SUCCEEDED(DWriteCreateFactory(
                  DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                  reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
              "DWrite factory initializes");

        gba::pinned::WidgetSurfaceCoordinator coordinator;
        std::wstring error;
        Check(coordinator.Initialize(
                  GetModuleHandleW(nullptr), nullptr, WM_APP + 0x410,
                  d2d.Get(), write.Get(), nullptr, error),
              "coordinator initializes with current production primitives");
        Check(!coordinator.Pin(Admission(false), error) && !error.empty(),
              "non-supporting widget is rejected safely");

        const auto privateBefore = PrivateWorkingSetBytes();
        if (!coordinator.Pin(Admission(), error)) {
            std::wcerr << L"pin error: " << error << L'\n';
            Check(false, "current supporting widget creates one host-owned surface");
        }
        const HWND surface = coordinator.window();
        Check(surface && IsWindow(surface) && IsWindowVisible(surface),
              "real pinned HWND is visible");
        Check(coordinator.presentationState() ==
                  gba::pinned::WidgetSurfacePresentationState::PinnedClickThrough,
              "new pin has typed nonactivating presentation state");
        Check(gba::pinned::WidgetSurfacePresentationStateValue(
                  coordinator.presentationState()) == L"pinnedClickThrough",
              "typed click-through state has a closed protocol value");
        const auto styles = static_cast<DWORD>(GetWindowLongPtrW(surface, GWL_EXSTYLE));
        Check((styles & (WS_EX_TOOLWINDOW | WS_EX_TOPMOST)) ==
                  (WS_EX_TOOLWINDOW | WS_EX_TOPMOST) &&
              (styles & WS_EX_APPWINDOW) == 0,
              "real pinned HWND uses the accepted tool-window/topmost contract");
        Check(!coordinator.Pin(Admission(), error) &&
                  error.find(L"already pinned") != std::wstring::npos,
              "duplicate pin is bounded");
        auto other = Admission();
        other.widgetId = L"org.gbar.samples.other";
        Check(!coordinator.Pin(std::move(other), error) &&
                  error.find(L"Only one") != std::wstring::npos,
              "simultaneous pin cap is enforced");

        UpdateWindow(surface);
        IRawElementProviderSimple* root{};
        Check(SUCCEEDED(coordinator.window()
                  ? UiaHostProviderFromHwnd(coordinator.window(), &root)
                  : E_FAIL) && root,
              "real pinned HWND has a UI Automation host provider");
        root->Release();
        Check(!coordinator.UpdateSnapshot(
                  coordinator.widgetId(), L"stale-runtime", Snapshot(2)),
              "stale runtime cannot replace pinned content");
        Check(coordinator.UpdateSnapshot(
                  coordinator.widgetId(), coordinator.runtimeGeneration(), Snapshot(2)),
              "current runtime updates the declarative surface");

        Check(coordinator.ToggleInteractionMode(),
              "visible overlay can explicitly make the pin interactive");
        coordinator.OnOverlayHidden();
        Check(coordinator.pinned() && IsWindowVisible(surface),
              "main overlay hide preserves the visible pinned HWND");
        Check(coordinator.presentationState() ==
                  gba::pinned::WidgetSurfacePresentationState::PinnedClickThrough,
              "overlay hide releases interaction into typed click-through state");
        const auto clickThrough = static_cast<DWORD>(
            GetWindowLongPtrW(surface, GWL_EXSTYLE));
        Check((clickThrough & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) ==
                  (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT),
              "hidden-overlay pin is nonactivating and click-through");
        Check(SendMessageW(surface, WM_NCHITTEST, 0, 0) == HTTRANSPARENT,
              "click-through pin rejects hit-test ownership");
        coordinator.OnOverlayShown();
        Check(coordinator.ToggleInteractionMode() &&
                  coordinator.presentationState() ==
                      gba::pinned::WidgetSurfacePresentationState::PinnedInteractive,
              "reopened overlay can explicitly restore one interactive pin");

        coordinator.ReconcileCatalog({Descriptor()});
        Check(coordinator.pinned(), "current catalog generation retains the surface");
        coordinator.ReconcileCatalog({Descriptor(L"runtime-2")});
        Check(!coordinator.pinned() && coordinator.teardownCount() == 1 &&
                  coordinator.lastStopReason() ==
                      gba::pinned::WidgetSurfaceStopReason::RuntimeReplaced,
              "runtime replacement tears down exactly once");
        Check(!IsWindow(surface), "replaced runtime leaves no orphaned HWND");

        Check(coordinator.Pin(Admission(), error), "surface can be repinned");
        coordinator.ReconcileCatalog({});
        Check(!coordinator.pinned() && coordinator.teardownCount() == 2 &&
                  coordinator.lastStopReason() ==
                      gba::pinned::WidgetSurfaceStopReason::WidgetRemoved,
              "catalog removal tears down exactly once");

        Check(coordinator.Pin(Admission(), error), "surface can be pinned for worker loss");
        const HWND failedWorkerSurface = coordinator.window();
        Check(coordinator.Unpin(
                  gba::pinned::WidgetSurfaceStopReason::WorkerUnavailable) &&
                  coordinator.teardownCount() == 3 &&
                  coordinator.lastStopReason() ==
                      gba::pinned::WidgetSurfaceStopReason::WorkerUnavailable,
              "worker loss tears down exactly once with its primary reason");
        Check(!IsWindow(failedWorkerSurface), "worker loss leaves no orphaned HWND");

        Check(coordinator.Pin(Admission(), error), "surface can be pinned for host exit");
        const auto privatePinned = PrivateWorkingSetBytes();
        std::this_thread::sleep_for(std::chrono::milliseconds(750));
        Check(coordinator.Unpin(gba::pinned::WidgetSurfaceStopReason::HostExit),
              "host exit performs terminal teardown");
        Check(!coordinator.Unpin(gba::pinned::WidgetSurfaceStopReason::HostExit) &&
                  coordinator.teardownCount() == 4,
              "terminal teardown is idempotent");
        coordinator.Dispose();
        coordinator.Dispose();
        Check(coordinator.teardownCount() == 4,
              "coordinator disposal after cleanup owns no second teardown");
        const auto privateDelta = static_cast<long long>(privatePinned) -
            static_cast<long long>(privateBefore);
        Check(privateDelta <= 128LL * 1024LL * 1024LL,
              "incremental pinned private working set stays below DLV-016 material gate");

        std::cout << "WidgetSurfaceCoordinatorTests passed (" << checks
                  << " checks, pinned private working-set delta "
                  << privateDelta << " bytes)\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "WidgetSurfaceCoordinatorTests failed after " << checks
                  << " checks: " << exception.what() << '\n';
        if (SUCCEEDED(apartment)) CoUninitialize();
        return 1;
    }
}
