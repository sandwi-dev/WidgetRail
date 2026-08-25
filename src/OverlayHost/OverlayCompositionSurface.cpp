#include "OverlayCompositionSurface.h"

#include <d3d11.h>
#include <dxgi1_2.h>

#include <chrono>
#include <array>
#include <cmath>

using Microsoft::WRL::ComPtr;

namespace widgetrail {

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
        result = device_->CreateVisual(rootVisual_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = device_->CreateVisual(content_.visual.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = device_->CreateVisual(guide_.visual.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = device_->CreateVisual(tray_.visual.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = device_->CreateEffectGroup(effect_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) result = rootVisual_->SetEffect(effect_.Get());
    if (SUCCEEDED(result)) {
        result = rootVisual_->AddVisual(content_.visual.Get(), FALSE, nullptr);
    }
    if (SUCCEEDED(result)) result = target_->SetRoot(rootVisual_.Get());
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

bool OverlayCompositionSurface::InitializeChromeTarget(
    const HWND window, std::wstring& error) {
    if (!window || !device_ || chromeTarget_ || chromeRootVisual_) {
        error = L"DirectComposition chrome target initialization received an invalid state";
        return false;
    }
    HRESULT result = desktopDevice_->CreateTargetForHwnd(
        window, TRUE, chromeTarget_.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) {
        result = device_->CreateVisual(chromeRootVisual_.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) {
        result = chromeRootVisual_->AddVisual(guide_.visual.Get(), FALSE, nullptr);
    }
    if (SUCCEEDED(result)) {
        result = chromeRootVisual_->AddVisual(tray_.visual.Get(), TRUE, guide_.visual.Get());
    }
    if (SUCCEEDED(result)) result = chromeTarget_->SetRoot(chromeRootVisual_.Get());
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result)) result = device_->WaitForCommitCompletion();
    if (FAILED(result)) {
        error = L"DirectComposition chrome target initialization failed hresult=" +
            std::to_wstring(static_cast<unsigned long>(result));
        if (chromeTarget_) (void)chromeTarget_->SetRoot(nullptr);
        chromeRootVisual_.Reset();
        chromeTarget_.Reset();
        return false;
    }
    return true;
}

void OverlayCompositionSurface::Reset() noexcept {
    if (chromeTarget_ && device_) {
        (void)chromeTarget_->SetRoot(nullptr);
    }
    if (target_ && device_) {
        (void)target_->SetRoot(nullptr);
        (void)device_->Commit();
    }
    content_ = {};
    externalContentVisual_.Reset();
    externalContentAttached_ = false;
    guide_ = {};
    tray_ = {};
    effect_.Reset();
    chromeRootVisual_.Reset();
    rootVisual_.Reset();
    chromeTarget_.Reset();
    target_.Reset();
    device_.Reset();
    desktopDevice_.Reset();
    d2dDevice_.Reset();
    d3dDevice_.Reset();
    paintCounters_ = {};
}

HRESULT OverlayCompositionSurface::CreateExternalContentTarget(
    IUnknown** target) noexcept {
    if (!target) return E_POINTER;
    *target = nullptr;
    if (!device_ || !rootVisual_ || !content_.visual || externalContentVisual_)
        return E_UNEXPECTED;
    HRESULT result = device_->CreateVisual(
        externalContentVisual_.ReleaseAndGetAddressOf());
    if (FAILED(result)) {
        externalContentVisual_.Reset();
        return result;
    }
    return externalContentVisual_.CopyTo(target);
}

HRESULT OverlayCompositionSurface::CommitExternalContentPresentation(
    const RECT& bounds, const bool visible, CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !externalContentVisual_ || !content_.visual ||
        bounds.right <= bounds.left || bounds.bottom <= bounds.top)
        return E_INVALIDARG;
    const auto started = std::chrono::steady_clock::now();
    const D2D_RECT_F clip{
        0.0F, 0.0F,
        static_cast<float>(bounds.right - bounds.left),
        static_cast<float>(bounds.bottom - bounds.top),
    };
    HRESULT result = externalContentVisual_->SetOffsetX(
        static_cast<float>(bounds.left));
    if (SUCCEEDED(result)) result = externalContentVisual_->SetOffsetY(
        static_cast<float>(bounds.top));
    if (SUCCEEDED(result)) result = externalContentVisual_->SetClip(clip);
    if (SUCCEEDED(result) && visible && !externalContentAttached_) {
        result = rootVisual_->AddVisual(
            externalContentVisual_.Get(), TRUE, content_.visual.Get());
        if (SUCCEEDED(result)) externalContentAttached_ = true;
    } else if (SUCCEEDED(result) && !visible && externalContentAttached_) {
        result = rootVisual_->RemoveVisual(externalContentVisual_.Get());
        if (SUCCEEDED(result)) externalContentAttached_ = false;
    }
    if (SUCCEEDED(result)) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    return result;
}

HRESULT OverlayCompositionSurface::DetachExternalContentTarget(
    CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !rootVisual_ || !content_.visual) return E_UNEXPECTED;
    if (!externalContentVisual_) return S_FALSE;
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = S_OK;
    if (externalContentAttached_)
        result = rootVisual_->RemoveVisual(externalContentVisual_.Get());
    if (SUCCEEDED(result)) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result)) {
        externalContentVisual_.Reset();
        externalContentAttached_ = false;
    }
    return result;
}

OverlayCompositionSurface::LayerState& OverlayCompositionSurface::StateFor(
    const Layer layer) noexcept {
    switch (layer) {
    case Layer::Guide: return guide_;
    case Layer::Tray: return tray_;
    case Layer::Content:
    default: return content_;
    }
}

const OverlayCompositionSurface::LayerState& OverlayCompositionSurface::StateFor(
    const Layer layer) const noexcept {
    switch (layer) {
    case Layer::Guide: return guide_;
    case Layer::Tray: return tray_;
    case Layer::Content:
    default: return content_;
    }
}

bool OverlayCompositionSurface::hasContent() const noexcept {
    return hasContent(Layer::Content);
}

bool OverlayCompositionSurface::hasContent(const Layer layer) const noexcept {
    return StateFor(layer).surface != nullptr;
}

unsigned int OverlayCompositionSurface::width() const noexcept {
    return content_.width;
}

unsigned int OverlayCompositionSurface::height() const noexcept {
    return content_.height;
}

unsigned int OverlayCompositionSurface::width(const Layer layer) const noexcept {
    return StateFor(layer).width;
}

unsigned int OverlayCompositionSurface::height(const Layer layer) const noexcept {
    return StateFor(layer).height;
}

HRESULT OverlayCompositionSurface::BeginFrame(
    const unsigned int width, const unsigned int height, Frame& frame) noexcept {
    return BeginFrame(Layer::Content, width, height, 0.0F, 0.0F, nullptr, frame);
}

HRESULT OverlayCompositionSurface::BeginFrame(
    const Layer layer, const unsigned int width, const unsigned int height,
    const float visualOffsetX, const float visualOffsetY,
    const RECT* update, Frame& frame) noexcept {
    frame = {};
    if (!device_ || width == 0 || height == 0 ||
        !std::isfinite(visualOffsetX) || !std::isfinite(visualOffsetY)) {
        return E_INVALIDARG;
    }

    frame.layer = layer;
    frame.width = width;
    frame.height = height;
    frame.visualOffsetX = visualOffsetX;
    frame.visualOffsetY = visualOffsetY;
    const auto& state = StateFor(layer);
    frame.replacement = !state.surface || width != state.width || height != state.height;
    if (frame.replacement) {
        const HRESULT createResult = device_->CreateSurface(
            width, height, DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE_PREMULTIPLIED,
            frame.surface.ReleaseAndGetAddressOf());
        if (FAILED(createResult)) return createResult;
    } else {
        frame.surface = state.surface;
    }

    RECT fullUpdate{0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
    const RECT* updateArea = frame.replacement || !update ? &fullUpdate : update;
    if (updateArea->left < 0 || updateArea->top < 0 ||
        updateArea->right <= updateArea->left ||
        updateArea->bottom <= updateArea->top ||
        updateArea->right > static_cast<LONG>(width) ||
        updateArea->bottom > static_cast<LONG>(height)) {
        frame = {};
        return E_INVALIDARG;
    }
    frame.updateArea = *updateArea;
    const HRESULT beginResult = frame.surface->BeginDraw(
        updateArea, __uuidof(ID2D1DeviceContext),
        reinterpret_cast<void**>(frame.target.ReleaseAndGetAddressOf()),
        &frame.updateOffset);
    if (SUCCEEDED(beginResult)) frame.drawing = true;
    return beginResult;
}

HRESULT OverlayCompositionSurface::EndFrame(Frame& frame) noexcept {
    if (!frame.drawing || !frame.surface) return E_UNEXPECTED;
    frame.target.Reset();
    frame.drawing = false;
    const HRESULT result = frame.surface->EndDraw();
    if (SUCCEEDED(result)) {
        switch (frame.layer) {
        case Layer::Content: ++paintCounters_.content; break;
        case Layer::Guide: ++paintCounters_.guide; break;
        case Layer::Tray: ++paintCounters_.tray; break;
        }
    }
    return result;
}

HRESULT OverlayCompositionSurface::CommitFrame(
    Frame& frame, const bool waitForCompletion, CommitTiming& timing,
    const VisualPresentation* presentation) noexcept {
    std::array<Frame*, 1> frames{&frame};
    return CommitFrames(frames, waitForCompletion, timing, presentation);
}

HRESULT OverlayCompositionSurface::CommitFrames(
    const std::span<Frame*> frames, const bool waitForCompletion,
    CommitTiming& timing, const VisualPresentation* presentation) noexcept {
    timing = {};
    if (!device_ || frames.empty()) return E_UNEXPECTED;
    for (const auto* frame : frames) {
        if (!frame || frame->drawing || !frame->surface) return E_UNEXPECTED;
    }

    const auto started = std::chrono::steady_clock::now();
    HRESULT result = S_OK;
    for (auto* frame : frames) {
        auto& state = StateFor(frame->layer);
        if (frame->replacement) result = state.visual->SetContent(frame->surface.Get());
        if (FAILED(result)) break;
        // Content placement belongs to the frame transaction. Guide and tray
        // offsets belong exclusively to the fixed-chrome session and remain
        // latched across their surface replacement or repaint.
        if (FrameOwnsVisualOffset(*frame)) {
            result = state.visual->SetOffsetX(frame->visualOffsetX);
            if (SUCCEEDED(result))
                result = state.visual->SetOffsetY(frame->visualOffsetY);
        }
        if (FAILED(result)) break;
    }
    if (SUCCEEDED(result) && presentation) result = ApplyPresentation(*presentation);
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result) && waitForCompletion) {
        result = device_->WaitForCommitCompletion();
        timing.waitedForCompletion = true;
    }
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result)) {
        for (auto* frame : frames) {
            if (!frame->replacement) continue;
            auto& state = StateFor(frame->layer);
            state.surface = frame->surface;
            state.width = frame->width;
            state.height = frame->height;
        }
    }
    for (auto* frame : frames) *frame = {};
    return result;
}

HRESULT OverlayCompositionSurface::ApplyPresentation(
    const VisualPresentation& presentation) noexcept {
    if (!content_.visual || !std::isfinite(presentation.scaleX) ||
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
    HRESULT result = content_.visual->SetTransform(transform);
    if (SUCCEEDED(result)) result = content_.visual->SetClip(clip);
    return result;
}

HRESULT OverlayCompositionSurface::ApplyChromePresentation(
    const ChromePresentation& presentation) noexcept {
    if (!guide_.visual || !tray_.visual ||
        !std::isfinite(presentation.guideOffsetX) ||
        !std::isfinite(presentation.guideOffsetY) ||
        !std::isfinite(presentation.trayOffsetX) ||
        !std::isfinite(presentation.trayOffsetY)) return E_INVALIDARG;
    HRESULT result = guide_.visual->SetOffsetX(presentation.guideOffsetX);
    if (SUCCEEDED(result)) result = guide_.visual->SetOffsetY(presentation.guideOffsetY);
    if (SUCCEEDED(result)) result = tray_.visual->SetOffsetX(presentation.trayOffsetX);
    if (SUCCEEDED(result)) result = tray_.visual->SetOffsetY(presentation.trayOffsetY);
    return result;
}

HRESULT OverlayCompositionSurface::CommitPresentation(
    const VisualPresentation& presentation, CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !content_.surface) return E_UNEXPECTED;
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = ApplyPresentation(presentation);
    if (SUCCEEDED(result)) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    return result;
}

HRESULT OverlayCompositionSurface::CommitChromePresentation(
    const ChromePresentation& presentation, CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !chromeTarget_ || !chromeRootVisual_) return E_UNEXPECTED;
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = ApplyChromePresentation(presentation);
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

} // namespace widgetrail
