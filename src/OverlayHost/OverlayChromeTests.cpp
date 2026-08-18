#include "OverlayChrome.h"
#include "OverlayCompositionSurface.h"
#include "OverlayState.h"
#include "TrayLayout.h"

#include <Windows.h>
#include <d2d1.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <array>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <utility>

namespace {

using Microsoft::WRL::ComPtr;

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void CheckCompositionCoordinatePolicies() {
    const auto dpi120 = gba::NormalizeCompositionUpdateOffset({5, -10}, 120);
    const auto dpi144 = gba::NormalizeCompositionUpdateOffset({3, 9}, 144);
    const auto fallback = gba::NormalizeCompositionUpdateOffset({7, -4}, 0);
    Check(std::abs(dpi120.x - 4.0F) < 0.001F &&
              std::abs(dpi120.y + 8.0F) < 0.001F,
          "125-percent BeginDraw pixels normalize to DIPs");
    Check(std::abs(dpi144.x - 2.0F) < 0.001F &&
              std::abs(dpi144.y - 6.0F) < 0.001F,
          "150-percent BeginDraw pixels normalize to DIPs");
    Check(fallback.x == 7.0F && fallback.y == -4.0F,
          "missing DPI uses the 96-DPI update-offset contract");

    gba::OverlayCompositionSurface::Frame content;
    content.layer = gba::OverlayCompositionSurface::Layer::Content;
    content.replacement = false;
    gba::OverlayCompositionSurface::Frame guide;
    guide.layer = gba::OverlayCompositionSurface::Layer::Guide;
    guide.replacement = false;
    gba::OverlayCompositionSurface::Frame tray;
    tray.layer = gba::OverlayCompositionSurface::Layer::Tray;
    tray.replacement = true;
    Check(gba::OverlayCompositionSurface::FrameOwnsVisualOffset(content),
          "ordinary content repaint owns its visual offset");
    Check(!gba::OverlayCompositionSurface::FrameOwnsVisualOffset(guide),
          "ordinary guide repaint preserves its latched chrome offset");
    Check(!gba::OverlayCompositionSurface::FrameOwnsVisualOffset(tray),
          "tray surface replacement preserves its latched chrome offset");

    constexpr RECT work{0, 0, 1920, 1080};
    const auto panel = gba::shell::ComputeContentWindowBoundsAboveGuide(
        work, 900, 760, 385, 5);
    Check(panel && panel->left == 580 && panel->right == 1340 &&
              panel->top == 510 && panel->bottom == 895,
          "panel-local content is centered and ends at the authored guide gap");
    Check(!gba::shell::ComputeContentWindowBoundsAboveGuide(
              work, 900, 0, 385, 5) &&
              !gba::shell::ComputeContentWindowBoundsAboveGuide(
                  work, 900, 760, 385, -1),
          "invalid panel-local content placement fails closed");
}

void CheckFrame(
    ID2D1Factory* d2d,
    IWICImagingFactory* wic,
    const float scale) {
    constexpr UINT width = 144;
    constexpr UINT height = 96;
    ComPtr<IWICBitmap> bitmap;
    Check(SUCCEEDED(wic->CreateBitmap(
              width, height, GUID_WICPixelFormat32bppPBGRA,
              WICBitmapCacheOnLoad, bitmap.ReleaseAndGetAddressOf())),
          "WIC color-key frame is created");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
              bitmap.Get(), D2D1::RenderTargetProperties(),
              target.ReleaseAndGetAddressOf())),
          "Direct2D color-key target is created");
    ComPtr<ID2D1SolidColorBrush> surface;
    Check(SUCCEEDED(target->CreateSolidColorBrush(
              D2D1::ColorF(0x2D / 255.0F, 0x35 / 255.0F, 0x48 / 255.0F, 1.0F),
              surface.ReleaseAndGetAddressOf())),
          "shell surface brush is created");

    target->BeginDraw();
    target->Clear(D2D1::ColorF(1 / 255.0F, 2 / 255.0F, 3 / 255.0F, 1.0F));
    target->SetTransform(D2D1::Matrix3x2F::Scale(scale, scale));
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    gba::shell::FillColorKeyRoundedRectangle(
        target.Get(),
        D2D1::RoundedRect(D2D1::RectF(8.0F, 8.0F, 88.0F, 56.0F), 12.0F, 12.0F),
        surface.Get());
    Check(target->GetAntialiasMode() == D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
          "outer chrome paint restores the caller antialias mode");
    target->SetTransform(D2D1::Matrix3x2F::Identity());
    Check(SUCCEEDED(target->EndDraw()), "color-key frame draw completes");

    WICRect area{0, 0, static_cast<INT>(width), static_cast<INT>(height)};
    ComPtr<IWICBitmapLock> lock;
    Check(SUCCEEDED(bitmap->Lock(
              &area, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())),
          "color-key frame pixels are readable");
    UINT stride{};
    UINT byteCount{};
    BYTE* bytes{};
    Check(SUCCEEDED(lock->GetStride(&stride)) &&
              SUCCEEDED(lock->GetDataPointer(&byteCount, &bytes)) && bytes,
          "color-key pixel buffer is available");

    const auto pixel = [&](const UINT x, const UINT y) {
        const auto* value = bytes + y * stride + x * 4U;
        return std::array<BYTE, 4>{value[0], value[1], value[2], value[3]};
    };
    const auto key = pixel(0, 0);
    const auto inside = pixel(
        static_cast<UINT>(48.0F * scale),
        static_cast<UINT>(32.0F * scale));
    Check(key != inside, "rounded shell has distinct key and surface colors");
    Check(pixel(static_cast<UINT>(8.0F * scale),
                static_cast<UINT>(8.0F * scale)) == key,
          "rounded outer corner remains exactly color-key transparent");

    std::size_t blended{};
    std::size_t keyPixels{};
    std::size_t surfacePixels{};
    for (UINT y = 0; y < height; ++y) {
        for (UINT x = 0; x < width; ++x) {
            const auto value = pixel(x, y);
            if (value == key) ++keyPixels;
            else if (value == inside) ++surfacePixels;
            else ++blended;
        }
    }
    Check(keyPixels != 0 && surfacePixels != 0,
          "frame retains both transparent canvas and authored chrome");
    Check(blended == 0,
          "color-key boundary contains no opaque antialias fringe pixels");
}

void CheckPremultipliedFrame(
    ID2D1Factory* d2d,
    IWICImagingFactory* wic,
    const float scale) {
    constexpr UINT width = 144;
    constexpr UINT height = 96;
    ComPtr<IWICBitmap> bitmap;
    Check(SUCCEEDED(wic->CreateBitmap(
              width, height, GUID_WICPixelFormat32bppPBGRA,
              WICBitmapCacheOnLoad, bitmap.ReleaseAndGetAddressOf())),
          "WIC premultiplied-alpha frame is created");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
              bitmap.Get(), D2D1::RenderTargetProperties(),
              target.ReleaseAndGetAddressOf())),
          "Direct2D premultiplied-alpha target is created");
    ComPtr<ID2D1SolidColorBrush> surface;
    Check(SUCCEEDED(target->CreateSolidColorBrush(
              D2D1::ColorF(0x2D / 255.0F, 0x35 / 255.0F, 0x48 / 255.0F, 1.0F),
              surface.ReleaseAndGetAddressOf())),
          "premultiplied shell surface brush is created");

    target->BeginDraw();
    target->Clear(D2D1::ColorF(0.0F, 0.0F, 0.0F, 0.0F));
    target->SetTransform(D2D1::Matrix3x2F::Scale(scale, scale));
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    gba::shell::FillColorKeyRoundedRectangle(
        target.Get(),
        D2D1::RoundedRect(D2D1::RectF(8.0F, 8.0F, 88.0F, 56.0F), 12.0F, 12.0F),
        surface.Get(), gba::shell::OuterChromeBoundary::PremultipliedAlpha);
    Check(target->GetAntialiasMode() == D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
          "premultiplied chrome retains antialiasing");
    target->SetTransform(D2D1::Matrix3x2F::Identity());
    Check(SUCCEEDED(target->EndDraw()), "premultiplied frame draw completes");

    WICRect area{0, 0, static_cast<INT>(width), static_cast<INT>(height)};
    ComPtr<IWICBitmapLock> lock;
    Check(SUCCEEDED(bitmap->Lock(
              &area, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())),
          "premultiplied pixel buffer is readable");
    UINT stride{};
    UINT byteCount{};
    BYTE* bytes{};
    Check(SUCCEEDED(lock->GetStride(&stride)) &&
              SUCCEEDED(lock->GetDataPointer(&byteCount, &bytes)) && bytes,
          "premultiplied pixel bytes are available");
    const auto alphaAt = [&](const UINT x, const UINT y) {
        return bytes[y * stride + x * 4U + 3U];
    };
    Check(alphaAt(0, 0) == 0,
          "unused composition client remains genuinely transparent");
    Check(alphaAt(static_cast<UINT>(48.0F * scale),
                  static_cast<UINT>(32.0F * scale)) == 255,
          "authored chrome remains fully opaque");
    std::size_t transparent{};
    std::size_t opaque{};
    std::size_t antialiased{};
    for (UINT y = 0; y < height; ++y) {
        for (UINT x = 0; x < width; ++x) {
            const BYTE alpha = alphaAt(x, y);
            if (alpha == 0) ++transparent;
            else if (alpha == 255) ++opaque;
            else ++antialiased;
        }
    }
    Check(transparent != 0 && opaque != 0 && antialiased != 0,
          "premultiplied frame retains transparent, authored, and rounded edge pixels");
}

void CheckRetainedTrayInvalidation() {
    gba::shell::RetainedTrayState initial{
        420, 84, 7,
        {
            {{8, 8, 68, 68}, L"settings", true, true},
            {{76, 8, 136, 68}, L"network", false, false},
            {{144, 8, 204, 68}, L"audio", false, false},
        },
    };
    Check(gba::shell::RequiresTrayRepaint(nullptr, initial),
          "first tray frame rebuilds its retained child surface");
    Check(!gba::shell::RequiresTrayRepaint(&initial, initial),
          "content-only publication retains tray pixels");

    auto selected = initial;
    selected.items[0].selected = false;
    selected.items[0].focused = false;
    selected.items[1].selected = true;
    selected.items[1].focused = true;
    Check(gba::shell::RequiresTrayRepaint(&initial, selected),
          "selection replaces the complete retained tray surface");

    auto reordered = selected;
    std::swap(reordered.items[1].identity, reordered.items[2].identity);
    Check(gba::shell::RequiresTrayRepaint(&selected, reordered),
          "reorder replaces the complete retained tray surface");

    auto provider = reordered;
    Check(!gba::shell::RequiresTrayRepaint(&reordered, provider),
          "provider, slider, scroll, focus, and motion retain unchanged tray pixels");

    auto appearance = provider;
    ++appearance.appearanceRevision;
    Check(gba::shell::RequiresTrayRepaint(&provider, appearance),
          "appearance revision rebuilds the tray child surface");
}

struct FixedChromePointerTestContext final {
    HWND content{};
    gba::OverlayState state{
        {}, {L"settings", L"network", L"audio"}};
    gba::shell::TrayLayout layout;
    unsigned int activationCount{};
};

void ActivateFixedChromeTray(
    void* opaque, const float x, const float y) noexcept {
    auto& context = *static_cast<FixedChromePointerTestContext*>(opaque);
    const auto* hit = gba::shell::HitTestTray(context.layout, x, y);
    if (!hit || hit->slot >= context.state.order().size()) return;
    ++context.activationCount;
    const auto& widget = context.state.order()[hit->slot];
    if (context.state.TrySelectTrayWidget(widget) &&
        context.state.selectedSlot() == hit->slot) {
        (void)context.state.Dispatch(gba::Command::Activate);
    }
}

LRESULT CALLBACK FixedChromeTestWindowProc(
    HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_NCCREATE) {
        const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(
            window, GWLP_USERDATA,
            reinterpret_cast<LONG_PTR>(create->lpCreateParams));
    } else if (message == WM_LBUTTONUP) {
        auto* context = reinterpret_cast<FixedChromePointerTestContext*>(
            GetWindowLongPtrW(window, GWLP_USERDATA));
        if (context) {
            (void)gba::shell::RouteFixedChromePointerRelease(
                window, context->content, lParam, context,
                ActivateFixedChromeTray);
            return 0;
        }
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

bool RejectChromeTarget(
    gba::OverlayCompositionSurface& surface,
    HWND,
    std::wstring& error) {
    Check(surface.available(),
          "content target exists when deterministic chrome-target failure occurs");
    error = L"deterministic second-target failure";
    return false;
}

void CheckFixedChromeWindowPolicy() {
    Check(gba::shell::FixedChromeWindowStyle() == WS_POPUP,
          "fixed chrome is a popup endpoint");
    const auto expectedExStyle = WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP |
        WS_EX_NOACTIVATE | WS_EX_TOPMOST;
    Check(gba::shell::FixedChromeWindowExStyle() == expectedExStyle,
          "fixed chrome is non-activating, topmost, and absent from task switching");

    const RECT oddWork{0, 0, 2185, 1400};
    const RECT evenWork{0, 0, 2186, 1400};
    const auto odd747 = gba::shell::ComputeFixedChromeWindowBounds(oddWork, 747, 141);
    const auto odd748 = gba::shell::ComputeFixedChromeWindowBounds(oddWork, 748, 141);
    const auto even747 = gba::shell::ComputeFixedChromeWindowBounds(evenWork, 747, 141);
    const auto even748 = gba::shell::ComputeFixedChromeWindowBounds(evenWork, 748, 141);
    Check(odd747.right - odd747.left == 747 && odd748.right - odd748.left == 748 &&
              even747.right - even747.left == 747 && even748.right - even748.left == 748,
          "fractional-DPI parity keeps the authored physical chrome width exact");
    Check(odd747.bottom == oddWork.bottom && odd748.bottom == oddWork.bottom &&
              even747.bottom == evenWork.bottom && even748.bottom == evenWork.bottom,
          "odd and even work areas retain one physical bottom anchor");
    const gba::shell::FixedChromeSessionKey session{
        oddWork, 144, 1.0, 7, {L"settings", L"network"}};
    Check(gba::shell::SameFixedChromeSession(session, session),
          "equal applied work-area and catalog authority reuses fixed chrome");
    auto changedWork = session;
    changedWork.workArea = {100, 0, 2285, 1400};
    auto changedCatalog = session;
    changedCatalog.catalogOrder.push_back(L"audio");
    auto changedScale = session;
    changedScale.interfaceScale = 1.25;
    auto changedAppearance = session;
    ++changedAppearance.appearanceRevision;
    Check(!gba::shell::SameFixedChromeSession(session, changedWork) &&
              !gba::shell::SameFixedChromeSession(session, changedCatalog) &&
              !gba::shell::SameFixedChromeSession(session, changedScale) &&
              !gba::shell::SameFixedChromeSession(session, changedAppearance),
          "work-area, catalog, scale, and appearance changes rebuild fixed chrome");

    const wchar_t className[] = L"WidgetRail.FixedChromePolicyTests";
    WNDCLASSW windowClass{};
    windowClass.lpfnWndProc = FixedChromeTestWindowProc;
    windowClass.hInstance = GetModuleHandleW(nullptr);
    windowClass.lpszClassName = className;
    Check(RegisterClassW(&windowClass) != 0, "fixed chrome test class registers");
    FixedChromePointerTestContext pointerContext;
    HWND content = CreateWindowExW(
        WS_EX_TOOLWINDOW, className, L"content", WS_POPUP,
        640, 600, 1000, 500, nullptr, nullptr, windowClass.hInstance, nullptr);
    pointerContext.content = content;
    HWND chrome = CreateWindowExW(
        gba::shell::FixedChromeWindowExStyle(), className, L"chrome",
        gba::shell::FixedChromeWindowStyle(), 0, 0, 1, 1,
        nullptr, nullptr, windowClass.hInstance, &pointerContext);
    Check(content && chrome, "content and chrome policy HWNDs are created");
    ShowWindow(content, SW_SHOWNOACTIVATE);
    const RECT fixed{720, 900, 1468, 1041};
    Check(gba::shell::ApplyFixedChromeWindow(content, chrome, fixed, true),
          "production policy applies the owned chrome rectangle");
    Check(GetWindow(chrome, GW_OWNER) == content,
          "chrome HWND has the content session window as its Win32 owner");
    const auto chromeAboveContent = [&] {
        for (HWND candidate = GetWindow(content, GW_HWNDPREV);
             candidate; candidate = GetWindow(candidate, GW_HWNDPREV)) {
            if (candidate == chrome) return true;
        }
        return false;
    };
    Check(chromeAboveContent(),
          "owned chrome remains above its content owner in the real Z order");
    Check(IsWindowVisible(content) && IsWindowVisible(chrome),
          "owned content and chrome endpoints are visible as one session");
    const auto actualExStyle = static_cast<DWORD>(GetWindowLongPtrW(chrome, GWL_EXSTYLE));
    Check((actualExStyle & expectedExStyle) == expectedExStyle,
          "applied chrome retains nonactivation and taskbar policy");
    RECT actual{};
    Check(GetWindowRect(chrome, &actual) && EqualRect(&actual, &fixed),
          "applied chrome rectangle equals the external Win32 rectangle");

    constexpr std::array<SIZE, 8> contentExtents{{
        {592, 698}, {632, 878}, {880, 520}, {760, 440},
        {1180, 700}, {520, 360}, {978, 466}, {1280, 720},
    }};
    for (const auto direction : {1, -1}) {
        for (int step = 0; step < static_cast<int>(contentExtents.size()); ++step) {
            const int index = direction > 0 ? step :
                static_cast<int>(contentExtents.size()) - 1 - step;
            const auto extent = contentExtents[static_cast<std::size_t>(index)];
            Check(SetWindowPos(content, HWND_TOPMOST, 40 + step, 50 + step,
                               extent.cx, extent.cy, SWP_NOACTIVATE) != FALSE,
                  "content endpoint accepts a distinct authored extent");
            RECT retained{};
            Check(GetWindowRect(chrome, &retained) && EqualRect(&retained, &fixed) &&
                      chromeAboveContent(),
                  "all eight forward and reverse content extents retain chrome corners");
        }
    }
    ShowWindow(content, SW_HIDE);
    Check(gba::shell::ApplyFixedChromeWindow(content, chrome, fixed, false) &&
              !IsWindowVisible(content) && !IsWindowVisible(chrome),
          "paired hide removes the chrome endpoint");
    ShowWindow(content, SW_SHOWNOACTIVATE);
    Check(gba::shell::ApplyFixedChromeWindow(content, chrome, fixed, true) &&
              GetWindowRect(chrome, &actual) && EqualRect(&actual, &fixed),
          "reopen with equal inputs restores identical chrome corners");
    const RECT guide{850, 900, 1338, 950};
    const RECT tray{720, 950, 1468, 1041};
    Check(gba::shell::IsFixedChromeHit({900, 920}, guide, tray) &&
              gba::shell::IsFixedChromeHit({800, 1000}, guide, tray) &&
              !gba::shell::IsFixedChromeHit({721, 901}, guide, tray),
          "chrome hit testing admits guide/tray and passes transparent gaps through");

    Check(SetWindowPos(
              content, HWND_TOPMOST, 640, 600, 1000, 500,
              SWP_NOACTIVATE) != FALSE,
          "content endpoint is placed for the applied chrome pointer route");
    Check(pointerContext.state.Dispatch(gba::Command::ToggleOverlay),
          "existing overlay state activation owner becomes visible");
    const auto trayLayout = gba::shell::ComputeTrayLayout(
        1000.0F, 500.0F, pointerContext.state.order().size(),
        pointerContext.state.selectedSlot(), gba::shell::TrayBand{300.0F, 400.0F});
    Check(trayLayout.has_value() && trayLayout->tiles.size() == 3,
          "production tray layout exposes the target tile");
    pointerContext.layout = *trayLayout;
    const auto& targetTile = pointerContext.layout.tiles[1].bounds;
    POINT release{
        static_cast<LONG>(targetTile.x + targetTile.width * 0.5F),
        static_cast<LONG>(targetTile.y + targetTile.height * 0.5F),
    };
    Check(ClientToScreen(content, &release) && ScreenToClient(chrome, &release),
          "target tile center maps into the applied chrome HWND");
    SendMessageW(
        chrome, WM_LBUTTONUP, 0,
        MAKELPARAM(static_cast<short>(release.x), static_cast<short>(release.y)));
    Check(pointerContext.activationCount == 1 &&
              pointerContext.state.selectedWidget() == L"network" &&
              pointerContext.state.activeWidget() == L"network" &&
              pointerContext.state.focusRegion() == gba::FocusRegion::Widget,
          "real chrome HWND release maps once through the existing tray selection and activation owner");

    ComPtr<ID2D1Factory1> compositionFactory;
    Check(SUCCEEDED(D2D1CreateFactory(
              D2D1_FACTORY_TYPE_SINGLE_THREADED,
              IID_PPV_ARGS(compositionFactory.ReleaseAndGetAddressOf()))),
          "Direct2D factory is created for paired endpoint recovery");
    gba::OverlayCompositionSurface composition;
    std::wstring compositionError;
    ShowWindow(chrome, SW_SHOWNOACTIVATE);
    Check(!gba::shell::InitializeFixedChromeComposition(
              composition, content, chrome, compositionFactory.Get(),
              compositionError, RejectChromeTarget) &&
              !composition.available() && !IsWindowVisible(chrome) &&
              compositionError == L"deterministic second-target failure",
          "second-target initialization failure resets the half-session and hides chrome");
    Check(gba::shell::InitializeFixedChromeComposition(
              composition, content, chrome, compositionFactory.Get(),
              compositionError),
          "real paired composition endpoints initialize for runtime recovery");
    ShowWindow(chrome, SW_SHOWNOACTIVATE);
    gba::shell::ResetFixedChromeComposition(composition, chrome);
    Check(!composition.available() && !IsWindowVisible(chrome),
          "runtime composition or device failure resets and hides both endpoints");

    DestroyWindow(content);
    Check(!IsWindow(content) && !IsWindow(chrome),
          "normal owner close destroys the fixed chrome endpoint");
    UnregisterClassW(className, windowClass.hInstance);
}

} // namespace

int main() {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "COM initializes for WIC");
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
              D2D1_FACTORY_TYPE_SINGLE_THREADED,
              d2d.ReleaseAndGetAddressOf())),
          "Direct2D factory is created");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
              CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
              IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
          "WIC factory is created");

    CheckFrame(d2d.Get(), wic.Get(), 1.0F);
    CheckFrame(d2d.Get(), wic.Get(), 1.5F);
    CheckPremultipliedFrame(d2d.Get(), wic.Get(), 1.0F);
    CheckPremultipliedFrame(d2d.Get(), wic.Get(), 1.5F);
    CheckCompositionCoordinatePolicies();
    CheckRetainedTrayInvalidation();
    CheckFixedChromeWindowPolicy();
    gba::shell::FillColorKeyRoundedRectangle(nullptr, {}, nullptr);

    wic.Reset();
    d2d.Reset();
    CoUninitialize();
    std::cout << "OverlayChromeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
