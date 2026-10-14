#include "OverlayPlatformExports.h"
#include <Windows.h>
#include <Ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>
#include <cstdint>
#include <cwchar>
#include <utility>

namespace {
using Microsoft::WRL::ComPtr;
HRESULT ContainsChooser(IUIAutomationTreeWalker* walker, IUIAutomationElement* node,
    int depth, int& remaining, ULONGLONG deadline, ComPtr<IUIAutomationElement>& found) noexcept {
    if (!node || found) return S_OK;
    if (depth > 10 || --remaining < 0 || GetTickCount64() > deadline) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    CONTROLTYPEID type{}; auto hr = node->get_CurrentControlType(&type);
    if (FAILED(hr) || type == UIA_DocumentControlTypeId) return hr;
    BSTR name{}; hr = node->get_CurrentClassName(&name);
    if (SUCCEEDED(hr) && name && std::wcscmp(name, L"DesktopMediaPickerDialogView") == 0) found = node;
    SysFreeString(name);
    if (FAILED(hr) || found) return hr;
    ComPtr<IUIAutomationElement> child; hr = walker->GetFirstChildElement(node, &child);
    while (SUCCEEDED(hr) && child && !found) {
        hr = ContainsChooser(walker, child.Get(), depth + 1, remaining, deadline, found);
        if (FAILED(hr)) return hr;
        ComPtr<IUIAutomationElement> next; hr = walker->GetNextSiblingElement(child.Get(), &next); child = std::move(next);
    }
    return hr;
}
HRESULT RuntimeIdentity(IUIAutomationElement* element, std::uint64_t& identity) noexcept {
    SAFEARRAY* values{};
    auto hr = element->GetRuntimeId(&values);
    if (FAILED(hr) || !values) return FAILED(hr) ? hr : E_FAIL;
    LONG lower{}, upper{};
    hr = SafeArrayGetLBound(values, 1, &lower);
    if (SUCCEEDED(hr)) hr = SafeArrayGetUBound(values, 1, &upper);
    std::uint64_t hash = 14695981039346656037ull;
    if (SUCCEEDED(hr) && (SafeArrayGetDim(values) != 1 || upper < lower || upper - lower >= 64)) hr = E_UNEXPECTED;
    for (LONG index = lower; SUCCEEDED(hr) && index <= upper; ++index) {
        LONG value{}; hr = SafeArrayGetElement(values, &index, &value);
        hash ^= static_cast<std::uint32_t>(value); hash *= 1099511628211ull;
    }
    SafeArrayDestroy(values);
    if (SUCCEEDED(hr)) identity = hash ? hash : 1;
    return hr;
}
HRESULT FindNativeDialog(IUIAutomationTreeWalker* walker, IUIAutomationElement* node,
    int depth, int& remaining, ULONGLONG deadline, ComPtr<IUIAutomationElement>& found, bool chooserOnly,
    std::uint64_t& identity, bool identityRequired) noexcept {
    if (!node) return S_OK;
    if (depth > 10 || --remaining < 0 || GetTickCount64() > deadline) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    CONTROLTYPEID type{};
    auto result = node->get_CurrentControlType(&type);
    if (FAILED(result) || type == UIA_DocumentControlTypeId) return result;
    // Do not enter webpage accessibility trees or inspect their content.
    if (type == UIA_WindowControlTypeId) {
        BSTR name{};
        result = node->get_CurrentClassName(&name);
        const bool native = SUCCEEDED(result) && name && std::wcscmp(name, L"RootView") == 0;
        SysFreeString(name);
        if (FAILED(result)) return result;
        if (native) {
            BOOL hidden{};
            result = node->get_CurrentIsOffscreen(&hidden);
            if (FAILED(result)) return result;
            ComPtr<IUIAutomationElement> chooser;
            if (!hidden && chooserOnly) {
                result = ContainsChooser(walker, node, 0, remaining, deadline, chooser);
                if (FAILED(result)) return result;
            }
            if (!hidden && (!chooserOnly || chooser)) {
                if (found) return E_PENDING; // Multiple native dialogs are ambiguous.
                if (identityRequired) {
                    result = RuntimeIdentity(chooserOnly ? chooser.Get() : node, identity);
                    if (FAILED(result)) return result;
                }
                found = node;
            }
            return S_OK;
        }
    }
    ComPtr<IUIAutomationElement> child;
    result = walker->GetFirstChildElement(node, &child);
    while (SUCCEEDED(result) && child) {
        result = FindNativeDialog(walker, child.Get(), depth + 1, remaining, deadline, found, chooserOnly, identity, identityRequired);
        if (FAILED(result)) return result;
        ComPtr<IUIAutomationElement> next;
        result = walker->GetNextSiblingElement(child.Get(), &next);
        child = std::move(next);
    }
    return result;
}
}

// UI Automation runs on the caller's background MTA, never on WinUI's thread.
// A Chromium dialog is an accessible native Window/RootView. The enclosing
// browser HWND can remain alive after that dialog has disappeared.
static std::int32_t QueryDialog(std::uintptr_t window,
    std::uint32_t expectedProcess, std::uint32_t close, std::uint32_t* present, RECT* bounds, bool chooserOnly,
    std::uint64_t expectedIdentity = 0, std::uint64_t* identity = nullptr) noexcept {
    if (!present || !bounds || !window || !expectedProcess || close > 1) return E_INVALIDARG;
    *present = 0;
    *bounds = {};
    const auto hwnd = reinterpret_cast<HWND>(window);
    DWORD process{};
    if (!IsWindow(hwnd) || !GetWindowThreadProcessId(hwnd, &process) || process != expectedProcess)
        return S_OK;
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialized)) return initialized;
    HRESULT result = E_FAIL;
    {
        using Microsoft::WRL::ComPtr;
        ComPtr<IUIAutomation> automation;
        result = CoCreateInstance(__uuidof(CUIAutomation8), nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&automation));
        if (SUCCEEDED(result)) {
            ComPtr<IUIAutomation2> options;
            if (SUCCEEDED(automation.As(&options))) {
                options->put_ConnectionTimeout(1000);
                options->put_TransactionTimeout(1000);
            }
            ComPtr<IUIAutomationElement> root;
            result = automation->ElementFromHandle(hwnd, &root);
            if (SUCCEEDED(result) && root) {
                ComPtr<IUIAutomationTreeWalker> walker;
                result = automation->get_ControlViewWalker(&walker);
                ComPtr<IUIAutomationElement> dialog;
                int remaining = 128;
                std::uint64_t foundIdentity{};
                if (SUCCEEDED(result)) result = FindNativeDialog(walker.Get(), root.Get(), 0, remaining, GetTickCount64() + 1000, dialog, chooserOnly, foundIdentity, identity || expectedIdentity);
                if (expectedIdentity && foundIdentity != expectedIdentity) dialog.Reset();
                if (SUCCEEDED(result) && dialog) {
                    if (identity) *identity = foundIdentity;
                    result = dialog->get_CurrentBoundingRectangle(bounds);
                    if (SUCCEEDED(result) && bounds->right > bounds->left && bounds->bottom > bounds->top) *present = 1;
                    if (close) {
                        ComPtr<IUIAutomationWindowPattern> pattern;
                        result = dialog->GetCurrentPatternAs(UIA_WindowPatternId, IID_PPV_ARGS(&pattern));
                        if (SUCCEEDED(result) && pattern) result = pattern->Close();
                    }
                }
            }
        }
    }
    CoUninitialize();
    return result;
}

// Additive session-aware observation: a reusable browser HWND is not dialog identity.
// Once admitted, inspect/close only that exact accessibility object. A new chooser
// on the same HWND must not inherit the old session's input or cancellation.
extern "C" WRAIL_OVERLAY_PLATFORM_API std::int32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformFindSharingDialog(std::uint32_t process, std::uint32_t close,
    std::uintptr_t* window, RECT* bounds, std::uint32_t* inspected) noexcept;

extern "C" WRAIL_OVERLAY_PLATFORM_API std::int32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformObserveSharingDialog(std::uint32_t process, std::uintptr_t knownWindow,
    std::uint64_t expectedIdentity, std::uint32_t close, std::uintptr_t* window,
    RECT* bounds, std::uint32_t* inspected, std::uint64_t* identity) noexcept {
    if (!process || !window || !bounds || !inspected || !identity || close > 1) return E_INVALIDARG;
    *window = 0; *bounds = {}; *inspected = 0; *identity = 0;
    if (knownWindow && expectedIdentity) {
        std::uint32_t present{}; *inspected = 1;
        const auto hr = QueryDialog(knownWindow, process, close, &present, bounds, true, expectedIdentity, identity);
        if (SUCCEEDED(hr) && present) *window = knownWindow;
        return hr;
    }
    // Admission is still scoped to the process authorized by ScreenCaptureStarting.
    const auto hr = WidgetRailOverlayPlatformFindSharingDialog(process, 0, window, bounds, inspected);
    if (FAILED(hr) || !*window) return hr;
    std::uint32_t present{};
    const auto verified = QueryDialog(*window, process, close, &present, bounds, true, 0, identity);
    if (FAILED(verified) || !present) *window = 0;
    return verified;
}

extern "C" WRAIL_OVERLAY_PLATFORM_API std::int32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformQueryNativeBrowserDialogBounds(std::uintptr_t window,
    std::uint32_t expectedProcess, std::uint32_t close, std::uint32_t* present, RECT* bounds) noexcept {
    return QueryDialog(window, expectedProcess, close, present, bounds, false);
}

// Enumerate all windows, because active sharing creates a separate indicator.
// Native class metadata distinguishes the chooser without reading titles or page content.
extern "C" WRAIL_OVERLAY_PLATFORM_API std::int32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformFindSharingDialog(std::uint32_t process, std::uint32_t close,
    std::uintptr_t* window, RECT* bounds, std::uint32_t* inspected) noexcept {
    if (!process || close > 1 || !window || !bounds || !inspected) return E_INVALIDARG;
    *window = 0; *bounds = {}; *inspected = 0;
    struct Search { DWORD process; HWND windows[16]{}; int count{}; bool overflow{}; } search{process};
    EnumWindows([](HWND hwnd, LPARAM context) -> BOOL {
        auto& value = *reinterpret_cast<Search*>(context);
        DWORD owner{}; GetWindowThreadProcessId(hwnd, &owner);
        if (owner != value.process || !IsWindowVisible(hwnd)) return TRUE;
        if (value.count == 16) { value.overflow = true; return FALSE; }
        value.windows[value.count++] = hwnd; return TRUE;
    }, reinterpret_cast<LPARAM>(&search));
    if (search.overflow) return E_PENDING;
    HRESULT failure = S_OK;
    const auto deadline = GetTickCount64() + 2000;
    for (int i = 0; i < search.count; ++i) {
        if (GetTickCount64() > deadline) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
        ++*inspected;
        std::uint32_t present{}; RECT candidate{};
        const auto hwnd = reinterpret_cast<std::uintptr_t>(search.windows[i]);
        const auto hr = QueryDialog(hwnd, process, 0, &present, &candidate, true);
        if (FAILED(hr)) { failure = hr; continue; }
        if (present) {
            if (*window) return E_PENDING;
            *window = hwnd; *bounds = candidate;
        }
    }
    if (FAILED(failure)) { *window = 0; return failure; }
    if (close && *window) { std::uint32_t present{}; return QueryDialog(*window, process, 1, &present, bounds, true); }
    return S_OK;
}

// Preserve the additive ABI used by earlier hosts; bounds are optional there.
extern "C" WRAIL_OVERLAY_PLATFORM_API std::int32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformQueryNativeBrowserDialog(std::uintptr_t window,
    std::uint32_t expectedProcess, std::uint32_t close, std::uint32_t* present) noexcept {
    RECT bounds{};
    return WidgetRailOverlayPlatformQueryNativeBrowserDialogBounds(window, expectedProcess, close, present, &bounds);
}
