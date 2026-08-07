#include "NativeIcons.h"

#include <array>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <string_view>
#include <utility>

#include <d2d1helper.h>
#include <objbase.h>
#include <wincodec.h>

namespace {

using gba::icons::DrawNativeIcon;
using gba::icons::NativeIcon;
using gba::icons::TryParseNativeIcon;

int checks = 0;

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

template <typename Interface>
void Release(Interface*& value) noexcept {
    if (value != nullptr) {
        value->Release();
        value = nullptr;
    }
}

void ClosedSemanticIds() {
    using Pair = std::pair<std::wstring_view, NativeIcon>;
    constexpr std::array expected{
        Pair{L"music", NativeIcon::Music}, Pair{L"play", NativeIcon::Play},
        Pair{L"pause", NativeIcon::Pause}, Pair{L"previous", NativeIcon::Previous},
        Pair{L"next", NativeIcon::Next}, Pair{L"refresh", NativeIcon::Refresh},
        Pair{L"shuffle", NativeIcon::Shuffle}, Pair{L"like", NativeIcon::Like},
        Pair{L"dislike", NativeIcon::Dislike}, Pair{L"repeat", NativeIcon::Repeat},
        Pair{L"settings", NativeIcon::Settings}, Pair{L"warning", NativeIcon::Warning},
        Pair{L"check", NativeIcon::Check}, Pair{L"connection", NativeIcon::Connection},
    };
    for (const auto& [name, expectedIcon] : expected) {
        NativeIcon parsed = NativeIcon::Warning;
        Check(TryParseNativeIcon(name, parsed), "protocol glyph parses");
        Check(parsed == expectedIcon, "protocol glyph maps to matching enum");
    }

    constexpr std::array aliases{
        Pair{L"toggle-playback", NativeIcon::Play}, Pair{L"play-pause", NativeIcon::Play},
        Pair{L"previous-track", NativeIcon::Previous}, Pair{L"next-track", NativeIcon::Next},
        Pair{L"retry", NativeIcon::Refresh}, Pair{L"repeat-mode", NativeIcon::Repeat},
        Pair{L"connect", NativeIcon::Connection}, Pair{L"pair", NativeIcon::Connection},
    };
    for (const auto& [name, expectedIcon] : aliases) {
        NativeIcon parsed = NativeIcon::Warning;
        Check(TryParseNativeIcon(name, parsed), "closed transport alias parses");
        Check(parsed == expectedIcon, "transport alias maps to fixed semantic enum");
    }

    NativeIcon unchanged = NativeIcon::Check;
    Check(!TryParseNativeIcon(L"custom-svg-path", unchanged), "unknown glyph fails closed");
    Check(unchanged == NativeIcon::Check, "failed parsing leaves destination unchanged");
    Check(!TryParseNativeIcon(L"Music", unchanged), "glyph parsing is ordinal and case-sensitive");
    Check(!TryParseNativeIcon(L"", unchanged), "empty glyph fails closed");
}

void RenderEveryIcon() {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    Check(SUCCEEDED(initialized), "COM initializes");

    IWICImagingFactory* imagingFactory = nullptr;
    IWICBitmap* bitmap = nullptr;
    ID2D1Factory* d2dFactory = nullptr;
    ID2D1RenderTarget* target = nullptr;
    ID2D1SolidColorBrush* brush = nullptr;

    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&imagingFactory))), "WIC factory creates");
    Check(SUCCEEDED(imagingFactory->CreateBitmap(
        64, 64, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &bitmap)),
        "WIC bitmap creates");
    Check(SUCCEEDED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2dFactory)),
        "Direct2D factory creates");
    const auto properties = D2D1::RenderTargetProperties(
        D2D1_RENDER_TARGET_TYPE_SOFTWARE,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
    Check(SUCCEEDED(d2dFactory->CreateWicBitmapRenderTarget(bitmap, properties, &target)),
        "WIC render target creates");
    Check(SUCCEEDED(target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White), &brush)),
        "icon brush creates");

    constexpr std::array icons{
        NativeIcon::Music, NativeIcon::Play, NativeIcon::Pause,
        NativeIcon::Previous, NativeIcon::Next, NativeIcon::Refresh,
        NativeIcon::Shuffle, NativeIcon::Like, NativeIcon::Dislike,
        NativeIcon::Repeat, NativeIcon::Settings, NativeIcon::Warning,
        NativeIcon::Check, NativeIcon::Connection,
    };
    for (const NativeIcon icon : icons) {
        target->BeginDraw();
        target->Clear(D2D1::ColorF(0, 0.0F));
        Check(DrawNativeIcon(target, icon, D2D1::RectF(4.0F, 4.0F, 60.0F, 60.0F), brush, 2.4F),
            "known icon renders");
        Check(SUCCEEDED(target->EndDraw()), "icon draw completes");

        IWICBitmapLock* lock = nullptr;
        const WICRect lockArea{0, 0, 64, 64};
        Check(SUCCEEDED(bitmap->Lock(&lockArea, WICBitmapLockRead, &lock)), "rendered bitmap locks");
        UINT byteCount = 0;
        BYTE* bytes = nullptr;
        Check(SUCCEEDED(lock->GetDataPointer(&byteCount, &bytes)), "rendered bytes are accessible");
        bool anyVisiblePixel = false;
        for (UINT offset = 3; offset < byteCount; offset += 4) {
            if (bytes[offset] != 0) {
                anyVisiblePixel = true;
                break;
            }
        }
        Check(anyVisiblePixel, "icon produces visible vector pixels");
        Release(lock);
    }

    const auto nan = std::numeric_limits<float>::quiet_NaN();
    Check(!DrawNativeIcon(target, NativeIcon::Check, D2D1::RectF(nan, 0, 32, 32), brush),
        "non-finite bounds fail closed");
    Check(!DrawNativeIcon(target, NativeIcon::Check, D2D1::RectF(32, 0, 0, 32), brush),
        "reversed bounds fail closed");
    Check(!DrawNativeIcon(target, NativeIcon::Check, D2D1::RectF(0, 0, 5000, 32), brush),
        "oversized bounds fail closed");
    Check(!DrawNativeIcon(target, NativeIcon::Check, D2D1::RectF(0, 0, 32, 32), brush, nan),
        "non-finite stroke fails closed");
    Check(!DrawNativeIcon(target, static_cast<NativeIcon>(255), D2D1::RectF(0, 0, 32, 32), brush),
        "unknown enum fails closed");
    Check(!DrawNativeIcon(target, NativeIcon::Check, D2D1::Point2F(nan, 0), 32, brush),
        "non-finite center fails closed");

    Release(brush);
    Release(target);
    Release(d2dFactory);
    Release(bitmap);
    Release(imagingFactory);
    CoUninitialize();
}

} // namespace

int main() {
    ClosedSemanticIds();
    RenderEveryIcon();
    std::cout << "NativeIconsTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
