#include "OverlayCompositionSurface.h"

#include <d3d11.h>
#include <dxgi1_2.h>

#include <chrono>
#include <cmath>

using Microsoft::WRL::ComPtr;

namespace gba {

bool OverlayCompositionSurface::Initialize(
    const HWND window, ID2D1Factory1* factory, std::wstring& error) {
    Reset();
    if (!window || !factory) {
        error = L"DirectComposition initialization received an invalid host";
        return false;
    }

    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    D3D_FEATURE_LEVEL featureLevel{};
    HRESULT result = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, nullptr, 0,
        D3D11_SDK_VERSION, d3dDevice_.ReleaseAndGetAddressOf(),
        &featureLevel, nullptr);
    if (FAILED(result)) {
        result = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, nullptr, 0,
            D3D11_SDK_VERSION, d3dDevice_.ReleaseAndGetAddressOf(),
            &featureLevel, nullptr);
    }
    if (FAILED(result)) {
        error = L"D3D11CreateDevice failed hresult=" +
            std::to_wstring(static_cast<unsigned long>(result));
        Reset();
        return false;
    }

    ComPtr<IDXGIDevice> dxgiDevice;
    result = d3dDevice_.As(&dxgiDevice);
    if (SUCCEEDED(result)) {
        result = factory->CreateDevice(
            dxgiDevice.Get(), d2dDevice_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = DCompositionCreateDevice2(
            d2dDevice_.Get(), __uuidof(IDCompositionDesktopDevice),
            reinterpret_cast<void**>(desktopDevice_.ReleaseAndGetAddressOf()));
    }
    if (SUCCEEDED(result)) result = desktopDevice_.As(&device_);
    if (SUCCEEDED(result)) {
        result = desktopDevice_->CreateTargetForHwnd(
            window, TRUE, target_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = device_->CreateVisual(visual_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = device_->CreateEffectGroup(effect_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) result = visual_->SetEffect(effect_.Get());
    if (SUCCEEDED(result)) result = target_->SetRoot(visual_.Get());
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result)) result = device_->WaitForCommitCompletion();
    if (FAILED(result)) {
        error = L"DirectComposition surface owner initialization failed hresult=" +
            std::to_wstring(static_cast<unsigned long>(result));
        Reset();
        return false;
    }
    return true;
}

void OverlayCompositionSurface::Reset() noexcept {
    if (target_ && device_) {
        (void)target_->SetRoot(nullptr);
        (void)device_->Commit();
    }
    surface_.Reset();
    effect_.Reset();
    visual_.Reset();
    target_.Reset();
    device_.Reset();
    desktopDevice_.Reset();
    d2dDevice_.Reset();
    d3dDevice_.Reset();
    width_ = 0;
    height_ = 0;
}

HRESULT OverlayCompositionSurface::BeginFrame(
    const unsigned int width, const unsigned int height, Frame& frame) noexcept {
    frame = {};
    if (!device_ || width == 0 || height == 0) return E_INVALIDARG;

    frame.width = width;
    frame.height = height;
    frame.replacement = !surface_ || width != width_ || height != height_;
    if (frame.replacement) {
        const HRESULT createResult = device_->CreateSurface(
            width, height, DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE_PREMULTIPLIED,
            frame.surface.ReleaseAndGetAddressOf());
        if (FAILED(createResult)) return createResult;
    } else {
        frame.surface = surface_;
    }

    RECT update{0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
    const HRESULT beginResult = frame.surface->BeginDraw(
        &update, __uuidof(ID2D1DeviceContext),
        reinterpret_cast<void**>(frame.target.ReleaseAndGetAddressOf()),
        &frame.updateOffset);
    if (SUCCEEDED(beginResult)) frame.drawing = true;
    return beginResult;
}

HRESULT OverlayCompositionSurface::EndFrame(Frame& frame) noexcept {
    if (!frame.drawing || !frame.surface) return E_UNEXPECTED;
    frame.target.Reset();
    frame.drawing = false;
    return frame.surface->EndDraw();
}

HRESULT OverlayCompositionSurface::CommitFrame(
    Frame& frame, const bool waitForCompletion, CommitTiming& timing,
    const VisualPresentation* presentation) noexcept {
    timing = {};
    if (!device_ || frame.drawing || !frame.surface) return E_UNEXPECTED;

    const auto started = std::chrono::steady_clock::now();
    HRESULT result = S_OK;
    if (frame.replacement) result = visual_->SetContent(frame.surface.Get());
    if (SUCCEEDED(result) && presentation) result = ApplyPresentation(*presentation);
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result) && waitForCompletion) {
        result = device_->WaitForCommitCompletion();
        timing.waitedForCompletion = true;
    }
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result) && frame.replacement) {
        surface_ = frame.surface;
        width_ = frame.width;
        height_ = frame.height;
    }
    frame = {};
    return result;
}

HRESULT OverlayCompositionSurface::ApplyPresentation(
    const VisualPresentation& presentation) noexcept {
    if (!visual_ || !std::isfinite(presentation.scaleX) ||
        !std::isfinite(presentation.scaleY) ||
        !std::isfinite(presentation.offsetX) ||
        !std::isfinite(presentation.offsetY) ||
        !std::isfinite(presentation.clipWidth) ||
        !std::isfinite(presentation.clipHeight) ||
        presentation.scaleX <= 0.0F || presentation.scaleY <= 0.0F ||
        presentation.clipWidth <= 0.0F || presentation.clipHeight <= 0.0F) {
        return E_INVALIDARG;
    }
    const D2D_MATRIX_3X2_F transform{
        presentation.scaleX, 0.0F,
        0.0F, presentation.scaleY,
        presentation.offsetX, presentation.offsetY,
    };
    const D2D_RECT_F clip{
        0.0F, 0.0F, presentation.clipWidth, presentation.clipHeight,
    };
    HRESULT result = visual_->SetTransform(transform);
    if (SUCCEEDED(result)) result = visual_->SetClip(clip);
    return result;
}

HRESULT OverlayCompositionSurface::CommitPresentation(
    const VisualPresentation& presentation, CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !surface_) return E_UNEXPECTED;
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = ApplyPresentation(presentation);
    if (SUCCEEDED(result)) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    return result;
}

HRESULT OverlayCompositionSurface::CommitOpacity(
    const float opacity, CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !effect_ || !std::isfinite(opacity) ||
        opacity < 0.0F || opacity > 1.0F) return E_INVALIDARG;
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = effect_->SetOpacity(opacity);
    if (SUCCEEDED(result)) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    return result;
}

void OverlayCompositionSurface::AbandonFrame(Frame& frame) noexcept {
    if (frame.drawing && frame.surface) {
        frame.target.Reset();
        (void)frame.surface->EndDraw();
    }
    frame = {};
}

} // namespace gba
