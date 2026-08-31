#include "DeclarativeRenderer.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"

#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <iostream>
#include <chrono>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {

void Require(const bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

void WaitForState(
    const widgetrail::RemoteImageCache& cache,
    const std::wstring_view key,
    const widgetrail::RemoteImageState expected,
    const char* message) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (std::chrono::steady_clock::now() < deadline) {
        if (cache.GetState(key) == expected) return;
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    Require(false, message);
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
        auto parsed = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot": {
                "protocolVersion":39,
                "sequence":1,
                "widgetInstanceId":"background-surface-test.instance",
                "activeInputScopeId":"background-surface-test.root",
                "initialFocusId":"background-surface-test.first",
                "root": {
                    "id":"background-surface-test.root",
                    "kind":"backgroundSurface",
                    "artworkHandle":"background-surface-test.artwork",
                    "imageFit":"cover",
                    "usesFocusedDescendantArtwork":true,
                    "children":[{
                        "id":"background-surface-test.foreground",
                        "kind":"stack",
                        "children":[{
                            "id":"background-surface-test.first",
                            "kind":"button",
                            "text":"First background",
                            "actionId":"background-surface-test.first",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.first",
                            "children":[]
                        },{
                            "id":"background-surface-test.second",
                            "kind":"button",
                            "text":"Second background",
                            "actionId":"background-surface-test.second",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.second",
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
        options.artworkAuthorityId = L"widgetrail.tests.background-surface\x1fruntime\x1fpresentation";
        options.surfaceBackground = {0.0F, 0.0F, 0.0F, 0.0F};
        const widgetrail::declarative::Rect viewport{0.0F, 0.0F, 640.0F, 420.0F};
        const auto render = [&](const std::wstring_view focusedId) {
            target->BeginDraw();
            auto result = renderer.Render(target.Get(), *parsed, focusedId, viewport, options);
            Require(SUCCEEDED(target->EndDraw()), "focused background render failed");
            Require(result.succeeded, "focused background render did not succeed");
            return result;
        };
        const auto defaultKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.artwork");
        const auto firstKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.first");
        const auto secondKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.second");
        constexpr std::wstring_view png =
            L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
            L"AAAADUlEQVR42mNk+M/wHwAF/gL+Xh8ftQAAAABJRU5ErkJggg==";

        // First establish the authored default as the last committed image.
        parsed->root.children[0].children[0].focusBackgroundArtworkHandle.clear();
        (void)render(L"background-surface-test.first");
        Require(requests.size() == 1U && requests.front() == defaultKey,
            "ordinary background render changed exact default artwork authority");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.artwork", L"image/png", std::wstring(png)),
            "default artwork completion was rejected");
        WaitForState(cache, defaultKey, widgetrail::RemoteImageState::Ready,
            "default artwork did not become ready");
        const auto defaultFrame = render(L"background-surface-test.first");
        Require(defaultFrame.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "default background did not become the committed image");

        parsed->root.children[0].children[0].focusBackgroundArtworkHandle =
            L"background-surface-test.focus.first";
        const auto firstPending = render(L"background-surface-test.first");
        Require(requests.size() == 2U && requests.back() == firstKey,
            "focused background did not request the exact focused handle");
        Require(firstPending.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "pending focused artwork blanked the last committed image");
        Require(cache.FailTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.first"),
            "focused artwork failure was not admitted");
        const auto firstFailed = render(L"background-surface-test.first");
        Require(firstFailed.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "failed focused artwork blanked the last committed image");

        const auto plan = renderer.PlanFocusUpdate(
            *parsed, L"background-surface-test.first",
            L"background-surface-test.second", viewport);
        Require(plan && plan->work == widgetrail::IncrementalPresentationWork::PaintOnly,
            "focus-background transition did not remain paint-only");
        const auto secondPending = render(L"background-surface-test.second");
        Require(requests.size() == 3U && requests.back() == secondKey,
            "second focus did not request its exact artwork handle once");
        Require(secondPending.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "second pending focus did not retain the committed image");

        // An older focus completion may enter the bounded cache, but cannot
        // overwrite the exact current focus selection.
        Require(!cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.first", L"image/png", std::wstring(png)),
            "failed older focus unexpectedly accepted a replacement completion");
        const auto afterLateFirst = render(L"background-surface-test.second");
        Require(afterLateFirst.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "older focus completion overwrote the current pending selection");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.second", L"image/png", std::wstring(png)),
            "current focused artwork completion was rejected");
        WaitForState(cache, secondKey, widgetrail::RemoteImageState::Ready,
            "current focused artwork did not become ready");
        const auto secondReady = render(L"background-surface-test.second");
        Require(secondReady.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.focus.second",
            "current focused artwork did not atomically replace the retained image");

        std::wstring nestedError;
        const auto nested = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":2,
            "widgetInstanceId":"background.nested","activeInputScopeId":"nested.outer",
            "initialFocusId":"nested.action","root":{"id":"nested.outer","kind":"backgroundSurface",
            "artworkHandle":"nested.outer.default","imageFit":"cover",
            "usesFocusedDescendantArtwork":true,
            "children":[{"id":"nested.inner","kind":"backgroundSurface",
            "artworkHandle":"nested.inner.default","imageFit":"cover",
            "usesFocusedDescendantArtwork":true,
            "children":[{"id":"nested.action","kind":"button","text":"Nested action",
            "actionId":"nested.action","focusBackgroundArtworkHandle":"nested.focus",
            "children":[]}]}]}}
        })json", nestedError);
        Require(nested.has_value() && nestedError.empty(),
            "native parser rejected nested focused-background ownership");
        const auto requestCountBeforeNested = requests.size();
        target->BeginDraw();
        const auto nestedFrame = renderer.Render(
            target.Get(), *nested, L"nested.action", viewport, options);
        Require(SUCCEEDED(target->EndDraw()) && nestedFrame.succeeded,
            "nested focused-background render failed");
        Require(requests.size() == requestCountBeforeNested + 2U,
            "nested focus did not preserve two independent surface owners");
        Require(requests[requestCountBeforeNested] ==
                widgetrail::RemoteImageCache::TrustedArtworkKey(
                    L"widgetrail.tests.background-surface", L"nested.outer",
                    L"nested.outer.default") &&
            requests[requestCountBeforeNested + 1U] ==
                widgetrail::RemoteImageCache::TrustedArtworkKey(
                    L"widgetrail.tests.background-surface", L"nested.inner",
                    L"nested.focus"),
            "nested BackgroundSurface did not form a hard focus-artwork boundary");

        std::wstring v38Error;
        const auto rejectedV38 = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":38,"sequence":1,
            "widgetInstanceId":"background.v38","activeInputScopeId":"surface",
            "initialFocusId":"action","root":{"id":"surface","kind":"backgroundSurface",
            "usesFocusedDescendantArtwork":true,"children":[{"id":"action","kind":"button",
            "text":"Action","actionId":"action","focusBackgroundArtworkHandle":"artwork.focus",
            "children":[]}]}}
        })json", v38Error);
        Require(!rejectedV38.has_value(),
            "native parser admitted focused backgrounds below protocol v39");
        std::wstring emptyHandleError;
        Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":1,
            "widgetInstanceId":"background.empty","activeInputScopeId":"root",
            "initialFocusId":"action","root":{"id":"root","kind":"stack",
            "children":[{"id":"action","kind":"button","text":"Action",
            "actionId":"action","focusBackgroundArtworkHandle":"","children":[]}]}}
        })json", emptyHandleError),
            "native parser admitted an empty focused-background handle");
        std::wstring wrongOwnerError;
        Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":1,
            "widgetInstanceId":"background.owner","activeInputScopeId":"root",
            "initialFocusId":"action","root":{"id":"root","kind":"stack",
            "usesFocusedDescendantArtwork":false,"children":[{"id":"action",
            "kind":"button","text":"Action","actionId":"action","children":[]}]}}
        })json", wrongOwnerError),
            "native parser admitted focused-background ownership on a non-surface");
        cache.Shutdown();
        std::cout << "BackgroundSurface host tests: 10/10 passed.\n";
        }
        if (SUCCEEDED(initialized)) CoUninitialize();
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "FAIL: " << exception.what() << '\n';
        return 1;
    }
}
