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
#include <fstream>
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

bool AutomationValueContains(const HWND window, const wchar_t* automationId,
                             const std::wstring_view expected) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    VARIANT value{};
    const HRESULT result = element->GetCurrentPropertyValue(
        UIA_HelpTextPropertyId, &value);
    const bool contains = SUCCEEDED(result) && value.vt == VT_BSTR && value.bstrVal &&
        std::wstring_view(value.bstrVal).find(expected) != std::wstring_view::npos;
    VariantClear(&value);
    return contains;
}

bool HasRangeValueWithoutInvoke(
    const HWND window, const wchar_t* automationId,
    const double expectedValue, const double expectedMaximum) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    ComPtr<IUIAutomationRangeValuePattern> range;
    ComPtr<IUIAutomationInvokePattern> invoke;
    double value{};
    double maximum{};
    return SUCCEEDED(element->GetCurrentPatternAs(
               UIA_RangeValuePatternId,
               IID_PPV_ARGS(range.ReleaseAndGetAddressOf()))) && range &&
        SUCCEEDED(range->get_CurrentValue(&value)) &&
        SUCCEEDED(range->get_CurrentMaximum(&maximum)) &&
        value == expectedValue && maximum == expectedMaximum &&
        (FAILED(element->GetCurrentPatternAs(
             UIA_InvokePatternId,
             IID_PPV_ARGS(invoke.ReleaseAndGetAddressOf()))) || !invoke);
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

widgetrail::WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

widgetrail::WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

widgetrail::WidgetSnapshot ScrollSnapshot(const long long sequence = 1) {
    auto snapshot = Snapshot(sequence);
    snapshot.initialFocusId = L"pin.scroll.item.0";
    snapshot.root.children.clear();

    widgetrail::WidgetNode scroll;
    scroll.id = L"pin.scroll";
    scroll.kind = L"scroll";
    scroll.inputScopeId = L"root";
    scroll.scrollAxis = L"vertical";
    scroll.baseStyle = {
        {L"height", Length(140.0)},
        {L"flex-shrink", Number(0.0)},
    };
    for (int index = 0; index < 8; ++index) {
        widgetrail::WidgetNode item;
        item.id = L"pin.scroll.item." + std::to_wstring(index);
        item.kind = L"button";
        item.text = L"Scrollable item " + std::to_wstring(index);
        item.accessibilityLabel = item.text;
        item.actionId = L"scroll-item";
        item.inputScopeId = L"root";
        item.focusRight = L"pin.outside";
        item.baseStyle = {
            {L"min-height", Length(44.0)},
            {L"flex-shrink", Number(0.0)},
        };
        scroll.children.push_back(std::move(item));
    }

    widgetrail::WidgetNode outside;
    outside.id = L"pin.outside";
    outside.kind = L"button";
    outside.text = L"Outside scroll";
    outside.accessibilityLabel = outside.text;
    outside.actionId = L"outside";
    outside.inputScopeId = L"root";
    snapshot.root.children = {std::move(scroll), std::move(outside)};
    return snapshot;
}

widgetrail::WidgetSnapshot CompactMediaSnapshot(const long long sequence = 1) {
    auto snapshot = Snapshot(sequence);
    widgetrail::EmbeddedMediaSurfaceDeclaration media;
    media.id = L"fixture.media";
    media.accessibleName = L"Provider-neutral compact media";
    media.entryAsset = L"index.html";
    media.aspectRatio = 16.0 / 9.0;
    media.commands = {
        L"togglePlayback", L"navigatePrevious", L"navigateNext",
        L"seekBackward", L"seekForward"};
    media.compactPinnedPresentation = true;
    media.compactPinnedSeekStepSeconds = 7.0;
    snapshot.embeddedMedia = std::move(media);
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
        coordinator.OnOverlayShown();
        Check(!coordinator.Pin(Admission(false), error) && !error.empty(),
              "non-supporting widget is rejected safely");

        {
            widgetrail::pinned::WidgetSurfaceCoordinator layouts;
            const auto layoutRoot = placementRoot / L"layouts";
            Check(layouts.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x411,
                      d2d.Get(), write.Get(), nullptr, error,
                      layoutRoot / L"placement.ini"),
                  "layout coordinator reuses the single production surface owner");
            layouts.OnOverlayShown();
            auto admission = Admission();
            auto compactProjection = Snapshot();
            compactProjection.activeInputScopeId = L"compact.root";
            compactProjection.initialFocusId = L"compact.action";
            compactProjection.root.id = L"compact.root";
            compactProjection.root.inputScopeId = L"compact.root";
            widgetrail::WidgetNode compactAction;
            compactAction.id = L"compact.action";
            compactAction.kind = L"button";
            compactAction.text = L"Compact action";
            compactAction.accessibilityLabel = compactAction.text;
            compactAction.actionId = L"compact-action";
            compactAction.inputScopeId = L"compact.root";
            compactProjection.root.children = {std::move(compactAction)};
            admission.pinnedLayouts = {
                {L"compact", L"Compact", 360.0F, 240.0F, compactProjection},
                {L"details", L"Details", 640.0F, 360.0F},
            };
            Check(layouts.Pin(admission, error) && layouts.setupActive() &&
                      layouts.layoutCount() == 3 && layouts.selectedLayoutIndex() == 0 &&
                      layouts.selectedLayoutName() == L"Full widget",
                  "Pin enters setup with the host Full widget fallback first");
            Check(!layouts.Pin(admission, error) &&
                      layouts.pinned() && layouts.layoutCount() == 3,
                  "layout setup retains the one-surface ownership cap");
            Check(layouts.CycleLayout(1) && layouts.selectedLayoutIndex() == 1 &&
                      layouts.selectedLayoutName() == L"Compact",
                  "LT or RT setup cycling selects one bounded authored layout");
            const auto compactDemand = layouts.TakeLayoutSelectionNotifications();
            Check(compactDemand.size() == 1 && compactDemand[0].selected &&
                      compactDemand[0].layoutId == L"compact" &&
                      compactDemand[0].runtimeGeneration == admission.runtimeGeneration,
                  "explicit selection publishes one generation-bound package demand");
            Check(layouts.QueueFocusedInput(L"a"),
                  "selected declarative projection owns focus and action resolution");
            const auto projectedInput = layouts.TakeInputRequests();
            Check(projectedInput.size() == 1 &&
                      projectedInput[0].nodeId == L"compact.action" &&
                      projectedInput[0].activeInputScopeId == L"compact.root" &&
                      layouts.IsCurrentInputRequest(projectedInput[0]),
                  "pinned input authority is bound to the atomically selected projection");
            auto wrongLayoutInput = projectedInput[0];
            wrongLayoutInput.selectedLayoutId = L"details";
            auto staleSequenceInput = projectedInput[0];
            --staleSequenceInput.snapshotSequence;
            Check(!layouts.IsCurrentInputRequest(wrongLayoutInput) &&
                      !layouts.IsCurrentInputRequest(staleSequenceInput),
                  "wrong-layout and stale-sequence pinned input fail closed");
            Check(layouts.QueueFocusedInput(L"b"),
                  "B enters the same selected-projection queue as other authored input");
            const auto backInput = layouts.TakeInputRequests();
            Check(backInput.size() == 1 &&
                      backInput[0].protocolButton == L"b" &&
                      backInput[0].selectedLayoutId == L"compact" &&
                      backInput[0].activeInputScopeId == L"compact.root" &&
                      layouts.IsCurrentInputRequest(backInput[0]),
                  "pinned B retains exact layout scope focus and sequence authority");
            Check(layouts.QueueFocusedInput(L"menu"),
                  "Menu remains ordinary selected-projection input outside host modes");
            const auto menuInput = layouts.TakeInputRequests();
            Check(menuInput.size() == 1 &&
                      menuInput[0].protocolButton == L"menu" &&
                      layouts.IsCurrentInputRequest(menuInput[0]),
                  "pinned Menu retains the same current authority as B");
            Check(layouts.CommitSetup(error) && !layouts.setupActive() &&
                      layouts.interactionMode() ==
                          widgetrail::pinned::InteractionMode::ClickThrough,
                  "A commits layout setup and restores click-through");
            RECT committedSetup{};
            GetWindowRect(layouts.window(), &committedSetup);
            Check(!layouts.CycleLayout(1) && layouts.selectedLayoutName() == L"Compact",
                  "layout triggers cannot consume LT or RT outside setup");
            Check(layouts.BeginSetup(false) && layouts.CycleLayout(1) &&
                      layouts.selectedLayoutName() == L"Details" &&
                      layouts.StepPlacement(
                          widgetrail::pinned::PlacementMode::Move,
                          widgetrail::pinned::PlacementDirection::Left),
                  "Adjust reopens the same layout and placement transaction");
            Check(layouts.CancelSetup() && !layouts.setupActive() &&
                      layouts.selectedLayoutName() == L"Compact",
                  "B cancels Adjust and restores the selected layout checkpoint");
            const auto restoredDemand = layouts.TakeLayoutSelectionNotifications();
            Check(restoredDemand.size() == 4 &&
                      !restoredDemand[0].selected &&
                      restoredDemand[0].layoutId == L"compact" &&
                      restoredDemand[1].selected &&
                      restoredDemand[1].layoutId == L"details" &&
                      !restoredDemand[2].selected &&
                      restoredDemand[2].layoutId == L"details" &&
                      restoredDemand[3].selected &&
                      restoredDemand[3].layoutId == L"compact",
                  "layout preview and cancellation retain exact demand revocation order");
            RECT canceledSetup{};
            GetWindowRect(layouts.window(), &canceledSetup);
            Check(EqualRect(&committedSetup, &canceledSetup),
                  "B restores the exact pre-Adjust rectangle");
            Check(layouts.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration, Snapshot(2), {}) &&
                      layouts.layoutCount() == 1 && layouts.selectedLayoutIndex() == 0 &&
                      layouts.selectedLayoutName() == L"Full widget",
                  "catalog removal falls back to the host Full widget layout");
            Check(layouts.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "layout fixture performs exact single-surface teardown");
            layouts.Dispose();
            std::error_code layoutCleanup;
            std::filesystem::remove_all(layoutRoot, layoutCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator compact;
            const auto compactRoot = placementRoot / L"compact-media";
            Check(compact.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x414,
                      d2d.Get(), write.Get(), nullptr, error,
                      compactRoot / L"placement.ini"),
                  "compact media fixture reuses the pinned frame and UIA owner");
            compact.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = CompactMediaSnapshot();
            Check(compact.Pin(admission, error) && compact.CommitSetup(error) &&
                      compact.ToggleInteractionMode() && compact.EnterControllerFocus(),
                  "compact media fixture establishes one focused native presentation");
            compact.UpdateCompactMediaPlayback(3.0, 20.0, false);
            UpdateWindow(compact.window());
            const auto rewind = compact.CompactMediaSeekTarget(
                widgetrail::input::NavigationDirection::Left);
            const auto forward = compact.CompactMediaSeekTarget(
                widgetrail::input::NavigationDirection::Right);
            Check(rewind && *rewind == 0.0 && forward && *forward == 10.0,
                  "compact LT and RT targets use the declared step and clamp exact playback state");
            compact.UpdateCompactMediaPlayback(18.0, 20.0, true);
            const auto clampedForward = compact.CompactMediaSeekTarget(
                widgetrail::input::NavigationDirection::Right);
            Check(clampedForward && *clampedForward == 20.0,
                  "compact forward seek clamps to the exact current duration");
            const auto beforeNavigation = compact.compactMediaState();
            Check(compact.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Left) &&
                      compact.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Right) &&
                      compact.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Up) &&
                      compact.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      compact.focusedElementId() == L"host.compact-media.seek" &&
                      compact.compactMediaState().previewPositionSeconds ==
                          beforeNavigation.previewPositionSeconds &&
                      !compact.TakeCompactMediaSeekRequest(),
                  "D-pad and left-stick navigation cannot mutate compact seek state");
            Check(!compact.QueueFocusedInput(
                      L"a", widgetrail::ControllerInputOrigin::PhysicalController) &&
                      compact.TakeInputRequests().empty() &&
                      !compact.TakeCompactMediaSeekRequest(),
                  "compact A has no authored or hidden scrub admission path");
            UpdateWindow(compact.window());
            Check(HasRangeValueWithoutInvoke(
                      compact.window(), L"host:host.compact-media.seek", 18.0, 20.0),
                  "compact seek UIA exposes RangeValue without Invoke");
            Check(compact.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "compact media fixture performs exact teardown");
            compact.Dispose();
            std::error_code compactCleanup;
            std::filesystem::remove_all(compactRoot, compactCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator scrolling;
            const auto scrollRoot = placementRoot / L"scroll-retention";
            Check(scrolling.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x413,
                      d2d.Get(), write.Get(), nullptr, error,
                      scrollRoot / L"placement.ini"),
                  "scroll fixture reuses the production pinned coordinator");
            scrolling.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = ScrollSnapshot();
            admission.pinnedLayouts = {{
                L"scroll-layout", L"Scrollable", 420.0F, 320.0F,
                ScrollSnapshot(),
            }};
            Check(scrolling.Pin(admission, error) && scrolling.CycleLayout(1) &&
                      scrolling.CommitSetup(error),
                  "scroll fixture commits one authored selected layout");
            Check(scrolling.ToggleInteractionMode(),
                  "scroll fixture enters the existing interactive surface mode");
            UpdateWindow(scrolling.window());
            Check(scrolling.EnterControllerFocus(),
                  "scroll fixture establishes exact pinned controller focus");
            UpdateWindow(scrolling.window());
            Check(scrolling.ScrollFocusedProjection(0, -32'768, 100),
                  "right stick commits one renderer-owned free-scroll offset");
            UpdateWindow(scrolling.window());
            const auto scrolledOffset = scrolling.ScrollOffsetForTesting(L"pin.scroll");
            Check(scrolledOffset && *scrolledOffset > 0.0F &&
                      scrolling.FreeScrollBindingForTesting(),
                  "free scroll retains exact renderer offset and interaction binding");
            Check(scrolling.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right) &&
                      !scrolling.FreeScrollBindingForTesting() &&
                      scrolling.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Right) &&
                      scrolling.focusedElementId() == L"pin.outside",
                  "re-entry retires only its binding and focus can leave the list");
            UpdateWindow(scrolling.window());

            auto compatibleProjection = ScrollSnapshot(2);
            Check(scrolling.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      ScrollSnapshot(2), {{
                          L"scroll-layout", L"Scrollable", 420.0F, 320.0F,
                          compatibleProjection,
                      }}),
                  "ordinary compatible pinned snapshot is admitted after re-entry");
            UpdateWindow(scrolling.window());
            const auto retainedOffset = scrolling.ScrollOffsetForTesting(L"pin.scroll");
            Check(retainedOffset && *retainedOffset == *scrolledOffset &&
                      !scrolling.FreeScrollBindingForTesting(),
                  "compatible snapshot retains renderer state independently of binding lifetime");

            Check(scrolling.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      ScrollSnapshot(3), {}) &&
                      scrolling.selectedLayoutName() == L"Full widget",
                  "selected-layout removal falls back through coordinator authority");
            UpdateWindow(scrolling.window());
            const auto replacedOffset = scrolling.ScrollOffsetForTesting(L"pin.scroll");
            Check(replacedOffset && *replacedOffset == 0.0F,
                  "genuine selected-layout replacement forgets incompatible renderer state");
            Check(scrolling.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "scroll fixture performs exact teardown");
            scrolling.Dispose();
            std::error_code scrollCleanup;
            std::filesystem::remove_all(scrollRoot, scrollCleanup);
        }

        {
            const auto blockedParent = placementRoot / L"blocked-placement-parent";
            std::filesystem::create_directories(placementRoot);
            std::ofstream blocker(blockedParent, std::ios::trunc);
            Check(static_cast<bool>(blocker),
                  "failed-store fixture creates one deterministic parent-file blocker");
            blocker.close();
            widgetrail::pinned::WidgetSurfaceCoordinator failedStore;
            Check(failedStore.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x412,
                      d2d.Get(), write.Get(), nullptr, error,
                      blockedParent / L"placement.ini"),
                  "failed-store coordinator reuses the production placement owner");
            failedStore.OnOverlayShown();
            Check(failedStore.Pin(Admission(), error),
                  "failed-store fixture enters the ordinary new-pin setup");
            RECT failedOriginal{};
            GetWindowRect(failedStore.window(), &failedOriginal);
            Check(failedStore.StepPlacement(
                      widgetrail::pinned::PlacementMode::Resize,
                      widgetrail::pinned::PlacementDirection::Right),
                  "failed-store fixture produces one legal preview");
            RECT failedPreview{};
            GetWindowRect(failedStore.window(), &failedPreview);
            Check(!EqualRect(&failedOriginal, &failedPreview),
                  "failed-store preview visibly differs from its captured original");
            error.clear();
            Check(!failedStore.CommitPlacement(error) &&
                      error == L"Pinned placement directory is unavailable." &&
                      failedStore.placementMode() ==
                          widgetrail::pinned::PlacementMode::None,
                  "failed persistence reports its precise error and closes placement");
            RECT failedRestored{};
            GetWindowRect(failedStore.window(), &failedRestored);
            Check(EqualRect(&failedOriginal, &failedRestored) &&
                      !std::filesystem::exists(blockedParent / L"placement.ini"),
                  "failed persistence restores exact original bounds without claiming a save");
            Check(failedStore.Unpin(
                      widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "failed-store fixture performs exact teardown");
            failedStore.Dispose();
            std::error_code blockerCleanup;
            std::filesystem::remove(blockedParent, blockerCleanup);
        }

        const auto privateBefore = PrivateWorkingSetBytes();
        if (!coordinator.Pin(Admission(), error)) {
            std::wcerr << L"pin error: " << error << L'\n';
            Check(false, "current supporting widget creates one host-owned surface");
        }
        const HWND surface = coordinator.window();
        Check(surface && IsWindow(surface) && IsWindowVisible(surface),
              "real pinned HWND is visible");
        Check(coordinator.setupActive() && coordinator.controllerFocused() &&
                  coordinator.selectedLayoutName() == L"Full widget",
              "new pin immediately focuses host-owned Full widget setup");
        Check(coordinator.CommitSetup(error),
              "A-equivalent setup commit returns the new pin to click-through");
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
        RECT previewBounds{};
        GetWindowRect(surface, &previewBounds);
        const UINT committedDpi = std::max(1U, GetDpiForWindow(surface));
        const int placementStepPixels = MulDiv(
            static_cast<int>(
                widgetrail::surface_geometry::kPlacementAdjustmentStepDip),
            static_cast<int>(committedDpi), 96);
        Check((originalBounds.right - originalBounds.left) -
                  (previewBounds.right - previewBounds.left) == placementStepPixels,
              "coordinator resize consumes the centralized 32-DIP session step");
        Check(coordinator.CommitPlacement(error),
              "current generation atomically commits real-HWND geometry");
        RECT committedBounds{};
        GetWindowRect(surface, &committedBounds);
        Check(EqualRect(&previewBounds, &committedBounds),
              "successful commit retains the exact constrained preview bounds");
        widgetrail::pinned::PinnedPlacementStore committedStore(
            placementRoot / L"placement.ini");
        const auto durableCommitted = committedStore.Load(coordinator.widgetId());
        Check(durableCommitted &&
                  static_cast<int>(std::lround(
                      durableCommitted->widthDip * committedDpi / 96.0F)) ==
                      committedBounds.right - committedBounds.left &&
                  static_cast<int>(std::lround(
                      durableCommitted->heightDip * committedDpi / 96.0F)) ==
                      committedBounds.bottom - committedBounds.top,
              "successful commit persists exact logical bounds for durable reload");
        const auto originalOpacity = coordinator.opacityPercent();
        Check(coordinator.BeginOpacityAdjustment() &&
                  coordinator.StepOpacity(widgetrail::pinned::PlacementDirection::Left) &&
                  coordinator.opacityPercent() != originalOpacity &&
                  coordinator.CancelOpacity() &&
                  coordinator.opacityPercent() == originalOpacity,
              "B-equivalent opacity cancellation restores the exact prior alpha");
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
        RECT clickThroughBounds{};
        GetWindowRect(surface, &clickThroughBounds);
        Check(EqualRect(&committedBounds, &clickThroughBounds),
              "click-through transition retains committed placement bounds");
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
        RECT refreshedBounds{};
        GetWindowRect(surface, &refreshedBounds);
        Check(EqualRect(&committedBounds, &refreshedBounds),
              "ordinary snapshot publication retains committed placement bounds");

        Check(coordinator.ToggleInteractionMode(),
              "visible overlay can explicitly make the pin interactive");
        RECT interactiveBounds{};
        GetWindowRect(surface, &interactiveBounds);
        Check(EqualRect(&committedBounds, &interactiveBounds),
              "interactive focus transition retains committed placement bounds");
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
        RECT focusedBounds{};
        GetWindowRect(surface, &focusedBounds);
        Check(EqualRect(&committedBounds, &focusedBounds),
              "controller focus acquisition retains committed placement bounds");
        UpdateWindow(surface);
        Check(FindAutomationId(surface, L"widget:pin.fixture.action") &&
                  FindAutomationId(surface, L"host:pinned.close") &&
                  FindAutomationId(surface, L"host:pinned.emergency"),
              "interactive UIA composes widget content with Close and emergency host actions");
        Check(AutomationValueContains(surface, L"host:pinned.mode", L"B is widget Back") &&
                  AutomationValueContains(surface, L"host:pinned.mode", L"View returns to the tray"),
              "pinned accessibility semantics distinguish widget B from host View");
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
        Check(coordinator.CommitSetup(error),
              "Close fixture commits the new-pin setup transaction");
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
        Check(coordinator.CommitSetup(error),
              "emergency fixture commits the new-pin setup transaction");
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
