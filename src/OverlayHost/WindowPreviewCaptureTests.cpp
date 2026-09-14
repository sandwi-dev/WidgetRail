#include "WindowPreviewCapture.h"
#include <d2d1_1.h>
#include <dcomp.h>
#include <winrt/base.h>
#include <iostream>
#include <array>
#include <algorithm>

// Own synthetic windows only. Large allocation probes remain offscreen; a
// small non-activating bottommost window verifies continuous DWM frame delivery.
// No user application is captured and no screenshots/pixel files are written.
int wmain() {
    try {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        winrt::init_apartment(winrt::apartment_type::single_threaded);
        Microsoft::WRL::ComPtr<ID3D11Device> device;
        winrt::check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE,
            nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
            &device, nullptr, nullptr));
        Microsoft::WRL::ComPtr<ID2D1Factory1> factory;
        winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,
            IID_PPV_ARGS(&factory)));
        Microsoft::WRL::ComPtr<IDXGIDevice> dxgi;
        winrt::check_hresult(device.As(&dxgi));
        Microsoft::WRL::ComPtr<ID2D1Device> d2d;
        winrt::check_hresult(factory->CreateDevice(dxgi.Get(), &d2d));
        Microsoft::WRL::ComPtr<ID2D1DeviceContext> context;
        winrt::check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &context));
        Microsoft::WRL::ComPtr<ID2D1Bitmap1> destination;
        auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        winrt::check_hresult(context->CreateBitmap({160, 90}, nullptr, 0, properties, &destination));
        context->SetTarget(destination.Get());
        WNDCLASSW cls{};
        cls.hInstance = GetModuleHandleW(nullptr);
        cls.lpfnWndProc = [](HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) -> LRESULT {
            if (message != WM_PAINT) return DefWindowProcW(hwnd, message, wparam, lparam);
            const auto tick = GetWindowLongPtrW(hwnd, GWLP_USERDATA) + 1;
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, tick);
            PAINTSTRUCT paint{};
            HDC dc = BeginPaint(hwnd, &paint);
            RECT client{};
            GetClientRect(hwnd, &client);
            HBRUSH brush = CreateSolidBrush((tick % 2) ? RGB(180, 20, 50) : RGB(20, 80, 190));
            FillRect(dc, &client, brush);
            DeleteObject(brush);
            EndPaint(hwnd, &paint);
            return 0;
        };
        cls.lpszClassName = L"WidgetRail.Preview.Synthetic";
        cls.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        if (!RegisterClassW(&cls)) return 2;
        HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
            cls.lpszClassName, L"Synthetic preview contract", WS_POPUP,
            -20000, -20000, 320, 180, nullptr, nullptr, cls.hInstance, nullptr);
        if (!window) return 3;
        ShowWindow(window, SW_SHOWNOACTIVATE);
        RECT work{};
        if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0)) return 19;
        SetWindowPos(window, HWND_BOTTOM, work.left + 4, work.bottom - 52, 48, 48, SWP_NOACTIVATE);
        UpdateWindow(window);
        FILETIME created{}, ignored{};
        GetProcessTimes(GetCurrentProcess(), &created, &ignored, &ignored, &ignored);
        widgetrail::WindowPreviewSource source{L"synthetic", window, GetCurrentProcessId(),
            (static_cast<ULONGLONG>(created.dwHighDateTime) << 32) | created.dwLowDateTime,
            cls.lpszClassName};
        widgetrail::WindowPreviewCapture capture;
        Microsoft::WRL::ComPtr<IDCompositionDesktopDevice> composition;
        winrt::check_hresult(DCompositionCreateDevice2(d2d.Get(), IID_PPV_ARGS(&composition)));
        const auto drawComposedPreview = [&](UINT width, UINT height) {
            Microsoft::WRL::ComPtr<IDCompositionSurface> surface;
            winrt::check_hresult(composition->CreateSurface(width, height,
                DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE_PREMULTIPLIED, &surface));
            Microsoft::WRL::ComPtr<ID2D1DeviceContext> target;
            POINT offset{};
            winrt::check_hresult(surface->BeginDraw(nullptr, IID_PPV_ARGS(&target), &offset));
            target->SetTransform(D2D1::Matrix3x2F::Translation(
                static_cast<float>(offset.x), static_cast<float>(offset.y)));
            target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
            auto bitmap = capture.Bitmap(target.Get(), source.windowId);
            if (bitmap) target->DrawBitmap(bitmap.Get(),
                D2D1::RectF(0, 0, static_cast<float>(width), static_cast<float>(height)));
            target.Reset();
            winrt::check_hresult(surface->EndDraw());
            winrt::check_hresult(composition->Commit());
        };
        if (!capture.Validate(source)) return 4;
        auto wrong = source;
        ++wrong.processCreated;
        if (capture.Validate(wrong)) return 5;
        // First paint records visible demand. Capture setup must follow EndDraw:
        // WGC may pump messages and dispatch another composition paint.
        std::cout << "Preview test: initial composition" << std::endl;
        drawComposedPreview(160, 90);
        // Temporarily exclude only our own synthetic window. Removing the
        // restriction must recover with identical demand, without leaving and
        // reentering the widget. Previously this latched failed forever.
        if (!SetWindowDisplayAffinity(window, WDA_EXCLUDEFROMCAPTURE)) return 9;
        std::cout << "Preview test: restricted source" << std::endl;
        capture.Reconcile(device.Get(), {&source, 1});
        if (capture.activeCount() != 0) return 10;
        const auto blocked = capture.TakeDiagnostics();
        if (blocked.size() != 1 || blocked[0].find(L"source-validation-retry") == std::wstring::npos)
            return 11;
        if (!SetWindowDisplayAffinity(window, WDA_NONE)) return 12;
        std::cout << "Preview test: restriction removed" << std::endl;
        capture.Reconcile(device.Get(), {&source, 1});
        if (capture.activeCount() != 0 || !capture.TakeDiagnostics().empty()) return 13;
        int frames{};
        std::cout << "Preview test: waiting for recovery" << std::endl;
        const auto deadline = GetTickCount64() + 3000;
        while (GetTickCount64() < deadline && frames < 2) {
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
            InvalidateRect(window, nullptr, FALSE);
            UpdateWindow(window);
            capture.Reconcile(device.Get(), {&source, 1});
            if (!capture.Poll().empty()) {
                auto bitmap = capture.Bitmap(context.Get(), source.windowId);
                if (bitmap) {
                    context->BeginDraw();
                    context->Clear(D2D1::ColorF(D2D1::ColorF::Black));
                    context->DrawBitmap(bitmap.Get(), D2D1::RectF(0, 0, 160, 90));
                    winrt::check_hresult(context->EndDraw());
                    for (UINT size : {160U, 240U, 320U, 160U})
                        drawComposedPreview(size, size * 9 / 16);
                    ++frames;
                }
            }
            Sleep(16);
        }
        std::cout << "Preview test: retiring source" << std::endl;
        DestroyWindow(window);
        (void)capture.Poll();
        if (capture.activeCount() != 0) return 6;
        capture.Reconcile(device.Get(), {});
        drawComposedPreview(160, 90);
        if (capture.Bitmap(context.Get(), source.windowId)) return 7;
        if (frames < 2) return 8;

        // Six simultaneous windows with the resolutions seen in the reported
        // failure. All must receive GPU previews without increasing the old
        // 192 MiB texture-storage budget or retaining full-size paint images.
        std::cout << "Preview test: six large windows" << std::endl;
        const std::array<SIZE, 6> sizes{{{3840,2160}, {3840,2160}, {3814,2146},
            {3638,2086}, {2539,1430}, {2516,1417}}};
        std::vector<widgetrail::WindowPreviewSource> sources;
        for (std::size_t i = 0; i < sizes.size(); ++i) {
            HWND candidate = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                cls.lpszClassName, L"Synthetic large preview", WS_POPUP,
                -20000, -20000, sizes[i].cx, sizes[i].cy, nullptr, nullptr, cls.hInstance, nullptr);
            if (!candidate) return 14;
            ShowWindow(candidate, SW_SHOWNOACTIVATE);
            UpdateWindow(candidate);
            auto next = source;
            next.windowId = L"synthetic-" + std::to_wstring(i);
            next.window = candidate;
            sources.push_back(std::move(next));
        }
        std::array<unsigned, 6> updates{};
        const auto largeDeadline = GetTickCount64() + 8000;
        while (GetTickCount64() < largeDeadline &&
            std::any_of(updates.begin(), updates.end(), [](unsigned count) { return count == 0; })) {
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
            for (const auto& candidate : sources) {
                InvalidateRect(candidate.window, nullptr, FALSE);
                UpdateWindow(candidate.window);
            }
            capture.Reconcile(device.Get(), sources);
            const auto changed = capture.Poll();
            for (std::size_t i = 0; i < sources.size(); ++i) {
                if (std::find(changed.begin(), changed.end(), sources[i].windowId) == changed.end()) continue;
                const auto bitmap = capture.Bitmap(context.Get(), sources[i].windowId);
                if (!bitmap) continue;
                const auto extent = bitmap->GetPixelSize();
                if (extent.width > 960 || extent.height > 540 || !extent.width || !extent.height) return 15;
                ++updates[i];
            }
            if (capture.reservedBytes() > 192ULL * 1024 * 1024) return 16;
            Sleep(16);
        }
        const auto largeBytes = capture.reservedBytes();
        const auto largeCount = capture.activeCount();
        for (const auto& diagnostic : capture.TakeDiagnostics()) std::wcout << diagnostic << L"\n";
        capture.Reset();
        for (const auto& candidate : sources) DestroyWindow(candidate.window);
        if (capture.reservedBytes() != 0 || capture.activeCount() != 0) return 17;
        std::cout << "Large preview updates:";
        for (const auto count : updates) std::cout << ' ' << count;
        std::cout << "; reserved bytes=" << largeBytes << std::endl;
        if (largeCount != 6 || std::any_of(updates.begin(), updates.end(), [](unsigned count) { return count == 0; }))
            return 18;
        std::cout << "Synthetic GPU frames: " << frames
            << "; transient restriction recovery, bounded retry, composed startup/resize/retirement, wrong-lifetime rejection and closure passed.\n";
        return 0;
    } catch (const winrt::hresult_error& error) {
        std::cerr << "Native preview check failed: " << std::hex << error.code().value << "\n";
        return 1;
    }
}
