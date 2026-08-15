#include "AccessibilityProvider.h"

#include <UIAutomation.h>
#include <wrl.h>

#include <atomic>
#include <cmath>
#include <cstdlib>
#include <future>
#include <iostream>
#include <thread>
#include <vector>

namespace {

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

int checks{};

LRESULT CALLBACK TestWindowProc(
    HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    auto* host = reinterpret_cast<gba::accessibility::ProviderHost*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        host = static_cast<gba::accessibility::ProviderHost*>(create->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(host));
    }
    if (message == WM_GETOBJECT && host &&
        static_cast<LONG>(lParam) == UiaRootObjectId)
        return host->HandleWmGetObject(wParam, lParam);
    if (message == WM_APP + 42 && host) {
        host->RaisePendingEvents();
        return 0;
    }
    if (message == WM_APP + 43 && host) {
        host->SetWindowFocused(true);
        return 0;
    }
    if (message == WM_APP + 44 && host) {
        host->SetWindowFocused(false);
        return 0;
    }
    if (message == WM_CLOSE) {
        DestroyWindow(window);
        return 0;
    }
    if (message == WM_DESTROY) {
        if (!GetWindow(window, GW_OWNER)) PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

class ClientEventHandler final : public RuntimeClass<
    RuntimeClassFlags<ClassicCom>,
    IUIAutomationEventHandler,
    IUIAutomationFocusChangedEventHandler,
    IUIAutomationPropertyChangedEventHandler,
    IUIAutomationStructureChangedEventHandler> {
public:
    ClientEventHandler()
        : focusEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          propertyEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          boundsEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          rangeEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          liveRegionEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          structureEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)) {}

    ~ClientEventHandler() override {
        if (focusEvent_) CloseHandle(focusEvent_);
        if (propertyEvent_) CloseHandle(propertyEvent_);
        if (boundsEvent_) CloseHandle(boundsEvent_);
        if (rangeEvent_) CloseHandle(rangeEvent_);
        if (liveRegionEvent_) CloseHandle(liveRegionEvent_);
        if (structureEvent_) CloseHandle(structureEvent_);
    }

    IFACEMETHODIMP HandleAutomationEvent(
        IUIAutomationElement*, const EVENTID eventId) noexcept override {
        if (eventId == UIA_LiveRegionChangedEventId) {
            ++liveRegionCount_;
            if (liveRegionEvent_) SetEvent(liveRegionEvent_);
        }
        return S_OK;
    }

    IFACEMETHODIMP HandleFocusChangedEvent(IUIAutomationElement*) noexcept override {
        if (focusEvent_) SetEvent(focusEvent_);
        return S_OK;
    }

    IFACEMETHODIMP HandlePropertyChangedEvent(
        IUIAutomationElement*, const PROPERTYID propertyId,
        const VARIANT newValue) noexcept override {
        if (propertyId == UIA_NamePropertyId) {
            if (propertyEvent_) SetEvent(propertyEvent_);
        } else if (propertyId == UIA_BoundingRectanglePropertyId) {
            ++boundsCount_;
            if (boundsEvent_) SetEvent(boundsEvent_);
        } else if (propertyId == UIA_RangeValueValuePropertyId &&
                   V_VT(&newValue) == VT_R8) {
            rangeValue_.store(V_R8(&newValue));
            if (rangeEvent_) SetEvent(rangeEvent_);
        }
        return S_OK;
    }

    IFACEMETHODIMP HandleStructureChangedEvent(
        IUIAutomationElement*, StructureChangeType, SAFEARRAY*) noexcept override {
        if (structureEvent_) SetEvent(structureEvent_);
        return S_OK;
    }

    [[nodiscard]] bool WaitForFocus() const noexcept {
        return focusEvent_ && WaitForSingleObject(focusEvent_, 2000) == WAIT_OBJECT_0;
    }

    [[nodiscard]] bool WaitForProperty() const noexcept {
        return propertyEvent_ && WaitForSingleObject(propertyEvent_, 2000) == WAIT_OBJECT_0;
    }

    [[nodiscard]] bool WaitForBoundsCount(const int expected) const noexcept {
        if (!boundsEvent_ || WaitForSingleObject(boundsEvent_, 2000) != WAIT_OBJECT_0)
            return false;
        const ULONGLONG deadline = GetTickCount64() + 2000;
        while (boundsCount_.load() < expected && GetTickCount64() < deadline)
            Sleep(10);
        return boundsCount_.load() >= expected;
    }

    [[nodiscard]] bool WaitForRangeValue(const double expected) const noexcept {
        return rangeEvent_ && WaitForSingleObject(rangeEvent_, 2000) == WAIT_OBJECT_0 &&
            std::abs(rangeValue_.load() - expected) < 1e-9;
    }

    [[nodiscard]] bool WaitForLiveRegion() const noexcept {
        return liveRegionEvent_ &&
            WaitForSingleObject(liveRegionEvent_, 2000) == WAIT_OBJECT_0;
    }

    [[nodiscard]] int LiveRegionCount() const noexcept {
        return liveRegionCount_.load();
    }

    [[nodiscard]] bool WaitForStructure() const noexcept {
        return structureEvent_ && WaitForSingleObject(structureEvent_, 2000) == WAIT_OBJECT_0;
    }

private:
    HANDLE focusEvent_{};
    HANDLE propertyEvent_{};
    HANDLE boundsEvent_{};
    HANDLE rangeEvent_{};
    HANDLE liveRegionEvent_{};
    HANDLE structureEvent_{};
    std::atomic<int> boundsCount_{};
    std::atomic<int> liveRegionCount_{};
    std::atomic<double> rangeValue_{};
};

std::wstring StringProperty(
    IRawElementProviderSimple* provider, const PROPERTYID property) {
    VARIANT value{};
    Check(SUCCEEDED(provider->GetPropertyValue(property, &value)),
          "property lookup succeeds");
    Check(V_VT(&value) == VT_BSTR, "string property returns a BSTR");
    std::wstring result{V_BSTR(&value), SysStringLen(V_BSTR(&value))};
    VariantClear(&value);
    return result;
}

std::vector<LONG> RuntimeId(IRawElementProviderFragment* provider) {
    SAFEARRAY* array{};
    Check(SUCCEEDED(provider->GetRuntimeId(&array)) && array,
          "node has a runtime ID");
    LONG lower{};
    LONG upper{};
    Check(SUCCEEDED(SafeArrayGetLBound(array, 1, &lower)) &&
          SUCCEEDED(SafeArrayGetUBound(array, 1, &upper)),
          "runtime ID bounds are readable");
    std::vector<LONG> result;
    for (LONG index = lower; index <= upper; ++index) {
        LONG value{};
        Check(SUCCEEDED(SafeArrayGetElement(array, &index, &value)),
              "runtime ID item is readable");
        result.push_back(value);
    }
    SafeArrayDestroy(array);
    return result;
}

gba::accessibility::Tree Tree(const std::wstring_view generation, const long long sequence) {
    gba::accessibility::Tree tree;
    tree.widgetId = L"music";
    tree.runtimeGeneration = generation;
    tree.snapshotSequence = sequence;
    tree.activeInputScopeId = L"root";

    gba::accessibility::Node button;
    button.id = L"next";
    button.name = L"Next track";
    button.actionId = L"next";
    button.bounds = {10, 20, 100, 40};
    button.role = gba::accessibility::Role::Button;
    button.focused = true;
    tree.nodes.push_back(button);
    tree.focusedNode = 0;

    gba::accessibility::Node slider;
    slider.id = L"progress";
    slider.name = L"Playback position";
    slider.value = L"one minute";
    slider.valueChangedActionId = L"seek";
    slider.bounds = {10, 80, 200, 30};
    slider.role = gba::accessibility::Role::Slider;
    slider.rangeValue = 60;
    slider.rangeMinimum = 0;
    slider.rangeMaximum = 180;
    slider.rangeStep = 5;
    tree.nodes.push_back(slider);
    return tree;
}

gba::WidgetSnapshot Snapshot() {
    gba::WidgetSnapshot snapshot;
    snapshot.sequence = 9;
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    gba::WidgetNode button;
    button.id = L"next";
    button.kind = L"button";
    button.actionId = L"next";
    snapshot.root.children.push_back(button);
    gba::WidgetNode slider;
    slider.id = L"progress";
    slider.kind = L"slider";
    slider.valueChangedActionId = L"seek";
    slider.value = 60;
    slider.minimum = 0;
    slider.maximum = 180;
    slider.step = 5;
    snapshot.root.children.push_back(slider);
    return snapshot;
}

gba::accessibility::Tree HostTree(const bool includeDashboard = false) {
    gba::accessibility::Tree tree;
    tree.widgetId = L"host.tray";
    tree.runtimeGeneration = L"host";
    tree.snapshotSequence = 21;
    tree.activeInputScopeId = L"host.tray";
    gba::accessibility::Node music;
    music.id = L"tray.music";
    music.domain = gba::accessibility::ElementDomain::Tray;
    music.name = L"YT Music";
    music.hostTargetId = L"music";
    music.bounds = {20, 220, 64, 64};
    music.role = gba::accessibility::Role::ListItem;
    music.hostAction = gba::accessibility::HostAction::ActivateTrayItem;
    music.positionInSet = 3;
    music.sizeOfSet = 8;
    music.selected = true;
    music.focused = true;
    tree.nodes.push_back(music);
    tree.focusedNode = 0;
    if (includeDashboard) {
        gba::accessibility::Node heading;
        heading.id = L"host.dashboard.title";
        heading.domain = gba::accessibility::ElementDomain::HostShell;
        heading.name = L"YT Music";
        heading.bounds = {20, 10, 760, 34};
        heading.role = gba::accessibility::Role::Heading;
        heading.headingLevel = gba::accessibility::HeadingLevel::Level1;
        tree.nodes.push_back(heading);
        gba::accessibility::Node help;
        help.id = L"host.dashboard.help";
        help.domain = gba::accessibility::ElementDomain::HostShell;
        help.name = L"A Select  B Close";
        help.bounds = {20, 44, 760, 22};
        help.role = gba::accessibility::Role::Text;
        tree.nodes.push_back(help);
    }
    return tree;
}

gba::accessibility::Tree OpenHostTree() {
    gba::accessibility::Tree tree;
    tree.widgetId = L"music";
    tree.runtimeGeneration = L"generation-open";
    tree.snapshotSequence = 30;
    tree.activeInputScopeId = L"music.sheet";
    tree.name = L"YT Music · Game Bar Alternative";
    gba::accessibility::Node back;
    back.id = L"host.open.back";
    back.domain = gba::accessibility::ElementDomain::HostShell;
    back.name = L"Back";
    back.bounds = {300, 240, 80, 30};
    back.role = gba::accessibility::Role::Button;
    back.hostAction = gba::accessibility::HostAction::BackWithinWidget;
    back.hostTargetId = L"music.sheet";
    back.keyboardFocusable = false;
    tree.nodes.push_back(back);
    gba::accessibility::Node close;
    close.id = L"host.open.close";
    close.domain = gba::accessibility::ElementDomain::HostShell;
    close.name = L"Close overlay";
    close.bounds = {380, 240, 100, 30};
    close.role = gba::accessibility::Role::Button;
    close.hostAction = gba::accessibility::HostAction::CloseOverlay;
    close.keyboardFocusable = false;
    tree.nodes.push_back(close);
    return tree;
}

gba::accessibility::Tree CollisionTree() {
    gba::accessibility::Tree tree;
    tree.widgetId = L"music";
    tree.runtimeGeneration = L"generation-collision";
    tree.snapshotSequence = 31;
    tree.activeInputScopeId = L"music.root";
    gba::accessibility::Node widget;
    widget.id = L"host.open.back";
    widget.name = L"Widget action";
    widget.actionId = L"widget.action";
    widget.role = gba::accessibility::Role::Button;
    widget.focused = true;
    tree.nodes.push_back(widget);
    tree.focusedNode = 0;
    gba::accessibility::Node hostBack;
    hostBack.domain = gba::accessibility::ElementDomain::HostShell;
    hostBack.id = L"host.open.back";
    hostBack.name = L"Back to widget tray";
    hostBack.hostAction = gba::accessibility::HostAction::BackToTray;
    hostBack.hostTargetId = L"music.root";
    hostBack.role = gba::accessibility::Role::Button;
    hostBack.keyboardFocusable = false;
    tree.nodes.push_back(hostBack);
    return tree;
}

} // namespace

int main() {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "COM initializes for provider coverage");
    gba::accessibility::ProviderHost host;
    gba::accessibility::ProviderHost chromeHost;
    std::promise<std::pair<HWND, HWND>> windowPromise;
    auto windowFuture = windowPromise.get_future();
    std::jthread windowThread([&] {
        const HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        WNDCLASSW windowClass{};
        windowClass.lpfnWndProc = TestWindowProc;
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpszClassName = L"GameBarAlternative.AccessibilityProviderTests";
        RegisterClassW(&windowClass);
        HWND threadWindow = CreateWindowExW(
            0, windowClass.lpszClassName, L"AccessibilityProviderTests", WS_OVERLAPPED,
            0, 0, 400, 300, nullptr, nullptr, windowClass.hInstance, &host);
        HWND threadChromeWindow = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            windowClass.lpszClassName, L"AccessibilityProviderChromeTests", WS_POPUP,
            100, 400, 300, 100, threadWindow, nullptr, windowClass.hInstance,
            &chromeHost);
        host.Bind(threadWindow, WM_APP + 42);
        chromeHost.Bind(threadChromeWindow, WM_APP + 42);
        windowPromise.set_value({threadWindow, threadChromeWindow});
        if (threadWindow) {
            MSG message{};
            while (GetMessageW(&message, nullptr, 0, 0) > 0) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        UnregisterClassW(windowClass.lpszClassName, windowClass.hInstance);
        if (SUCCEEDED(apartment)) CoUninitialize();
    });
    const auto [window, chromeWindow] = windowFuture.get();
    Check(window != nullptr && chromeWindow != nullptr,
          "content and owned chrome HWNDs are created on one pumping UI thread");
    host.Publish(Tree(L"generation-1", 9), {100, 200, 2, 800, 600});

    ComPtr<IUIAutomation> client;
    Check(SUCCEEDED(CoCreateInstance(
              CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
              IID_PPV_ARGS(client.GetAddressOf()))) && client,
          "Windows UI Automation client is available");
    ComPtr<IUIAutomationElement> clientRoot;
    Check(SUCCEEDED(client->ElementFromHandle(window, clientRoot.GetAddressOf())) && clientRoot,
          "UIA client obtains the provider through WM_GETOBJECT");
    BSTR clientRootName{};
    Check(SUCCEEDED(clientRoot->get_CurrentName(&clientRootName)) &&
          std::wstring_view{clientRootName, SysStringLen(clientRootName)} ==
              L"Game Bar Alternative",
          "UIA client reads the custom root name");
    SysFreeString(clientRootName);
    VARIANT automationId{};
    V_VT(&automationId) = VT_BSTR;
    V_BSTR(&automationId) = SysAllocString(L"widget:next");
    ComPtr<IUIAutomationCondition> buttonCondition;
    Check(SUCCEEDED(client->CreatePropertyCondition(
              UIA_AutomationIdPropertyId, automationId,
              buttonCondition.GetAddressOf())) && buttonCondition,
          "UIA client creates an AutomationId condition");
    VariantClear(&automationId);
    ComPtr<IUIAutomationElement> clientButton;
    Check(SUCCEEDED(clientRoot->FindFirst(
              TreeScope_Descendants, buttonCondition.Get(),
              clientButton.GetAddressOf())) && clientButton,
          "UIA client traverses to the semantic button");
    BSTR clientButtonName{};
    Check(SUCCEEDED(clientButton->get_CurrentName(&clientButtonName)) &&
          std::wstring_view{clientButtonName, SysStringLen(clientButtonName)} == L"Next track",
          "UIA client reads the semantic button name");
    SysFreeString(clientButtonName);

    const auto partition = gba::accessibility::PartitionForFixedChrome(
        HostTree(true), 200.0F, 0.0F, 200.0F);
    host.Publish(partition.content, {0, 0, 1, 400, 300});
    chromeHost.Publish(partition.chrome, {100, 400, 1, 300, 100});
    SendMessageW(window, WM_APP + 42, 0, 0);
    SendMessageW(chromeWindow, WM_APP + 42, 0, 0);
    ComPtr<IUIAutomationElement> chromeClientRoot;
    Check(SUCCEEDED(client->ElementFromHandle(
              chromeWindow, chromeClientRoot.GetAddressOf())) && chromeClientRoot,
          "UIA client obtains a root bound to the fixed chrome HWND");
    VARIANT trayId{};
    V_VT(&trayId) = VT_BSTR;
    V_BSTR(&trayId) = SysAllocString(L"tray:tray.music");
    ComPtr<IUIAutomationCondition> trayOnlyCondition;
    Check(SUCCEEDED(client->CreatePropertyCondition(
              UIA_AutomationIdPropertyId, trayId,
              trayOnlyCondition.GetAddressOf())) && trayOnlyCondition,
          "UIA client creates the fixed-chrome partition condition");
    VariantClear(&trayId);
    ComPtr<IUIAutomationElement> contentTray;
    ComPtr<IUIAutomationElement> chromeTray;
    Check(SUCCEEDED(clientRoot->FindFirst(
              TreeScope_Descendants, trayOnlyCondition.Get(),
              contentTray.GetAddressOf())) && !contentTray &&
          SUCCEEDED(chromeClientRoot->FindFirst(
              TreeScope_Descendants, trayOnlyCondition.Get(),
              chromeTray.GetAddressOf())) && chromeTray,
          "tray semantics are published once by the chrome HWND root");
    RECT chromeWindowBounds{};
    RECT contentWindowBounds{};
    tagRECT chromeRootBounds{};
    tagRECT contentRootBounds{};
    Check(GetWindowRect(chromeWindow, &chromeWindowBounds) &&
          GetWindowRect(window, &contentWindowBounds) &&
          SUCCEEDED(chromeClientRoot->get_CurrentBoundingRectangle(&chromeRootBounds)) &&
          SUCCEEDED(clientRoot->get_CurrentBoundingRectangle(&contentRootBounds)) &&
          chromeRootBounds.left == chromeWindowBounds.left &&
          chromeRootBounds.top == chromeWindowBounds.top &&
          chromeRootBounds.right == chromeWindowBounds.right &&
          chromeRootBounds.bottom == chromeWindowBounds.bottom &&
          contentRootBounds.left == contentWindowBounds.left &&
          contentRootBounds.top == contentWindowBounds.top &&
          contentRootBounds.right == contentWindowBounds.right &&
          contentRootBounds.bottom == contentWindowBounds.bottom,
          "each UIA root reports its actual HWND screen rectangle");
    ComPtr<IRawElementProviderSimple> contentPartitionRoot;
    ComPtr<IRawElementProviderSimple> chromePartitionRoot;
    ComPtr<IRawElementProviderFragmentRoot> contentFragmentRoot;
    ComPtr<IRawElementProviderFragmentRoot> chromeFragmentRoot;
    ComPtr<IRawElementProviderFragment> contentFocus;
    ComPtr<IRawElementProviderFragment> chromeFocus;
    Check(SUCCEEDED(host.GetRootProvider(contentPartitionRoot.GetAddressOf())) &&
          SUCCEEDED(chromeHost.GetRootProvider(chromePartitionRoot.GetAddressOf())) &&
          SUCCEEDED(contentPartitionRoot.As(&contentFragmentRoot)) &&
          SUCCEEDED(chromePartitionRoot.As(&chromeFragmentRoot)) &&
          SUCCEEDED(contentFragmentRoot->GetFocus(contentFocus.GetAddressOf())) &&
          !contentFocus &&
          SUCCEEDED(chromeFragmentRoot->GetFocus(chromeFocus.GetAddressOf())) &&
          chromeFocus,
          "logical tray focus is exposed only by the chrome endpoint");
    ComPtr<IUIAutomationInvokePattern> chromeInvoke;
    Check(SUCCEEDED(chromeTray->GetCurrentPatternAs(
              UIA_InvokePatternId, IID_PPV_ARGS(chromeInvoke.GetAddressOf()))) &&
          chromeInvoke && SUCCEEDED(chromeInvoke->Invoke()),
          "chrome tray action enters its HWND-bound provider queue");
    const auto chromeActions = chromeHost.TakeActions();
    Check(chromeActions.size() == 1 &&
              chromeActions[0].domain == gba::accessibility::ElementDomain::Tray &&
              chromeActions[0].hostAction ==
                  gba::accessibility::HostAction::ActivateTrayItem &&
              chromeActions[0].hostTargetId == L"music",
          "chrome action retains exact typed identity for the sole host action owner");
    host.Publish(Tree(L"generation-1", 9), {100, 200, 2, 800, 600});
    SendMessageW(window, WM_APP + 42, 0, 0);

    ComPtr<IRawElementProviderSimple> root;
    Check(SUCCEEDED(host.GetRootProvider(root.GetAddressOf())) && root,
          "root provider is available");
    Check(StringProperty(root.Get(), UIA_NamePropertyId) == L"Game Bar Alternative",
          "root provider has a stable accessible name");
    SendMessageW(window, WM_APP + 43, 0, 0);
    VARIANT rootFocus{};
    BOOL clientRootFocused{};
    Check(SUCCEEDED(root->GetPropertyValue(UIA_HasKeyboardFocusPropertyId, &rootFocus)) &&
          V_VT(&rootFocus) == VT_BOOL && V_BOOL(&rootFocus) == VARIANT_TRUE,
          "root focus reads UI-thread-published state from a client thread");
    Check(SUCCEEDED(clientRoot->get_CurrentHasKeyboardFocus(&clientRootFocused)) &&
          clientRootFocused,
          "real UIA client reads published root focus without thread-local GetFocus");
    VariantClear(&rootFocus);
    SendMessageW(window, WM_APP + 44, 0, 0);
    Check(SUCCEEDED(root->GetPropertyValue(UIA_HasKeyboardFocusPropertyId, &rootFocus)) &&
          V_VT(&rootFocus) == VT_BOOL && V_BOOL(&rootFocus) == VARIANT_FALSE,
          "root focus clears from UI-thread-published state");
    Check(SUCCEEDED(clientRoot->get_CurrentHasKeyboardFocus(&clientRootFocused)) &&
          !clientRootFocused,
          "real UIA client observes published root focus loss");
    VariantClear(&rootFocus);

    ComPtr<IRawElementProviderFragment> rootFragment;
    Check(SUCCEEDED(root.As(&rootFragment)), "root exposes fragment navigation");
    ComPtr<IRawElementProviderFragment> buttonFragment;
    Check(SUCCEEDED(rootFragment->Navigate(
              NavigateDirection_FirstChild, buttonFragment.GetAddressOf())) &&
          buttonFragment, "root navigates to the first semantic node");
    ComPtr<IRawElementProviderSimple> button;
    Check(SUCCEEDED(buttonFragment.As(&button)), "button exposes simple provider");
    Check(StringProperty(button.Get(), UIA_NamePropertyId) == L"Next track",
          "button name comes from the immutable tree");

    UiaRect bounds{};
    Check(SUCCEEDED(buttonFragment->get_BoundingRectangle(&bounds)) &&
          bounds.left == 120 && bounds.top == 240 &&
          bounds.width == 200 && bounds.height == 80,
          "DIP geometry is transformed to physical screen coordinates");

    ComPtr<IUnknown> invokeUnknown;
    Check(SUCCEEDED(button->GetPatternProvider(
              UIA_InvokePatternId, invokeUnknown.GetAddressOf())) && invokeUnknown,
          "button advertises Invoke");
    ComPtr<IInvokeProvider> invoke;
    Check(SUCCEEDED(invokeUnknown.As(&invoke)), "Invoke pattern is queryable");
    Check(SUCCEEDED(invoke->Invoke()), "Invoke is admitted asynchronously");
    auto actions = host.TakeActions();
    Check(actions.size() == 1 && actions[0].kind == gba::accessibility::ActionKind::Invoke &&
          actions[0].widgetId == L"music" && actions[0].runtimeGeneration == L"generation-1" &&
          actions[0].snapshotSequence == 9 && actions[0].activeInputScopeId == L"root" &&
          actions[0].domain == gba::accessibility::ElementDomain::Widget &&
          actions[0].nodeId == L"next" && actions[0].actionId == L"next",
          "Invoke retains the full generation and snapshot authority tuple");

    Check(SUCCEEDED(buttonFragment->SetFocus()), "focus request is admitted asynchronously");
    actions = host.TakeActions();
    Check(actions.size() == 1 && actions[0].kind == gba::accessibility::ActionKind::Focus &&
          actions[0].domain == gba::accessibility::ElementDomain::Widget &&
          actions[0].nodeId == L"next", "focus request retains node identity");

    ComPtr<IRawElementProviderFragmentRoot> fragmentRoot;
    Check(SUCCEEDED(root.As(&fragmentRoot)), "root fragment interface is queryable");
    ComPtr<IRawElementProviderFragment> focused;
    Check(SUCCEEDED(fragmentRoot->GetFocus(focused.GetAddressOf())) && focused,
          "logical controller focus is exposed through UIA");

    ComPtr<IRawElementProviderFragment> sliderFragment;
    Check(SUCCEEDED(buttonFragment->Navigate(
              NavigateDirection_NextSibling, sliderFragment.GetAddressOf())) && sliderFragment,
          "sibling navigation reaches the slider");
    ComPtr<IRawElementProviderSimple> slider;
    Check(SUCCEEDED(sliderFragment.As(&slider)), "slider exposes simple provider");
    ComPtr<IUnknown> rangeUnknown;
    Check(SUCCEEDED(slider->GetPatternProvider(
              UIA_RangeValuePatternId, rangeUnknown.GetAddressOf())) && rangeUnknown,
          "slider advertises RangeValue");
    ComPtr<IRangeValueProvider> range;
    Check(SUCCEEDED(rangeUnknown.As(&range)), "RangeValue pattern is queryable");
    double value{};
    Check(SUCCEEDED(range->get_Value(&value)) && value == 60,
          "RangeValue reports the immutable current value");
    Check(range->SetValue(-1) == E_INVALIDARG, "out-of-range values are rejected");
    Check(SUCCEEDED(range->SetValue(66)) && SUCCEEDED(range->SetValue(72)),
          "valid RangeValue changes are admitted");
    actions = host.TakeActions();
    Check(actions.size() == 1 && actions[0].kind == gba::accessibility::ActionKind::SetValue &&
          actions[0].requestedValue == 70 && actions[0].actionId == L"seek",
          "RangeValue is quantized and coalesced latest-wins per slider");

    auto currentSnapshot = Snapshot();
    const auto resolved = gba::accessibility::ResolveActionRequest(
        actions[0], L"music", L"generation-1", currentSnapshot);
    Check(resolved && resolved->protocolButton == L"DPadRight" &&
          resolved->requestedValue == 70,
          "UI thread resolves a current RangeValue request to controller input");
    auto staleSequence = currentSnapshot;
    staleSequence.sequence = 10;
    Check(!gba::accessibility::ResolveActionRequest(
              actions[0], L"music", L"generation-1", staleSequence),
          "queued request is rejected after snapshot replacement");
    Check(!gba::accessibility::ResolveActionRequest(
              actions[0], L"music", L"generation-2", currentSnapshot),
          "queued request is rejected after runtime replacement");
    auto unavailable = currentSnapshot;
    unavailable.root.children[1].isBusy = true;
    Check(!gba::accessibility::ResolveActionRequest(
              actions[0], L"music", L"generation-1", unavailable),
          "queued request is rejected when its current control becomes unavailable");

    const auto runtimeId = RuntimeId(buttonFragment.Get());
    host.Publish(Tree(L"generation-1", 10), {100, 200, 2, 800, 600});
    Check(RuntimeId(buttonFragment.Get()) == runtimeId,
          "runtime ID remains stable across snapshots in one runtime generation");
    bool capacityAdmitted = true;
    for (std::size_t index = 0; index < 64; ++index)
        capacityAdmitted = SUCCEEDED(invoke->Invoke()) && capacityAdmitted;
    Check(capacityAdmitted, "Invoke queue admits requests through its hard capacity");
    Check(invoke->Invoke() == UIA_E_INVALIDOPERATION,
          "Invoke queue fails closed above its hard capacity");
    Check(host.TakeActions().size() == 64, "Invoke queue never exceeds its hard capacity");
    host.Publish(Tree(L"generation-2", 11), {100, 200, 2, 800, 600});
    Check(invoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,
          "stale provider cannot enqueue into a replacement runtime");
    Check(host.TakeActions().empty(), "stale provider leaves the action queue unchanged");

    host.Publish(HostTree(), {100, 200, 2, 800, 600});
    ComPtr<IUnknown> selectionUnknown;
    Check(SUCCEEDED(root->GetPatternProvider(
              UIA_SelectionPatternId, selectionUnknown.GetAddressOf())) && selectionUnknown,
          "tray root advertises single selection");
    ComPtr<ISelectionProvider> selection;
    Check(SUCCEEDED(selectionUnknown.As(&selection)), "Selection pattern is queryable");
    BOOL required{};
    BOOL multiple{TRUE};
    Check(SUCCEEDED(selection->get_IsSelectionRequired(&required)) && required &&
          SUCCEEDED(selection->get_CanSelectMultiple(&multiple)) && !multiple,
          "tray requires exactly one selected item");
    ComPtr<IRawElementProviderFragment> trayFragment;
    Check(SUCCEEDED(rootFragment->Navigate(
              NavigateDirection_FirstChild, trayFragment.GetAddressOf())) && trayFragment,
          "root navigates to the visible tray item");
    ComPtr<IRawElementProviderSimple> trayItem;
    Check(SUCCEEDED(trayFragment.As(&trayItem)) &&
          StringProperty(trayItem.Get(), UIA_NamePropertyId) == L"YT Music",
          "tray item exposes its shell-owned name");
    VARIANT positionInSet{};
    VARIANT sizeOfSet{};
    Check(SUCCEEDED(trayItem->GetPropertyValue(
              UIA_PositionInSetPropertyId, &positionInSet)) &&
          V_VT(&positionInSet) == VT_I4 && V_I4(&positionInSet) == 3 &&
          SUCCEEDED(trayItem->GetPropertyValue(
              UIA_SizeOfSetPropertyId, &sizeOfSet)) &&
          V_VT(&sizeOfSet) == VT_I4 && V_I4(&sizeOfSet) == 8,
          "tray item announces its stable catalog position and full set size");
    VariantClear(&positionInSet);
    VariantClear(&sizeOfSet);
    ComPtr<IUnknown> selectionItemUnknown;
    Check(SUCCEEDED(trayItem->GetPatternProvider(
              UIA_SelectionItemPatternId, selectionItemUnknown.GetAddressOf())) &&
          selectionItemUnknown,
          "tray item advertises SelectionItem");
    ComPtr<ISelectionItemProvider> selectionItem;
    Check(SUCCEEDED(selectionItemUnknown.As(&selectionItem)),
          "SelectionItem pattern is queryable");
    Check(SUCCEEDED(selectionItem->Select()), "tray selection posts asynchronously");
    actions = host.TakeActions();
    Check(actions.size() == 1 && actions[0].kind == gba::accessibility::ActionKind::Focus &&
          actions[0].hostAction == gba::accessibility::HostAction::ActivateTrayItem &&
          actions[0].hostTargetId == L"music",
          "tray selection retains closed host target authority");
    ComPtr<IUnknown> trayInvokeUnknown;
    Check(SUCCEEDED(trayItem->GetPatternProvider(
              UIA_InvokePatternId, trayInvokeUnknown.GetAddressOf())) && trayInvokeUnknown,
          "tray item advertises Invoke");
    ComPtr<IInvokeProvider> trayInvoke;
    Check(SUCCEEDED(trayInvokeUnknown.As(&trayInvoke)) && SUCCEEDED(trayInvoke->Invoke()),
          "tray Invoke posts asynchronously");
    actions = host.TakeActions();
    Check(actions.size() == 1 && actions[0].kind == gba::accessibility::ActionKind::Invoke &&
          actions[0].domain == gba::accessibility::ElementDomain::Tray &&
          actions[0].hostTargetId == L"music",
          "tray Invoke retains the selected widget target");

    auto replacementRuntimeTree = HostTree();
    replacementRuntimeTree.widgetId = L"network-controls";
    replacementRuntimeTree.runtimeGeneration = L"generation-network-controls";
    replacementRuntimeTree.snapshotSequence = 30;
    host.Publish(replacementRuntimeTree, {100, 200, 2, 800, 600});
    Check(StringProperty(trayItem.Get(), UIA_NamePropertyId) == L"YT Music",
          "host-owned tray provider identity survives the selected widget runtime change");

    host.Publish(OpenHostTree(), {100, 200, 2, 800, 600});
    BSTR openRootName{};
    Check(SUCCEEDED(clientRoot->get_CurrentName(&openRootName)) &&
          std::wstring_view{openRootName, SysStringLen(openRootName)} ==
              L"YT Music · Game Bar Alternative",
          "real UIA client reads open-widget page context from the composite root");
    SysFreeString(openRootName);
    ComPtr<IRawElementProviderFragment> backFragment;
    ComPtr<IRawElementProviderSimple> backProvider;
    Check(SUCCEEDED(rootFragment->Navigate(
              NavigateDirection_FirstChild, backFragment.GetAddressOf())) &&
          backFragment && SUCCEEDED(backFragment.As(&backProvider)),
          "composite root traverses to the host Back command");
    VARIANT backFocusable{};
    Check(SUCCEEDED(backProvider->GetPropertyValue(
              UIA_IsKeyboardFocusablePropertyId, &backFocusable)) &&
          V_VT(&backFocusable) == VT_BOOL && V_BOOL(&backFocusable) == VARIANT_FALSE &&
          backFragment->SetFocus() == UIA_E_NOTSUPPORTED,
          "host Back remains invokable without creating a second logical focus owner");
    VariantClear(&backFocusable);
    ComPtr<IUnknown> backInvokeUnknown;
    ComPtr<IInvokeProvider> backInvoke;
    Check(SUCCEEDED(backProvider->GetPatternProvider(
              UIA_InvokePatternId, backInvokeUnknown.GetAddressOf())) &&
          backInvokeUnknown && SUCCEEDED(backInvokeUnknown.As(&backInvoke)) &&
          SUCCEEDED(backInvoke->Invoke()),
          "host Back exposes a closed Invoke pattern");
    actions = host.TakeActions();
    Check(actions.size() == 1 &&
          actions[0].domain == gba::accessibility::ElementDomain::HostShell &&
          actions[0].hostAction == gba::accessibility::HostAction::BackWithinWidget &&
          actions[0].hostTargetId == L"music.sheet" &&
          actions[0].widgetId == L"music" &&
          actions[0].runtimeGeneration == L"generation-open" &&
          actions[0].snapshotSequence == 30,
          "host Back retains the composite widget generation tuple");
    ComPtr<IRawElementProviderFragment> closeFragment;
    ComPtr<IRawElementProviderSimple> closeProvider;
    ComPtr<IUnknown> closeInvokeUnknown;
    ComPtr<IInvokeProvider> closeInvoke;
    Check(SUCCEEDED(backFragment->Navigate(
              NavigateDirection_NextSibling, closeFragment.GetAddressOf())) &&
          closeFragment && SUCCEEDED(closeFragment.As(&closeProvider)) &&
          SUCCEEDED(closeProvider->GetPatternProvider(
              UIA_InvokePatternId, closeInvokeUnknown.GetAddressOf())) &&
          closeInvokeUnknown && SUCCEEDED(closeInvokeUnknown.As(&closeInvoke)) &&
          SUCCEEDED(closeInvoke->Invoke()),
          "host Close is separately traversable and invokable");
    actions = host.TakeActions();
    Check(actions.size() == 1 &&
          actions[0].domain == gba::accessibility::ElementDomain::HostShell &&
          actions[0].hostAction == gba::accessibility::HostAction::CloseOverlay,
          "host Close queues only the typed idempotent close authority");

    const auto collisionTree = CollisionTree();
    Check(gba::accessibility::HasUniqueElementKeys(collisionTree),
          "cross-domain raw-ID reuse is a valid composite tree");
    host.Publish(collisionTree, {100, 200, 2, 800, 600});
    ComPtr<IRawElementProviderFragment> collisionWidget;
    ComPtr<IRawElementProviderFragment> collisionHost;
    Check(SUCCEEDED(rootFragment->Navigate(
              NavigateDirection_FirstChild, collisionWidget.GetAddressOf())) &&
          collisionWidget && SUCCEEDED(collisionWidget->Navigate(
              NavigateDirection_NextSibling, collisionHost.GetAddressOf())) &&
          collisionHost,
          "provider traverses both elements that share a raw ID");
    ComPtr<IRawElementProviderSimple> collisionWidgetSimple;
    ComPtr<IRawElementProviderSimple> collisionHostSimple;
    Check(SUCCEEDED(collisionWidget.As(&collisionWidgetSimple)) &&
          SUCCEEDED(collisionHost.As(&collisionHostSimple)) &&
          StringProperty(collisionWidgetSimple.Get(), UIA_AutomationIdPropertyId) ==
              L"widget:host.open.back" &&
          StringProperty(collisionHostSimple.Get(), UIA_AutomationIdPropertyId) ==
              L"host:host.open.back" &&
          RuntimeId(collisionWidget.Get()) != RuntimeId(collisionHost.Get()),
          "AutomationId and runtime identity remain collision-proof by owner domain");
    ComPtr<IUnknown> collisionWidgetInvokeUnknown;
    ComPtr<IUnknown> collisionHostInvokeUnknown;
    ComPtr<IInvokeProvider> collisionWidgetInvoke;
    ComPtr<IInvokeProvider> collisionHostInvoke;
    Check(SUCCEEDED(collisionWidgetSimple->GetPatternProvider(
              UIA_InvokePatternId, collisionWidgetInvokeUnknown.GetAddressOf())) &&
          SUCCEEDED(collisionHostSimple->GetPatternProvider(
              UIA_InvokePatternId, collisionHostInvokeUnknown.GetAddressOf())) &&
          collisionWidgetInvokeUnknown && collisionHostInvokeUnknown &&
          SUCCEEDED(collisionWidgetInvokeUnknown.As(&collisionWidgetInvoke)) &&
          SUCCEEDED(collisionHostInvokeUnknown.As(&collisionHostInvoke)) &&
          SUCCEEDED(collisionWidgetInvoke->Invoke()) &&
          SUCCEEDED(collisionHostInvoke->Invoke()),
          "both colliding raw IDs retain their independent Invoke workflows");
    actions = host.TakeActions();
    Check(actions.size() == 2 &&
          actions[0].domain == gba::accessibility::ElementDomain::Widget &&
          actions[0].actionId == L"widget.action" &&
          actions[1].domain == gba::accessibility::ElementDomain::HostShell &&
          actions[1].hostAction == gba::accessibility::HostAction::BackToTray,
          "queued actions retain typed identity through the real provider workflow");
    auto duplicateTree = collisionTree;
    duplicateTree.nodes.push_back(duplicateTree.nodes[0]);
    host.Publish(duplicateTree, {100, 200, 2, 800, 600});
    ComPtr<IRawElementProviderFragment> invalidChild;
    Check(SUCCEEDED(rootFragment->Navigate(
              NavigateDirection_FirstChild, invalidChild.GetAddressOf())) && !invalidChild,
          "provider fails closed instead of publishing duplicate same-domain identity");

    host.Publish(HostTree(), {100, 200, 2, 800, 600});
    VARIANT trayAutomationId{};
    V_VT(&trayAutomationId) = VT_BSTR;
    V_BSTR(&trayAutomationId) = SysAllocString(L"tray:tray.music");
    ComPtr<IUIAutomationCondition> trayCondition;
    Check(SUCCEEDED(client->CreatePropertyCondition(
              UIA_AutomationIdPropertyId, trayAutomationId,
              trayCondition.GetAddressOf())) && trayCondition,
          "real UIA client creates a tray-item condition");
    VariantClear(&trayAutomationId);
    ComPtr<IUIAutomationElement> clientTrayItem;
    Check(SUCCEEDED(clientRoot->FindFirst(
              TreeScope_Descendants, trayCondition.Get(),
              clientTrayItem.GetAddressOf())) && clientTrayItem,
          "real UIA client discovers the visible tray item");
    CONTROLTYPEID trayControlType{};
    Check(SUCCEEDED(clientTrayItem->get_CurrentControlType(&trayControlType)) &&
          trayControlType == UIA_ListItemControlTypeId,
          "real UIA client sees the closed ListItem control type");
    ComPtr<IUnknown> clientSelectionItem;
    Check(SUCCEEDED(clientTrayItem->GetCurrentPattern(
              UIA_SelectionItemPatternId, clientSelectionItem.GetAddressOf())) &&
          clientSelectionItem,
          "real UIA client obtains SelectionItem from the tray item");

    SendMessageW(window, WM_APP + 42, 0, 0);
    ComPtr<ClientEventHandler> eventHandler = Make<ClientEventHandler>();
    Check(eventHandler, "real UIA event handler is created");
    SAFEARRAY* observedProperties = SafeArrayCreateVector(VT_I4, 0, 3);
    LONG observedPropertyIndex{};
    PROPERTYID observedProperty = UIA_NamePropertyId;
    const bool nameFilterAdded = observedProperties && SUCCEEDED(SafeArrayPutElement(
        observedProperties, &observedPropertyIndex, &observedProperty));
    ++observedPropertyIndex;
    observedProperty = UIA_BoundingRectanglePropertyId;
    const bool boundsFilterAdded = nameFilterAdded && SUCCEEDED(SafeArrayPutElement(
        observedProperties, &observedPropertyIndex, &observedProperty));
    ++observedPropertyIndex;
    observedProperty = UIA_RangeValueValuePropertyId;
    Check(boundsFilterAdded && SUCCEEDED(SafeArrayPutElement(
              observedProperties, &observedPropertyIndex, &observedProperty)),
          "real UIA property subscription filter is created");
    Check(SUCCEEDED(client->AddFocusChangedEventHandler(
              nullptr, eventHandler.Get())) &&
          SUCCEEDED(client->AddPropertyChangedEventHandler(
              clientRoot.Get(), static_cast<TreeScope>(TreeScope_Element | TreeScope_Subtree),
              nullptr, eventHandler.Get(),
              observedProperties)) &&
          SUCCEEDED(client->AddAutomationEventHandler(
              UIA_LiveRegionChangedEventId, clientRoot.Get(),
              static_cast<TreeScope>(TreeScope_Element | TreeScope_Subtree),
              nullptr, eventHandler.Get())) &&
          SUCCEEDED(client->AddStructureChangedEventHandler(
              clientRoot.Get(), static_cast<TreeScope>(TreeScope_Element | TreeScope_Subtree),
              nullptr, eventHandler.Get())),
          "real UIA client subscribes to focus, property, live-region, and structure events");
    SafeArrayDestroy(observedProperties);
    auto changedHostTree = HostTree(true);
    changedHostTree.nodes[0].selected = false;
    changedHostTree.nodes[0].focused = false;
    gba::accessibility::Node settings;
    settings.id = L"tray.settings";
    settings.domain = gba::accessibility::ElementDomain::Tray;
    settings.name = L"Settings";
    settings.hostTargetId = L"settings";
    settings.bounds = {100, 220, 64, 64};
    settings.role = gba::accessibility::Role::ListItem;
    settings.hostAction = gba::accessibility::HostAction::ActivateTrayItem;
    settings.selected = true;
    settings.focused = true;
    changedHostTree.nodes.push_back(settings);
    changedHostTree.focusedNode = 3;
    auto finalHostTree = changedHostTree;
    finalHostTree.nodes[0].name = L"YouTube Music";
    finalHostTree.nodes[2].name = L"A Select  B Close  Y Reorder";
    host.Publish(changedHostTree, {100, 200, 2, 800, 600});
    host.Publish(finalHostTree, {100, 200, 2, 800, 600});
    SendMessageW(window, WM_APP + 43, 0, 0);
    SendMessageW(window, WM_APP + 42, 0, 0);
    Check(eventHandler->WaitForFocus(),
          "real UIA client receives the coalesced logical-focus event");
    Check(eventHandler->WaitForProperty(),
          "real UIA client receives a closed semantic property event");
    Check(eventHandler->WaitForStructure(),
          "real UIA client receives the coalesced structure event");
    BSTR coalescedName{};
    Check(SUCCEEDED(clientTrayItem->get_CurrentName(&coalescedName)) &&
          std::wstring_view{coalescedName, SysStringLen(coalescedName)} == L"YouTube Music",
          "retained client element observes the newest coalesced publication");
    SysFreeString(coalescedName);
    Check(eventHandler->LiveRegionCount() == 0,
          "routine dashboard help changes do not raise a live-region event");

    const auto findByAutomationId = [&](const wchar_t* id) {
        VARIANT automationId{};
        V_VT(&automationId) = VT_BSTR;
        V_BSTR(&automationId) = SysAllocString(id);
        ComPtr<IUIAutomationCondition> condition;
        ComPtr<IUIAutomationElement> element;
        if (SUCCEEDED(client->CreatePropertyCondition(
                UIA_AutomationIdPropertyId, automationId,
                condition.GetAddressOf())) && condition) {
            (void)clientRoot->FindFirst(
                TreeScope_Descendants, condition.Get(), element.GetAddressOf());
        }
        VariantClear(&automationId);
        return element;
    };
    auto headingElement = findByAutomationId(L"host:host.dashboard.title");
    auto helpElement = findByAutomationId(L"host:host.dashboard.help");
    VARIANT headingLevel{};
    VARIANT helpLiveSetting{};
    Check(headingElement && SUCCEEDED(headingElement->GetCurrentPropertyValue(
              UIA_HeadingLevelPropertyId, &headingLevel)) &&
          V_VT(&headingLevel) == VT_I4 && V_I4(&headingLevel) == HeadingLevel1,
          "real UIA client reads the dashboard level-one heading");
    Check(helpElement && SUCCEEDED(helpElement->GetCurrentPropertyValue(
              UIA_LiveSettingPropertyId, &helpLiveSetting)) &&
          V_VT(&helpLiveSetting) == VT_I4 && V_I4(&helpLiveSetting) == Off,
          "real UIA client reads routine dashboard help as non-live text");
    VariantClear(&headingLevel);
    VariantClear(&helpLiveSetting);
    gba::accessibility::Node status;
    status.id = L"host.dashboard.status";
    status.domain = gba::accessibility::ElementDomain::HostShell;
    status.name = L"Playback command failed";
    status.bounds = finalHostTree.nodes[2].bounds;
    status.role = gba::accessibility::Role::Status;
    status.liveSetting = gba::accessibility::LiveSetting::Polite;
    finalHostTree.nodes[2] = status;
    host.Publish(finalHostTree, {100, 200, 2, 800, 600});
    SendMessageW(window, WM_APP + 42, 0, 0);
    Check(eventHandler->WaitForLiveRegion(),
          "real UIA client receives the dashboard live-region change");
    auto statusElement = findByAutomationId(L"host:host.dashboard.status");
    VARIANT liveSetting{};
    CONTROLTYPEID statusControlType{};
    Check(statusElement && SUCCEEDED(statusElement->get_CurrentControlType(
              &statusControlType)) && statusControlType == UIA_StatusBarControlTypeId &&
          SUCCEEDED(statusElement->GetCurrentPropertyValue(
              UIA_LiveSettingPropertyId, &liveSetting)) &&
          V_VT(&liveSetting) == VT_I4 && V_I4(&liveSetting) == Polite,
          "real UIA client reads transient dashboard feedback as polite status");
    VariantClear(&liveSetting);
    BSTR liveStatus{};
    Check(SUCCEEDED(statusElement->get_CurrentName(&liveStatus)) &&
          std::wstring_view{liveStatus, SysStringLen(liveStatus)} ==
              L"Playback command failed",
          "retained status element exposes the newest host feedback");
    SysFreeString(liveStatus);
    host.Publish(finalHostTree, {120, 240, 1.5, 600, 450});
    SendMessageW(window, WM_APP + 42, 0, 0);
    Check(eventHandler->WaitForBoundsCount(5),
          "transform-only publication updates root and every existing node bound");
    RECT transformedBounds{};
    Check(SUCCEEDED(clientTrayItem->get_CurrentBoundingRectangle(&transformedBounds)) &&
          transformedBounds.left == 150 && transformedBounds.top == 570 &&
          transformedBounds.right == 246 && transformedBounds.bottom == 666,
          "retained client element observes the transformed physical bounds");
    RECT transformedRootBounds{};
    Check(SUCCEEDED(clientRoot->get_CurrentBoundingRectangle(&transformedRootBounds)) &&
          transformedRootBounds.left == 120 && transformedRootBounds.top == 240 &&
          transformedRootBounds.right == 720 && transformedRootBounds.bottom == 690,
          "real UIA client observes transformed root bounds");
    host.Publish(finalHostTree, {120, 240, 1.5, 480, 540, 1.2, 1.8, 20, 10});
    SendMessageW(window, WM_APP + 42, 0, 0);
    RECT composedBounds{};
    Check(SUCCEEDED(clientTrayItem->get_CurrentBoundingRectangle(&composedBounds)) &&
          composedBounds.left == 164 && composedBounds.top == 646 &&
          composedBounds.right == 240 && composedBounds.bottom == 761,
          "composed nonuniform motion keeps actionable UIA bounds on presented pixels");
    RECT composedRootBounds{};
    Check(SUCCEEDED(clientRoot->get_CurrentBoundingRectangle(&composedRootBounds)) &&
          composedRootBounds.left == 140 && composedRootBounds.top == 250 &&
          composedRootBounds.right == 620 && composedRootBounds.bottom == 790,
          "composed root follows visual offset and presented extent");
    host.Publish(
        finalHostTree,
        {120, 240, 1.5, 600, 450, 1.2, 1.8, 20, 10, 40, 30, true});
    SendMessageW(window, WM_APP + 42, 0, 0);
    RECT independentTrayBounds{};
    Check(SUCCEEDED(clientTrayItem->get_CurrentBoundingRectangle(
              &independentTrayBounds)) &&
          independentTrayBounds.left == 190 &&
          independentTrayBounds.top == 600 &&
          independentTrayBounds.right == 286 &&
          independentTrayBounds.bottom == 696,
          "tray UIA projects through the fixed chrome child coordinate space");
    RECT independentRootBounds{};
    Check(SUCCEEDED(clientRoot->get_CurrentBoundingRectangle(
              &independentRootBounds)) &&
          independentRootBounds.left == 120 &&
          independentRootBounds.top == 240 &&
          independentRootBounds.right == 720 &&
          independentRootBounds.bottom == 690,
          "single UIA root retains the real union HWND bounds with child visuals");
    host.Publish(
        finalHostTree,
        {120, 240, 1.5, 600, 450, 0.8, 1.1, 90, 70, 40, 30, true});
    SendMessageW(window, WM_APP + 42, 0, 0);
    RECT retainedTrayBounds{};
    Check(SUCCEEDED(clientTrayItem->get_CurrentBoundingRectangle(
              &retainedTrayBounds)) &&
          EqualRect(&retainedTrayBounds, &independentTrayBounds),
          "content motion republishes UIA without moving retained tray bounds");

    auto presentedSliderTree = Tree(L"generation-4", 13);
    host.Publish(presentedSliderTree, {120, 240, 1.5, 600, 450});
    SendMessageW(window, WM_APP + 42, 0, 0);
    presentedSliderTree.snapshotSequence = 14;
    presentedSliderTree.nodes[1].rangeValue = 75;
    host.Publish(presentedSliderTree, {120, 240, 1.5, 600, 450});
    SendMessageW(window, WM_APP + 42, 0, 0);
    Check(eventHandler->WaitForRangeValue(75),
          "real UIA client receives the presented RangeValue revision");
    VARIANT sliderAutomationId{};
    V_VT(&sliderAutomationId) = VT_BSTR;
    V_BSTR(&sliderAutomationId) = SysAllocString(L"widget:progress");
    ComPtr<IUIAutomationCondition> sliderCondition;
    Check(SUCCEEDED(client->CreatePropertyCondition(
              UIA_AutomationIdPropertyId, sliderAutomationId,
              sliderCondition.GetAddressOf())) && sliderCondition,
          "real UIA client creates a presented-slider condition");
    VariantClear(&sliderAutomationId);
    ComPtr<IUIAutomationElement> presentedSlider;
    Check(SUCCEEDED(clientRoot->FindFirst(
              TreeScope_Descendants, sliderCondition.Get(),
              presentedSlider.GetAddressOf())) && presentedSlider,
          "real UIA client discovers the presented slider");
    ComPtr<IUnknown> presentedRangeUnknown;
    ComPtr<IUIAutomationRangeValuePattern> presentedRange;
    double presentedRangeValue{};
    Check(SUCCEEDED(presentedSlider->GetCurrentPattern(
              UIA_RangeValuePatternId, presentedRangeUnknown.GetAddressOf())) &&
          presentedRangeUnknown && SUCCEEDED(presentedRangeUnknown.As(&presentedRange)) &&
          SUCCEEDED(presentedRange->get_CurrentValue(&presentedRangeValue)) &&
          presentedRangeValue == 75,
          "provider and RangeValue event expose the same presented revision");
    Check(SUCCEEDED(client->RemoveFocusChangedEventHandler(eventHandler.Get())) &&
          SUCCEEDED(client->RemovePropertyChangedEventHandler(
              clientRoot.Get(), eventHandler.Get())) &&
          SUCCEEDED(client->RemoveAutomationEventHandler(
              UIA_LiveRegionChangedEventId, clientRoot.Get(), eventHandler.Get())) &&
          SUCCEEDED(client->RemoveStructureChangedEventHandler(
              clientRoot.Get(), eventHandler.Get())),
          "real UIA client unsubscribes before the test window closes");

    ComPtr<IRawElementProviderFragment> currentButtonFragment;
    ComPtr<IRawElementProviderSimple> currentButton;
    ComPtr<IUnknown> currentInvokeUnknown;
    ComPtr<IInvokeProvider> currentInvoke;
    Check(SUCCEEDED(rootFragment->Navigate(
              NavigateDirection_FirstChild, currentButtonFragment.GetAddressOf())) &&
          currentButtonFragment && SUCCEEDED(currentButtonFragment.As(&currentButton)) &&
          SUCCEEDED(currentButton->GetPatternProvider(
              UIA_InvokePatternId, currentInvokeUnknown.GetAddressOf())) &&
          currentInvokeUnknown && SUCCEEDED(currentInvokeUnknown.As(&currentInvoke)),
          "current-generation fragment is retained for detach coverage");

    host.Detach();
    chromeHost.Detach();
    VARIANT detachedName{};
    Check(root->GetPropertyValue(UIA_NamePropertyId, &detachedName) ==
              UIA_E_ELEMENTNOTAVAILABLE,
          "retained root is unavailable after explicit detach");
    SAFEARRAY* detachedRuntimeId{};
    Check(currentInvoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE &&
          buttonFragment->GetRuntimeId(&detachedRuntimeId) ==
              UIA_E_ELEMENTNOTAVAILABLE && !detachedRuntimeId,
          "retained fragment actions are unavailable after explicit detach");
    ComPtr<IRawElementProviderSimple> detachedRoot;
    Check(host.GetRootProvider(detachedRoot.GetAddressOf()) == UIA_E_ELEMENTNOTAVAILABLE &&
          !detachedRoot,
          "detached host cannot mint a new root provider");
    host.Bind(window, WM_APP + 42);
    host.Publish(Tree(L"generation-3", 12), {120, 240, 1.5, 600, 450});
    SendMessageW(window, WM_APP + 42, 0, 0);
    ComPtr<IRawElementProviderSimple> reboundRoot;
    Check(SUCCEEDED(host.GetRootProvider(reboundRoot.GetAddressOf())) && reboundRoot &&
          StringProperty(reboundRoot.Get(), UIA_NamePropertyId) == L"Game Bar Alternative",
          "same-HWND rebind creates a new provider generation");
    ComPtr<IUIAutomationElement> reboundClientRoot;
    Check(SUCCEEDED(client->ElementFromHandle(
              window, reboundClientRoot.GetAddressOf())) && reboundClientRoot,
          "real UIA client acquires the rebound same-HWND provider generation");
    Check(root->GetPropertyValue(UIA_NamePropertyId, &detachedName) ==
              UIA_E_ELEMENTNOTAVAILABLE &&
          currentInvoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,
          "old providers stay unavailable after same-HWND reuse");
    host.Detach();
    Check(invoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,
          "second detach is idempotent and retained elements remain unavailable");
    PostMessageW(window, WM_CLOSE, 0, 0);
    windowThread.join();
    BSTR destroyedName{};
    Check(!IsWindow(window) &&
          clientRoot->get_CurrentName(&destroyedName) == UIA_E_ELEMENTNOTAVAILABLE &&
          !destroyedName,
          "retained original UIA client root stays unavailable after DestroyWindow");
    Check(reboundClientRoot->get_CurrentName(&destroyedName) ==
              UIA_E_ELEMENTNOTAVAILABLE && !destroyedName,
          "retained rebound UIA client root stays unavailable after DestroyWindow");
    CoUninitialize();
    std::cout << "AccessibilityProviderTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
