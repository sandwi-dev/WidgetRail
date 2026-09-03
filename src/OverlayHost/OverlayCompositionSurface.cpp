#include "OverlayCompositionSurface.h"

#include <d3d11.h>
#include <dxgi1_2.h>

#include <algorithm>
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

bool OverlayCompositionSurface::InitializePinnedExternalContentEndpoint(
    const HWND window, std::wstring& error) {
    if (!window || !device_ || !desktopDevice_ || pinnedExternalTarget_ ||
        pinnedExternalRootVisual_ || pinnedExternalContentVisual_) {
        error = L"DirectComposition pinned external endpoint received an invalid state";
        return false;
    }
    HRESULT result = desktopDevice_->CreateTargetForHwnd(
        window, TRUE, pinnedExternalTarget_.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result))
        result = device_->CreateVisual(pinnedExternalRootVisual_.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result))
        result = pinnedExternalTarget_->SetRoot(pinnedExternalRootVisual_.Get());
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result)) result = device_->WaitForCommitCompletion();
    if (FAILED(result)) {
        error = L"DirectComposition pinned external endpoint failed hresult=" +
            std::to_wstring(static_cast<unsigned long>(result));
        if (pinnedExternalTarget_) (void)pinnedExternalTarget_->SetRoot(nullptr);
        pinnedExternalRootVisual_.Reset();
        pinnedExternalTarget_.Reset();
        return false;
    }
    return true;
}

void OverlayCompositionSurface::Reset() noexcept {
    if (pinnedExternalTarget_ && device_)
        (void)pinnedExternalTarget_->SetRoot(nullptr);
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
    externalContentPresentation_ = {};
    pinnedExternalContentVisual_.Reset();
    pinnedExternalContentAttached_ = false;
    pinnedExternalContentPresentation_ = {};
    pinnedMediaChromeVisual_.Reset();
    pinnedMediaChromeSurface_.Reset();
    pinnedMediaChromeAttached_ = false;
    pinnedMediaChromePresentation_.reset();
    pinnedMediaChromeWidth_ = 0;
    pinnedMediaChromeHeight_ = 0;
    pinnedExternalRootVisual_.Reset();
    guide_ = {};
    tray_ = {};
    effect_.Reset();
    chromeRootVisual_.Reset();
    rootVisual_.Reset();
    chromeTarget_.Reset();
    pinnedExternalTarget_.Reset();
    target_.Reset();
    device_.Reset();
    desktopDevice_.Reset();
    d2dDevice_.Reset();
    d3dDevice_.Reset();
    paintCounters_ = {};
#if defined(WRAIL_EMBEDDED_MEDIA_HANDOFF_TESTING)
    externalContentFailureForTest_ = ExternalContentFailureOperation::None;
#endif
}

HRESULT OverlayCompositionSurface::CreateExternalContentTarget(
    IUnknown** target) noexcept {
    return CreateExternalContentTarget(ExternalContentEndpoint::Overlay, target);
}

HRESULT OverlayCompositionSurface::CreateExternalContentTarget(
    const ExternalContentEndpoint endpoint, IUnknown** target) noexcept {
    if (!target) return E_POINTER;
    *target = nullptr;
    auto& visual = endpoint == ExternalContentEndpoint::Overlay
        ? externalContentVisual_ : pinnedExternalContentVisual_;
    auto& presentation = ExternalPresentationFor(endpoint);
    const bool endpointReady = endpoint == ExternalContentEndpoint::Overlay
        ? rootVisual_ && content_.visual
        : pinnedExternalTarget_ && pinnedExternalRootVisual_;
    if (!device_ || !endpointReady || visual)
        return E_UNEXPECTED;
#if defined(WRAIL_EMBEDDED_MEDIA_HANDOFF_TESTING)
    const auto createFailure = endpoint == ExternalContentEndpoint::Overlay
        ? ExternalContentFailureOperation::CreateOverlayTarget
        : ExternalContentFailureOperation::CreatePinnedTarget;
    if (externalContentFailureForTest_ == createFailure) {
        externalContentFailureForTest_ = ExternalContentFailureOperation::None;
        return E_FAIL;
    }
#endif
    HRESULT result = device_->CreateVisual(visual.ReleaseAndGetAddressOf());
    if (FAILED(result)) {
        visual.Reset();
        return result;
    }
    presentation.current = false;
    return visual.CopyTo(target);
}

HRESULT OverlayCompositionSurface::CreateExternalContentParkingTarget(
    IUnknown** target) noexcept {
    if (!target) return E_POINTER;
    *target = nullptr;
    if (!device_) return E_UNEXPECTED;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> visual;
    const HRESULT result = device_->CreateVisual(visual.ReleaseAndGetAddressOf());
    if (FAILED(result)) return result;
    return visual.CopyTo(target);
}

HRESULT OverlayCompositionSurface::CommitExternalContentPresentation(
    const ExternalContentEndpoint endpoint, const RECT& bounds,
    const bool visible, CommitTiming& timing) noexcept {
    return CommitExternalContentPresentation(
        endpoint, bounds, bounds, visible, timing);
}

HRESULT OverlayCompositionSurface::CommitExternalContentPresentation(
    const ExternalContentEndpoint endpoint, const RECT& bounds,
    const RECT& clipBounds, const bool visible, CommitTiming& timing) noexcept {
    timing = {};
    auto& visual = endpoint == ExternalContentEndpoint::Overlay
        ? externalContentVisual_ : pinnedExternalContentVisual_;
    auto& attached = endpoint == ExternalContentEndpoint::Overlay
        ? externalContentAttached_ : pinnedExternalContentAttached_;
    auto& presentation = ExternalPresentationFor(endpoint);
    ++presentation.counters.requested;
    const auto coordinates = ExternalContentCoordinates(endpoint);
    auto* parent = coordinates == ExternalContentCoordinateSpace::ContentLocal
        ? content_.visual.Get() : pinnedExternalRootVisual_.Get();
    if (!device_ || !visual || !parent || bounds.right <= bounds.left ||
        bounds.bottom <= bounds.top || clipBounds.right <= clipBounds.left ||
        clipBounds.bottom <= clipBounds.top) return E_INVALIDARG;
    const auto started = std::chrono::steady_clock::now();
    const auto width = bounds.right - bounds.left;
    const auto height = bounds.bottom - bounds.top;
    const D2D_RECT_F clip{
        static_cast<float>(std::clamp(clipBounds.left - bounds.left, 0L, width)),
        static_cast<float>(std::clamp(clipBounds.top - bounds.top, 0L, height)),
        static_cast<float>(std::clamp(clipBounds.right - bounds.left, 0L, width)),
        static_cast<float>(std::clamp(clipBounds.bottom - bounds.top, 0L, height))};
    if (clip.right <= clip.left || clip.bottom <= clip.top) return E_INVALIDARG;
    const auto sameRect = [](const RECT& left, const RECT& right) noexcept {
        return left.left == right.left && left.top == right.top &&
            left.right == right.right && left.bottom == right.bottom;
    };
    if (presentation.current && presentation.visual == visual.Get() &&
        presentation.parent == parent && sameRect(presentation.bounds, bounds) &&
        sameRect(presentation.clipBounds, clipBounds) &&
        presentation.visible == visible) return S_FALSE;
    HRESULT result = visual->SetOffsetX(static_cast<float>(bounds.left));
    if (SUCCEEDED(result)) result = visual->SetOffsetY(static_cast<float>(bounds.top));
    if (SUCCEEDED(result)) result = visual->SetClip(clip);
    if (SUCCEEDED(result) && visible && !attached) {
        IDCompositionVisual2* reference =
            endpoint == ExternalContentEndpoint::Pinned &&
                pinnedMediaChromeAttached_
            ? pinnedMediaChromeVisual_.Get()
            : nullptr;
        result = parent->AddVisual(
            visual.Get(),
            coordinates == ExternalContentCoordinateSpace::ContentLocal ? TRUE : FALSE,
            reference);
        if (SUCCEEDED(result)) attached = true;
    } else if (SUCCEEDED(result) && !visible && attached) {
        result = parent->RemoveVisual(visual.Get());
        if (SUCCEEDED(result)) attached = false;
    }
    if (SUCCEEDED(result)) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result)) {
        timing.externalPresentationCommitted = true;
        ++presentation.counters.committed;
        presentation.current = true;
        presentation.visual = visual.Get();
        presentation.parent = parent;
        presentation.bounds = bounds;
        presentation.clipBounds = clipBounds;
        presentation.visible = visible;
    }
    return result;
}

HRESULT OverlayCompositionSurface::CommitExternalContentPresentation(
    const RECT& bounds, const bool visible, CommitTiming& timing) noexcept {
    return CommitExternalContentPresentation(bounds, bounds, visible, timing);
}

HRESULT OverlayCompositionSurface::CommitExternalContentPresentation(
    const RECT& bounds, const RECT& clipBounds,
    const bool visible, CommitTiming& timing) noexcept {
    return CommitExternalContentPresentation(
        ExternalContentEndpoint::Overlay, bounds, clipBounds, visible, timing);
}

HRESULT OverlayCompositionSurface::DetachExternalContentTarget(
    CommitTiming& timing) noexcept {
    return DetachExternalContentTarget(ExternalContentEndpoint::Overlay, timing);
}

HRESULT OverlayCompositionSurface::DetachExternalContentTarget(
    const ExternalContentEndpoint endpoint, CommitTiming& timing) noexcept {
    timing = {};
    auto& visual = endpoint == ExternalContentEndpoint::Overlay
        ? externalContentVisual_ : pinnedExternalContentVisual_;
    auto& attached = endpoint == ExternalContentEndpoint::Overlay
        ? externalContentAttached_ : pinnedExternalContentAttached_;
    const auto coordinates = ExternalContentCoordinates(endpoint);
    auto* parent = coordinates == ExternalContentCoordinateSpace::ContentLocal
        ? content_.visual.Get() : pinnedExternalRootVisual_.Get();
    if (!device_ || !parent) return E_UNEXPECTED;
    if (!visual) return S_FALSE;
#if defined(WRAIL_EMBEDDED_MEDIA_HANDOFF_TESTING)
    const auto detachFailure = endpoint == ExternalContentEndpoint::Overlay
        ? ExternalContentFailureOperation::DetachOverlay
        : ExternalContentFailureOperation::DetachPinned;
    if (externalContentFailureForTest_ == detachFailure) {
        externalContentFailureForTest_ = ExternalContentFailureOperation::None;
        return E_FAIL;
    }
#endif
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = S_OK;
    if (attached) result = parent->RemoveVisual(visual.Get());
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result)) {
        result = device_->WaitForCommitCompletion();
        timing.waitedForCompletion = true;
    }
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result)) {
        visual.Reset();
        attached = false;
        ExternalPresentationFor(endpoint).current = false;
    }
    return result;
}

HRESULT OverlayCompositionSurface::ReleasePinnedExternalContentEndpoint(
    CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !pinnedExternalTarget_ || !pinnedExternalRootVisual_)
        return S_FALSE;
    if (pinnedExternalContentVisual_) return E_UNEXPECTED;
#if defined(WRAIL_EMBEDDED_MEDIA_HANDOFF_TESTING)
    if (externalContentFailureForTest_ ==
            ExternalContentFailureOperation::ReleasePinnedEndpoint) {
        externalContentFailureForTest_ = ExternalContentFailureOperation::None;
        return E_FAIL;
    }
#endif
    const auto started = std::chrono::steady_clock::now();
    HRESULT result = pinnedExternalTarget_->SetRoot(nullptr);
    if (SUCCEEDED(result)) result = device_->Commit();
    if (SUCCEEDED(result)) {
        result = device_->WaitForCommitCompletion();
        timing.waitedForCompletion = true;
    }
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result)) {
        pinnedMediaChromeVisual_.Reset();
        pinnedMediaChromeSurface_.Reset();
        pinnedMediaChromeAttached_ = false;
        pinnedMediaChromePresentation_.reset();
        pinnedMediaChromeWidth_ = 0;
        pinnedMediaChromeHeight_ = 0;
        pinnedExternalRootVisual_.Reset();
        pinnedExternalTarget_.Reset();
        pinnedExternalContentAttached_ = false;
        pinnedExternalContentPresentation_.current = false;
    }
    return result;
}

HRESULT OverlayCompositionSurface::CommitPinnedMediaChrome(
    const PinnedMediaChromePresentation& presentation,
    CommitTiming& timing) noexcept {
    timing = {};
    if (!device_ || !pinnedExternalRootVisual_ ||
        presentation.bounds.right <= presentation.bounds.left ||
        presentation.bounds.bottom <= presentation.bounds.top ||
        !std::isfinite(presentation.progress) || presentation.progress < 0.0 ||
        presentation.progress > 1.0) return E_INVALIDARG;
    const auto sameColor = [](const D2D1_COLOR_F& left,
                              const D2D1_COLOR_F& right) noexcept {
        return left.r == right.r && left.g == right.g && left.b == right.b &&
            left.a == right.a;
    };
    const auto samePresentation = [&](
        const PinnedMediaChromePresentation& left,
        const PinnedMediaChromePresentation& right) noexcept {
        return left.bounds.left == right.bounds.left &&
            left.bounds.top == right.bounds.top &&
            left.bounds.right == right.bounds.right &&
            left.bounds.bottom == right.bounds.bottom &&
            left.visible == right.visible && left.focused == right.focused &&
            left.scrubActive == right.scrubActive &&
            left.progress == right.progress &&
            sameColor(left.backgroundColor, right.backgroundColor) &&
            sameColor(left.trackColor, right.trackColor) &&
            sameColor(left.accentColor, right.accentColor) &&
            sameColor(left.focusColor, right.focusColor);
    };
    if (pinnedMediaChromePresentation_ &&
        samePresentation(*pinnedMediaChromePresentation_, presentation))
        return S_FALSE;
    const auto started = std::chrono::steady_clock::now();
    const unsigned int width = static_cast<unsigned int>(
        presentation.bounds.right - presentation.bounds.left);
    const unsigned int height = static_cast<unsigned int>(
        presentation.bounds.bottom - presentation.bounds.top);
    const bool replacement = !pinnedMediaChromeSurface_ ||
        width != pinnedMediaChromeWidth_ || height != pinnedMediaChromeHeight_;
    const bool sameBounds = pinnedMediaChromePresentation_ &&
        pinnedMediaChromePresentation_->bounds.left == presentation.bounds.left &&
        pinnedMediaChromePresentation_->bounds.top == presentation.bounds.top &&
        pinnedMediaChromePresentation_->bounds.right == presentation.bounds.right &&
        pinnedMediaChromePresentation_->bounds.bottom == presentation.bounds.bottom;
    const bool renderedContentChanged = replacement ||
        !pinnedMediaChromePresentation_ ||
        pinnedMediaChromePresentation_->focused != presentation.focused ||
        pinnedMediaChromePresentation_->scrubActive != presentation.scrubActive ||
        pinnedMediaChromePresentation_->progress != presentation.progress ||
        !sameColor(pinnedMediaChromePresentation_->backgroundColor,
                   presentation.backgroundColor) ||
        !sameColor(pinnedMediaChromePresentation_->trackColor,
                   presentation.trackColor) ||
        !sameColor(pinnedMediaChromePresentation_->accentColor,
                   presentation.accentColor) ||
        !sameColor(pinnedMediaChromePresentation_->focusColor,
                   presentation.focusColor);
    HRESULT result = S_OK;
    if (!pinnedMediaChromeVisual_)
        result = device_->CreateVisual(
            pinnedMediaChromeVisual_.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result) && replacement) {
        result = device_->CreateSurface(
            width, height, DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE_PREMULTIPLIED,
            pinnedMediaChromeSurface_.ReleaseAndGetAddressOf());
        if (SUCCEEDED(result))
            result = pinnedMediaChromeVisual_->SetContent(
                pinnedMediaChromeSurface_.Get());
    }
    if (FAILED(result)) return result;

    if (renderedContentChanged) {
        RECT update{0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
        ComPtr<ID2D1DeviceContext> target;
        POINT offset{};
        result = pinnedMediaChromeSurface_->BeginDraw(
            &update, __uuidof(ID2D1DeviceContext),
            reinterpret_cast<void**>(target.ReleaseAndGetAddressOf()), &offset);
        if (SUCCEEDED(result)) {
            target->SetTransform(D2D1::Matrix3x2F::Translation(
                static_cast<float>(offset.x), static_cast<float>(offset.y)));
            target->Clear(D2D1::ColorF(0, 0.0F));
            const float barHeight = std::min(28.0F, static_cast<float>(height));
            const float top = static_cast<float>(height) - barHeight;
            ComPtr<ID2D1SolidColorBrush> shade;
            ComPtr<ID2D1SolidColorBrush> track;
            ComPtr<ID2D1SolidColorBrush> accent;
            result = target->CreateSolidColorBrush(
                presentation.backgroundColor, shade.ReleaseAndGetAddressOf());
            if (SUCCEEDED(result)) result = target->CreateSolidColorBrush(
                presentation.trackColor, track.ReleaseAndGetAddressOf());
            if (SUCCEEDED(result)) result = target->CreateSolidColorBrush(
                presentation.scrubActive ? presentation.focusColor
                                         : presentation.accentColor,
                accent.ReleaseAndGetAddressOf());
            if (SUCCEEDED(result)) {
                target->FillRectangle(
                    D2D1::RectF(0.0F, top, static_cast<float>(width),
                                static_cast<float>(height)), shade.Get());
                const float left = 12.0F;
                const float right = std::max(
                    left, static_cast<float>(width) - 12.0F);
                const float trackTop = top + barHeight * 0.5F - 2.0F;
                target->FillRoundedRectangle(
                    D2D1::RoundedRect(
                        D2D1::RectF(left, trackTop, right, trackTop + 4.0F),
                        2.0F, 2.0F), track.Get());
                const float progressRight = left + (right - left) *
                    static_cast<float>(presentation.progress);
                target->FillRoundedRectangle(
                    D2D1::RoundedRect(
                        D2D1::RectF(left, trackTop, progressRight,
                                    trackTop + 4.0F), 2.0F, 2.0F),
                    accent.Get());
                if (presentation.focused) {
                    target->DrawRoundedRectangle(
                        D2D1::RoundedRect(
                            D2D1::RectF(4.0F, top + 3.0F,
                                        static_cast<float>(width) - 4.0F,
                                        static_cast<float>(height) - 3.0F),
                            5.0F, 5.0F),
                        accent.Get(), presentation.scrubActive ? 2.5F : 1.5F);
                }
            }
            target.Reset();
            const HRESULT end = pinnedMediaChromeSurface_->EndDraw();
            if (SUCCEEDED(result)) result = end;
        }
        if (FAILED(result)) return result;
    }
    const bool treeChanged = replacement || !sameBounds ||
        !pinnedMediaChromePresentation_ ||
        pinnedMediaChromePresentation_->visible != presentation.visible;
    if (!sameBounds) {
        result = pinnedMediaChromeVisual_->SetOffsetX(
            static_cast<float>(presentation.bounds.left));
        if (SUCCEEDED(result)) result = pinnedMediaChromeVisual_->SetOffsetY(
            static_cast<float>(presentation.bounds.top));
    }
    if (SUCCEEDED(result) && presentation.visible && !pinnedMediaChromeAttached_) {
        result = pinnedExternalRootVisual_->AddVisual(
            pinnedMediaChromeVisual_.Get(), FALSE, nullptr);
        if (SUCCEEDED(result)) pinnedMediaChromeAttached_ = true;
    } else if (SUCCEEDED(result) && !presentation.visible &&
               pinnedMediaChromeAttached_) {
        result = pinnedExternalRootVisual_->RemoveVisual(
            pinnedMediaChromeVisual_.Get());
        if (SUCCEEDED(result)) pinnedMediaChromeAttached_ = false;
    }
    const bool presentationChanged = renderedContentChanged || treeChanged;
    if (SUCCEEDED(result) && presentationChanged) result = device_->Commit();
    timing.commitMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - started).count());
    if (SUCCEEDED(result)) {
        timing.externalPresentationCommitted = presentationChanged;
        pinnedMediaChromePresentation_ = presentation;
        pinnedMediaChromeWidth_ = width;
        pinnedMediaChromeHeight_ = height;
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

OverlayCompositionSurface::ExternalContentPresentationState&
OverlayCompositionSurface::ExternalPresentationFor(
    const ExternalContentEndpoint endpoint) noexcept {
    return endpoint == ExternalContentEndpoint::Overlay
        ? externalContentPresentation_ : pinnedExternalContentPresentation_;
}

const OverlayCompositionSurface::ExternalContentPresentationState&
OverlayCompositionSurface::ExternalPresentationFor(
    const ExternalContentEndpoint endpoint) const noexcept {
    return endpoint == ExternalContentEndpoint::Overlay
        ? externalContentPresentation_ : pinnedExternalContentPresentation_;
}

OverlayCompositionSurface::ExternalContentCommitCounters
OverlayCompositionSurface::externalContentCommitCounters(
    const ExternalContentEndpoint endpoint) const noexcept {
    return ExternalPresentationFor(endpoint).counters;
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
