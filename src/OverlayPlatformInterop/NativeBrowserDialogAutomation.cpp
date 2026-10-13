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
    int depth, int& remaining, ULONGLONG deadline, bool& found) noexcept {
    if (!node || found) return S_OK;
    if (depth > 10 || --remaining < 0 || GetTickCount64() > deadline) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    CONTROLTYPEID type{}; auto hr = node->get_CurrentControlType(&type);
    if (FAILED(hr) || type == UIA_DocumentControlTypeId) return hr;
    BSTR name{}; hr = node->get_CurrentClassName(&name);
    found = SUCCEEDED(hr) && name && std::wcscmp(name, L"DesktopMediaPickerDialogView") == 0;
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
HRESULT FindNativeDialog(IUIAutomationTreeWalker* walker, IUIAutomationElement* node,
    int depth, int& remaining, ULONGLONG deadline, ComPtr<IUIAutomationElement>& found, bool chooserOnly) noexcept {
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
            bool chooser = !chooserOnly;
            if (!hidden && chooserOnly) {
                result = ContainsChooser(walker, node, 0, remaining, deadline, chooser);
                if (FAILED(result)) return result;
            }
            if (!hidden && chooser) {
                if (found) return E_PENDING; // Multiple native dialogs are ambiguous.
                found = node;
            }
            return S_OK;
        }
    }
    ComPtr<IUIAutomationElement> child;
    result = walker->GetFirstChildElement(node, &child);
    while (SUCCEEDED(result) && child) {
        result = FindNativeDialog(walker, child.Get(), depth + 1, remaining, deadline, found, chooserOnly);
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
    std::uint32_t expectedProcess, std::uint32_t close, std::uint32_t* present, RECT* bounds, bool chooserOnly) noexcept {
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
                if (SUCCEEDED(result)) result = FindNativeDialog(walker.Get(), root.Get(), 0, remaining, GetTickCount64() + 1000, dialog, chooserOnly);
                if (SUCCEEDED(result) && dialog) {
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
