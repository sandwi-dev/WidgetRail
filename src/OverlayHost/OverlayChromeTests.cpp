#include "OverlayChrome.h"

#include <Windows.h>
#include <d2d1.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <array>
#include <cstdlib>
#include <iostream>

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
    gba::shell::FillColorKeyRoundedRectangle(nullptr, {}, nullptr);

    wic.Reset();
    d2d.Reset();
    CoUninitialize();
    std::cout << "OverlayChromeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
