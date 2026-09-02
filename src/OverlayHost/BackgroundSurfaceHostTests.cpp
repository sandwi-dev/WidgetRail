#include "DeclarativeRenderer.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"

#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
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

const std::wstring& RequireBackgroundHandle(
    const widgetrail::RenderResult& result,
    const std::wstring_view surfaceId,
    const char* const message) {
    const auto found = result.backgroundArtworkHandles.find(
        std::wstring{surfaceId});
    Require(found != result.backgroundArtworkHandles.end(), message);
    return found->second;
}

const widgetrail::declarative::Rect& RequireElementRect(
    const widgetrail::RenderResult& result,
    const std::wstring_view elementId,
    const char* const message) {
    const auto found = result.elementRects.find(std::wstring{elementId});
    Require(found != result.elementRects.end(), message);
    return found->second;
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
                        },{
                            "id":"background-surface-test.ordinary",
                            "kind":"button",
                            "text":"Ordinary action",
                            "actionId":"background-surface-test.ordinary",
                            "children":[]
                        },{
                            "id":"background-surface-test.supersession",
                            "kind":"button",
                            "text":"Superseding background",
                            "actionId":"background-surface-test.supersession",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.supersession",
                            "children":[]
                        },{
                            "id":"background-surface-test.replacement",
                            "kind":"button",
                            "text":"Replacement background",
                            "actionId":"background-surface-test.replacement",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.replacement",
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
        std::uint64_t frameTime = 1000;
        options.animationTimestampMilliseconds = frameTime;
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
        const auto replacementKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.replacement");
        const auto supersessionKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.supersession");
        const auto failedReplacementKey =
            widgetrail::RemoteImageCache::TrustedArtworkKey(
                L"widgetrail.tests.background-surface",
                L"background-surface-test.root",
                L"background-surface-test.focus.failure");
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

        // A replacement target owns a different Direct2D resource domain.
        // The renderer must retire the committed target-local bitmap before
        // the render pass copies transition state, then recreate it from the
        // bounded decoded cache for the exact current proposal.
        ComPtr<IWICBitmap> replacementCanvas;
        Require(SUCCEEDED(wic->CreateBitmap(
            640, 420, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad,
            replacementCanvas.ReleaseAndGetAddressOf())),
            "replacement WIC canvas creation failed");
        ComPtr<ID2D1RenderTarget> replacementTarget;
        Require(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
            replacementCanvas.Get(), D2D1::RenderTargetProperties(),
            replacementTarget.ReleaseAndGetAddressOf())),
            "replacement render-target creation failed");
        replacementTarget->BeginDraw();
        const auto replacementDomainFrame = renderer.Render(
            replacementTarget.Get(), *parsed,
            L"background-surface-test.first", viewport, options);
        Require(SUCCEEDED(replacementTarget->EndDraw()),
            "resource-domain replacement reused a prior-target background bitmap");
        Require(replacementDomainFrame.succeeded &&
                replacementDomainFrame.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                    L"background-surface-test.artwork",
            "resource-domain replacement did not recreate the exact committed background");

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
            "current focused artwork did not begin the bounded replacement");

        const auto supersessionPending =
            render(L"background-surface-test.supersession");
        Require(requests.size() == 4U && requests.back() == supersessionKey,
            "latest focus did not request its exact superseding proposal");
        Require(supersessionPending.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.artwork" &&
                std::any_of(
                    supersessionPending.diagnostics.begin(),
                    supersessionPending.diagnostics.end(),
                    [](const auto& diagnostic) {
                        return diagnostic.code ==
                            L"background_crossfade_supersession";
                    }),
            "A-to-B-to-pending-C did not retire B and retain committed A");

        // Re-enter B from the decoded cache, then deterministically cross the
        // complete transition interval before exercising the older
        // B-to-pending-C retention oracle below.
        (void)render(L"background-surface-test.second");
        frameTime += 200;
        options.animationTimestampMilliseconds = frameTime;
        const auto secondSettled = render(L"background-surface-test.second");
        Require(secondSettled.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                    L"background-surface-test.focus.second" &&
                !secondSettled.backgroundSurfaceAnimationDamage,
            "focused B did not become the exact committed image after 200 ms");

        const auto requestsBeforeOrdinary = requests.size();
        const auto ordinaryFrame = render(L"background-surface-test.ordinary");
        Require(requests.size() == requestsBeforeOrdinary,
            "ordinary focus requested a replacement background");
        Require(ordinaryFrame.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.focus.second",
            "ordinary focus did not retain the last ready focused artwork");

        const auto replacementPending = render(L"background-surface-test.replacement");
        Require(requests.size() == requestsBeforeOrdinary + 1U &&
                requests.back() == replacementKey,
            "subsequent focused background did not request its exact handle once");
        Require(replacementPending.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.second",
            "pending subsequent artwork did not retain the prior focused image");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.replacement", L"image/png", std::wstring(png)),
            "subsequent focused artwork completion was rejected");
        WaitForState(cache, replacementKey, widgetrail::RemoteImageState::Ready,
            "subsequent focused artwork did not become ready");
        const auto replacementReady = render(L"background-surface-test.replacement");
        Require(replacementReady.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.replacement",
            "subsequent ready artwork did not replace the retained image");

        // A target-backed frame may paint a ready replacement and still fail
        // on a later node. That rejected frame cannot publish its staged
        // replacement into the renderer's committed presentation state.
        (void)render(L"background-surface-test.second");
        options.failAfterNodeDrawForTesting = true;
        target->BeginDraw();
        const auto rejectedReplacement = renderer.Render(
            target.Get(), *parsed, L"background-surface-test.replacement",
            viewport, options);
        Require(SUCCEEDED(target->EndDraw()),
            "post-draw replacement rejection did not complete target drawing");
        Require(!rejectedReplacement.succeeded,
            "post-draw replacement fixture did not reject the frame");
        options.failAfterNodeDrawForTesting = false;
        const auto afterRejectedReplacement =
            render(L"background-surface-test.ordinary");
        Require(afterRejectedReplacement.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.second",
            "rejected ready replacement mutated committed background state");

        // Explicit clearing is transaction-owned too: a later render failure
        // must leave the exact prior committed focused background available.
        (void)render(L"background-surface-test.replacement");
        parsed->root.usesFocusedDescendantArtwork = false;
        options.failAfterNodeDrawForTesting = true;
        target->BeginDraw();
        const auto rejectedClear = renderer.Render(
            target.Get(), *parsed, L"background-surface-test.ordinary",
            viewport, options);
        Require(SUCCEEDED(target->EndDraw()),
            "post-draw clear rejection did not complete target drawing");
        Require(!rejectedClear.succeeded,
            "post-draw clear fixture did not reject the frame");
        options.failAfterNodeDrawForTesting = false;
        parsed->root.usesFocusedDescendantArtwork = true;
        const auto afterRejectedClear = render(L"background-surface-test.ordinary");
        Require(afterRejectedClear.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.replacement",
            "rejected explicit clear mutated committed background state");

        frameTime += 200;
        options.animationTimestampMilliseconds = frameTime;
        const auto replacementSettled =
            render(L"background-surface-test.replacement");
        Require(replacementSettled.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                    L"background-surface-test.focus.replacement" &&
                !replacementSettled.backgroundSurfaceAnimationDamage,
            "replacement C did not become committed after the full transition");

        parsed->root.children[0].children[0].focusBackgroundArtworkHandle =
            L"background-surface-test.focus.failure";
        const auto failedReplacementPending =
            render(L"background-surface-test.first");
        Require(requests.back() == failedReplacementKey,
            "failed replacement did not request its exact handle");
        Require(RequireBackgroundHandle(
                    failedReplacementPending,
                    L"background-surface-test.root",
                    "pending later override omitted the committed focused image") ==
                L"background-surface-test.focus.replacement",
            "pending later override did not retain the prior focused image");
        Require(cache.FailTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.failure"),
            "later focused artwork failure was not admitted");
        const auto failedReplacement = render(L"background-surface-test.first");
        Require(RequireBackgroundHandle(
                    failedReplacement,
                    L"background-surface-test.root",
                    "failed later override omitted the committed focused image") ==
                L"background-surface-test.focus.replacement",
            "failed later override replaced the prior focused image with default");
        (void)render(L"background-surface-test.replacement");

        const auto replacementPlan = renderer.PlanFocusUpdate(
            *parsed, L"background-surface-test.replacement",
            L"background-surface-test.ordinary", viewport);
        Require(replacementPlan &&
                replacementPlan->work == widgetrail::IncrementalPresentationWork::PaintOnly,
            "sparse focused-background transition did not remain paint-only");
        const auto replacementSurfaceBounds = RequireElementRect(
            replacementReady, L"background-surface-test.root",
            "ready replacement omitted BackgroundSurface geometry");
        Require(replacementPlan->damage.x <= replacementSurfaceBounds.x + 0.01F &&
                replacementPlan->damage.y <= replacementSurfaceBounds.y + 0.01F &&
                replacementPlan->damage.x + replacementPlan->damage.width >=
                    replacementSurfaceBounds.x + replacementSurfaceBounds.width - 0.01F &&
                replacementPlan->damage.y + replacementPlan->damage.height >=
                    replacementSurfaceBounds.y + replacementSurfaceBounds.height - 0.01F &&
                replacementPlan->damage.x >= viewport.x - 0.01F &&
                replacementPlan->damage.y >= viewport.y - 0.01F &&
                replacementPlan->damage.x + replacementPlan->damage.width <=
                    viewport.x + viewport.width + 0.01F &&
                replacementPlan->damage.y + replacementPlan->damage.height <=
                    viewport.y + viewport.height + 0.01F,
            "focused-background change did not contain exact full-surface damage");

        parsed->root.artworkHandle = L"background-surface-test.artwork.changed";
        const auto changedDefaultKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.artwork.changed");
        const auto changedDefaultPending = render(L"background-surface-test.ordinary");
        Require(requests.back() == changedDefaultKey &&
                !changedDefaultPending.backgroundArtworkHandles.contains(
                    L"background-surface-test.root"),
            "changed default did not reset the retained focused artwork");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.artwork.changed", L"image/png", std::wstring(png)),
            "changed default artwork completion was rejected");
        WaitForState(cache, changedDefaultKey, widgetrail::RemoteImageState::Ready,
            "changed default artwork did not become ready");
        const auto changedDefaultReady = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    changedDefaultReady,
                    L"background-surface-test.root",
                    "changed default omitted the reset surface artwork") ==
                L"background-surface-test.artwork.changed",
            "changed default did not initialize the reset surface");

        (void)render(L"background-surface-test.replacement");
        parsed->root.usesFocusedDescendantArtwork = false;
        (void)render(L"background-surface-test.ordinary");
        parsed->root.usesFocusedDescendantArtwork = true;
        const auto afterExplicitReset = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    afterExplicitReset,
                    L"background-surface-test.root",
                    "explicit reset omitted the authored default artwork") ==
                L"background-surface-test.artwork.changed",
            "explicit reset retained the prior focused override");

        (void)render(L"background-surface-test.replacement");
        const auto afterFocusAuthorityLoss = render(L"");
        Require(RequireBackgroundHandle(
                    afterFocusAuthorityLoss,
                    L"background-surface-test.root",
                    "focus loss omitted the committed focused background") ==
                L"background-surface-test.focus.replacement",
            "host-owned focus loss discarded the exact committed focused background");

        (void)render(L"background-surface-test.replacement");
        options.artworkAuthorityId =
            L"widgetrail.tests.background-surface\x1fruntime\x1fpresentation.changed";
        const auto changedAuthority = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    changedAuthority,
                    L"background-surface-test.root",
                    "new presentation authority omitted its default artwork") ==
                L"background-surface-test.artwork.changed",
            "new presentation authority inherited a prior focused override");
        options.artworkAuthorityId =
            L"widgetrail.tests.background-surface\x1fruntime\x1fpresentation";
        const auto priorAuthorityReentered =
            render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    priorAuthorityReentered,
                    L"background-surface-test.root",
                    "retired presentation re-entry omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "returning to a retired presentation authority resurrected its old override");

        (void)render(L"background-surface-test.replacement");
        renderer.ForgetWidgetState(parsed->instanceId);
        const auto afterRetirement = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    afterRetirement,
                    L"background-surface-test.root",
                    "widget retirement omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "widget retirement retained a focused override");

        (void)render(L"background-surface-test.replacement");
        const auto retainedSurface = parsed->root;
        parsed->root.kind = L"stack";
        parsed->root.imageSource.clear();
        parsed->root.artworkHandle.clear();
        parsed->root.usesFocusedDescendantArtwork = false;
        (void)render(L"background-surface-test.ordinary");
        parsed->root = retainedSurface;
        const auto afterSurfaceReadded = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    afterSurfaceReadded,
                    L"background-surface-test.root",
                    "re-added surface omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "removed and re-added surface resurrected its old focused override");

        auto responsiveSurface = parsed->root;
        responsiveSurface.visibleWhen = L"expandedOnly";
        auto compactAction = responsiveSurface.children[0].children[2];
        compactAction.id = L"background-surface-test.compact";
        compactAction.actionId = L"background-surface-test.compact";
        compactAction.visibleWhen = L"compactOnly";
        auto responsiveRoot = responsiveSurface;
        responsiveRoot.id = L"background-surface-test.responsive-root";
        responsiveRoot.kind = L"stack";
        responsiveRoot.imageSource.clear();
        responsiveRoot.artworkHandle.clear();
        responsiveRoot.imageFit.clear();
        responsiveRoot.usesFocusedDescendantArtwork = false;
        responsiveRoot.visibleWhen.clear();
        responsiveRoot.children = {responsiveSurface, compactAction};
        parsed->root = responsiveRoot;
        options.responsiveViewport = widgetrail::declarative::Size{960.0F, 540.0F};
        const auto responsiveOverride =
            render(L"background-surface-test.replacement");
        Require(RequireBackgroundHandle(
                    responsiveOverride,
                    L"background-surface-test.root",
                    "expanded surface omitted focused artwork") ==
                L"background-surface-test.focus.replacement",
            "expanded surface did not establish its ready focused override");
        options.responsiveViewport = widgetrail::declarative::Size{959.0F, 540.0F};
        (void)render(L"background-surface-test.compact");
        options.responsiveViewport = widgetrail::declarative::Size{960.0F, 540.0F};
        const auto responsiveReadded = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    responsiveReadded,
                    L"background-surface-test.root",
                    "responsive re-entry omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "responsive mode away and back resurrected an absent surface override");
        parsed->root = responsiveSurface;
        parsed->root.visibleWhen.clear();
        options.responsiveViewport.reset();

        std::wstring nestedError;
        auto nested = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":2,
            "widgetInstanceId":"background.nested","activeInputScopeId":"nested.outer",
            "initialFocusId":"nested.action","root":{"id":"nested.outer","kind":"backgroundSurface",
            "artworkHandle":"nested.outer.default","imageFit":"cover",
            "usesFocusedDescendantArtwork":true,
            "children":[{"id":"nested.content","kind":"stack","children":[
            {"id":"nested.inner","kind":"backgroundSurface",
            "artworkHandle":"nested.inner.default","imageFit":"cover",
            "usesFocusedDescendantArtwork":true,
            "children":[{"id":"nested.inner.content","kind":"stack","children":[
            {"id":"nested.action","kind":"button","text":"Nested action",
            "actionId":"nested.action","focusBackgroundArtworkHandle":"nested.focus",
            "children":[]},{"id":"nested.ordinary","kind":"button",
            "text":"Nested ordinary","actionId":"nested.ordinary","children":[]}]}]},
            {"id":"nested.outer.action","kind":"button","text":"Outer action",
            "actionId":"nested.outer.action",
            "focusBackgroundArtworkHandle":"nested.outer.focus","children":[]}]}]}}
        })json", nestedError);
        Require(nested.has_value() && nestedError.empty(),
            "native parser rejected nested focused-background ownership");
        const auto renderNested = [&](const std::wstring_view focusedId) {
            target->BeginDraw();
            auto result = renderer.Render(
                target.Get(), *nested, focusedId, viewport, options);
            Require(SUCCEEDED(target->EndDraw()), "nested focused-background draw failed");
            Require(result.succeeded, "nested focused-background render failed");
            return result;
        };
        const auto requestCountBeforeNested = requests.size();
        const auto nestedFrame = renderNested(L"nested.action");
        const auto expectedOuterKey =
            widgetrail::RemoteImageCache::TrustedArtworkKey(
                L"widgetrail.tests.background-surface", L"nested.outer",
                L"nested.outer.default");
        const auto expectedInnerKey =
            widgetrail::RemoteImageCache::TrustedArtworkKey(
                L"widgetrail.tests.background-surface", L"nested.inner",
                L"nested.focus");
        Require(requests.size() == requestCountBeforeNested + 2U,
            "nested focus did not preserve two independent surface owners");
        Require(requests[requestCountBeforeNested] == expectedOuterKey &&
                requests[requestCountBeforeNested + 1U] == expectedInnerKey,
            "nested BackgroundSurface did not form a hard focus-artwork boundary");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.outer.default",
            L"image/png", std::wstring(png)),
            "nested outer artwork completion was rejected");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.focus",
            L"image/png", std::wstring(png)),
            "nested focused artwork completion was rejected");
        WaitForState(cache, expectedOuterKey, widgetrail::RemoteImageState::Ready,
            "nested outer artwork did not become ready");
        WaitForState(cache, expectedInnerKey, widgetrail::RemoteImageState::Ready,
            "nested focused artwork did not become ready");
        const auto nestedReady = renderNested(L"nested.action");
        Require(RequireBackgroundHandle(
                    nestedReady, L"nested.outer",
                    "nested ready frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    nestedReady, L"nested.inner",
                    "nested ready frame omitted inner artwork") ==
                    L"nested.focus",
            "nested surfaces did not retain independent exact ready handles");

        const auto outerFocusKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface", L"nested.outer",
            L"nested.outer.focus");
        const auto innerDefaultKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface", L"nested.inner",
            L"nested.inner.default");
        const auto requestsBeforeOuterFocus = requests.size();
        const auto outerFocusPending = renderNested(L"nested.outer.action");
        Require(requests.size() == requestsBeforeOuterFocus + 2U &&
                requests[requestsBeforeOuterFocus] == outerFocusKey &&
                requests[requestsBeforeOuterFocus + 1U] == innerDefaultKey &&
                RequireBackgroundHandle(
                    outerFocusPending, L"nested.outer",
                    "outer-focus pending frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                !outerFocusPending.backgroundArtworkHandles.contains(L"nested.inner"),
            "selecting a different surface did not retire the prior surface override");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.outer.focus",
            L"image/png", std::wstring(png)),
            "outer focused artwork completion was rejected");
        WaitForState(cache, outerFocusKey, widgetrail::RemoteImageState::Ready,
            "outer focused artwork did not become ready");
        const auto outerFocusReady = renderNested(L"nested.outer.action");
        Require(RequireBackgroundHandle(
                    outerFocusReady, L"nested.outer",
                    "outer-focus ready frame omitted outer artwork") ==
                    L"nested.outer.focus" &&
                !outerFocusReady.backgroundArtworkHandles.contains(L"nested.inner"),
            "different selected surface did not establish independent authority");
        const auto innerFocusRestored = renderNested(L"nested.action");
        Require(RequireBackgroundHandle(
                    innerFocusRestored, L"nested.outer",
                    "inner-focus frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    innerFocusRestored, L"nested.inner",
                    "inner-focus frame omitted inner artwork") ==
                    L"nested.focus",
            "selecting the inner surface retained the different outer override");

        auto& outerContent = nested->root.children.front();
        const auto retainedInner = outerContent.children.front();
        outerContent.children.erase(outerContent.children.begin());
        const auto innerRemoved = renderNested(L"");
        Require(RequireBackgroundHandle(
                    innerRemoved, L"nested.outer",
                    "inner-removal frame omitted outer artwork") ==
                L"nested.outer.default",
            "removing the inner surface altered the outer surface handle");
        outerContent.children.insert(outerContent.children.begin(), retainedInner);
        const auto innerReaddedPending = renderNested(L"nested.ordinary");
        Require(requests.back() == innerDefaultKey &&
                !innerReaddedPending.backgroundArtworkHandles.contains(L"nested.inner") &&
                RequireBackgroundHandle(
                    innerReaddedPending, L"nested.outer",
                    "inner-readded pending frame omitted outer artwork") ==
                    L"nested.outer.default",
            "re-added inner surface resurrected its old focused override");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.inner.default",
            L"image/png", std::wstring(png)),
            "nested inner default completion was rejected");
        WaitForState(cache, innerDefaultKey, widgetrail::RemoteImageState::Ready,
            "nested inner default did not become ready");
        const auto innerReaddedReady = renderNested(L"nested.ordinary");
        Require(RequireBackgroundHandle(
                    innerReaddedReady, L"nested.outer",
                    "inner-readded ready frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    innerReaddedReady, L"nested.inner",
                    "inner-readded ready frame omitted inner artwork") ==
                    L"nested.inner.default",
            "re-added inner surface did not initialize independently from default");

        const auto outerOverrideBeforeNonOpt =
            renderNested(L"nested.outer.action");
        Require(RequireBackgroundHandle(
                    outerOverrideBeforeNonOpt, L"nested.outer",
                    "non-opt-in fixture omitted outer artwork") ==
                    L"nested.outer.focus" &&
                RequireBackgroundHandle(
                    outerOverrideBeforeNonOpt, L"nested.inner",
                    "non-opt-in fixture omitted inner artwork") ==
                    L"nested.inner.default",
            "non-opt-in boundary fixture did not establish the outer override");
        outerContent.children.front().usesFocusedDescendantArtwork = false;
        const auto nestedNonOpt = renderNested(L"nested.action");
        Require(RequireBackgroundHandle(
                    nestedNonOpt, L"nested.outer",
                    "nested non-opt-in frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    nestedNonOpt, L"nested.inner",
                    "nested non-opt-in frame omitted inner artwork") ==
                    L"nested.inner.default",
            "focus inside a nested non-opt-in surface retained the outer override");
        outerContent.children.front().usesFocusedDescendantArtwork = true;
        const auto innerReset = renderNested(L"nested.ordinary");
        Require(RequireBackgroundHandle(
                    innerReset, L"nested.outer",
                    "inner-reset frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    innerReset, L"nested.inner",
                    "inner-reset frame omitted inner artwork") ==
                    L"nested.inner.default",
            "resetting the inner surface altered the independent outer owner");

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
