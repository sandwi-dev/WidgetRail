#include "OverlayProcessInterop.h"
#include "../OverlayHost/OverlayProcessOwner.h"
#include <algorithm>
#include <memory>

namespace {
struct ProcessLease final {
    DWORD thread{GetCurrentThreadId()};
    // Reverse destruction: stop activation/election, then release installer marker.
    widgetrail::process::OverlayInstallationLifetime installation;
    widgetrail::process::OverlayProcessOwner owner;
};
void Error(wchar_t* target, const std::uint32_t capacity, const std::wstring_view value) noexcept {
    if (!target || !capacity) return;
    const auto count = std::min<std::size_t>(capacity - 1, value.size());
    std::copy_n(value.data(), count, target); target[count] = L'\0';
}
}

WidgetRailProcessResult WRAIL_OVERLAY_PLATFORM_CALL WidgetRailProcessBegin(
    const std::uint32_t version, const wchar_t* profile, const std::uint32_t profileLength,
    const std::uint32_t showExisting, const std::uint32_t timeoutMilliseconds, void** owner,
    wchar_t* error, const std::uint32_t errorCapacity) noexcept {
    if (owner) *owner = nullptr;
    Error(error, errorCapacity, L"");
    if (version != WRAIL_PROCESS_ABI_VERSION || !owner || !profile || profileLength == 0 ||
        profileLength > 64 || showExisting > 1 || timeoutMilliseconds == 0 || timeoutMilliseconds > 10000) {
        Error(error, errorCapacity, L"Process ownership arguments or ABI version are invalid.");
        return WidgetRailProcessResult::Failed;
    }
    try {
        auto lease = std::make_unique<ProcessLease>();
        std::wstring detail;
        if (!lease->installation.Begin(detail)) {
            Error(error, errorCapacity, detail); return WidgetRailProcessResult::Failed;
        }
        const auto result = lease->owner.Begin(std::wstring_view(profile, profileLength),
            std::chrono::milliseconds(timeoutMilliseconds), detail,
            showExisting ? widgetrail::process::ActivationMode::Show : widgetrail::process::ActivationMode::EnsureRunning);
        if (result == widgetrail::process::OwnershipResult::Owner) {
            *owner = lease.release(); return WidgetRailProcessResult::Owner;
        }
        if (result == widgetrail::process::OwnershipResult::ClientAcknowledged) return WidgetRailProcessResult::Redirected;
        if (result == widgetrail::process::OwnershipResult::AlreadyRunning) return WidgetRailProcessResult::AlreadyRunning;
        Error(error, errorCapacity, detail);
    } catch (...) { Error(error, errorCapacity, L"Process ownership initialization failed."); }
    return WidgetRailProcessResult::Failed;
}

WidgetRailProcessStatus WRAIL_OVERLAY_PLATFORM_CALL WidgetRailProcessBindWindow(
    void* owner, const std::uintptr_t window, const std::uint32_t message) noexcept {
    auto* lease = static_cast<ProcessLease*>(owner);
    if (!lease || !window || message < WM_APP || message > 0xBFFF || !IsWindow(reinterpret_cast<HWND>(window)))
        return WidgetRailProcessStatus::InvalidArgument;
    if (lease->thread != GetCurrentThreadId()) return WidgetRailProcessStatus::WrongThread;
    DWORD process{}; GetWindowThreadProcessId(reinterpret_cast<HWND>(window), &process);
    if (process != GetCurrentProcessId()) return WidgetRailProcessStatus::InvalidArgument;
    lease->owner.BindNotificationWindow(reinterpret_cast<HWND>(window), message);
    return WidgetRailProcessStatus::Ok;
}

WidgetRailProcessStatus WRAIL_OVERLAY_PLATFORM_CALL WidgetRailProcessEnd(void* owner) noexcept {
    auto* lease = static_cast<ProcessLease*>(owner);
    if (!lease) return WidgetRailProcessStatus::InvalidArgument;
    if (lease->thread != GetCurrentThreadId()) return WidgetRailProcessStatus::WrongThread;
    delete lease;
    return WidgetRailProcessStatus::Ok;
}
