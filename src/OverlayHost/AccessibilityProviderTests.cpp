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
        PostQuitMessage(0);
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
    IUIAutomationFocusChangedEventHandler,
    IUIAutomationPropertyChangedEventHandler,
    IUIAutomationStructureChangedEventHandler> {
public:
    ClientEventHandler()
        : focusEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          propertyEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          boundsEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          rangeEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
          structureEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)) {}

    ~ClientEventHandler() override {
        if (focusEvent_) CloseHandle(focusEvent_);
        if (propertyEvent_) CloseHandle(propertyEvent_);
        if (boundsEvent_) CloseHandle(boundsEvent_);
        if (rangeEvent_) CloseHandle(rangeEvent_);
        if (structureEvent_) CloseHandle(structureEvent_);
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

    [[nodiscard]] bool WaitForStructure() const noexcept {
        return structureEvent_ && WaitForSingleObject(structureEvent_, 2000) == WAIT_OBJECT_0;
    }

private:
    HANDLE focusEvent_{};
    HANDLE propertyEvent_{};
    HANDLE boundsEvent_{};
    HANDLE rangeEvent_{};
    HANDLE structureEvent_{};
    std::atomic<int> boundsCount_{};
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

gba::accessibility::Tree HostTree() {
    gba::accessibility::Tree tree;
    tree.widgetId = L"host.tray";
    tree.runtimeGeneration = L"host";
    tree.snapshotSequence = 21;
    tree.activeInputScopeId = L"host.tray";
    gba::accessibility::Node music;
    music.id = L"tray.music";
    music.name = L"YT Music";
    music.hostTargetId = L"music";
    music.bounds = {20, 220, 64, 64};
    music.role = gba::accessibility::Role::ListItem;
    music.hostAction = gba::accessibility::HostAction::ActivateTrayItem;
    music.selected = true;
    music.focused = true;
    tree.nodes.push_back(music);
    tree.focusedNode = 0;
    return tree;
}

} // namespace

int main() {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "COM initializes for provider coverage");
    gba::accessibility::ProviderHost host;
    std::promise<HWND> windowPromise;
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
        host.Bind(threadWindow, WM_APP + 42);
        windowPromise.set_value(threadWindow);
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
    const HWND window = windowFuture.get();
    Check(window != nullptr, "test HWND is created on a pumping UI thread");
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
    V_BSTR(&automationId) = SysAllocString(L"next");
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
          actions[0].nodeId == L"next" && actions[0].actionId == L"next",
          "Invoke retains the full generation and snapshot authority tuple");

    Check(SUCCEEDED(buttonFragment->SetFocus()), "focus request is admitted asynchronously");
    actions = host.TakeActions();
    Check(actions.size() == 1 && actions[0].kind == gba::accessibility::ActionKind::Focus &&
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
          actions[0].hostTargetId == L"music",
          "tray Invoke retains the selected widget target");
    VARIANT trayAutomationId{};
    V_VT(&trayAutomationId) = VT_BSTR;
    V_BSTR(&trayAutomationId) = SysAllocString(L"tray.music");
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
          SUCCEEDED(client->AddStructureChangedEventHandler(
              clientRoot.Get(), static_cast<TreeScope>(TreeScope_Element | TreeScope_Subtree),
              nullptr, eventHandler.Get())),
          "real UIA client subscribes to focus, property, and structure events");
    SafeArrayDestroy(observedProperties);
    auto changedHostTree = HostTree();
    changedHostTree.nodes[0].selected = false;
    changedHostTree.nodes[0].focused = false;
    gba::accessibility::Node settings;
    settings.id = L"tray.settings";
    settings.name = L"Settings";
    settings.hostTargetId = L"settings";
    settings.bounds = {100, 220, 64, 64};
    settings.role = gba::accessibility::Role::ListItem;
    settings.hostAction = gba::accessibility::HostAction::ActivateTrayItem;
    settings.selected = true;
    settings.focused = true;
    changedHostTree.nodes.push_back(settings);
    changedHostTree.focusedNode = 1;
    auto finalHostTree = changedHostTree;
    finalHostTree.nodes[0].name = L"YouTube Music";
    host.Publish(changedHostTree, {100, 200, 2, 800, 600});
    host.Publish(finalHostTree, {100, 200, 2, 800, 600});
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
    host.Publish(finalHostTree, {120, 240, 1.5, 600, 450});
    SendMessageW(window, WM_APP + 42, 0, 0);
    Check(eventHandler->WaitForBoundsCount(3),
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
    V_BSTR(&sliderAutomationId) = SysAllocString(L"progress");
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
    Check(root->GetPropertyValue(UIA_NamePropertyId, &detachedName) ==
              UIA_E_ELEMENTNOTAVAILABLE &&
          currentInvoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,
          "old providers stay unavailable after same-HWND reuse");
    host.Detach();
    Check(invoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,
          "second detach is idempotent and retained elements remain unavailable");
    PostMessageW(window, WM_CLOSE, 0, 0);
    windowThread.join();
    CoUninitialize();
    std::cout << "AccessibilityProviderTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
