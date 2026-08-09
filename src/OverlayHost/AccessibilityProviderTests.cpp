#include "AccessibilityProvider.h"

#include <UIAutomation.h>
#include <wrl.h>

#include <cmath>
#include <cstdlib>
#include <future>
#include <iostream>
#include <thread>
#include <vector>

namespace {

using Microsoft::WRL::ComPtr;

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

    host.Clear();
    Check(invoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,
          "cleared provider tree rejects retained element references");
    PostMessageW(window, WM_CLOSE, 0, 0);
    windowThread.join();
    CoUninitialize();
    std::cout << "AccessibilityProviderTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
