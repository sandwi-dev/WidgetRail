#include "DeclarativeRenderer.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"

#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

void Require(const bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

} // namespace

int wmain() {
    using Microsoft::WRL::ComPtr;
    try {
        const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        Require(SUCCEEDED(initialized) || initialized == RPC_E_CHANGED_MODE,
            "COM initialization failed");

        {
        std::wstring error;
        const auto parsed = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot": {
                "protocolVersion":38,
                "sequence":1,
                "widgetInstanceId":"background-surface-test.instance",
                "activeInputScopeId":"background-surface-test.root",
                "initialFocusId":"background-surface-test.action",
                "root": {
                    "id":"background-surface-test.root",
                    "kind":"backgroundSurface",
                    "artworkHandle":"background-surface-test.artwork",
                    "imageFit":"cover",
                    "children":[{
                        "id":"background-surface-test.foreground",
                        "kind":"stack",
                        "children":[{
                            "id":"background-surface-test.action",
                            "kind":"button",
                            "text":"Foreground action",
                            "actionId":"background-surface-test.activate",
                            "children":[]
                        }]
                    }]
                }
            }
        })json", error);
        Require(parsed.has_value() && error.empty(),
            "production native snapshot parser rejected the package projection");

        ComPtr<ID2D1Factory> d2d;
        Require(SUCCEEDED(D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
            "D2D factory creation failed");
        ComPtr<IDWriteFactory> write;
        Require(SUCCEEDED(DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
            "DirectWrite factory creation failed");
        ComPtr<IWICImagingFactory> wic;
        Require(SUCCEEDED(CoCreateInstance(
            CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
            "WIC factory creation failed");
        ComPtr<IWICBitmap> canvas;
        Require(SUCCEEDED(wic->CreateBitmap(
            640, 420, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
            "WIC canvas creation failed");
        ComPtr<ID2D1RenderTarget> target;
        Require(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
            canvas.Get(), D2D1::RenderTargetProperties(),
            target.ReleaseAndGetAddressOf())),
            "render-target creation failed");

        std::vector<std::wstring> requests;
        widgetrail::RemoteImageCache cache(
            {}, {}, {},
            [&](const std::wstring_view key) {
                requests.emplace_back(key);
                return true;
            });
        widgetrail::DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};
        widgetrail::DeclarativeRenderOptions options;
        options.artworkWidgetId = L"widgetrail.tests.background-surface";
        options.surfaceBackground = {0.0F, 0.0F, 0.0F, 0.0F};
        target->BeginDraw();
        (void)renderer.Render(
            target.Get(), *parsed, L"background-surface-test.action",
            {0.0F, 0.0F, 640.0F, 420.0F}, options);
        Require(SUCCEEDED(target->EndDraw()), "macro-free production render failed");
        Require(requests.size() == 1U, "production render did not queue one artwork request");
        Require(requests.front() == widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.artwork"),
            "production render changed exact artwork request authority");
        cache.Shutdown();
        std::cout << "BackgroundSurface host tests: 1/1 passed.\n";
        }
        if (SUCCEEDED(initialized)) CoUninitialize();
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "FAIL: " << exception.what() << '\n';
        return 1;
    }
}
