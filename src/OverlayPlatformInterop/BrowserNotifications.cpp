#include "OverlayPlatformExports.h"
#include <Windows.h>
#include <Ole2.h>
#include <wrl.h>
#include <WebView2.h>
#include <WebView2Interop.h>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <cstdint>

namespace {
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Callback;
using Notify = void(__stdcall*)(std::uint64_t, std::uint32_t, const wchar_t*, const wchar_t*, const wchar_t*, const wchar_t*, std::uint32_t);
struct Notice {
    ComPtr<ICoreWebView2Notification> value;
    EventRegistrationToken close{};
    bool subscribed{};
    ~Notice() { if (subscribed) value->remove_CloseRequested(close); }
};
struct Session {
    DWORD thread{GetCurrentThreadId()};
    ComPtr<ICoreWebView2_24> browser;
    EventRegistrationToken received{};
    Notify callback{};
    bool subscribed{};
    std::uint64_t next{};
    std::unordered_map<std::uint64_t, std::shared_ptr<Notice>> notices;
    ~Session() { if (subscribed) browser->remove_NotificationReceived(received); notices.clear(); }
};
std::mutex gate;
std::unordered_map<std::uint64_t, std::shared_ptr<Session>> sessions;
std::uint64_t nextSession{};
std::shared_ptr<Session> Find(std::uint64_t id) {
    std::lock_guard lock(gate); auto it = sessions.find(id); return it == sessions.end() ? nullptr : it->second;
}
struct Text {
    LPWSTR value{};
    ~Text() { CoTaskMemFree(value); }
    std::wstring Limited(size_t maximum, bool discardLong = false) const {
        if (!value) return {};
        size_t size{}; while (size <= maximum && value[size]) ++size;
        if (size > maximum) { if (discardLong) return {}; size = maximum; }
        if (size && value[size - 1] >= 0xd800 && value[size - 1] <= 0xdbff) --size;
        return {value, size};
    }
};
}

extern "C" WRAIL_OVERLAY_PLATFORM_API HRESULT WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailWebNotificationsSubscribe(IUnknown* winrtCore, Notify callback, std::uint64_t* handle) noexcept {
    if (!winrtCore || !callback || !handle) return E_INVALIDARG;
    *handle = 0;
    try {
        ComPtr<ICoreWebView2Interop2> interop;
        auto hr = winrtCore->QueryInterface(IID_PPV_ARGS(&interop)); if (FAILED(hr)) return hr;
        ComPtr<ICoreWebView2> core;
        hr = interop->GetComICoreWebView2(&core); if (FAILED(hr)) return hr;
        auto state = std::make_shared<Session>(); state->callback = callback;
        hr = core.As(&state->browser); if (FAILED(hr)) return hr;
        std::weak_ptr<Session> weak = state;
        auto listener = Callback<ICoreWebView2NotificationReceivedEventHandler>([weak](ICoreWebView2*, ICoreWebView2NotificationReceivedEventArgs* args) -> HRESULT {
            if (!args) return E_INVALIDARG;
            args->put_Handled(TRUE); // Never fall through to OS notification UI.
            auto owner = weak.lock(); if (!owner) return S_OK;
            try {
                // Bounded raw handles. Managed admission/overflow normally
                // releases excess entries immediately during this callback.
                // One extra slot lets managed admission replace a tagged entry
                // or report inbox overflow before immediately releasing it.
                if (owner->notices.size() >= 257) return S_OK;
                auto item = std::make_shared<Notice>();
                auto result = args->get_Notification(&item->value); if (FAILED(result)) return result;
                const auto id = ++owner->next;
                auto closed = Callback<ICoreWebView2NotificationCloseRequestedEventHandler>([weak, id](ICoreWebView2Notification*, IUnknown*) -> HRESULT {
                    if (auto current = weak.lock()) {
                        auto found = current->notices.find(id);
                        if (found != current->notices.end()) {
                            auto keepAlive = found->second;
                            current->callback(id, 2, nullptr, nullptr, nullptr, nullptr, 0);
                        }
                    }
                    return S_OK;
                });
                result = item->value->add_CloseRequested(closed.Get(), &item->close); if (FAILED(result)) return result;
                item->subscribed = true;
                Text origin, title, body, tag;
                if (FAILED(args->get_SenderOrigin(&origin.value)) || FAILED(item->value->get_Title(&title.value)) ||
                    FAILED(item->value->get_Body(&body.value)) || FAILED(item->value->get_Tag(&tag.value))) return E_FAIL;
                BOOL silent{}, renotify{}; item->value->get_IsSilent(&silent); item->value->get_ShouldRenotify(&renotify);
                const auto o = origin.Limited(2048, true), t = title.Limited(256), b = body.Limited(2048), g = tag.Limited(4096, true);
                owner->notices.emplace(id, item);
                owner->callback(id, 1, o.c_str(), g.c_str(), t.c_str(), b.c_str(), (silent ? 1u : 0u) | (renotify ? 2u : 0u));
                // item and args are released on this creation thread even when
                // managed admission synchronously declines/removes the notice.
                return S_OK;
            } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
        });
        hr = state->browser->add_NotificationReceived(listener.Get(), &state->received); if (FAILED(hr)) return hr;
        state->subscribed = true;
        std::lock_guard lock(gate);
        const auto id = ++nextSession; sessions.emplace(id, state); *handle = id;
        return S_OK;
    } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

extern "C" WRAIL_OVERLAY_PLATFORM_API HRESULT WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailWebNotificationCommand(std::uint64_t handle, std::uint64_t id, std::uint32_t command) noexcept {
    auto state = Find(handle); if (!state) return E_INVALIDARG;
    if (GetCurrentThreadId() != state->thread) return RPC_E_WRONG_THREAD;
    auto it = state->notices.find(id); if (it == state->notices.end()) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    auto item = it->second; // Keep alive through reentrant CloseRequested.
    switch (command) {
    case 0: return item->value->ReportShown();
    case 1: return item->value->ReportClicked();
    case 2: return item->value->ReportClosed();
    case 3: state->notices.erase(id); return S_OK;
    default: return E_INVALIDARG;
    }
}

extern "C" WRAIL_OVERLAY_PLATFORM_API HRESULT WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailWebNotificationsUnsubscribe(std::uint64_t handle) noexcept {
    auto state = Find(handle); if (!state) return S_OK;
    if (GetCurrentThreadId() != state->thread) return RPC_E_WRONG_THREAD;
    { std::lock_guard lock(gate); sessions.erase(handle); }
    return S_OK; // Last COM references release on the owning dispatcher here.
}
