#include "OverlayPlatformExports.h"
#include <Windows.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cwchar>
#include <mutex>
#include <new>

namespace {
constexpr wchar_t ClassName[] = L"WidgetRail.ControllerPointer.v1";
struct PaintState { int size{}; int border{}; std::uint32_t fill{}, outline{}; };
LRESULT CALLBACK CursorProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_NCHITTEST) return HTTRANSPARENT;
    if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    if (message == WM_NCDESTROY) {
        delete reinterpret_cast<PaintState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        SetWindowLongPtrW(window, GWLP_USERDATA, 0);
    }
    return DefWindowProcW(window, message, wparam, lparam);
}
bool Owned(HWND window) noexcept {
    DWORD process{};
    if (!window || GetWindowThreadProcessId(window, &process) != GetCurrentThreadId() || process != GetCurrentProcessId()) return false;
    wchar_t name[64]{};
    return GetClassNameW(window, name, 64) && std::wcscmp(name, ClassName) == 0;
}
HRESULT Paint(HWND window, int size, int border, std::uint32_t fill, std::uint32_t outline, POINT position) noexcept {
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = size;
    info.bmiHeader.biHeight = -size; info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
    void* pixels{};
    auto bitmap = CreateDIBSection(nullptr, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
    if (!bitmap) return HRESULT_FROM_WIN32(GetLastError());
    auto dc = CreateCompatibleDC(nullptr);
    if (!dc) { DeleteObject(bitmap); return E_OUTOFMEMORY; }
    auto old = SelectObject(dc, bitmap);
    auto output = static_cast<std::uint32_t*>(pixels);
    const double radius = size / 2.0;
    for (int y = 0; y < size; ++y) for (int x = 0; x < size; ++x) {
        const auto distance = std::hypot(x + .5 - radius, y + .5 - radius);
        const auto outer = std::clamp(radius + .5 - distance, 0.0, 1.0);
        const auto inner = std::clamp(radius - border + .5 - distance, 0.0, 1.0);
        const auto fa = inner * (fill >> 24) / 255.0;
        const auto oa = (outer - inner) * (outline >> 24) / 255.0;
        const auto channel = [&](int shift) { return static_cast<std::uint32_t>(std::lround(((fill >> shift) & 255) * fa + ((outline >> shift) & 255) * oa)); };
        output[y * size + x] = (static_cast<std::uint32_t>(std::lround(255 * (fa + oa))) << 24) | (channel(16) << 16) | (channel(8) << 8) | channel(0);
    }
    SIZE dimensions{size, size}; POINT origin{}; BLENDFUNCTION blend{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};
    const auto ok = UpdateLayeredWindow(window, nullptr, &position, &dimensions, dc, &origin, 0, &blend, ULW_ALPHA);
    const auto error = GetLastError();
    SelectObject(dc, old); DeleteDC(dc); DeleteObject(bitmap);
    return ok ? S_OK : HRESULT_FROM_WIN32(error);
}
}

// One tiny, click-through, nonactivating visual. It neither captures input nor
// changes the global cursor, and is owned/disposed on the frontend UI thread.
extern "C" WRAIL_OVERLAY_PLATFORM_API std::int32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformDrawControllerCursor(std::uintptr_t* value, int x, int y,
    int size, int border, std::uint32_t fill, std::uint32_t outline, std::uint32_t visible) noexcept {
    if (!value || visible > 1 || size < 8 || size > 128 || border < 1 || border > size / 2) return E_INVALIDARG;
    auto window = reinterpret_cast<HWND>(*value);
    if (window && !Owned(window)) return E_ACCESSDENIED;
    if (!visible) { if (window) ShowWindow(window, SW_HIDE); return S_OK; }
    if (!window) {
        static std::once_flag once;
        static ATOM atom{};
        std::call_once(once, [] {
            WNDCLASSW type{}; type.lpfnWndProc = CursorProc; type.hInstance = GetModuleHandleW(nullptr); type.lpszClassName = ClassName;
            atom = RegisterClassW(&type);
        });
        if (!atom) return E_FAIL;
        window = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            ClassName, L"WidgetRail controller pointer", WS_POPUP, x, y, size, size, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!window) return HRESULT_FROM_WIN32(GetLastError());
        const auto state = new(std::nothrow) PaintState{};
        if (!state) { DestroyWindow(window); return E_OUTOFMEMORY; }
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
        *value = reinterpret_cast<std::uintptr_t>(window);
    }
    auto& state = *reinterpret_cast<PaintState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    POINT position{x - size / 2, y - size / 2};
    if (state.size != size || state.border != border || state.fill != fill || state.outline != outline) {
        const auto result = Paint(window, size, border, fill, outline, position);
        if (FAILED(result)) { ShowWindow(window, SW_HIDE); return result; }
        state = {size, border, fill, outline};
    }
    return SetWindowPos(window, HWND_TOPMOST, position.x, position.y, size, size, SWP_NOACTIVATE | SWP_SHOWWINDOW) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
}

extern "C" WRAIL_OVERLAY_PLATFORM_API void WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformDestroyControllerCursor(std::uintptr_t window) noexcept {
    const auto owned = reinterpret_cast<HWND>(window);
    if (Owned(owned)) DestroyWindow(owned);
}
