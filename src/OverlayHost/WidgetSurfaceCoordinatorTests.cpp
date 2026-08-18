#include "WidgetSurfaceCoordinator.h"

#include <Windows.h>
#include <UIAutomation.h>
#include <d2d1.h>
#include <dwrite.h>
#include <psapi.h>
#include <wrl/client.h>

#include <chrono>
#include <cstddef>
#include <filesystem>
#include <iostream>
#include <optional>
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

bool InvokeAutomationId(const HWND window, const wchar_t* automationId) {
    ComPtr<IUIAutomation> automation;
    if (FAILED(CoCreateInstance(
            CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(automation.ReleaseAndGetAddressOf())))) return false;
    ComPtr<IUIAutomationElement> root;
    if (FAILED(automation->ElementFromHandle(window, root.ReleaseAndGetAddressOf())) || !root)
        return false;
    VARIANT expected{};
    expected.vt = VT_BSTR;
    expected.bstrVal = SysAllocString(automationId);
    ComPtr<IUIAutomationCondition> condition;
    const HRESULT conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, expected, condition.ReleaseAndGetAddressOf());
    VariantClear(&expected);
    if (FAILED(conditionResult) || !condition) return false;
    ComPtr<IUIAutomationElement> element;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), element.ReleaseAndGetAddressOf())) ||
        !element) return false;
    ComPtr<IUIAutomationInvokePattern> invoke;
    if (FAILED(element->GetCurrentPatternAs(
            UIA_InvokePatternId, IID_PPV_ARGS(invoke.ReleaseAndGetAddressOf()))) || !invoke)
        return false;
    if (FAILED(invoke->Invoke())) return false;
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return true;
}

ComPtr<IUIAutomationElement> FindAutomationId(
    const HWND window, const wchar_t* automationId) {
    ComPtr<IUIAutomation> automation;
    if (FAILED(CoCreateInstance(
            CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(automation.ReleaseAndGetAddressOf())))) return {};
    ComPtr<IUIAutomationElement> root;
    if (FAILED(automation->ElementFromHandle(window, root.ReleaseAndGetAddressOf())) || !root)
        return {};
    VARIANT expected{};
    expected.vt = VT_BSTR;
    expected.bstrVal = SysAllocString(automationId);
    ComPtr<IUIAutomationCondition> condition;
    const HRESULT conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, expected, condition.ReleaseAndGetAddressOf());
    VariantClear(&expected);
    if (FAILED(conditionResult) || !condition) return {};
    ComPtr<IUIAutomationElement> element;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), element.ReleaseAndGetAddressOf())))
        return {};
    return element;
}

bool ActionBoundsInsideWindow(const HWND window, const wchar_t* automationId) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    RECT bounds{};
    RECT windowBounds{};
    if (FAILED(element->get_CurrentBoundingRectangle(&bounds)) ||
        !GetWindowRect(window, &windowBounds)) return false;
    constexpr double tolerance = 0.51;
    return bounds.right - bounds.left > 0 && bounds.bottom - bounds.top > 0 &&
        bounds.left + tolerance >= windowBounds.left &&
        bounds.top + tolerance >= windowBounds.top &&
        bounds.right <= windowBounds.right + tolerance &&
        bounds.bottom <= windowBounds.bottom + tolerance;
}

bool ReadNamedButtonBounds(const HWND window, const wchar_t* automationId,
                           const wchar_t* expectedName, RECT& bounds) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    BSTR name{};
    CONTROLTYPEID controlType{};
    const bool valid = SUCCEEDED(element->get_CurrentName(&name)) && name &&
        std::wstring_view(name) == expectedName &&
        SUCCEEDED(element->get_CurrentControlType(&controlType)) &&
        controlType == UIA_ButtonControlTypeId &&
        SUCCEEDED(element->get_CurrentBoundingRectangle(&bounds));
    SysFreeString(name);
    return valid;
}

bool IsAssertiveLiveRegion(const HWND window, const wchar_t* automationId) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    VARIANT value{};
    const HRESULT result = element->GetCurrentPropertyValue(
        UIA_LiveSettingPropertyId, &value);
    const bool assertive = SUCCEEDED(result) && value.vt == VT_I4 &&
        value.lVal == Assertive;
    VariantClear(&value);
    return assertive;
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

widgetrail::WidgetSnapshot Snapshot(const long long sequence = 1) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"gallery.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.initialFocusId = L"pin.fixture.action";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"root";
    widgetrail::WidgetNode heading;
    heading.id = L"pin.fixture.heading";
    heading.kind = L"text";
    heading.text = L"Pinned declarative fixture";
    heading.accessibilityLabel = heading.text;
    heading.inputScopeId = L"root";
    widgetrail::WidgetNode action;
    action.id = L"pin.fixture.action";
    action.kind = L"button";
    action.text = L"Deterministic action";
    action.accessibilityLabel = action.text;
    action.actionId = L"fixture-action";
    action.inputScopeId = L"root";
    action.focusRight = L"pin.fixture.second";
    widgetrail::WidgetNode second;
    second.id = L"pin.fixture.second";
    second.kind = L"button";
    second.text = L"Second deterministic action";
    second.accessibilityLabel = second.text;
    second.actionId = L"fixture-second";
    second.inputScopeId = L"root";
    second.focusLeft = L"pin.fixture.action";
    snapshot.root.children = {
        std::move(heading), std::move(action), std::move(second)};
    return snapshot;
}

widgetrail::pinned::WidgetSurfaceAdmission Admission(const bool supported = true) {
    return {
        L"widgetrail.samples.sdk-gallery",
        L"gallery.instance",
        L"runtime-1",
        L"presentation-1",
        L"SDK Gallery",
        supported,
        Snapshot(),
    };
}

widgetrail::WidgetDescriptor Descriptor(
    const std::wstring_view runtime = L"runtime-1",
    const bool supported = true) {
    widgetrail::WidgetDescriptor descriptor;
    descriptor.id = L"widgetrail.samples.sdk-gallery";
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

        widgetrail::pinned::WidgetSurfaceCoordinator coordinator;
        std::wstring error;
        const auto placementRoot = std::filesystem::temp_directory_path() /
            (L"wrail-widget-surface-" + std::to_wstring(GetCurrentProcessId()));
        Check(coordinator.Initialize(
                  GetModuleHandleW(nullptr), nullptr, WM_APP + 0x410,
                  d2d.Get(), write.Get(), nullptr, error,
                  placementRoot / L"placement.ini"),
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
                  widgetrail::pinned::WidgetSurfacePresentationState::PinnedClickThrough,
              "new pin has typed nonactivating presentation state");
        Check(widgetrail::pinned::WidgetSurfacePresentationStateValue(
                  coordinator.presentationState()) == L"pinnedClickThrough",
              "typed click-through state has a closed protocol value");
        const auto styles = static_cast<DWORD>(GetWindowLongPtrW(surface, GWL_EXSTYLE));
        Check((styles & (WS_EX_TOOLWINDOW | WS_EX_TOPMOST)) ==
                  (WS_EX_TOOLWINDOW | WS_EX_TOPMOST) &&
              (styles & WS_EX_APPWINDOW) == 0,
              "real pinned HWND uses the accepted tool-window/topmost contract");
        RECT originalBounds{};
        GetWindowRect(surface, &originalBounds);
        Check(coordinator.BeginPlacement(widgetrail::pinned::PlacementMode::Move) &&
                  coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Left) &&
                  coordinator.CancelPlacement(),
              "controller move and cancel share one bounded placement session");
        RECT canceledBounds{};
        GetWindowRect(surface, &canceledBounds);
        Check(EqualRect(&originalBounds, &canceledBounds),
              "cancel restores the exact pre-gesture real-HWND rectangle");
        Check(coordinator.BeginPlacement(widgetrail::pinned::PlacementMode::Resize) &&
                  coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Left),
              "controller resize changes the real HWND through the placement state machine");
        Check(coordinator.CommitPlacement(error),
              "current generation atomically commits real-HWND geometry");
        RECT committedBounds{};
        GetWindowRect(surface, &committedBounds);
        Check(std::filesystem::exists(placementRoot / L"placement.ini"),
              "committed real-HWND geometry creates the isolated durable record");
        Check(!coordinator.Pin(Admission(), error) &&
                  error.find(L"already pinned") != std::wstring::npos,
              "duplicate pin is bounded");
        auto other = Admission();
        other.widgetId = L"widgetrail.samples.other";
        Check(!coordinator.Pin(std::move(other), error) &&
                  error.find(L"Only one") != std::wstring::npos,
              "simultaneous pin cap is enforced");

        Check(coordinator.SetInteractionMode(widgetrail::pinned::InteractionMode::ClickThrough),
              "fixture returns to closed click-through mode after placement setup");
        UpdateWindow(surface);
        const auto initialClickThroughPaint = coordinator.PaintTraceForTesting();
        Check(initialClickThroughPaint.snapshotSequence == 1 &&
                  coordinator.interactionMode() ==
                      widgetrail::pinned::InteractionMode::ClickThrough &&
                  initialClickThroughPaint.contentPresentation ==
                      widgetrail::pinned::ContentPresentation::AdmittedWidget &&
                  initialClickThroughPaint.declarativeRenderSucceeded &&
                  initialClickThroughPaint.admittedContentPresented &&
                  initialClickThroughPaint.navigationNodeCount >= 2,
              "click-through production paint retains admitted sentinel content without a placeholder");
        Check(!FindAutomationId(surface, L"host:pinned.move") &&
                  !FindAutomationId(surface, L"widget:pin.fixture.action"),
              "click-through UIA tree exposes no hidden interactive controls");
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
                  widgetrail::pinned::WidgetSurfacePresentationState::PinnedClickThrough,
              "overlay hide releases interaction into typed click-through state");
        const auto clickThrough = static_cast<DWORD>(
            GetWindowLongPtrW(surface, GWL_EXSTYLE));
        Check((clickThrough & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) ==
                  (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT),
              "hidden-overlay pin is nonactivating and click-through");
        Check(SendMessageW(surface, WM_NCHITTEST, 0, 0) == HTTRANSPARENT,
              "click-through pin rejects hit-test ownership");
        Check(coordinator.UpdateSnapshot(
                  coordinator.widgetId(), coordinator.runtimeGeneration(), Snapshot(3)),
              "hidden click-through pin admits a current replacement snapshot");
        UpdateWindow(surface);
        const auto hiddenReplacementPaint = coordinator.PaintTraceForTesting();
        Check(hiddenReplacementPaint.snapshotSequence == 3 &&
                  coordinator.interactionMode() ==
                      widgetrail::pinned::InteractionMode::ClickThrough &&
                  hiddenReplacementPaint.contentPresentation ==
                      widgetrail::pinned::ContentPresentation::AdmittedWidget &&
                  hiddenReplacementPaint.declarativeRenderSucceeded &&
                  hiddenReplacementPaint.admittedContentPresented &&
                  hiddenReplacementPaint.navigationNodeCount >= 2,
              "hidden-overlay repaint presents the current sentinel content without reopening");
        SendMessageW(surface, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(40, 100));
        SendMessageW(surface, WM_LBUTTONUP, 0, MAKELPARAM(40, 100));
        Check(!coordinator.controllerFocused() &&
                  coordinator.TakeInputRequests().empty() &&
                  !FindAutomationId(surface, L"widget:pin.fixture.action") &&
                  !FindAutomationId(surface, L"host:pinned.close"),
              "hidden click-through content retains no focus, input, or actionable UIA authority");
        coordinator.OnOverlayShown();
        Check(coordinator.ToggleInteractionMode() &&
                  coordinator.presentationState() ==
                      widgetrail::pinned::WidgetSurfacePresentationState::PinnedInteractive,
              "reopened overlay can explicitly restore one interactive pin");
        UpdateWindow(surface);
        Check(coordinator.EnterControllerFocus() && coordinator.controllerFocused() &&
                  GetFocus() == surface,
              "one explicit host transition gives controller focus to the pinned HWND");
        UpdateWindow(surface);
        Check(FindAutomationId(surface, L"widget:pin.fixture.action") &&
                  FindAutomationId(surface, L"host:pinned.close") &&
                  FindAutomationId(surface, L"host:pinned.emergency"),
              "interactive UIA composes widget content with Close and emergency host actions");
        Check(coordinator.MoveControllerFocus(
                  widgetrail::input::NavigationDirection::Right) &&
                  coordinator.focusedElementId() == L"pin.fixture.second" &&
                  coordinator.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Left) &&
                  coordinator.focusedElementId() == L"pin.fixture.action",
              "controller focus uses the shared authored and geometric navigation owner");
        Check(coordinator.QueueFocusedInput(
                  L"a", widgetrail::ControllerInputOrigin::PhysicalController),
              "focused controller activation enters the bounded pinned input queue");
        auto controllerInputs = coordinator.TakeInputRequests();
        Check(controllerInputs.size() == 1 &&
                  controllerInputs[0].widgetId == coordinator.widgetId() &&
                  controllerInputs[0].runtimeGeneration == coordinator.runtimeGeneration() &&
                  controllerInputs[0].snapshotSequence == 3 &&
                  controllerInputs[0].nodeId == L"pin.fixture.action" &&
                  controllerInputs[0].protocolButton == L"a" &&
                  controllerInputs[0].origin ==
                      widgetrail::ControllerInputOrigin::PhysicalController,
              "queued pinned controller input retains exact current generation and focus");
        coordinator.SetActionFeedback(
            L"Pinned action failed. Reopen the overlay and try again.", true);
        Check(IsAssertiveLiveRegion(surface, L"host:pinned.feedback"),
              "sanitized pinned action failure is an assertive UIA live status");
        coordinator.SetActionFeedback({}, false);

        const auto widgetAction = FindAutomationId(
            surface, L"widget:pin.fixture.action");
        RECT widgetBounds{};
        Check(widgetAction &&
                  SUCCEEDED(widgetAction->get_CurrentBoundingRectangle(&widgetBounds)),
              "pinned widget action exposes real on-screen UIA bounds");
        const auto widgetPoint = coordinator.PointerPointForTesting(
            L"pin.fixture.action");
        Check(widgetPoint.has_value(),
              "pinned widget action retains one production pointer hit region");
        SendMessageW(surface, WM_LBUTTONDOWN, MK_LBUTTON,
                     MAKELPARAM(widgetPoint->x, widgetPoint->y));
        ReleaseCapture();
        Check(coordinator.TakeInputRequests().empty(),
              "pointer capture loss cancels pinned activation without forwarding input");
        SendMessageW(surface, WM_LBUTTONDOWN, MK_LBUTTON,
                     MAKELPARAM(widgetPoint->x, widgetPoint->y));
        SendMessageW(surface, WM_LBUTTONUP, 0,
                     MAKELPARAM(widgetPoint->x, widgetPoint->y));
        auto pointerInputs = coordinator.TakeInputRequests();
        Check(pointerInputs.size() == 1 &&
                  pointerInputs[0].nodeId == L"pin.fixture.action",
              "pointer activation shares exact pinned focus and input admission");

        Check(InvokeAutomationId(surface, L"widget:pin.fixture.action"),
              "real UI Automation invokes the composed pinned widget action");
        auto automationInputs = coordinator.TakeInputRequests();
        Check(automationInputs.size() == 1 &&
                  automationInputs[0].origin ==
                      widgetrail::ControllerInputOrigin::AccessibilityAutomation &&
                  automationInputs[0].nodeId == L"pin.fixture.action",
              "UI Automation action uses the same current bounded input queue");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.move") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.resize"),
              "Move and Resize UIA action bounds remain inside the pinned HWND");
        Check(InvokeAutomationId(surface, L"host:pinned.move") &&
                  coordinator.placementMode() == widgetrail::pinned::PlacementMode::Move,
              "real UI Automation Move action enters the shared placement state machine");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.commit") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.cancel"),
              "Commit and Cancel UIA action bounds remain inside the pinned HWND");
        Check(coordinator.CancelPlacement(),
              "UI Automation placement can be canceled through the same authority");

        Check(coordinator.BeginPlacement(widgetrail::pinned::PlacementMode::Resize),
              "minimum-size UIA fixture enters the production resize state machine");
        for (int step = 0; step < 40; ++step) {
            (void)coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Left);
            (void)coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Up);
        }
        Check(coordinator.CommitPlacement(error),
              "minimum-size real-HWND placement commits through the production store");
        UpdateWindow(surface);
        RECT minimumBounds{};
        GetWindowRect(surface, &minimumBounds);
        const UINT minimumDpi = std::max(1U, GetDpiForWindow(surface));
        Check(minimumBounds.right - minimumBounds.left >= MulDiv(240, minimumDpi, 96) &&
                  minimumBounds.bottom - minimumBounds.top >= MulDiv(135, minimumDpi, 96),
              "real pinned HWND remains at or above the injected minimum size");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.move") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.resize"),
              "Move and Resize UIA bounds fit the minimum-size surface");
        RECT moveBounds{};
        RECT resizeBounds{};
        Check(ReadNamedButtonBounds(surface, L"host:pinned.move", L"Move pinned surface",
                                    moveBounds) &&
                  ReadNamedButtonBounds(surface, L"host:pinned.resize",
                                        L"Resize pinned surface",
                                        resizeBounds) &&
                  moveBounds.left < resizeBounds.left,
              "minimum-size UIA publishes ordered named Move and Resize buttons");
        Check(InvokeAutomationId(surface, L"host:pinned.move"),
              "minimum-size surface exposes the Move action");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.commit") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.cancel"),
              "Commit and Cancel UIA bounds fit the minimum-size surface");
        RECT commitBounds{};
        RECT cancelBounds{};
        Check(ReadNamedButtonBounds(surface, L"host:pinned.commit", L"Commit placement",
                                    commitBounds) &&
                  ReadNamedButtonBounds(surface, L"host:pinned.cancel", L"Cancel placement",
                                        cancelBounds) &&
                  commitBounds.left < cancelBounds.left,
              "minimum-size UIA publishes ordered named Commit and Cancel buttons");
        Check(coordinator.CancelPlacement(),
              "minimum-size placement cancellation remains exact");

        const HMONITOR currentMonitor =
            MonitorFromWindow(surface, MONITOR_DEFAULTTONEAREST);
        MONITORINFOEXW currentInfo{sizeof(currentInfo)};
        Check(currentMonitor && GetMonitorInfoW(currentMonitor, &currentInfo),
              "real HWND monitor metadata is available for reconciliation");
        coordinator.ReconcileDisplayEnvironmentForTesting({{
            L"fixture-replacement-monitor",
            {currentInfo.rcWork.left, currentInfo.rcWork.top,
             currentInfo.rcWork.right, currentInfo.rcWork.bottom},
            minimumDpi,
            true,
        }});
        RECT reconciledBounds{};
        GetWindowRect(surface, &reconciledBounds);
        Check(reconciledBounds.left >= currentInfo.rcWork.left &&
                  reconciledBounds.top >= currentInfo.rcWork.top &&
                  reconciledBounds.right <= currentInfo.rcWork.right &&
                  reconciledBounds.bottom <= currentInfo.rcWork.bottom,
              "coordinator monitor-loss reconciliation keeps the real HWND fully on-screen");
        Check(coordinator.controllerFocused() && GetFocus() == surface &&
                  !coordinator.focusedElementId().empty(),
              "display reconciliation retains one deterministic valid pinned focus owner");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.move") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.resize"),
              "reconciled minimum surface retains bounded host UIA actions");

        coordinator.ReconcileCatalog({Descriptor()});
        Check(coordinator.pinned(), "current catalog generation retains the surface");
        coordinator.ReconcileCatalog({Descriptor(L"runtime-2")});
        Check(!coordinator.pinned() && coordinator.teardownCount() == 1 &&
                  !coordinator.controllerFocused() &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::RuntimeReplaced,
              "runtime replacement tears down exactly once");
        Check(!IsWindow(surface), "replaced runtime leaves no orphaned HWND");

        Check(coordinator.Pin(Admission(), error), "surface can be repinned");
        RECT restoredBounds{};
        GetWindowRect(coordinator.window(), &restoredBounds);
        Check(restoredBounds.right - restoredBounds.left ==
                  reconciledBounds.right - reconciledBounds.left &&
                  restoredBounds.bottom - restoredBounds.top ==
                      reconciledBounds.bottom - reconciledBounds.top,
              "repin restores committed logical size from durable storage");
        coordinator.ReconcileCatalog({});
        Check(!coordinator.pinned() && coordinator.teardownCount() == 2 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::WidgetRemoved,
              "catalog removal tears down exactly once");

        Check(coordinator.Pin(Admission(), error), "surface can be pinned for worker loss");
        const HWND failedWorkerSurface = coordinator.window();
        Check(coordinator.Unpin(
                  widgetrail::pinned::WidgetSurfaceStopReason::WorkerUnavailable) &&
                  coordinator.teardownCount() == 3 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::WorkerUnavailable,
              "worker loss tears down exactly once with its primary reason");
        Check(!IsWindow(failedWorkerSurface), "worker loss leaves no orphaned HWND");

        Check(coordinator.Pin(Admission(), error),
              "surface can be pinned for UI Automation Close");
        coordinator.OnOverlayShown();
        Check(coordinator.ToggleInteractionMode(),
              "Close fixture explicitly enters Interactive mode");
        const HWND closeSurface = coordinator.window();
        UpdateWindow(closeSurface);
        Check(InvokeAutomationId(closeSurface, L"host:pinned.close") &&
                  !coordinator.pinned() && !IsWindow(closeSurface) &&
                  coordinator.teardownCount() == 4 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::Close,
              "real UI Automation Close performs exact paired teardown");

        Check(coordinator.Pin(Admission(), error),
              "surface can be pinned for emergency hide");
        coordinator.OnOverlayShown();
        Check(coordinator.ToggleInteractionMode(),
              "emergency fixture explicitly enters Interactive mode");
        const HWND emergencySurface = coordinator.window();
        UpdateWindow(emergencySurface);
        Check(InvokeAutomationId(emergencySurface, L"host:pinned.emergency") &&
                  !coordinator.pinned() && !IsWindow(emergencySurface) &&
                  coordinator.teardownCount() == 5 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::EmergencyHide,
              "accessible host emergency action removes every bounded pin and interaction");

        Check(coordinator.Pin(Admission(), error), "surface can be pinned for host exit");
        const auto privatePinned = PrivateWorkingSetBytes();
        std::this_thread::sleep_for(std::chrono::milliseconds(750));
        Check(coordinator.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::HostExit),
              "host exit performs terminal teardown");
        Check(!coordinator.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::HostExit) &&
                  coordinator.teardownCount() == 6,
              "terminal teardown is idempotent");
        coordinator.Dispose();
        coordinator.Dispose();
        Check(coordinator.teardownCount() == 6,
              "coordinator disposal after cleanup owns no second teardown");
        std::error_code cleanup;
        std::filesystem::remove_all(placementRoot, cleanup);
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
